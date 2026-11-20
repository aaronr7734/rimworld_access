using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keeps vanilla's own thing-filter tree panels showing what the keyboard is doing: branches
    /// the player expands open on screen, the focused row carries the shared focus ring, and a
    /// focused row scrolled out of view is brought back.
    ///
    /// One mechanism serves every <c>ThingFilterUI.DoThingFilterConfigWindow</c> surface in the
    /// game, including a mod's, because both halves are read from vanilla rather than
    /// reconstructed: the window prefix hands over the panel's <c>UIState</c>, the
    /// <c>openMask</c> the caller actually passed and the <c>ThingFilter</c> identifying the
    /// registered scope, and the per-row prefixes hand over each row's y inside
    /// <see cref="Listing_Tree"/>'s own group, in vanilla's draw order.
    ///
    /// ROW IDENTITY IS BY OBJECT, NEVER BY TEXT: every row carries the vanilla object it was
    /// built from in <see cref="ThingFilterSessionCore.FilterNodeData.Reference"/>, exactly the
    /// arguments vanilla passes to its three row painters. A row not identified this way draws
    /// NOTHING: the undiscovered-items row is rebuilt per pass, never matches by reference, and
    /// stays unringed. A wrong ring is worse than no ring.
    ///
    /// The open-state sync is EDGE-TRIGGERED, so vanilla's own bits stay vanilla's: an open bit
    /// is written only when the model's expansion actually changes, and the first sight of a node
    /// records its state without writing. Vanilla's remembered open branches therefore survive
    /// the window opening instead of being flattened.
    /// </summary>
    internal static class ThingFilterTreeSync
    {
        private static readonly List<FilterTreeScopeBase> scopes = new List<FilterTreeScopeBase>();

        // The openMask is a literal at each vanilla call site, never exposed by an API, and it
        // differs per screen while one scope type can serve several. It is therefore remembered
        // against the FILTER a panel was seen drawing, the same object a rebuilt model asks about.
        // Weak keys, so a destroyed building's settings are not held alive by this record.
        private static readonly ConditionalWeakTable<ThingFilter, object> openMasks =
            new ConditionalWeakTable<ThingFilter, object>();

        // Each panel's rows in absolute UI points, for pointer routing. Per FILTER, not one
        // shared map: the reading dialog draws two panels whose rows are the SAME vanilla objects,
        // so a single map would let one panel's geometry answer for the other's row.
        private static readonly ConditionalWeakTable<ThingFilter, RowGeometryCache> rowGeometry =
            new ConditionalWeakTable<ThingFilter, RowGeometryCache>();

        // curY is protected on Listing itself, and a non-public field is not found through a
        // derived type, so this ref is taken against the DECLARING type.
        private static readonly AccessTools.FieldRef<Listing, float> curYField =
            AccessTools.FieldRefAccess<Listing, float>("curY");

        private static readonly AccessTools.FieldRef<Listing_TreeThingFilter, Rect> visibleRectField =
            AccessTools.FieldRefAccess<Listing_TreeThingFilter, Rect>("visibleRect");

        // Per-panel pass state. DoThingFilterConfigWindow calls never nest, so one set of fields
        // covers the reading dialog's two panels drawn back to back.
        private static ThingFilterUI.UIState panelState;
        private static RowGeometryCache panelGeometry;
        private static object focusedReference;
        private static float pendingScrollDelta;
        private static bool ringPending;
        private static float ringY;

        internal static void Register(FilterTreeScopeBase scope)
        {
            if (scope != null && !scopes.Contains(scope))
            {
                scopes.Add(scope);
            }
        }

        internal static void Unregister(FilterTreeScopeBase scope)
        {
            scopes.Remove(scope);
        }

        /// <summary>
        /// Opens the pass for one drawn panel: finds the registered scope editing this filter,
        /// pushes keyboard-driven expansion into vanilla's own open bits, and arms the row hooks
        /// with the focused row's vanilla object.
        /// </summary>
        private static void BeginPanel(ThingFilter filter, ThingFilterUI.UIState state, int openMask)
        {
            EndPanel();
            if (filter == null || state == null)
            {
                return;
            }
            for (int i = 0; i < scopes.Count; i++)
            {
                if (!scopes[i].TryResolveVisualPanel(filter, out InspectionTreeItem root, out object focused))
                {
                    continue;
                }
                panelState = state;
                panelGeometry = rowGeometry.GetValue(filter, _ => new RowGeometryCache());
                focusedReference = focused;
                NoteOpenMask(filter, openMask);
                PushOpenState(root, openMask);
                return;
            }
        }

        private static void EndPanel()
        {
            panelState = null;
            panelGeometry = null;
            focusedReference = null;
            pendingScrollDelta = 0f;
            ringPending = false;
        }

        /// <summary>Remembers the mask this filter's panel draws with, boxing only on a change so a per-frame draw allocates nothing.</summary>
        private static void NoteOpenMask(ThingFilter filter, int openMask)
        {
            object known;
            if (openMasks.TryGetValue(filter, out known))
            {
                if ((int)known == openMask)
                {
                    return;
                }
                openMasks.Remove(filter);
            }
            openMasks.Add(filter, openMask);
        }

        /// <summary>The mask vanilla last drew this filter's panel with, or 0 before any draw — a windowless screen, or any screen's first build.</summary>
        internal static int OpenMaskFor(ThingFilter filter)
        {
            object known;
            return filter != null && openMasks.TryGetValue(filter, out known) ? (int)known : 0;
        }

        /// <summary>
        /// Writes each category's open bit into vanilla's own <see cref="TreeNode"/> when, and
        /// only when, the model's expansion changed since the last write. Hand-rolled recursion
        /// rather than an enumerator: this runs for every drawn panel on every GUI pass.
        /// </summary>
        private static void PushOpenState(InspectionTreeItem item, int openMask)
        {
            if (item == null)
            {
                return;
            }
            if (item.Data is ThingFilterSessionCore.FilterNodeData data
                && data.Reference is TreeNode_ThingCategory node)
            {
                if (!data.LastSyncedOpen.HasValue)
                {
                    data.LastSyncedOpen = item.IsExpanded;
                }
                else if (data.LastSyncedOpen.Value != item.IsExpanded)
                {
                    data.LastSyncedOpen = item.IsExpanded;
                    node.SetOpen(openMask, item.IsExpanded);
                }
            }
            List<InspectionTreeItem> children = item.Children;
            if (children == null)
            {
                return;
            }
            for (int i = 0; i < children.Count; i++)
            {
                PushOpenState(children[i], openMask);
            }
        }

        /// <summary>Called from each row painter's prefix, before vanilla's off-screen cull, so a focused row scrolled out of view still reports its position.</summary>
        private static void NoteRow(Listing_TreeThingFilter listing, object reference)
        {
            if (panelGeometry != null)
            {
                panelGeometry.Record(reference,
                    new Rect(0f, curYField(listing), listing.ColumnWidth, listing.lineHeight));
            }
            if (focusedReference == null || !ReferenceEquals(reference, focusedReference))
            {
                return;
            }
            float y = curYField(listing);
            ringPending = true;
            ringY = y;

            Rect visible = visibleRectField(listing);
            float bottom = y + listing.lineHeight;
            if (y < visible.yMin)
            {
                pendingScrollDelta = y - visible.yMin;
            }
            else if (bottom > visible.yMax)
            {
                pendingScrollDelta = bottom - visible.yMax;
            }
        }

        /// <summary>Appends a row as a pointer-routing candidate for (region, index), when that row drew.</summary>
        internal static void AddRouteCandidates(ThingFilter filter, object reference, int region, int index,
            List<PointerHitCandidate> candidates, List<ScreenScope.RouteTarget> targets)
        {
            RowGeometryCache cache;
            if (filter == null || !rowGeometry.TryGetValue(filter, out cache))
            {
                return;
            }
            cache.AddCandidate(reference, region, index, candidates, targets);
        }

        /// <summary>The window whose GUI pass last drew this filter's panel, or null before its first draw — the pointer surface for a mirror-pushed scope.</summary>
        internal static Window HostWindowFor(ThingFilter filter)
        {
            RowGeometryCache cache;
            return filter != null && rowGeometry.TryGetValue(filter, out cache) ? cache.HostWindow : null;
        }

        /// <summary>
        /// Called from each row painter's postfix, inside <see cref="Listing.Begin"/>'s own group,
        /// so the rect vanilla drew into is the rect the ring draws into. A culled row is clipped
        /// away by the surrounding scroll view rather than specially handled.
        /// </summary>
        private static void DrawRowRing(Listing_TreeThingFilter listing)
        {
            if (!ringPending)
            {
                return;
            }
            ringPending = false;
            FocusRing.Draw(new Rect(0f, ringY, listing.ColumnWidth, listing.lineHeight));
        }

        /// <summary>Applies the deferred scroll nudge once the panel has been walked: only on a repaint, and only when the focused row fell outside the visible band.</summary>
        private static void ApplyScroll()
        {
            if (panelState == null || pendingScrollDelta == 0f
                || Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            Vector2 scroll = panelState.scrollPosition;
            scroll.y = Mathf.Max(0f, scroll.y + pendingScrollDelta);
            panelState.scrollPosition = scroll;
        }

        [HarmonyPatch(typeof(ThingFilterUI), nameof(ThingFilterUI.DoThingFilterConfigWindow))]
        internal static class PanelPatch
        {
            [HarmonyPrefix]
            public static void Prefix(ThingFilterUI.UIState state, ThingFilter filter, int openMask)
            {
                BeginPanel(filter, state, openMask);
            }

            [HarmonyPostfix]
            public static void Postfix()
            {
                ApplyScroll();
                EndPanel();
            }
        }

        /// <summary>The range editor's type-in box: these two private statics each draw one Widgets.FloatRange for their filter.</summary>
        [HarmonyPatch(typeof(ThingFilterUI), "DrawHitPointsFilterConfig")]
        internal static class HitPointsRangeDrawPatch
        {
            [HarmonyPrefix]
            public static void Prefix(ThingFilter __2, out bool __state)
            {
                __state = RangeEditMenuState.EnterRangeDraw(__2, RangeEditMenuState.RangeType.HitPoints);
            }

            [HarmonyFinalizer]
            public static void Finalizer(bool __state)
            {
                if (__state)
                {
                    RangeEditMenuState.ExitRangeDraw();
                }
            }
        }

        [HarmonyPatch(typeof(ThingFilterUI), "DrawMentalBreakFilterConfig")]
        internal static class MentalBreakRangeDrawPatch
        {
            [HarmonyPrefix]
            public static void Prefix(ThingFilter __2, out bool __state)
            {
                __state = RangeEditMenuState.EnterRangeDraw(__2, RangeEditMenuState.RangeType.MentalBreakChance);
            }

            [HarmonyFinalizer]
            public static void Finalizer(bool __state)
            {
                if (__state)
                {
                    RangeEditMenuState.ExitRangeDraw();
                }
            }
        }

        /// <summary>
        /// The three identifiable row painters: private, so each needs its own TargetMethod, and
        /// arguments bind positionally (<c>__0</c>).
        /// </summary>
        [HarmonyPatch]
        internal static class CategoryRowPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(Listing_TreeThingFilter), "DoCategory");
            }

            [HarmonyPrefix]
            public static void Prefix(Listing_TreeThingFilter __instance, TreeNode_ThingCategory __0)
            {
                NoteRow(__instance, __0);
            }

            [HarmonyPostfix]
            public static void Postfix(Listing_TreeThingFilter __instance)
            {
                DrawRowRing(__instance);
            }
        }

        [HarmonyPatch]
        internal static class ThingDefRowPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(Listing_TreeThingFilter), "DoThingDef");
            }

            [HarmonyPrefix]
            public static void Prefix(Listing_TreeThingFilter __instance, ThingDef __0)
            {
                NoteRow(__instance, __0);
            }

            [HarmonyPostfix]
            public static void Postfix(Listing_TreeThingFilter __instance)
            {
                DrawRowRing(__instance);
            }
        }

        [HarmonyPatch]
        internal static class SpecialFilterRowPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(Listing_TreeThingFilter), "DoSpecialFilter");
            }

            [HarmonyPrefix]
            public static void Prefix(Listing_TreeThingFilter __instance, SpecialThingFilterDef __0)
            {
                NoteRow(__instance, __0);
            }

            [HarmonyPostfix]
            public static void Postfix(Listing_TreeThingFilter __instance)
            {
                DrawRowRing(__instance);
            }
        }
    }
}
