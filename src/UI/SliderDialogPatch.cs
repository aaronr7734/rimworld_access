using System;
using HarmonyLib;
using UnityEngine;
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

        /// <summary>
        /// Redirects RimWorld's Accept-key (Enter) handling for an active <see cref="Dialog_Slider"/>
        /// to our <see cref="SliderDialogState.Confirm"/>. Vanilla's <c>Window.OnAcceptKeyPressed</c>
        /// just calls <c>Close()</c> when <c>closeOnAccept</c> is true (which it is for Dialog_Slider),
        /// dismissing the dialog WITHOUT invoking its confirm action — so e.g. "Refuel from cargo" would
        /// close on Enter but never apply the chosen amount. We confirm instead, then skip the original.
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
        public static class Window_OnAcceptKeyPressed_Slider_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (!(__instance is Dialog_Slider) || !SliderDialogState.IsActive) return true;
                try
                {
                    SliderDialogState.Confirm();
                    Event.current?.Use();
                }
                catch (Exception ex) { Log.Error($"[SliderDialogPatch] OnAcceptKeyPressed failed: {ex.Message}"); }
                return false; // Skip vanilla's close-without-confirm.
            }
        }
    }
}
