using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Ambient map claims: the four position-gated
    /// openers that have no menu/mode of their own — Ctrl+V
    /// (paste a copied plan), I (open colony inventory), P (open the
    /// prisoner tab, only when one is visible), and Escape (open the real
    /// pause menu). Unlike <see cref="MapScope.TimeAnnounce.Game.cs"/>'s
    /// cluster, none of these four is registered on <see cref="WorldScope"/>:
    /// their legacy handlers' own written gates checked <c>Find.CurrentMap != null</c>
    /// with no <c>WorldNavigationState.IsActive</c> alternative term, so
    /// world-view reachability is not part of their contract (see each
    /// claim's own gate below). MapScope-exclusive registration
    /// is also structurally required for the I opener specifically:
    /// <see cref="AmbientScopeSelector"/> swaps the focus stack's BASE
    /// exclusively between MapScope and WorldScope, never both — while the
    /// planet is rendered MapScope is not merely shadowed but entirely absent
    /// from the stack, so a MapScope-only claim cannot fire there regardless
    /// of the state of <see cref="Verse.Find.CurrentMap"/> (WorldScope's own
    /// <c>world.caravan.inspect</c> claim already owns bare I on the world view
    /// for an unrelated purpose).
    ///
    /// Every gate below drops the legacy handler's own
    /// <c>!RimWorldAccess.Shell.FocusStack.AnyLiveModal</c> term for the same
    /// structural reason documented in <see cref="MapScope.TimeAnnounce.Game.cs"/>'s
    /// class remarks: any live MODAL scope above this ambient base already
    /// stops the dispatch walk before it reaches these claims. Every other
    /// term each legacy handler carried is preserved VERBATIM (law J-κ) — see
    /// each gate's own remarks for anything kept despite looking redundant.
    /// Bottom-of-ladder position protection is likewise structural, not a
    /// gate term: any still-open menu's own CharSink swallows a typed
    /// letter/character before chord matching runs, and any live modal scope
    /// stops the walk outright.
    /// </summary>
    public sealed partial class MapScope
    {
        private void RegisterEndgameOpenerClaims()
        {
            Claim("map.plan.paste", OnPastePlan, when: PlanPasteLive);
            Claim("map.menu.inventory", OnOpenInventory, when: InventoryOpenerLive);
            Claim("map.menu.prisonerTab", OnOpenPrisonerTab, when: PrisonerTabOpenerLive);
            Claim("map.pause.open", OnOpenPauseMenu, when: PauseMenuOpenerLive);
        }

        /// <summary>
        /// The legacy handler's gate, verbatim minus !AnyLiveModal — ALL
        /// six explicit state terms plus MapNavigationState.IsInitialized and
        /// TextInputManager.Active == null are kept even though some overlap
        /// other structural protections today (law J-κ: a structurally-
        /// redundant term is never cleaned up, only ever an explicit drop
        /// with its own justification). PlanClipboard.HasContent moves in
        /// from the legacy handler's OUTER test — this is the only gate
        /// mechanism a claim has, so it folds in rather than living outside.
        /// </summary>
        private static bool PlanPasteLive()
        {
            return PlanClipboard.HasContent
                && Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && MapNavigationState.IsInitialized
                && TextInputManager.Active == null
                && !ShapePlacementState.IsActive
                && !GizmoNavigationState.IsActive
                && !WindowlessInspectionState.IsActive
                && !WindowlessFloatMenuState.IsActive
                && !WindowlessInventoryState.IsActive;
        }

        private static void OnPastePlan(KeyEventSnapshot e)
        {
            PlanClipboard.PasteAt(MapNavigationState.CurrentCursorPosition, Find.CurrentMap);
        }

        /// <summary>The legacy handler's gate, verbatim minus !AnyLiveModal.</summary>
        private static bool InventoryOpenerLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !WindowlessInventoryState.IsActive;
        }

        private static void OnOpenInventory(KeyEventSnapshot e)
        {
            WindowlessInventoryState.Open();
        }

        /// <summary>
        /// The legacy handler's gate, verbatim minus !AnyLiveModal, PLUS
        /// the prisoner-lookup folded into the gate itself (rather than left
        /// as an inner if inside the handler) so a bare P with no prisoner
        /// visible is NOT consumed and falls through untouched, exactly as
        /// the legacy handler fell through. The handler re-resolves the same
        /// helper rather than caching the gate's result — defensive against
        /// the prisoner disappearing between the gate check and the handler
        /// running (e.g. death the same frame); GetCurrentPrisoner() has no
        /// side effects, so calling it twice is safe.
        /// </summary>
        private static bool PrisonerTabOpenerLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !PrisonerTabState.IsActive
                && PrisonerTabPatch.GetCurrentPrisoner() != null;
        }

        private static void OnOpenPrisonerTab(KeyEventSnapshot e)
        {
            Pawn prisoner = PrisonerTabPatch.GetCurrentPrisoner();
            if (prisoner != null)
            {
                PrisonerTabState.Open(prisoner);
            }
        }

        /// <summary>
        /// The legacy handler's gate, verbatim minus !AnyLiveModal. Every
        /// other term survives untouched per the migration brief: the
        /// !ShellGuards.MenuOwnsInput() call (relocated verbatim from
        /// KeyboardHelper.IsAnyAccessibilityMenuActive —
        /// porting it verbatim here reproduces the legacy gate exactly
        /// rather than partially inlining it), !CaravanInspectState.IsActive
        /// (redundant-but-harmless — already a ShellGuards.MenuOwnsInput
        /// member), and the Find.Targeter term — NOT redundant: it is the
        /// one thing that lets vanilla's own Escape-cancels-targeting
        /// behavior win during the several targeting sub-modes that have no
        /// cancel claim of their own on TargetingScope (see the J9 slice
        /// report's per-key position audit). The frozen
        /// !QuestLocationsBrowserState / !SettlementBrowserState dead-feature
        /// carve-outs were deleted with those features.
        /// </summary>
        private static bool PauseMenuOpenerLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !ShellGuards.MenuOwnsInput()
                && !CaravanInspectState.IsActive
                && !ExternalMapTargeting.MapTargetingActive;
        }

        private static void OnOpenPauseMenu(KeyEventSnapshot e)
        {
            if (ScreenReturn.ReturnOnCancel(ScreenReturn.Take(ScreenReturn.Binding.Map)))
            {
                return;
            }
            PauseMenuScope.OpenRealMenu();
        }
    }
}
