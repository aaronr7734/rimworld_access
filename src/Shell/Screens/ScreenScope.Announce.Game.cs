using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    public abstract partial class ScreenScope : FocusScope
    {
        private ElementDescription DescribeCurrent(ListModel region)
        {
            ElementDescription d;
            if (InExtrasRegion())
            {
                d = DescribeExtrasRow(region.Index);
            }
            else if (InActionsRegion())
            {
                d = DescribeActionRow(region.Index);
            }
            else
            {
                d = DescribeContentItem(Model.RegionIndex, region.Index) ?? new ElementDescription();
            }
            if (!d.PositionIndex.HasValue)
            {
                d.PositionIndex = region.Position;
                d.PositionCount = region.Count;
            }
            return d;
        }

        private ElementDescription DescribeActionRow(int index)
        {
            ElementDescription d = new ElementDescription();
            d.Role = ElementRole.Button;
            if (index < capturedButtons.Count)
            {
                d.Label = CapturedButtonLabel(index, capturedButtons[index].Label);
                d.Hotkey = CapturedButtonHotkey(index);
            }
            else
            {
                IReadOnlyList<ScreenAction> declared = ResolvedDeclaredActions();
                int declaredIndex = index - capturedButtons.Count;
                if (declared != null && declaredIndex >= 0 && declaredIndex < declared.Count)
                {
                    ScreenAction action = declared[declaredIndex];
                    d.Label = action.Label;
                    d.Hotkey = ChordDisplay(action.ActionId);
                    if (action.Check.HasValue)
                    {
                        d.Role = ElementRole.Checkbox;
                        d.Check = action.Check;
                    }
                    d.Disabled = action.Disabled;
                    if (action.Disabled && !string.IsNullOrEmpty(action.DisabledReason))
                        d.Extras = action.DisabledReason;
                }
            }
            return d;
        }

        /// <summary>Speak the current item, shared-composer form (both-axes context in a table).</summary>
        protected void AnnounceCurrentItem()
        {
            AnnounceCurrent(CellAxis.Entry);
        }

        /// <summary>
        /// Speak the current item. In a table region the axis drives the delta grammar: row moves
        /// re-read the row identity, column moves the column name and tooltip, entry both.
        /// Protected so a screen re-orienting the user after its own move can pick the honest axis.
        /// </summary>
        protected void AnnounceCurrent(CellAxis axis)
        {
            // The single choke point every landing funnels through, and so the one place the
            // caller-gated tooltip harvest is asked for. Its results land on the next Layout pass
            // and survive the one after, so the tips are indexed by the next read.
            TooltipCapture.RequestHarvest();
            string text = ComposeCurrentText(axis);
            if (text != null)
                TolkHelper.SpeakData(text);
        }

        /// <summary>
        /// Optional prefix folded into the current item's plain landing announcement, in the SAME
        /// utterance rather than a second, interrupting Speak — the boundary-context channel for a
        /// structural change that would otherwise pass silently. Null speaks nothing extra.
        /// Consulted only from <see cref="ComposeCurrentText"/>'s plain, non-table, non-all-empty
        /// branch, the shape every arrow/Home/End/typeahead/reveal landing funnels through.
        /// </summary>
        protected virtual string AnnouncePrefix(int region, int index)
        {
            return null;
        }

        protected static string CombinePrefixFragments(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            return a + ". " + b;
        }

        /// <summary>
        /// The current item's composed announcement text without speaking it — the same
        /// three-branch grammar <see cref="AnnounceCurrent"/> uses, factored out so a caller
        /// wrapping it in a prefix of its own never hand-rolls the branching. Null when the screen
        /// has no regions, or when the row resolved with no label AND no role, which would leave a
        /// bare state word with nothing to attach it to.
        /// </summary>
        private string ComposeCurrentText(CellAxis axis)
        {
            // Consumed unconditionally on the first call after a focus. Entry speaks the item
            // alone: a screen reader announces the container only when the user moves INTO a
            // named section (the Tab press's AnnounceRegion), never as a frame around every
            // fresh-entry item.
            awaitingEntryAnnouncement = false;

            ListModel region = Model.CurrentRegion;
            if (region == null)
                return null;
            TableModel table = Model.CurrentTable;
            if (table != null && !Model.CurrentRegionIsEmpty)
            {
                ComposeOptions options = TextDialogShared.StandardComposeOptions();
                ElementDescription cell = DescribeCell(Model.RegionIndex, table, axis);
                return AnnouncementComposer.ComposeCell(cell, TranslatedShellVocabulary.Instance, options);
            }
            if (Model.CurrentRegionIsEmpty)
            {
                // Reachable only when EVERY region is empty, or on a region that declares itself
                // always navigable. Names the region so the screen says what it is instead of
                // going silent.
                ElementDescription empty = new ElementDescription();
                empty.Label = RegionNameFor(Model.RegionIndex);
                empty.Extras = "RimWorldAccess.Shell.Screen.EmptyRegion".Translate().ToString();
                return AnnouncementComposer.ComposeFocus(
                    empty, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions());
            }
            ElementDescription d = DescribeCurrent(region);
            if (string.IsNullOrWhiteSpace(d.Label) && d.Role == ElementRole.None)
            {
                // A row caught mid-rebuild, label and role both blank, would speak a bare state
                // word with nothing to attach it to. A legitimately unlabeled row always carries a
                // real Role — RoleFor is a deterministic function of the widget kind — so this
                // never fires for one. Skip; the next refresh re-announces it fully labeled.
                return null;
            }
            ApplyLevelChangeGate(d);
            string composed = AnnouncementComposer.ComposeFocus(
                d, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions());
            string prefix = AnnouncePrefix(Model.RegionIndex, region.Index);
            return string.IsNullOrEmpty(prefix) ? composed : prefix + ". " + composed;
        }

        /// <summary>
        /// Drops <paramref name="d"/>'s depth fragment when it repeats the depth already spoken in
        /// this region, keeping it on every real change. A row carrying no depth at all clears the
        /// gate, so stepping back onto a tree row states its depth again.
        /// </summary>
        private void ApplyLevelChangeGate(ElementDescription d)
        {
            if (LevelAlreadySpoken(d.Level))
            {
                d.Level = null;
                return;
            }
            RememberAnnouncedLevel(d.Level);
        }

        /// <summary>Whether <paramref name="level"/> only repeats the depth already spoken in this region.</summary>
        private bool LevelAlreadySpoken(int? level)
        {
            return level.HasValue
                && Model.RegionIndex == lastAnnouncedLevelRegion
                && lastAnnouncedLevel == level;
        }

        /// <summary>
        /// Records the depth an announcement just spoke. <see cref="AnnounceRegion"/> calls this
        /// rather than the gate: a Tab landing restates the depth to orient the user, but the
        /// arrow press after it must not.
        /// </summary>
        private void RememberAnnouncedLevel(int? level)
        {
            lastAnnouncedLevelRegion = Model.RegionIndex;
            lastAnnouncedLevel = level;
        }

        /// <summary>
        /// Wraps an already-composed item announcement in the current region's name
        /// ("Filters. &lt;itemText&gt;"), the grammar <see cref="AnnounceRegion"/> speaks on a
        /// Tab press. The region is a named container, not a tab: no role word. When the
        /// section-count setting is on, "section 2 of 3" trails the whole utterance, over the
        /// reachable sections only, unless <see cref="RegionNamesCarryPosition"/> opts out.
        /// </summary>
        private string ComposeWithRegionFrame(string itemText)
        {
            ElementDescription d = new ElementDescription();
            d.Label = RegionNameFor(Model.RegionIndex);
            d.Extras = itemText;
            string composed = AnnouncementComposer.ComposeFocus(
                d, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions());
            if (TextDialogShared.AnnounceTabCount && !RegionNamesCarryPosition)
            {
                int count = Model.NonEmptyRegionCount;
                int position = Model.NonEmptyRegionPosition;
                if (count > 1 && position > 0)
                {
                    composed += ". " + "RimWorldAccess.Shell.Screen.SectionPosition"
                        .Translate(position, count).ToString();
                }
            }
            return composed;
        }

        /// <summary>
        /// The current cell's composed announcement text without speaking it, for screens that
        /// wrap it in a phrase of their own. Null outside a table region. Keeps the grammar in the
        /// composer even when the screen assembles the utterance.
        /// </summary>
        protected string ComposeCurrentCellText(CellAxis axis)
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table == null)
                return null;
            ComposeOptions options = TextDialogShared.StandardComposeOptions();
            ElementDescription cell = DescribeCell(Model.RegionIndex, table, axis);
            return AnnouncementComposer.ComposeCell(cell, TranslatedShellVocabulary.Instance, options);
        }

        /// <summary>
        /// The changed-state-only announcement after a cell mutation: one announcement per action,
        /// the new state alone, never the row identity the user already knows.
        /// </summary>
        protected void AnnounceCurrentCellStateChange()
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            ListModel region = Model.CurrentRegion;
            if (table == null || region == null || region.Index <= 0)
                return;
            string value = ContentCellText(Model.RegionIndex, region.Index - 1, table.ColumnIndex);
            if (!string.IsNullOrEmpty(value))
            {
                TolkHelper.SpeakData(value);
            }
        }

        /// <summary>
        /// The composer description for the current table cell: the header row describes the
        /// column itself, data rows pair the row identity with the current column's value.
        /// </summary>
        private ElementDescription DescribeCell(int regionIndex, TableModel table, CellAxis axis)
        {
            int column = table.ColumnIndex;
            TableColumnInfo info = ContentColumnInfo(regionIndex, column) ?? new TableColumnInfo();
            ElementDescription d = new ElementDescription();
            d.Role = ElementRole.TableCell;
            d.Axis = axis;
            d.ColumnName = info.Label;
            d.RowIndex = table.Rows.Position;
            d.RowCount = table.Rows.Count;
            d.ColumnIndex = table.ColumnPosition;
            d.ColumnCount = table.ColumnCount;

            int modelRow = table.Rows.Index;
            if (modelRow == 0)
            {
                d.IsHeaderCell = true;
                d.Sortable = info.Sortable;
                if (table.SortColumnIndex == column)
                {
                    d.SortDescending = table.SortDescending;
                }
                d.ColumnTooltip = info.HeaderTip;
                if (info.Sortable)
                {
                    d.Hint = "RimWorldAccess.Shell.Table.SortHint".Translate().ToString();
                }
                return d;
            }

            int row = modelRow - 1;
            ElementDescription identity = DescribeContentItem(regionIndex, row) ?? new ElementDescription();
            d.Label = identity.Label;
            // A button-column cell keeps the cell grammar but adds its control role word.
            if (info.CellRole != ElementRole.None)
            {
                d.Role = info.CellRole;
            }
            d.Value = ContentCellText(regionIndex, row, column);
            d.Selected = ContentCellSelected(regionIndex, row, column);
            d.ColumnTooltip = info.HeaderTip;
            string cellTip = ContentCellTip(regionIndex, row, column);
            d.Extras = axis == CellAxis.Column
                ? cellTip
                : TooltipTextJoin.Append(ContentRowTip(regionIndex, row), new[] { cellTip });
            return d;
        }

        /// <summary>
        /// Speak a region switch: the region's name, with the item under the cursor folded into
        /// the same utterance. Table regions fold their dimensions in first, verbosity-gated.
        /// </summary>
        protected virtual void AnnounceRegion()
        {
            // This utterance IS the fresh-entry announcement — same region frame, same item
            // composition — so it consumes the pending one rather than letting AfterFocusDispatch
            // speak the identical sentence again in the same frame.
            awaitingEntryAnnouncement = false;

            string extras;
            ListModel region = Model.CurrentRegion;
            TableModel table = Model.CurrentTable;
            if (region == null || Model.CurrentRegionIsEmpty)
            {
                extras = "RimWorldAccess.Shell.Screen.EmptyRegion".Translate().ToString();
            }
            else if (table != null)
            {
                var parts = new List<string>();
                if (TextDialogShared.AnnounceTableDimensions)
                {
                    parts.Add(TranslatedShellVocabulary.Instance.TableDimensions(table.ColumnCount, table.Rows.Count));
                }
                int modelRow = table.Rows.Index;
                string resting;
                if (modelRow == 0)
                {
                    TableColumnInfo info = ContentColumnInfo(Model.RegionIndex, table.ColumnIndex);
                    resting = info == null ? null : info.Label;
                }
                else
                {
                    ElementDescription identity = DescribeContentItem(Model.RegionIndex, modelRow - 1);
                    resting = identity == null ? null : identity.Label;
                }
                if (!string.IsNullOrEmpty(resting))
                {
                    parts.Add(resting);
                }
                extras = string.Join(". ", parts.ToArray());
            }
            else
            {
                // Fold the full item announcement in, spoken exactly as an arrow press would, so
                // one Tab press orients the user to both the region and the item.
                ElementDescription item = DescribeCurrent(region);
                extras = AnnouncementComposer.ComposeFocus(
                    item, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions());
                RememberAnnouncedLevel(item.Level);
            }
            if (Model.RegionIndex < ContentRegionCount)
            {
                extras = CombinePrefixFragments(ContentRegionEntryDetail(Model.RegionIndex), extras);
            }
            // The same region-tab frame the fresh-entry announcement borrows, so the two cannot
            // read as different phrasings of "where you are."
            TolkHelper.SpeakData(ComposeWithRegionFrame(extras));
        }

        /// <summary>Fragment spoken between a content region's name and its item on region ENTRY only (a per-section figure from its header); null adds nothing.</summary>
        protected virtual string ContentRegionEntryDetail(int region)
        {
            return null;
        }

        /// <summary>
        /// The "2 sections" fragment for a screen's open announcement, or null when the setting is
        /// off or fewer than two regions are reachable — empty regions are not navigable and not
        /// counted. Subclasses fold it into their first-focus announcement.
        /// </summary>
        protected string TabCountFragment()
        {
            RefreshModel();
            int count = Model.NonEmptyRegionCount;
            if (!TextDialogShared.AnnounceTabCount || count < 2)
                return null;
            return TranslatedShellVocabulary.Instance.TabCount(count);
        }
    }

}
