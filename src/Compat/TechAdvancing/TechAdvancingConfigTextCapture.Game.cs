using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Feeds the generic reader the config window's prose: every description, preview and legend
    /// line goes through the window's private DrawText, a raw GUI.Label no widget tap sees.
    /// </summary>
    internal static class TechAdvancingConfigTextCapture
    {
        // Runs before the body advances drawpos, so the rect is the one the label lands in.
        public static void Prefix(Rect canvas, string text, ref float drawpos)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            Rect rect = new Rect(canvas.x, canvas.y + drawpos, canvas.width, Text.CalcHeight(text, canvas.width));
            WidgetCapture.RecordLabel(rect, text);
        }
    }
}
