using RimWorld;
using RimWorld.Planet;

namespace RimWorldAccess
{
    /// <summary>
    /// Tracks biome changes during world navigation and provides biome descriptions.
    /// Returns the biome description paragraph when entering a new biome, null when
    /// staying in the same biome. "New" means different from the LAST biome -
    /// revisiting a biome after passing through another triggers re-announcement.
    /// Used by both in-game world map and world generation starting site screen.
    /// </summary>
    public static class BiomeDescriptionTracker
    {
        private static BiomeDef lastAnnouncedBiome = null;

        /// <summary>
        /// Clears all tracking state. Call when closing world navigation.
        /// </summary>
        public static void Reset()
        {
            lastAnnouncedBiome = null;
        }

        /// <summary>
        /// The biome description when the tile's biome differs from the last announced one, else null. Updates tracking.
        /// </summary>
        public static string GetBiomeDescriptionIfNew(PlanetTile tile)
        {
            return GetBiomeDescriptionIfNew(tile, ref lastAnnouncedBiome);
        }

        /// <summary>The same rule against a caller-owned memory, for a reader that must not disturb the keyboard cursor's.</summary>
        public static string GetBiomeDescriptionIfNew(PlanetTile tile, ref BiomeDef lastBiome)
        {
            if (!tile.Valid) return null;

            BiomeDef currentBiome = tile.Tile?.PrimaryBiome;
            if (currentBiome == null) return null;

            if (currentBiome == lastBiome) return null;

            lastBiome = currentBiome;
            return currentBiome.description;
        }
    }
}
