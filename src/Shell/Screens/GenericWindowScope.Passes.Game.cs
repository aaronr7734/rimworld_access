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
        // Per-GUI-pass work, driven by GenericWindowDrawPatch below.

        internal void BeginDrawPass()
        {
            TooltipCapture.BeginPass();
            // The ring must outline a CAPTURE row: RingCaptureIndex, not the presentation index.
            int focused = RowIndex;
            bool hasRow = focused >= 0 && focused < presentationRows.Count;
            int ringIndex = hasRow ? presentationRows[focused].RingCaptureIndex : -1;
            // The Prefix runs at InnerWindowOnGUI entry, so this is the window's own clip level;
            // everything DoWindowContents draws sits at least one group deeper.
            passRootClipDepth = GuiSpace.CurrentClip().Depth;
            WidgetCapture.BeginPass(ringIndex);
            WidgetCapture.SetFocusedControl(hasRow ? presentationRows[focused].ActivateCaptureIndex : -1);
        }

        internal void OnGuiPass()
        {
            WidgetCapture.EndPass();
            TooltipCapture.EndPass();

            captureRows.Clear();
            captureRows.AddRange(WidgetCapture.Items);

            BuildPresentationRows();

            // The rebuilt row list is this screen's whole region, so re-derive it and clamp the
            // cursor to what the pass actually drew.
            RefreshModel();

            RecordPassOutcome();

            if (session.Editing)
            {
                // Re-post the live buffer as this pass's forced TextField result every frame, per
                // TextFieldEditSession's per-pass contract, routed through
                // WidgetCapture.RequestTextOverride rather than a reflected backing field.
                RetargetTypeInEdit();
                session.MirrorLive();
            }

            if (pendingReveal.HasValue)
            {
                ResolvePendingReveal();
            }
            else if (pendingStateChange.HasValue)
            {
                ResolvePendingStateChange();
            }
            else if (pendingAnnounce && !TextDialogShared.ForeignWindowAbove(window) && !ShellDispatcherPatch.LegacyKeyboardOverlayActive())
            {
                if (announceSettleArmed
                    && presentationRows.Count != announceSettleCount
                    && --announceSettlePassesLeft > 0)
                {
                    // A tab switch is in flight and the switched-to page is mid-draw. Wait for two
                    // consecutive passes to agree, or for the deadline, so the utterance carries the
                    // page's real first row and count.
                    announceSettleCount = presentationRows.Count;
                }
                else
                {
                    announceSettleArmed = false;
                    pendingAnnounce = false;
                    AnnounceCurrent();
                }
            }
        }

        /// <summary>
        /// Snapshots the acted-on presentation row's comparable state before an injection posts its
        /// change; <see cref="ResolvePendingStateChange"/> uses it to decide the redraw caught up.
        /// Replaces any still-pending record outright, so only one announcement is ever queued.
        /// </summary>
        private void ArmPendingStateChange(int presentationIndex)
        {
            PresentationRow row = presentationRows[presentationIndex];
            var labelTexts = new List<string>(presentationRows.Count);
            for (int i = 0; i < presentationRows.Count; i++)
            {
                // Buttons are tracked alongside plain Labels: a sort click reorders fused pawn rows,
                // which present as Buttons, and Label-only tracking made the sort look like a no-op.
                labelTexts.Add(TracksSwappedText(presentationRows[i].Kind) ? presentationRows[i].Label : null);
            }
            pendingStateChange = new PendingStateChange
            {
                PresentationIndex = presentationIndex,
                Check = row.Check,
                SliderValue = row.Source.SliderValue,
                Selected = row.EffectiveSelected,
                Text = row.Source.Text,
                Label = row.Label,
                Expanded = row.Expanded,
                RangeLow = row.Source != null ? row.Source.RangeLow : 0f,
                RangeHigh = row.Source != null ? row.Source.RangeHigh : 0f,
                PassesLeft = 3,
                RowCountAtArm = presentationRows.Count,
                LabelTextsAtArm = labelTexts,
            };
        }

        /// <summary>
        /// Consumes <see cref="pendingStateChange"/> once the rebuilt row reflects the acted-on
        /// change, the injection's result lagging one pass behind the request. Failing that, counts
        /// down and announces the CURRENT, still-unchanged state at zero — the honest "the mod
        /// clamped or rejected it" case, where the AtMaximum flag is the useful part. Clears
        /// silently if the presentation index fell out of range.
        /// </summary>
        private void ResolvePendingStateChange()
        {
            PendingStateChange pending = pendingStateChange.Value;
            if (pending.PresentationIndex >= presentationRows.Count)
            {
                // The acted row vanished outright, so there is nothing left to compare state
                // against. Immediate: a vanished row is unambiguous without the settle passes.
                pendingStateChange = null;
                TryAnnounceContentChange(pending, rowVanished: true);
                return;
            }
            PresentationRow row = presentationRows[pending.PresentationIndex];
            bool changed = row.Check != pending.Check
                || row.Source.SliderValue != pending.SliderValue
                || row.EffectiveSelected != pending.Selected
                || row.Source.Text != pending.Text
                || row.Label != pending.Label
                || row.Expanded != pending.Expanded
                || (row.Source != null && (row.Source.RangeLow != pending.RangeLow || row.Source.RangeHigh != pending.RangeHigh));
            // A VALUE control's injected adjust applies inside the widget on the pass AFTER the
            // request, so that pass still captures the pre-adjust value, while a dependent sibling
            // label the mod recomputes from the already-assigned field moves on that very pass.
            // Letting the sibling echo resolve there speaks the wrong row, in the wrong direction,
            // and swallows the acted control's own change, so for these kinds the echo waits out the
            // acted row's deferral and speaks only at the honesty deadline.
            bool valueControl = row.Kind == WidgetKind.Slider
                || row.Kind == WidgetKind.Range
                || row.Kind == WidgetKind.Stepper;
            if (!changed && !valueControl && TryAnnounceSwappedLabels(pending))
            {
                // A sibling Label's text changed in place while the acted row and the row count
                // stayed identical: the pager/wizard shape. The changed text is the activation's
                // outcome and has just been spoken, and is the same redraw-caught-up evidence.
                pendingStateChange = null;
                return;
            }
            if (!changed && --pending.PassesLeft > 0)
            {
                pendingStateChange = pending;
                return;
            }
            pendingStateChange = null;
            if (!changed && pending.FractionalAdjustDirection != 0 && row.Kind == WidgetKind.Slider
                && (pending.FractionalAdjustDirection > 0
                    ? row.Source.SliderValue < row.Source.SliderMax
                    : row.Source.SliderValue > row.Source.SliderMin))
            {
                PostAdjust(row.ActivateCaptureIndex, pending.FractionalAdjustDirection, false);
                ArmPendingStateChange(pending.PresentationIndex);
                return;
            }
            if (!changed && valueControl && TryAnnounceSwappedLabels(pending))
            {
                // Deadline reached with the acted control unchanged but a sibling label moved: the
                // mod clamped this thumb and shifted a dependent row, which is the real outcome.
                return;
            }
            if (!changed && TryAnnounceContentChange(pending, rowVanished: false))
            {
                // The acted row never changed but the page around it did, and the content-swap echo
                // already spoke, so the per-row announcement below must not also fire.
                return;
            }
            AnnounceStateChange(row, captionMoved: row.Label != pending.Label);
        }

        /// <summary>
        /// The generic fallback for an activation whose acted row shows no state change of its own,
        /// or vanished, but reshaped the window anyway. Fires at most once per activation, since it
        /// is reached only from <see cref="ResolvePendingStateChange"/>'s once-per-arm resolution,
        /// and stands down whenever another surface — a dialog, a FloatMenu, any scope now above
        /// this one — already announced itself. Activation-triggered by construction: the ordinary
        /// scroll-sweep pass never arms <see cref="pendingStateChange"/> at all.
        /// </summary>
        private bool TryAnnounceContentChange(PendingStateChange pending, bool rowVanished)
        {
            if (!ReferenceEquals(FocusStack.Top, this))
            {
                return false;
            }
            int delta = Math.Abs(presentationRows.Count - pending.RowCountAtArm);
            if (!rowVanished && delta < 3)
            {
                return false;
            }
            TolkHelper.SpeakData("RimWorldAccess.Shell.ContentChanged".Translate(presentationRows.Count).ToString());
            return true;
        }

        /// <summary>
        /// The pager/wizard echo: when the acted row's state never moved but a sibling Label's text
        /// changed IN PLACE — row count identical, a pure swap, never an insert or remove, which the
        /// row-count-delta echo owns — the new text is the activation's outcome and is spoken as
        /// data. Stands down, like <see cref="TryAnnounceContentChange"/>, whenever another surface
        /// already announced itself. More than three swapped labels reads as a whole-page swap and
        /// falls to the generic content-changed line instead of a paragraph dump.
        /// </summary>
        private bool TryAnnounceSwappedLabels(PendingStateChange pending)
        {
            if (pending.LabelTextsAtArm == null
                || presentationRows.Count != pending.LabelTextsAtArm.Count)
            {
                return false;
            }
            List<string> changedTexts = null;
            for (int i = 0; i < presentationRows.Count; i++)
            {
                string armed = pending.LabelTextsAtArm[i];
                if (armed == null || !TracksSwappedText(presentationRows[i].Kind))
                {
                    continue;
                }
                string now = presentationRows[i].Label ?? "";
                if (now != armed && !string.IsNullOrWhiteSpace(now))
                {
                    (changedTexts ?? (changedTexts = new List<string>())).Add(now);
                }
            }
            if (changedTexts == null)
            {
                return false;
            }
            if (!ReferenceEquals(FocusStack.Top, this))
            {
                // Another surface already owns the user and announced itself. The swap still
                // RESOLVES the pending record, since the redraw demonstrably caught up; it just does
                // not speak over the new surface.
                return true;
            }
            if (changedTexts.Count > 3)
            {
                // A whole-page swap where the SAME labels merely moved is a re-sort, not new
                // content, so say what a sighted player sees: the rows reordered.
                TolkHelper.SpeakData(LabelsArePermutation(pending)
                    ? "RimWorldAccess.Shell.ContentReordered".Translate().ToString()
                    : "RimWorldAccess.Shell.ContentChanged".Translate(presentationRows.Count).ToString());
            }
            else
            {
                TolkHelper.SpeakData(string.Join(" ", changedTexts));
            }
            return true;
        }

        /// <summary>The row kinds the swapped-labels echo snapshots: Labels and Buttons, since fused pawn-table rows present as Buttons.</summary>
        private static bool TracksSwappedText(WidgetKind kind)
        {
            return kind == WidgetKind.Label || kind == WidgetKind.Button;
        }

        /// <summary>True when the tracked rows carry exactly the armed snapshot's texts in a different order: the sort-header signature.</summary>
        private bool LabelsArePermutation(PendingStateChange pending)
        {
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < pending.LabelTextsAtArm.Count; i++)
            {
                string armed = pending.LabelTextsAtArm[i];
                if (armed == null)
                {
                    continue;
                }
                int count;
                counts.TryGetValue(armed, out count);
                counts[armed] = count + 1;
            }
            for (int i = 0; i < presentationRows.Count; i++)
            {
                if (!TracksSwappedText(presentationRows[i].Kind))
                {
                    continue;
                }
                string now = presentationRows[i].Label ?? "";
                int count;
                if (!counts.TryGetValue(now, out count) || count == 0)
                {
                    return false;
                }
                counts[now] = count - 1;
            }
            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (pair.Value != 0)
                {
                    return false;
                }
            }
            return true;
        }

        private void RecordPassOutcome()
        {
            armedPasses++;
            if (captureRows.Count > 0)
            {
                CapturedAnything = true;
            }
            if (!sawInteractive)
            {
                // A tab strip is a real control even once the bar grammar excludes its rows from
                // presentationRows, so tabRows, never filtered, is checked on its own.
                if (tabRows.Count > 0)
                {
                    sawInteractive = true;
                }
                else
                {
                    for (int i = 0; i < presentationRows.Count; i++)
                    {
                        // FillableBar is read-only data like Label, so a window of bars and text is
                        // not an interactive surface; a dropped orphan InvisibleButton never reaches
                        // this list, while a fused Label+InvisibleButton pair presents as a Button
                        // and correctly does flip the gate. A LEAF tree row is read-only too, since
                        // Listing_Tree draws no expander for a non-openable node; one openable node
                        // flips the gate, its expander being a real control.
                        if (presentationRows[i].Kind == WidgetKind.TreeItem && !presentationRows[i].Expanded.HasValue)
                        {
                            continue;
                        }
                        // A read-only checkbox draws no click target, so it is read-only data too.
                        if (presentationRows[i].Kind == WidgetKind.Checkbox && presentationRows[i].ReadOnlyCheckbox)
                        {
                            continue;
                        }
                        if (presentationRows[i].Kind != WidgetKind.Label && presentationRows[i].Kind != WidgetKind.FillableBar)
                        {
                            sawInteractive = true;
                            break;
                        }
                    }
                }
            }
        }

        private string ResolveWindowTitle()
        {
            if (cachedTitle != null)
            {
                return cachedTitle;
            }
            if (!string.IsNullOrEmpty(window.optionalTitle))
            {
                cachedTitle = window.optionalTitle;
            }
            else if (captureRows.Count > 0 && captureRows[0].Kind == WidgetKind.Label
                && captureRows[0].Composite != CompositeMember.Icon)
            {
                // The raw capture list, not presentation rows: a Medium-font title is the heading
                // detector's own signal, and a heading never becomes a presentation row. An ICON row
                // is excluded, being a Label row only because a picture has no better shape, or a
                // window whose first draw is an icon would be titled after that def.
                cachedTitle = captureRows[0].Label;
            }
            // The same markup cleanup every presentation label gets: a window is free to bold or
            // colour its own title, and the tags would otherwise be read as literal angle brackets.
            if (!string.IsNullOrEmpty(cachedTitle))
            {
                cachedTitle = cachedTitle.StripTags();
            }
            return cachedTitle;
        }

    }
}
