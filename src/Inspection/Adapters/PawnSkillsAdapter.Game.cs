using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for a pawn's "Skills" inspection category. Lists each skill in
    /// vanilla's order as the bar reads (level, passion), expandable into the
    /// lines of vanilla's own hover tooltip.
    /// </summary>
    internal sealed class PawnSkillsAdapter : InspectNodeAdapter
    {
        private static readonly List<Action<InspectionTreeItem, Pawn, SkillRecord>> detailExtenders =
            new List<Action<InspectionTreeItem, Pawn, SkillRecord>>();

        /// <summary>
        /// Registers a mod-compat detail extender that runs for EVERY skill after the
        /// tooltip lines, before <see cref="BuildSkillsChildren"/> folds the labels of the
        /// rows extenders attached into the skill's collapsed label. Public for mod-compat shims (e.g.
        /// VseExpertiseCompat) that nest per-skill sub-nodes without this adapter
        /// knowing about the mod that owns them.
        /// </summary>
        public static void RegisterDetailExtender(Action<InspectionTreeItem, Pawn, SkillRecord> extender)
        {
            if (extender != null)
                detailExtenders.Add(extender);
        }

        public override string CategoryKey => "Skills";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn == null)
                return;
            BuildSkillsChildren(categoryItem, pawn);
        }

        private static void BuildSkillsChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (pawn.skills?.skills == null)
                return;

            // Fixed mnemonic order, matching every vanilla skill listing
            // (SkillUI.Reset: DefDatabase<SkillDef>.AllDefs.OrderByDescending(listOrder)),
            // not the pawn's current levels.
            var skills = pawn.skills.skills.OrderByDescending(s => s.def.listOrder).ToList();

            foreach (var skill in skills)
            {
                string summary = RowSummary(skill);
                var skillItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = summary,
                    ExpandedLabel = summary,
                    Data = skill,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };

                // Built eagerly: extender rows fold into the collapsed label. The tooltip lines
                // stay one expand away, as they are one hover away on the vanilla bar.
                foreach (string line in TooltipLines(skill))
                    InspectNodeFactory.DetailLine(skillItem, line);
                int extenderStart = skillItem.Children.Count;
                RunDetailExtenders(skillItem, pawn, skill);
                var extenderLabels = skillItem.Children.Skip(extenderStart).Select(c => c.Label).ToList();
                if (extenderLabels.Count > 0)
                    skillItem.Label += $". {string.Join(". ", extenderLabels)}";

                InspectNodeFactory.Attach(parentItem, skillItem);
            }
        }

        /// <summary>What the skill bar itself shows: the name, the level or the disabled dash, and the passion icon.</summary>
        private static string RowSummary(SkillRecord skill)
        {
            var parts = new List<string>
            {
                skill.TotallyDisabled
                    ? "DisabledLower".Translate().ToString().ToUpper()
                    : $"{"Level".Translate()} {skill.Level}"
            };
            if (skill.passion != Passion.None)
                parts.Add(skill.passion.GetLabel());
            return $"{skill.def.skillLabel.CapitalizeFirst()}: {string.Join(". ", parts)}";
        }

        /// <summary>
        /// Vanilla's skill tooltip (<c>SkillUI.GetSkillDescription</c>, with whatever other mods patch
        /// into it) as tag-free lines, minus its title line, which repeats the skill's own name.
        /// Falls back to the def description when the private builder cannot be reached.
        /// </summary>
        internal static List<string> TooltipLines(SkillRecord skill)
        {
            string tooltip = null;
            if (PawnColumnWorker_AccessSkill.SkillDescription != null)
            {
                try
                {
                    tooltip = PawnColumnWorker_AccessSkill.SkillDescription(skill);
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("PawnSkillsAdapter skill tooltip", ex);
                }
            }
            if (string.IsNullOrWhiteSpace(tooltip))
                return InspectTextUtility.SplitLines(skill.def.description);

            List<string> lines = InspectTextUtility.SplitLines(tooltip.StripTags());
            if (lines.Count > 0 && lines[0].StartsWith(skill.def.LabelCap.ToString(), StringComparison.Ordinal))
                lines.RemoveAt(0);
            return lines;
        }

        /// <summary>Mod-compat extenders, after the vanilla rows. A broken extender is logged, never fatal.</summary>
        private static void RunDetailExtenders(InspectionTreeItem skillItem, Pawn pawn, SkillRecord skill)
        {
            foreach (var extender in detailExtenders)
            {
                try
                {
                    extender(skillItem, pawn, skill);
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"PawnSkillsAdapter detail extender failed: {ex.Message}");
                }
            }
        }
    }
}
