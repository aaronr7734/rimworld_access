using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Accessible color selection and color naming for RimWorld "Plan" markers
    /// (<see cref="Designator_Plan_Add"/> and the plan management actions).
    ///
    /// Plans use <see cref="ColorDef"/>s with <c>colorType == ColorType.Planning</c>. Unlike the
    /// structure/paint colors, these nine planning ColorDefs ship with EMPTY labels, so the game
    /// exposes no name to announce. This helper supplies a localized name per color (keyed off the
    /// def's canonical defName suffix, e.g. <c>PlanRed</c> → "Red") and drives the keyboard color
    /// picker.
    ///
    /// As with the paint tools, the keyboard architect flow selects a designator via
    /// <c>DesignatorManager.Select</c> and never triggers vanilla's visual <c>FloatMenuGrid</c>
    /// swatch picker (which is also unusable for screen readers — its options carry no text label).
    /// This opens an equivalent <see cref="WindowlessFloatMenuState"/> listing every planning color
    /// by name.
    /// </summary>
    public static class PlanColorHelper
    {
        private static readonly FieldInfo ColorDefField =
            AccessTools.Field(typeof(Designator_Plan_Add), "colorDef");

        private static readonly PropertyInfo CanSelectColorProperty =
            AccessTools.Property(typeof(Designator_Plan_Add), "CanSelectColor");

        /// <summary>
        /// The localized name for a planning color. The planning ColorDefs have empty labels, so we
        /// key off the def's canonical name suffix (<c>PlanRed</c> → "Red") which is translated via
        /// <c>RimWorldAccess.Building.PlanColor.&lt;suffix&gt;</c>. Falls back to the suffix itself
        /// (or, for a non-plan color that happens to be named, its own label) so modded planning
        /// colors still announce something sensible.
        /// </summary>
        /// <summary>
        /// The language-independent suffix identifying a planning color, used as a stable key /
        /// scanner-subcategory id (e.g. <c>PlanRed</c> → "Red"). Falls back to the raw defName.
        /// </summary>
        public static string ColorSuffix(ColorDef colorDef)
        {
            if (colorDef == null) return "Other";
            return colorDef.defName.StartsWith("Plan")
                ? colorDef.defName.Substring("Plan".Length)
                : colorDef.defName;
        }

        public static string ColorName(ColorDef colorDef)
        {
            if (colorDef == null) return string.Empty;

            string suffix = ColorSuffix(colorDef);

            string key = "RimWorldAccess.Building.PlanColor." + suffix;
            if (key.CanTranslate())
                return key.Translate().ToString();

            if (!colorDef.label.NullOrEmpty())
                return colorDef.LabelCap.ToString();

            return suffix;
        }

        /// <summary>
        /// True when the designator is a plan tool whose color the player is allowed to choose. We
        /// gate on the game's own <c>CanSelectColor</c> signal rather than the concrete type, so the
        /// base plan tool (and any modded color-selectable plan tool) qualifies while
        /// <see cref="Designator_Plan_Expand"/> — which inherits from <see cref="Designator_Plan_Add"/>
        /// but reports <c>CanSelectColor == false</c> because it adopts the target plan's color — does not.
        /// </summary>
        public static bool IsPlanColorDesignator(Designator designator)
        {
            if (!(designator is Designator_Plan_Add planAdd))
                return false;
            return CanSelectColorProperty == null
                || (bool)CanSelectColorProperty.GetValue(planAdd);
        }

        /// <summary>The designator's currently selected planning color, or null.</summary>
        public static ColorDef GetCurrentColor(Designator_Plan_Add designator) =>
            ColorDefField?.GetValue(designator) as ColorDef;

        /// <summary>
        /// Opens the accessible color picker for the given plan designator. Selecting an option sets
        /// the designator's color in place (no re-selection needed) and announces it.
        /// </summary>
        public static void OpenColorPicker(Designator_Plan_Add designator)
        {
            if (designator == null || ColorDefField == null)
                return;

            List<ColorDef> colors = Designator_Plan_Add.Colors.ToList();
            ColorDef current = GetCurrentColor(designator);

            var options = new List<FloatMenuOption>();
            foreach (ColorDef colorDef in colors)
            {
                ColorDef captured = colorDef;
                string label = ColorName(colorDef);
                if (colorDef == current)
                    label += ", " + "RimWorldAccess.Building.PlanColor.Current".Translate();

                options.Add(new FloatMenuOption(label, () =>
                {
                    ColorDefField.SetValue(designator, captured);
                    TolkHelper.SpeakData(
                        "RimWorldAccess.Building.PlanColor.Set".Translate(ColorName(captured)));
                }));
            }

            if (options.Count == 0)
            {
                TolkHelper.Speak("NoneLower".Loc());
                return;
            }

            int startIndex = current != null ? colors.IndexOf(current) : 0;
            if (startIndex < 0) startIndex = 0;

            TolkHelper.Speak("RimWorldAccess.Building.PlanColor.MenuTitle".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false, startIndex: startIndex,
                onClose: ShapePlacementState.OnColorPickerClosed);
        }

        // Reflection on the private Plan.Color setter so changing an existing plan's color also
        // recreates its overlay material (the public setter does this; a direct field write wouldn't).
        private static readonly MethodInfo PlanColorSetter =
            AccessTools.PropertySetter(typeof(Plan), "Color");

        /// <summary>
        /// Opens the accessible color picker for an already-placed plan. Choosing a color recolors
        /// the plan and runs its contiguity check (matching vanilla's "Change color" gizmo, which
        /// can split a plan if the recolor makes it touch a same-colored neighbor differently).
        /// </summary>
        public static void OpenColorPickerForPlan(Plan plan)
        {
            if (plan == null || PlanColorSetter == null)
                return;

            List<ColorDef> colors = Designator_Plan_Add.Colors.ToList();
            ColorDef current = plan.Color;

            var options = new List<FloatMenuOption>();
            foreach (ColorDef colorDef in colors)
            {
                ColorDef captured = colorDef;
                string label = ColorName(colorDef);
                if (colorDef == current)
                    label += ", " + "RimWorldAccess.Building.PlanColor.Current".Translate();

                options.Add(new FloatMenuOption(label, () =>
                {
                    PlanColorSetter.Invoke(plan, new object[] { captured });
                    plan.CheckContiguous();
                    TolkHelper.SpeakData(
                        "RimWorldAccess.Building.PlanColor.Set".Translate(ColorName(captured)));
                }));
            }

            if (options.Count == 0)
            {
                TolkHelper.Speak("NoneLower".Loc());
                return;
            }

            int startIndex = current != null ? colors.IndexOf(current) : 0;
            if (startIndex < 0) startIndex = 0;

            TolkHelper.Speak("RimWorldAccess.Building.PlanColor.MenuTitle".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false, startIndex: startIndex);
        }
    }
}
