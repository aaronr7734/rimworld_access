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
        // TreeLevelKey "IdeoPresets" is now managed internally by presetsTreeNav

        private static bool initialized;
        private static int currentTab;
        private static bool hasShownPresetsHint;

        public static bool IsActive { get; private set; }

        // Options tab
        private static List<OptionEntry> options = new List<OptionEntry>();
        private static int optionsIndex;
        private static TypeaheadSearchHelper optionsTypeahead = new TypeaheadSearchHelper();

        // Presets tab (tree) — uses TreeNavigationHelper for navigation
        private static TreeNavigationHelper presetsTreeNav = new TreeNavigationHelper("IdeoPresets");
        private static bool presetsTreeNavConfigured = false;

        public static int CurrentTab => currentTab;

        private class OptionEntry
        {
            public string Label;
            public string Description;
            public int EnumValue; // 0=Classic, 1=CustomFluid, 2=CustomFixed, 3=Load
        }

        private static void EnsurePresetsTreeNavConfigured()
        {
            if (presetsTreeNavConfigured) return;
            presetsTreeNavConfigured = true;

            presetsTreeNav.FormatItemAnnouncement = FormatPresetsItemAnnouncement;
            presetsTreeNav.FormatSearchAnnouncement = FormatPresetsSearchAnnouncement;
        }

        private static string FormatPresetsItemAnnouncement(InspectionTreeItem item)
        {
            var sb = new StringBuilder();
            sb.Append(item.Label);

            sb.Append(TreeNavigationHelper.FormatExpansionSuffix(item, includeChildCount: true));

            var (pos, total) = presetsTreeNav.GetSiblingPosition(item);
            sb.Append("RimWorldAccess.Ideology.PresetSiblingPosition".Translate(pos, total));

            string levelSuffix = MenuHelper.GetLevelSuffix("IdeoPresets", item.IndentLevel, skipLevelOne: false);
            if (!string.IsNullOrEmpty(levelSuffix))
                sb.Append(levelSuffix);

            return sb.ToString();
        }

        private static string FormatPresetsSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead)
        {
            string label = item.Label;
            return typeahead.BuildItemAnnouncement(label);
        }

        private static string FormatPresetsStateChangeAnnouncement(InspectionTreeItem item)
        {
            string state = item.IsExpanded
                ? (string)"RimWorldAccess.Tree.StateExpanded".Translate()
                : (string)"RimWorldAccess.Tree.StateCollapsed".Translate();
            return "RimWorldAccess.Ideology.StateChangeAnnouncement".Translate(state.CapitalizeFirst(), FormatPresetsItemAnnouncement(item));
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

            EnsurePresetsTreeNavConfigured();
            currentTab = TabOptions;
            optionsIndex = 0;
            optionsTypeahead.ClearSearch();
            IsActive = true;
            hasShownPresetsHint = false;
            initialized = true;
        }

        public static void Reset()
        {
            initialized = false;
            IsActive = false;
            currentTab = TabOptions;
            optionsIndex = 0;
            options.Clear();
            presetsTreeNav.Reset();
            optionsTypeahead.ClearSearch();
        }

        private static void BuildPresetTree()
        {
            var rootItem = new InspectionTreeItem
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
                    Label = "RimWorldAccess.Ideology.CategoryWithDescription".Translate(category.LabelCap, category.description),
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

            presetsTreeNav.Initialize(rootItem);
        }

        #endregion

        #region Tab Switching

        public static void SwitchTab()
        {
            optionsTypeahead.ClearSearch();

            if (currentTab == TabOptions)
            {
                currentTab = TabPresets;
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
                string tabName = "RimWorldAccess.Ideology.OptionsTab".Translate();
                TolkHelper.Speak("RimWorldAccess.Ideology.TabPrefix".Loc(tabName, BuildOptionAnnouncement()));
            }
            else
            {
                string tabName = "RimWorldAccess.Ideology.PresetsTab".Translate();
                if (presetsTreeNav.Count > 0)
                {
                    string hint = "";
                    if (!hasShownPresetsHint)
                    {
                        hasShownPresetsHint = true;
                        hint = "RimWorldAccess.Ideology.PresetsHint".Translate();
                    }
                    TolkHelper.Speak("RimWorldAccess.Ideology.TabPrefix".Loc(tabName, BuildTreeItemAnnouncement() + hint));
                }
                else
                    TolkHelper.Speak("RimWorldAccess.Ideology.TabNoneSuffix".Loc(tabName, "NoneLower".Translate()));
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
            TolkHelper.SpeakData(BuildOptionAnnouncement());
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
            TolkHelper.SpeakData(BuildOptionAnnouncement());
        }

        public static void NavigateOptionHome()
        {
            if (options.Count == 0) return;
            optionsTypeahead.ClearSearch();
            optionsIndex = 0;
            TolkHelper.SpeakData(BuildOptionAnnouncement());
        }

        public static void NavigateOptionEnd()
        {
            if (options.Count == 0) return;
            optionsTypeahead.ClearSearch();
            optionsIndex = options.Count - 1;
            TolkHelper.SpeakData(BuildOptionAnnouncement());
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
                optionsTypeahead.SpeakNoMatches();
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
            TolkHelper.SpeakData(BuildOptionAnnouncement());
        }

        private static string BuildOptionAnnouncement()
        {
            if (options.Count == 0 || optionsIndex < 0 || optionsIndex >= options.Count)
                return "";

            var opt = options[optionsIndex];
            string text = opt.Label;
            if (!string.IsNullOrEmpty(opt.Description))
                text = "RimWorldAccess.Ideology.OptionWithDescription".Translate(text, opt.Description);

            string position = MenuHelper.FormatPosition(optionsIndex, options.Count);
            if (!string.IsNullOrEmpty(position))
                text = "RimWorldAccess.Ideology.OptionDotPosition".Translate(text, position);

            return text;
        }

        private static void AnnounceOptionWithSearch()
        {
            if (options.Count == 0 || optionsIndex < 0 || optionsIndex >= options.Count)
                return;

            string name = options[optionsIndex].Label;
            TolkHelper.SpeakData(optionsTypeahead.BuildItemAnnouncement(name));
        }

        public static void AnnounceCurrentOption()
        {
            TolkHelper.SpeakData(BuildOptionAnnouncement());
        }

        #endregion

        #region Presets Tab Tree Navigation

        /// <summary>
        /// Handles keyboard input for the presets tree tab.
        /// Returns true if input was consumed; false if caller should handle (e.g., Escape, Enter pass-through).
        /// </summary>
        public static bool HandlePresetsInput(Event ev)
        {
            if (ev.type != EventType.KeyDown)
                return false;

            KeyCode key = ev.keyCode;

            // Enter — behavior depends on node type (handled before treeNav)
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                int level = CurrentTreeNodeLevel;
                if (level == 0)
                {
                    // Category node — toggle expand/collapse
                    var item = presetsTreeNav.SelectedItem;
                    if (item != null && item.IsExpandable)
                    {
                        item.IsExpanded = !item.IsExpanded;
                        presetsTreeNav.RebuildVisibleList();
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        TolkHelper.SpeakData(FormatPresetsStateChangeAnnouncement(item));
                    }
                    return true;
                }
                else if (level == 1)
                {
                    // Preset node — let Enter pass through to game DoNext
                    return false;
                }
                else
                {
                    // Meme node — re-announce
                    presetsTreeNav.ReannounceCurrentItem();
                    return true;
                }
            }

            // Delegate standard tree navigation to TreeNavigationHelper
            if (presetsTreeNav.HandleInput(ev))
                return true;

            // HandleInput returned false = Escape with no active search
            // Let caller handle (go back to options tab or close)
            return false;
        }

        public static bool HasPresetsSearch => presetsTreeNav.HasActiveSearch;

        public static void ClearPresetsSearch()
        {
            presetsTreeNav.Typeahead.ClearSearchAndAnnounce();
            presetsTreeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Returns the indent level of the currently focused tree item.
        /// 0 = category, 1 = preset, 2 = meme.
        /// </summary>
        public static int CurrentTreeNodeLevel
        {
            get
            {
                var item = presetsTreeNav.SelectedItem;
                return item?.IndentLevel ?? -1;
            }
        }

        public static void AnnounceCurrentTreeItem()
        {
            presetsTreeNav.ReannounceCurrentItem();
        }

        private static string BuildTreeItemAnnouncement()
        {
            var item = presetsTreeNav.SelectedItem;
            if (item == null) return "";
            return FormatPresetsItemAnnouncement(item);
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
            var item = presetsTreeNav.SelectedItem;
            if (item == null)
                return null;

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

            // Build menu options
            var menuOptions = new List<FloatMenuOption>();

            // Random option (only if current is not already random)
            if (selectedStructure != null)
            {
                menuOptions.Add(new FloatMenuOption("Random".Translate().ToString(), () =>
                {
                    HarmonyLib.AccessTools.Field(typeof(Page_ChooseIdeoPreset), "selectedStructure")
                        .SetValue(page, null);
                    TolkHelper.Speak("RimWorldAccess.Ideology.StructureRandomTip".Loc("Structure".Translate(), "Random".Translate(), "RandomStructureTip".Translate()));
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
                menuOptions.Add(new FloatMenuOption("RimWorldAccess.Ideology.StructureMemeOption".Translate(captured.LabelCap, captured.description), () =>
                {
                    HarmonyLib.AccessTools.Field(typeof(Page_ChooseIdeoPreset), "selectedStructure")
                        .SetValue(page, captured);
                    TolkHelper.Speak("RimWorldAccess.Ideology.StructureColon".Loc("Structure".Translate(), captured.LabelCap));
                }));
            }

            WindowlessFloatMenuState.Open(menuOptions, colonistOrders: false);
        }

        #endregion

        #region Style Menu

        public static void OpenStyleMenu(Page_ChooseIdeoPreset page)
        {
            var stylesField = HarmonyLib.AccessTools.Field(typeof(Page_ChooseIdeoPreset), "selectedStyles");
            var styles = (List<StyleCategoryDef>)stylesField.GetValue(page);

            // Build slot menu
            var menuOptions = new List<FloatMenuOption>();

            for (int i = 0; i < styles.Count; i++)
            {
                int slotIndex = i;
                string slotName = styles[i] != null
                    ? styles[i].LabelCap.ToString()
                    : "Random".Translate().ToString();
                string label = "RimWorldAccess.Ideology.StyleSlot".Translate("Styles".Translate(), i + 1, slotName);

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
                    TolkHelper.SpeakData(AnnounceStyleChange(page));
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
                    TolkHelper.SpeakData(AnnounceStyleChange(page));
                }));
            }

            // Remove option (only for existing slots, and only if more than 1 slot)
            if (slotIndex >= 0 && styles.Count > 1)
            {
                menuOptions.Add(new FloatMenuOption("Remove".Translate().ToString(), () =>
                {
                    styles.RemoveAt(slotIndex);
                    RecacheStyles(page);
                    TolkHelper.SpeakData(AnnounceStyleChange(page));
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
            return "RimWorldAccess.Ideology.StylesAnnouncement".Translate("Styles".Translate(), string.Join(", ", styleNames));
        }

        #endregion

        #region Opening Announcement

        public static string BuildOpeningAnnouncement()
        {
            return "RimWorldAccess.Ideology.OpeningAnnouncement".Translate(
                "ChooseYourIdeoligion".Translate(),
                "ChooseYourIdeoligionDesc".Translate(),
                BuildOptionAnnouncement());
        }

        #endregion
    }
}
