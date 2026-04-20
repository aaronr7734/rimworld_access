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
        /// Returns the biome description if the tile's biome differs from the last announced biome.
        /// Returns null if the biome is the same. Updates tracking on every call.
        /// </summary>
        public static string GetBiomeDescriptionIfNew(PlanetTile tile)
        {
            if (!tile.Valid) return null;

            BiomeDef currentBiome = tile.Tile?.PrimaryBiome;
            if (currentBiome == null) return null;

            if (currentBiome == lastAnnouncedBiome) return null;

            lastAnnouncedBiome = currentBiome;
            return currentBiome.description;
        }
    }
}
