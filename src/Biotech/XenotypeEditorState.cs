using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages accessible keyboard navigation for Dialog_CreateXenotype (xenotype editor).
    /// Uses a tabbed treeview architecture: Tab/Shift+Tab switches between Selected Genes,
    /// Gene Library (organized by GeneCategoryDef), and Controls. Each gene tab is an
    /// InspectionTreeItem treeview with WCAG tree keyboard navigation.
    /// </summary>
    public static class XenotypeEditorState
    {
        private enum Tab { Selected, Library, Controls }

        // ===== Global State =====
        private static bool isActive;
        public static bool IsActive => isActive;
        private static bool isRenaming;
        public static bool IsRenaming => isRenaming;

        private static Window dialog;
        private static Tab currentTab;

        // ===== Per-Tab State: Selected Genes =====
        private static InspectionTreeItem selectedRoot;
        private static List<InspectionTreeItem> selectedVisible = new List<InspectionTreeItem>();
        private static int selectedIdx;
        private static TypeaheadSearchHelper selectedTypeahead = new TypeaheadSearchHelper();

        // ===== Per-Tab State: Gene Library =====
        private static InspectionTreeItem libraryRoot;
        private static List<InspectionTreeItem> libraryVisible = new List<InspectionTreeItem>();
        private static int libraryIdx;
        private static TypeaheadSearchHelper libraryTypeahead = new TypeaheadSearchHelper();

        // ===== Controls Tab =====
        private static List<ControlItem> controlItems = new List<ControlItem>();
        private static int controlIdx;

        private class ControlItem
        {
            public string Label;
            public string Tooltip;
            public Action OnActivate;
        }

        // ===== Reflection Cache =====
        private static FieldInfo fi_selectedGenes;
        private static FieldInfo fi_inheritable;
        private static FieldInfo fi_collapsedCategories;
        private static FieldInfo fi_generationRequestIndex;
        private static FieldInfo fi_callback;
        private static FieldInfo fi_ignoreRestrictionsConfirmationSent;
        private static FieldInfo fi_xenotypeName;
        private static FieldInfo fi_xenotypeNameLocked;
        private static FieldInfo fi_iconDef;
        private static FieldInfo fi_gcx;
        private static FieldInfo fi_met;
        private static FieldInfo fi_arc;
        private static FieldInfo fi_ignoreRestrictions;
        private static FieldInfo fi_leftChosenGroups;
        private static MethodInfo mi_onGenesChanged;
        private static MethodInfo mi_accept;
        private static MethodInfo mi_canAccept;

        private const string LevelKey = "XenotypeEditor";

        static XenotypeEditorState()
        {
            fi_selectedGenes = AccessTools.Field(typeof(Dialog_CreateXenotype), "selectedGenes");
            fi_inheritable = AccessTools.Field(typeof(Dialog_CreateXenotype), "inheritable");
            fi_collapsedCategories = AccessTools.Field(typeof(Dialog_CreateXenotype), "collapsedCategories");
            fi_generationRequestIndex = AccessTools.Field(typeof(Dialog_CreateXenotype), "generationRequestIndex");
            fi_callback = AccessTools.Field(typeof(Dialog_CreateXenotype), "callback");
            fi_ignoreRestrictionsConfirmationSent = AccessTools.Field(typeof(Dialog_CreateXenotype), "ignoreRestrictionsConfirmationSent");
            fi_xenotypeName = AccessTools.Field(typeof(GeneCreationDialogBase), "xenotypeName");
            fi_xenotypeNameLocked = AccessTools.Field(typeof(GeneCreationDialogBase), "xenotypeNameLocked");
            fi_iconDef = AccessTools.Field(typeof(GeneCreationDialogBase), "iconDef");
            fi_gcx = AccessTools.Field(typeof(GeneCreationDialogBase), "gcx");
            fi_met = AccessTools.Field(typeof(GeneCreationDialogBase), "met");
            fi_arc = AccessTools.Field(typeof(GeneCreationDialogBase), "arc");
            fi_ignoreRestrictions = AccessTools.Field(typeof(GeneCreationDialogBase), "ignoreRestrictions");
            fi_leftChosenGroups = AccessTools.Field(typeof(GeneCreationDialogBase), "leftChosenGroups");
            mi_onGenesChanged = AccessTools.Method(typeof(GeneCreationDialogBase), "OnGenesChanged");
            mi_accept = AccessTools.Method(typeof(Dialog_CreateXenotype), "Accept");
            mi_canAccept = AccessTools.Method(typeof(Dialog_CreateXenotype), "CanAccept");
        }

        // ===== Lifecycle =====

        public static void Open(Window dialogInstance)
        {
            try
            {
                if (dialogInstance == null)
                    return;

                dialog = dialogInstance;
                isActive = true;

                // Build trees and controls
                RebuildAllTrees();
                BuildControlItems();

                // Start on Selected if genes exist, otherwise Library
                var selected = GetSelectedGenes();
                currentTab = (selected != null && selected.Count > 0) ? Tab.Selected : Tab.Library;

                // Reset navigation
                selectedIdx = 0;
                libraryIdx = 0;
                controlIdx = 0;
                selectedTypeahead.ClearSearch();
                libraryTypeahead.ClearSearch();
                MenuHelper.ResetLevel(LevelKey);

                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                AnnounceOpening();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in XenotypeEditorState.Open: {ex}");
                Close();
            }
        }

        public static void Close()
        {
            isActive = false;
            isRenaming = false;
            dialog = null;
            selectedRoot = null;
            selectedVisible.Clear();
            libraryRoot = null;
            libraryVisible.Clear();
            controlItems.Clear();
            selectedIdx = 0;
            libraryIdx = 0;
            controlIdx = 0;
            selectedTypeahead.ClearSearch();
            libraryTypeahead.ClearSearch();
            MenuHelper.ResetLevel(LevelKey);
        }

        // ===== Input Handling =====

        public static bool HandleInput(Event ev)
        {
            if (!isActive || ev.type != EventType.KeyDown)
                return false;

            // Let float menu (e.g. Load Custom/Premade) handle its own input
            if (WindowlessFloatMenuState.IsActive)
                return false;

            KeyCode key = ev.keyCode;
            bool shift = ev.shift;
            bool ctrl = ev.control;
            bool alt = ev.alt;

            // Alt+S: Save and apply shortcut from any tab
            if (key == KeyCode.S && alt && !ctrl && !shift)
            {
                SaveAndApply();
                return true;
            }

            // Alt+I: InfoCard for current item
            if (key == KeyCode.I && alt && !ctrl && !shift)
            {
                OpenInfoCard();
                return true;
            }

            // Tab / Shift+Tab: switch tabs
            if (key == KeyCode.Tab && !ctrl && !alt)
            {
                SwitchTab(!shift);
                return true;
            }

            // Escape
            if (key == KeyCode.Escape)
            {
                return HandleEscape();
            }

            // Block ] to prevent StartingPawnState context menu from opening underneath
            if (key == KeyCode.RightBracket)
                return true;

            // Route to current tab
            switch (currentTab)
            {
                case Tab.Selected:
                    return HandleTreeInput(ev, key, shift, ctrl, alt,
                        selectedVisible, selectedTypeahead, selectedRoot, true);
                case Tab.Library:
                    return HandleTreeInput(ev, key, shift, ctrl, alt,
                        libraryVisible, libraryTypeahead, libraryRoot, false);
                case Tab.Controls:
                    return HandleControlsInput(key, shift, ctrl, alt, ev);
            }

            return false;
        }

        private static bool HandleTreeInput(Event ev, KeyCode key, bool shift, bool ctrl, bool alt,
            List<InspectionTreeItem> visible, TypeaheadSearchHelper typeahead,
            InspectionTreeItem root, bool isSelectedTab)
        {
            if (visible == null || visible.Count == 0)
            {
                return false;
            }

            int idx = GetTreeIdx(isSelectedTab);

            // Up arrow
            if (key == KeyCode.UpArrow && !alt)
            {
                if (typeahead.HasActiveSearch)
                {
                    int prev = typeahead.GetPreviousMatch(idx);
                    if (prev >= 0) idx = prev;
                }
                else
                {
                    idx = MenuHelper.SelectPrevious(idx, visible.Count);
                }
                SetTreeIdx(isSelectedTab, idx);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceTreeItem(visible, idx, root);
                return true;
            }

            // Down arrow
            if (key == KeyCode.DownArrow && !alt)
            {
                if (typeahead.HasActiveSearch)
                {
                    int next = typeahead.GetNextMatch(idx);
                    if (next >= 0) idx = next;
                }
                else
                {
                    idx = MenuHelper.SelectNext(idx, visible.Count);
                }
                SetTreeIdx(isSelectedTab, idx);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceTreeItem(visible, idx, root);
                return true;
            }

            // Right arrow - expand
            if (key == KeyCode.RightArrow && !alt)
            {
                ExpandNode(visible, isSelectedTab, root);
                return true;
            }

            // Left arrow - collapse
            if (key == KeyCode.LeftArrow && !alt)
            {
                CollapseNode(visible, isSelectedTab, root);
                return true;
            }

            // Home
            if (key == KeyCode.Home && !alt)
            {
                typeahead.ClearSearch();
                int homeIdx = GetTreeIdx(isSelectedTab);
                MenuHelper.HandleTreeHomeKey(visible, ref homeIdx,
                    item => item.IndentLevel, ctrl,
                    () => { });
                SetTreeIdx(isSelectedTab, homeIdx);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceTreeItem(visible, homeIdx, root);
                return true;
            }

            // End
            if (key == KeyCode.End && !alt)
            {
                typeahead.ClearSearch();
                int endIdx = GetTreeIdx(isSelectedTab);
                MenuHelper.HandleTreeEndKey(visible, ref endIdx,
                    item => item.IndentLevel,
                    item => item.IsExpanded,
                    item => item.IsExpandable && item.Children.Count > 0,
                    ctrl,
                    () => { });
                SetTreeIdx(isSelectedTab, endIdx);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceTreeItem(visible, endIdx, root);
                return true;
            }

            // Page Down - jump to next top-level item (gene in Selected, category in Library)
            if (key == KeyCode.PageDown && !alt)
            {
                JumpToNextTopLevel(visible, isSelectedTab, root, forward: true);
                return true;
            }

            // Page Up - jump to previous top-level item
            if (key == KeyCode.PageUp && !alt)
            {
                JumpToNextTopLevel(visible, isSelectedTab, root, forward: false);
                return true;
            }

            // Asterisk (*) - expand all siblings
            bool isStar = key == KeyCode.KeypadMultiply || (shift && key == KeyCode.Alpha8);
            if (isStar)
            {
                ExpandAllSiblings(visible, isSelectedTab, root);
                return true;
            }

            // Enter / Space - context action
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space)
            {
                HandleTreeEnter(visible, isSelectedTab, root);
                return true;
            }

            // Backspace - clear search
            if (key == KeyCode.Backspace)
            {
                if (typeahead.HasActiveSearch)
                {
                    var labels = BuildSearchLabels(visible);
                    if (typeahead.ProcessBackspace(labels, out int newIndex))
                    {
                        if (newIndex >= 0) SetTreeIdx(isSelectedTab, newIndex);
                        AnnounceWithSearch(visible, GetTreeIdx(isSelectedTab), root, typeahead);
                    }
                    return true;
                }
                return false;
            }

            // Typeahead - alphanumeric characters (not with Alt modifier)
            if (!alt && ev.character != '\0' && char.IsLetterOrDigit(ev.character))
            {
                var labels = BuildSearchLabels(visible);
                if (typeahead.ProcessCharacterInput(ev.character, labels, out int newIndex))
                {
                    if (newIndex >= 0)
                    {
                        SetTreeIdx(isSelectedTab, newIndex);
                        AnnounceWithSearch(visible, newIndex, root, typeahead);
                    }
                }
                else
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'.");
                }
                return true;
            }

            return false;
        }

        private static bool HandleControlsInput(KeyCode key, bool shift, bool ctrl, bool alt, Event ev)
        {
            if (controlItems.Count == 0)
                return false;

            // Up
            if (key == KeyCode.UpArrow && !alt)
            {
                controlIdx = MenuHelper.SelectPrevious(controlIdx, controlItems.Count);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceControlItem();
                return true;
            }

            // Down
            if (key == KeyCode.DownArrow && !alt)
            {
                controlIdx = MenuHelper.SelectNext(controlIdx, controlItems.Count);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceControlItem();
                return true;
            }

            // Home
            if (key == KeyCode.Home)
            {
                controlIdx = 0;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceControlItem();
                return true;
            }

            // End
            if (key == KeyCode.End)
            {
                controlIdx = controlItems.Count - 1;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceControlItem();
                return true;
            }

            // Enter
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space)
            {
                var item = controlItems[controlIdx];
                if (item.OnActivate != null)
                {
                    item.OnActivate();
                }
                else
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
                return true;
            }

            return false;
        }

        // ===== Tab Switching =====

        private static void SwitchTab(bool forward)
        {
            // Clear current tab's search
            GetCurrentTypeahead()?.ClearSearch();

            int tabCount = 3; // Selected, Library, Controls
            int idx = (int)currentTab;

            if (forward)
            {
                idx++;
                if (idx >= tabCount)
                    idx = RimWorldAccessMod_Settings.Settings?.WrapNavigation == true ? 0 : tabCount - 1;
            }
            else
            {
                idx--;
                if (idx < 0)
                    idx = RimWorldAccessMod_Settings.Settings?.WrapNavigation == true ? tabCount - 1 : 0;
            }

            currentTab = (Tab)idx;
            MenuHelper.ResetLevel(LevelKey);
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceTabSwitch();
        }

        // ===== Escape =====

        private static bool HandleEscape()
        {
            // First: clear search if active
            var typeahead = GetCurrentTypeahead();
            if (typeahead != null && typeahead.HasActiveSearch)
            {
                typeahead.ClearSearchAndAnnounce();
                // Re-announce current item
                switch (currentTab)
                {
                    case Tab.Selected:
                        if (selectedVisible.Count > 0) AnnounceTreeItem(selectedVisible, selectedIdx, selectedRoot);
                        break;
                    case Tab.Library:
                        if (libraryVisible.Count > 0) AnnounceTreeItem(libraryVisible, libraryIdx, libraryRoot);
                        break;
                    case Tab.Controls:
                        AnnounceControlItem();
                        break;
                }
                return true;
            }

            // Otherwise: close dialog
            CloseDialog();
            return true;
        }

        private static void CloseDialog()
        {
            if (dialog != null)
            {
                dialog.Close(doCloseSound: false);
            }
            Close();
            TolkHelper.Speak((string)"Close".Translate());
        }

        // ===== Rename Input =====
        // Character input is captured separately in UnifiedKeyboardPatch's keyCode==None section.
        // This method handles control keys only (Enter, Escape, Backspace, Ctrl+V, Tab).

        public static bool HandleRenameInput(Event ev)
        {
            KeyCode key = ev.keyCode;

            // Enter - confirm rename
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                ConfirmRename();
                return true;
            }

            // Escape - cancel rename
            if (key == KeyCode.Escape)
            {
                CancelRename();
                return true;
            }

            // Backspace
            if (key == KeyCode.Backspace)
            {
                TextInputHelper.HandleBackspace();
                return true;
            }

            // Ctrl+V - paste
            if (key == KeyCode.V && ev.control)
            {
                TextInputHelper.HandlePaste();
                return true;
            }

            // Tab - read current text
            if (key == KeyCode.Tab)
            {
                TextInputHelper.ReadCurrentText();
                return true;
            }

            return false;
        }

        public static void CancelRename()
        {
            isRenaming = false;
            TextInputHelper.Clear();
            SoundDefOf.Click.PlayOneShotOnCamera();
            TolkHelper.Speak("Rename cancelled.");
        }

        private static void ConfirmRename()
        {
            string newName = TextInputHelper.CurrentText;

            if (string.IsNullOrWhiteSpace(newName))
            {
                TolkHelper.Speak("Cannot set empty name. Enter a name or press Escape to cancel.", SpeechPriority.High);
                return;
            }

            if (dialog == null)
            {
                isRenaming = false;
                TextInputHelper.Clear();
                return;
            }

            // Apply the new name
            fi_xenotypeName.SetValue(dialog, newName);

            // Auto-lock the name so it doesn't get overwritten by gene changes
            fi_xenotypeNameLocked.SetValue(dialog, true);

            isRenaming = false;
            TextInputHelper.Clear();

            // Rebuild controls to reflect new name and lock state
            BuildControlItems();

            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.Speak($"Renamed to {newName}. Name locked.");
        }

        // ===== Tree Operations =====

        private static void ExpandNode(List<InspectionTreeItem> visible, bool isSelectedTab, InspectionTreeItem root)
        {
            int idx = GetTreeIdx(isSelectedTab);
            if (idx < 0 || idx >= visible.Count)
                return;

            var item = visible[idx];

            // Leaf node - reject
            if (!item.IsExpandable)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            // Already expanded - move to first child
            if (item.IsExpanded)
            {
                if (idx + 1 < visible.Count && visible[idx + 1].Parent == item)
                {
                    SetTreeIdx(isSelectedTab, idx + 1);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceTreeItem(visible, idx + 1, root);
                }
                return;
            }

            // Collapsed - expand
            if (item.OnActivate != null && item.Children.Count == 0)
            {
                item.OnActivate();
            }

            if (item.Children.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            item.IsExpanded = true;
            RebuildCurrentVisibleList(isSelectedTab, idx);
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceTreeItem(visible, idx, root);
        }

        private static void CollapseNode(List<InspectionTreeItem> visible, bool isSelectedTab, InspectionTreeItem root)
        {
            int idx = GetTreeIdx(isSelectedTab);
            if (idx < 0 || idx >= visible.Count)
                return;

            var item = visible[idx];

            // Expanded - collapse
            if (item.IsExpandable && item.IsExpanded)
            {
                item.IsExpanded = false;

                // Restore rich label for gene nodes (Description stores the rich collapsed label)
                if (item.Data is GeneDef && !string.IsNullOrEmpty(item.Description))
                {
                    item.Label = item.Description;
                }

                RebuildCurrentVisibleList(isSelectedTab, idx);
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceTreeItem(visible, idx, root);
                return;
            }

            // Move to parent
            var parent = item.Parent;
            if (parent == null || parent == root)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            int parentIdx = visible.IndexOf(parent);
            if (parentIdx >= 0)
            {
                SetTreeIdx(isSelectedTab, parentIdx);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceTreeItem(visible, parentIdx, root);
            }
        }

        private static void ExpandAllSiblings(List<InspectionTreeItem> visible, bool isSelectedTab, InspectionTreeItem root)
        {
            int idx = GetTreeIdx(isSelectedTab);
            if (idx < 0 || idx >= visible.Count)
                return;

            var item = visible[idx];
            var siblings = (item.Parent == null || item.Parent == root) ? root.Children : item.Parent.Children;

            int expandedCount = 0;
            foreach (var sibling in siblings)
            {
                if (sibling.IsExpandable && !sibling.IsExpanded)
                {
                    if (sibling.OnActivate != null && sibling.Children.Count == 0)
                        sibling.OnActivate();
                    if (sibling.Children.Count > 0)
                    {
                        sibling.IsExpanded = true;
                        expandedCount++;
                    }
                }
            }

            if (expandedCount > 0)
            {
                RebuildCurrentVisibleList(isSelectedTab, idx);
                SoundDefOf.Click.PlayOneShotOnCamera();
                TolkHelper.Speak($"Expanded {expandedCount} {(expandedCount == 1 ? "item" : "items")}.");
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        private static void JumpToNextTopLevel(List<InspectionTreeItem> visible, bool isSelectedTab, InspectionTreeItem root, bool forward)
        {
            if (visible == null || visible.Count == 0)
                return;

            int idx = GetTreeIdx(isSelectedTab);
            int start = idx;
            int direction = forward ? 1 : -1;
            int current = idx + direction;

            while (current >= 0 && current < visible.Count)
            {
                if (visible[current].IndentLevel == 0)
                {
                    SetTreeIdx(isSelectedTab, current);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceTreeItem(visible, current, root);
                    return;
                }
                current += direction;
            }

            // Wrap if enabled
            if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                current = forward ? 0 : visible.Count - 1;
                while (current != start)
                {
                    if (visible[current].IndentLevel == 0)
                    {
                        SetTreeIdx(isSelectedTab, current);
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceTreeItem(visible, current, root);
                        return;
                    }
                    current += direction;
                    if (current < 0 || current >= visible.Count)
                        break;
                }
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        // ===== Enter Key Actions =====

        private static void HandleTreeEnter(List<InspectionTreeItem> visible, bool isSelectedTab, InspectionTreeItem root)
        {
            int idx = GetTreeIdx(isSelectedTab);
            if (idx < 0 || idx >= visible.Count)
                return;

            var item = visible[idx];

            // Gene item (IndentLevel 0 in Selected, IndentLevel 1 in Library) - toggle selection
            if (item.Data is GeneDef gene)
            {
                ToggleGene(gene);
                return;
            }

            // Category node in Library (IndentLevel 0, Data is GeneCategoryDef) - expand/collapse
            if (item.Data is GeneCategoryDef)
            {
                if (!item.IsExpanded)
                    ExpandNode(visible, isSelectedTab, root);
                else
                    CollapseNode(visible, isSelectedTab, root);
                return;
            }

            // Expandable node - expand/collapse it (standard tree behavior)
            if (item.IsExpandable)
            {
                if (!item.IsExpanded)
                {
                    ExpandNode(visible, isSelectedTab, root);
                }
                else
                {
                    CollapseNode(visible, isSelectedTab, root);
                }
                return;
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        // ===== Gene Selection Toggle =====

        private static void ToggleGene(GeneDef gene)
        {
            if (dialog == null) return;

            var selectedList = GetSelectedGenes();
            if (selectedList == null) return;

            bool adding;
            if (selectedList.Contains(gene))
            {
                selectedList.Remove(gene);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                adding = false;
            }
            else
            {
                selectedList.Add(gene);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                adding = true;
            }

            // Update xenotype name if not locked
            bool nameLocked = (bool)fi_xenotypeNameLocked.GetValue(dialog);
            if (!nameLocked)
            {
                string newName = GeneUtility.GenerateXenotypeNameFromGenes(selectedList);
                fi_xenotypeName.SetValue(dialog, newName);
            }

            // Update biostats and conflict info
            mi_onGenesChanged.Invoke(dialog, null);

            // Save cursor context
            GeneDef cursorGene = GetCurrentGeneDef();

            // Rebuild trees
            RebuildAllTrees();
            BuildControlItems();

            // Restore cursor
            RestoreCursor(cursorGene);

            // Announce feedback
            string action = adding ? "added" : "removed";
            string biostats = FormatCurrentBiostats();
            TolkHelper.Speak($"{gene.LabelCap} {action}. {biostats}");
        }

        private static GeneDef GetCurrentGeneDef()
        {
            InspectionTreeItem item = GetCurrentItem();
            if (item?.Data is GeneDef gene)
                return gene;
            return null;
        }

        private static void RestoreCursor(GeneDef cursorGene)
        {
            if (cursorGene == null) return;

            List<InspectionTreeItem> visible;
            switch (currentTab)
            {
                case Tab.Selected:
                    visible = selectedVisible;
                    for (int i = 0; i < visible.Count; i++)
                    {
                        if (visible[i].Data is GeneDef g && g == cursorGene) { selectedIdx = i; return; }
                    }
                    // Gene was removed from selected - clamp index
                    if (selectedIdx >= visible.Count)
                        selectedIdx = Math.Max(0, visible.Count - 1);
                    break;
                case Tab.Library:
                    visible = libraryVisible;
                    for (int i = 0; i < visible.Count; i++)
                    {
                        if (visible[i].Data is GeneDef g && g == cursorGene) { libraryIdx = i; return; }
                    }
                    if (libraryIdx >= visible.Count)
                        libraryIdx = Math.Max(0, visible.Count - 1);
                    break;
            }
        }

        // ===== Save and Apply =====

        private static void SaveAndApply()
        {
            if (dialog == null) return;

            var selectedList = GetSelectedGenes();

            // Validate - announce specific errors
            if (selectedList == null || selectedList.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak(((string)"MessageNoSelectedGenes".Translate()).StripTags());
                return;
            }

            string name = (string)fi_xenotypeName.GetValue(dialog);
            if (string.IsNullOrEmpty(name?.Trim()))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak(((string)"XenotypeNameCannotBeEmpty".Translate()).StripTags());
                return;
            }

            // Check file count limit
            if (GenFilePaths.AllCustomXenotypeFiles.EnumerableCount() >= 200)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Too many custom xenotypes saved. Delete some first.");
                return;
            }

            bool ignoreRestr = (bool)fi_ignoreRestrictions.GetValue(dialog);
            if (!ignoreRestr)
            {
                // Check for gene conflicts
                var leftChosenGroups = fi_leftChosenGroups.GetValue(dialog) as System.Collections.IList;
                if (leftChosenGroups != null && leftChosenGroups.Count > 0)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak(((string)"MessageConflictingGenesPresent".Translate()).StripTags());
                    return;
                }

                // Check for missing prerequisites
                foreach (var gene in selectedList)
                {
                    if (gene.prerequisite != null && !selectedList.Contains(gene.prerequisite))
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        TolkHelper.Speak(((string)"MessageGeneMissingPrerequisite".Translate(gene.label)).CapitalizeFirst().StripTags()
                            + ": " + gene.prerequisite.LabelCap);
                        return;
                    }
                }
            }

            // All checks passed - deactivate our state before Accept, which may
            // show a confirmation dialog or close the window. Other accessibility
            // handlers (WindowlessConfirmationState) will handle any dialogs that appear.
            var savedDialog = dialog;
            Close();
            try
            {
                mi_accept.Invoke(savedDialog, null);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error invoking Accept: {ex}");
            }
        }

        // ===== Load Custom / Premade =====

        private static void LoadCustom()
        {
            if (dialog == null) return;

            var files = GenFilePaths.AllCustomXenotypeFiles.ToList();
            if (files.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No saved custom xenotypes.");
                return;
            }

            var options = new List<FloatMenuOption>();
            foreach (var file in files)
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(file.Name);
                var capturedFile = file;
                options.Add(new FloatMenuOption(fileName, () =>
                {
                    string filePath = capturedFile.FullName;
                    if (GameDataSaveLoader.TryLoadXenotype(filePath, out CustomXenotype xenotype))
                    {
                        ApplyCustomXenotype(xenotype);
                    }
                    else
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        TolkHelper.Speak("Failed to load xenotype.");
                    }
                }));
            }

            WindowlessFloatMenuState.Open(options, false);
        }

        private static void ApplyCustomXenotype(CustomXenotype xenotype)
        {
            if (dialog == null) return;

            fi_xenotypeName.SetValue(dialog, xenotype.name);
            fi_xenotypeNameLocked.SetValue(dialog, true);
            fi_iconDef.SetValue(dialog, xenotype.IconDef);

            var selectedList = GetSelectedGenes();
            selectedList.Clear();
            selectedList.AddRange(xenotype.genes);

            fi_inheritable.SetValue(dialog, xenotype.inheritable);

            mi_onGenesChanged.Invoke(dialog, null);

            // Enable ignore restrictions if needed
            bool hasArchite = selectedList.Any(g => g.biostatArc > 0);
            if (hasArchite)
            {
                fi_ignoreRestrictions.SetValue(dialog, true);
            }

            RebuildAllTrees();
            BuildControlItems();

            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.Speak($"Loaded {xenotype.name}. {selectedList.Count} genes. {FormatCurrentBiostats()}");
        }

        private static void LoadPremade()
        {
            if (dialog == null) return;

            var xenotypes = DefDatabase<XenotypeDef>.AllDefs
                .OrderByDescending(x => x.displayPriority)
                .ToList();

            if (xenotypes.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No premade xenotypes available.");
                return;
            }

            var options = new List<FloatMenuOption>();
            var infoCardDefs = new List<Def>();

            foreach (var xenotype in xenotypes)
            {
                var localXeno = xenotype;
                options.Add(new FloatMenuOption(localXeno.LabelCap, () => ApplyPremadeXenotype(localXeno)));
                infoCardDefs.Add(localXeno);
            }

            WindowlessFloatMenuState.Open(options, false, infoCardDefs: infoCardDefs);
        }

        private static void ApplyPremadeXenotype(XenotypeDef xenotype)
        {
            if (dialog == null) return;

            fi_xenotypeName.SetValue(dialog, xenotype.label);
            fi_xenotypeNameLocked.SetValue(dialog, true);

            var selectedList = GetSelectedGenes();
            selectedList.Clear();
            selectedList.AddRange(xenotype.genes);

            fi_inheritable.SetValue(dialog, xenotype.inheritable);

            mi_onGenesChanged.Invoke(dialog, null);

            // Enable ignore restrictions if needed
            bool hasArchite = selectedList.Any(g => g.biostatArc > 0);
            if (hasArchite)
            {
                fi_ignoreRestrictions.SetValue(dialog, true);
            }

            RebuildAllTrees();
            BuildControlItems();

            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.Speak($"Loaded {xenotype.LabelCap}. {selectedList.Count} genes. {FormatCurrentBiostats()}");
        }

        // ===== InfoCard =====

        private static void OpenInfoCard()
        {
            InspectionTreeItem item = GetCurrentItem();
            if (item == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            if (item.Data is GeneDef geneDef)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(geneDef));
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        // ===== Tree Building =====

        private static void RebuildAllTrees()
        {
            var selectedList = GetSelectedGenes();
            bool ignoreRestr = dialog != null && (bool)fi_ignoreRestrictions.GetValue(dialog);

            // Build Selected tree (flat list of selected genes)
            selectedRoot = BuildSelectedTree(selectedList);
            selectedVisible.Clear();
            CollectVisibleItems(selectedRoot, selectedVisible);
            if (selectedIdx >= selectedVisible.Count)
                selectedIdx = Math.Max(0, selectedVisible.Count - 1);

            // Build Library tree (categories with gene children)
            libraryRoot = BuildLibraryTree(selectedList, ignoreRestr);
            libraryVisible.Clear();
            CollectVisibleItems(libraryRoot, libraryVisible);
            if (libraryIdx >= libraryVisible.Count)
                libraryIdx = Math.Max(0, libraryVisible.Count - 1);
        }

        private static InspectionTreeItem BuildSelectedTree(List<GeneDef> selectedGenes)
        {
            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = "Root",
                IsExpandable = true,
                IsExpanded = true,
                IndentLevel = -1
            };

            if (selectedGenes == null || selectedGenes.Count == 0)
                return root;

            // Sort to match game's display order
            var sorted = selectedGenes
                .OrderByDescending(g => g.displayCategory?.displayPriorityInXenotype ?? 0)
                .ThenBy(g => g.displayCategory?.label ?? "")
                .ThenBy(g => g.displayOrderInCategory)
                .ThenBy(g => g.label)
                .ToList();

            foreach (var gene in sorted)
            {
                var geneNode = GeneTreeBuilder.CreateGeneNode(gene, root.IndentLevel + 1, includeCategory: false);
                GeneTreeBuilder.AddChild(root, geneNode);
            }

            return root;
        }

        private static InspectionTreeItem BuildLibraryTree(List<GeneDef> selectedGenes, bool ignoreRestrictions)
        {
            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = "Root",
                IsExpandable = true,
                IsExpanded = true,
                IndentLevel = -1
            };

            GeneCategoryDef currentCategory = null;
            InspectionTreeItem categoryNode = null;

            foreach (var gene in GeneUtility.GenesInOrder)
            {
                // Skip archite genes if restrictions not ignored
                if (!ignoreRestrictions && gene.biostatArc > 0)
                    continue;

                // New category?
                if (gene.displayCategory != currentCategory)
                {
                    currentCategory = gene.displayCategory;
                    string catLabel = currentCategory?.LabelCap ?? "Uncategorized";

                    categoryNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.SubCategory,
                        Label = catLabel,
                        Data = currentCategory,
                        IsExpandable = true,
                        IsExpanded = false,
                        IndentLevel = 0
                    };

                    GeneTreeBuilder.AddChild(root, categoryNode);
                }

                // Build gene node under category
                bool isSelected = selectedGenes != null && selectedGenes.Contains(gene);
                string suffix = isSelected ? $" [{((string)"StartingPawnsSelected".Translate()).ToLower()}]" : "";

                var geneNode = GeneTreeBuilder.CreateGeneNode(gene, 1, includeCategory: false);
                geneNode.Label = geneNode.Label + suffix;
                // Ensure Data stores the GeneDef for toggle operations
                geneNode.Data = gene;

                GeneTreeBuilder.AddChild(categoryNode, geneNode);
            }

            return root;
        }

        private static void BuildControlItems()
        {
            controlItems.Clear();

            // 0. Biostats summary
            controlItems.Add(new ControlItem
            {
                Label = FormatCurrentBiostats(),
                OnActivate = () =>
                {
                    TolkHelper.Speak(FormatCurrentBiostats());
                }
            });

            // 1. Xenotype name (Enter to rename)
            controlItems.Add(new ControlItem
            {
                Label = FormatXenotypeName(),
                OnActivate = () =>
                {
                    if (dialog == null) return;
                    string currentName = (string)fi_xenotypeName.GetValue(dialog);
                    isRenaming = true;
                    TextInputHelper.SetText(currentName ?? "", replaceOnType: true);
                    TolkHelper.Speak($"Renaming {currentName}. Type new name and press Enter, Escape to cancel.");
                }
            });

            // 2. Name lock toggle
            controlItems.Add(new ControlItem
            {
                Label = FormatNameLock(),
                Tooltip = FormatNameLockTooltip(),
                OnActivate = () =>
                {
                    if (dialog == null) return;
                    bool locked = (bool)fi_xenotypeNameLocked.GetValue(dialog);
                    fi_xenotypeNameLocked.SetValue(dialog, !locked);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    string desc = !locked
                        ? ((string)"LockNameOn".Translate()).StripTags()
                        : ((string)"LockNameOff".Translate()).StripTags();
                    TolkHelper.Speak(desc);
                    controlItems[controlIdx].Label = FormatNameLock();
                    controlItems[controlIdx].Tooltip = FormatNameLockTooltip();
                }
            });

            // 3. Randomize name
            controlItems.Add(new ControlItem
            {
                Label = ((string)"RandomizeName".Translate()).StripTags(),
                OnActivate = () =>
                {
                    if (dialog == null) return;
                    var genes = GetSelectedGenes();
                    if (genes == null || genes.Count == 0)
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        TolkHelper.Speak(((string)"SelectAGeneToRandomizeName".Translate()).StripTags());
                        return;
                    }
                    string newName = GeneUtility.GenerateXenotypeNameFromGenes(genes);
                    fi_xenotypeName.SetValue(dialog, newName);
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    TolkHelper.Speak($"{((string)"XenotypeName".Translate()).CapitalizeFirst()}: {newName}");
                    BuildControlItems();
                }
            });

            // 4. Inheritable toggle
            controlItems.Add(new ControlItem
            {
                Label = FormatInheritable(),
                Tooltip = ((string)"GenesAreInheritableDesc".Translate()).StripTags(),
                OnActivate = () =>
                {
                    if (dialog == null) return;
                    bool inheritable = (bool)fi_inheritable.GetValue(dialog);
                    fi_inheritable.SetValue(dialog, !inheritable);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    TolkHelper.Speak(FormatInheritable());
                    controlItems[controlIdx].Label = FormatInheritable();
                }
            });

            // 5. Ignore restrictions toggle
            controlItems.Add(new ControlItem
            {
                Label = FormatIgnoreRestrictions(),
                Tooltip = ((string)"IgnoreRestrictionsDesc".Translate()).StripTags(),
                OnActivate = () =>
                {
                    if (dialog == null) return;
                    bool ignoreRestr = (bool)fi_ignoreRestrictions.GetValue(dialog);

                    if (!ignoreRestr)
                    {
                        // Enabling - check if confirmation needed
                        bool confirmSent = (bool)fi_ignoreRestrictionsConfirmationSent.GetValue(null);
                        if (!confirmSent)
                        {
                            fi_ignoreRestrictionsConfirmationSent.SetValue(null, true);
                            // Show confirmation dialog - WindowlessConfirmationState will handle it
                            Find.WindowStack.Add(new Dialog_MessageBox(
                                (string)"IgnoreRestrictionsConfirmation".Translate(),
                                (string)"Yes".Translate(),
                                () =>
                                {
                                    fi_ignoreRestrictions.SetValue(dialog, true);
                                    RebuildAllTrees();
                                    BuildControlItems();
                                    TolkHelper.Speak($"{((string)"IgnoreRestrictions".Translate()).StripTags()}: Yes");
                                },
                                (string)"No".Translate(),
                                () =>
                                {
                                    TolkHelper.Speak($"{((string)"IgnoreRestrictions".Translate()).StripTags()}: No");
                                }));
                            return;
                        }

                        fi_ignoreRestrictions.SetValue(dialog, true);
                        RebuildAllTrees();
                        BuildControlItems();
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        TolkHelper.Speak($"{((string)"IgnoreRestrictions".Translate()).StripTags()}: Yes");
                    }
                    else
                    {
                        // Disabling - remove archite genes
                        fi_ignoreRestrictions.SetValue(dialog, false);
                        var selectedList = GetSelectedGenes();
                        int removed = selectedList.RemoveAll(g => g.biostatArc > 0);
                        mi_onGenesChanged.Invoke(dialog, null);
                        RebuildAllTrees();
                        BuildControlItems();
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        string msg = $"{((string)"IgnoreRestrictions".Translate()).StripTags()}: No";
                        if (removed > 0)
                            msg += $". {removed} archite {(removed == 1 ? "gene" : "genes")} removed.";
                        TolkHelper.Speak(msg);
                    }

                    controlItems[controlIdx].Label = FormatIgnoreRestrictions();
                }
            });

            // 6. Load custom
            controlItems.Add(new ControlItem
            {
                Label = ((string)"LoadCustom".Translate()).StripTags(),
                OnActivate = LoadCustom
            });

            // 7. Load premade
            controlItems.Add(new ControlItem
            {
                Label = ((string)"LoadPremade".Translate()).StripTags(),
                OnActivate = LoadPremade
            });

            // 8. Save and apply
            controlItems.Add(new ControlItem
            {
                Label = ((string)"SaveAndApply".Translate()).CapitalizeFirst().StripTags(),
                OnActivate = SaveAndApply
            });

            // 9. Close
            controlItems.Add(new ControlItem
            {
                Label = (string)"Close".Translate(),
                OnActivate = CloseDialog
            });
        }

        // ===== Visible List Management =====

        private static void RebuildVisibleList(InspectionTreeItem root, List<InspectionTreeItem> visible, ref int idx, int preserveIdx)
        {
            visible.Clear();
            CollectVisibleItems(root, visible);
            if (preserveIdx >= 0 && preserveIdx < visible.Count)
                idx = preserveIdx;
            else if (idx >= visible.Count)
                idx = Math.Max(0, visible.Count - 1);
        }

        private static void RebuildCurrentVisibleList(bool isSelectedTab, int preserveIdx)
        {
            if (isSelectedTab)
                RebuildVisibleList(selectedRoot, selectedVisible, ref selectedIdx, preserveIdx);
            else
                RebuildVisibleList(libraryRoot, libraryVisible, ref libraryIdx, preserveIdx);
        }

        private static void CollectVisibleItems(InspectionTreeItem item, List<InspectionTreeItem> list)
        {
            if (item.IndentLevel >= 0)
                list.Add(item);

            if (item.IsExpanded || item.IndentLevel < 0)
            {
                foreach (var child in item.Children)
                    CollectVisibleItems(child, list);
            }
        }

        // ===== Announcements =====

        private static void AnnounceOpening()
        {
            string header = ((string)"CreateXenotype".Translate()).CapitalizeFirst().StripTags();

            var sb = new System.Text.StringBuilder();
            sb.Append($"{header}. ");

            // Announce current tab (includes item count)
            sb.Append(GetTabAnnouncement());

            TolkHelper.Speak(sb.ToString());
        }

        private static void AnnounceTabSwitch()
        {
            TolkHelper.Speak(GetTabAnnouncement());
        }

        private static string GetTabAnnouncement()
        {
            switch (currentTab)
            {
                case Tab.Selected:
                    string selLabel = ((string)"SelectedGenes".Translate()).StripTags();
                    return $"{selLabel}, {selectedVisible.Count} {(selectedVisible.Count == 1 ? "item" : "items")}.";
                case Tab.Library:
                    string libLabel = ((string)"Genes".Translate()).CapitalizeFirst().StripTags();
                    int categoryCount = libraryRoot?.Children?.Count ?? 0;
                    return $"{libLabel}, {categoryCount} {(categoryCount == 1 ? "category" : "categories")}.";
                case Tab.Controls:
                    return "Controls.";
            }
            return "";
        }

        private static void AnnounceTreeItem(List<InspectionTreeItem> visible, int idx, InspectionTreeItem root)
        {
            if (visible == null || visible.Count == 0 || idx < 0 || idx >= visible.Count)
            {
                TolkHelper.Speak("Empty.");
                return;
            }

            var item = visible[idx];
            string label = item.Label.StripTags().TrimEnd();

            string stateIndicator = "";
            if (item.IsExpandable)
            {
                // Ensure label ends with punctuation for a pause before state indicator
                if (!label.EndsWith(".") && !label.EndsWith("!") && !label.EndsWith("?"))
                    label += ".";
                stateIndicator = item.IsExpanded ? " expanded" : " collapsed";
            }

            var (position, total) = GetSiblingPosition(item, root);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);
            string levelSuffix = MenuHelper.GetLevelSuffix(LevelKey, item.IndentLevel);

            string announcement = string.IsNullOrEmpty(positionPart)
                ? $"{label}{stateIndicator}.{levelSuffix}"
                : $"{label}{stateIndicator}. {positionPart}.{levelSuffix}";

            TolkHelper.Speak(announcement);
        }

        private static void AnnounceWithSearch(List<InspectionTreeItem> visible, int idx, InspectionTreeItem root, TypeaheadSearchHelper typeahead)
        {
            if (visible == null || visible.Count == 0 || idx < 0 || idx >= visible.Count)
                return;

            var item = visible[idx];
            string label = item.Label.StripTags();
            string stateIndicator = item.IsExpandable ? (item.IsExpanded ? " expanded" : " collapsed") : "";

            string announcement = $"{label}{stateIndicator}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";
            TolkHelper.Speak(announcement);
        }

        private static void AnnounceControlItem()
        {
            if (controlIdx < 0 || controlIdx >= controlItems.Count)
                return;

            // Refresh dynamic labels and tooltips before announcing
            if (controlIdx == 0) controlItems[0].Label = FormatCurrentBiostats();
            if (controlIdx == 1) controlItems[1].Label = FormatXenotypeName();
            if (controlIdx == 2)
            {
                controlItems[2].Label = FormatNameLock();
                controlItems[2].Tooltip = FormatNameLockTooltip();
            }
            if (controlIdx == 3) controlItems[3].Label = ((string)"RandomizeName".Translate()).StripTags();
            if (controlIdx == 4) controlItems[4].Label = FormatInheritable();
            if (controlIdx == 5) controlItems[5].Label = FormatIgnoreRestrictions();

            var item = controlItems[controlIdx];
            string positionPart = MenuHelper.FormatPosition(controlIdx, controlItems.Count);

            var sb = new System.Text.StringBuilder();
            sb.Append(item.Label);

            if (!string.IsNullOrEmpty(item.Tooltip))
            {
                sb.Append(". ");
                sb.Append(item.Tooltip);
            }

            if (!string.IsNullOrEmpty(positionPart))
            {
                sb.Append(". ");
                sb.Append(positionPart);
            }

            sb.Append(".");
            TolkHelper.Speak(sb.ToString());
        }

        // ===== Formatting =====

        private static string FormatCurrentBiostats()
        {
            if (dialog == null) return "";

            int gcx = (int)fi_gcx.GetValue(dialog);
            int met = (int)fi_met.GetValue(dialog);
            int arc = (int)fi_arc.GetValue(dialog);

            string complexityLabel = ((string)"Complexity".Translate()).CapitalizeFirst();
            string metabolismLabel = ((string)"Metabolism".Translate()).CapitalizeFirst();

            var sb = new System.Text.StringBuilder();
            sb.Append($"{complexityLabel} {gcx}");
            sb.Append($", {metabolismLabel} {met.ToStringWithSign()}");

            if (arc > 0)
            {
                string architesLabel = ((string)"ArchitesRequired".Translate()).CapitalizeFirst();
                sb.Append($", {architesLabel} {arc}");
            }

            // Announce gene conflicts if any
            var leftChosenGroups = fi_leftChosenGroups.GetValue(dialog) as System.Collections.IList;
            if (leftChosenGroups != null && leftChosenGroups.Count > 0)
            {
                sb.Append($". {((string)"GenesConflict".Translate()).StripTags()}");
            }

            return sb.ToString();
        }

        private static string FormatXenotypeName()
        {
            if (dialog == null) return "";
            string name = (string)fi_xenotypeName.GetValue(dialog);
            string label = ((string)"XenotypeName".Translate()).CapitalizeFirst();
            if (string.IsNullOrEmpty(name))
                return $"{label}: ({((string)"NoneLower".Translate()).StripTags()})";
            return $"{label}: {name}";
        }

        private static string FormatNameLock()
        {
            if (dialog == null) return "";
            bool locked = (bool)fi_xenotypeNameLocked.GetValue(dialog);
            if (locked)
                return ((string)"LockNameOn".Translate()).StripTags();
            else
                return ((string)"LockNameOff".Translate()).StripTags();
        }

        private static string FormatNameLockTooltip()
        {
            return ((string)"LockNameButtonDesc".Translate()).StripTags();
        }

        private static string FormatInheritable()
        {
            if (dialog == null) return "";
            bool inheritable = (bool)fi_inheritable.GetValue(dialog);
            string label = ((string)"GenesAreInheritable".Translate()).StripTags();
            return $"{label}: {(inheritable ? "Yes" : "No")}";
        }

        private static string FormatIgnoreRestrictions()
        {
            if (dialog == null) return "";
            bool ignoreRestr = (bool)fi_ignoreRestrictions.GetValue(dialog);
            string label = ((string)"IgnoreRestrictions".Translate()).StripTags();
            return $"{label}: {(ignoreRestr ? "Yes" : "No")}";
        }

        // ===== Helpers =====

        private static (int position, int total) GetSiblingPosition(InspectionTreeItem item, InspectionTreeItem root)
        {
            var siblings = (item.Parent == null || item.Parent == root) ? root.Children : item.Parent.Children;
            int position = siblings.IndexOf(item) + 1;
            return (position, siblings.Count);
        }

        private static InspectionTreeItem GetCurrentItem()
        {
            switch (currentTab)
            {
                case Tab.Selected:
                    if (selectedIdx >= 0 && selectedIdx < selectedVisible.Count)
                        return selectedVisible[selectedIdx];
                    break;
                case Tab.Library:
                    if (libraryIdx >= 0 && libraryIdx < libraryVisible.Count)
                        return libraryVisible[libraryIdx];
                    break;
            }
            return null;
        }

        private static TypeaheadSearchHelper GetCurrentTypeahead()
        {
            switch (currentTab)
            {
                case Tab.Selected: return selectedTypeahead;
                case Tab.Library: return libraryTypeahead;
            }
            return null;
        }

        private static int GetTreeIdx(bool isSelectedTab)
        {
            return isSelectedTab ? selectedIdx : libraryIdx;
        }

        private static void SetTreeIdx(bool isSelectedTab, int value)
        {
            if (isSelectedTab) selectedIdx = value;
            else libraryIdx = value;
        }

        private static List<string> BuildSearchLabels(List<InspectionTreeItem> visible)
        {
            return visible.Select(item => item.Label.StripTags()).ToList();
        }

        // ===== Reflection Accessors =====

        private static List<GeneDef> GetSelectedGenes()
        {
            return dialog != null ? (List<GeneDef>)fi_selectedGenes.GetValue(dialog) : null;
        }
    }
}
