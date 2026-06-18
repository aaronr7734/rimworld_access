using RimWorld;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Maps terrain types to audio files for audio feedback during map navigation.
    /// Uses language-independent defNames instead of translated labels.
    /// </summary>
    public static class TerrainAudioHelper
    {
        /// <summary>
        /// Audio file played when the cursor is over a wall (man-made or natural rock).
        /// Takes precedence over the terrain beneath the wall.
        /// </summary>
        public const string WallAudioFile = "wall.wav";

        /// <summary>
        /// Exact defName-to-audio mapping for all known terrain types.
        /// To add a terrain-specific sound, update the value for that defName.
        /// </summary>
        private static readonly Dictionary<string, string> exactTerrainAudioMap = new Dictionary<string, string>()
        {
            // Soil/Earth
            { "Soil", "soil.wav" },
            { "SoilRich", "Rich Soil.wav" },
            { "Gravel", "stoney Soil.wav" },
            { "MossyTerrain", "soil.wav" },
            { "MarshyTerrain", "mud.wav" },
            { "Mud", "mud.wav" },
            { "Marsh", "mud.wav" },
            { "PackedDirt", "soil.wav" },
            { "Riverbank", "mud.wav" },
            { "FungalGravel", "stoney Soil.wav" },
            { "GlowforestSoil", "soil.wav" },
            { "GrasslandSoil", "soil.wav" },
            { "DryLakeBed", "soil.wav" },

            // Sand
            { "Sand", "soil.wav" },
            { "SoftSand", "soil.wav" },

            // Stone Tiles
            { "TileSandstone", "stone flooring.wav" },
            { "TileGranite", "stone flooring.wav" },
            { "TileLimestone", "stone flooring.wav" },
            { "TileSlate", "stone flooring.wav" },
            { "TileMarble", "stone flooring.wav" },
            { "TileVacstone", "stone flooring.wav" },

            // Fine Stone Tiles (Royalty)
            { "FineTileSandstone", "stone flooring.wav" },
            { "FineTileGranite", "stone flooring.wav" },
            { "FineTileLimestone", "stone flooring.wav" },
            { "FineTileSlate", "stone flooring.wav" },
            { "FineTileMarble", "stone flooring.wav" },
            { "FineTileVacstone", "stone flooring.wav" },

            // Morbid Tiles (Ideology)
            { "Tile_MorbidSandstone", "stone flooring.wav" },
            { "Tile_MorbidGranite", "stone flooring.wav" },
            { "Tile_MorbidLimestone", "stone flooring.wav" },
            { "Tile_MorbidSlate", "stone flooring.wav" },
            { "Tile_MorbidMarble", "stone flooring.wav" },
            { "Tile_MorbidVacstone", "stone flooring.wav" },

            // Spikecore Tiles (Ideology)
            { "Tile_SpikecoreSandstone", "stone flooring.wav" },
            { "Tile_SpikecoreGranite", "stone flooring.wav" },
            { "Tile_SpikecoreLimestone", "stone flooring.wav" },
            { "Tile_SpikecoreSlate", "stone flooring.wav" },
            { "Tile_SpikecoreMarble", "stone flooring.wav" },
            { "Tile_SpikecoreVacstone", "stone flooring.wav" },

            // Totemic Tiles (Ideology)
            { "Tile_TotemicSandstone", "stone flooring.wav" },
            { "Tile_TotemicGranite", "stone flooring.wav" },
            { "Tile_TotemicLimestone", "stone flooring.wav" },
            { "Tile_TotemicSlate", "stone flooring.wav" },
            { "Tile_TotemicMarble", "stone flooring.wav" },
            { "Tile_TotemicVacstone", "stone flooring.wav" },

            // Flagstone
            { "FlagstoneSandstone", "stone flooring.wav" },
            { "FlagstoneGranite", "stone flooring.wav" },
            { "FlagstoneLimestone", "stone flooring.wav" },
            { "FlagstoneSlate", "stone flooring.wav" },
            { "FlagstoneMarble", "stone flooring.wav" },
            { "FlagstoneVacstone", "stone flooring.wav" },

            // Other Hard Floors
            { "PavedTile", "stone flooring.wav" },
            { "Concrete", "stone flooring.wav" },
            { "SterileTile", "stone flooring.wav" },
            { "MetalTile", "stone flooring.wav" },
            { "SilverTile", "stone flooring.wav" },
            { "GoldTile", "stone flooring.wav" },
            { "AncientTile", "stone flooring.wav" },
            { "Plates_Spikecore", "stone flooring.wav" },
            { "Tile_Transhumanist", "stone flooring.wav" },
            { "Voidmetal", "stone flooring.wav" },
            { "BioferritePlate", "stone flooring.wav" },
            { "BurnedBioferritePlate", "stone flooring.wav" },
            { "BrokenAsphalt", "stone flooring.wav" },

            // Natural Rock (smooth)
            { "VolcanicRock", "stone flooring.wav" },
            { "VolcanicRock_Smooth", "stone flooring.wav" },

            // Wood Floors
            { "WoodPlankFloor", "wood flooring.wav" },
            { "AncientWoodPlankFloor", "wood flooring.wav" },
            { "BurnedWoodPlankFloor", "wood flooring.wav" },
            { "Bridge", "wood flooring.wav" },
            { "Boards_Totemic", "wood flooring.wav" },

            // Carpet
            { "Carpet", "carpet.wav" },
            { "CarpetFine", "carpet.wav" },
            { "Carpet_Morbid", "carpet.wav" },
            { "Carpet_MindbendA", "carpet.wav" },
            { "Carpet_MindbendB", "carpet.wav" },
            { "Carpet_MindbendC", "carpet.wav" },
            { "Carpet_MindbendD", "carpet.wav" },
            { "Carpet_MindbendE", "carpet.wav" },
            { "Carpet_Transhumanist", "carpet.wav" },
            { "BurnedCarpet", "carpet.wav" },
            { "StrawMatting", "carpet.wav" },
            { "BurnedStrawMatting", "carpet.wav" },

            // Water
            { "WaterDeep", "water.wav" },
            { "WaterOceanDeep", "water.wav" },
            { "WaterShallow", "water.wav" },
            { "WaterOceanShallow", "water.wav" },
            { "WaterMovingShallow", "water.wav" },
            { "WaterMovingChestDeep", "water.wav" },
            { "ToxicWaterDeep", "water.wav" },
            { "ToxicWaterOceanDeep", "water.wav" },
            { "ToxicWaterShallow", "water.wav" },
            { "ToxicWaterOceanShallow", "water.wav" },
            { "ToxicWaterMovingShallow", "water.wav" },
            { "ToxicWaterMovingChestDeep", "water.wav" },
            { "HotSpring", "water.wav" },

            // Ice/Lava
            { "Ice", "water.wav" },
            { "LavaDeep", "water.wav" },
            { "CooledLava", "stone flooring.wav" },

            // Other
            { "AncientMegastructure", "stone flooring.wav" },
            { "Flesh", "mud.wav" },
            { "GraySurface", "stone flooring.wav" },
        };

        /// <summary>
        /// Substring patterns for matching unknown/modded terrain defNames.
        /// Only checked when no exact defName match is found.
        /// Order matters - more specific patterns should come first.
        /// </summary>
        private static readonly List<KeyValuePair<string, string>> substringTerrainAudioMap = new List<KeyValuePair<string, string>>()
        {
            new KeyValuePair<string, string>("Sandstone", "stone flooring.wav"),
            new KeyValuePair<string, string>("Granite", "stone flooring.wav"),
            new KeyValuePair<string, string>("Limestone", "stone flooring.wav"),
            new KeyValuePair<string, string>("Slate", "stone flooring.wav"),
            new KeyValuePair<string, string>("Marble", "stone flooring.wav"),
            new KeyValuePair<string, string>("Vacstone", "stone flooring.wav"),
            new KeyValuePair<string, string>("Flagstone", "stone flooring.wav"),
            new KeyValuePair<string, string>("Tile", "stone flooring.wav"),
            new KeyValuePair<string, string>("Concrete", "stone flooring.wav"),
            new KeyValuePair<string, string>("WoodPlank", "wood flooring.wav"),
            new KeyValuePair<string, string>("Bridge", "wood flooring.wav"),
            new KeyValuePair<string, string>("Boards", "wood flooring.wav"),
            new KeyValuePair<string, string>("Carpet", "carpet.wav"),
            new KeyValuePair<string, string>("Straw", "carpet.wav"),
            new KeyValuePair<string, string>("Mud", "mud.wav"),
            new KeyValuePair<string, string>("Marsh", "mud.wav"),
            new KeyValuePair<string, string>("Water", "water.wav"),
            new KeyValuePair<string, string>("Lava", "water.wav"),
            new KeyValuePair<string, string>("Ice", "water.wav"),
            new KeyValuePair<string, string>("Soil", "soil.wav"),
            new KeyValuePair<string, string>("Sand", "soil.wav"),
            new KeyValuePair<string, string>("Gravel", "stoney Soil.wav"),
        };

        /// <summary>
        /// Gets the audio filename for a given terrain type.
        /// Checks exact defName match first, then falls back to substring matching.
        /// </summary>
        /// <param name="terrain">The terrain definition</param>
        /// <returns>Audio filename if a match is found, null otherwise</returns>
        public static string GetAudioForTerrain(TerrainDef terrain)
        {
            if (terrain == null)
                return null;

            string defName = terrain.defName;

            // Check exact defName match first
            if (exactTerrainAudioMap.TryGetValue(defName, out string audioFile))
            {
                return audioFile;
            }

            // Fall back to substring matching for unknown/modded terrains
            foreach (var kvp in substringTerrainAudioMap)
            {
                if (defName.Contains(kvp.Key))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Checks if a terrain type has a matching audio file.
        /// </summary>
        /// <param name="terrain">The terrain definition</param>
        /// <returns>True if an audio match exists, false otherwise</returns>
        public static bool HasAudioMatch(TerrainDef terrain)
        {
            return GetAudioForTerrain(terrain) != null;
        }

        /// <summary>
        /// Plays the audio for a given terrain type if a match exists.
        /// </summary>
        /// <param name="terrain">The terrain definition</param>
        /// <param name="volume">Volume to play at (0.0 to 1.0)</param>
        /// <returns>True if audio was played, false if no match found</returns>
        public static bool PlayTerrainAudio(TerrainDef terrain, float volume = 0.5f)
        {
            if (terrain == null)
            {
                ModLogger.Warning("PlayTerrainAudio called with null terrain");
                return false;
            }

            string audioFile = GetAudioForTerrain(terrain);

            if (audioFile != null)
            {
                EmbeddedAudioHelper.PlayEmbeddedSound(audioFile, volume);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Determines whether a cell currently presents a wall for navigation purposes.
        /// This covers any full-fill edifice - man-made walls, ancient structures, and
        /// natural rock (mountain) alike. A door counts as a wall only while it is closed;
        /// an open door is a passable gap and is treated as terrain.
        /// </summary>
        /// <param name="cell">The map cell to test</param>
        /// <param name="map">The map containing the cell</param>
        /// <returns>True if the cell is currently walled, false otherwise</returns>
        public static bool IsWall(IntVec3 cell, Map map)
        {
            if (map == null)
                return false;

            Building edifice = cell.GetEdifice(map);
            if (edifice == null || edifice.def.Fillage != FillCategory.Full)
                return false;

            // A closed door is effectively a wall; an open door is a passable opening.
            if (edifice is Building_Door door)
                return !door.Open;

            return true;
        }

        /// <summary>
        /// Plays the audio cue for a map cell. A wall (man-made or natural rock, including a
        /// closed door) plays the wall sound and takes precedence over the terrain beneath it;
        /// otherwise the terrain's own sound plays.
        /// </summary>
        /// <param name="cell">The map cell the cursor landed on</param>
        /// <param name="map">The map containing the cell</param>
        /// <param name="volume">Volume to play at (0.0 to 1.0)</param>
        /// <returns>True if any audio was played, false otherwise</returns>
        public static bool PlayCellAudio(IntVec3 cell, Map map, float volume = 0.5f)
        {
            if (map == null)
                return false;

            if (IsWall(cell, map))
            {
                EmbeddedAudioHelper.PlayEmbeddedSound(WallAudioFile, volume);
                return true;
            }

            return PlayTerrainAudio(cell.GetTerrain(map), volume);
        }
    }
}
