using RimWorld;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Picks the navigation cue for a terrain from the game's own data: its tags, category,
    /// construction effect, cost materials and burn source. Modded terrain that
    /// declares the same data sounds right with no table entry.
    /// </summary>
    public static class TerrainAudioHelper
    {
        /// <summary>
        /// Audio file played when the cursor is over a wall (man-made or natural rock).
        /// Takes precedence over the terrain beneath the wall.
        /// </summary>
        public const string WallAudioFile = "wall.wav";

        private const string SoilAudio = "soil.wav";
        private const string RichSoilAudio = "Rich Soil.wav";
        private const string StonySoilAudio = "stoney Soil.wav";
        private const string MudAudio = "mud.wav";
        private const string MarshAudio = "marsh.wav";
        private const string SandAudio = "sand.wav";
        private const string WaterAudio = "water.wav";
        private const string IceAudio = "ice.wav";
        private const string LavaAudio = "lava.wav";
        private const string RoughStoneAudio = "rough stone.wav";
        private const string SmoothStoneAudio = "smooth stone.wav";
        private const string StoneFlooringAudio = "stone flooring.wav";
        private const string MetalFlooringAudio = "metal flooring.wav";
        private const string WoodFlooringAudio = "wood flooring.wav";
        private const string CarpetAudio = "carpet.wav";

        /// <summary>
        /// Vanilla terrains whose defs carry nothing that names their surface: no material,
        /// construction effect, or tag that tells them apart from a generic floor.
        /// </summary>
        private static readonly Dictionary<string, string> unmarkedTerrainAudio = new Dictionary<string, string>()
        {
            { "LavaDeep", LavaAudio },
            { "LavaShallow", LavaAudio },
            { "AncientTile", MetalFlooringAudio },
            { "Voidmetal", MetalFlooringAudio },
            { "OrbitalPlatform", MetalFlooringAudio },
            { "MechanoidPlatform", MetalFlooringAudio },
            { "AncientWoodPlankFloor", WoodFlooringAudio },
            { "StrawMatting", CarpetAudio },
            { "CooledLava", RoughStoneAudio },
            { "MarshyTerrain", MudAudio },
            { "Riverbank", MudAudio },
            { "Flesh", MudAudio },
            { "InsectSludge", MudAudio },
        };

        private static readonly Dictionary<TerrainDef, string> audioCache = new Dictionary<TerrainDef, string>();
        private static Dictionary<TerrainDef, List<TerrainDef>> burnSources;
        private static EffecterDef constructWood;

        /// <summary>
        /// Gets the audio filename for a terrain, or null for terrain with no surface to step on.
        /// </summary>
        public static string GetAudioForTerrain(TerrainDef terrain)
        {
            if (terrain == null)
                return null;

            if (!audioCache.TryGetValue(terrain, out string audio))
            {
                // Seeded before classifying so a burn-source cycle resolves instead of recursing.
                audioCache[terrain] = StoneFlooringAudio;
                audio = Classify(terrain);
                audioCache[terrain] = audio;
            }
            return audio;
        }

        private static string Classify(TerrainDef terrain)
        {
            if (unmarkedTerrainAudio.TryGetValue(terrain.defName, out string audio))
                return audio;

            if (terrain.IsIce)
                return IceAudio;
            if (terrain.HasTag("WaterMarshy"))
                return MarshAudio;
            if (terrain.IsWater)
                return WaterAudio;
            if (terrain.categoryType == TerrainDef.TerrainCategoryType.Sand)
                return SandAudio;
            if (terrain.categoryType == TerrainDef.TerrainCategoryType.Stone)
                return terrain.smoothedTerrain != null ? RoughStoneAudio : SmoothStoneAudio;

            audio = MaterialAudio(terrain);
            if (audio != null)
                return audio;

            // A burned floor keeps the sound of what it burned from.
            if (BurnSources().TryGetValue(terrain, out List<TerrainDef> sources))
            {
                foreach (TerrainDef source in sources)
                {
                    audio = GetAudioForTerrain(source);
                    if (audio != StoneFlooringAudio)
                        return audio;
                }
            }

            if (terrain.HasTag("Mud"))
                return MudAudio;
            // Growing media laid as floors (fungal gravel) carry fertility but no soil tag.
            if (terrain.IsSoil || terrain.categoryType == TerrainDef.TerrainCategoryType.Soil || terrain.fertility > 0f)
                return GradeSoil(terrain);

            if (terrain.exposesToVacuum)
                return null;

            return StoneFlooringAudio;
        }

        private static string MaterialAudio(TerrainDef terrain)
        {
            if (terrain.IsCarpet)
                return CarpetAudio;
            if (terrain.constructEffect != null)
            {
                if (terrain.constructEffect == EffecterDefOf.ConstructMetal)
                    return MetalFlooringAudio;
                if (constructWood == null)
                    constructWood = DefDatabase<EffecterDef>.GetNamedSilentFail("ConstructWood");
                if (terrain.constructEffect == constructWood)
                    return WoodFlooringAudio;
            }

            // Metallic costs are skipped: concrete is paid for in steel.
            List<ThingDefCountClass> costs = terrain.CostList;
            if (costs == null)
                return null;
            foreach (ThingDefCountClass cost in costs)
            {
                List<StuffCategoryDef> categories = cost.thingDef?.stuffProps?.categories;
                if (categories == null)
                    continue;
                if (categories.Contains(StuffCategoryDefOf.Fabric))
                    return CarpetAudio;
                if (categories.Contains(StuffCategoryDefOf.Woody))
                    return WoodFlooringAudio;
            }
            return null;
        }

        /// <summary>
        /// Graded against vanilla soil's own fertility: richer reads as rich soil, poorer
        /// (but still growable) as stony soil. Infertile packed dirt stays plain soil.
        /// </summary>
        private static string GradeSoil(TerrainDef terrain)
        {
            float baseline = TerrainDefOf.Soil.fertility;
            if (terrain.fertility > baseline)
                return RichSoilAudio;
            if (terrain.fertility > 0f && terrain.fertility < baseline)
                return StonySoilAudio;
            return SoilAudio;
        }

        private static Dictionary<TerrainDef, List<TerrainDef>> BurnSources()
        {
            if (burnSources != null)
                return burnSources;

            burnSources = new Dictionary<TerrainDef, List<TerrainDef>>();
            foreach (TerrainDef def in DefDatabase<TerrainDef>.AllDefsListForReading)
            {
                if (def.burnedDef == null)
                    continue;
                if (!burnSources.TryGetValue(def.burnedDef, out List<TerrainDef> list))
                    burnSources[def.burnedDef] = list = new List<TerrainDef>();
                list.Add(def);
            }
            return burnSources;
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

            // Never reveal what lies under fog of war. An undiscovered cell must not
            // betray its terrain or any natural-rock wall through an audio cue.
            if (cell.Fogged(map))
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
