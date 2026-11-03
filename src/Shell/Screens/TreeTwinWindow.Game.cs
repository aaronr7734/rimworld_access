using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>One visible row of a <see cref="TreeTwinWindow"/> mount, filled from the mount's own node data.</summary>
    internal struct TreeTwinRow
    {
        public string Label;
        public int Level;
        public bool IsExpandable;
        public bool IsExpanded;

        /// <summary>Drawn with <see cref="Widgets.DefIcon"/> unless <see cref="IconThing"/> is set; <see cref="IconStuff"/> is its material.</summary>
        public Def Icon;
        public ThingDef IconStuff;
        public Thing IconThing;

        /// <summary>Right-aligned secondary text (a count), or null.</summary>
        public string RightText;
    }

    /// <summary>
    /// What a screen publishes for <see cref="TreeTwinWindow"/> to draw. Several mounts can be
    /// registered at once, and the twin draws whichever one's scope sits highest on the focus stack.
    /// </summary>
    internal interface ITreeTwinMount
    {
        /// <summary>The scope this mount belongs to; it decides both which mount draws and whether the focus presentation is live.</summary>
        FocusScope Scope { get; }

        string Title { get; }
        int RowCount { get; }
        TreeTwinRow RowAt(int index);

        /// <summary>The row the keyboard rests on, or -1 while the cursor is off the tree.</summary>
        int FocusedIndex { get; }

        /// <summary>Silent cursor sync, for a mouse click landing on a row.</summary>
        void MoveFocusTo(int index);

        /// <summary>Runs the row through the scope's own activation path — the one Enter takes.</summary>
        void ActivateRow(int index);
    }

    /// <summary>
    /// The visible body for screens whose data is a tree with no vanilla window of its own. Built on
    /// <see cref="FloatMenuTwin"/>'s mechanism and bound by the same safety contract: an
    /// ImmediateWindow cannot steal Escape or Enter, never attracts a ScopeForWindow attachment, is
    /// exempt from every ForeignWindowAbove guard and never reaches DialogInterceptionPatch, so the
    /// mounted scope's lifecycle, modality and Escape symmetry are what they were while it drew
    /// nothing.
    ///
    /// Rows are painted with the game's own widgets so the tree reads as vanilla, and a left-click
    /// routes through the mount's ActivateRow, the path Enter takes.
    /// </summary>
    internal static class TreeTwinWindow
    {
        private const int TwinWindowID = 74112611;

        /// <summary>Beneath <see cref="FloatMenuTwin"/>'s layer, so a row's context menu draws over the tree.</summary>
        private const WindowLayer TwinLayer = WindowLayer.Dialog;

        private const float RowHeight = TreeRowPainter.RowHeight;
        private const float ScrollBarWidth = 16f;
        private const float TitleGap = 8f;

        /// <summary>
        /// Every registered mount, not one slot: tree screens nest, so a single slot would be
        /// overwritten by the inner screen and left null when it popped, stranding the outer screen's
        /// window. Exactly one is drawn per frame — see <see cref="TopmostMount"/>.
        /// </summary>
        private static readonly List<ITreeTwinMount> mounts = new List<ITreeTwinMount>();

        /// <summary>The mount drawn last frame; a change hands the presentation state over.</summary>
        private static ITreeTwinMount activeMount;

        private static Rect windowRect;
        private static int sizedForWidth;
        private static int sizedForHeight;
        private static Vector2 scrollPosition;
        private static int lastFocusedIndex = -1;

        /// <summary>Whether the mounted scope holds the keyboard, resolved in <see cref="Request"/> for the draw pass that follows it.</summary>
        private static bool scopeIsTop;

        /// <summary>Each row's geometry from the last draw pass, recorded where the scroll offset and the window's own group are already applied.</summary>
        private static readonly List<PointerHitCandidate> rowHits = new List<PointerHitCandidate>();

        /// <summary>The mount row index behind each <see cref="rowHits"/> entry; scrolled-out rows are never recorded.</summary>
        private static readonly List<int> rowHitRows = new List<int>();

        internal static IReadOnlyList<PointerHitCandidate> RowHits => rowHits;

        internal static IReadOnlyList<int> RowHitRows => rowHitRows;

        /// <summary>The twin's own window rect: the positive ownership test for pointer routing, an ImmediateWindow being invisible to the covering-window guards.</summary>
        internal static Rect WindowRect => windowRect;

        /// <summary>
        /// The twin's live ImmediateWindow, or null when it is off the stack: the surface a mounted
        /// scope hands <see cref="PointerRouting.PointerOwnedBy"/>. WindowStack negates the id it is
        /// opened with, so the stored id is the negated constant.
        /// </summary>
        internal static Window LiveWindow
        {
            get
            {
                IList<Window> windows = Find.WindowStack != null ? Find.WindowStack.Windows : null;
                if (windows == null)
                {
                    return null;
                }
                for (int i = 0; i < windows.Count; i++)
                {
                    if (windows[i].ID == -TwinWindowID)
                    {
                        return windows[i];
                    }
                }
                return null;
            }
        }

        /// <summary>Whether <paramref name="scope"/> is the mount being drawn AND holds the keyboard — the gate on reading this frame's row geometry.</summary>
        internal static bool IsActiveMountScope(FocusScope scope)
        {
            return activeMount != null && scopeIsTop && ReferenceEquals(activeMount.Scope, scope);
        }

        // Cached so the immediate window's identity check does not thrash: a fresh lambda every
        // frame would look like a different delegate.
        private static readonly Action DrawContentsAction = DrawContents;

        internal static void Mount(ITreeTwinMount newMount)
        {
            if (newMount == null || IndexOf(newMount) >= 0)
            {
                return;
            }
            mounts.Add(newMount);
        }

        internal static void Unmount(ITreeTwinMount oldMount)
        {
            int index = IndexOf(oldMount);
            if (index < 0)
            {
                return;
            }
            mounts.RemoveAt(index);
        }

        private static int IndexOf(ITreeTwinMount candidate)
        {
            for (int i = 0; i < mounts.Count; i++)
            {
                if (ReferenceEquals(mounts[i], candidate))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// The mount whose scope sits highest on the live focus stack, or null when none is live.
        /// Walking downward means the innermost open tree screen wins and the ones beneath hand over
        /// rather than drawing stacked bodies.
        /// </summary>
        private static ITreeTwinMount TopmostMount()
        {
            if (mounts.Count == 0)
            {
                return null;
            }
            IReadOnlyList<FocusScope> live = FocusStack.ScopesBottomUp;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                for (int m = 0; m < mounts.Count; m++)
                {
                    if (ReferenceEquals(mounts[m].Scope, live[i]))
                    {
                        return mounts[m];
                    }
                }
            }
            return null;
        }

        private static void ResetPresentation()
        {
            scrollPosition = Vector2.zero;
            lastFocusedIndex = -1;
            scopeIsTop = false;
            rowHits.Clear();
            rowHitRows.Clear();
            windowRect = default(Rect);
            sizedForWidth = 0;
            sizedForHeight = 0;
        }

        internal static void Request()
        {
            try
            {
                // UIRootOnGUI runs during early startup, before the stack exists.
                if (Find.WindowStack == null) return;

                // Body and focus presentation are gated apart. The BODY draws while the chosen
                // mount's scope is on the live stack, so a row's context menu floats over a tree
                // that is still on screen. The PRESENTATION — selection highlight, ring, pointer
                // follow, and the row hits a click or Alt+Shift+J would land on — belongs to
                // whichever scope has the keyboard, so it is suppressed unless the chosen mount's
                // scope is top and nothing contends for the indicator or the hot control.
                ITreeTwinMount topmost = TopmostMount();
                if (!ReferenceEquals(topmost, activeMount))
                {
                    activeMount = topmost;
                    ResetPresentation();
                }
                if (activeMount == null)
                {
                    return;
                }
                scopeIsTop = ReferenceEquals(FocusStack.Top, activeMount.Scope);

                EnsureRect();
                Find.WindowStack.ImmediateWindow(TwinWindowID, windowRect, TwinLayer, DrawContentsAction,
                    doBackground: true, absorbInputAroundWindow: false);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Tree twin draw error", ex);
            }
        }

        /// <summary>Vanilla's mid-size dialog band, centered and held fixed until the screen resizes.</summary>
        private static void EnsureRect()
        {
            if (windowRect.width > 0f && sizedForWidth == UI.screenWidth && sizedForHeight == UI.screenHeight)
            {
                return;
            }
            float width = Mathf.Min(620f, UI.screenWidth * 0.6f);
            float height = Mathf.Min(700f, UI.screenHeight * 0.8f);
            windowRect = new Rect((UI.screenWidth - width) / 2f, (UI.screenHeight - height) / 2f, width, height);
            sizedForWidth = UI.screenWidth;
            sizedForHeight = UI.screenHeight;
        }

        private static void DrawContents()
        {
            rowHits.Clear();
            rowHitRows.Clear();
            if (activeMount == null) return;

            // ImmediateWindow calls its draw func with no inRect and a Margin of 0f, so the standard
            // window margin is ours to apply.
            Rect inner = windowRect.AtZero().ContractedBy(Window.StandardMargin);

            Text.Font = GameFont.Medium;
            float titleHeight = Text.LineHeight;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, titleHeight), activeMount.Title);
            Text.Font = GameFont.Small;
            float listY = inner.y + titleHeight + TitleGap;
            Widgets.DrawLineHorizontal(inner.x, listY, inner.width);
            listY += TitleGap;

            int rowCount = activeMount.RowCount;
            Rect outRect = new Rect(inner.x, listY, inner.width, inner.yMax - listY);
            Rect viewRect = new Rect(0f, 0f, outRect.width - ScrollBarWidth, rowCount * RowHeight);

            // -1 while another scope holds the keyboard: no focused row means no highlight, no ring,
            // no pointer follow and no scroll chasing a cursor the player is not driving.
            int focused = scopeIsTop ? activeMount.FocusedIndex : -1;
            if (focused != lastFocusedIndex)
            {
                TreeRowPainter.ClampScrollTo(ref scrollPosition, focused, outRect.height, viewRect.height);
                lastFocusedIndex = focused;
            }

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            Text.Anchor = TextAnchor.MiddleLeft;
            int pendingActivation = -1;
            for (int i = 0; i < rowCount; i++)
            {
                Rect rowRect = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                if (rowRect.yMax < scrollPosition.y || rowRect.y > scrollPosition.y + outRect.height)
                {
                    continue;
                }

                if (scopeIsTop)
                {
                    // Consumed before any widget below can take the hot control, so the click can
                    // only reach the mount's own activation path.
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                        && rowRect.Contains(Event.current.mousePosition))
                    {
                        pendingActivation = i;
                        Event.current.Use();
                    }
                    Widgets.ButtonInvisible(rowRect);
                    rowHits.Add(new PointerHitCandidate
                    {
                        Primary = GuiSpace.VisibleScreenRect(rowRect),
                        ClipDepth = GuiSpace.CurrentClip().Depth,
                    });
                    rowHitRows.Add(i);
                }

                // Mouse feedback only while the row can actually take a click.
                TreeRowPainter.DrawRow(activeMount.RowAt(i), rowRect, i, i == focused, i == focused, mouseover: scopeIsTop);
                if (i == focused)
                {
                    // The twin is the surface, so its own draw pass publishes the mirror-pushed
                    // scope's focused rect: a scope with no OwnedWindow cannot use
                    // FocusedContentRect.
                    UiPointerFollow.NotifyFocusedRect(GuiSpace.VisibleScreenRect(rowRect));
                }
            }
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.EndScrollView();

            if (pendingActivation >= 0)
            {
                activeMount.MoveFocusTo(pendingActivation);
                activeMount.ActivateRow(pendingActivation);
            }
        }
    }

    /// <summary>The tree row look shared by <see cref="TreeTwinWindow"/> and <see cref="MapOverviewWindow"/>.</summary>
    internal static class TreeRowPainter
    {
        internal const float RowHeight = 28f;
        private const float IndentPerLevel = 17f;
        private const float CaretSize = 18f;
        private const float ColumnGap = 4f;
        private const float RightColumnWidth = 90f;

        internal static void DrawRow(TreeTwinRow row, Rect rowRect, int index, bool selected, bool ringed, bool mouseover,
            Dictionary<string, string> truncateCache = null)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rowRect);
            }
            if (selected)
            {
                Widgets.DrawHighlightSelected(rowRect);
            }
            if (mouseover)
            {
                Widgets.DrawHighlightIfMouseover(rowRect);
            }

            float x = rowRect.x + row.Level * IndentPerLevel;
            if (row.IsExpandable)
            {
                // Listing_Tree's own caret pair, drawn: the row's click runs the same expand path.
                Rect caretRect = new Rect(x, rowRect.y + (RowHeight - CaretSize) / 2f, CaretSize, CaretSize);
                GUI.DrawTexture(caretRect, row.IsExpanded ? TexButton.Collapse : TexButton.Reveal);
            }
            x += CaretSize + ColumnGap;

            if (row.IconThing != null)
            {
                Widgets.ThingIcon(new Rect(x, rowRect.y, RowHeight, RowHeight), row.IconThing);
                x += RowHeight + ColumnGap;
            }
            else if (row.Icon != null)
            {
                Widgets.DefIcon(new Rect(x, rowRect.y, RowHeight, RowHeight), row.Icon, row.IconStuff);
                x += RowHeight + ColumnGap;
            }

            float labelWidth = rowRect.xMax - x - ColumnGap;
            if (!string.IsNullOrEmpty(row.RightText))
            {
                labelWidth -= RightColumnWidth;
                Rect rightRect = new Rect(rowRect.xMax - RightColumnWidth, rowRect.y, RightColumnWidth, RowHeight);
                Color previous = GUI.color;
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(rightRect, row.RightText);
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = previous;
            }
            if (labelWidth > 0f && row.Label != null)
            {
                Widgets.Label(new Rect(x, rowRect.y, labelWidth, RowHeight), row.Label.Truncate(labelWidth, truncateCache));
            }

            if (ringed)
            {
                FocusRing.Draw(rowRect);
            }
        }

        /// <summary>Keeps the focused row inside the view with one clamp per focus change.</summary>
        internal static void ClampScrollTo(ref Vector2 scrollPosition, int focused, float viewHeight, float contentHeight)
        {
            if (focused < 0)
            {
                return;
            }
            float top = focused * RowHeight;
            if (top < scrollPosition.y)
            {
                scrollPosition.y = top;
            }
            else if (top + RowHeight > scrollPosition.y + viewHeight)
            {
                scrollPosition.y = top + RowHeight - viewHeight;
            }
            scrollPosition.y = Mathf.Clamp(scrollPosition.y, 0f, Mathf.Max(0f, contentHeight - viewHeight));
        }
    }

    /// <summary>
    /// Re-requests the twin's ImmediateWindow every frame, on the same hook point and self-expiring
    /// contract as <see cref="FloatMenuTwinRequestPatch"/>.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot), "UIRootOnGUI")]
    internal static class TreeTwinWindowRequestPatch
    {
        [HarmonyPostfix]
        internal static void Postfix()
        {
            try
            {
                TreeTwinWindow.Request();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Tree twin request patch error", ex);
            }
        }
    }

    /// <summary>
    /// The read/drive side a <see cref="TreeRegionScope"/> exposes to
    /// <see cref="TreeTwinWindow"/>: the visible rows, the focused row, and the two cursor movements
    /// a mouse click needs. Everything routes through the scope's own model and activation path, so
    /// the twin adds no second way to move or activate a row.
    /// </summary>
    public abstract partial class TreeRegionScope
    {
        private TreePanel TwinPanel
        {
            get { return PanelFor(TreeRegionIndex); }
        }

        internal IReadOnlyList<InspectionTreeItem> TwinRows
        {
            get
            {
                TreePanel panel = TwinPanel;
                return panel != null ? panel.Tree.Visible : null;
            }
        }

        /// <summary>Bumps whenever the visible rows are rebuilt — the twin's row-cache key.</summary>
        internal int TwinRowVersion
        {
            get
            {
                TreePanel panel = TwinPanel;
                return panel != null ? panel.Tree.Version : 0;
            }
        }

        internal int TwinFocusedRow
        {
            get
            {
                TreePanel panel = TwinPanel;
                if (panel == null || Model.RegionIndex != TreeRegionIndex)
                {
                    return -1;
                }
                ListModel region = Model.CurrentRegion;
                if (region == null || region.IsEmpty)
                {
                    return -1;
                }
                int treeIndex = region.Index - PrefixRowCountFor(TreeRegionIndex);
                return treeIndex >= 0 && treeIndex < panel.Tree.Count ? treeIndex : -1;
            }
        }

        internal void TwinMoveFocusTo(int treeIndex)
        {
            TreePanel panel = TwinPanel;
            if (panel == null || treeIndex < 0 || treeIndex >= panel.Tree.Count)
            {
                return;
            }
            if (Model.RegionIndex != TreeRegionIndex)
            {
                MoveResult moved = Model.MoveToRegion(TreeRegionIndex);
                if (moved.Changed)
                {
                    OnRegionChanged(moved);
                }
            }
            ListModel region = Model.CurrentRegion;
            if (region == null)
            {
                return;
            }
            region.MoveTo(treeIndex + PrefixRowCountFor(TreeRegionIndex));
            NotifyCursorSettled();
        }

        internal void TwinActivateRow(int treeIndex)
        {
            TwinMoveFocusTo(treeIndex);
            ActivateCurrent();
        }
    }

    /// <summary>
    /// The shared <see cref="ITreeTwinMount"/> for a <see cref="TreeRegionScope"/>: rows come from
    /// the scope's visible tree, cached until the tree's version says they were rebuilt, so a draw
    /// pass allocates nothing. A screen with icons or counts overrides <see cref="Decorate"/>.
    /// </summary>
    internal class TreeRegionTwinMount : ITreeTwinMount
    {
        private readonly TreeRegionScope scope;
        private readonly Func<string> title;
        private readonly List<TreeTwinRow> rows = new List<TreeTwinRow>();
        private int rowsVersion = -1;

        internal TreeRegionTwinMount(TreeRegionScope scope, Func<string> title)
        {
            this.scope = scope;
            this.title = title;
        }

        public FocusScope Scope
        {
            get { return scope; }
        }

        public string Title
        {
            get { return title(); }
        }

        public int RowCount
        {
            get
            {
                EnsureRows();
                return rows.Count;
            }
        }

        public TreeTwinRow RowAt(int index)
        {
            EnsureRows();
            return index >= 0 && index < rows.Count ? rows[index] : default(TreeTwinRow);
        }

        public int FocusedIndex
        {
            get { return scope.TwinFocusedRow; }
        }

        public void MoveFocusTo(int index)
        {
            scope.TwinMoveFocusTo(index);
        }

        public void ActivateRow(int index)
        {
            scope.TwinActivateRow(index);
        }

        /// <summary>Adds whatever this screen's node data carries beyond the shared label/indent/expansion.</summary>
        protected virtual void Decorate(InspectionTreeItem item, ref TreeTwinRow row)
        {
        }

        private void EnsureRows()
        {
            int version = scope.TwinRowVersion;
            if (version == rowsVersion)
            {
                return;
            }
            rowsVersion = version;
            rows.Clear();

            IReadOnlyList<InspectionTreeItem> visible = scope.TwinRows;
            if (visible == null)
            {
                return;
            }
            for (int i = 0; i < visible.Count; i++)
            {
                InspectionTreeItem item = visible[i];
                TreeTwinRow row = new TreeTwinRow
                {
                    Label = item.Label,
                    Level = Mathf.Max(0, item.IndentLevel),
                    IsExpandable = item.IsExpandable,
                    IsExpanded = item.IsExpanded,
                };
                Decorate(item, ref row);
                rows.Add(row);
            }
        }
    }
}
