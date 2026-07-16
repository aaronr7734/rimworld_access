using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches on the widget primitives mods use to draw their settings UI
    /// (Listing_Standard.* and the raw Widgets.* equivalents). Every patch below is gated
    /// on <see cref="ModSettingsCaptureState.Capturing"/> so there is zero overhead outside
    /// Dialog_ModSettings.DoWindowContents.
    ///
    /// <b>Reentrancy:</b> Listing_Standard's widgets are thin wrappers around the raw
    /// Widgets.* equivalents, which themselves often call Widgets.Label internally - e.g.
    /// Listing_Standard.CheckboxLabeled -> Widgets.CheckboxLabeled -> Widgets.Label. Since
    /// every one of those is independently patched, a naive Prefix/Postfix would record one
    /// logical control 2-3 times. ModSettingsCaptureState.EnterWidget/ExitWidget track a
    /// nesting depth so only the OUTERMOST patched call for a given control records or
    /// applies anything; inner calls still balance Enter/Exit (via try/finally) but are
    /// otherwise no-ops. Widgets.Label is the exception - it's always a leaf that never
    /// calls another patched widget, so it just checks AtTopLevel instead of touching depth.
    ///
    /// Each composite (non-Label) patched method gets two responsibilities, but only for the
    /// outermost call:
    ///  1. Prefix - record a <see cref="CapturedWidget"/> describing this control as drawn
    ///     this frame (its DrawIndex, label, current value) for ModSettingsMenuState to show.
    ///  2. Postfix - if ModSettingsMenuState queued a pending activation targeting this exact
    ///     (DrawIndex, Label, Kind), apply it: write straight to the ref parameter for
    ///     checkbox/slider controls (the ref aliases the mod's own field, so it persists),
    ///     or override __result for return-value controls (button/radio/SliderLabeled) so the
    ///     mod's own "if (ButtonText(...))" / "x = SliderLabeled(...)" code reacts exactly as
    ///     if the user had clicked or dragged it. Applying on the outermost call is correct -
    ///     its ref/return value is what the mod's own code actually reads.
    ///
    /// Overloads are disambiguated by explicit argument-type arrays where more than one
    /// overload shares a name (verified against the RimWorld 1.6 reference assembly).
    /// The raw Widgets.HorizontalSlider is intentionally not patched - out of scope for
    /// this pass, same as text fields and dropdowns.
    /// </summary>
    [HarmonyPatch]
    public static class ModSettingsWidgetPatches
    {
        // ============================================================
        // Listing_Standard.CheckboxLabeled(string, ref bool, string, float, float)
        // Calls raw Widgets.CheckboxLabeled internally.
        // ============================================================

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.CheckboxLabeled),
            new Type[] { typeof(string), typeof(bool), typeof(string), typeof(float), typeof(float) },
            new ArgumentType[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal })]
        [HarmonyPrefix]
        public static void ListingCheckboxLabeled_Prefix(string label, ref bool checkOn, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.EnterWidget()) return;
            __state = ModSettingsCaptureState.RecordWidget(CapturedWidget.Kind.Checkbox, label, boolValue: checkOn);
        }

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.CheckboxLabeled),
            new Type[] { typeof(string), typeof(bool), typeof(string), typeof(float), typeof(float) },
            new ArgumentType[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal })]
        [HarmonyPostfix]
        public static void ListingCheckboxLabeled_Postfix(string label, ref bool checkOn, int __state)
        {
            if (!ModSettingsCaptureState.Capturing) return;
            try
            {
                if (__state >= 0 && ModSettingsCaptureState.TryConsumePendingBool(__state, label, CapturedWidget.Kind.Checkbox, out bool newValue))
                    checkOn = newValue;
            }
            finally
            {
                ModSettingsCaptureState.ExitWidget();
            }
        }

        // ============================================================
        // Listing_Standard.ButtonText(string, string, float)
        // Calls raw Widgets.ButtonText internally.
        // ============================================================

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.ButtonText))]
        [HarmonyPrefix]
        public static void ListingButtonText_Prefix(string label, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.EnterWidget()) return;
            __state = ModSettingsCaptureState.RecordWidget(CapturedWidget.Kind.Button, label);
        }

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.ButtonText))]
        [HarmonyPostfix]
        public static void ListingButtonText_Postfix(string label, int __state, ref bool __result)
        {
            if (!ModSettingsCaptureState.Capturing) return;
            try
            {
                if (__state >= 0 && ModSettingsCaptureState.TryConsumePendingActivation(__state, label, CapturedWidget.Kind.Button))
                    __result = true;
            }
            finally
            {
                ModSettingsCaptureState.ExitWidget();
            }
        }

        // ============================================================
        // Listing_Standard.RadioButton(string, bool, float, string, float?)
        // Calls the (unpatched) 7-param RadioButton overload -> Widgets.RadioButtonLabeled
        // -> Widgets.Label internally.
        // ============================================================

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.RadioButton),
            new Type[] { typeof(string), typeof(bool), typeof(float), typeof(string), typeof(float?) })]
        [HarmonyPrefix]
        public static void ListingRadioButton_Prefix(string label, bool active, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.EnterWidget()) return;
            __state = ModSettingsCaptureState.RecordWidget(CapturedWidget.Kind.RadioButton, label, boolValue: active);
        }

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.RadioButton),
            new Type[] { typeof(string), typeof(bool), typeof(float), typeof(string), typeof(float?) })]
        [HarmonyPostfix]
        public static void ListingRadioButton_Postfix(string label, int __state, ref bool __result)
        {
            if (!ModSettingsCaptureState.Capturing) return;
            try
            {
                if (__state >= 0 && ModSettingsCaptureState.TryConsumePendingActivation(__state, label, CapturedWidget.Kind.RadioButton))
                    __result = true;
            }
            finally
            {
                ModSettingsCaptureState.ExitWidget();
            }
        }

        // ============================================================
        // Listing_Standard.SliderLabeled(string, float, float, float, float, string)
        // Calls Widgets.Label (+ the unpatched Widgets.HorizontalSlider) internally.
        // ============================================================

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.SliderLabeled))]
        [HarmonyPrefix]
        public static void ListingSliderLabeled_Prefix(string label, float val, float min, float max, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.EnterWidget()) return;
            __state = ModSettingsCaptureState.RecordWidget(CapturedWidget.Kind.Slider, label, floatValue: val, min: min, max: max);
        }

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.SliderLabeled))]
        [HarmonyPostfix]
        public static void ListingSliderLabeled_Postfix(int __state, ref float __result)
        {
            if (!ModSettingsCaptureState.Capturing) return;
            try
            {
                if (__state >= 0 && ModSettingsCaptureState.TryConsumePendingFloat(__state, CapturedWidget.Kind.Slider, out float newValue))
                    __result = newValue;
            }
            finally
            {
                ModSettingsCaptureState.ExitWidget();
            }
        }

        // ============================================================
        // Listing_Standard.Slider(float val, float min, float max) - a bare slider with NO
        // label of its own; mods draw the "Name: value" text as a separate Label just before
        // it, which RecordSlider adopts. Calls Widgets.HorizontalSlider internally.
        // ============================================================

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.Slider))]
        [HarmonyPrefix]
        public static void ListingSlider_Prefix(float val, float min, float max, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.EnterWidget()) return;
            __state = ModSettingsCaptureState.RecordSlider(null, val, min, max);
        }

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.Slider))]
        [HarmonyPostfix]
        public static void ListingSlider_Postfix(int __state, ref float __result)
        {
            if (!ModSettingsCaptureState.Capturing) return;
            try
            {
                if (__state >= 0 && ModSettingsCaptureState.TryConsumePendingFloat(__state, CapturedWidget.Kind.Slider, out float newValue))
                    __result = newValue;
            }
            finally
            {
                ModSettingsCaptureState.ExitWidget();
            }
        }

        // ============================================================
        // Raw Widgets.HorizontalSlider(Rect, ref float value, FloatRange range, string, float)
        // The by-ref overload - apply writes the ref directly. Bare (label usually null), so
        // RecordSlider adopts the preceding Label.
        // ============================================================

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.HorizontalSlider),
            new Type[] { typeof(Rect), typeof(float), typeof(FloatRange), typeof(string), typeof(float) },
            new ArgumentType[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal })]
        [HarmonyPrefix]
        public static void WidgetsHorizontalSliderRef_Prefix(ref float value, FloatRange range, string label, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.EnterWidget()) return;
            __state = ModSettingsCaptureState.RecordSlider(label, value, range.min, range.max);
        }

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.HorizontalSlider),
            new Type[] { typeof(Rect), typeof(float), typeof(FloatRange), typeof(string), typeof(float) },
            new ArgumentType[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal })]
        [HarmonyPostfix]
        public static void WidgetsHorizontalSliderRef_Postfix(ref float value, int __state)
        {
            if (!ModSettingsCaptureState.Capturing) return;
            try
            {
                if (__state >= 0 && ModSettingsCaptureState.TryConsumePendingFloat(__state, CapturedWidget.Kind.Slider, out float newValue))
                    value = newValue;
            }
            finally
            {
                ModSettingsCaptureState.ExitWidget();
            }
        }

        // ============================================================
        // Raw Widgets.HorizontalSlider(Rect, float value, float min, float max, bool, string,
        // string, string, float) - the classic overload; apply overrides the return value.
        // ============================================================

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.HorizontalSlider),
            new Type[] { typeof(Rect), typeof(float), typeof(float), typeof(float), typeof(bool), typeof(string), typeof(string), typeof(string), typeof(float) })]
        [HarmonyPrefix]
        public static void WidgetsHorizontalSlider_Prefix(float value, float min, float max, string label, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.EnterWidget()) return;
            __state = ModSettingsCaptureState.RecordSlider(label, value, min, max);
        }

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.HorizontalSlider),
            new Type[] { typeof(Rect), typeof(float), typeof(float), typeof(float), typeof(bool), typeof(string), typeof(string), typeof(string), typeof(float) })]
        [HarmonyPostfix]
        public static void WidgetsHorizontalSlider_Postfix(int __state, ref float __result)
        {
            if (!ModSettingsCaptureState.Capturing) return;
            try
            {
                if (__state >= 0 && ModSettingsCaptureState.TryConsumePendingFloat(__state, CapturedWidget.Kind.Slider, out float newValue))
                    __result = newValue;
            }
            finally
            {
                ModSettingsCaptureState.ExitWidget();
            }
        }

        // ============================================================
        // Listing_Standard.Label(string, float, TipSignal?) - capture only, never activatable.
        // Calls Widgets.Label internally, so it still needs Enter/Exit bracketing even
        // though there's nothing to apply.
        // ============================================================

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.Label),
            new Type[] { typeof(string), typeof(float), typeof(TipSignal?) })]
        [HarmonyPrefix]
        public static void ListingLabel_Prefix(string label, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.EnterWidget()) return;
            __state = 0;
            ModSettingsCaptureState.RecordWidget(CapturedWidget.Kind.Label, label);
        }

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.Label),
            new Type[] { typeof(string), typeof(float), typeof(TipSignal?) })]
        [HarmonyPostfix]
        public static void ListingLabel_Postfix(int __state)
        {
            if (!ModSettingsCaptureState.Capturing) return;
            ModSettingsCaptureState.ExitWidget();
        }

        // ============================================================
        // Raw Widgets.CheckboxLabeled(Rect, string, ref bool, bool, Texture2D, Texture2D, bool, bool)
        // Calls Widgets.Label internally.
        // ============================================================

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.CheckboxLabeled))]
        [HarmonyPrefix]
        public static void WidgetsCheckboxLabeled_Prefix(string label, ref bool checkOn, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.EnterWidget()) return;
            __state = ModSettingsCaptureState.RecordWidget(CapturedWidget.Kind.Checkbox, label, boolValue: checkOn);
        }

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.CheckboxLabeled))]
        [HarmonyPostfix]
        public static void WidgetsCheckboxLabeled_Postfix(string label, ref bool checkOn, int __state)
        {
            if (!ModSettingsCaptureState.Capturing) return;
            try
            {
                if (__state >= 0 && ModSettingsCaptureState.TryConsumePendingBool(__state, label, CapturedWidget.Kind.Checkbox, out bool newValue))
                    checkOn = newValue;
            }
            finally
            {
                ModSettingsCaptureState.ExitWidget();
            }
        }

        // ============================================================
        // Raw Widgets.ButtonText(Rect, string, bool, bool, bool, TextAnchor?)
        // ============================================================

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText),
            new Type[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(TextAnchor?) })]
        [HarmonyPrefix]
        public static void WidgetsButtonText_Prefix(string label, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.EnterWidget()) return;
            __state = ModSettingsCaptureState.RecordWidget(CapturedWidget.Kind.Button, label);
        }

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText),
            new Type[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(TextAnchor?) })]
        [HarmonyPostfix]
        public static void WidgetsButtonText_Postfix(string label, int __state, ref bool __result)
        {
            if (!ModSettingsCaptureState.Capturing) return;
            try
            {
                if (__state >= 0 && ModSettingsCaptureState.TryConsumePendingActivation(__state, label, CapturedWidget.Kind.Button))
                    __result = true;
            }
            finally
            {
                ModSettingsCaptureState.ExitWidget();
            }
        }

        // ============================================================
        // Raw Widgets.RadioButtonLabeled(Rect, string, bool, bool)
        // Calls Widgets.Label internally.
        // ============================================================

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.RadioButtonLabeled))]
        [HarmonyPrefix]
        public static void WidgetsRadioButtonLabeled_Prefix(string labelText, bool chosen, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.EnterWidget()) return;
            __state = ModSettingsCaptureState.RecordWidget(CapturedWidget.Kind.RadioButton, labelText, boolValue: chosen);
        }

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.RadioButtonLabeled))]
        [HarmonyPostfix]
        public static void WidgetsRadioButtonLabeled_Postfix(string labelText, int __state, ref bool __result)
        {
            if (!ModSettingsCaptureState.Capturing) return;
            try
            {
                if (__state >= 0 && ModSettingsCaptureState.TryConsumePendingActivation(__state, labelText, CapturedWidget.Kind.RadioButton))
                    __result = true;
            }
            finally
            {
                ModSettingsCaptureState.ExitWidget();
            }
        }

        // ============================================================
        // Raw Widgets.Label(Rect, string) - capture only, never activatable, and always a
        // leaf (never calls another patched widget). Records only when called at the top
        // level (i.e. directly by the mod, not nested inside some other patched widget's
        // own internal Widgets.Label call) - so it never touches captureDepth itself.
        // ============================================================

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.Label),
            new Type[] { typeof(Rect), typeof(string) })]
        [HarmonyPrefix]
        public static void WidgetsLabel_Prefix(string label)
        {
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.AtTopLevel) return;
            ModSettingsCaptureState.RecordWidget(CapturedWidget.Kind.Label, label);
        }
    }
}
