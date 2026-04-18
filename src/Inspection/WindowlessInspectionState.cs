using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the windowless inspection panel state.
    /// Uses a tree structure with inline expansion/collapse.
    /// Uses TreeNavigationHelper for standard treeview keyboard navigation.
    /// </summary>
    public static class WindowlessInspectionState
    {
        public static bool IsActive { get; private set; } = false;

        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("Inspection");
        public static TypeaheadSearchHelper Typeahead => treeNav.Typeahead;

        private static IntVec3 inspectionPosition;
        private static object parentObject = null; // Track parent object for navigation back
        private static List<object> previousSelection = new List<object>();

        static WindowlessInspectionState()
        {
            treeNav.TrackLastChild = true;
            treeNav.FormatItemAnnouncement = FormatItemAnnouncement;
            treeNav.FormatSearchAnnouncement = FormatSearchAnnouncement;
            treeNav.OnActivate = HandleActivate;
            treeNav.OnBeforeExpand = HandleBeforeExpand;
            treeNav.OnInfo = HandleInfo;
        }

        /// <summary>
        /// Opens the inspection menu for the specified position.
        /// </summary>
        public static void Open(IntVec3 position)
        {
            try
            {
                inspectionPosition = position;

                // Build the object list
                var objects = BuildObjectList();

                if (objects.Count == 0)
                {
                    TolkHelper.Speak("No items here to inspect.");
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }

                // Save current selection to restore on close
                previousSelection.Clear();
                previousSelection.AddRange(Find.Selector.SelectedObjects.Cast<object>());

                // Select the first object so that tab visibility checks work correctly
                // (tabs use Find.Selector.SingleSelectedThing for SelPawn/SelThing)
                if (objects.Count > 0 && objects[0] is Thing thingToSelect)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(thingToSelect, playSound: false, forceDesignatorDeselect: false);
                }

                // Build the tree
                var rootItem = InspectionTreeBuilder.BuildTree(objects);
                treeNav.Initialize(rootItem);

                IsActive = true;
                SoundDefOf.TabOpen.PlayOneShotOnCamera();

                // Special handling for single object: auto-expand and position on first child
                if (objects.Count == 1 && treeNav.Count > 0)
                {
                    var singleItem = treeNav.VisibleItems[0];
                    if (singleItem.IsExpandable)
                    {
                        // Announce just the item name (no expand/collapse status)
                        TolkHelper.Speak(singleItem.Label.StripTags());

                        // Trigger lazy loading if needed
                        if (singleItem.OnActivate != null && singleItem.Children.Count == 0)
                        {
                            singleItem.OnActivate();
                        }

                        if (singleItem.Children.Count > 0)
                        {
                            singleItem.IsExpanded = true;
                            treeNav.RebuildVisibleList();
                            // In submenu mode, the expanded parent is removed from the visible list,
                            // so the first child is at index 0 instead of 1
                            int firstChildIndex = (RimWorldAccessMod_Settings.Settings?.SubmenuTreeNavigation ?? false) ? 0 : 1;
                            treeNav.SetSelectedIndex(firstChildIndex);
                            treeNav.MarkCurrentParentAsAnnounced();
                            treeNav.ReannounceCurrentItem();
                        }
                        return; // Early return - skip normal announcement
                    }
                }

                // Normal case: multiple objects or non-expandable single object
                treeNav.ReannounceCurrentItem();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error opening inspection menu: {ex}");
                Close();
            }
        }

        /// <summary>
        /// Opens the inspection menu for a specific object (Thing, Pawn, Building, etc.).
        /// </summary>
        /// <param name="obj">The object to inspect</param>
        /// <param name="parent">Optional parent object to return to when pressing Escape</param>
        public static void OpenForObject(object obj, object parent = null)
        {
            try
            {
                if (obj == null)
                {
                    TolkHelper.Speak("No object to inspect.");
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }

                // Set position if it's a Thing
                if (obj is Thing thing)
                {
                    inspectionPosition = thing.Position;
                }
                else
                {
                    inspectionPosition = IntVec3.Invalid;
                }

                // Store parent for navigation back
                parentObject = parent;

                // Save current selection to restore on close
                previousSelection.Clear();
                previousSelection.AddRange(Find.Selector.SelectedObjects.Cast<object>());

                // Select the object so that tab visibility checks work correctly
                // (tabs use Find.Selector.SingleSelectedThing for SelPawn/SelThing)
                if (obj is Thing thingToSelect)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(thingToSelect, playSound: false, forceDesignatorDeselect: false);
                }

                // Build tree with just this object
                var objects = new List<object> { obj };
                var rootItem = InspectionTreeBuilder.BuildTree(objects);
                treeNav.Initialize(rootItem);

                IsActive = true;
                SoundDefOf.TabOpen.PlayOneShotOnCamera();

                // Auto-expand the single object node and position on first child
                if (treeNav.Count > 0)
                {
                    var singleItem = treeNav.VisibleItems[0];
                    if (singleItem.IsExpandable)
                    {
                        TolkHelper.Speak(singleItem.Label.StripTags());

                        if (singleItem.OnActivate != null && singleItem.Children.Count == 0)
                        {
                            singleItem.OnActivate();
                        }

                        if (singleItem.Children.Count > 0)
                        {
                            singleItem.IsExpanded = true;
                            treeNav.RebuildVisibleList();
                            // In submenu mode, the expanded parent is removed from the visible list,
                            // so the first child is at index 0 instead of 1
                            int firstChildIndex = (RimWorldAccessMod_Settings.Settings?.SubmenuTreeNavigation ?? false) ? 0 : 1;
                            treeNav.SetSelectedIndex(firstChildIndex);
                            treeNav.MarkCurrentParentAsAnnounced();
                            treeNav.ReannounceCurrentItem();
                            return;
                        }
                    }
                }

                treeNav.ReannounceCurrentItem();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error opening inspection menu for object: {ex}");
                Close();
            }
        }

        /// <summary>
        /// Opens the inspection menu for a specific object with specified mode.
        /// </summary>
        /// <param name="obj">The object to inspect</param>
        /// <param name="parent">Optional parent object to return to when pressing Escape</param>
        /// <param name="mode">Inspection mode - Full allows actions, ReadOnly is view-only</param>
        public static void OpenForObject(object obj, object parent, InspectionMode mode)
        {
            try
            {
                if (obj == null)
                {
                    TolkHelper.Speak("No object to inspect.");
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }

                // Set position if it's a Thing
                if (obj is Thing thing)
                {
                    inspectionPosition = thing.Position;
                }
                else
                {
                    inspectionPosition = IntVec3.Invalid;
                }

                // Store parent for navigation back
                parentObject = parent;

                // Build tree with just this object, using specified mode
                var objects = new List<object> { obj };
                var rootItem = InspectionTreeBuilder.BuildTree(objects, mode);
                treeNav.Initialize(rootItem);

                IsActive = true;
                SoundDefOf.TabOpen.PlayOneShotOnCamera();

                // Special handling for single object: auto-expand and position on first child
                if (treeNav.Count > 0)
                {
                    var singleItem = treeNav.VisibleItems[0];
                    if (singleItem.IsExpandable)
                    {
                        // Announce just the item name (no expand/collapse status)
                        TolkHelper.Speak(singleItem.Label.StripTags());

                        // Trigger lazy loading if needed
                        if (singleItem.OnActivate != null && singleItem.Children.Count == 0)
                        {
                            singleItem.OnActivate();
                        }

                        if (singleItem.Children.Count > 0)
                        {
                            singleItem.IsExpanded = true;
                            treeNav.RebuildVisibleList();
                            // In submenu mode, the expanded parent is removed from the visible list,
                            // so the first child is at index 0 instead of 1
                            int firstChildIndex = (RimWorldAccessMod_Settings.Settings?.SubmenuTreeNavigation ?? false) ? 0 : 1;
                            treeNav.SetSelectedIndex(firstChildIndex);
                            treeNav.MarkCurrentParentAsAnnounced();
                            treeNav.ReannounceCurrentItem();
                        }
                        return; // Early return - skip normal announcement
                    }
                }

                // Normal case: non-expandable single object
                treeNav.ReannounceCurrentItem();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error opening inspection menu for object: {ex}");
                Close();
            }
        }

        /// <summary>
        /// Closes the inspection menu.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            parentObject = null;
            treeNav.Reset();

            // Restore previous selection
            if (previousSelection.Count > 0)
            {
                Find.Selector.ClearSelection();
                foreach (var obj in previousSelection)
                {
                    if (obj is Thing thing && thing.Spawned)
                    {
                        Find.Selector.Select(thing, playSound: false, forceDesignatorDeselect: false);
                    }
                }
                previousSelection.Clear();
            }
        }

        /// <summary>
        /// Rebuilds after an action that modifies the tree structure (e.g., deleting a job).
        /// </summary>
        public static void RebuildAfterAction()
        {
            if (!IsActive)
                return;

            treeNav.RebuildVisibleList();

            // Try to keep selection valid
            if (treeNav.SelectedIndex >= treeNav.Count)
                treeNav.SetSelectedIndex(Math.Max(0, treeNav.Count - 1));

            treeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Rebuilds the tree (used after actions that modify state).
        /// </summary>
        public static void RebuildTree()
        {
            if (!IsActive)
                return;

            var objects = BuildObjectList();
            var rootItem = InspectionTreeBuilder.BuildTree(objects);
            treeNav.Initialize(rootItem);

            // Try to keep selection valid
            if (treeNav.SelectedIndex >= treeNav.Count)
                treeNav.SetSelectedIndex(Math.Max(0, treeNav.Count - 1));

            treeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Re-flattens the visible items list after tree children have been modified,
        /// preserving the current cursor position.
        /// </summary>
        public static void RefreshVisibleList()
        {
            if (!IsActive)
                return;

            var currentItem = treeNav.SelectedItem;

            treeNav.RebuildVisibleList();

            if (currentItem != null)
            {
                int newIndex = treeNav.VisibleItems.ToList().IndexOf(currentItem);
                if (newIndex >= 0)
                    treeNav.SetSelectedIndex(newIndex);
                else if (treeNav.SelectedIndex >= treeNav.Count)
                    treeNav.SetSelectedIndex(Math.Max(0, treeNav.Count - 1));
            }
        }

        /// <summary>
        /// Builds the list of inspectable objects at the cursor position.
        /// </summary>
        private static List<object> BuildObjectList()
        {
            var objects = new List<object>();

            if (Find.CurrentMap == null)
                return objects;

            // Check for zone at cursor position first (zones are not returned by SelectableObjectsAt)
            Zone zone = inspectionPosition.GetZone(Find.CurrentMap);
            if (zone != null)
            {
                objects.Add(zone);
            }

            // Get other selectable objects at this position
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

        /// <summary>
        /// Checks if there's only one root item (single object being inspected).
        /// </summary>
        private static bool HasSingleRoot()
        {
            return treeNav.RootItem != null && treeNav.RootItem.Children.Count == 1;
        }

        /// <summary>
        /// Re-announces the current selection. Used when returning from a sub-state (e.g., HealthTabState).
        /// </summary>
        public static void ReannounceCurrentSelection()
        {
            if (!IsActive)
                return;
            treeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Closes the entire panel (Escape key).
        /// If there's a parent object, returns to that parent's inspection instead of fully closing.
        /// </summary>
        public static void ClosePanel()
        {
            if (!IsActive)
                return;

            // Check if we have a parent to return to
            if (parentObject != null)
            {
                var parent = parentObject; // Save reference before Close() clears it
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
                OpenForObject(parent); // Open parent without a parent (we don't go deeper than 2 levels)
            }
            else
            {
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
                TolkHelper.Speak("Inspection panel closed.");
            }
        }

        /// <summary>
        /// Handles keyboard input for the inspection menu.
        /// Returns true if the input was handled.
        /// </summary>
        public static bool HandleInput(Event ev)
        {
            if (!IsActive)
                return false;

            // Yield to float menu when it's open (e.g., opened from an OnActivate callback)
            if (WindowlessFloatMenuState.IsActive)
                return false;

            if (ev.type != EventType.KeyDown)
                return false;

            try
            {
                // Check if any tab state is active and delegate input to it
                if (HealthTabState.IsActive)
                {
                    return HealthTabState.HandleInput(ev);
                }

                KeyCode key = ev.keyCode;

                // Handle Escape - clear search FIRST, then close
                if (key == KeyCode.Escape)
                {
                    if (treeNav.HasActiveSearch)
                    {
                        treeNav.Typeahead.ClearSearchAndAnnounce();
                        treeNav.ReannounceCurrentItem();
                        ev.Use();
                        return true;
                    }
                    ClosePanel();
                    ev.Use();
                    return true;
                }

                // Handle Right arrow - custom expand with lazy-load empty correction
                if (key == KeyCode.RightArrow)
                {
                    HandleRightArrow();
                    ev.Use();
                    return true;
                }

                // Handle Left arrow - custom collapse that skips non-expandable parents
                if (key == KeyCode.LeftArrow)
                {
                    HandleLeftArrow();
                    ev.Use();
                    return true;
                }

                // Handle Enter - custom activate with cursor restoration
                if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    HandleEnterKey();
                    ev.Use();
                    return true;
                }

                // Handle Space on Action items as toggle/activate (e.g., food checkboxes)
                if (key == KeyCode.Space)
                {
                    var currentItem = treeNav.SelectedItem;
                    if (currentItem?.Type == InspectionTreeItem.ItemType.Action && currentItem.OnActivate != null)
                    {
                        HandleEnterKey();
                        ev.Use();
                        return true;
                    }
                    // Otherwise fall through to TreeNavigationHelper (re-announce)
                }

                // Handle Alt+I - use KeyboardHelper.IsAltHeld for AZERTY compatibility
                if (KeyboardHelper.IsAltHeld && key == KeyCode.I)
                {
                    HandleInfoKey();
                    ev.Use();
                    return true;
                }

                // Delegate all other input to TreeNavigationHelper
                if (treeNav.HandleInput(ev))
                {
                    ev.Use();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error handling input in inspection menu: {ex}");
            }

            return false;
        }

        #region Announcement Formatters

        /// <summary>
        /// Formats item announcement:
        /// "{name} {state}. {X} of {Y}. Inspectable. level N"
        /// Level is adjusted for single-root trees and only announced when it changes.
        /// </summary>
        private static string FormatItemAnnouncement(InspectionTreeItem item)
        {
            try
            {
                // Smart labels: use ExpandedLabel (short form) when expanded, full Label when collapsed
                string rawLabel;
                if (item.IsExpandable && item.IsExpanded && !string.IsNullOrEmpty(item.ExpandedLabel))
                    rawLabel = item.ExpandedLabel;
                else
                    rawLabel = item.Label;
                string label = rawLabel.StripTags().TrimEnd('.', '!', '?');

                // Build state indicator (only for expandable items)
                string stateIndicator = "";
                if (item.IsExpandable)
                {
                    stateIndicator = item.IsExpanded ? ". expanded" : ". collapsed";
                }

                // Get sibling position
                var (position, total) = treeNav.GetSiblingPosition(item);

                // Get level prefix (only announced when level changes)
                // If single root, subtract 1 so children start at level 1
                int adjustedLevel = item.IndentLevel;
                if (HasSingleRoot())
                {
                    adjustedLevel = Math.Max(0, adjustedLevel - 1);
                }
                // Check if this item has a direct info card available
                string inspectable = HasDirectInfoCard(item) ? " Inspectable." : "";

                // Build full announcement: "{name} {state}. {X} of {Y}. level N"
                string levelSuffix = MenuHelper.GetLevelSuffix("Inspection", adjustedLevel);
                string positionPart = MenuHelper.FormatPosition(position - 1, total);
                string positionSection = string.IsNullOrEmpty(positionPart) ? "." : $". {positionPart}.";
                string announcement = $"{label}{stateIndicator}{positionSection}{inspectable}{levelSuffix}";

                return announcement;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error in FormatItemAnnouncement: {ex}");
                return item.Label.StripTags();
            }
        }

        /// <summary>
        /// Formats search announcement:
        /// "{label} {state}, {N} of {M} matches for '{search}'"
        /// </summary>
        private static string FormatSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead)
        {
            // Smart labels: use ExpandedLabel (short form) when expanded
            string rawLabel;
            if (item.IsExpandable && item.IsExpanded && !string.IsNullOrEmpty(item.ExpandedLabel))
                rawLabel = item.ExpandedLabel;
            else
                rawLabel = item.Label;
            string label = rawLabel.StripTags();

            // Build state indicator (only for expandable items)
            string stateIndicator = "";
            if (item.IsExpandable)
            {
                stateIndicator = item.IsExpanded ? ". expanded" : ". collapsed";
            }

            return typeahead.BuildItemAnnouncement($"{label}{stateIndicator}");
        }

        #endregion

        #region Custom Actions

        /// <summary>
        /// Handles Enter key activation with cursor restoration after actions.
        /// For expandable items, Enter acts like Right arrow (expand with lazy-load).
        /// For items with actions, executes and restores cursor position.
        /// </summary>
        private static bool HandleActivate(InspectionTreeItem item)
        {
            // For expandable items, Enter acts like Right arrow
            if (item.IsExpandable && !item.IsExpanded)
            {
                HandleRightArrow();
                return true;
            }

            // For items with actions, execute the action
            if (item.OnActivate != null)
            {
                item.OnActivate();

                // If the action closed this state (e.g., opening HealthTabState), stop here
                if (!IsActive)
                    return true;

                SoundDefOf.Click.PlayOneShotOnCamera();

                // Rebuild visible list, then restore cursor to the acted-upon item
                // or its nearest visible ancestor (the item itself may have been removed
                // when its parent's children were cleared)
                treeNav.RebuildVisibleList();
                int restoredIndex = -1;
                var candidate = item;
                while (candidate != null && restoredIndex < 0)
                {
                    restoredIndex = treeNav.VisibleItems.ToList().IndexOf(candidate);
                    candidate = candidate.Parent;
                }
                if (restoredIndex >= 0)
                    treeNav.SetSelectedIndex(restoredIndex);
                else if (treeNav.SelectedIndex >= treeNav.Count)
                    treeNav.SetSelectedIndex(Math.Max(0, treeNav.Count - 1));

                treeNav.ReannounceCurrentItem();
                return true;
            }

            // Otherwise, nothing to do
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("No action available for this item.");
            return true;
        }

        /// <summary>
        /// Handles lazy loading before expanding a node.
        /// Calls item.OnActivate to load children.
        /// </summary>
        private static void HandleBeforeExpand(InspectionTreeItem item)
        {
            if (item.OnActivate != null && item.Children.Count == 0)
            {
                item.OnActivate();
            }
        }

        /// <summary>
        /// Handles Enter key - delegates to treeNav OnActivate via HandleActivate.
        /// Separated to allow the HandleInput pre-intercept pattern.
        /// </summary>
        private static void HandleEnterKey()
        {
            if (treeNav.Count == 0 || treeNav.SelectedItem == null)
                return;

            var item = treeNav.SelectedItem;

            // Try custom activate (which handles all our domain-specific logic)
            if (HandleActivate(item))
                return;

            // Default: toggle expand/collapse (shouldn't reach here since HandleActivate always returns true)
            if (item.IsExpandable)
            {
                treeNav.Typeahead.ClearSearch();
                if (!item.IsExpanded)
                    HandleBeforeExpand(item);
                item.IsExpanded = !item.IsExpanded;
                treeNav.RebuildVisibleList();
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                treeNav.ReannounceCurrentItem();
            }
        }

        /// <summary>
        /// Handles Right arrow with lazy-load empty correction.
        /// WCAG behavior:
        /// - On closed node: Open node with lazy loading, reject if empty after load
        /// - On open node: Move to first child
        /// - On end node: Reject sound + feedback
        /// </summary>
        private static void HandleRightArrow()
        {
            var item = treeNav.SelectedItem;
            if (item == null)
                return;

            // Clear search when expanding
            treeNav.Typeahead.ClearSearch();

            // End node (not expandable) - reject
            if (!item.IsExpandable)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Cannot expand this item.", SpeechPriority.High);
                return;
            }

            // Already expanded - drill down to first child via treeNav
            if (item.IsExpanded)
            {
                treeNav.ExpandOrDrillDown();
                return;
            }

            // Collapsed node - expand it with lazy loading
            if (item.OnActivate != null && item.Children.Count == 0)
            {
                item.OnActivate();
            }

            if (item.Children.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No items to show.");
                return;
            }

            item.IsExpanded = true;
            treeNav.RebuildVisibleList();
            SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();

            // User just expanded this node — suppress section prefix announcement
            treeNav.MarkCurrentParentAsAnnounced();
            treeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Handles Left arrow - collapse with custom parent-skipping behavior.
        /// WCAG behavior:
        /// - On open node: Close node, focus stays on current item
        /// - On closed node: Move to nearest expandable ancestor (skipping non-expandable parents)
        /// - On end node: Move to nearest expandable ancestor
        /// </summary>
        private static void HandleLeftArrow()
        {
            if (treeNav.Count == 0 || treeNav.SelectedItem == null)
                return;

            // Clear search when collapsing
            treeNav.Typeahead.ClearSearch();

            var item = treeNav.SelectedItem;

            // Case 1: Item is expandable and expanded - collapse it (focus stays)
            if (item.IsExpandable && item.IsExpanded)
            {
                item.IsExpanded = false;
                treeNav.RebuildVisibleList();

                // Adjust selection if it's now out of range
                if (treeNav.SelectedIndex >= treeNav.Count)
                    treeNav.SetSelectedIndex(Math.Max(0, treeNav.Count - 1));

                SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
                treeNav.ReannounceCurrentItem();
                return;
            }

            // Case 2: Item is collapsed or end node - move to nearest expandable ancestor
            var parent = item.Parent;

            // Skip non-expandable parents (like root) to find an expandable ancestor
            while (parent != null && !parent.IsExpandable)
            {
                parent = parent.Parent;
            }

            if (parent == null)
            {
                // No expandable parent - we're at the top level
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.TopLevel, SpeechPriority.High);
                return;
            }

            // Find parent in visible list
            int parentIndex = -1;
            for (int i = 0; i < treeNav.VisibleItems.Count; i++)
            {
                if (treeNav.VisibleItems[i] == parent)
                {
                    parentIndex = i;
                    break;
                }
            }

            if (parentIndex >= 0)
            {
                treeNav.SetSelectedIndex(parentIndex);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                treeNav.ReannounceCurrentItem();
            }
            else
            {
                // Parent not visible (hidden in submenu mode) — collapse it to make it visible
                parent.IsExpanded = false;
                treeNav.RebuildVisibleList();

                parentIndex = -1;
                for (int j = 0; j < treeNav.VisibleItems.Count; j++)
                {
                    if (treeNav.VisibleItems[j] == parent)
                    {
                        parentIndex = j;
                        break;
                    }
                }

                if (parentIndex >= 0)
                {
                    treeNav.SetSelectedIndex(parentIndex);
                    SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
                    treeNav.ReannounceCurrentItem();
                }
                else
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.TopLevel, SpeechPriority.High);
                }
            }
        }

        /// <summary>
        /// Handles Alt+I - open info card for current item, or walks up to parent.
        /// </summary>
        private static bool HandleInfo(InspectionTreeItem item)
        {
            // Try to open info card for the item directly, or walk up to parent
            if (TryOpenInfoCardForItem(item))
                return true;

            // Walk up to parent for children of inspectable items
            var parent = item.Parent;
            while (parent != null)
            {
                if (TryOpenInfoCardForItem(parent))
                    return true;
                parent = parent.Parent;
            }

            InfoCardState.SpeakNoInfoCardAvailable();
            return true;
        }

        /// <summary>
        /// Handles Alt+I key press - wraps HandleInfo for direct invocation.
        /// </summary>
        private static void HandleInfoKey()
        {
            if (treeNav.Count == 0 || treeNav.SelectedItem == null)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }

            HandleInfo(treeNav.SelectedItem);
        }

        #endregion

        #region Info Card Support

        /// <summary>
        /// Returns true if the item has a direct info card (gear, hediff, gene, or LinkedDef).
        /// Only matches types the game natively offers info card buttons for.
        /// </summary>
        private static bool HasDirectInfoCard(InspectionTreeItem item)
        {
            if (item.OnInfo != null) return true;
            if (item.Data is InteractiveGearHelper.GearItem gi && gi.Thing != null) return true;
            if (item.Data is Hediff) return true;
            if (item.Data is Gene || item.Data is GeneDef) return true;
            if (item.LinkedDef != null) return true;
            return false;
        }

        /// <summary>
        /// Attempts to open an info card for a single item. Returns true if successful.
        /// </summary>
        private static bool TryOpenInfoCardForItem(InspectionTreeItem item)
        {
            // Custom info action (e.g. romance factor breakdown via StatBreakdownState)
            if (item.OnInfo != null)
            {
                item.OnInfo();
                return true;
            }

            // Gear items (game shows info cards on Gear tab)
            if (item.Data is InteractiveGearHelper.GearItem gearItem && gearItem.Thing != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(gearItem.Thing));
                return true;
            }

            // Hediffs (game shows info cards on Health tab)
            if (item.Data is Hediff hediff)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(hediff));
                return true;
            }

            // Genes (game shows info cards on Genes tab)
            if (item.Data is Gene gene)
            {
                InfoCardState.OpenInfoCardForDef(gene.def);
                return true;
            }
            if (item.Data is GeneDef geneDef)
            {
                InfoCardState.OpenInfoCardForDef(geneDef);
                return true;
            }

            // LinkedDef fallback (for items explicitly marked)
            if (item.LinkedDef != null)
            {
                InfoCardState.OpenInfoCardForDef(item.LinkedDef);
                return true;
            }

            return false;
        }

        #endregion
    }
}
