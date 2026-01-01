using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to intercept the Wildlife tab opening and replace it with our windowless version.
    /// Since MainTabWindow_Wildlife doesn't override DoWindowContents, we patch the base class
    /// MainTabWindow_PawnTable.DoWindowContents and check the instance type.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_PawnTable), nameof(MainTabWindow_PawnTable.DoWindowContents))]
    public static class WildlifeMenuPatch
    {
        private static bool hasIntercepted = false;

        [HarmonyPrefix]
        public static bool Prefix(MainTabWindow_PawnTable __instance)
        {
            // Only intercept Wildlife windows, not other PawnTable windows (like Animals)
            if (!(__instance is MainTabWindow_Wildlife))
            {
                return true; // Let other windows proceed normally
            }

            // Only intercept once per window opening
            if (!hasIntercepted)
            {
                hasIntercepted = true;

                // Open our windowless version instead
                WildlifeMenuState.Open();

                // Close the window that was just opened
                Find.WindowStack.TryRemove(typeof(MainTabWindow_Wildlife), doCloseSound: false);

                // Reset flag after a brief delay to allow for future opens
                hasIntercepted = false;

                // Return false to prevent the original DoWindowContents from executing
                return false;
            }

            return true;
        }
    }
}
