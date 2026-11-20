using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Window-attached scope for vanilla's <c>Dialog_Slider</c> (the integer picker behind "Pick Up
    /// Some," drug schedule timers): one content region holding the slider, plus the Buttons region
    /// captured from vanilla's Cancel/OK draw. The slider row owns Up/Down/Home/End as value
    /// adjustment; Left/Right adjust by ±10% of range.
    /// <c>Dialog_Slider</c> leaves <c>closeOnCancel</c>/<c>closeOnAccept</c> TRUE, so vanilla's own
    /// bodies are live: unblocked, Escape closes without our announcement and Enter closes WITHOUT
    /// invoking <c>confirmAction</c>. Hence <see cref="OwnsCancel"/> is unconditionally true and both
    /// paths stamp <see cref="ShellFrameStamps"/>. Digits arrive layout-resolved via
    /// <see cref="CharSink"/> and stay this dialog's own clamped entry even where a type-in box draws.
    /// </summary>
    public sealed class SliderDialogScope : ScreenScope
    {
        private readonly Window page;

        public SliderDialogScope(Window page)
        {
            this.page = page;

            Claim(SharedMenuGrammar.Cancel, OnCancel);
            Claim(SharedMenuGrammar.SearchBackspace, delegate { SliderDialogState.ProcessBackspace(); },
                when: () => SliderDialogState.HasDigitBuffer);
            Claim("sliderDialog.increment", delegate { SliderDialogState.StepUp(); });
            Claim("sliderDialog.decrement", delegate { SliderDialogState.StepDown(); });
            Claim("sliderDialog.jumpToMin", delegate { SliderDialogState.JumpToMin(); });
            Claim("sliderDialog.jumpToMax", delegate { SliderDialogState.JumpToMax(); });
        }

        public override string Name
        {
            get { return "slider-dialog"; }
        }

        public override bool IsModal
        {
            get { return !ForeignWindowAbove(); }
        }

        /// <summary>Unconditionally true: vanilla's live cancel body must never run.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>Digits type an exact value; nothing else on this screen consumes characters.</summary>
        public override ICharSink CharSink
        {
            get { return this; }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, page);
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            // Shares the range-edit dialog's region key: the from-to range being picked inside.
            return "RimWorldAccess.Shell.RangeEdit.RegionName".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return 1;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return SliderDialogState.DescribeValue(includeRange: true);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            SliderDialogState.Confirm();
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return true;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (direction > 0)
            {
                SliderDialogState.PercentUp();
            }
            else
            {
                SliderDialogState.PercentDown();
            }
        }

        protected override bool ContentItemOwnsNavigationKeys(int region, int index)
        {
            return true;
        }

        public override bool HandleChar(char c)
        {
            if (!char.IsDigit(c))
            {
                return false;
            }
            SliderDialogState.AppendDigit(c);
            return true;
        }

        private bool CursorOnSliderRow()
        {
            ListModel region = Model.CurrentRegion;
            return Model.RegionIndex == 0 && region != null && !region.IsEmpty;
        }

        internal static bool CursorOnSlider(Window window)
        {
            SliderDialogScope scope = TextDialogShared.ScopeOwning<SliderDialogScope>(
                window, delegate(SliderDialogScope s, Window w) { return s.Owns(w); });
            return scope != null && scope.CursorOnSliderRow();
        }

        /// <summary>True when an unmanaged window sits above this dialog on the stack.</summary>
        private bool ForeignWindowAbove()
        {
            IList<Window> windows = Find.WindowStack.Windows;
            bool above = false;
            for (int i = 0; i < windows.Count; i++)
            {
                Window window = windows[i];
                if (ReferenceEquals(window, page))
                {
                    above = true;
                    continue;
                }
                if (!above || window is ImmediateWindow)
                {
                    continue;
                }
                if (!ScopeForWindow.HasAttachedScope(window))
                {
                    return true;
                }
            }
            return false;
        }

        private static void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            SliderDialogState.Cancel();
        }
    }

    [HarmonyPatch(typeof(Dialog_Slider), nameof(Dialog_Slider.DoWindowContents))]
    public static class SliderDialogSliderFocusPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_Slider __instance)
        {
            SoleSliderFocus.Enter(SliderDialogScope.CursorOnSlider(__instance));
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            SoleSliderFocus.Exit();
        }
    }
}
