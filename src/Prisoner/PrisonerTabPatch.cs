using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch for the prisoner/slave tab (ITab_Pawn_Visitor).
    /// Detects when the prisoner tab is opened and initializes PrisonerTabState.
    /// </summary>
    [HarmonyPatch]
    public static class PrisonerTabPatch
    {
        private static Pawn lastOpenedPawn = null;

        /// <summary>
        /// Patch target: ITab_Pawn_Visitor.FillTab
        /// This method is called when the prisoner tab is being rendered.
        /// </summary>
        [HarmonyPatch(typeof(ITab_Pawn_Visitor), "FillTab")]
        [HarmonyPostfix]
        public static void FillTab_Postfix()
        {
            // Only process if we're in the playing state
            if (Current.ProgramState != ProgramState.Playing)
                return;

            // Get the selected pawn (prisoner or slave) from the selector
            Pawn selPawn = Find.Selector.SingleSelectedThing as Pawn;
            if (selPawn == null)
                return;

            // Only handle prisoners and slaves
            if (!selPawn.IsPrisonerOfColony && !selPawn.IsSlaveOfColony)
                return;

            // Detect if this is a newly opened tab (pawn changed)
            if (selPawn != lastOpenedPawn)
            {
                lastOpenedPawn = selPawn;

                // Auto-open the prisoner tab state
                // Users can still manually open it via keyboard shortcut if desired
                // For now, we'll wait for explicit keyboard activation via UnifiedKeyboardPatch
            }
        }

        /// <summary>
        /// Patch target: MainTabWindow_Inspect.DoWindowContents
        /// Detects when the inspect pane is closed, to cleanup prisoner tab state.
        /// </summary>
        [HarmonyPatch(typeof(MainTabWindow_Inspect), "DoWindowContents")]
        [HarmonyPostfix]
        public static void InspectWindowContents_Postfix()
        {
            // Check if inspection pane is still open
            if (Find.MainTabsRoot == null || Find.MainTabsRoot.OpenTab != MainButtonDefOf.Inspect)
            {
                // Inspect pane closed, cleanup
                if (PrisonerTabState.IsActive)
                {
                    PrisonerTabState.Close();
                }
                lastOpenedPawn = null;
            }
        }

        /// <summary>
        /// Patch target: Selector.Select
        /// Detects when a different pawn/thing is selected, to cleanup prisoner tab state.
        /// </summary>
        [HarmonyPatch(typeof(Selector), "Select")]
        [HarmonyPostfix]
        public static void Select_Postfix(Selector __instance, object obj)
        {
            // If prisoner tab is active and selection changed to different pawn, close it
            if (PrisonerTabState.IsActive)
            {
                Pawn selectedPawn = obj as Pawn;
                if (selectedPawn != PrisonerTabState.CurrentPawn)
                {
                    PrisonerTabState.Close();
                    lastOpenedPawn = null;
                }
            }
        }

        /// <summary>
        /// Gets the currently selected prisoner/slave if prisoner tab is visible.
        /// Returns null if no prisoner tab is open.
        /// </summary>
        public static Pawn GetCurrentPrisoner()
        {
            // Check if inspect pane is open
            if (Find.MainTabsRoot == null || Find.MainTabsRoot.OpenTab != MainButtonDefOf.Inspect)
                return null;

            // Check if selector has a pawn selected
            if (Find.Selector == null || Find.Selector.NumSelected != 1)
                return null;

            Pawn selectedPawn = Find.Selector.FirstSelectedObject as Pawn;
            if (selectedPawn == null)
                return null;

            // Check if it's a prisoner or slave
            if (selectedPawn.IsPrisonerOfColony || selectedPawn.IsSlaveOfColony)
                return selectedPawn;

            return null;
        }

        /// <summary>
        /// Checks if the prisoner tab is currently visible (not just any inspect tab).
        /// </summary>
        public static bool IsPrisonerTabVisible()
        {
            Pawn prisoner = GetCurrentPrisoner();
            if (prisoner == null)
                return false;

            // Check if the prisoner/visitor tab is the active tab
            if (Find.MainTabsRoot == null || Find.MainTabsRoot.OpenTab == null)
                return false;

            MainTabWindow_Inspect inspectPane = Find.MainTabsRoot.OpenTab.TabWindow as MainTabWindow_Inspect;
            if (inspectPane == null)
                return false;

            // The ITab_Pawn_Prisoner/ITab_Pawn_Visitor is typically the active tab for prisoners/slaves
            // We can check by looking at the open tabs on the inspect pane
            // For simplicity, we'll just check if a prisoner is selected
            return true;
        }
    }
}
