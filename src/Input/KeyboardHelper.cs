using System;
using Verse;

namespace RimWorldAccess
{
    public static class KeyboardHelper
    {
        /// <summary>
        /// Returns true if ANY modal accessibility menu is currently active.
        /// When true, ALL keyboard input should go to that menu, not the game.
        /// </summary>
        public static bool IsAnyAccessibilityMenuActive()
        {
            // Safety: Don't run during loading - only when game is actually playing or in main menu
            // Entry = main menu, Playing = in-game, MapInitializing = loading a map
            if (Current.ProgramState != ProgramState.Playing && Current.ProgramState != ProgramState.Entry)
                return false;

            // NOTE: MapNavigationState.IsInitialized is NOT included here - it's too broad.
            // Map navigation is always "active" when on the map, but we don't want to block
            // ALL keyboard input. Instead, MapNavigationPatch handles arrow keys directly
            // and consumes them there.

            // Windowless menus (main accessibility menus)
            return WindowlessFloatMenuState.IsActive
                || WindowlessInventoryState.IsActive
                || WindowlessInspectionState.IsActive
                || WindowlessSaveMenuState.IsActive
                || WindowlessOptionsMenuState.IsActive
                || WindowlessPauseMenuState.IsActive
                || WindowlessResearchMenuState.IsActive
                || WindowlessResearchDetailState.IsActive
                || WindowlessScheduleState.IsActive
                || WindowlessDialogState.IsActive
                || WindowlessConfirmationState.IsActive
                || WindowlessAreaState.IsActive
                // Policy menus
                || WindowlessOutfitPolicyState.IsActive
                || WindowlessFoodPolicyState.IsActive
                || WindowlessDrugPolicyState.IsActive
                // Main gameplay menus
                // Note: SettlementBrowserState, CaravanStatsState, and QuestLocationsBrowserState are
                // intentionally NOT included here - they're world-view-specific and handle their own
                // input in WorldNavigationPatch (which runs after this check)
                || (CaravanFormationState.IsActive && !CaravanFormationState.IsChoosingDestination)
                || QuestMenuState.IsActive
                || NotificationMenuState.IsActive
                || AssignMenuState.IsActive
                || WorkMenuState.IsActive
                || StorageSettingsMenuState.IsActive
                || ZoneSettingsMenuState.IsActive
                || ZoneRenameState.IsActive
                || PlantSelectionMenuState.IsActive
                || GizmoNavigationState.IsActive
                || TradeNavigationState.IsActive
                || TradeConfirmationState.IsActive
                // Bills and building menus
                || BillsMenuState.IsActive
                || BillConfigState.IsActive
                || BuildingInspectState.IsActive
                || RangeEditMenuState.IsActive
                || TempControlMenuState.IsActive
                // Building component controls
                || ForbidControlState.IsActive
                || FlickableComponentState.IsActive
                || BreakdownableComponentState.IsActive
                || DoorControlState.IsActive
                || RefuelableComponentState.IsActive
                || UninstallControlState.IsActive
                || BedAssignmentState.IsActive
                // Pawn inspection tabs
                || CharacterTabState.IsActive
                || HealthTabState.IsActive
                || NeedsTabState.IsActive
                || SocialTabState.IsActive
                || TrainingTabState.IsActive
                || PrisonerTabState.IsActive
                // Filter navigation
                || ThingFilterMenuState.IsActive
                || ThingFilterNavigationState.IsActive
                // Other menus
                || ArchitectState.IsActive
                || ArchitectTreeState.IsActive
                || AnimalsMenuState.IsActive
                || WildlifeMenuState.IsActive
                || ModListState.IsActive
                || StorytellerSelectionState.IsActive
                || PlaySettingsMenuState.IsActive
                || AreaPaintingState.IsActive;
        }
    }
}
