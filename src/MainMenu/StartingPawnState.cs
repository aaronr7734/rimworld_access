using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    public static class StartingPawnState
    {
        private static List<PawnTreeItem> hierarchy = new List<PawnTreeItem>();
        private static List<PawnTreeItem> flattenedItems = new List<PawnTreeItem>();
        private static int selectedIndex = 0;
        private static bool isActive = false;
        private static int openedOnFrame = -1;
        private static bool awaitingRenameRebuild = false;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        private const string LevelTrackingKey = "PawnSelection";

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        public static void Open()
        {
            isActive = true;
            openedOnFrame = Time.frameCount;
            selectedIndex = 0;
            typeahead.ClearSearch();
            MenuHelper.ResetLevel(LevelTrackingKey);

            RebuildTree();

            if (flattenedItems.Count > 0)
            {
                // Select first pawn (skip group header)
                for (int i = 0; i < flattenedItems.Count; i++)
                {
                    if (flattenedItems[i].NodeType == PawnNodeType.Pawn)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
                TolkHelper.Speak("CreateCharacters".Translate());
                AnnounceCurrentItem();
            }
        }

        public static void Close()
        {
            isActive = false;
            awaitingRenameRebuild = false;
            hierarchy.Clear();
            flattenedItems.Clear();
            selectedIndex = 0;
            typeahead.ClearSearch();
            MenuHelper.ResetLevel(LevelTrackingKey);
        }

        public static void CheckPendingRenameRebuild()
        {
            if (awaitingRenameRebuild && !WindowlessDialogState.IsActive)
            {
                awaitingRenameRebuild = false;
                RebuildTree();
                AnnounceCurrentItem();
            }
        }

        public static int GetSelectedPawnIndex()
        {
            if (flattenedItems.Count == 0 || selectedIndex < 0 || selectedIndex >= flattenedItems.Count)
                return 0;
            return flattenedItems[selectedIndex].PawnIndex >= 0
                ? flattenedItems[selectedIndex].PawnIndex
                : 0;
        }

        private static void RebuildTree()
        {
            // Save expansion state
            var expandedPawns = new Dictionary<int, HashSet<PawnCategoryType>>();
            foreach (var item in flattenedItems)
            {
                if (item.NodeType == PawnNodeType.Pawn && item.IsExpanded && item.PawnIndex >= 0)
                {
                    var expandedCats = new HashSet<PawnCategoryType>();
                    foreach (var child in item.Children)
                    {
                        if (child.IsExpanded && child.CategoryType.HasValue)
                            expandedCats.Add(child.CategoryType.Value);
                    }
                    expandedPawns[item.PawnIndex] = expandedCats;
                }
            }

            hierarchy = StartingPawnHelper.BuildTree();
            FlattenItems();

            // Restore expansion state
            foreach (var item in flattenedItems)
            {
                if (item.NodeType == PawnNodeType.Pawn && expandedPawns.ContainsKey(item.PawnIndex))
                {
                    item.IsExpanded = true;
                }
            }
            // Re-flatten after restoring pawn expansion
            FlattenItems();

            // Restore category expansion
            foreach (var item in flattenedItems)
            {
                if (item.NodeType == PawnNodeType.Category && item.CategoryType.HasValue
                    && item.Parent != null && expandedPawns.ContainsKey(item.Parent.PawnIndex)
                    && expandedPawns[item.Parent.PawnIndex].Contains(item.CategoryType.Value))
                {
                    item.IsExpanded = true;
                }
            }
            // Final flatten with all state restored
            FlattenItems();

            // Clamp selection
            if (selectedIndex >= flattenedItems.Count)
                selectedIndex = flattenedItems.Count - 1;
            if (selectedIndex < 0)
                selectedIndex = 0;
        }

        public static void RebuildAndAnnounce()
        {
            RebuildTree();
            AnnounceCurrentItem();
        }

        private static void FlattenItems()
        {
            flattenedItems.Clear();
            foreach (var item in hierarchy)
            {
                flattenedItems.Add(item);
                if (item.IsExpandable && item.IsExpanded)
                    FlattenChildren(item);
            }
        }

        private static void FlattenChildren(PawnTreeItem parent)
        {
            foreach (var child in parent.Children)
            {
                flattenedItems.Add(child);
                if (child.IsExpandable && child.IsExpanded)
                    FlattenChildren(child);
            }
        }

        // ===== INPUT HANDLING =====

        public static bool HandleInput(KeyCode key, Event currentEvent)
        {
            if (!isActive || flattenedItems.Count == 0) return false;

            bool ctrl = currentEvent.control;
            bool alt = currentEvent.alt;

            // Alt+R: Randomize
            if (alt && key == KeyCode.R)
            {
                RandomizeCurrentPawn();
                return true;
            }

            // Alt+N: Rename
            if (alt && key == KeyCode.N)
            {
                RenameCurrentPawn();
                return true;
            }

            // Alt+I: Open info card
            if (alt && key == KeyCode.I)
            {
                OpenInfoCard();
                return true;
            }

            // Ctrl+Up/Down: Reorder
            if (ctrl && key == KeyCode.UpArrow)
            {
                ReorderCurrentPawn(-1);
                return true;
            }
            if (ctrl && key == KeyCode.DownArrow)
            {
                ReorderCurrentPawn(1);
                return true;
            }

            // Page Up/Down: Switch pawn preserving position
            if (key == KeyCode.PageUp)
            {
                SwitchPawn(-1);
                return true;
            }
            if (key == KeyCode.PageDown)
            {
                SwitchPawn(1);
                return true;
            }

            // Up/Down: Navigate
            if (key == KeyCode.UpArrow)
            {
                if (typeahead.HasActiveSearch)
                    SelectPreviousMatch();
                else
                    NavigateUp();
                return true;
            }
            if (key == KeyCode.DownArrow)
            {
                if (typeahead.HasActiveSearch)
                    SelectNextMatch();
                else
                    NavigateDown();
                return true;
            }

            // Right: Expand or drill down
            if (key == KeyCode.RightArrow)
            {
                ExpandOrDrillDown();
                return true;
            }

            // Left: Collapse or drill up
            if (key == KeyCode.LeftArrow)
            {
                CollapseOrDrillUp();
                return true;
            }

            // Home/End
            if (key == KeyCode.Home)
            {
                HandleHomeKey(ctrl);
                return true;
            }
            if (key == KeyCode.End)
            {
                HandleEndKey(ctrl);
                return true;
            }

            // ] Right bracket: Context menu
            if (key == KeyCode.RightBracket)
            {
                OpenContextMenu();
                return true;
            }

            // Enter: Start game with confirmation
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                if (Time.frameCount <= openedOnFrame + 1)
                    return true; // Consume but ignore — key repeat from site selection
                ConfirmStartGame();
                return true;
            }

            // Escape: Clear search or go back
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentItem();
                }
                else
                {
                    StartingPawnPatch.DoBack();
                }
                return true;
            }

            // Backspace: Remove search character
            if (key == KeyCode.Backspace)
            {
                if (HandleTypeaheadBackspace())
                    return true;
            }

            return false;
        }

        public static bool HandleCharacterInput(char c)
        {
            if (!isActive || flattenedItems.Count == 0) return false;

            // ] Right bracket: Context menu (fallback for keyboards where ] arrives as character only)
            if (c == ']')
            {
                OpenContextMenu();
                return true;
            }

            // Asterisk: Expand all siblings
            if (c == '*')
            {
                ExpandAllSiblings();
                return true;
            }

            if (char.IsLetterOrDigit(c))
            {
                HandleTypeahead(c);
                return true;
            }

            return false;
        }

        // ===== NAVIGATION =====

        private static void NavigateUp()
        {
            typeahead.ClearSearch();
            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, flattenedItems.Count);
            AnnounceCurrentItem();
        }

        private static void NavigateDown()
        {
            typeahead.ClearSearch();
            selectedIndex = MenuHelper.SelectNext(selectedIndex, flattenedItems.Count);
            AnnounceCurrentItem();
        }

        private static void ExpandOrDrillDown()
        {
            typeahead.ClearSearch();
            var item = flattenedItems[selectedIndex];

            if (!item.IsExpandable)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            if (!item.IsExpanded)
            {
                item.IsExpanded = true;
                FlattenItems();
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentItem();
            }
            else if (item.Children.Count > 0)
            {
                // Move to first child
                int childIdx = flattenedItems.IndexOf(item.Children[0]);
                if (childIdx >= 0)
                {
                    selectedIndex = childIdx;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentItem();
                }
            }
        }

        private static void CollapseOrDrillUp()
        {
            typeahead.ClearSearch();
            var item = flattenedItems[selectedIndex];

            if (item.IsExpandable && item.IsExpanded)
            {
                item.IsExpanded = false;
                FlattenItems();
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentItem();
            }
            else if (item.Parent != null)
            {
                int parentIdx = flattenedItems.IndexOf(item.Parent);
                if (parentIdx >= 0)
                {
                    selectedIndex = parentIdx;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentItem();
                }
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        private static void HandleHomeKey(bool ctrl)
        {
            typeahead.ClearSearch();
            MenuHelper.HandleTreeHomeKey(
                flattenedItems, ref selectedIndex,
                item => item.IndentLevel,
                ctrl,
                () => AnnounceCurrentItem());
        }

        private static void HandleEndKey(bool ctrl)
        {
            typeahead.ClearSearch();
            MenuHelper.HandleTreeEndKey(
                flattenedItems, ref selectedIndex,
                item => item.IndentLevel,
                item => item.IsExpanded,
                item => item.IsExpandable && item.Children.Count > 0,
                ctrl,
                () => AnnounceCurrentItem());
        }

        private static void ExpandAllSiblings()
        {
            typeahead.ClearSearch();
            var current = flattenedItems[selectedIndex];
            int level = current.IndentLevel;
            bool anyExpanded = false;

            // Find sibling range
            foreach (var item in flattenedItems)
            {
                if (item.IndentLevel == level && item.IsExpandable && !item.IsExpanded
                    && IsSibling(item, current))
                {
                    item.IsExpanded = true;
                    anyExpanded = true;
                }
            }

            if (anyExpanded)
            {
                FlattenItems();
                // Re-find our item after flatten
                for (int i = 0; i < flattenedItems.Count; i++)
                {
                    if (ReferenceEquals(flattenedItems[i], current))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                TolkHelper.Speak("All siblings expanded");
            }
        }

        private static bool IsSibling(PawnTreeItem a, PawnTreeItem b)
        {
            return ReferenceEquals(a.Parent, b.Parent);
        }

        // ===== PAGE UP/DOWN: SWITCH PAWN =====

        private static void SwitchPawn(int direction)
        {
            typeahead.ClearSearch();

            // Find current pawn index
            int currentPawnIdx = GetCurrentPawnIndex();
            if (currentPawnIdx < 0) return;

            // Save position within current pawn
            var position = SaveTreePosition();

            // Find all pawn nodes
            var pawnNodes = new List<PawnTreeItem>();
            foreach (var item in hierarchy)
            {
                if (item.NodeType == PawnNodeType.GroupHeader)
                {
                    foreach (var child in item.Children)
                    {
                        if (child.NodeType == PawnNodeType.Pawn)
                            pawnNodes.Add(child);
                    }
                }
            }

            // Find current pawn position in pawn list
            int pawnListIdx = -1;
            for (int i = 0; i < pawnNodes.Count; i++)
            {
                if (pawnNodes[i].PawnIndex == currentPawnIdx)
                {
                    pawnListIdx = i;
                    break;
                }
            }
            if (pawnListIdx < 0) return;

            // Calculate target
            int targetPawnListIdx = pawnListIdx + direction;
            if (targetPawnListIdx < 0 || targetPawnListIdx >= pawnNodes.Count)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            var targetPawn = pawnNodes[targetPawnListIdx];

            // Expand target pawn if source was expanded
            if (position.WasExpanded && !targetPawn.IsExpanded)
            {
                targetPawn.IsExpanded = true;
                FlattenItems();
            }

            // Restore position
            RestoreTreePosition(targetPawn, position);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        private static int GetCurrentPawnIndex()
        {
            if (selectedIndex < 0 || selectedIndex >= flattenedItems.Count)
                return -1;

            var current = flattenedItems[selectedIndex];

            // Walk up to find the pawn node
            while (current != null)
            {
                if (current.NodeType == PawnNodeType.Pawn)
                    return current.PawnIndex;
                current = current.Parent;
            }

            // If on a group header, find the nearest pawn
            return -1;
        }

        private static TreePosition SaveTreePosition()
        {
            var current = flattenedItems[selectedIndex];
            var position = TreePosition.Default;

            // Check if we're on a pawn node
            if (current.NodeType == PawnNodeType.Pawn)
            {
                position.WasExpanded = current.IsExpanded;
                return position;
            }

            // Walk up to find category and pawn
            var item = current;
            while (item != null)
            {
                if (item.NodeType == PawnNodeType.Category && item.CategoryType.HasValue)
                {
                    position.Category = item.CategoryType;
                    position.WasCategoryExpanded = item.IsExpanded;

                    // Find item index within category
                    if (current.NodeType == PawnNodeType.Leaf)
                    {
                        position.ItemIndex = item.Children.IndexOf(current);
                    }
                    else
                    {
                        position.ItemIndex = -1; // On the category node itself
                    }
                }
                if (item.NodeType == PawnNodeType.Pawn)
                {
                    position.WasExpanded = item.IsExpanded;
                    break;
                }
                item = item.Parent;
            }

            return position;
        }

        private static void RestoreTreePosition(PawnTreeItem targetPawn, TreePosition position)
        {
            if (!position.Category.HasValue)
            {
                // Navigate to the pawn node itself
                int idx = flattenedItems.IndexOf(targetPawn);
                if (idx >= 0) selectedIndex = idx;
                return;
            }

            // Find matching category
            PawnTreeItem targetCategory = null;
            foreach (var child in targetPawn.Children)
            {
                if (child.CategoryType == position.Category)
                {
                    targetCategory = child;
                    break;
                }
            }

            if (targetCategory == null)
            {
                // Category doesn't exist on target, land on pawn
                int idx = flattenedItems.IndexOf(targetPawn);
                if (idx >= 0) selectedIndex = idx;
                return;
            }

            if (position.ItemIndex < 0)
            {
                // Was on the category node itself, preserve its expand/collapse state
                targetCategory.IsExpanded = position.WasCategoryExpanded;
                FlattenItems();
                int idx = flattenedItems.IndexOf(targetCategory);
                if (idx >= 0) selectedIndex = idx;
                return;
            }

            // Was on a leaf, expand category and navigate
            targetCategory.IsExpanded = true;
            FlattenItems();

            // Clamp item index
            int clampedIdx = Math.Min(position.ItemIndex, targetCategory.Children.Count - 1);
            if (clampedIdx >= 0 && clampedIdx < targetCategory.Children.Count)
            {
                int idx = flattenedItems.IndexOf(targetCategory.Children[clampedIdx]);
                if (idx >= 0)
                    selectedIndex = idx;
                else
                {
                    idx = flattenedItems.IndexOf(targetCategory);
                    if (idx >= 0) selectedIndex = idx;
                }
            }
            else
            {
                int idx = flattenedItems.IndexOf(targetCategory);
                if (idx >= 0) selectedIndex = idx;
            }
        }

        // ===== ACTIONS =====

        private static void RandomizeCurrentPawn()
        {
            int pawnIdx = GetCurrentPawnIndex();
            if (pawnIdx < 0) return;

            // Save position so user returns to same logical location after reroll
            var position = SaveTreePosition();

            StartingPawnUtility.RandomizePawn(pawnIdx);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            RebuildTree();

            // Restore position within the rerolled pawn
            foreach (var item in flattenedItems)
            {
                if (item.NodeType == PawnNodeType.Pawn && item.PawnIndex == pawnIdx)
                {
                    RestoreTreePosition(item, position);
                    break;
                }
            }

            TolkHelper.Speak("Randomize".Translate());
            AnnounceCurrentItem();
        }

        private static void RenameCurrentPawn()
        {
            int pawnIdx = GetCurrentPawnIndex();
            if (pawnIdx < 0) return;

            var pawn = StartingPawnHelper.GetPawnAtIndex(pawnIdx);
            if (pawn == null) return;

            var allFields = NameFilter.First | NameFilter.Nick | NameFilter.Last | NameFilter.Title;
            awaitingRenameRebuild = true;
            Find.WindowStack.Add(new Dialog_NamePawn(pawn, allFields, allFields, null));
        }

        private static void ReorderCurrentPawn(int direction)
        {
            int currentPawnIdx = GetCurrentPawnIndex();
            if (currentPawnIdx < 0) return;

            var pawns = Find.GameInitData.startingAndOptionalPawns;
            int targetIdx = currentPawnIdx + direction;

            if (targetIdx < 0 || targetIdx >= pawns.Count)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            // Swap pawns in the list
            var temp = pawns[currentPawnIdx];
            pawns[currentPawnIdx] = pawns[targetIdx];
            pawns[targetIdx] = temp;

            // Also reorder the generation requests
            StartingPawnUtility.ReorderRequests(currentPawnIdx, targetIdx);

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            RebuildTree();

            // Navigate to the swapped pawn's new position
            for (int i = 0; i < flattenedItems.Count; i++)
            {
                if (flattenedItems[i].NodeType == PawnNodeType.Pawn && flattenedItems[i].PawnIndex == targetIdx)
                {
                    selectedIndex = i;
                    break;
                }
            }

            // Announce what happened with neighbor context
            var movedPawn = pawns[targetIdx];
            string movedName = movedPawn.LabelShort;

            int startingCount = StartingPawnHelper.GetStartingPawnCount();
            bool crossedBoundary = (currentPawnIdx < startingCount) != (targetIdx < startingCount);

            var announceParts = new List<string>();
            announceParts.Add(movedName);

            if (crossedBoundary)
            {
                string group = targetIdx < startingCount
                    ? "StartingPawnsSelected".Translate()
                    : "StartingPawnsLeftBehind".Translate();
                announceParts.Add(group);
            }

            // Add neighbor context
            string prevName = targetIdx > 0 ? pawns[targetIdx - 1].LabelShort : null;
            string nextName = targetIdx < pawns.Count - 1 ? pawns[targetIdx + 1].LabelShort : null;

            if (prevName != null && nextName != null)
                announceParts.Add($"between {prevName} and {nextName}");
            else if (prevName != null)
                announceParts.Add($"after {prevName}");
            else if (nextName != null)
                announceParts.Add($"before {nextName}");

            TolkHelper.Speak(string.Join(", ", announceParts));
            AnnounceCurrentItem();
        }

        private static void OpenContextMenu()
        {
            try
            {
                int pawnIdx = GetCurrentPawnIndex();
                if (pawnIdx < 0)
                {
                    TolkHelper.Speak("No pawn selected");
                    return;
                }

                var options = StartingPawnHelper.GetContextMenuOptions(pawnIdx, () => RebuildAndAnnounce());
                if (options.Count > 0)
                {
                    WindowlessFloatMenuState.Open(options, colonistOrders: false);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in OpenContextMenu: {ex}");
            }
        }

        private static void ConfirmStartGame()
        {
            // Validate before showing confirmation — CanDoNext() checks names, work types, etc.
            // It also posts Messages to the game's message system if validation fails.
            if (!StartingPawnPatch.CanDoNext())
                return;

            var pawns = Find.GameInitData.startingAndOptionalPawns;
            int startingCount = Find.GameInitData.startingPawnCount;
            var names = new List<string>();
            for (int i = 0; i < startingCount && i < pawns.Count; i++)
                names.Add(pawns[i].LabelShort);

            string pawnList = names.ToCommaList(useAnd: true);
            string message = $"Start the game with {pawnList}?";

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                message,
                () => StartingPawnPatch.DoNext(),
                destructive: false));
        }

        // ===== TYPEAHEAD SEARCH =====

        private static void HandleTypeahead(char c)
        {
            var labels = flattenedItems.Select(i => i.Label).ToList();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        private static bool HandleTypeaheadBackspace()
        {
            if (!typeahead.HasActiveSearch) return false;
            var labels = flattenedItems.Select(i => i.Label).ToList();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0) selectedIndex = newIndex;
                AnnounceWithSearch();
            }
            return true;
        }

        private static void SelectNextMatch()
        {
            if (!typeahead.HasActiveSearch) return;
            int next = typeahead.GetNextMatch(selectedIndex);
            if (next >= 0) { selectedIndex = next; AnnounceWithSearch(); }
        }

        private static void SelectPreviousMatch()
        {
            if (!typeahead.HasActiveSearch) return;
            int prev = typeahead.GetPreviousMatch(selectedIndex);
            if (prev >= 0) { selectedIndex = prev; AnnounceWithSearch(); }
        }

        // ===== ANNOUNCEMENTS =====

        private static void AnnounceCurrentItem()
        {
            if (selectedIndex < 0 || selectedIndex >= flattenedItems.Count) return;

            var item = flattenedItems[selectedIndex];
            var (position, total) = MenuHelper.GetSiblingPosition(
                flattenedItems, selectedIndex, i => i.IndentLevel);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);

            string announcement;

            switch (item.NodeType)
            {
                case PawnNodeType.GroupHeader:
                    // Group headers are structural dividers — no position or level suffix
                    announcement = item.Label;
                    break;

                case PawnNodeType.Pawn:
                    string pawnState = item.IsExpanded ? "expanded" : "collapsed";
                    announcement = $"{item.Label}, {pawnState}";
                    if (!string.IsNullOrEmpty(positionPart))
                        announcement += $" ({positionPart})";
                    announcement += MenuHelper.GetLevelSuffix(LevelTrackingKey, item.IndentLevel, skipLevelOne: false);
                    if (HasInfoCard(item)) announcement += " Inspectable.";
                    break;

                case PawnNodeType.Category:
                    if (!item.IsExpandable)
                    {
                        // Non-expandable categories (e.g. "Traits: None") — treat as plain item
                        announcement = item.Label;
                    }
                    else
                    {
                        string catState = item.IsExpanded ? "expanded" : "collapsed";
                        if (!item.IsExpanded)
                        {
                            string summary = StartingPawnHelper.GetCategorySummary(item);
                            if (!string.IsNullOrEmpty(summary))
                                announcement = $"{item.Label}: {summary}, {catState}";
                            else
                                announcement = $"{item.Label}, {catState}";
                        }
                        else
                        {
                            announcement = $"{item.Label}, {catState}";
                        }
                    }
                    if (!string.IsNullOrEmpty(positionPart))
                        announcement += $" ({positionPart})";
                    announcement += MenuHelper.GetLevelSuffix(LevelTrackingKey, item.IndentLevel, skipLevelOne: false);
                    break;

                default: // Leaf
                    announcement = item.Label;
                    if (!string.IsNullOrEmpty(item.Tooltip))
                    {
                        // Replace newlines with sentence breaks for natural screen reader pauses
                        string spokenTooltip = item.Tooltip
                            .Replace("\r\n", "\n")
                            .Replace("\n\n", ". ")
                            .Replace("\n", ". ")
                            .Trim();
                        announcement += ". " + spokenTooltip;
                    }
                    if (!string.IsNullOrEmpty(positionPart))
                        announcement += $" ({positionPart})";
                    announcement += MenuHelper.GetLevelSuffix(LevelTrackingKey, item.IndentLevel, skipLevelOne: false);
                    if (HasInfoCard(item)) announcement += " Inspectable.";
                    break;
            }

            TolkHelper.Speak(announcement);
        }

        private static void AnnounceWithSearch()
        {
            if (!typeahead.HasActiveSearch) { AnnounceCurrentItem(); return; }
            var item = flattenedItems[selectedIndex];
            TolkHelper.Speak($"{item.Label}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
        }

        private static bool HasInfoCard(PawnTreeItem item)
        {
            if (item.NodeType == PawnNodeType.Pawn && item.Data is Pawn)
                return true;
            if (item.NodeType == PawnNodeType.Leaf)
            {
                if (item.Data is ThingDefCount || item.Data is Hediff || item.Data is XenotypeDef)
                    return true;
            }
            return false;
        }

        private static void OpenInfoCard()
        {
            if (selectedIndex < 0 || selectedIndex >= flattenedItems.Count) return;
            var item = flattenedItems[selectedIndex];

            if (!HasInfoCard(item))
            {
                TolkHelper.Speak("No info card available");
                return;
            }

            if (item.NodeType == PawnNodeType.Pawn && item.Data is Pawn pawn)
                Find.WindowStack.Add(new Dialog_InfoCard(pawn));
            else if (item.Data is ThingDefCount tdc)
                Find.WindowStack.Add(new Dialog_InfoCard(tdc.ThingDef));
            else if (item.Data is Hediff hediff)
                Find.WindowStack.Add(new Dialog_InfoCard(hediff));
            else if (item.Data is XenotypeDef xenotype)
                Find.WindowStack.Add(new Dialog_InfoCard(xenotype));
        }
    }
}
