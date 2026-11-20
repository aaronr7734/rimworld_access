namespace RimWorldAccess.Shell
{
    public abstract partial class ScreenScope
    {
        // A mod's slider type-in box on a content row: typing a number or pressing
        // Enter on the row edits through the box. See SliderTypeInEditor.
        private readonly SliderTypeInEditor sliderTypeIn = new SliderTypeInEditor();
        private bool ownEditReturnPending;

        /// <summary>
        /// True during an <see cref="OnFocus"/> that returns from this scope's own text edit, whose
        /// exit already arranged the row's re-read; a subclass that announces on focus skips it.
        /// </summary>
        protected bool ReturnFromOwnEdit { get; private set; }

        /// <summary>
        /// The raw value of the game slider behind a content row, which is what a type-in box shows
        /// and what typing sets; <paramref name="high"/> picks a range's maximum box. Override for
        /// rows drawn by the game's own slider or range widgets. The capture that rings the row must
        /// also report it to <see cref="SliderTypeInFocus.CursorOnDrawingSlider"/>.
        /// </summary>
        protected virtual bool TryGetSliderBox(int region, int index, out float value, out bool high)
        {
            value = 0f;
            high = false;
            return false;
        }

        /// <summary>
        /// True for a slider row that already has the scope's own exact number entry, which keeps
        /// the game's limits a type-in box would skip. The box's number is still spoken there, but
        /// typing stays with the scope.
        /// </summary>
        protected virtual bool HasOwnNumberEntry(int region, int index)
        {
            return false;
        }

        private bool FocusedSliderBoxTypable(out float value, out bool high)
        {
            return FocusedSliderBox(out value, out high)
                && !HasOwnNumberEntry(Model.RegionIndex, Model.CurrentRegion.Index);
        }

        private bool FocusedSliderBox(out float value, out bool high)
        {
            value = 0f;
            high = false;
            if (!SliderTypeInFocus.BoxesAvailable || InExtrasRegion() || InActionsRegion() || Model.CurrentTable != null)
            {
                return false;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return false;
            }
            return TryGetSliderBox(Model.RegionIndex, region.Index, out value, out high);
        }

        private string FocusedRowLabel()
        {
            return DescribeContentItem(Model.RegionIndex, Model.CurrentRegion.Index)?.Label;
        }

        private bool TryBeginSliderTypeIn(bool announcePrompt)
        {
            float value;
            bool high;
            return FocusedSliderBoxTypable(out value, out high)
                && sliderTypeIn.TryBegin(value, high, FocusedRowLabel(), announcePrompt, OnSliderTypeInExit, AnnounceCurrentItem);
        }

        private bool TryTypeIntoSliderBox(char c)
        {
            float value;
            bool high;
            return SliderTypeInText.StartsNumber(c)
                && FocusedSliderBoxTypable(out value, out high)
                && sliderTypeIn.TryBeginFromChar(c, value, high, FocusedRowLabel(), OnSliderTypeInExit, AnnounceCurrentItem);
        }

        private bool HasTypableSliderBox()
        {
            float value;
            bool high;
            return FocusedSliderBoxTypable(out value, out high);
        }

        private void ApplySliderBoxValue(ElementDescription d)
        {
            float value;
            bool high;
            if (FocusedSliderBox(out value, out high))
            {
                SliderTypeInText.ApplyBoxValue(d, value);
            }
        }

        private void OnSliderTypeInExit()
        {
            // The new value reaches the row a pass or two later, and that re-read is the only one.
            SuppressNextEntryAnnouncement();
            ownEditReturnPending = true;
        }

        private void CancelSliderTypeIn()
        {
            sliderTypeIn.Cancel();
        }
    }
}
