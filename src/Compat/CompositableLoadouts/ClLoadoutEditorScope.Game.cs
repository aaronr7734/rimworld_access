using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Compositable Loadouts' loadout editor (<c>Dialog_LoadoutEditor</c>). Regions follow the window:
    /// the header (the Pawn Stats, Add Tag and Coverage buttons, then the "view as if" state
    /// dropdown), the applied tags table, the loadout statistics, and the two side panels, which stay
    /// out of the Tab cycle while the window hides them. The header buttons are the window's own,
    /// clicked through the capture so their inline handlers run unmodified.
    ///
    /// The window switches pawns on any key press while an arrow, comma or period is held; that
    /// test is masked (<see cref="ClLoadoutEditorDrawPatch"/>) and the switch is this scope's chord,
    /// which calls the same vanilla colonist cycling the mod patches to reopen the editor.
    /// </summary>
    internal sealed partial class ClLoadoutEditorScope : ScreenScope
    {
        private const string PreviousPawnActionId = "clLoadoutEditor.previousPawn";
        private const string NextPawnActionId = "clLoadoutEditor.nextPawn";
        private const string EditTagActionId = "clLoadoutEditor.editTag";
        private const string RemoveTagActionId = "clLoadoutEditor.removeTag";

        private const int HeaderRegion = 0;
        private const int TagsRegion = 1;
        private const int StatisticsRegion = 2;
        private const int PawnStatsRegion = 3;
        private const int CoverageRegion = 4;

        private const int PawnStatsRow = 0;
        private const int AddTagRow = 1;
        private const int CoverageRow = 2;
        private const int ShownStateRow = 3;
        private const int HeaderButtonCount = 3;

        private const int StateColumn = 0;
        private const int ItemsColumn = 1;
        private const int EditColumn = 2;
        private const int RemoveColumn = 3;

        // Where the cursor was when this editor handed over to the next pawn's, so the new one opens there.
        private static int[] pendingCarry;

        private readonly Window dialog;
        private readonly string[] headerLabels;
        private readonly int[] headerCaptureIndex = { -1, -1, -1 };
        private readonly Rect[] headerRects = new Rect[HeaderButtonCount];
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        private readonly List<object> elements = new List<object>();
        private readonly List<string> elementItems = new List<string>();
        private readonly List<Row> statisticsRows = new List<Row>();
        private readonly List<Row> pawnStatsRows = new List<Row>();
        private readonly List<Row> coverageRows = new List<Row>();

        private Pawn pawn;
        private object loadout;
        private object shownState;
        private bool pawnStatsShown;
        private bool coverageShown;

        private List<object> tagsBeforeAdd;
        private int pendingToggleRow = -1;
        private bool pendingToggleWas;
        private int pendingToggleFrame;

        public ClLoadoutEditorScope(Window dialog)
        {
            this.dialog = dialog;
            headerLabels = new[]
            {
                CompatText.ModText("CompositableLoadouts.PawnStats"),
                CompatText.ModText("CompositableLoadouts.AddTag"),
                CompatText.ModText("CompositableLoadouts.Coverage"),
            };
            Claim(PreviousPawnActionId, e => SwitchPawn(next: false));
            Claim(NextPawnActionId, e => SwitchPawn(next: true));
            Claim(EditTagActionId, e => EditTag(CurrentElementIndex()), when: () => CurrentElementIndex() >= 0);
            Claim(RemoveTagActionId, e => RemoveTag(CurrentElementIndex()), when: () => CurrentElementIndex() >= 0);
        }

        public override string Name => "clLoadoutEditor";

        protected internal override Window OwnedWindow => dialog;

        protected override int ContentRegionCount => 5;

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case HeaderRegion:
                    return "RimWorldAccess.Compat.CompositableLoadouts.Editor.HeaderRegion".Translate();
                case TagsRegion:
                    return CompatText.ModText("CompositableLoadouts.AppliedTags");
                case StatisticsRegion:
                    return CompatText.ModText("CompositableLoadouts.LoadoutStatistics");
                case PawnStatsRegion:
                    return CompatText.ModText("CompositableLoadouts.PawnStats");
                default:
                    return CompatText.ModText("CompositableLoadouts.Coverage");
            }
        }

        protected override string ComposeOpenAnnouncement()
        {
            Pawn shown = ClCompat.Editor.PawnOf(dialog);
            return shown == null ? null : CompatText.ModArgs("CompositableLoadouts.EditXLoadout", shown.LabelShort);
        }

        /// <summary>A hidden side panel is not drawn at all, so its region leaves the Tab cycle.</summary>
        protected override bool ContentRegionAlwaysNavigable(int region)
        {
            if (region == PawnStatsRegion)
            {
                return pawnStatsShown;
            }
            return region != CoverageRegion || coverageShown;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            AnnounceAddedTags();
            int[] carry = pendingCarry;
            pendingCarry = null;
            if (carry == null || carry[0] >= Model.RegionCount || !Model.IsRegionNavigable(carry[0]))
            {
                return;
            }
            Model.MoveToRegion(carry[0]);
            ListModel region = Model.CurrentRegion;
            if (region != null && region.Count > 0)
            {
                region.MoveTo(Mathf.Clamp(carry[1], 0, region.Count - 1));
            }
        }

        protected override void RefreshContent()
        {
            pawn = ClCompat.Editor.PawnOf(dialog);
            loadout = ClCompat.LoadoutOf(pawn);
            shownState = ClCompat.Editor.ShownState(dialog);
            pawnStatsShown = ClCompat.Editor.PawnStatsShown(dialog);
            coverageShown = ClCompat.Editor.CoverageShown(dialog);

            elements.Clear();
            elementItems.Clear();
            statisticsRows.Clear();
            pawnStatsRows.Clear();
            coverageRows.Clear();
            if (pawn == null || loadout == null)
            {
                return;
            }

            elements.AddRange(ClCompat.Elements(loadout));
            object slots = ClCompat.Editor.HypotheticalProsthetics(loadout, shownState, pawn.RaceProps.body);
            foreach (object element in elements)
            {
                elementItems.Add(ItemsSummary(element, slots));
            }
            BuildStatistics();
            if (pawnStatsShown)
            {
                BuildPawnStats();
            }
            if (coverageShown)
            {
                BuildCoverage();
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case HeaderRegion:
                    return pawn == null ? 0 : 4;
                case TagsRegion:
                    return elements.Count;
                case StatisticsRegion:
                    return statisticsRows.Count;
                case PawnStatsRegion:
                    return pawnStatsRows.Count;
                default:
                    return coverageRows.Count;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            switch (region)
            {
                case HeaderRegion:
                    return DescribeHeader(index);
                case TagsRegion:
                    return new ElementDescription { Label = index >= 0 && index < elements.Count ? TagNameAt(index) : "" };
                default:
                    Row row = RowAt(region, index);
                    return row == null ? new ElementDescription() : row.Describe();
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            switch (region)
            {
                case HeaderRegion:
                    ActivateHeader(index);
                    return;
                case TagsRegion:
                    EditTag(index);
                    return;
                default:
                    Row row = RowAt(region, index);
                    if (row?.Activate != null)
                    {
                        row.Activate();
                    }
                    else if (row != null)
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        TolkHelper.SpeakData(TranslatedShellVocabulary.Instance.Word(ElementStateWord.ReadOnly));
                    }
                    return;
            }
        }

        private Row RowAt(int region, int index)
        {
            List<Row> rows = region == StatisticsRegion ? statisticsRows
                : region == PawnStatsRegion ? pawnStatsRows
                : region == CoverageRegion ? coverageRows
                : null;
            return rows != null && index >= 0 && index < rows.Count ? rows[index] : null;
        }

        // ---- Header ----

        private ElementDescription DescribeHeader(int index)
        {
            var d = new ElementDescription();
            switch (index)
            {
                case PawnStatsRow:
                case CoverageRow:
                    d.Label = headerLabels[index];
                    d.Role = ElementRole.Checkbox;
                    d.Check = HeaderToggleState(index) ? CheckState.Checked : CheckState.Unchecked;
                    break;
                case AddTagRow:
                    d.Label = headerLabels[index];
                    d.Role = ElementRole.Button;
                    break;
                case ShownStateRow:
                    d.Label = "RimWorldAccess.Compat.CompositableLoadouts.Editor.ViewAsIf".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = ClCompat.StateName(shownState) ?? CompatText.ModText("CompositableLoadouts.DefaultStateNameInUse");
                    break;
            }
            return d;
        }

        private bool HeaderToggleState(int index)
        {
            return index == PawnStatsRow ? pawnStatsShown : coverageShown;
        }

        private void ActivateHeader(int index)
        {
            if (index == ShownStateRow)
            {
                DropdownColumnHandler.OpenGeneratedMenu(ClCompat.Editor.StatesGenerator, null, dialog);
                return;
            }
            if (index < 0 || index >= HeaderButtonCount || headerCaptureIndex[index] < 0)
            {
                return;
            }
            if (index == AddTagRow)
            {
                tagsBeforeAdd = new List<object>(elements);
                SuppressNextEntryAnnouncement();
            }
            else
            {
                pendingToggleRow = index;
                pendingToggleWas = HeaderToggleState(index);
                pendingToggleFrame = Time.frameCount;
            }
            ButtonTextCapture.RequestClick(headerCaptureIndex[index], this);
        }

        /// <summary>Back from the tag picker: names the tags it added, or re-reads the row when it added none.</summary>
        private void AnnounceAddedTags()
        {
            List<object> before = tagsBeforeAdd;
            if (before == null)
            {
                return;
            }
            tagsBeforeAdd = null;
            var added = new List<string>();
            for (int i = 0; i < elements.Count; i++)
            {
                if (!before.Contains(elements[i]))
                {
                    added.Add(TagNameAt(i));
                }
            }
            if (added.Count > 0)
            {
                TolkHelper.Speak("RimWorldAccess.Compat.CompositableLoadouts.Editor.TagsAdded".Loc(string.Join(", ", added)));
            }
            else
            {
                AnnounceCurrentItem();
            }
        }

        /// <summary>
        /// Runs after each non-layout draw of the window, inside this scope's button capture pass:
        /// records where the header buttons were drawn, and speaks a toggle once its click has run.
        /// </summary>
        internal void AfterDraw()
        {
            IReadOnlyList<ButtonTextCapture.CapturedButton> buttons = ButtonTextCapture.Items;
            for (int row = 0; row < HeaderButtonCount; row++)
            {
                headerCaptureIndex[row] = -1;
                for (int i = 0; i < buttons.Count; i++)
                {
                    if (buttons[i].Label == headerLabels[row])
                    {
                        headerCaptureIndex[row] = i;
                        headerRects[row] = buttons[i].VisibleScreenRect;
                        break;
                    }
                }
            }
            if (pendingToggleRow < 0)
            {
                return;
            }
            bool changed = (pendingToggleRow == PawnStatsRow
                ? ClCompat.Editor.PawnStatsShown(dialog)
                : ClCompat.Editor.CoverageShown(dialog)) != pendingToggleWas;
            if (changed || Time.frameCount - pendingToggleFrame > 2)
            {
                pendingToggleRow = -1;
                RefreshModel();
                AnnounceCurrentItem();
            }
        }

        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != HeaderRegion || region == null || region.Index < 0 || region.Index >= HeaderButtonCount
                || headerCaptureIndex[region.Index] < 0)
            {
                return default(Rect);
            }
            return headerRects[region.Index];
        }

        /// <summary>The header buttons are presented as content rows; every other text button belongs to a content row too.</summary>
        protected override bool KeepCapturedButton(string rawLabel)
        {
            return false;
        }

        // ---- Applied tags ----

        protected override int ContentColumnCount(int region)
        {
            return region == TagsRegion ? 4 : 0;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            switch (column)
            {
                case StateColumn:
                    return new TableColumnInfo(CompatText.ModText("CompositableLoadouts.Overview.State"), cellRole: ElementRole.Button);
                case ItemsColumn:
                    return new TableColumnInfo("RimWorldAccess.Compat.CompositableLoadouts.Editor.ItemsColumn".Translate());
                case EditColumn:
                    return new TableColumnInfo("Edit".Translate().ToString(), cellRole: ElementRole.Button);
                default:
                    return new TableColumnInfo("Delete".Translate().ToString(), cellRole: ElementRole.Button);
            }
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (region != TagsRegion || row < 0 || row >= elements.Count)
            {
                return "";
            }
            switch (column)
            {
                case StateColumn:
                    return ClCompat.ElementStateName(elements[row]);
                case ItemsColumn:
                    return elementItems[row];
                default:
                    return "";
            }
        }

        protected override string ContentCellHint(int region, int row, int column)
        {
            if (region != TagsRegion)
            {
                return null;
            }
            return column == EditColumn ? ChordDisplay(EditTagActionId)
                : column == RemoveColumn ? ChordDisplay(RemoveTagActionId)
                : null;
        }

        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (region != TagsRegion || row < 0 || row >= elements.Count)
            {
                return false;
            }
            switch (column)
            {
                case StateColumn:
                    ClCompat.OpenSetTagLoadoutState(elements[row]);
                    return true;
                case RemoveColumn:
                    RemoveTag(row);
                    return true;
                default:
                    EditTag(row);
                    return true;
            }
        }

        protected override bool CanReorderContentItem(int region, int index, int direction)
        {
            int target = index + direction;
            return region == TagsRegion && index >= 0 && index < elements.Count && target >= 0 && target < elements.Count;
        }

        protected override int ReorderContentItem(int region, int index, int direction)
        {
            if (!CanReorderContentItem(region, index, direction) || !ClCompat.Editor.SwapElements(loadout, index, direction))
            {
                return -1;
            }
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            return index + direction;
        }

        private int CurrentElementIndex()
        {
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != TagsRegion || region == null)
            {
                return -1;
            }
            int row = region.Index - 1;
            return row >= 0 && row < elements.Count ? row : -1;
        }

        private string TagNameAt(int index)
        {
            return ClCompat.TagName(ClCompat.ElementTag(elements[index])) ?? "";
        }

        private void EditTag(int index)
        {
            if (index >= 0 && index < elements.Count)
            {
                ClCompat.OpenTagEditor(ClCompat.ElementTag(elements[index]));
            }
        }

        private void RemoveTag(int index)
        {
            if (index < 0 || index >= elements.Count)
            {
                return;
            }
            string name = TagNameAt(index);
            // MUTATION-C: mirrors Dialog_LoadoutEditor.cs:166-168; the delete icon's body is inline in DrawTags.
            if (ClCompat.RemoveTag(pawn, elements[index]))
            {
                RefreshModel();
                TolkHelper.Speak("RimWorldAccess.Compat.CompositableLoadouts.Editor.TagRemoved".Loc(name));
            }
        }

        /// <summary>What a tag row's icons show: each item's name or alternatives tooltip, its count and "+N" badge, and a prosthetic conflict overlay's tooltip.</summary>
        private string ItemsSummary(object element, object slots)
        {
            bool active = ClCompat.ElementActive(element, shownState);
            var parts = new List<string>();
            foreach (object item in ClCompat.TagItems(ClCompat.ElementTag(element)))
            {
                ThingDef def = ClCompat.ItemRepresentativeDef(item);
                string label = def == null ? ClCompat.ItemLabel(item)
                    : ClCompat.ItemHasAlternatives(item)
                        ? CompatText.JoinSentences(new List<string>
                        {
                            CompatText.ModText("CompositableLoadouts.OneOfHeading"),
                            CompatText.Flatten(ClCompat.ItemRankedMembersDescription(item)),
                        })
                        : def.LabelCap.ToString();
                int quantity = ClCompat.ItemQuantity(item);
                if (quantity > 1)
                {
                    label = "RimWorldAccess.Compat.CompositableLoadouts.Editor.ItemCount".Translate(label, quantity);
                }
                parts.Add(label);
                if (ClCompat.ItemHasAlternatives(item))
                {
                    parts.Add(AlternativesBadge(item));
                }
                if (active && ClCompat.Editor.Conflicting(slots, item))
                {
                    object blocker = ClCompat.Editor.ConflictBlocker(slots, item);
                    parts.Add(blocker == null
                        ? CompatText.ModText("CompositableLoadouts.ProstheticNoRoom")
                        : CompatText.ModArgs("CompositableLoadouts.ProstheticConflict", ClCompat.TagName(blocker)));
                }
            }
            return CompatText.JoinSentences(parts);
        }

        private static string AlternativesBadge(object item)
        {
            return "RimWorldAccess.Compat.CompositableLoadouts.Editor.AlternativesBadge".Translate(ClCompat.ItemMembers(item).Count - 1);
        }

        // ---- Pawn switching ----

        /// <summary>
        /// The window's own previous and next pawn keys: vanilla's colonist cycling, whose
        /// postfix in the mod closes this editor and opens one for the newly selected pawn.
        /// </summary>
        private void SwitchPawn(bool next)
        {
            ListModel region = Model.CurrentRegion;
            pendingCarry = new[] { Model.RegionIndex, region == null ? 0 : region.Index };
            if (next)
            {
                ThingSelectionUtility.SelectNextColonist();
            }
            else
            {
                ThingSelectionUtility.SelectPreviousColonist();
            }
            if (dialog.IsOpen)
            {
                // No other loadout holder was selected, so this editor stays.
                pendingCarry = null;
                if (pawn != null)
                {
                    TolkHelper.SpeakData(pawn.LabelShortCap);
                }
            }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("RimWorldAccess.Compat.CompositableLoadouts.Editor.PreviousPawn".Translate(),
                    () => SwitchPawn(next: false), PreviousPawnActionId));
                actions.Add(new ScreenAction("RimWorldAccess.Compat.CompositableLoadouts.Editor.NextPawn".Translate(),
                    () => SwitchPawn(next: true), NextPawnActionId));
                actions.Add(new ScreenAction("CloseButton".Translate().ToString(), () => dialog.Close(),
                    SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        /// <summary>One read-only or single-action row of the statistics, pawn stats or coverage regions.</summary>
        private sealed class Row
        {
            public string Label;
            public string Extras;
            public string Hotkey;
            public ElementRole Role = ElementRole.None;
            public bool? Selected;
            public int? PositionIndex;
            public int? PositionCount;
            public Action Activate;

            public ElementDescription Describe()
            {
                return new ElementDescription
                {
                    Label = Label,
                    Extras = Extras,
                    Hotkey = Hotkey,
                    Role = Role,
                    Selected = Selected,
                    ReadOnly = Activate == null,
                    PositionIndex = PositionIndex,
                    PositionCount = PositionCount,
                };
            }
        }
    }
}
