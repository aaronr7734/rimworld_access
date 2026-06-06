using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Read-only pawn-skills table. Rows are colonists (colonist bar order);
    /// columns are SkillDefs in vanilla SkillUI order (listOrder descending).
    /// Opened via Alt+P. Navigation mirrors WorkTableState but without editing
    /// or painting — this view only surfaces information.
    /// </summary>
    public static class PawnSkillsTableState
    {
        public static bool IsActive { get; private set; }

        private static List<Pawn> pawns = new List<Pawn>();
        private static TabularMenuHelper<Pawn> tableHelper;

        public static TabularMenuHelper<Pawn> TableHelper => tableHelper;
        public static TypeaheadSearchHelper Typeahead => tableHelper?.Typeahead;
        public static int CurrentRowIndex => tableHelper?.CurrentRowIndex ?? 0;
        public static int CurrentColumnIndex => tableHelper?.CurrentColumnIndex ?? 0;
        public static int PawnCount => pawns.Count;

        public static Pawn CurrentPawn =>
            pawns.Count > 0 && CurrentRowIndex >= 0 && CurrentRowIndex < pawns.Count
                ? pawns[CurrentRowIndex]
                : null;

        #region Lifecycle

        public static void Open()
        {
            if (IsActive) return;
            if (Current.ProgramState != ProgramState.Playing)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NotInGame".Loc());
                return;
            }
            if (Find.CurrentMap == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoMapLoaded".Loc());
                return;
            }

            PawnSkillsTableHelper.RefreshSkills();

            pawns = Find.ColonistBar.GetColonistsInOrder()
                .Where(p => p != null && p.Spawned && p.Map == Find.CurrentMap && p.skills != null)
                .ToList();

            if (pawns.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Input.WorkMenu.NoColonistsAvailable".Loc());
                return;
            }

            tableHelper = new TabularMenuHelper<Pawn>(
                getColumnCount: () => PawnSkillsTableHelper.TotalColumnCount,
                getItemLabel: PawnSkillsTableHelper.GetPawnLabel,
                getColumnName: PawnSkillsTableHelper.GetColumnName,
                getColumnValue: PawnSkillsTableHelper.GetColumnValue,
                sortByColumn: (items, col, desc) => PawnSkillsTableHelper.SortPawnsByColumn(items, col, desc),
                getColumnTooltip: PawnSkillsTableHelper.GetColumnTooltip,
                isColumnSortable: PawnSkillsTableHelper.IsColumnSortable);

            tableHelper.Reset();
            tableHelper.SetDefaultOrder(pawns);

            // Start on the first skill column (not Name) so Alt+S immediately sorts by a skill.
            tableHelper.CurrentColumnIndex = PawnSkillsTableHelper.Skills.Count > 0 ? 1 : 0;

            IsActive = true;
            TolkHelper.SpeakData("RimWorldAccess.Pawns.SkillsTable.Opened".Translate(pawns.Count.ToString(), PawnSkillsTableHelper.Skills.Count.ToString()).ToString());
            AnnounceInitialCell();
        }

        public static void Close()
        {
            if (!IsActive) return;
            CleanupState();
            TolkHelper.Speak("RimWorldAccess.Pawns.SkillsTable.Closed".Loc());
        }

        private static void CleanupState()
        {
            IsActive = false;
            pawns.Clear();
            tableHelper?.ClearSearch();
            tableHelper = null;
        }

        /// <summary>
        /// Initial-entry announcement includes column tooltip (skill description).
        /// BuildCellAnnouncement would normally suppress the tooltip on row moves;
        /// the first cell has no prior context, so we speak the description once.
        /// </summary>
        private static void AnnounceInitialCell()
        {
            if (pawns.Count == 0) return;
            Pawn pawn = CurrentPawn;
            if (pawn == null) return;

            string cell = tableHelper.BuildCellAnnouncement(pawn, pawns.Count, includeItemName: true);
            string tooltip = PawnSkillsTableHelper.GetColumnTooltip(pawn, CurrentColumnIndex);
            TolkHelper.SpeakData(string.IsNullOrEmpty(tooltip) ? cell : $"{cell}. {tooltip}");
        }

        #endregion

        #region Navigation

        public static void SelectNextPawn()
        {
            if (pawns.Count == 0) return;
            tableHelper.SelectNextRow(pawns.Count);
            AnnounceCurrentCell(includePawnName: true, includeColumnName: false);
        }

        public static void SelectPreviousPawn()
        {
            if (pawns.Count == 0) return;
            tableHelper.SelectPreviousRow(pawns.Count);
            AnnounceCurrentCell(includePawnName: true, includeColumnName: false);
        }

        public static void SelectNextColumn()
        {
            if (pawns.Count == 0) return;
            tableHelper.SelectNextColumn();
            AnnounceCurrentCell(includePawnName: false);
        }

        public static void SelectPreviousColumn()
        {
            if (pawns.Count == 0) return;
            tableHelper.SelectPreviousColumn();
            AnnounceCurrentCell(includePawnName: false);
        }

        public static void JumpToFirst()
        {
            if (pawns.Count == 0) return;
            tableHelper.JumpToFirst(pawns.Count);
            AnnounceCurrentCell(includePawnName: true, includeColumnName: false);
        }

        public static void JumpToLast()
        {
            if (pawns.Count == 0) return;
            tableHelper.JumpToLast(pawns.Count);
            AnnounceCurrentCell(includePawnName: true, includeColumnName: false);
        }

        #endregion

        #region Sorting / Typeahead

        public static void ToggleSortByCurrentColumn()
        {
            if (pawns.Count == 0) return;
            var result = tableHelper.ToggleSortByCurrentColumn(pawns, out string direction, out bool sortCleared);
            if (result == null)
            {
                TolkHelper.SpeakData("RimWorldAccess.Animals.Sort.CannotSort".Translate(tableHelper.GetCurrentColumnName()).ToString());
                return;
            }
            pawns = result.ToList();
            if (sortCleared)
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Animals.Sort.Cleared".Loc());
            }
            else
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                TolkHelper.SpeakData("RimWorldAccess.Animals.Sort.SortedBy".Translate(tableHelper.GetCurrentColumnName(), direction).ToString());
            }
            AnnounceCurrentCell(includePawnName: true);
        }

        public static bool HandleTypeahead(char c)
        {
            if (pawns.Count == 0) return false;
            if (tableHelper.HandleTypeahead(c, pawns, out _))
            {
                AnnounceWithSearch();
                return true;
            }
            tableHelper.Typeahead.SpeakNoMatches();
            return false;
        }

        public static bool HandleBackspace()
        {
            if (pawns.Count == 0) return false;
            if (tableHelper.HandleBackspace(pawns, out _))
            {
                if (tableHelper.Typeahead.HasActiveSearch)
                    AnnounceWithSearch();
                else
                    AnnounceCurrentCell(includePawnName: true);
                return true;
            }
            return false;
        }

        public static bool ClearSearchIfActive()
        {
            if (tableHelper?.Typeahead.HasActiveSearch == true)
            {
                tableHelper.Typeahead.ClearSearchAndAnnounce();
                AnnounceCurrentCell(includePawnName: true);
                return true;
            }
            return false;
        }

        #endregion

        #region Announcements

        public static void AnnounceCurrentCell(bool includePawnName, bool includeColumnName = true)
        {
            if (pawns.Count == 0) return;
            Pawn pawn = CurrentPawn;
            if (pawn == null) return;
            string announcement = tableHelper.BuildCellAnnouncement(pawn, pawns.Count, includePawnName, includeColumnName);
            TolkHelper.SpeakData(announcement);
        }

        public static void AnnounceWithSearch()
        {
            if (pawns.Count == 0) return;
            Pawn pawn = CurrentPawn;
            if (pawn == null) return;
            string announcement = tableHelper.BuildCellAnnouncementWithSearch(pawn, pawns.Count);
            TolkHelper.SpeakData(announcement);
        }

        #endregion
    }
}
