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
        /// Re-reads policy, content counts and the Buttons region into the model, then reconciles
        /// the region cursor against what is now empty (<see cref="ReconcileEmptyRegion"/>) — the
        /// only place a region emptying out under the cursor is handled.
        /// </summary>
        protected void RefreshModel()
        {
            relocatedThisRefresh = false;
            // Read BEFORE SetRegions below overwrites the region item counts; the field
            // doc explains why this, not Model.RegionCount, is the latch.
            if (Model.NonEmptyRegionCount > 0)
            {
                modelEverHadItems = true;
            }
            try
            {
                RefreshContent();
            }
            catch (Exception ex)
            {
                // A scope whose model build throws must fail SAFE, not silent: otherwise it
                // stays a live modal that announces nothing and swallows every key, Escape
                // included. Logged once per SCOPE TYPE, since a broken scope throws on every
                // pass.
                Log.ErrorOnce("[RimWorldAccess] " + Name + " RefreshContent threw: " + ex,
                    GetType().GetHashCode() ^ 0x5343464C);
                if (!announcedModelFailure)
                {
                    announcedModelFailure = true;
                    TolkHelper.SpeakData("RimWorldAccess.Shell.ScopeFailed".Translate().ToString());
                }
                modelFailed = true;
                Model.SetRegions(new List<RegionSpec>(), WrapItems);
                return;
            }
            if (modelFailed)
            {
                // Recovered: a later RefreshContent succeeded, so the scope goes live again
                // with no announcement.
                modelFailed = false;
                announcedModelFailure = false;
            }
            Model.WrapRegions = WrapTabs;
            Model.RememberPositions = RememberTabPositions;
            regionSpecs.Clear();
            int content = ContentRegionCount;
            for (int i = 0; i < content; i++)
            {
                int columns = ContentColumnCount(i);
                int items = ContentItemCount(i);
                // Only content regions can be always-navigable; the extras and Buttons specs
                // below keep the plain empty-region rule.
                bool navigable = ContentRegionAlwaysNavigable(i);
                // Table regions carry their header row as row 0 of the model.
                regionSpecs.Add(columns > 0
                    ? new RegionSpec(items + 1, columns, navigable)
                    : new RegionSpec(items, alwaysNavigable: navigable));
            }
            if (HasExtrasRegion())
            {
                regionSpecs.Add(new RegionSpec(extrasRows.Count));
            }
            if (HasActionsRegion())
            {
                regionSpecs.Add(new RegionSpec(capturedButtons.Count + DeclaredActionCount()));
            }
            Model.SetRegions(regionSpecs, WrapItems);
            // Only skip a Buttons region reader flow can still reach: the flow needs a
            // non-empty flowing content region to leave from, or the toolbar is unreachable.
            Model.CycleSkipRegion = !ActionsRegionInTabCycle && HasActionsRegion() && LastFlowingContentRegion() >= 0
                ? ActionsRegionIndex()
                : -1;
            ReconcileEmptyRegion();
        }

        /// <summary>
        /// Moves the cursor off a region that no longer exists for navigation — an emptied extras
        /// or Buttons region, or a content region whose <see cref="ContentRegionAlwaysNavigable"/>
        /// opt-out says it is not currently drawn — onto the nearest region holding something,
        /// through the same change/sound/announce sequence Tab uses. Default content regions are
        /// never reconciled away: the cursor may rest in one while empty, and every landing path
        /// announces that. Every pass before <see cref="modelEverHadItems"/> goes true stays
        /// silent, not just the first: a screen pushed before vanilla has drawn anything can
        /// trickle real items in over several passes, and that must read as a first build, not as
        /// "it just emptied out." Nothing happens when EVERY region is empty — the cursor stays
        /// put and the empty-region announcement says so.
        /// </summary>
        private void ReconcileEmptyRegion()
        {
            if (reconcilingEmptyRegion || Model.IsRegionNavigable(Model.RegionIndex))
                return;
            reconcilingEmptyRegion = true;
            try
            {
                MoveResult result = Model.MoveToNearestNonEmptyRegion();
                if (!result.Changed)
                    return;
                TypeaheadReset();
                OnRegionChanged(result);
                if (!modelEverHadItems)
                    return;
                if (TabSwitchSound != null)
                {
                    TabSwitchSound.PlayOneShotOnCamera();
                }
                AnnounceRegion();
                relocatedThisRefresh = true;
            }
            finally
            {
                reconcilingEmptyRegion = false;
            }
        }

        /// <summary>True when the <see cref="RefreshModel"/> just run relocated the cursor out of an emptied region and spoke the landing; the caller then stands down.</summary>
        protected bool RelocatedThisRefresh
        {
            get { return relocatedThisRefresh; }
        }

        protected bool HasActionsRegion()
        {
            return IncludeActionsRegion
                && (capturedButtons.Count > 0 || DeclaredActionCount() > 0);
        }

        private bool HasExtrasRegion()
        {
            return WantsWidgetCapture && extrasRows.Count > 0;
        }

        private int DeclaredActionCount()
        {
            IReadOnlyList<ScreenAction> declared = ResolvedDeclaredActions();
            return declared == null ? 0 : declared.Count;
        }

        /// <summary>
        /// This pass's region layout — content, then extras if it has rows, then Buttons if
        /// it has any. Rebuilt per call because every input can change between passes.
        /// </summary>
        private ScreenRegionLayout RegionLayout()
        {
            return new ScreenRegionLayout(ContentRegionCount, HasExtrasRegion(), HasActionsRegion());
        }

        /// <summary>
        /// The model region index of the automatic Buttons region: right after content,
        /// shifted by one when the captured-extras region sits between the two.
        /// </summary>
        protected int ActionsRegionIndex()
        {
            return RegionLayout().ActionsRegionIndex;
        }

        private bool InActionsRegion()
        {
            return HasActionsRegion() && Model.RegionIndex == ActionsRegionIndex();
        }

        private bool InExtrasRegion()
        {
            return RegionLayout().KindOf(Model.RegionIndex) == ScreenRegionKind.Extras;
        }

        /// <summary>
        /// Alt+Shift+J: lands the cursor on whatever the mouse pointer rests on and reads it
        /// exactly as an arrow key would, answering from the captured-extras rows (by widget
        /// identity first, then by rect), the captured buttons, and
        /// <see cref="CollectRouteCandidates"/>. A screen whose content rows carry no
        /// geometry refuses out loud rather than letting the modal swallow eat the chord.
        /// </summary>
        private void RouteToPointer()
        {
            int region;
            int index;
            int column;
            if (!PointerRouting.PointerOwnedBy(PointerSurface) || !TryFindPointerTarget(out region, out index, out column))
            {
                PointerRouting.RejectNoTarget();
                return;
            }
            if (region != Model.RegionIndex)
            {
                MoveResult result = Model.MoveToRegion(region);
                if (result.Changed)
                    OnRegionChanged(result);
            }
            ListModel list = Model.CurrentRegion;
            if (list == null)
                return;
            list.MoveTo(index);
            // Bounds-checked: candidates were collected before MoveToRegion, which a screen
            // may answer by rebuilding its columns.
            TableModel table = Model.CurrentTable;
            if (table != null && column >= 0 && column < table.ColumnCount)
                table.MoveToColumn(column);
            ShellDispatcherPatch.NotifyCursorMoved();
            NotifyCursorSettled();
            AnnounceCurrentItem();
        }

        /// <summary>The region, item and column the pointer resolves to, or false for nothing routable.</summary>
        private bool TryFindPointerTarget(out int region, out int index, out int column)
        {
            column = 0;
            ScreenRegionLayout layout = RegionLayout();
            if (layout.HasExtras)
            {
                int identity = FindExtrasRowByHoverIdentity();
                if (identity >= 0)
                {
                    region = layout.ExtrasRegionIndex;
                    index = identity;
                    return true;
                }
            }
            if (TryFindContentRowByHoverIdentity(out region, out index))
            {
                return true;
            }
            routeCandidates.Clear();
            routeTargets.Clear();
            if (layout.HasExtras)
            {
                for (int i = 0; i < extrasRows.Count; i++)
                {
                    // A read-only extras line has no control and so no rect of its own.
                    InteractiveMember member = extrasRows[i].Member;
                    if (member == null || member.Source == null)
                        continue;
                    routeCandidates.Add(new PointerHitCandidate
                    {
                        Primary = member.Source.VisibleScreenRect,
                        ClipDepth = member.Source.Clip.Depth,
                    });
                    routeTargets.Add(new RouteTarget { Region = layout.ExtrasRegionIndex, Index = i });
                }
            }
            if (layout.HasActions)
            {
                // Declared actions draw nothing of their own, so only the captured
                // buttons — which occupy the region's leading indices — are routable.
                for (int i = 0; i < capturedButtons.Count; i++)
                {
                    routeCandidates.Add(new PointerHitCandidate
                    {
                        Primary = capturedButtons[i].VisibleScreenRect,
                        ClipDepth = capturedButtons[i].Clip.Depth,
                    });
                    routeTargets.Add(new RouteTarget { Region = layout.ActionsRegionIndex, Index = i });
                }
            }
            CollectRouteCandidates(routeCandidates, routeTargets);
            int hit = PointerRouting.HitTest(routeCandidates);
            if (hit < 0)
            {
                region = -1;
                index = -1;
                return false;
            }
            region = routeTargets[hit].Region;
            index = routeTargets[hit].Index;
            column = routeTargets[hit].Column;
            return true;
        }

        /// <summary>The extras row whose control is the widget hover last hit, or -1.</summary>
        private int FindExtrasRowByHoverIdentity()
        {
            CapturedWidget hit = HoverLanding.Widget;
            if (hit == null)
                return -1;
            for (int i = 0; i < extrasRows.Count; i++)
            {
                InteractiveMember member = extrasRows[i].Member;
                if (member != null && ReferenceEquals(member.Source, hit))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// The content row the widget hover last hit, as (region, index). Consulted ahead of
        /// any geometry: a screen whose rows ARE captured widgets can name the row exactly,
        /// since the hovered widget is the innermost control under the pointer. False (the
        /// default) leaves routing to <see cref="CollectRouteCandidates"/>'s geometry.
        /// </summary>
        private protected virtual bool TryFindContentRowByHoverIdentity(out int region, out int index)
        {
            region = -1;
            index = -1;
            return false;
        }

        /// <summary>
        /// Contributes this screen's content rows as pointer-routing targets: one candidate
        /// per routable row, with its (region, index) appended to <paramref name="targets"/>
        /// in matching order. Geometry MUST be absolute UI points recorded at draw time,
        /// never converted after the fact.
        /// </summary>
        private protected virtual void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
        }

        /// <summary>Where a pointer-routing candidate lands: the region, the item within it, and — for a table region, whose contributors always set this — the column.</summary>
        internal struct RouteTarget
        {
            public int Region;
            public int Index;
            public int Column;
        }

        private bool CurrentItemAdjustable()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty || InActionsRegion() || InExtrasRegion() || Model.CurrentTable != null)
                return false;
            return CanAdjustContentItem(Model.RegionIndex, region.Index);
        }

        /// <summary>Whether the current extras row is an operable, enabled slider — the only extras kind Left/Right adjusts.</summary>
        private bool CurrentExtrasRowAdjustable()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return false;
            int index = region.Index;
            if (index < 0 || index >= extrasRows.Count)
                return false;
            ExtrasRow row = extrasRows[index];
            return row.Member != null && row.Member.Kind == WidgetKind.Slider && !row.Disabled;
        }

        /// <summary>
        /// Guard for the shared Left/Right claims: a table region (columns) or the Buttons
        /// region (toolbar) always wants them; a flat content region only when the current
        /// item is adjustable; an extras slider row too — otherwise the chords fall through
        /// to any per-screen alias beneath. A transposed screen's content regions want them
        /// for row movement, empty ones included: an empty always-navigable level is a legal
        /// landing there and must speak rather than fall to the swallow.
        /// </summary>
        private bool HorizontalClaimable()
        {
            RefreshModel();
            if (Model.CurrentTable != null)
                return true;
            ListModel region = Model.CurrentRegion;
            if (region == null)
                return false;
            if (TransposeContentAxes && !InActionsRegion() && !InExtrasRegion())
                return true;
            if (region.IsEmpty)
                return false;
            if (InActionsRegion())
                return true;
            if (InExtrasRegion())
                return CurrentExtrasRowAdjustable();
            return CurrentItemAdjustable();
        }

        /// <summary>
        /// Guard for the shared Up/Down/Home/End claims: true (row navigation) for every
        /// region and row, except a content row declaring itself a navigable surface via
        /// <see cref="ContentItemOwnsNavigationKeys"/>. Deliberately does NOT call
        /// <see cref="RefreshModel"/>: claim predicates run speculatively during dispatch and
        /// must stay side-effect free, and the previous pass's model is what the cursor is
        /// actually sitting on.
        /// </summary>
        private bool ItemNavigationClaimable()
        {
            if (Model.CurrentTable != null)
                return true;
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return true;
            if (InActionsRegion() || InExtrasRegion())
                return true;
            return !ContentItemOwnsNavigationKeys(Model.RegionIndex, region.Index);
        }

        /// <summary>Left/Right router: columns in a table, buttons in the toolbar, a slider's value in the extras region, value adjust elsewhere.</summary>
        private void OnHorizontal(int direction)
        {
            RefreshModel();
            if (relocatedThisRefresh)
                return;
            TableModel table = Model.CurrentTable;
            if (table != null)
            {
                MoveColumn(direction);
                return;
            }
            if (InActionsRegion())
            {
                ListModel region = Model.CurrentRegion;
                if (region == null)
                    return;
                if (!MenuHelper.SoundMove(region.MoveBy(direction)))
                    return;
                AnnounceCurrentItem();
                return;
            }
            if (InExtrasRegion())
            {
                OnExtrasAdjust(direction);
                return;
            }
            if (TransposeContentAxes)
            {
                ListModel content = Model.CurrentRegion;
                if (content == null)
                    return;
                if (!MenuHelper.SoundMove(content.MoveBy(direction)))
                    return;
                NotifyCursorSettled();
                AnnounceCurrent(CellAxis.Row);
                return;
            }
            OnAdjust(direction);
        }

        /// <summary>Left/Right on an extras-region slider row: steps it one grid unit via WidgetCapture's own live channel, exactly as Enter's +1 nudge does.</summary>
        private void OnExtrasAdjust(int direction)
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return;
            int index = region.Index;
            if (index < 0 || index >= extrasRows.Count)
                return;
            ExtrasRow row = extrasRows[index];
            if (row.Member == null || row.Member.Kind != WidgetKind.Slider || row.Disabled)
                return;
            WidgetCapture.RequestAdjust(row.Member.RawLabel, row.Member.Ordinal, direction);
            ArmExtrasPendingChange(index);
        }

        /// <summary>Column movement in the current table region; the last column answers with the edge tone.</summary>
        protected void MoveColumn(int direction)
        {
            TableModel table = Model.CurrentTable;
            if (table == null)
                return;
            MoveResult result = direction > 0 ? table.NextColumn() : table.PreviousColumn();
            if (!MenuHelper.SoundMove(result))
                return;
            AnnounceCurrent(CellAxis.Column);
        }

        /// <summary>
        /// Up/Down handler. During an active typeahead search these move
        /// through the match list (crossing regions when a match lives in
        /// another tab) instead of the plain row order.
        /// </summary>
        protected virtual void MoveItem(int delta)
        {
            if (TypeaheadMatchMove(delta))
                return;
            RefreshModel();
            if (relocatedThisRefresh)
                return;
            if (TransposeContentAxes && Model.CurrentTable == null && !InActionsRegion() && !InExtrasRegion())
            {
                StepRegionBounded(delta);
                return;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null)
                return;
            if (TryReaderFlow(delta, region))
                return;
            if (!MenuHelper.SoundMove(region.MoveBy(delta)))
                return;
            NotifyCursorSettled();
            AnnounceCurrent(CellAxis.Row);
        }

        /// <summary>
        /// Fires after the cursor lands on a content item through any navigation path and
        /// BEFORE the landing announcement composes, so a subclass that auto-selects radio
        /// rows on arrival has applied the selection by the time the checked state is spoken
        /// (announcements compose lazily from <see cref="DescribeContentItem"/>).
        ///
        /// Implementations must be cheap, silent and idempotent — they run on EVERY landing,
        /// speak nothing, play no sound, and no-op when the row is already selected. They
        /// must NOT call <see cref="RefreshModel"/>: a rebuild mid-move can relocate the very
        /// cursor that is landing, and counts a selection reveals settle on the next action's
        /// own refresh. Default: nothing.
        ///
        /// <paramref name="region"/> can be ANY live region, Buttons and captured-extras
        /// included (bounds-check past a typed region list's end), and
        /// <paramref name="index"/> is the region model's own row index, so a TABLE region
        /// counts its header row at 0.
        /// </summary>
        protected virtual void OnCursorSettled(int region, int index)
        {
        }

        /// <summary>
        /// Fires <see cref="OnCursorSettled"/> for wherever the cursor rests right now.
        /// Called from every cursor-landing path; protected so a screen with a bespoke
        /// landing path of its own can keep the contract.
        /// </summary>
        protected void NotifyCursorSettled()
        {
            // Cursor movement quietly disarms the Enter double-press confirm; kept out of
            // OnCursorSettled so the two features never couple.
            DisarmDefaultAccept();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return;
            OnCursorSettled(Model.RegionIndex, region.Index);
        }

        /// <summary>
        /// Message-reader arrow-flow: Down off a <see cref="ContentFlowsToActions"/> region
        /// lands on the Buttons bar, Up from ANY button returns to the text, Down holds still;
        /// Left/Right walk the bar.
        /// </summary>
        private bool TryReaderFlow(int delta, ListModel region)
        {
            if (region.IsEmpty)
                return false;
            if (ContentRegionsFlowVertically && Model.RegionIndex < ContentRegionCount)
            {
                if (delta > 0 && region.Index >= region.Count - 1)
                {
                    int next = NextNonEmptyContentRegion(Model.RegionIndex, 1);
                    if (next >= 0)
                        return FlowMoveToRegion(next, landOnLastRow: false);
                }
                if (delta < 0 && region.Index <= 0)
                {
                    int previous = NextNonEmptyContentRegion(Model.RegionIndex, -1);
                    if (previous >= 0)
                        return FlowMoveToRegion(previous, landOnLastRow: true);
                }
            }
            if (!HasActionsRegion())
                return false;
            if (delta > 0
                && Model.RegionIndex < ContentRegionCount
                && ContentFlowsToActions(Model.RegionIndex)
                && region.Index >= region.Count - 1)
            {
                return FlowMoveToRegion(ActionsRegionIndex(), landOnLastRow: false);
            }
            if (InActionsRegion() && LastFlowingContentRegion() >= 0)
            {
                if (delta < 0)
                {
                    return FlowMoveToRegion(LastFlowingContentRegion(), landOnLastRow: true);
                }
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return true;
            }

            return false;
        }

        private int LastFlowingContentRegion()
        {
            for (int i = ContentRegionCount - 1; i >= 0; i--)
            {
                if (ContentFlowsToActions(i) && !Model.IsRegionEmpty(i))
                    return i;
            }
            return -1;
        }

        /// <summary>Region jump on behalf of <see cref="TryReaderFlow"/>, pinning the landing row (first button going down, last text row coming back up). A content-to-content crossing mirrors <see cref="MoveRegion"/>'s tab-sound-and-frame sequence; a crossing into or out of the Buttons region lands like a plain arrow move instead, since that toolbar is the continuation of the text rather than a section of its own.</summary>
        private bool FlowMoveToRegion(int targetRegion, bool landOnLastRow)
        {
            // An empty target region does not exist to flow into; Down past the last text row
            // then keeps the hard edge.
            if (Model.IsRegionEmpty(targetRegion))
                return false;
            ListModel target = Model.Region(targetRegion);
            if (target == null || target.IsEmpty)
                return false;
            int fromRegion = Model.RegionIndex;
            bool continuesIntoActions = HasActionsRegion()
                && (targetRegion == ActionsRegionIndex() || fromRegion == ActionsRegionIndex());
            MoveResult result = Model.MoveToRegion(targetRegion);
            if (result.Kind == MoveKind.Empty)
                return false;
            if (result.Changed)
            {
                TypeaheadReset();
                OnRegionChanged(result);
                if (!continuesIntoActions && TabSwitchSound != null)
                {
                    TabSwitchSound.PlayOneShotOnCamera();
                }
            }
            // Pin the landing row LAST: MoveToRegion's position policy and a subclass's
            // OnRegionChanged landing behavior may both reposition the cursor, and the flow's
            // continuation semantics override both.
            if (landOnLastRow)
            {
                target.MoveLast();
            }
            else
            {
                target.MoveTo(0);
            }
            NotifyCursorSettled();
            if (continuesIntoActions)
            {
                AnnounceCurrent(CellAxis.Row);
            }
            else
            {
                AnnounceRegion();
            }
            return true;
        }

        /// <summary>Home/End handler; during an active search they jump to the first/last match.</summary>
        protected virtual void MoveItemEdge(bool first)
        {
            if (TypeaheadMatchEdge(first))
                return;
            RefreshModel();
            if (relocatedThisRefresh)
                return;
            ListModel region = Model.CurrentRegion;
            if (region == null)
                return;
            MoveResult result = first ? region.MoveFirst() : region.MoveLast();
            if (!MenuHelper.SoundMove(result))
                return;
            NotifyCursorSettled();
            AnnounceCurrent(CellAxis.Row);
        }

        /// <summary>
        /// Opt-out gate for the base ctor's unconditional Tab/Shift+Tab region-cycle claims.
        /// Checked in the claims' own <c>when:</c> predicate
        /// (<see cref="RegionCyclingClaimable"/>) rather than by skipping registration, so
        /// subclass constructor order never matters. This flag is for a screen where Tab must
        /// never cycle regions at all; one that needs Tab inert only during an internal
        /// sub-mode overrides <see cref="MoveRegion"/> instead.
        /// </summary>
        protected virtual bool EnableRegionCycling
        {
            get { return true; }
        }

        /// <summary>
        /// For grid-shaped screens whose content regions are the VERTICAL axis of a 2D grid
        /// (<see cref="WorkMenuScope"/>: priority levels stack, work types run across). When
        /// true, in CONTENT regions only — never a table, Buttons or captured-extras region —
        /// Up/Down step between regions, bounded, announcing as Tab cycling does; Left/Right
        /// walk the current region's rows, bounded; Home/End keep their row-edge meaning.
        /// Typeahead is unaffected. Usually paired with <see cref="EnableRegionCycling"/>
        /// false, leaving Tab for the screen's own axis.
        /// </summary>
        protected virtual bool TransposeContentAxes
        {
            get { return false; }
        }

        /// <summary>
        /// Opt-in gate for the message-reader arrow-flow, per content region
        /// (<see cref="TryReaderFlow"/>). List-shaped regions keep their hard edge; this is
        /// for read-only prose regions whose toolbar is a natural continuation of the text.
        /// </summary>
        protected virtual bool ContentFlowsToActions(int region)
        {
            return false;
        }

        /// <summary>Up/Down continue across content-region boundaries (last row down / first row up); Tab keeps its region meaning.</summary>
        protected virtual bool ContentRegionsFlowVertically
        {
            get { return false; }
        }

        /// <summary>Opt-out for the "section 2 of 5" suffix, for a screen whose region names already place the user.</summary>
        protected virtual bool RegionNamesCarryPosition
        {
            get { return false; }
        }

        /// <summary>The nearest non-empty content region from <paramref name="from"/> in <paramref name="step"/>'s direction, or -1.</summary>
        private int NextNonEmptyContentRegion(int from, int step)
        {
            for (int i = from + step; i >= 0 && i < ContentRegionCount; i += step)
            {
                if (!Model.IsRegionEmpty(i))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Whether Tab/Shift+Tab visit the Buttons region and announcements number it as a
        /// tab. False belongs to screens whose real window draws its buttons INSIDE the
        /// content box rather than in a footer of their own (decompiled
        /// RimWorld/MainTabWindow_Quests.cs:461-494 scrolls Accept with the quest text) —
        /// there the buttons are the end of what the player is reading, so Down off the last
        /// content line lands on the leftmost one and Up comes back, while Tab keeps meaning
        /// "the next part of the screen".
        ///
        /// Turning this off without a <see cref="ContentFlowsToActions"/> region to arrive
        /// from would strand the buttons, so the two travel together;
        /// <see cref="RefreshModel"/> refuses the skip when no flowing region holds anything.
        /// </summary>
        protected virtual bool ActionsRegionInTabCycle
        {
            get { return true; }
        }

        /// <summary>
        /// Action id of this screen's default/proceed button (Next, Generate, Done...). When
        /// set, Enter on an element with no Enter behavior of its own (see
        /// <see cref="DefaultAcceptInertness"/>) arms a two-step confirm: the first press
        /// offers the action by name, a second press on the SAME element activates it, and
        /// any cursor movement in between re-arms against the new element. Resolves through
        /// <see cref="DeclaredActions"/> only — a screen whose proceed button is a CAPTURED
        /// row overrides <see cref="CapturedDefaultAcceptAction"/> instead.
        /// </summary>
        protected virtual string DefaultAcceptActionId
        {
            get { return null; }
        }

        /// <summary>
        /// This screen's proceed button when the Buttons region shows it as a CAPTURED row
        /// rather than a <see cref="DeclaredActions"/> one. Return a
        /// <see cref="ScreenAction"/> whose Label is built from the SAME expression the button
        /// is drawn with (so the armed prompt and the row's spoken label cannot drift) and
        /// whose Activate rides the SAME vehicle the button runs — the scope's own claimed
        /// handler, never a second copy of the commit. Nothing here resolves from the capture
        /// snapshot, so it is immune to draw order.
        /// </summary>
        protected virtual ScreenAction CapturedDefaultAcceptAction
        {
            get { return null; }
        }

        /// <summary>True where the row owns Shift+Enter: the proceed claim, registered first, stands down for the subclass's.</summary>
        protected virtual bool ContentOwnsActivateDefault
        {
            get { return false; }
        }

        /// <summary>Whether this screen names a proceed button at all, by either channel.</summary>
        private bool HasDefaultAccept
        {
            get { return DefaultAcceptActionId != null || CapturedDefaultAcceptAction != null; }
        }

        /// <summary>
        /// The proceed button the whole grammar acts on: the declared row when
        /// <see cref="DefaultAcceptActionId"/> names one, otherwise the captured stand-in.
        /// Every path reads through here so the two channels cannot diverge.
        /// </summary>
        private ScreenAction ResolveDefaultAccept()
        {
            ScreenAction declared = DefaultAcceptActionId != null
                ? FindDeclaredAction(DefaultAcceptActionId)
                : null;
            return declared ?? CapturedDefaultAcceptAction;
        }

        private bool RegionCyclingClaimable()
        {
            return EnableRegionCycling;
        }

        /// <summary>
        /// Tab/Shift+Tab handler, and the target for the per-screen tab-cycle chords kept as
        /// aliases. Virtual so a subclass can block region cycling under a narrower condition
        /// than <see cref="EnableRegionCycling"/> — an internal sub-mode rather than the whole
        /// screen — by no-op'ing here and delegating to base otherwise.
        /// </summary>
        protected virtual void MoveRegion(bool forward)
        {
            RefreshModel();
            if (relocatedThisRefresh)
                return;
            // One section is nowhere to go; re-announcing it on every Tab is pure noise.
            if (Model.NonEmptyRegionCount < 2)
                return;
            MoveResult result = forward ? Model.NextRegion() : Model.PreviousRegion();
            if (result.Kind == MoveKind.Empty)
                return;
            LandInRegion(result);
        }

        /// <summary>
        /// The landing half of a region move, shared between Tab's cycle and a transposed
        /// Up/Down. A move that went nowhere answers with the edge tone. Only a real section
        /// switch plays the tab sound: a transposed step is a grid-row move and lands silently.
        /// </summary>
        private void LandInRegion(MoveResult result, bool playTabSound = true)
        {
            if (!MenuHelper.SoundMove(result))
                return;
            TypeaheadReset();
            OnRegionChanged(result);
            if (playTabSound && TabSwitchSound != null)
            {
                TabSwitchSound.PlayOneShotOnCamera();
            }
            NotifyCursorSettled();
            AnnounceRegion();
        }

        /// <summary>
        /// Up/Down on a screen whose content regions are the vertical axis
        /// (<see cref="TransposeContentAxes"/>): one content region along, bounded — the axis
        /// never wraps, so its ends answer with the edge tone. Only reachable with no search
        /// active, since <see cref="TypeaheadMatchMove"/> claims Up/Down while one runs.
        /// </summary>
        private void StepRegionBounded(int delta)
        {
            int target = Model.RegionIndex + delta;
            if (target < 0 || target >= ContentRegionCount)
            {
                MenuHelper.PlayEdgeTone();
                return;
            }
            // An unreachable neighbour is as far as this axis goes, and LandInRegion tones for it.
            LandInRegion(Model.MoveToRegion(target), playTabSound: false);
        }

        /// <summary>
        /// Fires after the region cursor lands somewhere new, before the announcement — the
        /// place to mirror the change into the game or reset per-screen sub-state.
        /// </summary>
        protected virtual void OnRegionChanged(MoveResult result)
        {
        }

    }
}
