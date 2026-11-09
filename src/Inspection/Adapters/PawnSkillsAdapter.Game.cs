using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for a pawn's "Skills" inspection category. Lists each skill
    /// ordered by level, expandable into level/passion/XP/description detail
    /// rows.
    /// </summary>
    internal sealed class PawnSkillsAdapter : InspectNodeAdapter
    {
        private static readonly List<Action<InspectionTreeItem, Pawn, SkillRecord>> detailExtenders =
            new List<Action<InspectionTreeItem, Pawn, SkillRecord>>();

        /// <summary>
        /// Registers a mod-compat detail extender that runs at the end of
        /// <see cref="BuildSkillDetailChildren"/> for EVERY skill, before the caller
        /// (<see cref="BuildSkillsChildren"/>) folds direct-child labels into the
        /// skill's collapsed label — so anything an extender attaches directly to the
        /// skill item becomes part of that fold. Public for mod-compat shims (e.g.
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
                string skillName = skill.def.skillLabel.CapitalizeFirst();

                var skillItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = skillName,
                    ExpandedLabel = skillName,
                    Data = skill,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };

                // Build children eagerly for collapsed summary
                BuildSkillDetailChildren(skillItem, pawn, skill);
                var skillChildLabels = skillItem.Children.Select(c => c.Label).ToList();
                if (skillChildLabels.Count > 0)
                    skillItem.Label += $": {string.Join(". ", skillChildLabels)}";

                InspectNodeFactory.Attach(parentItem, skillItem);
            }
        }

        /// <summary>
        /// Builds detail children for a skill.
        /// </summary>
        private static void BuildSkillDetailChildren(InspectionTreeItem skillItem, Pawn pawn, SkillRecord skill)
        {
            if (skillItem.Children.Count > 0)
                return; // Already built

            int childIndent = skillItem.IndentLevel + 1;

            // Level
            InspectNodeFactory.Attach(skillItem, new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = $"{"Level".Translate()} {skill.Level}",
                IndentLevel = childIndent,
                IsExpandable = false
            });

            // Passion
            if (skill.passion != Passion.None)
            {
                InspectNodeFactory.Attach(skillItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = skill.passion.GetLabel(),
                    IndentLevel = childIndent,
                    IsExpandable = false
                });
            }

            // Disabled
            if (skill.TotallyDisabled)
            {
                InspectNodeFactory.Attach(skillItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "DisabledLower".Translate().ToString().ToUpper(),
                    IndentLevel = childIndent,
                    IsExpandable = false
                });
            }

            // XP progress
            InspectNodeFactory.Attach(skillItem, new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = "RimWorldAccess.Inspection.Tree.SkillXpProgress".Translate(
                    skill.xpSinceLastLevel.ToString("F0"),
                    skill.XpRequiredForLevelUp.ToString("F0")),
                IndentLevel = childIndent,
                IsExpandable = false
            });

            // Description
            if (!string.IsNullOrEmpty(skill.def.description))
            {
                InspectNodeFactory.Attach(skillItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = skill.def.description,
                    IndentLevel = childIndent,
                    IsExpandable = false
                });
            }

            // Mod-compat extenders (e.g. VSE expertise) run after the vanilla detail
            // rows so their nodes attach directly to the skill item and fold into its
            // collapsed label alongside them. A broken extender is logged, never fatal.
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
