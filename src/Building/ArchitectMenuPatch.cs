using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

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
            if (ArchitectTreeState.IsActive)
            {
                if (Event.current.type == EventType.KeyDown)
                {
                    HandleArchitectTreeInput();
                }
                return;
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
        /// </summary>
        private static void HandleArchitectTreeInput()
        {
            KeyCode key = Event.current.keyCode;

            // Handle Escape - clear search first, then close
            if (key == KeyCode.Escape)
            {
                if (ArchitectTreeState.HasActiveSearch)
                {
                    ArchitectTreeState.ClearTypeaheadSearch();
                }
                else
                {
                    ArchitectTreeState.Close();
                    ArchitectState.Reset(); // Also reset ArchitectState so Tab works again
                    TolkHelper.Speak("Architect menu closed");
                }
                Event.current.Use();
                return;
            }

            // Handle Home - jump to first
            if (key == KeyCode.Home)
            {
                ArchitectTreeState.JumpToFirst();
                Event.current.Use();
                return;
            }

            // Handle End - jump to last
            if (key == KeyCode.End)
            {
                ArchitectTreeState.JumpToLast();
                Event.current.Use();
                return;
            }

            // Handle Backspace for search
            if (key == KeyCode.Backspace && ArchitectTreeState.HasActiveSearch)
            {
                ArchitectTreeState.ProcessBackspace();
                Event.current.Use();
                return;
            }

            // Handle * key - expand all sibling categories
            bool isStar = key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8);
            if (isStar)
            {
                ArchitectTreeState.ExpandAllSiblings();
                Event.current.Use();
                return;
            }

            // Handle Up/Down with typeahead filtering
            if (key == KeyCode.UpArrow)
            {
                if (ArchitectTreeState.HasActiveSearch && !ArchitectTreeState.HasNoMatches)
                {
                    ArchitectTreeState.SelectPreviousMatch();
                }
                else
                {
                    ArchitectTreeState.SelectPrevious();
                }
                Event.current.Use();
                return;
            }

            if (key == KeyCode.DownArrow)
            {
                if (ArchitectTreeState.HasActiveSearch && !ArchitectTreeState.HasNoMatches)
                {
                    ArchitectTreeState.SelectNextMatch();
                }
                else
                {
                    ArchitectTreeState.SelectNext();
                }
                Event.current.Use();
                return;
            }

            // Handle Right arrow - expand or move to first child
            if (key == KeyCode.RightArrow)
            {
                ArchitectTreeState.ExpandCurrent();
                Event.current.Use();
                return;
            }

            // Handle Left arrow - collapse or move to parent
            if (key == KeyCode.LeftArrow)
            {
                ArchitectTreeState.CollapseCurrent();
                Event.current.Use();
                return;
            }

            // Handle Enter - activate current item
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                ArchitectTreeState.ActivateCurrent();
                Event.current.Use();
                return;
            }

            // Handle typeahead search characters (letters only)
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            if (isLetter && !Event.current.alt && !Event.current.shift)
            {
                char c = (char)('a' + (key - KeyCode.A));
                ArchitectTreeState.ProcessTypeaheadCharacter(c);
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
        /// Uses reflection to check the type hierarchy since we can't directly reference RimWorld types.
        /// </summary>
        private static bool IsZoneDesignator(Designator designator)
        {
            if (designator == null)
                return false;

            // Check if this designator's type hierarchy includes "Designator_Cells"
            // This covers all multi-cell designators: zones, areas, roofs, etc.
            System.Type type = designator.GetType();
            while (type != null)
            {
                if (type.Name == "Designator_Cells")
                    return true;
                type = type.BaseType;
            }

            return false;
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
