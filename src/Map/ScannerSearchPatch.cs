using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Patches KeyBindingDef.JustPressed to block RimWorld's built-in search bar.
    /// This prevents RimWorld's inaccessible search from ever opening when on the map/world,
    /// allowing our accessible scanner search to handle all Z key presses instead.
    /// </summary>
    [HarmonyPatch(typeof(KeyBindingDef))]
    [HarmonyPatch("JustPressed", MethodType.Getter)]
    public static class ScannerSearchPatch
    {
        /// <summary>
        /// Returns false for OpenMapSearch keybinding when on map or world map.
        /// This blocks ALL Z key presses (with or without modifiers) from triggering
        /// RimWorld's search dialogs, since JustPressed uses Input.GetKeyDown() which
        /// bypasses Unity's Event system entirely.
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(KeyBindingDef __instance, ref bool __result)
        {
            // Block OpenMapSearch when in playing state on map or world
            if (__instance == KeyBindingDefOf.OpenMapSearch &&
                (Current.ProgramState == ProgramState.Playing || WorldNavigationState.Context == WorldNavContext.WorldGen) &&
                (MapNavigationState.IsInitialized || WorldNavigationState.IsActive))
            {
                __result = false;
                return false; // Skip original method
            }
            return true; // Run original method
        }
    }
}
