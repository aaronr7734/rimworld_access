using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(Page_CreateWorldParams))]
    [HarmonyPatch("DoWindowContents")]
    public class WorldParamsPatch
    {
        private static bool patchActive = false;
        private static bool hasAnnouncedTitle = false;
        private static readonly float[] PlanetCoverages = new float[] { 0.3f, 0.5f, 1f };

        // Prefix: Handle keyboard input for navigation and value changes
        static void Prefix(Page_CreateWorldParams __instance, Rect rect)
        {
            try
            {
                // Initialize navigation state
                WorldParamsNavigationState.Initialize();

                // Announce window title and initial field once
                if (!hasAnnouncedTitle)
                {
                    string pageTitle = "Create World";
                    TolkHelper.Speak($"{pageTitle} - Use Up/Down to navigate fields, Left/Right to change values, R to randomize seed");
                    hasAnnouncedTitle = true;
                }

                // Handle keyboard input
                if (Event.current.type == EventType.KeyDown)
                {
                    KeyCode keyCode = Event.current.keyCode;

                    // Check if we're in seed text input mode
                    if (WorldParamsNavigationState.IsEditingSeed)
                    {
                        // Handle text input for seed
                        if (keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter)
                        {
                            // Confirm seed input
                            string newSeed = WorldParamsNavigationState.ConfirmSeedEdit();
                            if (!string.IsNullOrEmpty(newSeed))
                            {
                                AccessTools.Field(typeof(Page_CreateWorldParams), "seedString").SetValue(__instance, newSeed);
                                TolkHelper.Speak($"World Seed: {newSeed} (Confirmed)");
                            }
                            else
                            {
                                TolkHelper.Speak("Seed input canceled (empty)");
                            }
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.Escape)
                        {
                            // Cancel seed input
                            WorldParamsNavigationState.CancelSeedEdit();
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.Backspace)
                        {
                            // Remove character
                            WorldParamsNavigationState.RemoveCharFromSeedBuffer();
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (Event.current.character != '\0' && !char.IsControl(Event.current.character))
                        {
                            // Add character to buffer
                            WorldParamsNavigationState.AddCharToSeedBuffer(Event.current.character);
                            Event.current.Use();
                            patchActive = true;
                        }
                    }
                    else
                    {
                        // Normal navigation mode
                        if (keyCode == KeyCode.UpArrow)
                        {
                            WorldParamsNavigationState.NavigateUp();
                            CopyCurrentFieldValue(__instance);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.DownArrow)
                        {
                            WorldParamsNavigationState.NavigateDown();
                            CopyCurrentFieldValue(__instance);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.LeftArrow)
                        {
                            ModifyCurrentField(__instance, -1);
                            CopyCurrentFieldValue(__instance);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.RightArrow)
                        {
                            ModifyCurrentField(__instance, 1);
                            CopyCurrentFieldValue(__instance);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter)
                        {
                            // Enter key on Seed field starts text input mode
                            if (WorldParamsNavigationState.GetCurrentFieldName() == "Seed")
                            {
                                string currentSeed = (string)AccessTools.Field(typeof(Page_CreateWorldParams), "seedString").GetValue(__instance);
                                WorldParamsNavigationState.StartSeedEdit(currentSeed);
                                Event.current.Use();
                                patchActive = true;
                            }
                        }
                        else if (keyCode == KeyCode.R)
                        {
                            // Randomize seed
                            if (WorldParamsNavigationState.GetCurrentFieldName() == "Seed")
                            {
                                string newSeed = GenText.RandomSeedString();
                                AccessTools.Field(typeof(Page_CreateWorldParams), "seedString").SetValue(__instance, newSeed);
                                TolkHelper.Speak($"World Seed: {newSeed} (Randomized)");
                                Event.current.Use();
                                patchActive = true;
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in WorldParamsPatch Prefix: {ex}");
            }
        }

        private static void ModifyCurrentField(Page_CreateWorldParams instance, int direction)
        {
            string fieldName = WorldParamsNavigationState.GetCurrentFieldName();

            switch (fieldName)
            {
                case "Seed":
                    // Can't modify seed with arrows, use R to randomize or Enter to type
                    TolkHelper.Speak("Press Enter to type custom seed, or R to randomize");
                    break;

                case "PlanetCoverage":
                    ModifyPlanetCoverage(instance, direction);
                    break;

                case "Rainfall":
                    ModifyRainfall(instance, direction);
                    break;

                case "Temperature":
                    ModifyTemperature(instance, direction);
                    break;

                case "Population":
                    ModifyPopulation(instance, direction);
                    break;

                case "LandmarkDensity":
                    ModifyLandmarkDensity(instance, direction);
                    break;

                case "Pollution":
                    ModifyPollution(instance, direction);
                    break;
            }
        }

        private static void ModifyPlanetCoverage(Page_CreateWorldParams instance, int direction)
        {
            float currentCoverage = (float)AccessTools.Field(typeof(Page_CreateWorldParams), "planetCoverage").GetValue(instance);

            // Find current index
            int currentIndex = 0;
            for (int i = 0; i < PlanetCoverages.Length; i++)
            {
                if (Mathf.Approximately(currentCoverage, PlanetCoverages[i]))
                {
                    currentIndex = i;
                    break;
                }
            }

            // Apply direction
            currentIndex += direction;
            if (currentIndex < 0) currentIndex = PlanetCoverages.Length - 1;
            if (currentIndex >= PlanetCoverages.Length) currentIndex = 0;

            float newCoverage = PlanetCoverages[currentIndex];
            AccessTools.Field(typeof(Page_CreateWorldParams), "planetCoverage").SetValue(instance, newCoverage);
        }

        private static void ModifyRainfall(Page_CreateWorldParams instance, int direction)
        {
            OverallRainfall current = (OverallRainfall)AccessTools.Field(typeof(Page_CreateWorldParams), "rainfall").GetValue(instance);
            int newValue = (int)current + direction;

            // Wrap around (0 = Low, 1 = Normal, 2 = High)
            if (newValue < 0) newValue = 2;
            if (newValue > 2) newValue = 0;

            AccessTools.Field(typeof(Page_CreateWorldParams), "rainfall").SetValue(instance, (OverallRainfall)newValue);
        }

        private static void ModifyTemperature(Page_CreateWorldParams instance, int direction)
        {
            OverallTemperature current = (OverallTemperature)AccessTools.Field(typeof(Page_CreateWorldParams), "temperature").GetValue(instance);
            int newValue = (int)current + direction;

            if (newValue < 0) newValue = 2;
            if (newValue > 2) newValue = 0;

            AccessTools.Field(typeof(Page_CreateWorldParams), "temperature").SetValue(instance, (OverallTemperature)newValue);
        }

        private static void ModifyPopulation(Page_CreateWorldParams instance, int direction)
        {
            OverallPopulation current = (OverallPopulation)AccessTools.Field(typeof(Page_CreateWorldParams), "population").GetValue(instance);
            int newValue = (int)current + direction;

            if (newValue < 0) newValue = 2;
            if (newValue > 2) newValue = 0;

            AccessTools.Field(typeof(Page_CreateWorldParams), "population").SetValue(instance, (OverallPopulation)newValue);
        }

        private static void ModifyLandmarkDensity(Page_CreateWorldParams instance, int direction)
        {
            LandmarkDensity current = (LandmarkDensity)AccessTools.Field(typeof(Page_CreateWorldParams), "landmarkDensity").GetValue(instance);
            int newValue = (int)current + direction;

            if (newValue < 0) newValue = 2;
            if (newValue > 2) newValue = 0;

            AccessTools.Field(typeof(Page_CreateWorldParams), "landmarkDensity").SetValue(instance, (LandmarkDensity)newValue);
        }

        private static void ModifyPollution(Page_CreateWorldParams instance, int direction)
        {
            float current = (float)AccessTools.Field(typeof(Page_CreateWorldParams), "pollution").GetValue(instance);
            float step = 0.05f; // 5% increments

            float newValue = current + (direction * step);

            // Clamp between 0 and 1
            if (newValue < 0f) newValue = 0f;
            if (newValue > 1f) newValue = 1f;

            AccessTools.Field(typeof(Page_CreateWorldParams), "pollution").SetValue(instance, newValue);
        }

        private static void CopyCurrentFieldValue(Page_CreateWorldParams instance)
        {
            string seedString = (string)AccessTools.Field(typeof(Page_CreateWorldParams), "seedString").GetValue(instance);
            float planetCoverage = (float)AccessTools.Field(typeof(Page_CreateWorldParams), "planetCoverage").GetValue(instance);
            OverallRainfall rainfall = (OverallRainfall)AccessTools.Field(typeof(Page_CreateWorldParams), "rainfall").GetValue(instance);
            OverallTemperature temperature = (OverallTemperature)AccessTools.Field(typeof(Page_CreateWorldParams), "temperature").GetValue(instance);
            OverallPopulation population = (OverallPopulation)AccessTools.Field(typeof(Page_CreateWorldParams), "population").GetValue(instance);
            LandmarkDensity landmarkDensity = (LandmarkDensity)AccessTools.Field(typeof(Page_CreateWorldParams), "landmarkDensity").GetValue(instance);
            float pollution = (float)AccessTools.Field(typeof(Page_CreateWorldParams), "pollution").GetValue(instance);

            WorldParamsNavigationState.CopyFieldValue(seedString, planetCoverage, rainfall, temperature, population, landmarkDensity, pollution);
        }

        // Postfix: Draw visual indicator
        static void Postfix(Page_CreateWorldParams __instance, Rect rect)
        {
            try
            {
                if (!patchActive) return;

                // Draw indicator of current field at top
                Rect indicatorRect = new Rect(rect.x + 10f, rect.y + 10f, 400f, 30f);
                string text;

                if (WorldParamsNavigationState.IsEditingSeed)
                {
                    text = $"[Typing Seed: {WorldParamsNavigationState.SeedInputBuffer}] (Enter=Confirm, Esc=Cancel)";
                }
                else
                {
                    string fieldName = WorldParamsNavigationState.GetCurrentFieldName();
                    text = $"[Editing: {fieldName}] (Use Arrow Keys)";
                }

                Widgets.DrawBoxSolid(indicatorRect, new Color(0.2f, 0.2f, 0.2f, 0.8f));
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(indicatorRect, text);
                Text.Anchor = TextAnchor.UpperLeft;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in WorldParamsPatch Postfix: {ex}");
            }
        }

        public static void ResetAnnouncement()
        {
            hasAnnouncedTitle = false;
        }
    }

    // Separate patch to reset state when page opens
    [HarmonyPatch(typeof(Page_CreateWorldParams), "PreOpen")]
    public class WorldParamsPatch_PreOpen
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            WorldParamsPatch.ResetAnnouncement();
            WorldParamsNavigationState.Reset();
        }
    }
}
