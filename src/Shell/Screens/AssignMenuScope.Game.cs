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
    /// Keyboard focus scope for the colonist assignment table (vanilla's Assign tab: every colonist,
    /// tabular by outfit/food/drug/reading-policy columns). The mod owns no window — the surface is
    /// the windowless <see cref="AssignMenuState"/> state machine — so the scope rides the focus
    /// stack through <see cref="AssignMenuScopeMirror"/>.
    ///
    /// ONE table region ("Assign") on the <see cref="ScreenScope"/> TABLE REGION contract, with no
    /// Buttons region (vanilla's Assign tab has no bottom buttons). Column set/order/headers and
    /// every cell's value, tooltip and menu come from vanilla's own Assign
    /// <see cref="PawnTableDef"/> (<see cref="PawnTableDefOf.Assign"/>) through the shared
    /// <see cref="PawnColumnHandlerRegistry"/>; Enter on a policy-like column opens a real
    /// <see cref="WindowlessFloatMenuState"/> populated by the worker's own menu generator
    /// (vehicle A). Two accepted consequences of that shared vehicle: the float menu opens at its
    /// first entry rather than the pawn's current value, and an actively sorted column keeps its old
    /// order until the next explicit sort.
    ///
    /// Policy shortcuts (Alt+N/R/C/E, Delete) and the <c>]</c> context menu always read the current
    /// column/row and ride <see cref="AssignMenuHelper.GetContextMenuOptions"/>/
    /// <see cref="AssignMenuHelper.ExecutePolicyAction"/>, which use vanilla's own database Try*/
    /// SetDefault/CopyFrom methods and its Dialog_Confirm delete gate.
    ///
    /// Policy-editor round trip (Alt+E, or the context menu's Edit option, on Outfit/Food/Drug/
    /// Reading): <see cref="PrepareForPolicyEditorReturn"/> captures the row/column to restore and
    /// relies entirely on <see cref="AssignMenuScopeMirror"/>'s
    /// <c>!ShellGuards.ForeignDialogWindowAbove()</c> gate to re-push this scope when the real
    /// policy window closes; <see cref="OnPush"/>/<see cref="OnFocus"/> detect the pending restore
    /// and skip the fresh-open reset.
    ///
    /// <c>AssignMenuState.IsActive</c> stays TRUE while a policy window or info card is open on top,
    /// so the mirror carries explicit stand-down gates rather than relying on stack masking alone.
    ///
    /// The real <see cref="MainTabWindow_Assign"/> stays open and rendered, and this scope
    /// implements <see cref="IPawnTableFocusSource"/> to supply the focused (pawn, column) cell to
    /// <see cref="PawnTableFocusDriver"/>, which rings it on that window.
    /// </summary>
    public sealed class AssignMenuScope : ScreenScope, IPawnTableFocusSource
    {
        private readonly List<Pawn> pawnsList = new List<Pawn>();
        private List<Pawn> defaultOrder;

        private bool pendingPolicyEditorReturn;
        private int pendingReturnRow;
        private int pendingReturnColumn;

        private bool announcedOpen;

        public AssignMenuScope()
        {
            Claim(SharedMenuGrammar.Cancel, delegate { HandleCancelKey(); });

            // Policy shortcuts and the context menu always read the current main-table row/column.
            Claim("assign.policyNew", delegate { HandlePolicyShortcut(AssignMenuHelper.PolicyAction.New); });
            Claim("assign.policyRename", delegate { HandlePolicyShortcut(AssignMenuHelper.PolicyAction.Rename); });
            Claim("assign.policyCopy", delegate { HandlePolicyShortcut(AssignMenuHelper.PolicyAction.Copy); });
            Claim("assign.policyEdit", delegate { HandlePolicyShortcut(AssignMenuHelper.PolicyAction.Edit); });
            Claim("assign.policyDelete", delegate { HandlePolicyShortcut(AssignMenuHelper.PolicyAction.Delete); });
            Claim("assign.contextMenu", delegate { OpenContextMenu(); });

            Claim("assign.infoCard", delegate { OpenInfoCard(); });
            Claim("assign.paintUp", delegate { PaintSingle(-1); });
            Claim("assign.paintDown", delegate { PaintSingle(1); });
            Claim("assign.paintToFirst", delegate { PaintToFirst(); });
            Claim("assign.paintToLast", delegate { PaintToLast(); });
            Claim("assign.paintEntireColumn",
                delegate (KeyEventSnapshot e) { PaintEntireColumn(e.Key == KeyCode.Home); });
        }

        public override string Name
        {
            get { return "assign"; }
        }

        /// <summary>Shared cross-region typeahead.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>No owning <c>Window</c> exists for this windowless overlay — self-claim Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        // One region, one table: rows are colonists in vanilla display order, columns are
        // AssignMenuHelper.BuildActiveColumns's mirror of vanilla's own mod-gated column set.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Reuses vanilla's own Assign tab label — no new translation key.</summary>
        protected override string ContentRegionName(int region)
        {
            MainButtonDef def = DefDatabase<MainButtonDef>.GetNamedSilentFail("Assign");
            return def != null ? def.LabelCap.Resolve() : "Assign";
        }

        protected override int ContentColumnCount(int region)
        {
            return AssignMenuHelper.GetColumnCount();
        }

        protected override int ContentItemCount(int region)
        {
            return pawnsList.Count;
        }

        /// <summary>
        /// One-time population: the live colonist roster is snapshotted here and never silently
        /// re-fetched, since re-querying every refresh would discard an active sort or the cursor
        /// position. Rebuilds only when the cached list is empty.
        /// </summary>
        protected override void RefreshContent()
        {
            if (pawnsList.Count > 0)
                return;

            AssignMenuHelper.BuildActiveColumns();
            List<Pawn> initial = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists
                .Where(p => !p.DevelopmentalStage.Baby())
                .ToList();
            if (initial.Count == 0)
                return;

            // Vanilla's default order: the colonist-bar display order.
            List<Pawn> ordered = PlayerPawnsDisplayOrderUtility.InOrder(initial).ToList();
            pawnsList.AddRange(ordered);
            defaultOrder = new List<Pawn>(pawnsList);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index >= 0 && index < pawnsList.Count)
            {
                d.Label = pawnsList[index].LabelShort;
            }
            return d;
        }

        /// <summary>Row default (Enter with no cell action): the display-only Ideo/Xenotype columns say they are read only.</summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (index < 0 || index >= pawnsList.Count)
                return;
            AnnounceCurrentCellReadOnly();
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            return new TableColumnInfo(
                AssignMenuHelper.GetColumnName(column),
                AssignMenuHelper.GetColumnHeaderTooltip(column),
                AssignMenuHelper.IsColumnSortable(column));
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (row < 0 || row >= pawnsList.Count)
                return "";
            return AssignMenuHelper.GetColumnValue(pawnsList[row], column);
        }

        /// <summary>The worker's own per-cell tooltip, via the registry.</summary>
        protected override string ContentCellTip(int region, int row, int column)
        {
            if (row < 0 || row >= pawnsList.Count)
                return null;
            return AssignMenuHelper.GetCellTip(pawnsList[row], column);
        }

        protected override string ContentCellHint(int region, int row, int column)
        {
            if (row < 0 || row >= pawnsList.Count)
                return null;
            return PawnColumnCellReader.CellHint(AssignMenuHelper.GetColumnDef(column), pawnsList[row]);
        }

        /// <summary>
        /// Enter on an interactive column: Name jumps to the pawn and closes the menu (this scope's
        /// own behavior, not the registry's select-and-jump-in-place); Ideo/Xenotype fall back to
        /// the row default; every other column dispatches to
        /// <see cref="AssignMenuHelper.ActivateCell"/> — vanilla's own menu generator, opened as a
        /// real float menu.
        /// </summary>
        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (row < 0 || row >= pawnsList.Count)
                return false;

            Pawn pawn = pawnsList[row];

            if (AssignMenuHelper.IsNameColumn(column))
            {
                JumpToPawnOnMap(pawn);
                return true;
            }
            if (AssignMenuHelper.IsDisplayOnlyColumn(column))
                return false; // Ideo/Xenotype -> row default (read only).

            PawnTable table = PawnColumnMutationHelper.CreateDetachedTable(PawnTableDefOf.Assign);
            AssignMenuHelper.ActivationOutcome outcome = AssignMenuHelper.ActivateCell(pawn, column, table, out string blockedReason);
            switch (outcome)
            {
                case AssignMenuHelper.ActivationOutcome.ReadOnly:
                    TolkHelper.SpeakData($"{pawn.LabelShort}: {blockedReason}", SpeechPriority.High);
                    return true;
                case AssignMenuHelper.ActivationOutcome.NotApplicable:
                    TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.NotApplicable".Loc(), SpeechPriority.High);
                    return true;
                case AssignMenuHelper.ActivationOutcome.StateChanged:
                    AfterCellEdit();
                    return true;
                case AssignMenuHelper.ActivationOutcome.OpenedUI:
                    return true; // the float menu announces itself.
                default:
                    return false;
            }
        }

        /// <summary>
        /// Re-orders for a new sort state using the game's own column comparers
        /// (PawnColumnWorker.Compare via PawnColumnSortHelper); "cleared" restores the display-order
        /// default captured at open.
        /// </summary>
        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            Pawn currentPawn = currentRow >= 0 && currentRow < pawnsList.Count ? pawnsList[currentRow] : null;
            List<Pawn> reordered = cycle == SortCycleResult.Cleared
                ? new List<Pawn>(defaultOrder ?? pawnsList)
                : AssignMenuHelper.SortByColumn(pawnsList, column, cycle == SortCycleResult.SortedDescending);

            pawnsList.Clear();
            pawnsList.AddRange(reordered);

            if (currentPawn == null)
                return 0;
            int index = pawnsList.IndexOf(currentPawn);
            return index >= 0 ? index : 0;
        }

        /// <summary>Vanilla's Assign tab has no bottom buttons — no Buttons region.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        // Lifecycle.

        public override void OnPush()
        {
            base.OnPush();
            if (pendingPolicyEditorReturn)
            {
                // Returning from a policy editor: pawnsList/columns/sort must survive the round
                // trip untouched; OnFocus below consumes the pending restore.
                return;
            }
            pawnsList.Clear();
            defaultOrder = null;
            TypeaheadReset();
            announcedOpen = false;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (pendingPolicyEditorReturn)
            {
                pendingPolicyEditorReturn = false;
                TableModel table = Model.CurrentTable;
                if (table != null && pawnsList.Count > 0)
                {
                    int row = Math.Min(pendingReturnRow, pawnsList.Count - 1);
                    int col = Math.Min(pendingReturnColumn, table.ColumnCount - 1);
                    if (row >= 0) table.Rows.MoveTo(row + 1);
                    if (col >= 0) table.MoveToColumn(col);
                }
                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                AnnounceCurrentItem();
                return;
            }
            if (announcedOpen)
                return;
            announcedOpen = true;
            if (pawnsList.Count == 0)
                return; // AssignMenuState.Open() already refused to open/announce for an empty roster.

            TableModel initialTable = Model.CurrentTable;
            if (initialTable != null)
            {
                Pawn selectedPawn = PawnSelectionState.LastSelectedPawn;
                if (selectedPawn != null)
                {
                    int idx = pawnsList.IndexOf(selectedPawn);
                    if (idx >= 0)
                        initialTable.Rows.MoveTo(idx + 1);
                }
                if (ScreenReturn.TryTakeCursor(Name, out TableReturnCursor saved))
                    saved.LandOn(initialTable, pawnsList);
            }

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            string tabLabel = DefDatabase<MainButtonDef>.GetNamed("Assign").LabelCap;
            TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.Opened".Loc(tabLabel, pawnsList.Count));
            AnnounceCurrentItem();
        }

        // The main table rides the base ScreenScope cross-region typeahead unmodified.

        private void HandleCancelKey()
        {
            // Escape-clears-search is the base typeahead claim (registered ahead of this one), so
            // reaching here means no active search.
            Close();
        }

        // Cell actions.

        private void OpenInfoCard()
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            int row = table == null ? -1 : table.Rows.Index - 1;
            Pawn pawn = row >= 0 && row < pawnsList.Count ? pawnsList[row] : null;
            if (pawn != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(pawn));
            }
            else
            {
                InfoCardState.SpeakNoInfoCardAvailable();
            }
        }

        private void JumpToPawnOnMap(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.PawnNotOnMap".Loc(), SpeechPriority.High);
                return;
            }
            IntVec3 position = pawn.Position;
            ScreenReturn.ArmMainTabTable(Name, MainTabWindowLink.Assign, pawn, Model.CurrentTable);
            Close();
            MapNavigationState.CurrentCursorPosition = position;
            Find.CameraDriver?.JumpToCurrentMapLoc(position);
            MapNavigationState.SpeakJumpedTo(pawn.LabelShort);
        }

        /// <summary>
        /// Re-sorts in place when the table is actively sorted by the just-edited column, keeping
        /// the cursor at the same ROW POSITION; otherwise speaks the edited cell through the shared
        /// composer (one announcement, the fresh value only).
        /// </summary>
        private void AfterCellEdit()
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table != null && table.HasActiveSort && table.SortColumnIndex == table.ColumnIndex)
            {
                List<Pawn> resorted = AssignMenuHelper.SortByColumn(pawnsList, table.ColumnIndex, table.SortDescending);
                pawnsList.Clear();
                pawnsList.AddRange(resorted);
                int currentRow = table.Rows.Index - 1;
                int clamped = Math.Min(currentRow, pawnsList.Count - 1);
                if (clamped >= 0)
                {
                    table.Rows.MoveTo(clamped + 1);
                    string cellText = BuildCellText(clamped);
                    TolkHelper.SpeakData("RimWorldAccess.Pawns.AssignMenu.ResortNowAt".Translate(cellText).ToString());
                    return;
                }
            }
            AnnounceCurrentCellStateChange();
        }

        /// <summary>"{pawn} - {column}: {value}" (or just the value when it echoes the pawn name).</summary>
        private string BuildCellText(int row)
        {
            if (row < 0 || row >= pawnsList.Count)
                return "";
            Pawn pawn = pawnsList[row];
            int column = Model.CurrentTable?.ColumnIndex ?? 0;
            string columnValue = AssignMenuHelper.GetColumnValue(pawn, column);
            string itemLabel = pawn.LabelShort;
            if (itemLabel == columnValue || columnValue.StartsWith(itemLabel))
                return columnValue;
            string columnName = AssignMenuHelper.GetColumnName(column);
            return $"{itemLabel} - {columnName}: {columnValue}";
        }

        // Policy shortcuts (Alt+N/R/C/E, Delete) and context menu.

        private bool HandlePolicyShortcut(AssignMenuHelper.PolicyAction action)
        {
            if (pawnsList.Count == 0)
                return false;

            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table == null)
                return false;
            int column = table.ColumnIndex;
            int row = table.Rows.Index - 1;
            if (row < 0 || row >= pawnsList.Count)
                return false;
            Pawn pawn = pawnsList[row];
            if (!AssignMenuHelper.IsColumnPolicyType(column))
                return false;

            Action editCallback = BuildEditCallback(column, pawn, null);
            Action refreshCallback = delegate { AnnounceCurrentCellStateChange(); };
            return AssignMenuHelper.ExecutePolicyAction(action, column, pawn, refreshCallback, editCallback);
        }

        private void OpenContextMenu()
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table == null || pawnsList.Count == 0)
                return;
            int column = table.ColumnIndex;
            if (!AssignMenuHelper.HasContextMenu(column))
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.NoContextMenuColumn".Loc());
                return;
            }
            int row = table.Rows.Index - 1;
            if (row < 0 || row >= pawnsList.Count)
                return;
            Pawn pawn = pawnsList[row];

            Action editCallback = BuildEditCallback(column, pawn, null);
            Action refreshCallback = delegate { AnnounceCurrentCellStateChange(); };
            List<FloatMenuOption> options = AssignMenuHelper.GetContextMenuOptions(column, pawn, refreshCallback, editCallback);
            if (options == null || options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.NoContextMenuColumn".Loc());
                return;
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        /// <summary>
        /// Builds the Edit action for a policy column (Outfit/Food/Drug/Reading only — the other
        /// policy-like columns have no dedicated editor dialog). Opens the REAL vanilla management
        /// window, exactly what vanilla's own "Edit..." cell option does (e.g.
        /// <c>PawnColumnWorker_FoodRestriction.cs:44-51</c>), so the player gets the policy list as
        /// well as its contents — see <see cref="PolicyDialogScope"/>.
        /// </summary>
        private Action BuildEditCallback(int column, Pawn pawn, Policy policyOverride)
        {
            PawnColumnDef def = AssignMenuHelper.GetColumnDef(column);
            Func<Policy, Window> openDialog = null;
            Policy policy = policyOverride;

            if (def?.Worker is PawnColumnWorker_Outfit)
            {
                policy = policy ?? pawn.outfits?.CurrentApparelPolicy;
                openDialog = p => new Dialog_ManageApparelPolicies((ApparelPolicy)p);
            }
            else if (def?.Worker is PawnColumnWorker_FoodRestriction)
            {
                policy = policy ?? pawn.foodRestriction?.CurrentFoodPolicy;
                openDialog = p => new Dialog_ManageFoodPolicies((FoodPolicy)p);
            }
            else if (def?.Worker is PawnColumnWorker_DrugPolicy)
            {
                policy = policy ?? pawn.drugs?.CurrentPolicy;
                openDialog = p => new Dialog_ManageDrugPolicies((DrugPolicy)p);
            }
            else if (def?.Worker is PawnColumnWorker_Reading)
            {
                policy = policy ?? pawn.reading?.CurrentPolicy;
                openDialog = p => new Dialog_ManageReadingPolicies((ReadingPolicy)p);
            }
            else if (def?.Worker is PawnColumnWorker_CombatPolicy)
            {
                policy = policy ?? PawnColumnWorker_CombatPolicy.CurrentPolicy(pawn);
                openDialog = p => new Dialog_ManageCombatPolicies((CombatPolicy)p);
            }
            else if (def?.Worker is PawnColumnWorker_HuntPolicy)
            {
                policy = policy ?? CombatAutopilotComponent.EffectiveHuntPolicy(pawn);
                openDialog = p => new Dialog_ManageHuntingPolicies((HuntingPolicy)p);
            }

            if (openDialog == null || policy == null)
                return null;

            Policy capturedPolicy = policy;
            Func<Policy, Window> capturedOpen = openDialog;
            return delegate
            {
                PrepareForPolicyEditorReturn();
                Find.WindowStack.Add(capturedOpen(capturedPolicy));
            };
        }

        /// <summary>Captures the main table's row/column to restore when a policy editor closes.</summary>
        private void PrepareForPolicyEditorReturn()
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            pendingPolicyEditorReturn = true;
            pendingReturnRow = table != null ? table.Rows.Index - 1 : 0;
            pendingReturnColumn = table != null ? table.ColumnIndex : 0;
        }

        // Paint (Shift+Up/Down single-cell, Shift+Home/End bulk-to-edge, Ctrl+Shift+Home/End entire
        // column), search-match-aware like row navigation.

        private int NextPaintRow(int currentRow, int delta)
        {
            int count = pawnsList.Count;
            if (TypeaheadHasActiveSearch)
            {
                int matchRow = TypeaheadMatchRowInRegion(0, currentRow, delta);
                if (matchRow >= 0 && matchRow < count)
                    return matchRow;
            }
            return ((currentRow + delta) % count + count) % count;
        }

        private void PaintSingle(int delta)
        {
            if (pawnsList.Count <= 1)
                return;
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table == null)
                return;
            int col = table.ColumnIndex;
            if (!AssignMenuHelper.CanPaintColumn(col))
            {
                MenuHelper.SpeakCannotPaintColumn();
                return;
            }

            int sourceRow = table.Rows.Index - 1;
            Pawn sourcePawn = pawnsList[sourceRow];
            string sourceValue = AssignMenuHelper.GetColumnValue(sourcePawn, col);

            int targetRow = NextPaintRow(sourceRow, delta);
            if (targetRow < 0 || targetRow >= pawnsList.Count)
                return;
            table.Rows.MoveTo(targetRow + 1);
            Pawn targetPawn = pawnsList[targetRow];
            string targetValue = AssignMenuHelper.GetColumnValue(targetPawn, col);
            string position = MenuHelper.FormatPosition(targetRow, pawnsList.Count);

            if (!AssignMenuHelper.CanAssignValue(targetPawn, col))
            {
                TolkHelper.SpeakData($"{targetPawn.LabelShort}: {AssignMenuHelper.GetReadOnlyReason(col)}. {position}", SpeechPriority.High);
                return;
            }

            if (sourceValue == targetValue)
            {
                string colName = AssignMenuHelper.GetColumnName(col);
                TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.AlreadyAt".Loc(targetPawn.LabelShort, colName, targetValue, position));
                return;
            }

            bool applied = AssignMenuHelper.ApplyValueToPawn(sourcePawn, targetPawn, col);
            SoundDef appliedSound = applied ? SoundDefOf.Click : SoundDefOf.ClickReject;
            appliedSound.PlayOneShotOnCamera();
            if (applied)
            {
                string value = AssignMenuHelper.GetColumnValue(targetPawn, col);
                TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.AppliedAt".Loc(targetPawn.LabelShort, value, position));
            }
            else
            {
                string colName = AssignMenuHelper.GetColumnName(col);
                TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.NotAppliedAt".Loc(targetPawn.LabelShort, colName, position));
            }
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
            if (pawnsList.Count == 0)
                return;
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table == null)
                return;
            int col = table.ColumnIndex;
            if (!AssignMenuHelper.CanPaintColumn(col))
            {
                MenuHelper.SpeakCannotPaintColumn();
                return;
            }

            int currentRow = table.Rows.Index - 1;
            Pawn sourcePawn = pawnsList[currentRow];
            string sourceValue = AssignMenuHelper.GetColumnValue(sourcePawn, col);
            string colName = AssignMenuHelper.GetColumnName(col);

            int startRow, endRow;
            if (entireColumn)
            {
                startRow = 0;
                endRow = pawnsList.Count - 1;
            }
            else if (towardFirst)
            {
                startRow = 0;
                endRow = currentRow;
            }
            else
            {
                startRow = currentRow;
                endRow = pawnsList.Count - 1;
            }

            var changed = new List<string>();
            int skippedNotOffered = 0;
            for (int i = startRow; i <= endRow; i++)
            {
                Pawn target = pawnsList[i];
                if (!AssignMenuHelper.CanAssignValue(target, col))
                {
                    continue;
                }
                string beforeValue = AssignMenuHelper.GetColumnValue(target, col);
                if (beforeValue != sourceValue)
                {
                    if (AssignMenuHelper.ApplyValueToPawn(sourcePawn, target, col))
                        changed.Add(target.LabelShort);
                    else
                        skippedNotOffered++;
                }
            }
            table.Rows.MoveTo((towardFirst ? startRow : endRow) + 1);

            if (changed.Count > 0)
            {
                BulkSoundQueue.Queue(changed.Count, SoundDefOf.Click);
                if (skippedNotOffered > 0)
                    TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.PaintedToWithSkips".Loc(colName, sourceValue, MenuHelper.FormatNameList(changed), skippedNotOffered));
                else
                    TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.PaintedTo".Loc(colName, sourceValue, MenuHelper.FormatNameList(changed)));
            }
            else if (skippedNotOffered > 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.NotOfferedAll".Loc(colName, sourceValue, skippedNotOffered));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.AllAlready".Loc(colName, sourceValue));
            }
        }

        // IPawnTableFocusSource.

        /// <summary>Alt+Shift+J routes on the real vanilla table drawing underneath, which this scope reads but was never attached to.</summary>
        protected override Window PointerSurface
        {
            get { return PawnTableFocusDriver.OpenTabWindowFor(this); }
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            PawnTableExtrasRouter.AddRouteCandidates(
                0, pawnsList, ContentColumnCount(0), AssignMenuHelper.GetColumnDef, candidates, targets);
        }

        Type IPawnTableFocusSource.TabWindowType
        {
            get { return typeof(MainTabWindow_Assign); }
        }

        Pawn IPawnTableFocusSource.FocusedTablePawn
        {
            get
            {
                TableModel table = Model.CurrentTable;
                int row = table != null ? table.Rows.Index - 1 : -1;
                return row >= 0 && row < pawnsList.Count ? pawnsList[row] : null;
            }
        }

        PawnColumnDef IPawnTableFocusSource.FocusedTableColumn
        {
            get
            {
                TableModel table = Model.CurrentTable;
                return table != null ? AssignMenuHelper.GetColumnDef(table.ColumnIndex) : null;
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

        // Close / external bridge.

        private void Close()
        {
            AssignMenuState.Close();
        }

        /// <summary>
        /// Bridge target for <see cref="AssignMenuState.AnnounceCurrentCell"/>, which
        /// <c>FloatMenuOverlayScope</c>'s Escape handler calls (load-bearing) after closing the
        /// <c>]</c> context menu.
        /// </summary>
        internal void AnnounceCurrentCellExternally(bool includeItemName)
        {
            RefreshModel();
            if (Model.CurrentTable == null)
                return;
            AnnounceCurrent(includeItemName ? CellAxis.Entry : CellAxis.Column);
        }
    }

    /// <summary>
    /// Keeps <see cref="AssignMenuScope"/> in lockstep with <see cref="AssignMenuState.IsActive"/>,
    /// reconciled every OnGUI pass AFTER <see cref="GizmoScopeMirror"/> so mouse-click-over-gizmo
    /// coexistence holds. Stands down while an info card is open, and while the real policy window
    /// is open — <c>AssignMenuState.IsActive</c> stays true the whole time that window is on top.
    /// </summary>
    internal static class AssignMenuScopeMirror
    {
        private static readonly AssignMenuScope scope = new AssignMenuScope();

        public static void Reconcile()
        {
            // ForeignDialogWindowAbove covers both the delete-policy confirmation (vanilla's
            // Dialog_Confirm, raised while AssignMenuState stays active) and policy editing:
            // Dialog_ManagePolicies sets absorbInputAroundWindow and carries an attached scope, so
            // it trips this predicate without a gate of its own.
            if (AssignMenuState.IsActive
                && !InfoCardState.IsActive
                && !ShellGuards.ForeignDialogWindowAbove())
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }

        /// <summary>Bridge for <see cref="AssignMenuState.AnnounceCurrentCell"/> (load-bearing).</summary>
        internal static void AnnounceCurrentCellExternally(bool includeItemName)
        {
            scope.AnnounceCurrentCellExternally(includeItemName);
        }
    }
}
