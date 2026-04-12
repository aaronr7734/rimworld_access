using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public enum FootstepCategory
    {
        Human,
        LargeAnimal,
        SmallAnimal,
        Mechanoid,
        Unknown
    }

    public static class FootstepClassifier
    {
        private const float SmallAnimalThreshold = 0.5f;
        private const float LargeAnimalThreshold = 1.5f;

        public static FootstepCategory ClassifyPawn(Pawn pawn)
        {
            if (pawn == null) return FootstepCategory.Unknown;
            if (pawn.RaceProps?.Humanlike == true)
            {
                return FootstepCategory.Human;
            }

            if (pawn.RaceProps?.Animal == true)
            {
                float bodySize = pawn.BodySize;
                
                if (bodySize <= SmallAnimalThreshold)
                {
                    return FootstepCategory.SmallAnimal;
                }
                else if (bodySize >= LargeAnimalThreshold)
                {
                    return FootstepCategory.LargeAnimal;
                }
                else
                {
                    return FootstepCategory.SmallAnimal;
                }
            }

            if (pawn.RaceProps?.IsMechanoid == true || pawn.def?.defName?.ToLowerInvariant().Contains("mech") == true)
            {
                return FootstepCategory.Mechanoid;
            }

            return FootstepCategory.Unknown;
        }

        public static bool IsValidPawn(Pawn pawn)
        {
            if (pawn == null) return false;
            if (pawn.Destroyed || pawn.Dead || pawn.Discarded) return false;
            if (pawn.Downed) return false;
            if (!pawn.Spawned) return false;
            if (pawn.Map == null) return false;
            if (pawn.Position == IntVec3.Invalid) return false;
            if (pawn.pather == null) return false;

            if (pawn.Faction != null && pawn.Faction.IsPlayer)
            {
                if (pawn.Drafted && !pawn.pather.Moving)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsAnimal(Pawn pawn)
        {
            if (pawn == null) return false;
            return pawn.RaceProps?.Animal == true;
        }

        public static bool IsHumanlike(Pawn pawn)
        {
            if (pawn == null) return false;
            return pawn.RaceProps?.Humanlike == true;
        }

        public static float GetVolumeMultiplier(Pawn pawn)
        {
            FootstepCategory category = ClassifyPawn(pawn);
            
            switch (category)
            {
                case FootstepCategory.Human:
                    return 1.0f;
                case FootstepCategory.LargeAnimal:
                    return 1.2f;
                case FootstepCategory.SmallAnimal:
                    return 0.5f;
                case FootstepCategory.Mechanoid:
                    return 1.3f;
                default:
                    return 0.8f;
            }
        }

        private const float MechPitchBase = 0.9f;
        private const float MechPitchReferenceSize = 4f;
        private const float MechPitchExponent = 0.4f;
        private const float MechHighPassMinCutoff = 50f;
        private const float MechHighPassMaxCutoff = 400f;
        private const float MechHighPassExponent = 1.5f;

        public static float GetPitchMultiplier(Pawn pawn)
        {
            FootstepCategory category = ClassifyPawn(pawn);

            switch (category)
            {
                case FootstepCategory.Human:
                    return 1.0f;
                case FootstepCategory.LargeAnimal:
                    return 0.85f;
                case FootstepCategory.SmallAnimal:
                    return 1.2f;
                case FootstepCategory.Mechanoid:
                    return GetMechPitchMultiplier(pawn);
                default:
                    return 1.0f;
            }
        }

        public static float GetMechHighPassCutoff(Pawn pawn)
        {
            float bodySize = Mathf.Clamp(pawn.BodySize, 0.1f, MechPitchReferenceSize);
            float normalized = 1f - (bodySize / MechPitchReferenceSize);
            return MechHighPassMinCutoff + (MechHighPassMaxCutoff - MechHighPassMinCutoff) * Mathf.Pow(normalized, MechHighPassExponent);
        }

        private static float GetMechPitchMultiplier(Pawn pawn)
        {
            float bodySize = Mathf.Clamp(pawn.BodySize, 0.1f, MechPitchReferenceSize);
            return MechPitchBase * Mathf.Pow(MechPitchReferenceSize / bodySize, MechPitchExponent);
        }
    }
}
