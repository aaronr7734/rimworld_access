using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Owns each world-param field's underlying VALUE: the dev-mode-dependent option
    /// tables (planet coverage, map size), the local index/value trackers kept in
    /// sync with the live page, and the clamp-then-write-through-the-bridge step for
    /// each field. WorldParamsNavigationState owns the field CURSOR (which field is
    /// selected, typeahead, wrap) and dispatches here via <see cref="Modify"/> once
    /// it decides a value should change -- split out per the architecture audit's
    /// note that ModifyCurrentValue's switch interleaved cursor concerns with game
    /// mutation. AnnounceFieldValue stays on WorldParamsNavigationState, called
    /// uniformly after every Modify (this class never announces).
    /// </summary>
    internal static class WorldParamsFieldValues
    {
        // Planet coverage - dev mode adds a 5% option at the end with a (dev) marker.
        private static float[] activePlanetCoverages;
        private static bool[] isPlanetCoverageDev;
        private static int planetCoverageIndex = 0;

        // Rainfall/Temperature/Population/LandmarkDensity labels derived from enum names.
        // Strings come from RimWorldAccess_MainMenu.xml so they're translatable.
        private static readonly string[] RainfallLabelKeys = { "RimWorldAccess.WorldParams.Rainfall.AlmostNone", "RimWorldAccess.WorldParams.Rainfall.Little", "RimWorldAccess.WorldParams.Rainfall.LittleLess", "RimWorldAccess.WorldParams.Rainfall.Normal", "RimWorldAccess.WorldParams.Rainfall.LittleMore", "RimWorldAccess.WorldParams.Rainfall.High", "RimWorldAccess.WorldParams.Rainfall.VeryHigh" };
        private static readonly string[] TemperatureLabelKeys = { "RimWorldAccess.WorldParams.Temperature.VeryCold", "RimWorldAccess.WorldParams.Temperature.Cold", "RimWorldAccess.WorldParams.Temperature.LittleColder", "RimWorldAccess.WorldParams.Temperature.Normal", "RimWorldAccess.WorldParams.Temperature.LittleWarmer", "RimWorldAccess.WorldParams.Temperature.Hot", "RimWorldAccess.WorldParams.Temperature.VeryHot" };
        private static readonly string[] PopulationLabelKeys = { "RimWorldAccess.WorldParams.Population.Sparse", "RimWorldAccess.WorldParams.Population.MoreSparse", "RimWorldAccess.WorldParams.Population.SlightSparse", "RimWorldAccess.WorldParams.Population.Normal", "RimWorldAccess.WorldParams.Population.SlightCrowded", "RimWorldAccess.WorldParams.Population.MoreCrowded", "RimWorldAccess.WorldParams.Population.Crowded" };
        private static readonly string[] LandmarkDensityLabelKeys = { "RimWorldAccess.WorldParams.LandmarkDensity.Sparse", "RimWorldAccess.WorldParams.LandmarkDensity.MoreSparse", "RimWorldAccess.WorldParams.LandmarkDensity.SlightSparse", "RimWorldAccess.WorldParams.LandmarkDensity.Normal", "RimWorldAccess.WorldParams.LandmarkDensity.SlightCrowded", "RimWorldAccess.WorldParams.LandmarkDensity.MoreCrowded", "RimWorldAccess.WorldParams.LandmarkDensity.Crowded" };

        private static string[] RainfallLabels => Array.ConvertAll(RainfallLabelKeys, k => k.Translate().ToString());
        private static string[] TemperatureLabels => Array.ConvertAll(TemperatureLabelKeys, k => k.Translate().ToString());
        private static string[] PopulationLabels => Array.ConvertAll(PopulationLabelKeys, k => k.Translate().ToString());
        private static string[] LandmarkDensityLabels => Array.ConvertAll(LandmarkDensityLabelKeys, k => k.Translate().ToString());

        private static int rainfallIndex = 3;       // Default: Normal
        private static int temperatureIndex = 3;    // Default: Normal
        private static int populationIndex = 3;     // Default: Normal
        private static int landmarkDensityIndex = 3; // Default: Normal
        private static float pollutionValue = 0f;

        // Map sizes - pulled dynamically based on dev mode (Prefs.TestMapSizes)
        private static int[] activeMapSizes;
        private static string[] activeMapSizeLabels;
        private static int mapSizeIndex = 2;  // Default 250 (Medium)

        // Seasons
        private static readonly Season[] Seasons = { Season.Undefined, Season.Spring, Season.Summer, Season.Fall, Season.Winter };
        private static int seasonIndex = 0;  // Default Auto (game's default)

        /// <summary>
        /// Set up dynamic options based on dev mode settings.
        /// </summary>
        public static void SetupDynamicOptions()
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

        /// <summary>
        /// Sync our internal indices with the game's actual slider values (via the bridge).
        /// </summary>
        public static void SyncFromGame()
        {
            // Planet coverage
            float coverage = WorldParamsPageBridge.PlanetCoverage;
            planetCoverageIndex = Array.FindIndex(activePlanetCoverages, c => Math.Abs(c - coverage) < 0.01f);
            if (planetCoverageIndex < 0) planetCoverageIndex = 0; // Default to first option

            rainfallIndex = (int)WorldParamsPageBridge.Rainfall;

            // Temperature
            temperatureIndex = (int)WorldParamsPageBridge.Temperature;

            // Population
            populationIndex = (int)WorldParamsPageBridge.Population;

            // Landmark Density (Odyssey)
            if (ModsConfig.OdysseyActive)
            {
                landmarkDensityIndex = (int)WorldParamsPageBridge.LandmarkDensity;
            }

            // Pollution (Biotech)
            if (ModsConfig.BiotechActive)
            {
                pollutionValue = WorldParamsPageBridge.Pollution;
            }
        }

        /// <summary>
        /// Sync advanced settings from GameInitData.
        /// </summary>
        public static void SyncAdvancedFromGame()
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

        // ===== MUTATION =====

        /// <summary>
        /// Clamps and applies direction to the given field's value, writing it
        /// through the bridge (or, for MapSize/StartingSeason, directly to
        /// GameInitData -- neither goes through the page at all in vanilla either).
        /// Caller (WorldParamsNavigationState.ModifyCurrentValue) announces
        /// uniformly afterward; nothing here ever speaks.
        /// </summary>
        public static void Modify(WorldParamField field, int direction)
        {
            switch (field)
            {
                case WorldParamField.PlanetCoverage: ModifyPlanetCoverage(direction); break;
                case WorldParamField.Rainfall: ModifyRainfall(direction); break;
                case WorldParamField.Temperature: ModifyTemperature(direction); break;
                case WorldParamField.Population: ModifyPopulation(direction); break;
                case WorldParamField.LandmarkDensity: ModifyLandmarkDensity(direction); break;
                case WorldParamField.Pollution: ModifyPollution(direction); break;
                case WorldParamField.MapSize: ModifyMapSize(direction); break;
                case WorldParamField.StartingSeason: ModifyStartingSeason(direction); break;
            }
        }

        private static void ModifyPlanetCoverage(int direction)
        {
            planetCoverageIndex = ClampIndex(planetCoverageIndex + direction, activePlanetCoverages.Length);
            WorldParamsPageBridge.PlanetCoverage = activePlanetCoverages[planetCoverageIndex];
        }

        private static void ModifyRainfall(int direction)
        {
            // Sliders clamp, don't wrap
            rainfallIndex = ClampIndex(rainfallIndex + direction, RainfallLabels.Length);
            WorldParamsPageBridge.Rainfall = (OverallRainfall)rainfallIndex;
        }

        private static void ModifyTemperature(int direction)
        {
            temperatureIndex = ClampIndex(temperatureIndex + direction, TemperatureLabels.Length);
            WorldParamsPageBridge.Temperature = (OverallTemperature)temperatureIndex;
        }

        private static void ModifyPopulation(int direction)
        {
            populationIndex = ClampIndex(populationIndex + direction, PopulationLabels.Length);
            WorldParamsPageBridge.Population = (OverallPopulation)populationIndex;
        }

        private static void ModifyLandmarkDensity(int direction)
        {
            landmarkDensityIndex = ClampIndex(landmarkDensityIndex + direction, LandmarkDensityLabels.Length);
            WorldParamsPageBridge.LandmarkDensity = (LandmarkDensity)landmarkDensityIndex;
        }

        private static void ModifyPollution(int direction)
        {
            // Pollution is a percentage slider - clamp between 0 and 1
            float step = 0.05f;
            pollutionValue = Math.Max(0f, Math.Min(1f, pollutionValue + (direction * step)));
            WorldParamsPageBridge.Pollution = pollutionValue;
        }

        private static void ModifyMapSize(int direction)
        {
            mapSizeIndex = ClampIndex(mapSizeIndex + direction, activeMapSizes.Length);
            // MUTATION-C: mirrors Dialog_AdvancedGameConfig.cs:59-63's mapSize radio-button
            // write; GameInitData.mapSize is a bare public field with no gated setter, and
            // activeMapSizes is populated from the same MapSizes/TestMapSizes arrays.
            Find.GameInitData.mapSize = activeMapSizes[mapSizeIndex];
        }

        private static void ModifyStartingSeason(int direction)
        {
            seasonIndex = ClampIndex(seasonIndex + direction, Seasons.Length);
            // MUTATION-C: mirrors Dialog_AdvancedGameConfig.cs:69-88's startingSeason
            // radio-button writes; GameInitData.startingSeason is a bare public field with
            // no gated setter and no biome/hemisphere validation exists anywhere in vanilla.
            Find.GameInitData.startingSeason = Seasons[seasonIndex];
        }

        private static int ClampIndex(int index, int count)
        {
            if (index < 0) return 0;
            if (index >= count) return count - 1;
            return index;
        }

        // ===== VALUE / WARNING READOUT =====

        // A slider type-in box writes any number, so a level can sit past the enum's named values.
        private static string LevelLabel(string[] labels, int index)
        {
            return index >= 0 && index < labels.Length ? labels[index] : index.ToString();
        }

        public static string GetValueString(WorldParamField field)
        {
            switch (field)
            {
                case WorldParamField.Seed:
                    return WorldParamsPageBridge.IsBound ? WorldParamsPageBridge.SeedString : "Unknown";

                case WorldParamField.PlanetCoverage:
                    string coverageValue = activePlanetCoverages[planetCoverageIndex].ToStringPercent();
                    if (isPlanetCoverageDev != null && isPlanetCoverageDev[planetCoverageIndex])
                        coverageValue += " (dev)";
                    return coverageValue;

                case WorldParamField.Rainfall:
                    return LevelLabel(RainfallLabels, rainfallIndex);

                case WorldParamField.Temperature:
                    return LevelLabel(TemperatureLabels, temperatureIndex);

                case WorldParamField.Population:
                    return LevelLabel(PopulationLabels, populationIndex);

                case WorldParamField.LandmarkDensity:
                    return LevelLabel(LandmarkDensityLabels, landmarkDensityIndex);

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

        public static string GetWarning(WorldParamField field)
        {
            switch (field)
            {
                case WorldParamField.PlanetCoverage:
                    if (activePlanetCoverages[planetCoverageIndex] == 1f)
                        return "RimWorldAccess.WorldParams.WarningPrefix".Translate("MessageMaxPlanetCoveragePerformanceWarning".Translate()).ToString();
                    break;

                case WorldParamField.MapSize:
                    if (activeMapSizes[mapSizeIndex] > 280)
                        return "RimWorldAccess.WorldParams.WarningPrefix".Translate("MapSizePerformanceWarning".Translate()).ToString();
                    break;

                case WorldParamField.StartingSeason:
                    if (Seasons[seasonIndex] == Season.Winter)
                        return "RimWorldAccess.WorldParams.WarningPrefix".Translate("MapWinterWarning".Translate()).ToString();
                    break;
            }
            return "";
        }
    }
}
