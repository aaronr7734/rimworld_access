using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    internal enum BleedOutKind
    {
        None,
        Timed,
        NotSoon,
        Deathless,
    }

    /// <summary>
    /// A pawn's downed, bleeding and overall-health state as the game shows it: the health card's
    /// bleeding line (HealthCardUtility.DrawHediffListing) and the map label's health bar
    /// (GenMapUI.DrawPawnLabel).
    /// </summary>
    internal readonly struct PawnHealthSummary
    {
        /// <summary>The map label draws its health bar below this fraction.</summary>
        private const float InjuredBelow = 0.999f;

        /// <summary>At or past one day the health card stops counting down.</summary>
        private const int NotSoonTicks = GenDate.TicksPerDay;

        public readonly bool Downed;
        public readonly float HealthFraction;
        public readonly BleedOutKind Bleeding;
        public readonly int TicksToBleedOut;

        private PawnHealthSummary(bool downed, float healthFraction, BleedOutKind bleeding, int ticksToBleedOut)
        {
            Downed = downed;
            HealthFraction = healthFraction;
            Bleeding = bleeding;
            TicksToBleedOut = ticksToBleedOut;
        }

        public bool IsBleeding => Bleeding != BleedOutKind.None;

        public bool IsInjured => HealthFraction < InjuredBelow;

        public bool IsHealthy => !Downed && !IsBleeding && !IsInjured;

        /// <summary>The whole-number percent the game prints for this fraction.</summary>
        public int HealthPercent => Mathf.RoundToInt(HealthFraction * 100f);

        /// <summary>Changes whenever <see cref="Tag"/>'s wording would, at the precision ToStringPercent and ToStringTicksToPeriod print.</summary>
        public int SpokenHash()
        {
            int healthStep = HealthFraction < 0.1f ? Mathf.RoundToInt(HealthFraction * 10000f) : -HealthPercent;
            int bleedStep = Bleeding != BleedOutKind.Timed ? 0
                : TicksToBleedOut < GenDate.TicksPerHour ? TicksToBleedOut / 60
                : -Mathf.RoundToInt((float)TicksToBleedOut / GenDate.TicksPerHour);
            unchecked
            {
                int hash = Downed ? 1 : 0;
                hash = hash * 31 + (int)Bleeding;
                hash = hash * 31 + healthStep;
                return hash * 31 + bleedStep;
            }
        }

        public static PawnHealthSummary Of(Pawn pawn)
        {
            BleedOutKind bleeding = BleedOut(pawn, out int ticks);
            return new PawnHealthSummary(pawn.Downed, pawn.health.summaryHealth.SummaryHealthPercent, bleeding, ticks);
        }

        /// <summary>
        /// The health card's bleeding verdict: none at or under 0.01 per day (non-bleeding races
        /// already total zero), Deathless with no countdown, else time until death from blood loss.
        /// </summary>
        internal static BleedOutKind BleedOut(Pawn pawn, out int ticks)
        {
            ticks = int.MaxValue;
            if (pawn?.health?.hediffSet == null || pawn.health.hediffSet.BleedRateTotal <= 0.01f)
            {
                return BleedOutKind.None;
            }
            if (ModsConfig.BiotechActive && pawn.genes != null && pawn.genes.HasActiveGene(GeneDefOf.Deathless))
            {
                return BleedOutKind.Deathless;
            }
            ticks = HealthUtility.TicksUntilDeathDueToBloodLoss(pawn);
            return ticks >= NotSoonTicks ? BleedOutKind.NotSoon : BleedOutKind.Timed;
        }

        /// <summary>"downed, bleeding out in 2 hours, 45% health", or null for a healthy pawn.</summary>
        public string Tag()
        {
            if (IsHealthy)
            {
                return null;
            }
            string health = HealthFraction.ToStringPercent();
            switch (Bleeding)
            {
                case BleedOutKind.Timed:
                    string period = TicksToBleedOut.ToStringTicksToPeriod();
                    return Downed
                        ? "RimWorldAccess.PawnHealth.Tag.DownedBleeding".Translate(health, period)
                        : "RimWorldAccess.PawnHealth.Tag.Bleeding".Translate(health, period);
                case BleedOutKind.NotSoon:
                    return Downed
                        ? "RimWorldAccess.PawnHealth.Tag.DownedBleedingNotSoon".Translate(health)
                        : "RimWorldAccess.PawnHealth.Tag.BleedingNotSoon".Translate(health);
                case BleedOutKind.Deathless:
                    return Downed
                        ? "RimWorldAccess.PawnHealth.Tag.DownedBleedingDeathless".Translate(health)
                        : "RimWorldAccess.PawnHealth.Tag.BleedingDeathless".Translate(health);
                default:
                    return Downed
                        ? "RimWorldAccess.PawnHealth.Tag.Downed".Translate(health)
                        : "RimWorldAccess.PawnHealth.Tag.Health".Translate(health);
            }
        }
    }
}
