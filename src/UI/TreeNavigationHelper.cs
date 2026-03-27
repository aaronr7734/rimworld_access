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
    /// Reusable tree navigation helper that encapsulates all standard treeview keyboard
    /// handling, typeahead search, expand/collapse, level tracking, and announcements.
    ///
    /// Usage:
    ///   var tree = new TreeNavigationHelper("MyFeature");
    ///   tree.OnActivate = item => { /* custom Enter behavior */ return false; };
    ///   tree.Initialize(rootItem);
    ///   // In input handler: tree.HandleInput(Event.current)
    /// </summary>
    public class TreeNavigationHelper
    {
        private readonly string levelTrackingKey;
        private readonly TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        private InspectionTreeItem rootItem;
        private List<InspectionTreeItem> visibleItems = new List<InspectionTreeItem>();
        private int selectedIndex;
        private Dictionary<InspectionTreeItem, InspectionTreeItem> lastChildPerParent;
        private InspectionTreeItem lastAnnouncedParent;

        #region Configuration

        /// <summary>
        /// Custom formatter for item announcements during normal navigation.
        /// Receives the current item; returns the full announcement string.
        /// If null, uses the default format: "Label, expanded, 3 items. 1 of 5. level 2"
        /// </summary>
        public Func<InspectionTreeItem, string> FormatItemAnnouncement { get; set; }

        /// <summary>
        /// Custom formatter for item announcements after expand/collapse state changes.
        /// If null, falls back to FormatItemAnnouncement (or default).
        /// Useful when you want a shorter announcement after toggling (e.g., just label + state).
        /// </summary>
        public Func<InspectionTreeItem, string> FormatStateChangeAnnouncement { get; set; }

        /// <summary>
        /// Custom formatter for announcements during typeahead search.
        /// Receives the current item and typeahead helper; returns the full announcement string.
        /// If null, uses default: "Label, expanded, 2 of 5 matches for 'w'"
        /// </summary>
        public Func<InspectionTreeItem, TypeaheadSearchHelper, string> FormatSearchAnnouncement { get; set; }

        /// <summary>
        /// Called when Enter is pressed on a node. Return true if handled;
        /// false falls back to default toggle expand/collapse behavior.
        /// </summary>
        public Func<InspectionTreeItem, bool> OnActivate { get; set; }

        /// <summary>
        /// Called when Delete is pressed. Return true if handled.
        /// </summary>
        public Func<InspectionTreeItem, bool> OnDelete { get; set; }

        /// <summary>
        /// Called when Alt+I is pressed. Return true if handled.
        /// If null, default behavior tries item.OnInfo, then opens info card for item.LinkedDef.
        /// </summary>
        public Func<InspectionTreeItem, bool> OnInfo { get; set; }

        /// <summary>
        /// Called before a node is expanded (right arrow or Enter toggle).
        /// Use for lazy loading: populate item.Children in this callback.
        /// </summary>
        public Action<InspectionTreeItem> OnBeforeExpand { get; set; }

        /// <summary>
        /// Whether to include child counts in expand/collapse announcements.
        /// Default: true ("expanded, 3 items"). Set to false for just "expanded".
        /// </summary>
        public bool AnnounceChildCounts { get; set; } = true;

        /// <summary>
        /// Whether to skip the root node in the visible list.
        /// Default: true (root is hidden, children are top-level visible items).
        /// </summary>
        public bool SkipRootInVisibleList { get; set; } = true;

        /// <summary>
        /// Whether to track the last visited child per parent node for cursor restoration.
        /// Default: false. Enable for inspection-style trees where returning to a parent
        /// should restore the previously visited child.
        /// </summary>
        public bool TrackLastChild { get; set; } = false;

        /// <summary>
        /// Returns true if submenu-style treeview navigation is enabled in settings.
        /// In submenu mode, expanded parents are hidden from the visible list.
        /// </summary>
        private bool IsSubmenuMode =>
            RimWorldAccessMod_Settings.Settings?.SubmenuTreeNavigation ?? false;

        #endregion

        #region Read-Only State

        public bool HasActiveSearch => typeahead.HasActiveSearch;
        public bool HasNoMatches => typeahead.HasNoMatches;
        public int SelectedIndex => selectedIndex;

        public InspectionTreeItem SelectedItem =>
            (selectedIndex >= 0 && selectedIndex < visibleItems.Count)
                ? visibleItems[selectedIndex]
                : null;

        public IReadOnlyList<InspectionTreeItem> VisibleItems => visibleItems;
        public InspectionTreeItem RootItem => rootItem;
        public int Count => visibleItems.Count;
        public TypeaheadSearchHelper Typeahead => typeahead;

        #endregion

        public TreeNavigationHelper(string levelTrackingKey)
        {
            this.levelTrackingKey = levelTrackingKey;
        }

        #region Lifecycle

        /// <summary>
        /// Initializes the tree with a root node. Flattens visible items,
        /// resets selection and search, and resets level tracking.
        /// Does NOT announce — caller should announce opening in their own format.
        /// </summary>
        public void Initialize(InspectionTreeItem root, int initialIndex = 0)
        {
            rootItem = root;
            selectedIndex = initialIndex;
            typeahead.ClearSearch();
            MenuHelper.ResetLevel(levelTrackingKey);
            if (TrackLastChild || IsSubmenuMode)
                lastChildPerParent = new Dictionary<InspectionTreeItem, InspectionTreeItem>();
            lastAnnouncedParent = null;
            RebuildVisibleList();
            if (selectedIndex >= visibleItems.Count)
                selectedIndex = Math.Max(0, visibleItems.Count - 1);
        }

        /// <summary>
        /// Resets all tree state.
        /// </summary>
        public void Reset()
        {
            rootItem = null;
            visibleItems.Clear();
            selectedIndex = 0;
            typeahead.ClearSearch();
            MenuHelper.ResetLevel(levelTrackingKey);
            lastChildPerParent?.Clear();
            lastAnnouncedParent = null;
        }

        #endregion

        #region Primary Input Handler

        /// <summary>
        /// Handles keyboard input for tree navigation.
        /// Returns true if input was consumed; false for unhandled events
        /// (Escape with no active search — caller decides close behavior).
        /// </summary>
        public bool HandleInput(Event ev)
        {
            if (ev.type != EventType.KeyDown)
                return false;

            KeyCode key = ev.keyCode;

            // Alt+I — info card (use KeyboardHelper.IsAltHeld for AZERTY compatibility)
            if (KeyboardHelper.IsAltHeld && key == KeyCode.I)
            {
                HandleInfoKey();
                return true;
            }

            // Delete
            if (key == KeyCode.Delete)
            {
                HandleDeleteKey();
                return true;
            }

            // Escape — clear search only; return false if no search (caller handles close)
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentItem();
                    return true;
                }
                return false;
            }

            // Up arrow
            if (key == KeyCode.UpArrow)
            {
                if (visibleItems.Count == 0) return true;
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int prev = typeahead.GetPreviousMatch(selectedIndex);
                    if (prev >= 0)
                    {
                        selectedIndex = prev;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    selectedIndex = MenuHelper.SelectPrevious(selectedIndex, visibleItems.Count);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentItem();
                }
                return true;
            }

            // Down arrow
            if (key == KeyCode.DownArrow)
            {
                if (visibleItems.Count == 0) return true;
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int next = typeahead.GetNextMatch(selectedIndex);
                    if (next >= 0)
                    {
                        selectedIndex = next;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    selectedIndex = MenuHelper.SelectNext(selectedIndex, visibleItems.Count);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentItem();
                }
                return true;
            }

            // Right arrow — expand or drill down
            if (key == KeyCode.RightArrow)
            {
                ExpandOrDrillDown();
                return true;
            }

            // Left arrow — collapse or drill up
            if (key == KeyCode.LeftArrow)
            {
                CollapseOrDrillUp();
                return true;
            }

            // Home — first sibling (Ctrl = absolute first)
            if (key == KeyCode.Home)
            {
                if (visibleItems.Count == 0) return true;
                typeahead.ClearSearch();
                MenuHelper.HandleTreeHomeKey(visibleItems, ref selectedIndex,
                    item => item.IndentLevel, ev.control, PlayTickAndAnnounce);
                return true;
            }

            // End — last sibling (Ctrl = absolute last)
            if (key == KeyCode.End)
            {
                if (visibleItems.Count == 0) return true;
                typeahead.ClearSearch();
                MenuHelper.HandleTreeEndKey(visibleItems, ref selectedIndex,
                    item => item.IndentLevel,
                    item => item.IsExpanded,
                    item => item.IsExpandable && item.Children.Count > 0,
                    ev.control, PlayTickAndAnnounce);
                return true;
            }

            // Space — re-announce current item
            if (key == KeyCode.Space)
            {
                AnnounceCurrentItem();
                return true;
            }

            // Enter — custom activate, then fall back to toggle expand/collapse
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                HandleEnterKey();
                return true;
            }

            // * key — expand all siblings
            bool isStar = key == KeyCode.KeypadMultiply
                          || (ev.shift && key == KeyCode.Alpha8);
            if (isStar)
            {
                ExpandAllSiblings();
                return true;
            }

            // Backspace — delete last search character
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = GetVisibleLabels();
                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0)
                        selectedIndex = newIndex;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceWithSearch();
                }
                return true;
            }

            // Typeahead search — alphanumeric keys
            {
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if ((isLetter || isNumber) && !KeyboardHelper.IsAltHeld && !ev.control)
                {
                    char c = isLetter
                        ? (char)('a' + (key - KeyCode.A))
                        : (char)('0' + (key - KeyCode.Alpha0));
                    HandleTypeahead(c);
                    return true;
                }
            }

            // Consume all other keys to prevent pass-through
            return true;
        }

        #endregion

        #region Fine-Grained Navigation

        public void SelectNext()
        {
            if (visibleItems.Count == 0) return;
            selectedIndex = MenuHelper.SelectNext(selectedIndex, visibleItems.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        public void SelectPrevious()
        {
            if (visibleItems.Count == 0) return;
            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, visibleItems.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        public void ExpandOrDrillDown()
        {
            if (visibleItems.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                return;

            typeahead.ClearSearch();
            var item = visibleItems[selectedIndex];

            if (!item.IsExpandable)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            if (!item.IsExpanded)
            {
                OnBeforeExpand?.Invoke(item);
                item.IsExpanded = true;
                SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();

                if (IsSubmenuMode)
                {
                    // Parent disappears — land on remembered or first child
                    InspectionTreeItem targetChild = null;
                    if (lastChildPerParent != null && lastChildPerParent.TryGetValue(item, out var remembered))
                        targetChild = remembered;
                    if (targetChild == null && item.Children.Count > 0)
                        targetChild = item.Children[0];

                    RebuildVisibleList();

                    if (targetChild != null)
                    {
                        int idx = visibleItems.IndexOf(targetChild);
                        if (idx >= 0)
                            selectedIndex = idx;
                        else
                            selectedIndex = Math.Max(0, Math.Min(selectedIndex, visibleItems.Count - 1));
                    }
                    else
                    {
                        selectedIndex = Math.Max(0, Math.Min(selectedIndex, visibleItems.Count - 1));
                    }
                    AnnounceCurrentItem();
                }
                else
                {
                    RebuildVisibleList();
                    AnnounceStateChange();
                }
            }
            else if (item.Children.Count > 0)
            {
                // Already expanded — drill into first child (standard mode only;
                // in submenu mode expanded items aren't visible)
                int childIndex = visibleItems.IndexOf(item.Children[0]);
                if (childIndex >= 0)
                {
                    if (TrackLastChild && lastChildPerParent != null)
                        lastChildPerParent[item] = item.Children[0];
                    selectedIndex = childIndex;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentItem();
                }
            }
        }

        public void CollapseOrDrillUp()
        {
            if (visibleItems.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                return;

            typeahead.ClearSearch();
            var item = visibleItems[selectedIndex];

            if (IsSubmenuMode)
            {
                // In submenu mode, expanded parents are never visible.
                // Left arrow always means: collapse my parent and go to it.
                var parent = item.Parent;
                if (parent != null && parent != rootItem)
                {
                    if (lastChildPerParent != null)
                        lastChildPerParent[parent] = item;

                    parent.IsExpanded = false;
                    RebuildVisibleList();
                    SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();

                    int parentIndex = visibleItems.IndexOf(parent);
                    if (parentIndex >= 0)
                        selectedIndex = parentIndex;
                    else
                        selectedIndex = Math.Max(0, Math.Min(selectedIndex, visibleItems.Count - 1));
                    AnnounceCurrentItem();
                }
                else
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
                return;
            }

            // Standard mode
            if (item.IsExpandable && item.IsExpanded)
            {
                item.IsExpanded = false;
                RebuildVisibleList();
                if (selectedIndex >= visibleItems.Count)
                    selectedIndex = Math.Max(0, visibleItems.Count - 1);
                SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
                AnnounceStateChange();
            }
            else if (item.Parent != null && item.Parent != rootItem)
            {
                int parentIndex = visibleItems.IndexOf(item.Parent);
                if (parentIndex >= 0)
                {
                    if (TrackLastChild && lastChildPerParent != null)
                        lastChildPerParent[item.Parent] = item;
                    selectedIndex = parentIndex;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentItem();
                }
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        public void ExpandAllSiblings()
        {
            if (visibleItems.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                return;

            var currentItem = visibleItems[selectedIndex];
            var siblings = (currentItem.Parent == null || currentItem.Parent == rootItem)
                ? rootItem.Children
                : currentItem.Parent.Children;

            int expandedCount = 0;
            foreach (var sibling in siblings)
            {
                if (sibling.IsExpandable && !sibling.IsExpanded)
                {
                    OnBeforeExpand?.Invoke(sibling);
                    sibling.IsExpanded = true;
                    expandedCount++;
                }
            }

            if (expandedCount > 0)
            {
                if (IsSubmenuMode)
                {
                    // All expanded siblings disappear — land on current item's first/remembered child
                    InspectionTreeItem targetChild = null;
                    if (currentItem.IsExpandable && currentItem.Children.Count > 0)
                    {
                        if (lastChildPerParent != null && lastChildPerParent.TryGetValue(currentItem, out var remembered))
                            targetChild = remembered;
                        if (targetChild == null)
                            targetChild = currentItem.Children[0];
                    }

                    RebuildVisibleList();
                    typeahead.ClearSearch();

                    if (targetChild != null)
                    {
                        int idx = visibleItems.IndexOf(targetChild);
                        if (idx >= 0)
                            selectedIndex = idx;
                        else
                            selectedIndex = Math.Max(0, Math.Min(selectedIndex, visibleItems.Count - 1));
                    }
                    else
                    {
                        selectedIndex = Math.Max(0, Math.Min(selectedIndex, visibleItems.Count - 1));
                    }

                    EmbeddedAudioHelper.PlaySoundDefWithReverb(SoundDefOf.FloatMenu_Open);
                    TolkHelper.Speak($"Expanded {expandedCount} {(expandedCount == 1 ? "item" : "items")}");
                    AnnounceCurrentItem();
                }
                else
                {
                    RebuildVisibleList();
                    typeahead.ClearSearch();
                    EmbeddedAudioHelper.PlaySoundDefWithReverb(SoundDefOf.FloatMenu_Open);
                    TolkHelper.Speak($"Expanded {expandedCount} {(expandedCount == 1 ? "item" : "items")}");
                }
            }
        }

        public void JumpToFirst(bool absolute)
        {
            if (visibleItems.Count == 0) return;
            typeahead.ClearSearch();
            MenuHelper.HandleTreeHomeKey(visibleItems, ref selectedIndex,
                item => item.IndentLevel, absolute, PlayTickAndAnnounce);
        }

        public void JumpToLast(bool absolute)
        {
            if (visibleItems.Count == 0) return;
            typeahead.ClearSearch();
            MenuHelper.HandleTreeEndKey(visibleItems, ref selectedIndex,
                item => item.IndentLevel,
                item => item.IsExpanded,
                item => item.IsExpandable && item.Children.Count > 0,
                absolute, PlayTickAndAnnounce);
        }

        /// <summary>
        /// Re-announces the current item using the standard announcement format.
        /// </summary>
        public void ReannounceCurrentItem()
        {
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Rebuilds the flattened visible items list from the tree structure.
        /// Call after modifying tree nodes externally (adding/removing children).
        /// </summary>
        public void RebuildVisibleList()
        {
            visibleItems.Clear();
            if (rootItem == null) return;

            if (IsSubmenuMode)
            {
                foreach (var child in rootItem.Children)
                    GetVisibleItemsSubmenuMode(child, visibleItems);
            }
            else if (SkipRootInVisibleList)
            {
                foreach (var child in rootItem.Children)
                {
                    visibleItems.AddRange(child.GetVisibleItems());
                }
            }
            else
            {
                visibleItems.AddRange(rootItem.GetVisibleItems());
            }
        }

        /// <summary>
        /// Sets the selected index directly. Clamps to valid range.
        /// </summary>
        public void SetSelectedIndex(int index)
        {
            selectedIndex = Math.Max(0, Math.Min(index, Math.Max(0, visibleItems.Count - 1)));
        }

        /// <summary>
        /// Gets the last visited child for a parent node (if TrackLastChild is enabled).
        /// Returns null if no child was tracked for this parent.
        /// </summary>
        public InspectionTreeItem GetLastChild(InspectionTreeItem parent)
        {
            if (lastChildPerParent != null && lastChildPerParent.TryGetValue(parent, out var child))
                return child;
            return null;
        }

        #endregion

        #region Announcements

        private void AnnounceCurrentItem()
        {
            if (visibleItems.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                return;

            var item = visibleItems[selectedIndex];
            string announcement = FormatItemAnnouncement != null
                ? FormatItemAnnouncement(item)
                : DefaultFormatItemAnnouncement(item);
            announcement += GetSubmenuParentSuffix(item);
            TolkHelper.Speak(announcement);
        }

        private void AnnounceStateChange()
        {
            if (visibleItems.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                return;

            var item = visibleItems[selectedIndex];

            if (FormatStateChangeAnnouncement != null)
            {
                TolkHelper.Speak(FormatStateChangeAnnouncement(item));
                return;
            }

            // When ExpandedLabel is set, state changes always use the short label
            // so the user doesn't hear the full summary on every expand/collapse
            if (!string.IsNullOrEmpty(item.ExpandedLabel))
            {
                string shortLabel = item.ExpandedLabel;
                string state = item.IsExpanded ? "expanded" : "collapsed";
                if (AnnounceChildCounts)
                {
                    int childCount = item.Children.Count;
                    string childWord = childCount == 1 ? "item" : "items";
                    TolkHelper.Speak($"{shortLabel}, {state}, {childCount} {childWord}");
                }
                else
                {
                    TolkHelper.Speak($"{shortLabel}, {state}");
                }
                return;
            }

            // Fall back to regular announcement
            AnnounceCurrentItem();
        }

        private void AnnounceWithSearch()
        {
            if (visibleItems.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                return;

            var item = visibleItems[selectedIndex];
            string announcement = FormatSearchAnnouncement != null
                ? FormatSearchAnnouncement(item, typeahead)
                : DefaultFormatSearchAnnouncement(item);
            announcement += GetSubmenuParentSuffix(item);
            TolkHelper.Speak(announcement);
        }

        #endregion

        #region Default Announcement Formats

        /// <summary>
        /// Default item announcement: "Label, expanded, 3 items. 1 of 5. level 2"
        /// When ExpandedLabel is set: uses short label when expanded, full label when collapsed.
        /// </summary>
        public string DefaultFormatItemAnnouncement(InspectionTreeItem item)
        {
            // Smart labels: use ExpandedLabel (short form) when expanded, full Label when collapsed
            string label;
            if (item.IsExpandable && item.IsExpanded && !string.IsNullOrEmpty(item.ExpandedLabel))
                label = item.ExpandedLabel;
            else
                label = item.Label.TrimEnd('.', '!', '?');

            string stateIndicator = "";
            if (item.IsExpandable)
            {
                string state = item.IsExpanded ? "expanded" : "collapsed";
                if (AnnounceChildCounts)
                {
                    int childCount = item.Children.Count;
                    string childWord = childCount == 1 ? "item" : "items";
                    stateIndicator = $", {state}, {childCount} {childWord}";
                }
                else
                {
                    stateIndicator = $", {state}";
                }
            }

            var (position, total) = GetSiblingPosition(item);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);
            string positionSection = string.IsNullOrEmpty(positionPart)
                ? "" : $". {positionPart}";

            string levelSuffix = MenuHelper.GetLevelSuffix(levelTrackingKey, item.IndentLevel);

            return $"{label}{stateIndicator}{positionSection}{levelSuffix}";
        }

        /// <summary>
        /// Default search announcement: "Label, expanded, 2 of 5 matches for 'w'"
        /// </summary>
        public string DefaultFormatSearchAnnouncement(InspectionTreeItem item)
        {
            // Smart labels: use short form when expanded during search too
            string label;
            if (item.IsExpandable && item.IsExpanded && !string.IsNullOrEmpty(item.ExpandedLabel))
                label = item.ExpandedLabel;
            else
                label = item.Label.TrimEnd('.', '!', '?');

            string stateIndicator = "";
            if (item.IsExpandable)
                stateIndicator = item.IsExpanded ? ", expanded" : ", collapsed";

            string searchInfo = $", {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";
            return $"{label}{stateIndicator}{searchInfo}";
        }

        #endregion

        #region Internal Helpers

        /// <summary>
        /// Gets the sibling position (1-indexed) and total count for a node.
        /// Uses the parent's children list (or root's children for top-level nodes).
        /// </summary>
        public (int position, int total) GetSiblingPosition(InspectionTreeItem item)
        {
            var siblings = (item.Parent == null || item.Parent == rootItem)
                ? rootItem.Children
                : item.Parent.Children;
            int pos = siblings.IndexOf(item) + 1;
            return (pos, siblings.Count);
        }

        private List<string> GetVisibleLabels()
        {
            return visibleItems.Select(item => item.Label).ToList();
        }

        private void HandleTypeahead(char c)
        {
            var labels = GetVisibleLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                selectedIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceWithSearch();
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'.");
            }
        }

        private void HandleEnterKey()
        {
            if (visibleItems.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                return;

            var item = visibleItems[selectedIndex];

            // Try custom activate first
            if (OnActivate != null && OnActivate(item))
                return;

            // Try the item's own OnActivate callback
            if (item.OnActivate != null)
            {
                item.OnActivate();
                return;
            }

            // Default: expand-and-enter (submenu mode) or toggle (standard mode)
            if (item.IsExpandable)
            {
                if (IsSubmenuMode)
                {
                    ExpandOrDrillDown();
                }
                else
                {
                    typeahead.ClearSearch();
                    if (!item.IsExpanded)
                        OnBeforeExpand?.Invoke(item);
                    item.IsExpanded = !item.IsExpanded;
                    RebuildVisibleList();
                    if (item.IsExpanded)
                        SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();
                    else
                        SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
                    AnnounceStateChange();
                }
            }
        }

        private void HandleDeleteKey()
        {
            if (visibleItems.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                return;

            var item = visibleItems[selectedIndex];

            // Try custom delete handler
            if (OnDelete != null && OnDelete(item))
                return;

            // Try item's own OnDelete callback
            if (item.OnDelete != null)
            {
                item.OnDelete();
                return;
            }
        }

        private void HandleInfoKey()
        {
            if (visibleItems.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleItems.Count)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No info card available.");
                return;
            }

            var item = visibleItems[selectedIndex];

            // Try custom info handler
            if (OnInfo != null && OnInfo(item))
                return;

            // Try item's own OnInfo callback
            if (item.OnInfo != null)
            {
                item.OnInfo();
                return;
            }

            // Default: open info card for LinkedDef
            if (item.LinkedDef != null)
            {
                InfoCardState.OpenInfoCardForDef(item.LinkedDef);
                return;
            }

            // Walk up tree looking for a LinkedDef
            var parent = item.Parent;
            while (parent != null && parent != rootItem)
            {
                if (parent.LinkedDef != null)
                {
                    InfoCardState.OpenInfoCardForDef(parent.LinkedDef);
                    return;
                }
                parent = parent.Parent;
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("No info card available.");
        }

        /// <summary>
        /// Returns a parent label suffix when the current item's parent differs from
        /// the last announced parent. Only active in submenu mode. Used to announce
        /// parent boundary crossings during up/down navigation.
        /// </summary>
        private string GetSubmenuParentSuffix(InspectionTreeItem item)
        {
            if (!IsSubmenuMode) return "";

            var parent = item.Parent;
            if (parent == null || parent == rootItem)
            {
                if (lastAnnouncedParent != null)
                    lastAnnouncedParent = null;
                return "";
            }

            if (parent == lastAnnouncedParent) return "";

            lastAnnouncedParent = parent;
            string parentLabel = !string.IsNullOrEmpty(parent.ExpandedLabel)
                ? parent.ExpandedLabel
                : parent.Label;
            return $". {parentLabel}";
        }

        /// <summary>
        /// Builds visible items for submenu mode. Expanded parents with children
        /// are excluded — only their children appear. Collapsed expandable nodes
        /// and leaf nodes are included normally.
        /// </summary>
        private void GetVisibleItemsSubmenuMode(InspectionTreeItem node, List<InspectionTreeItem> result)
        {
            if (node.IsExpandable && node.IsExpanded && node.Children.Count > 0)
            {
                foreach (var child in node.Children)
                    GetVisibleItemsSubmenuMode(child, result);
            }
            else
            {
                result.Add(node);
            }
        }

        private void PlayTickAndAnnounce()
        {
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        #endregion

        #region Smart Label Utilities

        /// <summary>
        /// Walks the tree and builds aggregated "smart labels" for expandable nodes
        /// with informational children. For each qualifying node:
        /// - ExpandedLabel is set to the current Label (the short title)
        /// - Label is rebuilt as: title + ". " + child1 + ". " + child2 + ...
        ///
        /// Nodes are skipped if shouldAggregate returns false. By default, nodes
        /// whose children are all non-Action types are aggregated.
        ///
        /// Treeviews with custom summarization (inventory, architect) simply
        /// don't call this and set ExpandedLabel manually where needed.
        /// </summary>
        /// <param name="root">Root of the tree to process</param>
        /// <param name="shouldAggregate">Optional predicate to control which nodes get smart labels.
        /// If null, defaults to: aggregate if no children are Action type.</param>
        public static void BuildSmartLabels(InspectionTreeItem root, Func<InspectionTreeItem, bool> shouldAggregate = null)
        {
            if (root == null) return;

            if (shouldAggregate == null)
            {
                shouldAggregate = node =>
                    node.IsExpandable &&
                    node.Children.Count > 0 &&
                    !node.Children.Exists(c => c.Type == InspectionTreeItem.ItemType.Action);
            }

            BuildSmartLabelsRecursive(root, shouldAggregate);
        }

        private static void BuildSmartLabelsRecursive(InspectionTreeItem node, Func<InspectionTreeItem, bool> shouldAggregate)
        {
            // Process children first (bottom-up so child labels are finalized before aggregation)
            foreach (var child in node.Children)
            {
                BuildSmartLabelsRecursive(child, shouldAggregate);
            }

            if (!shouldAggregate(node))
                return;

            // Save current label as the short form
            node.ExpandedLabel = node.Label;

            // Build aggregated label from children
            var sb = new System.Text.StringBuilder(node.Label);
            foreach (var child in node.Children)
            {
                string childText = child.Label;
                if (string.IsNullOrEmpty(childText))
                    continue;

                // Use ". " as sentence separator, avoiding double periods
                if (sb.Length > 0)
                {
                    char lastChar = sb[sb.Length - 1];
                    if (lastChar == '.' || lastChar == '!' || lastChar == '?')
                        sb.Append(' ');
                    else
                        sb.Append(". ");
                }
                sb.Append(childText);
            }

            node.Label = sb.ToString();
        }

        #endregion
    }
}

