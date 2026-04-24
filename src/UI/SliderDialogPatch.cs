using System;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Wires <see cref="SliderDialogState"/> into the lifecycle of every <see cref="Dialog_Slider"/>.
    /// </summary>
    public static class SliderDialogPatch
    {
        [HarmonyPatch(typeof(Window), "PostOpen")]
        public static class Window_PostOpen_Slider_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_Slider slider)) return;
                try { SliderDialogState.Open(slider); }
                catch (Exception ex) { Log.Error($"[SliderDialogPatch] PostOpen failed: {ex.Message}"); }
            }
        }

        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_Slider_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_Slider)) return;
                try
                {
                    if (SliderDialogState.IsActive)
                        SliderDialogState.Close();
                }
                catch (Exception ex) { Log.Error($"[SliderDialogPatch] PostClose failed: {ex.Message}"); }
            }
        }
    }
}
