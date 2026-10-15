using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    // Harmony taps, one patch class per Widgets primitive.

    /// <summary>TargetMethod, not the attribute's Type[] form: a by-ref parameter type is not a compile-time constant (CS0182).</summary>
    [HarmonyPatch]
    internal static class WidgetCaptureCheckboxLabeledPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "CheckboxLabeled",
                new Type[] { typeof(Rect), typeof(string), typeof(bool).MakeByRefType(), typeof(bool), typeof(Texture2D), typeof(Texture2D), typeof(bool), typeof(bool) });
        }

        [HarmonyPrefix]
        public static void Prefix(Rect rect, string label, ref bool checkOn, bool disabled)
        {
            WidgetCapture.RecordAndMaybeToggleCheckbox(rect, label, ref checkOn, disabled);
            WidgetCapture.EnterSelfCaptioned(label);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitSelfCaptioned();
        }
    }

    [HarmonyPatch]
    internal static class WidgetCaptureCheckboxPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "Checkbox",
                new Type[] { typeof(float), typeof(float), typeof(bool).MakeByRefType(), typeof(float), typeof(bool), typeof(bool), typeof(Texture2D), typeof(Texture2D) });
        }

        [HarmonyPrefix]
        public static void Prefix(float x, float y, ref bool checkOn, float size, bool disabled)
        {
            WidgetCapture.RecordAndMaybeToggleCheckbox(new Rect(x, y, size, size), "", ref checkOn, disabled);
        }
    }

    [HarmonyPatch(typeof(Widgets), "RadioButtonLabeled", new Type[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool) })]
    internal static class WidgetCaptureRadioButtonPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, string labelText, bool chosen, bool disabled, out int __state)
        {
            __state = WidgetCapture.RecordRadio(rect, labelText, chosen, disabled);
            WidgetCapture.EnterSelfCaptioned(labelText);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref bool __result, bool disabled)
        {
            WidgetCapture.ExitSelfCaptioned();
            WidgetCapture.MaybeForceActivate(__state, ref __result, disabled);
        }
    }

    /// <summary>
    /// One area swatch of an allowed-area strip (RimWorld/AreaAllowedGUI.cs:45-83). Vanilla
    /// selects it only by a raw Mouse.IsOver + Input.GetMouseButton(0) drag, with no click
    /// primitive to ride, and unlike Hospitality's copies takes no getArea/setArea delegates —
    /// it reads and writes <c>p.playerSettings.AreaRestrictionInPawnCurrentMap</c> directly.
    /// The label comes from the method's own <c>AreaUtility.AreaAllowedLabel_Area</c> call, and
    /// the self-caption bracket keeps its inner Widgets.Label from double-recording.
    /// </summary>
    [HarmonyPatch(typeof(AreaAllowedGUI), "DoAreaSelector", new Type[] { typeof(Rect), typeof(Pawn), typeof(Area) })]
    internal static class WidgetCaptureAreaAllowedSelectorPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, Pawn p, Area area, out int __state)
        {
            string label = AreaUtility.AreaAllowedLabel_Area(area);
            bool chosen = p.playerSettings.AreaRestrictionInPawnCurrentMap == area;
            __state = WidgetCapture.RecordRadio(rect, label, chosen, false);
            WidgetCapture.EnterSelfCaptioned(label);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, Pawn p, Area area)
        {
            WidgetCapture.ExitSelfCaptioned();
            bool activated = false;
            WidgetCapture.MaybeForceActivate(__state, ref activated, false);
            if (activated)
            {
                // MUTATION-C: mirrors RimWorld.AreaAllowedGUI.DoAreaSelector's
                // own drag-handler write (decompiled RimWorld/AreaAllowedGUI.cs:77)
                // exactly — no getArea/setArea delegate or setter method
                // exists on this overload for a vehicle A/B call to ride; the
                // method reads and writes this same field directly.
                p.playerSettings.AreaRestrictionInPawnCurrentMap = area;
            }
        }
    }

    /// <summary>
    /// The vanilla filter-tree panel (Verse/ThingFilterUI.cs:29) that Gastronomy's Menu and
    /// Storefront's stock filter hand their whole rect to. The prefix records one synthetic
    /// FilterPanelHandoff row (<see cref="WidgetCapture.RecordFilterPanel"/>) and suppresses the
    /// call's body, so the hundreds of rows Listing_TreeThingFilter draws never reach
    /// presentation. Binds by parameter name against a subset of the 12-argument signature, so
    /// the arguments the panel is called without still resolve to their defaults.
    /// </summary>
    [HarmonyPatch(typeof(ThingFilterUI), "DoThingFilterConfigWindow")]
    internal static class WidgetCaptureThingFilterPanelPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, ThingFilter filter, ThingFilter parentFilter,
            IEnumerable<SpecialThingFilterDef> forceHiddenFilters, bool forceHideHitPointsConfig, bool forceHideQualityConfig)
        {
            WidgetCapture.RecordFilterPanel(rect, filter, parentFilter, forceHiddenFilters, forceHideHitPointsConfig, forceHideQualityConfig);
            WidgetCapture.EnterFilterPanelSuppression();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitFilterPanelSuppression();
        }
    }

    /// <summary>Both public ButtonText overloads funnel through this one.</summary>
    [HarmonyPatch(typeof(Widgets), "ButtonText",
        new Type[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(Color), typeof(bool), typeof(TextAnchor?) })]
    internal static class WidgetCaptureButtonTextPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, string label, bool active, out int __state)
        {
            __state = WidgetCapture.RecordButton(rect, label, !active);
            WidgetCapture.EnterSelfCaptioned(label);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref bool __result, bool active)
        {
            WidgetCapture.ExitSelfCaptioned();
            WidgetCapture.MaybeForceActivate(__state, ref __result, !active);
        }
    }

    [HarmonyPatch(typeof(Widgets), "HorizontalSlider",
        new Type[] { typeof(Rect), typeof(float), typeof(float), typeof(float), typeof(bool), typeof(string), typeof(string), typeof(string), typeof(float) })]
    internal static class WidgetCaptureSliderPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, float value, float min, float max, bool middleAlignment, string label, string leftAlignedLabel, string rightAlignedLabel, float roundTo, out int __state)
        {
            __state = WidgetCapture.RecordSlider(rect, label, leftAlignedLabel, rightAlignedLabel, value, min, max, roundTo,
                SliderDragSpeech.ControlId(rect, min, max, middleAlignment, label));
            // Vanilla draws all three captions through the hooked Label, and the slider's own
            // row already carries each of them, so all three are suppressed.
            WidgetCapture.EnterSelfCaptioned(label, leftAlignedLabel, rightAlignedLabel);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, float min, float max, float roundTo, ref float __result)
        {
            WidgetCapture.ExitSelfCaptioned();
            WidgetCapture.MaybeStepSlider(__state, min, max, roundTo, ref __result);
        }
    }

    [HarmonyPatch(typeof(Widgets), "TextField", new Type[] { typeof(Rect), typeof(string) })]
    internal static class WidgetCaptureTextFieldPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, string text, out int __state)
        {
            __state = WidgetCapture.RecordTextField(rect, text, multiLine: false);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, Rect rect, ref string __result)
        {
            WidgetCapture.MaybeOverrideText(__state, ref __result);
            FrameProfilerCapture.OnTextField(rect, ref __result, false);
        }
    }

    [HarmonyPatch(typeof(Widgets), "TextArea", new Type[] { typeof(Rect), typeof(string), typeof(bool) })]
    internal static class WidgetCaptureTextAreaPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, string text, out int __state)
        {
            __state = WidgetCapture.RecordTextField(rect, text, multiLine: true);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, Rect rect, ref string __result)
        {
            WidgetCapture.MaybeOverrideText(__state, ref __result);
            FrameProfilerCapture.OnTextField(rect, ref __result, true);
        }
    }

    /// <summary>Dialog_Options only ever calls the TaggedString overload; here every generic window's TaggedString/GUIContent labels forward to THIS string overload (verified against decompiled Widgets.cs).</summary>
    [HarmonyPatch(typeof(Widgets), "Label", new Type[] { typeof(Rect), typeof(string) })]
    internal static class WidgetCaptureLabelPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, string label)
        {
            WidgetCapture.RecordLabel(rect, label);
        }
    }

    /// <summary>
    /// The tab-strip tap. TabDrawer's DrawTabs and DrawTabsOverflow both delegate to the one
    /// core <c>DrawTabs&lt;TTabRecord&gt;(Rect, List&lt;TTabRecord&gt;, float)</c>, so this
    /// single patch sees every vanilla-drawn strip. It targets the <see cref="TabRecord"/>
    /// instantiation, whose compiled body Mono shares with every reference-type
    /// instantiation, so TabRecord subclasses ride along — hence the prefix takes the list as
    /// <c>object</c> (lists are not covariant) and the recorder walks it as a plain IList.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureTabDrawerPatch
    {
        static MethodBase TargetMethod()
        {
            foreach (MethodInfo method in typeof(TabDrawer).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != "DrawTabs" || !method.IsGenericMethodDefinition)
                {
                    continue;
                }
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 3 && parameters[0].ParameterType == typeof(Rect))
                {
                    return method.MakeGenericMethod(typeof(TabRecord));
                }
            }
            return null;
        }

        [HarmonyPrefix]
        public static void Prefix(Rect baseRect, object tabs, float maxTabWidth)
        {
            WidgetCapture.RecordTabs(baseRect, tabs as System.Collections.IList, maxTabWidth);
            WidgetCapture.EnterTabStrip();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitTabStrip();
        }
    }

    /// <summary>
    /// Column-break tap: Listing.NewColumn is both the explicit multi-column call and where
    /// Listing's own overflow wrapping lands, so one tap marks every column boundary a listing
    /// produces. Postfix — the break index is the sink count either way, NewColumn records
    /// nothing itself.
    /// </summary>
    [HarmonyPatch(typeof(Listing), nameof(Listing.NewColumn))]
    internal static class WidgetCaptureListingNewColumnPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.RecordListingColumnBreak();
        }
    }

    /// <summary>
    /// The 5-arg core every public FillableBar overload funnels through; FillableBarLabeled
    /// draws its caption via the hooked Label separately. Prefix, because the body mutates
    /// its rect (border contraction, fill rescale) before returning.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "FillableBar",
        new Type[] { typeof(Rect), typeof(float), typeof(Texture2D), typeof(Texture2D), typeof(bool) })]
    internal static class WidgetCaptureFillableBarPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, float fillPercent)
        {
            WidgetCapture.RecordFillableBar(rect, fillPercent);
        }
    }

    /// <summary>
    /// Generic scroll-follow on the terminal Widgets.BeginScrollView overload every other
    /// overload funnels through. Prefix, because nudging the ref scrollPosition has to land
    /// before GUI.BeginScrollView reads it and before a mod's own cull decision does. See
    /// WidgetCapture.RecordScrollContainerBegin for the arm/match logic.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureScrollViewPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "BeginScrollView",
                new Type[] { typeof(Rect), typeof(Vector2).MakeByRefType(), typeof(Rect), typeof(bool) });
        }

        [HarmonyPrefix]
        public static void Prefix(Rect outRect, ref Vector2 scrollPosition, Rect viewRect)
        {
            WidgetCapture.RecordScrollContainerBegin(outRect, ref scrollPosition, viewRect);
        }
    }

    /// <summary>Companion to <see cref="WidgetCaptureScrollViewPatch"/>: pops the container stack BeginScrollView pushed.</summary>
    [HarmonyPatch(typeof(Widgets), "EndScrollView", new Type[0])]
    internal static class WidgetCaptureEndScrollViewPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            WidgetCapture.RecordScrollContainerEnd();
        }
    }

    /// <summary>
    /// PawnTableOnGUI is non-virtual, so every subclass funnels through this one body and
    /// any window embedding a live vanilla pawn table draws here. The recorded note is what
    /// lets presentation fuse per-cell fragments into one row per pawn and name header cells
    /// from the table's own column defs. Nothing is recorded outside a live capture pass, and
    /// the vanilla body's Layout-pass early return matches the capture bracket's Layout gate,
    /// so live and note geometry always come from the same pass kind.
    /// </summary>
    [HarmonyPatch(typeof(PawnTable), nameof(PawnTable.PawnTableOnGUI))]
    internal static class WidgetCapturePawnTablePatch
    {
        [HarmonyPrefix]
        public static void Prefix(PawnTable __instance, Vector2 position)
        {
            try
            {
                WidgetCapture.RecordPawnTable(__instance, position);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("WidgetCapturePawnTablePatch", ex);
            }
        }
    }

    /// <summary>
    /// Widgets.CustomButtonText: the permit boxes and mod buttons. It draws its caption
    /// through the hooked Label, so the bracket suppresses that inner row and the postfix
    /// records the button once with the final rect (cacheHeight resizes the ref rect inside
    /// the call). A pending scope activation forces the click result as for ButtonText.
    /// TargetMethod because the by-ref Rect parameter type is not a constant (CS0182).
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureCustomButtonTextPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "CustomButtonText",
                new Type[]
                {
                    typeof(Rect).MakeByRefType(), typeof(string), typeof(Color), typeof(Color), typeof(Color),
                    typeof(Color), typeof(bool), typeof(float), typeof(bool), typeof(bool), typeof(float),
                });
        }

        [HarmonyPrefix]
        public static void Prefix(string label)
        {
            WidgetCapture.EnterSelfCaptioned(label);
        }

        [HarmonyPostfix]
        public static void Postfix(ref Rect rect, string label, ref bool __result, bool active)
        {
            int index = WidgetCapture.ExitCustomButtonAndRecord(rect, label, !active);
            WidgetCapture.MaybeForceActivate(index, ref __result, !active);
        }
    }

    // Composite-widget bracket taps. None of the four targets below has a by-ref parameter,
    // so each binds through the attribute's Type[] form.

    [HarmonyPatch(typeof(Widgets), "ButtonInvisible", new Type[] { typeof(Rect), typeof(bool) })]
    internal static class WidgetCaptureButtonInvisiblePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect butRect, out int __state)
        {
            __state = WidgetCapture.RecordInvisibleButton(butRect);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref bool __result)
        {
            WidgetCapture.MaybeForceActivate(__state, ref __result);
        }
    }

    /// <summary>Draws via ButtonImageDraggable, not ButtonInvisible; the checkboxMultiDepth bracket is defensive symmetry with the other suppression counters.</summary>
    [HarmonyPatch(typeof(Widgets), "CheckboxMulti", new Type[] { typeof(Rect), typeof(MultiCheckboxState), typeof(bool) })]
    internal static class WidgetCaptureCheckboxMultiPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, MultiCheckboxState state, out int __state)
        {
            __state = WidgetCapture.RecordCheckboxMulti(rect, state);
            WidgetCapture.EnterCheckboxMulti();
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref MultiCheckboxState __result)
        {
            WidgetCapture.ExitCheckboxMulti();
            WidgetCapture.MaybeForceCheckboxMulti(__state, ref __result);
        }
    }

    /// <summary>Declared on Listing and non-virtual, so this one tap sees Listing_Standard's GapLine calls too.</summary>
    [HarmonyPatch(typeof(Listing), "GapLine", new Type[] { typeof(float) })]
    internal static class WidgetCaptureGapLinePatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            WidgetCapture.MarkGapLinePending();
        }
    }

    /// <summary>
    /// Patches only the 3-arg (Rect, Texture, ScaleMode) overload: GUI.DrawTexture(Rect,
    /// Texture) is IL-verified to be a pure forward into it with the same arguments, so this
    /// one tap covers the 2-arg callers too. See RecordCheckTexMarker's remarks.
    /// </summary>
    [HarmonyPatch(typeof(GUI), "DrawTexture", new Type[] { typeof(Rect), typeof(Texture), typeof(ScaleMode) })]
    internal static class WidgetCaptureCheckTexMarkerPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect position, Texture image)
        {
            WidgetCapture.RecordCheckTexMarker(position, image);
        }
    }

    /// <summary>
    /// Patches only the 8-arg core overload; every other
    /// <c>Widgets.DrawTextureFitted</c> overload forwards into it unchanged, so this one tap
    /// sees every call site. Parameter names must stay <c>outerRect</c>/<c>tex</c> to match
    /// the declaring method for Harmony's binding. See
    /// <see cref="WidgetCapture.RecordCheckTexMarkerFitted"/>.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "DrawTextureFitted", new Type[] { typeof(Rect), typeof(Texture), typeof(float), typeof(Vector2), typeof(Rect), typeof(float), typeof(Material), typeof(float) })]
    internal static class WidgetCaptureCheckTexMarkerFittedPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect outerRect, Texture tex)
        {
            WidgetCapture.RecordCheckTexMarkerFitted(outerRect, tex);
        }
    }

    // ButtonImage family and draggable-button taps. Each icon-only button is captured once
    // as a Button row named from its tooltip or texture (ImageButtonLabel), the self-caption
    // bracket suppressing the inner ButtonInvisible(Draggable) it draws; the draggable
    // entries force a Pressed result. ButtonImageWithBG funnels through ButtonText(""), so
    // its bracket only NAMES that inner record rather than recording one of its own.

    [HarmonyPatch(typeof(Widgets), "ButtonImageWithBG",
        new Type[] { typeof(Rect), typeof(Texture2D), typeof(Vector2?) })]
    internal static class WidgetCaptureButtonImageWithBGPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect butRect, Texture2D image)
        {
            WidgetCapture.EnterImageWithBGName(butRect, image);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitBlankButtonName();
        }
    }

    /// <summary>
    /// Icon-only button core: the 6-arg ButtonImage every 3-/4-arg overload funnels through.
    /// The self-caption bracket suppresses the inner ButtonInvisible record, so the button is
    /// captured exactly once. The overload has no active/disabled parameter, so no gate.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "ButtonImage",
        new Type[] { typeof(Rect), typeof(Texture2D), typeof(Color), typeof(Color), typeof(bool), typeof(string) })]
    internal static class WidgetCaptureButtonImagePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect butRect, Texture2D tex, string tooltip, out int __state)
        {
            // Listing_Tree's expander is a plain ButtonImage whose texture is its open/closed
            // state, so hand it over before the row records.
            WidgetCapture.NoteTreeExpanderTexture(tex);
            string label = WidgetCapture.ImageButtonLabel(butRect, tex, tooltip, out bool nameFromTip, out bool needsLateTipName, out bool labelIsTextureName);
            bool closeX = tex != null && (tex == TexButton.CloseXSmall || tex == TexButton.CloseXBig);
            __state = WidgetCapture.RecordButton(butRect, label, false, closeX, nameFromTip, needsLateTipName, labelIsTextureName);
            WidgetCapture.EnterSelfCaptioned(label);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref bool __result)
        {
            WidgetCapture.ExitSelfCaptioned();
            WidgetCapture.MaybeForceActivate(__state, ref __result);
        }
    }

    /// <summary>ButtonImageFitted core (4-arg; 2-/3-arg delegate). Same bracket; with no tooltip parameter the name is texture-only.</summary>
    [HarmonyPatch(typeof(Widgets), "ButtonImageFitted",
        new Type[] { typeof(Rect), typeof(Texture2D), typeof(Color), typeof(Color) })]
    internal static class WidgetCaptureButtonImageFittedPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect butRect, Texture2D tex, out int __state)
        {
            string label = WidgetCapture.ImageButtonLabel(butRect, tex, null, out bool nameFromTip, out bool needsLateTipName, out bool labelIsTextureName);
            bool closeX = tex != null && (tex == TexButton.CloseXSmall || tex == TexButton.CloseXBig);
            __state = WidgetCapture.RecordButton(butRect, label, false, closeX, nameFromTip, needsLateTipName, labelIsTextureName);
            WidgetCapture.EnterSelfCaptioned(label);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref bool __result)
        {
            WidgetCapture.ExitSelfCaptioned();
            WidgetCapture.MaybeForceActivate(__state, ref __result);
        }
    }

    /// <summary>
    /// ButtonImageDraggable core (4-arg; 2-/3-arg delegate); its inner
    /// ButtonInvisibleDraggable is suppressed by the self-caption bracket. Vanilla
    /// CheckboxMulti draws through this method, and RecordButton deliberately ignores the
    /// suppression counters, so the prefix must gate on
    /// <see cref="WidgetCapture.InsideCheckboxMulti"/> or a tri-state checkbox double-records.
    /// __state == -1 means "not recorded" and the postfix skips the whole bracket.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "ButtonImageDraggable",
        new Type[] { typeof(Rect), typeof(Texture2D), typeof(Color), typeof(Color) })]
    internal static class WidgetCaptureButtonImageDraggablePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect butRect, Texture2D tex, out int __state)
        {
            if (WidgetCapture.InsideCheckboxMulti)
            {
                __state = -1;
                return;
            }
            string label = WidgetCapture.ImageButtonLabel(butRect, tex, null, out bool nameFromTip, out bool needsLateTipName, out bool labelIsTextureName);
            bool closeX = tex != null && (tex == TexButton.CloseXSmall || tex == TexButton.CloseXBig);
            __state = WidgetCapture.RecordButton(butRect, label, false, closeX, nameFromTip, needsLateTipName, labelIsTextureName);
            WidgetCapture.EnterSelfCaptioned(label);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref Widgets.DraggableResult __result)
        {
            if (__state < 0)
            {
                return;
            }
            WidgetCapture.ExitSelfCaptioned();
            WidgetCapture.MaybeForceDraggablePressed(__state, ref __result);
        }
    }

    /// <summary>
    /// ButtonTextDraggable core (7-arg; the 6-arg overload delegates). It goes through the
    /// private ButtonTextWorker, whose hooked Label and ButtonInvisibleDraggable the
    /// self-caption bracket suppresses, so the ButtonText tap never sees it and the button
    /// records once here.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "ButtonTextDraggable",
        new Type[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(Color), typeof(bool), typeof(TextAnchor?) })]
    internal static class WidgetCaptureButtonTextDraggablePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, string label, bool active, out int __state)
        {
            __state = WidgetCapture.RecordButton(rect, label, !active);
            WidgetCapture.EnterSelfCaptioned(label);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref Widgets.DraggableResult __result, bool active)
        {
            WidgetCapture.ExitSelfCaptioned();
            WidgetCapture.MaybeForceDraggablePressed(__state, ref __result, !active);
        }
    }

    /// <summary>
    /// ButtonInvisibleDraggable, the icon-mode Dropdown opener's click target.
    /// RecordInvisibleButton honors all three suppression counters, so a draggable drawn
    /// inside a bracketing widget records nothing and a standalone one records as an
    /// InvisibleButton, carrying DropdownOpener when inside a Dropdown body.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "ButtonInvisibleDraggable", new Type[] { typeof(Rect), typeof(bool) })]
    internal static class WidgetCaptureButtonInvisibleDraggablePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect butRect, out int __state)
        {
            __state = WidgetCapture.RecordInvisibleButton(butRect);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref Widgets.DraggableResult __result)
        {
            WidgetCapture.MaybeForceDraggablePressed(__state, ref __result);
        }
    }
}
