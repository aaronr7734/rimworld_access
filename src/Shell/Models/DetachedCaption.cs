namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Text and marks placed where the generic reader's backward adjacency search does not look:
    /// a caption just right of a bare control on its own line (drawn before it), a caption a few
    /// rows above a control whose caption reads out the control's own value, or a small icon
    /// drawn inside a control. All run on every unregistered mod window, so each demands
    /// placement evidence and the caller adds a second proof.
    ///
    /// PURE: no Unity/Verse types, links into the test project.
    /// </summary>
    public static class DetachedCaption
    {
        private const float TrailingGapMax = 16f;
        private const float AboveReachRows = 3f;

        /// <summary>The caption shares the control's line and begins at its right edge, within a short gap.</summary>
        public static bool TrailsControl(LineBox control, LineBox caption)
        {
            if (control.Width <= 0f || caption.Width <= 0f)
            {
                return false;
            }
            float gap = caption.GapRight(control);
            return caption.OnLineOf(control) && gap >= -LineBox.EdgeSlack && gap <= TrailingGapMax;
        }

        /// <summary>A small mark (an info or warning icon) drawn inside the control's box, on its line.</summary>
        public static bool InsideOnLine(LineBox control, LineBox mark)
        {
            if (control.Width <= 0f || mark.Width <= 0f || mark.Width >= control.Width)
            {
                return false;
            }
            return mark.Inside(control) && mark.OnLineOf(control);
        }

        /// <summary>
        /// The caption sits wholly above the control, in its column, no further up than three of
        /// the control's own heights.
        /// </summary>
        public static bool AboveInReach(LineBox control, LineBox caption)
        {
            if (control.Width <= 0f || caption.Width <= 0f || control.Height <= 0f)
            {
                return false;
            }
            float gap = control.GapBelow(caption);
            return gap >= 0f && gap <= control.Height * AboveReachRows && caption.OverlapsColumn(control);
        }
    }
}
