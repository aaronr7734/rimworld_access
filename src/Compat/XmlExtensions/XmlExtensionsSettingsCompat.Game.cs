using System;
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
    /// that name rather than landing as a stray row at the end of the page.
    /// </summary>
    internal static class XmlExtensionsSettingsCompat
    {
        private const int NotRecorded = int.MinValue;

        private static FieldInfo checkboxStyleField;
        private static FieldInfo radioButtonStyleField;
        private static FieldInfo radioButtonsStyleField;
        private static FieldInfo collapsibleStateField;
        private static FieldInfo collapsibleHeaderHeightField;
        private static FieldInfo labelField;
        private static FieldInfo tKeyField;
        private static FieldInfo tooltipField;
        private static FieldInfo tKeyTipField;
        private static FieldInfo menuDefField;
        private static FieldInfo searchTextField;
        private static FieldInfo foundResultsField;
        private static FieldInfo showCountField;
        private static MethodInfo translateIfTKeyAvailable;
        private static object stateOpen;
        private static object stateClosed;

        /// <summary>True between a collapsible header's capture and the first child draw.</summary>
        private static bool collapsibleCaptionOpen;

        /// <summary>How many collapsible sections enclose the one drawing now.</summary>
        private static int collapsibleDepth;

        /// <summary>
        /// The result count a search box drew last pass. Its draw comes after every setting, by
        /// when the field it describes is already recorded; it changes only as the search text does.
        /// </summary>
        private static object searchCountOwner;
        private static string searchCountText;

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

                checkboxStyleField = surface.Field(checkboxType, "style");
                radioButtonStyleField = surface.Field(radioButtonType, "style");
                radioButtonsStyleField = surface.Field(radioButtonsType, "style");
                collapsibleStateField = surface.Field(collapsibleType, "state");
                collapsibleHeaderHeightField = surface.Field(collapsibleType, "headerHeight");
                labelField = surface.Field(containerType, "label");
                tKeyField = surface.Field(containerType, "tKey");
                tooltipField = surface.Field(containerType, "tooltip");
                tKeyTipField = surface.Field(containerType, "tKeyTip");
                menuDefField = surface.Field(surface.Type("XmlExtensions.Container"), "menuDef");
                searchTextField = surface.Field(menuDefType, "searchText");
                foundResultsField = surface.Field(menuDefType, "foundResults");
                showCountField = surface.Field(searchboxType, "showCount");
                translateIfTKeyAvailable = surface.Method(surface.Type("XmlExtensions.StringExtensions"),
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
                if (!surface.Ready || !stateType.IsEnum
                    || !Enum.IsDefined(stateType, "Open") || !Enum.IsDefined(stateType, "Closed"))
                {
                    return;
                }
                stateOpen = Enum.Parse(stateType, "Open");
                stateClosed = Enum.Parse(stateType, "Closed");

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
            }
            catch (Exception ex)
            {
                ModLogger.Error("XML Extensions compat registration failed: " + ex.Message);
            }
        }

        /// <summary>Every style enum in the mod lists its plain vanilla widget first.</summary>
        private static bool DrawsVanillaWidget(FieldInfo styleField, object setting)
        {
            return Convert.ToInt32(styleField.GetValue(setting)) == 0;
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

        private static int BeginRadio(FieldInfo styleField, object setting, Rect rect, string label, bool active, string tooltip)
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

        private static string Translated(object setting, FieldInfo text, FieldInfo key)
        {
            return (string)translateIfTKeyAvailable.Invoke(null, new[] { text.GetValue(setting), key.GetValue(setting) });
        }

        private static void CollapsiblePrefix(object __instance, Rect inRect, out bool __state)
        {
            __state = false;
            string label = Translated(__instance, labelField, tKeyField) ?? "";
            Rect header = inRect.TopPartPixels((float)collapsibleHeaderHeightField.GetValue(__instance));
            bool open = Equals(collapsibleStateField.GetValue(__instance), stateOpen);
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
                collapsibleStateField.SetValue(__instance, open ? stateClosed : stateOpen);
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
            string name = "RimWorldAccess.Compat.XmlExtensions.Search".Translate();
            if (__instance == searchCountOwner && !string.IsNullOrEmpty(searchCountText))
            {
                name = "RimWorldAccess.Compat.XmlExtensions.SearchWithResults".Translate(searchCountText);
            }
            WidgetCapture.EnterFieldLabel(name);
        }

        private static Exception SearchboxFinalizer(Exception __exception)
        {
            WidgetCapture.ExitFieldLabel();
            return __exception;
        }

        /// <summary>Mirrors the count Searchbox.PostDrawSettingContents draws, in the mod's own words.</summary>
        private static void SearchCountPrefix(object __instance)
        {
            object menuDef = menuDefField.GetValue(__instance);
            searchCountOwner = __instance;
            searchCountText = null;
            if ((bool)showCountField.GetValue(__instance)
                && !string.IsNullOrEmpty((string)searchTextField.GetValue(menuDef)))
            {
                string format = (string)translateIfTKeyAvailable.Invoke(null,
                    new object[] { "{0} Result(s)", "XmlExtensions_SearchResults" });
                searchCountText = format.Replace("{0}", foundResultsField.GetValue(menuDef).ToString());
            }
            WidgetCapture.EnterFilterPanelSuppression();
        }

        private static Exception SearchCountFinalizer(Exception __exception)
        {
            WidgetCapture.ExitFilterPanelSuppression();
            return __exception;
        }
    }
}
