using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Catch-all targeting state for any active <c>Targeter</c> session that doesn't match
    /// our specific handlers (Jump, Ability, Permit, CompTargetable item).
    ///
    /// Background: most targetable verbs in the game (apparel-mounted weapons like turret
    /// packs, mech ranged abilities like Diabolus's Hellsphere Cannon, modded ability verbs)
    /// extend <c>Verb_LaunchProjectile</c> or some other <c>Verb</c> subclass. They expose
    /// the standard <c>ITargetingSource</c> interface (range, params, OrderForceTarget) but
    /// were previously routed through <see cref="ItemTargetingState"/>, which assumes the
    /// source is a <c>CompTargetable</c> item targeting a Thing — and that assumption made
    /// <see cref="TargetingPatch"/> reject any cell-only target ("no valid target at cursor"),
    /// which is exactly what a projectile weapon needs to be allowed to do.
    ///
    /// This state opens for any other ITargetingSource and gives the user a useful
    /// announcement (label, range, what kind of target is expected) plus an R-key range
    /// check, without injecting the wrong validation rules.
    /// </summary>
    public static class GenericTargetingState
    {
        private static bool isActive;
        private static ITargetingSource currentSource;
        private static IntVec3 casterPosition = IntVec3.Invalid;
        private static Map casterMap;
        private static float effectiveRange;
        private static float minRange;
        private static float aoeRadius;
        private static string sourceLabel;

        public static bool IsActive => isActive;
        public static ITargetingSource CurrentSource => currentSource;
        public static float EffectiveRange => effectiveRange;
        public static float MinRange => minRange;
        public static float AoeRadius => aoeRadius;
        public static IntVec3 CasterPosition => casterPosition;

        public static void Open(ITargetingSource source)
        {
            if (source == null) return;
            currentSource = source;
            casterPosition = source.Caster?.Position ?? IntVec3.Invalid;
            casterMap = source.Caster?.Map;
            effectiveRange = ExtractRange(source);
            minRange = ExtractMinRange(source);
            aoeRadius = ExtractAoeRadius(source);
            sourceLabel = ExtractLabel(source);
            isActive = true;

            TolkHelper.SpeakData(BuildStartAnnouncement(), SpeechPriority.Normal);
        }

        public static void Close()
        {
            isActive = false;
            currentSource = null;
            casterPosition = IntVec3.Invalid;
            casterMap = null;
            effectiveRange = 0f;
            minRange = 0f;
            aoeRadius = 0f;
            sourceLabel = null;
        }

        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive) return false;
            if (key == KeyCode.R && !shift && !ctrl && !alt)
            {
                AnnounceRangeInfo();
                return true;
            }
            return false;
        }

        public static void AnnounceRangeInfo()
        {
            if (!isActive)
            {
                TolkHelper.Speak("RimWorldAccess.Abilities.Generic.NoTargetingActive".Loc());
                return;
            }
            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            if (!cursor.IsValid)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.InvalidCursorPosition".Loc());
                return;
            }
            var sb = new StringBuilder();
            if (casterPosition.IsValid)
            {
                float distance = (cursor - casterPosition).LengthHorizontal;
                sb.Append((string)"RimWorldAccess.Abilities.Range.Distance".Translate(distance.ToString("F0")));
                if (distance > effectiveRange && effectiveRange > 0f)
                {
                    sb.Append((string)"RimWorldAccess.Abilities.Range.OutOfRange".Translate(effectiveRange.ToString("F0")));
                }
                else if (distance < minRange && minRange > 0f)
                {
                    sb.Append((string)"RimWorldAccess.Abilities.Generic.TooClose".Translate(minRange.ToString("F0")));
                }
                else if (effectiveRange > 0f)
                {
                    sb.Append((string)"RimWorldAccess.Abilities.Range.InRange".Translate());
                }
                if (casterMap != null && !GenSight.LineOfSight(casterPosition, cursor, casterMap))
                {
                    sb.Append((string)"RimWorldAccess.Abilities.Range.NoLineOfSight".Translate());
                }
                if (aoeRadius > 0f)
                {
                    sb.Append((string)"RimWorldAccess.Abilities.Generic.ExplosionRadius".Translate(aoeRadius.ToString("F0")));
                }
            }
            else if (effectiveRange > 0f)
            {
                sb.Append((string)"RimWorldAccess.Abilities.Generic.RangeOnly".Translate(effectiveRange.ToString("F0")));
                if (aoeRadius > 0f)
                    sb.Append((string)"RimWorldAccess.Abilities.Generic.ExplosionRadius".Translate(aoeRadius.ToString("F0")));
            }
            else
            {
                sb.Append((string)"RimWorldAccess.Abilities.Generic.NoRangeInfo".Translate());
            }
            TolkHelper.SpeakData(sb.ToString());
        }

        /// <summary>
        /// Returns null if the cursor is within the verb's min/max range, or a localized error
        /// message if below min or beyond max. Mirrors the actual hit-check vanilla uses in
        /// <c>Verb.OutOfRange</c>, but reports it to the user before committing the order so we
        /// can stay in targeting mode instead of accepting a job the verb will silently refuse.
        /// </summary>
        public static string ValidateRangeError(IntVec3 cursor)
        {
            if (!isActive || !casterPosition.IsValid || !cursor.IsValid)
                return null;

            float distance = (cursor - casterPosition).LengthHorizontal;

            if (effectiveRange > 0f && distance > effectiveRange)
                return $"Out of range. Distance: {distance:F0}, max range: {effectiveRange:F0}";

            // Use the target-aware min range (matches Verb.OutOfRange). For non-adjacent targets
            // of projectile weapons this snaps to 1.421 even when minRange is 0, so gate on the
            // declared minRange being > 0 to avoid announcing a "too close" error for weapons
            // that have no declared minimum.
            var verb = currentSource?.GetVerb;
            if (verb?.verbProps != null && verb.verbProps.minRange > 0f)
            {
                float effMin = verb.verbProps.EffectiveMinRange(new LocalTargetInfo(cursor), verb.caster);
                if (distance < effMin)
                    return $"Too close. Distance: {distance:F0}, min range: {effMin:F0}";
            }

            return null;
        }

        public static string BuildSuccessAnnouncement(LocalTargetInfo target)
        {
            string targetLabel = target.HasThing ? target.Thing.LabelShort : "location";
            return string.IsNullOrEmpty(sourceLabel)
                ? $"Target selected: {targetLabel}"
                : $"{sourceLabel}: {targetLabel}";
        }

        private static string BuildStartAnnouncement()
        {
            var sb = new StringBuilder();
            sb.Append(string.IsNullOrEmpty(sourceLabel)
                ? (string)"RimWorldAccess.Abilities.Generic.StartFallback".Translate()
                : (string)"RimWorldAccess.Abilities.Generic.StartHeader".Translate(sourceLabel));
            string typeDesc = TargetingParametersDescriber.Describe(currentSource?.targetParams);
            if (!string.IsNullOrEmpty(typeDesc))
                sb.Append((string)"RimWorldAccess.Abilities.Generic.StartTypeDesc".Translate(typeDesc));
            if (effectiveRange > 0f)
            {
                if (minRange > 0f)
                    sb.Append((string)"RimWorldAccess.Abilities.Generic.StartRangeMinMax".Translate(minRange.ToString("F0"), effectiveRange.ToString("F0")));
                else
                    sb.Append((string)"RimWorldAccess.Abilities.Start.Range".Translate(effectiveRange.ToString("F0")));
            }
            if (aoeRadius > 0f)
                sb.Append((string)"RimWorldAccess.Abilities.Generic.StartExplosionRadius".Translate(aoeRadius.ToString("F0")));
            sb.Append((string)"RimWorldAccess.Abilities.Generic.StartInstructionTail".Translate());
            return sb.ToString();
        }

        private static string ExtractLabel(ITargetingSource source)
        {
            // Verb-based: prefer the verb props' label, fall back to the source's caster label.
            if (source is Verb verb)
            {
                string verbLabel = verb.verbProps?.label;
                if (!string.IsNullOrEmpty(verbLabel)) return verbLabel.CapitalizeFirst();
                // Apparel-mounted verbs: caster is the wearer; look at the equipment instead.
                if (verb.EquipmentSource != null) return verb.EquipmentSource.LabelCap;
            }
            // Most Caster things expose a useful label.
            return source.Caster?.LabelShortCap ?? "Targeting";
        }

        private static float ExtractRange(ITargetingSource source)
        {
            try { return source.GetVerb?.EffectiveRange ?? 0f; }
            catch { return 0f; }
        }

        /// <summary>
        /// Returns the minimum-range value sighted players see drawn as a ring around the caster
        /// during targeting. Matches the call in <c>VerbProperties.DrawRadiusRing</c>, which uses
        /// <c>allowAdjacentShot: true</c> — the smaller-of-the-two effective min, i.e. the ring
        /// actually rendered on screen.
        /// </summary>
        private static float ExtractMinRange(ITargetingSource source)
        {
            try
            {
                var props = source.GetVerb?.verbProps;
                if (props == null) return 0f;
                // Gate on declared minRange so projectile weapons without a min don't announce
                // the 1.421-adjacency floor that DrawRadiusRing skips.
                if (props.minRange <= 0f) return 0f;
                return props.EffectiveMinRange(allowAdjacentShot: true);
            }
            catch { return 0f; }
        }

        /// <summary>
        /// Returns the AOE ring radius sighted players see drawn around the cursor during
        /// targeting. Matches <c>Verb.DrawHighlightFieldRadiusAroundTarget</c>, which calls the
        /// same <c>HighlightFieldRadiusAroundTarget</c> method overridden by
        /// <c>Verb_LaunchProjectile</c> (projectile explosionRadius + display padding,
        /// plus forcedMissRadius for burst shots), <c>Verb_MechCluster</c>, etc.
        /// </summary>
        private static float ExtractAoeRadius(ITargetingSource source)
        {
            try
            {
                return source.GetVerb?.HighlightFieldRadiusAroundTarget(out _) ?? 0f;
            }
            catch { return 0f; }
        }
    }
}
