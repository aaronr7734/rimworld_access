using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(Page_ConfigureStartingPawns))]
    [HarmonyPatch("PreOpen")]
    public static class StartingPawnPreOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Page_ConfigureStartingPawns __instance)
        {
            try
            {
                // Close world navigation state left over from site selection
                if (WorldNavigationState.IsActive)
                {
                    WorldNavigationState.Close();
                    StartingSiteContext.Close();
                }

                StartingPawnPatch.SetInstance(__instance);
                StartingPawnState.Open();

                // Restore IMGUI focus to this page after dialog closures
                Find.WindowStack.Notify_ManuallySetFocus(__instance);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in StartingPawnPreOpenPatch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Window))]
    [HarmonyPatch("PostClose")]
    public static class StartingPawnPostClosePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Window __instance)
        {
            if (!(__instance is Page_ConfigureStartingPawns)) return;
            try
            {
                StartingPawnState.Close();
                StartingPawnPatch.SetInstance(null);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in StartingPawnPostClosePatch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Page_ConfigureStartingPawns))]
    [HarmonyPatch("DoWindowContents")]
    public static class StartingPawnDoWindowContentsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Page_ConfigureStartingPawns __instance)
        {
            try
            {
                if (!StartingPawnState.IsActive) return;

                // Check if rename dialog just closed and tree needs rebuilding
                StartingPawnState.CheckPendingRenameRebuild();

                // Sync curPawnIndex with our tree selection
                int pawnIdx = StartingPawnState.GetSelectedPawnIndex();
                AccessTools.Field(typeof(Page_ConfigureStartingPawns), "curPawnIndex")
                    .SetValue(__instance, pawnIdx);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in StartingPawnDoWindowContentsPatch: {ex}");
            }
        }
    }

    /// <summary>
    /// Block DoNext when our states are active to prevent accidental advancement.
    /// The game calls DoNext from DoBottomButtons when the Start button is clicked.
    /// We want Enter to go through our confirmation dialog instead.
    /// </summary>
    [HarmonyPatch(typeof(Page))]
    [HarmonyPatch("DoNext")]
    public static class StartingPawnDoNextBlockPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Page __instance)
        {
            if (__instance is Page_ConfigureStartingPawns)
            {
                // Block DoNext when our overlay menus are active
                if (WindowlessFloatMenuState.IsActive || InfoCardState.IsActive)
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Block DoBack when our overlay menus are active to prevent Escape from
    /// closing the chargen page. Page.DoBottomButtons checks KeyBindingDefOf.Cancel
    /// directly and calls DoBack(), bypassing closeOnCancel entirely.
    /// </summary>
    [HarmonyPatch(typeof(Page))]
    [HarmonyPatch("DoBack")]
    public static class StartingPawnDoBackBlockPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Page __instance)
        {
            if (__instance is Page_ConfigureStartingPawns)
            {
                bool blocked = WindowlessFloatMenuState.IsActive || WindowlessDialogState.IsActive || InfoCardState.IsActive;
                if (blocked)
                    return false;
                // Mark the frame so cascading DoBack on the previous page is blocked
                StartingPawnPatch.backHandledOnFrame = Time.frameCount;
            }
            if (__instance is Page_SelectStartingSite)
            {
                if (StartingPawnPatch.backHandledOnFrame == Time.frameCount)
                    return false;
            }
            return true;
        }
    }

    public static class StartingPawnPatch
    {
        private static Page_ConfigureStartingPawns instance;
        public static int backHandledOnFrame = -1;

        public static void SetInstance(Page_ConfigureStartingPawns page)
        {
            instance = page;
        }

        public static Page_ConfigureStartingPawns GetInstance()
        {
            return instance;
        }

        public static bool CanDoNext()
        {
            if (instance == null) return false;
            try
            {
                return (bool)AccessTools.Method(typeof(Page_ConfigureStartingPawns), "CanDoNext")
                    .Invoke(instance, null);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error calling CanDoNext: {ex}");
                return false;
            }
        }

        public static void DoNext()
        {
            if (instance == null) return;
            try
            {
                AccessTools.Method(typeof(Page_ConfigureStartingPawns), "DoNext").Invoke(instance, null);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error calling DoNext: {ex}");
            }
        }

        public static void DoBack()
        {
            if (instance == null) return;
            try
            {
                backHandledOnFrame = Time.frameCount;
                AccessTools.Method(typeof(Page), "DoBack").Invoke(instance, null);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error calling DoBack: {ex}");
            }
        }
    }
}
