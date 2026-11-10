using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>The one Rect to <see cref="LineBox"/> conversion the caption rules run on.</summary>
    internal static class LineBoxRect
    {
        internal static LineBox ToLineBox(this Rect r)
        {
            return new LineBox { X = r.x, Y = r.y, Width = r.width, Height = r.height };
        }

        /// <summary>The row's screen rect as a <see cref="LineBox"/>.</summary>
        internal static LineBox Box(this CapturedWidget row)
        {
            return row.ScreenRect.ToLineBox();
        }
    }
}
