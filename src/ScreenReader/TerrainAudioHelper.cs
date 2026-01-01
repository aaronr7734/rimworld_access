using RimWorld;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Maps terrain types to audio files for audio feedback during map navigation.
    /// </summary>
    public static class TerrainAudioHelper
    {
        /// <summary>
        /// Mapping of terrain label patterns to audio filenames.
        /// Keys are lowercase substrings to match against terrain labels.
        /// </summary>
        private static readonly Dictionary<string, string> terrainAudioMap = new Dictionary<string, string>()
        {
            // Order matters - more specific matches should come first
            { "rich soil", "Rich Soil.wav" },
            { "stony soil", "stoney Soil.wav" },  // Note: audio file uses "stoney" spelling
            { "stoney soil", "stoney Soil.wav" }, // Alternative spelling
            { "smoothed sandstone", "stone flooring.wav" },  // Smoothed stone walls
            { "smoothed granite", "stone flooring.wav" },
            { "smoothed limestone", "stone flooring.wav" },
            { "smoothed slate", "stone flooring.wav" },
            { "smoothed marble", "stone flooring.wav" },
            { "smooth sandstone", "stone flooring.wav" },  // Alternative spelling
            { "smooth granite", "stone flooring.wav" },
            { "smooth limestone", "stone flooring.wav" },
            { "smooth slate", "stone flooring.wav" },
            { "smooth marble", "stone flooring.wav" },
            { "sandstone", "stone flooring.wav" },  // Rough stone (natural rock)
            { "granite", "stone flooring.wav" },
            { "limestone", "stone flooring.wav" },
            { "slate", "stone flooring.wav" },
            { "marble", "stone flooring.wav" },
            { "stone floor", "stone flooring.wav" },
            { "stone tile", "stone flooring.wav" },
            { "paved tile", "stone flooring.wav" },  // Paved tile floor
            { "flagstone", "stone flooring.wav" },
            { "slate tile", "stone flooring.wav" },
            { "marble tile", "stone flooring.wav" },
            { "granite tile", "stone flooring.wav" },
            { "limestone tile", "stone flooring.wav" },
            { "sandstone tile", "stone flooring.wav" },
            { "wood floor", "wood flooring.wav" },
            { "wooden floor", "wood flooring.wav" },
            { "carpet", "carpet.wav" },
            { "mud", "mud.wav" },
            { "muddy", "mud.wav" },
            { "marsh", "mud.wav" },
            { "water", "water.wav" },
            { "shallow water", "water.wav" },
            { "deep water", "water.wav" },
            { "ice", "water.wav" },
            { "soil", "soil.wav" },  // Keep this last among soil types as fallback
        };

        /// <summary>
        /// Gets the audio filename for a given terrain type.
        /// </summary>
        /// <param name="terrain">The terrain definition</param>
        /// <returns>Audio filename if a match is found, null otherwise</returns>
        public static string GetAudioForTerrain(TerrainDef terrain)
        {
            if (terrain == null)
                return null;

            string terrainLabel = terrain.label.ToLower();

            // Check for matches in order (more specific first)
            foreach (var kvp in terrainAudioMap)
            {
                if (terrainLabel.Contains(kvp.Key))
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
    }
}
