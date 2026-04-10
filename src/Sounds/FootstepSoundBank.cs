using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public sealed class FootstepSoundCollection
    {
        private readonly string resourcePath;
        private readonly List<AudioClip> clips = new List<AudioClip>();
        private readonly float pitchVariance;
        private readonly float volumeVariance;
        private bool loaded;

        public int ClipCount => clips.Count;
        public bool HasClips => clips.Count > 0;

        public FootstepSoundCollection(string resourcePath, float pitchVariance, float volumeVariance)
        {
            this.resourcePath = resourcePath;
            this.pitchVariance = pitchVariance;
            this.volumeVariance = volumeVariance;
        }

        public void Load()
        {
            if (loaded) return;

            try
            {
                HashSet<string> seenClipNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (string searchPath in GetSearchPaths(resourcePath))
                {
                    foreach (AudioClip clip in ContentFinder<AudioClip>.GetAllInFolder(searchPath))
                    {
                        if (clip != null && seenClipNames.Add(clip.name))
                        {
                            clips.Add(clip);
                        }
                    }
                }

                loaded = true;
                if (clips.Count > 0)
                {
                    Log.Message($"[RimWorld Access] Loaded {clips.Count} footstep clips from {resourcePath}");
                }
                else
                {
                    Log.Warning($"[RimWorld Access] No footstep clips found for {resourcePath}");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimWorld Access] Error loading footstep clips from {resourcePath}: {ex.Message}");
            }
        }

        public (AudioClip clip, float pitch, float volume) GetRandomSound()
        {
            if (!loaded || clips.Count == 0)
            {
                return (null, 1f, 1f);
            }

            AudioClip clip = clips[Random.Range(0, clips.Count)];
            float pitch = 1f + Random.Range(-pitchVariance, pitchVariance);
            float volume = 1f + Random.Range(-volumeVariance, volumeVariance);
            return (clip, pitch, volume);
        }

        private static IEnumerable<string> GetSearchPaths(string folderPath)
        {
            string normalized = folderPath.Replace('\\', '/').Trim('/');
            yield return normalized;

            if (normalized.StartsWith("Sounds/", System.StringComparison.OrdinalIgnoreCase))
            {
                yield return normalized.Substring("Sounds/".Length);
            }
            else
            {
                yield return "Sounds/" + normalized;
            }
        }
    }

    public static class FootstepSoundBank
    {
        private static readonly Dictionary<string, FootstepSoundCollection> collections =
            new Dictionary<string, FootstepSoundCollection>(System.StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, (float pitchVariance, float volumeVariance)> folderDefinitions =
            new Dictionary<string, (float pitchVariance, float volumeVariance)>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "Sounds/human/dirt", (0.08f, 0.10f) },
                { "Sounds/human/stone", (0.10f, 0.10f) },
                { "Sounds/human/wood", (0.12f, 0.10f) },
                { "Sounds/human/metal", (0.15f, 0.08f) },
                { "Sounds/human/snow", (0.06f, 0.15f) },
                { "Sounds/human/carpet", (0.08f, 0.08f) },
                { "Sounds/human/water", (0.10f, 0.10f) },
                { "Sounds/human/bridge", (0.10f, 0.10f) },
                { "Sounds/animal/heavy", (0.15f, 0.10f) },
                { "Sounds/animal/light", (0.20f, 0.15f) },
                { "Sounds/mechanoid", (0.05f, 0.05f) },
                { "Sounds/default", (0.10f, 0.10f) },
            };

        private static bool initialized;
        private static Camera cachedCamera;
        private static readonly List<PooledAudioSource> audioSources = new List<PooledAudioSource>();
        private static GameObject audioSourceRoot;
        private const int InitialAudioSourcePoolSize = 16;
        private const int MaxAudioSourcePoolSize = 32;

        // Optimized: Room openness cache to avoid recomputing every footstep
        private static readonly Dictionary<Room, (float openness, int cacheTick)> roomOpennessCache = new Dictionary<Room, (float, int)>();
        private static int currentCacheTick = 0;

        public static bool EnsureInitialized()
        {
            if (!initialized)
            {
                Initialize();
            }

            return EnsureAudioSource();
        }

        public static void Reset()
        {
            initialized = false;
            collections.Clear();
            audioSources.Clear();
            if (audioSourceRoot != null)
            {
                Object.Destroy(audioSourceRoot);
                audioSourceRoot = null;
            }
            cachedCamera = null;
            ClearRoomOpennessCache();
            ScreenPanUtility.ClearCachedCamera();
            CameraZoomUtility.ClearCachedCamera();
        }

        public static bool PlayFootstep(Pawn pawn, TerrainDef terrain, float volume, FootstepSpatialProfile spatialProfile)
        {
            if (!EnsureInitialized()) return false;

            FootstepSoundCollection collection = GetForPawnAndTerrain(pawn, terrain);
            if (collection == null || !collection.HasClips) return false;

            (AudioClip clip, float pitch, float volumeMultiplier) = collection.GetRandomSound();
            if (clip == null) return false;

            PooledAudioSource pooledSource = GetAvailableAudioSource();
            if (pooledSource == null) return false;

            pooledSource.Source.pitch = Mathf.Clamp(
                pitch * FootstepClassifier.GetPitchMultiplier(pawn) * spatialProfile.PitchFactor,
                0.5f,
                1.75f);

            if (RimWorldAccessMod_Settings.Settings.FootstepStereoPan && pawn != null)
            {
                pooledSource.Source.spatialBlend = 1f;
                pooledSource.Source.transform.position = pawn.DrawPos;
            }
            else
            {
                pooledSource.Source.spatialBlend = 0f;
            }

            ApplySpatialMix(pooledSource, pawn, spatialProfile);

            if (RimWorldAccessMod_Settings.Settings.FootstepStereoPan && pawn != null)
            {
                pooledSource.ITDProcessor.SetEnabled(true);
                pooledSource.ITDProcessor.SetPan(spatialProfile.Pan);
            }
            else
            {
                pooledSource.ITDProcessor.SetEnabled(false);
            }

            pooledSource.Source.PlayOneShot(clip, Mathf.Clamp(volume * volumeMultiplier * spatialProfile.Presence, 0f, 1.4f));
            return true;
        }

        public static FootstepSoundCollection GetForPawnAndTerrain(Pawn pawn, TerrainDef terrain)
        {
            if (pawn == null) return GetCollection("Sounds/default");

            FootstepCategory category = FootstepClassifier.ClassifyPawn(pawn);
            string categoryPath;
            switch (category)
            {
                case FootstepCategory.Human:
                    categoryPath = "Sounds/human";
                    break;
                case FootstepCategory.LargeAnimal:
                    categoryPath = "Sounds/animal/heavy";
                    break;
                case FootstepCategory.SmallAnimal:
                    categoryPath = "Sounds/animal/light";
                    break;
                case FootstepCategory.Mechanoid:
                    categoryPath = "Sounds/mechanoid";
                    break;
                default:
                    categoryPath = "Sounds/default";
                    break;
            }

            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings != null && settings.FootstepTerrainVariation && category == FootstepCategory.Human)
            {
                FootstepSoundCollection terrainCollection = GetCollection(categoryPath + "/" + GetTerrainSuffix(terrain));
                if (terrainCollection != null && terrainCollection.HasClips)
                {
                    return terrainCollection;
                }
            }

            FootstepSoundCollection categoryCollection = GetCollection(categoryPath);
            if (categoryCollection != null && categoryCollection.HasClips)
            {
                return categoryCollection;
            }

            FootstepSoundCollection defaultCollection = GetCollection("Sounds/default");
            if (defaultCollection != null && defaultCollection.HasClips)
            {
                return defaultCollection;
            }

            return GetCollection("Sounds/human/dirt");
        }

        private static FootstepSoundCollection GetCollection(string path)
        {
            collections.TryGetValue(path, out FootstepSoundCollection collection);
            return collection;
        }

        private static void Initialize()
        {
            initialized = true;
            collections.Clear();

            foreach (KeyValuePair<string, (float pitchVariance, float volumeVariance)> entry in folderDefinitions)
            {
                FootstepSoundCollection collection =
                    new FootstepSoundCollection(entry.Key, entry.Value.pitchVariance, entry.Value.volumeVariance);
                collection.Load();
                collections[entry.Key] = collection;
            }

            int totalClips = collections.Values.Sum(collection => collection.ClipCount);
            Log.Message($"[RimWorld Access] Footstep sound bank initialized with {totalClips} clips.");
        }

        private static bool EnsureAudioSource()
        {
            Camera activeCamera = Find.Camera ?? Camera.main;
            if (activeCamera == null) return false;

            if (cachedCamera == activeCamera && audioSources.Count >= InitialAudioSourcePoolSize)
            {
                return true;
            }

            if (audioSourceRoot != null)
            {
                Object.Destroy(audioSourceRoot);
                audioSourceRoot = null;
            }

            cachedCamera = activeCamera;
            audioSources.Clear();
            ClearRoomOpennessCache(); // Clear cache on camera change
            audioSourceRoot = new GameObject("RimWorldAccessFootstepAudioPool");

            for (int i = 0; i < InitialAudioSourcePoolSize; i++)
            {
                AddAudioSource(i);
            }
            return true;
        }

        private static void AddAudioSource(int index)
        {
            if (audioSourceRoot == null)
            {
                return;
            }

            GameObject sourceObject = new GameObject("RimWorldAccessFootstepAudio_" + index);
            sourceObject.transform.SetParent(audioSourceRoot.transform, false);
            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.loop = false;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 200f;
            source.maxDistance = 300f;
            source.spread = 0f;

            // Optimized: Create low pass filter only once and reuse configuration
            AudioLowPassFilter lowPassFilter = sourceObject.AddComponent<AudioLowPassFilter>();
            lowPassFilter.enabled = true;
            lowPassFilter.lowpassResonanceQ = 1.05f; // Pre-set constant value

            // Optimized: Delay reverb filter creation - only create when needed
            AudioReverbFilter reverbFilter = sourceObject.AddComponent<AudioReverbFilter>();
            reverbFilter.enabled = false;

            ITDProcessor itdProcessor = sourceObject.AddComponent<ITDProcessor>();
            itdProcessor.SetEnabled(false);

            audioSources.Add(new PooledAudioSource(source, lowPassFilter, reverbFilter, itdProcessor));
        }

        private static PooledAudioSource GetAvailableAudioSource()
        {
            for (int i = 0; i < audioSources.Count; i++)
            {
                if (!audioSources[i].Source.isPlaying)
                {
                    return audioSources[i];
                }
            }

            if (audioSources.Count < MaxAudioSourcePoolSize)
            {
                AddAudioSource(audioSources.Count);
                return audioSources[audioSources.Count - 1];
            }

            return null;
        }

        private static void ApplySpatialMix(PooledAudioSource pooledSource, Pawn pawn, FootstepSpatialProfile spatialProfile)
        {
            pooledSource.LowPassFilter.cutoffFrequency = Mathf.Lerp(9000f, 16500f, spatialProfile.Brightness);
            pooledSource.LowPassFilter.lowpassResonanceQ = 1.05f;

            Room room = pawn?.GetRoom();
            if (room == null || room.PsychologicallyOutdoors)
            {
                pooledSource.ReverbFilter.enabled = false;
                pooledSource.Source.reverbZoneMix = 0f;
                return;
            }

            pooledSource.ReverbFilter.enabled = true;
            pooledSource.ReverbFilter.reverbPreset = GetRoomReverbPreset(room);
            pooledSource.Source.reverbZoneMix = GetRoomReverbIntensity(room);
        }

        private static AudioReverbPreset GetRoomReverbPreset(Room room)
        {
            if (room == null) return AudioReverbPreset.Room;

            float openness = GetRoomOpenness(room);
            if (openness >= 4f) return AudioReverbPreset.StoneCorridor;
            if (openness >= 2.5f) return AudioReverbPreset.Livingroom;
            return AudioReverbPreset.Room;
        }

        private static float GetRoomReverbIntensity(Room room)
        {
            if (room == null) return 0f;
            float openness = GetRoomOpenness(room);
            return Mathf.Clamp01(Mathf.Lerp(0.15f, 0.6f, Mathf.InverseLerp(1f, 5f, openness)));
        }

        private static float GetRoomOpenness(Room room)
        {
            // Optimized: Use cached room openness value
            if (room == null || room.CellCount == 0) return 0f;

            // Check cache (valid for ~60 ticks)
            if (roomOpennessCache.TryGetValue(room, out var cached))
            {
                if (currentCacheTick - cached.cacheTick < 60)
                {
                    return cached.openness;
                }
            }

            int minX = int.MaxValue, maxX = int.MinValue;
            int minZ = int.MaxValue, maxZ = int.MinValue;

            foreach (IntVec3 cell in room.Cells)
            {
                if (cell.x < minX) minX = cell.x;
                if (cell.x > maxX) maxX = cell.x;
                if (cell.z < minZ) minZ = cell.z;
                if (cell.z > maxZ) maxZ = cell.z;
            }

            int width = maxX - minX + 1;
            int depth = maxZ - minZ + 1;

            if (width == 0 || depth == 0) return 0f;

            int boundingArea = width * depth;
            float cellRatio = (float)room.CellCount / boundingArea;

            // Aspect ratio: wide rooms (squares) = 1, narrow corridors < 1
            float aspectRatio = (float)Mathf.Min(width, depth) / Mathf.Max(width, depth);

            // Combine: cellRatio (how filled) * aspectRatio (how square)
            float openness = cellRatio * aspectRatio;

            // Cache the result
            roomOpennessCache[room] = (openness, currentCacheTick);
            return openness;
        }

        private static void ClearRoomOpennessCache()
        {
            roomOpennessCache.Clear();
            currentCacheTick = 0;
        }

        private static string GetTerrainSuffix(TerrainDef terrain)
        {
            string terrainDefName = terrain?.defName ?? string.Empty;
            if (string.IsNullOrEmpty(terrainDefName)) return "dirt";

            string name = terrainDefName.ToLowerInvariant();
            if (name.Contains("bridge")) return "bridge";
            if (name.Contains("carpet")) return "carpet";
            if (name.Contains("water") || name.Contains("marsh")) return "water";
            if (name.Contains("snow") || name.Contains("ice")) return "snow";
            if (name.Contains("metal") || name.Contains("steel")) return "metal";
            if (name.Contains("wood") || name.Contains("plank") || name.Contains("hardwood")) return "wood";
            if (name.Contains("stone") || name.Contains("tile") || name.Contains("concrete") || name.Contains("marble") || name.Contains("granite")) return "stone";
            return "dirt";
        }

        private sealed class PooledAudioSource
        {
            public PooledAudioSource(AudioSource source, AudioLowPassFilter lowPassFilter, AudioReverbFilter reverbFilter, ITDProcessor itdProcessor)
            {
                Source = source;
                LowPassFilter = lowPassFilter;
                ReverbFilter = reverbFilter;
                ITDProcessor = itdProcessor;
            }

            public AudioSource Source { get; }
            public AudioLowPassFilter LowPassFilter { get; }
            public AudioReverbFilter ReverbFilter { get; }
            public ITDProcessor ITDProcessor { get; }
        }
    }
}
