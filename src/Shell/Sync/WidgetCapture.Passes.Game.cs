using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    public static partial class WidgetCapture
    {
        public static int IndexOfKind(WidgetKind kind, int ordinal)
        {
            int seen = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Kind == kind && seen++ == ordinal)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// True while ANY pass (live or detached) is open. ScreenScopeDrawPatch's own
        /// widget-capture bracket consults this before calling <see cref="BeginPass"/>, so two
        /// taps on one InnerWindowOnGUI call cannot clobber each other's in-flight stream.
        /// </summary>
        internal static bool IsPassOpen
        {
            get { return passOpen; }
        }

        /// <summary>
        /// True while a DETACHED pass is open — the inspect-tab harness's off-screen channel.
        /// <see cref="TooltipCapture"/> reads this rather than its own detached flag, which is
        /// also raised around a real on-screen window's live pass.
        /// </summary>
        internal static bool DetachedPassOpen
        {
            get { return detachedPass; }
        }

        /// <summary>Screen-space checkbox-texture markers recorded by the current/most recent pass — see CheckTexMarker's remarks.</summary>
        public static IReadOnlyList<CheckTexMarker> CheckTexMarkers
        {
            get { return checkTexMarkers; }
        }

        /// <summary>Widgets.DrawOptionBackground markers recorded by the current/most recent pass — see OptionBackgroundMarker's remarks.</summary>
        public static IReadOnlyList<OptionBackgroundMarker> OptionBackgroundMarkers
        {
            get { return optionBackgroundMarkers; }
        }

        /// <summary>Sink indexes where a Listing.NewColumn ran during the current/most recent pass, ascending by construction.</summary>
        public static IReadOnlyList<int> ListingColumnBreaks
        {
            get { return listingColumnBreaks; }
        }

        /// <summary>Start recording; clears the previous pass. Call from the owning window's InnerWindowOnGUI prefix.</summary>
        public static void BeginPass(int focusedIndex)
        {
            // A live pass outranks a detached one, which would otherwise keep CurrentSink and
            // swallow everything this pass records.
            EndDetachedPass();
            items.Clear();
            checkTexMarkers.Clear();
            optionBackgroundMarkers.Clear();
            listingColumnBreaks.Clear();
            suppressedScratchSink.Clear();
            lastBareTextureValid = false;
            lastBareTipValid = false;
            pendingTipTextureValid = false;
            WidgetCapture.focusedIndex = focusedIndex;
            passOpen = true;
            passOpenFrame = Time.frameCount;
            // An unbalanced BeginScrollView/EndScrollView from a mod that threw mid-body must
            // not leak a stale container stack into this pass's recording.
            scrollContainers.Clear();
            containerStack.Clear();
            pawnTableNotes.Clear();
            nudgeLandedContainerIndexThisPass = -1;
            if (armedNudge.HasValue)
            {
                ScrollNudgeRequest req = armedNudge.Value;
                req.PassesRemaining--;
                armedNudge = req.PassesRemaining > 0 ? req : (ScrollNudgeRequest?)null;
            }
            // Live descriptor matching restarts each pass; an unconsumed post survives across
            // passes until its frame deadline, then expires here.
            liveActivateSeen = 0;
            liveActivateFireIndex = -1;
            liveAdjustSeen = 0;
            liveAdjustFireIndex = -1;
            liveRangeAdjustSeen = 0;
            liveRangeAdjustFireIndex = -1;
            if (pendingActivateSet && Time.frameCount > pendingActivateDeadline)
            {
                pendingActivateSet = false;
            }
            if (pendingAdjustSet && Time.frameCount > pendingAdjustDeadline)
            {
                pendingAdjustSet = false;
            }
            if (pendingRangeAdjustSet && Time.frameCount > pendingRangeAdjustDeadline)
            {
                pendingRangeAdjustSet = false;
            }
            // If a DrawTabs, self-captioned or CheckboxMulti body threw, its postfix never ran
            // and the depth would suppress labels and InvisibleButton rows forever.
            tabStripDepth = 0;
            selfCaptionDepth = 0;
            checkboxMultiDepth = 0;
            dropdownDepth = 0;
            labeledButtonDepth = 0;
            numericFieldDepth = 0;
            fieldLabelDepth = 0;
            percentFieldDepth = 0;
            vectorFieldDepth = 0;
            intEntryDepth = 0;
            intAdjusterDepth = 0;
            fillableBarLabelDepth = 0;
            labelDoubleDepth = 0;
            defLabelIconDepth = 0;
            hyperlinkDepth = 0;
            selectableRowDepth = 0;
            selectableDefDepth = 0;
            infoCardDepth = 0;
            listingTreeExpanderDepth = 0;
            listingTreeLabelDepth = 0;
            rangeDepth = 0;
            rangeTypeInDepth = 0;
            labelEllipsesDepth = 0;
            iconDepth = 0;
            widgetRowDefIconDepth = 0;
            toggleableIconDepth = 0;
            toggleableIconRow = -1;
            colorBoxDepth = 0;
            medicalCareDepth = 0;
            gapLinePending = false;
        }

        /// <summary>
        /// Self-heals a live pass that leaked open past its own frame. A live pass opens and
        /// closes within the same InnerWindowOnGUI call, so passOpen surviving into a later frame
        /// proves EndPass never ran (Enter closing the owning window mid-pass, say). Frame
        /// inequality is the only signal: a same-frame heuristic would misfire on a pass still
        /// legitimately mid-flight. A leaked pass can grow the items list to millions of rows, so
        /// this TrimExcess()es rather than leaving the capacity for the next BeginPass to inherit.
        /// </summary>
        internal static void EndLeakedLivePass()
        {
            if (!passOpen || detachedPass || Time.frameCount == passOpenFrame)
            {
                return;
            }
            EndPass();
            items.Clear();
            items.TrimExcess();
        }

        /// <summary>
        /// Start a capture-only pass recording into <paramref name="sink"/> instead of
        /// <see cref="Items"/>, for the inspection capture harness: its reflected FillTab runs in
        /// a Repaint event after every real window has drawn, so a regular BeginPass there would
        /// wipe the Items a GenericWindowScope captured this frame and an outstanding
        /// RequestActivate post could fire into the captured surface. A detached pass therefore
        /// leaves items and pending posts untouched and disables every scope injection path, which
        /// with the harness's Repaint-only discipline makes it mutation-inert by construction; the
        /// armed channel (<see cref="BeginArmedDetachedPass"/>) is the one sanctioned exception.
        /// Throws if any pass is open — callers must run outside the engine's own pass bracket.
        /// </summary>
        public static void BeginDetachedPass(List<CapturedWidget> sink)
        {
            // Heal a leaked live pass BEFORE the guard below can throw on it, or one leak
            // poisons every detached capture until an unrelated generic window happens to open
            // and close cleanly. A genuine same-frame conflict still throws.
            EndLeakedLivePass();
            if (passOpen || detachedPass)
            {
                throw new InvalidOperationException(
                    "WidgetCapture.BeginDetachedPass: a capture pass is already open.");
            }
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }
            detachedSink = sink;
            detachedPass = true;
            passOpen = true;
            checkTexMarkers.Clear();
            optionBackgroundMarkers.Clear();
            listingColumnBreaks.Clear();
            savedFocusedIndex = focusedIndex;
            focusedIndex = -1;
            savedTabStripDepth = tabStripDepth;
            tabStripDepth = 0;
            savedCheckboxMultiDepth = checkboxMultiDepth;
            checkboxMultiDepth = 0;
            savedDropdownDepth = dropdownDepth;
            dropdownDepth = 0;
            savedLabeledButtonDepth = labeledButtonDepth;
            labeledButtonDepth = 0;
            savedNumericFieldDepth = numericFieldDepth;
            numericFieldDepth = 0;
            savedFieldLabelDepth = fieldLabelDepth;
            fieldLabelDepth = 0;
            savedPercentFieldDepth = percentFieldDepth;
            percentFieldDepth = 0;
            savedVectorFieldDepth = vectorFieldDepth;
            vectorFieldDepth = 0;
            savedIntEntryDepth = intEntryDepth;
            intEntryDepth = 0;
            savedIntAdjusterDepth = intAdjusterDepth;
            intAdjusterDepth = 0;
            savedFillableBarLabelDepth = fillableBarLabelDepth;
            fillableBarLabelDepth = 0;
            savedLabelDoubleDepth = labelDoubleDepth;
            labelDoubleDepth = 0;
            savedDefLabelIconDepth = defLabelIconDepth;
            defLabelIconDepth = 0;
            savedHyperlinkDepth = hyperlinkDepth;
            hyperlinkDepth = 0;
            savedSelectableRowDepth = selectableRowDepth;
            selectableRowDepth = 0;
            savedSelectableDefDepth = selectableDefDepth;
            selectableDefDepth = 0;
            savedInfoCardDepth = infoCardDepth;
            infoCardDepth = 0;
            savedListingTreeExpanderDepth = listingTreeExpanderDepth;
            listingTreeExpanderDepth = 0;
            savedListingTreeLabelDepth = listingTreeLabelDepth;
            listingTreeLabelDepth = 0;
            savedRangeDepth = rangeDepth;
            rangeDepth = 0;
            savedRangeTypeInDepth = rangeTypeInDepth;
            rangeTypeInDepth = 0;
            savedLabelEllipsesDepth = labelEllipsesDepth;
            labelEllipsesDepth = 0;
            savedIconDepth = iconDepth;
            iconDepth = 0;
            savedWidgetRowDefIconDepth = widgetRowDefIconDepth;
            widgetRowDefIconDepth = 0;
            savedToggleableIconDepth = toggleableIconDepth;
            toggleableIconDepth = 0;
            toggleableIconRow = -1;
            savedColorBoxDepth = colorBoxDepth;
            colorBoxDepth = 0;
            savedMedicalCareDepth = medicalCareDepth;
            medicalCareDepth = 0;
            // An outer pass's bare-texture note must be invisible to, and unclobbered by, a
            // nested detached capture's TipRegion calls.
            savedLastBareTextureValid = lastBareTextureValid;
            lastBareTextureValid = false;
            savedLastBareTipValid = lastBareTipValid;
            lastBareTipValid = false;
            savedPendingTipTextureValid = pendingTipTextureValid;
            pendingTipTextureValid = false;
            // A self-captioned widget body that threw last pass never ran its postfix.
            selfCaptionDepth = 0;
            gapLinePending = false;
        }

        /// <summary>
        /// A detached pass that also ARMS one widget: the control matching
        /// (<paramref name="kind"/>, <paramref name="label"/>, <paramref name="ordinal"/> in draw
        /// order) has its own vanilla click handler fired as the stream re-records. At most one
        /// handler fires and everything else merely records, so no other control sees a click.
        /// Used only by <see cref="InspectTabCaptureHarness"/>.
        /// </summary>
        public static void BeginArmedDetachedPass(List<CapturedWidget> sink, WidgetKind kind, string label, int ordinal)
        {
            BeginArmedCore(sink, kind, label, ordinal, ArmedAction.Activate, 0, 0f, null);
        }

        /// <summary>
        /// Arm the ordinal-th slider matching <paramref name="label"/> to step one unit in
        /// <paramref name="direction"/> (+1/-1), the move the live keyboard adjust makes.
        /// </summary>
        public static void BeginArmedAdjustPass(List<CapturedWidget> sink, string label, int ordinal, int direction)
        {
            BeginArmedCore(sink, WidgetKind.Slider, label, ordinal, ArmedAction.Adjust, direction, 0f, null);
        }

        /// <summary>
        /// Arm the ordinal-th slider matching <paramref name="label"/> to
        /// <paramref name="value"/>, snapped to its step grid and clamped to its bounds.
        /// </summary>
        public static void BeginArmedSliderSetPass(List<CapturedWidget> sink, string label, int ordinal, float value)
        {
            BeginArmedCore(sink, WidgetKind.Slider, label, ordinal, ArmedAction.SetSlider, 0, value, null);
        }

        /// <summary>
        /// Arm the ordinal-th text field to return <paramref name="text"/> instead of its own
        /// value; the vanilla caller consumes it as native typing. Text fields record with an
        /// empty label, so identity is the ordinal among text fields alone.
        /// </summary>
        public static void BeginArmedTextSetPass(List<CapturedWidget> sink, int ordinal, string text)
        {
            BeginArmedCore(sink, WidgetKind.TextField, "", ordinal, ArmedAction.SetText, 0, 0f, text);
        }

        private static void BeginArmedCore(List<CapturedWidget> sink, WidgetKind kind, string label, int ordinal,
            ArmedAction action, int direction, float setValue, string setText)
        {
            BeginDetachedPass(sink);
            armedActive = true;
            armedKind = kind;
            armedLabel = label ?? "";
            armedOrdinal = ordinal;
            armedSeen = 0;
            armedFireIndex = null;
            armedFired = false;
            armedGateBlocked = false;
            armedAction = action;
            armedAdjustDirection = direction;
            armedSetValue = setValue;
            armedSetText = setText ?? "";
        }

        /// <summary>Whether the most recent armed pass fired its widget. Stays valid after <see cref="EndDetachedPass"/>; reset only by the next <see cref="BeginArmedDetachedPass"/>.</summary>
        public static bool ArmedFired => armedFired;

        /// <summary>Whether the most recent armed pass matched its widget but refused to fire because vanilla drew it gated (disabled/inactive). Same lifetime as <see cref="ArmedFired"/>.</summary>
        public static bool ArmedGateBlocked => armedGateBlocked;

        /// <summary>The draw-order index the most recent armed pass matched, or -1. Same lifetime as <see cref="ArmedFired"/>.</summary>
        public static int ArmedFireIndexOrMinusOne => armedFireIndex ?? -1;

        /// <summary>
        /// The armed channel's matcher, called by each Record* as the stream re-records. Returns
        /// true and pins <see cref="armedFireIndex"/> exactly once, on the ordinal-th widget of
        /// the armed kind+label, so that site can fire its vanilla handler.
        /// </summary>
        private static bool MaybeArmMatch(WidgetKind kind, string label, int index)
        {
            if (!detachedPass || !armedActive || armedFired || armedFireIndex.HasValue
                || kind != armedKind || (label ?? "") != armedLabel || RecordingSuppressed)
            {
                return false;
            }
            if (armedSeen++ == armedOrdinal)
            {
                armedFireIndex = index;
                return true;
            }
            return false;
        }

        /// <summary>Close a detached pass. Restores the scope engine's state exactly as BeginDetachedPass found it.</summary>
        public static void EndDetachedPass()
        {
            if (!detachedPass)
            {
                return;
            }
            // Must run BEFORE detachedPass flips false: CurrentSink still has to resolve to
            // detachedSink, the sink this pass's RecordCheckTexMarker candidate was counted against.
            ResolvePendingTipTexture(CurrentSink);
            if (armedActive)
            {
                armedActive = false;
                InjectedClickGuard.InFlight = false;
            }
            detachedPass = false;
            detachedSink = null;
            passOpen = false;
            focusedIndex = savedFocusedIndex;
            tabStripDepth = savedTabStripDepth;
            checkboxMultiDepth = savedCheckboxMultiDepth;
            dropdownDepth = savedDropdownDepth;
            labeledButtonDepth = savedLabeledButtonDepth;
            numericFieldDepth = savedNumericFieldDepth;
            fieldLabelDepth = savedFieldLabelDepth;
            percentFieldDepth = savedPercentFieldDepth;
            vectorFieldDepth = savedVectorFieldDepth;
            intEntryDepth = savedIntEntryDepth;
            intAdjusterDepth = savedIntAdjusterDepth;
            fillableBarLabelDepth = savedFillableBarLabelDepth;
            labelDoubleDepth = savedLabelDoubleDepth;
            defLabelIconDepth = savedDefLabelIconDepth;
            hyperlinkDepth = savedHyperlinkDepth;
            selectableRowDepth = savedSelectableRowDepth;
            selectableDefDepth = savedSelectableDefDepth;
            infoCardDepth = savedInfoCardDepth;
            listingTreeExpanderDepth = savedListingTreeExpanderDepth;
            listingTreeLabelDepth = savedListingTreeLabelDepth;
            rangeDepth = savedRangeDepth;
            rangeTypeInDepth = savedRangeTypeInDepth;
            labelEllipsesDepth = savedLabelEllipsesDepth;
            iconDepth = savedIconDepth;
            widgetRowDefIconDepth = savedWidgetRowDefIconDepth;
            toggleableIconDepth = savedToggleableIconDepth;
            toggleableIconRow = -1;
            colorBoxDepth = savedColorBoxDepth;
            medicalCareDepth = savedMedicalCareDepth;
            lastBareTextureValid = savedLastBareTextureValid;
            lastBareTipValid = savedLastBareTipValid;
            pendingTipTextureValid = savedPendingTipTextureValid;
            selfCaptionDepth = 0;
        }

        /// <summary>Stop recording. Call from the owning window's InnerWindowOnGUI postfix.</summary>
        public static void EndPass()
        {
            // Must run BEFORE passOpen flips false: CurrentSink's gates elsewhere are keyed off
            // it, and this resolves the same live sink the candidate was counted against.
            ResolvePendingTipTexture(CurrentSink);
            passOpen = false;
            focusedIndex = -1;
            // Unconsumed live descriptor posts deliberately SURVIVE EndPass and expire by frame
            // deadline in BeginPass instead: clearing here races the dispatcher against the
            // Layout/Repaint pairing, killing a post that lands between a frame's two passes.
            // pendingTextIndex/Value survive too — see the class remarks on RequestTextOverride.
            InjectedClickGuard.InFlight = false;
            gapLinePending = false;
        }

        /// <summary>
        /// Ask the live channel to force a click/flip/select on the <paramref name="ordinal"/>-th
        /// record of <paramref name="kind"/> with <paramref name="rawLabel"/>, counted over the
        /// full stream, on the next pass that reaches it within the post's lifetime. See the
        /// pending-state remarks for why this is not index-addressed.
        /// </summary>
        public static void RequestActivate(WidgetKind kind, string rawLabel, int ordinal)
        {
            pendingActivateSet = true;
            pendingActivateKind = kind;
            pendingActivateLabel = rawLabel ?? "";
            pendingActivateOrdinal = ordinal;
            pendingActivateDeadline = UnityEngine.Time.frameCount + LivePostLifetimeFrames;
        }

        /// <summary>Slider twin of <see cref="RequestActivate"/>: step the matching slider one grid unit in <paramref name="direction"/> (+1/-1); label + ordinal identify it. <paramref name="fractional"/> is the caption's proof of a fractional slider (<see cref="SliderCaption.CaptionImpliesFractional"/>).</summary>
        public static void RequestAdjust(string rawLabel, int ordinal, int direction, bool fractional = false)
        {
            pendingAdjustSet = true;
            pendingAdjustLabel = rawLabel ?? "";
            pendingAdjustOrdinal = ordinal;
            pendingAdjustDirection = direction;
            pendingAdjustFractional = fractional;
            pendingAdjustDeadline = UnityEngine.Time.frameCount + LivePostLifetimeFrames;
            if (FlightRecorder.Active)
            {
                FlightRecorder.Record("adjust", "post label='" + pendingAdjustLabel + "' ordinal=" + ordinal
                    + " dir=" + direction + (fractional ? " fractional" : ""));
            }
        }

        /// <summary>
        /// Live-channel twin of <see cref="MaybeArmMatch"/>: counts matches per kind+label in
        /// lockstep with CaptureDescriptor.OrdinalOf, so a posted ordinal re-finds its widget even
        /// when the stream shifted. At most one fire index per pass; consume sites compare their
        /// own index against it and clear the pending on fire.
        /// </summary>
        private static void MaybeLiveActivateMatch(WidgetKind kind, string label, int index)
        {
            if (detachedPass || !pendingActivateSet || liveActivateFireIndex >= 0
                || kind != pendingActivateKind || (label ?? "") != pendingActivateLabel
                || RecordingSuppressed)
            {
                return;
            }
            if (liveActivateSeen++ == pendingActivateOrdinal)
            {
                liveActivateFireIndex = index;
            }
        }

        private static void MaybeLiveAdjustMatch(string label, int index)
        {
            if (detachedPass || !pendingAdjustSet || liveAdjustFireIndex >= 0
                || (label ?? "") != pendingAdjustLabel || RecordingSuppressed)
            {
                return;
            }
            if (liveAdjustSeen++ == pendingAdjustOrdinal)
            {
                liveAdjustFireIndex = index;
            }
        }

        private static void ClearPendingActivate()
        {
            pendingActivateSet = false;
            liveActivateFireIndex = -1;
        }

        private static void ClearPendingAdjust()
        {
            pendingAdjustSet = false;
            liveAdjustFireIndex = -1;
        }

        /// <summary>Range twin of <see cref="MaybeLiveAdjustMatch"/>; called only from <see cref="RecordRange"/>, so its count stays in lockstep with CaptureDescriptor.OrdinalOf over the Range rows alone.</summary>
        private static void MaybeLiveRangeAdjustMatch(string label, int index)
        {
            if (detachedPass || !pendingRangeAdjustSet || liveRangeAdjustFireIndex >= 0
                || (label ?? "") != pendingRangeAdjustLabel || RecordingSuppressed)
            {
                return;
            }
            if (liveRangeAdjustSeen++ == pendingRangeAdjustOrdinal)
            {
                liveRangeAdjustFireIndex = index;
            }
        }

        private static void ClearPendingRangeAdjust()
        {
            pendingRangeAdjustSet = false;
            liveRangeAdjustFireIndex = -1;
        }

        /// <summary>
        /// Range twin of <see cref="RequestAdjust"/>: step ONE thumb of the matching range one
        /// grid unit in <paramref name="direction"/> (+1/-1), <paramref name="high"/> picking the
        /// maximum thumb. Ranges record with an empty label — their drawn text moves with the
        /// values, so a descriptor carrying it stops matching the moment the post takes effect —
        /// leaving the ordinal among range rows as their identity.
        /// </summary>
        public static void RequestAdjustRange(string rawLabel, int ordinal, bool high, int direction)
        {
            pendingRangeAdjustSet = true;
            pendingRangeAdjustLabel = rawLabel ?? "";
            pendingRangeAdjustOrdinal = ordinal;
            pendingRangeAdjustHigh = high;
            pendingRangeAdjustDirection = direction;
            pendingRangeAdjustDeadline = UnityEngine.Time.frameCount + LivePostLifetimeFrames;
        }

        /// <summary>
        /// Ask the text-field tap reaching index <paramref name="index"/> to return
        /// <paramref name="value"/> instead of vanilla's result, on every pass until cleared. The
        /// owning scope re-posts it each pass while its TextFieldEditSession is live, so whatever
        /// local vanilla's DoWindowContents assigns reflects the live edit.
        /// </summary>
        public static void RequestTextOverride(int index, string value)
        {
            pendingTextIndex = index;
            pendingTextValue = value ?? "";
        }

        /// <summary>Stop overriding text — call when the edit session ends (confirm, cancel, or scope teardown).</summary>
        public static void ClearTextOverride()
        {
            pendingTextIndex = null;
            pendingTextValue = null;
        }

        private static void DrawFocusRingIfFocused(int index, Rect rect)
        {
            if (index != focusedIndex || rect.width <= 0f)
            {
                return;
            }
            Rect expanded = rect.ExpandedBy(FocusRingExpand);
            Color previous = GUI.color;
            GUI.color = FocusRingColor;
            Widgets.DrawBox(expanded, 2);
            GUI.color = previous;
        }

        // Record/inject, one call per widget kind — see class remarks for why a single level
        // suffices here, unlike ListingRowCapture.

    }
}
