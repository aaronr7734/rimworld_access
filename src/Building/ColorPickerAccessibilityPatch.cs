using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Wires <see cref="ColorPickerAccessibilityState"/> into the lifecycle of every
    /// <see cref="Dialog_ColorPickerBase"/> (the light "change color" gizmo's
    /// <see cref="Dialog_GlowerColorPicker"/>, and <see cref="Dialog_AllowedAreaColorPicker"/>, which
    /// shares the identical inaccessible mouse-only UI). No <c>OnCancelKeyPressed</c> or
    /// <c>OnAcceptKeyPressed</c> patch is needed here — see the design notes on
    /// <see cref="ColorPickerAccessibilityState"/> for why, mirroring <c>SliderDialogPatch</c>.
    /// </summary>
    public static class ColorPickerAccessibilityPatch
    {
        [HarmonyPatch(typeof(Window), "PostOpen")]
        public static class Window_PostOpen_ColorPicker_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_ColorPickerBase picker)) return;
                try { ColorPickerAccessibilityState.Open(picker); }
                catch (Exception ex) { Log.Error($"[ColorPickerAccessibilityPatch] PostOpen failed: {ex.Message}"); }
            }
        }

        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_ColorPicker_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_ColorPickerBase)) return;
                try
                {
                    if (ColorPickerAccessibilityState.IsActive)
                        ColorPickerAccessibilityState.Close();
                }
                catch (Exception ex) { Log.Error($"[ColorPickerAccessibilityPatch] PostClose failed: {ex.Message}"); }
            }
        }
    }
}
