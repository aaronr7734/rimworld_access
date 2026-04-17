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

            // -0.2: Scanner search + GoTo numeric input
            TypeaheadDispatcher.Register(-0.2, () => ScannerSearchState.IsActive, c => ScannerSearchState.HandleCharacter(c));
            TypeaheadDispatcher.Register(-0.2, () => GoToState.IsActive, c => GoToState.HandleCharacter(c));

            // 0.0-0.5: Modal browsers
            TypeaheadDispatcher.Register(0.0, () => SettlementBrowserState.IsActive, c => SettlementBrowserState.HandleTypeahead(c));

            // 1.0-3.0: Menus
            TypeaheadDispatcher.Register(1.5, () => SellableItemsState.IsActive, c => SellableItemsState.ProcessTypeaheadCharacter(c));
            TypeaheadDispatcher.Register(1.6, () => WindowlessSaveMenuState.IsActive, c => WindowlessSaveMenuState.ProcessTypeaheadCharacter(c));
            TypeaheadDispatcher.Register(1.7, () => WindowlessOptionsMenuState.IsActive, c => WindowlessOptionsMenuState.ProcessTypeaheadCharacter(c));
            TypeaheadDispatcher.Register(1.8, () => WindowlessScheduleState.IsActive, c => WindowlessScheduleState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(1.9, () => WindowlessResearchDetailState.IsActive, c => WindowlessResearchDetailState.ProcessTypeaheadCharacter(c));
            TypeaheadDispatcher.Register(2.0, () => WindowlessResearchMenuState.IsActive, c => WindowlessResearchMenuState.ProcessTypeaheadCharacter(c));
            TypeaheadDispatcher.Register(2.1, () => QuestMenuState.IsActive, c => QuestMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(2.2, () => WildlifeMenuState.IsActive, c => WildlifeMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(2.3, () => AnimalsMenuState.IsActive, c => AnimalsMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(2.4, () => MechsMenuState.IsActive, c => MechsMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(2.5, () => NotificationMenuState.IsActive, c => NotificationMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(2.6, () => LearningHelperState.IsActive, c => LearningHelperState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(2.7, () => AssignMenuState.IsActive, c => AssignMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(2.8, () => StorageSettingsMenuState.IsActive, c => StorageSettingsMenuState.ProcessTypeaheadCharacter(c));
            TypeaheadDispatcher.Register(2.9, () => PlantSelectionMenuState.IsActive, c => PlantSelectionMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.0, () => ThingFilterNavigationState.IsActive, c => ThingFilterNavigationState.ProcessTypeaheadCharacter(c));
            TypeaheadDispatcher.Register(3.1, () => ThingFilterMenuState.IsActive, c => ThingFilterMenuState.ProcessTypeaheadCharacter(c));
            TypeaheadDispatcher.Register(3.2, () => RefuelableComponentState.IsActive, c => RefuelableComponentState.ProcessTypeaheadCharacter(c));
            TypeaheadDispatcher.Register(3.3, () => WorkMenuState.IsActive, c => WorkMenuState.ProcessSearchCharacter(c));

            // 4.0+: Float menus and floating overlays
            TypeaheadDispatcher.Register(4.0, () => WindowlessPauseMenuState.IsActive, c => WindowlessPauseMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.1, () => ExtraMenusState.IsActive, c => ExtraMenusState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.2, () => MechControlGroupState.IsActive, c => MechControlGroupState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.3, () => GrowthMomentState.IsActive, c => GrowthMomentState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(5.0, () => WindowlessFloatMenuState.IsActive, c => WindowlessFloatMenuState.HandleTypeahead(c));

            // Submenu typeahead variants — registered ahead of their main-menu counterparts
            // (lower priority number = higher precedence). When AnimalsMenuState is in
            // submenu mode, the submenu handler wins; otherwise the main handler runs.
            TypeaheadDispatcher.Register(2.25, () => AnimalsMenuState.IsActive && AnimalsMenuState.IsInSubmenu, c => AnimalsMenuState.SubmenuHandleTypeahead(c));
            TypeaheadDispatcher.Register(2.45, () => MechsMenuState.IsActive && MechsMenuState.IsInSubmenu, c => MechsMenuState.SubmenuHandleTypeahead(c));
            TypeaheadDispatcher.Register(2.65, () => AssignMenuState.IsActive && AssignMenuState.IsInSubmenu, c => AssignMenuState.SubmenuHandleTypeahead(c));

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

            // Migrated typeahead consumers (formerly inline RequestCharacter sites)
            TypeaheadDispatcher.Register(3.6, () => TransportPodLoadingState.IsActive, c => TransportPodLoadingState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.7, () => GearEquipMenuState.IsActive, c => GearEquipMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.8, () => CaravanFormationState.IsActive, c => CaravanFormationState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.9, () => SplitCaravanState.IsActive, c => SplitCaravanState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.4, () => IdeologyTabState.IsActive, c => IdeologyTabState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.5, () => GizmoNavigationState.IsActive, c => GizmoNavigationState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.6, () => PawnAreaMenuState.IsActive, c => PawnAreaMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.7, () => ModListState.IsActive, c => ModListState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.8, () => AreaSelectionMenuState.IsActive, c => AreaSelectionMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.85, () => ShapeSelectionMenuState.IsActive, c => ShapeSelectionMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(4.9, () => MenuNavigationState.IsActive, c => MenuNavigationState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.65, () => BillsMenuState.IsActive, c => BillsMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.66, () => BillConfigState.IsActive, c => BillConfigState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.67, () => FishingZoneMenuState.IsActive, c => FishingZoneMenuState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.68, () => StorytellerSelectionState.IsActive, c => StorytellerSelectionState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.69, () => RitualState.IsActive, c => RitualState.HandleTypeahead(c));
            TypeaheadDispatcher.Register(3.70, () => TradeNavigationState.IsActive, c => TradeNavigationState.HandleTypeahead(c));
        }
    }
}
