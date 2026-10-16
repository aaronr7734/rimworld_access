using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Records the 60x60 tile of every style item the styling station's grids draw — the four
    /// style tabs (decompiled RimWorld/Dialog_StylingStation.cs:544-604 DrawStylingItemType)
    /// and the two dev-only type tabs (:605-658 DevDrawBodyType, :660-713 DevDrawHeadType) —
    /// so <see cref="StylingStationScope"/> can ring the focused tile and scroll it into view.
    /// Vanilla's own <c>DrawBox</c> marks only the item the pawn currently wears (:583), and
    /// this screen deliberately does not auto-select on settle, so the keyboard cursor has no
    /// indicator of its own.
    ///
    /// None of the three grids hands out a per-tile rect, so each is bracketed and the tiles
    /// arrive through the one widget every tile calls exactly once, <c>Widgets.ButtonInvisible</c>
    /// (:585) — the deity-box shape <see cref="IdeoBoxDrawPatch"/> documents. Identity comes
    /// from vanilla's own draw order: <c>tmpStyleItems</c> (:107) is filled and sorted
    /// immediately before the loop walks it (:552-554), and the two dev loops walk DefDatabase
    /// under the filters <see cref="StylingStationHelper.BuildBodyTypeItems"/>/
    /// <see cref="StylingStationHelper.BuildHeadTypeItems"/> mirror. Anything the bracket
    /// records past that item count belongs to the trailing color selector (:599-602, :653-655),
    /// which is the Wave-6 ColorSelector family, and is discarded; too FEW records means a game
    /// change broke the one-button-per-tile assumption, so the pass publishes nothing.
    /// </summary>
    internal static class StylingGridDrawPatch
    {
        private struct Tile
        {
            public Def Def;

            /// <summary>Absolute UI points, already clipped to the scroll view's visible band.</summary>
            public Rect Screen;

            /// <summary>The tile as vanilla laid it out, in the grid's own view space (scroll-independent).</summary>
            public Rect Raw;
        }

        /// <summary>
        /// Read once per <c>Widgets.ButtonInvisible</c> call game-wide, so it stays a plain
        /// field and is the tap's first statement.
        /// </summary>
        internal static bool Recording;

        private static readonly List<Rect> rawRects = new List<Rect>();
        private static readonly List<Rect> screenRects = new List<Rect>();
        private static readonly List<Tile> tiles = new List<Tile>();

        private static bool armed;
        private static bool gridOpened;
        private static StylingTabKind recordingTab;
        private static Rect recordingOutRect;

        private static bool published;
        private static StylingTabKind publishedTab;
        private static Rect publishedOutRect;

        private static readonly AccessTools.FieldRef<Dialog_StylingStation, Dialog_StylingStation.StylingTab> CurTabRef =
            AccessTools.FieldRefAccess<Dialog_StylingStation, Dialog_StylingStation.StylingTab>("curTab");

        private static readonly AccessTools.FieldRef<List<StyleItemDef>> TmpStyleItemsRef =
            AccessTools.StaticFieldRefAccess<List<StyleItemDef>>(
                AccessTools.Field(typeof(Dialog_StylingStation), "tmpStyleItems"));

        /// <summary>
        /// The focused tile of <paramref name="tab"/>, or false when that tab is not the one the
        /// last pass drew (a region switch leaves them diverged for a frame), when the pass
        /// published nothing, or when the tile is scrolled out of view.
        /// <paramref name="outRect"/> is the grid's scroll-view rect, the origin its view space
        /// is anchored to.
        /// </summary>
        internal static bool TryGetTile(StylingTabKind tab, Def def, out Rect screen, out Rect raw, out Rect outRect)
        {
            screen = default(Rect);
            raw = default(Rect);
            outRect = default(Rect);
            if (!published || publishedTab != tab || def == null)
            {
                return false;
            }
            for (int i = 0; i < tiles.Count; i++)
            {
                if (!ReferenceEquals(tiles[i].Def, def))
                {
                    continue;
                }
                if (tiles[i].Screen.width <= 0f || tiles[i].Screen.height <= 0f)
                {
                    return false;
                }
                screen = tiles[i].Screen;
                raw = tiles[i].Raw;
                outRect = publishedOutRect;
                return true;
            }
            return false;
        }

        internal static void Begin(Window dialog, StylingTabKind tab, Rect rect)
        {
            try
            {
                Event ev = Event.current;
                if (ev == null || ev.type == EventType.Layout)
                {
                    return;
                }
                StylingStationScope scope = FocusStack.Top as StylingStationScope;
                if (scope == null || !scope.Owns(dialog))
                {
                    return;
                }
                armed = true;
                gridOpened = true;
                Recording = true;
                recordingTab = tab;
                recordingOutRect = rect;
                rawRects.Clear();
                screenRects.Clear();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Styling grid capture error", ex);
            }
        }

        /// <summary>The station whose DrawTabs is on the stack; only its own scroll views can be a style grid.</summary>
        private static Dialog_StylingStation DrawingTabsOf;

        internal static void BeginStationTabs(Dialog_StylingStation dialog)
        {
            DrawingTabsOf = dialog;
            gridOpened = false;
        }

        /// <summary>An empty style list skips the grid's scroll view entirely, so nothing must stay published for it.</summary>
        internal static void EndStationTabs()
        {
            Dialog_StylingStation dialog = DrawingTabsOf;
            DrawingTabsOf = null;
            End();
            Event ev = Event.current;
            if (!gridOpened && dialog != null && ev != null && ev.type != EventType.Layout && IsStyleItemTab(CurrentTab(dialog)))
            {
                published = false;
            }
        }

        private static bool IsStyleItemTab(StylingTabKind tab)
        {
            return tab == StylingTabKind.Hair || tab == StylingTabKind.Beard
                || tab == StylingTabKind.FaceTattoo || tab == StylingTabKind.BodyTattoo;
        }

        internal static void BeginStyleItemGrid(Rect outRect)
        {
            if (DrawingTabsOf == null)
            {
                return;
            }
            StylingTabKind tab = CurrentTab(DrawingTabsOf);
            if (IsStyleItemTab(tab))
            {
                Begin(DrawingTabsOf, tab, outRect);
            }
        }

        /// <summary>Arrives from a finalizer, so a modded style def that throws mid-grid disarms the tap all the same.</summary>
        internal static void End()
        {
            if (!armed)
            {
                return;
            }
            armed = false;
            Recording = false;
            Publish();
        }

        internal static void RecordTile(Rect butRect)
        {
            try
            {
                rawRects.Add(butRect);
                screenRects.Add(GuiSpace.VisibleScreenRect(butRect));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Styling grid tile capture error", ex);
            }
        }

        private static void Publish()
        {
            try
            {
                published = false;
                tiles.Clear();
                IReadOnlyList<Def> defs = DefsForPass();
                if (defs == null || defs.Count == 0 || rawRects.Count < defs.Count)
                {
                    return;
                }
                for (int i = 0; i < defs.Count; i++)
                {
                    tiles.Add(new Tile { Def = defs[i], Screen = screenRects[i], Raw = rawRects[i] });
                }
                publishedTab = recordingTab;
                publishedOutRect = recordingOutRect;
                published = true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Styling grid publish error", ex);
            }
        }

        private static IReadOnlyList<Def> DefsForPass()
        {
            switch (recordingTab)
            {
                case StylingTabKind.BodyType:
                    return StylingStationHelper.BuildBodyTypeItems();
                case StylingTabKind.HeadType:
                    return StylingStationHelper.BuildHeadTypeItems();
                default:
                    return TmpStyleItemsRef();
            }
        }

        /// <summary>The style tab vanilla is drawing, straight off its own field — our enum shares that one's ordinals.</summary>
        internal static StylingTabKind CurrentTab(Dialog_StylingStation dialog)
        {
            return (StylingTabKind)(int)CurTabRef(dialog);
        }
    }

    /// <summary>
    /// The four style tabs' grid. DrawStylingItemType&lt;T&gt; is generic and so unpatchable on
    /// 1.7; its only caller, the non-generic DrawTabs, scopes the grid's own scroll view instead.
    /// The bracket opens at that scroll view, after TabDrawer's hotspots, so tile order holds.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_StylingStation), "DrawTabs")]
    internal static class StylingItemGridScopePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_StylingStation __instance)
        {
            StylingGridDrawPatch.BeginStationTabs(__instance);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            StylingGridDrawPatch.EndStationTabs();
        }
    }

    [HarmonyPatch]
    internal static class StylingItemGridBracketPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "BeginScrollView",
                new[] { typeof(Rect), typeof(Vector2).MakeByRefType(), typeof(Rect), typeof(bool) });
        }

        [HarmonyPrefix]
        public static void Prefix(Rect outRect)
        {
            StylingGridDrawPatch.BeginStyleItemGrid(outRect);
        }
    }

    /// <summary>The dev-only Body type grid (decompiled :605).</summary>
    [HarmonyPatch(typeof(Dialog_StylingStation), "DevDrawBodyType")]
    internal static class StylingBodyTypeGridBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_StylingStation __instance, Rect rect)
        {
            StylingGridDrawPatch.Begin(__instance, StylingTabKind.BodyType, rect);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            StylingGridDrawPatch.End();
        }
    }

    /// <summary>The dev-only Head type grid (decompiled :660).</summary>
    [HarmonyPatch(typeof(Dialog_StylingStation), "DevDrawHeadType")]
    internal static class StylingHeadTypeGridBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_StylingStation __instance, Rect rect)
        {
            StylingGridDrawPatch.Begin(__instance, StylingTabKind.HeadType, rect);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            StylingGridDrawPatch.End();
        }
    }

    /// <summary>
    /// One tile per call, in draw order. This runs on every invisible button the game draws
    /// anywhere, so the static flag the brackets set is literally the first statement and is
    /// all an off-path click target ever costs.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonInvisible), new[] { typeof(Rect), typeof(bool) })]
    internal static class StylingGridTilePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect butRect)
        {
            if (!StylingGridDrawPatch.Recording)
            {
                return;
            }
            StylingGridDrawPatch.RecordTile(butRect);
        }
    }
}
