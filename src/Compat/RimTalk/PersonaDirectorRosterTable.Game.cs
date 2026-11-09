using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The table model for PersonaDirector's <c>PawnRosterList</c>, shared by every window that
    /// embeds one: its leading Selected/Name/Race/Faction/Tag columns, sort, search, category
    /// filters and selection. Rows come from the roster's own <c>VisiblePawns</c>, never from
    /// draw capture, so rows scrolled out of view stay reachable. Owning scopes append their
    /// trailing columns from <see cref="ColumnCount"/> on.
    ///
    /// The roster's selection column answers only raw mouse presses and drags
    /// (<c>Widgets.CheckboxDraw</c> plus <c>Mouse.IsOver</c>), so a row toggles one pawn at a
    /// time into the same set the mouse paints.
    /// </summary>
    internal sealed class PersonaDirectorRosterTable
    {
        internal const int ColSelected = 0;
        internal const int ColName = 1;
        internal const int ColRace = 2;
        internal const int ColFaction = 3;
        internal const int ColTag = 4;
        internal const int ColumnCount = 5;

        private readonly Func<object> rosterOf;
        private readonly Action refreshModel;
        private readonly List<Pawn> pawns = new List<Pawn>();

        public PersonaDirectorRosterTable(Func<object> rosterOf, Action refreshModel)
        {
            this.rosterOf = rosterOf;
            this.refreshModel = refreshModel;
        }

        public object Roster
        {
            get { return rosterOf(); }
        }

        public int Count
        {
            get { return pawns.Count; }
        }

        public Pawn PawnAt(int row)
        {
            return row >= 0 && row < pawns.Count ? pawns[row] : null;
        }

        public void Refresh()
        {
            pawns.Clear();
            object roster = Roster;
            if (roster != null)
            {
                pawns.AddRange(PersonaDirectorCompat.VisiblePawns(roster));
            }
        }

        public static TableColumnInfo ColumnInfo(int column)
        {
            switch (column)
            {
                case ColSelected: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.PersonaDirector.HeaderSelected".Translate());
                case ColName: return new TableColumnInfo(Translator.Translate("RPD_Batch_Header_Name"), null, true);
                case ColRace: return new TableColumnInfo(Translator.Translate("RPD_Batch_Header_Race"), null, true);
                case ColFaction: return new TableColumnInfo(Translator.Translate("RPD_Batch_Header_Faction"), null, true);
                default: return new TableColumnInfo(Translator.Translate("RPD_Batch_Header_Tag"), null, true);
            }
        }

        public string CellText(int row, int column)
        {
            Pawn pawn = PawnAt(row);
            if (pawn == null) return "";
            switch (column)
            {
                case ColSelected:
                    return IsSelected(pawn)
                        ? "RimWorldAccess.Compat.RimTalk.PersonaDirector.Selected".Translate()
                        : "RimWorldAccess.Compat.RimTalk.PersonaDirector.NotSelected".Translate();
                case ColName:
                    return pawn.LabelShortCap;
                case ColRace:
                    return pawn.genes != null && pawn.genes.Xenotype != null ? pawn.genes.XenotypeLabel : pawn.def.label;
                case ColFaction:
                    return pawn.Faction != null ? pawn.Faction.Name : (string)Translator.Translate("RPD_Faction_None");
                default:
                    return PersonaDirectorCompat.PawnTag(pawn);
            }
        }

        /// <summary>The name cell's own hover tooltip, the pawn's current persona.</summary>
        public string CellTip(int row, int column)
        {
            Pawn pawn = PawnAt(row);
            if (pawn == null || column != ColName) return null;
            return TranslatorFormattedStringExtensions.Translate("RPD_Batch_PawnTooltip",
                PersonaDirectorCompat.CurrentPersonality(pawn));
        }

        public bool IsSelected(Pawn pawn)
        {
            HashSet<Pawn> selected = PersonaDirectorCompat.SelectedPawns(Roster);
            return selected != null && selected.Contains(pawn);
        }

        /// <summary>MUTATION-C: mirrors DrawSelection's single-row paint (`_selectedPawns.Add`/`Remove`), which only a raw mouse press reaches.</summary>
        public void ToggleSelection(int row)
        {
            HashSet<Pawn> selected = PersonaDirectorCompat.SelectedPawns(Roster);
            Pawn pawn = PawnAt(row);
            if (selected == null || pawn == null) return;
            if (!selected.Add(pawn))
            {
                selected.Remove(pawn);
            }
        }

        /// <summary>Enter on one of the shared columns; false leaves it to the row default.</summary>
        public bool ActivateCell(int row, int column)
        {
            Pawn pawn = PawnAt(row);
            if (pawn == null || column != ColName) return false;
            PersonaDirectorCompat.JumpToPawn(Roster, pawn);
            refreshModel();
            return true;
        }

        /// <summary>Vehicle A: the row icon's own click body.</summary>
        public void OpenInfoCard(int row)
        {
            Pawn pawn = PawnAt(row);
            if (pawn == null)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }
            Find.WindowStack.Add(new Dialog_InfoCard(pawn));
        }

        // Sort: the roster's own 2-state header toggle against this codebase's 3-state cycle;
        // Cleared maps back to its constructed default, Name ascending.

        public bool TryGetLiveSort(out int column, out bool descending)
        {
            column = -1;
            descending = false;
            object roster = Roster;
            if (roster == null) return false;
            bool ascending;
            column = ColName + PersonaDirectorCompat.SortOrdinal(roster, out ascending);
            descending = !ascending;
            return true;
        }

        public int ApplySort(int column, SortCycleResult cycle, int currentRow)
        {
            int ordinal = column - ColName;
            object roster = Roster;
            if (roster == null || ordinal < 0 || ordinal > 3) return -1;

            Pawn current = PawnAt(currentRow);
            if (cycle == SortCycleResult.Cleared)
            {
                PersonaDirectorCompat.SetSort(roster, 0, true);
            }
            else
            {
                PersonaDirectorCompat.SetSort(roster, ordinal, cycle == SortCycleResult.SortedAscending);
            }
            Refresh();

            int index = current == null ? 0 : pawns.IndexOf(current);
            return index >= 0 ? index : 0;
        }

        public string SearchText
        {
            get { return PersonaDirectorCompat.SearchText(Roster); }
        }

        public void ApplySearch(string value)
        {
            object roster = Roster;
            if (roster == null) return;
            PersonaDirectorCompat.SetSearchText(roster, value);
            refreshModel();
        }

        // Buttons-region rows for DrawFilters and DrawList's header box.

        public ScreenAction SelectAllFiltersAction(string actionId = null)
        {
            return new ScreenAction(Translator.Translate("RPD_Batch_SelectAll"), SelectAllFilters, actionId);
        }

        public ScreenAction RefreshAction(string actionId = null)
        {
            return new ScreenAction(Translator.Translate("RPD_Batch_Refresh"), RefreshRoster, actionId);
        }

        public void AddFilterActions(List<ScreenAction> actions, string actionIdPrefix = null)
        {
            object roster = Roster;
            IDictionary filters = PersonaDirectorCompat.BatchFilters();
            if (roster == null || filters == null) return;
            foreach (string key in PersonaDirectorCompat.AvailableFilterKeys(roster))
            {
                if (!filters.Contains(key)) continue;
                string filterKey = key;
                actions.Add(new ScreenAction(
                    Translator.Translate("RPD_Filter_" + filterKey),
                    () => ToggleFilter(filterKey),
                    actionIdPrefix == null ? null : actionIdPrefix + filterKey,
                    check: (bool)filters[filterKey] ? CheckState.Checked : CheckState.Unchecked));
            }
        }

        public ScreenAction SelectAllListedAction()
        {
            return new ScreenAction(
                "RimWorldAccess.Compat.RimTalk.PersonaDirector.SelectAllListed".Translate(),
                ToggleAllListed,
                check: AllListedSelected() ? CheckState.Checked : CheckState.Unchecked);
        }

        /// <summary>MUTATION-C: mirrors DrawFilters' Select All click (every available filter set to "any was off", then Refresh).</summary>
        private void SelectAllFilters()
        {
            object roster = Roster;
            IDictionary filters = PersonaDirectorCompat.BatchFilters();
            if (roster == null || filters == null) return;
            List<string> keys = PersonaDirectorCompat.AvailableFilterKeys(roster).Where(filters.Contains).ToList();
            bool value = keys.Any(key => !(bool)filters[key]);
            foreach (string key in keys)
            {
                filters[key] = value;
            }
            PersonaDirectorCompat.RefreshRoster(roster);
            refreshModel();
        }

        /// <summary>MUTATION-C: mirrors DrawFilters' per-filter checkbox and label click (flip the BatchFilters entry, then Refresh).</summary>
        private void ToggleFilter(string key)
        {
            object roster = Roster;
            IDictionary filters = PersonaDirectorCompat.BatchFilters();
            if (roster == null || filters == null || !filters.Contains(key)) return;
            filters[key] = !(bool)filters[key];
            PersonaDirectorCompat.RefreshRoster(roster);
            refreshModel();
        }

        private bool AllListedSelected()
        {
            HashSet<Pawn> selected = PersonaDirectorCompat.SelectedPawns(Roster);
            return pawns.Count > 0 && selected != null && pawns.All(selected.Contains);
        }

        /// <summary>MUTATION-C: mirrors DrawList's header checkbox (add or remove every listed pawn); the header box is a bare Widgets.Checkbox with no caption to address.</summary>
        private void ToggleAllListed()
        {
            HashSet<Pawn> selected = PersonaDirectorCompat.SelectedPawns(Roster);
            if (selected == null) return;
            bool select = !AllListedSelected();
            foreach (Pawn pawn in pawns)
            {
                if (select) selected.Add(pawn); else selected.Remove(pawn);
            }
        }

        private void RefreshRoster()
        {
            object roster = Roster;
            if (roster == null) return;
            PersonaDirectorCompat.RefreshRoster(roster);
            refreshModel();
        }
    }
}
