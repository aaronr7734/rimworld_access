using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Records the visible rows of the dev def editor's field tree so
    /// <see cref="DevDefEditorScope"/> can ring the focused one.
    /// <c>EditWindow_DefEditor.DoWindowContents</c> draws the whole tree through one
    /// <c>Listing_TreeDefs</c> (decompiled LudeonTK/EditWindow_DefEditor.cs:67-71), and every node
    /// row reaches the screen through that listing's <c>NodeLabelLeft</c> (decompiled
    /// Verse/Listing_TreeDefs.cs:107) — which carries the <c>TreeNode_Editor</c> itself, so rows
    /// are keyed by node REFERENCE and the scope's own row resolves to its rect directly.
    ///
    /// Reference identity keeps the ring correct: vanilla's own expansion state (<c>openMask</c>)
    /// is never written by the keyboard, so the two row sequences diverge. A row vanilla did not
    /// draw this pass has no entry, and no ring is drawn.
    /// </summary>
    internal static class DefEditorRowCapture
    {
        internal static readonly RowDrawCapture Rows = new RowDrawCapture();

        /// <summary>
        /// Read once per <c>Listing_TreeDefs.NodeLabelLeft</c> call, so it stays a plain field and
        /// is the tap's first statement.
        /// </summary>
        internal static bool Recording;

        private static readonly AccessTools.FieldRef<Listing, float> CurYRef =
            AccessTools.FieldRefAccess<Listing, float>("curY");

        internal static DevDefEditorScope SliderScope;

        // Set on every event type, unlike Recording: a type-in mod's hover test runs on Layout too.
        private static TreeNode_Editor focusedValueNode;
        private static bool drawingFocusedSlider;
        private static TreeNode_Editor drawnNode;
        private static int drawnFrame = -1;

        internal static bool DrawingFocusedSlider
        {
            get { return drawingFocusedSlider; }
        }

        /// <summary>Whether vanilla drew <paramref name="node"/>'s value widget as the focused row within the last frames.</summary>
        internal static bool DrewRecently(TreeNode_Editor node)
        {
            return drawnFrame >= 0 && Time.frameCount - drawnFrame <= 2 && ReferenceEquals(node, drawnNode);
        }

        internal static void EnterValueWidget(TreeNode_Editor node)
        {
            if (focusedValueNode == null || !ReferenceEquals(node, focusedValueNode))
            {
                return;
            }
            drawingFocusedSlider = true;
            drawnNode = node;
            drawnFrame = Time.frameCount;
        }

        internal static void ExitValueWidget()
        {
            drawingFocusedSlider = false;
        }

        internal static void BeginPass(Window window)
        {
            try
            {
                DevDefEditorScope owner = SliderScope;
                focusedValueNode = owner != null && owner.Owns(window) ? owner.FocusedValueNode : null;
                Event ev = Event.current;
                if (ev == null || ev.type == EventType.Layout)
                {
                    return;
                }
                var scope = FocusStack.Top as DevDefEditorScope;
                if (scope == null || !scope.Owns(window))
                {
                    return;
                }
                Recording = true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Def editor row capture error", ex);
            }
        }

        /// <summary>Arrives from a finalizer, so a def whose editor throws mid-tree still disarms the pass.</summary>
        internal static void EndPass()
        {
            Recording = false;
            focusedValueNode = null;
            drawingFocusedSlider = false;
        }

        /// <summary>
        /// Rebuilds the row band the label was laid out in — the rect <c>LabelLeft</c> hands
        /// <c>Widgets.DrawHighlightIfMouseover</c>, before the label-column narrowing that follows
        /// (decompiled Verse/Listing_Tree.cs:38-40, with XAtIndentLevel at :33; NodeLabelLeft
        /// passes no leftOffset). The listing's <c>curY</c> is still the row's top here: neither
        /// method advances it, <c>Listing_TreeDefs.Node</c> does that afterwards through EndLine.
        /// </summary>
        internal static void RecordRow(Listing_TreeDefs listing, TreeNode_Editor node, int indentLevel)
        {
            try
            {
                var rect = new Rect(0f, CurYRef(listing), listing.ColumnWidth, listing.lineHeight);
                rect.xMin = indentLevel * listing.nestIndentWidth + 18f;
                Rows.Record(node, rect);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Def editor row capture error", ex);
            }
        }
    }

    /// <summary>Brackets one def-editor pass: every row the listing draws inside it belongs to that pass.</summary>
    [HarmonyPatch]
    internal static class DefEditorWindowBracketPatch
    {
        /// <summary>EditWindow_DefEditor is internal to Assembly-CSharp, so the target is resolved by name (the scope holds the type).</summary>
        static bool Prepare()
        {
            return DevDefEditorScope.WindowType != null;
        }

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(DevDefEditorScope.WindowType, "DoWindowContents",
                new Type[] { typeof(Rect) });
        }

        [HarmonyPrefix]
        public static void Prefix(Window __instance)
        {
            DefEditorRowCapture.BeginPass(__instance);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            DefEditorRowCapture.EndPass();
        }
    }

    [HarmonyPatch(typeof(Listing_TreeDefs), "ValueEditWidgetRight")]
    internal static class DefEditorValueWidgetPatch
    {
        [HarmonyPrefix]
        public static void Prefix(TreeNode_Editor node)
        {
            DefEditorRowCapture.EnterValueWidget(node);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            DefEditorRowCapture.ExitValueWidget();
        }
    }

    /// <summary>
    /// One call per drawn node row, carrying the node itself. Patched on the declaring type, which
    /// is total coverage here: the method is non-virtual, so no subclass can route around it.
    /// </summary>
    [HarmonyPatch(typeof(Listing_TreeDefs), "NodeLabelLeft")]
    internal static class DefEditorRowLabelPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Listing_TreeDefs __instance, TreeNode_Editor node, int indentLevel)
        {
            if (!DefEditorRowCapture.Recording)
            {
                return;
            }
            DefEditorRowCapture.RecordRow(__instance, node, indentLevel);
        }
    }
}
