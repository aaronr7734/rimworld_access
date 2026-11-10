using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// XML Extensions draws settings pages for every mod that declares them in XML, inside vanilla
    /// Dialog_ModSettings and its own settings windows, so the generic reader owns those pages.
    /// Three of its controls carry their state only in paint:
    ///
    /// The Checkbox setting in its button styles and the RadioButton/RadioButtons settings in
    /// their button styles draw a caption over an invisible hotspot and show on or chosen only
    /// as a pressed or boxed background. Each draw is recorded as the checkbox or radio row it
    /// is, and the body's own captures are discarded so the row is not read twice. A keyboard
    /// toggle flips the method's own ref parameter or forces its own return value, which the
    /// caller then writes through SettingsManager.SetSetting.
    ///
    /// CollapsibleSettings draws its header as caption, Reveal texture and invisible hotspot.
    /// The header is recorded in Listing_Tree's own shape, an expander carrying the open state
    /// followed by the node's label at its nesting depth, so the reader presents one named tree
    /// item; the drawn caption stays suppressed until the section's children start drawing.
    ///
    /// Searchbox is a bare text field whose only caption is a magnifier icon, and whose result
    /// count is drawn only after every setting has drawn. The field is named, and the count rides
    /// that name rather than landing as a stray row at the end of the page. Search hits show only
    /// as outline boxes drawn after every setting, so each row inside one is marked a match once
    /// the page has drawn.
    ///
    /// Slider and Range store their value rounded to their own <c>decimals</c>, which their
    /// vanilla widget calls never declare, so the keyboard steps on that grid.
    /// </summary>
    internal static class XmlExtensionsSettingsCompat
    {
        private const int NotRecorded = int.MinValue;

        // Field refs, not FieldInfo reads: every one of these is read per setting per frame.
        // Enum fields read as their underlying int.
        private static AccessTools.FieldRef<object, int> checkboxStyleField;
        private static AccessTools.FieldRef<object, int> radioButtonStyleField;
        private static AccessTools.FieldRef<object, int> radioButtonsStyleField;
        private static AccessTools.FieldRef<object, int> collapsibleStateRef;
        private static AccessTools.FieldRef<object, float> collapsibleHeaderHeightField;
        private static AccessTools.FieldRef<object, string> labelField;
        private static AccessTools.FieldRef<object, string> tKeyField;
        private static AccessTools.FieldRef<object, string> tooltipField;
        private static AccessTools.FieldRef<object, string> tKeyTipField;
        private static AccessTools.FieldRef<object, object> menuDefField;
        private static AccessTools.FieldRef<object, string> searchTextField;
        private static AccessTools.FieldRef<object, int> foundResultsField;
        private static AccessTools.FieldRef<object, bool> showCountField;
        private static AccessTools.FieldRef<object, int> sliderDecimalsField;
        private static AccessTools.FieldRef<object, int> rangeDecimalsField;
        private static AccessTools.FieldRef<object, Rect?> postDrawRectField;
        private static AccessTools.FieldRef<object, IDictionary> childCollectionsField;
        private static Func<string, string, string> translateIfTKeyAvailable;
        private static int stateOpenValue;
        private static int stateClosedValue;

        /// <summary>True between a collapsible header's capture and the first child draw.</summary>
        private static bool collapsibleCaptionOpen;

        /// <summary>How many collapsible sections enclose the one drawing now.</summary>
        private static int collapsibleDepth;

        /// <summary>
        /// The result count a search box drew last pass. Its draw comes after every setting, by
        /// when the field it describes is already recorded; it changes only as the search text does.
        /// </summary>
        private static object searchCountOwner;
        private static int? searchCount;

        // The search box's name, rebuilt only when the result count or language changes.
        private static LoadedLanguage searchboxNameLanguage;
        private static int? searchboxNameCount;
        private static string searchboxName;

        /// <summary>The search-hit outlines drawn during the settings page now drawing, in screen space.</summary>
        private static readonly List<Rect> searchHitRects = new List<Rect>();

        public static void Register(Harmony harmony)
        {
            try
            {
                var surface = new ReflectionSurface("XML Extensions settings");
                Type checkboxType = surface.Type("XmlExtensions.Setting.Checkbox");
                Type radioButtonType = surface.Type("XmlExtensions.Setting.RadioButton");
                Type radioButtonsType = surface.Type("XmlExtensions.Setting.RadioButtons");
                Type collapsibleType = surface.Type("XmlExtensions.Setting.CollapsibleSettings");
                Type containerType = surface.Type("XmlExtensions.Setting.SettingContainer");
                Type stateType = surface.Type("XmlExtensions.Setting.CollapsibleSettings+State");
                Type searchboxType = surface.Type("XmlExtensions.Setting.Searchbox");
                Type menuDefType = surface.Type("XmlExtensions.SettingsMenuDef");

                AccessTools.FieldRef<object, T> Ref<T>(Type type, string name)
                {
                    FieldInfo field = surface.Field(type, name);
                    return field == null ? null : AccessTools.FieldRefAccess<object, T>(field);
                }

                // Bound before any patch, so a field whose type moved leaves the mod unpatched.
                checkboxStyleField = Ref<int>(checkboxType, "style");
                radioButtonStyleField = Ref<int>(radioButtonType, "style");
                radioButtonsStyleField = Ref<int>(radioButtonsType, "style");
                collapsibleStateRef = Ref<int>(collapsibleType, "state");
                collapsibleHeaderHeightField = Ref<float>(collapsibleType, "headerHeight");
                labelField = Ref<string>(containerType, "label");
                tKeyField = Ref<string>(containerType, "tKey");
                tooltipField = Ref<string>(containerType, "tooltip");
                tKeyTipField = Ref<string>(containerType, "tKeyTip");
                Type xmlContainer = surface.Type("XmlExtensions.Container");
                menuDefField = Ref<object>(xmlContainer, "menuDef");
                searchTextField = Ref<string>(menuDefType, "searchText");
                foundResultsField = Ref<int>(menuDefType, "foundResults");
                showCountField = Ref<bool>(searchboxType, "showCount");
                Type sliderType = surface.Type("XmlExtensions.Setting.Slider");
                Type rangeType = surface.Type("XmlExtensions.Setting.Range");
                sliderDecimalsField = Ref<int>(sliderType, "decimals");
                rangeDecimalsField = Ref<int>(rangeType, "decimals");
                postDrawRectField = Ref<Rect?>(xmlContainer, "postDrawRect");
                childCollectionsField = Ref<IDictionary>(xmlContainer, "initializedContainerCollections");
                MethodInfo translate = surface.Method(surface.Type("XmlExtensions.StringExtensions"),
                    "TranslateIfTKeyAvailable", new[] { typeof(string), typeof(string) });

                Type[] radioSignature = { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(string), typeof(float?) };
                MethodInfo drawCheckbox = surface.Method(checkboxType, "DrawCheckbox",
                    new[] { typeof(Rect), typeof(string), typeof(bool).MakeByRefType(), typeof(string), typeof(float?) });
                MethodInfo drawRadioButton = surface.Method(radioButtonType, "DrawRadioButton", radioSignature);
                MethodInfo drawRadioButtons = surface.Method(radioButtonsType, "DrawRadioButton", radioSignature);
                MethodInfo drawCollapsible = surface.Method(collapsibleType, "DrawSettingContents", new[] { typeof(Rect) });
                MethodInfo drawSettingsList = surface.Method(containerType, "DrawSettingsList", null);
                MethodInfo drawSearchbox = surface.Method(searchboxType, "DrawSettingContents", new[] { typeof(Rect) });
                MethodInfo postDrawSearchbox = surface.Method(searchboxType, "PostDrawSettingContents", new[] { typeof(Rect) });
                MethodInfo drawSlider = surface.Method(sliderType, "DrawSettingContents", new[] { typeof(Rect) });
                MethodInfo drawRange = surface.Method(rangeType, "DrawSettingContents", new[] { typeof(Rect) });
                MethodInfo filterBox = surface.Method(containerType, "FilterBox", new[] { typeof(Rect) });
                MethodInfo drawSettingsMenu = surface.Method(menuDefType, "DrawSettingsMenu", new[] { typeof(Rect) });
                object stateOpen = surface.EnumValue(stateType, "Open");
                object stateClosed = surface.EnumValue(stateType, "Closed");
                if (!surface.Ready)
                {
                    return;
                }
                stateOpenValue = Convert.ToInt32(stateOpen);
                stateClosedValue = Convert.ToInt32(stateClosed);

                translateIfTKeyAvailable = AccessTools.MethodDelegate<Func<string, string, string>>(translate);

                harmony.Patch(drawCheckbox,
                    prefix: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(CheckboxPrefix)),
                    finalizer: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(CheckboxFinalizer)));
                harmony.Patch(drawRadioButton,
                    prefix: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(RadioButtonPrefix)),
                    postfix: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(RadioPostfix)),
                    finalizer: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(RadioFinalizer)));
                harmony.Patch(drawRadioButtons,
                    prefix: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(RadioButtonsPrefix)),
                    postfix: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(RadioPostfix)),
                    finalizer: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(RadioFinalizer)));
                harmony.Patch(drawCollapsible,
                    prefix: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(CollapsiblePrefix)),
                    finalizer: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(CollapsibleFinalizer)));
                harmony.Patch(drawSettingsList,
                    prefix: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(SettingsListPrefix)));
                harmony.Patch(drawSearchbox,
                    prefix: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(SearchboxPrefix)),
                    finalizer: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(SearchboxFinalizer)));
                harmony.Patch(postDrawSearchbox,
                    prefix: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(SearchCountPrefix)),
                    finalizer: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(SearchCountFinalizer)));
                harmony.Patch(drawSlider,
                    prefix: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(SliderPrefix)),
                    finalizer: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(StepGridFinalizer)));
                harmony.Patch(drawRange,
                    prefix: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(RangePrefix)),
                    finalizer: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(StepGridFinalizer)));
                harmony.Patch(filterBox,
                    prefix: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(FilterBoxPrefix)));
                harmony.Patch(drawSettingsMenu,
                    prefix: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(SettingsMenuPrefix)),
                    finalizer: new HarmonyMethod(typeof(XmlExtensionsSettingsCompat), nameof(SettingsMenuFinalizer)));
            }
            catch (Exception ex)
            {
                ModLogger.Error("XML Extensions compat registration failed: " + ex.Message);
            }
        }

        /// <summary>Every style enum in the mod lists its plain vanilla widget first.</summary>
        private static bool DrawsVanillaWidget(AccessTools.FieldRef<object, int> styleField, object setting)
        {
            return styleField(setting) == 0;
        }

        /// <summary>
        /// Records the row, then hands the body to the shared discard bracket. The mod's own
        /// TipRegion runs inside the bracket, so the tip is recorded here against the same rect.
        /// </summary>
        private static void BeginStyledRow(Rect rect, string tooltip)
        {
            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipCapture.RecordSignal(rect, tooltip);
            }
            WidgetCapture.EnterFilterPanelSuppression();
        }

        private static void CheckboxPrefix(object __instance, Rect rect, string label, ref bool active, string tooltip, out bool __state)
        {
            __state = false;
            if (DrawsVanillaWidget(checkboxStyleField, __instance))
            {
                return;
            }
            WidgetCapture.RecordAndMaybeToggleCheckbox(rect, label, ref active, false);
            BeginStyledRow(rect, tooltip);
            __state = true;
        }

        private static Exception CheckboxFinalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                WidgetCapture.ExitFilterPanelSuppression();
            }
            return __exception;
        }

        private static void RadioButtonPrefix(object __instance, Rect rect, string label, bool active, string tooltip, out int __state)
        {
            __state = BeginRadio(radioButtonStyleField, __instance, rect, label, active, tooltip);
        }

        private static void RadioButtonsPrefix(object __instance, Rect rect, string label, bool active, string tooltip, out int __state)
        {
            __state = BeginRadio(radioButtonsStyleField, __instance, rect, label, active, tooltip);
        }

        private static int BeginRadio(AccessTools.FieldRef<object, int> styleField, object setting, Rect rect, string label, bool active, string tooltip)
        {
            if (DrawsVanillaWidget(styleField, setting))
            {
                return NotRecorded;
            }
            int index = WidgetCapture.RecordRadio(rect, label, active, false);
            BeginStyledRow(rect, tooltip);
            return index;
        }

        /// <summary>Forcing the method's own return value runs its caller's SetSetting branch.</summary>
        private static void RadioPostfix(int __state, ref bool __result)
        {
            if (__state != NotRecorded)
            {
                WidgetCapture.MaybeForceActivate(__state, ref __result, false);
            }
        }

        private static Exception RadioFinalizer(Exception __exception, int __state)
        {
            if (__state != NotRecorded)
            {
                WidgetCapture.ExitFilterPanelSuppression();
            }
            return __exception;
        }

        private static string Translated(object setting, AccessTools.FieldRef<object, string> text, AccessTools.FieldRef<object, string> key)
        {
            return translateIfTKeyAvailable(text(setting), key(setting));
        }

        private static void CollapsiblePrefix(object __instance, Rect inRect, out bool __state)
        {
            __state = false;
            string label = Translated(__instance, labelField, tKeyField) ?? "";
            Rect header = inRect.TopPartPixels(collapsibleHeaderHeightField(__instance));
            bool open = collapsibleStateRef(__instance) == stateOpenValue;
            int level = collapsibleDepth;

            WidgetCapture.EnterListingTreeExpander(level);
            WidgetCapture.NoteTreeExpanderTexture(open ? TexButton.Collapse : TexButton.Reveal);
            int index = WidgetCapture.RecordButton(header, label, false);
            WidgetCapture.ExitListingTreeExpander();
            WidgetCapture.EnterListingTreeLabel(label, Translated(__instance, tooltipField, tKeyTipField), level);
            WidgetCapture.RecordLabel(header, label);
            WidgetCapture.ExitListingTreeLabel();

            bool activated = false;
            WidgetCapture.MaybeForceActivate(index, ref activated, false);
            if (activated)
            {
                // MUTATION-C: mirrors CollapsibleSettings.DrawSettingContents' own header click; its hotspot is a bare ButtonInvisible this bracket keeps out of capture, so no widget is left to force.
                collapsibleStateRef(__instance) = open ? stateClosedValue : stateOpenValue;
            }

            WidgetCapture.EnterSelfCaptioned(label);
            collapsibleCaptionOpen = true;
            collapsibleDepth++;
            __state = true;
        }

        private static void SettingsListPrefix()
        {
            if (collapsibleCaptionOpen)
            {
                collapsibleCaptionOpen = false;
                WidgetCapture.ExitSelfCaptioned();
            }
        }

        private static Exception CollapsibleFinalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                SettingsListPrefix();
                collapsibleDepth--;
            }
            return __exception;
        }

        private static void SearchboxPrefix(object __instance)
        {
            int? results = __instance == searchCountOwner ? searchCount : null;
            if (searchboxName == null || results != searchboxNameCount
                || searchboxNameLanguage != LanguageDatabase.activeLanguage)
            {
                searchboxName = results == null
                    ? "RimWorldAccess.Compat.XmlExtensions.Search".Translate()
                    : "RimWorldAccess.Compat.XmlExtensions.SearchWithResults".Translate(
                        translateIfTKeyAvailable("{0} Result(s)", "XmlExtensions_SearchResults").Replace("{0}", results.Value.ToString()));
                searchboxNameCount = results;
                searchboxNameLanguage = LanguageDatabase.activeLanguage;
            }
            WidgetCapture.EnterFieldLabel(searchboxName);
        }

        private static Exception SearchboxFinalizer(Exception __exception)
        {
            WidgetCapture.ExitFieldLabel();
            return __exception;
        }

        /// <summary>Mirrors the count Searchbox.PostDrawSettingContents draws, in the mod's own words.</summary>
        private static void SearchCountPrefix(object __instance)
        {
            object menuDef = menuDefField(__instance);
            searchCountOwner = __instance;
            searchCount = showCountField(__instance) && !string.IsNullOrEmpty(searchTextField(menuDef))
                ? foundResultsField(menuDef)
                : (int?)null;
            WidgetCapture.EnterFilterPanelSuppression();
        }

        private static Exception SearchCountFinalizer(Exception __exception)
        {
            WidgetCapture.ExitFilterPanelSuppression();
            return __exception;
        }

        private static void SliderPrefix(object __instance)
        {
            WidgetCapture.EnterDeclaredStepGrid(DecimalGrid(sliderDecimalsField, __instance));
        }

        private static void RangePrefix(object __instance)
        {
            WidgetCapture.EnterDeclaredStepGrid(DecimalGrid(rangeDecimalsField, __instance));
        }

        /// <summary>The setting saves Math.Round(value, decimals), so one unit in that last place is its grid.</summary>
        private static double DecimalGrid(AccessTools.FieldRef<object, int> decimalsField, object setting)
        {
            int decimals = Mathf.Clamp(decimalsField(setting), 0, 15);
            return Math.Pow(10.0, -decimals);
        }

        private static Exception StepGridFinalizer(Exception __exception)
        {
            WidgetCapture.ExitDeclaredStepGrid();
            return __exception;
        }

        private static void SettingsMenuPrefix(out int __state)
        {
            searchHitRects.Clear();
            __state = WidgetCapture.RowSpanStart();
        }

        private static Exception SettingsMenuFinalizer(Exception __exception, int __state)
        {
            WidgetCapture.MarkSearchMatches(__state, searchHitRects);
            searchHitRects.Clear();
            return __exception;
        }

        /// <summary>
        /// A container with settings of its own outlines its whole rect when any of them matched;
        /// those settings outline themselves, so only the narrower outlines (a matched setting, a
        /// collapsible header, a tab) say which row matched.
        /// </summary>
        private static void FilterBoxPrefix(object __instance, Rect inRect)
        {
            Rect? whole = postDrawRectField(__instance);
            bool enclosesChildren = childCollectionsField(__instance).Count > 0
                && whole.HasValue && whole.Value == inRect;
            if (!enclosesChildren)
            {
                searchHitRects.Add(GuiSpace.ToScreen(inRect));
            }
        }
    }
}
