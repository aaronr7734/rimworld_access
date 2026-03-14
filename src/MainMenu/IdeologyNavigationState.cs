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
    public static class IdeologyNavigationState
    {
        private const int TabOptions = 0;
        private const int TabPresets = 1;
        private const string TreeLevelKey = "IdeoPresets";

        private static bool initialized;
        private static int currentTab;

        public static bool IsActive { get; private set; }

        // Options tab
        private static List<OptionEntry> options = new List<OptionEntry>();
        private static int optionsIndex;
        private static TypeaheadSearchHelper optionsTypeahead = new TypeaheadSearchHelper();

        // Presets tab (tree)
        private static InspectionTreeItem rootItem;
        private static List<InspectionTreeItem> visibleItems = new List<InspectionTreeItem>();
        private static int selectedTreeIndex;
        private static TypeaheadSearchHelper presetsTypeahead = new TypeaheadSearchHelper();

        public static int CurrentTab => currentTab;

        private class OptionEntry
        {
            public string Label;
            public string Description;
            public int EnumValue; // 0=Classic, 1=CustomFluid, 2=CustomFixed, 3=Load
        }

        #region Initialize / Reset

        public static void Initialize()
        {
            if (initialized)
                return;

            // Build options
            options.Clear();
            options.Add(new OptionEntry
            {
                Label = "PlayClassic".Translate().ToString(),
                Description = IdeoPresetCategoryDefOf.Classic.description,
                EnumValue = 0
            });
            options.Add(new OptionEntry
            {
                Label = "CreateCustomFluid".Translate().ToString().Replace("\n", " "),
                Description = IdeoPresetCategoryDefOf.Fluid.description,
                EnumValue = 1
            });
            options.Add(new OptionEntry
            {
                Label = "CreateCustomFixed".Translate().ToString().Replace("\n", " "),
                Description = IdeoPresetCategoryDefOf.Custom.description,
                EnumValue = 2
            });
            options.Add(new OptionEntry
            {
                Label = "LoadSaved".Translate().ToString(),
                Description = "",
                EnumValue = 3
            });

            // Build preset tree
            BuildPresetTree();

            currentTab = TabOptions;
            optionsIndex = 0;
            selectedTreeIndex = 0;
            optionsTypeahead.ClearSearch();
            presetsTypeahead.ClearSearch();
            MenuHelper.ResetLevel(TreeLevelKey);
            IsActive = true;
            initialized = true;
        }

        public static void Reset()
        {
            initialized = false;
            IsActive = false;
            currentTab = TabOptions;
            optionsIndex = 0;
            selectedTreeIndex = 0;
            options.Clear();
            rootItem = null;
            visibleItems.Clear();
            optionsTypeahead.ClearSearch();
            presetsTypeahead.ClearSearch();
            MenuHelper.ResetLevel(TreeLevelKey);
        }

        private static void BuildPresetTree()
        {
            rootItem = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpandable = true,
                IsExpanded = true,
                Type = InspectionTreeItem.ItemType.Category
            };

            // Get preset categories (excluding Classic, Custom, Fluid — those are on the options tab)
            var categories = DefDatabase<IdeoPresetCategoryDef>.AllDefsListForReading
                .Where(c => c != IdeoPresetCategoryDefOf.Classic
                         && c != IdeoPresetCategoryDefOf.Custom
                         && c != IdeoPresetCategoryDefOf.Fluid)
                .ToList();

            foreach (var category in categories)
            {
                var presets = DefDatabase<IdeoPresetDef>.AllDefs
                    .Where(p => p.categoryDef == category)
                    .ToList();

                if (presets.Count == 0)
                    continue;

                // Category node (level 0)
                var categoryNode = new InspectionTreeItem
                {
                    Label = category.LabelCap + ": " + category.description,
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Type = InspectionTreeItem.ItemType.Category,
                    Data = category,
                    Parent = rootItem
                };

                foreach (var preset in presets)
                {
                    // Preset node (level 1)
                    var presetNode = new InspectionTreeItem
                    {
                        Label = preset.LabelCap + ": " + preset.description,
                        IndentLevel = 1,
                        IsExpandable = preset.memes != null && preset.memes.Count > 0,
                        IsExpanded = false,
                        Type = InspectionTreeItem.ItemType.Item,
                        Data = preset,
                        Parent = categoryNode
                    };

                    if (preset.memes != null)
                    {
                        foreach (var meme in preset.memes)
                        {
                            var memeNode = new InspectionTreeItem
                            {
                                Label = meme.LabelCap + ": " + meme.description,
                                IndentLevel = 2,
                                IsExpandable = false,
                                IsExpanded = false,
                                Type = InspectionTreeItem.ItemType.DetailText,
                                Data = meme,
                                LinkedDef = meme,
                                Parent = presetNode
                            };
                            presetNode.Children.Add(memeNode);
                        }
                    }

                    categoryNode.Children.Add(presetNode);
                }

                rootItem.Children.Add(categoryNode);
            }

            RebuildVisibleItems();
        }

        private static void RebuildVisibleItems()
        {
            visibleItems.Clear();
            if (rootItem == null)
                return;

            // Root is hidden — add only its visible descendants
            foreach (var child in rootItem.Children)
            {
                AddVisibleRecursive(child);
            }
        }

        private static void AddVisibleRecursive(InspectionTreeItem item)
        {
            visibleItems.Add(item);
            if (item.IsExpanded && item.Children.Count > 0)
            {
                foreach (var child in item.Children)
                {
                    AddVisibleRecursive(child);
                }
            }
        }

        #endregion

        #region Tab Switching

        public static void SwitchTab()
        {
            optionsTypeahead.ClearSearch();
            presetsTypeahead.ClearSearch();

            if (currentTab == TabOptions)
            {
                currentTab = TabPresets;
                if (visibleItems.Count > 0 && selectedTreeIndex >= visibleItems.Count)
                    selectedTreeIndex = 0;
                MenuHelper.ResetLevel(TreeLevelKey);
                AnnounceTabSwitch();
            }
            else
            {
                currentTab = TabOptions;
                AnnounceTabSwitch();
            }
        }

        private static void AnnounceTabSwitch()
        {
            if (currentTab == TabOptions)
            {
                string tabName = "Options";
                TolkHelper.Speak(tabName + ". " + BuildOptionAnnouncement());
            }
            else
            {
                string tabName = "Presets";
                if (visibleItems.Count > 0)
                    TolkHelper.Speak(tabName + ". " + BuildTreeItemAnnouncement());
                else
                    TolkHelper.Speak(tabName + ". " + "NoneLower".Translate() + ".");
            }
        }

        #endregion

        #region Options Tab Navigation

        public static void NavigateOptionUp()
        {
            if (options.Count == 0) return;

            if (optionsTypeahead.HasActiveSearch && !optionsTypeahead.HasNoMatches)
            {
                int prev = optionsTypeahead.GetPreviousMatch(optionsIndex);
                if (prev >= 0)
                {
                    optionsIndex = prev;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceOptionWithSearch();
                }
                return;
            }

            int newIndex = MenuHelper.SelectPrevious(optionsIndex, options.Count);
            if (newIndex != optionsIndex)
            {
                optionsIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            TolkHelper.Speak(BuildOptionAnnouncement());
        }

        public static void NavigateOptionDown()
        {
            if (options.Count == 0) return;

            if (optionsTypeahead.HasActiveSearch && !optionsTypeahead.HasNoMatches)
            {
                int next = optionsTypeahead.GetNextMatch(optionsIndex);
                if (next >= 0)
                {
                    optionsIndex = next;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceOptionWithSearch();
                }
                return;
            }

            int newIndex = MenuHelper.SelectNext(optionsIndex, options.Count);
            if (newIndex != optionsIndex)
            {
                optionsIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            TolkHelper.Speak(BuildOptionAnnouncement());
        }

        public static void NavigateOptionHome()
        {
            if (options.Count == 0) return;
            optionsTypeahead.ClearSearch();
            optionsIndex = 0;
            TolkHelper.Speak(BuildOptionAnnouncement());
        }

        public static void NavigateOptionEnd()
        {
            if (options.Count == 0) return;
            optionsTypeahead.ClearSearch();
            optionsIndex = options.Count - 1;
            TolkHelper.Speak(BuildOptionAnnouncement());
        }

        public static bool HandleOptionTypeahead(char c)
        {
            var labels = options.Select(o => o.Label).ToList();
            if (optionsTypeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                optionsIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceOptionWithSearch();
                return true;
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak($"No matches for '{optionsTypeahead.LastFailedSearch}'.");
                return true;
            }
        }

        public static bool HandleOptionBackspace()
        {
            if (!optionsTypeahead.HasActiveSearch)
                return false;

            var labels = options.Select(o => o.Label).ToList();
            if (optionsTypeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                    optionsIndex = newIndex;
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceOptionWithSearch();
            }
            return true;
        }

        public static bool HasOptionsSearch => optionsTypeahead.HasActiveSearch;

        public static void ClearOptionsSearch()
        {
            optionsTypeahead.ClearSearchAndAnnounce();
            TolkHelper.Speak(BuildOptionAnnouncement());
        }

        private static string BuildOptionAnnouncement()
        {
            if (options.Count == 0 || optionsIndex < 0 || optionsIndex >= options.Count)
                return "";

            var opt = options[optionsIndex];
            var sb = new StringBuilder(opt.Label);
            if (!string.IsNullOrEmpty(opt.Description))
                sb.Append(". ").Append(opt.Description);

            string position = MenuHelper.FormatPosition(optionsIndex, options.Count);
            if (!string.IsNullOrEmpty(position))
                sb.Append(". ").Append(position);

            return sb.ToString();
        }

        private static void AnnounceOptionWithSearch()
        {
            if (options.Count == 0 || optionsIndex < 0 || optionsIndex >= options.Count)
                return;

            string name = options[optionsIndex].Label;
            string searchInfo = $", {optionsTypeahead.CurrentMatchPosition} of {optionsTypeahead.MatchCount} matches for '{optionsTypeahead.SearchBuffer}'";
            TolkHelper.Speak($"{name}{searchInfo}");
        }

        public static void AnnounceCurrentOption()
        {
            TolkHelper.Speak(BuildOptionAnnouncement());
        }

        #endregion

        #region Presets Tab Tree Navigation

        public static void NavigateTreeUp()
        {
            if (visibleItems.Count == 0) return;

            if (presetsTypeahead.HasActiveSearch && !presetsTypeahead.HasNoMatches)
            {
                int prev = presetsTypeahead.GetPreviousMatch(selectedTreeIndex);
                if (prev >= 0)
                {
                    selectedTreeIndex = prev;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceTreeWithSearch();
                }
                return;
            }

            int newIndex = MenuHelper.SelectPrevious(selectedTreeIndex, visibleItems.Count);
            if (newIndex != selectedTreeIndex)
            {
                selectedTreeIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            TolkHelper.Speak(BuildTreeItemAnnouncement());
        }

        public static void NavigateTreeDown()
        {
            if (visibleItems.Count == 0) return;

            if (presetsTypeahead.HasActiveSearch && !presetsTypeahead.HasNoMatches)
            {
                int next = presetsTypeahead.GetNextMatch(selectedTreeIndex);
                if (next >= 0)
                {
                    selectedTreeIndex = next;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceTreeWithSearch();
                }
                return;
            }

            int newIndex = MenuHelper.SelectNext(selectedTreeIndex, visibleItems.Count);
            if (newIndex != selectedTreeIndex)
            {
                selectedTreeIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            TolkHelper.Speak(BuildTreeItemAnnouncement());
        }

        public static void ExpandOrDrillDown()
        {
            if (visibleItems.Count == 0 || selectedTreeIndex < 0 || selectedTreeIndex >= visibleItems.Count)
                return;

            var item = visibleItems[selectedTreeIndex];

            if (item.IsExpandable && !item.IsExpanded)
            {
                // Expand
                item.IsExpanded = true;
                RebuildVisibleItems();
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                TolkHelper.Speak("Expanded. " + BuildTreeItemAnnouncement());
            }
            else if (item.IsExpanded && item.Children.Count > 0)
            {
                // Drill to first child
                int childIndex = visibleItems.IndexOf(item.Children[0]);
                if (childIndex >= 0)
                {
                    selectedTreeIndex = childIndex;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    TolkHelper.Speak(BuildTreeItemAnnouncement());
                }
            }
        }

        public static void CollapseOrDrillUp()
        {
            if (visibleItems.Count == 0 || selectedTreeIndex < 0 || selectedTreeIndex >= visibleItems.Count)
                return;

            var item = visibleItems[selectedTreeIndex];

            if (item.IsExpandable && item.IsExpanded)
            {
                // Collapse
                item.IsExpanded = false;
                RebuildVisibleItems();
                // Clamp index
                if (selectedTreeIndex >= visibleItems.Count)
                    selectedTreeIndex = visibleItems.Count - 1;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                TolkHelper.Speak("Collapsed. " + BuildTreeItemAnnouncement());
            }
            else if (item.Parent != null && item.Parent != rootItem)
            {
                // Drill up to parent
                int parentIndex = visibleItems.IndexOf(item.Parent);
                if (parentIndex >= 0)
                {
                    selectedTreeIndex = parentIndex;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    TolkHelper.Speak(BuildTreeItemAnnouncement());
                }
            }
        }

        public static void ToggleExpand()
        {
            if (visibleItems.Count == 0 || selectedTreeIndex < 0 || selectedTreeIndex >= visibleItems.Count)
                return;

            var item = visibleItems[selectedTreeIndex];
            if (!item.IsExpandable)
                return;

            item.IsExpanded = !item.IsExpanded;
            RebuildVisibleItems();
            if (selectedTreeIndex >= visibleItems.Count)
                selectedTreeIndex = visibleItems.Count - 1;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            string state = item.IsExpanded ? "Expanded" : "Collapsed";
            TolkHelper.Speak(state + ". " + BuildTreeItemAnnouncement());
        }

        public static void ExpandAllSiblings()
        {
            if (visibleItems.Count == 0 || selectedTreeIndex < 0 || selectedTreeIndex >= visibleItems.Count)
                return;

            var current = visibleItems[selectedTreeIndex];
            int currentLevel = current.IndentLevel;
            int expanded = 0;

            // Find parent's children at same level
            var parent = current.Parent ?? rootItem;
            foreach (var sibling in parent.Children)
            {
                if (sibling.IndentLevel == currentLevel && sibling.IsExpandable && !sibling.IsExpanded)
                {
                    sibling.IsExpanded = true;
                    expanded++;
                }
            }

            if (expanded > 0)
            {
                RebuildVisibleItems();
                // Maintain selection on same item
                int newIndex = visibleItems.IndexOf(current);
                if (newIndex >= 0)
                    selectedTreeIndex = newIndex;
                TolkHelper.Speak($"Expanded {expanded} items");
            }
        }

        public static void HandleTreeHome(bool ctrl)
        {
            if (visibleItems.Count == 0) return;
            presetsTypeahead.ClearSearch();

            MenuHelper.HandleTreeHomeKey(
                visibleItems, ref selectedTreeIndex,
                item => item.IndentLevel, ctrl,
                () => TolkHelper.Speak(BuildTreeItemAnnouncement()));
        }

        public static void HandleTreeEnd(bool ctrl)
        {
            if (visibleItems.Count == 0) return;
            presetsTypeahead.ClearSearch();

            MenuHelper.HandleTreeEndKey(
                visibleItems, ref selectedTreeIndex,
                item => item.IndentLevel,
                item => item.IsExpanded,
                item => item.Children.Count > 0,
                ctrl,
                () => TolkHelper.Speak(BuildTreeItemAnnouncement()));
        }

        public static bool HandlePresetTypeahead(char c)
        {
            var labels = visibleItems.Select(v => v.Label).ToList();
            if (presetsTypeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                selectedTreeIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceTreeWithSearch();
                return true;
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak($"No matches for '{presetsTypeahead.LastFailedSearch}'.");
                return true;
            }
        }

        public static bool HandlePresetBackspace()
        {
            if (!presetsTypeahead.HasActiveSearch)
                return false;

            var labels = visibleItems.Select(v => v.Label).ToList();
            if (presetsTypeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                    selectedTreeIndex = newIndex;
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceTreeWithSearch();
            }
            return true;
        }

        public static bool HasPresetsSearch => presetsTypeahead.HasActiveSearch;

        public static void ClearPresetsSearch()
        {
            presetsTypeahead.ClearSearchAndAnnounce();
            TolkHelper.Speak(BuildTreeItemAnnouncement());
        }

        /// <summary>
        /// Returns the indent level of the currently focused tree item.
        /// 0 = category, 1 = preset, 2 = meme.
        /// </summary>
        public static int CurrentTreeNodeLevel
        {
            get
            {
                if (visibleItems.Count == 0 || selectedTreeIndex < 0 || selectedTreeIndex >= visibleItems.Count)
                    return -1;
                return visibleItems[selectedTreeIndex].IndentLevel;
            }
        }

        public static void AnnounceCurrentTreeItem()
        {
            TolkHelper.Speak(BuildTreeItemAnnouncement());
        }

        private static string BuildTreeItemAnnouncement()
        {
            if (visibleItems.Count == 0 || selectedTreeIndex < 0 || selectedTreeIndex >= visibleItems.Count)
                return "";

            var item = visibleItems[selectedTreeIndex];
            var sb = new StringBuilder();

            sb.Append(item.Label);

            // Expand state for expandable items
            if (item.IsExpandable)
            {
                string state = item.IsExpanded ? "expanded" : "collapsed";
                int childCount = item.Children.Count;
                sb.Append($". {state}, {childCount} items");
            }

            // Position among siblings
            var (pos, total) = MenuHelper.GetSiblingPosition(visibleItems, selectedTreeIndex, i => i.IndentLevel);
            sb.Append($". {pos} of {total}");

            // Level suffix
            string levelSuffix = MenuHelper.GetLevelSuffix(TreeLevelKey, item.IndentLevel, skipLevelOne: false);
            if (!string.IsNullOrEmpty(levelSuffix))
                sb.Append(levelSuffix);

            return sb.ToString();
        }

        private static void AnnounceTreeWithSearch()
        {
            if (visibleItems.Count == 0 || selectedTreeIndex < 0 || selectedTreeIndex >= visibleItems.Count)
                return;

            string label = visibleItems[selectedTreeIndex].Label;
            string searchInfo = $", {presetsTypeahead.CurrentMatchPosition} of {presetsTypeahead.MatchCount} matches for '{presetsTypeahead.SearchBuffer}'";
            TolkHelper.Speak($"{label}{searchInfo}");
        }

        #endregion

        #region Selection Sync

        /// <summary>
        /// Gets the enum value for the currently selected option (Options tab).
        /// 0=Classic, 1=CustomFluid, 2=CustomFixed, 3=Load.
        /// </summary>
        public static int CurrentOptionEnumValue
        {
            get
            {
                if (options.Count == 0 || optionsIndex < 0 || optionsIndex >= options.Count)
                    return 0;
                return options[optionsIndex].EnumValue;
            }
        }

        /// <summary>
        /// Gets the IdeoPresetDef for the currently focused tree node.
        /// Returns the preset if on a preset node (level 1),
        /// the parent preset if on a meme node (level 2),
        /// or null if on a category node (level 0).
        /// </summary>
        public static IdeoPresetDef GetSelectedPresetDef()
        {
            if (visibleItems.Count == 0 || selectedTreeIndex < 0 || selectedTreeIndex >= visibleItems.Count)
                return null;

            var item = visibleItems[selectedTreeIndex];

            // Level 1: preset node
            if (item.IndentLevel == 1 && item.Data is IdeoPresetDef preset)
                return preset;

            // Level 2: meme node — return parent preset
            if (item.IndentLevel == 2 && item.Parent != null && item.Parent.Data is IdeoPresetDef parentPreset)
                return parentPreset;

            // Level 0: category node — no preset selected
            return null;
        }

        #endregion

        #region Structure Menu

        public static void OpenStructureMenu(Page_ChooseIdeoPreset page)
        {
            // Read current structure via reflection
            var selectedStructure = (MemeDef)HarmonyLib.AccessTools
                .Field(typeof(Page_ChooseIdeoPreset), "selectedStructure")
                .GetValue(page);

            // Announce current
            string currentName = selectedStructure != null
                ? selectedStructure.LabelCap.ToString()
                : "Random".Translate().ToString();
            string tooltip = selectedStructure != null
                ? IdeoUIUtility.StructureTooltip(selectedStructure, IdeoEditMode.None).ToString()
                : "RandomStructureTip".Translate().ToString();

            // Build menu options
            var menuOptions = new List<FloatMenuOption>();

            // Random option (only if current is not already random)
            if (selectedStructure != null)
            {
                menuOptions.Add(new FloatMenuOption("Random".Translate().ToString(), () =>
                {
                    HarmonyLib.AccessTools.Field(typeof(Page_ChooseIdeoPreset), "selectedStructure")
                        .SetValue(page, null);
                    TolkHelper.Speak("Structure".Translate() + ": " + "Random".Translate() + ". " + "RandomStructureTip".Translate());
                }));
            }

            // All allowed structure memes
            foreach (var meme in DefDatabase<MemeDef>.AllDefsListForReading)
            {
                if (meme == selectedStructure)
                    continue;
                if (meme.category != MemeCategory.Structure)
                    continue;
                if (!IdeoUtility.IsMemeAllowedFor(meme, Find.FactionManager.OfPlayer.def))
                    continue;

                var captured = meme;
                menuOptions.Add(new FloatMenuOption(meme.LabelCap.ToString(), () =>
                {
                    HarmonyLib.AccessTools.Field(typeof(Page_ChooseIdeoPreset), "selectedStructure")
                        .SetValue(page, captured);
                    TolkHelper.Speak("Structure".Translate() + ": " + captured.LabelCap);
                }));
            }

            TolkHelper.Speak("Structure".Translate() + ": " + currentName + ". " + tooltip);
            WindowlessFloatMenuState.Open(menuOptions, colonistOrders: false);
        }

        #endregion

        #region Style Menu

        public static void OpenStyleMenu(Page_ChooseIdeoPreset page)
        {
            var stylesField = HarmonyLib.AccessTools.Field(typeof(Page_ChooseIdeoPreset), "selectedStyles");
            var styles = (List<StyleCategoryDef>)stylesField.GetValue(page);

            // Announce current styles
            var styleNames = new List<string>();
            for (int i = 0; i < styles.Count; i++)
            {
                styleNames.Add(styles[i] != null ? styles[i].LabelCap.ToString() : "Random".Translate().ToString());
            }
            string currentStyles = string.Join(", ", styleNames);
            TolkHelper.Speak("Styles".Translate() + ": " + currentStyles + ". " + "StyleCategoryDescriptionAbstract".Translate());

            // Build slot menu
            var menuOptions = new List<FloatMenuOption>();

            for (int i = 0; i < styles.Count; i++)
            {
                int slotIndex = i;
                string slotName = styles[i] != null
                    ? styles[i].LabelCap.ToString()
                    : "Random".Translate().ToString();
                string label = "Styles".Translate() + " " + (i + 1) + ": " + slotName;

                menuOptions.Add(new FloatMenuOption(label, () =>
                {
                    OpenStyleSlotMenu(page, slotIndex);
                }));
            }

            // Add slot option if < 3
            if (styles.Count < 3)
            {
                menuOptions.Add(new FloatMenuOption("AddStyleCategory".Translate().ToString(), () =>
                {
                    OpenStyleSlotMenu(page, -1); // -1 = add new
                }));
            }

            WindowlessFloatMenuState.Open(menuOptions, colonistOrders: false);
        }

        private static void OpenStyleSlotMenu(Page_ChooseIdeoPreset page, int slotIndex)
        {
            var stylesField = HarmonyLib.AccessTools.Field(typeof(Page_ChooseIdeoPreset), "selectedStyles");
            var styles = (List<StyleCategoryDef>)stylesField.GetValue(page);

            var menuOptions = new List<FloatMenuOption>();

            // Random option
            if (slotIndex == -1 || styles[slotIndex] != null)
            {
                menuOptions.Add(new FloatMenuOption("Random".Translate().ToString(), () =>
                {
                    if (slotIndex == -1)
                        styles.Add(null);
                    else
                        styles[slotIndex] = null;
                    RecacheStyles(page);
                    TolkHelper.Speak(AnnounceStyleChange(page));
                }));
            }

            // Available style categories
            foreach (var style in DefDatabase<StyleCategoryDef>.AllDefs
                .Where(s => !s.fixedIdeoOnly && !styles.Contains(s)))
            {
                var captured = style;
                menuOptions.Add(new FloatMenuOption(style.LabelCap.ToString(), () =>
                {
                    if (slotIndex == -1)
                        styles.Add(captured);
                    else
                        styles[slotIndex] = captured;
                    RecacheStyles(page);
                    TolkHelper.Speak(AnnounceStyleChange(page));
                }));
            }

            // Remove option (only for existing slots, and only if more than 1 slot)
            if (slotIndex >= 0 && styles.Count > 1)
            {
                menuOptions.Add(new FloatMenuOption("Remove".Translate().ToString(), () =>
                {
                    styles.RemoveAt(slotIndex);
                    RecacheStyles(page);
                    TolkHelper.Speak(AnnounceStyleChange(page));
                }));
            }

            WindowlessFloatMenuState.Open(menuOptions, colonistOrders: false);
        }

        private static void RecacheStyles(Page_ChooseIdeoPreset page)
        {
            HarmonyLib.AccessTools.Method(typeof(Page_ChooseIdeoPreset), "RecacheStyleCategoriesWithPriority")
                .Invoke(page, null);
        }

        private static string AnnounceStyleChange(Page_ChooseIdeoPreset page)
        {
            var styles = (List<StyleCategoryDef>)HarmonyLib.AccessTools
                .Field(typeof(Page_ChooseIdeoPreset), "selectedStyles")
                .GetValue(page);

            var styleNames = new List<string>();
            for (int i = 0; i < styles.Count; i++)
            {
                styleNames.Add(styles[i] != null ? styles[i].LabelCap.ToString() : "Random".Translate().ToString());
            }
            return "Styles".Translate() + ": " + string.Join(", ", styleNames);
        }

        #endregion

        #region Opening Announcement

        public static string BuildOpeningAnnouncement()
        {
            var sb = new StringBuilder();
            sb.Append("ChooseYourIdeoligion".Translate());
            sb.Append(". ");
            sb.Append("ChooseYourIdeoligionDesc".Translate());
            sb.Append(". ");
            sb.Append(BuildOptionAnnouncement());
            return sb.ToString();
        }

        #endregion
    }
}
