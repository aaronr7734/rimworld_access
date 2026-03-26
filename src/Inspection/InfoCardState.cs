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
    /// Uses TreeNavigationHelper for standard treeview keyboard navigation.
    /// </summary>
    public static class InfoCardState
    {
        public static bool IsActive { get; private set; } = false;

        private static Dialog_InfoCard currentDialog = null;

        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("InfoCard");
        public static TypeaheadSearchHelper Typeahead => treeNav.Typeahead;

        // State stack for nested info card support
        private class SavedCardState
        {
            public Dialog_InfoCard dialog;
            public InspectionTreeItem rootItem;
            public int selectedIndex;
        }
        private static Stack<SavedCardState> cardStack = new Stack<SavedCardState>();

        // Flag to prevent PostClose from interfering when CloseInfoCard is driving
        private static bool closingFromAccessibility = false;

        // Tracks the last announced section name for section boundary announcements
        private static string lastAnnouncedSection = null;

        // Tracks whether InfoCardState opened the current float menu (for multi-hyperlink selection).
        // When true, InfoCardState delegates input to the float menu instead of handling it.
        // When false, a float menu from another context (e.g., recipe selection) is active
        // and InfoCardState should handle its own input normally.
        private static bool ownsFloatMenu = false;
        public static bool IsClosingFromAccessibility => closingFromAccessibility;
        public static bool HasSavedState => cardStack.Count > 0;

        static InfoCardState()
        {
            treeNav.FormatItemAnnouncement = FormatItemAnnouncement;
            treeNav.FormatSearchAnnouncement = FormatSearchAnnouncement;
            treeNav.OnActivate = HandleActivate;
            treeNav.OnBeforeExpand = HandleBeforeExpand;
            treeNav.OnInfo = HandleInfo;
        }

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
                var rootItem = InfoCardTreeBuilder.BuildTree(dialog);
                treeNav.Initialize(rootItem);

                ownsFloatMenu = false;
                lastAnnouncedSection = null;

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
                var rootItem = InfoCardTreeBuilder.BuildTree(currentDialog);
                treeNav.Initialize(rootItem);
                lastAnnouncedSection = null;

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
            cardStack.Clear();
            ownsFloatMenu = false;
            treeNav.Reset();
        }

        /// <summary>
        /// Jumps to the next section (Page Down).
        /// Finds the next item whose Description (section name) differs from the current item's Description.
        /// </summary>
        public static void JumpToNextCategory()
        {
            if (!IsActive || treeNav.Count == 0)
                return;

            treeNav.Typeahead.ClearSearch();
            var visibleItems = treeNav.VisibleItems;
            int selectedIndex = treeNav.SelectedIndex;
            string currentSection = visibleItems[selectedIndex].Description ?? "";

            // Search forward for item with different section
            for (int i = selectedIndex + 1; i < visibleItems.Count; i++)
            {
                string section = visibleItems[i].Description ?? "";
                if (section != currentSection)
                {
                    treeNav.SetSelectedIndex(i);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    treeNav.ReannounceCurrentItem();
                    return;
                }
            }

            // Wrap to beginning (if enabled)
            if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                for (int i = 0; i <= selectedIndex; i++)
                {
                    string section = visibleItems[i].Description ?? "";
                    if (section != currentSection)
                    {
                        treeNav.SetSelectedIndex(i);
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        treeNav.ReannounceCurrentItem();
                        return;
                    }
                }
            }

            // No different section found or wrap disabled
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Jumps to the previous section (Page Up).
        /// Finds the first item of the previous section whose Description differs from the current item's Description.
        /// </summary>
        public static void JumpToPreviousCategory()
        {
            if (!IsActive || treeNav.Count == 0)
                return;

            treeNav.Typeahead.ClearSearch();
            var visibleItems = treeNav.VisibleItems;
            int selectedIndex = treeNav.SelectedIndex;
            string currentSection = visibleItems[selectedIndex].Description ?? "";

            // Search backward for first item of a different section
            // First, find any item with a different section
            int diffIndex = -1;
            for (int i = selectedIndex - 1; i >= 0; i--)
            {
                string section = visibleItems[i].Description ?? "";
                if (section != currentSection)
                {
                    diffIndex = i;
                    break;
                }
            }

            if (diffIndex >= 0)
            {
                // Now find the first item of that section
                string targetSection = visibleItems[diffIndex].Description ?? "";
                int firstOfSection = diffIndex;
                for (int i = diffIndex - 1; i >= 0; i--)
                {
                    string section = visibleItems[i].Description ?? "";
                    if (section == targetSection)
                        firstOfSection = i;
                    else
                        break;
                }
                treeNav.SetSelectedIndex(firstOfSection);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                treeNav.ReannounceCurrentItem();
                return;
            }

            // Wrap to end (if enabled)
            if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                // Find last section that differs from current
                for (int i = visibleItems.Count - 1; i > selectedIndex; i--)
                {
                    string section = visibleItems[i].Description ?? "";
                    if (section != currentSection)
                    {
                        // Find first item of that section
                        string targetSection = section;
                        int firstOfSection = i;
                        for (int j = i - 1; j >= 0; j--)
                        {
                            string s = visibleItems[j].Description ?? "";
                            if (s == targetSection)
                                firstOfSection = j;
                            else
                                break;
                        }
                        treeNav.SetSelectedIndex(firstOfSection);
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        treeNav.ReannounceCurrentItem();
                        return;
                    }
                }
            }

            // No different section found or wrap disabled
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
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
                treeNav.Initialize(saved.rootItem, saved.selectedIndex);
                SoundDefOf.Click.PlayOneShotOnCamera();
                treeNav.ReannounceCurrentItem();
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
                treeNav.Initialize(saved.rootItem, saved.selectedIndex);
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

            // Direct Def data (e.g. gene nodes under xenotype info card)
            if (item.Data is Def directDef)
            {
                result.Add(directDef);
                return result;
            }

            // List of Defs (e.g. Genes parent node with multiple gene defs)
            if (item.Data is IReadOnlyList<Def> defList && defList.Count > 0)
            {
                foreach (var d in defList)
                    result.Add(d);
                return result;
            }

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
                rootItem = treeNav.RootItem,
                selectedIndex = treeNav.SelectedIndex
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

            // Page Down - jump to next category (custom behavior, not in TreeNavigationHelper)
            if (key == KeyCode.PageDown)
            {
                JumpToNextCategory();
                return true;
            }

            // Page Up - jump to previous category (custom behavior, not in TreeNavigationHelper)
            if (key == KeyCode.PageUp)
            {
                JumpToPreviousCategory();
                return true;
            }

            // Right arrow - intercept to handle lazy-load empty correction
            if (key == KeyCode.RightArrow)
            {
                HandleRightArrow();
                return true;
            }

            // Escape - clear search FIRST, then close
            if (key == KeyCode.Escape)
            {
                if (treeNav.HasActiveSearch)
                {
                    treeNav.Typeahead.ClearSearchAndAnnounce();
                    treeNav.ReannounceCurrentItem();
                    return true;
                }
                CloseInfoCard();
                return true;
            }

            // Delegate all other input to TreeNavigationHelper
            return treeNav.HandleInput(ev);
        }

        /// <summary>
        /// Rebuilds the visible items list and clamps selection index after tree structure changes.
        /// Called from InfoCardTreeBuilder after permit allocation or refund modifies the tree.
        /// </summary>
        public static void RefreshVisibleListAndAnnounce()
        {
            treeNav.RebuildVisibleList();
            if (treeNav.Count == 0)
            {
                treeNav.SetSelectedIndex(0);
                return;
            }
            if (treeNav.SelectedIndex >= treeNav.Count)
                treeNav.SetSelectedIndex(Math.Max(0, treeNav.Count - 1));
            treeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Announces the opening of the Info Card.
        /// </summary>
        private static void AnnounceOpening()
        {
            if (treeNav.RootItem == null)
                return;

            string rootLabel = treeNav.RootItem.Label.StripTags();

            // Check if children are tabs (Category type) or direct content (single-tab case)
            bool hasTabs = treeNav.RootItem.Children.Count > 0 &&
                           treeNav.RootItem.Children[0].Type == InspectionTreeItem.ItemType.Category;

            string announcement;
            if (hasTabs)
            {
                int tabCount = treeNav.RootItem.Children.Count;
                announcement = $"{rootLabel}. {tabCount} tabs available. Use Up and Down to navigate, Right to expand, Left to collapse.";
            }
            else
            {
                int itemCount = treeNav.RootItem.Children.Count;
                announcement = $"{rootLabel}. {itemCount} items. Use Up and Down to navigate, Page Up and Page Down to jump between categories.";
            }

            TolkHelper.Speak(announcement);
        }

        #region Announcement Formatters

        /// <summary>
        /// Formats item announcement matching the original InfoCardState format:
        /// "{label stripped}{space+expanded/collapsed}.{levelSuffix} {position}.{inspectable hint}"
        /// </summary>
        private static string FormatItemAnnouncement(InspectionTreeItem item)
        {
            try
            {
                // Smart labels: use ExpandedLabel (short form) when expanded
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
                    stateIndicator = item.IsExpanded ? " expanded" : " collapsed";
                }

                // Get sibling position
                var (position, total) = treeNav.GetSiblingPosition(item);

                // Build level suffix if level changed
                string levelSuffix = MenuHelper.GetLevelSuffix("InfoCard", item.IndentLevel, skipLevelOne: false);

                // Add inspectable hint for items with hyperlinks (stat entries only, matching vanilla)
                string inspectableHint = TryGetInspectableDef(item) != null ? " Inspectable." : "";

                // Build full announcement (respects AnnouncePosition setting)
                string positionPart = MenuHelper.FormatPosition(position - 1, total);
                string announcement = string.IsNullOrEmpty(positionPart)
                    ? $"{label}{stateIndicator}.{levelSuffix}{inspectableHint}"
                    : $"{label}{stateIndicator}.{levelSuffix} {positionPart}.{inspectableHint}";

                // Append section suffix when crossing section boundaries
                string sectionName = item.Description;
                if (!string.IsNullOrEmpty(sectionName) && sectionName != lastAnnouncedSection)
                {
                    announcement += $" {sectionName} section";
                    lastAnnouncedSection = sectionName;
                }
                else if (string.IsNullOrEmpty(sectionName) && lastAnnouncedSection != null)
                {
                    // Moving from a section to an item without a section
                    lastAnnouncedSection = null;
                }

                return announcement;
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardState] Error formatting announcement: {ex.Message}");
                return item.Label.StripTags();
            }
        }

        /// <summary>
        /// Formats search announcement matching the original InfoCardState format:
        /// "{label stripped}{space+expanded/collapsed}, {N} of {M} matches for '{search}'"
        /// </summary>
        private static string FormatSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead)
        {
            string label = item.Label.StripTags();

            string stateIndicator = "";
            if (item.IsExpandable)
            {
                stateIndicator = item.IsExpanded ? " expanded" : " collapsed";
            }

            string announcement = $"{label}{stateIndicator}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";
            return announcement;
        }

        #endregion

        #region Custom Actions

        /// <summary>
        /// Handles Enter key activation. For expandable items that are already expanded,
        /// shows a reject message. For collapsed expandable items, expands them (with lazy-load
        /// empty correction). For non-expandable items with actions, executes the action.
        /// </summary>
        private static bool HandleActivate(InspectionTreeItem item)
        {
            // Handle expandable items
            if (item.IsExpandable)
            {
                if (item.IsExpanded)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("Already expanded.");
                    return true;
                }
                // Collapsed: expand with lazy-load empty correction (same as Right arrow)
                HandleRightArrow();
                return true;
            }

            // For non-expandable items with actions, execute the action
            if (item.OnActivate != null)
            {
                item.OnActivate();
                SoundDefOf.Click.PlayOneShotOnCamera();
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
        /// Note: Does NOT correct IsExpandable here — that is handled in HandleRightArrow
        /// and HandleActivate which check Children.Count after lazy load.
        /// </summary>
        private static void HandleBeforeExpand(InspectionTreeItem item)
        {
            if (item.OnActivate != null && item.Children.Count == 0)
            {
                item.OnActivate();
            }
        }

        /// <summary>
        /// Handles Right arrow with lazy-load empty correction.
        /// When expanding a node that was marked expandable but has no content after lazy loading,
        /// corrects IsExpandable and announces instead of expanding to an empty node.
        /// </summary>
        private static void HandleRightArrow()
        {
            var item = treeNav.SelectedItem;
            if (item == null)
                return;

            // Not expandable - reject
            if (!item.IsExpandable)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Cannot expand this item.", SpeechPriority.High);
                return;
            }

            // Already expanded - drill down to first child
            if (item.IsExpanded)
            {
                treeNav.ExpandOrDrillDown();
                return;
            }

            // Collapsed: trigger lazy load
            if (item.OnActivate != null && item.Children.Count == 0)
            {
                item.OnActivate();
            }

            // Check if lazy load produced children
            if (item.Children.Count == 0)
            {
                // Item was marked expandable but has no content - correct its state
                item.IsExpandable = false;
                SoundDefOf.Click.PlayOneShotOnCamera();
                treeNav.ReannounceCurrentItem();
                return;
            }

            // Has children - expand normally
            item.IsExpanded = true;
            treeNav.RebuildVisibleList();
            SoundDefOf.Click.PlayOneShotOnCamera();
            treeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Handles Alt+I - open nested info card via vanilla's Hyperlink system.
        /// </summary>
        private static bool HandleInfo(InspectionTreeItem item)
        {
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
            return true;
        }

        #endregion
    }
}
