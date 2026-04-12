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
    public enum PawnEditorContext
    {
        GameStart,
        Wanderer
    }

    public static class StartingPawnState
    {
        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("PawnSelection");
        private static bool isActive = false;
        private static int openedOnFrame = -1;
        private static bool awaitingRenameRebuild = false;
        private static string lastAnnouncedSection = null;

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => treeNav.HasActiveSearch;
        public static PawnEditorContext Context { get; private set; } = PawnEditorContext.GameStart;

        static StartingPawnState()
        {
            treeNav.FormatItemAnnouncement = FormatItemAnnouncement;
            treeNav.FormatSearchAnnouncement = FormatSearchAnnouncement;
            treeNav.AnnounceChildCounts = false;
        }

        public static void Open(PawnEditorContext context = PawnEditorContext.GameStart)
        {
            Context = context;
            isActive = true;
            openedOnFrame = Time.frameCount;
            lastAnnouncedSection = null;

            RebuildTree();

            var visibleItems = treeNav.VisibleItems;
            if (visibleItems.Count > 0)
            {
                // Select first pawn (skip group header if present)
                for (int i = 0; i < visibleItems.Count; i++)
                {
                    var ptd = StartingPawnHelper.GetPawnData(visibleItems[i]);
                    if (ptd != null && ptd.NodeType == PawnNodeType.Pawn)
                    {
                        treeNav.SetSelectedIndex(i);
                        break;
                    }
                }
                string title = context == PawnEditorContext.Wanderer
                    ? "ChooseNewWanderers".Translate() + ". Alt+A to add, Delete to remove."
                    : "CreateCharacters".Translate();
                TolkHelper.Speak(title);
                treeNav.ReannounceCurrentItem();
            }
        }

        public static void Close()
        {
            isActive = false;
            awaitingRenameRebuild = false;
            lastAnnouncedSection = null;
            treeNav.Reset();
        }

        public static void CheckPendingRenameRebuild()
        {
            if (awaitingRenameRebuild && !WindowlessDialogState.IsActive)
            {
                awaitingRenameRebuild = false;
                RebuildTree();
                treeNav.ReannounceCurrentItem();
            }
        }

        public static int GetSelectedPawnIndex()
        {
            var item = treeNav.SelectedItem;
            if (item == null) return 0;
            var ptd = StartingPawnHelper.GetPawnData(item);
            if (ptd != null && ptd.PawnIndex >= 0)
                return ptd.PawnIndex;
            return 0;
        }

        private static void RebuildTree()
        {
            // Save expansion state. Walk the full tree rather than VisibleItems
            // because "Rashad Hates Treeviews" submenu mode hides expanded
            // parents from the flat list.
            var expandedPawns = new Dictionary<int, HashSet<PawnCategoryType>>();
            if (treeNav.RootItem != null)
            {
                foreach (var pawnItem in GetAllNodes(treeNav.RootItem))
                {
                    var ptd = StartingPawnHelper.GetPawnData(pawnItem);
                    if (ptd == null) continue;
                    if (ptd.NodeType != PawnNodeType.Pawn || !pawnItem.IsExpanded || ptd.PawnIndex < 0)
                        continue;
                    var expandedCats = new HashSet<PawnCategoryType>();
                    foreach (var child in pawnItem.Children)
                    {
                        var childPtd = StartingPawnHelper.GetPawnData(child);
                        if (childPtd != null && child.IsExpanded && childPtd.CategoryType.HasValue)
                            expandedCats.Add(childPtd.CategoryType.Value);
                    }
                    expandedPawns[ptd.PawnIndex] = expandedCats;
                }
            }

            var root = StartingPawnHelper.BuildTree();
            int savedIndex = treeNav.SelectedIndex;
            treeNav.Initialize(root, savedIndex);

            // Restore expansion state across the full tree.
            foreach (var node in GetAllNodes(root))
            {
                var ptd = StartingPawnHelper.GetPawnData(node);
                if (ptd == null) continue;
                if (ptd.NodeType == PawnNodeType.Pawn && expandedPawns.ContainsKey(ptd.PawnIndex))
                {
                    node.IsExpanded = true;
                }
                else if (ptd.NodeType == PawnNodeType.Category && ptd.CategoryType.HasValue && node.Parent != null)
                {
                    var parentPtd = StartingPawnHelper.GetPawnData(node.Parent);
                    if (parentPtd != null
                        && expandedPawns.TryGetValue(parentPtd.PawnIndex, out var cats)
                        && cats.Contains(ptd.CategoryType.Value))
                    {
                        node.IsExpanded = true;
                    }
                }
            }
            treeNav.RebuildVisibleList();

            // Clamp selection
            if (treeNav.SelectedIndex >= treeNav.Count)
                treeNav.SetSelectedIndex(Math.Max(0, treeNav.Count - 1));
        }

        public static void RebuildAndAnnounce()
        {
            RebuildTree();
            treeNav.ReannounceCurrentItem();
        }

        // ===== INPUT HANDLING =====

        public static bool HandleInput(Event ev)
        {
            if (!isActive || ev.type != EventType.KeyDown) return false;
            if (treeNav.Count == 0) return false;

            KeyCode key = ev.keyCode;
            bool ctrl = ev.control;

            // --- Pre-intercept custom keys before delegating to treeNav ---

            // Alt+F: Open filter editor
            if (KeyboardHelper.IsAltHeld && key == KeyCode.F)
            {
                PawnFilterState.Open();
                return true;
            }

            // Alt+R: Randomize
            if (KeyboardHelper.IsAltHeld && key == KeyCode.R)
            {
                RandomizeCurrentPawn();
                return true;
            }

            // Alt+N: Rename
            if (KeyboardHelper.IsAltHeld && key == KeyCode.N)
            {
                RenameCurrentPawn();
                return true;
            }

            // Alt+I: Open info card
            if (KeyboardHelper.IsAltHeld && key == KeyCode.I)
            {
                OpenInfoCard();
                return true;
            }

            // Alt+A: Add pawn (wanderer only)
            if (KeyboardHelper.IsAltHeld && key == KeyCode.A && Context == PawnEditorContext.Wanderer)
            {
                AddWandererPawn();
                return true;
            }

            // Delete: Remove current pawn (wanderer only)
            if (key == KeyCode.Delete && Context == PawnEditorContext.Wanderer)
            {
                RemoveWandererPawn();
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

            // ] Right bracket: Context menu
            if (key == KeyCode.RightBracket)
            {
                OpenContextMenu();
                return true;
            }

            // Enter: Confirm (context-dependent) — override treeNav's default toggle
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                if (Time.frameCount <= openedOnFrame + 1)
                    return true; // Consume but ignore — key repeat from prior screen
                if (Context == PawnEditorContext.Wanderer)
                    ConfirmWanderers();
                else
                    ConfirmStartGame();
                return true;
            }

            // Escape: Clear search or go back/close (context-dependent)
            if (key == KeyCode.Escape)
            {
                if (treeNav.HasActiveSearch)
                {
                    treeNav.Typeahead.ClearSearchAndAnnounce();
                    treeNav.ReannounceCurrentItem();
                }
                else if (Context == PawnEditorContext.Wanderer)
                {
                    WandererPatch.CloseDialog();
                }
                else
                {
                    StartingPawnPatch.DoBack();
                }
                return true;
            }

            // Delegate all standard tree navigation to treeNav
            // (Up/Down, Left/Right, Home/End, *, Space, typeahead, Backspace)
            return treeNav.HandleInput(ev);
        }

        /// <summary>
        /// Finds the index of an item in the visible items list.
        /// IReadOnlyList does not have IndexOf, so we use a loop.
        /// </summary>
        private static int FindVisibleIndex(InspectionTreeItem target)
        {
            var items = treeNav.VisibleItems;
            for (int i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], target))
                    return i;
            }
            return -1;
        }

        // ===== PAGE UP/DOWN: SWITCH PAWN =====

        private static void SwitchPawn(int direction)
        {
            treeNav.Typeahead.ClearSearch();

            // Find current pawn index
            int currentPawnIdx = GetCurrentPawnIndex();
            if (currentPawnIdx < 0) return;

            // Save position within current pawn
            var position = SaveTreePosition();

            // Find all pawn nodes
            var pawnNodes = new List<InspectionTreeItem>();
            foreach (var item in treeNav.VisibleItems)
            {
                var ptd = StartingPawnHelper.GetPawnData(item);
                if (ptd != null && ptd.NodeType == PawnNodeType.Pawn)
                    pawnNodes.Add(item);
            }
            // Also check collapsed pawns from root children
            if (treeNav.RootItem != null)
            {
                foreach (var child in treeNav.RootItem.Children)
                {
                    var ptd = StartingPawnHelper.GetPawnData(child);
                    if (ptd != null && ptd.NodeType == PawnNodeType.Pawn && !pawnNodes.Contains(child))
                        pawnNodes.Add(child);
                }
                // Sort by PawnIndex for consistent ordering
                pawnNodes.Sort((a, b) =>
                {
                    var pa = StartingPawnHelper.GetPawnData(a);
                    var pb = StartingPawnHelper.GetPawnData(b);
                    return (pa?.PawnIndex ?? 0).CompareTo(pb?.PawnIndex ?? 0);
                });
            }

            // Find current pawn position in pawn list
            int pawnListIdx = -1;
            for (int i = 0; i < pawnNodes.Count; i++)
            {
                var ptd = StartingPawnHelper.GetPawnData(pawnNodes[i]);
                if (ptd != null && ptd.PawnIndex == currentPawnIdx)
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
                treeNav.RebuildVisibleList();
            }

            // Restore position
            RestoreTreePosition(targetPawn, position);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            treeNav.ReannounceCurrentItem();
        }

        private static int GetCurrentPawnIndex()
        {
            var item = treeNav.SelectedItem;
            if (item == null) return -1;

            // Walk up to find the pawn node
            var current = item;
            while (current != null)
            {
                var ptd = StartingPawnHelper.GetPawnData(current);
                if (ptd != null && ptd.NodeType == PawnNodeType.Pawn)
                    return ptd.PawnIndex;
                current = current.Parent;
            }

            // If on a group header, find the nearest pawn
            return -1;
        }

        private static TreePosition SaveTreePosition()
        {
            var current = treeNav.SelectedItem;
            if (current == null) return TreePosition.Default;

            var position = TreePosition.Default;
            var currentPtd = StartingPawnHelper.GetPawnData(current);

            // Check if we're on a pawn node
            if (currentPtd != null && currentPtd.NodeType == PawnNodeType.Pawn)
            {
                position.WasExpanded = current.IsExpanded;
                return position;
            }

            // Walk up to find category and pawn
            var item = current;
            while (item != null)
            {
                var ptd = StartingPawnHelper.GetPawnData(item);
                if (ptd == null) { item = item.Parent; continue; }

                if (ptd.NodeType == PawnNodeType.Category && ptd.CategoryType.HasValue)
                {
                    position.Category = ptd.CategoryType;
                    position.WasCategoryExpanded = item.IsExpanded;

                    // Find item index within category
                    if (currentPtd != null && currentPtd.NodeType == PawnNodeType.Leaf)
                    {
                        position.ItemIndex = item.Children.IndexOf(current);
                    }
                    else
                    {
                        position.ItemIndex = -1; // On the category node itself
                    }
                }
                if (ptd.NodeType == PawnNodeType.Pawn)
                {
                    position.WasExpanded = item.IsExpanded;
                    break;
                }
                item = item.Parent;
            }

            return position;
        }

        private static void RestoreTreePosition(InspectionTreeItem targetPawn, TreePosition position)
        {
            if (!position.Category.HasValue)
            {
                // Navigate to the pawn node itself
                int idx = FindVisibleIndex(targetPawn);
                if (idx >= 0) treeNav.SetSelectedIndex(idx);
                return;
            }

            // Find matching category
            InspectionTreeItem targetCategory = null;
            foreach (var child in targetPawn.Children)
            {
                var ptd = StartingPawnHelper.GetPawnData(child);
                if (ptd != null && ptd.CategoryType == position.Category)
                {
                    targetCategory = child;
                    break;
                }
            }

            if (targetCategory == null)
            {
                // Category doesn't exist on target, land on pawn
                int idx = FindVisibleIndex(targetPawn);
                if (idx >= 0) treeNav.SetSelectedIndex(idx);
                return;
            }

            // Ensure the pawn itself is expanded so the category/leaf is reachable.
            // Matters in submenu mode, where an unexpanded pawn hides its entire subtree.
            if (!targetPawn.IsExpanded)
            {
                targetPawn.IsExpanded = true;
                treeNav.RebuildVisibleList();
            }

            if (position.ItemIndex < 0)
            {
                // Was on the category node itself, preserve its expand/collapse state
                targetCategory.IsExpanded = position.WasCategoryExpanded;
                treeNav.RebuildVisibleList();
                int idx = FindVisibleIndex(targetCategory);
                if (idx >= 0) treeNav.SetSelectedIndex(idx);
                return;
            }

            // Was on a leaf, expand category and navigate
            targetCategory.IsExpanded = true;
            treeNav.RebuildVisibleList();

            // Clamp item index
            int clampedIdx = Math.Min(position.ItemIndex, targetCategory.Children.Count - 1);
            if (clampedIdx >= 0 && clampedIdx < targetCategory.Children.Count)
            {
                int idx = FindVisibleIndex(targetCategory.Children[clampedIdx]);
                if (idx >= 0)
                    treeNav.SetSelectedIndex(idx);
                else
                {
                    idx = FindVisibleIndex(targetCategory);
                    if (idx >= 0) treeNav.SetSelectedIndex(idx);
                }
            }
            else
            {
                int idx = FindVisibleIndex(targetCategory);
                if (idx >= 0) treeNav.SetSelectedIndex(idx);
            }
        }

        // ===== ACTIONS =====

        private static int rerollPawnIdx = -1;
        private static TreePosition rerollSavedPosition;

        private static void RandomizeCurrentPawn()
        {
            RandomizePawnAt(GetCurrentPawnIndex());
        }

        public static void RandomizePawnAt(int pawnIdx)
        {
            if (pawnIdx < 0) return;

            // Save position so user returns to same logical location after reroll
            rerollPawnIdx = pawnIdx;
            rerollSavedPosition = SaveTreePosition();

            // If filters are active, PawnFilterRandomizePatch routes the call
            // into RerollState, which owns tree rebuild, position restore, and
            // announcement via OnRerollComplete (synchronously on first-attempt
            // match, otherwise across frames). Don't duplicate that work here.
            bool filtersActive = PawnFilterData.HasActiveFilters();
            StartingPawnUtility.RandomizePawn(pawnIdx);
            if (filtersActive)
                return;

            // No filters active or immediate match — finalize now
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            RebuildTree();
            RestoreRerollPosition();

            string randomizeAnnouncement = "Randomize".Translate();
            if (PawnFilterData.HasActiveFilters())
            {
                if (PawnFilterData.LastRerollSucceeded)
                    randomizeAnnouncement += $". Found match in {PawnFilterData.LastRerollAttempts} attempts.";
                else
                    randomizeAnnouncement += $". No match after {PawnFilterData.LastRerollAttempts} attempts, keeping last result.";
            }

            // Append pawn summary (age, traits, top skills)
            var pawn = StartingPawnHelper.GetPawnAtIndex(pawnIdx);
            if (pawn != null)
            {
                string summary = StartingPawnHelper.GetPawnRollSummary(pawn);
                if (!string.IsNullOrEmpty(summary))
                    randomizeAnnouncement += $". {summary}";
            }
            TolkHelper.Speak(randomizeAnnouncement);
            treeNav.ReannounceCurrentItem();
        }

        public static void OnRerollComplete(bool success, int attempts, bool cancelled)
        {
            int savedPawnIdx = rerollPawnIdx;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            RebuildTree();
            RestoreRerollPosition();

            string announcement;
            if (cancelled)
                announcement = $"Stopped at {attempts} attempts.";
            else if (success)
                announcement = $"Found match in {attempts} attempts.";
            else
                announcement = $"No match after {attempts} attempts, keeping last result.";

            // Append pawn summary (age, traits, top skills)
            var pawn = StartingPawnHelper.GetPawnAtIndex(savedPawnIdx);
            if (pawn != null)
            {
                string summary = StartingPawnHelper.GetPawnRollSummary(pawn);
                if (!string.IsNullOrEmpty(summary))
                    announcement += $". {summary}";
            }

            TolkHelper.Speak(announcement);
            treeNav.ReannounceCurrentItem();
        }

        private static void RestoreRerollPosition()
        {
            if (rerollPawnIdx < 0) return;
            var pawnNode = FindPawnNode(rerollPawnIdx);
            if (pawnNode != null)
                RestoreTreePosition(pawnNode, rerollSavedPosition);
            rerollPawnIdx = -1;
        }

        // Walk the full tree (not just VisibleItems). In submenu mode
        // ("Rashad Hates Treeviews"), expanded parents are hidden from the
        // flat visible list, so scanning VisibleItems would miss a pawn node
        // whose children are currently the ones visible.
        private static InspectionTreeItem FindPawnNode(int pawnIdx)
        {
            var root = treeNav.RootItem;
            if (root == null) return null;
            foreach (var child in root.Children)
            {
                var found = FindPawnNodeRecursive(child, pawnIdx);
                if (found != null) return found;
            }
            return null;
        }

        private static InspectionTreeItem FindPawnNodeRecursive(InspectionTreeItem node, int pawnIdx)
        {
            var ptd = StartingPawnHelper.GetPawnData(node);
            if (ptd != null && ptd.NodeType == PawnNodeType.Pawn && ptd.PawnIndex == pawnIdx)
                return node;
            foreach (var child in node.Children)
            {
                var found = FindPawnNodeRecursive(child, pawnIdx);
                if (found != null) return found;
            }
            return null;
        }

        private static IEnumerable<InspectionTreeItem> GetAllNodes(InspectionTreeItem root)
        {
            if (root == null) yield break;
            var stack = new Stack<InspectionTreeItem>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;
                foreach (var child in node.Children)
                    stack.Push(child);
            }
        }

        private static void RenameCurrentPawn()
        {
            RenamePawnAt(GetCurrentPawnIndex());
        }

        public static void RenamePawnAt(int pawnIdx)
        {
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
            foreach (var item in treeNav.VisibleItems)
            {
                var ptd = StartingPawnHelper.GetPawnData(item);
                if (ptd != null && ptd.NodeType == PawnNodeType.Pawn && ptd.PawnIndex == targetIdx)
                {
                    treeNav.SetSelectedIndex(FindVisibleIndex(item));
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
            treeNav.ReannounceCurrentItem();
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

        // ===== WANDERER-SPECIFIC ACTIONS =====

        private static void ConfirmWanderers()
        {
            var pawns = Find.GameInitData.startingAndOptionalPawns;
            var names = new List<string>();
            for (int i = 0; i < pawns.Count; i++)
                names.Add(pawns[i].LabelShort);

            string pawnList = names.ToCommaList(useAnd: true);
            string message = "Confirm".Translate() + ": " + pawnList + "?";

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                message,
                () => WandererPatch.ConfirmWanderers(),
                destructive: false));
        }

        private static void AddWandererPawn()
        {
            var pawns = Find.GameInitData.startingAndOptionalPawns;
            if (pawns.Count >= 6)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Maximum 6");
                return;
            }

            WandererPatch.AddPawn();
            RebuildTree();

            // Navigate to the newly added pawn (last pawn node)
            for (int i = treeNav.Count - 1; i >= 0; i--)
            {
                var ptd = StartingPawnHelper.GetPawnData(treeNav.VisibleItems[i]);
                if (ptd != null && ptd.NodeType == PawnNodeType.Pawn)
                {
                    treeNav.SetSelectedIndex(i);
                    break;
                }
            }

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            TolkHelper.Speak("Pawn added");
            treeNav.ReannounceCurrentItem();
        }

        private static void RemoveWandererPawn()
        {
            int pawnIdx = GetCurrentPawnIndex();
            if (pawnIdx < 0) return;

            var pawns = Find.GameInitData.startingAndOptionalPawns;
            if (pawns.Count <= 1)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Minimum 1");
                return;
            }

            string removedName = pawns[pawnIdx].LabelShort;
            WandererPatch.RemovePawn(pawnIdx);
            RebuildTree();

            // Clamp selection
            if (treeNav.SelectedIndex >= treeNav.Count)
                treeNav.SetSelectedIndex(Math.Max(0, treeNav.Count - 1));

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            TolkHelper.Speak(removedName + " removed");
            treeNav.ReannounceCurrentItem();
        }

        // ===== ANNOUNCEMENTS =====

        private static string FormatItemAnnouncement(InspectionTreeItem item)
        {
            var ptd = StartingPawnHelper.GetPawnData(item);
            if (ptd == null)
            {
                // Fallback for items without PawnTreeData
                return treeNav.DefaultFormatItemAnnouncement(item);
            }

            var (position, total) = treeNav.GetSiblingPosition(item);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);

            string announcement;

            switch (ptd.NodeType)
            {
                case PawnNodeType.Pawn:
                    string pawnState = item.IsExpanded ? "expanded" : "collapsed";
                    announcement = $"{item.Label}, {pawnState}";
                    if (!string.IsNullOrEmpty(positionPart))
                        announcement += $" ({positionPart})";
                    announcement += MenuHelper.GetLevelSuffix("PawnSelection", item.IndentLevel, skipLevelOne: false);
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
                    announcement += MenuHelper.GetLevelSuffix("PawnSelection", item.IndentLevel, skipLevelOne: false);
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
                    announcement += MenuHelper.GetLevelSuffix("PawnSelection", item.IndentLevel, skipLevelOne: false);
                    if (HasInfoCard(item)) announcement += " Inspectable.";
                    break;
            }

            // Prepend "Selected / Left behind" section name when crossing the
            // boundary (info-card style). Section lives on the pawn node's
            // Description; leaves/categories inherit via their pawn ancestor.
            string sectionName = GetSectionForItem(item);
            if (!string.IsNullOrEmpty(sectionName) && sectionName != lastAnnouncedSection)
            {
                announcement = $"{sectionName}. {announcement}";
                lastAnnouncedSection = sectionName;
            }
            else if (string.IsNullOrEmpty(sectionName))
            {
                lastAnnouncedSection = null;
            }

            return announcement;
        }

        private static string GetSectionForItem(InspectionTreeItem item)
        {
            var walk = item;
            while (walk != null)
            {
                if (!string.IsNullOrEmpty(walk.Description))
                    return walk.Description;
                walk = walk.Parent;
            }
            return null;
        }

        private static string FormatSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead)
        {
            return $"{item.Label}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";
        }

        private static bool HasInfoCard(InspectionTreeItem item)
        {
            var ptd = StartingPawnHelper.GetPawnData(item);
            if (ptd == null) return false;

            if (ptd.NodeType == PawnNodeType.Leaf)
            {
                if (ptd.DomainData is ThingDefCount || ptd.DomainData is XenotypeDef)
                    return true;
            }
            return false;
        }

        private static void OpenInfoCard()
        {
            var item = treeNav.SelectedItem;
            if (item == null) return;

            if (!HasInfoCard(item))
            {
                TolkHelper.Speak("No info card available");
                return;
            }

            var ptd = StartingPawnHelper.GetPawnData(item);
            if (ptd == null) return;

            if (ptd.DomainData is ThingDefCount tdc)
                Find.WindowStack.Add(new Dialog_InfoCard(tdc.ThingDef));
            else if (ptd.DomainData is XenotypeDef xenotype)
                Find.WindowStack.Add(new Dialog_InfoCard(xenotype));
        }
    }
}
