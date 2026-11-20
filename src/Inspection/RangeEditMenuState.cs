using System;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Facade for the hit-points / quality / mental-break-chance range sub-editor, shared by
    /// four callers: <see cref="BillConfigState"/>, the two filter-tree screens
    /// (<see cref="RimWorldAccess.Shell.StorageSettingsScope"/>,
    /// <see cref="RimWorldAccess.Shell.ThingFilterMenuScope"/>) and the policy window
    /// (<see cref="RimWorldAccess.Shell.FilterPolicyDialogScope"/>). All cursor/navigation logic
    /// lives in <see cref="RimWorldAccess.Shell.RangeEditScope"/>, a two-row (Min, Max)
    /// ScreenScope subclass riding ScreenModel directly (the migration family's D1 ruling:
    /// "states remain facades" — no <c>selectedOption</c> field survives here, per D2). This
    /// class stores no range of its own: it is a two-row stepper facade over the accessor pair
    /// its caller supplies, writing the real setting through on every press, exactly as vanilla's
    /// slider writes on every frame of a drag.
    /// </summary>
    public static class RangeEditMenuState
    {
        public enum RangeType
        {
            HitPoints,
            Quality,
            MentalBreakChance,
            Percent
        }

        private static bool isActive = false;
        private static RangeType currentRangeType;
        private static Func<FloatRange> getFloatRange;
        private static Action<FloatRange> setFloatRange;
        private static Func<QualityRange> getQualityRange;
        private static Action<QualityRange> setQualityRange;
        private static Action onClosed;
        private static string percentNoun;
        private static float percentStep;
        private static object typeInBoxOwner;
        private static int typeInBoxDrawnFrame = -1;
        private static int rangeDrawDepth;

        public static bool IsActive => isActive;

        /// <summary>The open range is drawn by a game range widget keyed by <paramref name="owner"/>; see <see cref="EnterRangeDraw"/>.</summary>
        public static void OfferTypeInBox(object owner)
        {
            typeInBoxOwner = owner;
            typeInBoxDrawnFrame = -1;
        }

        /// <summary>The range widget drawing right now is the one this editor edits.</summary>
        internal static bool DrawingEditedRange => rangeDrawDepth > 0;

        /// <summary>Brackets a range widget's draw; true (pair with <see cref="ExitRangeDraw"/>) only for the edited range.</summary>
        internal static bool EnterRangeDraw(object owner, RangeType type)
        {
            if (!isActive || typeInBoxOwner == null || !ReferenceEquals(owner, typeInBoxOwner) || type != currentRangeType)
            {
                return false;
            }
            rangeDrawDepth++;
            typeInBoxDrawnFrame = Time.frameCount;
            return true;
        }

        internal static void ExitRangeDraw()
        {
            if (rangeDrawDepth > 0)
            {
                rangeDrawDepth--;
            }
        }

        /// <summary>The raw bound a type-in box shows, while the edited range's widget is still drawing.</summary>
        internal static bool TryGetTypeInBoxValue(int selectedOption, out float value)
        {
            value = 0f;
            if (!isActive || typeInBoxOwner == null || currentRangeType == RangeType.Quality || getFloatRange == null
                || typeInBoxDrawnFrame < 0 || Time.frameCount - typeInBoxDrawnFrame > 2)
            {
                return false;
            }
            FloatRange range = getFloatRange();
            value = selectedOption == 0 ? range.min : range.max;
            return true;
        }

        public static void OpenHitPointsRange(Func<FloatRange> get, Action<FloatRange> set, Action onClosed = null)
        {
            isActive = true;
            typeInBoxOwner = null;
            currentRangeType = RangeType.HitPoints;
            getFloatRange = get;
            setFloatRange = set;
            RangeEditMenuState.onClosed = onClosed;
        }

        public static void OpenQualityRange(Func<QualityRange> get, Action<QualityRange> set, Action onClosed = null)
        {
            isActive = true;
            typeInBoxOwner = null;
            currentRangeType = RangeType.Quality;
            getQualityRange = get;
            setQualityRange = set;
            RangeEditMenuState.onClosed = onClosed;
        }

        /// <summary>
        /// Reading Policy "Book Effects" only (Anomaly). Labelled with vanilla's bare noun
        /// "BookMentalBreakChance"; the widget's own "MaxMentalBreakChance" key is a template
        /// (Verse/ThingFilterUI.cs:113-121).
        /// </summary>
        public static void OpenMentalBreakChanceRange(Func<FloatRange> get, Action<FloatRange> set, Action onClosed = null)
        {
            isActive = true;
            typeInBoxOwner = null;
            currentRangeType = RangeType.MentalBreakChance;
            getFloatRange = get;
            setFloatRange = set;
            RangeEditMenuState.onClosed = onClosed;
        }

        /// <summary>
        /// Generic 0..1 percent range under a caller-supplied noun (e.g. the game's own
        /// "HarmedRevengeChance" label for hunting policies). One-decimal display so
        /// sub-percent animal revenge chances stay distinguishable.
        /// </summary>
        public static void OpenPercentRange(string noun, Func<FloatRange> get, Action<FloatRange> set,
            float step = 0.01f, Action onClosed = null)
        {
            isActive = true;
            typeInBoxOwner = null;
            currentRangeType = RangeType.Percent;
            percentNoun = noun;
            percentStep = step;
            getFloatRange = get;
            setFloatRange = set;
            RangeEditMenuState.onClosed = onClosed;
        }

        /// <summary>Closes the submenu and releases the caller's accessors. Silent — safe as a StateResetRegistry entry.</summary>
        public static void Close()
        {
            isActive = false;
            getFloatRange = null;
            setFloatRange = null;
            getQualityRange = null;
            setQualityRange = null;
            onClosed = null;
            percentNoun = null;
            typeInBoxOwner = null;
        }

        /// <summary>
        /// Enter or Escape on the sub-editor. Both do the same thing, because every step was
        /// already committed through the caller's setter — there is nothing left to apply and
        /// nothing left to discard, exactly as when a sighted player releases the slider.
        /// </summary>
        public static void Dismiss()
        {
            if (!isActive) return;
            Action closed = onClosed;
            Close();
            closed?.Invoke();
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Increases the value at <paramref name="selectedOption"/> (0 = Min, 1 = Max) and speaks
        /// the result. <paramref name="selectedOption"/> comes from the scope's own ScreenModel
        /// region cursor (D2: no selection arithmetic survives here).
        /// </summary>
        public static void IncreaseValue(int selectedOption)
        {
            AdjustValue(selectedOption, 1);
        }

        /// <summary>Decreases the value at <paramref name="selectedOption"/> (0 = Min, 1 = Max) and speaks the result.</summary>
        public static void DecreaseValue(int selectedOption)
        {
            AdjustValue(selectedOption, -1);
        }

        private static void AdjustValue(int selectedOption, int direction)
        {
            switch (currentRangeType)
            {
                case RangeType.HitPoints:
                {
                    if (getFloatRange == null || setFloatRange == null) return;
                    const float step = 0.1f;
                    FloatRange range = getFloatRange();
                    if (selectedOption == 0)
                    {
                        range.min = direction > 0
                            ? Mathf.Min(range.max - step, range.min + step)
                            : Mathf.Max(0f, range.min - step);
                    }
                    else
                    {
                        range.max = direction > 0
                            ? Mathf.Min(1f, range.max + step)
                            : Mathf.Max(range.min + step, range.max - step);
                    }
                    setFloatRange(range);
                    break;
                }
                case RangeType.Quality:
                {
                    if (getQualityRange == null || setQualityRange == null) return;
                    QualityCategory[] qualities = (QualityCategory[])Enum.GetValues(typeof(QualityCategory));
                    QualityRange range = getQualityRange();
                    if (selectedOption == 0)
                    {
                        int minIndex = Array.IndexOf(qualities, range.min);
                        if (direction > 0)
                        {
                            if (minIndex < Array.IndexOf(qualities, range.max))
                                range.min = qualities[minIndex + 1];
                        }
                        else if (minIndex > 0)
                        {
                            range.min = qualities[minIndex - 1];
                        }
                    }
                    else
                    {
                        int maxIndex = Array.IndexOf(qualities, range.max);
                        if (direction > 0)
                        {
                            if (maxIndex < qualities.Length - 1)
                                range.max = qualities[maxIndex + 1];
                        }
                        else if (maxIndex > Array.IndexOf(qualities, range.min))
                        {
                            range.max = qualities[maxIndex - 1];
                        }
                    }
                    setQualityRange(range);
                    break;
                }
                case RangeType.Percent:
                {
                    if (getFloatRange == null || setFloatRange == null) return;
                    float step = percentStep;
                    FloatRange range = getFloatRange();
                    if (selectedOption == 0)
                    {
                        range.min = direction > 0
                            ? Mathf.Min(range.max, range.min + step)
                            : Mathf.Max(0f, range.min - step);
                    }
                    else
                    {
                        range.max = direction > 0
                            ? Mathf.Min(1f, range.max + step)
                            : Mathf.Max(range.min, range.max - step);
                    }
                    setFloatRange(range);
                    break;
                }
                case RangeType.MentalBreakChance:
                {
                    if (getFloatRange == null || setFloatRange == null) return;
                    // Same 5% step as the retired inline slider's MentalBreakChance case.
                    const float step = 0.05f;
                    FloatRange range = getFloatRange();
                    if (selectedOption == 0)
                    {
                        range.min = direction > 0
                            ? Mathf.Min(range.max, range.min + step)
                            : Mathf.Max(0f, range.min - step);
                    }
                    else
                    {
                        range.max = direction > 0
                            ? Mathf.Min(1f, range.max + step)
                            : Mathf.Max(range.min, range.max - step);
                    }
                    setFloatRange(range);
                    break;
                }
            }

            TolkHelper.SpeakData(BuildAnnouncement(selectedOption));
        }

        /// <summary>
        /// The two-row stepper announcement (selected value + both bounds) — used both as the
        /// scope's on-focus row Label and as the post-adjust speech, so the two are always
        /// byte-identical. Ported verbatim from the retired instance method's composition logic.
        /// </summary>
        public static string BuildAnnouncement(int selectedOption)
        {
            string optionName = selectedOption == 0
                ? "RimWorldAccess.Inspection.RangeEdit.MinOption".Translate()
                : "RimWorldAccess.Inspection.RangeEdit.MaxOption".Translate();

            switch (currentRangeType)
            {
                case RangeType.HitPoints:
                {
                    if (getFloatRange == null) return string.Empty;
                    FloatRange range = getFloatRange();
                    float value = selectedOption == 0 ? range.min : range.max;
                    string hitPointsNoun = "HitPointsBasic".Translate().ToString().CapitalizeFirst();
                    return "RimWorldAccess.Inspection.RangeEdit.HitPointsLine".Translate(
                        hitPointsNoun,
                        optionName,
                        value.ToString("P0"),
                        range.min.ToString("P0"),
                        range.max.ToString("P0"));
                }
                case RangeType.Percent:
                {
                    if (getFloatRange == null) return string.Empty;
                    FloatRange range = getFloatRange();
                    float value = selectedOption == 0 ? range.min : range.max;
                    return "RimWorldAccess.Inspection.RangeEdit.PercentLine".Translate(
                        percentNoun,
                        optionName,
                        value.ToString("P1"),
                        range.min.ToString("P1"),
                        range.max.ToString("P1"));
                }
                case RangeType.MentalBreakChance:
                {
                    if (getFloatRange == null) return string.Empty;
                    FloatRange range = getFloatRange();
                    float value = selectedOption == 0 ? range.min : range.max;
                    string noun = "BookMentalBreakChance".Translate();
                    return "RimWorldAccess.Inspection.RangeEdit.MentalBreakChanceLine".Translate(
                        noun,
                        optionName,
                        value.ToString("P0"),
                        range.min.ToString("P0"),
                        range.max.ToString("P0"));
                }
                default: // Quality
                {
                    if (getQualityRange == null) return string.Empty;
                    QualityRange range = getQualityRange();
                    QualityCategory value = selectedOption == 0 ? range.min : range.max;
                    return "RimWorldAccess.Inspection.RangeEdit.QualityLine".Translate(
                        "Quality".Translate(),
                        optionName,
                        value.GetLabel().CapitalizeFirst(),
                        range.min.GetLabel().CapitalizeFirst(),
                        range.max.GetLabel().CapitalizeFirst());
                }
            }
        }
    }
}
