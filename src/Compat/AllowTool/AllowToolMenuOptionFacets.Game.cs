using System;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Allow Tool's ATFloatMenuOption keeps two things the windowless menu cannot read. Its
    /// check-mark rows (Party Hunt's right-click settings) paint their state in extraPartOnGUI,
    /// and every context-menu entry holds its description in a private field that its own DoGUI
    /// shows as a tooltip only while the menu watermark setting is on. Both are attached as
    /// <see cref="FloatMenuOptionFacets"/> where the mod builds the option.
    /// </summary>
    internal static class AllowToolMenuOptionFacets
    {
        private static FieldInfo showWatermarkField;

        public static void Register(Harmony harmony)
        {
            try
            {
                var surface = new ReflectionSurface("AllowTool menu options");
                Type optionType = surface.Type("AllowTool.ATFloatMenuOption");
                ConstructorInfo ctor = surface.Constructor(optionType, new[]
                {
                    typeof(string), typeof(Action), typeof(MenuOptionPriority), typeof(Action<Rect>), typeof(Thing),
                    typeof(float), typeof(Func<Rect, bool>), typeof(WorldObject), typeof(string)
                });
                FieldInfo showWatermark = surface.Field(optionType, "showWatermark");
                MethodInfo makeCheckmark = surface.Method(surface.Type("AllowTool.AllowToolUtility"), "MakeCheckmarkOption",
                    new[] { typeof(string), typeof(string), typeof(Func<bool>), typeof(Action<bool>) });
                if (!surface.Ready)
                {
                    return;
                }
                showWatermarkField = showWatermark;
                harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(AllowToolMenuOptionFacets), nameof(OptionCtorPostfix)));
                harmony.Patch(makeCheckmark, postfix: new HarmonyMethod(typeof(AllowToolMenuOptionFacets), nameof(MakeCheckmarkPostfix)));
            }
            catch (Exception ex)
            {
                ModLogger.Error("Allow Tool compat (menu options) registration failed: " + ex.Message);
            }
        }

        private static void OptionCtorPostfix(object __instance, string tooltipText)
        {
            if ((bool)showWatermarkField.GetValue(__instance))
            {
                FloatMenuOptionFacets.SetTooltip(__instance as FloatMenuOption, tooltipText);
            }
        }

        private static void MakeCheckmarkPostfix(object __result, Func<bool> getter)
        {
            FloatMenuOptionFacets.SetCheckState(__result as FloatMenuOption, getter);
        }
    }
}
