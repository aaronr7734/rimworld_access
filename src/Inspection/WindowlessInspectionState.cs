using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Data facade behind the windowless inspection panel: which objects are at the inspected
    /// cell, the parent object Escape walks back to, and the map selection to restore on close.
    /// It owns the tree's content and lifecycle only — the cursor, typeahead, expansion, and
    /// every announcement live in <see cref="InspectionScope"/>, which the refresh and
    /// re-announce methods here forward to.
    /// </summary>
    public static class WindowlessInspectionState
    {
        public static bool IsActive { get; private set; } = false;

        private static IntVec3 inspectionPosition;
        private static object parentObject = null;
        private static List<object> previousSelection = new List<object>();
        private static bool openedAtCell;

        /// <summary>The tree the scope is presenting, kept here so a re-pushed scope can re-seed.</summary>
        private static InspectionTreeItem treeRoot;

        /// <summary>The presented tree's root, for callers rebuilding a branch in place. Null when no tree is open.</summary>
        internal static InspectionTreeItem CurrentTreeRoot => treeRoot;

        /// <summary>Set when a tree was built before any scope existed to receive it.</summary>
        private static InspectionTreeOpening? pendingOpening;

        private static Type pendingLandingTabType;

        /// <summary>
        /// Hands a freshly built tree to the scope, which presents it and speaks the opening
        /// announcement. The pending fallback covers only the case of a tree built before the
        /// mirror created its scope; that tree reaches the screen silently on the first push.
        /// </summary>
        private static void PresentTree(InspectionTreeItem root, InspectionTreeOpening opening)
        {
            treeRoot = root;
            InspectionScope live = InspectionScope.Live;
            if (live == null)
            {
                pendingOpening = opening;
                return;
            }
            pendingOpening = null;
            live.OpenTree(root, opening);
        }

        /// <summary>Called from the scope's OnPush: adopt the current tree if it has not already.</summary>
        internal static void NotifyScopeAttached(InspectionScope scope)
        {
            if (!IsActive || treeRoot == null)
            {
                return;
            }
            if (pendingOpening.HasValue)
            {
                InspectionTreeOpening opening = pendingOpening.Value;
                pendingOpening = null;
                scope.OpenTree(treeRoot, opening);
                return;
            }
            scope.EnsureTree(treeRoot);
        }

        private static void ClearTree()
        {
            treeRoot = null;
            pendingOpening = null;
            pendingLandingTabType = null;
            InspectionScope live = InspectionScope.Live;
            if (live != null)
            {
                live.ClearTree();
            }
        }

        /// <summary>Opens the inspection panel on the objects at <paramref name="position"/>.</summary>
        public static void Open(IntVec3 position)
        {
            try
            {
                inspectionPosition = position;

                // Fogged tiles surface nothing beyond "Undiscovered", as vanilla's
                // MouseoverReadout does.
                if (Find.CurrentMap != null && position.Fogged(Find.CurrentMap))
                {
                    TolkHelper.Speak("Undiscovered".Loc());
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }

                var objects = BuildObjectList();

                if (objects.Count == 0)
                {
                    TolkHelper.Speak("RimWorldAccess.Inspection.Panel.NoItems".Loc());
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }

                previousSelection.Clear();
                previousSelection.AddRange(Find.Selector.SelectedObjects.Cast<object>());

                // Inspect-tab visibility reads Find.Selector.SingleSelectedThing, so select first.
                if (objects.Count > 0 && objects[0] is Thing thingToSelect)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(thingToSelect, playSound: false, forceDesignatorDeselect: false);
                }

                var rootItem = InspectionTreeBuilder.BuildTree(objects);

                IsActive = true;
                openedAtCell = true;
                SoundDefOf.TabOpen.PlayOneShotOnCamera();

                PresentTree(rootItem, objects.Count == 1
                    ? InspectionTreeOpening.SingleObject
                    : InspectionTreeOpening.AnnounceRow);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error opening inspection menu: {ex}");
                Close();
            }
        }

        /// <summary>
        /// Opens the panel on one object. <paramref name="parent"/>, when given, is the object
        /// Escape walks back to instead of closing.
        /// </summary>
        public static void OpenForObject(object obj, object parent = null)
        {
            try
            {
                if (obj == null)
                {
                    TolkHelper.Speak("RimWorldAccess.Inspection.Panel.NoObject".Loc());
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }

                if (obj is Thing thing)
                {
                    inspectionPosition = thing.Position;
                }
                else
                {
                    inspectionPosition = IntVec3.Invalid;
                }

                parentObject = parent;

                previousSelection.Clear();
                previousSelection.AddRange(Find.Selector.SelectedObjects.Cast<object>());

                // Inspect-tab visibility reads Find.Selector.SingleSelectedThing, so select first.
                if (obj is Thing thingToSelect)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(thingToSelect, playSound: false, forceDesignatorDeselect: false);
                }

                var objects = new List<object> { obj };
                var rootItem = InspectionTreeBuilder.BuildTree(objects);

                IsActive = true;
                openedAtCell = false;
                SoundDefOf.TabOpen.PlayOneShotOnCamera();

                // Only this path falls through to the row announcement when the lazy load yields
                // no children; the other two stop at the bare object name.
                PresentTree(rootItem, InspectionTreeOpening.SingleObjectAnnounceIfEmpty);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error opening inspection menu for object: {ex}");
                Close();
            }
        }

        /// <summary>Opens the panel on one object with the cursor on the category presenting
        /// <paramref name="tabType"/>. Categories exist only once the object row expands, so the
        /// landing rides the opening pass rather than following it: one announcement, not two.</summary>
        public static void OpenForObjectOnTab(object obj, Type tabType)
        {
            pendingLandingTabType = tabType;
            OpenForObject(obj);
        }

        internal static Type ConsumeLandingTabType()
        {
            Type type = pendingLandingTabType;
            pendingLandingTabType = null;
            return type;
        }

        /// <summary>The Category row presenting the given inspect-tab type, at any depth.</summary>
        internal static InspectionTreeItem FindCategoryForTabType(InspectionTreeItem node, Type tabType)
        {
            if (node == null || tabType == null)
            {
                return null;
            }
            if (node.Type == InspectionTreeItem.ItemType.Category
                && node.SourceTab != null
                && tabType.IsInstanceOfType(node.SourceTab))
            {
                return node;
            }
            foreach (InspectionTreeItem child in node.Children)
            {
                InspectionTreeItem found = FindCategoryForTabType(child, tabType);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        /// <summary>Moves an already-open panel's cursor onto that tab's category; false when the
        /// tree does not carry one, leaving the caller to reopen.</summary>
        internal static bool TryLandOnTab(Type tabType)
        {
            InspectionScope live = InspectionScope.Live;
            InspectionTreeItem node = FindCategoryForTabType(treeRoot, tabType);
            if (live == null || node == null)
            {
                return false;
            }
            live.RefreshTreeRowsLandingOn(node);
            live.AnnounceCurrentRow();
            return true;
        }

        /// <summary>
        /// Opens the panel on one object in the given mode: Full allows actions, ReadOnly does
        /// not. Does not save or restore the map selection.
        /// </summary>
        public static void OpenForObject(object obj, object parent, InspectionMode mode)
        {
            try
            {
                if (obj == null)
                {
                    TolkHelper.Speak("RimWorldAccess.Inspection.Panel.NoObject".Loc());
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }

                if (obj is Thing thing)
                {
                    inspectionPosition = thing.Position;
                }
                else
                {
                    inspectionPosition = IntVec3.Invalid;
                }

                parentObject = parent;

                var objects = new List<object> { obj };
                var rootItem = InspectionTreeBuilder.BuildTree(objects, mode);

                IsActive = true;
                openedAtCell = false;
                SoundDefOf.TabOpen.PlayOneShotOnCamera();

                PresentTree(rootItem, InspectionTreeOpening.SingleObject);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error opening inspection menu for object: {ex}");
                Close();
            }
        }

        /// <summary>Closes the panel and restores the map selection it replaced.</summary>
        public static void Close()
        {
            IsActive = false;
            parentObject = null;
            ClearTree();

            if (previousSelection.Count > 0)
            {
                // Snapshot first: a mod's Select postfix can re-enter Open and rewrite the list.
                object[] restore = previousSelection.ToArray();
                previousSelection.Clear();
                Find.Selector.ClearSelection();
                foreach (var obj in restore)
                {
                    if (obj is Thing thing && thing.Spawned)
                    {
                        Find.Selector.Select(thing, playSound: false, forceDesignatorDeselect: false);
                    }
                }
            }
        }

        /// <summary>Reopens on the thing that replaced the panel's object in place, keeping the
        /// walk-back parent and the selection Escape restores.</summary>
        internal static void RehomeOnReplacement(Thing replacement)
        {
            object parent = parentObject;
            object[] restore = previousSelection.ToArray();
            if (openedAtCell)
            {
                Open(inspectionPosition);
            }
            else
            {
                OpenForObject(replacement, parent);
            }
            parentObject = parent;
            previousSelection.Clear();
            previousSelection.AddRange(restore);
        }

        /// <summary>
        /// Clears inspection state at a game session boundary. Deliberately not
        /// <see cref="Close"/>: that reselects the previous selection, whose Things may belong
        /// to a map that no longer exists, so this never touches Find.Selector.
        /// </summary>
        public static void Reset()
        {
            IsActive = false;
            parentObject = null;
            previousSelection.Clear();
            ClearTree();
        }

        /// <summary>
        /// Rebuilds the tree after a state-changing action. With a live scope the swap preserves
        /// expansion and lands the cursor on the same node, else its nearest surviving ancestor.
        /// </summary>
        public static void RebuildTree()
        {
            if (!IsActive)
                return;

            InspectionTreeItem root = InspectionTreeBuilder.BuildTree(BuildObjectList());
            InspectionScope live = InspectionScope.Live;
            if (live == null)
            {
                PresentTree(root, InspectionTreeOpening.AnnounceRow);
                return;
            }
            treeRoot = root;
            pendingOpening = null;
            live.ReplaceTreePreservingCursor(root);
        }

        /// <summary>Re-flattens the visible rows after children changed, keeping the cursor.</summary>
        public static void RefreshVisibleList()
        {
            if (!IsActive)
                return;

            InspectionScope live = InspectionScope.Live;
            if (live != null)
                live.RefreshTreeRows();
        }

        /// <summary>The inspectable objects at the inspected cell.</summary>
        private static List<object> BuildObjectList()
        {
            var objects = new List<object>();

            if (Find.CurrentMap == null)
                return objects;

            // Zones and plans are not returned by SelectableObjectsAt.
            Zone zone = inspectionPosition.GetZone(Find.CurrentMap);
            if (zone != null)
            {
                objects.Add(zone);
            }

            Plan plan = inspectionPosition.GetPlan(Find.CurrentMap);
            if (plan != null)
            {
                objects.Add(plan);
            }

            var objectsAtPosition = Selector.SelectableObjectsAt(inspectionPosition, Find.CurrentMap);

            foreach (var obj in objectsAtPosition)
            {
                if (obj is Pawn || obj is Building || obj is Plant || obj is Thing)
                {
                    objects.Add(obj);
                }
            }

            return objects;
        }

        /// <summary>Re-announces the current row, for returns from a sub-state.</summary>
        public static void ReannounceCurrentSelection()
        {
            if (!IsActive)
                return;

            InspectionScope live = InspectionScope.Live;
            if (live != null)
                live.AnnounceCurrentRow();
        }

        /// <summary>Escape: returns to the parent object's inspection if there is one, else closes.</summary>
        public static void ClosePanel()
        {
            if (!IsActive)
                return;

            if (parentObject != null)
            {
                var parent = parentObject;
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
                // No parent of its own: the walk-back never goes deeper than two levels.
                OpenForObject(parent);
            }
            else
            {
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Inspection.Panel.PanelClosed".Loc());
            }
        }
    }
}
