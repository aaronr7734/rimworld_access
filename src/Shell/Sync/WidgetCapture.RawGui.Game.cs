using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    public static partial class WidgetCapture
    {
        // Open while a vanilla wrapper that already recorded its own row is inside the raw GUI
        // call, so the raw taps stay silent there.
        private static int wrappedTextAreaDepth;
        private static int wrappedLabelDepth;

        internal static bool InWrappedTextArea
        {
            get { return wrappedTextAreaDepth > 0; }
        }

        internal static void EnterWrappedTextArea()
        {
            wrappedTextAreaDepth++;
        }

        internal static void ExitWrappedTextArea()
        {
            if (wrappedTextAreaDepth > 0)
            {
                wrappedTextAreaDepth--;
            }
        }

        internal static void EnterWrappedLabel()
        {
            wrappedLabelDepth++;
        }

        internal static void ExitWrappedLabel()
        {
            if (wrappedLabelDepth > 0)
            {
                wrappedLabelDepth--;
            }
        }

        /// <summary>A raw GUI.Label worth its own row: real text, outside every wrapper, and not the window title Window.InnerWindowOnGUI draws itself.</summary>
        internal static bool RawLabelRecordable(string text)
        {
            if (!passOpen || wrappedLabelDepth > 0 || string.IsNullOrEmpty(text))
            {
                return false;
            }
            Window drawing = Find.WindowStack?.currentlyDrawnWindow;
            return drawing == null || drawing.optionalTitle != text;
        }

        private static bool selectedHighlightValid;
        private static Rect selectedHighlightScreenRect;
        private static GuiSpace.ClipKey selectedHighlightClip;
        private static int selectedHighlightSinkIndex;

        /// <summary>
        /// Widgets.DrawHighlightSelected, remembered only until the next row records. A button
        /// recorded straight after it, inside its rect, gets <see cref="CapturedWidget.HighlightSelected"/>:
        /// the on state an IMGUI toggle drawn as a plain button shows. The generic reader presents
        /// such a button as a checkbox, and keeps it one (unchecked) on passes without the highlight.
        /// </summary>
        internal static void RecordSelectedHighlight(Rect rect)
        {
            if (!passOpen)
            {
                return;
            }
            selectedHighlightValid = true;
            selectedHighlightScreenRect = GuiSpace.ToScreen(rect);
            selectedHighlightClip = GuiSpace.CurrentClip();
            selectedHighlightSinkIndex = CurrentSink.Count;
        }

        private static bool TakeSelectedHighlight(CapturedWidget row, int index)
        {
            if (!selectedHighlightValid || index != selectedHighlightSinkIndex)
            {
                return false;
            }
            selectedHighlightValid = false;
            return row.Clip.Equals(selectedHighlightClip)
                && selectedHighlightScreenRect.Contains(row.ScreenRect.center)
                && row.ScreenRect.width <= selectedHighlightScreenRect.width + 1f
                && row.ScreenRect.height <= selectedHighlightScreenRect.height + 1f;
        }
    }
}
