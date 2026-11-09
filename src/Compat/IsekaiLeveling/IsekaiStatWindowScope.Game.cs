using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for ISEKAI RPG LEVELING's stat allocation windows
    /// (Window_StatsAttribution for pawns, Window_CreatureStats for ranked creatures), one scope
    /// over both through <see cref="IsekaiStatWindowAdapter"/>. The sighted interaction is a
    /// +/- pair per stat that edits a pending value, an Apply button and a Close button; here
    /// each stat is a stepper row (Left/Right one point, Shift, Ctrl and Ctrl+Shift the mod's
    /// own 5, 20 and 100 bulk steps, read by the mod off the same modifier keys), and Apply and
    /// Close are declared actions riding the window's own methods. God mode widens the bounds
    /// exactly as the window's handlers do. The pawn window's affected-stats list is a second,
    /// read-only region fed by <see cref="IsekaiAffectedStatsCapture"/>.
    /// </summary>
    internal sealed class IsekaiStatWindowScope : ScreenScope
    {
        private readonly Window window;
        private readonly IsekaiStatWindowAdapter adapter;
        private readonly List<string> affectedRows = new List<string>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool announcedOpen;

        private const int SummaryRows = 1;

        public IsekaiStatWindowScope(Window window, IsekaiStatWindowAdapter adapter)
        {
            this.window = window;
            this.adapter = adapter;
            Claim("isekaiStats.increaseBulk", delegate { AdjustCurrent(1); }, when: CursorOnStat);
            Claim("isekaiStats.decreaseBulk", delegate { AdjustCurrent(-1); }, when: CursorOnStat);
        }

        public override string Name => "isekai-stat-window";

        protected internal override Window OwnedWindow => window;

        /// <summary>Vanilla-mode draws the -/+ pair as ButtonText, which capture would list as window buttons.</summary>
        protected override bool CaptureWindowButtons => false;

        protected override int ContentRegionCount => adapter.IsCreature ? 1 : 2;

        protected override string ContentRegionName(int region)
        {
            return region == 0
                ? "RimWorldAccess.Compat.Isekai.StatsRegion".Translate()
                : "RimWorldAccess.Compat.Isekai.AffectedRegion".Translate();
        }

        protected override void RefreshContent()
        {
            affectedRows.Clear();
            if (adapter.IsCreature)
                return;
            List<string> rows = IsekaiAffectedStatsCapture.RowsFor(window);
            if (rows != null)
                affectedRows.AddRange(rows);
        }

        protected override int ContentItemCount(int region)
        {
            if (region == 0)
                return adapter.Ready ? SummaryRows + IsekaiCompat.StatDisplayOrder.Length : 0;
            return affectedRows.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == 1)
            {
                if (index >= 0 && index < affectedRows.Count)
                {
                    d.Label = affectedRows[index];
                    d.ReadOnly = true;
                }
                return d;
            }
            if (!adapter.Ready)
                return d;

            if (index == 0)
            {
                d.Label = "RimWorldAccess.Compat.Isekai.PointsAvailable".Translate(AvailableNow());
                var extras = new List<string>();
                int spent = adapter.PointsSpent(window);
                if (spent > 0)
                    extras.Add("RimWorldAccess.Compat.Isekai.PointsToApply".Translate(spent));
                extras.Add(adapter.LevelRankLine(window));
                d.Extras = CompatText.JoinSentences(extras);
                d.ReadOnly = true;
                return d;
            }

            int ordinal = OrdinalAt(index);
            int pending = adapter.Pending(window, ordinal);
            int original = adapter.OriginalStat(window, ordinal);
            d.Label = IsekaiCompat.StatAbbreviation(ordinal) + " " + StatName(ordinal);
            d.Role = ElementRole.Stepper;
            d.Value = pending == original
                ? pending.ToString()
                : (string)"RimWorldAccess.Compat.Isekai.PendingValue".Translate(pending, pending - original);
            d.AtMinimum = !CanDecrease(ordinal);
            d.AtMaximum = !CanIncrease(ordinal);
            d.Extras = CompatText.Flatten(StatEffects(ordinal, pending));
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            // Enter on a stepper nudges it up one, the shared stepper convention.
            if (region == 0 && index >= SummaryRows)
                AdjustContentItem(region, index, 1);
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return region == 0 && index >= SummaryRows;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (!CanAdjustContentItem(region, index))
                return;
            Adjust(OrdinalAt(index), direction);
        }

        private bool CursorOnStat()
        {
            return Model.RegionIndex == 0 && Model.CurrentRegion != null && Model.CurrentRegion.Index >= SummaryRows;
        }

        private void AdjustCurrent(int direction)
        {
            if (Model.CurrentRegion == null)
                return;
            Adjust(OrdinalAt(Model.CurrentRegion.Index), direction);
        }

        /// <summary>
        /// The window's own +/- handler, step and bounds included: bulk from the mod's modifier
        /// read, god mode lifting the floor to zero and the point cap.
        /// </summary>
        private void Adjust(int ordinal, int direction)
        {
            if (!adapter.Ready)
                return;
            int pending = adapter.Pending(window, ordinal);
            int next = pending;
            int bulk = IsekaiCompat.BulkAmount();
            bool godMode = Prefs.DevMode && DebugSettings.godMode;
            if (direction < 0)
            {
                if (!CanDecrease(ordinal))
                {
                    NumericStepperHelper.SpeakBoundary(direction);
                    return;
                }
                int floor = godMode ? 0 : adapter.OriginalStat(window, ordinal);
                next = pending - Math.Min(bulk, pending - floor);
            }
            else
            {
                if (!CanIncrease(ordinal))
                {
                    NumericStepperHelper.SpeakBoundary(direction);
                    return;
                }
                int headroom = IsekaiCompat.EffectiveMaxStat() - pending;
                if (!godMode)
                    headroom = Math.Min(headroom, AvailableNow());
                next = pending + Math.Min(bulk, headroom);
            }
            if (next == pending)
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }
            adapter.SetPending(window, ordinal, next);
            IsekaiAffectedStatsCapture.MarkDirty(window);
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            RefreshModel();
            AnnounceCurrentItem();
        }

        private int AvailableNow()
        {
            object stats = adapter.StatsOf(window);
            return stats == null ? 0 : IsekaiCompat.AvailablePoints(stats) - adapter.PointsSpent(window);
        }

        private bool CanDecrease(int ordinal)
        {
            bool godMode = Prefs.DevMode && DebugSettings.godMode;
            return godMode ? adapter.Pending(window, ordinal) > 0 : adapter.CanDecrease(window, ordinal);
        }

        private bool CanIncrease(int ordinal)
        {
            bool godMode = Prefs.DevMode && DebugSettings.godMode;
            int pending = adapter.Pending(window, ordinal);
            int max = IsekaiCompat.EffectiveMaxStat();
            return godMode ? pending < max : AvailableNow() > 0 && pending < max;
        }

        private static int OrdinalAt(int index)
        {
            return IsekaiCompat.StatDisplayOrder[index - SummaryRows];
        }

        private string StatName(int ordinal)
        {
            return adapter.IsCreature ? IsekaiCompat.CreatureStatName(ordinal) : IsekaiCompat.StatName(ordinal);
        }

        private string StatEffects(int ordinal, int value)
        {
            return adapter.IsCreature ? IsekaiCompat.CreatureStatEffects(ordinal, value) : IsekaiCompat.StatEffects(ordinal, value);
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                bool anyPending = adapter.Ready && adapter.PointsSpent(window) != 0;
                actions.Add(new ScreenAction(CompatText.ModText("Isekai_Apply"), delegate
                {
                    // The window's own confirm handler: ApplyChanges then Close.
                    adapter.Apply(window);
                    window.Close();
                }, SharedMenuGrammar.ActivateDefault,
                    disabled: !anyPending,
                    disabledReason: anyPending ? null : (string)"RimWorldAccess.Compat.Isekai.NothingToApply".Translate()));
                actions.Add(new ScreenAction(CompatText.ModText("Isekai_Close"), delegate { window.Close(); }, SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen || !adapter.Ready)
                return;
            announcedOpen = true;
            Pawn pawn = adapter.PawnOf(window);
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Isekai.StatWindowOpen".Translate(
                pawn != null ? pawn.LabelShortCap : "", adapter.LevelRankLine(window), AvailableNow()));
        }

        public override void OnPop()
        {
            base.OnPop();
            IsekaiAffectedStatsCapture.Forget(window);
        }
    }
}
