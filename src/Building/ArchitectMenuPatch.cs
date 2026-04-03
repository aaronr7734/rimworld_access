using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;
using RimWorld.Planet;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to add Tab key for opening the accessible architect menu.
    /// Handles category selection (treeview), tool selection, and material selection.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class ArchitectMenuPatch
    {
        private static float lastArchitectKeyTime = 0f;
        private const float ArchitectKeyCooldown = 0.3f;

        /// <summary>
        /// Prefix patch to handle keyboard input for architect tree menu and Tab key.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        public static void Prefix()
        {
            // Handle architect tree menu keyboard input first
            if (ArchitectTreeState.IsActive && !InfoCardState.IsActive)
            {
                // Clean up stale architect state when on the world map
                // (user may have opened architect then switched to world view without closing it)
                if (WorldNavigationState.IsActive)
                {
                    ArchitectTreeState.Close();
                    ArchitectState.Reset();
                    // Fall through to normal handling
                }
                else
                {
                    if (Event.current.type == EventType.KeyDown)
                    {
                        HandleArchitectTreeInput();
                    }
                    return;
                }
            }

            // If any accessibility menu is active, don't intercept - let UnifiedKeyboardPatch handle it
            if (KeyboardHelper.IsAnyAccessibilityMenuActive())
                return;

            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            KeyCode key = Event.current.keyCode;

            // Only process Tab key for opening the architect menu
            if (key != KeyCode.Tab)
                return;

            // Cooldown to prevent accidental double-presses
            if (Time.time - lastArchitectKeyTime < ArchitectKeyCooldown)
                return;

            lastArchitectKeyTime = Time.time;

            // Only process during normal gameplay with a valid map
            if (Find.CurrentMap == null || !MapNavigationState.IsInitialized)
                return;

            // Never open architect menu while on the world map
            if (WorldNavigationState.IsActive)
                return;

            // Don't process if any dialog or window that prevents camera motion is open
            if (Find.WindowStack != null && Find.WindowStack.WindowsPreventCameraMotion)
                return;

            // Don't process if already in zone creation mode
            if (ZoneCreationState.IsInCreationMode)
                return;

            // Don't process if windowless orders menu is active
            if (WindowlessFloatMenuState.IsActive)
                return;

            // Don't process if schedule window is active
            if (WindowlessScheduleState.IsActive)
                return;

            // If already in architect mode (but in placement), cancel back to menu
            if (ArchitectState.IsInPlacementMode)
            {
                ArchitectState.Cancel();
                Event.current.Use();
                return;
            }

            // If architect mode is active (in category/tool selection), close it
            if (ArchitectState.IsActive)
            {
                ArchitectState.Reset();
                TolkHelper.Speak("Architect menu closed");
                Event.current.Use();
                return;
            }

            // Open the architect tree menu
            OpenArchitectTreeMenu();

            // Consume the event
            Event.current.Use();
        }

        /// <summary>
        /// Handles keyboard input when the architect tree menu is active.
        /// Delegates standard tree navigation to TreeNavigationHelper via ArchitectTreeState.HandleInput,
        /// and handles architect-specific keys (Right Bracket, Escape close) here.
        /// </summary>
        private static void HandleArchitectTreeInput()
        {
            // Forward character events to TypeaheadCharacterBuffer (this patch runs before
            // UnifiedKeyboardPatch, so character events must be dispatched here)
            if (TypeaheadCharacterBuffer.TryForwardCharacterEvent())
            {
                Event.current.Use();
                return;
            }

            // Don't handle input if a float menu (like right-click options) is open
            if (WindowlessFloatMenuState.IsActive)
                return;

            KeyCode key = Event.current.keyCode;
            key = KeyboardHelper.RemapCharacterToKeyCode(key);

            // Handle Right Bracket - open right-click options for selected designator
            // (architect-specific, not part of standard tree navigation)
            if (key == KeyCode.RightBracket)
            {
                OpenDesignatorRightClickOptions();
                Event.current.Use();
                return;
            }

            // Handle Alt+I - open info card for selected designator's PlacingDef
            // (architect-specific: needs Designator_Build awareness, not generic tree LinkedDef)
            if (key == KeyCode.I && KeyboardHelper.IsAltHeld)
            {
                OpenDesignatorInfoCard();
                Event.current.Use();
                return;
            }

            // Delegate standard tree navigation to TreeNavigationHelper
            if (ArchitectTreeState.HandleInput(Event.current))
            {
                Event.current.Use();
                return;
            }

            // HandleInput returned false = Escape with no active search
            if (key == KeyCode.Escape)
            {
                ArchitectTreeState.Close();
                ArchitectState.Reset(); // Also reset ArchitectState so Tab works again
                TolkHelper.Speak("Architect menu closed");
                Event.current.Use();
                return;
            }

            // Consume other keys to prevent passthrough
            Event.current.Use();
        }

        /// <summary>
        /// Opens the architect tree menu with categories and tools.
        /// </summary>
        private static void OpenArchitectTreeMenu()
        {
            // Enter category selection mode in ArchitectState
            ArchitectState.EnterCategorySelection();

            // Open the tree menu with callback for when a designator is selected
            ArchitectTreeState.Open(OnDesignatorSelected);

            Log.Message("Opened architect tree menu");
        }

        /// <summary>
        /// Opens the right-click options for the currently selected designator.
        /// </summary>
        private static void OpenDesignatorRightClickOptions()
        {
            Designator designator = ArchitectTreeState.GetSelectedDesignator();
            if (designator == null)
            {
                TolkHelper.Speak("No designator selected");
                return;
            }

            // Get right-click options from the designator
            List<FloatMenuOption> options = designator.RightClickFloatMenuOptions?.ToList();

            if (options == null || options.Count == 0)
            {
                TolkHelper.Speak("No additional options");
                return;
            }

            // Announce and open the options menu
            TolkHelper.Speak($"Options for {designator.LabelCap}");
            WindowlessFloatMenuState.Open(options, false);

            Log.Message($"Opened right-click options for designator: {designator.LabelCap}");
        }

        /// <summary>
        /// Opens the info card for the currently selected designator's building/terrain def.
        /// Only available for Designator_Build items; non-build designators show a message.
        /// </summary>
        private static void OpenDesignatorInfoCard()
        {
            Designator designator = ArchitectTreeState.GetSelectedDesignator();
            if (designator == null)
            {
                TolkHelper.Speak("Select a building to view its info card");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            if (designator is Designator_Build buildDesignator)
            {
                BuildableDef placingDef = buildDesignator.PlacingDef;
                if (placingDef != null)
                {
                    InfoCardState.OpenInfoCardForDef(placingDef);
                    return;
                }
            }

            TolkHelper.Speak("No info card available for this tool");
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Called when a designator (tool) is selected from the menu.
        /// </summary>
        private static void OnDesignatorSelected(Designator designator)
        {
            // Check if this is a zone designator - enter zone placement directly
            if (IsZoneDesignator(designator))
            {
                EnterZonePlacement(designator);
                return;
            }

            // Check if this is a build designator that needs material selection
            if (designator is Designator_Build buildDesignator)
            {
                BuildableDef buildable = buildDesignator.PlacingDef;

                if (ArchitectHelper.RequiresMaterialSelection(buildable))
                {
                    // Show material selection menu
                    ShowMaterialMenu(buildable, designator);
                    return;
                }
            }

            // No material selection needed - go straight to placement
            ArchitectState.EnterPlacementMode(designator);
        }

        /// <summary>
        /// Shows the material selection menu for a buildable.
        /// </summary>
        private static void ShowMaterialMenu(BuildableDef buildable, Designator originalDesignator)
        {
            // Create material options
            List<FloatMenuOption> options = ArchitectHelper.CreateMaterialOptions(
                buildable,
                (material) => OnMaterialSelected(buildable, material)
            );

            if (options.Count == 0)
            {
                TolkHelper.Speak($"No materials available for {buildable.label}");
                ArchitectState.Reset();
                return;
            }

            // Enter material selection mode
            ArchitectState.EnterMaterialSelection(buildable, originalDesignator);

            // Open the windowless menu
            WindowlessFloatMenuState.Open(options, false);

            Log.Message($"Opened material menu for: {buildable.defName}");
        }

        /// <summary>
        /// Called when a material is selected.
        /// Creates the build designator and enters placement mode.
        /// </summary>
        private static void OnMaterialSelected(BuildableDef buildable, ThingDef material)
        {
            // Create a build designator with the selected material
            Designator_Build designator = ArchitectHelper.CreateBuildDesignator(buildable, material);

            // Enter placement mode
            ArchitectState.EnterPlacementMode(designator, material);
        }

        /// <summary>
        /// Checks if a designator is a zone/area/cell-based designator.
        /// This includes zones (stockpiles, growing zones), areas (home, roof), and other multi-cell designators.
        /// Delegates to ShapeHelper for the type hierarchy check.
        /// </summary>
        private static bool IsZoneDesignator(Designator designator)
        {
            return ShapeHelper.IsCellsDesignator(designator) || ShapeHelper.IsZoneDesignator(designator);
        }

        /// <summary>
        /// Enters zone placement mode with rectangle selection.
        /// </summary>
        private static void EnterZonePlacement(Designator designator)
        {
            ArchitectState.EnterPlacementMode(designator);
            string zoneName = designator.Label ?? "zone";
            Log.Message($"Entered zone placement for {zoneName}");
        }
    }
}
