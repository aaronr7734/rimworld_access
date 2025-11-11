using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to add Tab key for opening the accessible architect menu.
    /// Handles category selection, tool selection, and material selection.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class ArchitectMenuPatch
    {
        private static float lastArchitectKeyTime = 0f;
        private const float ArchitectKeyCooldown = 0.3f;

        /// <summary>
        /// Prefix patch to check for A key press at GUI event level.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        public static void Prefix()
        {
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

            // Open the architect category menu
            OpenCategoryMenu();

            // Consume the event
            Event.current.Use();
        }

        /// <summary>
        /// Opens the category selection menu.
        /// </summary>
        private static void OpenCategoryMenu()
        {
            // Get all visible categories
            List<DesignationCategoryDef> categories = ArchitectHelper.GetAllCategories();

            if (categories.Count == 0)
            {
                TolkHelper.Speak("No architect categories available");
                return;
            }

            // Create menu options
            List<FloatMenuOption> options = ArchitectHelper.CreateCategoryOptions(
                categories,
                OnCategorySelected
            );

            // Enter category selection mode
            ArchitectState.EnterCategorySelection();

            // Open the windowless menu
            WindowlessFloatMenuState.Open(options, false); // false = doesn't give colonist orders

            MelonLoader.MelonLogger.Msg("Opened architect category menu");
        }

        /// <summary>
        /// Called when a category is selected from the menu.
        /// </summary>
        private static void OnCategorySelected(DesignationCategoryDef category)
        {
            // Get all designators in this category
            List<Designator> designators = ArchitectHelper.GetDesignatorsForCategory(category);

            if (designators.Count == 0)
            {
                TolkHelper.Speak($"No tools available in {category.LabelCap}");
                ArchitectState.Reset();
                return;
            }

            // Enter tool selection mode
            ArchitectState.EnterToolSelection(category);

            // Create menu options for designators
            List<FloatMenuOption> options = ArchitectHelper.CreateDesignatorOptions(
                designators,
                OnDesignatorSelected
            );

            // Open the windowless menu
            WindowlessFloatMenuState.Open(options, false);

            MelonLoader.MelonLogger.Msg($"Opened tool menu for category: {category.defName}");
        }

        /// <summary>
        /// Called when a designator (tool) is selected from the menu.
        /// </summary>
        private static void OnDesignatorSelected(Designator designator)
        {
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

            MelonLoader.MelonLogger.Msg($"Opened material menu for: {buildable.defName}");
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
    }
}
