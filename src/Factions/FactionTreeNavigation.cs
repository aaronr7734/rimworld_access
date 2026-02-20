using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared tree navigation logic for faction screens.
    /// Used by both FactionTabState (in-game, windowless) and
    /// FactionLandingState (pre-game, dialog-based).
    /// </summary>
    internal class FactionTreeNavigation
    {
        private const string LevelTrackingKey = "FactionTree";

        private InspectionTreeItem rootItem;
        private List<InspectionTreeItem> visibleItems = new List<InspectionTreeItem>();
        private int selectedIndex;
        private TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Initializes the tree from a faction list. Builds tree, flattens,
        /// resets selection to 0, and announces opening.
        /// </summary>
        public void Initialize(List<Faction> factions)
        {
            rootItem = FactionHelper.BuildFactionTree(factions);
            RebuildVisibleList();
            selectedIndex = 0;
            typeahead.ClearSearch();
            MenuHelper.ResetLevel(LevelTrackingKey);
            AnnounceOpening(factions.Count);
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
            MenuHelper.ResetLevel(LevelTrackingKey);
        }

        /// <summary>
        /// Handles keyboard input for tree navigation.
        /// Returns true if input was handled.
        /// Returns false for Escape-close (no active search), letting the caller handle it.
        /// </summary>
        public bool HandleInput(Event ev)
        {
            if (ev.type != EventType.KeyDown)
                return false;

            KeyCode key = ev.keyCode;

            // Alt+I — open info card for selected faction
            if (ev.alt && key == KeyCode.I)
            {
                OpenInfoCard();
                return true;
            }

            // Escape — clear search only (caller handles close)
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
                    item => item.IndentLevel, ev.control, ClearAndAnnounce);
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
                    ev.control, ClearAndAnnounce);
                return true;
            }

            // Space — re-announce current item
            if (key == KeyCode.Space)
            {
                AnnounceCurrentItem();
                return true;
            }

            // Enter — toggle expansion on expandable nodes
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                if (visibleItems.Count > 0 && selectedIndex >= 0 && selectedIndex < visibleItems.Count)
                {
                    var item = visibleItems[selectedIndex];
                    if (item.IsExpandable)
                    {
                        typeahead.ClearSearch();
                        item.IsExpanded = !item.IsExpanded;
                        RebuildVisibleList();
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceCurrentItem();
                    }
                }
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

                if ((isLetter || isNumber) && !ev.alt && !ev.control)
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

        #region Tree Navigation

        private void ExpandOrDrillDown()
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
                item.IsExpanded = true;
                RebuildVisibleList();
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentItem();
            }
            else if (item.Children.Count > 0)
            {
                int childIndex = visibleItems.IndexOf(item.Children[0]);
                if (childIndex >= 0)
                {
                    selectedIndex = childIndex;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentItem();
                }
            }
        }

        private void CollapseOrDrillUp()
        {
            if (visibleItems.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                return;

            typeahead.ClearSearch();
            var item = visibleItems[selectedIndex];

            if (item.IsExpandable && item.IsExpanded)
            {
                item.IsExpanded = false;
                RebuildVisibleList();
                if (selectedIndex >= visibleItems.Count)
                    selectedIndex = Math.Max(0, visibleItems.Count - 1);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentItem();
            }
            else if (item.Parent != null && item.Parent != rootItem)
            {
                int parentIndex = visibleItems.IndexOf(item.Parent);
                if (parentIndex >= 0)
                {
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

        private void ExpandAllSiblings()
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
                    sibling.IsExpanded = true;
                    expandedCount++;
                }
            }

            if (expandedCount > 0)
            {
                RebuildVisibleList();
                typeahead.ClearSearch();
                TolkHelper.Speak($"Expanded {expandedCount} {(expandedCount == 1 ? "item" : "items")}");
            }
        }

        #endregion

        #region Announcements

        private void AnnounceOpening(int factionCount)
        {
            if (factionCount > 0 && visibleItems.Count > 0)
            {
                var sb = new StringBuilder($"Faction relations, {factionCount} factions");
                var firstItem = visibleItems[0];
                FactionHelper.AppendSentence(sb, firstItem.Label);

                if (firstItem.IsExpandable)
                {
                    string state = firstItem.IsExpanded ? "expanded" : "collapsed";
                    sb.Append($", {state}, {firstItem.Children.Count} {(firstItem.Children.Count == 1 ? "item" : "items")}");
                }

                var (pos, total) = GetSiblingPosition(firstItem);
                string position = MenuHelper.FormatPosition(pos - 1, total);
                if (!string.IsNullOrEmpty(position))
                    FactionHelper.AppendSentence(sb, position);

                TolkHelper.Speak(sb.ToString());
            }
            else
            {
                TolkHelper.Speak("Faction relations. No factions.");
            }
        }

        private void AnnounceCurrentItem()
        {
            if (visibleItems.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                return;

            var item = visibleItems[selectedIndex];
            string label = item.Label.TrimEnd('.', '!', '?');

            string stateIndicator = "";
            if (item.IsExpandable)
            {
                string state = item.IsExpanded ? "expanded" : "collapsed";
                int childCount = item.Children.Count;
                string childWord = childCount == 1 ? "item" : "items";
                stateIndicator = $", {state}, {childCount} {childWord}";
            }

            var (position, total) = GetSiblingPosition(item);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);
            string positionSection = string.IsNullOrEmpty(positionPart)
                ? "" : $". {positionPart}";

            string levelSuffix = MenuHelper.GetLevelSuffix(LevelTrackingKey, item.IndentLevel);

            TolkHelper.Speak($"{label}{stateIndicator}{positionSection}{levelSuffix}");
        }

        private void AnnounceWithSearch()
        {
            if (visibleItems.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                return;

            var item = visibleItems[selectedIndex];
            string label = item.Label.TrimEnd('.', '!', '?');

            string stateIndicator = "";
            if (item.IsExpandable)
                stateIndicator = item.IsExpanded ? ", expanded" : ", collapsed";

            string searchInfo = $", {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";
            TolkHelper.Speak($"{label}{stateIndicator}{searchInfo}");
        }

        private void ClearAndAnnounce()
        {
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        #endregion

        #region Helpers

        private void RebuildVisibleList()
        {
            visibleItems.Clear();
            if (rootItem == null) return;

            foreach (var child in rootItem.Children)
            {
                visibleItems.AddRange(child.GetVisibleItems());
            }
        }

        private (int position, int total) GetSiblingPosition(InspectionTreeItem item)
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

        private void OpenInfoCard()
        {
            Faction faction = GetSelectedFaction();
            if (faction == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No faction selected.");
                return;
            }
            Find.WindowStack.Add(new Dialog_InfoCard(faction));
        }

        /// <summary>
        /// Finds the Faction object for the current selection by walking up the tree
        /// until a node with Data of type Faction is found.
        /// </summary>
        private Faction GetSelectedFaction()
        {
            if (visibleItems.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                return null;

            var item = visibleItems[selectedIndex];
            while (item != null)
            {
                if (item.Data is Faction f) return f;
                item = item.Parent;
            }
            return null;
        }

        #endregion
    }
}
