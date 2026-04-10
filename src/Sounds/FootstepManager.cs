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

        private FootstepManager()
        {
        }

        public void Reset()
        {
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

            FootstepSpatialProfile profile;
            if (!ScreenPanUtility.TryGetSpatialProfile(pawn, out profile)) return;

            bool focused = IsFocusedPawn(pawn);
            TerrainDef terrain = pawn.Map?.terrainGrid?.TerrainAt(pawn.Position);

            float volume = FootstepClassifier.GetVolumeMultiplier(pawn);

            FootstepCategory category = FootstepClassifier.ClassifyPawn(pawn);
            if (FootstepClassifier.IsAnimal(pawn))
            {
                volume *= RimWorldAccessMod_Settings.Settings.FootstepAnimalVolume;
            }
            else if (category == FootstepCategory.Mechanoid)
            {
                volume *= RimWorldAccessMod_Settings.Settings.FootstepMechVolume;
            }
            else
            {
                volume *= RimWorldAccessMod_Settings.Settings.FootstepHumanVolume;
            }

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
