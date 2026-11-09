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
        internal static int RecordCheckboxMulti(Rect rect, MultiCheckboxState state)
        {
            if (!passOpen)
            {
                return -1;
            }
            gapLinePending = false;
            List<CapturedWidget> sink = CurrentSink;
            int index = sink.Count;
            MaybeArmMatch(WidgetKind.Checkbox, "", index);
            MaybeLiveActivateMatch(WidgetKind.Checkbox, "", index);
            sink.Add(new CapturedWidget
            {
                Kind = WidgetKind.Checkbox,
                Label = "",
                Rect = rect,
                ScreenRect = GuiSpace.ToScreen(rect),
                VisibleScreenRect = GuiSpace.VisibleScreenRect(rect),
                Clip = GuiSpace.CurrentClip(),
                Checked = state == MultiCheckboxState.On,
                TriState = state,
            });
            DrawFocusRingIfFocused(index, rect);
            return index;
        }

        /// <summary>
        /// Postfix half of the CheckboxMulti tap: forces the same click transition and sound
        /// vanilla's click handling would produce, for the live or the armed detached channel.
        /// </summary>
        internal static void MaybeForceCheckboxMulti(int index, ref MultiCheckboxState result)
        {
            if (index < 0 || !passOpen)
            {
                return;
            }
            if (detachedPass)
            {
                if (armedActive && !armedFired && armedFireIndex == index)
                {
                    ApplyCheckboxMultiTransition(ref result);
                    armedFired = true;
                }
                return;
            }
            if (liveActivateFireIndex != index)
            {
                return;
            }
            ClearPendingActivate();
            ApplyCheckboxMultiTransition(ref result);
        }

        private static void ApplyCheckboxMultiTransition(ref MultiCheckboxState result)
        {
            // MUTATION-C: mirrors Widgets.CheckboxMulti's own click transition
            // + sound (Widgets.cs:1347-1378); no invokable vanilla path exists
            // for a keyboard-forced state change.
            result = (result != MultiCheckboxState.Off) ? MultiCheckboxState.Off : MultiCheckboxState.On;
            if (result == MultiCheckboxState.On)
            {
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            else
            {
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }
            InjectedClickGuard.InFlight = true;
        }

        /// <summary>Listing.GapLine's tap: marks the next recorded row a heading candidate (see RecordLabel). Dormant-cheap: one bool test.</summary>
        internal static void MarkGapLinePending()
        {
            if (passOpen)
            {
                gapLinePending = true;
            }
        }

        // Widgets.RadioButOffTex is private (decompiled Verse/Widgets.cs:92) but assigned once in
        // the Widgets static constructor, so one lazy resolve is enough. A failed resolve only
        // degrades an OFF radio to a plain Button; the ON compare stands on its own, so no wrong
        // chosen state is possible.
        private static bool radioButOffTexResolved;
        private static Texture2D radioButOffTex;

        private static bool IsRadioButOffTex(Texture image)
        {
            if (!radioButOffTexResolved)
            {
                radioButOffTexResolved = true;
                FieldInfo field = AccessTools.Field(typeof(Widgets), "RadioButOffTex");
                if (field != null)
                {
                    radioButOffTex = field.GetValue(null) as Texture2D;
                }
            }
            return radioButOffTex != null && ReferenceEquals(image, radioButOffTex);
        }

        /// <summary>
        /// Shared by both CheckTexMarker taps (see <see cref="RecordCheckTexMarkerFitted"/>):
        /// resolves a drawn texture reference to the checkbox/radio state it represents, or
        /// reports "not one of those" so each caller can apply its own fallback.
        /// </summary>
        private static bool TryResolveCheckTexState(Texture image, out MultiCheckboxState state, out bool radio)
        {
            radio = false;
            if (ReferenceEquals(image, Widgets.CheckboxOnTex))
            {
                state = MultiCheckboxState.On;
                return true;
            }
            if (ReferenceEquals(image, Widgets.CheckboxOffTex))
            {
                state = MultiCheckboxState.Off;
                return true;
            }
            if (ReferenceEquals(image, Widgets.CheckboxPartialTex))
            {
                state = MultiCheckboxState.Partial;
                return true;
            }
            if (ReferenceEquals(image, Widgets.RadioButOnTex))
            {
                state = MultiCheckboxState.On;
                radio = true;
                return true;
            }
            if (IsRadioButOffTex(image))
            {
                state = MultiCheckboxState.Off;
                radio = true;
                return true;
            }
            state = default(MultiCheckboxState);
            return false;
        }

        /// <summary>
        /// GUI.DrawTexture tap (every overload; see WidgetCaptureCheckTexMarkerPatch): records a
        /// CheckTexMarker when the drawn texture reference-matches a vanilla checkbox or radio
        /// texture — RadioButtonDraw picks one of the pair from its `chosen` argument (decompiled
        /// Verse/Widgets.cs:1420-1431), so the texture reference IS the state. Order relative to
        /// the hotspot is irrelevant: markers are matched geometrically after the pass.
        /// </summary>
        internal static void RecordCheckTexMarker(Rect rect, Texture image)
        {
            if (!passOpen || (!detachedPass && ClipUnseen()))
            {
                return;
            }
            MultiCheckboxState state;
            bool radio;
            if (!TryResolveCheckTexState(image, out state, out radio))
            {
                // Hover highlights are decoration over some other widget's rect, never content.
                // Letting one count as "the most recent bare draw" made every hovered
                // highlight-then-TipRegion row (Listing_Standard.CheckboxLabeled's tooltip arm,
                // DrawHighlightIfMouseover callers) mint a phantom read-only row from its own
                // tooltip text, which the read-only sweep then dressed as a checkbox.
                if (ReferenceEquals(image, TexUI.HighlightTex) || ReferenceEquals(image, TexUI.HighlightSelectedTex))
                {
                    return;
                }
                GuiSpace.ClipKey drawClip = GuiSpace.CurrentClip();
                // Backward shape: a tip was registered on this EXACT rect/clip a moment ago with
                // nothing captured in between, and this draw is the widget it is about
                // (Gastronomy's DrawDefIcon: TipRegion then GUI.DrawTexture). Don't mint a row
                // yet — a Widgets.ButtonInvisible on this same rect may still follow and should
                // become the operable row; ResolvePendingTipTexture handles the case where
                // nothing does.
                if (lastBareTipValid && lastBareTipRect == rect && lastBareTipClip.Equals(drawClip)
                    && CurrentSink.Count == lastBareTipSinkIndex)
                {
                    lastBareTipValid = false;
                    pendingTipTextureValid = true;
                    pendingTipTextureRect = rect;
                    pendingTipTextureScreenRect = GuiSpace.ToScreen(rect);
                    pendingTipTextureVisibleScreenRect = GuiSpace.VisibleScreenRect(rect);
                    pendingTipTextureClip = drawClip;
                    pendingTipTextureResolve = lastBareTipResolve;
                    pendingTipTextureSinkIndex = CurrentSink.Count;
                    return;
                }
                // An ordinary decorative icon draw (Colony Manager Redux's disabled manager-tab
                // icon: GUI.DrawTexture, never a button, then TipRegion on the same rect). Note
                // it as the most recent bare draw so TryRecordOrphanTipRow can promote a matching
                // widgetless tip to a read-only row. One most-recent slot suffices: it only ever
                // matches the single TipRegion a mod places right after its own DrawTexture.
                //
                // EXCEPT when a widget row was just recorded at this exact rect:
                // Widgets.ButtonImage's own body draws GUI.DrawTexture(rect, tex) as part of
                // drawing itself, right after the capture prefix recorded the button's row, and
                // the following TipRegion would then mint a phantom Label row on top of it.
                List<CapturedWidget> currentSink = CurrentSink;
                if (currentSink.Count > 0 && currentSink[currentSink.Count - 1].Rect == rect)
                {
                    return;
                }
                lastBareTextureRect = rect;
                lastBareTextureClip = drawClip;
                lastBareTextureSinkIndex = currentSink.Count;
                lastBareTextureValid = true;
                return;
            }
            checkTexMarkers.Add(new CheckTexMarker { ScreenRect = GuiSpace.ToScreen(rect), VisibleScreenRect = GuiSpace.VisibleScreenRect(rect), Clip = GuiSpace.CurrentClip(), State = state, Radio = radio });
        }

        /// <summary>
        /// Second CheckTexMarker tap: <see cref="Widgets.DrawTextureFitted"/> routes through
        /// <c>GUI.DrawTextureWithTexCoords</c>, never <c>GUI.DrawTexture</c> (decompiled
        /// Verse/GenUI.cs:182-198), so <see cref="RecordCheckTexMarker"/> never sees a call drawn
        /// this way — yet mods draw read-only checkbox state through exactly this method. Taps
        /// the 8-arg core overload (Verse/Widgets.cs:3328) every other overload forwards into.
        ///
        /// Deliberately does NOT share <see cref="RecordCheckTexMarker"/>'s bare-texture
        /// orphan-tip side channel: every def/portrait/ability/faction icon funnels through
        /// DrawTextureFitted, so treating non-checkbox calls as bare-draw candidates would flood
        /// the single most-recent slot and misattribute unrelated tips. Only a genuine
        /// checkbox/radio texture reaches <see cref="checkTexMarkers"/>; everything else no-ops.
        ///
        /// As a Harmony prefix this runs before the method's own
        /// <c>Event.current.type == EventType.Repaint</c> guard, so a call site that calls every
        /// event records a marker on every pass. <see cref="GenericWindowScope"/>'s read-only
        /// checkbox latch covers the other case, where the call site itself gates on Repaint.
        /// </summary>
        internal static void RecordCheckTexMarkerFitted(Rect outerRect, Texture tex)
        {
            if (!passOpen || (!detachedPass && ClipUnseen()))
            {
                return;
            }
            MultiCheckboxState state;
            bool radio;
            if (!TryResolveCheckTexState(tex, out state, out radio))
            {
                return;
            }
            checkTexMarkers.Add(new CheckTexMarker { ScreenRect = GuiSpace.ToScreen(outerRect), VisibleScreenRect = GuiSpace.VisibleScreenRect(outerRect), Clip = GuiSpace.CurrentClip(), State = state, Radio = radio });
        }

        // See RecordCheckTexMarker's remarks and TryRecordOrphanTipRow.
        private static Rect lastBareTextureRect;
        private static GuiSpace.ClipKey lastBareTextureClip;
        private static int lastBareTextureSinkIndex = -1;
        private static bool lastBareTextureValid;

        // Backward twin of the four fields above (TipRegion, then GUI.DrawTexture, then
        // optionally Widgets.ButtonInvisible): set by NoteLastBareTip, consumed by
        // RecordCheckTexMarker's bare-texture branch.
        private static Rect lastBareTipRect;
        private static GuiSpace.ClipKey lastBareTipClip;
        private static int lastBareTipSinkIndex = -1;
        private static bool lastBareTipValid;
        private static Func<string> lastBareTipResolve;

        // The texture RecordCheckTexMarker matched against lastBareTip*, held one call further:
        // an immediately following Widgets.ButtonInvisible on the same rect becomes the operable
        // Button, otherwise ResolvePendingTipTexture mints a read-only Label row. ScreenRect and
        // VisibleScreenRect must be captured NOW, while the GUIClip stack is still the one the
        // texture drew under; resolving them at EndPass would read an already-unwound stack.
        private static Rect pendingTipTextureRect;
        private static Rect pendingTipTextureScreenRect;
        private static Rect pendingTipTextureVisibleScreenRect;
        private static GuiSpace.ClipKey pendingTipTextureClip;
        private static int pendingTipTextureSinkIndex = -1;
        private static bool pendingTipTextureValid;
        private static Func<string> pendingTipTextureResolve;

        /// <summary>
        /// A registered tip whose rect and clip exactly match the most recent bare
        /// <c>GUI.DrawTexture</c> call, with nothing captured in between, becomes a read-only
        /// Label row: something was drawn there but no interactive widget exists to name it.
        /// <paramref name="resolve"/> runs only once the cheap rect/clip/index match holds, so
        /// the hot TipRegion path stays lazy for the majority of calls that do have a widget.
        /// </summary>
        internal static bool TryRecordOrphanTipRow(Rect rect, GuiSpace.ClipKey clip, Func<string> resolve)
        {
            if (!passOpen || !lastBareTextureValid || resolve == null)
            {
                return false;
            }
            if (!(lastBareTextureRect == rect) || !lastBareTextureClip.Equals(clip)
                || CurrentSink.Count != lastBareTextureSinkIndex)
            {
                return false;
            }
            // Consumed regardless of outcome: a second TipRegion on this exact rect this frame
            // (vanilla stacks tips) must not also match.
            lastBareTextureValid = false;
            // A widget row recorded immediately before the texture, whose rect CONTAINS it,
            // already speaks for this spot: Hospitality's area swatches record a RadioButton,
            // then draw their colour texture and tip contracted by 1px inside it, which slips
            // past an exact-rect test and mints a phantom duplicate row per swatch. Containment
            // with a small epsilon, same clip, previous row only.
            List<CapturedWidget> currentSink = CurrentSink;
            if (currentSink.Count > 0)
            {
                CapturedWidget prev = currentSink[currentSink.Count - 1];
                if (prev.Kind != WidgetKind.Label && prev.Clip.Equals(clip) && RectContains(prev.Rect, rect, 2f))
                {
                    return false;
                }
            }
            string text = resolve();
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            List<CapturedWidget> sink = CurrentSink;
            int index = sink.Count;
            sink.Add(new CapturedWidget
            {
                Kind = WidgetKind.Label,
                Label = text,
                Rect = rect,
                ScreenRect = GuiSpace.ToScreen(rect),
                VisibleScreenRect = GuiSpace.VisibleScreenRect(rect),
                Clip = clip,
            });
            DrawFocusRingIfFocused(index, rect);
            return true;
        }

        /// <summary>Outer contains inner within <paramref name="epsilon"/> px on every edge.</summary>
        private static bool RectContains(Rect outer, Rect inner, float epsilon)
        {
            return inner.xMin >= outer.xMin - epsilon && inner.yMin >= outer.yMin - epsilon
                && inner.xMax <= outer.xMax + epsilon && inner.yMax <= outer.yMax + epsilon;
        }

        /// <summary>
        /// Called by TooltipCapture immediately after registering a tip: notes it as the most
        /// recent widgetless tip so a texture drawn moments later at the same rect and clip can
        /// be recognized by <see cref="RecordCheckTexMarker"/> as the tip-then-texture shape.
        /// </summary>
        internal static void NoteLastBareTip(Rect rect, GuiSpace.ClipKey clip, Func<string> resolve)
        {
            if (!passOpen)
            {
                return;
            }
            lastBareTipRect = rect;
            lastBareTipClip = clip;
            lastBareTipSinkIndex = CurrentSink.Count;
            lastBareTipResolve = resolve;
            lastBareTipValid = true;
        }

        /// <summary>
        /// Closes out a pending tip-then-texture candidate. Call from EndPass/EndDetachedPass
        /// BEFORE either tears down the sink it resolves into — detachedPass flips false first
        /// there, which would misdirect CurrentSink.
        ///
        /// Scans forward from the point the texture drew for a real widget row already occupying
        /// its exact rect and clip. A captionless InvisibleButton found there is named after the
        /// tip and promoted to an operable Button; any other kind already speaks for the rect, so
        /// the candidate is dropped rather than duplicated. Nothing found at all mints the same
        /// read-only row <see cref="TryRecordOrphanTipRow"/> mints for the forward direction.
        /// </summary>
        private static void ResolvePendingTipTexture(List<CapturedWidget> sink)
        {
            if (!pendingTipTextureValid)
            {
                return;
            }
            pendingTipTextureValid = false;
            for (int i = pendingTipTextureSinkIndex; i < sink.Count; i++)
            {
                if (sink[i].Rect == pendingTipTextureRect && sink[i].Clip.Equals(pendingTipTextureClip))
                {
                    if (sink[i].Kind == WidgetKind.InvisibleButton && string.IsNullOrEmpty(sink[i].Label))
                    {
                        string text = pendingTipTextureResolve();
                        if (!string.IsNullOrEmpty(text))
                        {
                            sink[i].Kind = WidgetKind.Button;
                            sink[i].Label = text;
                        }
                    }
                    return;
                }
            }
            AddPendingTipTextureLabel(sink);
        }

        private static void AddPendingTipTextureLabel(List<CapturedWidget> sink)
        {
            string label = pendingTipTextureResolve();
            if (string.IsNullOrEmpty(label))
            {
                return;
            }
            sink.Add(new CapturedWidget
            {
                Kind = WidgetKind.Label,
                Label = label,
                Rect = pendingTipTextureRect,
                ScreenRect = pendingTipTextureScreenRect,
                VisibleScreenRect = pendingTipTextureVisibleScreenRect,
                Clip = pendingTipTextureClip,
            });
        }
    }
}
