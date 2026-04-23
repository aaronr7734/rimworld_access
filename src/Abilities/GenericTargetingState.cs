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
        private static string sourceLabel;

        public static bool IsActive => isActive;
        public static ITargetingSource CurrentSource => currentSource;
        public static float EffectiveRange => effectiveRange;
        public static IntVec3 CasterPosition => casterPosition;

        public static void Open(ITargetingSource source)
        {
            if (source == null) return;
            currentSource = source;
            casterPosition = source.Caster?.Position ?? IntVec3.Invalid;
            casterMap = source.Caster?.Map;
            effectiveRange = ExtractRange(source);
            sourceLabel = ExtractLabel(source);
            isActive = true;

            TolkHelper.Speak(BuildStartAnnouncement(), SpeechPriority.Normal);
        }

        public static void Close()
        {
            isActive = false;
            currentSource = null;
            casterPosition = IntVec3.Invalid;
            casterMap = null;
            effectiveRange = 0f;
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
                TolkHelper.Speak("No targeting active");
                return;
            }
            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            if (!cursor.IsValid)
            {
                TolkHelper.Speak("Invalid cursor position");
                return;
            }
            var sb = new StringBuilder();
            if (casterPosition.IsValid)
            {
                float distance = (cursor - casterPosition).LengthHorizontal;
                sb.Append($"Distance: {distance:F0} tiles");
                if (effectiveRange > 0f)
                {
                    sb.Append(distance <= effectiveRange ? ", IN RANGE" : $", OUT OF RANGE (max {effectiveRange:F0})");
                }
                if (casterMap != null && !GenSight.LineOfSight(casterPosition, cursor, casterMap))
                {
                    sb.Append(", NO LINE OF SIGHT");
                }
            }
            else if (effectiveRange > 0f)
            {
                sb.Append($"Range: {effectiveRange:F0} tiles");
            }
            else
            {
                sb.Append("No range information");
            }
            TolkHelper.Speak(sb.ToString());
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
            sb.Append(string.IsNullOrEmpty(sourceLabel) ? "Targeting" : $"{sourceLabel} targeting");
            string typeDesc = TargetingParametersDescriber.Describe(currentSource?.targetParams);
            if (!string.IsNullOrEmpty(typeDesc))
                sb.Append($". {typeDesc}");
            if (effectiveRange > 0f)
                sb.Append($". Range: {effectiveRange:F0} tiles");
            sb.Append(". Press Enter at cursor to confirm. R for distance and line of sight. Escape to cancel.");
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
    }
}
