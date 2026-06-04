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
    /// Genepack Library, and Controls. Each genepack tab uses a TreeNavigationHelper instance
    /// with WCAG tree keyboard navigation.
    /// </summary>
    public static class XenogermState
    {
        private enum Tab { Library, Selected, Controls }

        // ===== Global State =====
        private static bool isActive;
        public static bool IsActive => isActive;
        private static readonly TextInputController renameController = new TextInputController();
        private static readonly TextFieldSpec renameSpec = new TextFieldSpec(
            labelKey: "RimWorldAccess.TextInput.LabelXenogerm",
            maxLength: 64,
            minLength: 1);
        public static bool IsRenaming => TextInputManager.Active == renameController;

        private static Window dialog;
        private static Tab currentTab;

        // ===== Per-Tab State: Selected Genepacks =====
        private static TreeNavigationHelper selectedTreeNav = new TreeNavigationHelper("XenogermSelected");

        // ===== Per-Tab State: Genepack Library =====
        private static TreeNavigationHelper libraryTreeNav = new TreeNavigationHelper("XenogermLibrary");

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

                // Start on Library if nothing selected, otherwise Selected
                var selected = GetSelectedGenepacks();
                currentTab = (selected != null && selected.Count > 0) ? Tab.Selected : Tab.Library;

                // Reset navigation
                controlIdx = 0;
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

            // Space - same as Enter (toggle genepack / expand / collapse)
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

            // Page Down - jump to next top-level genepack
            if (key == KeyCode.PageDown && !alt)
            {
                JumpToNextGenepack(treeNav, forward: true);
                return true;
            }

            // Page Up - jump to previous top-level genepack
            if (key == KeyCode.PageUp && !alt)
            {
                JumpToNextGenepack(treeNav, forward: false);
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

        // Rename input flows through TextInputManager (priority -1.6 in UnifiedKeyboardPatch).

        private static void OnRenameCancel()
        {
            SoundDefOf.Click.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Biotech.Xenogerm.RenameCancelled".Loc());
        }

        private static void OnRenameConfirm(string newName)
        {
            if (dialog == null) return;
            fi_xenotypeName.SetValue(dialog, newName);
            // Auto-lock the name so it doesn't get overwritten by gene changes
            fi_xenotypeNameLocked.SetValue(dialog, true);
            BuildControlItems();

            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Biotech.Xenogerm.Renamed".Loc(newName));
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

            // Top-level genepack item (IndentLevel 0) - toggle selection
            if (item.IndentLevel == 0 && item.Data is Genepack genepack)
            {
                ToggleGenepack(genepack, adding: !isSelectedTab);
                return true;
            }

            // Let TreeNavigationHelper handle expandable nodes (toggle expand/collapse)
            // and reject non-actionable leaf nodes
            if (item.IsExpandable)
                return false; // fall through to default toggle behavior

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            return true;
        }

        // ===== Page Up/Down: Jump Between Genepacks =====

        private static void JumpToNextGenepack(TreeNavigationHelper treeNav, bool forward)
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
            if (currentTab == Tab.Selected)
            {
                var item = selectedTreeNav.SelectedItem;
                cursorPack = item?.Data as Genepack;
            }
            else if (currentTab == Tab.Library)
            {
                var item = libraryTreeNav.SelectedItem;
                cursorPack = item?.Data as Genepack;
            }

            // Rebuild trees
            RebuildAllTrees();
            BuildControlItems();

            // Restore cursor
            RestoreCursor(cursorPack);

            // Announce feedback
            string packLabel = genepack.LabelNoCount;
            string biostats = FormatCurrentBiostats();
            TolkHelper.Speak(adding
                ? "RimWorldAccess.Biotech.Xenogerm.PackAdded".Translate(packLabel, biostats)
                : "RimWorldAccess.Biotech.Xenogerm.PackRemoved".Translate(packLabel, biostats));
        }

        private static void RestoreCursor(Genepack cursorPack)
        {
            if (cursorPack == null) return;

            switch (currentTab)
            {
                case Tab.Selected:
                    for (int i = 0; i < selectedTreeNav.VisibleItems.Count; i++)
                    {
                        if (selectedTreeNav.VisibleItems[i].Data == cursorPack) { selectedTreeNav.SetSelectedIndex(i); return; }
                    }
                    // Pack was removed from selected - index already clamped by Initialize
                    break;
                case Tab.Library:
                    for (int i = 0; i < libraryTreeNav.VisibleItems.Count; i++)
                    {
                        if (libraryTreeNav.VisibleItems[i].Data == cursorPack) { libraryTreeNav.SetSelectedIndex(i); return; }
                    }
                    // Pack was moved to selected - index already clamped by Initialize
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
                TolkHelper.Speak("RimWorldAccess.Biotech.Xenogerm.NoSavedTemplates".Loc());
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
                return "RimWorldAccess.Biotech.Xenogerm.TemplateLabel".Translate(
                    template.name, string.Join(", ", geneNames));
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
            sb.Append("RimWorldAccess.Biotech.Xenogerm.TemplateLoaded".Translate(template.name, matched.Count));
            if (missingGenes.Count > 0)
            {
                sb.Append("RimWorldAccess.Biotech.Xenogerm.TemplateMissingGenes".Translate(string.Join(", ", missingGenes)));
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
            var selRoot = BuildGenepackTree(selectedList, unpowered);
            selectedTreeNav.Initialize(selRoot);

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
            var libRoot = BuildGenepackTree(availableLibrary, unpowered);
            libraryTreeNav.Initialize(libRoot);
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
                    renameController.Begin(currentName ?? string.Empty, renameSpec, OnRenameConfirm, OnRenameCancel, replaceOnType: true);
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

        // ===== Announcements =====

        private static void AnnounceOpening()
        {
            int maxGCX = (int)fi_maxGCX.GetValue(dialog);

            string header = ((string)"AssembleGenes".Translate()).StripTags();
            string complexity = ((string)"Complexity".Translate()).CapitalizeFirst();

            TolkHelper.Speak("RimWorldAccess.Biotech.Xenogerm.OpeningSummary".Loc(
                header, complexity, maxGCX, GetTabAnnouncement()));
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
                    return selectedTreeNav.Count == 1
                        ? "RimWorldAccess.Biotech.Xenogerm.TabSummaryOne".Translate(selLabel)
                        : "RimWorldAccess.Biotech.Xenogerm.TabSummaryMany".Translate(selLabel, selectedTreeNav.Count);
                case Tab.Library:
                    string libLabel = ((string)"GenepackLibrary".Translate()).StripTags();
                    return libraryTreeNav.Count == 1
                        ? "RimWorldAccess.Biotech.Xenogerm.TabSummaryOne".Translate(libLabel)
                        : "RimWorldAccess.Biotech.Xenogerm.TabSummaryMany".Translate(libLabel, libraryTreeNav.Count);
                case Tab.Controls:
                    return "RimWorldAccess.Biotech.Xenogerm.Controls".Translate();
            }
            return "";
        }

        /// <summary>
        /// Custom announcement format for tree items.
        /// Format: "{label stripped}{space+expanded/collapsed}. {position}.{levelSuffix}"
        /// </summary>
        private static string FormatTreeItemAnnouncement(InspectionTreeItem item)
        {
            string label = item.Label.StripTags().TrimEnd('.', '!', '?');

            string stateIndicator = TreeNavigationHelper.FormatExpansionSpaceSuffix(item);

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
                sb.Append(", ");
                sb.Append("RimWorldAccess.Biotech.Xenogerm.PackUnpowered".Translate());
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
            if (maxGCX >= 0)
                sb.Append("RimWorldAccess.Biotech.Xenogerm.BiostatsOfMax".Translate(complexityLabel, gcx, maxGCX));
            else
                sb.Append("RimWorldAccess.Biotech.Xenogerm.BiostatsNoMax".Translate(complexityLabel, gcx));
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
                return "RimWorldAccess.Biotech.Xenogerm.NameNone".Translate(
                    label, ((string)"NoneLower".Translate()).StripTags());
            return "RimWorldAccess.Biotech.Xenogerm.NameWithValue".Translate(label, name);
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
            // Check if item is in the selected tree
            if (selectedTreeNav.VisibleItems.Contains(item))
                return selectedTreeNav;
            if (libraryTreeNav.VisibleItems.Contains(item))
                return libraryTreeNav;
            // Fallback
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
