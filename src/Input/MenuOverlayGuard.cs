namespace RimWorldAccess
{
    /// <summary>
    /// Single source of truth for "should a map-level hotkey be suppressed because an
    /// accessibility menu or overlay currently owns keyboard input?"
    ///
    /// Historically three separate hand-maintained lists answered this question — one in
    /// <see cref="MapNavigationPatch"/> (arrow keys), one in <c>DetailInfoPatch</c>
    /// (tile-info keys 1-7), and one inline in <c>UnifiedKeyboardPatch</c> (time-speed
    /// keys 1/2/3). They drifted apart, so the number row leaked through to the map
    /// underneath menus the arrow list already knew about (architect tree, area menu,
    /// shape picker, play settings, history tab, ...).
    ///
    /// This type makes the arrow-suppression set the canonical "a menu is open" signal
    /// (it is exercised constantly, so it stays the most complete and correct list) and
    /// layers each call site's genuine differences on top of it. Placement and cursor
    /// modes (architect placement, shape placement, viewing mode) deliberately keep the
    /// map cursor live, so they are NOT part of the base signal — but time-speed keys are
    /// the one class that should still be blocked during placement, so
    /// <see cref="BlockTimeSpeedKeys"/> re-adds them.
    /// </summary>
    public static class MenuOverlayGuard
    {
        /// <summary>
        /// The "an arrow-capturing menu is active" flag, recomputed every frame by
        /// <c>MapNavigationPatch.UpdateSuppressionFlag()</c>. It is false during
        /// placement/cursor modes (they keep arrows live). This is the canonical signal.
        /// </summary>
        private static bool ArrowCapturingMenuActive => MapNavigationState.SuppressMapNavigation;

        /// <summary>
        /// Overlays that intentionally keep the map cursor (arrow keys) live, yet should
        /// still block the tile-info number row: the scanner search field and GoTo
        /// coordinate entry consume the digits themselves, gizmo navigation owns the
        /// number row, and the mod list / settlement browser run over a different surface.
        /// These are the entries the tile-info list historically had that the arrow list
        /// deliberately omits.
        /// </summary>
        private static bool NonArrowMapOverlayActive =>
            ScannerSearchState.IsActive ||
            GizmoNavigationState.IsActive ||
            GoToState.IsActive ||
            ModListState.IsActive ||
            SettlementBrowserState.IsActive ||
            FishingZoneMenuState.IsActive;

        /// <summary>
        /// Placement / cursor modes that keep the map cursor live. They are excluded from
        /// the base signal so tile-info still works during placement, but time-speed keys
        /// are still suppressed under them (see <see cref="BlockTimeSpeedKeys"/>).
        /// </summary>
        private static bool PlacementModeActive =>
            ArchitectState.IsActive ||
            ShapePlacementState.IsActive ||
            ViewingModeState.IsActive ||
            ZoneCreationState.IsInCreationMode;

        /// <summary>
        /// Full-screen tabs that own input but predate the arrow-suppression list. Kept
        /// here so time-speed keys stay blocked beneath them; flagged for a future
        /// arrow-list audit (their arrow handling is owned by their own input patches).
        /// </summary>
        private static bool FullScreenTabActive =>
            FactionTabState.IsActive ||
            IdeologyTabState.IsActive ||
            StorytellerSelectionState.IsActive;

        /// <summary>
        /// True when the map's tile-info hotkeys (1-7) must NOT fire because a menu owns
        /// input. Consumed by <c>DetailInfoPatch</c>.
        /// </summary>
        public static bool BlockMapInfoKeys =>
            ArrowCapturingMenuActive || NonArrowMapOverlayActive;

        /// <summary>
        /// True when the map's time-speed hotkeys (1/2/3 and Shift+1/2/3) must NOT fire.
        /// Adds placement modes and full-screen tabs on top of <see cref="BlockMapInfoKeys"/>.
        /// </summary>
        public static bool BlockTimeSpeedKeys =>
            BlockMapInfoKeys || PlacementModeActive || FullScreenTabActive;
    }
}
