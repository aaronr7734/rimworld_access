using System;
using System.Text.RegularExpressions;
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
    ///
    /// <b>Coverage:</b> checkboxes, buttons, radio buttons, sliders, labels, labeled-button
    /// dropdown rows (ButtonTextLabeled/Pct), text fields (Widgets.TextField / TextArea - the
    /// leaf every string AND generic-numeric field bottoms out in), and integer +/- entries
    /// (IntEntry). Fully custom hand-drawn controls and two-handle range sliders
    /// (Widgets.IntRange / FloatRange) remain out of scope.
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
        // Listing_Standard.ButtonTextLabeled / ButtonTextLabeledPct - a "label: current value"
        // row whose value side is a button. Mods use these for dropdown-style settings: clicking
        // opens a FloatMenu of choices. We capture them as an activatable Button whose label reads
        // "{label}: {value}" so a screen-reader user hears both the setting and its current value;
        // activating returns true, the mod opens its FloatMenu, and DialogInterceptionPatch routes
        // that FloatMenu into the accessible windowless menu (gated on ModSettingsMenuState.IsActive).
        // The display string is rebuilt identically in the Postfix so the pending-activation label
        // check still matches.
        // ============================================================

        /// <summary>Builds the "{label}: {value}" caption used for a labeled-button (dropdown) row.
        /// A blank buttonLabel falls back to just the label.</summary>
        internal static string FormatButtonLabeled(string label, string buttonLabel)
        {
            if (string.IsNullOrEmpty(buttonLabel)) return label ?? "";
            if (string.IsNullOrEmpty(label)) return buttonLabel;
            // Many mods' labels already end with their own colon ("Storage Priority:"); don't add a
            // second one ("Storage Priority:: value").
            string trimmed = label.TrimEnd();
            if (trimmed.EndsWith(":")) trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
            return trimmed + ": " + buttonLabel;
        }

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.ButtonTextLabeled))]
        [HarmonyPrefix]
        public static void ListingButtonTextLabeled_Prefix(string label, string buttonLabel, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.EnterWidget()) return;
            __state = ModSettingsCaptureState.RecordWidget(CapturedWidget.Kind.Button, FormatButtonLabeled(label, buttonLabel));
        }

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.ButtonTextLabeled))]
        [HarmonyPostfix]
        public static void ListingButtonTextLabeled_Postfix(string label, string buttonLabel, int __state, ref bool __result)
        {
            if (!ModSettingsCaptureState.Capturing) return;
            try
            {
                if (__state >= 0 && ModSettingsCaptureState.TryConsumePendingActivation(__state, FormatButtonLabeled(label, buttonLabel), CapturedWidget.Kind.Button))
                    __result = true;
            }
            finally
            {
                ModSettingsCaptureState.ExitWidget();
            }
        }

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.ButtonTextLabeledPct))]
        [HarmonyPrefix]
        public static void ListingButtonTextLabeledPct_Prefix(string label, string buttonLabel, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.EnterWidget()) return;
            __state = ModSettingsCaptureState.RecordWidget(CapturedWidget.Kind.Button, FormatButtonLabeled(label, buttonLabel));
        }

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.ButtonTextLabeledPct))]
        [HarmonyPostfix]
        public static void ListingButtonTextLabeledPct_Postfix(string label, string buttonLabel, int __state, ref bool __result)
        {
            if (!ModSettingsCaptureState.Capturing) return;
            try
            {
                if (__state >= 0 && ModSettingsCaptureState.TryConsumePendingActivation(__state, FormatButtonLabeled(label, buttonLabel), CapturedWidget.Kind.Button))
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

        // ============================================================
        // Text fields. Widgets.TextField / TextArea are the LEAF every text-like control bottoms
        // out in - not just direct string fields, but also the generic numeric fields
        // (TextFieldNumeric<T> -> TextField) and the labeled wrappers (TextEntryLabeled /
        // TextFieldNumericLabeled draw a Widgets.Label then call TextField). Patching only these
        // leaves therefore captures every text/numeric field WITHOUT touching the generic
        // TextFieldNumeric<T> methods (which Harmony cannot reliably patch for value-type T).
        //
        // Like the leaf Widgets.Label, these never call another patched widget, so they check
        // AtTopLevel instead of Enter/Exit-ing the depth: a TextField called INSIDE a composite we
        // record as one control (IntEntry's inner numeric field) is not top-level and is skipped,
        // while one reached only through an unpatched generic wrapper is top-level and records,
        // adopting the standalone Label the mod drew just before it as its caption. Applying a
        // pending edit overrides the returned string; the mod's own "x = TextField(...)" or
        // numeric re-parse then reads the edited value.
        // ============================================================

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.TextField),
            new Type[] { typeof(Rect), typeof(string) })]
        [HarmonyPrefix]
        public static void WidgetsTextField_Prefix(string text, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.AtTopLevel) return;
            __state = ModSettingsCaptureState.RecordTextField(text);
        }

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.TextField),
            new Type[] { typeof(Rect), typeof(string) })]
        [HarmonyPostfix]
        public static void WidgetsTextField_Postfix(string text, int __state, ref string __result)
        {
            if (!ModSettingsCaptureState.Capturing || __state < 0) return;
            if (ModSettingsCaptureState.TryConsumePendingString(__state, CapturedWidget.Kind.TextField, out string newValue))
                __result = newValue;
        }

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.TextField),
            new Type[] { typeof(Rect), typeof(string), typeof(int), typeof(Regex) })]
        [HarmonyPrefix]
        public static void WidgetsTextFieldValidated_Prefix(string text, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.AtTopLevel) return;
            __state = ModSettingsCaptureState.RecordTextField(text);
        }

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.TextField),
            new Type[] { typeof(Rect), typeof(string), typeof(int), typeof(Regex) })]
        [HarmonyPostfix]
        public static void WidgetsTextFieldValidated_Postfix(int __state, ref string __result)
        {
            if (!ModSettingsCaptureState.Capturing || __state < 0) return;
            if (ModSettingsCaptureState.TryConsumePendingString(__state, CapturedWidget.Kind.TextField, out string newValue))
                __result = newValue;
        }

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.TextArea))]
        [HarmonyPrefix]
        public static void WidgetsTextArea_Prefix(string text, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.AtTopLevel) return;
            __state = ModSettingsCaptureState.RecordTextField(text);
        }

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.TextArea))]
        [HarmonyPostfix]
        public static void WidgetsTextArea_Postfix(string text, int __state, ref string __result)
        {
            if (!ModSettingsCaptureState.Capturing || __state < 0) return;
            if (ModSettingsCaptureState.TryConsumePendingString(__state, CapturedWidget.Kind.TextField, out string newValue))
                __result = newValue;
        }

        // ============================================================
        // Integer +/- entry. IntEntry draws four ButtonText (-10/-1/+1/+10) plus an inner numeric
        // TextField; recording those as separate controls would be useless noise, so we record the
        // whole IntEntry as ONE control (adjusted with Left/Right in the menu) and use the depth
        // guard so its inner buttons/field are skipped. Both the Listing_Standard wrapper (which
        // carries the min floor) and the raw Widgets overload are patched; the wrapper is the
        // outer call when used, so it wins and contributes the floor.
        // ============================================================

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.IntEntry))]
        [HarmonyPrefix]
        public static void ListingIntEntry_Prefix(int val, int multiplier, int min, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.EnterWidget()) return;
            __state = ModSettingsCaptureState.RecordIntEntry(val, multiplier, min);
        }

        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.IntEntry))]
        [HarmonyPostfix]
        public static void ListingIntEntry_Postfix(ref int val, ref string editBuffer, int min, int __state)
        {
            if (!ModSettingsCaptureState.Capturing) return;
            try
            {
                if (__state >= 0 && ModSettingsCaptureState.TryConsumePendingFloat(__state, CapturedWidget.Kind.IntEntry, out float newValue))
                {
                    int applied = Mathf.Max(min, Mathf.RoundToInt(newValue));
                    val = applied;
                    editBuffer = applied.ToString();
                }
            }
            finally
            {
                ModSettingsCaptureState.ExitWidget();
            }
        }

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.IntEntry))]
        [HarmonyPrefix]
        public static void WidgetsIntEntry_Prefix(int value, int multiplier, out int __state)
        {
            __state = -1;
            if (!ModSettingsCaptureState.Capturing) return;
            if (!ModSettingsCaptureState.EnterWidget()) return;
            __state = ModSettingsCaptureState.RecordIntEntry(value, multiplier, 0);
        }

        [HarmonyPatch(typeof(Widgets), nameof(Widgets.IntEntry))]
        [HarmonyPostfix]
        public static void WidgetsIntEntry_Postfix(ref int value, ref string editBuffer, int __state)
        {
            if (!ModSettingsCaptureState.Capturing) return;
            try
            {
                if (__state >= 0 && ModSettingsCaptureState.TryConsumePendingFloat(__state, CapturedWidget.Kind.IntEntry, out float newValue))
                {
                    int applied = Mathf.RoundToInt(newValue);
                    value = applied;
                    editBuffer = applied.ToString();
                }
            }
            finally
            {
                ModSettingsCaptureState.ExitWidget();
            }
        }
    }
}
