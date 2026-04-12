using System.Collections.Generic;
using System.Linq;
using RimWorld;
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

        private static bool debugTerrainLogging;
        private static int debugFootstepCounter;

        // Wall occlusion: cached listener room (refreshed once per tick)
        private static Room cachedListenerRoom;
        private static int cachedListenerRoomTick = -1;
        private const int MaxOcclusionBFSRegions = 30;
        private const float FullOcclusionFactor = 0.15f;
        private const float WallOcclusionBase = 0.3f;
        private const float OpenDoorBonus = 0.2f;
        private const float ClosedDoorBonus = 0.05f;
        private const float OccludedCutoffHz = 4000f;

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
            cachedListenerRoom = null;
            cachedListenerRoomTick = -1;
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

            if (pawn != null && FootstepClassifier.ClassifyPawn(pawn) == FootstepCategory.Mechanoid)
            {
                pooledSource.HighPassFilter.enabled = true;
                pooledSource.HighPassFilter.cutoffFrequency = FootstepClassifier.GetMechHighPassCutoff(pawn);
            }
            else
            {
                pooledSource.HighPassFilter.enabled = false;
            }

            float wallOcclusion = GetWallOcclusion(pawn);
            ApplySpatialMix(pooledSource, pawn, spatialProfile, wallOcclusion);

            if (RimWorldAccessMod_Settings.Settings.FootstepStereoPan && pawn != null)
            {
                pooledSource.ITDProcessor.SetEnabled(true);
                pooledSource.ITDProcessor.SetPan(spatialProfile.Pan);
            }
            else
            {
                pooledSource.ITDProcessor.SetEnabled(false);
            }

            pooledSource.Source.PlayOneShot(clip, Mathf.Clamp(volume * volumeMultiplier * spatialProfile.Presence * wallOcclusion, 0f, 1.4f));
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
                string suffix = GetTerrainSuffix(terrain);
                string terrainPath = categoryPath + "/" + suffix;
                FootstepSoundCollection terrainCollection = GetCollection(terrainPath);

                if (debugTerrainLogging && ++debugFootstepCounter % 120 == 0)
                {
                    Log.Message($"[RimWorld Access] Terrain debug: defName={terrain?.defName ?? "null"} → suffix={suffix} → path={terrainPath} → clips={terrainCollection?.ClipCount ?? 0}");
                }

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
            Log.Message($"[RimWorld Access] Footstep sound bank initialized with {totalClips} clips:");
            foreach (KeyValuePair<string, FootstepSoundCollection> entry in collections)
            {
                Log.Message($"[RimWorld Access]   {entry.Key}: {entry.Value.ClipCount} clips, hasClips={entry.Value.HasClips}");
            }
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

            AudioHighPassFilter highPassFilter = sourceObject.AddComponent<AudioHighPassFilter>();
            highPassFilter.enabled = false;
            highPassFilter.highpassResonanceQ = 1.0f;

            audioSources.Add(new PooledAudioSource(source, lowPassFilter, highPassFilter, reverbFilter, itdProcessor));
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

        private static void ApplySpatialMix(PooledAudioSource pooledSource, Pawn pawn, FootstepSpatialProfile spatialProfile, float wallOcclusion)
        {
            float baseCutoff = Mathf.Lerp(9000f, 16500f, spatialProfile.Brightness);
            pooledSource.LowPassFilter.cutoffFrequency = Mathf.Lerp(OccludedCutoffHz, baseCutoff, wallOcclusion);
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

        private static Room GetListenerRoom()
        {
            int tick = Find.TickManager?.TicksGame ?? -1;
            if (tick == cachedListenerRoomTick)
                return cachedListenerRoom;

            Map map = Find.CurrentMap;
            if (map == null) return null;

            IntVec3 listenerPos = MapNavigationState.CurrentCursorPosition;
            if (listenerPos.IsValid && listenerPos.InBounds(map))
            {
                cachedListenerRoom = listenerPos.GetRoom(map);
                cachedListenerRoomTick = tick;
            }
            return cachedListenerRoom;
        }

        private static float GetWallOcclusion(Pawn pawn)
        {
            if (pawn == null) return 1f;

            Room pawnRoom = pawn.GetRoom();
            Room listenerRoom = GetListenerRoom();

            if (pawnRoom == null || listenerRoom == null) return 1f;
            if (pawnRoom == listenerRoom) return 1f;
            if (pawnRoom.PsychologicallyOutdoors && listenerRoom.PsychologicallyOutdoors) return 1f;

            Map map = pawn.Map;
            if (map == null) return WallOcclusionBase;

            Region pawnRegion = pawn.Position.GetRegion(map, RegionType.Set_All);
            IntVec3 listenerPos = MapNavigationState.CurrentCursorPosition;
            Region listenerRegion = listenerPos.IsValid && listenerPos.InBounds(map)
                ? listenerPos.GetRegion(map, RegionType.Set_All)
                : null;

            if (pawnRegion == null || listenerRegion == null) return WallOcclusionBase;
            if (pawnRegion == listenerRegion) return 1f;

            int openDoors = 0;
            int closedDoors = 0;
            bool reachedListener = false;

            RegionTraverser.BreadthFirstTraverse(
                pawnRegion,
                RegionTraverser.PassAll,
                (Region reg) =>
                {
                    if (reg == listenerRegion)
                    {
                        reachedListener = true;
                        return true;
                    }
                    if (reg.IsDoorway && reg.door != null)
                    {
                        if (reg.door.FreePassage)
                            openDoors++;
                        else
                            closedDoors++;
                    }
                    return false;
                },
                MaxOcclusionBFSRegions,
                RegionType.Set_All);

            if (!reachedListener) return FullOcclusionFactor;

            float occlusion = WallOcclusionBase
                + openDoors * OpenDoorBonus
                + closedDoors * ClosedDoorBonus;

            return Mathf.Clamp01(occlusion);
        }

        private static AudioReverbPreset GetRoomReverbPreset(Room room)
        {
            if (room == null) return AudioReverbPreset.Room;
            int cellCount = room.CellCount;
            if (cellCount >= 100) return AudioReverbPreset.StoneCorridor;
            if (cellCount >= 25) return AudioReverbPreset.Livingroom;
            return AudioReverbPreset.Room;
        }

        private static float GetRoomReverbIntensity(Room room)
        {
            if (room == null) return 0f;
            int cellCount = room.CellCount;
            return Mathf.Clamp01(Mathf.Lerp(0.15f, 0.6f, Mathf.InverseLerp(10f, 120f, (float)cellCount)));
        }

        private static string GetTerrainSuffix(TerrainDef terrain)
        {
            return TerrainAudioHelper.GetFootstepCategory(terrain);
        }

        public static bool PlayTerrainSound(TerrainDef terrain, float volume)
        {
            if (!EnsureInitialized()) return false;

            string suffix = GetTerrainSuffix(terrain);
            FootstepSoundCollection collection = GetCollection("Sounds/human/" + suffix);
            if (collection == null || !collection.HasClips)
            {
                collection = GetCollection("Sounds/human/dirt");
            }
            if (collection == null || !collection.HasClips) return false;

            (AudioClip clip, float pitch, float volumeMultiplier) = collection.GetRandomSound();
            if (clip == null) return false;

            PooledAudioSource pooledSource = GetAvailableAudioSource();
            if (pooledSource == null) return false;

            pooledSource.Source.pitch = Mathf.Clamp(pitch, 0.5f, 1.75f);
            pooledSource.Source.spatialBlend = 0f;
            pooledSource.ITDProcessor.SetEnabled(false);

            // Apply room reverb at the cursor position
            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
            Map map = Find.CurrentMap;
            Room room = (map != null && cursorPos.IsValid && cursorPos.InBounds(map))
                ? cursorPos.GetRoom(map)
                : null;

            if (room == null || room.PsychologicallyOutdoors)
            {
                pooledSource.LowPassFilter.cutoffFrequency = 16500f;
                pooledSource.ReverbFilter.enabled = false;
                pooledSource.Source.reverbZoneMix = 0f;
            }
            else
            {
                pooledSource.LowPassFilter.cutoffFrequency = 16500f;
                pooledSource.ReverbFilter.enabled = true;
                pooledSource.ReverbFilter.reverbPreset = GetRoomReverbPreset(room);
                pooledSource.Source.reverbZoneMix = GetRoomReverbIntensity(room);
            }

            pooledSource.Source.PlayOneShot(clip, Mathf.Clamp(volume * volumeMultiplier, 0f, 1f));
            return true;
        }

        public static void ToggleDebugTerrainLogging()
        {
            debugTerrainLogging = !debugTerrainLogging;
            Log.Message($"[RimWorld Access] Terrain footstep debug logging: {(debugTerrainLogging ? "ON" : "OFF")}");
        }

        private sealed class PooledAudioSource
        {
            public PooledAudioSource(AudioSource source, AudioLowPassFilter lowPassFilter, AudioHighPassFilter highPassFilter, AudioReverbFilter reverbFilter, ITDProcessor itdProcessor)
            {
                Source = source;
                LowPassFilter = lowPassFilter;
                HighPassFilter = highPassFilter;
                ReverbFilter = reverbFilter;
                ITDProcessor = itdProcessor;
            }

            public AudioSource Source { get; }
            public AudioLowPassFilter LowPassFilter { get; }
            public AudioHighPassFilter HighPassFilter { get; }
            public AudioReverbFilter ReverbFilter { get; }
            public ITDProcessor ITDProcessor { get; }
        }
    }
}
