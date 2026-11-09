using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Column-driven helpers for the work table. Column 0 is the pawn name, column 1 vanilla's own
    /// CopyPasteWorkPriorities column, and the rest follow WorkTypeDefsInPriorityOrder;
    /// PawnColumnDefGenerator inserts every work-type column immediately after the copy/paste
    /// column, so this matches vanilla's final column list exactly.
    /// </summary>
    public static class WorkTableHelper
    {
        public const int NameColumnIndex = 0;
        public const int CopyPasteColumnIndex = 1;

        private static List<WorkTypeDef> workTypesCache;

        private static PawnColumnDef copyPasteWorkPrioritiesDefCache;

        /// <summary>
        /// Vanilla's own CopyPasteWorkPriorities column def, resolved once. Its Worker is the same
        /// singleton the sighted table uses, so the static clipboard is shared with sighted play.
        /// </summary>
        public static PawnColumnDef CopyPasteWorkPrioritiesDef
        {
            get
            {
                if (copyPasteWorkPrioritiesDefCache == null)
                {
                    copyPasteWorkPrioritiesDefCache = PawnTableDefOf.Work.columns
                        .Find(c => c.Worker is PawnColumnWorker_CopyPasteWorkPriorities);
                }
                return copyPasteWorkPrioritiesDefCache;
            }
        }

        private static readonly PropertyInfo anythingInClipboardProp =
            AccessTools.Property(typeof(PawnColumnWorker_CopyPaste), "AnythingInClipboard");

        public static bool IsCopyPasteColumn(int columnIndex) => columnIndex == CopyPasteColumnIndex;

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

        public static int TotalColumnCount => 2 + WorkTypes.Count;

        /// <summary>
        /// Live eligible colonists — free colonists with work settings, babies excluded — in vanilla
        /// display order. Shared by the open guard and the scope's population so the two never drift.
        /// </summary>
        public static List<Pawn> GetEligibleColonists()
        {
            if (Find.CurrentMap == null) return new List<Pawn>();
            return PlayerPawnsDisplayOrderUtility.InOrder(
                    Find.CurrentMap.mapPawns.FreeColonists
                        .Where(p => !p.DevelopmentalStage.Baby() && p.workSettings != null))
                .ToList();
        }

        public static WorkTypeDef WorkTypeForColumn(int columnIndex)
        {
            if (columnIndex <= CopyPasteColumnIndex) return null;
            int workIndex = columnIndex - (CopyPasteColumnIndex + 1);
            if (workIndex < 0 || workIndex >= WorkTypes.Count) return null;
            return WorkTypes[workIndex];
        }

        public static string GetColumnName(int columnIndex)
        {
            if (columnIndex == NameColumnIndex) return "RimWorldAccess.Work.Column.Name".Translate();
            if (columnIndex == CopyPasteColumnIndex) return CopyPasteColumnLabel();
            WorkTypeDef workType = WorkTypeForColumn(columnIndex);
            return workType != null
                ? workType.labelShort.CapitalizeFirst()
                : "RimWorldAccess.Work.Column.Unknown".Translate().ToString();
        }

        /// <summary>
        /// The CopyPasteWorkPriorities column's header label, from PawnColumnHandlerRegistry: the def
        /// carries neither label nor headerTip, vanilla drawing two icon buttons instead, so the
        /// registry names it from the game's own Copy and Paste words.
        /// </summary>
        private static string CopyPasteColumnLabel()
        {
            PawnColumnDef def = CopyPasteWorkPrioritiesDef;
            string label = def != null ? PawnColumnHandlerRegistry.Resolve(def).HeaderLabel(def) : null;
            return !string.IsNullOrEmpty(label) ? label : "RimWorldAccess.Work.Column.Unknown".Translate().ToString();
        }

        public static string GetPawnLabel(Pawn pawn) => pawn?.LabelShort ?? "";

        /// <summary>
        /// The terse cell value, or "incapable" for a permanently disabled cell. State is the
        /// priority digit in manual mode, on/off in basic mode.
        /// </summary>
        public static string GetColumnValue(Pawn pawn, int columnIndex)
        {
            if (columnIndex == NameColumnIndex)
                return pawn.LabelShort;

            if (columnIndex == CopyPasteColumnIndex)
                return GetCopyPasteCellValue(pawn);

            WorkTypeDef workType = WorkTypeForColumn(columnIndex);
            if (workType == null) return "";

            if (pawn.workSettings == null || !pawn.workSettings.EverWork)
                return "RimWorldAccess.Work.Table.Incapable".Translate();

            if (pawn.WorkTypeIsDisabled(workType))
            {
                string reasons = BuildDisabledReasons(pawn, workType);
                return string.IsNullOrEmpty(reasons)
                    ? "RimWorldAccess.Work.Table.Incapable".Translate().ToString()
                    : "RimWorldAccess.Work.Table.IncapableWithReasons".Translate(reasons).ToString();
            }

            int priority = pawn.workSettings.GetPriority(workType);
            string state = FormatState(priority);
            string skillInfo = FormatSkillInfo(pawn, workType);

            return string.IsNullOrEmpty(skillInfo)
                ? state
                : $"{state}, {skillInfo}";
        }

        /// <summary>Vanilla's DoCell draws nothing for a pawn that is dead or can never work; this is that gate.</summary>
        public static bool IsCopyPasteEligible(Pawn pawn) =>
            pawn != null && !pawn.Dead && pawn.workSettings != null && pawn.workSettings.EverWork;

        private static readonly MethodInfo copyFromMethod =
            AccessTools.Method(typeof(PawnColumnWorker_CopyPaste), "CopyFrom");

        private static readonly MethodInfo pasteToMethod =
            AccessTools.Method(typeof(PawnColumnWorker_CopyPaste), "PasteTo");

        /// <summary>Whether vanilla's shared work-priority clipboard holds anything; the same test that decides whether the Paste icon draws.</summary>
        public static bool CanPastePriorities
        {
            get
            {
                PawnColumnDef def = CopyPasteWorkPrioritiesDef;
                if (def == null || anythingInClipboardProp == null)
                    return false;
                return (bool)anythingInClipboardProp.GetValue(def.Worker, null);
            }
        }

        /// <summary>
        /// Copies the pawn's work priorities into vanilla's clipboard by invoking the column worker's
        /// own protected CopyFrom, the delegate the sighted Copy icon runs, so buttons and chords
        /// share one clipboard. Callers gate on <see cref="IsCopyPasteEligible"/>.
        /// </summary>
        public static void CopyPriorities(Pawn pawn)
        {
            PawnColumnDef def = CopyPasteWorkPrioritiesDef;
            if (def == null || copyFromMethod == null) return;
            copyFromMethod.Invoke(def.Worker, new object[] { pawn });
        }

        /// <summary>
        /// Pastes vanilla's clipboard onto the pawn through the worker's own protected PasteTo, which
        /// skips permanently disabled work types itself. Callers gate on
        /// <see cref="CanPastePriorities"/> and <see cref="IsCopyPasteEligible"/>.
        /// </summary>
        public static void PastePriorities(Pawn pawn)
        {
            PawnColumnDef def = CopyPasteWorkPrioritiesDef;
            if (def == null || pasteToMethod == null) return;
            pasteToMethod.Invoke(def.Worker, new object[] { pawn });
        }

        /// <summary>
        /// The copy/paste cell's readable value: Copy is always available for an eligible pawn, and
        /// Paste only when the clipboard holds something, matching which icons vanilla draws.
        /// </summary>
        private static string GetCopyPasteCellValue(Pawn pawn)
        {
            PawnColumnDef def = CopyPasteWorkPrioritiesDef;
            if (def == null || !IsCopyPasteEligible(pawn))
                return "";

            bool canPaste = anythingInClipboardProp != null && (bool)anythingInClipboardProp.GetValue(def.Worker, null);
            return canPaste
                ? ("Copy".Translate() + " / " + "Paste".Translate()).ToString()
                : "Copy".Translate().ToString();
        }

        /// <summary>The column tooltip: gerundLabel, description, work givers with emergency markers, then any ideology warning.</summary>
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
                sb.Append("RimWorldAccess.Work.Task.IncludesPrefix".Translate().ToString());
                sb.Append(workList);
            }
            if (pawn?.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
            {
                sb.Append("RimWorldAccess.Work.Task.IdeologyOpposes".Translate().ToString());
            }
            return sb.ToString();
        }

        /// <summary>Vanilla's own PawnColumnDef.sortable, which the copy/paste def never sets.</summary>
        public static bool IsColumnSortable(int columnIndex) => columnIndex != CopyPasteColumnIndex;

        /// <summary>
        /// The one column-tooltip fragment that varies per row: whether THIS pawn's ideoligion
        /// opposes this column's work type. It rides ContentCellTip, hence Extras, which the composer
        /// always includes, so the warning surfaces on row moves as well as column moves.
        /// </summary>
        public static string GetIdeologyWarning(Pawn pawn, int columnIndex)
        {
            WorkTypeDef workType = WorkTypeForColumn(columnIndex);
            if (workType == null || pawn?.Ideo == null) return null;
            if (!pawn.Ideo.IsWorkTypeConsideredDangerous(workType)) return null;
            return "RimWorldAccess.Work.Task.IdeologyOpposes".Translate().ToString();
        }

        /// <summary>
        /// Sorts pawns by a column: the name column alphabetically, work columns by vanilla's own
        /// Compare, which orders on AverageOfRelevantSkillsFor with disabled -1 and no-work -2.
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
        /// The work type's WorkGiverDefs as a comma-separated list with emergency markers, mirroring
        /// vanilla's SpecificWorkListString but with commas rather than newlines.
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

        /// <summary>The comma-joined reasons from Pawn.GetReasonsForDisabledWorkType, or empty.</summary>
        public static string BuildDisabledReasons(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn == null || workType == null) return "";
            var reasons = pawn.GetReasonsForDisabledWorkType(workType);
            if (reasons == null || reasons.Count == 0) return "";
            return string.Join(", ", reasons);
        }

        /// <summary>The localized passion label from vanilla's own keys; empty when there is no passion.</summary>
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

        private static string FormatState(int priority)
        {
            if (!Find.PlaySettings.useWorkPriorities)
                return (priority > 0
                    ? "RimWorldAccess.Work.Status.Enabled".Translate()
                    : "RimWorldAccess.Work.Status.Disabled".Translate()).ToString();
            return priority.ToString();
        }

        /// <summary>
        /// The relevant skills' labels joined by commas plus the average level, vanilla's own compare
        /// value. Empty when the work type has no relevant skills.
        /// </summary>
        private static string FormatSkillInfo(Pawn pawn, WorkTypeDef workType)
        {
            var skills = workType.relevantSkills;
            if (skills == null || skills.Count == 0) return "";

            string skillNames = string.Join(", ", skills.Select(s => (s.skillLabel ?? s.label ?? s.defName).CapitalizeFirst()));
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
