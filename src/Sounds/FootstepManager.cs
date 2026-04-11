using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Processes footstep sounds for individual pawns. Called by FootstepPatch
    /// when a pawn enters a new tile (event-driven, not polling).
    /// </summary>
    public sealed class FootstepManager
    {
        private static readonly FootstepManager instance = new FootstepManager();
        public static FootstepManager Instance => instance;

        private const int PerformanceModeCap = 18;
        private int _perfModeLastTick = -1;
        private int _perfModeTickCount;

        private FootstepManager()
        {
        }

        public void Reset()
        {
            _perfModeLastTick = -1;
            _perfModeTickCount = 0;
        }

        /// <summary>
        /// Process a single footstep for a pawn that just entered a new tile.
        /// Called from FootstepPatch.Postfix — one call per tile entry, O(1).
        /// </summary>
        public void ProcessFootstep(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Destroyed) return;
            if (!FootstepClassifier.IsValidPawn(pawn)) return;
            if (!FootstepSoundBank.EnsureInitialized()) return;

            FootstepCategory category = FootstepClassifier.ClassifyPawn(pawn);
            float categoryVolume = FootstepClassifier.IsAnimal(pawn)
                ? RimWorldAccessMod_Settings.Settings.FootstepAnimalVolume
                : category == FootstepCategory.Mechanoid
                    ? RimWorldAccessMod_Settings.Settings.FootstepMechVolume
                    : RimWorldAccessMod_Settings.Settings.FootstepHumanVolume;
            if (categoryVolume <= 0f) return;

            if (RimWorldAccessMod_Settings.Settings.FootstepPerformanceMode)
            {
                if (ShouldThrottleFootstep(pawn)) return;
            }

            FootstepSpatialProfile profile;
            if (!ScreenPanUtility.TryGetSpatialProfile(pawn, out profile)) return;

            bool focused = IsFocusedPawn(pawn);
            TerrainDef terrain = pawn.Map?.terrainGrid?.TerrainAt(pawn.Position);

            float volume = FootstepClassifier.GetVolumeMultiplier(pawn);
            volume *= categoryVolume;

            volume *= profile.Audibility;
            volume *= GetMixVolumeScale(profile, focused, pawn);

            if (RimWorldAccessMod_Settings.Settings.FootstepZoomScaling)
            {
                volume *= CameraZoomUtility.GetZoomVolumeScale();
            }

            volume = Mathf.Clamp(volume, 0f, 1.35f);
            if (volume <= 0.025f) return;

            FootstepSoundBank.PlayFootstep(pawn, terrain, volume, profile);
        }

        private static float GetMixVolumeScale(FootstepSpatialProfile profile, bool focusedPawn, Pawn pawn)
        {
            float scale = profile.Presence;
            FootstepCategory category = FootstepClassifier.ClassifyPawn(pawn);

            if (!focusedPawn)
            {
                scale *= 0.8f;
            }

            switch (category)
            {
                case FootstepCategory.LargeAnimal:
                    scale *= 0.72f;
                    break;
                case FootstepCategory.SmallAnimal:
                    scale *= 0.42f;
                    break;
            }

            return Mathf.Clamp(scale, 0.25f, 1.15f);
        }

        private bool ShouldThrottleFootstep(Pawn pawn)
        {
            int currentTick = Find.TickManager?.TicksGame ?? -1;
            if (currentTick != _perfModeLastTick)
            {
                _perfModeLastTick = currentTick;
                _perfModeTickCount = 0;
            }

            int priority = GetFootstepPriority(pawn);
            if (priority <= 1) return false;

            if (_perfModeTickCount >= PerformanceModeCap) return true;

            _perfModeTickCount++;
            return false;
        }

        private static int GetFootstepPriority(Pawn pawn)
        {
            if (IsSelected(pawn)) return 0;
            if (pawn.Drafted) return 1;

            bool hostile = Faction.OfPlayer != null && pawn.HostileTo(Faction.OfPlayer);
            if (hostile && FootstepClassifier.IsHumanlike(pawn)) return 2;
            if (hostile) return 3;

            if (pawn.Faction?.IsPlayer == true && FootstepClassifier.IsHumanlike(pawn)) return 4;
            return 5;
        }

        private static bool IsFocusedPawn(Pawn pawn)
        {
            if (pawn == null) return false;
            if (IsSelected(pawn)) return true;
            if (pawn.Drafted) return true;
            if (pawn.Faction?.IsPlayer == true && FootstepClassifier.IsHumanlike(pawn)) return true;
            return Faction.OfPlayer != null && pawn.HostileTo(Faction.OfPlayer);
        }

        private static bool IsSelected(Pawn pawn)
        {
            return pawn != null && Find.Selector != null && Find.Selector.IsSelected(pawn);
        }
    }
}
