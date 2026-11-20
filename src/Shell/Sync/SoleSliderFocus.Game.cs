namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Focus for a window whose body draws exactly one slider or range and has no widget capture.
    /// A bracket around that body reports whether the owning scope's cursor is on the control, so
    /// whatever slider draws inside the bracket is the focused one.
    /// </summary>
    internal static class SoleSliderFocus
    {
        private static int depth;
        private static bool cursorOnSlider;

        internal static bool DrawingFocusedSlider
        {
            get { return depth > 0 && cursorOnSlider; }
        }

        internal static void Enter(bool onSlider)
        {
            depth++;
            cursorOnSlider = onSlider;
        }

        internal static void Exit()
        {
            if (depth > 0)
            {
                depth--;
            }
            if (depth == 0)
            {
                cursorOnSlider = false;
            }
        }
    }
}
