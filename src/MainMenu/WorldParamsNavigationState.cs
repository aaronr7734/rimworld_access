using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    public static class WorldParamsNavigationState
    {
        private static bool initialized = false;
        private static int currentFieldIndex = 0;

        // Text input state for seed editing
        private static bool isEditingSeed = false;
        private static string seedInputBuffer = "";

        // Field identifiers
        private enum WorldParamField
        {
            Seed,
            PlanetCoverage,
            Rainfall,
            Temperature,
            Population,
            LandmarkDensity, // Odyssey only
            Pollution        // Biotech only
        }

        private static List<WorldParamField> availableFields = new List<WorldParamField>();

        public static void Initialize()
        {
            if (!initialized)
            {
                // Build list of available fields based on active mods
                availableFields.Clear();
                availableFields.Add(WorldParamField.Seed);
                availableFields.Add(WorldParamField.PlanetCoverage);
                availableFields.Add(WorldParamField.Rainfall);
                availableFields.Add(WorldParamField.Temperature);
                availableFields.Add(WorldParamField.Population);

                if (ModsConfig.OdysseyActive)
                {
                    availableFields.Add(WorldParamField.LandmarkDensity);
                }

                if (ModsConfig.BiotechActive)
                {
                    availableFields.Add(WorldParamField.Pollution);
                }

                currentFieldIndex = 0;
                initialized = true;
            }
        }

        public static void Reset()
        {
            initialized = false;
            currentFieldIndex = 0;
            availableFields.Clear();
        }

        public static void NavigateUp()
        {
            if (availableFields.Count == 0) return;

            currentFieldIndex--;
            if (currentFieldIndex < 0)
                currentFieldIndex = availableFields.Count - 1;

            CopyCurrentFieldToClipboard();
        }

        public static void NavigateDown()
        {
            if (availableFields.Count == 0) return;

            currentFieldIndex++;
            if (currentFieldIndex >= availableFields.Count)
                currentFieldIndex = 0;

            CopyCurrentFieldToClipboard();
        }

        public static string GetCurrentFieldName()
        {
            if (currentFieldIndex < 0 || currentFieldIndex >= availableFields.Count)
                return "Unknown";

            return availableFields[currentFieldIndex].ToString();
        }

        public static void CopyFieldValue(string seedString, float planetCoverage, OverallRainfall rainfall,
            OverallTemperature temperature, OverallPopulation population, LandmarkDensity landmarkDensity, float pollution)
        {
            if (currentFieldIndex < 0 || currentFieldIndex >= availableFields.Count)
                return;

            WorldParamField currentField = availableFields[currentFieldIndex];
            string fieldName = "";
            string fieldValue = "";

            switch (currentField)
            {
                case WorldParamField.Seed:
                    fieldName = "World Seed";
                    fieldValue = seedString;
                    break;

                case WorldParamField.PlanetCoverage:
                    fieldName = "Planet Coverage";
                    fieldValue = planetCoverage.ToStringPercent();
                    break;

                case WorldParamField.Rainfall:
                    fieldName = "Rainfall";
                    fieldValue = GetRainfallLabel(rainfall);
                    break;

                case WorldParamField.Temperature:
                    fieldName = "Temperature";
                    fieldValue = GetTemperatureLabel(temperature);
                    break;

                case WorldParamField.Population:
                    fieldName = "Population";
                    fieldValue = GetPopulationLabel(population);
                    break;

                case WorldParamField.LandmarkDensity:
                    fieldName = "Landmark Density";
                    fieldValue = GetLandmarkDensityLabel(landmarkDensity);
                    break;

                case WorldParamField.Pollution:
                    fieldName = "Pollution";
                    fieldValue = pollution.ToStringPercent();
                    break;
            }

            string text = $"{fieldName}: {fieldValue}";
            TolkHelper.Speak(text);
        }

        private static void CopyCurrentFieldToClipboard()
        {
            if (currentFieldIndex < 0 || currentFieldIndex >= availableFields.Count)
                return;

            WorldParamField currentField = availableFields[currentFieldIndex];
            string fieldName = "";
            string description = "";

            switch (currentField)
            {
                case WorldParamField.Seed:
                    fieldName = "World Seed";
                    description = "Random seed for world generation. Press Enter to type custom seed, or R to randomize.";
                    break;

                case WorldParamField.PlanetCoverage:
                    fieldName = "Planet Coverage";
                    description = "How much of the planet to generate. Use Left/Right to change (30%, 50%, 100%).";
                    break;

                case WorldParamField.Rainfall:
                    fieldName = "Rainfall";
                    description = "Overall planet rainfall level. Use Left/Right to adjust (Low, Normal, High).";
                    break;

                case WorldParamField.Temperature:
                    fieldName = "Temperature";
                    description = "Overall planet temperature. Use Left/Right to adjust (Low, Normal, High).";
                    break;

                case WorldParamField.Population:
                    fieldName = "Population";
                    description = "World population density. Use Left/Right to adjust (Low, Normal, High).";
                    break;

                case WorldParamField.LandmarkDensity:
                    fieldName = "Landmark Density";
                    description = "Density of landmarks (Odyssey). Use Left/Right to adjust (Low, Normal, High).";
                    break;

                case WorldParamField.Pollution:
                    fieldName = "Pollution";
                    description = "Starting pollution level (Biotech). Use Left/Right to adjust (0-100%).";
                    break;
            }

            string text = $"[Field] {fieldName} - {description}";
            TolkHelper.Speak(text);
        }

        private static string GetRainfallLabel(OverallRainfall rainfall)
        {
            // Enum values: 0=Low, 1=Normal, 2=High
            if ((int)rainfall == 0) return "Low";
            if ((int)rainfall == 1) return "Normal";
            if ((int)rainfall == 2) return "High";
            return rainfall.ToString();
        }

        private static string GetTemperatureLabel(OverallTemperature temperature)
        {
            // Enum values: 0=Low, 1=Normal, 2=High
            if ((int)temperature == 0) return "Low";
            if ((int)temperature == 1) return "Normal";
            if ((int)temperature == 2) return "High";
            return temperature.ToString();
        }

        private static string GetPopulationLabel(OverallPopulation population)
        {
            // Enum values: 0=Low, 1=Normal, 2=High
            if ((int)population == 0) return "Low";
            if ((int)population == 1) return "Normal";
            if ((int)population == 2) return "High";
            return population.ToString();
        }

        private static string GetLandmarkDensityLabel(LandmarkDensity density)
        {
            // Enum values: 0=Low, 1=Normal, 2=High
            if ((int)density == 0) return "Low";
            if ((int)density == 1) return "Normal";
            if ((int)density == 2) return "High";
            return density.ToString();
        }

        public static int CurrentFieldIndex => currentFieldIndex;
        public static int FieldCount => availableFields.Count;

        // Seed text input methods
        public static void StartSeedEdit(string currentSeed)
        {
            isEditingSeed = true;
            seedInputBuffer = currentSeed ?? "";
            TolkHelper.Speak($"[Editing Seed] Type seed, Enter to confirm, Escape to cancel. Current: {seedInputBuffer}");
        }

        public static void CancelSeedEdit()
        {
            isEditingSeed = false;
            seedInputBuffer = "";
            TolkHelper.Speak("Seed editing canceled");
        }

        public static string ConfirmSeedEdit()
        {
            isEditingSeed = false;
            string result = seedInputBuffer;
            seedInputBuffer = "";
            return result;
        }

        public static void AddCharToSeedBuffer(char c)
        {
            seedInputBuffer += c;
            TolkHelper.Speak($"[Editing Seed] {seedInputBuffer}");
        }

        public static void RemoveCharFromSeedBuffer()
        {
            if (seedInputBuffer.Length > 0)
            {
                seedInputBuffer = seedInputBuffer.Substring(0, seedInputBuffer.Length - 1);
                TolkHelper.Speak($"[Editing Seed] {seedInputBuffer}");
            }
        }

        public static bool IsEditingSeed => isEditingSeed;
        public static string SeedInputBuffer => seedInputBuffer;
    }
}
