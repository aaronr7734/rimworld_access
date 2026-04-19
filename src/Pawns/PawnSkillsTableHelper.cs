using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Column-driven helpers for PawnSkillsTableState (pawn rows x skill columns).
    /// Column 0 is the pawn name; columns 1..N follow vanilla's SkillUI ordering
    /// (DefDatabase AllDefs ordered by listOrder descending).
    /// </summary>
    public static class PawnSkillsTableHelper
    {
        public const int NameColumnIndex = 0;

        private static List<SkillDef> skillsCache;

        public static void RefreshSkills()
        {
            skillsCache = DefDatabase<SkillDef>.AllDefs
                .OrderByDescending(sd => sd.listOrder)
                .ToList();
        }

        public static List<SkillDef> Skills
        {
            get
            {
                if (skillsCache == null)
                    RefreshSkills();
                return skillsCache;
            }
        }

        public static int TotalColumnCount => 1 + Skills.Count;

        public static SkillDef SkillForColumn(int columnIndex)
        {
            if (columnIndex <= NameColumnIndex) return null;
            int idx = columnIndex - 1;
            if (idx < 0 || idx >= Skills.Count) return null;
            return Skills[idx];
        }

        public static string GetColumnName(int columnIndex)
        {
            if (columnIndex == NameColumnIndex) return "Name";
            SkillDef def = SkillForColumn(columnIndex);
            if (def == null) return "Unknown";
            // skillLabel is the translatable form; LabelCap falls back to defName.
            string label = !string.IsNullOrEmpty(def.skillLabel) ? def.skillLabel : def.label;
            return string.IsNullOrEmpty(label) ? def.defName : label.CapitalizeFirst();
        }

        public static string GetPawnLabel(Pawn pawn) => pawn?.LabelShort ?? "";

        /// <summary>
        /// Cell value: pawn name for column 0, otherwise a terse skill readout.
        /// Skill format: "{passion, }{level}, {LevelDescriptor}" or "incapable" for disabled.
        /// </summary>
        public static string GetColumnValue(Pawn pawn, int columnIndex)
        {
            if (columnIndex == NameColumnIndex)
                return pawn?.LabelShort ?? "";

            SkillDef def = SkillForColumn(columnIndex);
            if (def == null || pawn?.skills == null) return "";

            SkillRecord record = pawn.skills.GetSkill(def);
            if (record == null) return "";

            if (record.TotallyDisabled)
                return "incapable";

            int level = record.GetLevelForUI();
            string descriptor = record.LevelDescriptor;
            string passion = PassionLabel(record.passion);

            if (string.IsNullOrEmpty(passion))
                return $"{level}, {descriptor}";
            return $"{passion}, {level}, {descriptor}";
        }

        /// <summary>
        /// Column tooltip (announced once on column change): the skill's def description.
        /// </summary>
        public static string GetColumnTooltip(Pawn pawn, int columnIndex)
        {
            if (columnIndex == NameColumnIndex) return null;
            SkillDef def = SkillForColumn(columnIndex);
            if (def == null) return null;
            return string.IsNullOrEmpty(def.description) ? null : def.description;
        }

        public static bool IsColumnSortable(int columnIndex) => true;

        /// <summary>
        /// Sort order: Name column is alphabetical; skill columns sort by level
        /// with disabled pawns sorted to the bottom (compare value -1). Passion is
        /// used as a tiebreaker (Major > Minor > None).
        /// </summary>
        public static List<Pawn> SortPawnsByColumn(IList<Pawn> pawns, int columnIndex, bool descending)
        {
            if (columnIndex == NameColumnIndex)
            {
                return descending
                    ? pawns.OrderByDescending(p => p.LabelShort).ToList()
                    : pawns.OrderBy(p => p.LabelShort).ToList();
            }

            SkillDef def = SkillForColumn(columnIndex);
            if (def == null) return pawns.ToList();

            if (descending)
            {
                return pawns
                    .OrderByDescending(p => SkillSortValue(p, def))
                    .ThenByDescending(p => PassionSortValue(p, def))
                    .ThenBy(p => p.LabelShort)
                    .ToList();
            }
            return pawns
                .OrderBy(p => SkillSortValue(p, def))
                .ThenBy(p => PassionSortValue(p, def))
                .ThenBy(p => p.LabelShort)
                .ToList();
        }

        private static int SkillSortValue(Pawn pawn, SkillDef def)
        {
            SkillRecord record = pawn?.skills?.GetSkill(def);
            if (record == null || record.TotallyDisabled) return -1;
            return record.GetLevelForUI();
        }

        private static int PassionSortValue(Pawn pawn, SkillDef def)
        {
            SkillRecord record = pawn?.skills?.GetSkill(def);
            if (record == null || record.TotallyDisabled) return -1;
            return (int)record.passion;
        }

        /// <summary>
        /// Localized passion label. Keys match vanilla (English: "Passion" / "Burning passion").
        /// Empty string when no passion.
        /// </summary>
        public static string PassionLabel(Passion passion)
        {
            switch (passion)
            {
                case Passion.Minor: return "PassionMinor".Translate();
                case Passion.Major: return "PassionMajor".Translate();
                default: return "";
            }
        }
    }
}
