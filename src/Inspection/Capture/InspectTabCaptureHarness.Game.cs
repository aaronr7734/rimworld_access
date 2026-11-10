using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// IMGUI capture harness for inspect tabs: runs one tab's own FillTab under a detached
    /// WidgetCapture pass and returns the widget rows the game actually drew, in draw order —
    /// "what a sighted player sees" read straight from the render path, for tabs with no readable
    /// data model.
    ///
    /// The puppeteering contract:
    /// - FillTab carries no validity guard (StillValid lives in DoTabGUI's ImmediateWindow
    ///   closure), so a reflected call works with the inspect pane closed.
    /// - ITab.SelThing/SelPawn read Find.Selector.SingleSelected*, so the harness selects the
    ///   target first and restores the prior selection after.
    /// - IsVisible is honored: it is the vanilla gate for which tabs appear, and several FillTab
    ///   bodies assume the state it checks.
    /// - UpdateSize() runs first, then scroll positions are zeroed so row rects start at the origin.
    /// - The pass runs ONLY during Repaint, where buttons cannot return true and Layout-gated size
    ///   bookkeeping is skipped, so it is mutation-inert by construction.
    /// - The detached pass records into the caller's sink with scope injection disabled, leaving
    ///   the focus-scope engine's own Items and pending posts untouched.
    /// - <see cref="TryActivatePass"/> is the deliberate exception to mutation-inertness: it arms
    ///   exactly ONE captured control and fires that widget's own vanilla handler on the player's
    ///   explicit activation. Still Repaint-only, so no OTHER widget can see a click.
    ///
    /// Limitation: capture yields TEXT structure. Icon-only widgets are semantically opaque at the
    /// render layer and their meaning ships from the data adapters. Tooltips registered through
    /// TooltipHandler are captured on a detached TooltipCapture pass bracketing the draw, since
    /// those prefixes fire before vanilla's mouse gate; call-site-gated Mouse.IsOver tips stay
    /// invisible.
    ///
    /// Scroll fields: the harness zeroes Vector2 "scroll" fields on the tab's own type chain,
    /// instance and static, plus the statics on the companion *CardUtility classes some vanilla
    /// tabs draw through.
    /// </summary>
    public static class InspectTabCaptureHarness
    {
        private static readonly AccessTools.FieldRef<InspectTabBase, Vector2> sizeRef =
            AccessTools.FieldRefAccess<InspectTabBase, Vector2>("size");

        private static readonly Dictionary<Type, MethodInfo> fillTabByType =
            new Dictionary<Type, MethodInfo>();

        private static readonly Dictionary<Type, MethodInfo> updateSizeByType =
            new Dictionary<Type, MethodInfo>();

        private static readonly Dictionary<Type, List<FieldInfo>> scrollFieldsByType =
            new Dictionary<Type, List<FieldInfo>>();

        // Static utility classes some vanilla tabs draw through, whose scroll statics live outside
        // the tab's type chain. Resolution walks the base chain, so mod subclasses are covered.
        private static readonly Dictionary<Type, Type[]> companionScrollTypes =
            new Dictionary<Type, Type[]>
            {
                { typeof(ITab_Pawn_Health), new[] { typeof(HealthCardUtility) } },
                { typeof(ITab_Pawn_Character), new[] { typeof(CharacterCardUtility) } },
                { typeof(ITab_Pawn_Social), new[] { typeof(SocialCardUtility), typeof(InteractionCardUtility) } },
            };

        /// <summary>
        /// Runs one capture pass over <paramref name="tab"/> for <paramref name="target"/> —
        /// anything Find.Selector can select — appending the drawn rows to
        /// <paramref name="into"/>. Returns false with a reason instead of throwing for every
        /// anticipated failure, so callers can fall back to the hand-read adapter tier.
        /// </summary>
        public static bool TryCapturePass(InspectTabBase tab, object target, List<CapturedWidget> into, out string error)
        {
            return TryPassCore(tab, target, into, null, out error, out _);
        }

        /// <summary>Also reports the draw's own fault, what it threw or the GUI scopes it left open; null when it ran clean.</summary>
        public static bool TryCapturePass(InspectTabBase tab, object target, List<CapturedWidget> into, out string error,
            out Exception fault)
        {
            return TryPassCore(tab, target, into, null, out error, out fault);
        }

        /// <summary>
        /// Re-runs the draw with the (kind, label, ordinal) control's own vanilla handler armed.
        /// <paramref name="fired"/> is false when the tab reshaped and the descriptor no longer resolves.
        /// </summary>
        public static bool TryActivatePass(InspectTabBase tab, object target, WidgetKind kind, string label, int ordinal,
            List<CapturedWidget> into, out bool fired, out string error)
        {
            fired = false;
            bool ok = TryPassCore(tab, target, into,
                new ArmedDescriptor { Action = PassAction.Activate, Kind = kind, Label = label, Ordinal = ordinal }, out error, out _);
            fired = WidgetCapture.ArmedFired;
            return ok;
        }

        /// <summary>
        /// Re-runs the draw with the ordinal-th slider matching <paramref name="label"/> stepped
        /// one grid unit in <paramref name="direction"/>. Same contract as
        /// <see cref="TryActivatePass"/>.
        /// </summary>
        public static bool TryAdjustPass(InspectTabBase tab, object target, string label, int ordinal, int direction,
            List<CapturedWidget> into, out bool fired, out string error)
        {
            fired = false;
            bool ok = TryPassCore(tab, target, into,
                new ArmedDescriptor { Action = PassAction.Adjust, Kind = WidgetKind.Slider, Label = label, Ordinal = ordinal, Direction = direction },
                out error, out _);
            fired = WidgetCapture.ArmedFired;
            return ok;
        }

        /// <summary>
        /// Re-runs the draw with the ordinal-th slider matching <paramref name="label"/> set to
        /// <paramref name="value"/>, grid-snapped and clamped to its bounds.
        /// </summary>
        public static bool TrySetSliderPass(InspectTabBase tab, object target, string label, int ordinal, float value,
            List<CapturedWidget> into, out bool fired, out string error)
        {
            fired = false;
            bool ok = TryPassCore(tab, target, into,
                new ArmedDescriptor { Action = PassAction.SetSlider, Kind = WidgetKind.Slider, Label = label, Ordinal = ordinal, Value = value },
                out error, out _);
            fired = WidgetCapture.ArmedFired;
            return ok;
        }

        /// <summary>
        /// Re-runs the draw with the ordinal-th text field returning <paramref name="text"/> in
        /// place of its own value, consumed as native typing. Text fields record with an empty
        /// label, so the ordinal alone is their identity.
        /// </summary>
        public static bool TrySetTextPass(InspectTabBase tab, object target, int ordinal, string text,
            List<CapturedWidget> into, out bool fired, out string error)
        {
            fired = false;
            bool ok = TryPassCore(tab, target, into,
                new ArmedDescriptor { Action = PassAction.SetText, Kind = WidgetKind.TextField, Label = "", Ordinal = ordinal, Text = text },
                out error, out _);
            fired = WidgetCapture.ArmedFired;
            return ok;
        }

        /// <summary>What an armed pass does to its matched widget — mirrors WidgetCapture's own armed-action set.</summary>
        private enum PassAction { Activate, Adjust, SetSlider, SetText }

        /// <summary>The (kind, label, ordinal) descriptor of the one widget an armed pass acts on, plus the action and its payload — see <see cref="CaptureDescriptor"/>.</summary>
        private struct ArmedDescriptor
        {
            public PassAction Action;
            public WidgetKind Kind;
            public string Label;
            public int Ordinal;
            public int Direction;
            public float Value;
            public string Text;
        }

        /// <summary>
        /// Shared lifecycle of every tab pass: selection, the IsVisible gate, UpdateSize, scroll
        /// zeroing, GUI state save/restore, and the offscreen bracket, armed or not.
        /// </summary>
        private static bool TryPassCore(InspectTabBase tab, object target, List<CapturedWidget> into,
            ArmedDescriptor? armed, out string error, out Exception fault)
        {
            error = null;
            fault = null;
            if (tab == null || target == null || into == null)
            {
                error = "null tab/target/sink";
                return false;
            }
            if (Event.current == null || Event.current.type != EventType.Repaint)
            {
                error = "capture must run during a Repaint event (mutation-inertness contract)";
                return false;
            }

            List<object> savedSelection = null;
            if (Find.Selector.NumSelected != 1 || !Find.Selector.IsSelected(target))
            {
                savedSelection = Find.Selector.SelectedObjects.ToList();
                Find.Selector.ClearSelection();
                Find.Selector.Select(target, playSound: false, forceDesignatorDeselect: false);
            }

            Color savedColor = GUI.color;
            GameFont savedFont = Text.Font;
            TextAnchor savedAnchor = Text.Anchor;
            bool savedWordWrap = Text.WordWrap;
            try
            {
                if (!tab.IsVisible)
                {
                    string targetLabel = target is Thing thing ? thing.LabelCap.ToString()
                        : target is Zone zone ? zone.label
                        : target.ToString();
                    error = $"{tab.GetType().Name} reports IsVisible=false for {targetLabel}";
                    return false;
                }

                ResolveUpdateSize(tab.GetType()).Invoke(tab, null);
                ZeroScrollFields(tab);
                ZeroCompanionScrollStatics(tab.GetType());

                MethodInfo fillTab = ResolveFillTab(tab.GetType());
                int leaked = CaptureOffscreen(() => fillTab.Invoke(tab, null), sizeRef(tab), into, armed);
                if (leaked > 0)
                {
                    fault = new InvalidOperationException(
                        $"{tab.GetType().FullName}.FillTab left {leaked} GUI scroll view(s) or group(s) open");
                }
                return true;
            }
            catch (Exception ex)
            {
                fault = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                error = $"{tab.GetType().Name} capture threw: {fault}";
                return false;
            }
            finally
            {
                GUI.color = savedColor;
                Text.Font = savedFont;
                Text.Anchor = savedAnchor;
                Text.WordWrap = savedWordWrap;
                if (savedSelection != null)
                {
                    Find.Selector.ClearSelection();
                    foreach (object obj in savedSelection)
                    {
                        Find.Selector.Select(obj, playSound: false, forceDesignatorDeselect: false);
                    }
                }
            }
        }

        /// <summary>
        /// Captures an arbitrary IMGUI draw call under the same Repaint-only, offscreen,
        /// detached-pass bracket as a tab capture, without the tab lifecycle. Callers zero any
        /// scroll statics first via <see cref="ZeroScrollStatics"/>.
        /// </summary>
        public static bool TryCaptureDraw(Action draw, Vector2 size, List<CapturedWidget> into, out string error)
        {
            error = null;
            if (draw == null || into == null)
            {
                error = "null draw/sink";
                return false;
            }
            if (Event.current == null || Event.current.type != EventType.Repaint)
            {
                error = "capture must run during a Repaint event (mutation-inertness contract)";
                return false;
            }

            Color savedColor = GUI.color;
            GameFont savedFont = Text.Font;
            TextAnchor savedAnchor = Text.Anchor;
            bool savedWordWrap = Text.WordWrap;
            try
            {
                CaptureOffscreen(draw, size, into);
                return true;
            }
            catch (Exception ex)
            {
                error = $"draw capture threw: {ex.InnerException ?? ex}";
                return false;
            }
            finally
            {
                GUI.color = savedColor;
                Text.Font = savedFont;
                Text.Anchor = savedAnchor;
                Text.WordWrap = savedWordWrap;
            }
        }

        /// <summary>Zero the Vector2 "scroll" statics declared on <paramref name="type"/> (an info-card utility class).</summary>
        public static void ZeroScrollStatics(Type type)
        {
            foreach (FieldInfo field in ResolveScrollFields(type, walkTabChain: false))
            {
                if (field.IsStatic)
                {
                    field.SetValue(null, Vector2.zero);
                }
            }
        }

        /// <summary>
        /// The shared offscreen bracket: a far-offscreen group (nothing rasterizes, the real mouse never
        /// overlaps) around a detached capture pass, run as the inspect pane the way vanilla draws a tab,
        /// since mods key per-window state off currentlyDrawnWindow (BetterSliders throws when it is null).
        /// Returns how many scopes the draw left open; all are closed again, even when it throws.
        /// </summary>
        private static int CaptureOffscreen(Action draw, Vector2 size, List<CapturedWidget> into, ArmedDescriptor? armed = null)
        {
            int scrollViewsAtEntry = Widgets.mouseOverScrollViewStack.Count;
            int groupsAtEntry = GuiSpace.ClipDepth;
            int leaked = 0;
            Widgets.BeginGroup(new Rect(-20000f, -20000f, size.x, size.y));
            try
            {
                if (armed.HasValue)
                {
                    ArmedDescriptor a = armed.Value;
                    switch (a.Action)
                    {
                        case PassAction.Adjust:
                            WidgetCapture.BeginArmedAdjustPass(into, a.Label, a.Ordinal, a.Direction);
                            break;
                        case PassAction.SetSlider:
                            WidgetCapture.BeginArmedSliderSetPass(into, a.Label, a.Ordinal, a.Value);
                            break;
                        case PassAction.SetText:
                            WidgetCapture.BeginArmedTextSetPass(into, a.Ordinal, a.Text);
                            break;
                        default:
                            WidgetCapture.BeginArmedDetachedPass(into, a.Kind, a.Label, a.Ordinal);
                            break;
                    }
                }
                else
                {
                    WidgetCapture.BeginDetachedPass(into);
                }
                TooltipCapture.BeginDetachedPass();
                WindowStack stack = Find.WindowStack;
                Window savedDrawn = stack?.currentlyDrawnWindow;
                if (stack != null && savedDrawn == null)
                {
                    stack.currentlyDrawnWindow = stack.WindowOfType<MainTabWindow_Inspect>()
                        ?? (stack.Windows.Count > 0 ? stack.Windows[stack.Windows.Count - 1] : null);
                }
                try
                {
                    draw();
                }
                finally
                {
                    if (stack != null)
                    {
                        stack.currentlyDrawnWindow = savedDrawn;
                    }
                    TooltipCapture.EndDetachedPass();
                    WidgetCapture.EndDetachedPass();
                }
            }
            finally
            {
                leaked = UnwindTo(scrollViewsAtEntry, groupsAtEntry);
            }
            return leaked;
        }

        /// <summary>
        /// Closes leaked Widgets scroll views (mouse-over stack and clip together), then groups down to
        /// the entry depth, the bracket's own last; a raw GUI scroll view closes as a group.
        /// </summary>
        private static int UnwindTo(int scrollViewsAtEntry, int groupsAtEntry)
        {
            int closed = 0;
            while (Widgets.mouseOverScrollViewStack.Count > scrollViewsAtEntry)
            {
                Widgets.EndScrollView();
                closed++;
            }
            if (groupsAtEntry < 0)
            {
                Widgets.EndGroup();
                return closed;
            }
            int groupsOpen = GuiSpace.ClipDepth - groupsAtEntry;
            for (int i = 0; i < groupsOpen; i++)
            {
                Widgets.EndGroup();
            }
            return closed + Math.Max(0, groupsOpen - 1);
        }

        private static MethodInfo ResolveFillTab(Type tabType)
        {
            if (!fillTabByType.TryGetValue(tabType, out MethodInfo method))
            {
                method = AccessTools.Method(tabType, "FillTab");
                fillTabByType[tabType] = method;
            }
            return method;
        }

        private static MethodInfo ResolveUpdateSize(Type tabType)
        {
            if (!updateSizeByType.TryGetValue(tabType, out MethodInfo method))
            {
                method = AccessTools.Method(tabType, "UpdateSize");
                updateSizeByType[tabType] = method;
            }
            return method;
        }

        private static void ZeroScrollFields(InspectTabBase tab)
        {
            foreach (FieldInfo field in ResolveScrollFields(tab.GetType(), walkTabChain: true))
            {
                field.SetValue(field.IsStatic ? null : tab, Vector2.zero);
            }
        }

        private static void ZeroCompanionScrollStatics(Type tabType)
        {
            for (Type t = tabType; t != null; t = t.BaseType)
            {
                if (!companionScrollTypes.TryGetValue(t, out Type[] utilities))
                {
                    continue;
                }
                foreach (Type utility in utilities)
                {
                    foreach (FieldInfo field in ResolveScrollFields(utility, walkTabChain: false))
                    {
                        field.SetValue(null, Vector2.zero);
                    }
                }
                return;
            }
        }

        private static List<FieldInfo> ResolveScrollFields(Type type, bool walkTabChain)
        {
            if (scrollFieldsByType.TryGetValue(type, out List<FieldInfo> fields))
            {
                return fields;
            }
            fields = new List<FieldInfo>();
            for (Type t = type;
                t != null && (!walkTabChain || typeof(InspectTabBase).IsAssignableFrom(t));
                t = walkTabChain ? t.BaseType : null)
            {
                foreach (FieldInfo field in t.GetFields(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.FieldType == typeof(Vector2)
                        && field.Name.IndexOf("scroll", StringComparison.OrdinalIgnoreCase) >= 0
                        && !field.IsLiteral && !field.IsInitOnly)
                    {
                        fields.Add(field);
                    }
                }
            }
            scrollFieldsByType[type] = fields;
            return fields;
        }
    }
}
