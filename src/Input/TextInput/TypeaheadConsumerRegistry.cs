namespace RimWorldAccess
{
    /// <summary>
    /// Registers every typeahead-consuming State with the <see cref="TypeaheadDispatcher"/>
    /// at mod init. The priority -1.5 character handler in <see cref="UnifiedKeyboardPatch"/>
    /// reads the layout-aware <c>Event.current.character</c> and walks this registry to find
    /// the first active consumer. Works on any keyboard layout (Cyrillic, Greek, etc.).
    ///
    /// Priority numbers loosely match each State's existing handler priority — if multiple
    /// typeahead-consuming States are simultaneously active (rare, since most menus are
    /// modal), the lowest-priority registered consumer wins.
    /// </summary>
    public static class TypeaheadConsumerRegistry
    {
        private static bool registered;

        public static void RegisterAll()
        {
            if (registered) return;
            registered = true;

            // Modal text-edit States are handled by TextInputManager at priority -1.6.
            // Typeahead-only consumers register here. Priority numbers approximate the
            // existing UnifiedKeyboardPatch dispatch order.

            // -0.25: Info Card — a modal dialog (keyboard handler at priority -0.25 in
            // UnifiedKeyboardPatch). It opens OVER the inspection tree (4.806) and other
            // inspection-adjacent screens, which stay active beneath it, so its typeahead
            // must win over them; otherwise the tree below steals the keystroke. A float
            // menu it owns (-1.0) still wins, and HandleTypeahead defers to it.
            TypeaheadDispatcher.Register(-0.25, () => InfoCardState.IsActive, c => InfoCardState.HandleTypeahead(c));

            // -0.2: Scanner search + GoTo numeric input
            TypeaheadDispatcher.Register(-0.2, () => ScannerSearchState.IsActive, c => ScannerSearchState.HandleCharacter(c));
            TypeaheadDispatcher.Register(-0.2, () => GoToState.IsActive, c => GoToState.HandleCharacter(c));

            // 0.0-0.5: Modal browsers
            TypeaheadDispatcher.Register(0.0, () => SettlementBrowserState.IsActive, c => SettlementBrowserState.HandleTypeahead(c));

            // 1.0-3.0: Menus
            TypeaheadDispatcher.Register(1.5, () => SellableItemsState.IsActive, c => SellableItemsState.ProcessTypeaheadCharacter(c));
            // Save menu has two focus zones (text-name field + existing-saves list).
            // HandleCharacter routes to AppendChar or ProcessTypeaheadCharacter depending
            // on focus and respects modifier suppression so Alt/Ctrl shortcuts still pass
            // through to global handlers.
            TypeaheadDispatcher.Register(1.6, () => WindowlessSaveMenuState.IsActive, c => WindowlessSaveMenuState.HandleCharacter(c));
            TypeaheadDispatcher.Register(1.7, () => WindowlessOptionsMenuState.IsActive, c => WindowlessOptionsMenuState.ProcessTypeaheadCharacter(c));
            // Schedule menu: 1-9/0 pick the assignment brush, so digits are action keys, not search.
            TypeaheadDispatcher.Register(1.8, () => WindowlessScheduleState.IsActive, c => WindowlessScheduleState.HandleTypeahead(c), acceptsDigits: false);
            TypeaheadDispatcher.Register(1.9, () => WindowlessResearchDetailState.IsActive, c => WindowlessResearchDetailState.ProcessTypeaheadCharacter(c));
            TypeaheadDispatcher.Register(2.0, () => WindowlessResearchMenuState.IsActive, c => WindowlessResearchMenuState.ProcessTypeaheadCharacter(c));
            TypeaheadDispatcher.Register(2.1, () => QuestMenuState.IsActive, c => QuestMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(2.2, () => WildlifeMenuState.IsActive, c => WildlifeMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(2.3, () => AnimalsMenuState.IsActive, c => AnimalsMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(2.4, () => MechsMenuState.IsActive, c => MechsMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(2.5, () => NotificationMenuState.IsActive, c => NotificationMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(2.6, () => LearningHelperState.IsActive, c => LearningHelperState.HandleTypeahead(c));
            // Gated off while a policy editor is open so editor typeahead (apparel/food/reading
            // filters via ThingFilterNavigationState, drug list below) isn't stolen by the
            // still-active assign table beneath it.
            TypeaheadDispatcher.Register(2.7, () => AssignMenuState.IsActive && !PolicyEditorState.IsActive, c => AssignMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(2.8, () => StorageSettingsMenuState.IsActive, c => StorageSettingsMenuState.ProcessTypeaheadCharacter(c));
            TypeaheadDispatcher.Register(2.9, () => PlantSelectionMenuState.IsActive, c => PlantSelectionMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.0, () => ThingFilterNavigationState.IsActive, c => ThingFilterNavigationState.ProcessTypeaheadCharacter(c));
            TypeaheadDispatcher.Register(3.1, () => ThingFilterMenuState.IsActive, c => ThingFilterMenuState.ProcessTypeaheadCharacter(c));
            // Drug policy editor's drug list — typeahead only in DrugList mode (not while editing a
            // drug's settings). The AssignMenuState consumers above are gated off while any policy
            // editor is open, so this wins when a drug policy is being edited.
            TypeaheadDispatcher.Register(2.95, () => DrugPolicyEditorState.IsActive && DrugPolicyEditorState.CurrentMode == DrugPolicyEditorState.NavigationMode.DrugList, c => DrugPolicyEditorState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.2, () => RefuelableComponentState.IsActive, c => RefuelableComponentState.ProcessTypeaheadCharacter(c));
            // Work menu (focused view): 0-4 set work priority directly, so digits are action keys.
            TypeaheadDispatcher.Register(3.3, () => WorkMenuState.IsActive, c => WorkMenuState.ProcessSearchCharacter(c), acceptsDigits: false);

            // 4.0+: Float menus and floating overlays
            TypeaheadDispatcher.Register(4.0, () => WindowlessPauseMenuState.IsActive, c => WindowlessPauseMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.1, () => ExtraMenusState.IsActive, c => ExtraMenusState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.2, () => MechControlGroupState.IsActive, c => MechControlGroupState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.3, () => GrowthMomentState.IsActive, c => GrowthMomentState.HandleTypeahead(c));
            // A windowless float menu is always the topmost modal overlay — it opens OVER
            // whatever menu spawned it (e.g. the bills menu's "add recipe" picker), and that
            // parent menu stays active beneath it. So its typeahead must win over every other
            // consumer; otherwise the lower-numbered parent (BillsMenuState at 3.65, etc.)
            // steals the search and matches its own list instead of the float menu's options.
            // Priority is the most negative of any consumer for that reason.
            TypeaheadDispatcher.Register(-1.0, () => WindowlessFloatMenuState.IsActive, c => WindowlessFloatMenuState.HandleTypeahead(c));

            // Submenu typeahead variants — registered ahead of their main-menu counterparts
            // (lower priority number = higher precedence). When AnimalsMenuState is in
            // submenu mode, the submenu handler wins; otherwise the main handler runs.
            TypeaheadDispatcher.Register(2.25, () => AnimalsMenuState.IsActive && AnimalsMenuState.IsInSubmenu, c => AnimalsMenuState.SubmenuHandleTypeahead(c));
            TypeaheadDispatcher.Register(2.45, () => MechsMenuState.IsActive && MechsMenuState.IsInSubmenu, c => MechsMenuState.SubmenuHandleTypeahead(c));
            TypeaheadDispatcher.Register(2.65, () => AssignMenuState.IsActive && AssignMenuState.IsInSubmenu && !PolicyEditorState.IsActive, c => AssignMenuState.SubmenuHandleTypeahead(c));

            // WindowlessAreaState's HandleActionsTypeahead is bool-returning, wrap in Action.
            TypeaheadDispatcher.Register(2.85, () => WindowlessAreaState.IsActive, c => WindowlessAreaState.HandleActionsTypeahead(c));

            // History tab + auto-slaughter
            TypeaheadDispatcher.Register(3.5, () => HistoryStatisticsState.IsActive, c => HistoryStatisticsState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.5, () => HistoryMessagesState.IsActive, c => HistoryMessagesState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.5, () => AutoSlaughterState.IsActive, c => AutoSlaughterState.HandleTypeahead(c));

            // Architect tree typeahead — TreeNavigationHelper instance owned by ArchitectTreeState
            TypeaheadDispatcher.Register(0.5, () => ArchitectTreeState.IsActive, c => ArchitectTreeState.HandleTypeahead(c));

            // Colony inventory typeahead — TreeNavigationHelper instance owned by WindowlessInventoryState.
            // Priority matches the state's ~4.805 position in UnifiedKeyboardPatch.
            TypeaheadDispatcher.Register(4.805, () => WindowlessInventoryState.IsActive && !WindowlessFloatMenuState.IsActive, c => WindowlessInventoryState.HandleTypeahead(c));

            // Pawn/building inspection tree — TreeNavigationHelper instance owned by
            // WindowlessInspectionState. Higher number than the sub-menus it can drill into
            // (bills 3.65, storage 2.8, ...) so those win typeahead while open; the float menu
            // (-1.0) still wins over everything.
            TypeaheadDispatcher.Register(4.806, () => WindowlessInspectionState.IsActive && !WindowlessFloatMenuState.IsActive, c => WindowlessInspectionState.HandleTypeahead(c));

            // HealthTabState (Operations / Health Settings / surgery recipe + body-part pickers)
            // opens FROM the inspection screen and inspection STAYS active beneath it. Registered
            // here at a lower number than inspection (4.806) and inventory (4.805) so its own
            // typeahead wins; otherwise the still-active inspection consumer steals the keystroke
            // (typing 'a' jumped to the pawn's Animals skill). Float menus it spawns (medical-care
            // / food-policy choice) still win at -1.0. Replaces the old inline character branch in
            // UnifiedKeyboardPatch, which TryDispatchChar shadowed and never reached.
            TypeaheadDispatcher.Register(4.78, () => HealthTabState.IsActive, c => HealthTabState.HandleCharacterInput(c));

            // Migrated typeahead consumers (formerly inline RequestCharacter sites)
            // The quantity menu (numeric entry) and an inspection overlay can open ON TOP of
            // these transfer dialogs while they stay active. The quantity menu reads digits via
            // its own KeyCode handler, so the dispatcher must NOT route those digits to the
            // dialog's list typeahead beneath it; likewise an inspection overlay (higher number,
            // so it would otherwise lose) must own the search. Gates mirror the KeyCode-channel
            // gates in UnifiedKeyboardPatch (lines ~829/844/884).
            TypeaheadDispatcher.Register(3.6, () => TransportPodLoadingState.IsActive && !QuantityMenuState.IsActive && !WindowlessInspectionState.IsActive, c => TransportPodLoadingState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.7, () => GearEquipMenuState.IsActive, c => GearEquipMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.8, () => CaravanFormationState.IsActive && !QuantityMenuState.IsActive && !WindowlessInspectionState.IsActive, c => CaravanFormationState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.9, () => SplitCaravanState.IsActive && !QuantityMenuState.IsActive && !WindowlessInspectionState.IsActive, c => SplitCaravanState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.4, () => IdeologyTabState.IsActive, c => IdeologyTabState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.5, () => GizmoNavigationState.IsActive, c => GizmoNavigationState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.6, () => PawnAreaMenuState.IsActive, c => PawnAreaMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.7, () => ModListState.IsActive, c => ModListState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.8, () => AreaSelectionMenuState.IsActive, c => AreaSelectionMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.85, () => ShapeSelectionMenuState.IsActive, c => ShapeSelectionMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.9, () => MenuNavigationState.IsActive, c => MenuNavigationState.HandleTypeahead(c));
            // BillConfigState (editing one bill) opens ON TOP of BillsMenuState (the bill list)
            // and the list stays active beneath it. Gate the list off so typing while editing a
            // bill searches the editor, not the list ("Add bill..." was stealing the 'a' key).
            TypeaheadDispatcher.Register(3.65, () => BillsMenuState.IsActive && !BillConfigState.IsActive, c => BillsMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.66, () => BillConfigState.IsActive, c => BillConfigState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.67, () => FishingZoneMenuState.IsActive, c => FishingZoneMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.68, () => StorytellerSelectionState.IsActive, c => StorytellerSelectionState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.70, () => TradeNavigationState.IsActive, c => TradeNavigationState.HandleTypeahead(c));

            // States added on ideoligion after c5a7e12 — wired into the dispatcher here.
            // RitualState was deleted; its functionality split between LordJobDialogState
            // (Lord-job dialogs: rituals, psychic rituals, gravship launch) and
            // DryadCasteState (gauranlen tree caste selection).
            TypeaheadDispatcher.Register(0.33, () => LordJobDialogState.IsActive, c => LordJobDialogState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(0.34, () => DryadCasteState.IsActive, c => DryadCasteState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.62, () => EntityTabState.IsActive, c => EntityTabState.HandleTypeahead(c));
            // Prisoner/Slave tab opens OVER the inspection tree (which stays active at 4.806). Register
            // lower so its cross-section typeahead wins; otherwise the tree beneath steals the search.
            TypeaheadDispatcher.Register(4.61, () => PrisonerTabState.IsActive, c => PrisonerTabState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.63, () => AnomalySettingsDialogState.IsActive, c => AnomalySettingsDialogState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.64, () => EntityCodexState.IsActive, c => EntityCodexState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.66, () => StylingStationState.IsActive, c => StylingStationState.HandleTypeahead(c));
            // bool-returning HandleTypeahead methods — wrapped in a void lambda.
            // Work menu (table view): 0-4 set work priority directly, so digits are action keys.
            TypeaheadDispatcher.Register(3.31, () => WorkTableState.IsActive, c => WorkTableState.HandleTypeahead(c), acceptsDigits: false);
            TypeaheadDispatcher.Register(3.32, () => PawnSkillsTableState.IsActive, c => PawnSkillsTableState.HandleTypeahead(c));
        }
    }
}
