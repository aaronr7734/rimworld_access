using System;
using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Cross-rebuild <see cref="InspectionTreeItem"/> node identity, shared by every
    /// <see cref="TreeRegionScope"/>-family tree that rebuilds its root from scratch: every expanded
    /// node that still exists re-expands, and the cursor lands on the same LOGICAL node (matched by
    /// Data reference, else by label path) rather than the same flat index.
    /// </summary>
    public static class TreeStatePreserve
    {
        public enum CursorRule
        {
            /// <summary>Nodes match by Data, label or position; a vanished cursor lands on its nearest surviving ancestor, expanded into view.</summary>
            NearestAncestor,

            /// <summary>Nodes match by Data alone; a node rebuilt childless stays collapsed. A vanished cursor follows its Data to a visible
            /// row at its old depth, else takes the row at its old place in the deepest surviving list, or that list's owner once empty.</summary>
            FollowData,
        }

        public static int SetRootPreservingState(
            TreeModel<InspectionTreeItem> tree,
            InspectionTreeItem newRoot,
            Action<InspectionTreeItem> applyRoot,
            int currentVisibleIndex,
            Action<InspectionTreeItem> onBeforeExpand = null)
        {
            return SetRootPreservingState(tree, newRoot, applyRoot, currentVisibleIndex, onBeforeExpand,
                CursorRule.NearestAncestor, out InspectionTreeItem _);
        }

        /// <summary>
        /// Replaces <paramref name="tree"/>'s root while preserving expansion and the cursor's logical
        /// node. Returns the restored cursor's flat visible index, or -1 when nothing visible survives
        /// (callers keep their numeric clamp). <paramref name="applyRoot"/> sets the new root; this
        /// method owns only the identity walk. <paramref name="onBeforeExpand"/> follows
        /// <see cref="TreeModel{T}.OnBeforeExpand"/>'s contract; a lazily populated tree needs it.
        /// <paramref name="formerParent"/>, FollowData only, is the rebuilt twin of the old cursor's parent.
        /// </summary>
        public static int SetRootPreservingState(
            TreeModel<InspectionTreeItem> tree,
            InspectionTreeItem newRoot,
            Action<InspectionTreeItem> applyRoot,
            int currentVisibleIndex,
            Action<InspectionTreeItem> onBeforeExpand,
            CursorRule rule,
            out InspectionTreeItem formerParent)
        {
            formerParent = null;
            bool dataOnly = rule == CursorRule.FollowData;
            List<PathSegment> cursorPath = null;
            var expandedPaths = new List<List<PathSegment>>();
            IReadOnlyList<InspectionTreeItem> visible = tree.Visible;
            InspectionTreeItem cursorNode = currentVisibleIndex >= 0 && currentVisibleIndex < visible.Count
                ? visible[currentVisibleIndex]
                : null;
            if (tree.Root != null)
            {
                if (cursorNode != null)
                {
                    cursorPath = new List<PathSegment>();
                    if (!TryBuildPath(tree.Root, cursorNode, cursorPath))
                    {
                        cursorPath = null;
                    }
                }
                CollectExpandedPaths(tree.Root, new List<PathSegment>(), expandedPaths);
            }

            applyRoot(newRoot);
            if (newRoot == null)
            {
                return -1;
            }

            foreach (List<PathSegment> path in expandedPaths)
            {
                InspectionTreeItem node = ResolvePath(newRoot, path, path.Count, false, out bool _, onBeforeExpand, dataOnly);
                if (node != null)
                {
                    // The node itself is about to be marked expanded, so populate its children first:
                    // ResolvePath did that only for nodes it walked THROUGH.
                    if (onBeforeExpand != null && node.IsExpandable && node.Children.Count == 0)
                    {
                        onBeforeExpand(node);
                    }
                    if (!dataOnly || node.Children.Count > 0)
                    {
                        node.IsExpanded = true;
                    }
                }
            }
            tree.Reflatten();

            if (dataOnly)
            {
                return cursorPath != null ? FollowData(tree, newRoot, cursorPath, cursorNode, onBeforeExpand, out formerParent) : -1;
            }

            // A row that activated something can be gone entirely: land on its closest surviving ancestor.
            for (int depth = cursorPath != null ? cursorPath.Count : 0; depth > 0; depth--)
            {
                InspectionTreeItem node = ResolvePath(newRoot, cursorPath, depth, true, out bool expandedAny, onBeforeExpand);
                if (expandedAny)
                {
                    tree.Reflatten();
                }
                if (node == null)
                {
                    continue;
                }
                int index = VisibleIndexFor(tree, node);
                if (index >= 0)
                {
                    tree.SetSelectedIndex(index);
                    return index;
                }
            }
            return -1;
        }

        private static int FollowData(TreeModel<InspectionTreeItem> tree, InspectionTreeItem newRoot, List<PathSegment> cursorPath,
            InspectionTreeItem oldCursor, Action<InspectionTreeItem> onBeforeExpand, out InspectionTreeItem formerParent)
        {
            int depth = cursorPath.Count;
            formerParent = depth > 1 ? ResolvePath(newRoot, cursorPath, depth - 1, false, out bool _, onBeforeExpand, true) : newRoot;
            InspectionTreeItem same = formerParent != null ? MatchChild(formerParent, cursorPath[depth - 1], true) : null;
            int index = same != null ? VisibleIndexFor(tree, same) : -1;
            if (index < 0)
            {
                object data = cursorPath[depth - 1].Data;
                IReadOnlyList<InspectionTreeItem> visible = tree.Visible;
                for (int i = 0; data != null && i < visible.Count; i++)
                {
                    if (visible[i].IndentLevel == oldCursor.IndentLevel && data.Equals(visible[i].Data))
                    {
                        index = i;
                        break;
                    }
                }
            }
            for (int level = depth - 1; index < 0 && level >= 0; level--)
            {
                InspectionTreeItem list = level > 0 ? ResolvePath(newRoot, cursorPath, level, false, out bool _, onBeforeExpand, true) : newRoot;
                if (list == null)
                {
                    continue;
                }
                List<InspectionTreeItem> children = list.Children;
                InspectionTreeItem landing = children.Count == 0
                    ? list
                    : children[Math.Min(Math.Max(cursorPath[level].Index, 0), children.Count - 1)];
                index = VisibleIndexFor(tree, landing);
                break;
            }
            if (index >= 0)
            {
                tree.SetSelectedIndex(index);
            }
            return index;
        }

        /// <summary>
        /// One level of a node's identity: the row's <see cref="InspectionTreeItem.Data"/> (rebuilds
        /// commonly re-wrap the same live objects, so Data survives a renamed label) plus its label
        /// paired with its occurrence index among same-label siblings, which two "Rituals" siblings
        /// would otherwise collide on. Internal rather than private because
        /// <see cref="BranchMemo"/>'s fields carry lists of this type.
        /// </summary>
        internal struct PathSegment
        {
            public readonly object Data;
            public readonly string LabelKey;

            /// <summary>Position among siblings, with the sibling count it was recorded against — the positional fallback's in-place-relabel signature (see <see cref="MatchChild"/>).</summary>
            public readonly int Index;
            public readonly int SiblingCount;

            public PathSegment(object data, string labelKey, int index, int siblingCount)
            {
                Data = data;
                LabelKey = labelKey;
                Index = index;
                SiblingCount = siblingCount;
            }
        }

        private static PathSegment SegmentFor(List<InspectionTreeItem> siblings, int index)
        {
            InspectionTreeItem node = siblings[index];
            string label = node.Label ?? "";
            int occurrence = 0;
            for (int i = 0; i < index; i++)
            {
                if (string.Equals(siblings[i].Label ?? "", label, StringComparison.Ordinal))
                {
                    occurrence++;
                }
            }
            return new PathSegment(node.Data, label + "#" + occurrence, index, siblings.Count);
        }

        /// <summary>
        /// Records <paramref name="target"/>'s path from <paramref name="parent"/> downwards. Walks
        /// DOWN rather than up <see cref="InspectionTreeItem.Parent"/>: an occurrence index needs the
        /// real sibling list, and these trees carry rows whose Parent was never set.
        /// </summary>
        private static bool TryBuildPath(InspectionTreeItem parent, InspectionTreeItem target, List<PathSegment> path)
        {
            List<InspectionTreeItem> children = parent.Children;
            for (int i = 0; i < children.Count; i++)
            {
                path.Add(SegmentFor(children, i));
                if (ReferenceEquals(children[i], target) || TryBuildPath(children[i], target, path))
                {
                    return true;
                }
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }

        /// <summary>Every expanded node's path, INCLUDING those inside collapsed branches: the old tree kept their flags, so re-expanding an outer branch must reveal the same shape.</summary>
        private static void CollectExpandedPaths(InspectionTreeItem parent, List<PathSegment> stack, List<List<PathSegment>> into)
        {
            List<InspectionTreeItem> children = parent.Children;
            for (int i = 0; i < children.Count; i++)
            {
                stack.Add(SegmentFor(children, i));
                if (children[i].IsExpanded)
                {
                    into.Add(new List<PathSegment>(stack));
                }
                CollectExpandedPaths(children[i], stack, into);
                stack.RemoveAt(stack.Count - 1);
            }
        }

        /// <summary>
        /// Walks the first <paramref name="depth"/> segments of <paramref name="path"/> down from
        /// <paramref name="root"/>, or null when a level has no match. With
        /// <paramref name="expandAncestors"/> it also expands every node it passes THROUGH (never the
        /// landing node) and reports through <paramref name="expandedAny"/> whether the caller owes a
        /// reflatten. <paramref name="onBeforeExpand"/> runs before a lazily-populated node is
        /// descended into or expanded; without it a lazy section's children never exist for
        /// <see cref="MatchChild"/> to search and the walk dies at the first such node.
        /// </summary>
        private static InspectionTreeItem ResolvePath(InspectionTreeItem root, List<PathSegment> path, int depth, bool expandAncestors, out bool expandedAny, Action<InspectionTreeItem> onBeforeExpand, bool dataOnly = false)
        {
            expandedAny = false;
            InspectionTreeItem node = root;
            for (int level = 0; level < depth; level++)
            {
                if (onBeforeExpand != null && node.IsExpandable && node.Children.Count == 0)
                {
                    onBeforeExpand(node);
                }
                InspectionTreeItem child = MatchChild(node, path[level], dataOnly);
                if (child == null)
                {
                    return null;
                }
                if (expandAncestors && level < depth - 1 && !child.IsExpanded)
                {
                    if (onBeforeExpand != null && child.IsExpandable && child.Children.Count == 0)
                    {
                        onBeforeExpand(child);
                    }
                    child.IsExpanded = true;
                    expandedAny = true;
                }
                node = child;
            }
            return node;
        }

        /// <summary>
        /// The child of <paramref name="parent"/> matching <paramref name="segment"/>: Data and label
        /// agreeing wins outright; then Data alone (a renamed row), but only when exactly one child
        /// carries it, since a tree whose rows all share one payload would otherwise resolve every
        /// renamed row to the first sibling; then label alone (a row whose Data is rebuilt fresh); and
        /// finally the same POSITION when the sibling count is unchanged, so an in-place relabel keeps
        /// the cursor on the row the player just acted on rather than throwing it to an ancestor.
        /// </summary>
        private static InspectionTreeItem MatchChild(InspectionTreeItem parent, PathSegment segment, bool dataOnly = false)
        {
            List<InspectionTreeItem> children = parent.Children;
            if (dataOnly)
            {
                for (int i = 0; segment.Data != null && i < children.Count; i++)
                {
                    if (segment.Data.Equals(children[i].Data))
                    {
                        return children[i];
                    }
                }
                return null;
            }
            InspectionTreeItem dataMatch = null;
            int dataMatchCount = 0;
            InspectionTreeItem labelMatch = null;
            for (int i = 0; i < children.Count; i++)
            {
                PathSegment candidate = SegmentFor(children, i);
                bool sameData = segment.Data != null && candidate.Data != null
                    && (ReferenceEquals(segment.Data, candidate.Data) || segment.Data.Equals(candidate.Data));
                bool sameLabel = string.Equals(segment.LabelKey, candidate.LabelKey, StringComparison.Ordinal);
                if (sameData && sameLabel)
                {
                    return children[i];
                }
                if (sameData)
                {
                    dataMatchCount++;
                    if (dataMatch == null)
                    {
                        dataMatch = children[i];
                    }
                }
                if (sameLabel && labelMatch == null)
                {
                    labelMatch = children[i];
                }
            }
            if (dataMatchCount == 1)
            {
                return dataMatch;
            }
            if (labelMatch != null)
            {
                return labelMatch;
            }
            if (children.Count == segment.SiblingCount && segment.Index >= 0 && segment.Index < children.Count)
            {
                return children[segment.Index];
            }
            return null;
        }

        /// <summary>
        /// Captured cursor/expansion state for a BRANCH-scoped rebuild (<see cref="CaptureBranch"/>/
        /// <see cref="RestoreBranch"/>) — the same <see cref="PathSegment"/> machinery as
        /// <see cref="SetRootPreservingState"/> above, but relative to an arbitrary branch root
        /// rather than the tree's real <c>Root</c>, for a caller that rebuilds one category's
        /// children in place (<c>InspectionTreeBuilder.RebuildAdapterCategory</c>) rather than
        /// swapping the whole tree. Both fields are relative to the branch root the memo was
        /// captured against; replaying them against a different root is meaningless.
        /// </summary>
        public sealed class BranchMemo
        {
            // Internal, not private: a private field of a NESTED class is unreachable from the
            // enclosing type's own methods. Still opaque elsewhere, since BranchMemo is only handed
            // out by CaptureBranch and only consumed by RestoreBranch.
            internal List<PathSegment> cursorPath;
            internal List<List<PathSegment>> expandedPaths;

            /// <summary>Whether the captured cursor sat inside the branch — when true and nothing
            /// on its path survives, the branch root itself is the nearest surviving ancestor.</summary>
            public bool CursorWasInBranch => cursorPath != null;
        }

        /// <summary>
        /// Records <paramref name="cursorNode"/>'s path and every expanded descendant's path, both
        /// relative to <paramref name="branchRoot"/>, before the caller rebuilds its children in
        /// place. The cursor may be null or outside the branch entirely; either way the memo then
        /// restores expansion only.
        /// </summary>
        public static BranchMemo CaptureBranch(InspectionTreeItem branchRoot, InspectionTreeItem cursorNode)
        {
            var memo = new BranchMemo { expandedPaths = new List<List<PathSegment>>() };
            if (branchRoot == null)
            {
                return memo;
            }
            if (cursorNode != null)
            {
                var path = new List<PathSegment>();
                if (TryBuildPath(branchRoot, cursorNode, path))
                {
                    memo.cursorPath = path;
                }
            }
            CollectExpandedPaths(branchRoot, new List<PathSegment>(), memo.expandedPaths);
            return memo;
        }

        /// <summary>
        /// After the caller rebuilt <paramref name="branchRoot"/>'s children in place, re-expands every
        /// surviving expanded descendant and resolves the cursor path to the nearest surviving node.
        /// Returns the landing node, or null when the memo had no cursor path or nothing survived.
        /// Does NOT reflatten or touch region/selection state — there is no
        /// <see cref="TreeModel{T}"/> reference at branch scope, so the caller owns both.
        /// </summary>
        public static InspectionTreeItem RestoreBranch(InspectionTreeItem branchRoot, BranchMemo memo, Action<InspectionTreeItem> onBeforeExpand)
        {
            if (branchRoot == null || memo == null)
            {
                return null;
            }

            foreach (List<PathSegment> path in memo.expandedPaths)
            {
                InspectionTreeItem node = ResolvePath(branchRoot, path, path.Count, false, out bool _, onBeforeExpand);
                if (node != null)
                {
                    if (onBeforeExpand != null && node.IsExpandable && node.Children.Count == 0)
                    {
                        onBeforeExpand(node);
                    }
                    node.IsExpanded = true;
                }
            }

            if (memo.cursorPath == null)
            {
                return null;
            }
            for (int depth = memo.cursorPath.Count; depth > 0; depth--)
            {
                InspectionTreeItem node = ResolvePath(branchRoot, memo.cursorPath, depth, true, out bool _, onBeforeExpand);
                if (node != null)
                {
                    return node;
                }
            }
            return null;
        }

        /// <summary>
        /// <paramref name="node"/>'s index in the flattened list, descending to its first visible
        /// descendant when the node itself is invisible — the normal case in submenu mode, where an
        /// expanded node's children take its place.
        /// </summary>
        private static int VisibleIndexFor(TreeModel<InspectionTreeItem> tree, InspectionTreeItem node)
        {
            InspectionTreeItem landing = node;
            int index = tree.IndexOf(landing);
            while (index < 0 && landing.IsExpanded && landing.Children.Count > 0)
            {
                landing = landing.Children[0];
                index = tree.IndexOf(landing);
            }
            return index;
        }
    }
}
