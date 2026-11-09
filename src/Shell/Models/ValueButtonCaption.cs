namespace RimWorldAccess.Shell
{
    /// <summary>One side of a <see cref="ValueButtonCaption"/> pairing, described without any GUI/game types.</summary>
    public struct ValueButtonPart
    {
        /// <summary>The rect the pairing is judged on: raw screen rect within one clip, visible rect across clips.</summary>
        public LineBox Box;

        /// <summary>Opaque identity of the row's clipping context; only equality is used.</summary>
        public int ClipId;

        /// <summary>Drawn by the window frame (close-X, bottom Close button) rather than its contents.</summary>
        public bool IsChrome;
    }

    /// <summary>
    /// Whether a plain Label names a text button whose own caption is the row's value or verb
    /// ("Input field display style" beside "Hover"). Two shapes qualify: the label IS the row,
    /// spanning the button on its line, or the label sits immediately left of the button on the
    /// same line. A frame button never takes a content caption: Tech Advancing's full-width legend
    /// line ends level with the window's bottom Close button, and on geometry alone that line hosts it.
    ///
    /// PURE: no Unity/Verse types, links into the test project.
    /// </summary>
    public static class ValueButtonCaption
    {
        /// <summary>Widest gap a beside caption may leave before the button; a label stranded across the line names something else.</summary>
        private const float MaxBesideGap = 12f;

        /// <param name="labelDrawnJustBefore">Nothing was captured between the label and the button.</param>
        /// <param name="buttonLeadsCluster">Another button follows on the same line; a caption beside a run names the run, not its first member.</param>
        public static bool Captions(ValueButtonPart label, ValueButtonPart button, bool labelDrawnJustBefore, bool buttonLeadsCluster)
        {
            if (label.IsChrome != button.IsChrome)
            {
                return false;
            }
            if (RowHostsButton(label.Box, button.Box))
            {
                return true;
            }
            return labelDrawnJustBefore && !buttonLeadsCluster && label.ClipId == button.ClipId
                && ValueLabeledSlider.CaptionsOnRow(label.Box, button.Box)
                && button.Box.X - label.Box.XMax <= MaxBesideGap;
        }

        /// <summary>
        /// The label is the row the button lives in: wider than the button and starting left of it,
        /// horizontal overlap (the button may hang past the label's right edge, since rows that trim
        /// the label for a scrollbar still anchor the button to the untrimmed edge), and the
        /// button's vertical center inside the label's line.
        /// </summary>
        public static bool RowHostsButton(LineBox label, LineBox button)
        {
            if (label.Width <= button.Width || button.X <= label.X)
            {
                return false;
            }
            if (button.X >= label.XMax || label.X >= button.XMax)
            {
                return false;
            }
            return button.CenterY >= label.Y && button.CenterY <= label.YMax;
        }
    }
}
