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
    /// Manages keyboard navigation state for Dialog_InfoCard.
    /// Provides tree-based navigation through Stats, Character, Health, Records, and Permits tabs.
    /// </summary>
    public static class InfoCardState
    {
        public static bool IsActive { get; private set; } = false;

        private static Dialog_InfoCard currentDialog = null;
        private static InspectionTreeItem rootItem = null;
        private static List<InspectionTreeItem> visibleItems = null;
        private static int selectedIndex = 0;

        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        public static TypeaheadSearchHelper Typeahead => typeahead;

        // State stack for nested info card support
        private class SavedCardState
        {
            public Dialog_InfoCard dialog;
            public InspectionTreeItem rootItem;
            public List<InspectionTreeItem> visibleItems;
            public int selectedIndex;
        }
        private static Stack<SavedCardState> cardStack = new Stack<SavedCardState>();

        // Flag to prevent PostClose from interfering when CloseInfoCard is driving
        private static bool closingFromAccessibility = false;

        // Tracks whether InfoCardState opened the current float menu (for multi-hyperlink selection).
        // When true, InfoCardState delegates input to the float menu instead of handling it.
        // When false, a float menu from another context (e.g., recipe selection) is active
        // and InfoCardState should handle its own input normally.
        private static bool ownsFloatMenu = false;
        public static bool IsClosingFromAccessibility => closingFromAccessibility;
        public static bool HasSavedState => cardStack.Count > 0;

        /// <summary>
        /// Opens the Info Card accessibility state for a dialog.
        /// </summary>
        /// <param name="dialog">The dialog to attach to</param>
        /// <param name="announceOpening">Whether to announce immediately (false to delay for stats to load)</param>
        public static void Open(Dialog_InfoCard dialog, bool announceOpening = true)
        {
            try
            {
                if (dialog == null)
                    return;

                currentDialog = dialog;
                IsActive = true;

                // Disable Enter-to-close and Escape-to-close since we handle both for navigation
                dialog.closeOnAccept = false;
                dialog.closeOnCancel = false;

                // Allow multiple info cards to coexist for nested navigation.
                // RimWorld's default (onlyOneOfTypeAllowed = true) causes WindowStack.Add
                // to remove the existing Dialog_InfoCard when opening a nested one.
                dialog.onlyOneOfTypeAllowed = false;

                // Build the tree (stats may not be populated yet on first frame)
                rootItem = InfoCardTreeBuilder.BuildTree(dialog);
                RebuildVisibleList();
                selectedIndex = 0;

                MenuHelper.ResetLevel("InfoCard");
                typeahead.ClearSearch();
                ownsFloatMenu = false;

                if (announceOpening)
                {
                    SoundDefOf.TabOpen.PlayOneShotOnCamera();
                    AnnounceOpening();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardState] Error opening: {ex.Message}");
                Close();
            }
        }

        /// <summary>
        /// Rebuilds the tree and announces opening. Called after stats have loaded.
        /// </summary>
        public static void RebuildAndAnnounce()
        {
            if (!IsActive || currentDialog == null)
                return;

            try
            {
                // Rebuild tree now that stats are populated
                rootItem = InfoCardTreeBuilder.BuildTree(currentDialog);
                RebuildVisibleList();
                selectedIndex = 0;

                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                AnnounceOpening();
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardState] Error rebuilding: {ex.Message}");
            }
        }

        /// <summary>
        /// Closes the Info Card accessibility state.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            currentDialog = null;
            rootItem = null;
            visibleItems = null;
            selectedIndex = 0;
            cardStack.Clear();
            ownsFloatMenu = false;
            typeahead.ClearSearch();
            MenuHelper.ResetLevel("InfoCard");
        }

        /// <summary>
        /// Selects the next item (Down arrow).
        /// </summary>
        public static void SelectNext()
        {
            if (!IsActive || visibleItems == null || visibleItems.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, visibleItems.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Selects the previous item (Up arrow).
        /// </summary>
        public static void SelectPrevious()
        {
            if (!IsActive || visibleItems == null || visibleItems.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, visibleItems.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the next category or subcategory header (Page Down).
        /// </summary>
        public static void JumpToNextCategory()
        {
            if (!IsActive || visibleItems == null || visibleItems.Count == 0)
                return;

            typeahead.ClearSearch();

            // Search forward from current position
            for (int i = selectedIndex + 1; i < visibleItems.Count; i++)
            {
                var item = visibleItems[i];
                if (item.Type == InspectionTreeItem.ItemType.Category ||
                    item.Type == InspectionTreeItem.ItemType.SubCategory)
                {
                    selectedIndex = i;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    return;
                }
            }

            // Wrap to beginning (if enabled)
            if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                for (int i = 0; i <= selectedIndex; i++)
                {
                    var item = visibleItems[i];
                    if (item.Type == InspectionTreeItem.ItemType.Category ||
                        item.Type == InspectionTreeItem.ItemType.SubCategory)
                    {
                        selectedIndex = i;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
                        return;
                    }
                }
            }

            // No categories found or wrap disabled
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Jumps to the previous category or subcategory header (Page Up).
        /// </summary>
        public static void JumpToPreviousCategory()
        {
            if (!IsActive || visibleItems == null || visibleItems.Count == 0)
                return;

            typeahead.ClearSearch();

            // Search backward from current position
            for (int i = selectedIndex - 1; i >= 0; i--)
            {
                var item = visibleItems[i];
                if (item.Type == InspectionTreeItem.ItemType.Category ||
                    item.Type == InspectionTreeItem.ItemType.SubCategory)
                {
                    selectedIndex = i;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    return;
                }
            }

            // Wrap to end (if enabled)
            if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                for (int i = visibleItems.Count - 1; i >= selectedIndex; i--)
                {
                    var item = visibleItems[i];
                    if (item.Type == InspectionTreeItem.ItemType.Category ||
                        item.Type == InspectionTreeItem.ItemType.SubCategory)
                    {
                        selectedIndex = i;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
                        return;
                    }
                }
            }

            // No categories found or wrap disabled
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Jumps to the first sibling at the same indent level (Home key).
        /// </summary>
        private static void JumpToFirst()
        {
            if (visibleItems == null || visibleItems.Count == 0) return;
            MenuHelper.HandleTreeHomeKey(visibleItems, ref selectedIndex, item => item.IndentLevel, false, ClearAndAnnounce);
        }

        /// <summary>
        /// Jumps to the last item in the current scope (End key).
        /// If on an expanded node, jumps to its last visible descendant.
        /// Otherwise, jumps to last sibling at same level.
        /// </summary>
        private static void JumpToLast()
        {
            if (visibleItems == null || visibleItems.Count == 0) return;
            MenuHelper.HandleTreeEndKey(visibleItems, ref selectedIndex, item => item.IndentLevel,
                item => item.IsExpanded, item => item.Children != null && item.Children.Count > 0, false, ClearAndAnnounce);
        }

        /// <summary>
        /// Jumps to the absolute first item in the entire tree (Ctrl+Home).
        /// </summary>
        private static void JumpToAbsoluteFirst()
        {
            if (visibleItems == null || visibleItems.Count == 0) return;
            MenuHelper.HandleTreeHomeKey(visibleItems, ref selectedIndex, item => item.IndentLevel, true, ClearAndAnnounce);
        }

        /// <summary>
        /// Jumps to the absolute last item in the entire tree (Ctrl+End).
        /// </summary>
        private static void JumpToAbsoluteLast()
        {
            if (visibleItems == null || visibleItems.Count == 0) return;
            MenuHelper.HandleTreeEndKey(visibleItems, ref selectedIndex, item => item.IndentLevel,
                item => item.IsExpanded, item => item.Children != null && item.Children.Count > 0, true, ClearAndAnnounce);
        }

        /// <summary>
        /// Clears typeahead search and announces the current selection.
        /// </summary>
        private static void ClearAndAnnounce()
        {
            typeahead?.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Expands the selected item (Right arrow).
        /// WCAG behavior:
        /// - On closed node: Open node, focus stays on current item
        /// - On open node: Move to first child
        /// - On end node: Reject sound
        /// </summary>
        public static void Expand()
        {
            if (!IsActive || visibleItems == null || selectedIndex >= visibleItems.Count)
                return;

            typeahead.ClearSearch();
            var item = visibleItems[selectedIndex];

            // End node (not expandable) - reject
            if (!item.IsExpandable)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Cannot expand this item.", SpeechPriority.High);
                return;
            }

            // Already expanded - move to first child
            if (item.IsExpanded)
            {
                MoveToFirstChild();
                return;
            }

            // Collapsed node - expand it
            if (item.OnActivate != null && item.Children.Count == 0)
            {
                item.OnActivate();
            }

            if (item.Children.Count == 0)
            {
                // Item was marked expandable but has no content - correct its state
                item.IsExpandable = false;
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
                return;
            }

            item.IsExpanded = true;
            RebuildVisibleList();
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Expands all sibling categories at the same level as the current item.
        /// WCAG tree view pattern: * key expands all siblings.
        /// </summary>
        public static void ExpandAllSiblings()
        {
            if (!IsActive || visibleItems == null || selectedIndex >= visibleItems.Count)
                return;

            typeahead.ClearSearch();

            var currentItem = visibleItems[selectedIndex];

            // Get siblings - items with the same parent
            List<InspectionTreeItem> siblings;
            if (currentItem.Parent == null || currentItem.Parent == rootItem)
            {
                siblings = rootItem.Children;
            }
            else
            {
                siblings = currentItem.Parent.Children;
            }

            // Find all collapsed sibling nodes that can be expanded
            var collapsedSiblings = new List<InspectionTreeItem>();
            foreach (var sibling in siblings)
            {
                if (sibling.IsExpandable && !sibling.IsExpanded)
                {
                    collapsedSiblings.Add(sibling);
                }
            }

            // Check if there are any expandable items at this level at all
            bool hasExpandableItems = false;
            foreach (var sibling in siblings)
            {
                if (sibling.IsExpandable)
                {
                    hasExpandableItems = true;
                    break;
                }
            }

            if (!hasExpandableItems)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No categories to expand at this level.");
                return;
            }

            if (collapsedSiblings.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("All categories already expanded at this level.");
                return;
            }

            // Expand all collapsed siblings
            int expandedCount = 0;
            foreach (var sibling in collapsedSiblings)
            {
                // Trigger lazy loading if needed
                if (sibling.OnActivate != null && sibling.Children.Count == 0)
                {
                    sibling.OnActivate();
                }

                // Only count as expanded if there are children to show
                if (sibling.Children.Count > 0)
                {
                    sibling.IsExpanded = true;
                    expandedCount++;
                }
            }

            // Rebuild the visible items list
            RebuildVisibleList();

            // Announce result
            if (expandedCount == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No categories to expand at this level.");
            }
            else
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                string categoryWord = expandedCount == 1 ? "category" : "categories";
                TolkHelper.Speak($"Expanded {expandedCount} {categoryWord}.");
            }
        }

        /// <summary>
        /// Collapses the selected item (Left arrow).
        /// WCAG behavior:
        /// - On open node: Close node, focus stays on current item
        /// - On closed node: Move to parent
        /// - On end node: Move to parent
        /// </summary>
        public static void Collapse()
        {
            if (!IsActive || visibleItems == null || selectedIndex >= visibleItems.Count)
                return;

            typeahead.ClearSearch();
            var item = visibleItems[selectedIndex];

            // Case 1: Item is expandable and expanded - collapse it
            if (item.IsExpandable && item.IsExpanded)
            {
                item.IsExpanded = false;
                RebuildVisibleList();

                if (selectedIndex >= visibleItems.Count)
                    selectedIndex = Math.Max(0, visibleItems.Count - 1);

                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
                return;
            }

            // Case 2: Move to parent
            MoveToParent();
        }

        /// <summary>
        /// Activates the selected item (Enter key).
        /// For expandable items: expands them.
        /// For action items: executes the action.
        /// </summary>
        public static void ActivateItem()
        {
            if (!IsActive || visibleItems == null || selectedIndex >= visibleItems.Count)
                return;

            var item = visibleItems[selectedIndex];

            // Handle expandable items
            if (item.IsExpandable)
            {
                if (!item.IsExpanded)
                {
                    Expand();
                }
                else
                {
                    // Already expanded - don't re-trigger OnActivate
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("Already expanded.");
                }
                return;
            }

            // For non-expandable items with actions, execute the action
            if (item.OnActivate != null)
            {
                item.OnActivate();
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            // Otherwise, nothing to do
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("No action available for this item.");
        }

        /// <summary>
        /// Closes the current Info Card. If nested, restores the outer card's state.
        /// </summary>
        public static void CloseInfoCard()
        {
            if (!IsActive)
                return;

            if (cardStack.Count > 0)
            {
                // Nested card: remove inner dialog without calling Dialog_InfoCard.Close()
                // (which would clear RimWorld's static history). Use TryRemove instead.
                closingFromAccessibility = true;
                if (currentDialog != null)
                    Find.WindowStack.TryRemove(currentDialog, doCloseSound: false);
                closingFromAccessibility = false;

                // Restore outer card state (cursor position, expansion state preserved)
                var saved = cardStack.Pop();
                currentDialog = saved.dialog;
                rootItem = saved.rootItem;
                visibleItems = saved.visibleItems;
                selectedIndex = saved.selectedIndex;
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
            }
            else
            {
                // No outer card - actually close the dialog
                closingFromAccessibility = true;
                if (currentDialog != null)
                    currentDialog.Close();
                closingFromAccessibility = false;

                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
                TolkHelper.Speak("Info card closed.");
            }
        }

        /// <summary>
        /// Restores state from the stack for a specific dialog (used by PostClose for external closures).
        /// </summary>
        public static void RestoreFromStack(Dialog_InfoCard remainingCard)
        {
            if (cardStack.Count > 0)
            {
                var saved = cardStack.Pop();
                currentDialog = saved.dialog;
                rootItem = saved.rootItem;
                visibleItems = saved.visibleItems;
                selectedIndex = saved.selectedIndex;
            }
            else
            {
                // Fallback: re-initialize from scratch
                Open(remainingCard, announceOpening: false);
            }
        }

        /// <summary>
        /// Clears the saved state stack.
        /// </summary>
        public static void ClearStack()
        {
            cardStack.Clear();
        }

        /// <summary>
        /// Extracts all inspectable Defs from a tree item using vanilla's Hyperlink system.
        /// Only stat entries can have hyperlinks (matching vanilla behavior).
        /// If the item itself is not a stat entry, walks up to the parent to find one
        /// (so children of an inspectable stat inherit its hyperlinks for Alt+I).
        /// </summary>
        private static List<Def> GetInspectableDefs(InspectionTreeItem item, bool walkUpToParent)
        {
            var result = new List<Def>();
            if (item == null) return result;

            // Check this item first
            var target = item;
            if (!(target.Data is StatDrawEntry) && walkUpToParent)
            {
                // Walk up to find the nearest ancestor with a StatDrawEntry
                target = target.Parent;
                while (target != null && !(target.Data is StatDrawEntry))
                    target = target.Parent;
            }

            if (target?.Data is StatDrawEntry statEntry)
            {
                try
                {
                    var hyperlinks = statEntry.GetHyperlinks(StatRequest.ForEmpty());
                    if (hyperlinks != null)
                    {
                        foreach (var link in hyperlinks)
                        {
                            if (link.def != null)
                                result.Add(link.def);
                            else if (link.thing?.def != null)
                                result.Add(link.thing.def);
                        }
                    }
                }
                catch { }
            }
            return result;
        }

        /// <summary>
        /// Checks if an item has inspectable hyperlinks directly (not walking up).
        /// Used for the "Inspectable" announcement hint on stat entry nodes only.
        /// </summary>
        public static Def TryGetInspectableDef(InspectionTreeItem item)
        {
            var defs = GetInspectableDefs(item, walkUpToParent: false);
            return defs.Count > 0 ? defs[0] : null;
        }

        /// <summary>
        /// Opens a Dialog_InfoCard for a Def, automatically providing default stuff
        /// for stuff-requiring ThingDefs. Matches the game's Hyperlink.ActivateHyperlink pattern.
        /// </summary>
        public static void OpenInfoCardForDef(Def def)
        {
            if (def is ThingDef thingDef && thingDef.MadeFromStuff)
            {
                var defaultStuff = GenStuff.DefaultStuffFor(thingDef);
                if (defaultStuff != null)
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(thingDef, defaultStuff));
                    return;
                }
            }
            Find.WindowStack.Add(new Dialog_InfoCard(def));
        }

        /// <summary>
        /// Saves current state to the stack and opens a nested info card for a Def.
        /// Used by both the direct Alt+I handler and the multi-hyperlink selection menu.
        /// </summary>
        private static void PushStateAndOpenDef(Def def)
        {
            cardStack.Push(new SavedCardState
            {
                dialog = currentDialog,
                rootItem = rootItem,
                visibleItems = visibleItems,
                selectedIndex = selectedIndex
            });
            OpenInfoCardForDef(def);
        }

        /// <summary>
        /// Handles keyboard input for the Info Card.
        /// Returns true if input was handled.
        /// Called from UnifiedKeyboardPatch which handles Event.current.Use().
        /// </summary>
        public static bool HandleInput(Event ev)
        {
            if (!IsActive || ev.type != EventType.KeyDown)
                return false;

            // Delegate to float menu when WE opened it (multi-hyperlink selection).
            // We call HandleInput directly instead of returning false, because returning false
            // causes the event to fall through to parent states (inventory at 4.805, research
            // at 4.6) before reaching the float menu handler at priority 5.
            // Only delegate when ownsFloatMenu is true — if the float menu was opened by
            // another context (e.g., recipe selection in bills), we handle our own input.
            if (ownsFloatMenu && WindowlessFloatMenuState.IsActive)
            {
                if (WindowlessFloatMenuState.HandleInput())
                    return true;

                // HandleInput returns false for Escape with no active search.
                // Close the float menu and return to this info card.
                if (ev.keyCode == KeyCode.Escape)
                {
                    SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
                    WindowlessFloatMenuState.Close();
                    ownsFloatMenu = false;
                    TolkHelper.Speak("Menu closed");
                    return true;
                }

                // Consume all other keys to prevent parent state fallthrough
                return true;
            }

            KeyCode key = ev.keyCode;

            // Alt+I - open nested info card via vanilla's Hyperlink system
            if (ev.alt && key == KeyCode.I)
            {
                if (visibleItems != null && selectedIndex >= 0 && selectedIndex < visibleItems.Count)
                {
                    var item = visibleItems[selectedIndex];
                    var defs = GetInspectableDefs(item, walkUpToParent: true);
                    if (defs.Count == 1)
                    {
                        // Single hyperlink - open directly
                        PushStateAndOpenDef(defs[0]);
                    }
                    else if (defs.Count > 1)
                    {
                        // Multiple hyperlinks - present selection menu
                        var options = new List<FloatMenuOption>();
                        foreach (var def in defs)
                        {
                            var capturedDef = def;
                            string label = def.label?.CapitalizeFirst() ?? def.defName;
                            options.Add(new FloatMenuOption(label, () => PushStateAndOpenDef(capturedDef)));
                        }
                        TolkHelper.Speak("Choose item to inspect");
                        ownsFloatMenu = true;
                        WindowlessFloatMenuState.Open(options, false);
                    }
                    else
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        TolkHelper.Speak("No info card available");
                    }
                }
                return true;
            }

            // Escape - clear search FIRST, then close
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentSelection();
                    return true;
                }
                CloseInfoCard();
                return true;
            }

            // Up arrow - previous item (navigate matches when search active)
            if (key == KeyCode.UpArrow)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int prevIndex = typeahead.GetPreviousMatch(selectedIndex);
                    if (prevIndex >= 0)
                    {
                        selectedIndex = prevIndex;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectPrevious();
                }
                return true;
            }

            // Down arrow - next item (navigate matches when search active)
            if (key == KeyCode.DownArrow)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int nextIndex = typeahead.GetNextMatch(selectedIndex);
                    if (nextIndex >= 0)
                    {
                        selectedIndex = nextIndex;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectNext();
                }
                return true;
            }

            // Right arrow - expand
            if (key == KeyCode.RightArrow)
            {
                Expand();
                return true;
            }

            // Left arrow - collapse
            if (key == KeyCode.LeftArrow)
            {
                Collapse();
                return true;
            }

            // Page Down - jump to next category
            if (key == KeyCode.PageDown)
            {
                JumpToNextCategory();
                return true;
            }

            // Page Up - jump to previous category
            if (key == KeyCode.PageUp)
            {
                JumpToPreviousCategory();
                return true;
            }

            // Home - jump to first (Ctrl+Home for absolute first)
            if (key == KeyCode.Home)
            {
                if (ev.control)
                    JumpToAbsoluteFirst();
                else
                    JumpToFirst();
                return true;
            }

            // End - jump to last (Ctrl+End for absolute last)
            if (key == KeyCode.End)
            {
                if (ev.control)
                    JumpToAbsoluteLast();
                else
                    JumpToLast();
                return true;
            }

            // Asterisk (*) - expand all sibling categories (WCAG tree view pattern)
            bool isStar = key == KeyCode.KeypadMultiply || (ev.shift && key == KeyCode.Alpha8);
            if (isStar)
            {
                ExpandAllSiblings();
                return true;
            }

            // Enter - activate
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                ActivateItem();
                return true;
            }

            // Backspace - progressive search deletion
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = new List<string>();
                foreach (var item in visibleItems)
                    labels.Add(item.Label.StripTags());

                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0)
                        selectedIndex = newIndex;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceWithSearch();
                }
                return true;
            }

            // Typeahead search - alphanumeric keys
            // Use KeyCode instead of ev.character (which is empty in Unity IMGUI)
            {
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if ((isLetter || isNumber) && !ev.alt)
                {
                    char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                    HandleTypeahead(c);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Rebuilds the visible items list and clamps selection index after tree structure changes.
        /// Called from InfoCardTreeBuilder after permit allocation or refund modifies the tree.
        /// </summary>
        public static void RefreshVisibleListAndAnnounce()
        {
            RebuildVisibleList();
            if (visibleItems == null || visibleItems.Count == 0)
            {
                selectedIndex = 0;
                return;
            }
            if (selectedIndex >= visibleItems.Count)
                selectedIndex = Math.Max(0, visibleItems.Count - 1);
            AnnounceCurrentSelection();
        }

        #region Private Methods

        /// <summary>
        /// Rebuilds the visible items list from the tree.
        /// </summary>
        private static void RebuildVisibleList()
        {
            visibleItems = new List<InspectionTreeItem>();
            if (rootItem != null)
            {
                CollectVisibleItems(rootItem, visibleItems);
            }
        }

        /// <summary>
        /// Recursively collects visible items from the tree.
        /// </summary>
        private static void CollectVisibleItems(InspectionTreeItem item, List<InspectionTreeItem> list)
        {
            // Skip root item (it's just a container)
            if (item.IndentLevel >= 0)
            {
                list.Add(item);
            }

            // Add children if expanded (or if it's the root)
            if (item.IsExpanded || item.IndentLevel < 0)
            {
                foreach (var child in item.Children)
                {
                    CollectVisibleItems(child, list);
                }
            }
        }

        /// <summary>
        /// Moves selection to the first child of the current item.
        /// </summary>
        private static void MoveToFirstChild()
        {
            var item = visibleItems[selectedIndex];
            if (item.Children.Count > 0)
            {
                int itemIndex = visibleItems.IndexOf(item);
                if (itemIndex < 0)
                    return; // Item not in list, shouldn't happen but guard against it

                int firstChildIndex = itemIndex + 1;
                if (firstChildIndex < visibleItems.Count)
                {
                    selectedIndex = firstChildIndex;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                }
            }
        }

        /// <summary>
        /// Moves selection to the parent of the current item.
        /// </summary>
        private static void MoveToParent()
        {
            var item = visibleItems[selectedIndex];
            var parent = item.Parent;

            // Don't move to root (hidden)
            if (parent == null || parent == rootItem)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("At top level.");
                return;
            }

            int parentIndex = visibleItems.IndexOf(parent);
            if (parentIndex >= 0)
            {
                selectedIndex = parentIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Handles typeahead search input.
        /// </summary>
        private static void HandleTypeahead(char c)
        {
            var labels = new List<string>();
            foreach (var item in visibleItems)
            {
                labels.Add(item.Label.StripTags());
            }

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

        /// <summary>
        /// Announces the current selection with search context if applicable.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (!IsActive || visibleItems == null || visibleItems.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                return;

            var item = visibleItems[selectedIndex];
            string label = item.Label.StripTags();

            string stateIndicator = "";
            if (item.IsExpandable)
            {
                stateIndicator = item.IsExpanded ? " expanded" : " collapsed";
            }

            string announcement = $"{label}{stateIndicator}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Gets the sibling position (X of Y) for the given item.
        /// </summary>
        private static (int position, int total) GetSiblingPosition(InspectionTreeItem item)
        {
            List<InspectionTreeItem> siblings;
            if (item.Parent == null || item.Parent == rootItem)
            {
                siblings = rootItem.Children;
            }
            else
            {
                siblings = item.Parent.Children;
            }
            int position = siblings.IndexOf(item) + 1;
            return (position, siblings.Count);
        }

        /// <summary>
        /// Announces the opening of the Info Card.
        /// </summary>
        private static void AnnounceOpening()
        {
            if (rootItem == null)
                return;

            string rootLabel = rootItem.Label.StripTags();

            // Check if children are tabs (Category type) or direct content (single-tab case)
            bool hasTabs = rootItem.Children.Count > 0 &&
                           rootItem.Children[0].Type == InspectionTreeItem.ItemType.Category;

            string announcement;
            if (hasTabs)
            {
                int tabCount = rootItem.Children.Count;
                announcement = $"{rootLabel}. {tabCount} tabs available. Use Up and Down to navigate, Right to expand, Left to collapse.";
            }
            else
            {
                int itemCount = rootItem.Children.Count;
                announcement = $"{rootLabel}. {itemCount} items. Use Up and Down to navigate, Page Up and Page Down to jump between categories.";
            }

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Announces the current selection to the screen reader.
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            try
            {
                if (visibleItems == null || visibleItems.Count == 0)
                {
                    TolkHelper.Speak("No items available.");
                    return;
                }

                if (selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                    return;

                var item = visibleItems[selectedIndex];

                // Strip XML tags from label
                string label = item.Label.StripTags().TrimEnd('.', '!', '?');

                // Build state indicator (only for expandable items)
                string stateIndicator = "";
                if (item.IsExpandable)
                {
                    stateIndicator = item.IsExpanded ? " expanded" : " collapsed";
                }

                // Get sibling position
                var (position, total) = GetSiblingPosition(item);

                // Build level suffix if level changed
                string levelSuffix = MenuHelper.GetLevelSuffix("InfoCard", item.IndentLevel, skipLevelOne: false);

                // Add inspectable hint for items with hyperlinks (stat entries only, matching vanilla)
                string inspectableHint = TryGetInspectableDef(item) != null ? " Inspectable." : "";

                // Build full announcement (respects AnnouncePosition setting)
                string positionPart = MenuHelper.FormatPosition(position - 1, total);
                string announcement = string.IsNullOrEmpty(positionPart)
                    ? $"{label}{stateIndicator}.{levelSuffix}{inspectableHint}"
                    : $"{label}{stateIndicator}.{levelSuffix} {positionPart}.{inspectableHint}";

                TolkHelper.Speak(announcement);
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardState] Error announcing selection: {ex.Message}");
            }
        }

        #endregion
    }
}
