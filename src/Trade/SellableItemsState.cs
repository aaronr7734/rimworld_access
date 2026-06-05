using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation state for Dialog_SellableItems.
    /// This is a read-only informational dialog showing what items a settlement will buy.
    /// </summary>
    public static class SellableItemsState
    {
        private static bool isActive = false;
        private static Dialog_SellableItems currentDialog = null;
        private static int currentTabIndex = 0;
        private static int currentIndex = 0;

        // Tab position memory - remembers position when switching tabs
        private static Dictionary<int, int> tabPositions = new Dictionary<int, int>();

        // Typeahead search
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // Cached reflection accessors
        private static FieldInfo tabsField = null;
        private static FieldInfo currentCategoryField = null;
        private static FieldInfo pawnsTabOpenField = null;
        private static FieldInfo traderField = null;
        private static MethodInfo getSellableItemsMethod = null;

        /// <summary>
        /// Gets whether the sellable items menu is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets whether typeahead search is active.
        /// </summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Gets whether typeahead search has no matches.
        /// </summary>
        public static bool HasNoMatches => typeahead.HasNoMatches;

        /// <summary>
        /// Opens the sellable items navigation state.
        /// </summary>
        public static void Open(Dialog_SellableItems dialog)
        {
            if (dialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Sellable.Open.NoDialog".Loc());
                return;
            }

            currentDialog = dialog;
            isActive = true;
            currentTabIndex = 0;
            currentIndex = 0;
            tabPositions.Clear();
            typeahead.ClearSearch();

            // Initialize reflection accessors
            InitializeReflection();

            // Build opening announcement
            string announcement = BuildOpeningAnnouncement();
            TolkHelper.SpeakData(announcement);

            SoundDefOf.TabOpen.PlayOneShotOnCamera();

            // Announce first item
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Closes the sellable items navigation state.
        /// </summary>
        public static void Close()
        {
            var dialogToClose = currentDialog;

            isActive = false;
            currentDialog = null;
            currentTabIndex = 0;
            currentIndex = 0;
            tabPositions.Clear();
            typeahead.ClearSearch();

            // Close the dialog if it still exists
            if (dialogToClose != null && Find.WindowStack != null)
            {
                Find.WindowStack.TryRemove(dialogToClose, doCloseSound: false);
            }

            TolkHelper.Speak("RimWorldAccess.Sellable.Open.Closed".Loc());
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Called when the dialog is closing externally (not via our Close method).
        /// </summary>
        public static void OnDialogClosing()
        {
            if (!isActive)
                return;

            isActive = false;
            currentDialog = null;
            currentTabIndex = 0;
            currentIndex = 0;
            tabPositions.Clear();
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Switches to the next tab.
        /// </summary>
        public static void NextTab()
        {
            var tabs = GetTabs();
            if (tabs == null || tabs.Count == 0)
                return;

            // Save current position
            tabPositions[currentTabIndex] = currentIndex;

            // Switch tab
            currentTabIndex = (currentTabIndex + 1) % tabs.Count;

            // Activate the tab via its action
            ActivateCurrentTab(tabs);

            // Restore saved position or start at 0
            currentIndex = tabPositions.TryGetValue(currentTabIndex, out int savedPos) ? savedPos : 0;
            ClampCurrentIndex();
            typeahead.ClearSearch();

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            AnnounceTabSwitch();
        }

        /// <summary>
        /// Switches to the previous tab.
        /// </summary>
        public static void PreviousTab()
        {
            var tabs = GetTabs();
            if (tabs == null || tabs.Count == 0)
                return;

            // Save current position
            tabPositions[currentTabIndex] = currentIndex;

            // Switch tab
            currentTabIndex = (currentTabIndex + tabs.Count - 1) % tabs.Count;

            // Activate the tab via its action
            ActivateCurrentTab(tabs);

            // Restore saved position or start at 0
            currentIndex = tabPositions.TryGetValue(currentTabIndex, out int savedPos) ? savedPos : 0;
            ClampCurrentIndex();
            typeahead.ClearSearch();

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            AnnounceTabSwitch();
        }

        /// <summary>
        /// Selects the next item in the current tab.
        /// </summary>
        public static void SelectNext()
        {
            var items = GetCurrentItems();
            if (items == null || items.Count == 0)
                return;

            int newIndex = MenuHelper.SelectNext(currentIndex, items.Count);
            if (newIndex != currentIndex)
            {
                currentIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Selects the previous item in the current tab.
        /// </summary>
        public static void SelectPrevious()
        {
            var items = GetCurrentItems();
            if (items == null || items.Count == 0)
                return;

            int newIndex = MenuHelper.SelectPrevious(currentIndex, items.Count);
            if (newIndex != currentIndex)
            {
                currentIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the first item in the current tab.
        /// </summary>
        public static void JumpToFirst()
        {
            var items = GetCurrentItems();
            if (items == null || items.Count == 0)
                return;

            currentIndex = 0;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the last item in the current tab.
        /// </summary>
        public static void JumpToLast()
        {
            var items = GetCurrentItems();
            if (items == null || items.Count == 0)
                return;

            currentIndex = items.Count - 1;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Selects the next typeahead match.
        /// </summary>
        public static void SelectNextMatch()
        {
            var items = GetCurrentItems();
            if (items == null || items.Count == 0)
                return;

            int newIndex = typeahead.GetNextMatch(currentIndex);
            if (newIndex >= 0 && newIndex != currentIndex)
            {
                currentIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Selects the previous typeahead match.
        /// </summary>
        public static void SelectPreviousMatch()
        {
            var items = GetCurrentItems();
            if (items == null || items.Count == 0)
                return;

            int newIndex = typeahead.GetPreviousMatch(currentIndex);
            if (newIndex >= 0 && newIndex != currentIndex)
            {
                currentIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Processes a typeahead character input.
        /// </summary>
        public static void ProcessTypeaheadCharacter(char c)
        {
            var items = GetCurrentItems();
            if (items == null || items.Count == 0)
                return;

            // Build label list for typeahead
            var labels = new List<string>();
            foreach (var item in items)
            {
                labels.Add(item.label ?? item.defName);
            }

            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0 && newIndex != currentIndex)
                {
                    currentIndex = newIndex;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }
            }
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Processes backspace for typeahead search.
        /// </summary>
        public static bool ProcessBackspace()
        {
            if (!typeahead.HasActiveSearch)
                return false;

            var items = GetCurrentItems();
            if (items == null || items.Count == 0)
            {
                typeahead.ClearSearch();
                TolkHelper.Speak("RimWorldAccess.Search.Cleared".Loc());
                return true;
            }

            // Build label list for typeahead
            var labels = new List<string>();
            foreach (var item in items)
            {
                labels.Add(item.label ?? item.defName);
            }

            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (typeahead.HasActiveSearch && newIndex >= 0 && newIndex != currentIndex)
                {
                    currentIndex = newIndex;
                }

                if (typeahead.HasActiveSearch)
                {
                    AnnounceCurrentSelection();
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Search.Cleared".Loc());
                }
            }
            return true;
        }

        /// <summary>
        /// Clears the typeahead search.
        /// </summary>
        public static void ClearTypeaheadSearch()
        {
            typeahead.ClearSearch();
            TolkHelper.Speak("RimWorldAccess.Search.Cleared".Loc());
        }

        // ===== Private Methods =====

        private static void InitializeReflection()
        {
            if (tabsField == null)
            {
                var dialogType = typeof(Dialog_SellableItems);
                tabsField = AccessTools.Field(dialogType, "tabs");
                currentCategoryField = AccessTools.Field(dialogType, "currentCategory");
                pawnsTabOpenField = AccessTools.Field(dialogType, "pawnsTabOpen");
                traderField = AccessTools.Field(dialogType, "trader");
                getSellableItemsMethod = AccessTools.Method(dialogType, "GetSellableItemsInCategory");
            }
        }

        private static List<TabRecord> GetTabs()
        {
            if (currentDialog == null || tabsField == null)
                return null;

            return tabsField.GetValue(currentDialog) as List<TabRecord>;
        }

        private static void ActivateCurrentTab(List<TabRecord> tabs)
        {
            if (tabs == null || currentTabIndex < 0 || currentTabIndex >= tabs.Count)
                return;

            // TabRecord has a clickedAction that switches the tab
            var tab = tabs[currentTabIndex];
            tab.clickedAction?.Invoke();
        }

        private static List<ThingDef> GetCurrentItems()
        {
            if (currentDialog == null || getSellableItemsMethod == null)
                return null;

            try
            {
                // Get current category and pawnsTabOpen state
                var currentCategory = currentCategoryField?.GetValue(currentDialog) as ThingCategoryDef;
                bool pawnsTabOpen = false;
                if (pawnsTabOpenField != null)
                {
                    object value = pawnsTabOpenField.GetValue(currentDialog);
                    if (value is bool boolValue)
                        pawnsTabOpen = boolValue;
                }

                // Call GetSellableItemsInCategory(currentCategory, pawnsTabOpen)
                return getSellableItemsMethod.Invoke(currentDialog, new object[] { currentCategory, pawnsTabOpen }) as List<ThingDef>;
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Error getting sellable items: {ex.Message}");
                return null;
            }
        }

        private static ITrader GetTrader()
        {
            if (currentDialog == null || traderField == null)
                return null;

            return traderField.GetValue(currentDialog) as ITrader;
        }

        private static string GetCurrentTabName()
        {
            var tabs = GetTabs();
            if (tabs == null || currentTabIndex < 0 || currentTabIndex >= tabs.Count)
                return "RimWorldAccess.Sellable.Tab.UnknownTab".Translate();

            return tabs[currentTabIndex].label;
        }

        private static void ClampCurrentIndex()
        {
            var items = GetCurrentItems();
            if (items == null || items.Count == 0)
            {
                currentIndex = 0;
                return;
            }

            if (currentIndex >= items.Count)
                currentIndex = items.Count - 1;
            if (currentIndex < 0)
                currentIndex = 0;
        }

        private static string BuildOpeningAnnouncement()
        {
            var trader = GetTrader();
            var tabs = GetTabs();

            string traderName = trader?.TraderName ?? "RimWorldAccess.Sellable.Open.TraderFallback".Translate().ToString();
            string tabCount = "";
            if (tabs != null)
            {
                tabCount = tabs.Count == 1
                    ? "RimWorldAccess.Sellable.Open.CategoriesOne".Translate().ToString()
                    : "RimWorldAccess.Sellable.Open.CategoriesMany".Translate(tabs.Count).ToString();
            }

            // Build restock info
            string restockInfo = "";
            if (trader is ITraderRestockingInfoProvider restockProvider)
            {
                int nextRestockTick = restockProvider.NextRestockTick;
                if (nextRestockTick != -1)
                {
                    float daysUntilRestock = (nextRestockTick - Find.TickManager.TicksGame).TicksToDays();
                    restockInfo = "RimWorldAccess.Sellable.Restock.NextRestock".Translate(daysUntilRestock.ToString("0.0"));
                }
                else if (!restockProvider.EverVisited)
                {
                    restockInfo = "RimWorldAccess.Sellable.Restock.NotVisited".Translate();
                }
                else if (restockProvider.RestockedSinceLastVisit)
                {
                    restockInfo = "RimWorldAccess.Sellable.Restock.Restocked".Translate();
                }
            }

            string controls = "RimWorldAccess.Sellable.Open.Controls".Translate();

            return "RimWorldAccess.Sellable.Open.Heading".Translate(traderName, restockInfo, tabCount, controls);
        }

        private static void AnnounceTabSwitch()
        {
            string tabName = GetCurrentTabName();
            var items = GetCurrentItems();
            int count = items?.Count ?? 0;

            string sentence = count == 1
                ? "RimWorldAccess.Sellable.Tab.SwitchOne".Translate(tabName).ToString()
                : "RimWorldAccess.Sellable.Tab.SwitchMany".Translate(tabName, count).ToString();
            TolkHelper.SpeakData(sentence);

            if (count > 0)
            {
                AnnounceCurrentSelection();
            }
        }

        private static void AnnounceCurrentSelection()
        {
            var items = GetCurrentItems();
            if (items == null || items.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Sellable.Tab.NoItems".Loc());
                return;
            }

            if (currentIndex < 0 || currentIndex >= items.Count)
            {
                ClampCurrentIndex();
            }

            ThingDef item = items[currentIndex];
            string announcement = BuildItemAnnouncement(item, items.Count);

            // Add search info if active
            if (typeahead.HasActiveSearch)
            {
                string key = typeahead.HasNoMatches
                    ? "RimWorldAccess.Sellable.Search.NoMatchesPrefix"
                    : "RimWorldAccess.Sellable.Search.Prefix";
                announcement = key.Translate(typeahead.SearchBuffer, announcement);
            }

            TolkHelper.SpeakData(announcement);
        }

        private static string BuildItemAnnouncement(ThingDef item, int totalCount)
        {
            if (item == null)
                return "RimWorldAccess.Sellable.Item.Unknown".Translate();

            // Item name
            string name = item.LabelCap;
            string description = item.description;
            string position = MenuHelper.FormatPosition(currentIndex, totalCount);

            bool hasDesc = !string.IsNullOrEmpty(description);
            bool hasPos = !string.IsNullOrEmpty(position);

            if (hasDesc && hasPos)
                return "RimWorldAccess.Sellable.Item.WithDescriptionPosition".Translate(name, description, position);
            if (hasDesc)
                return "RimWorldAccess.Sellable.Item.WithDescription".Translate(name, description);
            if (hasPos)
                return "RimWorldAccess.Sellable.Item.WithPosition".Translate(name, position);
            return "RimWorldAccess.Sellable.Item.Bare".Translate(name);
        }
    }
}
