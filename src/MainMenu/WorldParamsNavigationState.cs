using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages navigation state for the Page_CreateWorldParams screen.
    /// All fields in a single menu, including Advanced settings (Map Size, Season).
    /// Uses modern patterns: MenuHelper for navigation, TypeaheadSearchHelper for search.
    /// </summary>
    public static class WorldParamsNavigationState
    {
        // ===== FIELD DEFINITIONS =====
        private enum WorldParamField
        {
            Seed,
            PlanetCoverage,
            Rainfall,
            Temperature,
            Population,
            LandmarkDensity,  // Odyssey only
            Pollution,        // Biotech only
            MapSize,          // Advanced
            StartingSeason    // Advanced
        }

        private static List<WorldParamField> availableFields = new List<WorldParamField>();
        private static int fieldIndex = 0;
        private static TypeaheadSearchHelper fieldTypeahead = new TypeaheadSearchHelper();

        // Per-slider value tracking (synced with game)
        // Planet coverage - pulled dynamically based on dev mode
        private static float[] activePlanetCoverages;
        private static bool[] isPlanetCoverageDev;  // Tracks which coverage options are dev-only
        private static int planetCoverageIndex = 0;

        // Rainfall/Temperature/Population/LandmarkDensity labels derived from enum names
        private static readonly string[] RainfallLabels = { "Almost None", "Little", "A Little Less", "Normal", "A Little More", "High", "Very High" };
        private static readonly string[] TemperatureLabels = { "Very Cold", "Cold", "A Little Colder", "Normal", "A Little Warmer", "Hot", "Very Hot" };
        private static readonly string[] PopulationLabels = { "Almost None", "Little", "A Little Less", "Normal", "A Little More", "High", "Very High" };
        private static readonly string[] LandmarkDensityLabels = { "Sparse", "Slightly More Sparse", "Slightly Sparse", "Normal", "Slightly Crowded", "Slightly More Crowded", "Crowded" };

        private static int rainfallIndex = 3;       // Default: Normal
        private static int temperatureIndex = 3;    // Default: Normal
        private static int populationIndex = 3;     // Default: Normal
        private static int landmarkDensityIndex = 3; // Default: Normal
        private static float pollutionValue = 0f;

        // Text input state for seed editing — flows through the unified controller.
        private static readonly TextInputController seedController = new TextInputController();
        private static bool isEditingSeed = false;

        // Map sizes - pulled dynamically based on dev mode (Prefs.TestMapSizes)
        private static int[] activeMapSizes;
        private static string[] activeMapSizeLabels;
        private static int mapSizeIndex = 2;  // Default 250 (Medium)

        // Seasons
        private static readonly Season[] Seasons = { Season.Undefined, Season.Spring, Season.Summer, Season.Fall, Season.Winter };
        private static int seasonIndex = 0;  // Default Auto (game's default)

        // ===== INITIALIZATION STATE =====
        private static bool initialized = false;
        private static Page_CreateWorldParams currentInstance = null;

        // ===== PUBLIC PROPERTIES =====
        public static bool IsEditingSeed => isEditingSeed;
        public static string SeedInputBuffer => isEditingSeed ? seedController.CurrentText : string.Empty;
        public static bool HasActiveSearch => fieldTypeahead.HasActiveSearch;

        public static int FieldCount => availableFields.Count;

        // ===== INITIALIZATION =====

        public static void Initialize(Page_CreateWorldParams instance)
        {
            // Always re-setup dynamic options (dev mode could have changed)
            SetupDynamicOptions();

            if (!initialized || currentInstance != instance)
            {
                currentInstance = instance;

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

                // Advanced settings always available
                availableFields.Add(WorldParamField.MapSize);
                availableFields.Add(WorldParamField.StartingSeason);

                // Sync slider values with game's actual values
                SyncFromGame(instance);

                // Sync advanced settings from GameInitData
                SyncAdvancedFromGame();

                // Reset navigation state — start with no field selected so Enter advances
                fieldIndex = -1;
                if (isEditingSeed) seedController.Cancel();
                isEditingSeed = false;
                fieldTypeahead.ClearSearch();

                initialized = true;
            }
        }

        /// <summary>
        /// Set up dynamic options based on dev mode settings.
        /// </summary>
        private static void SetupDynamicOptions()
        {
            // Planet coverage - dev mode adds 5% option at the end with (dev) marker
            if (Prefs.DevMode)
            {
                activePlanetCoverages = new float[] { 0.3f, 0.5f, 1f, 0.05f };
                isPlanetCoverageDev = new bool[] { false, false, false, true };
            }
            else
            {
                activePlanetCoverages = new float[] { 0.3f, 0.5f, 1f };
                isPlanetCoverageDev = new bool[] { false, false, false };
            }

            // Map sizes - Prefs.TestMapSizes adds 350 and 400 in numeric order
            if (Prefs.TestMapSizes)
            {
                activeMapSizes = new int[] { 200, 225, 250, 275, 300, 325, 350, 400 };
            }
            else
            {
                activeMapSizes = new int[] { 200, 225, 250, 275, 300, 325 };
            }

            // Generate labels using game's translation keys
            activeMapSizeLabels = new string[activeMapSizes.Length];
            for (int i = 0; i < activeMapSizes.Length; i++)
            {
                activeMapSizeLabels[i] = GetMapSizeCategory(activeMapSizes[i]);
            }
        }

        /// <summary>
        /// Get the translated category label for a map size, matching game's thresholds.
        /// </summary>
        private static string GetMapSizeCategory(int size)
        {
            if (size >= 350)
                return "MapSizeExtreme".Translate().ToString();
            if (size >= 300)
                return "MapSizeLarge".Translate().ToString();
            if (size >= 250)
                return "MapSizeMedium".Translate().ToString();
            return "MapSizeSmall".Translate().ToString();
        }

        public static void Reset()
        {
            initialized = false;
            currentInstance = null;
            fieldIndex = -1;
            availableFields.Clear();
            fieldTypeahead.ClearSearch();
            if (isEditingSeed) seedController.Cancel();
            isEditingSeed = false;
        }

        /// <summary>
        /// Sync our internal indices with the game's actual slider values.
        /// </summary>
        private static void SyncFromGame(Page_CreateWorldParams instance)
        {
            // Planet coverage
            float coverage = (float)AccessTools.Field(typeof(Page_CreateWorldParams), "planetCoverage").GetValue(instance);
            planetCoverageIndex = Array.FindIndex(activePlanetCoverages, c => Math.Abs(c - coverage) < 0.01f);
            if (planetCoverageIndex < 0) planetCoverageIndex = 0; // Default to first option

            // Rainfall
            OverallRainfall rainfall = (OverallRainfall)AccessTools.Field(typeof(Page_CreateWorldParams), "rainfall").GetValue(instance);
            rainfallIndex = (int)rainfall;

            // Temperature
            OverallTemperature temperature = (OverallTemperature)AccessTools.Field(typeof(Page_CreateWorldParams), "temperature").GetValue(instance);
            temperatureIndex = (int)temperature;

            // Population
            OverallPopulation population = (OverallPopulation)AccessTools.Field(typeof(Page_CreateWorldParams), "population").GetValue(instance);
            populationIndex = (int)population;

            // Landmark Density (Odyssey)
            if (ModsConfig.OdysseyActive)
            {
                LandmarkDensity density = (LandmarkDensity)AccessTools.Field(typeof(Page_CreateWorldParams), "landmarkDensity").GetValue(instance);
                landmarkDensityIndex = (int)density;
            }

            // Pollution (Biotech)
            if (ModsConfig.BiotechActive)
            {
                pollutionValue = (float)AccessTools.Field(typeof(Page_CreateWorldParams), "pollution").GetValue(instance);
            }
        }

        /// <summary>
        /// Sync advanced settings from GameInitData.
        /// </summary>
        private static void SyncAdvancedFromGame()
        {
            // Map size
            int currentMapSize = Find.GameInitData.mapSize;
            mapSizeIndex = Array.IndexOf(activeMapSizes, currentMapSize);
            if (mapSizeIndex < 0) mapSizeIndex = 2; // Default to 250

            // Season - game defaults to Auto (Season.Undefined)
            Season currentSeason = Find.GameInitData.startingSeason;
            seasonIndex = Array.IndexOf(Seasons, currentSeason);
            if (seasonIndex < 0) seasonIndex = 0; // Default to Auto
        }

        // ===== NAVIGATION =====

        public static void NavigateUp()
        {
            if (availableFields.Count == 0) return;
            fieldTypeahead.ClearSearch();
            if (fieldIndex < 0)
            {
                fieldIndex = availableFields.Count - 1;
            }
            else
            {
                fieldIndex = MenuHelper.SelectPrevious(fieldIndex, availableFields.Count);
            }
            AnnounceCurrentField();
        }

        public static void NavigateDown()
        {
            if (availableFields.Count == 0) return;
            fieldTypeahead.ClearSearch();
            if (fieldIndex < 0)
            {
                fieldIndex = 0;
            }
            else
            {
                fieldIndex = MenuHelper.SelectNext(fieldIndex, availableFields.Count);
            }
            AnnounceCurrentField();
        }

        public static void NavigateHome()
        {
            if (availableFields.Count == 0) return;
            fieldTypeahead.ClearSearch();
            fieldIndex = 0;
            AnnounceCurrentField();
        }

        public static void NavigateEnd()
        {
            if (availableFields.Count == 0) return;
            fieldTypeahead.ClearSearch();
            fieldIndex = availableFields.Count - 1;
            AnnounceCurrentField();
        }

        // ===== VALUE MODIFICATION =====

        public static void ModifyCurrentValue(int direction)
        {
            if (fieldIndex < 0 || fieldIndex >= availableFields.Count) return;

            WorldParamField field = availableFields[fieldIndex];
            switch (field)
            {
                case WorldParamField.Seed:
                    TolkHelper.Speak("Press Enter to type custom seed, or R to randomize");
                    break;

                case WorldParamField.PlanetCoverage:
                    planetCoverageIndex = ClampIndex(planetCoverageIndex + direction, activePlanetCoverages.Length);
                    ApplyPlanetCoverage();
                    AnnounceFieldValue(field);
                    break;

                case WorldParamField.Rainfall:
                    // Sliders clamp, don't wrap
                    rainfallIndex = ClampIndex(rainfallIndex + direction, RainfallLabels.Length);
                    ApplyRainfall();
                    AnnounceFieldValue(field);
                    break;

                case WorldParamField.Temperature:
                    temperatureIndex = ClampIndex(temperatureIndex + direction, TemperatureLabels.Length);
                    ApplyTemperature();
                    AnnounceFieldValue(field);
                    break;

                case WorldParamField.Population:
                    populationIndex = ClampIndex(populationIndex + direction, PopulationLabels.Length);
                    ApplyPopulation();
                    AnnounceFieldValue(field);
                    break;

                case WorldParamField.LandmarkDensity:
                    landmarkDensityIndex = ClampIndex(landmarkDensityIndex + direction, LandmarkDensityLabels.Length);
                    ApplyLandmarkDensity();
                    AnnounceFieldValue(field);
                    break;

                case WorldParamField.Pollution:
                    // Pollution is a percentage slider - clamp between 0 and 1
                    float step = 0.05f;
                    pollutionValue = Math.Max(0f, Math.Min(1f, pollutionValue + (direction * step)));
                    ApplyPollution();
                    AnnounceFieldValue(field);
                    break;

                case WorldParamField.MapSize:
                    mapSizeIndex = ClampIndex(mapSizeIndex + direction, activeMapSizes.Length);
                    Find.GameInitData.mapSize = activeMapSizes[mapSizeIndex];
                    AnnounceFieldValue(field);
                    break;

                case WorldParamField.StartingSeason:
                    seasonIndex = ClampIndex(seasonIndex + direction, Seasons.Length);
                    Find.GameInitData.startingSeason = Seasons[seasonIndex];
                    AnnounceFieldValue(field);
                    break;
            }
        }

        private static int ClampIndex(int index, int count)
        {
            if (index < 0) return 0;
            if (index >= count) return count - 1;
            return index;
        }

        // ===== APPLY VALUES TO GAME =====

        private static void ApplyPlanetCoverage()
        {
            if (currentInstance == null) return;
            AccessTools.Field(typeof(Page_CreateWorldParams), "planetCoverage").SetValue(currentInstance, activePlanetCoverages[planetCoverageIndex]);
        }

        private static void ApplyRainfall()
        {
            if (currentInstance == null) return;
            AccessTools.Field(typeof(Page_CreateWorldParams), "rainfall").SetValue(currentInstance, (OverallRainfall)rainfallIndex);
        }

        private static void ApplyTemperature()
        {
            if (currentInstance == null) return;
            AccessTools.Field(typeof(Page_CreateWorldParams), "temperature").SetValue(currentInstance, (OverallTemperature)temperatureIndex);
        }

        private static void ApplyPopulation()
        {
            if (currentInstance == null) return;
            AccessTools.Field(typeof(Page_CreateWorldParams), "population").SetValue(currentInstance, (OverallPopulation)populationIndex);
        }

        private static void ApplyLandmarkDensity()
        {
            if (currentInstance == null) return;
            AccessTools.Field(typeof(Page_CreateWorldParams), "landmarkDensity").SetValue(currentInstance, (LandmarkDensity)landmarkDensityIndex);
        }

        private static void ApplyPollution()
        {
            if (currentInstance == null) return;
            AccessTools.Field(typeof(Page_CreateWorldParams), "pollution").SetValue(currentInstance, pollutionValue);
        }

        // ===== SEED EDITING =====

        public static void StartSeedEdit()
        {
            if (currentInstance == null) return;
            string currentSeed = (string)AccessTools.Field(typeof(Page_CreateWorldParams), "seedString").GetValue(currentInstance);
            var spec = TextFieldSpec.Unrestricted("RimWorldAccess.TextInput.LabelWorldSeed");
            isEditingSeed = true;
            seedController.Begin(
                currentSeed ?? string.Empty,
                spec,
                onConfirm: seed =>
                {
                    isEditingSeed = false;
                    if (currentInstance == null) return;
                    if (!string.IsNullOrEmpty(seed))
                    {
                        AccessTools.Field(typeof(Page_CreateWorldParams), "seedString").SetValue(currentInstance, seed);
                        TolkHelper.Speak($"World Seed: {seed} (Confirmed)");
                    }
                    else
                    {
                        TolkHelper.Speak("Seed input canceled (empty)");
                    }
                },
                onCancel: () =>
                {
                    isEditingSeed = false;
                    TolkHelper.Speak("Seed editing canceled");
                },
                replaceOnType: false,
                modal: true);
        }

        public static void RandomizeSeed()
        {
            if (currentInstance == null) return;
            string newSeed = GenText.RandomSeedString();
            AccessTools.Field(typeof(Page_CreateWorldParams), "seedString").SetValue(currentInstance, newSeed);
            TolkHelper.Speak($"World Seed: {newSeed} (Randomized)");
        }

        // ===== TYPEAHEAD SEARCH =====

        public static bool HandleTypeahead(char character)
        {
            if (availableFields.Count == 0) return false;

            var labels = availableFields.Select(f => GetFieldLabel(f)).ToList();
            if (fieldTypeahead.ProcessCharacterInput(character, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    fieldIndex = newIndex;
                    AnnounceFieldWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{fieldTypeahead.LastFailedSearch}'");
            }
            return true;
        }

        public static bool HandleTypeaheadBackspace()
        {
            if (!fieldTypeahead.HasActiveSearch) return false;

            var labels = availableFields.Select(f => GetFieldLabel(f)).ToList();
            if (fieldTypeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    fieldIndex = newIndex;
                    AnnounceFieldWithSearch();
                }
            }
            return true;
        }

        public static bool ClearTypeaheadSearch()
        {
            if (fieldTypeahead.ClearSearchAndAnnounce())
            {
                AnnounceCurrentField();
                return true;
            }
            return false;
        }

        public static bool SelectNextMatch()
        {
            if (!fieldTypeahead.HasActiveSearch) return false;
            int next = fieldTypeahead.GetNextMatch(fieldIndex);
            if (next >= 0)
            {
                fieldIndex = next;
                AnnounceFieldWithSearch();
            }
            return true;
        }

        public static bool SelectPreviousMatch()
        {
            if (!fieldTypeahead.HasActiveSearch) return false;
            int prev = fieldTypeahead.GetPreviousMatch(fieldIndex);
            if (prev >= 0)
            {
                fieldIndex = prev;
                AnnounceFieldWithSearch();
            }
            return true;
        }

        // ===== ANNOUNCEMENTS =====

        public static void AnnounceCurrentField()
        {
            if (fieldIndex < 0 || fieldIndex >= availableFields.Count) return;

            WorldParamField field = availableFields[fieldIndex];
            string fieldName = GetFieldLabel(field);
            string value = GetFieldValueString(field);
            string position = MenuHelper.FormatPosition(fieldIndex, availableFields.Count);

            string text = $"{fieldName}: {value}";
            if (!string.IsNullOrEmpty(position))
            {
                text += $" ({position})";
            }

            TolkHelper.Speak(text);
        }

        private static void AnnounceFieldValue(WorldParamField field)
        {
            // Use short label (no "(Advanced)" suffix) for value changes
            string fieldName = GetFieldLabel(field, includeAdvancedSuffix: false);
            string value = GetFieldValueString(field);
            string warning = GetFieldWarning(field);
            TolkHelper.Speak($"{fieldName}: {value}{warning}");
        }

        private static string GetFieldWarning(WorldParamField field)
        {
            switch (field)
            {
                case WorldParamField.PlanetCoverage:
                    if (activePlanetCoverages[planetCoverageIndex] == 1f)
                        return ". " + "MessageMaxPlanetCoveragePerformanceWarning".Translate().ToString();
                    break;

                case WorldParamField.MapSize:
                    if (activeMapSizes[mapSizeIndex] > 280)
                        return ". " + "MapSizePerformanceWarning".Translate().ToString();
                    break;

                case WorldParamField.StartingSeason:
                    if (Seasons[seasonIndex] == Season.Winter)
                        return ". " + "MapWinterWarning".Translate().ToString();
                    break;
            }
            return "";
        }

        private static void AnnounceFieldWithSearch()
        {
            if (fieldIndex < 0 || fieldIndex >= availableFields.Count) return;

            WorldParamField field = availableFields[fieldIndex];
            string fieldName = GetFieldLabel(field);
            string value = GetFieldValueString(field);

            if (fieldTypeahead.HasActiveSearch)
            {
                TolkHelper.Speak($"{fieldName}: {value}, {fieldTypeahead.CurrentMatchPosition} of {fieldTypeahead.MatchCount} matches for '{fieldTypeahead.SearchBuffer}'");
            }
            else
            {
                AnnounceCurrentField();
            }
        }

        // ===== HELPERS =====

        private static string GetFieldLabel(WorldParamField field, bool includeAdvancedSuffix = true)
        {
            string label;
            switch (field)
            {
                case WorldParamField.Seed:
                    label = "WorldSeed".Translate().ToString();
                    break;
                case WorldParamField.PlanetCoverage:
                    label = "PlanetCoverage".Translate().ToString();
                    break;
                case WorldParamField.Rainfall:
                    label = "PlanetRainfall".Translate().ToString();
                    break;
                case WorldParamField.Temperature:
                    label = "PlanetTemperature".Translate().ToString();
                    break;
                case WorldParamField.Population:
                    label = "PlanetPopulation".Translate().ToString();
                    break;
                case WorldParamField.LandmarkDensity:
                    label = "PlanetLandmarkDensity".Translate().ToString();
                    break;
                case WorldParamField.Pollution:
                    label = "PlanetPollution".Translate().ToString();
                    break;
                case WorldParamField.MapSize:
                    label = "MapSize".Translate().ToString();
                    if (includeAdvancedSuffix)
                        label += " (" + "AdvancedSettings".Translate().ToString() + ")";
                    return label;
                case WorldParamField.StartingSeason:
                    label = "MapStartSeason".Translate().ToString();
                    if (includeAdvancedSuffix)
                        label += " (" + "AdvancedSettings".Translate().ToString() + ")";
                    return label;
                default:
                    return "Unknown";
            }
            return label;
        }

        private static string GetFieldValueString(WorldParamField field)
        {
            switch (field)
            {
                case WorldParamField.Seed:
                    if (currentInstance == null) return "Unknown";
                    return (string)AccessTools.Field(typeof(Page_CreateWorldParams), "seedString").GetValue(currentInstance);

                case WorldParamField.PlanetCoverage:
                    string coverageValue = activePlanetCoverages[planetCoverageIndex].ToStringPercent();
                    if (isPlanetCoverageDev != null && isPlanetCoverageDev[planetCoverageIndex])
                        coverageValue += " (dev)";
                    return coverageValue;

                case WorldParamField.Rainfall:
                    return RainfallLabels[rainfallIndex];

                case WorldParamField.Temperature:
                    return TemperatureLabels[temperatureIndex];

                case WorldParamField.Population:
                    return PopulationLabels[populationIndex];

                case WorldParamField.LandmarkDensity:
                    return LandmarkDensityLabels[landmarkDensityIndex];

                case WorldParamField.Pollution:
                    return pollutionValue.ToStringPercent();

                case WorldParamField.MapSize:
                    int size = activeMapSizes[mapSizeIndex];
                    string sizeLabel = activeMapSizeLabels[mapSizeIndex];
                    return $"{size}x{size} {sizeLabel}";

                case WorldParamField.StartingSeason:
                    // Use game's translation - "MapStartSeasonDefault" for Auto, or Season.LabelCap() for others
                    if (Seasons[seasonIndex] == Season.Undefined)
                        return "MapStartSeasonDefault".Translate().ToString();
                    return Seasons[seasonIndex].LabelCap().ToString();

                default:
                    return "Unknown";
            }
        }

        public static string GetCurrentFieldName()
        {
            if (fieldIndex < 0 || fieldIndex >= availableFields.Count)
                return "Unknown";
            return availableFields[fieldIndex].ToString();
        }

        public static bool IsOnSeedField()
        {
            if (fieldIndex < 0 || fieldIndex >= availableFields.Count) return false;
            return availableFields[fieldIndex] == WorldParamField.Seed;
        }
    }
}
