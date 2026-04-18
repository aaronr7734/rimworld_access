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
    /// Gene Library (organized by GeneCategoryDef), and Controls. Each gene tab uses a
    /// TreeNavigationHelper instance with WCAG tree keyboard navigation.
    /// </summary>
    public static class XenotypeEditorState
    {
        private enum Tab { Selected, Library, Controls }

        // ===== Global State =====
        private static bool isActive;
        public static bool IsActive => isActive;
        private static readonly TextInputController renameController = new TextInputController();
        private static readonly TextFieldSpec renameSpec = new TextFieldSpec(
            labelKey: "RimWorldAccess.TextInput.LabelXenotype",
            maxLength: 64,
            minLength: 1);
        public static bool IsRenaming => TextInputManager.Active == renameController;

        private static Window dialog;
        private static Tab currentTab;

        // ===== Per-Tab State: Selected Genes =====
        private static TreeNavigationHelper selectedTreeNav = new TreeNavigationHelper("XenotypeEditorSelected");

        // ===== Per-Tab State: Gene Library =====
        private static TreeNavigationHelper libraryTreeNav = new TreeNavigationHelper("XenotypeEditorLibrary");

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

            // Configure Selected tree helper
            selectedTreeNav.FormatItemAnnouncement = FormatTreeItemAnnouncement;
            selectedTreeNav.FormatSearchAnnouncement = FormatTreeSearchAnnouncement;
            selectedTreeNav.OnActivate = item => HandleTreeEnter(item, isSelectedTab: true);
            selectedTreeNav.OnBeforeExpand = LazyLoadChildren;
            selectedTreeNav.AnnounceChildCounts = false;

            // Configure Library tree helper
            libraryTreeNav.FormatItemAnnouncement = FormatTreeItemAnnouncement;
            libraryTreeNav.FormatSearchAnnouncement = FormatTreeSearchAnnouncement;
            libraryTreeNav.OnActivate = item => HandleTreeEnter(item, isSelectedTab: false);
            libraryTreeNav.OnBeforeExpand = LazyLoadChildren;
            libraryTreeNav.AnnounceChildCounts = false;
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
                controlIdx = 0;
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
            if (TextInputManager.Active == renameController) TextInputManager.Clear();
            dialog = null;
            selectedTreeNav.Reset();
            libraryTreeNav.Reset();
            controlItems.Clear();
            controlIdx = 0;
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
                    return HandleTreeTabInput(ev, key, alt, selectedTreeNav, true);
                case Tab.Library:
                    return HandleTreeTabInput(ev, key, alt, libraryTreeNav, false);
                case Tab.Controls:
                    return HandleControlsInput(key, shift, ctrl, alt, ev);
            }

            return false;
        }

        private static bool HandleTreeTabInput(Event ev, KeyCode key, bool alt, TreeNavigationHelper treeNav, bool isSelectedTab)
        {
            if (treeNav.Count == 0)
                return false;

            // Space - same as Enter (toggle gene / expand / collapse)
            if (key == KeyCode.Space)
            {
                var item = treeNav.SelectedItem;
                if (item != null)
                {
                    if (!HandleTreeEnter(item, isSelectedTab))
                    {
                        // HandleTreeEnter returned false — fall through to default expand/collapse
                        treeNav.ExpandOrDrillDown();
                    }
                }
                return true;
            }

            // Page Down - jump to next top-level item (gene in Selected, category in Library)
            if (key == KeyCode.PageDown && !alt)
            {
                JumpToNextTopLevel(treeNav, forward: true);
                return true;
            }

            // Page Up - jump to previous top-level item
            if (key == KeyCode.PageUp && !alt)
            {
                JumpToNextTopLevel(treeNav, forward: false);
                return true;
            }

            // Left arrow - intercept to restore GeneDef label on collapse
            if (key == KeyCode.LeftArrow && !alt)
            {
                HandleLeftArrow(treeNav);
                return true;
            }

            // Delegate all other input to TreeNavigationHelper
            return treeNav.HandleInput(ev);
        }

        /// <summary>
        /// Handles left arrow with GeneDef label restoration on collapse.
        /// </summary>
        private static void HandleLeftArrow(TreeNavigationHelper treeNav)
        {
            var item = treeNav.SelectedItem;
            if (item == null)
                return;

            // If this is an expanded GeneDef node, restore the rich label before collapsing
            if (item.IsExpandable && item.IsExpanded && item.Data is GeneDef && !string.IsNullOrEmpty(item.Description))
            {
                item.Label = item.Description;
            }

            treeNav.CollapseOrDrillUp();
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
            GetCurrentTreeNav()?.Typeahead.ClearSearch();

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
            var treeNav = GetCurrentTreeNav();
            if (treeNav != null && treeNav.HasActiveSearch)
            {
                treeNav.Typeahead.ClearSearchAndAnnounce();
                treeNav.ReannounceCurrentItem();
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

        // Rename input flows through TextInputManager (priority -1.6 in UnifiedKeyboardPatch).
        // Callbacks run when the controller commits or cancels.

        private static void OnRenameCancel()
        {
            SoundDefOf.Click.PlayOneShotOnCamera();
            TolkHelper.Speak("Rename cancelled.");
        }

        private static void OnRenameConfirm(string newName)
        {
            if (dialog == null) return;
            fi_xenotypeName.SetValue(dialog, newName);
            // Auto-lock the name so it doesn't get overwritten by gene changes
            fi_xenotypeNameLocked.SetValue(dialog, true);
            BuildControlItems();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.Speak($"Renamed to {newName}. Name locked.");
        }

        // ===== Lazy Loading =====

        private static void LazyLoadChildren(InspectionTreeItem item)
        {
            if (item.OnActivate != null && item.Children.Count == 0)
                item.OnActivate();
        }

        // ===== Enter Key Actions =====

        private static bool HandleTreeEnter(InspectionTreeItem item, bool isSelectedTab)
        {
            if (item == null)
                return true;

            // Gene item (IndentLevel 0 in Selected, IndentLevel 1 in Library) - toggle selection
            if (item.Data is GeneDef gene)
            {
                ToggleGene(gene);
                return true;
            }

            // Category node in Library (IndentLevel 0, Data is GeneCategoryDef) - let default handle expand/collapse
            if (item.Data is GeneCategoryDef)
            {
                return false; // fall through to default toggle behavior
            }

            // Expandable node - let default handle expand/collapse
            if (item.IsExpandable)
                return false;

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            return true;
        }

        // ===== Page Up/Down: Jump Between Top-Level Items =====

        private static void JumpToNextTopLevel(TreeNavigationHelper treeNav, bool forward)
        {
            if (treeNav.Count == 0)
                return;

            var visible = treeNav.VisibleItems;
            int idx = treeNav.SelectedIndex;
            int start = idx;
            int direction = forward ? 1 : -1;
            int current = idx + direction;

            while (current >= 0 && current < visible.Count)
            {
                if (visible[current].IndentLevel == 0)
                {
                    treeNav.SetSelectedIndex(current);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    treeNav.ReannounceCurrentItem();
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
                        treeNav.SetSelectedIndex(current);
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        treeNav.ReannounceCurrentItem();
                        return;
                    }
                    current += direction;
                    if (current < 0 || current >= visible.Count)
                        break;
                }
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

            switch (currentTab)
            {
                case Tab.Selected:
                    for (int i = 0; i < selectedTreeNav.VisibleItems.Count; i++)
                    {
                        if (selectedTreeNav.VisibleItems[i].Data is GeneDef g && g == cursorGene) { selectedTreeNav.SetSelectedIndex(i); return; }
                    }
                    // Gene was removed from selected - index already clamped by Initialize
                    break;
                case Tab.Library:
                    for (int i = 0; i < libraryTreeNav.VisibleItems.Count; i++)
                    {
                        if (libraryTreeNav.VisibleItems[i].Data is GeneDef g && g == cursorGene) { libraryTreeNav.SetSelectedIndex(i); return; }
                    }
                    // Index already clamped by Initialize
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
            var selRoot = BuildSelectedTree(selectedList);
            selectedTreeNav.Initialize(selRoot);

            // Build Library tree (categories with gene children)
            var libRoot = BuildLibraryTree(selectedList, ignoreRestr);
            libraryTreeNav.Initialize(libRoot);
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
                    renameController.Begin(currentName ?? string.Empty, renameSpec, OnRenameConfirm, OnRenameCancel, replaceOnType: true);
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
                    return $"{selLabel}, {selectedTreeNav.Count} {(selectedTreeNav.Count == 1 ? "item" : "items")}.";
                case Tab.Library:
                    string libLabel = ((string)"Genes".Translate()).CapitalizeFirst().StripTags();
                    int categoryCount = libraryTreeNav.RootItem?.Children?.Count ?? 0;
                    return $"{libLabel}, {categoryCount} {(categoryCount == 1 ? "category" : "categories")}.";
                case Tab.Controls:
                    return "Controls.";
            }
            return "";
        }

        /// <summary>
        /// Custom announcement format for tree items.
        /// Format: "{label stripped}{punctuation}{space+expanded/collapsed}. {position}.{levelSuffix}"
        /// </summary>
        private static string FormatTreeItemAnnouncement(InspectionTreeItem item)
        {
            string label = item.Label.StripTags().TrimEnd();

            string stateIndicator = "";
            if (item.IsExpandable)
            {
                // Ensure label ends with punctuation for a pause before state indicator
                if (!label.EndsWith(".") && !label.EndsWith("!") && !label.EndsWith("?"))
                    label += ".";
                stateIndicator = TreeNavigationHelper.FormatExpansionSpaceSuffix(item);
            }

            var treeNav = GetTreeNavForItem(item);
            var (position, total) = treeNav.GetSiblingPosition(item);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);
            string levelSuffix = MenuHelper.GetLevelSuffix(LevelKey, item.IndentLevel);

            string announcement = string.IsNullOrEmpty(positionPart)
                ? $"{label}{stateIndicator}.{levelSuffix}"
                : $"{label}{stateIndicator}. {positionPart}.{levelSuffix}";

            return announcement;
        }

        /// <summary>
        /// Custom search announcement format for tree items.
        /// </summary>
        private static string FormatTreeSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead)
        {
            string label = item.Label.StripTags();
            string stateIndicator = TreeNavigationHelper.FormatExpansionSpaceSuffix(item);

            return typeahead.BuildItemAnnouncement($"{label}{stateIndicator}");
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

        private static TreeNavigationHelper GetCurrentTreeNav()
        {
            switch (currentTab)
            {
                case Tab.Selected: return selectedTreeNav;
                case Tab.Library: return libraryTreeNav;
            }
            return null;
        }

        /// <summary>
        /// Gets the TreeNavigationHelper that owns a given item by checking which tree contains it.
        /// Falls back to the current tab's tree.
        /// </summary>
        private static TreeNavigationHelper GetTreeNavForItem(InspectionTreeItem item)
        {
            if (selectedTreeNav.VisibleItems.Contains(item))
                return selectedTreeNav;
            if (libraryTreeNav.VisibleItems.Contains(item))
                return libraryTreeNav;
            return GetCurrentTreeNav() ?? selectedTreeNav;
        }

        private static InspectionTreeItem GetCurrentItem()
        {
            switch (currentTab)
            {
                case Tab.Selected:
                    return selectedTreeNav.SelectedItem;
                case Tab.Library:
                    return libraryTreeNav.SelectedItem;
            }
            return null;
        }

        // ===== Reflection Accessors =====

        private static List<GeneDef> GetSelectedGenes()
        {
            return dialog != null ? (List<GeneDef>)fi_selectedGenes.GetValue(dialog) : null;
        }
    }
}
