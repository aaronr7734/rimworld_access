using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The state popup closes itself once the pointer strays 95 px outside it, as a float menu does,
    /// and a keyboard player's pointer is wherever it was left. While a scope drives the popup and the
    /// pointer is outside, its fade-and-close check is skipped and it stays drawn at full opacity.
    /// </summary>
    internal static class ClTagStatePopupVanishPatch
    {
        public static void Install(Harmony harmony)
        {
            MethodInfo target = ClCompat.Dialogs.PopupUpdateBaseColor;
            if (target != null)
            {
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(ClTagStatePopupVanishPatch), nameof(Prefix)));
            }
        }

        public static bool Prefix(Window __instance, ref bool __result)
        {
            if (Event.current == null || !ScopeForWindow.HasAttachedScope(__instance))
            {
                return true;
            }
            var local = new Rect(0f, 0f, __instance.windowRect.width, __instance.windowRect.height);
            if (local.Contains(Event.current.mousePosition))
            {
                return true;
            }
            __result = false;
            return false;
        }
    }
}
