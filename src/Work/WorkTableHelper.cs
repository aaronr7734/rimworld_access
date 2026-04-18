using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Column-driven helpers for WorkTableState (pawn rows x work-type columns).
    /// Column 0 is the pawn name; columns 1..N follow vanilla's
    /// WorkTypeDefsInPriorityOrder for the visible work types.
    /// </summary>
    public static class WorkTableHelper
    {
        public const int NameColumnIndex = 0;

        private static List<WorkTypeDef> workTypesCache;

        public static void RefreshWorkTypes()
        {
            workTypesCache = WorkTypeDefsUtility.WorkTypeDefsInPriorityOrder
                .Where(w => w.visible)
                .ToList();
        }

        public static List<WorkTypeDef> WorkTypes
        {
            get
            {
                if (workTypesCache == null)
                    RefreshWorkTypes();
                return workTypesCache;
            }
        }

        public static int TotalColumnCount => 1 + WorkTypes.Count;

        public static WorkTypeDef WorkTypeForColumn(int columnIndex)
        {
            if (columnIndex <= NameColumnIndex) return null;
            int workIndex = columnIndex - 1;
            if (workIndex < 0 || workIndex >= WorkTypes.Count) return null;
            return WorkTypes[workIndex];
        }

        public static string GetColumnName(int columnIndex)
        {
            if (columnIndex == NameColumnIndex) return "Name";
            WorkTypeDef workType = WorkTypeForColumn(columnIndex);
            return workType != null ? workType.labelShort.CapitalizeFirst() : "Unknown";
        }

        public static string GetPawnLabel(Pawn pawn) => pawn?.LabelShort ?? "";

        /// <summary>
        /// Builds the terse cell value: "{state}, {skills}: {level}{passion}" or
        /// "incapable" for permanently disabled cells. State is the priority digit
        /// (manual mode) or "on"/"off" (basic mode).
        /// </summary>
        public static string GetColumnValue(Pawn pawn, int columnIndex)
        {
            if (columnIndex == NameColumnIndex)
                return pawn.LabelShort;

            WorkTypeDef workType = WorkTypeForColumn(columnIndex);
            if (workType == null) return "";

            if (pawn.workSettings == null || !pawn.workSettings.EverWork)
                return "incapable";

            if (pawn.WorkTypeIsDisabled(workType))
                return "incapable";

            int priority = pawn.workSettings.GetPriority(workType);
            string state = FormatState(priority);
            string skillInfo = FormatSkillInfo(pawn, workType);

            return string.IsNullOrEmpty(skillInfo)
                ? state
                : $"{state}, {skillInfo}";
        }

        /// <summary>
        /// Builds the column tooltip: gerundLabel, description, work giver list
        /// (with emergency markers), then ideology warning if applicable.
        /// Per design philosophy, this is announced only on column-change navigation.
        /// </summary>
        public static string GetColumnTooltip(Pawn pawn, int columnIndex)
        {
            if (columnIndex == NameColumnIndex) return null;

            WorkTypeDef workType = WorkTypeForColumn(columnIndex);
            if (workType == null) return null;

            var sb = new StringBuilder();
            sb.Append(workType.gerundLabel.CapitalizeFirst());
            if (!string.IsNullOrEmpty(workType.description))
            {
                sb.Append(". ");
                sb.Append(workType.description);
            }
            string workList = BuildSpecificWorkList(workType);
            if (!string.IsNullOrEmpty(workList))
            {
                sb.Append(". Includes: ");
                sb.Append(workList);
            }
            if (pawn?.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
            {
                sb.Append(". Ideology opposes this work");
            }
            return sb.ToString();
        }

        public static bool IsColumnSortable(int columnIndex) => true;

        /// <summary>
        /// Sorts pawns by the given column. Name column sorts alphabetically.
        /// Work columns mirror PawnColumnWorker_WorkPriority.Compare which orders
        /// by AverageOfRelevantSkillsFor with disabled = -1 and no-work = -2.
        /// </summary>
        public static List<Pawn> SortPawnsByColumn(IList<Pawn> pawns, int columnIndex, bool descending)
        {
            if (columnIndex == NameColumnIndex)
            {
                return descending
                    ? pawns.OrderByDescending(p => p.LabelShort).ToList()
                    : pawns.OrderBy(p => p.LabelShort).ToList();
            }

            WorkTypeDef workType = WorkTypeForColumn(columnIndex);
            if (workType == null) return pawns.ToList();

            return descending
                ? pawns.OrderByDescending(p => CompareValueForWork(p, workType)).ToList()
                : pawns.OrderBy(p => CompareValueForWork(p, workType)).ToList();
        }

        private static float CompareValueForWork(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn.workSettings == null || !pawn.workSettings.EverWork) return -2f;
            if (pawn.WorkTypeIsDisabled(workType)) return -1f;
            return pawn.skills.AverageOfRelevantSkillsFor(workType);
        }

        /// <summary>
        /// Joins the work type's WorkGiverDefs into a comma-separated list with
        /// "(emergency)" markers, mirroring vanilla SpecificWorkListString but
        /// using commas instead of newlines so screen readers parse it cleanly.
        /// </summary>
        public static string BuildSpecificWorkList(WorkTypeDef workType)
        {
            if (workType?.workGiversByPriority == null || workType.workGiversByPriority.Count == 0)
                return "";

            var parts = new List<string>(workType.workGiversByPriority.Count);
            foreach (var giver in workType.workGiversByPriority)
            {
                string label = giver.LabelCap;
                if (giver.emergency)
                    label += " (" + "EmergencyWorkMarker".Translate() + ")";
                parts.Add(label);
            }
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Returns the comma-joined disabled reasons from
        /// Pawn.GetReasonsForDisabledWorkType, or empty string if none.
        /// </summary>
        public static string BuildDisabledReasons(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn == null || workType == null) return "";
            var reasons = pawn.GetReasonsForDisabledWorkType(workType);
            if (reasons == null || reasons.Count == 0) return "";
            return string.Join(", ", reasons);
        }

        /// <summary>
        /// Localized passion label matching vanilla's keyed strings
        /// (e.g. English: "Passion" / "Burning passion"). Empty when no passion.
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

        private static string FormatState(int priority)
        {
            if (!Find.PlaySettings.useWorkPriorities)
                return priority > 0 ? "on" : "off";
            return priority.ToString();
        }

        /// <summary>
        /// "{skills}: {level}{stars}" using each relevant skill's label joined by commas
        /// and the average skill level (matching vanilla's compare value).
        /// Returns empty if the work type has no relevant skills.
        /// </summary>
        private static string FormatSkillInfo(Pawn pawn, WorkTypeDef workType)
        {
            var skills = workType.relevantSkills;
            if (skills == null || skills.Count == 0) return "";

            string skillNames = string.Join(", ", skills.Select(s => s.skillLabel ?? s.label ?? s.defName));
            int level = SkillLevel(pawn, workType);
            string passionLabel = PassionLabel(pawn.skills.MaxPassionOfRelevantSkillsFor(workType));
            return passionLabel.Length > 0
                ? $"{skillNames}: {level}, {passionLabel}"
                : $"{skillNames}: {level}";
        }

        private static int SkillLevel(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.skills == null) return 0;
            float avg = pawn.skills.AverageOfRelevantSkillsFor(workType);
            if (avg < 0f) avg = 0f;
            if (avg > 20f) avg = 20f;
            return Mathf.RoundToInt(avg);
        }
    }
}
