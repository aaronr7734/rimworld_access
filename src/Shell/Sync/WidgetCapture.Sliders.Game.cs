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
        // The precision the caller rounds a roundTo-less slider's result to, or 0: a finer
        // keyboard step would be rounded away, a coarser one skips values it stores.
        private static double declaredStepGrid;

        internal static void EnterDeclaredStepGrid(double grid)
        {
            declaredStepGrid = grid;
        }

        internal static void ExitDeclaredStepGrid()
        {
            declaredStepGrid = 0.0;
        }

        internal static int RecordSlider(Rect rect, string label, string leftAlignedLabel, string rightAlignedLabel, float value, float min, float max, float roundTo, int controlId = 0)
        {
            if (!passOpen)
            {
                return -1;
            }
            gapLinePending = false;
            string effectiveLabel = SliderCaption.EffectiveLabel(label, leftAlignedLabel, rightAlignedLabel, min, max);
            List<CapturedWidget> sink = CurrentSink;
            int index = sink.Count;
            MaybeArmMatch(WidgetKind.Slider, effectiveLabel, index);
            MaybeLiveAdjustMatch(effectiveLabel, index);
            MaybeMatchFocusedContentControl(WidgetKind.Slider, effectiveLabel, index);
            sink.Add(new CapturedWidget
            {
                Kind = WidgetKind.Slider,
                Label = effectiveLabel,
                Rect = rect,
                ScreenRect = GuiSpace.ToScreen(rect),
                VisibleScreenRect = GuiSpace.VisibleScreenRect(rect),
                Clip = GuiSpace.CurrentClip(),
                SliderValue = value,
                SliderMin = min,
                SliderMax = max,
                SliderRoundTo = roundTo,
                SliderValueText = SliderCaption.EffectiveValueText(leftAlignedLabel, rightAlignedLabel, min, max),
                SliderControlId = controlId,
            });
            NoteValueControl(sink, index);
            DrawFocusRingIfFocused(index, rect);
            return index;
        }

        internal static void MaybeStepSlider(int index, float min, float max, float roundTo, ref float result)
        {
            if (index < 0 || !passOpen)
            {
                return;
            }
            if (detachedPass)
            {
                // Armed detached channel: a slider has no gate and spawns nothing, so no InjectedClickGuard.
                if (armedActive && !armedFired && armedFireIndex == index)
                {
                    if (armedAction == ArmedAction.Adjust)
                    {
                        result = SliderStep.Stepped(result, armedAdjustDirection, min, max, roundTo, grid: declaredStepGrid);
                        armedFired = true;
                    }
                    else if (armedAction == ArmedAction.SetSlider)
                    {
                        // Quantizes an EXTERNALLY supplied value, never the slider's own current one.
                        result = SliderStep.Snapped(armedSetValue, min, max, roundTo, declaredStepGrid);
                        armedFired = true;
                    }
                }
                return;
            }
            if (liveAdjustFireIndex == index)
            {
                // Exactly one step from the CURRENT value, never snapped onto a grid first:
                // snapping the live value breaks reversibility for a non-grid-aligned start.
                float before = result;
                result = SliderStep.Stepped(result, pendingAdjustDirection, min, max, roundTo, pendingAdjustFractional, declaredStepGrid);
                ClearPendingAdjust();
                if (FlightRecorder.Active)
                {
                    FlightRecorder.Record("adjust", "fire index=" + index + " range=" + min + ".." + max
                        + " value=" + before + "->" + result);
                }
            }
        }

        /// <summary>
        /// One Widgets.IntRange / FloatRange / QualityRange control, from its bracket's prefix.
        /// The ONE recorder a bracket owns rather than enriches: vanilla draws both thumbs with
        /// raw GUI.DrawTexture and moves them from raw Event.current state, so no captured
        /// primitive can carry the control. Recorded BEFORE the body runs, so the "min - max"
        /// Label the core draws next lands immediately after it — the pairing the scope folds on.
        /// </summary>
        internal static int RecordRange(Rect rect, RangeFamily family, float low, float high,
            float limitMin, float limitMax, float gap, float roundTo, ToStringStyle style)
        {
            if (!passOpen)
            {
                return -1;
            }
            gapLinePending = false;
            List<CapturedWidget> sink = CurrentSink;
            int index = sink.Count;
            // Empty label by construction — see RequestAdjustRange. No MaybeArmMatch: the armed
            // detached channel has no range action.
            MaybeLiveRangeAdjustMatch("", index);
            MaybeMatchFocusedContentControl(WidgetKind.Range, "", index);
            sink.Add(new CapturedWidget
            {
                Kind = WidgetKind.Range,
                Label = "",
                Rect = rect,
                ScreenRect = GuiSpace.ToScreen(rect),
                VisibleScreenRect = GuiSpace.VisibleScreenRect(rect),
                Clip = GuiSpace.CurrentClip(),
                RangeFamily = family,
                RangeLow = low,
                RangeHigh = high,
                RangeLimitMin = limitMin,
                RangeLimitMax = limitMax,
                RangeGap = gap,
                RangeRoundTo = roundTo,
                RangeStyle = style,
            });
            NoteValueControl(sink, index);
            DrawFocusRingIfFocused(index, rect);
            return index;
        }

        /// <summary>
        /// Whether the pending range post addresses the control at <paramref name="index"/>,
        /// consuming it if so. A DETACHED pass never matches: injection is disabled there and the
        /// armed channel has no range action.
        /// </summary>
        private static bool ConsumeRangeAdjust(int index, out bool high, out int direction)
        {
            high = false;
            direction = 0;
            if (index < 0 || !passOpen || detachedPass || liveRangeAdjustFireIndex != index)
            {
                return false;
            }
            high = pendingRangeAdjustHigh;
            direction = pendingRangeAdjustDirection;
            ClearPendingRangeAdjust();
            return true;
        }

        /// <summary>
        /// The keyboard twin of vanilla's own range drag block: steps ONE thumb onto the next grid
        /// point, then re-applies vanilla's gap clamp and roundTo rounding so a keyboard adjust
        /// can never produce a range the mouse could not.
        ///
        /// One mirror covers all three cores because they run the same arithmetic: IntRange's
        /// <c>minWidth</c> IS FloatRange's <c>gap</c> and its extra re-clamps cannot bind for a
        /// value already clamped into [limitMin, limitMax] with a non-negative gap, and
        /// QualityRange is the gap == 0 case, where FloatRange's pre-clamp is a no-op for the
        /// same reason.
        ///
        /// The STEP is vanilla's own declared granularity, never an invented one: a FloatRange's
        /// <c>roundTo</c> where it declares one, and 1 for IntRange and QualityRange, whose drags
        /// quantize with Mathf.RoundToInt. A FloatRange with no roundTo is pixel-continuous under
        /// the mouse and declares no grid, so <see cref="SliderStep"/> supplies the step.
        /// </summary>
        private static void ApplyRangeAdjust(ref float low, ref float high, bool adjustHigh, int direction,
            float limitMin, float limitMax, float gap, float roundTo)
        {
            if (adjustHigh)
            {
                float value = SliderStep.Stepped(high, direction, limitMin, limitMax, roundTo, grid: declaredStepGrid);
                high =Mathf.Max(value, limitMin + gap);
                if (low > high - gap)
                {
                    low = high - gap;
                }
            }
            else
            {
                float value = SliderStep.Stepped(low, direction, limitMin, limitMax, roundTo, grid: declaredStepGrid);
                low = Mathf.Min(value, limitMax - gap);
                if (high < low + gap)
                {
                    high = low + gap;
                }
            }
            if (roundTo != 0f)
            {
                low = Mathf.Round(low / roundTo) * roundTo;
                high = Mathf.Round(high / roundTo) * roundTo;
            }
        }

        /// <summary>Slider twin (<see cref="MaybeStepSlider"/>) for Widgets.IntRange, consumed inside the patched call by writing its own <c>ref IntRange</c>.</summary>
        internal static void MaybeAdjustIntRange(int index, ref IntRange range, int limitMin, int limitMax, int minWidth)
        {
            bool high;
            int direction;
            if (!ConsumeRangeAdjust(index, out high, out direction))
            {
                return;
            }
            float low = range.min;
            float top = range.max;
            // Integer granularity is vanilla's own: its drag rounds the dragged position with
            // Mathf.RoundToInt before writing either end.
            ApplyRangeAdjust(ref low, ref top, high, direction, limitMin, limitMax, minWidth, 1f);
            // MUTATION-C: mirrors Verse/Widgets.cs:2382-2412 (IntRange's own
            // drag block, minWidth clamp included); vanilla moves both thumbs
            // from raw Event.current state with no Try*/Can* twin to ride.
            range.min = Mathf.RoundToInt(low);
            range.max = Mathf.RoundToInt(top);
        }

        /// <summary>Slider twin (<see cref="MaybeStepSlider"/>) for Widgets.FloatRange.</summary>
        internal static void MaybeAdjustFloatRange(int index, ref FloatRange range, float limitMin, float limitMax, float gap, float roundTo)
        {
            bool high;
            int direction;
            if (!ConsumeRangeAdjust(index, out high, out direction))
            {
                return;
            }
            float low = range.min;
            float top = range.max;
            ApplyRangeAdjust(ref low, ref top, high, direction, limitMin, limitMax, gap, roundTo);
            // MUTATION-C: mirrors Verse/Widgets.cs:2283-2308 (FloatRange's own
            // drag block, gap clamp and roundTo rounding included); vanilla
            // moves both thumbs from raw Event.current state, no A/B vehicle.
            range.min = low;
            range.max = top;
        }

        /// <summary>Slider twin (<see cref="MaybeStepSlider"/>) for Widgets.QualityRange; bounds are the quality enum's own extent.</summary>
        internal static void MaybeAdjustQualityRange(int index, ref RimWorld.QualityRange range)
        {
            bool high;
            int direction;
            if (!ConsumeRangeAdjust(index, out high, out direction))
            {
                return;
            }
            float low = (int)range.min;
            float top = (int)range.max;
            // Quality drags quantize to whole categories over 0..QualityCount-1 and let the two
            // ends meet: step 1, gap 0.
            ApplyRangeAdjust(ref low, ref top, high, direction, 0f, QualityUtility.QualityCount - 1, 0f, 1f);
            // MUTATION-C: mirrors Verse/Widgets.cs:2493-2513 (QualityRange's
            // own drag block and its meet-in-the-middle clamp); vanilla moves
            // both thumbs from raw Event.current state, no A/B vehicle.
            range.min = (QualityCategory)Mathf.RoundToInt(low);
            range.max = (QualityCategory)Mathf.RoundToInt(top);
        }

        internal static int RecordTextField(Rect rect, string text, bool multiLine)
        {
            if (!passOpen)
            {
                return -1;
            }
            gapLinePending = false;
            List<CapturedWidget> sink = CurrentSink;
            int index = sink.Count;
            // Text fields record with an empty Label — any adjacent caption is a separate Label
            // row — so their stable identity is the ordinal among text fields alone. A labeled
            // wrapper's caption rides FieldLabel, never Label, so enrichment cannot shift it.
            MaybeArmMatch(WidgetKind.TextField, "", index);
            CapturedWidget row = new CapturedWidget { Kind = WidgetKind.TextField, Rect = rect, ScreenRect = GuiSpace.ToScreen(rect), VisibleScreenRect = GuiSpace.VisibleScreenRect(rect), Clip = GuiSpace.CurrentClip(), Text = text ?? "", MultiLine = multiLine };
            if (numericFieldDepth > 0)
            {
                row.NumericField = true;
                row.NumericIsInt = numericFieldIsInt;
                row.NumericMin = numericFieldMin;
                row.NumericMax = numericFieldMax;
                row.NumericPercent = percentFieldDepth > 0;
            }
            if (intEntryDepth > 0)
            {
                row.StepperValueField = true;
            }
            if (sliderTypeInDepth > 0)
            {
                StampSliderTypeIn(row);
            }
            else if (vectorFieldDepth > 0 && numericFieldDepth > 0)
            {
                row.FieldLabel = vectorAxisLabel ?? "";
            }
            else if (rangeTypeInDepth > 0)
            {
                // FloatRangeWithTypeIn's two boxes, in draw order: min then max. Vanilla captions
                // neither; they are told apart only by sitting either side of the slider.
                row.FieldLabel = (rangeTypeInOrdinal++ == 0
                    ? "RimWorldAccess.UI.GenericWindow.RangeMinimum"
                    : "RimWorldAccess.UI.GenericWindow.RangeMaximum").Translate().ToString();
            }
            else if (fieldLabelDepth > 0)
            {
                row.FieldLabel = fieldLabelText ?? "";
            }
            sink.Add(row);
            DrawFocusRingIfFocused(index, rect);
            return index;
        }

        internal static void MaybeOverrideText(int index, ref string result)
        {
            if (index < 0 || !passOpen)
            {
                return;
            }
            if (detachedPass)
            {
                // Armed detached channel: hand this one field the caller's string in place of
                // vanilla's result, which the caller consumes exactly as native typing.
                // One-shot by construction — armedFired latches.
                if (armedActive && !armedFired && armedFireIndex == index && armedAction == ArmedAction.SetText)
                {
                    result = armedSetText;
                    armedFired = true;
                }
                return;
            }
            if (pendingTextIndex.HasValue && pendingTextIndex.Value == index)
            {
                result = pendingTextValue ?? "";
            }
        }

    }
}
