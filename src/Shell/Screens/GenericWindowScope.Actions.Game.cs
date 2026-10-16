using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    public sealed partial class GenericWindowScope
    {
        /// <summary>
        /// Up/Down, wrapping the chassis's move with scroll-follow: a row at its container's
        /// culled boundary arms a nudge and DEFERS the step to the pass that reveals the row
        /// beyond it (<see cref="TryArmBoundaryNudge"/>), so nothing is announced here.
        /// </summary>
        protected override void MoveItem(int delta)
        {
            // The chassis's empty-region utterance would name a region this single-region
            // screen never speaks of, and the focus announcement already said the window is
            // empty.
            if (presentationRows.Count == 0)
            {
                return;
            }
            // A reveal in flight owns the next completion; a keypress landing mid-nudge is
            // dropped rather than racing two navigation steps against one armed request.
            if (pendingReveal.HasValue)
            {
                return;
            }
            // Bottom button bar: Up returns to the last row above it, Down holds; Left/Right
            // (claimed in the constructor) walk the buttons.
            if (buttonBarStart >= 0 && !TypeaheadHasActiveSearch && RowIndex >= buttonBarStart)
            {
                if (delta > 0)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }
                TypeaheadReset();
                MoveToRow(buttonBarStart - 1);
                AnnounceCurrent();
                return;
            }
            int index = RowIndex;
            if (index >= 0 && !TypeaheadHasActiveSearch)
            {
                int containerIndex;
                if (delta > 0 && IsLastRowOfItsContainer(index)
                    && TryArmBoundaryNudge(index, forward: true, out containerIndex))
                {
                    pendingReveal = new PendingReveal { Action = RevealAction.Next, ContainerIndex = containerIndex, PassesLeft = 3 };
                    return;
                }
                if (delta < 0 && IsFirstRowOfItsContainer(index)
                    && TryArmBoundaryNudge(index, forward: false, out containerIndex))
                {
                    pendingReveal = new PendingReveal { Action = RevealAction.Previous, ContainerIndex = containerIndex, PassesLeft = 3 };
                    return;
                }
            }
            base.MoveItem(delta);
        }

        /// <summary>Left/Right inside the bottom button bar; the edges re-announce in place.</summary>
        private void MoveWithinButtonBar(int direction)
        {
            if (buttonBarStart < 0)
            {
                return;
            }
            int target = RowIndex + direction;
            if (target < buttonBarStart || target >= presentationRows.Count)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            TypeaheadReset();
            MoveToRow(target);
            AnnounceCurrent();
        }

        /// <summary>
        /// The rect tier of Alt+Shift+J: each row's drawn geometry, primary rect plus the fused
        /// caption's, so the chassis's deepest-clip hit test finds the innermost row under the
        /// pointer. <see cref="TryFindContentRowByHoverIdentity"/> answers first.
        /// </summary>
        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            if (!ScreenActive)
            {
                return;
            }
            for (int i = 0; i < presentationRows.Count; i++)
            {
                PresentationRow row = presentationRows[i];
                candidates.Add(new PointerHitCandidate
                {
                    Primary = row.VisibleScreenRect,
                    Secondary = row.LabelVisibleScreenRect,
                    ClipDepth = row.Clip.Depth,
                });
                targets.Add(new RouteTarget { Region = 0, Index = i });
            }
        }

        /// <summary>
        /// The identity tier of Alt+Shift+J: the widget hover last hit is by reference one of
        /// this pass's <see cref="captureRows"/> entries, so the owning row is exact. A widget
        /// from an older pass is absent, this misses, and the rect tier answers — as it also
        /// does whenever hover speech is off.
        /// </summary>
        private protected override bool TryFindContentRowByHoverIdentity(out int region, out int index)
        {
            region = 0;
            index = ScreenActive ? FindRowByHoverIdentity() : -1;
            return index >= 0;
        }

        /// <summary>The presentation row owning the widget hover last hit, or -1.</summary>
        private int FindRowByHoverIdentity()
        {
            CapturedWidget hit = HoverLanding.Widget;
            if (hit == null)
            {
                return -1;
            }
            int captureIndex = -1;
            for (int i = 0; i < captureRows.Count; i++)
            {
                if (ReferenceEquals(captureRows[i], hit))
                {
                    captureIndex = i;
                    break;
                }
            }
            if (captureIndex < 0)
            {
                return -1;
            }
            for (int i = 0; i < presentationRows.Count; i++)
            {
                PresentationRow row = presentationRows[i];
                if (row.RingCaptureIndex == captureIndex
                    || row.ActivateCaptureIndex == captureIndex
                    || row.SecondaryCaptureIndex == captureIndex
                    || row.StepDownCaptureIndex == captureIndex
                    || row.StepUpCaptureIndex == captureIndex)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Home/End, wrapping the chassis's edge move with scroll-to-edge follow: the same
        /// deferral <see cref="MoveItem"/> uses, asking for the whole remaining distance at
        /// once (<see cref="TryArmEdgeNudge"/>).
        /// </summary>
        protected override void MoveItemEdge(bool first)
        {
            // As in MoveItem: the chassis's empty-region utterance names a region this
            // screen never speaks of, and focus already reported the window empty.
            if (presentationRows.Count == 0)
            {
                return;
            }
            if (pendingReveal.HasValue)
            {
                return;
            }
            if (TypeaheadHasActiveSearch)
            {
                base.MoveItemEdge(first);
                return;
            }
            int edgeIndex = first ? 0 : presentationRows.Count - 1;
            int containerIndex;
            if (TryArmEdgeNudge(edgeIndex, forward: !first, out containerIndex))
            {
                pendingReveal = new PendingReveal
                {
                    Action = first ? RevealAction.First : RevealAction.Last,
                    ContainerIndex = containerIndex,
                    // Several rounds may be needed when a mod's content height keeps growing
                    // as rows draw; the cap sits well above any real list, so a mod that never
                    // converges cannot livelock the key.
                    PassesLeft = 30,
                };
                return;
            }
            base.MoveItemEdge(first);
        }

        /// <summary>True when no later presentation row shares <paramref name="presentationIndex"/>'s ScrollContainer — the row a forward scroll-follow nudge would need to reveal past.</summary>
        private bool IsLastRowOfItsContainer(int presentationIndex)
        {
            CapturedWidget source = presentationRows[presentationIndex].Source;
            if (source == null || source.ContainerIndex < 0)
            {
                return false;
            }
            for (int i = presentationIndex + 1; i < presentationRows.Count; i++)
            {
                CapturedWidget other = presentationRows[i].Source;
                if (other != null && other.ContainerIndex == source.ContainerIndex)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>True when no earlier presentation row shares <paramref name="presentationIndex"/>'s ScrollContainer — the row a backward scroll-follow nudge would need to reveal past.</summary>
        private bool IsFirstRowOfItsContainer(int presentationIndex)
        {
            CapturedWidget source = presentationRows[presentationIndex].Source;
            if (source == null || source.ContainerIndex < 0)
            {
                return false;
            }
            for (int i = presentationIndex - 1; i >= 0; i--)
            {
                CapturedWidget other = presentationRows[i].Source;
                if (other != null && other.ContainerIndex == source.ContainerIndex)
                {
                    return false;
                }
            }
            return true;
        }

        private const float ScrollContentSlack = 0.5f;

        private static bool ContainerHasMoreBelow(WidgetCapture.ScrollContainer c)
        {
            return c.ViewHeight > c.OutHeight + c.ScrollPosition.y + ScrollContentSlack;
        }

        private static bool ContainerHasMoreAbove(WidgetCapture.ScrollContainer c)
        {
            return c.ScrollPosition.y > ScrollContentSlack;
        }

        /// <summary>
        /// Arms a one-row scroll-follow nudge when the focused row sits at its container's
        /// culled boundary with content beyond it. Never arms while one is in flight, and never
        /// on a container with no room — a genuinely-at-the-end list falls through to the
        /// caller's ordinary Move/AtEdge/wrap behavior.
        /// </summary>
        private bool TryArmBoundaryNudge(int presentationIndex, bool forward, out int containerIndex)
        {
            containerIndex = -1;
            if (WidgetCapture.HasArmedNudge)
            {
                return false;
            }
            CapturedWidget source = presentationRows[presentationIndex].Source;
            if (source == null || source.ContainerIndex < 0)
            {
                return false;
            }
            WidgetCapture.ScrollContainer container;
            if (!WidgetCapture.TryGetScrollContainer(source.ContainerIndex, out container))
            {
                return false;
            }
            if (forward ? !ContainerHasMoreBelow(container) : !ContainerHasMoreAbove(container))
            {
                return false;
            }
            float step = source.Rect.height > 1f ? source.Rect.height : 24f;
            float requestedY = forward ? container.ScrollPosition.y + step : container.ScrollPosition.y - step;
            WidgetCapture.ArmScrollNudge(source.ContainerIndex, container.OutRectScreen, requestedY);
            containerIndex = source.ContainerIndex;
            return true;
        }

        /// <summary>
        /// Arms a scroll-to-edge nudge for Home/End, asking for the full remaining distance at
        /// once (0, or current + ViewHeight — an overshoot the capture-side patch clamps).
        /// <see cref="ResolvePendingReveal"/> re-arms while the container reports more room.
        /// </summary>
        private bool TryArmEdgeNudge(int presentationIndex, bool forward, out int containerIndex)
        {
            containerIndex = -1;
            if (WidgetCapture.HasArmedNudge || presentationIndex < 0 || presentationIndex >= presentationRows.Count)
            {
                return false;
            }
            CapturedWidget source = presentationRows[presentationIndex].Source;
            if (source == null || source.ContainerIndex < 0)
            {
                return false;
            }
            WidgetCapture.ScrollContainer container;
            if (!WidgetCapture.TryGetScrollContainer(source.ContainerIndex, out container))
            {
                return false;
            }
            if (forward ? !ContainerHasMoreBelow(container) : !ContainerHasMoreAbove(container))
            {
                return false;
            }
            float requestedY = forward ? container.ScrollPosition.y + container.ViewHeight : 0f;
            WidgetCapture.ArmScrollNudge(source.ContainerIndex, container.OutRectScreen, requestedY);
            containerIndex = source.ContainerIndex;
            return true;
        }

        /// <summary>
        /// Runs once per pass while <see cref="pendingReveal"/> is set. This pass's
        /// <see cref="presentationRows"/> already reflect a landed nudge, since
        /// BuildPresentationRows runs after the armed BeginScrollView call in the same pass.
        /// Home/End re-arm while the container reports more room, capped by PassesLeft;
        /// Next/Previous complete on first landing. A nudge that never lands still completes
        /// the deferred step, falling through to ordinary AtEdge/wrap behavior.
        /// </summary>
        private void ResolvePendingReveal()
        {
            PendingReveal reveal = pendingReveal.Value;
            bool landed = WidgetCapture.NudgeLandedContainerIndex == reveal.ContainerIndex;
            if (!landed)
            {
                int left = reveal.PassesLeft - 1;
                if (left > 0)
                {
                    reveal.PassesLeft = left;
                    pendingReveal = reveal;
                    return;
                }
                pendingReveal = null;
                CompleteReveal(reveal.Action);
                return;
            }
            if (reveal.Action == RevealAction.First || reveal.Action == RevealAction.Last)
            {
                WidgetCapture.ScrollContainer container;
                if (!WidgetCapture.HasArmedNudge && WidgetCapture.TryGetScrollContainer(reveal.ContainerIndex, out container))
                {
                    bool stillMore = reveal.Action == RevealAction.Last
                        ? ContainerHasMoreBelow(container)
                        : ContainerHasMoreAbove(container);
                    if (stillMore && reveal.PassesLeft > 1)
                    {
                        float requestedY = reveal.Action == RevealAction.Last
                            ? container.ScrollPosition.y + container.ViewHeight
                            : 0f;
                        WidgetCapture.ArmScrollNudge(reveal.ContainerIndex, container.OutRectScreen, requestedY);
                        pendingReveal = new PendingReveal { Action = reveal.Action, ContainerIndex = reveal.ContainerIndex, PassesLeft = reveal.PassesLeft - 1 };
                        return;
                    }
                }
            }
            pendingReveal = null;
            CompleteReveal(reveal.Action);
        }

        /// <summary>Finishes the navigation step ResolvePendingReveal deferred, with the ONE announcement the whole arm-nudge-complete sequence produces.</summary>
        private void CompleteReveal(RevealAction action)
        {
            TypeaheadReset();
            ListModel region = Model.CurrentRegion;
            if (region == null)
            {
                return;
            }
            switch (action)
            {
                case RevealAction.Next:
                    region.MoveBy(1);
                    break;
                case RevealAction.Previous:
                    region.MoveBy(-1);
                    break;
                case RevealAction.First:
                    region.MoveFirst();
                    break;
                case RevealAction.Last:
                    region.MoveLast();
                    break;
            }
            AnnounceCurrent();
        }

        private void OnJumpToPreviousSection(KeyEventSnapshot e)
        {
            JumpToPreviousSection();
        }

        private void OnJumpToNextSection(KeyEventSnapshot e)
        {
            JumpToNextSection();
        }

        /// <summary>PageUp: first row of the PREVIOUS section. Clamps at the first section (no wrap) with the reject sound — PawnFilterState/InfoCardState's own section-jump precedent.</summary>
        private void JumpToPreviousSection()
        {
            JumpToAdjacentSection(forward: false);
        }

        /// <summary>PageDown: first row of the NEXT section. Clamps at the last section (no wrap) with the reject sound.</summary>
        private void JumpToNextSection()
        {
            JumpToAdjacentSection(forward: true);
        }

        private void JumpToAdjacentSection(bool forward)
        {
            int index = RowIndex;
            if (presentationRows.Count == 0 || index < 0)
            {
                return;
            }
            int target = SectionNavigation.FindAdjacentSectionStart(
                presentationRows.Count, index, i => presentationRows[i].Section, forward);
            if (target < 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            TypeaheadReset();
            MoveToRow(target);
            AnnounceCurrent();
        }

        /// <summary>The focused row's activation: the body behind the chassis's Enter and its Space alias.</summary>
        private void ActivateFocusedRow()
        {
            if (!ScreenActive)
            {
                // The chassis claims Enter unconditionally, so the refusal lives here: nothing
                // may be posted into a window this screen cannot answer for, or under a foreign
                // window whose own Enter the player is pressing.
                return;
            }
            int focused = RowIndex;
            if (focused < 0 || focused >= presentationRows.Count)
            {
                return;
            }
            PresentationRow row = presentationRows[focused];
            // Vanilla refuses the mouse click on a gated control, so the keyboard refuses too,
            // with a voice instead of silence. Sliders, text fields and tabs carry no gate.
            if (row.Source != null && row.Source.Disabled)
            {
                TolkHelper.Speak("RimWorldAccess.Shell.GenericWindow.Disabled".Loc(row.Label));
                return;
            }
            // Checked ahead of Kind: the captured widget stays WidgetKind.Label, so there is
            // no vanilla widget to re-fire, only the filter screen to open.
            if (row.Source != null && row.Source.Composite == CompositeMember.FilterPanelHandoff
                && row.Source.Payload is CapturedFilterPanel filterPayload)
            {
                RimWorldAccess.ThingFilterMenuState.Open(filterPayload.Filter, filterPayload.ParentFilter,
                    ResolveWindowTitle() ?? "RimWorldAccess.Inspection.Tree.FilterPanelTitle".Translate(),
                    filterPayload.ForceHideHitPointsConfig, filterPayload.ForceHideQualityConfig,
                    filterPayload.ForceHiddenFilters);
                return;
            }
            switch (row.Kind)
            {
                case WidgetKind.Checkbox:
                    if (row.ReadOnlyCheckbox)
                    {
                        // Some rows draw no click target at all; posting here would fire at a
                        // bare Label with nothing listening and arm a state change that can
                        // never come. Re-read, the "nothing to activate" convention.
                        AnnounceCurrent();
                        break;
                    }
                    PostActivate(row.ActivateCaptureIndex);
                    ArmPendingStateChange(focused);
                    break;
                case WidgetKind.RadioButton:
                    PostActivate(row.ActivateCaptureIndex);
                    ArmPendingStateChange(focused);
                    break;
                case WidgetKind.Slider:
                case WidgetKind.Range:
                    // A slider owns Left/Right and nothing else; Enter re-reads the row.
                    // Range rows follow the same rule.
                    AnnounceCurrent();
                    break;
                case WidgetKind.Button:
                    // Silent otherwise: the click's own side effect announces itself. The
                    // state-change snapshot still arms — AnnounceStateChange has no Button case,
                    // so an ordinary button pays nothing, and it is what catches the rarer click
                    // that silently swapped the whole page.
                    PostActivate(row.ActivateCaptureIndex);
                    ArmPendingStateChange(focused);
                    break;
                case WidgetKind.Tab:
                    // The new selected state is the announcement; a tab click is silent.
                    ActivateTabRow(row, focused, armStateChange: true);
                    break;
                case WidgetKind.TextField:
                    BeginEdit(row, announcePrompt: true);
                    break;
                case WidgetKind.TreeItem:
                    if (row.Expanded.HasValue)
                    {
                        // The expander's own click runs node.SetOpen and plays vanilla's
                        // TabOpen/TabClose, so the injected click IS the vanilla toggle.
                        PostActivate(row.ActivateCaptureIndex);
                        ArmPendingStateChange(focused);
                    }
                    else
                    {
                        // A leaf has no expander to click; re-read the row.
                        AnnounceCurrent();
                    }
                    break;
                case WidgetKind.Stepper:
                    if (row.Source != null && (row.Source.StepperValueField || row.Source.Kind == WidgetKind.TextField))
                    {
                        // The anchor IS the center field either way, so the TextField path
                        // opens the numeric edit session unchanged.
                        BeginEdit(row, announcePrompt: true);
                    }
                    else
                    {
                        // No field to edit (the value is a bare Label, or absent); re-read.
                        AnnounceCurrent();
                    }
                    break;
                default: // Label: nothing to activate.
                    AnnounceCurrent();
                    break;
            }
        }

        private void OnNextTab(KeyEventSnapshot e)
        {
            CycleTab(1);
        }

        private void OnPreviousTab(KeyEventSnapshot e)
        {
            CycleTab(-1);
        }

        /// <summary>
        /// The current tab's index into <see cref="tabRows"/>: the reference
        /// <see cref="CycleTab"/> steps from and <see cref="BuildEntryTabFrameText"/> describes.
        /// The tab VANILLA draws as selected wins, so the cycle follows the page actually
        /// showing. When no captured record claims selection — a mod tracking it outside
        /// TabRecord.Selected — the cursor's own tab, then the last position this scope cycled
        /// to (possibly a DISABLED stop), are the fallbacks.
        /// </summary>
        private int ResolveReferenceTabIndex()
        {
            if (lastCycledTabIndex >= 0 && lastCycledTabIndex < tabRows.Count
                && tabRows[lastCycledTabIndex].DisabledTabStop)
            {
                // Parked on a DISABLED stop: landing there activates nothing, so the
                // vanilla-selected tab still names the page behind the cursor. Trusting it
                // re-derives the same neighbor forever and Tab can never walk past the stop.
                // The parked position is newer information; a real switch updates both.
                return lastCycledTabIndex;
            }
            int reference = -1;
            for (int i = 0; i < tabRows.Count; i++)
            {
                if (tabRows[i].EffectiveSelected)
                {
                    reference = i;
                    break;
                }
            }
            int focused = RowIndex;
            if (reference < 0 && focused >= 0 && focused < presentationRows.Count)
            {
                // A window that is nothing but a tab strip keeps its tabs in presentationRows,
                // so the cursor can genuinely sit on one.
                reference = tabRows.IndexOf(presentationRows[focused]);
            }
            if (reference < 0 && lastCycledTabIndex >= 0 && lastCycledTabIndex < tabRows.Count)
            {
                // No tab reports itself selected: a hand-rolled strip marking its current tab
                // in some way the capture engine cannot read. Cycling from where this scope last
                // landed is a fact about what the player did rather than a guess about the draw,
                // and without it every Tab press would walk from position zero.
                reference = lastCycledTabIndex;
            }
            if (reference < 0)
            {
                reference = 0;
            }
            return reference;
        }

        /// <summary>
        /// Tab/Shift+Tab: the next/previous tab of the window's strip, with wrap. Reads
        /// <see cref="tabRows"/>, since tabs have left <see cref="presentationRows"/>, and lands
        /// the cursor on the refreshed page's FIRST row, the tab itself no longer being
        /// navigable.
        /// The switch does not announce alone: it arms <see cref="pendingTabFramePrefix"/> and
        /// leaves <see cref="pendingAnnounce"/> set, so the next pass's
        /// <see cref="AnnounceCurrent"/> — once the mod has rebuilt the page — speaks the tab
        /// frame and the new first row as one utterance, through the same
        /// <see cref="BuildTabFrameText"/> the entry-side frame uses.
        /// A DISABLED stop activates nothing and leaves the content untouched, so its frame
        /// speaks immediately rather than waiting on a rebuild that will never come.
        /// </summary>
        private void CycleTab(int delta)
        {
            if (tabRows.Count < 2)
            {
                return;
            }
            int reference = ResolveReferenceTabIndex();
            int target = ((reference + delta) % tabRows.Count + tabRows.Count) % tabRows.Count;
            PresentationRow targetTab = tabRows[target];

            TypeaheadReset();
            lastCycledTabIndex = target;

            if (targetTab.DisabledTabStop)
            {
                pendingStateChange = null;
                pendingAnnounce = false;
                // An unresolved switch from the preceding Tab press may have armed this for the
                // next AnnounceCurrent; clear it, since this press's switch never happens.
                pendingTabFramePrefix = null;
                announceSettleArmed = false;
                AnnounceTabSwitch(targetTab, target + 1, tabRows.Count);
                return;
            }

            // This pass's presentationRows still describe the PRE-switch page; the next pass's
            // rebuild re-anchors index 0 onto the new page's first row.
            MoveToRow(0);
            // One announcement per action: this arms the single combined utterance, so the
            // deferred channels must add none — their snapshot is of the pre-switch row list.
            pendingStateChange = null;
            pendingTabFramePrefix = ApplySectionPrefix(targetTab, BuildTabFrameText(targetTab, target + 1, tabRows.Count));
            pendingAnnounce = true;
            // Hold the utterance until the new page's rows settle; -1 never equals a real
            // count, so at least one full post-switch pass is always waited.
            announceSettleArmed = true;
            announceSettleCount = -1;
            announceSettlePassesLeft = 6;
            ActivateTabRow(targetTab, -1, armStateChange: false);
        }

        /// <summary>
        /// Fires one tab's own vanilla handler: the posted activation makes
        /// <c>WidgetCapture.RecordTab</c> invoke that TabRecord's <c>clickedAction</c> with
        /// vanilla's click semantics on the next pass. No selection state is set here, and a tab
        /// carries no vanilla gate. <paramref name="armStateChange"/> is false for
        /// <see cref="CycleTab"/>, which arms its own combined announcement instead and passes
        /// -1 for <paramref name="presentationIndex"/>.
        /// </summary>
        private void ActivateTabRow(PresentationRow row, int presentationIndex, bool armStateChange)
        {
            PostActivate(row.ActivateCaptureIndex);
            if (armStateChange)
            {
                ArmPendingStateChange(presentationIndex);
            }
        }

        /// <summary>
        /// Space on a Widgets.CheckboxLabeledSelectable row: fires its ROW-SELECT hotspot, which
        /// Enter structurally cannot reach. The control draws two disjoint click targets
        /// (Verse/Widgets.cs:1268-1313) — the row minus its right 24 pixels, live only while
        /// unselected, which sets <c>selected</c>; and the right square, always live, which
        /// flips <c>checkOn</c>. A mouse player needs two separate clicks and neither reacts to
        /// hover, so binding selection to FOCUS would mutate on navigation. Two keys on one row
        /// is the faithful parity, each riding vanilla's own hotspot (vehicle A).
        /// </summary>
        private void OnSelectRow(KeyEventSnapshot e)
        {
            int focused = RowIndex;
            if (focused < 0 || focused >= presentationRows.Count)
            {
                return;
            }
            PresentationRow row = presentationRows[focused];
            if (row.SecondaryCaptureIndex < 0)
            {
                // Vanilla draws no select hotspot once the row IS selected, so there is nothing
                // to click; say the state rather than posting into nothing.
                TolkHelper.Speak("RimWorldAccess.UI.GenericWindow.RowSelected".Loc());
                return;
            }
            PostActivate(row.SecondaryCaptureIndex);
            ArmPendingStateChange(focused);
        }

        private void OnIncrease(KeyEventSnapshot e)
        {
            AdjustFocusedValue(1);
        }

        private void OnDecrease(KeyEventSnapshot e)
        {
            AdjustFocusedValue(-1);
        }

        /// <summary>Left/Right on a slider row, or one thumb of a range row: the same grammar and deferred announcement.</summary>
        private void AdjustFocusedValue(int direction)
        {
            int focused = RowIndex;
            if (focused < 0 || focused >= presentationRows.Count)
            {
                return;
            }
            PresentationRow row = presentationRows[focused];
            if (row.Kind == WidgetKind.Range)
            {
                PostAdjustRange(row.ActivateCaptureIndex, row.RangeHigh, direction);
                ArmPendingStateChange(focused);
                return;
            }
            if (row.Kind != WidgetKind.Slider)
            {
                return;
            }
            PostAdjust(row.ActivateCaptureIndex, direction, CaptionProvesFractional(row));
            ArmPendingStateChange(focused);
        }

        /// <summary>The fused caption's evidence that a whole-number-bounded slider is fractional (see <see cref="SliderCaption.CaptionImpliesFractional"/>).</summary>
        private bool CaptionProvesFractional(PresentationRow row)
        {
            int index = row.ActivateCaptureIndex;
            if (index < 0 || index >= captureRows.Count)
            {
                return false;
            }
            CapturedWidget source = captureRows[index];
            return SliderCaption.CaptionImpliesFractional(row.Label, source.SliderValue, source.SliderMin, source.SliderMax);
        }

        private void OnStepperIncrease(KeyEventSnapshot e)
        {
            StepStepper(1);
        }

        private void OnStepperDecrease(KeyEventSnapshot e)
        {
            StepStepper(-1);
        }

        /// <summary>
        /// Left/Right on a fused stepper row: posts a live activation on the run's SMALL
        /// minus/plus button, so vanilla's own handler runs the arithmetic and the click sound.
        /// That arithmetic reads the physical Ctrl/Shift through
        /// GenUI.CurrentAdjustmentMultiplier, which is why the action carries the modified
        /// chords and the big buttons stay unwired.
        /// </summary>
        private void StepStepper(int direction)
        {
            int focused = RowIndex;
            if (focused < 0 || focused >= presentationRows.Count || presentationRows[focused].Kind != WidgetKind.Stepper)
            {
                return;
            }
            PresentationRow row = presentationRows[focused];
            int captureIndex = direction > 0 ? row.StepUpCaptureIndex : row.StepDownCaptureIndex;
            if (captureIndex < 0)
            {
                return;
            }
            PostActivate(captureIndex);
            // Both shapes arm: IntEntry's observable is its value field's rewritten text,
            // IntAdjuster's is the fused caption, which is where its number lives. When neither
            // moved, the resolver's countdown re-reads the row rather than leaving it silent.
            ArmPendingStateChange(focused);
        }

        /// <summary>
        /// Posts a live activation for the control at this CAPTURE index as a descriptor (kind,
        /// raw label, ordinal), never as a raw index, which the next pass could resolve to a
        /// shifted row.
        /// </summary>
        private void PostActivate(int captureIndex)
        {
            if (captureIndex < 0 || captureIndex >= captureRows.Count)
            {
                return;
            }
            CapturedWidget source = captureRows[captureIndex];
            // A window this click opens is by construction the surface the player asked for.
            // Arming here covers every activation this scope posts, so a plain Window subclass
            // that never sets absorbInputAroundWindow still gets a scope instead of silence;
            // when the click opens nothing the watch simply expires.
            ScopeForWindow.ArmDeliberateGenericAttach();
            WidgetCapture.RequestActivate(source.Kind, source.Label,
                RimWorldAccess.CaptureDescriptor.OrdinalOf(captureRows, captureIndex));
        }

        /// <summary>Slider twin of <see cref="PostActivate"/>.</summary>
        private void PostAdjust(int captureIndex, int direction, bool fractional)
        {
            if (captureIndex < 0 || captureIndex >= captureRows.Count)
            {
                return;
            }
            CapturedWidget source = captureRows[captureIndex];
            WidgetCapture.RequestAdjust(source.Label,
                RimWorldAccess.CaptureDescriptor.OrdinalOf(captureRows, captureIndex), direction, fractional);
        }

        /// <summary>
        /// Range twin of <see cref="PostAdjust"/>. A range records with an empty label, so its
        /// ordinal among range rows is its identity; the post also names the thumb.
        /// </summary>
        private void PostAdjustRange(int captureIndex, bool high, int direction)
        {
            if (captureIndex < 0 || captureIndex >= captureRows.Count)
            {
                return;
            }
            CapturedWidget source = captureRows[captureIndex];
            WidgetCapture.RequestAdjustRange(source.Label,
                RimWorldAccess.CaptureDescriptor.OrdinalOf(captureRows, captureIndex), high, direction);
        }

        /// <summary>Escape once no typeahead search is live; the chassis's Cancel claim clears the search first.</summary>
        private void OnCancelClaim(KeyEventSnapshot e)
        {
            // A window with closeOnCancel disabled is dismissed only through its own drawn
            // button, so Escape performs that instead of vanilla's no-op cancel chain.
            WindowDismissRegistry.Entry dismiss = WindowDismissRegistry.EntryFor(window);
            if (dismiss != null)
            {
                if (dismiss.Handler != null)
                {
                    ShellFrameStamps.MarkCancelConsumed();
                    dismiss.Handler(window);
                    return;
                }
                int dismissRow = FindDismissButtonRow(dismiss.Labels);
                if (dismissRow >= 0)
                {
                    ShellFrameStamps.MarkCancelConsumed();
                    PostActivate(presentationRows[dismissRow].ActivateCaptureIndex);
                    return;
                }
                // No dismiss control on screen is the same dead end a sighted player has:
                // swallow rather than close what the UI never offers.
                ShellFrameStamps.MarkCancelConsumed();
                return;
            }
            if (!window.absorbInputAroundWindow)
            {
                // Coexisting window: close through its own vanilla Close so the remove hook
                // pops this scope and focus returns beneath.
                ShellFrameStamps.MarkCancelConsumed();
                window.Close();
                return;
            }
            // Absorbing window: route through vanilla's own Notify_PressedCancel rather than
            // closing directly, so closeOnCancel/openMenuOnCancel run as vanilla intends
            // (vehicle A). The stack check is required — an earlier same-frame vanilla pass can
            // already have closed the window, and Notify_PressedCancel would then walk to
            // whatever is now on top and close THAT one instead.
            // MarkCancelConsumed is stamped AFTER the call: it is a global this-frame flag
            // WindowCancelKeyRouterPatch checks first, so stamping it earlier would block our
            // own run of vanilla's body through that same patched method.
            if (Find.WindowStack.Windows.Contains(window))
            {
                Find.WindowStack.Notify_PressedCancel();
            }
            ShellFrameStamps.MarkCancelConsumed();
        }

        /// <summary>
        /// The enabled captured Button row captioned with one of
        /// <see cref="WindowDismissRegistry"/>'s dismiss literals (ordinal, trimmed), or -1.
        /// </summary>
        private int FindDismissButtonRow(string[] labels)
        {
            for (int i = 0; i < presentationRows.Count; i++)
            {
                PresentationRow row = presentationRows[i];
                if (row.Kind != WidgetKind.Button || (row.Source != null && row.Source.Disabled))
                {
                    continue;
                }
                string label = row.Label != null ? row.Label.Trim() : null;
                if (string.IsNullOrEmpty(label))
                {
                    continue;
                }
                for (int j = 0; j < labels.Length; j++)
                {
                    if (string.Equals(label, labels[j], StringComparison.Ordinal))
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

    }
}
