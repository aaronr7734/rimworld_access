using System.Collections.Generic;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// A LearnPsycasts menu row shows only the psycast's name, icon and info-card button. The row
    /// speaks what that card holds, the def's own stat lines then its description, and Alt+I opens
    /// the card itself.
    /// </summary>
    internal sealed class CopPsycastOptionDetails : IFloatMenuOptionDetailSource
    {
        public string ExtrasFor(FloatMenuOption option)
        {
            if (!CopReflection.TryReadOption(option, out AbilityDef ability, out object gizmo))
            {
                return null;
            }
            Pawn pawn = CopReflection.PawnOf(gizmo);
            var parts = new List<string>();

            // A multi-level menu mixes psycasts of several levels; a single-level one would repeat its own.
            CopReflection.LevelsOf(gizmo, out int low, out int high);
            if (low != high)
            {
                parts.Add(CopLearnPsycastsGizmoHandler.DescribeLevels(ability.level, ability.level));
            }
            foreach (string line in ability.StatSummary(pawn))
            {
                parts.Add(CompatText.Flatten(line));
            }
            parts.Add(RangeLine(ability));
            parts.Add(CompatText.Flatten(ability.description));
            return CompatText.JoinSentences(parts);
        }

        public Def InfoDefFor(FloatMenuOption option)
        {
            return CopReflection.TryReadOption(option, out AbilityDef ability, out object _) ? ability : null;
        }

        /// <summary>
        /// GizmoNavigationState.GetAbilityRangeInfo's ladder read from the def: StatSummary omits
        /// range, and an unlearned psycast has no Ability or verb to ask.
        /// </summary>
        private static string RangeLine(AbilityDef ability)
        {
            if (!ability.targetRequired)
            {
                return "RimWorldAccess.Inspection.Gizmo.Ability.RangeSelf".Translate();
            }
            if (ability.targetWorldCell)
            {
                return "RimWorldAccess.Inspection.Gizmo.Ability.RangeWorldMap".Translate();
            }
            float range = ability.verbProperties?.range ?? 0f;
            bool touch = range <= 0f || typeof(Verb_CastAbilityTouch).IsAssignableFrom(ability.verbProperties.verbClass);
            return touch
                ? "RimWorldAccess.Inspection.Gizmo.Ability.RangeTouch".Translate()
                : "RimWorldAccess.Inspection.Gizmo.Ability.RangeTiles".Translate(range.ToString("F0"));
        }
    }
}
