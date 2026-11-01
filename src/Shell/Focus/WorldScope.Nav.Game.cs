using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The in-game world view's ambient claims: the Tab/Shift+Tab planet-layer cycle, F8 dismiss,
    /// the key-blackout consume set, and the C / ']' side-door handoffs.
    /// <see cref="WorldScope"/> stays non-modal.
    ///
    /// <b>Positional substitutes.</b> <see cref="WorldNavigationPatch"/> prefixes
    /// <c>WorldInterface.HandleLowPriorityInput</c>, which vanilla calls AFTER
    /// <c>UIRoot.UIRootOnGUI</c> returns, so its branches only ever saw keys nothing else had
    /// consumed — a "runs last" position, not a precedence rule. An ambient claim dispatches at
    /// shell priority instead, so each gate below carries explicit terms standing in for that
    /// position.
    ///
    /// The cursor arrows and the tile-info digits are registered by <see cref="WorldMapElement"/>,
    /// shared with the starting-site screen; their gates (<see cref="ArrowsLive"/>,
    /// <see cref="TileInfoDigitsLive"/>) stay here and reach the element through its profile.
    /// </summary>
    public sealed partial class WorldScope
    {
        private void RegisterNavClaims()
        {
            // Cursor arrows, tile-info digits and the scanner browse/jump cluster, each behind
            // this scope's own gate passed through the profile.
            map.RegisterClaims(this);

            // One action, two chords, one handler: CyclePlanetLayer has no reverse direction, so
            // Shift+Tab cycles forward exactly as bare Tab does.
            Claim("world.layer.cycle", delegate (KeyEventSnapshot e) { WorldNavigationState.CyclePlanetLayer(); }, when: TabCycleLive);

            Claim("world.dismiss", OnDismissWorld, when: DismissLive);

            // Blackout consume-set: a silent no-op consume, load-bearing against map rungs whose
            // own gates pass on the world view because Find.CurrentMap stays non-null while the
            // planet renders (the P prisoner tab, uppercase I colony inventory, and the
            // Enter-opens-windowless-inspection rung).
            //
            // This claim uses BlockedKeysLive, not the BlackoutLive the Tab claim keeps: the
            // scanner-search keycode block, the object-selection tree and the settlement browser
            // all consume plain letters as search input and are non-members of
            // IsAnyAccessibilityMenuActive, so without their terms this claim would swallow
            // A/Q/P/S/L out of a live search or typeahead. Tab needs only the
            // WorldObjectSelectionState term, because TreeNavigationHelper.HandleInput ends in
            // `return key != KeyCode.None;` — a consume-all tail — while the search block and the
            // browser never handled Tab at all.
            //
            // DEVIATION: Return/KeypadEnter are DELIBERATELY OMITTED from the blocked-key list. A
            // consume-only Enter claim at shell priority would swallow Enter before the
            // Enter-opens-world-object-selection path runs, breaking selection outright.
            Claim("world.blackout.blockKey", delegate (KeyEventSnapshot e) { }, when: BlockedKeysLive);

            // C forms a caravan at the selected settlement, ']' gives orders to the selected
            // caravan. The side door's CaravanFormationState precondition is dropped:
            // Dialog_FormCaravan carries its own MODAL CaravanFormationScope, whose structural
            // masking already stops these claims while the dialog owns input.
            //
            // The gates add !WorldObjectSelectionState.IsActive and !StatBreakdownState.IsActive:
            // both are TreeNavigationHelper-backed with the consume-all tail, so their typeahead
            // would otherwise lose plain letters to these claims. Bare C additionally takes
            // !ScannerSearchState.IsActive, since the search keycode block consumes letters; ']'
            // is not a letter and needs no such term.
            Claim("world.formCaravan", delegate (KeyEventSnapshot e) { WorldNavigationState.FormCaravanAtSelectedSettlement(); }, when: FormCaravanLive);
            Claim("world.caravanOrders", delegate (KeyEventSnapshot e) { WorldNavigationState.GiveCaravanOrders(); }, when: WorldSideDoorLive);

            Claim("map.cursor.pullFromPointer", delegate (KeyEventSnapshot e) { PointerWarpState.PullCursorToPointer(); }, when: ArrowsLive);
            Claim("map.cursor.toggleMouseTracking", delegate (KeyEventSnapshot e) { PointerWarpState.ToggleMouseTracking(); }, when: ArrowsLive);
        }

        /// <summary>The cursor arrows' gate.</summary>
        private static bool ArrowsLive()
        {
            return !ShellGuards.MenuOwnsInput()
                && !WorldObjectSelectionState.IsActive;
        }

        /// <summary>
        /// The outer gate shared by the Tab layer-cycle claim and the blackout consume claim.
        /// </summary>
        private static bool BlackoutLive()
        {
            return WorldNavigationState.IsActive
                && WorldNavigationState.Context == WorldNavContext.InGame
                && !CaravanFormationState.IsActive
                && !SplitCaravanState.IsActive
                && !GearEquipMenuState.IsActive
                && !QuantityMenuState.IsActive
                && !QuestMenuState.IsActive
                && !CaravanInspectState.IsActive
                && !ShellGuards.MenuOwnsInput();
        }

        /// <summary>
        /// The Tab layer-cycle gate: BlackoutLive plus the object-selection-tree term, whose
        /// consume-all tail also claims Tab.
        /// </summary>
        private static bool TabCycleLive()
        {
            return BlackoutLive()
                && !WorldObjectSelectionState.IsActive;
        }

        /// <summary>
        /// The blocked-key gate: BlackoutLive plus the non-member overlay terms and the
        /// StatBreakdownState pairing. Tab uses the narrower TabCycleLive.
        /// </summary>
        private static bool BlockedKeysLive()
        {
            return BlackoutLive()
                && !StatBreakdownState.IsActive
                && !ScannerSearchState.IsActive
                && !WorldObjectSelectionState.IsActive;
        }

        /// <summary>
        /// The F8 dismiss gate, including the object-selection-tree term for the same
        /// consume-all-tail reason TabCycleLive carries it.
        /// </summary>
        private static bool DismissLive()
        {
            return WorldNavigationState.IsActive
                && WorldNavigationState.Context == WorldNavContext.InGame
                && !CaravanFormationState.IsActive
                && !SplitCaravanState.IsActive
                && !ShellGuards.MenuOwnsInput()
                && !WorldObjectSelectionState.IsActive;
        }

        private static void OnDismissWorld(KeyEventSnapshot e)
        {
            CameraJumper.TryHideWorld();
            MapNavigationState.RestoreCursorForCurrentMap();
            TolkHelper.Speak("RimWorldAccess.Input.Close.ReturnedToMap".Loc());
        }

        /// <summary>
        /// The tile-info digits' gate, with the positional-substitute terms.
        /// </summary>
        private static bool TileInfoDigitsLive()
        {
            return WorldNavigationState.IsActive
                && Current.ProgramState == ProgramState.Playing
                && !ShellGuards.MenuOwnsInput()
                && !StatBreakdownState.IsActive
                && !ScannerSearchState.IsActive
                && !WorldObjectSelectionState.IsActive;
        }

        /// <summary>
        /// True when the world surface is active and readable — the same conditions under which
        /// the tile-info digits describe a tile. Exposed for the drag-box reader, which watches the
        /// surface without going through a claim.
        /// </summary>
        internal static bool WorldSurfaceLive()
        {
            return TileInfoDigitsLive();
        }

        /// <summary>
        /// The shared C / ']' preconditions plus the two TreeNavigationHelper-backed
        /// consume-all terms.
        /// </summary>
        private static bool WorldSideDoorLive()
        {
            return !ShellGuards.MenuOwnsInput()
                && !WorldObjectSelectionState.IsActive
                && !StatBreakdownState.IsActive;
        }

        /// <summary>
        /// The C claim's gate: WorldSideDoorLive plus the search keycode block, the remaining
        /// bare-letter consumer. ']' is not a letter and takes WorldSideDoorLive alone.
        /// </summary>
        private static bool FormCaravanLive()
        {
            return WorldSideDoorLive()
                && !ScannerSearchState.IsActive;
        }
    }
}
