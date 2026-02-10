using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(Page_SelectScenario))]
    [HarmonyPatch("DoWindowContents")]
    public partial class ScenarioSelectionPatch
    {
        private static bool patchActive = false;
        private static bool hasAnnouncedTitle = false;

        // Special marker for the "Scenario Builder" placeholder entry
        public const string ScenarioBuilderMarker = "__SCENARIO_BUILDER__";

        /// <summary>
        /// Checks if the currently selected item is the Scenario Builder placeholder.
        /// </summary>
        public static bool IsScenarioBuilderSelected()
        {
            return ScenarioNavigationState.IsScenarioBuilderSelected;
        }

        /// <summary>
        /// Opens the Scenario Builder (Page_ScenarioEditor) with a new scenario.
        /// </summary>
        public static void OpenScenarioBuilder(Page_SelectScenario page)
        {
            // Create new scenario editor with null scenario (generates random)
            Page_ScenarioEditor editor = new Page_ScenarioEditor(null);
            editor.prev = page;
            Find.WindowStack.Add(editor);
            page.Close();
        }

        /// <summary>
        /// Opens the Scenario Editor for the currently selected scenario.
        /// Matches the game's CanEditScenario logic:
        /// - CustomLocal scenarios can be edited directly
        /// - Your own workshop uploads can be edited directly
        /// - Built-in and others' workshop scenarios get copied
        /// </summary>
        public static void OpenScenarioEditor(Page_SelectScenario page)
        {
            Scenario selected = ScenarioNavigationState.SelectedScenario;
            if (selected == null) return;

            // Match the game's CanEditScenario logic exactly
            bool canEditDirectly = selected.Category == ScenarioCategory.CustomLocal
                                || selected.CanToUploadToWorkshop();

            Scenario scenarioToEdit;
            string actionDescription;

            if (canEditDirectly)
            {
                scenarioToEdit = selected;
                actionDescription = $"Editing scenario: {selected.name}";
            }
            else
            {
                // Create an editable copy for built-in or others' workshop scenarios
                scenarioToEdit = selected.CopyForEditing();
                actionDescription = $"Editing copy of: {selected.name}";
            }

            Page_ScenarioEditor editor = new Page_ScenarioEditor(scenarioToEdit);
            editor.prev = page;
            Find.WindowStack.Add(editor);
            page.Close();

            TolkHelper.Speak(actionDescription);
        }

        // Prefix: Build flat scenario list and handle keyboard input
        static void Prefix(Page_SelectScenario __instance, Rect rect)
        {
            try
            {
                // Build flat list of all scenarios
                List<Scenario> allScenarios = new List<Scenario>();

                // Add built-in scenarios
                var builtInScenarios = ScenarioLister.ScenariosInCategory(ScenarioCategory.FromDef).Where(s => s.showInUI);
                allScenarios.AddRange(builtInScenarios);

                // Add custom local scenarios
                var customScenarios = ScenarioLister.ScenariosInCategory(ScenarioCategory.CustomLocal).Where(s => s.showInUI);
                allScenarios.AddRange(customScenarios);

                // Add Steam Workshop scenarios
                var workshopScenarios = ScenarioLister.ScenariosInCategory(ScenarioCategory.SteamWorkshop).Where(s => s.showInUI);
                allScenarios.AddRange(workshopScenarios);

                // Initialize navigation state (with scenario builder placeholder added internally)
                ScenarioNavigationState.Initialize(allScenarios);

                // Announce window title and initial selection once
                if (!hasAnnouncedTitle)
                {
                    string pageTitle = "Choose Scenario";
                    Scenario firstScenario = ScenarioNavigationState.SelectedScenario;
                    if (firstScenario != null)
                    {
                        string categorySuffix = GetCategorySuffixString(firstScenario.Category);
                        TolkHelper.Speak($"{pageTitle} - {firstScenario.name} - {firstScenario.summary}{categorySuffix}. Tab for details, Alt+E to edit.");
                    }
                    else
                    {
                        TolkHelper.Speak(pageTitle);
                    }
                    hasAnnouncedTitle = true;
                }

                // Handle keyboard input
                // Skip if a dialog is active - let the dialog handle input
                if (Event.current.type == EventType.KeyDown && !WindowlessDialogState.IsActive)
                {
                    KeyCode keyCode = Event.current.keyCode;

                    // Route to detail panel navigation if active
                    if (ScenarioNavigationState.DetailPanelActive)
                    {
                        if (keyCode == KeyCode.UpArrow)
                        {
                            // If active search, navigate to previous match
                            if (ScenarioNavigationState.HasActiveSearch)
                            {
                                ScenarioNavigationState.SelectPreviousMatch();
                            }
                            else
                            {
                                ScenarioNavigationState.NavigateDetailUp();
                            }
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.DownArrow)
                        {
                            // If active search, navigate to next match
                            if (ScenarioNavigationState.HasActiveSearch)
                            {
                                ScenarioNavigationState.SelectNextMatch();
                            }
                            else
                            {
                                ScenarioNavigationState.NavigateDetailDown();
                            }
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.RightArrow)
                        {
                            // Expand current item or drill down to first child
                            ScenarioNavigationState.ExpandOrDrillDown();
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.LeftArrow)
                        {
                            // Collapse current item or drill up to parent
                            ScenarioNavigationState.CollapseOrDrillUp();
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.Home)
                        {
                            // Home jumps to first sibling, Ctrl+Home to absolute first
                            ScenarioNavigationState.HandleHomeKey(Event.current.control);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.End)
                        {
                            // End jumps to last sibling, Ctrl+End to absolute last
                            ScenarioNavigationState.HandleEndKey(Event.current.control);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.Escape)
                        {
                            // Escape clears search first, then exits detail panel
                            if (ScenarioNavigationState.HasActiveSearch)
                            {
                                ScenarioNavigationState.ClearTypeaheadSearch();
                            }
                            else
                            {
                                ScenarioNavigationState.ToggleDetailPanel();
                            }
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.Tab)
                        {
                            // Tab exits detail panel
                            ScenarioNavigationState.ToggleDetailPanel();
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.Backspace)
                        {
                            // Backspace removes last character from search
                            if (ScenarioNavigationState.HandleTypeaheadBackspace())
                            {
                                Event.current.Use();
                                patchActive = true;
                            }
                        }
                        else if (Event.current.character != '\0' &&
                                 !Event.current.control && !Event.current.alt &&
                                 char.IsLetterOrDigit(Event.current.character))
                        {
                            // Handle typeahead search for printable characters
                            ScenarioNavigationState.HandleTypeahead(Event.current.character);
                            Event.current.Use();
                            patchActive = true;
                        }
                    }
                    else
                    {
                        // Normal scenario list navigation

                        // Handle Alt+E to edit the selected scenario
                        if (Event.current.alt && keyCode == KeyCode.E)
                        {
                            if (ScenarioNavigationState.IsScenarioBuilderSelected)
                            {
                                TolkHelper.Speak("Cannot edit Scenario Builder entry. Press Enter to create a new scenario.");
                            }
                            else
                            {
                                OpenScenarioEditor(__instance);
                            }
                            Event.current.Use();
                            patchActive = true;
                            return;
                        }

                        // Handle Enter key to open scenario builder or select scenario
                        if (keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter)
                        {
                            if (ScenarioNavigationState.IsScenarioBuilderSelected)
                            {
                                // Open the Scenario Builder
                                OpenScenarioBuilder(__instance);
                                Event.current.Use();
                                patchActive = true;
                                return;
                            }
                            // Otherwise let Enter pass through to select the scenario normally
                        }

                        if (keyCode == KeyCode.UpArrow)
                        {
                            // If active search, navigate to previous match
                            if (ScenarioNavigationState.ListHasActiveSearch)
                            {
                                ScenarioNavigationState.SelectPreviousListMatch();
                            }
                            else
                            {
                                ScenarioNavigationState.NavigateUp();
                            }
                            SyncPageSelection(__instance);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.DownArrow)
                        {
                            // If active search, navigate to next match
                            if (ScenarioNavigationState.ListHasActiveSearch)
                            {
                                ScenarioNavigationState.SelectNextListMatch();
                            }
                            else
                            {
                                ScenarioNavigationState.NavigateDown();
                            }
                            SyncPageSelection(__instance);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.Home)
                        {
                            ScenarioNavigationState.NavigateHome();
                            SyncPageSelection(__instance);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.End)
                        {
                            ScenarioNavigationState.NavigateEnd();
                            SyncPageSelection(__instance);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.Tab)
                        {
                            ScenarioNavigationState.ToggleDetailPanel();
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.Delete)
                        {
                            // Delete key: delete custom scenarios or unsubscribe from workshop scenarios
                            if (ScenarioNavigationState.IsScenarioBuilderSelected)
                            {
                                TolkHelper.Speak("Cannot delete Scenario Builder entry");
                            }
                            else if (ScenarioNavigationState.CanDeleteSelectedScenario())
                            {
                                ScenarioNavigationState.DeleteSelectedScenario(__instance);
                            }
                            else if (ScenarioNavigationState.IsWorkshopScenario())
                            {
                                ScenarioNavigationState.UnsubscribeSelectedScenario(__instance);
                            }
                            else
                            {
                                TolkHelper.Speak("Cannot delete built-in scenarios");
                            }
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.Escape)
                        {
                            // Escape clears search if active
                            if (ScenarioNavigationState.ListHasActiveSearch)
                            {
                                ScenarioNavigationState.ClearListTypeaheadSearch();
                                Event.current.Use();
                                patchActive = true;
                            }
                            // Otherwise let it pass through to close the page
                        }
                        else if (keyCode == KeyCode.Backspace)
                        {
                            // Backspace removes last character from search
                            if (ScenarioNavigationState.HandleListTypeaheadBackspace())
                            {
                                SyncPageSelection(__instance);
                                Event.current.Use();
                                patchActive = true;
                            }
                        }
                        else if (Event.current.character != '\0' &&
                                 !Event.current.control && !Event.current.alt &&
                                 char.IsLetterOrDigit(Event.current.character))
                        {
                            // Handle typeahead search for printable characters
                            ScenarioNavigationState.HandleListTypeahead(Event.current.character);
                            SyncPageSelection(__instance);
                            Event.current.Use();
                            patchActive = true;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in ScenarioSelectionPatch Prefix: {ex}");
            }
        }

        // Helper to sync the page's current scenario selection with our navigation state
        private static void SyncPageSelection(Page_SelectScenario instance)
        {
            Scenario selected = ScenarioNavigationState.SelectedScenario;
            if (selected != null)
            {
                AccessTools.Field(typeof(Page_SelectScenario), "curScen").SetValue(instance, selected);
            }
        }

        // Postfix: Draw visual highlight on selected scenario
        static void Postfix(Page_SelectScenario __instance, Rect rect)
        {
            try
            {
                if (!patchActive) return;

                Scenario selectedScenario = ScenarioNavigationState.SelectedScenario;
                if (selectedScenario == null) return;

                // Get the current scenario from the page to verify sync
                Scenario curScen = (Scenario)AccessTools.Field(typeof(Page_SelectScenario), "curScen").GetValue(__instance);
                if (curScen != selectedScenario)
                {
                    // Sync the selection
                    AccessTools.Field(typeof(Page_SelectScenario), "curScen").SetValue(__instance, selectedScenario);
                }

                // Calculate highlight position
                // The scenario list starts at mainRect which is GetMainRect(rect)
                // Left panel is 35% of main rect width
                Rect mainRect = GetMainRect(__instance, rect);
                float leftPanelWidth = mainRect.width * 0.35f;

                // Calculate vertical offset
                // We need to find which index in the full list (including categories) our scenario is
                int visualIndex = CalculateVisualIndex(selectedScenario);

                // Each scenario entry is 68px + 6px gap (except first)
                float yOffset = 0f;
                if (visualIndex > 0)
                {
                    yOffset = visualIndex * (68f + 6f);
                }

                // Account for category labels and gaps
                yOffset += GetCategoryHeaderOffset(selectedScenario);

                // Draw highlight rectangle
                Rect highlightRect = new Rect(
                    mainRect.x + 4f,
                    mainRect.y + yOffset,
                    leftPanelWidth - 8f,
                    68f
                );

                // Draw a colored overlay
                Color highlightColor = new Color(0.3f, 0.7f, 1f, 0.3f);
                Widgets.DrawBoxSolid(highlightRect, highlightColor);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in ScenarioSelectionPatch Postfix: {ex}");
            }
        }

        private static Rect GetMainRect(Page_SelectScenario instance, Rect rect)
        {
            // Replicate the GetMainRect logic from Page base class
            float y = 45f; // Title area
            float height = rect.height - 45f - 38f; // Subtract title and bottom buttons
            return new Rect(0f, y, rect.width, height);
        }

        private static int CalculateVisualIndex(Scenario scenario)
        {
            int index = 0;

            // Count scenarios in built-in category
            var builtInScenarios = ScenarioLister.ScenariosInCategory(ScenarioCategory.FromDef).Where(s => s.showInUI);
            foreach (var scen in builtInScenarios)
            {
                if (scen == scenario) return index;
                index++;
            }

            // Count scenarios in custom category
            var customScenarios = ScenarioLister.ScenariosInCategory(ScenarioCategory.CustomLocal).Where(s => s.showInUI);
            foreach (var scen in customScenarios)
            {
                if (scen == scenario) return index;
                index++;
            }

            // Count scenarios in workshop category
            var workshopScenarios = ScenarioLister.ScenariosInCategory(ScenarioCategory.SteamWorkshop).Where(s => s.showInUI);
            foreach (var scen in workshopScenarios)
            {
                if (scen == scenario) return index;
                index++;
            }

            return index;
        }

        private static float GetCategoryHeaderOffset(Scenario scenario)
        {
            float offset = 0f;

            // Built-in scenarios have no header above them (they're first)
            if (scenario.Category == ScenarioCategory.FromDef)
            {
                return 0f;
            }

            // Custom scenarios have one category header + gap
            if (scenario.Category == ScenarioCategory.CustomLocal)
            {
                offset += 30f; // Gap after built-in section
                offset += 24f; // "ScenariosCustom" label height
                return offset;
            }

            // Workshop scenarios have two category headers + gaps
            if (scenario.Category == ScenarioCategory.SteamWorkshop)
            {
                offset += 30f; // Gap after built-in section
                offset += 24f; // "ScenariosCustom" label
                offset += 30f; // Gap after custom section
                offset += 24f; // "ScenariosSteamWorkshop" label
                offset += 38f; // "OpenSteamWorkshop" button
                return offset;
            }

            return offset;
        }

        private static string GetCategorySuffixString(ScenarioCategory category)
        {
            switch (category)
            {
                case ScenarioCategory.FromDef:
                    return " (Built-in)";
                case ScenarioCategory.CustomLocal:
                    return " (Custom)";
                case ScenarioCategory.SteamWorkshop:
                    return " (Workshop)";
                default:
                    return "";
            }
        }
    }

    // Separate patch to reset state when page opens
    [HarmonyPatch(typeof(Page_SelectScenario), "PreOpen")]
    public class ScenarioSelectionPatch_PreOpen
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            ScenarioSelectionPatch.ResetAnnouncement();
            ScenarioNavigationState.Reset();
        }
    }

    // Extension to ScenarioSelectionPatch for state reset
    public partial class ScenarioSelectionPatch
    {
        public static void ResetAnnouncement()
        {
            hasAnnouncedTitle = false;
        }
    }
}
