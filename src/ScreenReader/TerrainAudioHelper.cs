using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Maps terrain types to footstep sound-folder categories (dirt, stone, wood,
    /// metal, snow, carpet, water, bridge) and plays terrain cue sounds for map
    /// cursor movement via the footstep sound bank.
    /// </summary>
    public static class TerrainAudioHelper
    {
        /// <summary>
        /// Maps a terrain type to its footstep sound folder category.
        /// Maps defNames directly to one of the 8 footstep sound folders
        /// (dirt, stone, wood, metal, snow, carpet, water, bridge).
        /// </summary>
        public static string GetFootstepCategory(TerrainDef terrain)
        {
            if (terrain == null) return "dirt";
            return exactFootstepCategoryMap.TryGetValue(terrain.defName, out string category)
                ? category
                : GetFootstepCategoryBySubstring(terrain.defName);
        }

        private static readonly Dictionary<string, string> exactFootstepCategoryMap = new Dictionary<string, string>()
        {
            // Dirt/Earth
            { "Soil", "dirt" },
            { "SoilRich", "dirt" },
            { "Gravel", "dirt" },
            { "MossyTerrain", "dirt" },
            { "MarshyTerrain", "dirt" },
            { "Mud", "dirt" },
            { "Marsh", "dirt" },
            { "PackedDirt", "dirt" },
            { "Riverbank", "dirt" },
            { "FungalGravel", "dirt" },
            { "GlowforestSoil", "dirt" },
            { "GrasslandSoil", "dirt" },
            { "DryLakeBed", "dirt" },
            { "Sand", "dirt" },
            { "SoftSand", "dirt" },
            { "BrokenAsphalt", "dirt" },
            { "Flesh", "dirt" },

            // Stone
            { "TileSandstone", "stone" },
            { "TileGranite", "stone" },
            { "TileLimestone", "stone" },
            { "TileSlate", "stone" },
            { "TileMarble", "stone" },
            { "TileVacstone", "stone" },
            { "FineTileSandstone", "stone" },
            { "FineTileGranite", "stone" },
            { "FineTileLimestone", "stone" },
            { "FineTileSlate", "stone" },
            { "FineTileMarble", "stone" },
            { "FineTileVacstone", "stone" },
            { "Tile_MorbidSandstone", "stone" },
            { "Tile_MorbidGranite", "stone" },
            { "Tile_MorbidLimestone", "stone" },
            { "Tile_MorbidSlate", "stone" },
            { "Tile_MorbidMarble", "stone" },
            { "Tile_MorbidVacstone", "stone" },
            { "Tile_SpikecoreSandstone", "stone" },
            { "Tile_SpikecoreGranite", "stone" },
            { "Tile_SpikecoreLimestone", "stone" },
            { "Tile_SpikecoreSlate", "stone" },
            { "Tile_SpikecoreMarble", "stone" },
            { "Tile_SpikecoreVacstone", "stone" },
            { "Tile_TotemicSandstone", "stone" },
            { "Tile_TotemicGranite", "stone" },
            { "Tile_TotemicLimestone", "stone" },
            { "Tile_TotemicSlate", "stone" },
            { "Tile_TotemicMarble", "stone" },
            { "Tile_TotemicVacstone", "stone" },
            { "FlagstoneSandstone", "stone" },
            { "FlagstoneGranite", "stone" },
            { "FlagstoneLimestone", "stone" },
            { "FlagstoneSlate", "stone" },
            { "FlagstoneMarble", "stone" },
            { "FlagstoneVacstone", "stone" },
            { "PavedTile", "stone" },
            { "Concrete", "stone" },
            { "AncientTile", "stone" },
            { "Plates_Spikecore", "stone" },
            { "Tile_Transhumanist", "stone" },
            { "AncientMegastructure", "stone" },
            { "VolcanicRock", "stone" },
            { "VolcanicRock_Smooth", "stone" },
            { "CooledLava", "stone" },
            { "GraySurface", "stone" },

            // Metal
            { "MetalTile", "metal" },
            { "SterileTile", "metal" },
            { "SilverTile", "metal" },
            { "GoldTile", "metal" },
            { "Voidmetal", "metal" },
            { "BioferritePlate", "metal" },
            { "BurnedBioferritePlate", "metal" },

            // Wood
            { "WoodPlankFloor", "wood" },
            { "AncientWoodPlankFloor", "wood" },
            { "BurnedWoodPlankFloor", "wood" },
            { "Boards_Totemic", "wood" },

            // Bridge
            { "Bridge", "bridge" },

            // Carpet
            { "Carpet", "carpet" },
            { "CarpetFine", "carpet" },
            { "Carpet_Morbid", "carpet" },
            { "Carpet_MindbendA", "carpet" },
            { "Carpet_MindbendB", "carpet" },
            { "Carpet_MindbendC", "carpet" },
            { "Carpet_MindbendD", "carpet" },
            { "Carpet_MindbendE", "carpet" },
            { "Carpet_Transhumanist", "carpet" },
            { "BurnedCarpet", "carpet" },
            { "StrawMatting", "carpet" },
            { "BurnedStrawMatting", "carpet" },

            // Water
            { "WaterDeep", "water" },
            { "WaterOceanDeep", "water" },
            { "WaterShallow", "water" },
            { "WaterOceanShallow", "water" },
            { "WaterMovingShallow", "water" },
            { "WaterMovingChestDeep", "water" },
            { "ToxicWaterDeep", "water" },
            { "ToxicWaterOceanDeep", "water" },
            { "ToxicWaterShallow", "water" },
            { "ToxicWaterOceanShallow", "water" },
            { "ToxicWaterMovingShallow", "water" },
            { "ToxicWaterMovingChestDeep", "water" },
            { "HotSpring", "water" },
            { "LavaDeep", "water" },

            // Snow/Ice
            { "Snow", "snow" },
            { "SnowMedium", "snow" },
            { "SnowHard", "snow" },
            { "Ice", "snow" },
        };

        private static string GetFootstepCategoryBySubstring(string defName)
        {
            if (defName.Contains("Bridge")) return "bridge";
            if (defName.Contains("Metal") || defName.Contains("Steel") || defName.Contains("Bioferrite")) return "metal";
            if (defName.Contains("Snow") || defName.Contains("Ice")) return "snow";
            if (defName.Contains("Carpet") || defName.Contains("Straw")) return "carpet";
            if (defName.Contains("Water") || defName.Contains("Marsh") || defName.Contains("Lava")) return "water";
            if (defName.Contains("WoodPlank") || defName.Contains("Boards")) return "wood";
            if (defName.Contains("Sandstone") || defName.Contains("Granite") || defName.Contains("Limestone") ||
                defName.Contains("Slate") || defName.Contains("Marble") || defName.Contains("Flagstone") ||
                defName.Contains("Tile") || defName.Contains("Concrete")) return "stone";
            return "dirt";
        }

        /// <summary>
        /// Plays the terrain-cue audio for cursor movement using the footstep sound system.
        /// Delegates to FootstepSoundBank so cursor cues share the same sound pool as
        /// pawn footsteps.
        /// </summary>
        public static bool PlayTerrainAudio(TerrainDef terrain, float volume = 0.5f)
        {
            if (terrain == null) return false;
            return FootstepSoundBank.PlayTerrainSound(terrain, volume);
        }
    }
}
