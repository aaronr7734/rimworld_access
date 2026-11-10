namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Where a blank text field's name can be drawn when the field carries none: a placeholder
    /// inside it, a caption directly above it, or the game's search magnifier at one end.
    ///
    /// PURE: no Unity/Verse types, links into the test project.
    /// </summary>
    public static class BlankFieldCaption
    {
        private const float AboveGapMax = 18f;
        private const float IconGapMax = 16f;

        /// <summary>Text centred inside the field: its placeholder.</summary>
        public static bool PlaceholderInside(LineBox field, LineBox label)
        {
            return field.Contains(label.CenterX, label.CenterY);
        }

        /// <summary>A caption wholly above the field, in its column, within a short gap.</summary>
        public static bool CaptionAbove(LineBox field, LineBox caption)
        {
            float gap = field.GapBelow(caption);
            return gap >= 0f && gap <= AboveGapMax && field.OverlapsColumn(caption);
        }

        /// <summary>An icon on the field's line, inside it or touching either end within a short gap.</summary>
        public static bool IconMarksEnd(LineBox field, LineBox icon)
        {
            if (field.Width <= 0f || icon.Width <= 0f || icon.Width >= field.Width)
            {
                return false;
            }
            return icon.OnLineOf(field) && icon.GapRight(field) <= IconGapMax && field.GapRight(icon) <= IconGapMax;
        }
    }
}
