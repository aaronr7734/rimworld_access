using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;
using System.Collections.Generic;
using System.Linq;

namespace RimWorldAccess
{
    /// <summary>
    /// Unified Harmony patch for UIRoot.UIRootOnGUI to handle all keyboard accessibility features.
    /// Handles: Escape key for pause menu, Enter key for building inspection, ] key for colonist orders, J key for jump menu, Alt+M for mood info, S for schedule, and all windowless menu navigation.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class UnifiedKeyboardPatch
    {
        /// <summary>
        /// Prefix patch that intercepts keyboard input for all accessibility features.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix()
        {
            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            KeyCode key = Event.current.keyCode;

            // Skip if no actual key (Unity IMGUI quirk)
            if (key == KeyCode.None)
                return;

            // Log if area painting is active
            if (AreaPaintingState.IsActive)
            {
                MelonLoader.MelonLogger.Msg($"RimWorld Access: UnifiedKeyboardPatch - Area painting is ACTIVE, key={key}");
            }

            // ===== PRIORITY 1: Handle delete confirmation if active =====
            if (WindowlessDeleteConfirmationState.IsActive)
            {
                if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessDeleteConfirmationState.Confirm();
                    Event.current.Use();
                    return;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessDeleteConfirmationState.Cancel();
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 2: Handle general confirmation if active =====
            if (WindowlessConfirmationState.IsActive)
            {
                if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessConfirmationState.Confirm();
                    Event.current.Use();
                    return;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessConfirmationState.Cancel();
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 2.5: Handle area painting mode if active =====
            if (AreaPaintingState.IsActive)
            {
                MelonLoader.MelonLogger.Msg($"RimWorld Access: Area painting active, handling key {key}");
                bool handled = false;

                if (key == KeyCode.Space)
                {
                    MelonLoader.MelonLogger.Msg("RimWorld Access: Space pressed in area painting mode");
                    AreaPaintingState.ToggleStageCell();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    MelonLoader.MelonLogger.Msg("RimWorld Access: Enter pressed in area painting mode");
                    AreaPaintingState.Confirm();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    MelonLoader.MelonLogger.Msg("RimWorld Access: Escape pressed in area painting mode");
                    AreaPaintingState.Cancel();
                    handled = true;
                }
                // Note: Arrow keys are NOT handled here - they pass through to MapNavigationPatch
                // The ZoneCreationAnnouncementPatch postfix will add "Selected" prefix if needed

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 3: Handle save/load menu if active =====
            if (WindowlessSaveMenuState.IsActive)
            {
                Log.Message($"RimWorld Access: Save menu active, handling key {key}");
                bool handled = false;

                if (key == KeyCode.DownArrow)
                {
                    WindowlessSaveMenuState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    WindowlessSaveMenuState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessSaveMenuState.ExecuteSelected();
                    handled = true;
                }
                else if (key == KeyCode.Delete)
                {
                    WindowlessSaveMenuState.DeleteSelected();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessSaveMenuState.GoBack();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4: Handle pause menu if active =====
            if (WindowlessPauseMenuState.IsActive)
            {
                Log.Message($"RimWorld Access: Pause menu active, handling key {key}");
                bool handled = false;

                if (key == KeyCode.DownArrow)
                {
                    Log.Message("RimWorld Access: Down arrow in pause menu");
                    WindowlessPauseMenuState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    Log.Message("RimWorld Access: Up arrow in pause menu");
                    WindowlessPauseMenuState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    Log.Message("RimWorld Access: Enter in pause menu");
                    WindowlessPauseMenuState.ExecuteSelected();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    Log.Message("RimWorld Access: Escape in pause menu - closing");
                    WindowlessPauseMenuState.Close();
                    ClipboardHelper.CopyToClipboard("Menu closed");
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    Log.Message("RimWorld Access: Event consumed");
                    return;
                }
            }

            // ===== PRIORITY 4.5: Handle storyteller selection (in-game) if active =====
            if (StorytellerSelectionState.IsActive)
            {
                bool handled = false;

                if (key == KeyCode.DownArrow)
                {
                    StorytellerSelectionState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    StorytellerSelectionState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.Tab)
                {
                    StorytellerSelectionState.SwitchLevel();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    StorytellerSelectionState.Confirm();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    StorytellerSelectionState.Close();
                    Find.WindowStack.TryRemove(typeof(Page_SelectStorytellerInGame));
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.6: Handle options menu if active =====
            if (WindowlessOptionsMenuState.IsActive)
            {
                bool handled = false;

                if (key == KeyCode.DownArrow)
                {
                    WindowlessOptionsMenuState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    WindowlessOptionsMenuState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.LeftArrow)
                {
                    WindowlessOptionsMenuState.AdjustSetting(-1);  // Decrease slider or cycle left
                    handled = true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    WindowlessOptionsMenuState.AdjustSetting(1);   // Increase slider or cycle right
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessOptionsMenuState.ExecuteSelected();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessOptionsMenuState.GoBack();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // Note: ThingFilterMenuState, BillConfigState, BillsMenuState, and BuildingInspectState
            // are all handled by BuildingInspectPatch with VeryHigh priority.
            // We don't need to check for them here because BuildingInspectPatch will consume
            // the events before they reach this patch. However, we DO need to continue processing
            // to handle WindowlessFloatMenuState which can be active at the same time as BillsMenuState.

            // ===== PRIORITY 4.55: Handle schedule menu if active =====
            if (WindowlessScheduleState.IsActive)
            {
                bool handled = false;
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;

                if (key == KeyCode.UpArrow)
                {
                    WindowlessScheduleState.MoveUp();
                    handled = true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    WindowlessScheduleState.MoveDown();
                    handled = true;
                }
                else if (key == KeyCode.LeftArrow)
                {
                    WindowlessScheduleState.MoveLeft();
                    handled = true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    if (shift)
                    {
                        WindowlessScheduleState.FillRow();
                    }
                    else
                    {
                        WindowlessScheduleState.MoveRight();
                    }
                    handled = true;
                }
                else if (key == KeyCode.Tab)
                {
                    WindowlessScheduleState.CycleAssignment();
                    handled = true;
                }
                else if (key == KeyCode.Space)
                {
                    WindowlessScheduleState.ApplyAssignment();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    // Confirm and close
                    WindowlessScheduleState.Confirm();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    // Cancel and close
                    WindowlessScheduleState.Cancel();
                    handled = true;
                }
                else if (ctrl && key == KeyCode.C)
                {
                    WindowlessScheduleState.CopySchedule();
                    handled = true;
                }
                else if (ctrl && key == KeyCode.V)
                {
                    WindowlessScheduleState.PasteSchedule();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.6: Handle research detail view if active =====
            if (WindowlessResearchDetailState.IsActive)
            {
                bool handled = false;

                if (key == KeyCode.DownArrow)
                {
                    WindowlessResearchDetailState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    WindowlessResearchDetailState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessResearchDetailState.ExecuteCurrentSection();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessResearchDetailState.Close();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.7: Handle research menu if active =====
            if (WindowlessResearchMenuState.IsActive)
            {
                bool handled = false;

                if (key == KeyCode.DownArrow)
                {
                    WindowlessResearchMenuState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    WindowlessResearchMenuState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    WindowlessResearchMenuState.ExpandCategory();
                    handled = true;
                }
                else if (key == KeyCode.LeftArrow)
                {
                    WindowlessResearchMenuState.CollapseCategory();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessResearchMenuState.ExecuteSelected();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessResearchMenuState.Close();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.75: Handle jump menu if active =====
            if (JumpMenuState.IsActive)
            {
                bool handled = false;

                if (key == KeyCode.DownArrow)
                {
                    JumpMenuState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    JumpMenuState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    JumpMenuState.ExpandCategory();
                    handled = true;
                }
                else if (key == KeyCode.LeftArrow)
                {
                    JumpMenuState.CollapseCategory();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    JumpMenuState.JumpToSelected();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    JumpMenuState.Close();
                    ClipboardHelper.CopyToClipboard("Jump menu closed");
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 5: Handle order float menu if active =====
            if (WindowlessFloatMenuState.IsActive)
            {
                bool handled = false;

                if (key == KeyCode.DownArrow)
                {
                    WindowlessFloatMenuState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    WindowlessFloatMenuState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessFloatMenuState.ExecuteSelected();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessFloatMenuState.Close();
                    ClipboardHelper.CopyToClipboard("Menu closed");
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 5.5: Handle time control with Shift+1/2/3, intercept 1/2/3 without Shift =====
            if ((key == KeyCode.Alpha1 || key == KeyCode.Keypad1 ||
                 key == KeyCode.Alpha2 || key == KeyCode.Keypad2 ||
                 key == KeyCode.Alpha3 || key == KeyCode.Keypad3) &&
                Current.ProgramState == ProgramState.Playing &&
                Find.CurrentMap != null &&
                (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion))
            {
                // Don't intercept if any menu is active (keys 1-5 are used for tile info)
                bool anyMenuActive = WorkMenuState.IsActive ||
                                    ArchitectState.IsActive ||
                                    ZoneCreationState.IsInCreationMode ||
                                    JumpMenuState.IsActive ||
                                    WindowlessFloatMenuState.IsActive ||
                                    WindowlessPauseMenuState.IsActive ||
                                    WindowlessSaveMenuState.IsActive ||
                                    WindowlessOptionsMenuState.IsActive ||
                                    WindowlessConfirmationState.IsActive ||
                                    StorageSettingsMenuState.IsActive ||
                                    PlantSelectionMenuState.IsActive ||
                                    WindowlessScheduleState.IsActive ||
                                    WindowlessResearchMenuState.IsActive ||
                                    StorytellerSelectionState.IsActive;

                if (!anyMenuActive)
                {
                    // If Shift is held, change time speed
                    if (Event.current.shift)
                    {
                        TimeSpeed newSpeed = TimeSpeed.Normal;

                        if (key == KeyCode.Alpha1 || key == KeyCode.Keypad1)
                            newSpeed = TimeSpeed.Normal;
                        else if (key == KeyCode.Alpha2 || key == KeyCode.Keypad2)
                            newSpeed = TimeSpeed.Fast;
                        else if (key == KeyCode.Alpha3 || key == KeyCode.Keypad3)
                            newSpeed = TimeSpeed.Superfast;

                        // Set the time speed
                        Find.TickManager.CurTimeSpeed = newSpeed;

                        // Play the appropriate sound effect
                        SoundDef soundDef = null;
                        switch (newSpeed)
                        {
                            case TimeSpeed.Paused:
                                soundDef = SoundDefOf.Clock_Stop;
                                break;
                            case TimeSpeed.Normal:
                                soundDef = SoundDefOf.Clock_Normal;
                                break;
                            case TimeSpeed.Fast:
                                soundDef = SoundDefOf.Clock_Fast;
                                break;
                            case TimeSpeed.Superfast:
                                soundDef = SoundDefOf.Clock_Superfast;
                                break;
                            case TimeSpeed.Ultrafast:
                                soundDef = SoundDefOf.Clock_Superfast;
                                break;
                        }
                        soundDef?.PlayOneShotOnCamera();

                        // Announce the change
                        string speedName = newSpeed == TimeSpeed.Normal ? "Normal" :
                                         newSpeed == TimeSpeed.Fast ? "Fast" :
                                         "Superfast";
                        ClipboardHelper.CopyToClipboard($"Time speed: {speedName}");

                        Event.current.Use();
                        return;
                    }
                    // If Shift is NOT held, consume event to block native time controls
                    // Keys 1-3 are now reserved for tile info (handled by DetailInfoPatch)
                    // DetailInfoPatch uses Input.GetKeyDown() which is separate from Event.current,
                    // so consuming the IMGUI event here won't affect DetailInfoPatch's functionality
                    else
                    {
                        Event.current.Use();
                        return;
                    }
                }
            }

            // ===== PRIORITY 6: Toggle draft mode with R key (if pawn is selected) =====
            if (key == KeyCode.R)
            {
                // Only toggle draft if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. A colonist pawn is selected
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    Find.Selector != null && Find.Selector.NumSelected > 0)
                {
                    // Get the first selected pawn
                    Pawn selectedPawn = Find.Selector.FirstSelectedObject as Pawn;

                    if (selectedPawn != null &&
                        selectedPawn.IsColonist &&
                        selectedPawn.drafter != null &&
                        selectedPawn.drafter.ShowDraftGizmo)
                    {
                        // Toggle draft state
                        bool wasDrafted = selectedPawn.drafter.Drafted;
                        selectedPawn.drafter.Drafted = !wasDrafted;

                        // Announce the change
                        string status = selectedPawn.drafter.Drafted ? "Drafted" : "Undrafted";
                        ClipboardHelper.CopyToClipboard($"{selectedPawn.LabelShort} {status}");

                        // Prevent the default R key behavior
                        Event.current.Use();
                        return;
                    }
                }
            }

            // ===== PRIORITY 6.5: Display mood info with Alt+M (if pawn is selected) =====
            if (key == KeyCode.M && Event.current.alt)
            {
                // Only display mood if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Display mood information
                    MoodState.DisplayMoodInfo();

                    // Prevent the default M key behavior
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.6: Open windowless schedule menu with S key =====
            if (key == KeyCode.S)
            {
                // Only open schedule if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. Schedule menu is not already active
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !WindowlessScheduleState.IsActive)
                {
                    // Prevent the default S key behavior
                    Event.current.Use();

                    // Open the windowless schedule menu
                    WindowlessScheduleState.Open();

                    return;
                }
            }

            // ===== PRIORITY 6.7: Open assign menu with Shift+A key =====
            if (key == KeyCode.A && Event.current.shift)
            {
                // Only open assign menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Prevent the default A key behavior (architect menu)
                    Event.current.Use();

                    // Get the selected pawn, or use first colonist if none selected
                    Pawn targetPawn = null;
                    if (Find.Selector != null && Find.Selector.NumSelected > 0)
                    {
                        targetPawn = Find.Selector.FirstSelectedObject as Pawn;
                    }

                    // If no pawn selected, use first colonist
                    if (targetPawn == null && Find.CurrentMap.mapPawns.FreeColonists.Any())
                    {
                        targetPawn = Find.CurrentMap.mapPawns.FreeColonists.First();
                    }

                    if (targetPawn != null)
                    {
                        // Open the assign menu
                        AssignMenuState.Open(targetPawn);
                    }
                    else
                    {
                        ClipboardHelper.CopyToClipboard("No colonists available");
                    }

                    return;
                }
            }

            // ===== PRIORITY 7: Open jump menu with J key (if no menu is active and we're in-game) =====
            if (key == KeyCode.J)
            {
                // Only open jump menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Prevent the default J key behavior
                    Event.current.Use();

                    // Open the jump menu
                    JumpMenuState.Open();
                    return;
                }
            }

            // ===== PRIORITY 7.5: Open research menu with P key (if no menu is active and we're in-game) =====
            if (key == KeyCode.P)
            {
                // Only open research menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Prevent the default P key behavior
                    Event.current.Use();

                    // Open the research menu
                    WindowlessResearchMenuState.Open();
                    return;
                }
            }

            // ===== PRIORITY 8: Open pause menu with Escape (if no menu is active and we're in-game) =====
            if (key == KeyCode.Escape)
            {
                Log.Message("RimWorld Access: Escape key pressed");
                // Only open pause menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    Log.Message("RimWorld Access: Opening pause menu");
                    // Prevent the default escape behavior (opening game's pause menu)
                    Event.current.Use();

                    // Open our windowless pause menu
                    WindowlessPauseMenuState.Open();
                    Log.Message($"RimWorld Access: Pause menu opened, IsActive = {WindowlessPauseMenuState.IsActive}");
                    return;
                }
            }

            // ===== PRIORITY 9: Handle Enter key for building inspection =====
            // Don't process if in zone creation mode
            if (ZoneCreationState.IsInCreationMode)
                return;

            // Handle Enter key for building inspection
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                // Only process during normal gameplay with a valid map
                if (Find.CurrentMap == null)
                    return;

                // Don't process if any dialog or window that prevents camera motion is open
                if (Find.WindowStack != null && Find.WindowStack.WindowsPreventCameraMotion)
                    return;

                // Check if map navigation is initialized
                if (!MapNavigationState.IsInitialized)
                    return;

                // Get the cursor position
                IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;
                Map map = Find.CurrentMap;

                // Validate cursor position
                if (!cursorPosition.IsValid || !cursorPosition.InBounds(map))
                {
                    ClipboardHelper.CopyToClipboard("Invalid position");
                    Event.current.Use();
                    return;
                }

                // Check if there's a building at the cursor position
                List<Thing> thingsAtPosition = map.thingGrid.ThingsListAt(cursorPosition);
                Thing buildingOrThing = null;

                // First priority: research benches - open research menu
                foreach (Thing thing in thingsAtPosition)
                {
                    if (thing is Building_ResearchBench)
                    {
                        // Open windowless research menu
                        WindowlessResearchMenuState.Open();
                        Event.current.Use();
                        return;
                    }
                }

                // Second priority: buildings with temperature control (coolers, heaters, etc.)
                foreach (Thing thing in thingsAtPosition)
                {
                    if (thing is Building building)
                    {
                        CompTempControl tempControl = building.TryGetComp<CompTempControl>();
                        if (tempControl != null)
                        {
                            // Directly open temperature control menu for coolers/heaters
                            TempControlMenuState.Open(building);
                            Event.current.Use();
                            return;
                        }
                    }
                }

                // Third priority: buildings with inspect tabs
                foreach (Thing thing in thingsAtPosition)
                {
                    if (thing.def.inspectorTabs != null && thing.def.inspectorTabs.Count > 0)
                    {
                        buildingOrThing = thing;
                        break;
                    }
                }

                // If there's a building with tabs, open building inspect menu
                if (buildingOrThing != null)
                {
                    BuildingInspectState.Open(buildingOrThing);
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 10: Handle right bracket ] key for colonist orders =====
            if (key == KeyCode.RightBracket)
            {
                // Only process during normal gameplay with a valid map
                if (Find.CurrentMap == null)
                    return;

                // Don't process if any dialog or window that prevents camera motion is open
                if (Find.WindowStack != null && Find.WindowStack.WindowsPreventCameraMotion)
                    return;

                // Check if map navigation is initialized
                if (!MapNavigationState.IsInitialized)
                    return;

                // Get the cursor position
                IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;
                Map map = Find.CurrentMap;

                // Validate cursor position
                if (!cursorPosition.IsValid || !cursorPosition.InBounds(map))
                {
                    ClipboardHelper.CopyToClipboard("Invalid position");
                    Event.current.Use();
                    return;
                }

                // Check for pawns to give orders to
                if (Find.Selector == null || !Find.Selector.SelectedPawns.Any())
                {
                    ClipboardHelper.CopyToClipboard("No pawn selected");
                    Event.current.Use();
                    return;
                }

                // Get selected pawns
                List<Pawn> selectedPawns = Find.Selector.SelectedPawns.ToList();

                // Get all available actions for this position using RimWorld's built-in system
                Vector3 clickPos = cursorPosition.ToVector3Shifted();
                List<FloatMenuOption> options = FloatMenuMakerMap.GetOptions(
                    selectedPawns,
                    clickPos,
                    out FloatMenuContext context
                );

                if (options != null && options.Count > 0)
                {
                    // Open the windowless menu with these options
                    WindowlessFloatMenuState.Open(options, true); // true = gives colonist orders
                }
                else
                {
                    ClipboardHelper.CopyToClipboard("No available actions");
                }

                // Consume the event
                Event.current.Use();
            }
        }

        /// <summary>
        /// Postfix patch that draws visual overlays for active windowless menus.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Draw schedule menu overlay if active
            if (WindowlessScheduleState.IsActive)
            {
                DrawScheduleMenuOverlay();
            }
        }

        /// <summary>
        /// Draws the visual overlay for the windowless schedule menu.
        /// </summary>
        private static void DrawScheduleMenuOverlay()
        {
            if (WindowlessScheduleState.Pawns.Count == 0)
                return;

            if (WindowlessScheduleState.SelectedPawnIndex < 0 ||
                WindowlessScheduleState.SelectedPawnIndex >= WindowlessScheduleState.Pawns.Count)
                return;

            Pawn selectedPawn = WindowlessScheduleState.Pawns[WindowlessScheduleState.SelectedPawnIndex];
            if (selectedPawn?.timetable == null)
                return;

            int hour = WindowlessScheduleState.SelectedHourIndex;
            TimeAssignmentDef currentAssignment = selectedPawn.timetable.GetAssignment(hour);

            // Get screen dimensions
            float screenWidth = UI.screenWidth;
            float screenHeight = UI.screenHeight;

            // Create overlay rect (top-center of screen)
            float overlayWidth = 800f;
            float overlayHeight = 140f;
            float overlayX = (screenWidth - overlayWidth) / 2f;
            float overlayY = 20f;

            Rect overlayRect = new Rect(overlayX, overlayY, overlayWidth, overlayHeight);

            // Draw semi-transparent background
            Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            Widgets.DrawBoxSolid(overlayRect, backgroundColor);

            // Draw border
            Color borderColor = new Color(0.5f, 0.7f, 1.0f, 1.0f);
            Widgets.DrawBox(overlayRect, 2);

            // Draw text
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            int pawnNum = WindowlessScheduleState.SelectedPawnIndex + 1;
            int totalPawns = WindowlessScheduleState.Pawns.Count;
            string title = $"Schedule Menu - {selectedPawn.LabelShort} ({pawnNum}/{totalPawns}) - Hour {hour}";
            string currentInfo = $"Current: {currentAssignment.label}";
            string instructions1 = "Arrows: Navigate | Tab: Change Cell | Space: Apply Selected";
            string instructions2 = "Shift+Right: Fill Row | Ctrl+C/V: Copy/Paste | Enter: Save | Esc: Cancel";

            Rect titleRect = new Rect(overlayX, overlayY + 10f, overlayWidth, 30f);
            Rect infoRect = new Rect(overlayX, overlayY + 40f, overlayWidth, 25f);
            Rect instructions1Rect = new Rect(overlayX, overlayY + 70f, overlayWidth, 25f);
            Rect instructions2Rect = new Rect(overlayX, overlayY + 100f, overlayWidth, 25f);

            Widgets.Label(titleRect, title);
            Widgets.Label(infoRect, currentInfo);

            Text.Font = GameFont.Tiny;
            Widgets.Label(instructions1Rect, instructions1);
            Widgets.Label(instructions2Rect, instructions2);

            // Reset text settings
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        /// <summary>
        /// Gets the label of the currently selected assignment type.
        /// </summary>
        private static string GetSelectedAssignmentLabel()
        {
            if (WindowlessScheduleState.SelectedAssignment != null)
            {
                return WindowlessScheduleState.SelectedAssignment.label;
            }
            return "Unknown";
        }

}
}
