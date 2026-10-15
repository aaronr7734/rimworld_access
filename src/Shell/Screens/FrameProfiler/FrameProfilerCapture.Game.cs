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
    /// <summary>Which part of <c>Window_FrameProfiler</c> is drawing; set by the draw brackets below.</summary>
    internal enum ProfilerZone
    {
        None,
        Title,
        Toolbar,
        HistogramHeader,
        Summary,
        TabStrip,
        ThreadButton,
        Pane,
        Status,
        Settings,
        Targets,
        Skip,
    }

    internal enum ProfilerControlKind
    {
        Chip,
        Ghost,
        Segment,
        Label,
        Heading,
        Checkbox,
        Field,
        TextArea,
        SearchField,
        ThreadButton,
    }

    /// <summary>One control or text run the profiler drew this pass, in absolute UI points.</summary>
    internal struct ProfilerControl
    {
        public ProfilerZone Zone;
        public ProfilerControlKind Kind;
        public string Label;
        public string Value;
        public string Unit;
        public bool Active;
        public bool Enabled;
        public Rect ScreenRect;
        public int Ordinal;
        public int SegmentGroup;
        public int SegmentIndex;
    }

    /// <summary>
    /// Records what <c>Window_FrameProfiler</c> draws, and presses its controls on request. Every
    /// interactive surface draws through a handful of primitives (<c>ProfilerGui.Chip</c>,
    /// <c>Ghost</c>, <c>Segments</c>, the settings drawer's private <c>Checkbox</c>/<c>Field</c>,
    /// <c>Widgets.TextField</c>/<c>TextArea</c>) whose return value IS the click, so an armed
    /// activation forces that return on the next pass and the window's own handler runs.
    ///
    /// Only the window the scope owns is recorded (<see cref="IsScoped"/>). Text runs draw on
    /// repaint only, so the published list is the last repaint pass's; arming matches on every pass.
    /// </summary>
    internal static class FrameProfilerCapture
    {
        internal static bool IsScoped(object window) => window != null && ReferenceEquals(window, FrameProfilerScope.Active?.ProfilerWindow);

        internal static ProfilerZone Zone;
        private static bool recording;
        private static int rowDepth;
        private static List<ProfilerControl> building = new List<ProfilerControl>();
        private static List<ProfilerControl> published = new List<ProfilerControl>();
        private static readonly Dictionary<long, int> ordinals = new Dictionary<long, int>();
        private static int segmentGroups;

        private struct Arm
        {
            public bool Live;
            public ProfilerControl Target;
            public string Text;
            public int Frame;
        }

        private static Arm arm;

        private static Func<string, GameFont, float> widthOf;

        /// <summary>The last repaint pass's controls, in draw order.</summary>
        internal static IReadOnlyList<ProfilerControl> Published => published;

        internal static void Reset()
        {
            building.Clear();
            published.Clear();
            arm = default(Arm);
        }

        /// <summary>Presses <paramref name="control"/> on the next pass that draws it.</summary>
        internal static void ArmPress(ProfilerControl control)
        {
            arm = new Arm { Live = true, Target = control, Frame = Time.frameCount };
        }

        /// <summary>Feeds <paramref name="text"/> into a field or text area as if typed, then commits it the way the field's Enter or Apply does.</summary>
        internal static void ArmText(ProfilerControl control, string text)
        {
            ArmPress(control);
            arm.Text = text ?? "";
        }

        private static bool Recording => recording && Zone != ProfilerZone.None && Zone != ProfilerZone.Skip && rowDepth == 0;

        private static int NextOrdinal(ProfilerZone zone, ProfilerControlKind kind, string label)
        {
            long key = ((long)zone << 40) ^ ((long)kind << 32) ^ (uint)(label ?? "").GetHashCode();
            int ordinal;
            ordinals.TryGetValue(key, out ordinal);
            ordinals[key] = ordinal + 1;
            return ordinal;
        }

        /// <summary>A live arm for this zone and kind; an arm nothing drew within two frames expires.</summary>
        private static bool ArmLive(ProfilerControlKind kind)
        {
            if (!arm.Live)
            {
                return false;
            }
            if (Time.frameCount > arm.Frame + 2)
            {
                arm.Live = false;
                return false;
            }
            return arm.Target.Zone == Zone && arm.Target.Kind == kind;
        }

        private static bool ArmMatches(ProfilerControlKind kind, string label, int ordinal)
        {
            return ArmLive(kind) && arm.Target.Label == label && arm.Target.Ordinal == ordinal;
        }

        private static void Add(ProfilerControl control)
        {
            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                building.Add(control);
            }
        }

        // ---- Pass bracket ----

        internal static void BeginPass(object window)
        {
            recording = IsScoped(window);
            Zone = ProfilerZone.None;
            rowDepth = 0;
            segmentGroups = 0;
            ordinals.Clear();
            building.Clear();
        }

        internal static void EndPass()
        {
            if (recording && Event.current != null && Event.current.type == EventType.Repaint)
            {
                List<ProfilerControl> swap = published;
                published = building;
                building = swap;
            }
            recording = false;
            Zone = ProfilerZone.None;
        }

        // ---- Primitive taps ----

        internal static void OnChip(Rect rect, string label, bool active, bool enabled, ProfilerControlKind kind, ref bool result)
        {
            if (!recording || Zone == ProfilerZone.Skip || rowDepth > 0)
            {
                return;
            }
            int ordinal = NextOrdinal(Zone, kind, label);
            if (enabled && !result && ArmMatches(kind, label, ordinal))
            {
                arm.Live = false;
                result = true;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            if (Zone != ProfilerZone.None)
            {
                Add(new ProfilerControl
                {
                    Zone = Zone, Kind = kind, Label = label ?? "", Active = active, Enabled = enabled,
                    ScreenRect = GuiSpace.ToScreen(rect), Ordinal = ordinal,
                });
            }
        }

        internal static void OnSegments(float x, float y, string[] labels, int activeIndex, ref int result)
        {
            if (!recording || Zone == ProfilerZone.None || Zone == ProfilerZone.Skip || labels == null)
            {
                return;
            }
            int group = segmentGroups++;
            if (ArmLive(ProfilerControlKind.Segment) && arm.Target.SegmentGroup == group)
            {
                arm.Live = false;
                int index = arm.Target.SegmentIndex;
                if (index != activeIndex && index >= 0 && index < labels.Length)
                {
                    result = index;
                    SoundDefOf.RowTabSelect.PlayOneShotOnCamera();
                }
            }
            if (Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            float left = x + 2f;
            for (int i = 0; i < labels.Length; i++)
            {
                float width = WidthOf(labels[i]) + 18f;
                building.Add(new ProfilerControl
                {
                    Zone = Zone, Kind = ProfilerControlKind.Segment, Label = labels[i] ?? "", Active = i == activeIndex,
                    Enabled = true, ScreenRect = GuiSpace.ToScreen(new Rect(left, y + 2f, width, 18f)),
                    SegmentGroup = group, SegmentIndex = i,
                });
                left += width;
            }
        }

        internal static void OnLabel(Rect rect, string text)
        {
            if (!Recording || Zone == ProfilerZone.Title || string.IsNullOrEmpty(text) || Event.current == null
                || Event.current.type != EventType.Repaint)
            {
                return;
            }
            if (Zone == ProfilerZone.ThreadButton)
            {
                AppendToLast(ProfilerControlKind.ThreadButton, text, rect);
                return;
            }
            building.Add(new ProfilerControl
            {
                Zone = Zone, Kind = ProfilerControlKind.Label, Label = text, Enabled = true, ScreenRect = GuiSpace.ToScreen(rect),
            });
        }

        private static void AppendToLast(ProfilerControlKind kind, string text, Rect rect)
        {
            Rect screen = GuiSpace.ToScreen(rect);
            int last = building.Count - 1;
            if (last >= 0 && building[last].Kind == kind)
            {
                ProfilerControl control = building[last];
                control.Value = string.IsNullOrEmpty(control.Value) ? text : control.Value + " " + text;
                control.ScreenRect = GuiSpace.Union(control.ScreenRect, screen);
                building[last] = control;
                return;
            }
            building.Add(new ProfilerControl { Zone = ProfilerZone.TabStrip, Kind = kind, Label = text, Enabled = true, ScreenRect = screen });
        }

        internal static void OnTextField(Rect rect, ref string result, bool area)
        {
            ProfilerControlKind kind = area ? ProfilerControlKind.TextArea : ProfilerControlKind.SearchField;
            if (!recording || rowDepth > 0 || (Zone != ProfilerZone.TabStrip && Zone != ProfilerZone.Targets))
            {
                return;
            }
            if (ArmMatches(kind, "", 0))
            {
                arm.Live = false;
                result = arm.Text;
            }
            Add(new ProfilerControl
            {
                Zone = Zone, Kind = kind, Label = "", Value = result ?? "", Enabled = true, ScreenRect = GuiSpace.ToScreen(rect),
            });
        }

        // ---- Settings drawer rows ----

        internal static void OnSection(float y, float w, string title)
        {
            if (!Recording)
            {
                return;
            }
            Add(new ProfilerControl
            {
                Zone = Zone, Kind = ProfilerControlKind.Heading, Label = title ?? "", Enabled = true,
                ScreenRect = GuiSpace.ToScreen(new Rect(14f, y, w - 28f, 28f)),
            });
        }

        internal static void EnterRow()
        {
            rowDepth++;
        }

        internal static void ExitRow()
        {
            rowDepth = Mathf.Max(0, rowDepth - 1);
        }

        internal static void OnCheckbox(float y, float w, string label, bool value, bool enabled, ref bool result)
        {
            if (!recording || Zone != ProfilerZone.Settings)
            {
                return;
            }
            if (enabled && ArmMatches(ProfilerControlKind.Checkbox, label, 0))
            {
                arm.Live = false;
                result = true;
                (value ? SoundDefOf.Checkbox_TurnedOff : SoundDefOf.Checkbox_TurnedOn).PlayOneShotOnCamera();
            }
            Add(new ProfilerControl
            {
                Zone = Zone, Kind = ProfilerControlKind.Checkbox, Label = label ?? "", Active = value, Enabled = enabled,
                ScreenRect = GuiSpace.ToScreen(new Rect(0f, y, w, 30f)),
            });
        }

        /// <summary>Prefix half of the settings Field tap: an armed edit replaces the draft before the field draws it.</summary>
        internal static bool TakeFieldText(string label, ref string draft)
        {
            if (!recording || Zone != ProfilerZone.Settings || !ArmMatches(ProfilerControlKind.Field, label, 0))
            {
                return false;
            }
            arm.Live = false;
            draft = arm.Text;
            return true;
        }

        internal static void OnField(float y, float w, string label, string unit, string draft, string applied)
        {
            if (!recording || Zone != ProfilerZone.Settings)
            {
                return;
            }
            Add(new ProfilerControl
            {
                Zone = Zone, Kind = ProfilerControlKind.Field, Label = label ?? "", Value = draft ?? applied ?? "", Unit = unit ?? "",
                Enabled = true, ScreenRect = GuiSpace.ToScreen(new Rect(0f, y, w, 30f)),
            });
        }

        private static float WidthOf(string text)
        {
            if (widthOf == null)
            {
                widthOf = AccessTools.MethodDelegate<Func<string, GameFont, float>>(
                    AccessTools.Method(FrameProfilerAccess.ProfilerGuiType, "Width", new[] { typeof(string), typeof(GameFont) }));
            }
            return widthOf(text ?? "", GameFont.Tiny);
        }
    }

    // ---- Harmony taps. Every class declines on 1.6, where the profiler types do not exist. ----

    [HarmonyPatch]
    internal static class FrameProfilerPassPatch
    {
        static bool Prepare() => FrameProfilerAccess.Ready;

        static MethodBase TargetMethod() => AccessTools.Method(FrameProfilerAccess.WindowType, "DoWindowContents");

        [HarmonyPrefix]
        public static void Prefix(object __instance)
        {
            FrameProfilerCapture.BeginPass(__instance);
        }

        [HarmonyPostfix]
        public static void Postfix(object __instance)
        {
            bool repaint = Event.current != null && Event.current.type == EventType.Repaint;
            FrameProfilerCapture.EndPass();
            FrameProfilerScope scope = FrameProfilerScope.Active;
            if (repaint && scope != null && FrameProfilerCapture.IsScoped(__instance))
            {
                scope.OnRepaint();
            }
        }
    }

    /// <summary>The scope owns the keyboard while attached; it calls the same session and view methods this handler does.</summary>
    [HarmonyPatch]
    internal static class FrameProfilerKeysPatch
    {
        static bool Prepare() => FrameProfilerAccess.Ready;

        static MethodBase TargetMethod() => AccessTools.Method(FrameProfilerAccess.WindowType, "HandleKeys");

        [HarmonyPrefix]
        public static bool Prefix(object __instance)
        {
            return !FrameProfilerCapture.IsScoped(__instance);
        }
    }

    [HarmonyPatch]
    internal static class FrameProfilerZonePatch
    {
        static bool Prepare() => FrameProfilerAccess.Ready;

        static IEnumerable<MethodBase> TargetMethods()
        {
            Type window = FrameProfilerAccess.WindowType;
            yield return AccessTools.Method(window, "DrawTitleBar");
            yield return AccessTools.Method(window, "DrawToolbar");
            yield return AccessTools.Method(window, "DrawHistogramHeader");
            yield return AccessTools.Method(window, "DrawSummary");
            yield return AccessTools.Method(window, "DrawTabStrip");
            yield return AccessTools.Method(window, "DrawStatusBar");
            yield return AccessTools.Method(FrameProfilerAccess.SettingsType, "Draw");
        }

        [HarmonyPrefix]
        public static void Prefix(MethodBase __originalMethod)
        {
            switch (__originalMethod.Name)
            {
                case "DrawTitleBar": FrameProfilerCapture.Zone = ProfilerZone.Title; break;
                case "DrawToolbar": FrameProfilerCapture.Zone = ProfilerZone.Toolbar; break;
                case "DrawHistogramHeader": FrameProfilerCapture.Zone = ProfilerZone.HistogramHeader; break;
                case "DrawSummary": FrameProfilerCapture.Zone = ProfilerZone.Summary; break;
                case "DrawTabStrip": FrameProfilerCapture.Zone = ProfilerZone.TabStrip; break;
                case "DrawStatusBar": FrameProfilerCapture.Zone = ProfilerZone.Status; break;
                default: FrameProfilerCapture.Zone = ProfilerZone.Settings; break;
            }
        }

        /// <summary>The table pane draws between the tab strip and the status bar, so the tab strip hands over to it.</summary>
        [HarmonyPostfix]
        public static void Postfix(MethodBase __originalMethod)
        {
            FrameProfilerCapture.Zone = __originalMethod.Name == "DrawTabStrip" ? ProfilerZone.Pane : ProfilerZone.None;
        }
    }

    /// <summary>
    /// Nested draws that switch zone for their duration: the thread button and the targets editor
    /// record as their own zones, and surfaces the scope reads from the model instead are skipped.
    /// </summary>
    [HarmonyPatch]
    internal static class FrameProfilerNestedZonePatch
    {
        static bool Prepare() => FrameProfilerAccess.Ready;

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(FrameProfilerAccess.ThreadFilterType, "DrawButton");
            yield return AccessTools.Method(FrameProfilerAccess.SettingsType, "DrawTargets");
            yield return AccessTools.Method(FrameProfilerAccess.ViewType, "DrawHeader");
            yield return AccessTools.Method(FrameProfilerAccess.ViewType, "DrawRows");
            yield return AccessTools.Method(FrameProfilerAccess.ViewType, "DrawBreadcrumbs");
            yield return AccessTools.Method(FrameProfilerAccess.CallTreeType, "DrawFlamegraph");
            yield return AccessTools.Method(FrameProfilerAccess.ThreadFilterType, "DrawPopup");
        }

        [HarmonyPrefix]
        public static void Prefix(MethodBase __originalMethod, out ProfilerZone __state)
        {
            __state = FrameProfilerCapture.Zone;
            switch (__originalMethod.Name)
            {
                case "DrawButton": FrameProfilerCapture.Zone = ProfilerZone.ThreadButton; break;
                case "DrawTargets": FrameProfilerCapture.Zone = ProfilerZone.Targets; break;
                default: FrameProfilerCapture.Zone = ProfilerZone.Skip; break;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(ProfilerZone __state)
        {
            FrameProfilerCapture.Zone = __state;
        }
    }

    [HarmonyPatch]
    internal static class FrameProfilerChipPatch
    {
        static bool Prepare() => FrameProfilerAccess.Ready;

        static MethodBase TargetMethod() => AccessTools.Method(FrameProfilerAccess.ProfilerGuiType, "Chip");

        /// <summary>The caption it draws is the control's own label, not a separate text run.</summary>
        [HarmonyPrefix]
        public static void Prefix()
        {
            FrameProfilerCapture.EnterRow();
        }

        [HarmonyPostfix]
        public static void Postfix(Rect rect, string label, bool active, bool enabled, ref bool __result)
        {
            FrameProfilerCapture.ExitRow();
            FrameProfilerCapture.OnChip(rect, label, active, enabled, ProfilerControlKind.Chip, ref __result);
        }
    }

    [HarmonyPatch]
    internal static class FrameProfilerGhostPatch
    {
        static bool Prepare() => FrameProfilerAccess.Ready;

        static MethodBase TargetMethod() => AccessTools.Method(FrameProfilerAccess.ProfilerGuiType, "Ghost");

        /// <summary>The caption it draws is the control's own label, not a separate text run.</summary>
        [HarmonyPrefix]
        public static void Prefix()
        {
            FrameProfilerCapture.EnterRow();
        }

        [HarmonyPostfix]
        public static void Postfix(Rect rect, string label, bool active, ref bool __result)
        {
            FrameProfilerCapture.ExitRow();
            FrameProfilerCapture.OnChip(rect, label, active, true, ProfilerControlKind.Ghost, ref __result);
        }
    }

    [HarmonyPatch]
    internal static class FrameProfilerSegmentsPatch
    {
        static bool Prepare() => FrameProfilerAccess.Ready;

        static MethodBase TargetMethod() => AccessTools.Method(FrameProfilerAccess.ProfilerGuiType, "Segments");

        [HarmonyPrefix]
        public static void Prefix(ref float x, out float __state)
        {
            __state = x;
            FrameProfilerCapture.EnterRow();
        }

        [HarmonyPostfix]
        public static void Postfix(float __state, float y, string[] labels, int activeIndex, ref int __result)
        {
            FrameProfilerCapture.ExitRow();
            FrameProfilerCapture.OnSegments(__state, y, labels, activeIndex, ref __result);
        }
    }

    [HarmonyPatch]
    internal static class FrameProfilerLabelPatch
    {
        static bool Prepare() => FrameProfilerAccess.Ready;

        static MethodBase TargetMethod() => AccessTools.Method(FrameProfilerAccess.ProfilerGuiType, "Label");

        [HarmonyPrefix]
        public static void Prefix(Rect rect, string text)
        {
            FrameProfilerCapture.OnLabel(rect, text);
        }
    }

    [HarmonyPatch]
    internal static class FrameProfilerSectionPatch
    {
        static bool Prepare() => FrameProfilerAccess.Ready;

        static MethodBase TargetMethod() => AccessTools.Method(FrameProfilerAccess.SettingsType, "Section");

        [HarmonyPrefix]
        public static void Prefix(ref float y, float w, string title)
        {
            FrameProfilerCapture.OnSection(y, w, title);
            FrameProfilerCapture.EnterRow();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            FrameProfilerCapture.ExitRow();
        }
    }

    [HarmonyPatch]
    internal static class FrameProfilerCheckboxPatch
    {
        static bool Prepare() => FrameProfilerAccess.Ready;

        static MethodBase TargetMethod() => AccessTools.Method(FrameProfilerAccess.SettingsType, "Checkbox");

        [HarmonyPrefix]
        public static void Prefix(ref float y, out float __state)
        {
            __state = y;
            FrameProfilerCapture.EnterRow();
        }

        [HarmonyPostfix]
        public static void Postfix(float __state, float w, string label, bool value, bool enabled, ref bool __result)
        {
            FrameProfilerCapture.ExitRow();
            FrameProfilerCapture.OnCheckbox(__state, w, label, value, enabled, ref __result);
        }
    }

    [HarmonyPatch]
    internal static class FrameProfilerFieldPatch
    {
        static bool Prepare() => FrameProfilerAccess.Ready;

        static MethodBase TargetMethod() => AccessTools.Method(FrameProfilerAccess.SettingsType, "Field");

        [HarmonyPrefix]
        public static void Prefix(ref float y, string label, ref string draft, out (float y, bool commit) __state)
        {
            __state = (y, FrameProfilerCapture.TakeFieldText(label, ref draft));
            FrameProfilerCapture.EnterRow();
        }

        [HarmonyPostfix]
        public static void Postfix((float y, bool commit) __state, float w, string label, string unit, ref string draft, string applied, ref bool __result)
        {
            FrameProfilerCapture.ExitRow();
            FrameProfilerCapture.OnField(__state.y, w, label, unit, draft, applied);
            if (__state.commit)
            {
                __result = true;
            }
        }
    }
}
