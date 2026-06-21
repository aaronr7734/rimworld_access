using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Produces a screen-reader-friendly version of the ranged shot calculation
    /// that RimWorld shows sighted players when a drafted shooter is selected and
    /// the mouse hovers a target.
    ///
    /// The hit-chance figure and every contributing factor (shooter ability,
    /// weapon, target size, weather, cover, darkness, etc.) come straight from the
    /// game's own <see cref="ShotReport"/>, so the numbers and their labels always
    /// match vanilla and stay correct under other mods. We only change the
    /// presentation: vanilla leads the tooltip with an awkward "Shot by X:" line,
    /// which we replace with a clear, target-named hit-chance headline, and we
    /// collapse the indented multi-line factor list into one spoken line (per
    /// CLAUDE.md, announcements never use newlines as separators).
    /// </summary>
    public static class ShotReportHelper
    {
        /// <summary>
        /// Returns a flattened, speech-ready shot report for firing the currently
        /// selected drafted colonist (or player turret) at <paramref name="target"/>,
        /// or null when no report applies.
        ///
        /// The gating mirrors the game's own <see cref="TooltipUtility.ShotCalculationTipString"/>:
        /// a single selected pawn with a ranged projectile weapon that is drafted
        /// (or not player-controlled), or a turret, and a target that is not the
        /// shooter itself. We add nothing to that logic.
        /// </summary>
        public static string GetShotReportFor(Thing target)
        {
            if (target == null)
                return null;

            Thing shooter = Find.Selector?.SingleSelectedThing;
            if (shooter == null || shooter == target)
                return null;

            Verb verb = null;
            if (shooter is Pawn pawn)
            {
                if (pawn.equipment?.Primary != null
                    && pawn.equipment.PrimaryEq.PrimaryVerb is Verb_LaunchProjectile
                    && (!pawn.IsPlayerControlled || pawn.Drafted))
                    verb = pawn.equipment.PrimaryEq.PrimaryVerb;
            }
            else if (shooter is Building_TurretGun turret)
            {
                verb = turret.AttackVerb;
            }

            if (verb == null)
                return null;

            if (!verb.CanHitTarget(target))
                return "RimWorldAccess.Combat.ShotReport.CannotHit".Translate(target.LabelShortCap);

            ShotReport report = ShotReport.HitReportFor(verb.caster, verb, target);

            // The game's readout leads with the bare total-hit-chance percent and
            // then an indented list of contributing factors. Flatten it: trim each
            // line and drop the blanks.
            string hitChance = report.TotalEstimatedHitChance.ToStringPercent();
            List<string> lines = report.GetTextReadout()
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();

            string headline;
            if (lines.Count > 0 && lines[0] == hitChance)
            {
                // Normal ranged weapon: the first line is the total chance. Replace
                // it with a clear, target-named headline; keep the factor lines.
                headline = "RimWorldAccess.Combat.ShotReport.Headline".Translate(target.LabelShortCap, hitChance);
                lines.RemoveAt(0);
            }
            else
            {
                // Miss-radius weapons (e.g. grenades) report differently and do not
                // lead with a single hit-chance figure; keep the game's own leading
                // lines and just name the target.
                headline = "RimWorldAccess.Combat.ShotReport.HeadlineGeneric".Translate(target.LabelShortCap);
            }

            return lines.Count > 0 ? headline + ". " + string.Join(", ", lines) : headline;
        }
    }
}
