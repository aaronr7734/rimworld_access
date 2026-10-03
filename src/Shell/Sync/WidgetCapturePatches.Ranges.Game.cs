using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;


namespace RimWorldAccess.Shell
{
    // Range brackets, the one place here where a bracket also RECORDS: a range's
    // two thumbs are raw GUI.DrawTexture draws moved by raw mouse-drag code, so no
    // captured primitive carries the control and nothing exists for the activation
    // channel to force. The prefix records the control with the call site's own
    // bounds, gap and rounding and opens the bracket that stamps the "min - max"
    // Label; the postfix writes the pending keyboard adjust into the method's own
    // `ref` range. All three cores need TargetMethod: a by-ref parameter type is
    // not a compile-time constant (CS0182).

    /// <summary>
    /// Widgets.IntRange (decompiled Verse/Widgets.cs:2318-2420).
    /// Listing_Standard.IntRange is a GetRect+delegate wrapper over it
    /// (Verse/Listing_Standard.cs:361-370) and needs nothing of its own.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureIntRangePatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "IntRange",
                new Type[]
                {
                    typeof(Rect), typeof(int), typeof(IntRange).MakeByRefType(), typeof(int),
                    typeof(int), typeof(string), typeof(int),
                });
        }

        [HarmonyPrefix]
        public static void Prefix(Rect rect, ref IntRange range, int min, int max, int minWidth, out int __state)
        {
            __state = WidgetCapture.RecordRange(rect, RangeFamily.Int, range.min, range.max,
                min, max, minWidth, 1f, ToStringStyle.Integer);
            WidgetCapture.EnterRange();
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref IntRange range, int min, int max, int minWidth)
        {
            WidgetCapture.ExitRange();
            WidgetCapture.MaybeAdjustIntRange(__state, ref range, min, max, minWidth);
        }
    }

    /// <summary>
    /// Widgets.FloatRange (decompiled Verse/Widgets.cs:2216-2316). `valueStyle`
    /// rides the row so each thumb announces the value in the caller's own
    /// rendering; `gap`/`roundTo` are what the keyboard adjust clamps against.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureFloatRangePatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "FloatRange",
                new Type[]
                {
                    typeof(Rect), typeof(int), typeof(FloatRange).MakeByRefType(), typeof(float),
                    typeof(float), typeof(string), typeof(ToStringStyle), typeof(float),
                    typeof(GameFont), typeof(Color?), typeof(float),
                });
        }

        [HarmonyPrefix]
        public static void Prefix(Rect rect, ref FloatRange range, float min, float max,
            ToStringStyle valueStyle, float gap, float roundTo, out int __state)
        {
            __state = WidgetCapture.RecordRange(rect, RangeFamily.Float, range.min, range.max,
                min, max, gap, roundTo, valueStyle);
            WidgetCapture.EnterRange();
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref FloatRange range, float min, float max, float gap, float roundTo)
        {
            WidgetCapture.ExitRange();
            WidgetCapture.MaybeAdjustFloatRange(__state, ref range, min, max, gap, roundTo);
        }
    }

    /// <summary>
    /// Widgets.QualityRange (decompiled Verse/Widgets.cs:2431-2521). Its
    /// bounds are the quality enum's own extent and its thumbs may meet, so
    /// the row records limits 0..QualityCount-1 with no gap.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureQualityRangePatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "QualityRange",
                new Type[] { typeof(Rect), typeof(int), typeof(RimWorld.QualityRange).MakeByRefType() });
        }

        [HarmonyPrefix]
        public static void Prefix(Rect rect, ref RimWorld.QualityRange range, out int __state)
        {
            __state = WidgetCapture.RecordRange(rect, RangeFamily.Quality, (int)range.min, (int)range.max,
                0f, QualityUtility.QualityCount - 1, 0f, 1f, ToStringStyle.Integer);
            WidgetCapture.EnterRange();
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref RimWorld.QualityRange range)
        {
            WidgetCapture.ExitRange();
            WidgetCapture.MaybeAdjustQualityRange(__state, ref range);
        }
    }

    /// <summary>
    /// Widgets.FloatRangeWithTypeIn (decompiled Verse/Widgets.cs:2523-2548).
    /// Everything in its body is already captured through the FloatRange bracket
    /// above; the one gap this closes is that vanilla draws no caption for either
    /// text box, so both would read "text field" — this names them minimum and
    /// maximum in draw order. No numeric clamp is carried: vanilla parses them with
    /// a bare float.TryParse and applies no bounds of its own (:2546-2547).
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureFloatRangeWithTypeInBracketPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "FloatRangeWithTypeIn",
                new Type[]
                {
                    typeof(Rect), typeof(int), typeof(FloatRange).MakeByRefType(), typeof(float),
                    typeof(float), typeof(ToStringStyle), typeof(string),
                });
        }

        [HarmonyPrefix]
        public static void Prefix()
        {
            WidgetCapture.EnterRangeTypeIn();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitRangeTypeIn();
        }
    }

    // The rule for the widgets below: BRACKET where the widget already funnels
    // into a captured primitive and only the data is wrong or missing, since a
    // second recorder would double-capture an existing row; RECORDER only where
    // the widget draws nothing this engine taps, as with the icon families, whose
    // callers' TipRegions otherwise have no row to resolve against.

    /// <summary>
    /// Widgets.LabelEllipses — body is
    /// <c>label = Text.ClampTextWithEllipsis(rect, label); Label(rect, label)</c>
    /// (decompiled Verse/Widgets.cs:894-898). The row is therefore already
    /// captured through the Label tap; what it carries without this bracket is
    /// the CLAMPED string. Widgets.LabelFit's third branch (:1110) and
    /// Verse/WidgetRow.cs:290 both reach the caller's text through here, so
    /// one bracket restores it for all three entry points.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "LabelEllipses", new Type[] { typeof(Rect), typeof(string) })]
    internal static class WidgetCaptureLabelEllipsesBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string label)
        {
            WidgetCapture.EnterLabelEllipses(label);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitLabelEllipses();
        }
    }

    /// <summary>
    /// Widgets.DefIcon — a dispatch over the def's concrete type ending in
    /// DrawTextureFitted or ThingIcon (decompiled Verse/Widgets.cs:393-470),
    /// with no captured primitive anywhere in it. The def's own LabelCap is
    /// the name a sighted player has for the icon; the shared icon bracket
    /// keeps the ThingDef branch (:405) from recording a second row.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "DefIcon",
        new Type[]
        {
            typeof(Rect), typeof(Def), typeof(ThingDef), typeof(float), typeof(ThingStyleDef),
            typeof(bool), typeof(Color?), typeof(Material), typeof(int?), typeof(float),
        })]
    internal static class WidgetCaptureDefIconPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, Def def)
        {
            if (WidgetCapture.WantsIconRow && def != null)
            {
                // TaggedString's implicit string conversion, which strips the
                // grammar/color tags rather than resolving them into markup
                // (Verse/TaggedString.cs:120 -> ColoredText.StripTags) and is
                // null-safe for a def whose label is empty (Verse/Def.cs
                // returns a null TaggedString there).
                WidgetCapture.RecordIcon(rect, def.LabelCap, null);
            }
            WidgetCapture.EnterIcon();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitIcon();
        }
    }

    /// <summary>
    /// Widgets.ThingIcon(Rect, Thing, ...) — resolves the thing's icon and
    /// hands it to the private ThingIconWorker (decompiled
    /// Verse/Widgets.cs:472-522); a blueprint delegates to DefIcon (:477),
    /// which the shared icon bracket keeps from recording twice. The label is
    /// the thing's own LabelCap, the same string vanilla writes beside it
    /// wherever it draws one.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "ThingIcon",
        new Type[] { typeof(Rect), typeof(Thing), typeof(float), typeof(Rot4?), typeof(bool), typeof(float), typeof(bool) })]
    internal static class WidgetCaptureThingIconThingPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, Thing thing)
        {
            if (WidgetCapture.WantsIconRow && thing != null)
            {
                WidgetCapture.RecordIcon(rect, thing.LabelCap, null);
            }
            WidgetCapture.EnterIcon();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitIcon();
        }
    }

    /// <summary>
    /// Widgets.ThingIcon(Rect, ThingDef, ...) — the def-level twin, reached
    /// both directly and from DefIcon's ThingDef branch. It is the one member
    /// of the family that states its own "draws nothing" condition in a single
    /// readable test (<c>thingDef.uiIcon == null || == BadTex</c>, decompiled
    /// Verse/Widgets.cs:526), so the tap mirrors that test rather than naming
    /// an icon that never rendered.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "ThingIcon",
        new Type[]
        {
            typeof(Rect), typeof(ThingDef), typeof(ThingDef), typeof(ThingStyleDef),
            typeof(float), typeof(Color?), typeof(int?), typeof(float),
        })]
    internal static class WidgetCaptureThingIconDefPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, ThingDef thingDef)
        {
            if (WidgetCapture.WantsIconRow && thingDef != null
                && thingDef.uiIcon != null && thingDef.uiIcon != BaseContent.BadTex)
            {
                WidgetCapture.RecordIcon(rect, thingDef.LabelCap, null);
            }
            WidgetCapture.EnterIcon();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitIcon();
        }
    }

    /// <summary>
    /// Verse.WidgetRow.Icon — a bare <c>GUI.DrawTexture</c> plus an optional
    /// TooltipHandler.TipRegion over the same rect (decompiled
    /// Verse/WidgetRow.cs:209-221), i.e. a control a sighted player can see and
    /// hover with no row of any kind behind it. Recorded from the POSTFIX
    /// because the rect is computed inside the body and returned; nothing else
    /// in the body records, so a postfix cannot double-capture. The tooltip is
    /// the caller's own pairing, so it rides the row as an exact tip; a
    /// tooltip-less icon falls back to the texture's asset name, the same
    /// honest-but-cryptic last resort ImageButtonLabel already uses.
    /// </summary>
    [HarmonyPatch(typeof(WidgetRow), "Icon", new Type[] { typeof(Texture), typeof(string), typeof(float) })]
    internal static class WidgetCaptureWidgetRowIconPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect __result, Texture tex, string tooltip)
        {
            if (!WidgetCapture.WantsIconRow)
            {
                return;
            }
            string label = !string.IsNullOrEmpty(tooltip) ? tooltip : (tex != null ? tex.name : null);
            WidgetCapture.RecordIcon(__result, label, tooltip);
        }
    }

    /// <summary>
    /// Verse.WidgetRow.DefIcon — Widgets.DefIcon plus an optional TipRegion
    /// over the same rect (decompiled Verse/WidgetRow.cs:223-234). The inner
    /// DefIcon tap already records the row, so this is a bracket, not a second
    /// recorder: it only hands down the tooltip vanilla's own caller paired
    /// with the icon, which is exact where the geometric channel would be a
    /// guess.
    /// </summary>
    [HarmonyPatch(typeof(WidgetRow), "DefIcon", new Type[] { typeof(ThingDef), typeof(string) })]
    internal static class WidgetCaptureWidgetRowDefIconPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string tooltip)
        {
            WidgetCapture.EnterWidgetRowDefIcon(tooltip);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitWidgetRowDefIcon();
        }
    }

    /// <summary>
    /// Verse.WidgetRow.ToggleableIcon — Widgets.ButtonImage, then a bare
    /// <c>GUI.DrawTexture</c> of CheckboxOnTex/CheckboxOffTex over its right
    /// half, then the caller's <c>ref bool</c> flip and tick sound on click
    /// (decompiled Verse/WidgetRow.cs:174-207). The ButtonImage already
    /// records a live Button row and its own click IS the toggle (vehicle A),
    /// so this is a bracket: it names the row from the tooltip vanilla
    /// registers (the ButtonImage is passed none) and stamps the drawn on/off
    /// state, which no existing mechanism could reach — the checkmark is a raw
    /// texture draw and marker promotion only ever runs against InvisibleButton
    /// rows. TargetMethod because the by-ref bool rules out the attribute's
    /// Type[] form (CS0182).
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureToggleableIconBracketPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(WidgetRow), "ToggleableIcon",
                new Type[]
                {
                    typeof(bool).MakeByRefType(), typeof(Texture2D), typeof(string),
                    typeof(SoundDef), typeof(string),
                });
        }

        [HarmonyPrefix]
        public static void Prefix(string tooltip, Texture2D tex)
        {
            WidgetCapture.EnterToggleableIcon(tooltip, tex);
        }

        [HarmonyPostfix]
        public static void Postfix(ref bool toggleable)
        {
            WidgetCapture.ExitToggleableIcon(toggleable);
        }
    }

    /// <summary>
    /// Widgets.ColorBox — one palette swatch: a selection outline when the
    /// current color is indistinguishable from this one, the filled square,
    /// then a ButtonInvisible whose click assigns the caller's
    /// <c>ref Color</c> and plays Tick_High (decompiled
    /// Verse/Widgets.cs:2925-2944). The hotspot already records AND is already
    /// operable through the live activation channel, so this is a bracket: it
    /// supplies the two things vanilla renders as pure pixels — the swatch's
    /// own value and whether it is the chosen one, read from vanilla's OWN
    /// IndistinguishableFrom test rather than a comparison of our own.
    /// Widgets.ColorSelector needs no bracket: its body is one ColorBox per
    /// swatch (:2962) plus a tinted preview of the same current color.
    /// TargetMethod because of the by-ref Color parameter (CS0182).
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureColorBoxBracketPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "ColorBox",
                new Type[]
                {
                    typeof(Rect), typeof(Color).MakeByRefType(), typeof(Color), typeof(int),
                    typeof(int), typeof(Action<Color, Rect>),
                });
        }

        [HarmonyPrefix]
        public static void Prefix(ref Color color, Color boxColor)
        {
            WidgetCapture.EnterColorBox(boxColor, color.IndistinguishableFrom(boxColor));
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitColorBox();
        }
    }

    /// <summary>
    /// MedicalCareUtility.MedicalCareSetter's icon hotspots already record and activate
    /// (RimWorld/MedicalCareUtility.cs:33-66); this bracket names them and marks the chosen one.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureMedicalCareSetterPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(MedicalCareUtility), nameof(MedicalCareUtility.MedicalCareSetter),
                new Type[] { typeof(Rect), typeof(MedicalCareCategory).MakeByRefType() });
        }

        [HarmonyPrefix]
        public static void Prefix(ref MedicalCareCategory medCare)
        {
            WidgetCapture.EnterMedicalCare(medCare);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitMedicalCare();
        }
    }

    /// <summary>
    /// Widgets.HSVColorWheel —the color-picker dialogs' hue/saturation wheel (decompiled
    /// Verse/Widgets.cs:2987-3021, drawn by Dialog_ColorPickerBase.cs:213). Zero capture of any
    /// kind existed for it: a screen-reader user never learned the control was there at all.
    /// TargetMethod because of the two by-ref parameters (CS0182), same as
    /// <see cref="WidgetCaptureColorBoxBracketPatch"/>. A single prefix suffices — there is no
    /// nested primitive to bracket and no post-click state to stamp afterward.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureHSVColorWheelPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "HSVColorWheel",
                new Type[]
                {
                    typeof(Rect), typeof(Color).MakeByRefType(), typeof(bool).MakeByRefType(),
                    typeof(float?), typeof(string),
                });
        }

        [HarmonyPrefix]
        public static void Prefix(Rect rect, ref Color color)
        {
            WidgetCapture.RecordColorWheel(rect, color);
        }
    }
}
