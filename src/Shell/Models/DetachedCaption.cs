namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Captions placed where the generic reader's backward adjacency search does not look: just
    /// right of a bare control on its own line (drawn before it), or a few rows above a control
    /// whose caption reads out the control's own value. Both run on every unregistered mod
    /// window, so each demands placement evidence and the caller adds a second proof.
    ///
    /// PURE: no Unity/Verse types, links into the test project.
    /// </summary>
    public static class DetachedCaption
    {
        private const float EdgeSlack = 2f;
        private const float TrailingGapMax = 16f;
        private const float AboveReachRows = 3f;

        /// <summary>The caption shares the control's line and begins at its right edge, within a short gap.</summary>
        public static bool TrailsControl(LineBox control, LineBox caption)
        {
            if (control.Width <= 0f || caption.Width <= 0f)
            {
                return false;
            }
            float gap = caption.X - control.XMax;
            return caption.CenterY >= control.Y && caption.CenterY <= control.YMax
                && gap >= -EdgeSlack && gap <= TrailingGapMax;
        }

        /// <summary>An icon on the field's line, inside it or touching either end within a short gap.</summary>
        public static bool MarksFieldEnd(LineBox field, LineBox icon)
        {
            if (field.Width <= 0f || icon.Width <= 0f || icon.Width >= field.Width)
            {
                return false;
            }
            return icon.CenterY >= field.Y && icon.CenterY <= field.YMax
                && icon.XMax >= field.X - TrailingGapMax && icon.X <= field.XMax + TrailingGapMax;
        }

        /// <summary>A small mark (an info or warning icon) drawn inside the control's box, on its line.</summary>
        public static bool InsideOnLine(LineBox control, LineBox mark)
        {
            if (control.Width <= 0f || mark.Width <= 0f || mark.Width >= control.Width)
            {
                return false;
            }
            return mark.X >= control.X - EdgeSlack && mark.XMax <= control.XMax + EdgeSlack
                && mark.CenterY >= control.Y && mark.CenterY <= control.YMax;
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
            float gap = control.Y - caption.YMax;
            return gap >= 0f && gap <= control.Height * AboveReachRows
                && caption.X < control.XMax && control.X < caption.XMax;
        }
    }
}
