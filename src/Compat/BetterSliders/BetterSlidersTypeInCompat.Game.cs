using System;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Two hooks on BetterSliders' per-slider <c>NumberEntryController</c>, which its own
    /// HorizontalSlider / FloatRange / IntRange postfixes drive right after the control draws.
    /// <list type="bullet">
    /// <item><c>BeginHysteresis(Rect)</c> is the mod's whole hover decision: it measures the
    /// pointer's distance from the slider and hides the box past the end threshold. While the
    /// keyboard cursor sits on that slider the measurement is fed a point on the slider's edge, so
    /// the "Hover" style shows the box exactly as it would under the mouse. Nothing else sees the
    /// fed point: the box's own focus grab and click tests run later against the real pointer, so
    /// IMGUI never hands the box keyboard focus.</item>
    /// <item><c>DrawMinimumEntry</c> / <c>DrawMaximumEntry</c> bracket the box's TextField so the
    /// generic reader folds it into its slider's row (WidgetCapture.EnterSliderTypeIn). Typing on
    /// that row writes through the box, so the mod's own parse, clamp and write-back apply.</item>
    /// </list>
    /// </summary>
    internal static class BetterSlidersTypeInCompat
    {
        private static bool pointerFed;
        private static Vector2 realPointer;

        public static void Register(Harmony harmony)
        {
            var surface = new ReflectionSurface("BetterSliders type-in compat");
            Type controller = surface.Type("SirRandoo.BetterSliders.Entities.NumberEntryController");
            Type[] entryParameters = { typeof(float).MakeByRefType(), typeof(bool) };
            MethodInfo beginHysteresis = surface.Method(controller, "BeginHysteresis", new[] { typeof(Rect) });
            MethodInfo drawMinimum = surface.Method(controller, "DrawMinimumEntry", entryParameters);
            MethodInfo drawMaximum = surface.Method(controller, "DrawMaximumEntry", entryParameters);
            if (!surface.Ready)
            {
                return;
            }
            try
            {
                harmony.Patch(beginHysteresis,
                    prefix: new HarmonyMethod(typeof(BetterSlidersTypeInCompat), nameof(HysteresisPrefix)),
                    finalizer: new HarmonyMethod(typeof(BetterSlidersTypeInCompat), nameof(HysteresisFinalizer)));
                harmony.Patch(drawMinimum,
                    prefix: new HarmonyMethod(typeof(BetterSlidersTypeInCompat), nameof(MinimumEntryPrefix)),
                    finalizer: new HarmonyMethod(typeof(BetterSlidersTypeInCompat), nameof(EntryFinalizer)));
                harmony.Patch(drawMaximum,
                    prefix: new HarmonyMethod(typeof(BetterSlidersTypeInCompat), nameof(MaximumEntryPrefix)),
                    finalizer: new HarmonyMethod(typeof(BetterSlidersTypeInCompat), nameof(EntryFinalizer)));
            }
            catch (Exception ex)
            {
                ModLogger.Error("BetterSliders type-in compat: Register failed: " + ex.Message);
            }
        }

        public static void HysteresisPrefix(Rect region)
        {
            Event current = Event.current;
            if (current == null || !WidgetCapture.LastValueControlFocused)
            {
                return;
            }
            realPointer = current.mousePosition;
            pointerFed = true;
            // Just past the right edge, not inside: a pointer inside the rect takes the mod's
            // early return, which keeps whatever hidden state the last real-pointer pass left.
            current.mousePosition = new Vector2(region.xMax + 1f, region.center.y);
        }

        /// <summary>Finalizer, not postfix: the fed point must never outlive the measurement, even if it throws.</summary>
        public static void HysteresisFinalizer()
        {
            if (!pointerFed)
            {
                return;
            }
            pointerFed = false;
            if (Event.current != null)
            {
                Event.current.mousePosition = realPointer;
            }
        }

        public static void MinimumEntryPrefix()
        {
            WidgetCapture.EnterSliderTypeIn(high: false);
        }

        public static void MaximumEntryPrefix()
        {
            WidgetCapture.EnterSliderTypeIn(high: true);
        }

        /// <summary>Finalizer, not postfix: the bracket must unwind even when the drawer throws, or a later TextField this pass would be claimed as a type-in box.</summary>
        public static void EntryFinalizer()
        {
            WidgetCapture.ExitSliderTypeIn();
        }
    }
}
