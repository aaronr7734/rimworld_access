using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for the mechanoid table: every player-controlled mech with an
    /// overseer, tabular by name/energy/draft/auto-repair/overseer/control-group/work-mode/
    /// allowed-area, with per-column submenus for Control Group, Work Mode and Allowed Area. The
    /// mod owns no window here — the surface is the windowless <see cref="MechsMenuState"/> state
    /// machine — so the scope rides the focus stack through <see cref="MechsScopeMirror"/>. One
    /// table region, no Buttons region. All row/cursor/search/submenu state is instance state:
    /// popping the scope IS the reset.
    ///
    /// <c>mechs.infoCard</c> and the paint chords have no base equivalent and stay per-screen
    /// claims gated to <see cref="BrowseMode"/>; the modal backstop
    /// (<c>ShellGuards.MenuOwnsInput()</c>) swallows them while a submenu is open.
    ///
    /// A submenu reshapes the same region rather than adding a second one:
    /// <see cref="ContentColumnCount"/> reports 0 while one is open, turning the region into a
    /// flat option list. Because the model's row cursor is reused for both the mech row and the
    /// picked option, <see cref="submenuMechRow"/> remembers the original mech row across the
    /// submenu's lifetime, and the submenu keeps its own hand-built announcement pair.
    ///
    /// One announcement per edit: <see cref="AfterCellEdit"/> speaks either the sorted "now at"
    /// phrase or the fresh cell value, never both. Work Mode is the one case with a richer phrase
    /// (RimWorldAccess.Mechs.WorkMode.SetForGroup), because the mode is set for the WHOLE control
    /// group and that context is genuinely new information. The "already in group" / "already
    /// set" branches are real game-logic early-outs: no mutation happens and the submenu stays
    /// open for another pick.
    ///
    /// The Draft column's tooltip is per-mech (<c>MechanitorUtility.CanDraftMech</c>'s rejection
    /// reason) while every other column's is static. <see cref="ContentColumnInfo"/> resolves it
    /// against the row the cursor rests on rather than exposing a per-cell
    /// <c>ContentCellTip</c>, which the composer would speak on every row move too.
    ///
    /// Manage-Areas drill-in: the AllowedArea/ManageAreas branch calls CloseSubmenuInternal
    /// (submenu only, NOT the whole scope) before raising the real Dialog_ManageAreas above this
    /// window; <see cref="MechsScopeMirror"/> stands down for it via
    /// ForeignDialogWindowAbove, and for the shape placement its Expand/Shrink hand off to
    /// via !ShapePlacementState.IsActive / !ViewingModeState.IsActive.
    /// </summary>
    public sealed class MechsScope : ScreenScope, IPawnTableFocusSource
    {
        private const string ManageAreasMarker = "ManageAreas";

        private enum SubmenuType { None, ControlGroup, WorkMode, AllowedArea }

        private readonly List<Pawn> mechsList = new List<Pawn>();
        private List<Pawn> defaultOrder;

        private SubmenuType activeSubmenu = SubmenuType.None;
        private readonly List<object> submenuOptions = new List<object>();
        private readonly TypeaheadSearchHelper submenuTypeahead = new TypeaheadSearchHelper();
        private int submenuMechRow = -1;
        private Area lastAppliedArea;

        private bool announcedOpen;

        public MechsScope()
        {
            Claim(SharedMenuGrammar.Cancel, delegate { HandleCancelKey(); });
            Claim(SharedMenuGrammar.SearchBackspace, delegate { HandleBackspaceKey(); },
                when: () => IsInSubmenu && submenuTypeahead.HasActiveSearch);

            Claim("mechs.infoCard", delegate { OpenInfoCard(); }, when: BrowseMode);

            // Painting: Shift+Up/Down single-cell, Shift+Home/End bulk-to-edge,
            // Ctrl+Shift+Home/End entire column.
            Claim("mechs.paintUp", delegate { PaintUp(); }, when: BrowseMode);
            Claim("mechs.paintDown", delegate { PaintDown(); }, when: BrowseMode);
            Claim("mechs.paintToFirst", delegate { PaintToFirst(); }, when: BrowseMode);
            Claim("mechs.paintToLast", delegate { PaintToLast(); }, when: BrowseMode);
            Claim("mechs.paintEntireColumn",
                delegate (KeyEventSnapshot e) { PaintEntireColumn(e.Key == KeyCode.Home); }, when: BrowseMode);
        }

        public override string Name
        {
            get { return "mechs"; }
        }

        /// <summary>Shared cross-region typeahead for the mech table; the submenu picker keeps its own search.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Windowless modal: this scope owns Escape itself (close the menu / cancel a submenu / clear a search).</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        private bool IsInSubmenu
        {
            get { return activeSubmenu != SubmenuType.None; }
        }

        private bool BrowseMode()
        {
            return !IsInSubmenu;
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Reuses vanilla's own Mechs tab label — no new translation key.</summary>
        protected override string ContentRegionName(int region)
        {
            MainButtonDef def = DefDatabase<MainButtonDef>.GetNamedSilentFail("Mechs");
            return def != null ? def.LabelCap.Resolve() : "Mechs";
        }

        /// <summary>0 while a submenu is open (a flat option list), else the fixed 8-column set.</summary>
        protected override int ContentColumnCount(int region)
        {
            return IsInSubmenu ? 0 : MechsMenuHelper.GetTotalColumnCount();
        }

        protected override int ContentItemCount(int region)
        {
            return IsInSubmenu ? submenuOptions.Count : mechsList.Count;
        }

        /// <summary>
        /// One-time population: the roster is snapshotted here and never silently re-fetched,
        /// rebuilding only when the cached list is empty. Default order matches
        /// PawnTable_Mechs.LabelSortFunction exactly (Overseer, ControlGroupIndex, KindLabel,
        /// Label via NaturalStringComparer), which is also what ColonistBarState.GetMechs() uses,
        /// so the bar and the menu agree on order.
        /// </summary>
        protected override void RefreshContent()
        {
            if (mechsList.Count > 0)
                return;
            Map map = Find.CurrentMap;
            if (map == null)
                return;
            List<Pawn> initial = map.mapPawns.PawnsInFaction(Faction.OfPlayer)
                .Where(p => p.RaceProps.IsMechanoid && p.OverseerSubject != null)
                .ToList();
            if (initial.Count == 0)
                return;

            MechsMenuHelper.InitColumnDefs();
            List<Pawn> sorted = initial
                .OrderBy(p => p.GetOverseer()?.thingIDNumber ?? int.MaxValue)
                .ThenBy(p => p.GetMechControlGroup()?.Index ?? int.MaxValue)
                .ThenBy(p => p.KindLabel)
                .ThenBy(p => p.Label, NaturalStringComparer.Instance)
                .ToList();
            mechsList.AddRange(sorted);
            defaultOrder = new List<Pawn>(mechsList);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (IsInSubmenu)
            {
                d.Label = index >= 0 && index < submenuOptions.Count ? GetSubmenuOptionText(submenuOptions[index]) : "";
                return d;
            }
            if (index >= 0 && index < mechsList.Count)
            {
                d.Label = MechsMenuHelper.GetMechName(mechsList[index]);
            }
            return d;
        }

        /// <summary>
        /// Row default: in submenu mode Enter applies the highlighted option; in table mode this
        /// only runs for the non-interactive columns (Energy, Overseer), which say they are read only.
        /// </summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (IsInSubmenu)
            {
                ApplySubmenuSelection(index);
                return;
            }
            if (index < 0 || index >= mechsList.Count)
                return;
            AnnounceCurrentCellReadOnly();
        }

        /// <summary>
        /// Column header tooltip. Static for every column except Draft, whose tooltip is the
        /// per-mech CanDraftMech rejection reason, resolved against the current row.
        /// </summary>
        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            // Only Draft's tooltip dereferences the pawn argument; the others are
            // pawn-independent and must still resolve on the header row, where there is no
            // current data row to take a context pawn from.
            bool needsPawn = MechsMenuHelper.GetColumnType(column) == MechsMenuHelper.ColumnType.Draft;
            Pawn contextPawn = needsPawn ? CurrentRowPawn() : null;
            string tooltip = (!needsPawn || contextPawn != null)
                ? MechsMenuHelper.GetColumnTooltip(contextPawn, column)
                : null;
            return new TableColumnInfo(
                MechsMenuHelper.GetColumnName(column),
                tooltip,
                MechsMenuHelper.IsColumnSortable(column));
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (row < 0 || row >= mechsList.Count)
                return "";
            return MechsMenuHelper.GetColumnValue(mechsList[row], column);
        }

        /// <summary>Registry cell tips for Unknown-classified (typically modded) columns only; Draft's per-mech tip stays on the header channel (see class remarks).</summary>
        protected override string ContentCellTip(int region, int row, int column)
        {
            if (IsInSubmenu || row < 0 || row >= mechsList.Count)
                return null;
            return MechsMenuHelper.GetUnknownCellTip(mechsList[row], column);
        }

        /// <summary>Enter on an interactive column: Name jumps to the mech, Draft/AutoRepair toggle, Control Group/Work Mode/Allowed Area open a submenu.</summary>
        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (row < 0 || row >= mechsList.Count)
                return false;
            if (MechsMenuHelper.GetColumnType(column) == MechsMenuHelper.ColumnType.Unknown)
            {
                switch (PawnColumnCellReader.ActivateCellTableless(MechsMenuHelper.GetDef(column), mechsList[row]))
                {
                    case PawnColumnActivation.StateChanged: AnnounceCurrentCellStateChange(); return true;
                    case PawnColumnActivation.OpenedUI: return true;
                    default: return false;
                }
            }
            if (!MechsMenuHelper.IsColumnInteractive(column))
                return false;

            Pawn pawn = mechsList[row];
            switch (MechsMenuHelper.GetColumnType(column))
            {
                case MechsMenuHelper.ColumnType.Name: JumpToMechOnMap(pawn); return true;
                case MechsMenuHelper.ColumnType.Draft: ToggleDraft(pawn); return true;
                case MechsMenuHelper.ColumnType.AutoRepair: ToggleAutoRepair(pawn); return true;
                case MechsMenuHelper.ColumnType.ControlGroup: OpenControlGroupSubmenu(pawn); return true;
                case MechsMenuHelper.ColumnType.WorkMode: OpenWorkModeSubmenu(pawn); return true;
                case MechsMenuHelper.ColumnType.AllowedArea: OpenAllowedAreaSubmenu(pawn); return true;
                default: return false;
            }
        }

        /// <summary>
        /// Re-order for a new sort state using the game's own column comparers; "cleared"
        /// restores the composite default order captured at open.
        /// </summary>
        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            Pawn currentPawn = currentRow >= 0 && currentRow < mechsList.Count ? mechsList[currentRow] : null;
            List<Pawn> reordered = cycle == SortCycleResult.Cleared
                ? new List<Pawn>(defaultOrder ?? mechsList)
                : MechsMenuHelper.SortMechsByColumn(mechsList, column, cycle == SortCycleResult.SortedDescending).ToList();

            mechsList.Clear();
            mechsList.AddRange(reordered);

            if (currentPawn == null)
                return 0;
            int index = mechsList.IndexOf(currentPawn);
            return index >= 0 ? index : 0;
        }

        /// <summary>Read-only-ish screen (no vanilla-equivalent button), no Buttons region.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        private Pawn CurrentRowPawn()
        {
            TableModel table = Model.CurrentTable;
            if (table == null)
                return null;
            int row = table.Rows.Index - 1;
            return row >= 0 && row < mechsList.Count ? mechsList[row] : null;
        }

        public override void OnPush()
        {
            base.OnPush();
            mechsList.Clear();
            defaultOrder = null;
            TypeaheadReset();
            activeSubmenu = SubmenuType.None;
            submenuOptions.Clear();
            submenuTypeahead.ClearSearch();
            submenuMechRow = -1;
            announcedOpen = false;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            if (mechsList.Count == 0)
                return; // MechsMenuState.Open() already refused to open/announce for an empty roster.

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            TolkHelper.SpeakData("RimWorldAccess.Mechs.Menu.OpeningTitle".Translate(mechsList.Count).ToString());
            AnnounceCurrentItem();
        }

        private void HandleCancelKey()
        {
            if (IsInSubmenu)
            {
                if (submenuTypeahead.HasActiveSearch)
                {
                    submenuTypeahead.ClearSearchAndAnnounce();
                    AnnounceSubmenuWithSearch();
                }
                else
                {
                    CloseSubmenuInternal();
                }
                return;
            }
            // Main-table Escape-clears-search is the base typeahead claim
            // (registered ahead of this one), so reaching here means no search.
            Close();
        }

        private void HandleBackspaceKey()
        {
            List<string> labels = submenuOptions.ConvertAll(GetSubmenuOptionText);
            if (submenuTypeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    RefreshModel();
                    Model.CurrentRegion?.MoveTo(newIndex);
                }
                AnnounceSubmenuWithSearch();
            }
        }

        /// <summary>The submenu picker keeps its own search; the table rides the base engine.</summary>
        public override bool HandleChar(char c)
        {
            if (!IsInSubmenu)
                return base.HandleChar(c);
            if (!TypeaheadMatcher.AcceptsSearchChar(c, submenuTypeahead.HasActiveSearch))
                return false;
            List<string> labels = submenuOptions.ConvertAll(GetSubmenuOptionText);
            if (submenuTypeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    RefreshModel();
                    Model.CurrentRegion?.MoveTo(newIndex);
                    AnnounceSubmenuWithSearch();
                }
            }
            else
            {
                submenuTypeahead.SpeakNoMatches();
            }
            return true;
        }

        /// <summary>
        /// Up/Down: submenu mode steps through options (wrapping, search-aware); table mode
        /// defers to the base row cursor.
        /// </summary>
        protected override void MoveItem(int delta)
        {
            if (IsInSubmenu)
            {
                SubmenuMove(delta);
                return;
            }
            base.MoveItem(delta);
        }

        /// <summary>Home/End: deliberately silent in submenu mode; table mode defers to the base.</summary>
        protected override void MoveItemEdge(bool first)
        {
            if (IsInSubmenu)
                return;
            base.MoveItemEdge(first);
        }

        private void OpenInfoCard()
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            int row = table == null ? -1 : table.Rows.Index - 1;
            Pawn mech = row >= 0 && row < mechsList.Count ? mechsList[row] : null;
            if (mech != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(mech));
            }
            else
            {
                InfoCardState.SpeakNoInfoCardAvailable();
            }
        }

        private void JumpToMechOnMap(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null)
            {
                TolkHelper.Speak("RimWorldAccess.Mechs.Menu.NotOnMap".Loc(), SpeechPriority.High);
                return;
            }
            IntVec3 position = pawn.Position;
            Close();
            MapNavigationState.CurrentCursorPosition = position;
            Find.CameraDriver?.JumpToCurrentMapLoc(position);
            string mechName = pawn.Name != null ? pawn.Name.ToStringShort : pawn.def.LabelCap.ToString();
            MapNavigationState.SpeakJumpedTo(mechName);
        }

        private void ToggleDraft(Pawn pawn)
        {
            if (!ModsConfig.BiotechActive || !pawn.IsColonyMechPlayerControlled || !pawn.Spawned)
            {
                TolkHelper.Speak("RimWorldAccess.Mechs.Draft.CannotDraftThisMech".Loc(), SpeechPriority.High);
                return;
            }

            AcceptanceReport canDraft = MechanitorUtility.CanDraftMech(pawn);
            if (!canDraft)
            {
                // canDraft.Reason is already localized; fall back when the game provides none.
                if (canDraft.Reason.NullOrEmpty())
                    TolkHelper.Speak("RimWorldAccess.Mechs.Draft.CannotDraft".Loc(), SpeechPriority.High);
                else
                    TolkHelper.SpeakData(canDraft.Reason, SpeechPriority.High);
                return;
            }

            pawn.drafter.Drafted = !pawn.Drafted;
            (pawn.Drafted ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            AfterCellEdit();
        }

        private void ToggleAutoRepair(Pawn pawn)
        {
            CompMechRepairable comp = pawn.GetComp<CompMechRepairable>();
            if (comp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Mechs.AutoRepair.NoComponent".Loc(), SpeechPriority.High);
                return;
            }

            comp.autoRepair = !comp.autoRepair;
            (comp.autoRepair ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            AfterCellEdit();
        }

        /// <summary>
        /// Re-sorts in place when the table is actively sorted by the just-edited column, keeping
        /// the cursor at the same ROW POSITION rather than following the edited item, and speaks
        /// the "now at {cell}" phrase; otherwise speaks
        /// <paramref name="overrideAnnouncement"/> when given, else the edited cell's fresh
        /// value. Exactly ONE utterance per edit either way.
        /// </summary>
        private void AfterCellEdit(string overrideAnnouncement = null)
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table != null && table.HasActiveSort && table.SortColumnIndex == table.ColumnIndex)
            {
                List<Pawn> resorted = MechsMenuHelper.SortMechsByColumn(mechsList, table.ColumnIndex, table.SortDescending).ToList();
                mechsList.Clear();
                mechsList.AddRange(resorted);
                int currentRow = table.Rows.Index - 1;
                int clamped = Math.Min(currentRow, mechsList.Count - 1);
                if (clamped >= 0)
                {
                    table.Rows.MoveTo(clamped + 1);
                    string cellText = BuildCellText(clamped, includeItemName: true);
                    TolkHelper.SpeakData("RimWorldAccess.Mechs.Sort.NowAt".Translate(cellText).ToString());
                    return;
                }
            }
            if (!string.IsNullOrEmpty(overrideAnnouncement))
            {
                TolkHelper.SpeakData(overrideAnnouncement);
                return;
            }
            AnnounceCurrentCellStateChange();
        }

        /// <summary>"{mech} - {column}: {value}", or just the value when it echoes the mech name.</summary>
        private string BuildCellText(int row, bool includeItemName)
        {
            if (row < 0 || row >= mechsList.Count)
                return "";
            Pawn pawn = mechsList[row];
            int column = Model.CurrentTable?.ColumnIndex ?? 0;
            string columnValue = MechsMenuHelper.GetColumnValue(pawn, column);
            if (!includeItemName)
                return columnValue;
            string itemLabel = MechsMenuHelper.GetMechName(pawn);
            if (itemLabel == columnValue || columnValue.StartsWith(itemLabel))
                return columnValue;
            string columnName = MechsMenuHelper.GetColumnName(column);
            return $"{itemLabel} - {columnName}: {columnValue}";
        }

        // Submenu system (Control Group / Work Mode / Allowed Area pickers).

        private void OpenSubmenu(SubmenuType type, int selectedIndex)
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            submenuMechRow = table != null ? table.Rows.Index - 1 : -1;

            activeSubmenu = type;
            submenuTypeahead.ClearSearch();
            RefreshModel(); // rebuild the region as a flat option list (ContentColumnCount now 0)
            ListModel region = Model.CurrentRegion;
            if (region != null && !region.IsEmpty)
            {
                region.MoveTo(Mathf.Clamp(selectedIndex, 0, region.Count - 1));
            }
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceSubmenuOption();
        }

        private void OpenControlGroupSubmenu(Pawn pawn)
        {
            if (pawn.IsGestating())
            {
                TolkHelper.SpeakData("Gestating".Translate().Resolve(), SpeechPriority.High);
                return;
            }

            Pawn overseer = pawn.GetOverseer();
            if (overseer?.mechanitor == null)
            {
                TolkHelper.Speak("RimWorldAccess.Mechs.Submenu.NoOverseer".Loc(), SpeechPriority.High);
                return;
            }

            var groups = overseer.mechanitor.controlGroups;
            MechanitorControlGroup currentGroup = pawn.GetMechControlGroup();

            submenuOptions.Clear();
            foreach (var group in groups)
                submenuOptions.Add(group);

            int selected = 0;
            if (currentGroup != null)
            {
                int idx = groups.IndexOf(currentGroup);
                if (idx >= 0)
                    selected = idx;
            }

            OpenSubmenu(SubmenuType.ControlGroup, selected);
        }

        private void OpenWorkModeSubmenu(Pawn pawn)
        {
            MechanitorControlGroup group = pawn.GetMechControlGroup();
            if (group == null)
            {
                TolkHelper.Speak("RimWorldAccess.Mechs.Submenu.NoControlGroup".Loc(), SpeechPriority.High);
                return;
            }

            var modes = DefDatabase<MechWorkModeDef>.AllDefsListForReading.OrderBy(d => d.uiOrder).ToList();

            submenuOptions.Clear();
            submenuOptions.AddRange(modes.Cast<object>());

            int selected = 0;
            int currentIdx = modes.IndexOf(group.WorkMode);
            if (currentIdx >= 0)
                selected = currentIdx;

            OpenSubmenu(SubmenuType.WorkMode, selected);
        }

        private void OpenAllowedAreaSubmenu(Pawn pawn)
        {
            List<Area> areas = MechsMenuHelper.GetAvailableAreas();
            submenuOptions.Clear();
            submenuOptions.Add(ManageAreasMarker);
            submenuOptions.Add(null); // unrestricted
            submenuOptions.AddRange(areas.Cast<object>());

            int selected = 1; // default: Unrestricted, not Manage Areas
            if (pawn.playerSettings?.AreaRestrictionInPawnCurrentMap != null)
            {
                for (int i = 0; i < areas.Count; i++)
                {
                    if (areas[i] == pawn.playerSettings.AreaRestrictionInPawnCurrentMap) { selected = i + 2; break; }
                }
            }
            OpenSubmenu(SubmenuType.AllowedArea, selected);
        }

        private void SubmenuMove(int delta)
        {
            if (submenuOptions.Count == 0)
                return;
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null)
                return;
            if (submenuTypeahead.HasActiveSearch && !submenuTypeahead.HasNoMatches)
            {
                int match = delta > 0 ? submenuTypeahead.GetNextMatch(region.Index) : submenuTypeahead.GetPreviousMatch(region.Index);
                if (match >= 0 && match < submenuOptions.Count)
                {
                    region.MoveTo(match);
                    AnnounceSubmenuWithSearch();
                }
                return;
            }
            int count = submenuOptions.Count;
            int next = ((region.Index + delta) % count + count) % count;
            region.MoveTo(next);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceSubmenuOption();
        }

        /// <summary>
        /// Applies the highlighted submenu option. The "already in group" / "already set"
        /// branches are real game-logic guards, not just wording: no mutation happens and the
        /// submenu stays open for another pick. Work Mode speaks its own richer phrase; Control
        /// Group and Allowed Area fall through to the shared <see cref="AfterCellEdit"/> tail.
        /// </summary>
        private void ApplySubmenuSelection(int index)
        {
            if (submenuMechRow < 0 || submenuMechRow >= mechsList.Count || submenuOptions.Count == 0)
            {
                CloseSubmenuInternal();
                return;
            }
            Pawn currentMech = mechsList[submenuMechRow];
            object selectedOption = index >= 0 && index < submenuOptions.Count ? submenuOptions[index] : null;

            switch (activeSubmenu)
            {
                case SubmenuType.ControlGroup:
                    if (selectedOption is MechanitorControlGroup group)
                    {
                        MechanitorControlGroup currentGroup = currentMech.GetMechControlGroup();
                        if (group == currentGroup)
                        {
                            TolkHelper.Speak("RimWorldAccess.Mechs.ControlGroup.AlreadyInGroup".Loc());
                            return;
                        }
                        group.Assign(currentMech);
                    }
                    break;

                case SubmenuType.WorkMode:
                    if (selectedOption is MechWorkModeDef mode)
                    {
                        MechanitorControlGroup mechGroup = currentMech.GetMechControlGroup();
                        if (mechGroup == null)
                            break;

                        if (mechGroup.WorkMode == mode)
                        {
                            TolkHelper.Speak("RimWorldAccess.Mechs.WorkMode.AlreadySet".Loc());
                            return;
                        }

                        mechGroup.SetWorkMode(mode);
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        CloseSubmenuInternal();
                        AfterCellEdit("RimWorldAccess.Mechs.WorkMode.SetForGroup".Translate(mode.LabelCap, mechGroup.Index).ToString());
                        return;
                    }
                    break;

                case SubmenuType.AllowedArea:
                    if (selectedOption is string marker && marker == ManageAreasMarker)
                    {
                        Map currentMap = Find.CurrentMap;
                        CloseSubmenuInternal();
                        if (currentMap != null)
                        {
                            // Vanilla's own Manage areas... delegate (AreaUtility); ManageAreasScope drives it.
                            Find.WindowStack.Add(new Dialog_ManageAreas(currentMap));
                        }
                        return; // already closed the submenu above
                    }
                    // The full vanilla gate (PawnColumnWorker_AllowedArea.DoCell:32-34), not just
                    // a null playerSettings check: a mech with no live overseer is refused here
                    // the same way vanilla's own cell would draw nothing for it.
                    if (!PawnColumnMutationHelper.CanEditAllowedArea(currentMech))
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        TolkHelper.Speak("RimWorldAccess.Mechs.Submenu.AreaNotAllowed".Loc(currentMech.LabelShort), SpeechPriority.High);
                        CloseSubmenuInternal();
                        return;
                    }
                    Area selectedArea = selectedOption as Area;
                    // MUTATION-C: mirrors PawnColumnWorker_AllowedArea.DoCell's own bare-field write
                    // (RimWorld/PawnColumnWorker_AllowedArea.cs:30-53); no gated setter exists on
                    // AreaRestrictionInPawnCurrentMap. The CanEditAllowedArea gate above already
                    // refused this branch for a pawn vanilla's own cell would draw nothing for.
                    currentMech.playerSettings.AreaRestrictionInPawnCurrentMap = selectedArea;
                    lastAppliedArea = selectedArea;
                    break;
            }

            SoundDefOf.Click.PlayOneShotOnCamera();
            CloseSubmenuInternal();
            AfterCellEdit();
        }

        private void CloseSubmenuInternal()
        {
            activeSubmenu = SubmenuType.None;
            submenuOptions.Clear();
            submenuTypeahead.ClearSearch();
            RefreshModel(); // rebuild the region back into table shape
            TableModel table = Model.CurrentTable;
            if (table != null && submenuMechRow >= 0 && submenuMechRow < mechsList.Count)
            {
                table.Rows.MoveTo(submenuMechRow + 1);
            }
            submenuMechRow = -1;
        }

        /// <summary>Instance method: needs <see cref="submenuMechRow"/> for the "current" marker.</summary>
        private string GetSubmenuOptionText(object option)
        {
            if (option is string s && s == ManageAreasMarker)
            {
                return "ManageAreas".Translate().Resolve();
            }
            if (option == null)
            {
                return "NoAreaAllowed".Translate().Resolve();
            }

            Pawn currentMech = submenuMechRow >= 0 && submenuMechRow < mechsList.Count ? mechsList[submenuMechRow] : null;

            if (option is MechanitorControlGroup group)
            {
                MechanitorControlGroup currentGroup = currentMech?.GetMechControlGroup();
                string label = group.LabelIndexWithWorkMode;
                return group == currentGroup
                    ? "RimWorldAccess.Mechs.Submenu.CurrentMarker".Translate(label).ToString()
                    : label;
            }
            if (option is MechWorkModeDef mode)
            {
                MechanitorControlGroup currentGroup = currentMech?.GetMechControlGroup();
                string label = mode.LabelCap.Resolve();
                return currentGroup?.WorkMode == mode
                    ? "RimWorldAccess.Mechs.Submenu.CurrentMarker".Translate(label).ToString()
                    : label;
            }
            if (option is Area area)
            {
                return area.Label;
            }
            return "RimWorldAccess.Mechs.Value.Unknown".Translate().ToString();
        }

        private void AnnounceSubmenuOption()
        {
            if (submenuOptions.Count == 0)
                return;
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            int index = region == null ? -1 : region.Index;
            if (index < 0 || index >= submenuOptions.Count)
                return;
            object option = submenuOptions[index];
            string optionText = GetSubmenuOptionText(option);
            string position = MenuHelper.FormatPosition(index, submenuOptions.Count);

            string announcement;
            if (option is MechWorkModeDef mode && !mode.description.NullOrEmpty())
                announcement = "RimWorldAccess.Mechs.Submenu.OptionWithDesc".Translate(optionText, mode.description, position).ToString();
            else
                announcement = "RimWorldAccess.Mechs.Submenu.OptionAnnounce".Translate(optionText, position).ToString();

            TolkHelper.SpeakData(announcement);
        }

        private void AnnounceSubmenuWithSearch()
        {
            if (submenuOptions.Count == 0)
                return;
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            int index = region == null ? -1 : region.Index;
            if (index < 0 || index >= submenuOptions.Count)
                return;
            string optionText = GetSubmenuOptionText(submenuOptions[index]);
            string position = MenuHelper.FormatPosition(index, submenuOptions.Count);
            string announcement = "RimWorldAccess.Mechs.Submenu.Option".Translate(optionText, position).ToString();
            if (submenuTypeahead.HasActiveSearch)
            {
                announcement += submenuTypeahead.BuildSearchContextSuffix();
            }
            TolkHelper.SpeakData(announcement);
        }

        // Painting. ControlGroup and AllowedArea carry their own paint semantics that do not
        // reduce to the generic boolean brush.

        /// <summary>Next data-row index for painting: search-match-aware, else a plain wrap.</summary>
        private int NextPaintRow(int currentRow, int delta)
        {
            int count = mechsList.Count;
            if (TypeaheadHasActiveSearch)
            {
                int matchRow = TypeaheadMatchRowInRegion(0, currentRow, delta);
                if (matchRow >= 0 && matchRow < count)
                    return matchRow;
            }
            return ((currentRow + delta) % count + count) % count;
        }

        private void PaintUp()
        {
            PaintSingle(-1);
        }

        private void PaintDown()
        {
            PaintSingle(1);
        }

        private void PaintSingle(int delta)
        {
            if (IsInSubmenu || mechsList.Count <= 1)
                return;
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table == null)
                return;
            int col = table.ColumnIndex;
            if (!MechsMenuHelper.CanPaintColumn(col))
            {
                MenuHelper.SpeakCannotPaintColumn();
                return;
            }

            int sourceRow = table.Rows.Index - 1;
            Pawn sourcePawn = mechsList[sourceRow];
            var columnType = MechsMenuHelper.GetColumnType(col);

            int targetRow = NextPaintRow(sourceRow, delta);
            if (targetRow < 0 || targetRow >= mechsList.Count)
                return;

            if (columnType == MechsMenuHelper.ColumnType.AllowedArea)
            {
                lastAppliedArea = sourcePawn.playerSettings?.AreaRestrictionInPawnCurrentMap;
                table.Rows.MoveTo(targetRow + 1);
                Pawn target = mechsList[targetRow];
                string areaName = lastAppliedArea?.Label ?? "NoAreaAllowed".Translate().Resolve();
                string position = MenuHelper.FormatPosition(targetRow, mechsList.Count);
                Area targetArea = target.playerSettings?.AreaRestrictionInPawnCurrentMap;
                if (targetArea == lastAppliedArea)
                {
                    TolkHelper.Speak("RimWorldAccess.Mechs.Paint.Single.AreaAlreadySet".Loc(target.LabelShort, areaName, position));
                }
                // The full vanilla gate, so the announcement never claims success for a write
                // vanilla itself would refuse (most commonly: no live overseer).
                else if (PawnColumnMutationHelper.CanEditAllowedArea(target))
                {
                    // MUTATION-C: mirrors PawnColumnWorker_AllowedArea.DoCell's own bare-field write
                    // (RimWorld/PawnColumnWorker_AllowedArea.cs:30-53); no gated setter exists on
                    // AreaRestrictionInPawnCurrentMap. Reached only after the CanEditAllowedArea gate above.
                    target.playerSettings.AreaRestrictionInPawnCurrentMap = lastAppliedArea;
                    SoundDefOf.Designate_DragStandard_Changed_NoCam.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.Mechs.Paint.Single.AreaApplied".Loc(target.LabelShort, areaName, position));
                }
                else
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.Mechs.Paint.Single.AreaNotAllowed".Loc(target.LabelShort, position));
                }
                return;
            }

            if (columnType == MechsMenuHelper.ColumnType.ControlGroup)
            {
                MechanitorControlGroup sourceGroup = sourcePawn.GetMechControlGroup();
                if (sourceGroup == null)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.Mechs.Paint.ControlGroup.NoGroupToPaint".Loc());
                    return;
                }

                table.Rows.MoveTo(targetRow + 1);
                Pawn target = mechsList[targetRow];
                string position = MenuHelper.FormatPosition(targetRow, mechsList.Count);

                if (target.IsGestating())
                {
                    TolkHelper.Speak("RimWorldAccess.Mechs.Paint.ControlGroup.GestatingCannotAssign".Loc(target.LabelShort, position));
                    return;
                }

                if (target.GetOverseer() != sourcePawn.GetOverseer())
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.Mechs.Paint.ControlGroup.DifferentOverseer".Loc(target.LabelShort, position));
                    return;
                }

                MechanitorControlGroup targetGroup = target.GetMechControlGroup();
                if (targetGroup == sourceGroup)
                {
                    TolkHelper.Speak("RimWorldAccess.Mechs.Paint.ControlGroup.AlreadyInGroup".Loc(target.LabelShort, sourceGroup.Index, position));
                    return;
                }

                sourceGroup.Assign(target);
                SoundDefOf.Click.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Mechs.Paint.ControlGroup.AssignedToGroup".Loc(target.LabelShort, sourceGroup.Index, position));
                return;
            }

            // Boolean columns (Draft, AutoRepair)
            bool brushValue = MechsMenuHelper.GetPaintableValue(sourcePawn, col);
            table.Rows.MoveTo(targetRow + 1);
            Pawn targetPawn = mechsList[targetRow];
            string colName = MechsMenuHelper.GetColumnName(col);
            string valueLabel = MechsMenuHelper.GetPaintValueLabel(col, brushValue);
            string pos = MenuHelper.FormatPosition(targetRow, mechsList.Count);

            bool targetValue = MechsMenuHelper.GetPaintableValue(targetPawn, col);
            if (targetValue == brushValue)
            {
                TolkHelper.Speak("RimWorldAccess.Mechs.Paint.Single.CellAlready".Loc(targetPawn.LabelShort, colName, valueLabel, pos));
                return;
            }
            bool applied = MechsMenuHelper.SetPaintableValue(targetPawn, col, brushValue);
            SoundDef sound = applied ? MechsMenuHelper.GetPaintSound(col, brushValue) : SoundDefOf.ClickReject;
            sound.PlayOneShotOnCamera();

            if (applied)
                TolkHelper.Speak("RimWorldAccess.Mechs.Paint.Single.CellApplied".Loc(targetPawn.LabelShort, colName, valueLabel, pos));
            else
                TolkHelper.Speak("RimWorldAccess.Mechs.Paint.Single.CannotApply".Loc(targetPawn.LabelShort, colName, pos));
        }

        private void PaintToLast()
        {
            PaintBulk(towardFirst: false, entireColumn: false);
        }

        private void PaintToFirst()
        {
            PaintBulk(towardFirst: true, entireColumn: false);
        }

        private void PaintEntireColumn(bool towardFirst)
        {
            PaintBulk(towardFirst, entireColumn: true);
        }

        private void PaintBulk(bool towardFirst, bool entireColumn)
        {
            if (IsInSubmenu || mechsList.Count == 0)
                return;
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table == null)
                return;
            int col = table.ColumnIndex;
            if (!MechsMenuHelper.CanPaintColumn(col))
            {
                MenuHelper.SpeakCannotPaintColumn();
                return;
            }

            var columnType = MechsMenuHelper.GetColumnType(col);
            int currentRow = table.Rows.Index - 1;
            string colName = MechsMenuHelper.GetColumnName(col);

            int startRow, endRow;
            if (entireColumn)
            {
                startRow = 0;
                endRow = mechsList.Count - 1;
            }
            else if (towardFirst)
            {
                startRow = 0;
                endRow = currentRow;
            }
            else
            {
                startRow = currentRow;
                endRow = mechsList.Count - 1;
            }

            if (columnType == MechsMenuHelper.ColumnType.AllowedArea)
            {
                Pawn source = mechsList[currentRow];
                lastAppliedArea = source.playerSettings?.AreaRestrictionInPawnCurrentMap;
                string areaName = lastAppliedArea?.Label ?? "NoAreaAllowed".Translate().Resolve();

                var changedNames = new List<string>();
                for (int i = startRow; i <= endRow; i++)
                {
                    Pawn pawn = mechsList[i];
                    // Full vanilla gate (see PaintSingle's AllowedArea branch above).
                    if (!PawnColumnMutationHelper.CanEditAllowedArea(pawn)) continue;
                    if (pawn.playerSettings.AreaRestrictionInPawnCurrentMap != lastAppliedArea)
                    {
                        // MUTATION-C: mirrors PawnColumnWorker_AllowedArea.DoCell's own bare-field write
                        // (RimWorld/PawnColumnWorker_AllowedArea.cs:30-53); no gated setter exists on
                        // AreaRestrictionInPawnCurrentMap. Reached only after the CanEditAllowedArea gate above.
                        pawn.playerSettings.AreaRestrictionInPawnCurrentMap = lastAppliedArea;
                        changedNames.Add(pawn.LabelShort);
                    }
                }
                table.Rows.MoveTo((towardFirst ? startRow : endRow) + 1);

                if (changedNames.Count > 0)
                {
                    BulkSoundQueue.Queue(changedNames.Count, SoundDefOf.Designate_DragStandard_Changed_NoCam);
                    TolkHelper.Speak("RimWorldAccess.Mechs.Paint.Bulk.AreaApplied".Loc(colName, areaName, MenuHelper.FormatNameList(changedNames)));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Mechs.Paint.Bulk.AreaAlreadyAll".Loc(areaName));
                }
                return;
            }

            if (columnType == MechsMenuHelper.ColumnType.ControlGroup)
            {
                Pawn source = mechsList[currentRow];
                MechanitorControlGroup sourceGroup = source.GetMechControlGroup();
                if (sourceGroup == null)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.Mechs.Paint.ControlGroup.NoGroupToPaint".Loc());
                    return;
                }
                Pawn sourceOverseer = source.GetOverseer();

                var changedNames = new List<string>();
                for (int i = startRow; i <= endRow; i++)
                {
                    Pawn pawn = mechsList[i];
                    if (pawn.IsGestating()) continue;
                    if (pawn.GetOverseer() != sourceOverseer) continue;
                    if (pawn.GetMechControlGroup() == sourceGroup) continue;
                    sourceGroup.Assign(pawn);
                    changedNames.Add(pawn.LabelShort);
                }

                table.Rows.MoveTo((towardFirst ? startRow : endRow) + 1);
                BulkSoundQueue.Queue(changedNames.Count, SoundDefOf.Click);

                if (changedNames.Count > 0)
                    TolkHelper.Speak("RimWorldAccess.Mechs.Paint.Bulk.GroupApplied".Loc(colName, sourceGroup.Index, MenuHelper.FormatNameList(changedNames)));
                else
                    TolkHelper.Speak("RimWorldAccess.Mechs.Paint.Bulk.GroupAlreadyAll".Loc(sourceGroup.Index));
                return;
            }

            // Boolean columns (Draft, AutoRepair)
            Pawn brushPawn = mechsList[currentRow];
            bool brushValue = MechsMenuHelper.GetPaintableValue(brushPawn, col);
            string valueLabel = MechsMenuHelper.GetPaintValueLabel(col, brushValue);

            var changed = new List<string>();
            for (int i = startRow; i <= endRow; i++)
            {
                Pawn pawn = mechsList[i];
                bool current = MechsMenuHelper.GetPaintableValue(pawn, col);
                if (current != brushValue)
                {
                    if (MechsMenuHelper.SetPaintableValue(pawn, col, brushValue))
                        changed.Add(pawn.LabelShort);
                }
            }

            table.Rows.MoveTo((towardFirst ? startRow : endRow) + 1);
            SoundDef bulkSound = MechsMenuHelper.GetPaintSound(col, brushValue);
            BulkSoundQueue.Queue(changed.Count, bulkSound);

            if (changed.Count > 0)
                TolkHelper.Speak("RimWorldAccess.Mechs.Paint.Bulk.CellApplied".Loc(colName, valueLabel, MenuHelper.FormatNameList(changed)));
            else
                TolkHelper.Speak("RimWorldAccess.Mechs.Paint.Bulk.CellAlreadyAll".Loc(colName, valueLabel));
        }

        // ------------------------------------------------------------------
        // IPawnTableFocusSource.
        // ------------------------------------------------------------------

        /// <summary>Alt+Shift+J routes on the real vanilla table drawing underneath, which this scope reads but was never attached to.</summary>
        protected override Window PointerSurface
        {
            get { return PawnTableFocusDriver.OpenTabWindowFor(this); }
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            PawnTableExtrasRouter.AddRouteCandidates(
                0, mechsList, ContentColumnCount(0), MechsMenuHelper.GetDef, candidates, targets);
        }

        Type IPawnTableFocusSource.TabWindowType
        {
            get { return typeof(MainTabWindow_Mechs); }
        }

        Pawn IPawnTableFocusSource.FocusedTablePawn
        {
            get { return CurrentRowPawn(); }
        }

        PawnColumnDef IPawnTableFocusSource.FocusedTableColumn
        {
            get
            {
                TableModel table = Model.CurrentTable;
                return table != null ? MechsMenuHelper.GetDef(table.ColumnIndex) : null;
            }
        }

        bool IPawnTableFocusSource.FocusedOnHeaderRow
        {
            get
            {
                TableModel table = Model.CurrentTable;
                return table != null && table.Rows.Index == 0;
            }
        }

        // ------------------------------------------------------------------
        // Close.
        // ------------------------------------------------------------------

        private void Close()
        {
            MechsMenuState.Close();
        }
    }

    /// <summary>
    /// Keeps <see cref="MechsScope"/> in lockstep with
    /// <see cref="MechsMenuState.IsActive"/>, reconciled every OnGUI pass
    /// AFTER <see cref="GizmoScopeMirror"/> (verified F4-over-gizmo
    /// coexistence — see the pre-migration history for the evidence).
    ///
    /// Stands down (pops) while an info card is open (Alt+I on a mech), while
    /// an input-owning dialog sits above (the Manage-Areas drill-in —
    /// MechsMenuState.IsActive stays true throughout that flow), and while
    /// <see cref="ShapePlacementState"/> or <see cref="ViewingModeState"/> is
    /// active (the area-shape placement that follows Manage Areas).
    /// </summary>
    internal static class MechsScopeMirror
    {
        private static readonly MechsScope scope = new MechsScope();

        public static void Reconcile()
        {
            if (MechsMenuState.IsActive
                && !InfoCardState.IsActive
                && !ShapePlacementState.IsActive
                && !ViewingModeState.IsActive
                && !ShellGuards.ForeignDialogWindowAbove())
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }
}
