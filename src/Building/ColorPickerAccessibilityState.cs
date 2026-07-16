using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard accessibility for vanilla's custom color-picker window
    /// (<see cref="Dialog_ColorPickerBase"/> and its subclasses <see cref="Dialog_GlowerColorPicker"/>
    /// — the "change color" gizmo on lights (<see cref="CompGlower"/>) — and
    /// <see cref="Dialog_AllowedAreaColorPicker"/>). Fixes GitHub issue #57.
    ///
    /// Vanilla's picker is a fully mouse-driven immediate-mode window: an HSV color wheel you drag,
    /// a "color temperature" bar you drag, a palette of 54 unlabeled swatches you click, and two
    /// quick-select swatches (Default / Darklight). None of it has a keyboard equivalent, so a blind
    /// user who opens the gizmo gets a modal dialog with no way to interact with it at all.
    ///
    /// Rather than trying to make the mouse-drag wheel keyboard-operable (a 2D continuous drag pad
    /// has no natural keyboard mapping), this presents the exact same palette as a flat, ordered list
    /// (Default, Darklight if supported, then each of the 54 preset swatches), plus two step-adjustable
    /// Hue / Saturation controls that reach any color the wheel could (brightness is fixed by the
    /// dialog itself via <c>ForcedColorValue</c> for both known subclasses, so no Value/brightness
    /// control is needed). Up/Down move between items; Left/Right move between items too, EXCEPT
    /// while focused on the Hue or Saturation stepper, where they nudge that value instead. Enter
    /// applies the focused swatch/preset as the working color (mirrors clicking a vanilla swatch —
    /// it does not close the dialog), or activates Accept/Cancel. Escape cancels.
    ///
    /// The real vanilla window is left in the WindowStack exactly like <see cref="SliderDialogState"/>
    /// does for <c>Dialog_Slider</c> — we don't need to intercept <c>WindowStack.Add</c> at all here.
    /// Every key we handle calls <c>Event.current.Use()</c> in <see cref="UnifiedKeyboardPatch"/>
    /// before the vanilla window's own <c>DoWindowContents</c> runs later in the same
    /// <c>UIRootOnGUI</c> call, so the mouse-only widgets underneath never see the keystroke.
    /// Escape closing the dialog ourselves before vanilla's own
    /// <c>KeyBindingDefOf.Cancel.KeyDownEvent &amp;&amp; IsOpen</c> check runs means that check sees
    /// <c>IsOpen == false</c> and never calls <c>Window.OnCancelKeyPressed</c> a second time — no
    /// <c>Window.OnCancelKeyPressed</c> patch is needed (unlike the CaravanFormation/QuantityMenu
    /// stacking case in the root CLAUDE.md, there is no separate outer dialog left open underneath
    /// us to be double-closed). Enter needs no <c>OnAcceptKeyPressed</c> patch either: the base
    /// dialog's constructor sets <c>closeOnAccept = false</c>, so vanilla's default Accept handling
    /// is already a no-op for this window.
    /// </summary>
    public static class ColorPickerAccessibilityState
    {
        private static readonly FieldInfo ColorField =
            AccessTools.Field(typeof(Dialog_ColorPickerBase), "color");
        private static readonly FieldInfo OldColorField =
            AccessTools.Field(typeof(Dialog_ColorPickerBase), "oldColor");
        private static readonly PropertyInfo PickableColorsProp =
            AccessTools.Property(typeof(Dialog_ColorPickerBase), "PickableColors");
        private static readonly PropertyInfo DefaultColorProp =
            AccessTools.Property(typeof(Dialog_ColorPickerBase), "DefaultColor");
        private static readonly PropertyInfo ShowDarklightProp =
            AccessTools.Property(typeof(Dialog_ColorPickerBase), "ShowDarklight");
        private static readonly PropertyInfo ForcedColorValueProp =
            AccessTools.Property(typeof(Dialog_ColorPickerBase), "ForcedColorValue");
        private static readonly MethodInfo SaveColorMethod =
            AccessTools.Method(typeof(Dialog_ColorPickerBase), "SaveColor", new[] { typeof(Color) });

        private const float HueStepDegrees = 15f;
        private const float SatStepPercent = 0.05f;

        private static bool isActive;
        private static Dialog_ColorPickerBase currentDialog;
        private static List<Color> palette = new List<Color>();
        private static bool showDarklight;
        private static float forcedValue;
        private static int selectedIndex;

        // Flat item layout, computed once at Open():
        //   0                       -> Default
        //   1 (only if showDarklight) -> Darklight
        //   paletteStart..paletteStart+palette.Count-1 -> palette swatches
        //   hueIndex                -> Hue stepper
        //   satIndex                -> Saturation stepper
        //   acceptIndex             -> Accept
        //   cancelIndex             -> Cancel (== totalCount - 1)
        private static int paletteStart;
        private static int hueIndex;
        private static int satIndex;
        private static int acceptIndex;
        private static int cancelIndex;
        private static int totalCount;

        public static bool IsActive => isActive;

        public static void Open(Dialog_ColorPickerBase dialog)
        {
            if (dialog == null) return;
            try
            {
                currentDialog = dialog;
                palette = PickableColorsProp?.GetValue(dialog) as List<Color> ?? new List<Color>();
                showDarklight = ShowDarklightProp != null && (bool)ShowDarklightProp.GetValue(dialog);
                forcedValue = ForcedColorValueProp != null ? (float)ForcedColorValueProp.GetValue(dialog) : -1f;

                paletteStart = 1 + (showDarklight ? 1 : 0);
                hueIndex = paletteStart + palette.Count;
                satIndex = hueIndex + 1;
                acceptIndex = satIndex + 1;
                cancelIndex = acceptIndex + 1;
                totalCount = cancelIndex + 1;

                selectedIndex = 0;
                isActive = true;

                Color current = GetColor();
                Color old = OldColorField != null ? (Color)OldColorField.GetValue(dialog) : current;
                string announcement = (string)"RimWorldAccess.Building.ColorPicker.Opened".Translate(
                    DescribeColor(current), DescribeColor(old), palette.Count.ToString());
                TolkHelper.SpeakData(announcement, SpeechPriority.High);
            }
            catch (Exception ex)
            {
                Log.Error($"[ColorPickerAccessibilityState] Open failed: {ex.Message}");
                Close();
            }
        }

        public static void Close()
        {
            isActive = false;
            currentDialog = null;
            palette = new List<Color>();
            selectedIndex = 0;
        }

        public static bool HandleInput(Event evt)
        {
            if (!isActive || currentDialog == null) return false;
            if (evt.type != EventType.KeyDown) return false;

            KeyCode key = evt.keyCode;

            if (key == KeyCode.Escape)
            {
                Cancel();
                return true;
            }

            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                Activate();
                return true;
            }

            if (key == KeyCode.UpArrow)
            {
                selectedIndex = MenuHelper.SelectPrevious(selectedIndex, totalCount);
                AnnounceCurrent();
                return true;
            }
            if (key == KeyCode.DownArrow)
            {
                selectedIndex = MenuHelper.SelectNext(selectedIndex, totalCount);
                AnnounceCurrent();
                return true;
            }

            if (key == KeyCode.LeftArrow)
            {
                if (selectedIndex == hueIndex) { AdjustHue(-HueStepDegrees); return true; }
                if (selectedIndex == satIndex) { AdjustSaturation(-SatStepPercent); return true; }
                selectedIndex = MenuHelper.SelectPrevious(selectedIndex, totalCount);
                AnnounceCurrent();
                return true;
            }
            if (key == KeyCode.RightArrow)
            {
                if (selectedIndex == hueIndex) { AdjustHue(HueStepDegrees); return true; }
                if (selectedIndex == satIndex) { AdjustSaturation(SatStepPercent); return true; }
                selectedIndex = MenuHelper.SelectNext(selectedIndex, totalCount);
                AnnounceCurrent();
                return true;
            }

            if (key == KeyCode.Home)
            {
                selectedIndex = 0;
                AnnounceCurrent();
                return true;
            }
            if (key == KeyCode.End)
            {
                selectedIndex = totalCount - 1;
                AnnounceCurrent();
                return true;
            }

            // Modal: consume any other key so it doesn't reach the vanilla widgets underneath.
            return true;
        }

        private static void Activate()
        {
            if (selectedIndex == 0)
            {
                ActivateDefault();
            }
            else if (showDarklight && selectedIndex == 1)
            {
                ActivateDarklight();
            }
            else if (selectedIndex >= paletteStart && selectedIndex < paletteStart + palette.Count)
            {
                ActivateSwatch(selectedIndex - paletteStart);
            }
            else if (selectedIndex == hueIndex || selectedIndex == satIndex)
            {
                // Steppers apply live via Left/Right; Enter just re-reads the current value.
                AnnounceCurrent();
            }
            else if (selectedIndex == acceptIndex)
            {
                Accept();
            }
            else if (selectedIndex == cancelIndex)
            {
                Cancel();
            }
        }

        private static void ActivateDefault()
        {
            Color raw = DefaultColorProp != null ? (Color)DefaultColorProp.GetValue(currentDialog) : Color.white;
            Color result = ApplyForcedValue(raw);
            SetColor(result);
            TolkHelper.SpeakData(
                (string)"RimWorldAccess.Building.ColorPicker.Selected".Translate(DescribeColor(result)));
        }

        private static void ActivateDarklight()
        {
            // Matches vanilla: the Darklight quick-select swatch uses the raw preset unmodified
            // (no ForcedColorValue adjustment), same as CompGlower's own Darklight toggle gizmo.
            Color result = DarklightUtility.DefaultDarklight;
            SetColor(result);
            TolkHelper.SpeakData(
                (string)"RimWorldAccess.Building.ColorPicker.Selected".Translate(
                    "RimWorldAccess.Building.ColorPicker.Darklight".Translate()));
        }

        private static void ActivateSwatch(int paletteIndex)
        {
            if (paletteIndex < 0 || paletteIndex >= palette.Count) return;
            Color result = palette[paletteIndex];
            SetColor(result);
            TolkHelper.SpeakData(
                (string)"RimWorldAccess.Building.ColorPicker.Selected".Translate(DescribeColor(result)));
        }

        private static void AdjustHue(float deltaDegrees)
        {
            Color.RGBToHSV(GetColor(), out float h, out float s, out float v);
            float newHue = Mathf.Repeat(h + deltaDegrees / 360f, 1f);
            SetColor(BuildColor(newHue, s, v));
            int hueDeg = Mathf.RoundToInt(newHue * 360f) % 360;
            TolkHelper.SpeakData(
                (string)"RimWorldAccess.Building.ColorPicker.HueChanged".Translate(hueDeg.ToString()),
                SpeechPriority.Low);
        }

        private static void AdjustSaturation(float deltaFraction)
        {
            Color.RGBToHSV(GetColor(), out float h, out float s, out float v);
            float newSat = Mathf.Clamp01(s + deltaFraction);
            SetColor(BuildColor(h, newSat, v));
            int satPct = Mathf.RoundToInt(newSat * 100f);
            TolkHelper.SpeakData(
                (string)"RimWorldAccess.Building.ColorPicker.SatChanged".Translate(satPct.ToString()),
                SpeechPriority.Low);
        }

        private static Color BuildColor(float h, float s, float v)
        {
            float value = forcedValue >= 0f ? forcedValue : v;
            Color c = Color.HSVToRGB(Mathf.Repeat(h, 1f), Mathf.Clamp01(s), value);
            c.a = 1f;
            return c;
        }

        private static Color ApplyForcedValue(Color raw)
        {
            if (forcedValue < 0f) return raw;
            Color.RGBToHSV(raw, out float h, out float s, out _);
            Color result = Color.HSVToRGB(h, s, forcedValue);
            result.a = 1f;
            return result;
        }

        private static void Accept()
        {
            try
            {
                Color final = GetColor();
                SaveColorMethod?.Invoke(currentDialog, new object[] { final });
                currentDialog?.Close(false);
                string desc = DescribeColor(final);
                Close();
                TolkHelper.SpeakData(
                    (string)"RimWorldAccess.Building.ColorPicker.Accepted".Translate(desc),
                    SpeechPriority.High);
            }
            catch (Exception ex)
            {
                Log.Error($"[ColorPickerAccessibilityState] Accept failed: {ex}");
                Close();
            }
        }

        private static void Cancel()
        {
            currentDialog?.Close(false);
            Close();
            TolkHelper.Speak("RimWorldAccess.UI.Cancelled".Loc());
        }

        private static Color GetColor()
        {
            try { return (Color)(ColorField?.GetValue(currentDialog) ?? Color.white); }
            catch { return Color.white; }
        }

        private static void SetColor(Color value)
        {
            try { ColorField?.SetValue(currentDialog, value); }
            catch (Exception ex) { Log.Warning($"[ColorPickerAccessibilityState] SetColor failed: {ex.Message}"); }
        }

        private static void AnnounceCurrent()
        {
            // Default, Darklight, and palette swatches occupy the contiguous range [0, hueIndex),
            // so selectedIndex is already the position within that range — hueIndex is the count.
            if (selectedIndex == 0)
            {
                Color raw = DefaultColorProp != null ? (Color)DefaultColorProp.GetValue(currentDialog) : Color.white;
                Color result = ApplyForcedValue(raw);
                TolkHelper.SpeakData((string)"RimWorldAccess.Building.ColorPicker.DefaultOption".Translate(
                    DescribeColor(result)) + " " + PositionSuffix(selectedIndex, hueIndex));
            }
            else if (showDarklight && selectedIndex == 1)
            {
                TolkHelper.SpeakData((string)"RimWorldAccess.Building.ColorPicker.DarklightOption".Translate(
                    DescribeColor(DarklightUtility.DefaultDarklight)) + " " + PositionSuffix(selectedIndex, hueIndex));
            }
            else if (selectedIndex >= paletteStart && selectedIndex < paletteStart + palette.Count)
            {
                int paletteIndex = selectedIndex - paletteStart;
                string desc = DescribeColor(palette[paletteIndex]);
                string position = PositionSuffix(selectedIndex, hueIndex);
                TolkHelper.SpeakData(desc + " " + position);
            }
            else if (selectedIndex == hueIndex)
            {
                Color.RGBToHSV(GetColor(), out float h, out _, out _);
                int hueDeg = Mathf.RoundToInt(h * 360f) % 360;
                TolkHelper.SpeakData(
                    (string)"RimWorldAccess.Building.ColorPicker.HueStepper".Translate(hueDeg.ToString()));
            }
            else if (selectedIndex == satIndex)
            {
                Color.RGBToHSV(GetColor(), out _, out float s, out _);
                int satPct = Mathf.RoundToInt(s * 100f);
                TolkHelper.SpeakData(
                    (string)"RimWorldAccess.Building.ColorPicker.SatStepper".Translate(satPct.ToString()));
            }
            else if (selectedIndex == acceptIndex)
            {
                TolkHelper.Speak("RimWorldAccess.Building.ColorPicker.Accept".Loc());
            }
            else if (selectedIndex == cancelIndex)
            {
                TolkHelper.Speak("RimWorldAccess.Building.ColorPicker.Cancel".Loc());
            }
        }

        private static string PositionSuffix(int index, int count) => MenuHelper.FormatPosition(index, count);

        /// <summary>
        /// Describes a color by its hue/saturation (brightness is fixed by the dialog, so it carries
        /// no distinguishing information). Near-zero saturation is reported as "White" instead of a
        /// meaningless hue angle, matching how the achromatic first palette entry actually renders.
        /// </summary>
        private static string DescribeColor(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out _);
            if (s < 0.02f)
                return "RimWorldAccess.Building.ColorPicker.White".Translate();
            int hueDeg = Mathf.RoundToInt(h * 360f) % 360;
            int satPct = Mathf.RoundToInt(s * 100f);
            return (string)"RimWorldAccess.Building.ColorPicker.HueSat".Translate(hueDeg.ToString(), satPct.ToString());
        }
    }
}
