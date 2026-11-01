using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    public abstract partial class ScreenScope : FocusScope
    {
        /// <summary>
        /// The Enter/menus.activate claim's target. Stamps the accept frame first —
        /// unconditionally, so vanilla's OnAcceptKeyPressed stays blocked whichever branch handles
        /// the press — then hands off to the two-step confirm when this screen names a
        /// <see cref="DefaultAcceptActionId"/> and the current element is inert. The shared Space
        /// alias calls <see cref="ActivateCurrent"/> directly and never reaches here, so Space
        /// always acts and never routes.
        /// </summary>
        private void OnActivateChord()
        {
            ShellFrameStamps.MarkAcceptConsumed();
            if (TryHandleAcceptChord())
                return;
            if (HasDefaultAccept && TryHandleDefaultAcceptDoublePress())
                return;
            DisarmDefaultAccept();
            ActivateCurrent();
        }

        /// <summary>
        /// Seam for a screen where Enter is a screen-level verb rather than row activation.
        /// Return true once the press is fully handled; false leaves Enter on its normal path.
        /// The shared Space alias never reaches here, so Space keeps activating the row.
        /// </summary>
        protected virtual bool TryHandleAcceptChord()
        {
            return false;
        }

        /// <summary>
        /// The double-press confirm. False falls through to the normal
        /// <see cref="ActivateCurrent"/> dispatch — a misconfigured id, a live typeahead search,
        /// or an element with real Enter behavior — and the caller disarms in every such case.
        /// True once the press is fully handled: armed, fired, or a same-refresh relocation absorbed.
        /// </summary>
        private bool TryHandleDefaultAcceptDoublePress()
        {
            ScreenAction action = ResolveDefaultAccept();
            if (action == null || TypeaheadHasActiveSearch)
                return false;
            RefreshModel();
            if (relocatedThisRefresh)
            {
                DisarmDefaultAccept();
                return true;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return false;
            if (!IsCurrentElementInertForDefaultAccept(region))
                return false;
            int regionIndex = Model.RegionIndex;
            int itemIndex = region.Index;
            if (armedDefaultAcceptRegion == regionIndex && armedDefaultAcceptIndex == itemIndex)
            {
                FireDefaultAccept(action);
            }
            else
            {
                ArmDefaultAccept(regionIndex, itemIndex, action);
            }
            return true;
        }

        /// <summary>
        /// Inert test: the element's fresh description has no control semantics
        /// (<see cref="ElementRole.None"/>), is explicitly <see cref="ElementDescription.ReadOnly"/>,
        /// is an already-selected radio button — either selection channel counts, since
        /// auto-select-on-landing radios set only one depending on the describe path — or is a
        /// slider/stepper without <see cref="ElementDescription.EntersEditOnAccept"/>, since a
        /// plain slider owns Left/Right and never Enter. Table cells never qualify: a header cell
        /// sorts and a data cell's row activation is real Enter behavior. Subclasses extend the
        /// test positionally through <see cref="DefaultAcceptRowInert"/>.
        /// </summary>
        private bool IsCurrentElementInertForDefaultAccept(ListModel region)
        {
            if (Model.CurrentTable != null)
                return false;
            if (DefaultAcceptInertness.IsInert(DescribeCurrent(region)))
                return true;
            return DefaultAcceptRowInert(Model.RegionIndex, region.Index);
        }

        /// <summary>
        /// Subclass extension of the inert test: true when the row at (region, row) has no real
        /// Enter behavior despite control semantics <see cref="DefaultAcceptInertness"/> cannot
        /// see past. Indices follow the ActivateContentItem convention and may name ANY region,
        /// Buttons and captured extras included — an override must reject those itself.
        /// </summary>
        protected virtual bool DefaultAcceptRowInert(int region, int row)
        {
            return false;
        }

        /// <summary>Arms the confirm at the given position and speaks the "press again" prompt.</summary>
        private void ArmDefaultAccept(int regionIndex, int itemIndex, ScreenAction action)
        {
            armedDefaultAcceptRegion = regionIndex;
            armedDefaultAcceptIndex = itemIndex;
            TolkHelper.SpeakData("RimWorldAccess.Shell.Screen.PressEnterAgain".Translate(action.Label).ToString());
        }

        /// <summary>
        /// Fires the second press: disarms, invokes the action as the toolbar would, honors a
        /// disabled default's refusal instead of firing, and names the button since the cursor is
        /// not on it.
        /// </summary>
        private void FireDefaultAccept(ScreenAction action)
        {
            DisarmDefaultAccept();
            if (action.Disabled)
            {
                TolkHelper.SpeakData(ComposeDisabledDefaultAcceptRefusal(action));
                return;
            }
            // Nothing else in the stream names what fired, and this must be spoken BEFORE the
            // action runs: a proceed button usually replaces the screen.
            TolkHelper.SpeakData("RimWorldAccess.Shell.Screen.PressingDefault"
                .Translate(action.Label).ToString());
            if (action.Activate != null)
                action.Activate();
        }

        /// <summary>
        /// The disabled-default-action refusal wording, shared by <see cref="FireDefaultAccept"/>
        /// and <see cref="OnActivateDefaultChord"/> so the two paths cannot drift.
        /// </summary>
        private string ComposeDisabledDefaultAcceptRefusal(ScreenAction action)
        {
            string refusal = "RimWorldAccess.Shell.GenericWindow.Disabled".Loc(action.Label).ToString();
            if (!string.IsNullOrEmpty(action.DisabledReason))
                refusal = refusal + " " + action.DisabledReason;
            return refusal;
        }

        /// <summary>
        /// Shift+Enter's target: presses <see cref="DefaultAcceptActionId"/> immediately from
        /// wherever the cursor sits, skipping the inert-row confirm. The announcement leads with
        /// the button's name and is spoken BEFORE the action runs, since proceeding usually
        /// replaces the screen. Stamps the accept frame first: Shift+Return still matches
        /// vanilla's Accept KeyBindingDef, because modifiers are not part of vanilla's bindings.
        /// The ctor claim's guard means this runs only when a proceed button is named, but
        /// <see cref="ResolveDefaultAccept"/> can still miss a misdeclared id, hence the
        /// NoDefaultButton fallback.
        /// </summary>
        private void OnActivateDefaultChord()
        {
            ShellFrameStamps.MarkAcceptConsumed();
            DisarmDefaultAccept();
            TypeaheadReset();
            ScreenAction action = ResolveDefaultAccept();
            if (action == null)
            {
                TolkHelper.SpeakData("RimWorldAccess.Shell.Screen.NoDefaultButton".Translate().ToString());
                return;
            }
            if (action.Disabled)
            {
                TolkHelper.SpeakData(ComposeDisabledDefaultAcceptRefusal(action));
                return;
            }
            TolkHelper.SpeakData("RimWorldAccess.Shell.Screen.PressingDefault".Translate(action.Label).ToString());
            if (action.Activate != null)
                action.Activate();
        }

        private void DisarmDefaultAccept()
        {
            armedDefaultAcceptRegion = -1;
            armedDefaultAcceptIndex = -1;
        }

        /// <summary>Finds a declared action by id, or null — the source of truth for <see cref="DefaultAcceptActionId"/>'s label/Activate/disabled state.</summary>
        private ScreenAction FindDeclaredAction(string actionId)
        {
            IReadOnlyList<ScreenAction> declared = ResolvedDeclaredActions();
            if (declared == null)
                return null;
            for (int i = 0; i < declared.Count; i++)
            {
                if (declared[i].ActionId == actionId)
                    return declared[i];
            }
            return null;
        }

        /// <summary>
        /// Enter handler, also reusable by a screen's own Space-style aliases: stamps the accept
        /// frame, then activates the current row. In a table region the header sorts and a data
        /// cell tries the cell first, then the row's declared default action. A live typeahead
        /// search simply ends here and the row activates as usual; Shift+Enter
        /// (SharedMenuGrammar.SearchSettle) is the "land without running" gesture.
        /// </summary>
        protected void ActivateCurrent()
        {
            ShellFrameStamps.MarkAcceptConsumed();
            TypeaheadReset();
            RefreshModel();
            if (relocatedThisRefresh)
                return;
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return;
            if (InExtrasRegion())
            {
                ActivateExtrasRow(region.Index);
                return;
            }
            if (InActionsRegion())
            {
                ActivateActionRow(region.Index);
                return;
            }
            TableModel table = Model.CurrentTable;
            if (table != null)
            {
                if (region.Index == 0)
                {
                    ToggleSortCurrentColumn();
                    return;
                }
                int dataRow = region.Index - 1;
                if (!ActivateContentCell(Model.RegionIndex, dataRow, table.ColumnIndex))
                {
                    ActivateContentItem(Model.RegionIndex, dataRow);
                }
                return;
            }
            ActivateContentItem(Model.RegionIndex, region.Index);
        }

        protected void AnnounceCurrentCellReadOnly()
        {
            TableModel table = Model.CurrentTable;
            if (table == null)
                return;
            TableColumnInfo info = ContentColumnInfo(Model.RegionIndex, table.ColumnIndex);
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.SpeakData(info == null || string.IsNullOrEmpty(info.Label)
                ? TranslatedShellVocabulary.Instance.Word(ElementStateWord.ReadOnly)
                : "RimWorldAccess.Shell.Table.CellReadOnly".Translate(info.Label).ToString());
        }

        /// <summary>
        /// Guard for the shared Space activation alias
        /// (<see cref="SharedMenuGrammar.ActivateAlias"/>). Registered through
        /// <see cref="FocusScope.ClaimFallback"/>, so it is offered only after every claim the
        /// screen makes itself: a screen whose Space mutates a table cell or steps a training bar
        /// keeps that meaning where its own claim's <c>when</c> applies, and Space activates the
        /// focused row everywhere else. Ordering, not a structural stand-down — standing down
        /// would kill Space across a WHOLE screen the moment one row kind claimed it.
        ///
        /// The one exception is typeahead: the keyboard delivers Space as twin events (keyCode
        /// then character), and the keyCode event would settle the search before ' ' reached
        /// <see cref="HandleChar"/>, killing multiword search. Standing down lets the keyCode
        /// event fall to the modal swallow and the character twin extend the buffer.
        /// </summary>
        private bool ActivateAliasClaimable()
        {
            return !TypeaheadHasActiveSearch;
        }

        /// <summary>Guard for the shared Alt+S chord: only inside a table region, and only where the screen allows it.</summary>
        private bool SortChordClaimable()
        {
            if (!EnableSortChord)
                return false;
            RefreshModel();
            return Model.CurrentTable != null;
        }

        /// <summary>
        /// Advances the vanilla 3-state sort cycle on the current column. The subclass re-orders
        /// through <see cref="ApplyContentSort"/>; the cursor follows its row, and one utterance
        /// speaks the new sort state plus the cell.
        /// </summary>
        protected void ToggleSortCurrentColumn()
        {
            // A re-order invalidates the search's cached row indices.
            TypeaheadReset();
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table == null)
                return;
            int regionIndex = Model.RegionIndex;
            int column = table.ColumnIndex;
            TableColumnInfo info = ContentColumnInfo(regionIndex, column) ?? new TableColumnInfo();
            SortCycleResult cycle = table.ToggleSortCycle(info.Sortable);
            if (cycle == SortCycleResult.NotSortable)
            {
                TolkHelper.SpeakData("RimWorldAccess.Shell.Table.NotSortable".Translate().ToString());
                return;
            }
            int currentDataRow = table.Rows.Index - 1;
            int newDataRow = ApplyContentSort(regionIndex, column, cycle, currentDataRow);
            RefreshModel();
            if (newDataRow >= 0 && newDataRow + 1 < table.Rows.Count)
            {
                table.Rows.MoveTo(newDataRow + 1);
            }

            string sortPhrase;
            if (cycle == SortCycleResult.Cleared)
            {
                sortPhrase = "RimWorldAccess.Shell.Table.SortCleared".Translate().ToString();
            }
            else
            {
                string direction = (cycle == SortCycleResult.SortedDescending
                    ? "RimWorldAccess.Common.SortDescending"
                    : "RimWorldAccess.Common.SortAscending").Translate().ToString();
                sortPhrase = "RimWorldAccess.Shell.Table.SortedBy".Translate(info.Label ?? "", direction).ToString();
            }
            ComposeOptions options = TextDialogShared.StandardComposeOptions();
            ElementDescription cell = DescribeCell(regionIndex, table, CellAxis.Entry);
            TolkHelper.SpeakData(sortPhrase + ". " + AnnouncementComposer.ComposeCell(
                cell, TranslatedShellVocabulary.Instance, options));
        }

        private void ActivateActionRow(int index)
        {
            if (index < capturedButtons.Count)
            {
                // Vanilla's inline click handler runs on the next armed pass and speaks for
                // itself. Owner-tagged so a stacked window's pass cannot steal or drop it.
                ButtonTextCapture.RequestClick(capturedButtonSourceIndex[index], this);
                return;
            }
            IReadOnlyList<ScreenAction> declared = ResolvedDeclaredActions();
            int declaredIndex = index - capturedButtons.Count;
            if (declared != null && declaredIndex >= 0 && declaredIndex < declared.Count)
            {
                ScreenAction action = declared[declaredIndex];
                if (action.Disabled)
                {
                    // The extras rows' refusal wording; the reason, when supplied, follows.
                    string refusal = "RimWorldAccess.Shell.GenericWindow.Disabled".Loc(action.Label).ToString();
                    if (!string.IsNullOrEmpty(action.DisabledReason))
                        refusal = refusal + " " + action.DisabledReason;
                    TolkHelper.SpeakData(refusal);
                    return;
                }
                if (action.Activate != null)
                    action.Activate();
            }
        }

        /// <summary>
        /// Enter on an extras row: read-only re-announces, a disabled operable row speaks the
        /// standard refusal, and otherwise the control kind picks the channel —
        /// Button/Checkbox/RadioButton/Tab fire vanilla's own handler through WidgetCapture's live
        /// descriptor channel (vehicle A), Slider nudges one step, TextField opens an edit session.
        /// </summary>
        private void ActivateExtrasRow(int index)
        {
            if (index < 0 || index >= extrasRows.Count)
                return;
            ExtrasRow row = extrasRows[index];
            if (row.Member == null)
            {
                AnnounceCurrentItem();
                return;
            }
            if (row.Disabled)
            {
                TolkHelper.Speak("RimWorldAccess.Shell.GenericWindow.Disabled".Loc(row.Label));
                return;
            }
            InteractiveMember member = row.Member;
            switch (member.Kind)
            {
                case WidgetKind.TextField:
                    BeginExtrasEdit(row, member);
                    break;
                case WidgetKind.Slider:
                    // A plain slider owns Left/Right and nothing else, so it describes as inert
                    // and Enter reaches the screen's proceed button instead. Never the default
                    // click below: a click on a slider track jumps the value to that position.
                    AnnounceCurrentItem();
                    break;
                case WidgetKind.Checkbox:
                case WidgetKind.RadioButton:
                case WidgetKind.Tab:
                    PostExtrasActivate(member);
                    ArmExtrasPendingChange(index);
                    break;
                default:
                    // Silence otherwise: the injected click's own side effect announces itself.
                    PostExtrasActivate(member);
                    break;
            }
        }

        /// <summary>
        /// The extras region's funnel onto the live activation channel; see
        /// <see cref="GenericWindowScope.PostActivate"/> for why a window this click opens is, by
        /// construction, the surface the player asked for.
        /// </summary>
        private void PostExtrasActivate(InteractiveMember member)
        {
            ScopeForWindow.ArmDeliberateGenericAttach();
            WidgetCapture.RequestActivate(member.Kind, member.RawLabel, member.Ordinal);
        }

        /// <summary>
        /// Snapshots the acted-on row's comparable state before an injection posts its change.
        /// The capture tap records what a pass DRAWS and the field assignment lands only after
        /// that pass's capture, so the state-change announcement waits for a later pass whose
        /// rebuilt row actually differs.
        /// </summary>
        private void ArmExtrasPendingChange(int index)
        {
            ExtrasRow row = extrasRows[index];
            extrasPendingChange = new ExtrasPendingChange
            {
                Index = index,
                Check = row.Check,
                Selected = row.Selected,
                SliderValue = row.Member != null && row.Member.Source != null ? row.Member.Source.SliderValue : 0f,
                PassesLeft = 3,
            };
        }

        /// <summary>
        /// Consumes <see cref="extrasPendingChange"/> once the rebuilt row reflects the change,
        /// or on timeout announces the still-unchanged state — the honest "clamped or rejected"
        /// case, where AtMinimum/AtMaximum is the useful part. Clears silently when the row fell
        /// out of range.
        /// </summary>
        private void ResolveExtrasPendingChange()
        {
            ExtrasPendingChange pending = extrasPendingChange.Value;
            if (pending.Index >= extrasRows.Count)
            {
                extrasPendingChange = null;
                return;
            }
            ExtrasRow row = extrasRows[pending.Index];
            float currentSlider = row.Member != null && row.Member.Source != null ? row.Member.Source.SliderValue : 0f;
            bool changed = row.Check != pending.Check || row.Selected != pending.Selected || currentSlider != pending.SliderValue;
            if (!changed && --pending.PassesLeft > 0)
            {
                extrasPendingChange = pending;
                return;
            }
            extrasPendingChange = null;
            AnnounceExtrasStateChange(row);
        }

        private void AnnounceExtrasStateChange(ExtrasRow row)
        {
            var d = new ElementDescription();
            if (row.Check.HasValue)
            {
                d.Check = row.Check;
            }
            else if (row.Selected.HasValue)
            {
                d.Selected = row.Selected;
            }
            else if (row.Member != null && row.Member.Kind == WidgetKind.Slider)
            {
                d.Value = row.Value;
                d.AtMinimum = row.AtMinimum;
                d.AtMaximum = row.AtMaximum;
            }
            else
            {
                return;
            }
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        /// <summary>
        /// Opens a text-edit session for an extras TextField row. The capture index is resolved
        /// ONCE at edit start and reused for the session, since RequestTextOverride is
        /// index-addressed and the window's draw order does not reshuffle while the user types.
        /// </summary>
        private void BeginExtrasEdit(ExtrasRow row, InteractiveMember member)
        {
            extrasEditingCaptureIndex = RimWorldAccess.CaptureDescriptor.FindIndex(
                widgetSnapshot, WidgetKind.TextField, member.RawLabel, member.Ordinal);
            TextFieldSpec spec = member.Source != null && member.Source.MultiLine
                ? TextFieldSpec.MultiLineUnrestricted("RimWorldAccess.TextInput.LabelDefault")
                : TextFieldSpec.Unrestricted("RimWorldAccess.TextInput.LabelDefault");
            extrasEditSession.EnterEdit(
                member.Source != null ? member.Source.Text : "",
                spec,
                row.Label,
                ApplyExtrasEditedText,
                ReAnnounceExtrasRow);
        }

        private void ApplyExtrasEditedText(string value)
        {
            WidgetCapture.RequestTextOverride(extrasEditingCaptureIndex, value);
        }

        private void ReAnnounceExtrasRow()
        {
            WidgetCapture.ClearTextOverride();
            extrasEditingCaptureIndex = -1;
            AnnounceCurrentItem();
        }

        private ElementDescription DescribeExtrasRow(int index)
        {
            if (index < 0 || index >= extrasRows.Count)
            {
                return new ElementDescription();
            }
            return CapturedExtrasRows.Describe(extrasRows[index]);
        }

        private void OnAdjust(int direction)
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty || InActionsRegion() || InExtrasRegion())
                return;
            AdjustContentItem(Model.RegionIndex, region.Index, direction);
        }

        // Shared reorder grammar (Ctrl+Up/Down): the keyboard equivalent of drag-and-drop.

        /// <summary>
        /// Resolves the region/item indices for a reorder chord. Refreshes the model first so a
        /// stale cursor never drives the check, and stands down when the refresh itself relocated
        /// the cursor, so a reorder never fires against a row the user landed on by surprise.
        /// False outside a content item: Buttons and captured extras never reorder, and a table's
        /// header row is not an item. Table item indices are DATA rows, matching
        /// <see cref="ApplyContentSort"/>.
        /// </summary>
        private bool TryGetReorderTarget(out int regionIndex, out int itemIndex)
        {
            RefreshModel();
            regionIndex = -1;
            itemIndex = -1;
            if (relocatedThisRefresh || InActionsRegion() || InExtrasRegion())
                return false;
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return false;
            TableModel table = Model.CurrentTable;
            if (table != null)
            {
                if (region.Index <= 0)
                    return false;
                itemIndex = region.Index - 1;
            }
            else
            {
                itemIndex = region.Index;
            }
            regionIndex = Model.RegionIndex;
            return true;
        }

        /// <summary>Guard for the shared Ctrl+Up/Down claims.</summary>
        private bool ReorderClaimable(int direction)
        {
            int regionIndex, itemIndex;
            if (!TryGetReorderTarget(out regionIndex, out itemIndex))
                return false;
            return CanReorderContentItem(regionIndex, itemIndex, direction);
        }

        /// <summary>
        /// Ctrl+Up/Down: asks the subclass to move, follows the item to its new position, and
        /// speaks one utterance — <see cref="ReorderDestinationPhrase"/> plus the standard item
        /// announcement, or a localized refusal when the subclass reports -1.
        /// </summary>
        private void ReorderCurrent(int direction)
        {
            int regionIndex, itemIndex;
            if (!TryGetReorderTarget(out regionIndex, out itemIndex))
                return;
            int newIndex = ReorderContentItem(regionIndex, itemIndex, direction);
            RefreshModel();
            if (newIndex < 0)
            {
                TolkHelper.SpeakData("RimWorldAccess.Shell.Screen.CannotReorder".Translate().ToString());
                return;
            }
            TableModel table = Model.CurrentTable;
            if (table != null)
            {
                table.Rows.MoveTo(newIndex + 1);
            }
            else
            {
                ListModel region = Model.CurrentRegion;
                if (region != null && !region.IsEmpty)
                    region.MoveTo(newIndex);
            }
            string moved = ReorderDestinationPhrase(regionIndex, newIndex);
            TolkHelper.SpeakData(moved + " " + ComposeCurrentText(CellAxis.Row));
        }

        /// <summary>
        /// Names the item's NEW neighbors so the user hears where the move landed. Reads
        /// <see cref="DescribeContentItem"/> at the neighbor indices — the same row-identity
        /// accessor <see cref="DescribeCell"/> uses — so it resolves identically for a plain list
        /// and a table region. Falls back to the bare "Moved." fragment when a neighbor's label is
        /// missing.
        /// </summary>
        private string ReorderDestinationPhrase(int regionIndex, int newIndex)
        {
            string fallback = "RimWorldAccess.Shell.Screen.Moved".Translate().ToString();
            int count = ContentItemCount(regionIndex);
            bool hasAbove = newIndex > 0;
            bool hasBelow = newIndex < count - 1;

            if (hasAbove && hasBelow)
            {
                string above = NeighborLabel(regionIndex, newIndex - 1);
                string below = NeighborLabel(regionIndex, newIndex + 1);
                if (string.IsNullOrEmpty(above) || string.IsNullOrEmpty(below))
                    return fallback;
                return "RimWorldAccess.Shell.Screen.MovedBetween".Translate(above, below).ToString();
            }
            if (!hasAbove)
            {
                string below = NeighborLabel(regionIndex, newIndex + 1);
                if (string.IsNullOrEmpty(below))
                    return fallback;
                return "RimWorldAccess.Shell.Screen.MovedToTop".Translate(below).ToString();
            }
            string above2 = NeighborLabel(regionIndex, newIndex - 1);
            if (string.IsNullOrEmpty(above2))
                return fallback;
            return "RimWorldAccess.Shell.Screen.MovedToBottom".Translate(above2).ToString();
        }

        private string NeighborLabel(int regionIndex, int itemIndex)
        {
            ElementDescription d = DescribeContentItem(regionIndex, itemIndex);
            return d == null ? null : d.Label;
        }

        // Announcements: all through the shared composer, never hand-built.

        private string RegionNameFor(int region)
        {
            ScreenRegionLayout layout = RegionLayout();
            if (layout.KindOf(region) == ScreenRegionKind.Extras)
                return ExtrasRegionName;
            if (region == layout.ActionsRegionIndex)
                return ActionsRegionName;
            return ContentRegionName(region);
        }

    }
}
