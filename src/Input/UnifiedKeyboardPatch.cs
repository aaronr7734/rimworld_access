using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;

namespace RimWorldAccess
{
    /// <summary>
    /// Unified Harmony patch for UIRoot.UIRootOnGUI to handle all keyboard accessibility features.
    /// Handles: Escape key for pause menu, Enter key for building inspection/beds, ] key for colonist orders, I key for inspection menu, J key for scanner, L key for notification menu, F7 key for quest menu, Alt+M for mood info, Alt+H for health info, Alt+N for needs info, Alt+K for top skills, Alt+F for unforbid all items, Alt+Home for scanner auto-jump toggle, Shift+C for reform caravan (temporary maps), F2 for schedule, F3 for assign, F6 for research, and all windowless menu navigation.
    /// Note: Dialog navigation (including research completion dialogs) is handled by DialogAccessibilityPatch.
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
            // Process per-frame sound queue for bulk painting operations
            BulkSoundQueue.Update();

            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            // Interrupt speech on key press for backends that don't handle it
            // (e.g., macOS AVSpeechSynthesizer when VoiceOver is not running)
            if (TolkHelper.ShouldInterruptOnKeyPress)
            {
                TolkHelper.StopSpeech();
            }

            KeyCode key = Event.current.keyCode;

#if DEBUG
            // ===== DEV: Hot-reload the mod assembly (Alt+F5) =====
            // Debug-only shortcut that unloads the current mod assembly, loads the
            // freshly-built DLL from disk, and re-applies Harmony patches without
            // restarting RimWorld. Entire block is stripped from Release builds.
            if (key == KeyCode.F5
                && KeyboardHelper.IsAltHeld
                && !Event.current.control
                && !Event.current.shift)
            {
                Event.current.Use();
                ReloadHelper.Reload();
                return;
            }
#endif

            // ===== PRIORITY -1.6: Active text-edit session takes ALL keystrokes =====
            // When TextInputManager has an active controller, every key is routed to it
            // (chars + Enter/Escape/Backspace + Ctrl+C/V + cursor nav). NOTHING else
            // fires. We consume the event unconditionally — even keys the controller
            // didn't recognize — so downstream window-specific patches (world-gen,
            // scenario builder, etc.) that hook DoWindowContents can't grab the key
            // out from under the modal edit session. MarkHandledThisFrame tags the
            // frame so Window.OnAcceptKeyPressed / OnCancelKeyPressed patches can
            // block the game's Enter/Escape even after HandleEnter clears IsActive.
            if (TextInputManager.IsActive)
            {
                TextInputManager.Active.HandleEvent(Event.current);
                TextInputManager.MarkHandledThisFrame();
                Event.current.Use();
                return;
            }

            // ===== PRIORITY -1.5: Handle character input =====
            // Unity IMGUI sends two KeyDown events for printable chars:
            // 1. keyCode = KeyCode.A (the key itself)
            // 2. keyCode = KeyCode.None, character = 'a' (the character)
            // We need to capture the second event BEFORE filtering out KeyCode.None.
            // For typeahead consumers we use Event.character (layout-aware) directly —
            // never a KeyCode-range gate, which broke on non-Latin layouts where keys
            // producing letters are reported as KeyCode.Comma etc.
            if (key == KeyCode.None && Event.current.character != '\0')
            {
                char c = Event.current.character;
                if (!char.IsControl(c))
                {
                    // Layout-aware typeahead: walk the registered consumers.
                    if (TypeaheadDispatcher.TryDispatchChar(c))
                    {
                        Event.current.Use();
                        return;
                    }

                    // ScenarioBuilderState text editing now flows through TextInputManager
                    // at priority -1.6 above. No branch needed here.

                    // ScenarioBuilderPartEditState dropdown typeahead
                    if (ScenarioBuilderPartEditState.IsActive)
                    {
                        if (ScenarioBuilderPartEditState.HandleCharacterInput(c))
                        {
                            Event.current.Use();
                            return;
                        }
                    }

                    // ScenarioBuilderAddPartState typeahead
                    if (ScenarioBuilderAddPartState.IsActive)
                    {
                        if (ScenarioBuilderAddPartState.HandleCharacterInput(c))
                        {
                            Event.current.Use();
                            return;
                        }
                    }

                    // WindowlessScenarioSaveState filename input
                    if (WindowlessScenarioSaveState.IsActive)
                    {
                        if (WindowlessScenarioSaveState.HandleCharacterInput(c))
                        {
                            Event.current.Use();
                            return;
                        }
                    }

                    // PawnFilterPresetSaveState preset name input
                    if (PawnFilterPresetSaveState.IsActive)
                    {
                        if (PawnFilterPresetSaveState.HandleCharacterInput(c))
                        {
                            Event.current.Use();
                            return;
                        }
                    }

                    // PawnFilterPresetLoadState typeahead
                    if (PawnFilterPresetLoadState.IsActive)
                    {
                        if (PawnFilterPresetLoadState.HandleCharacterInput(c))
                        {
                            Event.current.Use();
                            return;
                        }
                    }

                    // PawnFilterState typeahead
                    if (PawnFilterState.IsActive && !WindowlessFloatMenuState.IsActive
                        && !PawnFilterPresetSaveState.IsActive && !PawnFilterPresetLoadState.IsActive)
                    {
                        if (PawnFilterState.HandleCharacterInput(c))
                        {
                            Event.current.Use();
                            return;
                        }
                    }

                    // HealthTabState typeahead (recipe and body part lists)
                    if (HealthTabState.IsActive)
                    {
                        if (HealthTabState.HandleCharacterInput(c))
                        {
                            Event.current.Use();
                            return;
                        }
                    }

                    // Xenogerm/XenotypeEditor renames flow through TextInputManager
                    // at priority -1.6 above.
                }
            }

            // Remap character-only events for non-US keyboard layouts (e.g., German AltGr+9 = ])
            key = KeyboardHelper.RemapCharacterToKeyCode(key);

            // Skip if no actual key (Unity IMGUI quirk)
            if (key == KeyCode.None)
                return;

            // ===== PRIORITY -1.1: Block ALL keys during pawn filter reroll =====
            // Only Escape is allowed (to cancel the reroll)
            if (RerollState.IsActive)
            {
                if (key == KeyCode.Escape)
                {
                    RerollState.Cancel();
                }
                Event.current.Use();
                return;
            }

            // (Old priority -1 / -0.95 / -0.94 modal text-input blocks moved to TextInputManager
            // at priority -1.6 above. Zone/storage/pen renames + xenogerm/xenotype renames are
            // all driven by TextInputController now.)

            // ===== PRIORITY -0.5: Block game hotkeys if windowless dialog is active =====
            // WindowlessDialogInputPatch handles navigation keys for the dialog
            // We need to block game-specific keys (R for draft, F for forbid, etc.)
            if (WindowlessDialogState.IsActive)
            {
                // If editing a text field, block EVERYTHING except the text field control keys
                // Text input characters will be handled by WindowlessDialogInputPatch
                if (WindowlessDialogState.IsEditingTextField)
                {
                    // Only allow Enter, Escape, Backspace, Delete for text field control
                    // Block ALL other keys including character input (will be handled by WindowlessDialogInputPatch)
                    if (key != KeyCode.Return && key != KeyCode.KeypadEnter &&
                        key != KeyCode.Escape && key != KeyCode.Backspace &&
                        key != KeyCode.Delete)
                    {
                        // Consume the event to prevent it from being processed by RimWorld's keybinding system
                        // This is critical for keys like Space (pause/unpause), F5 (save), and other game hotkeys
                        Event.current.Use();
                        return;
                    }
                }
                else
                {
                    // Not editing - allow arrow keys and Enter/Escape for dialog navigation
                    // These keys will be handled by WindowlessDialogInputPatch (VeryHigh priority)
                    // Block everything else (R, F, A, Z, Tab, etc.)
                    if (key != KeyCode.UpArrow && key != KeyCode.DownArrow &&
                        key != KeyCode.LeftArrow && key != KeyCode.RightArrow &&
                        key != KeyCode.Return && key != KeyCode.KeypadEnter &&
                        key != KeyCode.Escape)
                    {
                        // Consume the event to prevent game actions during dialog
                        Event.current.Use();
                        return;
                    }
                    // If we reach here, key is arrow/Enter/Escape for dialog navigation
                    // These are handled by WindowlessDialogInputPatch, so don't process them here
                    // Return immediately to prevent other handlers from interfering
                    return;
                }
            }

            // ===== EARLY CHECK: Skip arrow keys and Enter if Dialog_NodeTree is open =====
            // DialogAccessibilityPatch handles keyboard navigation for Dialog_NodeTree windows
            if (Find.WindowStack != null)
            {
                // Check if any Dialog_NodeTree window is currently open
                foreach (var window in Find.WindowStack.Windows)
                {
                    if (window is Dialog_NodeTree)
                    {
                        // Let arrow keys and Enter pass through to DialogAccessibilityPatch
                        if (key == KeyCode.UpArrow || key == KeyCode.DownArrow ||
                            key == KeyCode.Return || key == KeyCode.KeypadEnter)
                        {
                            Log.Message($"[UnifiedKeyboardPatch] Dialog_NodeTree open, letting key {key} pass through");
                            // Don't consume these keys - let DialogAccessibilityPatch handle them
                            return;
                        }
                        break;
                    }
                }
            }

            // ===== EARLY CHECK: Skip Enter/Escape if Dialog_MessageBox is open =====
            // MessageBoxAccessibilityPatch handles keyboard input for Dialog_MessageBox windows
            // (e.g. romance relationship warnings, shelf linking confirmations)
            if (Find.WindowStack != null)
            {
                foreach (var window in Find.WindowStack.Windows)
                {
                    if (window is Dialog_MessageBox)
                    {
                        if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Escape)
                        {
                            return; // Let MessageBoxAccessibilityPatch handle these
                        }
                        break;
                    }
                }
            }

            // ===== PRIORITY -0.2: Scanner search text input =====
            // Must run before all other handlers to capture letter keys that would otherwise
            // be intercepted by route planner (R), notifications (L), settlement browser (S), etc.
            if (ScannerSearchState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                // Enter: confirm search
                if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    ScannerSearchState.ConfirmSearch();
                    Event.current.Use();
                    return;
                }

                // Escape: cancel search
                if (key == KeyCode.Escape)
                {
                    ScannerSearchState.CancelSearch();
                    Event.current.Use();
                    return;
                }

                // Backspace: delete last character
                if (key == KeyCode.Backspace)
                {
                    ScannerSearchState.HandleBackspace();
                    Event.current.Use();
                    return;
                }

                // Letter keys (A-Z): add to search buffer
                // Request layout-aware character for typeahead (supports non-Latin keyboards)
                if (key >= KeyCode.A && key <= KeyCode.Z && !ctrl && !alt)
                {
                    Event.current.Use();
                    return;
                }

                // Number keys (0-9): add to search buffer
                if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9 && !ctrl && !alt)
                {
                    Event.current.Use();
                    return;
                }

                // Space/PageUp/PageDown/Home/End/Arrow keys: let pass through to game
                // Space is needed for placing designators while search is active
            }

            // ===== PRIORITY -0.2: GoTo coordinate text input =====
            // Must run before all other handlers to capture number keys
            if (GoToState.IsActive)
            {
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                // Enter: confirm and move cursor (only if no overlay menu is on top of us)
                if (!GoToState.ShouldYieldToOverlayMenu() &&
                    (key == KeyCode.Return || key == KeyCode.KeypadEnter))
                {
                    GoToState.ConfirmGoTo();
                    Event.current.Use();
                    return;
                }

                // Escape: cancel (only if no overlay menu is on top of us)
                if (!GoToState.ShouldYieldToOverlayMenu() && key == KeyCode.Escape)
                {
                    GoToState.Cancel();
                    Event.current.Use();
                    return;
                }

                // Backspace: delete last character
                if (key == KeyCode.Backspace)
                {
                    GoToState.HandleBackspace();
                    Event.current.Use();
                    return;
                }

                // Number keys (0-9) from main keyboard
                if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9 && !ctrl && !alt)
                {
                    Event.current.Use();
                    return;
                }

                // Number keys (0-9) from numpad
                if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9 && !ctrl && !alt)
                {
                    Event.current.Use();
                    return;
                }

                // Plus key for relative positive offset (Equals, Shift+Equals, or KeypadPlus)
                if ((key == KeyCode.KeypadPlus || key == KeyCode.Equals) && !ctrl && !alt)
                {
                    GoToState.HandleCharacter('+');
                    Event.current.Use();
                    return;
                }

                // Minus key for relative negative offset
                if ((key == KeyCode.Minus || key == KeyCode.KeypadMinus) && !ctrl && !alt)
                {
                    GoToState.HandleCharacter('-');
                    Event.current.Use();
                    return;
                }

                // Comma or Space: switch to Z field
                if ((key == KeyCode.Comma || key == KeyCode.Space) && !ctrl && !alt)
                {
                    GoToState.HandleFieldSeparator();
                    Event.current.Use();
                    return;
                }

                // Arrow keys, Tab, etc.: intentionally NOT consumed - pass through
            }

            // ===== PRIORITY -0.25: Handle Info Card dialog if active =====
            // Info Card is a modal dialog that should take precedence over most other handlers
            if (InfoCardState.IsActive)
            {
                if (InfoCardState.HandleInput(Event.current))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY -0.24: Handle Auto-Slaughter dialog if active =====
            // Auto-Slaughter is a modal dialog that should take precedence over most other handlers
            if (AutoSlaughterState.IsActive)
            {
                if (AutoSlaughterState.HandleInput(Event.current))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY -0.23: Handle Baby Gene Inspection if active =====
            // Gene inspection is a modal view for pregnancy genes (Biotech DLC)
            if (GeneInspectionState.IsActive)
            {
                if (GeneInspectionState.HandleInput(Event.current))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY -0.215: Handle Growth Moment dialog if active =====
            // Growth moment is a modal dialog for child development choices (Biotech DLC)
            if (GrowthMomentState.IsActive)
            {
                if (GrowthMomentState.HandleInput(key, Event.current.shift, Event.current.control, KeyboardHelper.IsAltHeld))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY -0.21: Handle Xenogerm Creation dialog if active =====
            // Gene processor dialog for creating xenogerms (Biotech DLC)
            if (XenogermState.IsActive)
            {
                if (XenogermState.HandleInput(Event.current))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY -0.205: Handle Xenotype Editor dialog if active =====
            // Xenotype editor dialog for creating custom xenotypes (Biotech DLC, chargen)
            if (XenotypeEditorState.IsActive)
            {
                if (XenotypeEditorState.HandleInput(Event.current))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY -0.22: Handle Faction Landing dialog if active =====
            // Faction relations is a modal dialog opened from starting site selection (F key)
            if (FactionLandingState.IsActive)
            {
                if (FactionLandingState.HandleInput(Event.current))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0: Handle world object selection if active =====
            if (WorldObjectSelectionState.IsActive && !WindowlessDialogState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;
                if (WorldObjectSelectionState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0: Handle caravan inspect screen if active (must be before key blocking) =====
            // BUT: Skip if windowless dialog, inspection, or gear equip menu is active - they take priority
            if (CaravanInspectState.IsActive && !WindowlessDialogState.IsActive && !GearEquipMenuState.IsActive && !WindowlessInspectionState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;
                if (CaravanInspectState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0: Handle settlement browser in world view (must be before key blocking) =====
            // BUT: Skip if windowless dialog is active - dialogs take absolute priority
            if (SettlementBrowserState.IsActive && !WindowlessDialogState.IsActive)
            {
                if (SettlementBrowserState.HandleInput(key))
                {
                    Event.current.Use();
                    return;
                }

                // === Handle Home/End for menu navigation ===
                if (key == KeyCode.Home)
                {
                    SettlementBrowserState.JumpToFirst();
                    Event.current.Use();
                    return;
                }
                if (key == KeyCode.End)
                {
                    SettlementBrowserState.JumpToLast();
                    Event.current.Use();
                    return;
                }

                // === Handle Backspace for typeahead ===
                if (key == KeyCode.Backspace)
                {
                    SettlementBrowserState.HandleBackspace();
                    Event.current.Use();
                    return;
                }

                // === Consume ALL alphanumeric + * for typeahead ===
                // This MUST be at the end to catch any unhandled characters
                // Request layout-aware character for typeahead (supports non-Latin keyboards)
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;
                bool isStar = key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8);

                if ((isLetter || isNumber || isStar) && !KeyboardHelper.IsAltHeld)
                {
                    if (isStar)
                    {
                        // Reserved for future "expand all at level" in tree views
                        Event.current.Use();
                        return;
                    }
                    Event.current.Use();
                    return;  // CRITICAL: Don't fall through to other handlers
                }
            }

            // ===== PRIORITY 0.25: Handle caravan destination selection if active =====
            // BUT: Skip if windowless dialog is active - dialogs take absolute priority
            if (CaravanFormationState.IsChoosingDestination && !WindowlessDialogState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                // Handle Enter key to set destination
                if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
                {
                    CaravanFormationState.SetDestination(WorldNavigationState.CurrentSelectedTile);
                    Event.current.Use();
                    return;
                }

                // Handle Escape key to cancel destination selection
                if (key == KeyCode.Escape)
                {
                    CaravanFormationState.CancelDestinationSelection();
                    Event.current.Use();
                    return;
                }

                // Let arrow keys pass through for world navigation
            }

            // ===== DEFENSIVE STATE CLEANUP =====
            // If placement state has stale internal values but no designator is selected,
            // clean up the state. This is belt-and-suspenders with the defensive IsActive properties.
            // Guard with CurrentMap check — Find.DesignatorManager throws during entry screen.
            if (Find.CurrentMap != null &&
                (ShapePlacementState.CurrentPhase != PlacementPhase.Inactive ||
                ArchitectState.CurrentMode == ArchitectMode.PlacementMode))
            {
                if (Find.DesignatorManager?.SelectedDesignator == null)
                {
                    Log.Message("[UnifiedKeyboardPatch] Detected stale placement state, cleaning up");
                    ShapePlacementState.Reset();
                    ArchitectState.Reset();
                    // State was stale, cleaned up - continue with normal flow
                }
            }

            // ===== PRIORITY 0.17: Shape Selection Menu =====
            if (ShapeSelectionMenuState.IsActive)
            {
                if (ShapeSelectionMenuState.HandleInput(Event.current))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.18: Viewing Mode (post-placement review) =====
            if (ViewingModeState.IsActive)
            {
                // Don't let ViewingMode steal input from overlay screens
                // opened on top of it (inventory, gizmos, inspection, etc.)
                bool overlayActive = WindowlessInventoryState.IsActive
                    || GizmoNavigationState.IsActive
                    || WindowlessInspectionState.IsActive
                    || WindowlessFloatMenuState.IsActive;

                if (!overlayActive && ViewingModeState.HandleInput(key, Event.current.shift))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.19: Shape Placement (two-point selection) =====
            // Input handled in ArchitectPlacementPatch, but state needs priority registration
            if (ShapePlacementState.IsActive)
            {
                // Let ArchitectPlacementPatch handle the input
                // This ensures proper priority ordering
            }

            // ===== PRIORITY 0.195: Line Formation Placement (multi-select pawn movement) =====
            if (LineFormationState.IsActive)
            {
                if (key == KeyCode.Space)
                {
                    LineFormationState.PlacePoint();
                    Event.current.Use();
                    return;
                }
                if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && LineFormationState.HasBothPoints)
                {
                    LineFormationState.Confirm();
                    Event.current.Use();
                    return;
                }
                if (key == KeyCode.Escape)
                {
                    LineFormationState.Cancel();
                    Event.current.Use();
                    return;
                }
                // Arrow keys pass through to map navigation (don't consume)
            }

            // ===== PRIORITY 0.22: Handle inspection menu EARLY if opened from caravan/split/inspect/transport pod dialogs =====
            // This ensures Escape in inspection doesn't get caught by other handlers
            // Note: Window.OnCancelKeyPressed is patched in CaravanFormationPatch and TransportPodPatch to block RimWorld's Cancel handling
            if (WindowlessInspectionState.IsActive && (CaravanFormationState.IsActive || SplitCaravanState.IsActive || CaravanInspectState.IsActive || TransportPodLoadingState.IsActive))
            {
                if (WindowlessInspectionState.HandleInput(Event.current))
                {
                    return;
                }
            }

            // ===== PRIORITY 0.24: Handle stat breakdown if active =====
            // This overlays caravan summary view when inspecting stat factors
            if (StatBreakdownState.IsActive)
            {
                if (StatBreakdownState.HandleInput(key))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.25: Handle quantity menu if active =====
            // This overlays caravan formation/split dialogs
            // Note: Window.OnCancelKeyPressed is patched in CaravanFormationPatch to block RimWorld's Cancel handling
            if (QuantityMenuState.IsActive && !WindowlessDialogState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                if (QuantityMenuState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.27: Handle shelf linking selection mode if active =====
            // This is the custom storage linking mode activated from our gizmos
            // Note: Confirmation dialog now uses Dialog_MessageBox, handled by MessageBoxAccessibilityPatch
            if (ShelfLinkingState.IsActive && !WindowlessDialogState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                if (ShelfLinkingState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.28: Handle transport pod selection mode if active =====
            // This is the custom pod grouping mode activated from our "Select pods to group" gizmo
            if (TransportPodSelectionState.IsActive && !WindowlessDialogState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                if (TransportPodSelectionState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.29: Handle area selection menu if active =====
            // This prompts for area selection when an area designator is chosen from Architect
            if (AreaSelectionMenuState.IsActive)
            {
                if (AreaSelectionMenuState.HandleInput(key))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.292: Handle pawn area assignment menu if active =====
            // Opened via Alt+A to assign allowed areas to the selected pawn
            if (PawnAreaMenuState.IsActive)
            {
                if (PawnAreaMenuState.HandleInput(key))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.3: Handle caravan formation dialog if active =====
            // BUT: Skip if windowless dialog, inspection, or quantity menu is active - they take priority
            if (CaravanFormationState.IsActive && !CaravanFormationState.IsChoosingDestination && !WindowlessDialogState.IsActive && !WindowlessInspectionState.IsActive && !QuantityMenuState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                if (CaravanFormationState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.32: Handle transport pod loading dialog if active =====
            // Skip if overlay menus are active - they take priority
            if (TransportPodLoadingState.IsActive && !WindowlessDialogState.IsActive && !WindowlessInspectionState.IsActive && !QuantityMenuState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                if (TransportPodLoadingState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.33: Handle ritual dialog if active =====
            // Handles all ritual types (weddings, funerals, childbirth, conversions, etc.)
            // Skip if overlay states are active - they take priority
            if (RitualState.IsActive && !WindowlessDialogState.IsActive && !StatBreakdownState.IsActive && !WindowlessInspectionState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                if (RitualState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.35: Handle split caravan dialog if active =====
            if (SplitCaravanState.IsActive && !WindowlessDialogState.IsActive && !WindowlessInspectionState.IsActive && !QuantityMenuState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                if (SplitCaravanState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.36: Handle transport pod launch targeting if active =====
            // This handles Enter/Escape/F keys during world map launch targeting
            if (TransportPodLaunchState.IsActive && !WindowlessDialogState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                if (TransportPodLaunchState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.365: Handle gravship destination targeting if active =====
            // This handles Enter/Escape/F keys during gravship world map destination selection
            if (GravshipDestinationState.IsActive && !WindowlessDialogState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                if (GravshipDestinationState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.37: Handle gear equip menu if active =====
            if (GearEquipMenuState.IsActive && !WindowlessDialogState.IsActive)
            {
                if (GearEquipMenuState.HandleInput(Event.current))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.372: Handle jump targeting if active =====
            // This provides R key during jump pack / locust armor targeting
            if (JumpTargetingState.IsActive && !WindowlessDialogState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                if (JumpTargetingState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.373: Handle ability targeting if active =====
            // This provides R, T, I keys during psycast/ability map targeting
            if (AbilityTargetingState.IsActive && !WindowlessDialogState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                if (AbilityTargetingState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.374: Handle Command_Target targeting with range context (R key) =====
            // This provides R key range check during animal attack targeting and similar Command_Target operations
            if (TargetingPatch.HasTargetingContext && Find.Targeter.IsTargeting && !WindowlessDialogState.IsActive)
            {
                if (key == KeyCode.R && !Event.current.shift && !Event.current.control && !KeyboardHelper.IsAltHeld)
                {
                    TargetingPatch.HandleRangeCheck();
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.376: Handle world ability targeting if active =====
            // This handles Enter/Escape/I keys during world map ability targeting (e.g., Farskip)
            if (WorldAbilityTargetingState.IsActive && !WindowlessDialogState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                if (WorldAbilityTargetingState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.5: Handle world scanner keys (PageUp/PageDown/Home/End) =====
            // Skip if any accessibility menu is active - they handle their own Enter/navigation keys
            // Note: KeyboardHelper.IsAnyAccessibilityMenuActive() covers all menus that need exclusion
            if (WorldNavigationState.IsActive &&
                !KeyboardHelper.IsAnyAccessibilityMenuActive())
            {
                bool handled = false;
                bool alt = KeyboardHelper.IsAltHeld;
                bool ctrl = Event.current.control;
                bool shift = Event.current.shift;

                // Page Down: Navigate scanner (Ctrl=category, Shift=subcategory, Alt=instance, none=item)
                if (key == KeyCode.PageDown)
                {
                    if (ctrl && !shift && !alt)
                        WorldScannerState.NextCategory();
                    else if (shift && !ctrl && !alt)
                        WorldScannerState.NextSubcategory();
                    else if (alt && !ctrl && !shift)
                        WorldScannerState.NextInstance();
                    else if (!ctrl && !shift && !alt)
                        WorldScannerState.NextItem();
                    handled = true;
                }
                // Page Up: Navigate scanner (Ctrl=category, Shift=subcategory, Alt=instance, none=item)
                else if (key == KeyCode.PageUp)
                {
                    if (ctrl && !shift && !alt)
                        WorldScannerState.PreviousCategory();
                    else if (shift && !ctrl && !alt)
                        WorldScannerState.PreviousSubcategory();
                    else if (alt && !ctrl && !shift)
                        WorldScannerState.PreviousInstance();
                    else if (!ctrl && !shift && !alt)
                        WorldScannerState.PreviousItem();
                    handled = true;
                }
                // Home: Jump to scanner item (Alt = home settlement)
                else if (key == KeyCode.Home && !shift && !ctrl)
                {
                    if (alt)
                        WorldNavigationState.JumpToHome();
                    else
                        WorldScannerState.JumpToCurrent();
                    handled = true;
                }
                // End: Read distance/direction (Alt = nearest caravan, in-game only)
                else if (key == KeyCode.End && !shift && !ctrl)
                {
                    if (alt && WorldNavigationState.Context == WorldNavContext.InGame)
                        WorldNavigationState.JumpToNearestCaravan();
                    else if (!alt)
                        WorldScannerState.ReadDistanceAndDirection();
                    handled = true;
                }
                // Alt+J: Toggle auto-jump mode
                else if (key == KeyCode.J && alt && !shift && !ctrl)
                {
                    WorldScannerState.ToggleAutoJumpMode();
                    handled = true;
                }
                // === In-game only keys (caravans, inspect, notifications, world object selection) ===
                // These must not consume events during WorldGen - StartingSitePatch handles I/Enter/etc.
                else if (WorldNavigationState.Context == WorldNavContext.InGame)
                {
                    // Comma/Period: Cycle caravans
                    if (key == KeyCode.Period && !shift && !ctrl && !alt)
                    {
                        WorldNavigationState.CycleToNextCaravan();
                        handled = true;
                    }
                    else if (key == KeyCode.Comma && !shift && !ctrl && !alt)
                    {
                        WorldNavigationState.CycleToPreviousCaravan();
                        handled = true;
                    }
                    // Ctrl+Space: Toggle caravan multi-selection
                    else if (key == KeyCode.Space && !shift && ctrl && !alt)
                    {
                        WorldNavigationState.ToggleCaravanSelection();
                        handled = true;
                    }
                    // Alt+C: Jump cursor to selected caravan(s)
                    else if (key == KeyCode.C && !shift && !ctrl && alt)
                    {
                        WorldNavigationState.JumpToSelectedCaravans();
                        handled = true;
                    }
                    // I key: Open caravan inspect screen for selected caravan
                    // Skip if gizmo menu is active - let typeahead handle the key
                    else if (key == KeyCode.I && !shift && !ctrl && !alt && !GizmoNavigationState.IsActive)
                    {
                        WorldNavigationState.ShowCaravanInspect();
                        handled = true;
                    }
                    // Enter key: Open world object selection/inspection at current tile
                    // Skip if route planner is active - it handles Enter for confirming routes
                    else if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt && !RoutePlannerState.IsActive)
                    {
                        PlanetTile currentTile = WorldNavigationState.CurrentSelectedTile;
                        if (currentTile.Valid)
                        {
                            WorldObjectSelectionState.Open(currentTile);
                            handled = true;
                        }
                    }
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.55: Handle world gen starting site keys =====
            // These keys are normally handled by StartingSitePatch.Prefix (inside GUI.Window),
            // but after closing the faction dialog, GUI.Window focus may not be re-established
            // properly, so we handle them here (outside GUI.Window context) as well.
            // This follows the same dual-handling pattern used for Z key (priority 4.745)
            // and 1-5 tile info keys (priority 5.45).
            if (WorldNavigationState.IsActive &&
                WorldNavigationState.Context == WorldNavContext.WorldGen &&
                !StartingPawnState.IsActive &&
                !IdeologyNavigationState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                // When I-menu is open, route keys to menu navigation
                if (StartingSiteContext.IsMenuOpen)
                {
                    if (key == KeyCode.UpArrow)
                    {
                        StartingSiteContext.NavigateMenu(-1);
                        Event.current.Use();
                        return;
                    }
                    else if (key == KeyCode.DownArrow)
                    {
                        StartingSiteContext.NavigateMenu(1);
                        Event.current.Use();
                        return;
                    }
                    else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                    {
                        StartingSiteContext.ReadSelectedMenuItem();
                        Event.current.Use();
                        return;
                    }
                    else if (key == KeyCode.Escape)
                    {
                        StartingSiteContext.CloseMenu();
                        Event.current.Use();
                        return;
                    }
                    // Block all other keys while I-menu is open
                    // (Scanner keys at priority 0.5 already passed through)
                    Event.current.Use();
                    return;
                }

                // Arrow keys: plain = world navigation, Ctrl = biome jump
                if (key == KeyCode.UpArrow || key == KeyCode.DownArrow ||
                    key == KeyCode.LeftArrow || key == KeyCode.RightArrow)
                {
                    if (ctrl)
                    {
                        StartingSiteContext.JumpToNextBiomeInDirection(key);
                    }
                    else
                    {
                        WorldNavigationState.HandleArrowKey(key);
                    }
                    Event.current.Use();
                    return;
                }

                // R key: random tile selection
                if (key == KeyCode.R && !shift && !ctrl && !alt && !ScannerSearchState.IsActive)
                {
                    StartingSiteContext.SelectRandomTile();
                    Event.current.Use();
                    return;
                }

                // Space: re-announce current tile
                if (key == KeyCode.Space && !shift && !ctrl && !alt)
                {
                    WorldNavigationState.AnnounceTile();
                    Event.current.Use();
                    return;
                }

                // I key: open additional info menu
                if (key == KeyCode.I && !shift && !ctrl && !alt && !ScannerSearchState.IsActive)
                {
                    StartingSiteContext.OpenAdditionalInfoMenu();
                    Event.current.Use();
                    return;
                }

                // F key: open faction relations dialog
                if (key == KeyCode.F && !shift && !ctrl && !alt && !ScannerSearchState.IsActive)
                {
                    Find.WindowStack.Add(new Dialog_FactionDuringLanding());
                    Event.current.Use();
                    return;
                }

                // Escape: go back to world generation settings
                if (key == KeyCode.Escape && !shift && !ctrl && !alt)
                {
                    var page = Find.WindowStack.WindowOfType<Page_SelectStartingSite>();
                    if (page != null)
                    {
                        AccessTools.Method(typeof(Page), "DoBack").Invoke(page, null);
                    }
                    Event.current.Use();
                    return;
                }

                // Note: Z key, 1-5 number keys, PageUp/PageDown/Home/End/J are already
                // handled by other sections (priorities -0.2, 0.5, 4.745, 5.45).
            }

            // ===== PRIORITY 0.6: Handle route planner if active =====
            // Route planner needs to handle Space (add waypoint), Delete (remove), E (ETA), Escape (close)
            // Space must be consumed to prevent pause/unpause
            // Note: Must check ProgramState first - Find.WorldRoutePlanner access crashes on main menu
            if (Current.ProgramState == ProgramState.Playing && RoutePlannerState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                if (RoutePlannerState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.7: R key to toggle route planner in world view =====
            // Note: Must check ProgramState first for safety
            // Note: Skip if any menu with typeahead is active - let typeahead handle the key
            if (Current.ProgramState == ProgramState.Playing &&
                WorldNavigationState.IsActive &&
                !CaravanFormationState.IsActive &&
                !CaravanInspectState.IsActive &&
                !WindowlessDialogState.IsActive &&
                !RoutePlannerState.IsActive &&
                !GizmoNavigationState.IsActive &&
                !TradeNavigationState.IsActive &&
                !SellableItemsState.IsActive &&
                !HistoryState.IsActive)
            {
                if (key == KeyCode.R && !Event.current.shift && !Event.current.control && !KeyboardHelper.IsAltHeld)
                {
                    RoutePlannerState.Open();
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.75: Handle F8 to dismiss world map and restore cursor (in-game only) =====
            // F8 is the world map toggle - when pressed while on world map, dismiss it and restore cursor
            if (key == KeyCode.F8 &&
                WorldNavigationState.IsActive &&
                WorldNavigationState.Context == WorldNavContext.InGame &&
                !CaravanFormationState.IsActive &&
                !SplitCaravanState.IsActive &&
                !KeyboardHelper.IsAnyAccessibilityMenuActive())
            {
                // Close world view and restore cursor to last known position
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
                TolkHelper.Speak("RimWorldAccess.Input.Close.ReturnedToMap".Loc());
                Event.current.Use();
                return;
            }

            // ===== EARLY BLOCK: If in world view (in-game), block most map-specific keys =====
            // Only applies to in-game world map - world gen has its own key handling in StartingSitePatch
            // Don't block when choosing destination (allow map interaction)
            // Don't block Enter/Escape when menus are active (need them for menu navigation)
            // Use IsAnyAccessibilityMenuActive() to cover all windowless menus (pause, save, load, options, etc.)
            if (WorldNavigationState.IsActive &&
                WorldNavigationState.Context == WorldNavContext.InGame &&
                !CaravanFormationState.IsActive &&
                !SplitCaravanState.IsActive &&
                !GearEquipMenuState.IsActive &&
                !QuantityMenuState.IsActive &&
                !QuestMenuState.IsActive &&
                !CaravanInspectState.IsActive &&
                !KeyboardHelper.IsAnyAccessibilityMenuActive())
            {
                // Tab/Shift+Tab: cycle planet layer (Surface ↔ Orbit)
                if (key == KeyCode.Tab && !Event.current.control && !KeyboardHelper.IsAltHeld)
                {
                    WorldNavigationState.CyclePlanetLayer();
                    Event.current.Use();
                    return;
                }

                // Block all map-specific keys - world scanner handles PageUp/PageDown/Home/End above
                // Note: R is NOT blocked - it opens route planner (handled above)
                // Note: G is NOT blocked - it opens gizmos for world objects (caravans, settlements)
                // Note: L is NOT blocked - it cycles planet layers (handled above)
                // Note: F1-F7 are NOT blocked - intercept patches handle them
                if (key == KeyCode.A ||
                    key == KeyCode.Q ||
                    key == KeyCode.Return || key == KeyCode.KeypadEnter ||
                    key == KeyCode.P || key == KeyCode.S ||
                    key == KeyCode.L ||
                    (key == KeyCode.M && KeyboardHelper.IsAltHeld) ||
                    (key == KeyCode.H && KeyboardHelper.IsAltHeld) ||
                    (key == KeyCode.N && KeyboardHelper.IsAltHeld) ||
                    (key == KeyCode.B && KeyboardHelper.IsAltHeld) ||
                    (key == KeyCode.K && KeyboardHelper.IsAltHeld) ||
                    (key == KeyCode.A && KeyboardHelper.IsAltHeld) ||
                    (key == KeyCode.F && KeyboardHelper.IsAltHeld) ||
                    (key == KeyCode.R && KeyboardHelper.IsAltHeld))
                {
                    // These keys should not work in world view - they're map-specific
                    // Must consume the event to prevent game from opening its inaccessible menus
                    Event.current.Use();
                    return;
                }
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

            // ===== PRIORITY 2.1: Handle Scenario Builder overlays (highest priority within builder) =====
            // These overlays must be handled before the main builder state
            if (WindowlessScenarioDeleteConfirmState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;
                if (WindowlessScenarioDeleteConfirmState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            if (WindowlessScenarioLoadState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;
                // Handle character input for typeahead
                if (Event.current.character != '\0' && !ctrl && !alt && char.IsLetterOrDigit(Event.current.character))
                {
                    if (WindowlessScenarioLoadState.HandleCharacterInput(Event.current.character))
                    {
                        Event.current.Use();
                        return;
                    }
                }

                if (WindowlessScenarioLoadState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            if (WindowlessScenarioSaveState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;
                // Handle character input for filename typing
                if (Event.current.character != '\0' && !ctrl && !alt)
                {
                    if (WindowlessScenarioSaveState.HandleCharacterInput(Event.current.character))
                    {
                        Event.current.Use();
                        return;
                    }
                }

                if (WindowlessScenarioSaveState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            if (ScenarioBuilderAddPartState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;
                // Handle character input for typeahead
                if (Event.current.character != '\0' && !ctrl && !alt && char.IsLetterOrDigit(Event.current.character))
                {
                    if (ScenarioBuilderAddPartState.HandleCharacterInput(Event.current.character))
                    {
                        Event.current.Use();
                        return;
                    }
                }

                if (ScenarioBuilderAddPartState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            if (ScenarioBuilderPartEditState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;
                // Handle character input for dropdown typeahead
                if (Event.current.character != '\0' && !ctrl && !alt && char.IsLetterOrDigit(Event.current.character))
                {
                    if (ScenarioBuilderPartEditState.HandleCharacterInput(Event.current.character))
                    {
                        Event.current.Use();
                        return;
                    }
                }

                if (ScenarioBuilderPartEditState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 2.2: Handle Scenario Builder main state =====
            if (ScenarioBuilderState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;
                // Handle character input for text editing or typeahead
                if (Event.current.character != '\0' && !ctrl && !alt)
                {
                    if (ScenarioBuilderState.HandleCharacterInput(Event.current.character))
                    {
                        Event.current.Use();
                        return;
                    }
                }

                if (ScenarioBuilderState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 2.2: Pawn filter preset overlays =====
            if (PawnFilterPresetDeleteConfirmState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;
                if (PawnFilterPresetDeleteConfirmState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            if (PawnFilterPresetLoadState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                if (PawnFilterPresetLoadState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            if (PawnFilterPresetSaveState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                if (PawnFilterPresetSaveState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 2.25: Pawn filter editor =====
            if (PawnFilterState.IsActive && !WindowlessFloatMenuState.IsActive
                && !PawnFilterPresetSaveState.IsActive && !PawnFilterPresetLoadState.IsActive)
            {
                if (PawnFilterState.HandleInput(key, Event.current))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 2.3: Handle pawn selection screen =====
            if (StartingPawnState.IsActive && !WindowlessFloatMenuState.IsActive && !WindowlessDialogState.IsActive
                && !PawnFilterState.IsActive && !Find.WindowStack.IsOpen<Dialog_NamePawn>())
            {
                if (StartingPawnState.HandleInput(Event.current))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 2.4: Handle windowless area manager if active =====
            // Skip if dialog is active (e.g., Rename Area dialog)
            if (WindowlessAreaState.IsActive && !WindowlessDialogState.IsActive)
            {
                bool areaHandled = false;
                var mode = WindowlessAreaState.CurrentMode;

                if (mode == WindowlessAreaState.NavigationMode.AreaList)
                {
                    // Area list mode
                    if (key == KeyCode.UpArrow)
                    {
                        WindowlessAreaState.SelectPreviousArea();
                        areaHandled = true;
                    }
                    else if (key == KeyCode.DownArrow)
                    {
                        WindowlessAreaState.SelectNextArea();
                        areaHandled = true;
                    }
                    else if (key == KeyCode.LeftArrow || key == KeyCode.RightArrow)
                    {
                        // Consume Left/Right to prevent them from reaching Schedule
                        // Area list doesn't use horizontal navigation
                        areaHandled = true;
                    }
                    else if (key == KeyCode.RightBracket)
                    {
                        WindowlessAreaState.EnterActionsMode();
                        areaHandled = true;
                    }
                    else if (key == KeyCode.Escape)
                    {
                        WindowlessAreaState.Close();
                        areaHandled = true;
                    }
                }
                else if (mode == WindowlessAreaState.NavigationMode.AreaActions)
                {
                    // Actions menu mode
                    // Escape - clear search first, then return to area list
                    if (key == KeyCode.Escape)
                    {
                        if (WindowlessAreaState.HasActiveActionsSearch)
                        {
                            WindowlessAreaState.ClearActionsSearch();
                            areaHandled = true;
                        }
                        else
                        {
                            WindowlessAreaState.ReturnToAreaList();
                            areaHandled = true;
                        }
                    }
                    // Backspace for search
                    else if (key == KeyCode.Backspace && WindowlessAreaState.HasActiveActionsSearch)
                    {
                        WindowlessAreaState.HandleActionsBackspace();
                        areaHandled = true;
                    }
                    // Up arrow - navigate with search awareness
                    else if (key == KeyCode.UpArrow)
                    {
                        WindowlessAreaState.SelectPreviousActionMatch();
                        areaHandled = true;
                    }
                    // Down arrow - navigate with search awareness
                    else if (key == KeyCode.DownArrow)
                    {
                        WindowlessAreaState.SelectNextActionMatch();
                        areaHandled = true;
                    }
                    else if (key == KeyCode.Home)
                    {
                        WindowlessAreaState.SelectFirstAction();
                        areaHandled = true;
                    }
                    else if (key == KeyCode.End)
                    {
                        WindowlessAreaState.SelectLastAction();
                        areaHandled = true;
                    }
                    else if (key == KeyCode.LeftArrow || key == KeyCode.RightArrow)
                    {
                        // Consume Left/Right to prevent them from reaching Schedule
                        // Actions menu doesn't use horizontal navigation
                        areaHandled = true;
                    }
                    else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                    {
                        WindowlessAreaState.ExecuteAction();
                        areaHandled = true;
                    }
                    // Typeahead characters (letters and numbers)
                    else
                    {
                        bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                        bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                        if ((isLetter || isNumber) && !KeyboardHelper.IsAltHeld)
                        {
                            areaHandled = true;
                        }
                    }
                }

                if (areaHandled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 2.5: Handle area painting mode if active =====
            // BUT: Skip if windowless dialog is active - dialogs take absolute priority
            if (AreaPaintingState.IsActive && !WindowlessDialogState.IsActive)
            {
                bool handled = false;

                // Tab key - toggle between box selection and single tile selection modes
                if (key == KeyCode.Tab)
                {
                    // Cancel any active rectangle selection when switching modes
                    if (AreaPaintingState.HasRectangleStart)
                    {
                        AreaPaintingState.CancelRectangle();
                    }

                    AreaPaintingState.ToggleSelectionMode();
                    handled = true;
                }
                else if (key == KeyCode.Space)
                {
                    IntVec3 currentPosition = MapNavigationState.CurrentCursorPosition;

                    if (AreaPaintingState.SelectionMode == AreaSelectionMode.SingleTile)
                    {
                        // Single tile mode - toggle the current cell
                        AreaPaintingState.ToggleStageCell();
                    }
                    else
                    {
                        // Box selection mode - set corners and confirm rectangles
                        if (!AreaPaintingState.HasRectangleStart)
                        {
                            // No start corner yet - set it
                            AreaPaintingState.SetRectangleStart(currentPosition);
                        }
                        else if (AreaPaintingState.IsInPreviewMode)
                        {
                            // We have a preview - confirm this rectangle
                            AreaPaintingState.ConfirmRectangle();
                        }
                        else
                        {
                            // Start is set but no end yet - update to create preview at current position
                            AreaPaintingState.UpdatePreview(currentPosition);
                            // Then confirm it
                            AreaPaintingState.ConfirmRectangle();
                        }
                    }
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    // If in preview mode, confirm the rectangle first
                    if (AreaPaintingState.IsInPreviewMode)
                    {
                        AreaPaintingState.ConfirmRectangle();
                    }
                    // Then confirm the area painting
                    AreaPaintingState.Confirm();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    if (AreaPaintingState.HasRectangleStart)
                    {
                        // Cancel current rectangle selection
                        AreaPaintingState.CancelRectangle();
                    }
                    else
                    {
                        // No rectangle in progress - cancel entire area painting
                        AreaPaintingState.Cancel();
                    }
                    handled = true;
                }
                // Note: Arrow keys are NOT handled here - they pass through to MapNavigationPatch

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 2.6: Handle trade menu if active =====
            // Skip if overlay states are active - they handle their own input
            if (TradeNavigationState.IsActive && !WindowlessDialogState.IsActive && !WindowlessInspectionState.IsActive && !StatBreakdownState.IsActive)
            {
                bool handled = false;

                // Check for modifier keys
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                // Handle Escape - clear search FIRST, then exit quantity mode, then close
                if (key == KeyCode.Escape)
                {
                    if (TradeNavigationState.HasActiveSearch)
                    {
                        TradeNavigationState.ClearTypeaheadSearch();
                        handled = true;
                    }
                    else
                    {
                        // Escape exits quantity mode first, then closes menu
                        bool exitedQuantityMode = TradeNavigationState.ExitQuantityMode();
                        // If we didn't exit quantity mode (were already in list view), close the trade
                        if (!exitedQuantityMode)
                        {
                            TradeNavigationState.CloseAndAnnounceCancel();
                            TradeSession.Close();
                        }
                        handled = true;
                    }
                }
                // Handle Backspace for search (but not in quantity mode - numeric backspace takes priority)
                else if (key == KeyCode.Backspace && TradeNavigationState.HasActiveSearch && !TradeNavigationState.IsInQuantityMode)
                {
                    TradeNavigationState.ProcessBackspace();
                    handled = true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    if (shift)
                        TradeNavigationState.AdjustQuantityLarge(-1);
                    else if (ctrl)
                        TradeNavigationState.AdjustQuantityVeryLarge(-1);
                    else if (TradeNavigationState.HasActiveSearch && !TradeNavigationState.HasNoMatches)
                        TradeNavigationState.SelectNextMatch();
                    else
                        TradeNavigationState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    if (shift)
                        TradeNavigationState.AdjustQuantityLarge(1);
                    else if (ctrl)
                        TradeNavigationState.AdjustQuantityVeryLarge(1);
                    else if (TradeNavigationState.HasActiveSearch && !TradeNavigationState.HasNoMatches)
                        TradeNavigationState.SelectPreviousMatch();
                    else
                        TradeNavigationState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.LeftArrow)
                {
                    TradeNavigationState.PreviousCategory();
                    handled = true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    TradeNavigationState.NextCategory();
                    handled = true;
                }
                else if (key == KeyCode.Home)
                {
                    if (TradeNavigationState.IsInQuantityMode || shift)
                    {
                        // Home or Shift+Home: set to minimum (most selling)
                        TradeNavigationState.SetToMinimumAction();
                    }
                    else
                    {
                        // Home: jump to first item
                        TradeNavigationState.JumpToFirst();
                    }
                    handled = true;
                }
                else if (key == KeyCode.End)
                {
                    if (TradeNavigationState.IsInQuantityMode || shift)
                    {
                        // End or Shift+End: set to maximum (most buying)
                        TradeNavigationState.SetToMaximumAction();
                    }
                    else
                    {
                        // End: jump to last item
                        TradeNavigationState.JumpToLast();
                    }
                    handled = true;
                }
                else if (key == KeyCode.Delete)
                {
                    // Delete: reset current item to zero
                    TradeNavigationState.ResetCurrentItem();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    // Enter either enters quantity mode or exits it
                    TradeNavigationState.EnterQuantityMode();
                    handled = true;
                }
                // Alt+key shortcuts (to not conflict with typeahead)
                else if (alt && key == KeyCode.A)
                {
                    TradeNavigationState.AcceptTrade();
                    handled = true;
                }
                else if (alt && key == KeyCode.R && !shift)
                {
                    TradeNavigationState.ResetCurrentItem();
                    handled = true;
                }
                else if (alt && key == KeyCode.R && shift)
                {
                    TradeNavigationState.ResetAll();
                    handled = true;
                }
                else if (alt && key == KeyCode.G)
                {
                    TradeNavigationState.ToggleGiftMode();
                    handled = true;
                }
                else if (alt && key == KeyCode.P)
                {
                    TradeNavigationState.ShowPriceBreakdown();
                    handled = true;
                }
                else if (alt && key == KeyCode.B)
                {
                    TradeNavigationState.AnnounceTradeBalance();
                    handled = true;
                }
                else if (alt && key == KeyCode.I)
                {
                    // Alt+I: Inspect current item
                    TradeNavigationState.InspectCurrentItem();
                    handled = true;
                }
                else if (key == KeyCode.Tab && !shift && !ctrl && !alt)
                {
                    // Tab: Show price breakdown (same as Alt+P)
                    TradeNavigationState.ShowPriceBreakdown();
                    handled = true;
                }
                else if (key == KeyCode.Minus || key == KeyCode.KeypadMinus)
                {
                    // In quantity mode, minus starts selling input; otherwise adjust by -1
                    if (TradeNavigationState.IsInQuantityMode && !shift && !ctrl && !alt)
                    {
                        TradeNavigationState.HandleNumericInput('-');
                    }
                    else
                    {
                        // Use AdjustQuantitySingle to respect selling/buying context
                        TradeNavigationState.AdjustQuantitySingle(-1);
                    }
                    handled = true;
                }
                else if (key == KeyCode.Plus || key == KeyCode.KeypadPlus || key == KeyCode.Equals)
                {
                    // Use AdjustQuantitySingle to respect selling/buying context
                    TradeNavigationState.AdjustQuantitySingle(1);
                    handled = true;
                }
                else if (key == KeyCode.Backspace && TradeNavigationState.IsInQuantityMode && TradeNavigationState.HasActiveNumericInput)
                {
                    // Backspace in quantity mode with active input: delete last digit
                    TradeNavigationState.HandleNumericBackspace();
                    handled = true;
                }
                // Handle typeahead characters (letters and numbers - commands now use Alt+ so no exclusions needed)
                else
                {
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;
                    bool isKeypadNumber = key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9;

                    if ((isLetter || isNumber || isKeypadNumber) && !shift && !ctrl && !alt)
                    {
                        // Layout-aware character dispatch routes to TradeNavigationState.HandleTypeahead.
                        // Just consume the KeyCode here so default game handlers don't fire.
                        handled = true;
                    }
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 2.7: Handle sellable items dialog if active =====
            if (SellableItemsState.IsActive)
            {
                bool handled = false;

                // Handle Escape - clear search first, then close
                if (key == KeyCode.Escape)
                {
                    if (SellableItemsState.HasActiveSearch)
                    {
                        SellableItemsState.ClearTypeaheadSearch();
                    }
                    else
                    {
                        SellableItemsState.Close();
                    }
                    handled = true;
                }
                // Handle Backspace for search
                else if (key == KeyCode.Backspace && SellableItemsState.HasActiveSearch)
                {
                    SellableItemsState.ProcessBackspace();
                    handled = true;
                }
                // Tab navigation
                else if (key == KeyCode.RightArrow)
                {
                    SellableItemsState.NextTab();
                    handled = true;
                }
                else if (key == KeyCode.LeftArrow)
                {
                    SellableItemsState.PreviousTab();
                    handled = true;
                }
                // Item navigation
                else if (key == KeyCode.DownArrow)
                {
                    if (SellableItemsState.HasActiveSearch && !SellableItemsState.HasNoMatches)
                        SellableItemsState.SelectNextMatch();
                    else
                        SellableItemsState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    if (SellableItemsState.HasActiveSearch && !SellableItemsState.HasNoMatches)
                        SellableItemsState.SelectPreviousMatch();
                    else
                        SellableItemsState.SelectPrevious();
                    handled = true;
                }
                // Jump navigation
                else if (key == KeyCode.Home)
                {
                    SellableItemsState.JumpToFirst();
                    handled = true;
                }
                else if (key == KeyCode.End)
                {
                    SellableItemsState.JumpToLast();
                    handled = true;
                }
                // Typeahead characters
                else
                {
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if ((isLetter || isNumber) && !Event.current.shift && !Event.current.control && !KeyboardHelper.IsAltHeld)
                    {
                        handled = true;
                    }
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 3: Handle save/load menu if active =====
            if (WindowlessSaveMenuState.IsActive)
            {
                bool handled = false;

                // Handle Home - jump to first
                if (key == KeyCode.Home)
                {
                    WindowlessSaveMenuState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to last
                else if (key == KeyCode.End)
                {
                    WindowlessSaveMenuState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then close
                else if (key == KeyCode.Escape)
                {
                    if (WindowlessSaveMenuState.HasActiveSearch)
                    {
                        WindowlessSaveMenuState.ClearTypeaheadSearch();
                    }
                    else
                    {
                        WindowlessSaveMenuState.GoBack();
                    }
                    handled = true;
                }
                // Handle Backspace for search
                else if (key == KeyCode.Backspace && WindowlessSaveMenuState.HasActiveSearch)
                {
                    WindowlessSaveMenuState.ProcessBackspace();
                    handled = true;
                }
                // Handle Down arrow - navigate with search awareness
                else if (key == KeyCode.DownArrow)
                {
                    WindowlessSaveMenuState.SelectNextMatch();
                    handled = true;
                }
                // Handle Up arrow - navigate with search awareness
                else if (key == KeyCode.UpArrow)
                {
                    WindowlessSaveMenuState.SelectPreviousMatch();
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
                // Handle typeahead characters
                else
                {
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if ((isLetter || isNumber) && !KeyboardHelper.IsAltHeld)
                    {
                        handled = true;
                    }
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
                if (WindowlessPauseMenuState.HandleInput())
                {
                    Event.current.Use();
                    return;
                }

                // HandleInput returns false for Escape without active search - handle closing here
                if (key == KeyCode.Escape)
                {
                    WindowlessPauseMenuState.Close();
                    TolkHelper.Speak("RimWorldAccess.Input.Close.MenuClosed".Loc());
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.05: Handle extra menus if active =====
            if (ExtraMenusState.IsActive)
            {
                if (ExtraMenusState.HandleInput())
                {
                    Event.current.Use();
                    return;
                }

                // HandleInput returns false for Escape without active search - handle closing here
                if (key == KeyCode.Escape)
                {
                    ExtraMenusState.Close();
                    TolkHelper.Speak("RimWorldAccess.Input.Close.MenuClosed".Loc());
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.2: Handle History tab if active =====
            // History tab has two sub-tabs: Statistics and Messages
            // Tab/Shift+Tab switches between them, sub-states handle navigation
            if (HistoryState.IsActive && !WindowlessDialogState.IsActive)
            {
                // Safety check: If the History window is no longer open (e.g., closed by game
                // when switching to world view via dialog jump), clean up our state.
                // This prevents Escape from being swallowed with no effect.
                bool historyWindowOpen = Find.WindowStack?.Windows?.Any(w => w is MainTabWindow_History) ?? false;
                if (!historyWindowOpen)
                {
                    HistoryState.Close();
                    // Don't consume the event - let it propagate to pause menu or world navigation
                }
                else
                {
                    bool shift = Event.current.shift;
                    bool ctrl = Event.current.control;
                    bool alt = KeyboardHelper.IsAltHeld;

                    // Check sub-states first - they handle navigation within their tabs
                    if (HistoryState.CurrentTab == HistoryState.Tab.Statistics && HistoryStatisticsState.IsActive)
                    {
                        if (HistoryStatisticsState.HandleInput(key, shift, ctrl, alt))
                        {
                            Event.current.Use();
                            return;
                        }
                    }
                    else if (HistoryState.CurrentTab == HistoryState.Tab.Messages && HistoryMessagesState.IsActive)
                    {
                        if (HistoryMessagesState.HandleInput(key, shift, ctrl, alt))
                        {
                            Event.current.Use();
                            return;
                        }
                    }

                    // Tab-level input (Tab key to switch tabs)
                    if (HistoryState.HandleInput(key, shift, ctrl, alt))
                    {
                        Event.current.Use();
                        return;
                    }

                    // Escape with no search active - let RimWorld close the window
                    // (HistoryPatch.Window_OnCancelKeyPressed_Prefix controls when to block)
                }
            }

            // ===== PRIORITY 4.5: Handle storyteller selection (in-game) if active =====
            // Skip if WindowlessFloatMenuState is active (e.g., reset to preset menu opened with Alt+R)
            if (StorytellerSelectionState.IsActive && !WindowlessFloatMenuState.IsActive)
            {
                bool handled = false;
                bool inCustomSettings = StorytellerSelectionState.CurrentLevel == StorytellerSelectionLevel.CustomSectionList ||
                                        StorytellerSelectionState.CurrentLevel == StorytellerSelectionLevel.CustomSettingsList;
                bool inSettingsList = StorytellerSelectionState.CurrentLevel == StorytellerSelectionLevel.CustomSettingsList;

                // Alt+R - Reset to preset (only in custom settings)
                if (key == KeyCode.R && KeyboardHelper.IsAltHeld && inCustomSettings)
                {
                    StorytellerSelectionState.OpenResetToPresetMenu();
                    handled = true;
                }
                // Home - Jump to first (or Shift+Home in settings = min value)
                else if (key == KeyCode.Home)
                {
                    if (Event.current.shift && inSettingsList)
                    {
                        StorytellerSelectionState.SetCurrentSettingToMin();
                    }
                    else
                    {
                        StorytellerSelectionState.JumpToFirst();
                    }
                    handled = true;
                }
                // End - Jump to last (or Shift+End in settings = max value)
                else if (key == KeyCode.End)
                {
                    if (Event.current.shift && inSettingsList)
                    {
                        StorytellerSelectionState.SetCurrentSettingToMax();
                    }
                    else
                    {
                        StorytellerSelectionState.JumpToLast();
                    }
                    handled = true;
                }
                // Navigation with typeahead support
                else if (key == KeyCode.DownArrow)
                {
                    if (StorytellerSelectionState.HasActiveSearch)
                    {
                        StorytellerSelectionState.SelectNextMatch();
                    }
                    else
                    {
                        StorytellerSelectionState.SelectNext();
                    }
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    if (StorytellerSelectionState.HasActiveSearch)
                    {
                        StorytellerSelectionState.SelectPreviousMatch();
                    }
                    else
                    {
                        StorytellerSelectionState.SelectPrevious();
                    }
                    handled = true;
                }
                // Left/Right - Adjust slider values with modifiers (only in custom settings list)
                else if (key == KeyCode.LeftArrow && inSettingsList)
                {
                    if (Event.current.control)
                    {
                        // Ctrl+Left = decrease by 25% of total positions
                        StorytellerSelectionState.AdjustCurrentSettingByPercent(-0.25f);
                    }
                    else if (Event.current.shift)
                    {
                        // Shift+Left = decrease by 10% of total positions
                        StorytellerSelectionState.AdjustCurrentSettingByPercent(-0.1f);
                    }
                    else
                    {
                        // Left = decrease by 1 step
                        StorytellerSelectionState.AdjustCurrentSetting(-1);
                    }
                    handled = true;
                }
                else if (key == KeyCode.RightArrow && inSettingsList)
                {
                    if (Event.current.control)
                    {
                        // Ctrl+Right = increase by 25% of total positions
                        StorytellerSelectionState.AdjustCurrentSettingByPercent(0.25f);
                    }
                    else if (Event.current.shift)
                    {
                        // Shift+Right = increase by 10% of total positions
                        StorytellerSelectionState.AdjustCurrentSettingByPercent(0.1f);
                    }
                    else
                    {
                        // Right = increase by 1 step
                        StorytellerSelectionState.AdjustCurrentSetting(1);
                    }
                    handled = true;
                }
                // Tab - Switch between storyteller/difficulty (only at top levels)
                else if (key == KeyCode.Tab)
                {
                    StorytellerSelectionState.SwitchLevel();
                    handled = true;
                }
                // Space - Toggle checkbox (only in custom settings list)
                else if (key == KeyCode.Space && inSettingsList)
                {
                    StorytellerSelectionState.ToggleCurrentSetting();
                    handled = true;
                }
                // Enter - Execute or enter deeper level
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    StorytellerSelectionState.ExecuteOrEnter();
                    handled = true;
                }
                // Escape - Clear search first, then go back or close
                else if (key == KeyCode.Escape)
                {
                    if (StorytellerSelectionState.HasActiveSearch)
                    {
                        StorytellerSelectionState.ClearTypeaheadSearch();
                    }
                    else if (!StorytellerSelectionState.GoBack())
                    {
                        // At top level - close the dialog and return to Gameplay options
                        StorytellerSelectionState.Close();
                        Find.WindowStack.TryRemove(typeof(Page_SelectStorytellerInGame), doCloseSound: false);
                        // Reopen the options menu at Gameplay category (3), Change Storyteller setting (0)
                        WindowlessOptionsMenuState.Open(3, 0);
                    }
                    handled = true;
                }
                // Backspace - Remove last character from typeahead
                else if (key == KeyCode.Backspace)
                {
                    if (StorytellerSelectionState.HasActiveSearch)
                    {
                        StorytellerSelectionState.ProcessBackspace();
                        handled = true;
                    }
                }
                // Typeahead character input - request layout-aware character (supports non-Latin keyboards)
                else if (!KeyboardHelper.IsAltHeld && !Event.current.control)
                {
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;
                    bool isKeypadNumber = key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9;

                    if (isLetter || isNumber || isKeypadNumber)
                    {
                        // Layout-aware character dispatch routes to StorytellerSelectionState.HandleTypeahead.
                        // Just consume the KeyCode here so default game handlers don't fire.
                        handled = true;
                    }
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.6: Handle options menu if active =====
            // Skip if float menu is active (e.g., language selection, reset preset menu)
            if (WindowlessOptionsMenuState.IsActive && !WindowlessFloatMenuState.IsActive)
            {
                bool handled = false;

                // Handle Home - jump to first
                if (key == KeyCode.Home)
                {
                    WindowlessOptionsMenuState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to last
                else if (key == KeyCode.End)
                {
                    WindowlessOptionsMenuState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then go back
                else if (key == KeyCode.Escape)
                {
                    if (WindowlessOptionsMenuState.HasActiveSearch)
                    {
                        WindowlessOptionsMenuState.ClearTypeaheadSearch();
                        handled = true;
                    }
                    else
                    {
                        WindowlessOptionsMenuState.GoBack();
                        handled = true;
                    }
                }
                // Handle Backspace for search
                else if (key == KeyCode.Backspace && WindowlessOptionsMenuState.HasActiveSearch)
                {
                    WindowlessOptionsMenuState.ProcessBackspace();
                    handled = true;
                }
                // Handle Up arrow - navigate with search awareness
                else if (key == KeyCode.UpArrow)
                {
                    if (WindowlessOptionsMenuState.HasActiveSearch && !WindowlessOptionsMenuState.HasNoMatches)
                    {
                        WindowlessOptionsMenuState.SelectPreviousMatch();
                    }
                    else
                    {
                        WindowlessOptionsMenuState.SelectPrevious();
                    }
                    handled = true;
                }
                // Handle Down arrow - navigate with search awareness
                else if (key == KeyCode.DownArrow)
                {
                    if (WindowlessOptionsMenuState.HasActiveSearch && !WindowlessOptionsMenuState.HasNoMatches)
                    {
                        WindowlessOptionsMenuState.SelectNextMatch();
                    }
                    else
                    {
                        WindowlessOptionsMenuState.SelectNext();
                    }
                    handled = true;
                }
                // Handle Left/Right arrows - only for settings level to adjust values
                else if (key == KeyCode.LeftArrow)
                {
                    if (WindowlessOptionsMenuState.CurrentLevel == OptionsMenuLevel.SettingsList)
                    {
                        WindowlessOptionsMenuState.AdjustSetting(-1);  // Decrease slider or cycle left
                        handled = true;
                    }
                }
                else if (key == KeyCode.RightArrow)
                {
                    if (WindowlessOptionsMenuState.CurrentLevel == OptionsMenuLevel.SettingsList)
                    {
                        WindowlessOptionsMenuState.AdjustSetting(1);   // Increase slider or cycle right
                        handled = true;
                    }
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessOptionsMenuState.ExecuteSelected();
                    handled = true;
                }
                // Handle typeahead characters (letter keys)
                else
                {
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if ((isLetter || isNumber) && !Event.current.shift && !Event.current.control && !KeyboardHelper.IsAltHeld)
                    {
                        handled = true;
                    }
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
            // Skip if float menu is open (e.g., right bracket context menu in Areas column)
            // Skip if placement mode is active (e.g., after Manage Areas → Expand Area)
            // Skip if dialog is active (e.g., Rename Area dialog)
            if (WindowlessScheduleState.IsActive && !WindowlessFloatMenuState.IsActive &&
                !ShapePlacementState.IsActive && !ViewingModeState.IsActive &&
                !WindowlessDialogState.IsActive)
            {
                bool handled = false;
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;

                // Tab / Shift+Tab: Switch columns (Schedule <-> Areas)
                if (key == KeyCode.Tab && !ctrl)
                {
                    WindowlessScheduleState.SwitchColumn();
                    handled = true;
                }
                // Escape: Close menu
                else if (key == KeyCode.Escape)
                {
                    WindowlessScheduleState.Close();
                    handled = true;
                }
                // Ctrl+C: Copy schedule
                else if (ctrl && key == KeyCode.C)
                {
                    WindowlessScheduleState.CopySchedule();
                    handled = true;
                }
                // Ctrl+V: Paste schedule
                else if (ctrl && key == KeyCode.V)
                {
                    WindowlessScheduleState.PasteSchedule();
                    handled = true;
                }
                // Number keys 1-9, 0: Select brush (both columns)
                else if (!ctrl && !shift && !KeyboardHelper.IsAltHeld)
                {
                    int brushIndex = -1;
                    if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9)
                        brushIndex = key - KeyCode.Alpha1; // 1->0, 2->1, ..., 9->8
                    else if (key == KeyCode.Alpha0)
                        brushIndex = 9; // 0->9

                    if (brushIndex >= 0)
                    {
                        WindowlessScheduleState.SelectBrush(brushIndex);
                        handled = true;
                    }
                }

                // Column-dependent navigation
                if (!handled)
                {
                    if (WindowlessScheduleState.IsInAreasColumn)
                    {
                        // === AREAS COLUMN ===
                        if (key == KeyCode.UpArrow && !shift)
                        {
                            WindowlessScheduleState.MoveUp();
                            handled = true;
                        }
                        else if (key == KeyCode.DownArrow && !shift)
                        {
                            WindowlessScheduleState.MoveDown();
                            handled = true;
                        }
                        else if (key == KeyCode.LeftArrow)
                        {
                            WindowlessScheduleState.SelectPreviousArea();
                            handled = true;
                        }
                        else if (key == KeyCode.RightArrow)
                        {
                            WindowlessScheduleState.SelectNextArea();
                            handled = true;
                        }
                        else if (key == KeyCode.UpArrow && shift)
                        {
                            WindowlessScheduleState.ApplyAreaToPawnAbove();
                            handled = true;
                        }
                        else if (key == KeyCode.DownArrow && shift)
                        {
                            WindowlessScheduleState.ApplyAreaToPawnBelow();
                            handled = true;
                        }
                        else if (key == KeyCode.RightBracket)
                        {
                            WindowlessScheduleState.OpenAreaContextMenu();
                            handled = true;
                        }
                        // Home/End navigation and painting in Areas column:
                        // Home/End = jump to first/last area
                        // Ctrl+Home/End = jump to first/last pawn
                        // Shift+Home/End = paint area to first/last pawn
                        // Ctrl+Shift+Home/End = paint area to ALL pawns
                        else if (key == KeyCode.Home && !ctrl && !shift)
                        {
                            WindowlessScheduleState.JumpToFirstArea();
                            handled = true;
                        }
                        else if (key == KeyCode.End && !ctrl && !shift)
                        {
                            WindowlessScheduleState.JumpToLastArea();
                            handled = true;
                        }
                        else if (key == KeyCode.Home && ctrl && !shift)
                        {
                            WindowlessScheduleState.JumpToFirstPawn();
                            handled = true;
                        }
                        else if (key == KeyCode.End && ctrl && !shift)
                        {
                            WindowlessScheduleState.JumpToLastPawn();
                            handled = true;
                        }
                        else if (key == KeyCode.Home && shift && !ctrl)
                        {
                            WindowlessScheduleState.PaintAreaToFirstPawn();
                            handled = true;
                        }
                        else if (key == KeyCode.End && shift && !ctrl)
                        {
                            WindowlessScheduleState.PaintAreaToLastPawn();
                            handled = true;
                        }
                        else if (key == KeyCode.Home && shift && ctrl)
                        {
                            WindowlessScheduleState.PaintAreaToAllPawns(towardFirst: true);
                            handled = true;
                        }
                        else if (key == KeyCode.End && shift && ctrl)
                        {
                            WindowlessScheduleState.PaintAreaToAllPawns(towardFirst: false);
                            handled = true;
                        }
                        else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                        {
                            WindowlessScheduleState.ConfirmAreaSelection();
                            handled = true;
                        }
                        else if (key == KeyCode.Backspace)
                        {
                            WindowlessScheduleState.HandleBackspace();
                            handled = true;
                        }
                    }
                    else
                    {
                        // === SCHEDULE COLUMN ===
                        if (key == KeyCode.UpArrow && !shift)
                        {
                            WindowlessScheduleState.MoveUp();
                            handled = true;
                        }
                        else if (key == KeyCode.DownArrow && !shift)
                        {
                            WindowlessScheduleState.MoveDown();
                            handled = true;
                        }
                        else if (key == KeyCode.LeftArrow && !shift)
                        {
                            WindowlessScheduleState.MoveLeft();
                            handled = true;
                        }
                        else if (key == KeyCode.RightArrow && !shift)
                        {
                            WindowlessScheduleState.MoveRight();
                            handled = true;
                        }
                        // Shift+Arrows: Paint mode
                        else if (key == KeyCode.UpArrow && shift)
                        {
                            WindowlessScheduleState.PaintUp();
                            handled = true;
                        }
                        else if (key == KeyCode.DownArrow && shift)
                        {
                            WindowlessScheduleState.PaintDown();
                            handled = true;
                        }
                        else if (key == KeyCode.LeftArrow && shift)
                        {
                            WindowlessScheduleState.PaintLeft();
                            handled = true;
                        }
                        else if (key == KeyCode.RightArrow && shift)
                        {
                            WindowlessScheduleState.PaintRight();
                            handled = true;
                        }
                        else if (key == KeyCode.Space || key == KeyCode.Return || key == KeyCode.KeypadEnter)
                        {
                            WindowlessScheduleState.ApplyBrush();
                            handled = true;
                        }
                        // Home/End navigation and painting:
                        // Home = first hour, End = last hour
                        // Shift+Home/End = paint to first/last hour
                        // Ctrl+Home/End = first/last pawn (keep column)
                        // Ctrl+Shift+Home/End = paint to first/last pawn
                        else if (key == KeyCode.Home && !ctrl && !shift)
                        {
                            WindowlessScheduleState.JumpToFirstHour();
                            handled = true;
                        }
                        else if (key == KeyCode.Home && !ctrl && shift)
                        {
                            WindowlessScheduleState.PaintToFirstHour();
                            handled = true;
                        }
                        else if (key == KeyCode.End && !ctrl && !shift)
                        {
                            WindowlessScheduleState.JumpToLastHour();
                            handled = true;
                        }
                        else if (key == KeyCode.End && !ctrl && shift)
                        {
                            WindowlessScheduleState.PaintToLastHour();
                            handled = true;
                        }
                        else if (key == KeyCode.Home && ctrl && !shift)
                        {
                            WindowlessScheduleState.JumpToFirstPawn();
                            handled = true;
                        }
                        else if (key == KeyCode.Home && ctrl && shift)
                        {
                            WindowlessScheduleState.PaintToFirstPawn();
                            handled = true;
                        }
                        else if (key == KeyCode.End && ctrl && !shift)
                        {
                            WindowlessScheduleState.JumpToLastPawn();
                            handled = true;
                        }
                        else if (key == KeyCode.End && ctrl && shift)
                        {
                            WindowlessScheduleState.PaintToLastPawn();
                            handled = true;
                        }
                        else if (key == KeyCode.Backspace)
                        {
                            WindowlessScheduleState.HandleBackspace();
                            handled = true;
                        }
                    }
                }

                // Typeahead: Letter keys for pawn name search (both columns)
                if (!handled && !ctrl && !KeyboardHelper.IsAltHeld)
                {
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    if (isLetter)
                    {
                        handled = true;
                    }
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

                // Handle Alt+I - open info card for current item
                if (KeyboardHelper.IsAltHeld && key == KeyCode.I && !Event.current.shift && !Event.current.control)
                {
                    WindowlessResearchDetailState.OpenInfoCard();
                    handled = true;
                }
                // Handle Home - jump to first (Ctrl+Home for absolute first)
                else if (key == KeyCode.Home)
                {
                    if (Event.current.control)
                        WindowlessResearchDetailState.JumpToAbsoluteFirst();
                    else
                        WindowlessResearchDetailState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to last (Ctrl+End for absolute last)
                else if (key == KeyCode.End)
                {
                    if (Event.current.control)
                        WindowlessResearchDetailState.JumpToAbsoluteLast();
                    else
                        WindowlessResearchDetailState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then close
                else if (key == KeyCode.Escape)
                {
                    if (WindowlessResearchDetailState.HasActiveSearch)
                    {
                        WindowlessResearchDetailState.ClearTypeaheadSearch();
                        handled = true;
                    }
                    else
                    {
                        WindowlessResearchDetailState.Close();
                        handled = true;
                    }
                }
                // Handle Backspace for search
                else if (key == KeyCode.Backspace && WindowlessResearchDetailState.HasActiveSearch)
                {
                    WindowlessResearchDetailState.ProcessBackspace();
                    handled = true;
                }
                // Handle Up/Down with typeahead filtering
                else if (key == KeyCode.DownArrow)
                {
                    if (WindowlessResearchDetailState.HasActiveSearch && !WindowlessResearchDetailState.HasNoMatches)
                    {
                        WindowlessResearchDetailState.SelectNextMatch();
                    }
                    else
                    {
                        WindowlessResearchDetailState.SelectNext();
                    }
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    if (WindowlessResearchDetailState.HasActiveSearch && !WindowlessResearchDetailState.HasNoMatches)
                    {
                        WindowlessResearchDetailState.SelectPreviousMatch();
                    }
                    else
                    {
                        WindowlessResearchDetailState.SelectPrevious();
                    }
                    handled = true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    WindowlessResearchDetailState.Expand();
                    handled = true;
                }
                else if (key == KeyCode.LeftArrow)
                {
                    WindowlessResearchDetailState.Collapse();
                    handled = true;
                }
                // Handle * key - expand all sibling categories (WCAG tree view pattern)
                else if (key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8))
                {
                    WindowlessResearchDetailState.ExpandAllSiblings();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessResearchDetailState.ExecuteCurrentItem();
                    handled = true;
                }
                // Handle typeahead characters
                else
                {
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if ((isLetter || isNumber) && !KeyboardHelper.IsAltHeld)
                    {
                        handled = true;
                    }
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

                // Handle Alt+I - open info card for selected project
                if (KeyboardHelper.IsAltHeld && key == KeyCode.I)
                {
                    WindowlessResearchMenuState.OpenInfoCard();
                    handled = true;
                }
                // Handle Home - jump to first (Ctrl+Home for absolute first)
                if (key == KeyCode.Home)
                {
                    if (Event.current.control)
                        WindowlessResearchMenuState.JumpToAbsoluteFirst();
                    else
                        WindowlessResearchMenuState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to last (Ctrl+End for absolute last)
                else if (key == KeyCode.End)
                {
                    if (Event.current.control)
                        WindowlessResearchMenuState.JumpToAbsoluteLast();
                    else
                        WindowlessResearchMenuState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then close
                else if (key == KeyCode.Escape)
                {
                    if (WindowlessResearchMenuState.HasActiveSearch)
                    {
                        WindowlessResearchMenuState.ClearTypeaheadSearch();
                        handled = true;
                    }
                    else
                    {
                        WindowlessResearchMenuState.Close();
                        handled = true;
                    }
                }
                // Handle Backspace for search
                else if (key == KeyCode.Backspace && WindowlessResearchMenuState.HasActiveSearch)
                {
                    WindowlessResearchMenuState.ProcessBackspace();
                    handled = true;
                }
                // Handle * key - expand all sibling categories (WCAG tree view pattern)
                else if (key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8))
                {
                    WindowlessResearchMenuState.ExpandAllSiblings();
                    handled = true;
                }
                // Handle Up/Down with typeahead filtering (only navigate matches when there ARE matches)
                else if (key == KeyCode.DownArrow)
                {
                    if (WindowlessResearchMenuState.HasActiveSearch && !WindowlessResearchMenuState.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        WindowlessResearchMenuState.SelectNextMatch();
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        WindowlessResearchMenuState.SelectNext();
                    }
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    if (WindowlessResearchMenuState.HasActiveSearch && !WindowlessResearchMenuState.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        WindowlessResearchMenuState.SelectPreviousMatch();
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        WindowlessResearchMenuState.SelectPrevious();
                    }
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
                // Handle typeahead characters
                // Request layout-aware character for typeahead (supports non-Latin keyboards)
                else
                {
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if ((isLetter || isNumber) && !KeyboardHelper.IsAltHeld)
                    {
                        handled = true;
                    }
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.71: Handle faction tab if active =====
            if (FactionTabState.IsActive)
            {
                if (FactionTabState.HandleInput(Event.current))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.72: Handle ideology tab if active =====
            if (IdeologyTabState.IsActive && !WindowlessFloatMenuState.IsActive)
            {
                if (IdeologyTabState.HandleInput(Event.current))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.73: Handle quest menu if active =====
            if (QuestMenuState.IsActive)
            {
                // Clean up reward menu state if float menu was closed externally
                if (QuestMenuState.HasActiveRewardMenu && !WindowlessFloatMenuState.IsActive)
                {
                    QuestMenuState.CleanupRewardMenu();
                }

                // --- Reward Choice Float Menu Active ---
                // Intercept only special keys; let everything else fall through to float menu at priority 5.0
                if (WindowlessFloatMenuState.IsActive && QuestMenuState.HasActiveRewardMenu)
                {
                    bool alt = KeyboardHelper.IsAltHeld;

                    if (QuestMenuState.IsInItemInspectionMenu)
                    {
                        // Item inspection sub-menu: intercept Enter and Escape
                        if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                        {
                            QuestMenuState.InspectCurrentItem();
                            Event.current.Use();
                            return;
                        }
                        else if (key == KeyCode.Escape)
                        {
                            QuestMenuState.ReturnToRewardChoiceMenu();
                            Event.current.Use();
                            return;
                        }
                        // Up/Down/Home/End: fall through to float menu handler at priority 5.0
                    }
                    else
                    {
                        // Reward choice menu: intercept Alt+I only
                        if (alt && key == KeyCode.I)
                        {
                            QuestMenuState.OpenItemInspectionForCurrentChoice();
                            Event.current.Use();
                            return;
                        }
                        // Enter/Up/Down/Escape/Home/End: fall through to float menu handler
                    }
                }
                // --- Normal Quest Menu Handling (no float menu active) ---
                else if (!WindowlessFloatMenuState.IsActive)
                {
                    bool handled = false;
                    bool alt = KeyboardHelper.IsAltHeld;

                    // --- Reward Preferences Mode ---
                    if (QuestMenuState.IsInRewardPrefsMode)
                    {
                        if (key == KeyCode.UpArrow)
                        {
                            QuestMenuState.RewardPrefsPrevious();
                            handled = true;
                        }
                        else if (key == KeyCode.DownArrow)
                        {
                            QuestMenuState.RewardPrefsNext();
                            handled = true;
                        }
                        else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                        {
                            QuestMenuState.RewardPrefsToggle();
                            handled = true;
                        }
                        else if (key == KeyCode.Tab)
                        {
                            QuestMenuState.ToggleRewardPreferencesMode();
                            handled = true;
                        }
                        else if (key == KeyCode.Escape)
                        {
                            QuestMenuState.ToggleRewardPreferencesMode();
                            handled = true;
                        }
                        else if (key == KeyCode.Home)
                        {
                            QuestMenuState.RewardPrefsJumpToFirst();
                            handled = true;
                        }
                        else if (key == KeyCode.End)
                        {
                            QuestMenuState.RewardPrefsJumpToLast();
                            handled = true;
                        }
                    }
                    // --- Quest Detail Mode ---
                    else if (QuestMenuState.IsInDetailView)
                    {
                        if (key == KeyCode.UpArrow)
                        {
                            QuestMenuState.SelectPreviousDetail();
                            handled = true;
                        }
                        else if (key == KeyCode.DownArrow)
                        {
                            QuestMenuState.SelectNextDetail();
                            handled = true;
                        }
                        else if (key == KeyCode.LeftArrow)
                        {
                            if (QuestMenuState.IsInButtonsSection)
                                QuestMenuState.SelectPreviousButton();
                            handled = true;
                        }
                        else if (key == KeyCode.RightArrow)
                        {
                            if (QuestMenuState.IsInButtonsSection)
                                QuestMenuState.SelectNextButton();
                            handled = true;
                        }
                        else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                        {
                            if (QuestMenuState.IsInButtonsSection)
                                QuestMenuState.ActivateCurrentButton();
                            handled = true;
                        }
                        else if (key == KeyCode.Escape)
                        {
                            QuestMenuState.GoBackToList();
                            handled = true;
                        }
                        else if (key == KeyCode.Home)
                        {
                            QuestMenuState.JumpToDetailStart();
                            handled = true;
                        }
                        else if (key == KeyCode.End)
                        {
                            QuestMenuState.JumpToDetailEnd();
                            handled = true;
                        }
                        else if (key == KeyCode.A && alt)
                        {
                            QuestMenuState.AcceptQuest();
                            handled = true;
                        }
                        else if (key == KeyCode.D && alt)
                        {
                            QuestMenuState.ToggleDismissQuest();
                            handled = true;
                        }
                        else if (key == KeyCode.I && alt)
                        {
                            QuestMenuState.OpenInfoCard();
                            handled = true;
                        }
                    }
                    // --- Quest List Mode ---
                    else
                    {
                        var typeahead = QuestMenuState.Typeahead;

                        if (key == KeyCode.Home)
                        {
                            QuestMenuState.JumpToFirst();
                            handled = true;
                        }
                        else if (key == KeyCode.End)
                        {
                            QuestMenuState.JumpToLast();
                            handled = true;
                        }
                        else if (key == KeyCode.Escape)
                        {
                            if (typeahead.HasActiveSearch)
                            {
                                typeahead.ClearSearchAndAnnounce();
                                QuestMenuState.AnnounceWithSearch();
                            }
                            else
                            {
                                QuestMenuState.Close();
                            }
                            handled = true;
                        }
                        else if (key == KeyCode.Backspace)
                        {
                            QuestMenuState.HandleBackspace();
                            handled = true;
                        }
                        else if (key == KeyCode.DownArrow)
                        {
                            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                            {
                                int newIndex = typeahead.GetNextMatch(QuestMenuState.CurrentIndex);
                                if (newIndex >= 0)
                                {
                                    QuestMenuState.SetCurrentIndex(newIndex);
                                    QuestMenuState.AnnounceWithSearch();
                                }
                            }
                            else
                            {
                                QuestMenuState.SelectNext();
                            }
                            handled = true;
                        }
                        else if (key == KeyCode.UpArrow)
                        {
                            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                            {
                                int newIndex = typeahead.GetPreviousMatch(QuestMenuState.CurrentIndex);
                                if (newIndex >= 0)
                                {
                                    QuestMenuState.SetCurrentIndex(newIndex);
                                    QuestMenuState.AnnounceWithSearch();
                                }
                            }
                            else
                            {
                                QuestMenuState.SelectPrevious();
                            }
                            handled = true;
                        }
                        else if (key == KeyCode.RightArrow)
                        {
                            QuestMenuState.NextTab();
                            handled = true;
                        }
                        else if (key == KeyCode.LeftArrow)
                        {
                            QuestMenuState.PreviousTab();
                            handled = true;
                        }
                        else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                        {
                            QuestMenuState.EnterDetailView();
                            handled = true;
                        }
                        else if (key == KeyCode.A && alt)
                        {
                            QuestMenuState.AcceptQuest();
                            handled = true;
                        }
                        else if (key == KeyCode.D && alt)
                        {
                            QuestMenuState.ToggleDismissQuest();
                            handled = true;
                        }
                        else if (key == KeyCode.Tab)
                        {
                            QuestMenuState.ToggleRewardPreferencesMode();
                            handled = true;
                        }
                    }

                    if (handled)
                    {
                        Event.current.Use();
                        return;
                    }

                    // Handle * key - consume to prevent passthrough
                    bool isStarKey = key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8);
                    if (isStarKey)
                    {
                        Event.current.Use();
                        return;
                    }

                    // Handle typeahead characters (only in quest list mode)
                    if (!QuestMenuState.IsInDetailView && !QuestMenuState.IsInRewardPrefsMode)
                    {
                        bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                        bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                        if ((isLetter || isNumber) && !KeyboardHelper.IsAltHeld)
                        {
                            Event.current.Use();
                            return;
                        }
                    }
                }
            }

            // ===== PRIORITY 4.73: Handle wildlife menu if active =====
            if (WildlifeMenuState.IsActive)
            {
                bool handled = false;
                var typeahead = WildlifeMenuState.Typeahead;

                // Handle Ctrl+Shift+Home/End - paint entire column
                if ((key == KeyCode.Home || key == KeyCode.End) && Event.current.control && Event.current.shift)
                {
                    WildlifeMenuState.PaintEntireColumn(key == KeyCode.Home);
                    handled = true;
                }
                // Handle Shift+Home - bulk paint to first
                else if (key == KeyCode.Home && Event.current.shift)
                {
                    WildlifeMenuState.PaintToFirst();
                    handled = true;
                }
                // Handle Shift+End - bulk paint to last
                else if (key == KeyCode.End && Event.current.shift)
                {
                    WildlifeMenuState.PaintToLast();
                    handled = true;
                }
                // Handle Home - jump to first
                else if (key == KeyCode.Home)
                {
                    WildlifeMenuState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to last
                else if (key == KeyCode.End)
                {
                    WildlifeMenuState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then close
                else if (key == KeyCode.Escape)
                {
                    if (typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearchAndAnnounce();
                        WildlifeMenuState.AnnounceWithSearch();
                        handled = true;
                    }
                    else
                    {
                        WildlifeMenuState.Close();
                        handled = true;
                    }
                }
                // Handle Backspace for search
                else if (key == KeyCode.Backspace)
                {
                    WildlifeMenuState.HandleBackspace();
                    handled = true;
                }
                // Handle Shift+Down - paint to next row
                else if (key == KeyCode.DownArrow && Event.current.shift)
                {
                    WildlifeMenuState.PaintDown();
                    handled = true;
                }
                // Handle Shift+Up - paint to previous row
                else if (key == KeyCode.UpArrow && Event.current.shift)
                {
                    WildlifeMenuState.PaintUp();
                    handled = true;
                }
                // Handle Down arrow - navigate animals (use typeahead if active with matches)
                else if (key == KeyCode.DownArrow)
                {
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        int newIndex = typeahead.GetNextMatch(WildlifeMenuState.CurrentAnimalIndex);
                        if (newIndex >= 0)
                        {
                            WildlifeMenuState.SetCurrentAnimalIndex(newIndex);
                            WildlifeMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        WildlifeMenuState.SelectNextAnimal();
                    }
                    handled = true;
                }
                // Handle Up arrow - navigate animals (use typeahead if active with matches)
                else if (key == KeyCode.UpArrow)
                {
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        int newIndex = typeahead.GetPreviousMatch(WildlifeMenuState.CurrentAnimalIndex);
                        if (newIndex >= 0)
                        {
                            WildlifeMenuState.SetCurrentAnimalIndex(newIndex);
                            WildlifeMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        WildlifeMenuState.SelectPreviousAnimal();
                    }
                    handled = true;
                }
                // Handle Right arrow - navigate columns
                else if (key == KeyCode.RightArrow)
                {
                    WildlifeMenuState.SelectNextColumn();
                    handled = true;
                }
                // Handle Left arrow - navigate columns
                else if (key == KeyCode.LeftArrow)
                {
                    WildlifeMenuState.SelectPreviousColumn();
                    handled = true;
                }
                // Handle Enter - confirm typeahead search if active, otherwise interact with current cell
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    if (typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearch();
                        WildlifeMenuState.AnnounceWithSearch();
                    }
                    else
                    {
                        WildlifeMenuState.InteractWithCurrentCell();
                    }
                    handled = true;
                }
                // Handle Alt+S - sort by current column
                else if (key == KeyCode.S && KeyboardHelper.IsAltHeld)
                {
                    WildlifeMenuState.ToggleSortByCurrentColumn();
                    handled = true;
                }

                // Handle Alt+I - open info card for selected animal
                if (KeyboardHelper.IsAltHeld && key == KeyCode.I)
                {
                    WildlifeMenuState.OpenInfoCard();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }

                // Handle typeahead characters
                // Request layout-aware character for typeahead (supports non-Latin keyboards)
                // Skip if Alt is held - Alt+key combos are shortcuts, not search input
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if ((isLetter || isNumber) && !KeyboardHelper.IsAltHeld)
                {
                    Event.current.Use();
                    return;
                }

                // Consume other keys to prevent passthrough
                Event.current.Use();
                return;
            }

            // ===== PRIORITY 4.74: Handle animals menu if active =====
            // Skip if placement mode is active (e.g., after Manage Areas → Expand Area)
            // Skip if dialog is active (e.g., Rename Area dialog)
            if (AnimalsMenuState.IsActive && !ShapePlacementState.IsActive && !ViewingModeState.IsActive &&
                !WindowlessDialogState.IsActive)
            {
                bool handled = false;
                var typeahead = AnimalsMenuState.Typeahead;

                // Check if in submenu
                if (AnimalsMenuState.IsInSubmenu)
                {
                    var submenuTypeahead = AnimalsMenuState.SubmenuTypeahead;

                    // Handle Escape - clear search FIRST, then close submenu
                    if (key == KeyCode.Escape)
                    {
                        if (submenuTypeahead.HasActiveSearch)
                        {
                            submenuTypeahead.ClearSearchAndAnnounce();
                            AnimalsMenuState.AnnounceSubmenuWithSearch();
                        }
                        else
                        {
                            AnimalsMenuState.SubmenuCancel();
                        }
                        handled = true;
                    }
                    // Handle Backspace for search
                    else if (key == KeyCode.Backspace)
                    {
                        AnimalsMenuState.SubmenuHandleBackspace();
                        handled = true;
                    }
                    // Handle Down arrow (use typeahead if active with matches)
                    else if (key == KeyCode.DownArrow)
                    {
                        if (submenuTypeahead.HasActiveSearch && !submenuTypeahead.HasNoMatches)
                        {
                            int newIndex = submenuTypeahead.GetNextMatch(AnimalsMenuState.SubmenuSelectedIndex);
                            if (newIndex >= 0)
                            {
                                AnimalsMenuState.SetSubmenuSelectedIndex(newIndex);
                                AnimalsMenuState.AnnounceSubmenuWithSearch();
                            }
                        }
                        else
                        {
                            AnimalsMenuState.SubmenuSelectNext();
                        }
                        handled = true;
                    }
                    // Handle Up arrow (use typeahead if active with matches)
                    else if (key == KeyCode.UpArrow)
                    {
                        if (submenuTypeahead.HasActiveSearch && !submenuTypeahead.HasNoMatches)
                        {
                            int newIndex = submenuTypeahead.GetPreviousMatch(AnimalsMenuState.SubmenuSelectedIndex);
                            if (newIndex >= 0)
                            {
                                AnimalsMenuState.SetSubmenuSelectedIndex(newIndex);
                                AnimalsMenuState.AnnounceSubmenuWithSearch();
                            }
                        }
                        else
                        {
                            AnimalsMenuState.SubmenuSelectPrevious();
                        }
                        handled = true;
                    }
                    else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                    {
                        AnimalsMenuState.SubmenuApply();
                        handled = true;
                    }

                    if (handled)
                    {
                        Event.current.Use();
                        return;
                    }

                    // Handle typeahead characters in submenu
                    bool isSubmenuLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isSubmenuNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if (isSubmenuLetter || isSubmenuNumber)
                    {
                        Event.current.Use();
                        return;
                    }

                    // Consume other keys in submenu
                    Event.current.Use();
                    return;
                }

                // Main menu handling
                // Handle Ctrl+Shift+Home/End - paint entire column
                if ((key == KeyCode.Home || key == KeyCode.End) && Event.current.control && Event.current.shift)
                {
                    AnimalsMenuState.PaintEntireColumn(key == KeyCode.Home);
                    handled = true;
                }
                // Handle Shift+Home - bulk paint to first
                else if (key == KeyCode.Home && Event.current.shift)
                {
                    AnimalsMenuState.PaintToFirst();
                    handled = true;
                }
                // Handle Shift+End - bulk paint to last
                else if (key == KeyCode.End && Event.current.shift)
                {
                    AnimalsMenuState.PaintToLast();
                    handled = true;
                }
                // Handle Home - jump to first
                else if (key == KeyCode.Home)
                {
                    AnimalsMenuState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to last
                else if (key == KeyCode.End)
                {
                    AnimalsMenuState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then close
                else if (key == KeyCode.Escape)
                {
                    if (typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearchAndAnnounce();
                        AnimalsMenuState.AnnounceWithSearch();
                        handled = true;
                    }
                    else
                    {
                        AnimalsMenuState.Close();
                        handled = true;
                    }
                }
                // Handle Backspace for search
                else if (key == KeyCode.Backspace)
                {
                    AnimalsMenuState.HandleBackspace();
                    handled = true;
                }
                // Handle Shift+Down - paint current cell value to next row
                else if (key == KeyCode.DownArrow && Event.current.shift)
                {
                    AnimalsMenuState.PaintDown();
                    handled = true;
                }
                // Handle Shift+Up - paint current cell value to previous row
                else if (key == KeyCode.UpArrow && Event.current.shift)
                {
                    AnimalsMenuState.PaintUp();
                    handled = true;
                }
                // Handle Down arrow - navigate animals (use typeahead if active with matches)
                else if (key == KeyCode.DownArrow)
                {
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        int newIndex = typeahead.GetNextMatch(AnimalsMenuState.CurrentAnimalIndex);
                        if (newIndex >= 0)
                        {
                            AnimalsMenuState.SetCurrentAnimalIndex(newIndex);
                            AnimalsMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        AnimalsMenuState.SelectNextAnimal();
                    }
                    handled = true;
                }
                // Handle Up arrow - navigate animals (use typeahead if active with matches)
                else if (key == KeyCode.UpArrow)
                {
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        int newIndex = typeahead.GetPreviousMatch(AnimalsMenuState.CurrentAnimalIndex);
                        if (newIndex >= 0)
                        {
                            AnimalsMenuState.SetCurrentAnimalIndex(newIndex);
                            AnimalsMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        AnimalsMenuState.SelectPreviousAnimal();
                    }
                    handled = true;
                }
                // Handle Right arrow - navigate columns
                else if (key == KeyCode.RightArrow)
                {
                    AnimalsMenuState.SelectNextColumn();
                    handled = true;
                }
                // Handle Left arrow - navigate columns
                else if (key == KeyCode.LeftArrow)
                {
                    AnimalsMenuState.SelectPreviousColumn();
                    handled = true;
                }
                // Handle Enter - confirm typeahead search if active, otherwise interact with current cell
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    if (typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearch();
                        AnimalsMenuState.AnnounceWithSearch();
                    }
                    else
                    {
                        AnimalsMenuState.InteractWithCurrentCell();
                    }
                    handled = true;
                }
                // Handle Alt+S - sort by current column
                else if (key == KeyCode.S && KeyboardHelper.IsAltHeld)
                {
                    AnimalsMenuState.ToggleSortByCurrentColumn();
                    handled = true;
                }
                // Handle Tab - open auto-slaughter settings
                else if (key == KeyCode.Tab)
                {
                    if (Find.CurrentMap != null)
                    {
                        Find.WindowStack.Add(new RimWorld.Dialog_AutoSlaughter(Find.CurrentMap));
                    }
                    handled = true;
                }
                // Handle Alt+I - open info card for selected animal
                else if (KeyboardHelper.IsAltHeld && key == KeyCode.I)
                {
                    AnimalsMenuState.OpenInfoCard();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }

                // Handle typeahead characters
                // Request layout-aware character for typeahead (supports non-Latin keyboards)
                // Skip if Alt is held - Alt+key combos are shortcuts, not search input
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if ((isLetter || isNumber) && !KeyboardHelper.IsAltHeld)
                {
                    Event.current.Use();
                    return;
                }

                // Consume other keys to prevent passthrough
                Event.current.Use();
                return;
            }

            // ===== PRIORITY 4.741: Handle mechs menu if active =====
            // Skip if placement mode is active (e.g., after Manage Areas → Expand Area)
            // Skip if dialog is active (e.g., Rename Area dialog)
            if (MechsMenuState.IsActive && !ShapePlacementState.IsActive && !ViewingModeState.IsActive &&
                !WindowlessDialogState.IsActive)
            {
                bool handled = false;
                var typeahead = MechsMenuState.Typeahead;

                // Check if in submenu
                if (MechsMenuState.IsInSubmenu)
                {
                    var submenuTypeahead = MechsMenuState.SubmenuTypeahead;

                    if (key == KeyCode.Escape)
                    {
                        if (submenuTypeahead.HasActiveSearch)
                        {
                            submenuTypeahead.ClearSearchAndAnnounce();
                            MechsMenuState.AnnounceSubmenuWithSearch();
                        }
                        else
                        {
                            MechsMenuState.SubmenuCancel();
                        }
                        handled = true;
                    }
                    else if (key == KeyCode.Backspace)
                    {
                        MechsMenuState.SubmenuHandleBackspace();
                        handled = true;
                    }
                    else if (key == KeyCode.DownArrow)
                    {
                        if (submenuTypeahead.HasActiveSearch && !submenuTypeahead.HasNoMatches)
                        {
                            int newIndex = submenuTypeahead.GetNextMatch(MechsMenuState.SubmenuSelectedIndex);
                            if (newIndex >= 0)
                            {
                                MechsMenuState.SetSubmenuSelectedIndex(newIndex);
                                MechsMenuState.AnnounceSubmenuWithSearch();
                            }
                        }
                        else
                        {
                            MechsMenuState.SubmenuSelectNext();
                        }
                        handled = true;
                    }
                    else if (key == KeyCode.UpArrow)
                    {
                        if (submenuTypeahead.HasActiveSearch && !submenuTypeahead.HasNoMatches)
                        {
                            int newIndex = submenuTypeahead.GetPreviousMatch(MechsMenuState.SubmenuSelectedIndex);
                            if (newIndex >= 0)
                            {
                                MechsMenuState.SetSubmenuSelectedIndex(newIndex);
                                MechsMenuState.AnnounceSubmenuWithSearch();
                            }
                        }
                        else
                        {
                            MechsMenuState.SubmenuSelectPrevious();
                        }
                        handled = true;
                    }
                    else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                    {
                        MechsMenuState.SubmenuApply();
                        handled = true;
                    }

                    if (handled)
                    {
                        Event.current.Use();
                        return;
                    }

                    bool isSubmenuLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isSubmenuNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if (isSubmenuLetter || isSubmenuNumber)
                    {
                        Event.current.Use();
                        return;
                    }

                    Event.current.Use();
                    return;
                }

                // Main menu handling
                if ((key == KeyCode.Home || key == KeyCode.End) && Event.current.control && Event.current.shift)
                {
                    MechsMenuState.PaintEntireColumn(key == KeyCode.Home);
                    handled = true;
                }
                else if (key == KeyCode.Home && Event.current.shift)
                {
                    MechsMenuState.PaintToFirst();
                    handled = true;
                }
                else if (key == KeyCode.End && Event.current.shift)
                {
                    MechsMenuState.PaintToLast();
                    handled = true;
                }
                else if (key == KeyCode.Home)
                {
                    MechsMenuState.JumpToFirst();
                    handled = true;
                }
                else if (key == KeyCode.End)
                {
                    MechsMenuState.JumpToLast();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    if (typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearchAndAnnounce();
                        MechsMenuState.AnnounceWithSearch();
                        handled = true;
                    }
                    else
                    {
                        MechsMenuState.Close();
                        handled = true;
                    }
                }
                else if (key == KeyCode.Backspace)
                {
                    MechsMenuState.HandleBackspace();
                    handled = true;
                }
                else if (key == KeyCode.DownArrow && Event.current.shift)
                {
                    MechsMenuState.PaintDown();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow && Event.current.shift)
                {
                    MechsMenuState.PaintUp();
                    handled = true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        int newIndex = typeahead.GetNextMatch(MechsMenuState.CurrentMechIndex);
                        if (newIndex >= 0)
                        {
                            MechsMenuState.SetCurrentMechIndex(newIndex);
                            MechsMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        MechsMenuState.SelectNextMech();
                    }
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        int newIndex = typeahead.GetPreviousMatch(MechsMenuState.CurrentMechIndex);
                        if (newIndex >= 0)
                        {
                            MechsMenuState.SetCurrentMechIndex(newIndex);
                            MechsMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        MechsMenuState.SelectPreviousMech();
                    }
                    handled = true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    MechsMenuState.SelectNextColumn();
                    handled = true;
                }
                else if (key == KeyCode.LeftArrow)
                {
                    MechsMenuState.SelectPreviousColumn();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    if (typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearch();
                        MechsMenuState.AnnounceWithSearch();
                    }
                    else
                    {
                        MechsMenuState.InteractWithCurrentCell();
                    }
                    handled = true;
                }
                else if (key == KeyCode.S && KeyboardHelper.IsAltHeld)
                {
                    MechsMenuState.ToggleSortByCurrentColumn();
                    handled = true;
                }
                else if (KeyboardHelper.IsAltHeld && key == KeyCode.I)
                {
                    MechsMenuState.OpenInfoCard();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }

                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if ((isLetter || isNumber) && !KeyboardHelper.IsAltHeld)
                {
                    Event.current.Use();
                    return;
                }

                Event.current.Use();
                return;
            }

            // ===== PRIORITY 4.745: Handle scanner search (Z key activates, letters go to buffer) =====
            // Z key activates search; when search is active, letter keys filter items
            // Works during placement mode (architect build or designator from gizmos)
            // Also works during world gen (WorldNavContext.WorldGen) for world scanner search
            if (Current.ProgramState == ProgramState.Playing ||
                WorldNavigationState.Context == WorldNavContext.WorldGen)
            {
                bool onWorldMap = WorldNavigationState.IsActive;
                bool onMap = MapNavigationState.IsInitialized && !onWorldMap;
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                // Check placement mode (search should work during placement)
                // Guard Find.DesignatorManager with CurrentMap — it chains through Find.MapUI which throws during Entry state
                bool inPlacementMode = ArchitectState.IsInPlacementMode ||
                    ViewingModeState.IsActive ||
                    ShapePlacementState.IsActive ||
                    (Find.CurrentMap != null && Find.DesignatorManager != null && Find.DesignatorManager.SelectedDesignator != null);

                // Determine if search should be allowed
                // Allow search when: on map/world AND (no blocking menus OR in placement mode)
                bool menuBlocksSearch = KeyboardHelper.IsAnyAccessibilityMenuActive() && !inPlacementMode;

                if ((onMap || onWorldMap) && !menuBlocksSearch &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion))
                {
                    // Z key activates search (no modifiers) when search is not active
                    // Text input when search IS active is handled by the early handler at priority -0.2
                    // Also check GoToState.IsActive for mutual exclusion
                    if (key == KeyCode.Z && !shift && !ctrl && !alt && !ScannerSearchState.IsActive && !GoToState.IsActive)
                    {
                        ScannerSearchState.Activate(onWorldMap);
                        // Block RimWorld's keybinding system from seeing this key
                        Event.current.keyCode = KeyCode.None;
                        Event.current.Use();
                        return;
                    }

                    // Ctrl+Z clears the active search filter (removes search category only)
                    if (key == KeyCode.Z && ctrl && !shift && !alt && !ScannerSearchState.IsActive && ScannerSearchState.HasActiveFilter)
                    {
                        ScannerSearchState.ClearActiveFilter();
                        Event.current.Use();
                        return;
                    }
                }
            }

            // ===== PRIORITY 4.746: Ctrl+G to activate Go To coordinate input =====
            // Only works on local map (not world map)
            if (Current.ProgramState == ProgramState.Playing)
            {
                bool onWorldMap = WorldNavigationState.IsActive;
                bool onMap = MapNavigationState.IsInitialized && !onWorldMap;
                bool ctrl = Event.current.control;
                bool shift = Event.current.shift;
                bool alt = KeyboardHelper.IsAltHeld;

                // Check placement mode (Go To should work during placement like scanner search)
                // Guard Find.DesignatorManager with CurrentMap — it chains through Find.MapUI which throws during Entry state
                bool inPlacementMode = ArchitectState.IsInPlacementMode ||
                    ViewingModeState.IsActive ||
                    ShapePlacementState.IsActive ||
                    (Find.CurrentMap != null && Find.DesignatorManager != null && Find.DesignatorManager.SelectedDesignator != null);

                // Determine if Go To should be allowed
                bool menuBlocksGoTo = KeyboardHelper.IsAnyAccessibilityMenuActive() && !inPlacementMode;

                if (onMap && !onWorldMap && key == KeyCode.G && ctrl && !shift && !alt)
                {
                    // Don't activate if scanner search is active (mutual exclusion)
                    // Don't activate if Go To is already active
                    if (!ScannerSearchState.IsActive && !GoToState.IsActive && !menuBlocksGoTo &&
                        (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion))
                    {
                        GoToState.Activate();
                        // Block RimWorld's keybinding system from seeing this key
                        Event.current.keyCode = KeyCode.None;
                        Event.current.Use();
                        return;
                    }
                }
            }

            // ===== PRIORITY 4.75: Handle scanner keys (always available during map navigation) =====
            // Only process scanner keys if in gameplay with map navigation initialized
            // IMPORTANT: Don't process scanner keys when any accessibility menu is active,
            // EXCEPT during placement mode (architect build or designator from gizmos)
            if (Current.ProgramState == ProgramState.Playing &&
                Find.CurrentMap != null &&
                MapNavigationState.IsInitialized &&
                (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                !ZoneCreationState.IsInCreationMode)
            {
                // Check placement mode here (after verifying we're in gameplay)
                bool inPlacementMode = ArchitectState.IsInPlacementMode ||
                    ViewingModeState.IsActive ||
                    ShapePlacementState.IsActive ||
                    (Find.DesignatorManager != null && Find.DesignatorManager.SelectedDesignator != null);

                if (!KeyboardHelper.IsAnyAccessibilityMenuActive() || inPlacementMode)
                {
                    // Ensure substructure overlay scanner category is in sync before scanner navigation
                    SubstructureOverlayState.CheckOverlayState();

                    bool handled = false;
                    bool ctrl = Event.current.control;
                    bool shift = Event.current.shift;
                    bool alt = KeyboardHelper.IsAltHeld;

                    if (key == KeyCode.PageDown)
                    {
                        if (alt)
                        {
                            ScannerState.NextBulkItem();
                        }
                        else if (ctrl)
                        {
                            ScannerState.NextCategory();
                        }
                        else if (shift)
                        {
                            ScannerState.NextSubcategory();
                        }
                        else
                        {
                            ScannerState.NextItem();
                        }
                        handled = true;
                    }
                    else if (key == KeyCode.PageUp)
                    {
                        if (alt)
                        {
                            ScannerState.PreviousBulkItem();
                        }
                        else if (ctrl)
                        {
                            ScannerState.PreviousCategory();
                        }
                        else if (shift)
                        {
                            ScannerState.PreviousSubcategory();
                        }
                        else
                        {
                            ScannerState.PreviousItem();
                        }
                        handled = true;
                    }
                    else if (key == KeyCode.Home)
                    {
                        if (alt)
                        {
                            // Alt+Home: Toggle auto-jump mode
                            ScannerState.ToggleAutoJumpMode();
                        }
                        else
                        {
                            // Home: Jump to current item
                            ScannerState.JumpToCurrent();
                        }
                        handled = true;
                    }
                    else if (key == KeyCode.End)
                    {
                        ScannerState.ReadDistanceAndDirection();
                        handled = true;
                    }

                    if (handled)
                    {
                        Event.current.Use();
                        return;
                    }
                }
            }

            // ===== PRIORITY 4.77: Handle notification menu if active =====
            if (NotificationMenuState.IsActive)
            {
                bool handled = false;
                var typeahead = NotificationMenuState.Typeahead;

                // Handle Home - jump to start of detail view or first item in list
                if (key == KeyCode.Home)
                {
                    if (NotificationMenuState.IsInDetailView)
                        NotificationMenuState.JumpToDetailStart();
                    else
                        NotificationMenuState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to end of detail view (buttons) or last item in list
                else if (key == KeyCode.End)
                {
                    if (NotificationMenuState.IsInDetailView)
                        NotificationMenuState.JumpToDetailEnd();
                    else
                        NotificationMenuState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then go back (detail->list) or close menu
                else if (key == KeyCode.Escape)
                {
                    if (typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearchAndAnnounce();
                        NotificationMenuState.AnnounceWithSearch();
                        handled = true;
                    }
                    else
                    {
                        // HandleEscape goes back from detail view, or closes menu from list view
                        NotificationMenuState.HandleEscape();
                        handled = true;
                    }
                }
                // Handle Backspace for search (only in list view)
                else if (key == KeyCode.Backspace && !NotificationMenuState.IsInDetailView)
                {
                    NotificationMenuState.HandleBackspace();
                    handled = true;
                }
                // Handle Down arrow - navigate notification list or detail view
                else if (key == KeyCode.DownArrow)
                {
                    // Typeahead search only works in list view
                    if (!NotificationMenuState.IsInDetailView && typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        int newIndex = typeahead.GetNextMatch(NotificationMenuState.CurrentIndex);
                        if (newIndex >= 0)
                        {
                            NotificationMenuState.SetCurrentIndex(newIndex);
                            NotificationMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        // Navigate normally (list view without search, or detail view)
                        NotificationMenuState.SelectNext();
                    }
                    handled = true;
                }
                // Handle Up arrow - navigate notification list or detail view
                else if (key == KeyCode.UpArrow)
                {
                    // Typeahead search only works in list view
                    if (!NotificationMenuState.IsInDetailView && typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        int newIndex = typeahead.GetPreviousMatch(NotificationMenuState.CurrentIndex);
                        if (newIndex >= 0)
                        {
                            NotificationMenuState.SetCurrentIndex(newIndex);
                            NotificationMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        // Navigate normally (list view without search, or detail view)
                        NotificationMenuState.SelectPrevious();
                    }
                    handled = true;
                }
                // Handle Left arrow - navigate to previous button
                else if (key == KeyCode.LeftArrow)
                {
                    NotificationMenuState.SelectPreviousButton();
                    handled = true;
                }
                // Handle Right arrow - navigate to next button
                else if (key == KeyCode.RightArrow)
                {
                    NotificationMenuState.SelectNextButton();
                    handled = true;
                }
                // Handle Enter - open detail view or activate button
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    if (!NotificationMenuState.IsInDetailView)
                    {
                        // In list view, Enter opens detail view
                        NotificationMenuState.EnterDetailView();
                    }
                    else if (NotificationMenuState.IsInButtonsSection)
                    {
                        // In detail view and on a button, Enter activates it
                        NotificationMenuState.ActivateCurrentButton();
                    }
                    // In detail view but not on a button, do nothing (continue navigating with arrows)
                    handled = true;
                }
                // Handle ] (right bracket) - delete letter (acts as right-click per mod convention)
                else if (key == KeyCode.RightBracket)
                {
                    NotificationMenuState.DeleteSelected();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }

                // Handle typeahead characters
                // Handle * key - consume to prevent passthrough
                bool isStarKey = key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8);
                if (isStarKey)
                {
                    Event.current.Use();
                    return;
                }

                // Handle typeahead characters for search (only in list view)
                // Skip if Alt is held - Alt+key combos are shortcuts, not search input
                if (!NotificationMenuState.IsInDetailView)
                {
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if ((isLetter || isNumber) && !KeyboardHelper.IsAltHeld)
                    {
                        Event.current.Use();
                        return;
                    }
                }
            }

            // ===== PRIORITY 4.772: Handle learning helper menu if active =====
            if (LearningHelperState.IsActive)
            {
                bool handled = false;
                var typeahead = LearningHelperState.Typeahead;

                // Handle Home - jump to start of detail view or first item in list
                if (key == KeyCode.Home)
                {
                    if (LearningHelperState.IsInDetailView)
                        LearningHelperState.JumpToDetailStart();
                    else
                        LearningHelperState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to end of detail view (buttons) or last item in list
                else if (key == KeyCode.End)
                {
                    if (LearningHelperState.IsInDetailView)
                        LearningHelperState.JumpToDetailEnd();
                    else
                        LearningHelperState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then go back (detail->list) or close menu
                else if (key == KeyCode.Escape)
                {
                    if (typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearchAndAnnounce();
                        LearningHelperState.AnnounceWithSearch();
                        handled = true;
                    }
                    else
                    {
                        LearningHelperState.HandleEscape();
                        handled = true;
                    }
                }
                // Handle Tab - toggle between active/all modes
                else if (key == KeyCode.Tab)
                {
                    LearningHelperState.ToggleMode();
                    handled = true;
                }
                // Handle Backspace for search (only in list view, all mode only)
                else if (key == KeyCode.Backspace && !LearningHelperState.IsInDetailView && LearningHelperState.ShowAllMode)
                {
                    LearningHelperState.HandleBackspace();
                    handled = true;
                }
                // Handle Down arrow - navigate list or detail view
                else if (key == KeyCode.DownArrow)
                {
                    if (!LearningHelperState.IsInDetailView && LearningHelperState.ShowAllMode &&
                        typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        int newIndex = typeahead.GetNextMatch(LearningHelperState.CurrentIndex);
                        if (newIndex >= 0)
                        {
                            LearningHelperState.SetCurrentIndex(newIndex);
                            LearningHelperState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        LearningHelperState.SelectNext();
                    }
                    handled = true;
                }
                // Handle Up arrow - navigate list or detail view
                else if (key == KeyCode.UpArrow)
                {
                    if (!LearningHelperState.IsInDetailView && LearningHelperState.ShowAllMode &&
                        typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        int newIndex = typeahead.GetPreviousMatch(LearningHelperState.CurrentIndex);
                        if (newIndex >= 0)
                        {
                            LearningHelperState.SetCurrentIndex(newIndex);
                            LearningHelperState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        LearningHelperState.SelectPrevious();
                    }
                    handled = true;
                }
                // Handle Left arrow - navigate to previous button
                else if (key == KeyCode.LeftArrow)
                {
                    LearningHelperState.SelectPreviousButton();
                    handled = true;
                }
                // Handle Right arrow - navigate to next button
                else if (key == KeyCode.RightArrow)
                {
                    LearningHelperState.SelectNextButton();
                    handled = true;
                }
                // Handle Enter - open detail view or activate button
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    if (!LearningHelperState.IsInDetailView)
                    {
                        LearningHelperState.EnterDetailView();
                    }
                    else if (LearningHelperState.IsInButtonsSection)
                    {
                        LearningHelperState.ActivateCurrentButton();
                    }
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }

                // Handle typeahead characters for search (only in list view, all mode)
                if (!LearningHelperState.IsInDetailView && LearningHelperState.ShowAllMode)
                {
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if ((isLetter || isNumber) && !Event.current.alt)
                    {
                        Event.current.Use();
                        return;
                    }
                }

                // Consume all other keys to prevent leakage
                Event.current.Use();
                return;
            }

            // ===== PRIORITY 4.776: Handle policy content editor if active =====
            if (PolicyEditorState.IsActive && !WindowlessDialogState.IsActive)
            {
                bool handled = false;

                if (ReadingPolicyEditorState.IsActive)
                {
                    // Reading policy: Tab/Shift+Tab switches panels
                    if (key == KeyCode.Tab)
                    {
                        ReadingPolicyEditorState.SwitchPanel();
                        handled = true;
                    }
                    else if (key == KeyCode.Escape)
                    {
                        if (ThingFilterNavigationState.IsActive && ThingFilterNavigationState.IsEditingSlider)
                        {
                            ThingFilterNavigationState.ExitSliderEdit();
                            handled = true;
                        }
                        else if (ThingFilterNavigationState.IsActive && ThingFilterNavigationState.HasActiveSearch)
                        {
                            ThingFilterNavigationState.ClearTypeaheadSearch();
                            handled = true;
                        }
                        else
                        {
                            PolicyEditorState.Close();
                            handled = true;
                        }
                    }
                    else if (ThingFilterNavigationState.IsActive)
                    {
                        handled = HandleThingFilterInput(key);
                    }
                }
                else if (DrugPolicyEditorState.IsActive)
                {
                    handled = HandleDrugEditorInput(key);
                }
                else if (ThingFilterNavigationState.IsActive)
                {
                    // Apparel/Food filter editing
                    if (key == KeyCode.Escape)
                    {
                        if (ThingFilterNavigationState.IsEditingSlider)
                        {
                            ThingFilterNavigationState.ExitSliderEdit();
                            handled = true;
                        }
                        else if (ThingFilterNavigationState.HasActiveSearch)
                        {
                            ThingFilterNavigationState.ClearTypeaheadSearch();
                            handled = true;
                        }
                        else
                        {
                            PolicyEditorState.Close();
                            handled = true;
                        }
                    }
                    else
                    {
                        handled = HandleThingFilterInput(key);
                    }
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }

                // Consume unhandled keys to prevent passthrough
                Event.current.Use();
                return;
            }

            // ===== PRIORITY 4.778: Handle assign menu if active =====
            // Skip if float menu is open (e.g., ] context menu for policies)
            // Skip if dialog is active (e.g., Rename Policy dialog)
            if (AssignMenuState.IsActive && !WindowlessFloatMenuState.IsActive &&
                !WindowlessDialogState.IsActive)
            {
                bool handled = false;
                var typeahead = AssignMenuState.Typeahead;

                // Policy shortcuts (work in both table and submenu when on policy column)
                if (KeyboardHelper.IsAltHeld && key == KeyCode.N)
                    handled = AssignMenuState.HandlePolicyShortcut(AssignMenuHelper.PolicyAction.New);
                else if (KeyboardHelper.IsAltHeld && key == KeyCode.R)
                    handled = AssignMenuState.HandlePolicyShortcut(AssignMenuHelper.PolicyAction.Rename);
                else if (KeyboardHelper.IsAltHeld && key == KeyCode.C)
                    handled = AssignMenuState.HandlePolicyShortcut(AssignMenuHelper.PolicyAction.Copy);
                else if (KeyboardHelper.IsAltHeld && key == KeyCode.E)
                    handled = AssignMenuState.HandlePolicyShortcut(AssignMenuHelper.PolicyAction.Edit);
                else if (key == KeyCode.Delete)
                    handled = AssignMenuState.HandlePolicyShortcut(AssignMenuHelper.PolicyAction.Delete);

                // Check if in submenu
                if (AssignMenuState.IsInSubmenu)
                {
                    var submenuTypeahead = AssignMenuState.SubmenuTypeahead;

                    // Handle Escape - clear search FIRST, then close submenu
                    if (key == KeyCode.Escape)
                    {
                        if (submenuTypeahead.HasActiveSearch)
                        {
                            submenuTypeahead.ClearSearchAndAnnounce();
                            AssignMenuState.AnnounceSubmenuOption();
                        }
                        else
                        {
                            AssignMenuState.SubmenuCancel();
                        }
                        handled = true;
                    }
                    // Handle Backspace for search
                    else if (key == KeyCode.Backspace)
                    {
                        AssignMenuState.SubmenuHandleBackspace();
                        handled = true;
                    }
                    // Handle Down arrow (use typeahead if active with matches)
                    else if (key == KeyCode.DownArrow)
                    {
                        if (submenuTypeahead.HasActiveSearch && !submenuTypeahead.HasNoMatches)
                        {
                            int newIndex = submenuTypeahead.GetNextMatch(AssignMenuState.SubmenuSelectedIndex);
                            if (newIndex >= 0)
                            {
                                AssignMenuState.SetSubmenuSelectedIndex(newIndex);
                                AssignMenuState.AnnounceSubmenuOption();
                            }
                        }
                        else
                        {
                            AssignMenuState.SubmenuSelectNext();
                        }
                        handled = true;
                    }
                    // Handle Up arrow (use typeahead if active with matches)
                    else if (key == KeyCode.UpArrow)
                    {
                        if (submenuTypeahead.HasActiveSearch && !submenuTypeahead.HasNoMatches)
                        {
                            int newIndex = submenuTypeahead.GetPreviousMatch(AssignMenuState.SubmenuSelectedIndex);
                            if (newIndex >= 0)
                            {
                                AssignMenuState.SetSubmenuSelectedIndex(newIndex);
                                AssignMenuState.AnnounceSubmenuOption();
                            }
                        }
                        else
                        {
                            AssignMenuState.SubmenuSelectPrevious();
                        }
                        handled = true;
                    }
                    else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                    {
                        AssignMenuState.SubmenuApply();
                        handled = true;
                    }
                    else if (key == KeyCode.RightBracket)
                    {
                        AssignMenuState.OpenSubmenuContextMenu();
                        handled = true;
                    }

                    if (handled)
                    {
                        Event.current.Use();
                        return;
                    }

                    // Handle typeahead characters in submenu
                    bool isSubmenuLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isSubmenuNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if (isSubmenuLetter || isSubmenuNumber)
                    {
                        Event.current.Use();
                        return;
                    }

                    // Consume other keys in submenu
                    Event.current.Use();
                    return;
                }

                // Main table handling
                // Handle Ctrl+Shift+Home/End - paint entire column
                if ((key == KeyCode.Home || key == KeyCode.End) && Event.current.control && Event.current.shift)
                {
                    AssignMenuState.PaintEntireColumn(key == KeyCode.Home);
                    handled = true;
                }
                // Handle Shift+Home - bulk paint to first
                else if (key == KeyCode.Home && Event.current.shift)
                {
                    AssignMenuState.PaintToFirst();
                    handled = true;
                }
                // Handle Shift+End - bulk paint to last
                else if (key == KeyCode.End && Event.current.shift)
                {
                    AssignMenuState.PaintToLast();
                    handled = true;
                }
                // Handle Home - jump to first
                else if (key == KeyCode.Home)
                {
                    AssignMenuState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to last
                else if (key == KeyCode.End)
                {
                    AssignMenuState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then close
                else if (key == KeyCode.Escape)
                {
                    if (typeahead != null && typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearchAndAnnounce();
                        AssignMenuState.AnnounceWithSearch();
                        handled = true;
                    }
                    else
                    {
                        AssignMenuState.Close();
                        handled = true;
                    }
                }
                // Handle Backspace for search
                else if (key == KeyCode.Backspace)
                {
                    AssignMenuState.HandleBackspace();
                    handled = true;
                }
                // Handle Shift+Down - paint value to next pawn (BEFORE regular Down)
                else if (key == KeyCode.DownArrow && Event.current.shift)
                {
                    AssignMenuState.PaintDown();
                    handled = true;
                }
                // Handle Shift+Up - paint value to previous pawn (BEFORE regular Up)
                else if (key == KeyCode.UpArrow && Event.current.shift)
                {
                    AssignMenuState.PaintUp();
                    handled = true;
                }
                // Handle Down arrow - navigate pawns (use typeahead if active with matches)
                else if (key == KeyCode.DownArrow)
                {
                    if (typeahead != null && typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        int newIndex = typeahead.GetNextMatch(AssignMenuState.CurrentPawnIndex);
                        if (newIndex >= 0)
                        {
                            AssignMenuState.SetCurrentPawnIndex(newIndex);
                            AssignMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        AssignMenuState.SelectNextPawn();
                    }
                    handled = true;
                }
                // Handle Up arrow - navigate pawns (use typeahead if active with matches)
                else if (key == KeyCode.UpArrow)
                {
                    if (typeahead != null && typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        int newIndex = typeahead.GetPreviousMatch(AssignMenuState.CurrentPawnIndex);
                        if (newIndex >= 0)
                        {
                            AssignMenuState.SetCurrentPawnIndex(newIndex);
                            AssignMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        AssignMenuState.SelectPreviousPawn();
                    }
                    handled = true;
                }
                // Handle Right arrow - navigate columns
                else if (key == KeyCode.RightArrow)
                {
                    AssignMenuState.SelectNextColumn();
                    handled = true;
                }
                // Handle Left arrow - navigate columns
                else if (key == KeyCode.LeftArrow)
                {
                    AssignMenuState.SelectPreviousColumn();
                    handled = true;
                }
                // Handle Enter - interact with current cell
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    AssignMenuState.InteractWithCurrentCell();
                    handled = true;
                }
                // Handle ] (right bracket) - open context menu
                else if (key == KeyCode.RightBracket)
                {
                    AssignMenuState.OpenContextMenu();
                    handled = true;
                }
                // Handle Alt+S - sort by current column
                else if (key == KeyCode.S && KeyboardHelper.IsAltHeld)
                {
                    AssignMenuState.ToggleSortByCurrentColumn();
                    handled = true;
                }
                // Handle Alt+I - open info card for selected pawn
                else if (KeyboardHelper.IsAltHeld && key == KeyCode.I)
                {
                    AssignMenuState.OpenInfoCard();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }

                // Handle typeahead characters
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if ((isLetter || isNumber) && !KeyboardHelper.IsAltHeld)
                {
                    Event.current.Use();
                    return;
                }

                // Consume other keys to prevent passthrough
                Event.current.Use();
                return;
            }

            // ===== PRIORITY 4.7791: Handle storage settings menu typeahead if active =====
            // Note: StorageSettingsMenuPatch handles navigation at higher priority, but letters fall through here
            if (StorageSettingsMenuState.IsActive)
            {
                // Handle Alt+I - open info card for selected item
                if (KeyboardHelper.IsAltHeld && key == KeyCode.I)
                {
                    StorageSettingsMenuState.OpenInfoCard();
                    Event.current.Use();
                    return;
                }

                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if ((isLetter || isNumber) && !KeyboardHelper.IsAltHeld)
                {
                    Event.current.Use();
                    return;
                }

                if (key == KeyCode.Backspace)
                {
                    StorageSettingsMenuState.ProcessBackspace();
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.7793: Handle plant selection menu typeahead if active =====
            if (PlantSelectionMenuState.IsActive)
            {
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if ((isLetter || isNumber) && !KeyboardHelper.IsAltHeld)
                {
                    Event.current.Use();
                    return;
                }

                if (key == KeyCode.Backspace)
                {
                    PlantSelectionMenuState.HandleBackspace();
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.7795: Handle mech control group menu if active =====
            if (MechControlGroupState.IsActive)
            {
                if (MechControlGroupState.HandleInput())
                {
                    return;
                }
            }

            // ===== PRIORITY 4.78: Handle gizmo navigation if active =====
            if (GizmoNavigationState.IsActive && !WindowlessFloatMenuState.IsActive)
            {
                // Let GizmoNavigationState.HandleInput() process all input
                // It handles typeahead-aware navigation, Home/End, Escape, Enter, etc.
                if (GizmoNavigationState.HandleInput())
                {
                    return;
                }

                // HandleInput returns false for Escape when no search is active
                // Handle close explicitly
                if (key == KeyCode.Escape)
                {
                    GizmoNavigationState.Close();
                    TolkHelper.Speak("RimWorldAccess.Input.Close.GizmoMenuClosed".Loc());
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.79: Handle health tab if active =====
            // Must be checked before inspection (4.8) because health tab opens
            // as an overlay while inspection stays active in the background.
            if (HealthTabState.IsActive)
            {
                if (HealthTabState.HandleInput(Event.current))
                {
                    return;
                }
            }

            // ===== PRIORITY 4.8: Handle inspection menu if active =====
            if (WindowlessInspectionState.IsActive)
            {
                if (WindowlessInspectionState.HandleInput(Event.current))
                {
                    return;
                }
            }

            // ===== PRIORITY 4.805: Handle inventory menu if active =====
            // Skip if float menu is open (e.g., right bracket context menu on inventory items)
            if (WindowlessInventoryState.IsActive && !WindowlessFloatMenuState.IsActive)
            {
                if (WindowlessInventoryState.HandleInput(Event.current))
                {
                    return;
                }
            }

            // ===== PRIORITY 4.85: Handle prisoner tab if active =====
            if (PrisonerTabState.IsActive)
            {
                bool handled = false;

                if (key == KeyCode.LeftArrow)
                {
                    PrisonerTabState.PreviousSection();
                    handled = true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    PrisonerTabState.NextSection();
                    handled = true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    PrisonerTabState.NavigateDown();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    PrisonerTabState.NavigateUp();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    PrisonerTabState.ExecuteAction();
                    handled = true;
                }
                else if (key == KeyCode.Space)
                {
                    PrisonerTabState.ToggleCheckbox();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    PrisonerTabState.Close();
                    InspectionReturnHelper.AnnounceParentOrFallback(null);
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
                    if (WindowlessFloatMenuState.HasActiveSearch && !WindowlessFloatMenuState.HasNoMatches)
                    {
                        // Navigate through search matches only
                        WindowlessFloatMenuState.HandleInput();
                    }
                    else
                    {
                        WindowlessFloatMenuState.SelectNext();
                    }
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    if (WindowlessFloatMenuState.HasActiveSearch && !WindowlessFloatMenuState.HasNoMatches)
                    {
                        // Navigate through search matches only
                        WindowlessFloatMenuState.HandleInput();
                    }
                    else
                    {
                        WindowlessFloatMenuState.SelectPrevious();
                    }
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessFloatMenuState.ExecuteSelected();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    // Clear search first if active, otherwise close the menu
                    if (WindowlessFloatMenuState.ClearTypeaheadSearch())
                    {
                        // Search was cleared, don't close the menu
                        handled = true;
                    }
                    else
                    {
                        // No active search, close the menu
                        SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
                        WindowlessFloatMenuState.Close();

                        // If architect mode is active (category/tool/material selection), also reset it
                        if (ArchitectState.IsActive && !ArchitectState.IsInPlacementMode)
                        {
                            ArchitectState.Reset();
                        }

                        // Re-announce context if returning to a known menu
                        if (AssignMenuState.IsActive)
                        {
                            AssignMenuState.AnnounceCurrentCell(includeItemName: false);
                        }
                        else
                        {
                            TolkHelper.Speak("RimWorldAccess.Input.Close.MenuClosed".Loc());
                        }
                        handled = true;
                    }
                }
                // === Handle Home/End for menu navigation ===
                else if (key == KeyCode.Home)
                {
                    WindowlessFloatMenuState.JumpToFirst();
                    handled = true;
                }
                else if (key == KeyCode.End)
                {
                    WindowlessFloatMenuState.JumpToLast();
                    handled = true;
                }
                // === Handle Backspace for typeahead ===
                else if (key == KeyCode.Backspace)
                {
                    WindowlessFloatMenuState.HandleBackspace();
                    handled = true;
                }
                // === Handle Alt+I - open info card for selected item ===
                else if (KeyboardHelper.IsAltHeld && key == KeyCode.I)
                {
                    WindowlessFloatMenuState.TryOpenInfoCardForSelected();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }

                // === Consume ALL alphanumeric + * for typeahead ===
                // This MUST be at the end to catch any unhandled characters
                // Request layout-aware character for typeahead (supports non-Latin keyboards)
                // Skip if Alt is held - Alt+key combos are shortcuts, not search input
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;
                bool isStar = key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8);

                if ((isLetter || isNumber || isStar) && !KeyboardHelper.IsAltHeld)
                {
                    if (isStar)
                    {
                        // Reserved for future "expand all at level" in tree views
                        Event.current.Use();
                        return;
                    }
                    Event.current.Use();
                    return;  // CRITICAL: Don't fall through to T=time, R=draft, etc.
                }
            }

            // ===== PRIORITY 5.45: Handle world map tile info keys 1-5 =====
            // Works during both in-game world map and world gen starting site screen
            if (WorldNavigationState.IsActive &&
                (Current.ProgramState == ProgramState.Playing || WorldNavigationState.Context == WorldNavContext.WorldGen) &&
                !Event.current.shift && !Event.current.control && !KeyboardHelper.IsAltHeld)
            {
                int category = 0;
                if (key == KeyCode.Alpha1 || key == KeyCode.Keypad1) category = 1;
                else if (key == KeyCode.Alpha2 || key == KeyCode.Keypad2) category = 2;
                else if (key == KeyCode.Alpha3 || key == KeyCode.Keypad3) category = 3;
                else if (key == KeyCode.Alpha4 || key == KeyCode.Keypad4) category = 4;
                else if (key == KeyCode.Alpha5 || key == KeyCode.Keypad5) category = 5;

                if (category > 0)
                {
                    WorldNavigationState.AnnounceTileInfoCategory(category);
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 5.5: Handle time control with Shift+1/2/3, intercept 1/2/3 without Shift =====
            // Skip if Alt or Ctrl is held - Alt+number for colonist bar, Ctrl+number for bookmarks
            if ((key == KeyCode.Alpha1 || key == KeyCode.Keypad1 ||
                 key == KeyCode.Alpha2 || key == KeyCode.Keypad2 ||
                 key == KeyCode.Alpha3 || key == KeyCode.Keypad3) &&
                !KeyboardHelper.IsAltHeld &&
                !Event.current.control &&
                Current.ProgramState == ProgramState.Playing &&
                Find.CurrentMap != null &&
                (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion))
            {
                // Don't intercept if any menu is active (keys 1-5 are used for tile info)
                bool anyMenuActive = WorkMenuState.IsActive ||
                                    ShapeSelectionMenuState.IsActive ||
                                    ViewingModeState.IsActive ||
                                    ShapePlacementState.IsActive ||
                                    ArchitectState.IsActive ||
                                    ZoneCreationState.IsInCreationMode ||
                                    NotificationMenuState.IsActive ||
                                    QuestMenuState.IsActive ||
                                    WindowlessFloatMenuState.IsActive ||
                                    WindowlessPauseMenuState.IsActive ||
                                    WindowlessSaveMenuState.IsActive ||
                                    WindowlessOptionsMenuState.IsActive ||
                                    WindowlessConfirmationState.IsActive ||
                                    StorageSettingsMenuState.IsActive ||
                                    PlantSelectionMenuState.IsActive ||
                                    MechControlGroupState.IsActive ||
                                    WindowlessScheduleState.IsActive ||
                                    WindowlessResearchMenuState.IsActive ||
                                    StorytellerSelectionState.IsActive ||
                                    PrisonerTabState.IsActive ||
                                    HealthTabState.IsActive ||
                                    FactionTabState.IsActive ||
                                    IdeologyTabState.IsActive;

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

                        // Note: Announcement is handled by TimeControlAccessibilityPatch
                        // which monitors the CurTimeSpeed setter

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
            if (key == KeyCode.R && !KeyboardHelper.IsAltHeld)
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
                    // Multi-select: draft/undraft using vanilla InheritInteractionsFrom behavior
                    // Only toggles pawns with the SAME draft state as the first pawn
                    if (MultiSelectState.IsMultiSelectActive)
                    {
                        var pawns = Find.Selector.SelectedPawns
                            .Where(p => p.IsColonist && p.drafter != null && p.drafter.ShowDraftGizmo)
                            .ToList();

                        if (pawns.Count > 0)
                        {
                            // Match vanilla InheritInteractionsFrom: only toggle pawns
                            // with the same current state as the first pawn
                            bool firstPawnDrafted = pawns[0].drafter.Drafted;
                            bool newState = !firstPawnDrafted;
                            var toggledNames = new List<string>();

                            foreach (var p in pawns)
                            {
                                if (p.drafter.Drafted == firstPawnDrafted)
                                {
                                    p.drafter.Drafted = newState;
                                    toggledNames.Add(p.LabelShort);
                                }
                            }

                            if (newState)
                                SoundDefOf.DraftOn.PlayOneShotOnCamera();
                            else
                                SoundDefOf.DraftOff.PlayOneShotOnCamera();

                            // Announce using end-state shorter-list logic
                            // Pawns already in desired state count as successes
                            var inDesiredState = pawns.Where(p => p.drafter.Drafted == newState)
                                .Select(p => p.LabelShort).ToList();
                            var notInDesiredState = pawns.Where(p => p.drafter.Drafted != newState)
                                .Select(p => p.LabelShort).ToList();

                            string everyone = ((string)"ConfirmAbandonHomeNegativeThoughts_Everyone".Translate()).TrimEnd(':', ' ');
                            string status = newState
                                ? "RimWorldAccess.Input.Drafting.StatusDrafted".Translate().ToString()
                                : "RimWorldAccess.Input.Drafting.StatusUndrafted".Translate().ToString();

                            if (notInDesiredState.Count == 0)
                                TolkHelper.Speak("RimWorldAccess.Input.Drafting.EveryoneStatus".Loc(everyone, status));
                            else if (notInDesiredState.Count <= inDesiredState.Count)
                            {
                                string exceptNames = MenuHelper.FormatNameList(notInDesiredState);
                                TolkHelper.Speak("RimWorldAccess.Input.Drafting.EveryoneExceptStatus".Loc(everyone, exceptNames, status));
                            }
                            else
                            {
                                string onlyNames = MenuHelper.FormatNameList(inDesiredState);
                                TolkHelper.Speak("RimWorldAccess.Input.Drafting.OnlyStatus".Loc(onlyNames, status));
                            }

                            Event.current.Use();
                            return;
                        }
                    }
                    else
                    {
                        // Single-select: draft/undraft one pawn
                        Pawn selectedPawn = Find.Selector.FirstSelectedObject as Pawn;

                        if (selectedPawn != null &&
                            selectedPawn.IsColonist &&
                            selectedPawn.drafter != null &&
                            selectedPawn.drafter.ShowDraftGizmo)
                        {
                            bool wasDrafted = selectedPawn.drafter.Drafted;
                            selectedPawn.drafter.Drafted = !wasDrafted;

                            if (selectedPawn.drafter.Drafted)
                                SoundDefOf.DraftOn.PlayOneShotOnCamera();
                            else
                                SoundDefOf.DraftOff.PlayOneShotOnCamera();

                            string status = selectedPawn.drafter.Drafted
                                ? "RimWorldAccess.Input.Drafting.StatusDraftedTitle".Translate().ToString()
                                : "RimWorldAccess.Input.Drafting.StatusUndraftedTitle".Translate().ToString();
                            TolkHelper.Speak("RimWorldAccess.Input.Drafting.SinglePawnStatus".Loc(selectedPawn.LabelShort, status));

                            Event.current.Use();
                            return;
                        }
                    }
                }
            }

            // ===== PRIORITY 6.40: Multi-Select Pawn Commands (Alt+Shift+Arrow, Alt+Space, Alt+Escape) =====
            if (Current.ProgramState == ProgramState.Playing &&
                Find.CurrentMap != null &&
                WorldRendererUtility.DrawingMap &&
                (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                !ZoneCreationState.IsInCreationMode)
            {
                bool alt = Event.current.alt;
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;

                // Alt+Shift+Right: extend selection contiguously to next pawn
                if (alt && shift && !ctrl && key == KeyCode.RightArrow)
                {
                    MultiSelectState.SelectContiguousNext();
                    Event.current.Use();
                    return;
                }

                // Alt+Shift+Left: extend selection contiguously to previous pawn
                if (alt && shift && !ctrl && key == KeyCode.LeftArrow)
                {
                    MultiSelectState.SelectContiguousPrevious();
                    Event.current.Use();
                    return;
                }

                // Alt+Space: toggle focused pawn in/out of multi-selection
                if (alt && !shift && !ctrl && key == KeyCode.Space)
                {
                    Pawn focusedPawn = MultiSelectState.IsMultiSelectActive
                        ? MultiSelectState.FocusedPawn ?? ColonistBarState.GetPawnAtCurrentPosition()
                        : Find.Selector?.SingleSelectedThing as Pawn ?? ColonistBarState.GetPawnAtCurrentPosition();
                    MultiSelectState.TogglePawn(focusedPawn);
                    Event.current.Use();
                    return;
                }

                // Alt+Ctrl+Space: toggle-all (clear if in multi-select, select all if not)
                if (alt && ctrl && !shift && key == KeyCode.Space)
                {
                    var allColonists = ColonistBarState.GetColonistsPublic();
                    if (allColonists.Count > 0)
                    {
                        if (MultiSelectState.IsMultiSelectActive)
                        {
                            // Already in multi-select → clear
                            MultiSelectState.ClearMultiSelect();
                        }
                        else
                        {
                            // Not in multi-select → select all
                            MultiSelectState.SelectAllColonists(allColonists);
                        }
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.MultiSelect.NoColonistsOnMap".Loc());
                    }
                    Event.current.Use();
                    return;
                }

                // Ctrl+Shift+F1-F4: save current selection to group slot
                if (ctrl && shift && !alt && MultiSelectState.IsMultiSelectActive &&
                    key >= KeyCode.F1 && key <= KeyCode.F4)
                {
                    int slot = key - KeyCode.F1;
                    var component = Current.Game?.GetComponent<MultiSelectGroupComponent>();
                    if (component != null)
                    {
                        component.SaveGroup(slot, MultiSelectState.SelectedPawns);
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.MultiSelect.CannotSaveGroups".Loc());
                    }
                    Event.current.Use();
                    return;
                }

                // Ctrl+F1-F4: recall group from slot
                if (ctrl && !shift && !alt && key >= KeyCode.F1 && key <= KeyCode.F4)
                {
                    int slot = key - KeyCode.F1;
                    var component = Current.Game?.GetComponent<MultiSelectGroupComponent>();
                    if (component != null)
                    {
                        component.RecallGroup(slot);
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.MultiSelect.CannotRecallGroups".Loc());
                    }
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.45: Colonist Bar Navigation (Alt+Arrow, Alt+Number, Ctrl+Alt+Arrow) =====
            if (Current.ProgramState == ProgramState.Playing &&
                Find.CurrentMap != null &&
                WorldRendererUtility.DrawingMap &&
                (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                !ZoneCreationState.IsInCreationMode)
            {
                bool alt = KeyboardHelper.IsAltHeld;
                bool ctrl = Event.current.control;

                // Alt+Left/Right: navigate bar linearly (crosses page boundaries)
                // In multi-select mode, moves focus only without changing selection
                if (alt && !ctrl && key == KeyCode.RightArrow)
                {
                    if (MultiSelectState.IsMultiSelectActive)
                        MultiSelectState.NavigateFocusNext();
                    else
                        ColonistBarState.NavigateRight();
                    Event.current.Use();
                    return;
                }
                if (alt && !ctrl && key == KeyCode.LeftArrow)
                {
                    if (MultiSelectState.IsMultiSelectActive)
                        MultiSelectState.NavigateFocusPrevious();
                    else
                        ColonistBarState.NavigateLeft();
                    Event.current.Use();
                    return;
                }

                // Alt+Down/Up: page down/up through colonist pages, then mech pages
                if (alt && !ctrl && key == KeyCode.DownArrow)
                {
                    if (MultiSelectState.IsMultiSelectActive)
                    {
                        var pawn = ColonistBarState.PageFocusDown();
                        if (pawn != null)
                        {
                            MultiSelectState.SetFocusedPawn(pawn);
                            MultiSelectState.AnnounceFocusedPawn(pawn);
                        }
                    }
                    else
                        ColonistBarState.PageDown();
                    Event.current.Use();
                    return;
                }
                if (alt && !ctrl && key == KeyCode.UpArrow)
                {
                    if (MultiSelectState.IsMultiSelectActive)
                    {
                        var pawn = ColonistBarState.PageFocusUp();
                        if (pawn != null)
                        {
                            MultiSelectState.SetFocusedPawn(pawn);
                            MultiSelectState.AnnounceFocusedPawn(pawn);
                        }
                    }
                    else
                        ColonistBarState.PageUp();
                    Event.current.Use();
                    return;
                }

                // Ctrl+Alt+Left/Right: reorder colonists (shift/insert)
                // Blocked during multi-select to avoid confusion
                if (alt && ctrl && key == KeyCode.RightArrow)
                {
                    if (MultiSelectState.IsMultiSelectActive)
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.MultiSelect.CannotReorderDuringMultiSelect".Loc());
                        Event.current.Use();
                        return;
                    }
                    ColonistBarState.MoveRight();
                    Event.current.Use();
                    return;
                }
                if (alt && ctrl && key == KeyCode.LeftArrow)
                {
                    if (MultiSelectState.IsMultiSelectActive)
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.MultiSelect.CannotReorderDuringMultiSelect".Loc());
                        Event.current.Use();
                        return;
                    }
                    ColonistBarState.MoveLeft();
                    Event.current.Use();
                    return;
                }

                // Ctrl+Alt+Down/Up: move colonist between pages (shift/insert)
                if (alt && ctrl && key == KeyCode.DownArrow)
                {
                    if (MultiSelectState.IsMultiSelectActive)
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.MultiSelect.CannotReorderDuringMultiSelect".Loc());
                        Event.current.Use();
                        return;
                    }
                    ColonistBarState.MoveDown();
                    Event.current.Use();
                    return;
                }
                if (alt && ctrl && key == KeyCode.UpArrow)
                {
                    if (MultiSelectState.IsMultiSelectActive)
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.MultiSelect.CannotReorderDuringMultiSelect".Loc());
                        Event.current.Use();
                        return;
                    }
                    ColonistBarState.MoveUp();
                    Event.current.Use();
                    return;
                }

                // Alt+1 through Alt+9: jump to position 1-9 on current page
                if (alt && !ctrl && key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9)
                {
                    int position = key - KeyCode.Alpha1; // 0-indexed
                    if (MultiSelectState.IsMultiSelectActive)
                    {
                        var pawn = ColonistBarState.JumpFocusToPosition(position);
                        if (pawn != null)
                        {
                            MultiSelectState.SetFocusedPawn(pawn);
                            MultiSelectState.AnnounceFocusedPawn(pawn);
                        }
                    }
                    else
                        ColonistBarState.JumpToPosition(position);
                    Event.current.Use();
                    return;
                }

                // Alt+0: jump to position 10 on current page
                if (alt && !ctrl && key == KeyCode.Alpha0)
                {
                    if (MultiSelectState.IsMultiSelectActive)
                    {
                        var pawn = ColonistBarState.JumpFocusToPosition(9);
                        if (pawn != null)
                        {
                            MultiSelectState.SetFocusedPawn(pawn);
                            MultiSelectState.AnnounceFocusedPawn(pawn);
                        }
                    }
                    else
                        ColonistBarState.JumpToPosition(9);
                    Event.current.Use();
                    return;
                }

                // Ctrl+Alt+Enter: open inspection tree for currently selected pawn
                // Note: Alt+Enter is captured by Unity for fullscreen toggle, so Ctrl+Alt+Enter is used instead.
                // Unity reports ctrl=true alongside alt, so we just check alt without excluding ctrl.
                if (alt && (key == KeyCode.Return || key == KeyCode.KeypadEnter))
                {
                    Pawn selectedPawn = Find.Selector?.SingleSelectedThing as Pawn;
                    if (selectedPawn != null)
                    {
                        WindowlessInspectionState.OpenForObject(selectedPawn);
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Guard.NoPawnSelected".Loc());
                    }
                    Event.current.Use();
                    return;
                }

                // Ctrl+Alt+I: open info card for currently selected pawn
                if (alt && ctrl && key == KeyCode.I)
                {
                    Pawn selectedPawn = Find.Selector?.SingleSelectedThing as Pawn;
                    if (selectedPawn != null)
                    {
                        Find.WindowStack.Add(new Dialog_InfoCard(selectedPawn));
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Guard.NoPawnSelected".Loc());
                    }
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.48: Map Bookmarks (Ctrl+0-9, Ctrl+Shift+0-9, Ctrl+Alt+0-9) =====
            if (Current.ProgramState == ProgramState.Playing &&
                Find.CurrentMap != null &&
                WorldRendererUtility.DrawingMap &&
                (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                MapNavigationState.IsInitialized &&
                Event.current.control &&
                key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
            {
                int slot = key - KeyCode.Alpha0;

                if (KeyboardHelper.IsAltHeld && !Event.current.shift)
                {
                    BookmarkHelper.SetBookmark(slot);
                }
                else if (Event.current.shift && !KeyboardHelper.IsAltHeld)
                {
                    BookmarkHelper.JumpToBookmark(slot);
                }
                else if (!Event.current.shift && !KeyboardHelper.IsAltHeld)
                {
                    BookmarkHelper.PeekOrJumpToBookmark(slot);
                }

                Event.current.Use();
                return;
            }

            // ===== PRIORITY 6.5: Display mood info with Alt+M (if pawn is selected) =====
            if (key == KeyCode.M && KeyboardHelper.IsAltHeld)
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

            // ===== PRIORITY 6.51: Display health info with Alt+H (if pawn is selected) =====
            if (key == KeyCode.H && KeyboardHelper.IsAltHeld)
            {
                // Only display health if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Display health information
                    HealthState.DisplayHealthInfo();

                    // Prevent the default H key behavior
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.52: Display needs info with Alt+N (if pawn is selected) =====
            if (key == KeyCode.N && KeyboardHelper.IsAltHeld)
            {
                // Only display needs if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Display needs information
                    NeedsState.DisplayNeedsInfo();

                    // Prevent the default N key behavior
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.525: Display combat log with Alt+B (if pawn is selected) =====
            if (key == KeyCode.B && KeyboardHelper.IsAltHeld)
            {
                // Only display combat log if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Display combat log information
                    CombatLogState.DisplayCombatLog();

                    // Prevent the default B key behavior
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.527: Display gear info with Alt+G (if pawn is selected) =====
            if (key == KeyCode.G && KeyboardHelper.IsAltHeld)
            {
                // Only display gear if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Display gear information
                    GearState.DisplayGearInfo();

                    // Prevent the default G key behavior
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.5275: Display top skills with Alt+K (if pawn is selected) =====
            if (key == KeyCode.K && KeyboardHelper.IsAltHeld)
            {
                // Only display skills if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Display top skills information
                    SkillsState.DisplaySkillsInfo();

                    // Prevent the default K key behavior
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.5276: Assign area with Alt+A (if pawn is selected) =====
            if (key == KeyCode.A && KeyboardHelper.IsAltHeld)
            {
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Try pawn at cursor first
                    Pawn pawn = null;
                    if (MapNavigationState.IsInitialized)
                    {
                        IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;
                        if (cursorPosition.IsValid && cursorPosition.InBounds(Find.CurrentMap))
                        {
                            pawn = Find.CurrentMap.thingGrid.ThingsListAt(cursorPosition)
                                .OfType<Pawn>().FirstOrDefault();
                        }
                    }

                    // Fall back to selected pawn
                    if (pawn == null)
                        pawn = Find.Selector?.FirstSelectedObject as Pawn;

                    // Open area assignment menu (handles null pawn and unsupported pawns internally)
                    PawnAreaMenuState.Open(pawn);

                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.528: Rename pawn with Alt+R =====
            if (key == KeyCode.R && KeyboardHelper.IsAltHeld)
            {
                // Only rename if:
                // 1. We're in gameplay
                // 2. Map is loaded
                // 3. No dialog blocking
                // 4. Not in zone creation mode
                // 5. Not in placement mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !ArchitectState.IsInPlacementMode &&
                    !ViewingModeState.IsActive &&
                    !ShapePlacementState.IsActive)
                {
                    // Get pawn at cursor
                    Pawn pawn = null;
                    if (MapNavigationState.IsInitialized)
                    {
                        IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;
                        if (cursorPosition.IsValid && cursorPosition.InBounds(Find.CurrentMap))
                        {
                            pawn = Find.CurrentMap.thingGrid.ThingsListAt(cursorPosition)
                                .OfType<Pawn>().FirstOrDefault();
                        }
                    }

                    // Fall back to selected pawn
                    if (pawn == null)
                        pawn = Find.Selector?.SingleSelectedObject as Pawn;

                    if (pawn == null)
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.Cursor.NoPawnAtCursor".Loc());
                        Event.current.Use();
                        return;
                    }

                    // Check if pawn can be renamed
                    if (!CanPawnBeRenamed(pawn))
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.Cursor.PawnCannotBeRenamed".Loc());
                        Event.current.Use();
                        return;
                    }

                    // Open rename dialog
                    Find.WindowStack.Add(pawn.NamePawnDialog());
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.53: Unforbid all items on the map with Alt+F =====
            if (key == KeyCode.F && KeyboardHelper.IsAltHeld)
            {
                // Only unforbid if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Unforbid all items on the map
                    UnforbidAllItems();

                    // Prevent the default F key behavior
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.54: Reform caravan with Shift+C (temporary maps only) =====
            if (key == KeyCode.C && Event.current.shift)
            {
                // Only reform caravan if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. On a map (not in world view)
                // Note: Must check !WorldNavigationState.IsActive because Find.CurrentMap returns last map even in world view
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    !WorldNavigationState.IsActive &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Trigger caravan reformation
                    CaravanFormationState.TriggerReformation();

                    // Prevent the default C key behavior
                    Event.current.Use();
                    return;
                }
                // Give feedback if on world map
                else if (Current.ProgramState == ProgramState.Playing && WorldNavigationState.IsActive)
                {
                    TolkHelper.Speak("RimWorldAccess.Input.Caravan.ReformOnlyOnTemporaryMaps".Loc());
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.55: Announce time (T) or performance (Alt+T) =====
            // Shift+T is intentionally ignored here so it can reach any gizmo whose hotkey is T.
            if (key == KeyCode.T && !Event.current.control && !Event.current.shift)
            {
                // Only announce if:
                // 1. We're in gameplay (not at main menu)
                // 2. On a map or world view
                // 3. No windows are preventing camera motion (means a dialog is open)
                // 4. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    (Find.CurrentMap != null || WorldNavigationState.IsActive) &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    if (KeyboardHelper.IsAltHeld)
                    {
                        // Announce performance (actual vs requested TPS)
                        PerformanceAnnouncementState.AnnouncePerformance();
                    }
                    else
                    {
                        // Announce time information
                        TimeAnnouncementState.AnnounceTime();
                    }

                    // Prevent the default T key behavior
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.56: Toggle forbid status on items at cursor with F key =====
            if (key == KeyCode.F && !Event.current.shift && !Event.current.control && !KeyboardHelper.IsAltHeld)
            {
                // Only toggle forbid if:
                // 1. We're in gameplay (not at main menu)
                // 2. A valid map with initialized navigation
                // 3. No windows are preventing camera motion (means a dialog is open)
                // 4. Not in zone creation mode
                // 5. No accessibility menu is active (they use letter keys for typeahead)
                // 6. Scanner search is not active (uses letter keys for filtering)
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    MapNavigationState.IsInitialized &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !KeyboardHelper.IsAnyAccessibilityMenuActive() &&
                    !ScannerSearchState.IsActive)
                {
                    ToggleForbidAtCursor();
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.55: Open work menu with F1 key =====
            if (key == KeyCode.F1)
            {
                // Only open work menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. Work menu is not already active
                // 5. No accessibility menu is active (they handle their own input)
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !WorkMenuState.IsActive &&
                    !KeyboardHelper.IsAnyAccessibilityMenuActive())
                {
                    // Prevent the default F1 key behavior
                    Event.current.Use();

                    // If on the world map, switch to colony map first and restore cursor
                    if (WorldNavigationState.IsActive)
                    {
                        CameraJumper.TryHideWorld();
                        MapNavigationState.RestoreCursorForCurrentMap();
                    }

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
                        // Open the work menu
                        WorkMenuState.Open(targetPawn);
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.WorkMenu.NoColonistsAvailable".Loc());
                    }

                    return;
                }
            }

            // ===== PRIORITY 6.55: Open animals/mechs with F4 key =====
            if (key == KeyCode.F4)
            {
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !KeyboardHelper.IsAnyAccessibilityMenuActive())
                {
                    Event.current.Use();

                    if (WorldNavigationState.IsActive)
                    {
                        CameraJumper.TryHideWorld();
                        MapNavigationState.RestoreCursorForCurrentMap();
                    }

                    bool hasAnimals = Find.CurrentMap.mapPawns.ColonyAnimals.Any();
                    bool hasMechs = ModsConfig.BiotechActive &&
                        Find.CurrentMap.mapPawns.PawnsInFaction(Faction.OfPlayer)
                            .Any(p => p.RaceProps.IsMechanoid && p.OverseerSubject != null);

                    if (hasAnimals && hasMechs)
                    {
                        string animalsLabel = DefDatabase<MainButtonDef>.GetNamed("Animals", false)?.label?.CapitalizeFirst() ?? "Animals";
                        string mechsLabel = DefDatabase<MainButtonDef>.GetNamed("Mechs", false)?.label?.CapitalizeFirst() ?? "Mechs";
                        var options = new List<FloatMenuOption>
                        {
                            new FloatMenuOption(animalsLabel, () => AnimalsMenuState.Open()),
                            new FloatMenuOption(mechsLabel, () => MechsMenuState.Open())
                        };
                        WindowlessFloatMenuState.Open(options, colonistOrders: false);
                    }
                    else if (hasAnimals)
                    {
                        AnimalsMenuState.Open();
                    }
                    else if (hasMechs)
                    {
                        MechsMenuState.Open();
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.AnimalsMenu.NoAnimalsOrMechs".Loc());
                    }

                    return;
                }
            }

            // ===== PRIORITY 6.6: Open windowless schedule menu with F2 key =====
            if (key == KeyCode.F2)
            {
                // Only open schedule if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. Schedule menu is not already active
                // 5. No accessibility menu is active (they handle their own input)
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !WindowlessScheduleState.IsActive &&
                    !KeyboardHelper.IsAnyAccessibilityMenuActive())
                {
                    // Prevent the default F2 key behavior
                    Event.current.Use();

                    // If on the world map, switch to colony map first and restore cursor
                    if (WorldNavigationState.IsActive)
                    {
                        CameraJumper.TryHideWorld();
                        MapNavigationState.RestoreCursorForCurrentMap();
                    }

                    // Open the windowless schedule menu
                    WindowlessScheduleState.Open();

                    return;
                }
            }

            // ===== PRIORITY 6.7: Open assign menu with F3 key =====
            if (key == KeyCode.F3)
            {
                // Only open assign menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. No accessibility menu is active (they handle their own input)
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !KeyboardHelper.IsAnyAccessibilityMenuActive())
                {
                    // Prevent the default F3 key behavior
                    Event.current.Use();

                    // If on the world map, switch to colony map first and restore cursor
                    if (WorldNavigationState.IsActive)
                    {
                        CameraJumper.TryHideWorld();
                        MapNavigationState.RestoreCursorForCurrentMap();
                    }

                    // Open the assign menu (handles pawn selection internally)
                    AssignMenuState.Open();

                    return;
                }
            }

            // J key is no longer used - scanner is always available via Page Up/Down keys

            // ===== PRIORITY 7.05: Open gizmo navigation with G key (if pawn or building is selected) =====
            if (key == KeyCode.G)
            {
                // Block gizmos during placement or viewing modes
                // BUT allow G key if Confirm() was just called (JustConfirmed) - fixes timing issue
                // where G key event processes before the Enter key's state changes take effect
                if (ShapePlacementState.IsActive || (ViewingModeState.IsActive && !ViewingModeState.JustConfirmed))
                {
                    TolkHelper.Speak("RimWorldAccess.Input.Gizmo.UnavailableDuringPlacement".Loc());
                    Event.current.Use();
                    return;
                }

                // Only open gizmo navigation if we're in gameplay and no dialogs are open
                if (Current.ProgramState == ProgramState.Playing &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion))
                {
                    // Check if we're on the world map
                    if (WorldRendererUtility.WorldSelected)
                    {
                        // World map: open gizmos for selected world objects (caravans, settlements, etc.)
                        Event.current.Use();
                        GizmoNavigationState.OpenFromWorldObjects();
                        return;
                    }
                    // Colony map: requires map to be loaded and cursor initialized
                    else if (Find.CurrentMap != null &&
                             !ZoneCreationState.IsInCreationMode &&
                             MapNavigationState.IsInitialized)
                    {
                        // Prevent the default G key behavior
                        Event.current.Use();

                        // Decide whether to open gizmos for selected objects or for objects at cursor
                        // Use selected pawn gizmos if:
                        //   - A pawn was just selected with , or . (PawnJustSelected)
                        //   - OR multi-select is active (always show selected pawns' gizmos)
                        // Otherwise, use objects at the cursor position
                        if ((GizmoNavigationState.PawnJustSelected || MultiSelectState.IsMultiSelectActive) &&
                            Find.Selector != null && Find.Selector.NumSelected > 0)
                        {
                            // Open gizmos for the selected pawn(s)
                            GizmoNavigationState.Open();
                        }
                        else
                        {
                            // Open gizmos for objects at the cursor position
                            IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;
                            GizmoNavigationState.OpenAtCursor(cursorPosition, Find.CurrentMap);
                        }
                        return;
                    }
                }
            }

            // ===== PRIORITY 7.1: Open notification menu with L key (if no menu is active and we're in-game) =====
            if (key == KeyCode.L)
            {
                // Only open notification menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Prevent the default L key behavior
                    Event.current.Use();

                    // Open the notification menu
                    NotificationMenuState.Open();
                    return;
                }
            }

            // ===== PRIORITY 7.14: Bare / - focus colonist/mech bar on pawn under cursor =====
            if (key == KeyCode.Slash && !Event.current.shift && !Event.current.control && !KeyboardHelper.IsAltHeld)
            {
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    MapNavigationState.IsInitialized)
                {
                    ColonistBarState.FocusPawnByCursor();
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 7.15: Open learning helper with ? key (Shift+/ on US, remapped on non-US) =====
            if (key == KeyCode.Slash && (Event.current.shift || KeyboardHelper.WasCharacterRemapped))
            {
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    !TutorSystem.TutorialMode &&
                    TutorSystem.AdaptiveTrainingEnabled &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    Event.current.Use();
                    LearningHelperState.Open();
                    return;
                }
            }

            // ===== PRIORITY 7.5: Open quest menu with F7 key (if no menu is active and we're in-game) =====
            if (key == KeyCode.F7)
            {
                // Only open quest menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. No accessibility menu is active (they handle their own input)
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !KeyboardHelper.IsAnyAccessibilityMenuActive())
                {
                    // Prevent the default F7 key behavior
                    Event.current.Use();

                    // If on the world map, switch to colony map first and restore cursor
                    if (WorldNavigationState.IsActive)
                    {
                        CameraJumper.TryHideWorld();
                        MapNavigationState.RestoreCursorForCurrentMap();
                    }

                    // Open the quest menu
                    QuestMenuState.Open();
                    return;
                }
            }

            // ===== PRIORITY 7.55: Open research menu with F6 key (if no menu is active and we're in-game) =====
            if (key == KeyCode.F6)
            {
                // Only open research menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. No accessibility menu is active (they handle their own input)
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !KeyboardHelper.IsAnyAccessibilityMenuActive())
                {
                    // Prevent the default F6 key behavior
                    Event.current.Use();

                    // If on the world map, switch to colony map first and restore cursor
                    if (WorldNavigationState.IsActive)
                    {
                        CameraJumper.TryHideWorld();
                        MapNavigationState.RestoreCursorForCurrentMap();
                    }

                    // Open the research menu
                    WindowlessResearchMenuState.Open();
                    return;
                }
            }

            // ===== PRIORITY 7.56: Open extra menus with F12 key =====
            if (key == KeyCode.F12)
            {
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !KeyboardHelper.IsAnyAccessibilityMenuActive())
                {
                    Event.current.Use();

                    if (WorldNavigationState.IsActive)
                    {
                        CameraJumper.TryHideWorld();
                        MapNavigationState.RestoreCursorForCurrentMap();
                    }

                    ExtraMenusState.Open();
                    return;
                }
            }

            // ===== PRIORITY 7.6: Open inspection menu with lowercase 'i' key (DISABLED - replaced by inventory menu) =====
            if (key == KeyCode.None) // Changed from KeyCode.I to disable this binding
            {
                // Only open inspection menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. Map navigation is initialized
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    MapNavigationState.IsInitialized)
                {
                    // Prevent the default I key behavior
                    Event.current.Use();

                    // Open the inspection menu at the current cursor position
                    WindowlessInspectionState.Open(MapNavigationState.CurrentCursorPosition);
                    return;
                }
            }

            // ===== PRIORITY 7.61: Open info card at cursor with Alt+I =====
            if (KeyboardHelper.IsAltHeld && key == KeyCode.I && !Event.current.shift && !Event.current.control)
            {
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    MapNavigationState.IsInitialized &&
                    !WindowlessResearchMenuState.IsActive &&
                    !WindowlessResearchDetailState.IsActive &&
                    !WindowlessInventoryState.IsActive &&
                    !GizmoNavigationState.IsActive &&
                    !WindowlessInspectionState.IsActive &&
                    !QuestMenuState.IsActive &&
                    !NotificationMenuState.IsActive &&
                    !LearningHelperState.IsActive &&
                    !WindowlessFloatMenuState.IsActive &&
                    !PlantSelectionMenuState.IsActive &&
                    !MechControlGroupState.IsActive &&
                    !StorageSettingsMenuState.IsActive &&
                    !BillsMenuState.IsActive &&
                    !BillConfigState.IsActive)
                {
                    Event.current.Use();
                    OpenInfoCardAtCursor();
                    return;
                }
            }

            // ===== PRIORITY 7.6b: Open colony inventory menu with uppercase 'I' key =====
            if (key == KeyCode.I)
            {
                // Only open inventory menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. Current map exists
                // 3. No windows are preventing camera motion (means a dialog is open)
                // 4. Not in zone creation mode
                // 5. Inventory menu is not already active
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !WindowlessInventoryState.IsActive)
                {
                    // Prevent the default I key behavior
                    Event.current.Use();

                    // Open the colony-wide inventory menu
                    WindowlessInventoryState.Open();
                    return;
                }
            }

            // ===== PRIORITY 7.7: Open prisoner tab with P key (if prisoner/slave is selected) =====
            if (key == KeyCode.P)
            {
                // Only open prisoner tab if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. Prisoner tab is not already active
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !PrisonerTabState.IsActive)
                {
                    // Check if a prisoner or slave is currently visible in the prisoner tab
                    Pawn prisoner = PrisonerTabPatch.GetCurrentPrisoner();
                    if (prisoner != null)
                    {
                        // Prevent the default P key behavior
                        Event.current.Use();

                        // Open the prisoner tab
                        PrisonerTabState.Open(prisoner);
                        return;
                    }
                }
            }

            // ===== PRIORITY 8: Open pause menu with Escape (if no menu is active and we're in-game) =====
            if (key == KeyCode.Escape)
            {
                // Only open pause menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. No accessibility menus are active (they handle their own Escape)
                // 5. Not in targeting mode (let RimWorld handle Escape to cancel targeting)
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !KeyboardHelper.IsAnyAccessibilityMenuActive() &&
                    !QuestLocationsBrowserState.IsActive &&
                    !SettlementBrowserState.IsActive &&
                    !CaravanInspectState.IsActive &&
                    (Find.Targeter == null || !Find.Targeter.IsTargeting))
                {
                    // Prevent the default escape behavior (opening game's pause menu)
                    Event.current.Use();

                    // Open our windowless pause menu
                    WindowlessPauseMenuState.Open();
                    return;
                }
            }

            // ===== PRIORITY 9: Handle Enter key for inspection menu =====
            // Don't process if in zone creation mode
            if (ZoneCreationState.IsInCreationMode)
                return;

            // Handle Enter key for opening the inspection menu (same as I key)
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                // Don't open inspection if HealthTabState is active (safety net)
                if (HealthTabState.IsActive)
                    return;

                // Only process during normal gameplay with a valid map
                if (Find.CurrentMap == null)
                    return;

                // Don't process if any dialog or window that prevents camera motion is open
                if (Find.WindowStack != null && Find.WindowStack.WindowsPreventCameraMotion)
                    return;

                // IMPORTANT: Don't intercept Enter if targeting mode is active
                // This allows the targeting system to handle target selection
                if (Find.Targeter != null && Find.Targeter.IsTargeting)
                    return;

                // Check if map navigation is initialized
                if (!MapNavigationState.IsInitialized)
                    return;

                // Get the cursor position
                IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;

                // Validate cursor position
                if (!cursorPosition.IsValid || !cursorPosition.InBounds(Find.CurrentMap))
                {
                    TolkHelper.Speak("RimWorldAccess.Input.Cursor.InvalidPosition".Loc());
                    Event.current.Use();
                    return;
                }

                // Prevent the default Enter behavior
                Event.current.Use();

                // Open the windowless inspection menu at the current cursor position
                // This is the same menu that opens with the I key
                WindowlessInspectionState.Open(cursorPosition);
                return;
            }

            // ===== PRIORITY 10: Handle left bracket [ key - execute top context menu option =====
            // Bare [  => issue order immediately ("PawnName: action")
            // Shift+[ => queue order (KeyBindingDefOf.QueueOrder picks up the shift during action())
            if (key == KeyCode.LeftBracket)
            {
                if (Find.World?.renderer?.wantedMode == RimWorld.Planet.WorldRenderMode.Planet)
                    return;
                if (Find.CurrentMap == null)
                    return;
                if (Find.WindowStack != null && Find.WindowStack.WindowsPreventCameraMotion)
                    return;
                if (!MapNavigationState.IsInitialized)
                    return;

                IntVec3 lbCursor = MapNavigationState.CurrentCursorPosition;
                Map lbMap = Find.CurrentMap;
                if (!lbCursor.IsValid || !lbCursor.InBounds(lbMap))
                {
                    TolkHelper.Speak("RimWorldAccess.Input.Cursor.InvalidPosition".Loc());
                    Event.current.Use();
                    return;
                }

                if (Find.Selector == null || !Find.Selector.SelectedPawns.Any())
                {
                    TolkHelper.Speak("RimWorldAccess.Guard.NoPawnSelected".Loc());
                    Event.current.Use();
                    return;
                }

                List<Pawn> lbPawns = Find.Selector.SelectedPawns.ToList();
                Vector3 lbClickPos = lbCursor.ToVector3Shifted();
                List<FloatMenuOption> lbOptions = FloatMenuMakerMap.GetOptions(
                    lbPawns,
                    lbClickPos,
                    out FloatMenuContext _
                );

                if (lbOptions == null || lbOptions.Count == 0)
                {
                    TolkHelper.Speak("RimWorldAccess.Input.Cursor.NoAvailableActions".Loc());
                    Event.current.Use();
                    return;
                }

                bool queueing = Event.current.shift;
                bool multiFeedback = MultiSelectState.IsMultiSelectActive && lbPawns.Count > 1;

                // For multi-select, wrap all options so the invoked action announces per-pawn
                // success/failure (same behavior as pressing Enter on the top option from the
                // right-bracket menu). For single pawn we announce ourselves below.
                if (multiFeedback)
                    WrapOptionsForMultiSelectFeedback(lbOptions, lbPawns);

                FloatMenuOption top = lbOptions[0];

                if (top.Disabled)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    string singlePrefix = lbPawns.Count == 1 ? lbPawns[0].LabelShort + ": " : "";
                    TolkHelper.Speak("RimWorldAccess.Input.Order.TopOptionUnavailable".Loc(singlePrefix, top.Label));
                    Event.current.Use();
                    return;
                }

                // Action is invoked with Event.current.shift intact so KeyBindingDefOf.QueueOrder.IsDownEvent
                // evaluates true when queueing — no need to pass colonistOrdering=true (we play the sound ourselves).
                SoundDefOf.ColonistOrdered.PlayOneShotOnCamera();
                // Capture option label so callback-based Targeter.BeginTargeting calls inside
                // the action (e.g., force-wear) can announce it as the second-phase prompt.
                PendingTargetingContext.Set(top.Label);
                try
                {
                    top.Chosen(false, null);
                }
                finally
                {
                    PendingTargetingContext.Clear();
                }

                // Multi-select: the wrapped action already announced per-pawn feedback.
                if (!multiFeedback)
                {
                    string prefix = lbPawns[0].LabelShort;
                    if (queueing)
                        TolkHelper.Speak("RimWorldAccess.Input.Order.QueuedAction".Loc(prefix, top.Label, "Queued".Translate()));
                    else
                        TolkHelper.Speak("RimWorldAccess.Input.Order.Action".Loc(prefix, top.Label));
                }

                Event.current.Use();
                return;
            }

            // ===== PRIORITY 10: Handle right bracket ] key for colonist orders =====
            if (key == KeyCode.RightBracket)
            {
                // Don't process if in world view - WorldNavigationPatch handles ] there
                if (Find.World?.renderer?.wantedMode == RimWorld.Planet.WorldRenderMode.Planet)
                    return;

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
                    TolkHelper.Speak("RimWorldAccess.Input.Cursor.InvalidPosition".Loc());
                    Event.current.Use();
                    return;
                }

                // Check for pawns to give orders to
                if (Find.Selector == null || !Find.Selector.SelectedPawns.Any())
                {
                    TolkHelper.Speak("RimWorldAccess.Guard.NoPawnSelected".Loc());
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
                    // If multi-select is active, wrap actions with feedback and inject formation option
                    if (MultiSelectState.IsMultiSelectActive && selectedPawns.Count > 1)
                    {
                        // Wrap existing options first (formation option injected below stays unwrapped
                        // — it enters placement mode and issues jobs later in LineFormationState.Confirm).
                        WrapOptionsForMultiSelectFeedback(options, selectedPawns);

                        // Insert formation option just after the GoHere entry.
                        int goHereIndex = options.FindIndex(o =>
                            o.Label != null && (
                                o.Label == "GoHere".Translate() ||
                                o.Label.StartsWith((string)"GoHere".Translate())));

                        if (goHereIndex >= 0)
                        {
                            var formationPawns = selectedPawns.ToList();
                            string goHereLabel = "GoHere".Translate();
                            var formationOption = new FloatMenuOption(
                                "RimWorldAccess.Input.Order.FormationOption".Translate(goHereLabel),
                                () => LineFormationState.Activate(formationPawns));
                            options.Insert(goHereIndex + 1, formationOption);
                        }
                    }

                    // Open the windowless menu with these options
                    WindowlessFloatMenuState.Open(options, true); // true = gives colonist orders
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Input.Cursor.NoAvailableActions".Loc());
                }

                // Consume the event
                Event.current.Use();
            }

            // ===== PRIORITY 10.5: Handle local map arrow key navigation =====
            // Uses Event.current for OS key repeat support (unlike CameraDriver.Update() which uses Input.GetKeyDown)
            if (key == KeyCode.UpArrow || key == KeyCode.DownArrow ||
                key == KeyCode.LeftArrow || key == KeyCode.RightArrow)
            {
                // Skip if in full planet view (but allow orbital/background-world maps)
                if (!WorldRendererUtility.DrawingMap)
                    return;

                // Only during gameplay with valid map
                if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null)
                    return;

                // Skip if windows prevent camera motion
                if (Find.WindowStack?.WindowsPreventCameraMotion == true)
                    return;

                // Skip if not initialized or suppressed
                if (!MapNavigationState.IsInitialized || MapNavigationState.SuppressMapNavigation)
                    return;

                // Handle the arrow key via MapArrowKeyHandler
                if (MapArrowKeyHandler.HandleArrowKey(key, Event.current.control, Event.current.shift))
                {
                    Event.current.Use();
                }
                return;
            }

            // CATCH-ALL: If any accessibility menu is active, consume ALL remaining key events
            // This prevents ANY keys from leaking to the game when a menu has focus
            if (KeyboardHelper.IsAnyAccessibilityMenuActive() && Event.current.isKey)
            {
                Event.current.Use();
                return;
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
            string title = "RimWorldAccess.Input.Schedule.OverlayTitle".Translate(selectedPawn.LabelShort, pawnNum, totalPawns, hour).ToString();
            string currentInfo = "RimWorldAccess.Input.Schedule.OverlayCurrent".Translate(currentAssignment.label).ToString();
            string instructions1 = "RimWorldAccess.Input.Schedule.OverlayInstructions1".Translate().ToString();
            string instructions2 = "RimWorldAccess.Input.Schedule.OverlayInstructions2".Translate().ToString();

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
        /// Checks if a pawn can be renamed by the player.
        /// </summary>
        private static bool CanPawnBeRenamed(Pawn pawn)
        {
            if (pawn == null) return false;
            // Colonists and colony subhumans (slaves, etc.) can be renamed
            if (pawn.IsColonist || pawn.IsColonySubhuman) return true;
            // Player-owned animals and mechanoids can be renamed
            if (pawn.Faction == Faction.OfPlayer &&
                (pawn.RaceProps.Animal || pawn.RaceProps.IsMechanoid))
                return true;
            return false;
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

        /// <summary>
        /// Unforbids all forbidden items on the current map.
        /// </summary>
        private static void UnforbidAllItems()
        {
            if (!GuardHelper.RequireMap(out Map map)) return;

            // Get all things on the map
            List<Thing> allThings = map.listerThings.AllThings;
            int unforbiddenCount = 0;

            // Iterate through all things and unforbid items
            foreach (Thing thing in allThings)
            {
                // Check if the thing can be forbidden (has CompForbiddable component)
                CompForbiddable forbiddable = thing.TryGetComp<CompForbiddable>();

                // If it has the component and is currently forbidden, unforbid it
                if (forbiddable != null && forbiddable.Forbidden)
                {
                    thing.SetForbidden(false, warnOnFail: false);
                    unforbiddenCount++;
                }
            }

            // Announce result to user
            if (unforbiddenCount == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Input.Unforbid.NoForbiddenItems".Loc());
            }
            else if (unforbiddenCount == 1)
            {
                TolkHelper.Speak("RimWorldAccess.Input.Unforbid.CountOne".Loc());
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Input.Unforbid.CountMany".Loc(unforbiddenCount));
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }

            Log.Message($"Unforbid all: {unforbiddenCount} items unforbidden");
        }

        #region Info Card at Cursor

        private static void OpenInfoCardAtCursor()
        {
            IntVec3 pos = MapNavigationState.CurrentCursorPosition;
            Map map = Find.CurrentMap;

            if (!pos.IsValid || !pos.InBounds(map))
            {
                TolkHelper.Speak("RimWorldAccess.Input.Cursor.NothingToInspect".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            // Gather all things at cursor position, sorted by AltitudeLayer descending
            // (matches TileInfoHelper ordering - highest layer first)
            var things = pos.GetThingList(map)
                .Where(t => !(t is Mote) && t.def.category != ThingCategory.Mote)
                .OrderByDescending(t => (int)t.def.altitudeLayer)
                .ToList();

            TerrainDef terrain = map.terrainGrid.TerrainAt(pos);

            if (things.Count == 1)
            {
                // Single thing - open its info card directly
                Find.WindowStack.Add(new Dialog_InfoCard(things[0]));
            }
            else if (things.Count == 0)
            {
                if (terrain != null)
                {
                    // Only terrain at this position
                    Find.WindowStack.Add(new Dialog_InfoCard(terrain));
                }
                else
                {
                    // Nothing at all
                    TolkHelper.Speak("RimWorldAccess.Input.Cursor.NothingToInspect".Loc());
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
            }
            else
            {
                // Multiple things - show selection menu with terrain as last option
                var options = new List<FloatMenuOption>();

                foreach (var thing in things)
                {
                    var capturedThing = thing;
                    options.Add(new FloatMenuOption(
                        capturedThing.LabelCapNoCount.StripTags(),
                        () => Find.WindowStack.Add(new Dialog_InfoCard(capturedThing))
                    ));
                }

                if (terrain != null)
                {
                    var capturedTerrain = terrain;
                    options.Add(new FloatMenuOption(
                        ((string)capturedTerrain.LabelCap).StripTags(),
                        () => Find.WindowStack.Add(new Dialog_InfoCard(capturedTerrain))
                    ));
                }

                WindowlessFloatMenuState.Open(options, colonistOrders: false);
            }
        }

        #endregion

        #region Forbid Toggle at Cursor

        private static float lastForbidToggleTime = 0f;
        private const float ForbidToggleCooldown = 0.3f;

        /// <summary>
        /// Toggles forbid/unforbid on items at the current cursor position.
        /// </summary>
        private static void ToggleForbidAtCursor()
        {
            // Cooldown to prevent accidental double-presses
            if (Time.time - lastForbidToggleTime < ForbidToggleCooldown)
                return;
            lastForbidToggleTime = Time.time;

            IntVec3 position = MapNavigationState.CurrentCursorPosition;
            Map map = Find.CurrentMap;

            List<Thing> allThings = position.GetThingList(map);
            List<Thing> forbiddableItems = new List<Thing>();

            foreach (Thing thing in allThings)
            {
                CompForbiddable forbiddable = thing.TryGetComp<CompForbiddable>();
                if (forbiddable != null)
                {
                    forbiddableItems.Add(thing);
                }
            }

            if (forbiddableItems.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Input.Forbid.NothingToForbid".Loc());
                return;
            }

            // Determine if we should forbid or unforbid
            // If any item is unforbidden, forbid all. If all are forbidden, unforbid all.
            bool shouldForbid = forbiddableItems.Any(t => !t.TryGetComp<CompForbiddable>().Forbidden);

            int toggledCount = 0;
            string firstItemName = null;

            foreach (Thing item in forbiddableItems)
            {
                if (firstItemName == null)
                    firstItemName = item.LabelShort;
                item.SetForbidden(shouldForbid, warnOnFail: false);
                toggledCount++;
            }

            string announcement;
            if (toggledCount == 1)
            {
                announcement = shouldForbid
                    ? "RimWorldAccess.Input.Forbid.SingleForbidden".Translate(firstItemName).ToString()
                    : "RimWorldAccess.Input.Forbid.SingleUnforbidden".Translate(firstItemName).ToString();
            }
            else
            {
                announcement = shouldForbid
                    ? "RimWorldAccess.Input.Forbid.ManyForbidden".Translate(toggledCount).ToString()
                    : "RimWorldAccess.Input.Forbid.ManyUnforbidden".Translate(toggledCount).ToString();
            }

            TolkHelper.SpeakData(announcement);
        }

        #endregion

        #region Policy Editor Helpers

        /// <summary>
        /// Handles ThingFilter keyboard input (shared by apparel, food, and reading policy editors).
        /// Does NOT handle Escape (caller handles that for proper context-dependent behavior).
        /// </summary>
        private static bool HandleThingFilterInput(KeyCode key)
        {
            if (!ThingFilterNavigationState.IsActive)
                return false;

            if (ThingFilterNavigationState.IsEditingSlider)
            {
                if (key == KeyCode.LeftArrow)
                {
                    ThingFilterNavigationState.AdjustSlider(-1);
                    return true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    ThingFilterNavigationState.AdjustSlider(1);
                    return true;
                }
                else if (key == KeyCode.UpArrow || key == KeyCode.DownArrow)
                {
                    ThingFilterNavigationState.ToggleSliderPart();
                    return true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Escape)
                {
                    ThingFilterNavigationState.ExitSliderEdit();
                    return true;
                }
                return false;
            }

            // Handle typeahead character input via keyCode ranges.
            // Request layout-aware character for typeahead (supports non-Latin keyboards)
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if ((isLetter || (isNumber && !Event.current.shift)) && !KeyboardHelper.IsAltHeld && !Event.current.control)
            {
                return true;
            }

            // Normal filter navigation
            if (key == KeyCode.Backspace)
            {
                if (ThingFilterNavigationState.HasActiveSearch)
                {
                    ThingFilterNavigationState.ProcessBackspace();
                    return true;
                }
            }
            else if (key == KeyCode.UpArrow)
            {
                if (ThingFilterNavigationState.HasActiveSearch && !ThingFilterNavigationState.HasNoMatches)
                    ThingFilterNavigationState.SelectPreviousMatch();
                else
                    ThingFilterNavigationState.SelectPrevious();
                return true;
            }
            else if (key == KeyCode.DownArrow)
            {
                if (ThingFilterNavigationState.HasActiveSearch && !ThingFilterNavigationState.HasNoMatches)
                    ThingFilterNavigationState.SelectNextMatch();
                else
                    ThingFilterNavigationState.SelectNext();
                return true;
            }
            else if (key == KeyCode.Space)
            {
                ThingFilterNavigationState.ToggleSelected();
                return true;
            }
            else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                ThingFilterNavigationState.ActivateSelected();
                return true;
            }
            else if (key == KeyCode.LeftArrow)
            {
                ThingFilterNavigationState.Collapse();
                return true;
            }
            else if (key == KeyCode.RightArrow)
            {
                ThingFilterNavigationState.Expand();
                return true;
            }
            else if (key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8))
            {
                ThingFilterNavigationState.ExpandAllSiblings();
                return true;
            }
            else if (key == KeyCode.Home)
            {
                ThingFilterNavigationState.JumpToFirst(Event.current.control);
                return true;
            }
            else if (key == KeyCode.End)
            {
                ThingFilterNavigationState.JumpToLast(Event.current.control);
                return true;
            }
            else if (key == KeyCode.A && Event.current.control)
            {
                ThingFilterNavigationState.AllowAll();
                return true;
            }
            else if (key == KeyCode.D && Event.current.control)
            {
                ThingFilterNavigationState.DisallowAll();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Handles drug policy editor keyboard input (drug list and drug settings modes).
        /// </summary>
        private static bool HandleDrugEditorInput(KeyCode key)
        {
            if (!DrugPolicyEditorState.IsActive)
                return false;

            var mode = DrugPolicyEditorState.CurrentMode;

            if (mode == DrugPolicyEditorState.NavigationMode.DrugList)
            {
                if (key == KeyCode.UpArrow)
                {
                    DrugPolicyEditorState.SelectPreviousDrug();
                    return true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    DrugPolicyEditorState.SelectNextDrug();
                    return true;
                }
                else if (key == KeyCode.Home)
                {
                    DrugPolicyEditorState.JumpToFirstDrug();
                    return true;
                }
                else if (key == KeyCode.End)
                {
                    DrugPolicyEditorState.JumpToLastDrug();
                    return true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    DrugPolicyEditorState.EnterDrugSettings();
                    return true;
                }
                else if (key == KeyCode.Escape)
                {
                    PolicyEditorState.Close();
                    return true;
                }
            }
            else if (mode == DrugPolicyEditorState.NavigationMode.DrugSettings)
            {
                if (key == KeyCode.UpArrow)
                {
                    DrugPolicyEditorState.SelectPreviousSetting();
                    return true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    DrugPolicyEditorState.SelectNextSetting();
                    return true;
                }
                else if (key == KeyCode.Space || key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    DrugPolicyEditorState.ToggleSetting();
                    return true;
                }
                else if (key == KeyCode.LeftArrow)
                {
                    DrugPolicyEditorState.AdjustSetting(-1);
                    return true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    DrugPolicyEditorState.AdjustSetting(1);
                    return true;
                }
                else if (key == KeyCode.Escape)
                {
                    DrugPolicyEditorState.ReturnToDrugList();
                    return true;
                }
            }

            return false;
        }

        #endregion

        /// <summary>
        /// Wraps each option's action so that, after invocation, it announces per-pawn
        /// success/failure feedback ("Everyone X", "No one could X", "Everyone except A, B X",
        /// "Only A, B X"). Used for both right-bracket (navigated menu) and left-bracket
        /// (top-option execution) when multi-select is active.
        /// </summary>
        private static void WrapOptionsForMultiSelectFeedback(List<FloatMenuOption> options, List<Pawn> selectedPawns)
        {
            var capturedPawns = selectedPawns.ToList();
            string everyone = ((string)"ConfirmAbandonHomeNegativeThoughts_Everyone".Translate()).TrimEnd(':', ' ');
            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                if (opt.Disabled || opt.action == null)
                    continue;

                var originalAction = opt.action;
                var optLabel = opt.Label;
                opt.action = () =>
                {
                    var jobsBefore = new Dictionary<Pawn, Verse.AI.Job>();
                    var queueCountsBefore = new Dictionary<Pawn, int>();
                    foreach (var p in capturedPawns)
                    {
                        jobsBefore[p] = p.jobs?.curJob;
                        queueCountsBefore[p] = p.jobs?.jobQueue?.Count ?? 0;
                    }

                    bool targeterWasActive = Find.Targeter?.IsTargeting ?? false;

                    originalAction.Invoke();

                    bool targeterNowActive = Find.Targeter?.IsTargeting ?? false;
                    if (!targeterWasActive && targeterNowActive)
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.MultiSelectOrder.EveryoneDoes".Loc(everyone, optLabel), SpeechPriority.Low);
                        return;
                    }

                    var succeeded = capturedPawns
                        .Where(p =>
                            p.jobs?.curJob != jobsBefore[p] ||
                            (p.jobs?.jobQueue?.Count ?? 0) > queueCountsBefore[p])
                        .ToList();
                    var unchanged = capturedPawns
                        .Where(p =>
                            p.jobs?.curJob == jobsBefore[p] &&
                            (p.jobs?.jobQueue?.Count ?? 0) <= queueCountsBefore[p])
                        .ToList();

                    if (unchanged.Count == 0)
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.MultiSelectOrder.EveryoneDoes".Loc(everyone, optLabel), SpeechPriority.Low);
                    }
                    else if (succeeded.Count == 0)
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.MultiSelectOrder.NoOneCould".Loc(optLabel), SpeechPriority.Low);
                    }
                    else if (unchanged.Count <= succeeded.Count)
                    {
                        string names = MenuHelper.FormatNameList(
                            unchanged.Select(p => p.LabelShort).ToList());
                        TolkHelper.Speak(
                            "RimWorldAccess.Input.MultiSelectOrder.EveryoneExcept".Loc(everyone, names, optLabel),
                            SpeechPriority.Low);
                    }
                    else
                    {
                        string names = MenuHelper.FormatNameList(
                            succeeded.Select(p => p.LabelShort).ToList());
                        TolkHelper.Speak(
                            "RimWorldAccess.Input.MultiSelectOrder.OnlyDoes".Loc(names, optLabel),
                            SpeechPriority.Low);
                    }
                };
            }
        }

}
}
