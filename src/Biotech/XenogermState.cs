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
    /// Manages accessible keyboard navigation for Dialog_CreateXenogerm (gene processor).
    /// Uses a tabbed treeview architecture: Tab/Shift+Tab switches between Selected Genepacks,
    /// Genepack Library, and Controls. Each genepack tab is an InspectionTreeItem treeview
    /// with WCAG tree keyboard navigation.
    /// </summary>
    public static class XenogermState
    {
        private enum Tab { Library, Selected, Controls }

        // ===== Global State =====
        private static bool isActive;
        public static bool IsActive => isActive;
        private static bool isRenaming;
        public static bool IsRenaming => isRenaming;

        private static Window dialog;
        private static Tab currentTab;

        // ===== Per-Tab State: Selected Genepacks =====
        private static InspectionTreeItem selectedRoot;
        private static List<InspectionTreeItem> selectedVisible = new List<InspectionTreeItem>();
        private static int selectedIdx;
        private static TypeaheadSearchHelper selectedTypeahead = new TypeaheadSearchHelper();

        // ===== Per-Tab State: Genepack Library =====
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
            public Action OnActivate;
        }

        // ===== Reflection Cache =====
        private static FieldInfo fi_selectedGenepacks;
        private static FieldInfo fi_libraryGenepacks;
        private static FieldInfo fi_unpoweredGenepacks;
        private static FieldInfo fi_geneAssembler;
        private static FieldInfo fi_xenotypeName;
        private static FieldInfo fi_xenotypeNameLocked;
        private static FieldInfo fi_iconDef;
        private static FieldInfo fi_gcx;
        private static FieldInfo fi_met;
        private static FieldInfo fi_arc;
        private static FieldInfo fi_maxGCX;
        private static MethodInfo mi_onGenesChanged;
        private static MethodInfo mi_accept;
        private static MethodInfo mi_canAccept;
        private static PropertyInfo pi_selectedGenes;

        private const string LevelKey = "XenogermCreation";

        static XenogermState()
        {
            fi_selectedGenepacks = AccessTools.Field(typeof(Dialog_CreateXenogerm), "selectedGenepacks");
            fi_libraryGenepacks = AccessTools.Field(typeof(Dialog_CreateXenogerm), "libraryGenepacks");
            fi_unpoweredGenepacks = AccessTools.Field(typeof(Dialog_CreateXenogerm), "unpoweredGenepacks");
            fi_geneAssembler = AccessTools.Field(typeof(Dialog_CreateXenogerm), "geneAssembler");
            fi_xenotypeName = AccessTools.Field(typeof(GeneCreationDialogBase), "xenotypeName");
            fi_xenotypeNameLocked = AccessTools.Field(typeof(GeneCreationDialogBase), "xenotypeNameLocked");
            fi_iconDef = AccessTools.Field(typeof(GeneCreationDialogBase), "iconDef");
            fi_gcx = AccessTools.Field(typeof(GeneCreationDialogBase), "gcx");
            fi_met = AccessTools.Field(typeof(GeneCreationDialogBase), "met");
            fi_arc = AccessTools.Field(typeof(GeneCreationDialogBase), "arc");
            fi_maxGCX = AccessTools.Field(typeof(GeneCreationDialogBase), "maxGCX");
            mi_onGenesChanged = AccessTools.Method(typeof(GeneCreationDialogBase), "OnGenesChanged");
            mi_accept = AccessTools.Method(typeof(Dialog_CreateXenogerm), "Accept");
            mi_canAccept = AccessTools.Method(typeof(Dialog_CreateXenogerm), "CanAccept");
            pi_selectedGenes = AccessTools.Property(typeof(Dialog_CreateXenogerm), "SelectedGenes");
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

                // Start on Library if nothing selected, otherwise Selected
                var selected = GetSelectedGenepacks();
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
                Log.Error($"[RimWorld Access] Error in XenogermState.Open: {ex}");
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

            // Let float menu (e.g. Load Template) handle its own input
            if (WindowlessFloatMenuState.IsActive)
                return false;

            KeyCode key = ev.keyCode;
            bool shift = ev.shift;
            bool ctrl = ev.control;
            bool alt = ev.alt;

            // Alt+S: Start combining shortcut from any tab
            if (key == KeyCode.S && alt && !ctrl && !shift)
            {
                StartCombining();
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

            // Page Down - jump to next top-level genepack
            if (key == KeyCode.PageDown && !alt)
            {
                JumpToNextGenepack(visible, isSelectedTab, root, forward: true);
                return true;
            }

            // Page Up - jump to previous top-level genepack
            if (key == KeyCode.PageUp && !alt)
            {
                JumpToNextGenepack(visible, isSelectedTab, root, forward: false);
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

        private static void JumpToNextGenepack(List<InspectionTreeItem> visible, bool isSelectedTab, InspectionTreeItem root, bool forward)
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

            // Top-level genepack item (IndentLevel 0) - toggle selection
            if (item.IndentLevel == 0 && item.Data is Genepack genepack)
            {
                ToggleGenepack(genepack, adding: !isSelectedTab);
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

        // ===== Genepack Selection Toggle =====

        private static void ToggleGenepack(Genepack genepack, bool adding)
        {
            if (dialog == null) return;

            var selectedList = GetSelectedGenepacks();
            var unpowered = GetUnpoweredGenepacks();

            if (adding)
            {
                // Check if unpowered
                if (unpowered != null && unpowered.Contains(genepack))
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak(((string)"GenepackUnusableGenebankUnpowered".Translate()).StripTags());
                    return;
                }

                selectedList.Add(genepack);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            else
            {
                selectedList.Remove(genepack);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }

            // Update xenotype name if not locked
            bool nameLocked = (bool)fi_xenotypeNameLocked.GetValue(dialog);
            if (!nameLocked)
            {
                var genes = (List<GeneDef>)pi_selectedGenes.GetValue(dialog);
                string newName = GeneUtility.GenerateXenotypeNameFromGenes(genes);
                fi_xenotypeName.SetValue(dialog, newName);
            }

            // Update biostats
            mi_onGenesChanged.Invoke(dialog, null);

            // Save cursor context
            Genepack cursorPack = null;
            if (currentTab == Tab.Selected && selectedIdx >= 0 && selectedIdx < selectedVisible.Count)
                cursorPack = selectedVisible[selectedIdx].Data as Genepack;
            else if (currentTab == Tab.Library && libraryIdx >= 0 && libraryIdx < libraryVisible.Count)
                cursorPack = libraryVisible[libraryIdx].Data as Genepack;

            // Rebuild trees
            RebuildAllTrees();
            BuildControlItems();

            // Restore cursor
            RestoreCursor(cursorPack);

            // Announce feedback
            string packLabel = genepack.LabelNoCount;
            string action = adding ? "added" : "removed";
            string biostats = FormatCurrentBiostats();
            TolkHelper.Speak($"{packLabel} {action}. {biostats}");
        }

        private static void RestoreCursor(Genepack cursorPack)
        {
            if (cursorPack == null) return;

            // Try to find the genepack in the current tab's tree
            List<InspectionTreeItem> visible;
            switch (currentTab)
            {
                case Tab.Selected:
                    visible = selectedVisible;
                    for (int i = 0; i < visible.Count; i++)
                    {
                        if (visible[i].Data == cursorPack) { selectedIdx = i; return; }
                    }
                    // Pack was removed from selected - clamp index
                    if (selectedIdx >= visible.Count)
                        selectedIdx = Math.Max(0, visible.Count - 1);
                    break;
                case Tab.Library:
                    visible = libraryVisible;
                    for (int i = 0; i < visible.Count; i++)
                    {
                        if (visible[i].Data == cursorPack) { libraryIdx = i; return; }
                    }
                    // Pack was moved to selected - clamp index
                    if (libraryIdx >= visible.Count)
                        libraryIdx = Math.Max(0, visible.Count - 1);
                    break;
            }
        }

        // ===== Start Combining =====

        private static void StartCombining()
        {
            if (dialog == null) return;

            var selectedList = GetSelectedGenepacks();

            // Validate - announce specific errors
            if (selectedList == null || selectedList.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak(((string)"MessageNoSelectedGenepacks".Translate()).StripTags());
                return;
            }

            int arc = (int)fi_arc.GetValue(dialog);
            if (arc > 0 && !ResearchProjectDefOf.Archogenetics.IsFinished)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak(((string)"AssemblingRequiresResearch".Translate(ResearchProjectDefOf.Archogenetics)).StripTags());
                return;
            }

            int gcx = (int)fi_gcx.GetValue(dialog);
            int maxGCX = (int)fi_maxGCX.GetValue(dialog);
            if (gcx > maxGCX)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak(((string)"ComplexityTooHighToCreateXenogerm".Translate(gcx.Named("AMOUNT"), maxGCX.Named("MAX"))).StripTags());
                return;
            }

            string name = (string)fi_xenotypeName.GetValue(dialog);
            if (string.IsNullOrEmpty(name?.Trim()))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak(((string)"XenotypeNameCannotBeEmpty".Translate()).StripTags());
                return;
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

        // ===== Save / Load Templates =====

        private static void SaveTemplate()
        {
            if (dialog == null) return;

            string name = (string)fi_xenotypeName.GetValue(dialog);
            XenotypeIconDef icon = (XenotypeIconDef)fi_iconDef.GetValue(dialog);
            List<Genepack> selected = GetSelectedGenepacks();

            AcceptanceReport result = CustomXenogermUtility.SaveXenogermTemplate(name, icon, selected);
            if (result.Accepted)
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                TolkHelper.Speak(((string)"XenogermTemplateSaved".Translate(name.Named("NAME"))).StripTags());
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak(result.Reason.StripTags());
            }
        }

        private static void LoadTemplate()
        {
            if (dialog == null) return;

            var templates = Find.CustomXenogermDatabase.CustomXenogermsForReading;
            if (templates == null || templates.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No saved templates.");
                return;
            }

            var options = new List<FloatMenuOption>();
            foreach (var template in templates)
            {
                string label = FormatTemplateLabel(template);
                var capturedTemplate = template;
                options.Add(new FloatMenuOption(label, () => ApplyTemplate(capturedTemplate)));
            }

            WindowlessFloatMenuState.Open(options, false);
        }

        private static string FormatTemplateLabel(CustomXenogerm template)
        {
            var geneNames = new List<string>();
            foreach (var geneSet in template.genesets)
            {
                foreach (var gene in geneSet.GenesListForReading)
                {
                    geneNames.Add(gene.LabelCap.ToString());
                }
            }

            if (geneNames.Count > 0)
                return $"{template.name}: {string.Join(", ", geneNames)}";
            return template.name;
        }

        private static void ApplyTemplate(CustomXenogerm template)
        {
            if (dialog == null) return;

            // Set name, lock, and icon
            fi_xenotypeName.SetValue(dialog, template.name);
            fi_xenotypeNameLocked.SetValue(dialog, true);
            fi_iconDef.SetValue(dialog, template.iconDef);

            // Match genepacks from library
            var libraryList = GetLibraryGenepacks();
            var selectedList = GetSelectedGenepacks();
            var matched = CustomXenogermUtility.GetMatchingGenepacks(template.genesets, libraryList).ToList();

            selectedList.Clear();
            selectedList.AddRange(matched);
            mi_onGenesChanged.Invoke(dialog, null);

            // Rebuild everything
            RebuildAllTrees();
            BuildControlItems();

            // Find missing genesets
            var missingGenes = new List<string>();
            foreach (var geneSet in template.genesets)
            {
                if (!matched.Any(gp => gp.GeneSet.Matches(geneSet)))
                {
                    foreach (var gene in geneSet.GenesListForReading)
                        missingGenes.Add(gene.LabelCap.ToString());
                }
            }

            // Announce result
            var sb = new System.Text.StringBuilder();
            sb.Append($"Loaded {template.name}. {matched.Count} genepacks matched.");
            if (missingGenes.Count > 0)
            {
                sb.Append($" Missing genes: {string.Join(", ", missingGenes)}.");
            }
            sb.Append($" {FormatCurrentBiostats()}");

            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.Speak(sb.ToString());
        }

        // ===== InfoCard =====

        private static void OpenInfoCard()
        {
            InspectionTreeItem item = GetCurrentItem();
            if (item == null) return;

            if (item.Data is GeneDef geneDef)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(geneDef));
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            if (item.Data is Genepack genepack)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(genepack));
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        // ===== Tree Building =====

        private static void RebuildAllTrees()
        {
            var selectedList = GetSelectedGenepacks();
            var libraryList = GetLibraryGenepacks();
            var unpowered = GetUnpoweredGenepacks();

            // Build Selected tree
            selectedRoot = BuildGenepackTree(selectedList, unpowered);
            selectedVisible.Clear();
            CollectVisibleItems(selectedRoot, selectedVisible);
            if (selectedIdx >= selectedVisible.Count)
                selectedIdx = Math.Max(0, selectedVisible.Count - 1);

            // Build Library tree (exclude already-selected packs)
            var availableLibrary = new List<Genepack>();
            if (libraryList != null)
            {
                foreach (var pack in libraryList)
                {
                    if (selectedList == null || !selectedList.Contains(pack))
                        availableLibrary.Add(pack);
                }
            }
            libraryRoot = BuildGenepackTree(availableLibrary, unpowered);
            libraryVisible.Clear();
            CollectVisibleItems(libraryRoot, libraryVisible);
            if (libraryIdx >= libraryVisible.Count)
                libraryIdx = Math.Max(0, libraryVisible.Count - 1);
        }

        private static InspectionTreeItem BuildGenepackTree(List<Genepack> packs, List<Genepack> unpowered)
        {
            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = "Root",
                IsExpandable = true,
                IsExpanded = true,
                IndentLevel = -1
            };

            if (packs == null || packs.Count == 0)
                return root;

            foreach (var pack in packs)
            {
                if (pack?.GeneSet == null || pack.GeneSet.GenesListForReading.NullOrEmpty())
                    continue;

                bool isUnpowered = unpowered != null && unpowered.Contains(pack);
                string label = FormatGenepackLabel(pack, isUnpowered);

                var packNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = label,
                    Data = pack,
                    IsExpandable = true,
                    IsExpanded = false,
                    IndentLevel = 0
                };

                // Lazy-load gene children
                packNode.OnActivate = () => BuildGenepackChildren(packNode, pack);

                GeneTreeBuilder.AddChild(root, packNode);
            }

            return root;
        }

        private static void BuildGenepackChildren(InspectionTreeItem packNode, Genepack pack)
        {
            if (packNode.Children.Count > 0) return;

            var genes = pack.GeneSet.GenesListForReading
                .OrderByDescending(g => g.displayCategory?.displayPriorityInGenepack ?? 0)
                .ThenBy(g => g.displayOrderInCategory)
                .ThenBy(g => g.label)
                .ToList();

            foreach (var gene in genes)
            {
                var geneNode = GeneTreeBuilder.CreateGeneNode(gene, packNode.IndentLevel + 1);
                GeneTreeBuilder.AddChild(packNode, geneNode);
            }
        }

        private static void BuildControlItems()
        {
            controlItems.Clear();

            // 1. Biostats summary
            controlItems.Add(new ControlItem
            {
                Label = FormatCurrentBiostats(),
                OnActivate = () =>
                {
                    TolkHelper.Speak(FormatCurrentBiostats());
                }
            });

            // 2. Xenotype name (Enter to rename)
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

            // 3. Name lock toggle
            controlItems.Add(new ControlItem
            {
                Label = FormatNameLock(),
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
                }
            });

            // 4. Randomize name
            controlItems.Add(new ControlItem
            {
                Label = ((string)"RandomizeName".Translate()).StripTags(),
                OnActivate = () =>
                {
                    if (dialog == null) return;
                    var genes = (List<GeneDef>)pi_selectedGenes.GetValue(dialog);
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

            // 5. Save template
            controlItems.Add(new ControlItem
            {
                Label = ((string)"SaveXenogermTemplate".Translate()).StripTags(),
                OnActivate = SaveTemplate
            });

            // 6. Load template
            controlItems.Add(new ControlItem
            {
                Label = ((string)"LoadXenogermTemplate".Translate()).StripTags(),
                OnActivate = LoadTemplate
            });

            // 7. Start Combining
            controlItems.Add(new ControlItem
            {
                Label = (string)"StartCombining".Translate(),
                OnActivate = StartCombining
            });

            // 8. Close
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
            int maxGCX = (int)fi_maxGCX.GetValue(dialog);

            string header = ((string)"AssembleGenes".Translate()).StripTags();
            string complexity = ((string)"Complexity".Translate()).CapitalizeFirst();

            var sb = new System.Text.StringBuilder();
            sb.Append($"{header}. {complexity} limit {maxGCX}. ");

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
                    string selLabel = ((string)"SelectedGenepacks".Translate()).StripTags();
                    return $"{selLabel}, {selectedVisible.Count} {(selectedVisible.Count == 1 ? "item" : "items")}.";
                case Tab.Library:
                    string libLabel = ((string)"GenepackLibrary".Translate()).StripTags();
                    return $"{libLabel}, {libraryVisible.Count} {(libraryVisible.Count == 1 ? "item" : "items")}.";
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
            string label = item.Label.StripTags().TrimEnd('.', '!', '?');

            string stateIndicator = "";
            if (item.IsExpandable)
                stateIndicator = item.IsExpanded ? " expanded" : " collapsed";

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

            // Refresh dynamic labels before announcing
            if (controlIdx == 0) controlItems[0].Label = FormatCurrentBiostats();
            if (controlIdx == 1) controlItems[1].Label = FormatXenotypeName();
            if (controlIdx == 2) controlItems[2].Label = FormatNameLock();
            if (controlIdx == 3) controlItems[3].Label = ((string)"RandomizeName".Translate()).StripTags();

            string label = controlItems[controlIdx].Label;
            string positionPart = MenuHelper.FormatPosition(controlIdx, controlItems.Count);

            string announcement = string.IsNullOrEmpty(positionPart)
                ? $"{label}."
                : $"{label}. {positionPart}.";

            TolkHelper.Speak(announcement);
        }

        // ===== Formatting =====

        private static string FormatGenepackLabel(Genepack pack, bool isUnpowered)
        {
            var sb = new System.Text.StringBuilder();

            // List all gene names so the user can identify the genepack
            var geneNames = pack.GeneSet.GenesListForReading
                .Select(g => g.LabelCap.ToString())
                .ToList();
            sb.Append(string.Join(", ", geneNames));

            int cpx = pack.GeneSet.ComplexityTotal;
            int met = pack.GeneSet.MetabolismTotal;
            int arc = pack.GeneSet.ArchitesTotal;

            string complexityLabel = ((string)"Complexity".Translate()).ToLower();
            string metabolismLabel = ((string)"Metabolism".Translate()).ToLower();

            sb.Append($", {complexityLabel} {cpx}, {metabolismLabel} {met.ToStringWithSign()}");

            if (arc > 0)
            {
                string architesLabel = ((string)"ArchitesRequired".Translate()).ToLower();
                sb.Append($", {architesLabel} {arc}");
            }

            if (isUnpowered)
            {
                sb.Append(", unpowered");
            }

            return sb.ToString();
        }

        private static string FormatCurrentBiostats()
        {
            if (dialog == null) return "";

            int gcx = (int)fi_gcx.GetValue(dialog);
            int met = (int)fi_met.GetValue(dialog);
            int arc = (int)fi_arc.GetValue(dialog);
            int maxGCX = (int)fi_maxGCX.GetValue(dialog);

            string complexityLabel = ((string)"Complexity".Translate()).CapitalizeFirst();
            string metabolismLabel = ((string)"Metabolism".Translate()).CapitalizeFirst();

            var sb = new System.Text.StringBuilder();
            sb.Append($"{complexityLabel} {gcx}");
            if (maxGCX >= 0)
                sb.Append($" of {maxGCX} max");
            sb.Append($", {metabolismLabel} {met.ToStringWithSign()}");

            if (arc > 0)
            {
                string architesLabel = ((string)"ArchitesRequired".Translate()).CapitalizeFirst();
                sb.Append($", {architesLabel} {arc}");
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
            // Use the game's lock description strings
            if (locked)
                return ((string)"LockNameOn".Translate()).StripTags();
            else
                return ((string)"LockNameOff".Translate()).StripTags();
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

        private static List<Genepack> GetSelectedGenepacks()
        {
            return dialog != null ? (List<Genepack>)fi_selectedGenepacks.GetValue(dialog) : null;
        }

        private static List<Genepack> GetLibraryGenepacks()
        {
            return dialog != null ? (List<Genepack>)fi_libraryGenepacks.GetValue(dialog) : null;
        }

        private static List<Genepack> GetUnpoweredGenepacks()
        {
            return dialog != null ? (List<Genepack>)fi_unpoweredGenepacks.GetValue(dialog) : null;
        }
    }
}
