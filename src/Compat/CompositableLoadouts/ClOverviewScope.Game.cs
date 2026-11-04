using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Compositable Loadouts' overview main tab (<c>MainTabWindow_LoadoutOverview</c>). Regions: the
    /// pawn table in the window's own filtered and sorted order, the search box, and the stats panel
    /// while the collapse strip shows it. Table columns follow the row: name, state dropdown, one
    /// column per tag chip (as many as the longest row has), add tag, copy, paste once something was
    /// copied, edit. The toolbar's scope and view-as buttons and the selection bar are the window's
    /// own text buttons, captured into the Buttons region and clicked; the collapse strip is a
    /// declared toggle there. Space, Ctrl+Space and Shift+Space are the name's click, Ctrl+click and
    /// Shift+click; cut, copy and paste on a chip stand in for dragging it, Ctrl held copying.
    /// </summary>
    internal sealed class ClOverviewScope : ScreenScope
    {
        private const int TableRegion = 0;
        private const int SearchRegion = 1;
        private const int StatsRegion = 2;

        private const int SortByName = 0;
        private const int SortByState = 1;
        private const int SortByTags = 2;

        private const string SelectActionId = "clOverview.select";
        private const string ToggleActionId = "clOverview.toggleSelection";
        private const string RangeActionId = "clOverview.selectRange";
        private const string RemoveActionId = "clOverview.removeTag";
        private const string ContextActionId = "clOverview.contextMenu";
        private const string CutActionId = "clOverview.cut";
        private const string CopyActionId = "clOverview.copy";
        private const string PasteActionId = "clOverview.paste";

        private enum Column
        {
            Name,
            State,
            Chip,
            Add,
            Copy,
            Paste,
            Edit,
        }

        private enum StatsRowKind
        {
            Previous,
            Next,
            Text,
        }

        private sealed class StatsRow
        {
            public StatsRowKind Kind;
            public string Label;
            public string Value;
            public string Extras;
        }

        private readonly Window window;
        private readonly List<Pawn> pawns = new List<Pawn>();
        private readonly List<StatsRow> statsRows = new List<StatsRow>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private readonly TextFieldEditSession searchSession = new TextFieldEditSession();
        private int chipColumns;
        private bool pasteColumn;

        private Pawn pickupPawn;
        private object pickupElement;
        private bool pickupCopy;

        private readonly Dictionary<Pawn, Rect> rowRects = new Dictionary<Pawn, Rect>();
        private readonly Dictionary<Pawn, Rect> nameRects = new Dictionary<Pawn, Rect>();
        private readonly Dictionary<object, Rect> chipRects = new Dictionary<object, Rect>();
        private Rect toolbarRect;
        private Rect statsRect;
        private float rowWidth;
        private Rect viewport;
        private Pawn scrolledPawn;
        private int scrolledIndex = -1;
        private bool landed;

        public ClOverviewScope(Window window)
        {
            this.window = window;
            Claim(SelectActionId, e => SelectOnly(), when: () => CursorPawn() != null);
            Claim(ToggleActionId, e => ToggleSelected(), when: () => CursorPawn() != null);
            Claim(RangeActionId, e => SelectRange(), when: () => CursorPawn() != null);
            Claim(RemoveActionId, e => RemoveCursorChip(), when: () => CursorElement() != null);
            Claim(ContextActionId, e => OpenContextMenu(), when: () => CursorElement() != null || (CursorPawn() != null && CursorColumn(out _) == Column.Copy));
            Claim(CutActionId, e => PickUp(copy: false), when: () => CursorElement() != null);
            Claim(CopyActionId, e => Copy(), when: () => CursorPawn() != null);
            Claim(PasteActionId, e => Paste(), when: () => CursorPawn() != null);
            Claim(SharedMenuGrammar.ReorderLeft, e => ReorderChip(-1), when: () => CanReorderChip(-1));
            Claim(SharedMenuGrammar.ReorderRight, e => ReorderChip(1), when: () => CanReorderChip(1));
            Claim(SharedMenuGrammar.Cancel, e => DropPickup(), when: () => pickupElement != null);
            RegisterPopTeardown(searchSession.CancelIfActive);
        }

        public override string Name => "clOverview";

        protected internal override Window OwnedWindow => window;

        protected override bool EnableTypeahead => true;

        /// <summary>Escape drops a tag being moved before it closes the tab, as the window's own cancel does mid-drag.</summary>
        public override bool OwnsCancel => base.OwnsCancel || pickupElement != null;

        public override void OnFocus()
        {
            base.OnFocus();
            if (!landed)
            {
                landed = true;
                ScreenReturn.LandTableCursor(Name, PawnTable(), pawns);
            }
        }

        // ---- Model ----

        protected override int ContentRegionCount => 3;

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case TableRegion:
                    return (window as MainTabWindow)?.def?.LabelCap.Resolve() ?? CompatText.ModText("CompositableLoadouts.Overview.Tags");
                case SearchRegion:
                    return "RimWorldAccess.Compat.CompositableLoadouts.Overview.Search".Translate();
                default:
                    return "RimWorldAccess.Compat.CompositableLoadouts.Overview.StatsRegion".Translate();
            }
        }

        protected override bool ContentRegionAlwaysNavigable(int region)
        {
            return region != StatsRegion || ClCompat.Overview.ShowStats(window);
        }

        protected override void RefreshContent()
        {
            Pawn cursorPawn = CursorPawn();
            Column cursorColumn = CursorColumn(out int cursorChip);
            int oldColumns = ColumnTotal();

            pawns.Clear();
            pawns.AddRange(ClCompat.Overview.Pawns(window));
            chipColumns = 0;
            foreach (Pawn pawn in pawns)
            {
                chipColumns = Mathf.Max(chipColumns, ElementsOf(pawn).Count);
            }
            pasteColumn = ClCompat.Columns.Primed;

            TableModel table = PawnTable();
            if (table != null && cursorPawn != null)
            {
                int row = pawns.IndexOf(cursorPawn);
                if (row >= 0 && row + 1 < table.Rows.Count && row + 1 != table.Rows.Index)
                {
                    table.Rows.MoveTo(row + 1);
                }
                if (ColumnTotal() != oldColumns)
                {
                    table.SetColumnCount(ColumnTotal());
                    table.MoveToColumn(ColumnIndexOf(cursorColumn, Mathf.Min(cursorChip, chipColumns - 1)));
                }
            }

            BuildStatsRows();
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case TableRegion:
                    return pawns.Count;
                case SearchRegion:
                    return 1;
                default:
                    return statsRows.Count;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == TableRegion)
            {
                Pawn pawn = PawnAt(index);
                d.Label = pawn?.LabelShortCap;
                return d;
            }
            if (region == SearchRegion)
            {
                d.Label = "RimWorldAccess.Compat.CompositableLoadouts.Overview.Search".Translate();
                d.Role = ElementRole.TextField;
                d.EntersEditOnAccept = true;
                string text = ClCompat.Overview.Search(window)?.filter.Text;
                if (text.NullOrEmpty())
                {
                    d.ValueBlank = true;
                }
                else
                {
                    d.Value = text;
                }
                d.Extras = CompatText.ModText("CompositableLoadouts.Overview.SearchDesc");
                return d;
            }
            if (index < 0 || index >= statsRows.Count)
            {
                return d;
            }
            StatsRow row = statsRows[index];
            d.Label = row.Label;
            d.Value = row.Value;
            d.Extras = row.Extras;
            if (row.Kind == StatsRowKind.Text)
            {
                d.ReadOnly = true;
            }
            else
            {
                d.Role = ElementRole.Button;
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == SearchRegion)
            {
                BeginSearchEdit();
                return;
            }
            if (region == StatsRegion && index >= 0 && index < statsRows.Count && statsRows[index].Kind != StatsRowKind.Text)
            {
                ClCompat.Overview.StepFocus(window, statsRows[index].Kind == StatsRowKind.Next ? 1 : -1);
                RefreshModel();
                Pawn focused = ClCompat.Overview.Focused(window);
                if (focused != null)
                {
                    TolkHelper.SpeakData(focused.LabelShortCap);
                }
            }
        }

        // ---- Table ----

        protected override int ContentColumnCount(int region)
        {
            return region == TableRegion ? ColumnTotal() : 0;
        }

        private int ColumnTotal()
        {
            return 2 + chipColumns + 3 + (pasteColumn ? 1 : 0);
        }

        private int ColumnIndexOf(Column kind, int chip)
        {
            switch (kind)
            {
                case Column.Name:
                    return 0;
                case Column.State:
                    return 1;
                case Column.Chip:
                    return chip >= 0 ? 2 + chip : 2 + chipColumns;
                case Column.Add:
                    return 2 + chipColumns;
                case Column.Copy:
                    return 3 + chipColumns;
                case Column.Paste:
                    return pasteColumn ? 4 + chipColumns : 3 + chipColumns;
                default:
                    return ColumnTotal() - 1;
            }
        }

        private Column KindOf(int column, out int chip)
        {
            chip = -1;
            if (column <= 0)
            {
                return Column.Name;
            }
            if (column == 1)
            {
                return Column.State;
            }
            if (column < 2 + chipColumns)
            {
                chip = column - 2;
                return Column.Chip;
            }
            int rest = column - 2 - chipColumns;
            if (rest == 0)
            {
                return Column.Add;
            }
            if (rest == 1)
            {
                return Column.Copy;
            }
            return rest == 2 && pasteColumn ? Column.Paste : Column.Edit;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            switch (KindOf(column, out int chip))
            {
                case Column.Name:
                    return new TableColumnInfo(CompatText.ModText("CompositableLoadouts.Overview.Name"), sortable: true);
                case Column.State:
                    return new TableColumnInfo(CompatText.ModText("CompositableLoadouts.Overview.State"), sortable: true,
                        cellRole: ElementRole.ComboBox);
                case Column.Chip:
                    return chip == 0
                        ? new TableColumnInfo(CompatText.ModText("CompositableLoadouts.Overview.Tags"), sortable: true)
                        : new TableColumnInfo("RimWorldAccess.Compat.CompositableLoadouts.Overview.TagColumn".Translate(chip + 1));
                case Column.Add:
                    return new TableColumnInfo(CompatText.ModText("CompositableLoadouts.AddTag"), cellRole: ElementRole.Button);
                case Column.Copy:
                    return new TableColumnInfo("Copy".Translate(), cellRole: ElementRole.Button);
                case Column.Paste:
                    return new TableColumnInfo("Paste".Translate(), cellRole: ElementRole.Button);
                default:
                    return new TableColumnInfo("AssignTabEdit".Translate(), cellRole: ElementRole.Button);
            }
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            Pawn pawn = PawnAt(row);
            if (pawn == null)
            {
                return null;
            }
            switch (KindOf(column, out int chip))
            {
                case Column.State:
                    return ClCompat.StateName(ClCompat.ActiveState(pawn)) ?? CompatText.ModText("CompositableLoadouts.SelectState");
                case Column.Chip:
                    object element = ElementAt(pawn, chip);
                    return element != null ? ChipText(pawn, element) : "RimWorldAccess.Compat.CompositableLoadouts.Overview.NoTag".Translate().ToString();
                case Column.Paste:
                    return ClCompat.Columns.CanPasteTo(pawn) ? null : "RimWorldAccess.Shell.State.Disabled".Translate().ToString();
                default:
                    return null;
            }
        }

        /// <summary>What the chip and its tooltip show: name, state condition, faded when inactive, and the red overlay's reasons.</summary>
        private string ChipText(Pawn pawn, object element)
        {
            var parts = new List<string> { ClCompat.TagName(ClCompat.ElementTag(element)) };
            if (ClCompat.ElementState(element) != null)
            {
                parts.Add(CompatText.ModArgs("CompositableLoadouts.Overview.ActiveWhen", ClCompat.ElementStateName(element)));
            }
            if (!ClCompat.ElementActive(element, ClCompat.Overview.ShownStateFor(window, pawn)))
            {
                parts.Add(CompatText.ModText("CompositableLoadouts.Overview.Inactive"));
            }
            parts.Add(CompatText.Flatten(ClCompat.Overview.Warning(window, pawn, element)));
            return CompatText.JoinSentences(parts);
        }

        protected override string ContentCellTip(int region, int row, int column)
        {
            Pawn pawn = PawnAt(row);
            if (pawn == null)
            {
                return null;
            }
            Column kind = KindOf(column, out int chip);
            if (kind == Column.Edit)
            {
                return CompatText.ModArgs("CompositableLoadouts.EditLoadoutDesc", pawn.LabelShortCap);
            }
            object element = kind == Column.Chip ? ElementAt(pawn, chip) : null;
            if (element == null)
            {
                return null;
            }
            var items = new List<string>();
            foreach (object item in ClCompat.TagItems(ClCompat.ElementTag(element)))
            {
                items.Add("RimWorldAccess.Compat.CompositableLoadouts.Overview.ItemLine".Translate(
                    ClCompat.ItemLabel(item), ClCompat.ItemQuantity(item)));
            }
            return CompatText.JoinSentences(items);
        }

        protected override string ContentRowTip(int region, int row)
        {
            Pawn pawn = PawnAt(row);
            if (pawn == null)
            {
                return null;
            }
            var parts = new List<string>();
            if (ClCompat.Overview.Selected(window)?.Contains(pawn) == true)
            {
                parts.Add("RimWorldAccess.Shell.State.Selected".Translate());
            }
            if (!pawn.Spawned)
            {
                parts.Add("RimWorldAccess.Compat.CompositableLoadouts.Overview.AwayFromMap".Translate());
            }
            return CompatText.JoinSentences(parts);
        }

        protected override string ContentCellHint(int region, int row, int column)
        {
            Pawn pawn = PawnAt(row);
            switch (KindOf(column, out int chip))
            {
                case Column.Name:
                    return "RimWorldAccess.Compat.CompositableLoadouts.Overview.NameHint".Translate(
                        ChordDisplay(SelectActionId), ChordDisplay(ToggleActionId), ChordDisplay(RangeActionId));
                case Column.Chip:
                    return pawn != null && ElementAt(pawn, chip) != null
                        ? "RimWorldAccess.Compat.CompositableLoadouts.Overview.ChipHint".Translate(
                            ChordDisplay(RemoveActionId), ChordDisplay(CutActionId), ChordDisplay(CopyActionId),
                            ChordDisplay(PasteActionId), ChordDisplay(SharedMenuGrammar.ReorderLeft),
                            ChordDisplay(SharedMenuGrammar.ReorderRight))
                        : null;
                case Column.Copy:
                    return "RimWorldAccess.Compat.CompositableLoadouts.Overview.CopyHint".Translate(ChordDisplay(ContextActionId));
                default:
                    return null;
            }
        }

        protected override bool ActivateContentCell(int region, int row, int column)
        {
            Pawn pawn = PawnAt(row);
            if (region != TableRegion || pawn == null)
            {
                return false;
            }
            switch (KindOf(column, out int chip))
            {
                case Column.Name:
                    JumpTo(pawn);
                    break;
                case Column.State:
                    DropdownColumnHandler.OpenGeneratedMenu(ClCompat.Overview.StateGenerator, null, pawn);
                    break;
                case Column.Chip:
                    object element = ElementAt(pawn, chip);
                    if (element != null)
                    {
                        OpenChipMenu(pawn, element);
                    }
                    else
                    {
                        OpenAddTag(pawn);
                    }
                    break;
                case Column.Add:
                    OpenAddTag(pawn);
                    break;
                case Column.Copy:
                    CopyAll(pawn);
                    break;
                case Column.Paste:
                    PasteClipboard(pawn);
                    break;
                default:
                    ClCompat.OpenLoadoutEditor(pawn);
                    break;
            }
            return true;
        }

        // ---- Sorting ----

        /// <summary>The window's default order (name, ascending) reads as unsorted: the mod has no other cleared state.</summary>
        protected override bool TryGetLiveSort(int region, out int column, out bool descending)
        {
            column = -1;
            descending = false;
            if (region != TableRegion)
            {
                return false;
            }
            int sortBy = ClCompat.Overview.SortBy(window);
            descending = ClCompat.Overview.Descending(window);
            if (sortBy == SortByName && !descending)
            {
                descending = false;
                return true;
            }
            column = sortBy == SortByName ? 0 : sortBy == SortByState ? 1 : chipColumns > 0 ? 2 : -1;
            return true;
        }

        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            Pawn tracked = PawnAt(currentRow);
            Column kind = KindOf(column, out _);
            int sortBy = kind == Column.State ? SortByState : kind == Column.Chip ? SortByTags : SortByName;
            if (cycle == SortCycleResult.Cleared)
            {
                ClCompat.Overview.SetSort(window, SortByName, false);
            }
            else
            {
                ClCompat.Overview.SetSort(window, sortBy, cycle == SortCycleResult.SortedDescending);
            }
            pawns.Clear();
            pawns.AddRange(ClCompat.Overview.Pawns(window));
            return tracked != null ? Mathf.Max(0, pawns.IndexOf(tracked)) : 0;
        }

        // ---- Selection: the name's click, Ctrl+click and Shift+click (MainTabWindow_LoadoutOverview.cs:154-174) ----

        private void SelectOnly()
        {
            Pawn pawn = CursorPawn();
            HashSet<Pawn> selected = ClCompat.Overview.Selected(window);
            if (pawn == null || selected == null)
            {
                return;
            }
            // MUTATION-C: mirrors MainTabWindow_LoadoutOverview.cs:166-167; Select reads the mouse event's modifiers.
            selected.Clear();
            selected.Add(pawn);
            ClCompat.Overview.SetFocused(window, pawn);
            TolkHelper.Speak("RimWorldAccess.Shell.State.Selected".Loc());
        }

        private void ToggleSelected()
        {
            Pawn pawn = CursorPawn();
            HashSet<Pawn> selected = ClCompat.Overview.Selected(window);
            if (pawn == null || selected == null)
            {
                return;
            }
            // MUTATION-C: mirrors MainTabWindow_LoadoutOverview.cs:155-158; Select reads the mouse event's modifiers.
            bool nowSelected = !selected.Remove(pawn);
            if (nowSelected)
            {
                selected.Add(pawn);
            }
            ClCompat.Overview.SetFocused(window, pawn);
            TolkHelper.SpeakData(CompatText.JoinSentences(new List<string>
            {
                (nowSelected ? "RimWorldAccess.Shell.State.Selected" : "RimWorldAccess.Shell.State.NotSelected").Translate(),
                selected.Count > 1 ? CompatText.ModArgs("CompositableLoadouts.Overview.SelectedCount", selected.Count) : null,
            }));
        }

        private void SelectRange()
        {
            Pawn pawn = CursorPawn();
            HashSet<Pawn> selected = ClCompat.Overview.Selected(window);
            List<Pawn> live = ClCompat.Overview.Pawns(window);
            int from = live.IndexOf(ClCompat.Overview.Focused(window));
            int to = live.IndexOf(pawn);
            if (selected == null || to < 0)
            {
                return;
            }
            if (from < 0)
            {
                SelectOnly();
                return;
            }
            // MUTATION-C: mirrors MainTabWindow_LoadoutOverview.cs:159-164; Select reads the mouse event's modifiers.
            for (int i = Mathf.Min(from, to); i <= Mathf.Max(from, to); i++)
            {
                selected.Add(live[i]);
            }
            ClCompat.Overview.SetFocused(window, pawn);
            TolkHelper.SpeakData(CompatText.ModArgs("CompositableLoadouts.Overview.SelectedCount", selected.Count));
        }

        /// <summary>The name's double-click jump, closing the tab so Escape on the map brings it back.</summary>
        private void JumpTo(Pawn pawn)
        {
            if (pawn.Spawned)
            {
                CameraJumper.TryJumpAndSelect(pawn);
            }
            MainButtonDef tab = ((MainTabWindow)window).def;
            ScreenReturn.JumpToPawn(Name, tab.defName, Model.CurrentTable, () =>
            {
                if (Find.MainTabsRoot.OpenTab == tab)
                {
                    Find.MainTabsRoot.EscapeCurrentTab();
                }
            }, pawn, "RimWorldAccess.Pawns.AssignMenu.PawnNotOnMap");
        }

        // ---- Chips ----

        private void OpenChipMenu(Pawn pawn, object element)
        {
            List<FloatMenuOption> options = ClCompat.Overview.ChipOptions(window, pawn, element);
            if (options.Count > 0)
            {
                WindowlessFloatMenuState.Open(options, colonistOrders: false);
            }
        }

        private void OpenContextMenu()
        {
            Pawn pawn = CursorPawn();
            object element = CursorElement();
            if (element != null)
            {
                OpenChipMenu(pawn, element);
            }
            else if (pawn != null)
            {
                WindowlessFloatMenuState.Open(CopyOptions(pawn), colonistOrders: false);
            }
        }

        private void RemoveCursorChip()
        {
            Pawn pawn = CursorPawn();
            object element = CursorElement();
            string name = ClCompat.TagName(ClCompat.ElementTag(element));
            if (ClCompat.Overview.RemoveChip(window, pawn, element))
            {
                if (pickupElement == element)
                {
                    ClearPickup();
                }
                RefreshModel();
                TolkHelper.Speak("RimWorldAccess.Compat.CompositableLoadouts.Overview.TagRemoved".Loc(name, pawn.LabelShort));
            }
        }

        /// <summary>The plus button: a picker over every tag the pawn lacks, appending the choices.</summary>
        private void OpenAddTag(Pawn pawn)
        {
            var owned = new HashSet<object>(ElementsOf(pawn).Select(ClCompat.ElementTag));
            List<object> tags = ClCompat.AllTags().Where(t => !owned.Contains(t)).ToList();
            // MUTATION-C: mirrors MainTabWindow_LoadoutOverview.cs:626-627; the plus button's callback is an inline lambda.
            ClCompat.OpenTagSelector(tags, tag => ClCompat.AddTag(pawn, tag, null, true, insertAtStart: false),
                multiSelect: true, customTitleSuffix: null);
        }

        private bool CanReorderChip(int direction)
        {
            Pawn pawn = CursorPawn();
            if (pawn == null || CursorColumn(out int chip) != Column.Chip)
            {
                return false;
            }
            int target = chip + direction;
            return chip < ElementsOf(pawn).Count && target >= 0 && target < ElementsOf(pawn).Count;
        }

        private void ReorderChip(int direction)
        {
            Pawn pawn = CursorPawn();
            CursorColumn(out int chip);
            if (!MoveElement(pawn, chip, chip + direction))
            {
                return;
            }
            RefreshModel();
            PawnTable()?.MoveToColumn(ColumnIndexOf(Column.Chip, chip + direction));
            TolkHelper.Speak("RimWorldAccess.Common.Drag.ReorderPosition".Loc(chip + direction + 1, ElementsOf(pawn).Count));
        }

        // MUTATION-C: mirrors MainTabWindow_LoadoutOverview.cs:308-315; the same-row drop is inline in Drop, which reads the live drag.
        private static bool MoveElement(Pawn pawn, int from, int to)
        {
            IList elements = ClCompat.Overview.ElementList(pawn);
            if (elements == null || from < 0 || from >= elements.Count || to < 0 || to >= elements.Count || from == to)
            {
                return false;
            }
            object element = elements[from];
            elements.RemoveAt(from);
            elements.Insert(to, element);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            return true;
        }

        // ---- Cut, copy and paste ----

        private void PickUp(bool copy)
        {
            pickupPawn = CursorPawn();
            pickupElement = CursorElement();
            pickupCopy = copy;
            string name = ClCompat.TagName(ClCompat.ElementTag(pickupElement));
            TolkHelper.Speak((copy
                ? "RimWorldAccess.Compat.CompositableLoadouts.Overview.TagCopied"
                : "RimWorldAccess.Compat.CompositableLoadouts.Overview.TagCut").Loc(name));
        }

        private void DropPickup()
        {
            string name = ClCompat.TagName(ClCompat.ElementTag(pickupElement));
            ClearPickup();
            TolkHelper.Speak("RimWorldAccess.Compat.CompositableLoadouts.Overview.PickupDropped".Loc(name));
        }

        private void ClearPickup()
        {
            pickupPawn = null;
            pickupElement = null;
            pickupCopy = false;
        }

        /// <summary>On a chip, picks the tag up to copy; anywhere else on the row, the copy button.</summary>
        private void Copy()
        {
            if (CursorElement() != null)
            {
                PickUp(copy: true);
                return;
            }
            CopyAll(CursorPawn());
        }

        private void CopyAll(Pawn pawn)
        {
            ClearPickup();
            ClCompat.Columns.CopyAll(pawn, replace: true);
            RefreshModel();
            TolkHelper.Speak("RimWorldAccess.Compat.CompositableLoadouts.Overview.Copied".Loc(pawn.LabelShort));
        }

        /// <summary>Drops the tag picked up last; with none, the paste button.</summary>
        private void Paste()
        {
            Pawn pawn = CursorPawn();
            if (pickupElement != null && ElementsOf(pickupPawn).Contains(pickupElement))
            {
                CursorColumn(out int chip);
                int count = ElementsOf(pawn).Count;
                DropOn(pawn, chip >= 0 && chip < count ? chip : count);
                return;
            }
            ClearPickup();
            if (!ClCompat.Columns.Primed)
            {
                TolkHelper.Speak("RimWorldAccess.Compat.CompositableLoadouts.Overview.NothingToPaste".Loc());
                return;
            }
            PasteClipboard(pawn);
        }

        private void PasteClipboard(Pawn pawn)
        {
            if (!ClCompat.Columns.CanPasteTo(pawn))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Compat.CompositableLoadouts.Overview.PasteChangesNothing".Loc(pawn.LabelShort));
                return;
            }
            ClCompat.Columns.PasteTo(pawn);
            RefreshModel();
            TolkHelper.Speak("RimWorldAccess.Compat.CompositableLoadouts.Overview.Pasted".Loc(pawn.LabelShort));
        }

        // MUTATION-C: mirrors MainTabWindow_LoadoutOverview.cs:300-341 (Drop, CanDropOn); Drop reads the live drag and the mouse event's ctrl flag.
        private void DropOn(Pawn target, int index)
        {
            Pawn from = pickupPawn;
            object element = pickupElement;
            bool copy = pickupCopy;
            object tag = ClCompat.ElementTag(element);
            string name = ClCompat.TagName(tag);
            ClearPickup();

            if (target == from)
            {
                int fromIndex = ElementsOf(from).IndexOf(element);
                int to = index > fromIndex ? index - 1 : index;
                if (copy)
                {
                    TolkHelper.SpeakData(CompatText.ModArgs("CompositableLoadouts.Overview.AlreadyHas", target.LabelShort, name));
                    return;
                }
                if (MoveElement(from, fromIndex, to))
                {
                    RefreshModel();
                }
                TolkHelper.Speak("RimWorldAccess.Common.Drag.ReorderPosition".Loc(to + 1, ElementsOf(from).Count));
                return;
            }

            HashSet<Pawn> selected = ClCompat.Overview.Selected(window);
            IEnumerable<Pawn> actedOn = selected != null && selected.Contains(target)
                ? ClCompat.Overview.Pawns(window).Where(selected.Contains).ToList()
                : new List<Pawn> { target };
            int added = 0;
            foreach (Pawn pawn in actedOn)
            {
                if (pawn == from || ElementsOf(pawn).Any(e => ClCompat.ElementTag(e) == tag))
                {
                    continue;
                }
                object copyElement = ClCompat.Overview.NewElement(tag, ClCompat.ElementState(element),
                    ClCompat.ElementActiveWhenStateActive(element));
                if (copyElement != null && ClCompat.Overview.InsertElement(pawn, copyElement, index))
                {
                    added++;
                }
            }
            if (added == 0)
            {
                Messages.Message(CompatText.ModArgs("CompositableLoadouts.Overview.AlreadyHas", target.LabelShort, name),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!copy)
            {
                ClCompat.RemoveTag(from, element);
            }
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            RefreshModel();
            string key = copy
                ? added == 1 ? "RimWorldAccess.Compat.CompositableLoadouts.Overview.TagCopiedTo" : "RimWorldAccess.Compat.CompositableLoadouts.Overview.TagCopiedToMany"
                : added == 1 ? "RimWorldAccess.Compat.CompositableLoadouts.Overview.TagMovedTo" : "RimWorldAccess.Compat.CompositableLoadouts.Overview.TagMovedToMany";
            TolkHelper.Speak(added == 1 ? key.Loc(name, target.LabelShort) : key.Loc(name, added));
        }

        /// <summary>The copy button's right-click options.</summary>
        private List<FloatMenuOption> CopyOptions(Pawn pawn)
        {
            return new List<FloatMenuOption>
            {
                new FloatMenuOption("RimWorldAccess.Compat.CompositableLoadouts.CopyAllReplace".Translate(pawn.LabelShort),
                    () => { ClearPickup(); ClCompat.Columns.CopyAll(pawn, replace: true); }),
                new FloatMenuOption("RimWorldAccess.Compat.CompositableLoadouts.CopyAllAdd".Translate(pawn.LabelShort),
                    () => { ClearPickup(); ClCompat.Columns.CopyAll(pawn, replace: false); }),
                new FloatMenuOption("RimWorldAccess.Compat.CompositableLoadouts.SelectTagsReplace".Translate(pawn.LabelShort),
                    () => SelectTagsToCopy(pawn, replace: true)),
                new FloatMenuOption("RimWorldAccess.Compat.CompositableLoadouts.SelectTagsAdd".Translate(pawn.LabelShort),
                    () => SelectTagsToCopy(pawn, replace: false)),
            };
        }

        private void SelectTagsToCopy(Pawn pawn, bool replace)
        {
            ClearPickup();
            ClCompat.Columns.ClearClipboard();
            List<object> elements = ElementsOf(pawn);
            ClCompat.OpenTagSelector(elements.Select(ClCompat.ElementTag).ToList(),
                tag => ClCompat.Columns.AddToClipboard(pawn, elements.FirstOrDefault(e => ClCompat.ElementTag(e) == tag), replace),
                multiSelect: true, customTitleSuffix: CompatText.ModText("CompositableLoadouts.ToCopy"));
        }

        // ---- Search ----

        private void BeginSearchEdit()
        {
            QuickSearchWidget search = ClCompat.Overview.Search(window);
            if (search == null)
            {
                return;
            }
            string label = "RimWorldAccess.Compat.CompositableLoadouts.Overview.Search".Translate();
            searchSession.EnterEdit(search.filter.Text, new TextFieldSpec(labelKey: null, maxLength: search.maxSearchTextLength, minLength: 0),
                label, SetSearch, () =>
                {
                    RefreshModel();
                    AnnounceCurrentItem();
                });
        }

        private void SetSearch(string value)
        {
            QuickSearchWidget search = ClCompat.Overview.Search(window);
            if (search == null || search.filter.Text == (value ?? ""))
            {
                return;
            }
            search.filter.Text = value ?? ""; // MUTATION-C: mirrors QuickSearchWidget.OnGUI's text write and the window's onFilterChange (MainTabWindow_LoadoutOverview.cs:402)
            ClCompat.Overview.FindPawns(window);
        }

        // ---- Stats panel (Panel_PawnStats.Draw) ----

        private void BuildStatsRows()
        {
            statsRows.Clear();
            Pawn pawn = ClCompat.Overview.ShowStats(window) ? ClCompat.Overview.Focused(window) : null;
            if (pawn == null)
            {
                return;
            }
            statsRows.Add(new StatsRow { Kind = StatsRowKind.Previous, Label = CompatText.ModText("CompositableLoadouts.SelectPrevious") });
            statsRows.Add(new StatsRow { Kind = StatsRowKind.Text, Label = pawn.LabelShort });
            statsRows.Add(new StatsRow { Kind = StatsRowKind.Next, Label = CompatText.ModText("CompositableLoadouts.SelectNext") });

            statsRows.Add(new StatsRow { Label = CompatText.ModText("CompositableLoadouts.TopFourSkills") });
            if (pawn.skills != null)
            {
                foreach (SkillRecord skill in pawn.skills.skills.OrderByDescending(s => s.Level).Take(4))
                {
                    statsRows.Add(new StatsRow
                    {
                        Label = skill.def.skillLabel.CapitalizeFirst(),
                        Value = CompatText.JoinSentences(new List<string>
                        {
                            skill.Level.ToString(),
                            skill.passion == Passion.Major ? "PassionMajor".Translate().ToString()
                                : skill.passion == Passion.Minor ? "PassionMinor".Translate().ToString() : null,
                        }),
                        Extras = PawnColumnWorker_AccessSkill.SkillDescription != null
                            ? CompatText.Flatten(PawnColumnWorker_AccessSkill.SkillDescription(skill))
                            : null,
                    });
                }
            }

            statsRows.Add(new StatsRow { Label = CompatText.ModText("CompositableLoadouts.PawnTraits") });
            if (pawn.story?.traits != null)
            {
                foreach (Trait trait in pawn.story.traits.allTraits)
                {
                    statsRows.Add(new StatsRow { Label = trait.LabelCap, Extras = CompatText.Flatten(trait.TipString(pawn)) });
                }
            }

            statsRows.Add(new StatsRow { Label = CompatText.ModText("CompositableLoadouts.ApparelWhichCanBeWorn") });
            foreach (ThingDef def in ClCompat.Overview.WearableApparel(pawn, ClCompat.Overview.ShownStateFor(window, pawn)))
            {
                statsRows.Add(new StatsRow { Label = def.LabelCap });
            }
        }

        // ---- Buttons ----

        /// <summary>Capture order is the draw order: scope, view-as state, then the selection bar.</summary>
        protected override string CapturedButtonLabel(int captureIndex, string rawLabel)
        {
            switch (captureIndex)
            {
                case 0:
                    return "RimWorldAccess.Compat.CompositableLoadouts.Overview.ScopeButton".Translate(rawLabel);
                case 1:
                    return "RimWorldAccess.Compat.CompositableLoadouts.Overview.ViewAsButton".Translate(rawLabel);
                default:
                    return rawLabel;
            }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                bool shown = ClCompat.Overview.ShowStats(window);
                actions.Add(new ScreenAction(CompatText.ModText("CompositableLoadouts.Overview.ShowStats"), ToggleStats,
                    check: shown ? CheckState.Checked : CheckState.Unchecked));
                return actions;
            }
        }

        private void ToggleStats()
        {
            ClCompat.Overview.SetShowStats(window, !ClCompat.Overview.ShowStats(window));
            RefreshModel();
            TolkHelper.Speak((ClCompat.Overview.ShowStats(window)
                ? "RimWorldAccess.Shell.State.Checked"
                : "RimWorldAccess.Shell.State.Unchecked").Loc());
        }

        // ---- Draw records: the focus ring and scroll-to-cursor ----

        internal void BeginPass()
        {
            rowRects.Clear();
            nameRects.Clear();
            chipRects.Clear();
            toolbarRect = default(Rect);
            statsRect = default(Rect);
            searchSession.MirrorLive();
            ScrollToCursor();
        }

        internal void RecordToolbar(Rect screen)
        {
            toolbarRect = screen;
        }

        internal void RecordRow(Pawn pawn, Rect local, Rect screen, Rect visible)
        {
            rowRects[pawn] = screen;
            rowWidth = local.width;
            viewport = visible;
        }

        internal void RecordName(Pawn pawn, Rect screen)
        {
            nameRects[pawn] = screen;
        }

        internal void RecordChip(object element, Rect screen)
        {
            if (element != null)
            {
                chipRects[element] = screen;
            }
        }

        internal void RecordStats(object panel, Rect screen)
        {
            if (panel == ClCompat.Overview.StatsPanel(window))
            {
                statsRect = screen;
            }
        }

        protected internal override Rect FocusedContentRect()
        {
            switch (Model.RegionIndex)
            {
                case SearchRegion:
                    return toolbarRect;
                case StatsRegion:
                    return statsRect;
            }
            Pawn pawn = CursorPawn();
            if (pawn == null)
            {
                return default(Rect);
            }
            object element = CursorElement();
            if (element != null && chipRects.TryGetValue(element, out Rect chip))
            {
                return chip;
            }
            if (CursorColumn(out _) == Column.Name && nameRects.TryGetValue(pawn, out Rect name))
            {
                return name;
            }
            return rowRects.TryGetValue(pawn, out Rect row) ? row : default(Rect);
        }

        /// <summary>Brings the cursor's row into the table's view once each time the cursor changes row.</summary>
        private void ScrollToCursor()
        {
            Pawn pawn = CursorPawn();
            List<Pawn> live = ClCompat.Overview.Pawns(window);
            int index = live.IndexOf(pawn);
            if (pawn == null || index < 0 || rowWidth <= 0f || viewport.height <= 0f
                || (pawn == scrolledPawn && index == scrolledIndex))
            {
                return;
            }
            scrolledPawn = pawn;
            scrolledIndex = index;
            float top = 0f;
            for (int i = 0; i < index; i++)
            {
                top += ClCompat.Overview.RowHeight(live[i], rowWidth);
            }
            float bottom = top + ClCompat.Overview.RowHeight(pawn, rowWidth);
            ref Vector2 scroll = ref ClCompat.Overview.TableScroll(window);
            if (top < scroll.y)
            {
                scroll.y = top;
            }
            else if (bottom > scroll.y + viewport.height)
            {
                scroll.y = bottom - viewport.height;
            }
        }

        // ---- Cursor ----

        private Pawn PawnAt(int row)
        {
            return row >= 0 && row < pawns.Count ? pawns[row] : null;
        }

        private Pawn CursorPawn()
        {
            if (Model.RegionIndex != TableRegion)
            {
                return null;
            }
            TableModel table = PawnTable();
            return table != null ? PawnAt(table.Rows.Index - 1) : null;
        }

        private Column CursorColumn(out int chip)
        {
            TableModel table = PawnTable();
            return KindOf(table != null ? table.ColumnIndex : 0, out chip);
        }

        /// <summary>The pawn table's grid state; null before the first refresh has built the regions.</summary>
        private TableModel PawnTable()
        {
            return Model.RegionCount > TableRegion ? Model.Table(TableRegion) : null;
        }

        private object CursorElement()
        {
            Pawn pawn = CursorPawn();
            return pawn != null && CursorColumn(out int chip) == Column.Chip ? ElementAt(pawn, chip) : null;
        }

        private static List<object> ElementsOf(Pawn pawn)
        {
            return pawn != null ? ClCompat.Elements(ClCompat.LoadoutOf(pawn)) : new List<object>();
        }

        private static object ElementAt(Pawn pawn, int index)
        {
            List<object> elements = ElementsOf(pawn);
            return index >= 0 && index < elements.Count ? elements[index] : null;
        }
    }
}
