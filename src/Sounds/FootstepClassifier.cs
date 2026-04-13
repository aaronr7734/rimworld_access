using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Coarse classification used for sound-bank selection and pitch/volume multipliers.
    /// </summary>
    public enum FootstepCategory
    {
        Human,
        LargeAnimal,
        SmallAnimal,
        Mechanoid,
        Unknown
    }

    /// <summary>
    /// Fine-grained audio category used for user-facing volume/toggle controls.
    /// Each value represents an independently configurable bucket of pawns.
    /// </summary>
    public enum FootstepAudioCategory
    {
        DraftedColonists,
        UndraftedColonists,
        UndraftedMechs,
        FriendlyHumans,
        NeutralHumans,
        Hostiles,
        TamedAnimals,
        WildAnimals,
    }

    public static class FootstepClassifier
    {
        private const float SmallAnimalThreshold = 0.5f;
        private const float LargeAnimalThreshold = 1.5f;

        public static readonly FootstepAudioCategory[] AllAudioCategories =
            (FootstepAudioCategory[])System.Enum.GetValues(typeof(FootstepAudioCategory));

        // Cached per-race "is anything in this race enabled" booleans so the patch
        // hot path can early-out without classifying. Invalidated whenever any
        // FootstepCategoryEnabled value changes via SetEnabled/RecomputeRaceCache.
        private static bool anyHumanlikeEnabled = true;
        private static bool anyAnimalEnabled = true;
        private static bool anyMechEnabled = true;

        public static FootstepCategory ClassifyPawn(Pawn pawn)
        {
            if (pawn == null) return FootstepCategory.Unknown;
            if (pawn.RaceProps?.Humanlike == true) return FootstepCategory.Human;

            if (pawn.RaceProps?.Animal == true)
            {
                float bodySize = pawn.BodySize;
                if (bodySize <= SmallAnimalThreshold) return FootstepCategory.SmallAnimal;
                if (bodySize >= LargeAnimalThreshold) return FootstepCategory.LargeAnimal;
                return FootstepCategory.SmallAnimal;
            }

            if (pawn.RaceProps?.IsMechanoid == true || pawn.def?.defName?.ToLowerInvariant().Contains("mech") == true)
            {
                return FootstepCategory.Mechanoid;
            }

            return FootstepCategory.Unknown;
        }

        /// <summary>
        /// Fine-grained classification used to look up per-category user settings.
        /// </summary>
        public static FootstepAudioCategory ClassifyPawnAudio(Pawn pawn)
        {
            if (pawn == null) return FootstepAudioCategory.WildAnimals;

            bool humanlike = pawn.RaceProps?.Humanlike == true;
            bool animal = pawn.RaceProps?.Animal == true;
            bool mech = !humanlike && !animal && (
                pawn.RaceProps?.IsMechanoid == true ||
                pawn.def?.defName?.ToLowerInvariant().Contains("mech") == true);

            Faction playerFaction = Faction.OfPlayerSilentFail;
            Faction pawnFaction = pawn.Faction;
            bool isPlayerFaction = pawnFaction != null && pawnFaction == playerFaction;

            // Drafted player pawns — humanlike or mech — share a single "combat-ready"
            // category so a player can silence non-combat ambience yet still hear
            // anyone they've personally drafted into action.
            if (isPlayerFaction && pawn.Drafted && (humanlike || mech))
            {
                return FootstepAudioCategory.DraftedColonists;
            }

            if (humanlike)
            {
                if (isPlayerFaction)
                    return FootstepAudioCategory.UndraftedColonists;

                if (playerFaction != null && pawn.HostileTo(playerFaction))
                    return FootstepAudioCategory.Hostiles;

                if (pawnFaction != null && playerFaction != null &&
                    pawnFaction.RelationKindWith(playerFaction) == FactionRelationKind.Ally)
                {
                    return FootstepAudioCategory.FriendlyHumans;
                }

                return FootstepAudioCategory.NeutralHumans;
            }

            if (animal)
            {
                if (isPlayerFaction)
                    return FootstepAudioCategory.TamedAnimals;

                bool manhunter = pawn.MentalStateDef == MentalStateDefOf.Manhunter ||
                                 pawn.MentalStateDef == MentalStateDefOf.ManhunterPermanent;
                if (manhunter) return FootstepAudioCategory.Hostiles;

                if (playerFaction != null && pawn.HostileTo(playerFaction))
                    return FootstepAudioCategory.Hostiles;

                return FootstepAudioCategory.WildAnimals;
            }

            if (mech)
            {
                if (playerFaction != null && pawn.HostileTo(playerFaction))
                    return FootstepAudioCategory.Hostiles;

                // Player's own undrafted mechs plus allied-faction mechs both sit in
                // UndraftedMechs — they're noisy in a way many players want to mute
                // even when they're keeping other friendly footsteps on.
                return FootstepAudioCategory.UndraftedMechs;
            }

            return FootstepAudioCategory.WildAnimals;
        }

        public static string GetCategoryDisplayName(FootstepAudioCategory cat)
        {
            switch (cat)
            {
                case FootstepAudioCategory.DraftedColonists: return "Drafted Colonists";
                case FootstepAudioCategory.UndraftedColonists: return "Undrafted Colonists";
                case FootstepAudioCategory.UndraftedMechs: return "Undrafted Mechs";
                case FootstepAudioCategory.FriendlyHumans: return "Friendly Humans";
                case FootstepAudioCategory.NeutralHumans: return "Neutral Humans";
                case FootstepAudioCategory.Hostiles: return "Hostiles";
                case FootstepAudioCategory.TamedAnimals: return "Tamed Animals";
                case FootstepAudioCategory.WildAnimals: return "Wild Animals";
                default: return cat.ToString();
            }
        }

        public static string GetCategoryDescription(FootstepAudioCategory cat)
        {
            switch (cat)
            {
                case FootstepAudioCategory.DraftedColonists: return "Drafted colonists and drafted colony mechs.";
                case FootstepAudioCategory.UndraftedColonists: return "Undrafted colonists (humanlike only).";
                case FootstepAudioCategory.UndraftedMechs: return "Undrafted colony and allied mechs. Mechs can be noisy; separate slider lets you mute them while keeping colonists audible.";
                case FootstepAudioCategory.FriendlyHumans: return "Visitors from allied factions.";
                case FootstepAudioCategory.NeutralHumans: return "Travelers and traders from neutral factions.";
                case FootstepAudioCategory.Hostiles: return "All attackers — raiders, enemy mechs, manhunters, and hostile wildlife.";
                case FootstepAudioCategory.TamedAnimals: return "Tamed colony animals.";
                case FootstepAudioCategory.WildAnimals: return "Wild, untamed animals.";
                default: return string.Empty;
            }
        }

        public static float GetDefaultVolume(FootstepAudioCategory cat) => 0.5f;
        public static bool GetDefaultEnabled(FootstepAudioCategory cat) => true;

        public static bool IsEnabled(FootstepAudioCategory cat)
        {
            RimWorldAccessSettings s = RimWorldAccessMod_Settings.Settings;
            if (s == null) return GetDefaultEnabled(cat);
            if (s.FootstepCategoryEnabled == null || !s.FootstepCategoryEnabled.TryGetValue(cat.ToString(), out bool v))
                return GetDefaultEnabled(cat);
            return v;
        }

        public static float GetVolume(FootstepAudioCategory cat)
        {
            RimWorldAccessSettings s = RimWorldAccessMod_Settings.Settings;
            if (s == null) return GetDefaultVolume(cat);
            if (s.FootstepCategoryVolume == null || !s.FootstepCategoryVolume.TryGetValue(cat.ToString(), out float v))
                return GetDefaultVolume(cat);
            return v;
        }

        public static float GetLastVolume(FootstepAudioCategory cat)
        {
            RimWorldAccessSettings s = RimWorldAccessMod_Settings.Settings;
            if (s == null) return GetDefaultVolume(cat);
            if (s.FootstepCategoryLastVolume == null || !s.FootstepCategoryLastVolume.TryGetValue(cat.ToString(), out float v))
                return GetDefaultVolume(cat);
            return v;
        }

        public static void SetEnabled(FootstepAudioCategory cat, bool enabled)
        {
            RimWorldAccessSettings s = RimWorldAccessMod_Settings.Settings;
            if (s == null) return;
            if (s.FootstepCategoryEnabled == null) s.FootstepCategoryEnabled = new Dictionary<string, bool>();
            s.FootstepCategoryEnabled[cat.ToString()] = enabled;
            RecomputeRaceCache();
        }

        public static void SetVolume(FootstepAudioCategory cat, float volume)
        {
            RimWorldAccessSettings s = RimWorldAccessMod_Settings.Settings;
            if (s == null) return;
            if (s.FootstepCategoryVolume == null) s.FootstepCategoryVolume = new Dictionary<string, float>();
            s.FootstepCategoryVolume[cat.ToString()] = volume;
            if (volume > 0.0001f)
            {
                if (s.FootstepCategoryLastVolume == null) s.FootstepCategoryLastVolume = new Dictionary<string, float>();
                s.FootstepCategoryLastVolume[cat.ToString()] = volume;
            }
        }

        public static bool AnyHumanlikeEnabled => anyHumanlikeEnabled;
        public static bool AnyAnimalEnabled => anyAnimalEnabled;
        public static bool AnyMechEnabled => anyMechEnabled;

        public static void RecomputeRaceCache()
        {
            // Hostiles is cross-race (raiders, enemy mechs, manhunters) — so if it's
            // enabled, all three race short-circuits must stay "live" or we'd silence
            // a whole race just because its non-hostile categories are off.
            bool hostiles = IsEnabled(FootstepAudioCategory.Hostiles);
            bool drafted = IsEnabled(FootstepAudioCategory.DraftedColonists);

            anyHumanlikeEnabled =
                drafted ||
                IsEnabled(FootstepAudioCategory.UndraftedColonists) ||
                IsEnabled(FootstepAudioCategory.FriendlyHumans) ||
                IsEnabled(FootstepAudioCategory.NeutralHumans) ||
                hostiles;
            anyAnimalEnabled =
                IsEnabled(FootstepAudioCategory.TamedAnimals) ||
                IsEnabled(FootstepAudioCategory.WildAnimals) ||
                hostiles;
            anyMechEnabled =
                drafted ||
                IsEnabled(FootstepAudioCategory.UndraftedMechs) ||
                hostiles;
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
                case FootstepCategory.Human: return 1.0f;
                case FootstepCategory.LargeAnimal: return 1.2f;
                case FootstepCategory.SmallAnimal: return 0.5f;
                case FootstepCategory.Mechanoid: return 1.3f;
                default: return 0.8f;
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
                case FootstepCategory.Human: return 1.0f;
                case FootstepCategory.LargeAnimal: return 0.85f;
                case FootstepCategory.SmallAnimal: return 1.2f;
                case FootstepCategory.Mechanoid: return GetMechPitchMultiplier(pawn);
                default: return 1.0f;
            }
        }

        public static float GetMechHighPassCutoff(Pawn pawn) => GetMechHighPassCutoffForSize(pawn.BodySize);

        public static float GetMechHighPassCutoffForSize(float bodySize)
        {
            bodySize = Mathf.Clamp(bodySize, 0.1f, MechPitchReferenceSize);
            float normalized = 1f - (bodySize / MechPitchReferenceSize);
            return MechHighPassMinCutoff + (MechHighPassMaxCutoff - MechHighPassMinCutoff) * Mathf.Pow(normalized, MechHighPassExponent);
        }

        private static float GetMechPitchMultiplier(Pawn pawn) => GetMechPitchMultiplierForSize(pawn.BodySize);

        public static float GetMechPitchMultiplierForSize(float bodySize)
        {
            bodySize = Mathf.Clamp(bodySize, 0.1f, MechPitchReferenceSize);
            return MechPitchBase * Mathf.Pow(MechPitchReferenceSize / bodySize, MechPitchExponent);
        }
    }
}
