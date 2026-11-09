using System;
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
            if (columnIndex == NameColumnIndex) return "RimWorldAccess.Common.NameColumn".Translate();
            SkillDef def = SkillForColumn(columnIndex);
            if (def == null) return "RimWorldAccess.Common.Unknown".Translate();
            // skillLabel is the translatable form; LabelCap falls back to defName.
            string label = !string.IsNullOrEmpty(def.skillLabel) ? def.skillLabel : def.label;
            return string.IsNullOrEmpty(label) ? def.defName : label.CapitalizeFirst();
        }

        public static string GetPawnLabel(Pawn pawn) => pawn?.LabelShort ?? "";

        /// <summary>
        /// Cell value: pawn name for column 0, otherwise a terse skill readout.
        /// Skill format: "{level}, {LevelDescriptor}[, {passion}] ({xp progress})" or
        /// "incapable" for disabled. The XP progress is recomputed from the live
        /// SkillRecord on every navigation, so it reflects current experience.
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
                return "RimWorldAccess.Pawns.SkillsTable.Incapable".Translate();

            int level = record.GetLevelForUI();
            string descriptor = record.LevelDescriptor;
            string passion = PassionLabel(record.passion);
            string xpInfo = BuildXpProgress(record);

            if (string.IsNullOrEmpty(passion))
                return $"{level}, {descriptor}{xpInfo}";
            return $"{level}, {descriptor}, {passion}{xpInfo}";
        }

        /// <summary>
        /// Terse localized XP progress suffix, e.g. "(Experience: 1,200 / 24,000)":
        /// current experience toward the next level over the amount required.
        /// Uses the game's own one-word "Experience" string (the label vanilla shows
        /// for maxed skills) to stay translatable without the longer
        /// "Progress to next learned level" phrasing.
        /// </summary>
        private static string BuildXpProgress(SkillRecord record)
        {
            string label = "Experience".Translate();
            string current = record.xpSinceLastLevel.ToString("N0");
            string required = record.XpRequiredForLevelUp.ToString("N0");
            return $" ({label}: {current} / {required})";
        }

        /// <summary>
        /// Column tooltip (the skill's def description) — a column-level
        /// constant, not per-pawn, so it takes only the column index (the
        /// ScreenScope table contract's <c>ContentColumnInfo</c> shape; the
        /// composer speaks it once, on column change).
        /// </summary>
        public static string GetColumnTooltip(int columnIndex)
        {
            if (columnIndex == NameColumnIndex) return null;
            SkillDef def = SkillForColumn(columnIndex);
            if (def == null) return null;
            return string.IsNullOrEmpty(def.description) ? null : def.description;
        }

        public static bool IsColumnSortable(int columnIndex) => true;

        /// <summary>
        /// The one comparison behind both callers: the keyboard sort cycle here and the
        /// vanilla <c>PawnColumnWorker</c> the skills window's table sorts with. Name column
        /// is alphabetical; skill columns sort by level with disabled pawns at the bottom
        /// (compare value -1) and passion as the tiebreaker (Major > Minor > None).
        ///
        /// It returns the DESCENDING order (highest first), which is the polarity PawnTable
        /// wants: it feeds Compare straight to its stable sort when its descending flag is
        /// set (decompiled RimWorld/PawnTable.cs:276-285), so this is what makes vanilla's
        /// first header click and our first sort press produce the same rows. Remaining ties
        /// are left alone — both callers sort stably over the colonist-bar order.
        /// </summary>
        public static int CompareByColumn(Pawn a, Pawn b, int columnIndex)
        {
            if (columnIndex == NameColumnIndex)
                return string.Compare(GetPawnLabel(b), GetPawnLabel(a), StringComparison.CurrentCulture);

            SkillDef def = SkillForColumn(columnIndex);
            if (def == null) return 0;

            int byLevel = SkillSortValue(b, def).CompareTo(SkillSortValue(a, def));
            return byLevel != 0 ? byLevel : PassionSortValue(b, def).CompareTo(PassionSortValue(a, def));
        }

        /// <summary>
        /// Row order for an open with no live table to sort, built the way
        /// <c>PawnTable.RecachePawns</c> builds its own: a stable sort of the captured order
        /// by <see cref="CompareByColumn"/>, reversed for the ascending half of the cycle.
        /// </summary>
        public static List<Pawn> SortPawnsByColumn(IReadOnlyList<Pawn> pawns, int columnIndex, bool descending)
        {
            List<Pawn> ordered = new List<Pawn>(pawns);
            if (descending)
                ordered.SortStable((a, b) => CompareByColumn(a, b, columnIndex));
            else
                ordered.SortStable((a, b) => CompareByColumn(b, a, columnIndex));
            return ordered;
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
                case Passion.None: return "";
                default: return passion.GetLabel();
            }
        }
    }
}
