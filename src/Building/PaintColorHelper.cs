using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Accessible color selection for the paint designators (Designator_PaintFloor and
    /// Designator_PaintBuilding, plus any modded Designator_Paint subclass).
    ///
    /// Vanilla pops a visual <c>FloatMenuGrid</c> of color swatches when the paint tool is
    /// clicked with the mouse. The keyboard architect flow selects a designator via
    /// <c>DesignatorManager.Select</c> and never triggers that grid, so without this helper a
    /// paint tool would be stuck on its default color. This opens an equivalent
    /// <see cref="WindowlessFloatMenuState"/> listing every <see cref="ColorDef"/> the designator
    /// offers, labelled by its translated <c>LabelCap</c>.
    /// </summary>
    public static class PaintColorHelper
    {
        private static readonly FieldInfo ColorDefField =
            AccessTools.Field(typeof(Designator_Paint), "colorDef");

        private static readonly PropertyInfo ColorsProperty =
            AccessTools.Property(typeof(Designator_Paint), "Colors");

        private static readonly FieldInfo CachedAttachmentStringField =
            AccessTools.Field(typeof(Designator_Paint), "cachedAttachmentString");

        /// <summary>True if the designator is a paint tool whose color we can drive.</summary>
        public static bool IsPaintDesignator(Designator designator) => designator is Designator_Paint;

        /// <summary>The designator's currently selected paint color, or null.</summary>
        public static ColorDef GetCurrentColor(Designator_Paint designator) =>
            ColorDefField?.GetValue(designator) as ColorDef;

        private static List<ColorDef> GetColors(Designator_Paint designator)
        {
            var colors = ColorsProperty?.GetValue(designator) as IEnumerable<ColorDef>;
            return colors?.ToList() ?? new List<ColorDef>();
        }

        private static string ColorLabel(ColorDef colorDef) =>
            colorDef.label.NullOrEmpty() ? colorDef.defName : colorDef.LabelCap.ToString();

        /// <summary>
        /// Opens the accessible color picker for the given paint designator. Selecting an option
        /// sets the designator's color in place (no re-selection needed) and announces it.
        /// </summary>
        public static void OpenColorPicker(Designator_Paint designator)
        {
            if (designator == null || ColorDefField == null)
                return;

            List<ColorDef> colors = GetColors(designator);
            ColorDef current = GetCurrentColor(designator);

            var options = new List<FloatMenuOption>();
            foreach (ColorDef colorDef in colors)
            {
                ColorDef captured = colorDef;
                string label = ColorLabel(colorDef);
                if (colorDef == current)
                    label += ", " + "RimWorldAccess.Building.PaintColor.Current".Translate();

                options.Add(new FloatMenuOption(label, () =>
                {
                    ColorDefField.SetValue(designator, captured);
                    // Drop the cached mouse-attachment text so the on-screen tooltip reflects
                    // the new color for sighted players watching alongside.
                    CachedAttachmentStringField?.SetValue(designator, null);
                    TolkHelper.SpeakData(
                        "RimWorldAccess.Building.PaintColor.Set".Translate(ColorLabel(captured)));
                }));
            }

            if (options.Count == 0)
            {
                TolkHelper.Speak("NoneLower".Loc());
                return;
            }

            int startIndex = current != null ? colors.IndexOf(current) : 0;
            if (startIndex < 0) startIndex = 0;

            TolkHelper.Speak("RimWorldAccess.Building.PaintColor.MenuTitle".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false, startIndex: startIndex);
        }
    }
}
