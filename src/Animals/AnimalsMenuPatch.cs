using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to intercept the Animals tab opening and replace it with our windowless version
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Animals), nameof(MainTabWindow_Animals.DoWindowContents))]
    public static class AnimalsMenuPatch
    {
        private static bool hasIntercepted = false;

        [HarmonyPrefix]
        public static bool Prefix()
        {
            // Only intercept once per window opening
            if (!hasIntercepted)
            {
                hasIntercepted = true;

                // Open our windowless version instead
                AnimalsMenuState.Open();

                // Close the window that was just opened
                Find.WindowStack.TryRemove(typeof(MainTabWindow_Animals), doCloseSound: false);

                // Reset flag after a brief delay to allow for future opens
                hasIntercepted = false;

                // Return false to prevent the original DoWindowContents from executing
                return false;
            }

            return true;
        }
    }
}
