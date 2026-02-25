using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static class KeyboardHelper
    {
        /// <summary>
        /// True if the last RemapCharacterToKeyCode call remapped a character event to a KeyCode.
        /// When true, ctrl/alt modifier flags may be artifacts of AltGr and should be ignored.
        /// </summary>
        public static bool WasCharacterRemapped { get; private set; }

        // Tracks the frame when a real KeyCode.RightBracket was seen, so we don't
        // also remap the follow-up character event that Unity sends for the same keypress.
        private static int lastRightBracketFrame = -1;

        // Same frame tracking for KeyCode.KeypadMultiply (asterisk).
        // On US keyboards, numpad * sends keyCode=KeypadMultiply then character='*' in the same frame.
        // On AZERTY keyboards, the dedicated * key sends only character='*' with keyCode=None.
        private static int lastKeypadMultiplyFrame = -1;

        /// <summary>
        /// Remaps character-only KeyDown events to their equivalent KeyCode.
        /// On non-US keyboards (e.g., German), layout-dependent characters like ] are produced
        /// via AltGr combinations, which Unity reports as keyCode=None with the character set.
        /// On US keyboards, Unity already sends keyCode=RightBracket followed by a separate
        /// character=']' event in the same frame; frame tracking prevents double-processing.
        /// Call after getting Event.current.keyCode, before any KeyCode.None early-return guard.
        /// </summary>
        public static KeyCode RemapCharacterToKeyCode(KeyCode key)
        {
            WasCharacterRemapped = false;

            // If we see a real RightBracket keyCode (US layout), record the frame
            if (key == KeyCode.RightBracket)
            {
                lastRightBracketFrame = Time.frameCount;
                return key;
            }

            // If we see a real KeypadMultiply keyCode (numpad *), record the frame
            if (key == KeyCode.KeypadMultiply)
            {
                lastKeypadMultiplyFrame = Time.frameCount;
                return key;
            }

            // If we see Shift+Alpha8 (US keyboard main-row *), record the frame
            // just like numpad * so the follow-up character='*' event won't be remapped.
            if (key == KeyCode.Alpha8 && Event.current.shift)
            {
                lastKeypadMultiplyFrame = Time.frameCount;
                return key;
            }

            if (key != KeyCode.None)
                return key;

            switch (Event.current.character)
            {
                case ']':
                    // Only remap if we didn't already see a real RightBracket keyCode this frame.
                    // On US keyboards, both events fire in the same frame — skip the character one.
                    // On German keyboards, the keyCode event was Alpha9, not RightBracket, so
                    // lastRightBracketFrame won't match and we correctly remap.
                    if (Time.frameCount == lastRightBracketFrame)
                        return key;
                    WasCharacterRemapped = true;
                    return KeyCode.RightBracket;
                case '*':
                    // On AZERTY keyboards, * is a dedicated key that sends keyCode=None + character='*'.
                    // On US keyboards, Shift+8 sends keyCode=Alpha8 (not KeypadMultiply), so no frame
                    // conflict. Numpad * sends KeypadMultiply then character='*' — frame tracking
                    // prevents double-processing.
                    if (Time.frameCount == lastKeypadMultiplyFrame)
                        return key;
                    WasCharacterRemapped = true;
                    return KeyCode.KeypadMultiply;
                default:
                    return key;
            }
        }

        /// <summary>
        /// Applies RemapCharacterToKeyCode globally by writing back to Event.current.keyCode.
        /// This ensures all downstream patches and handlers see the remapped keyCode without
        /// needing to call RemapCharacterToKeyCode individually.
        /// </summary>
        public static void ApplyGlobalRemap()
        {
            if (Event.current.type != EventType.KeyDown)
                return;

            var original = Event.current.keyCode;
            var remapped = RemapCharacterToKeyCode(original);
            if (remapped != original)
                Event.current.keyCode = remapped;
        }

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
                // Policy editor
                || PolicyEditorState.IsActive
                // Main gameplay menus
                // Note: SettlementBrowserState and QuestLocationsBrowserState are
                // intentionally NOT included here - they're world-view-specific and handle their own
                // input via UnifiedKeyboardPatch priorities
                || CaravanInspectState.IsActive
                || (CaravanFormationState.IsActive && !CaravanFormationState.IsChoosingDestination)
                || RitualState.IsActive
                || QuestMenuState.IsActive
                || NotificationMenuState.IsActive
                || AssignMenuState.IsActive
                || WorkMenuState.IsActive
                || StorageSettingsMenuState.IsActive
                || ZoneRenameState.IsActive
                || StorageRenameState.IsActive
                || PlantSelectionMenuState.IsActive
                || MechControlGroupState.IsActive
                || GizmoNavigationState.IsActive
                || TradeNavigationState.IsActive
                || SellableItemsState.IsActive
                // Bills and building menus
                || BillsMenuState.IsActive
                || BillConfigState.IsActive
                || FishingZoneMenuState.IsActive
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
                || BuildingOwnerAssignmentState.IsActive
                // Pawn inspection tabs
                || HealthTabState.IsActive
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
                || AreaPaintingState.IsActive
                // Split caravan and related menus
                || SplitCaravanState.IsActive
                || GearEquipMenuState.IsActive
                || QuantityMenuState.IsActive
                || AreaSelectionMenuState.IsActive
                || PawnAreaMenuState.IsActive
                // History tab
                || HistoryState.IsActive
                || HistoryStatisticsState.IsActive
                || HistoryMessagesState.IsActive
                // Building placement modes
                || ViewingModeState.IsActive
                || ShapePlacementState.IsActive
                // Info Card (modal dialog overlay)
                || InfoCardState.IsActive
                // Biotech modal dialogs
                || GrowthMomentState.IsActive
                // Factions tab
                || FactionTabState.IsActive
                // Extra menus
                || ExtraMenusState.IsActive;
        }
    }

    /// <summary>
    /// Highest-priority UIRootOnGUI prefix that remaps keyboard layout-dependent characters
    /// (e.g., AZERTY *) to their canonical KeyCodes before any other patch reads Event.current.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class KeyRemapPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First + 100)]
        public static void Prefix()
        {
            KeyboardHelper.ApplyGlobalRemap();
        }
    }
}
