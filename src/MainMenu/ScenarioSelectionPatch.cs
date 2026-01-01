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

                // Initialize navigation state
                ScenarioNavigationState.Initialize(allScenarios);

                // Announce window title and initial selection once
                if (!hasAnnouncedTitle)
                {
                    string pageTitle = "Choose Scenario";
                    Scenario firstScenario = ScenarioNavigationState.SelectedScenario;
                    if (firstScenario != null)
                    {
                        string categoryPrefix = GetCategoryPrefixString(firstScenario.Category);
                        TolkHelper.Speak($"{pageTitle} - {categoryPrefix}{firstScenario.name} - {firstScenario.summary}");
                    }
                    else
                    {
                        TolkHelper.Speak(pageTitle);
                    }
                    hasAnnouncedTitle = true;
                }

                // Handle keyboard input
                if (Event.current.type == EventType.KeyDown)
                {
                    KeyCode keyCode = Event.current.keyCode;

                    if (keyCode == KeyCode.UpArrow)
                    {
                        ScenarioNavigationState.NavigateUp();

                        // Update the page's current scenario selection
                        Scenario selected = ScenarioNavigationState.SelectedScenario;
                        if (selected != null)
                        {
                            AccessTools.Field(typeof(Page_SelectScenario), "curScen").SetValue(__instance, selected);
                        }

                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.DownArrow)
                    {
                        ScenarioNavigationState.NavigateDown();

                        // Update the page's current scenario selection
                        Scenario selected = ScenarioNavigationState.SelectedScenario;
                        if (selected != null)
                        {
                            AccessTools.Field(typeof(Page_SelectScenario), "curScen").SetValue(__instance, selected);
                        }

                        Event.current.Use();
                        patchActive = true;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in ScenarioSelectionPatch Prefix: {ex}");
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

        private static string GetCategoryPrefixString(ScenarioCategory category)
        {
            switch (category)
            {
                case ScenarioCategory.FromDef:
                    return "[Built-in] ";
                case ScenarioCategory.CustomLocal:
                    return "[Custom] ";
                case ScenarioCategory.SteamWorkshop:
                    return "[Workshop] ";
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
