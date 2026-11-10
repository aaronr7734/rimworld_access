using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Compat for Hospitality, the visiting-guest hotel mod. Three independent gaps,
    /// each registered and degraded on its own:
    ///
    /// 1. The area-selector strips (AreaGUI.DoAreaSelector, GuestUtility.DoAreaSelector)
    /// are hand-copies of vanilla RimWorld.AreaAllowedGUI.DoAreaSelector that kept its
    /// drawing but not a control primitive -- selection happens only through
    /// Mouse.IsOver + a mouse-button drag, so our capture records bare Labels and no
    /// control at all. Both are bracketed here as WidgetCapture.RadioButton rows
    /// (RecordRadio + MaybeForceActivate), mirroring the vanilla
    /// WidgetCaptureRadioButtonPatch bracket exactly, and write back through the call's
    /// own delegate/ref parameter (Category A).
    ///
    /// 2. The mod settings panel's four hand-rolled sliders (CustomWidget.
    /// HorizontalSlider / HorizontalRangeSlider) call no Widgets primitive at all --
    /// the rail is Widgets.DrawAtlas, the handle is GUI.DrawTexture, every label is a
    /// bare Widgets.Label. Bracketed here as WidgetCapture Slider/Range rows, mirroring
    /// the vanilla WidgetCaptureSliderPatch/WidgetCaptureFloatRangePatch brackets, and
    /// the keyboard-adjusted value overwrites the method's own return value, which the
    /// mod's own Listing_Custom wrappers then assign to the setting (Category A).
    ///
    /// 3. The three Gizmo_ModifyNumber&lt;T&gt; subclasses (Gizmo_GuestBed,
    /// Gizmo_VendingMachine, Gizmo_VendingMachineContent) are bare Gizmos that draw
    /// their own three ButtonImages and never return GizmoState.Clicked, so nothing in
    /// this codebase's gizmo pipeline can read or activate them -- the rental fee and
    /// both vending prices are otherwise unreachable. Handled via
    /// <see cref="ModifyNumberGizmoHandler"/>, registered per concrete type.
    /// </summary>
    internal static class HospitalityCompat
    {
        public static void Register(Harmony harmony)
        {
            try
            {
                RegisterAreaGuiTap(harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Hospitality compat (AreaGUI) registration failed: {ex.Message}");
            }

            try
            {
                RegisterGuestUtilityTap(harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Hospitality compat (GuestUtility) registration failed: {ex.Message}");
            }

            try
            {
                RegisterSliderTaps(harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Hospitality compat (sliders) registration failed: {ex.Message}");
            }
        }

        public static bool HasRoyalTitle(Pawn pawn)
        {
            return pawn.royalty?.MostSeniorTitle != null;
        }

        public static void RegisterGizmoHandlers()
        {
            CompatRegistration.GizmoHandler("Hospitality.Gizmo_GuestBed",
                t => new GuestBedGizmoHandler(t));
            CompatRegistration.GizmoHandler("Hospitality.Gizmo_VendingMachine",
                t => new VendingPriceGizmoHandler(t));
            CompatRegistration.GizmoHandler("Hospitality.Gizmo_VendingMachineContent",
                t => new VendingThresholdGizmoHandler(t));
        }

        // -----------------------------------------------------------------
        // Area-selector radio taps (Category A: the postfix writes back
        // through the call's own delegate/ref parameter, never a field).
        // -----------------------------------------------------------------

        private static void RegisterAreaGuiTap(Harmony harmony)
        {
            Type areaGuiType = AccessTools.TypeByName("Hospitality.MainTab.AreaGUI");
            if (areaGuiType == null)
            {
                return;
            }

            MethodInfo doAreaSelector = AccessTools.Method(areaGuiType, "DoAreaSelector",
                new[] { typeof(Rect), typeof(Pawn), typeof(Area), typeof(Func<Pawn, Area>), typeof(Action<Pawn, Area>) });
            if (doAreaSelector == null)
            {
                ModLogger.Error("Hospitality compat: AreaGUI.DoAreaSelector not found, area strip tap skipped.");
                return;
            }

            harmony.Patch(doAreaSelector,
                prefix: new HarmonyMethod(typeof(HospitalityCompat), nameof(AreaGuiPrefix)),
                postfix: new HarmonyMethod(typeof(HospitalityCompat), nameof(AreaGuiPostfix)));
        }

        private static void AreaGuiPrefix(Rect rect, Pawn p, Area area, Func<Pawn, Area> getArea, out int __state)
        {
            // Label mirrors the method's own draw exactly (AreaGUI.cs:53).
            string label = AreaUtility.AreaAllowedLabel_Area(area);
            bool chosen = getArea(p) == area;
            __state = WidgetCapture.RecordRadio(rect, label, chosen, false);
            WidgetCapture.EnterSelfCaptioned(label);
        }

        private static void AreaGuiPostfix(int __state, Pawn p, Area area, Action<Pawn, Area> setArea)
        {
            WidgetCapture.ExitSelfCaptioned();
            bool activated = false;
            WidgetCapture.MaybeForceActivate(__state, ref activated, false);
            if (activated)
            {
                // The call's own setter delegate -- Category A.
                setArea(p, area);
            }
        }

        private static void RegisterGuestUtilityTap(Harmony harmony)
        {
            Type guestUtilityType = AccessTools.TypeByName("Hospitality.Utilities.GuestUtility");
            if (guestUtilityType == null)
            {
                return;
            }

            MethodInfo doAreaSelector = AccessTools.Method(guestUtilityType, "DoAreaSelector",
                new[] { typeof(Rect), typeof(Area), typeof(Func<Area, string>), typeof(Area).MakeByRefType() });
            if (doAreaSelector == null)
            {
                ModLogger.Error("Hospitality compat: GuestUtility.DoAreaSelector not found, area strip tap skipped.");
                return;
            }

            harmony.Patch(doAreaSelector,
                prefix: new HarmonyMethod(typeof(HospitalityCompat), nameof(GuestAreaPrefix)),
                postfix: new HarmonyMethod(typeof(HospitalityCompat), nameof(GuestAreaPostfix)));
        }

        private static void GuestAreaPrefix(Rect rect, Area area, Func<Area, string> getLabel, ref Area currentArea, out int __state)
        {
            // Label comes from the call's OWN getLabel delegate, never re-derived --
            // AccommodationArea and ShoppingArea pass different label logic through it.
            string label = getLabel(area);
            bool chosen = currentArea == area;
            __state = WidgetCapture.RecordRadio(rect, label, chosen, false);
            WidgetCapture.EnterSelfCaptioned(label);
        }

        private static void GuestAreaPostfix(int __state, Area area, ref Area currentArea)
        {
            WidgetCapture.ExitSelfCaptioned();
            bool activated = false;
            WidgetCapture.MaybeForceActivate(__state, ref activated, false);
            if (activated)
            {
                // Writes the method's own ref parameter -- the same store its mouse-drag
                // branch writes (GuestUtility.cs). The caller (GenericUtility.
                // DoAreaRestriction) compares this against the original area and invokes
                // its own setArea delegate, so the mod's real mutation path still runs --
                // Category A.
                currentArea = area;
            }
        }

        // -----------------------------------------------------------------
        // Settings-panel slider/range brackets (Category A: the postfix
        // overwrites the method's own return value; the mod's Listing_Custom
        // wrappers assign it to the setting themselves).
        // -----------------------------------------------------------------

        // Mirrors CustomWidget.HorizontalRangeSlider's own drag clamp
        // (Mathf.Clamp(mouseValue, minLimit, updatedMaxValue - 0.01f) and its mirror for
        // the max handle) -- the minimum separation vanilla's own FloatRange enforces
        // via its "gap" parameter.
        private const float RangeSliderGap = 0.01f;

        private static void RegisterSliderTaps(Harmony harmony)
        {
            // The shipped 1.6 assembly keeps this type at the assembly root
            // (Hospitality.CustomWidget) while the source tree nests it under
            // Utilities. Try both; only conclude "mod absent" if a type the two
            // taps above already resolved is missing too.
            Type customWidgetType = AccessTools.TypeByName("Hospitality.CustomWidget")
                ?? AccessTools.TypeByName("Hospitality.Utilities.CustomWidget");
            if (customWidgetType == null)
            {
                if (AccessTools.TypeByName("Hospitality.Utilities.GuestUtility") != null)
                {
                    ModLogger.Error("Hospitality compat: CustomWidget not found under either known name; settings slider taps skipped.");
                }
                return;
            }

            MethodInfo horizontalSlider = AccessTools.Method(customWidgetType, "HorizontalSlider",
                new[] { typeof(Rect), typeof(float), typeof(float), typeof(float), typeof(bool), typeof(string), typeof(string), typeof(string), typeof(float) });
            if (horizontalSlider != null)
            {
                harmony.Patch(horizontalSlider,
                    prefix: new HarmonyMethod(typeof(HospitalityCompat), nameof(SliderPrefix)),
                    postfix: new HarmonyMethod(typeof(HospitalityCompat), nameof(SliderPostfix)));
            }
            else
            {
                ModLogger.Error("Hospitality compat: CustomWidget.HorizontalSlider not found, settings slider tap skipped.");
            }

            MethodInfo horizontalRangeSlider = AccessTools.Method(customWidgetType, "HorizontalRangeSlider",
                new[] { typeof(Rect), typeof(float), typeof(float), typeof(float), typeof(float), typeof(bool), typeof(string), typeof(string), typeof(string), typeof(float) });
            if (horizontalRangeSlider != null)
            {
                harmony.Patch(horizontalRangeSlider,
                    prefix: new HarmonyMethod(typeof(HospitalityCompat), nameof(RangePrefix)),
                    postfix: new HarmonyMethod(typeof(HospitalityCompat), nameof(RangePostfix)));
            }
            else
            {
                ModLogger.Error("Hospitality compat: CustomWidget.HorizontalRangeSlider not found, settings range slider tap skipped.");
            }
        }

        private static void SliderPrefix(Rect rect, float value, float min, float max, string label, string leftAlignedLabel, string rightAlignedLabel, float roundTo, out int __state)
        {
            // The mod's label/left/right strings are its VALUE and bound
            // captions ("7", "1", "20"), not the setting's name: passing them
            // through names the slider after its own value and speaks the max
            // as the value. The name is a separate
            // Listing_Custom.CustomSliderLabel Label beside the rail, so record
            // BLANK and let the blank-control caption fusion adopt it; the
            // float value/min/max carry the numbers.
            __state = WidgetCapture.RecordSlider(rect, null, null, null, value, min, max, roundTo);
            WidgetCapture.EnterSelfCaptioned(label, leftAlignedLabel, rightAlignedLabel);
        }

        private static void SliderPostfix(int __state, float min, float max, float roundTo, ref float __result)
        {
            WidgetCapture.ExitSelfCaptioned();
            WidgetCapture.MaybeStepSlider(__state, min, max, roundTo, ref __result);
        }

        private static void RangePrefix(Rect rect, float minValue, float maxValue, float minLimit, float maxLimit, float roundTo, out int __state)
        {
            __state = WidgetCapture.RecordRange(rect, RangeFamily.Float, minValue, maxValue, minLimit, maxLimit, RangeSliderGap, roundTo, ToStringStyle.Integer);
            WidgetCapture.EnterRange();
        }

        private static void RangePostfix(int __state, float minLimit, float maxLimit, float roundTo, ref (float minValue, float maxValue) __result)
        {
            WidgetCapture.ExitRange();
            FloatRange scratch = new FloatRange(__result.minValue, __result.maxValue);
            WidgetCapture.MaybeAdjustFloatRange(__state, ref scratch, minLimit, maxLimit, RangeSliderGap, roundTo);
            __result = (scratch.min, scratch.max);
        }
    }
}
