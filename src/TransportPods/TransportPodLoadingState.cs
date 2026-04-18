using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for keyboard navigation in Dialog_LoadTransporters.
    /// Simplified version of CaravanFormationState with only two tabs (Pawns/Items).
    /// </summary>
    public static class TransportPodLoadingState
    {
        private enum Tab
        {
            Pawns,
            Items
        }

        private const int TabCount = 2;

        private static bool isActive = false;
        private static Dialog_LoadTransporters currentDialog = null;
        private static Tab currentTab = Tab.Pawns;
        private static int selectedIndex = 0;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // Position memory per tab - preserves selected index when switching tabs
        private static Dictionary<Tab, int> tabPositions = new Dictionary<Tab, int>();

        // Flag to track if accept was attempted (to avoid announcing "cancelled" on successful accept)
        private static bool acceptAttempted = false;

        // Flag to bypass OnAcceptKeyPressed patch when we're calling Accept() ourselves
        private static bool acceptingFromOurCode = false;

        // Summary toggle state (Tab key to quickly view stats)
        private static bool showingSummary = false;
        private static Tab savedTab = Tab.Pawns;
        private static int savedIndex = 0;

        // Summary navigation (up/down arrows to navigate through stats)
        private static List<string> summaryItems = new List<string>();
        private static int summaryIndex = 0;

        // Reflection fields for accessing private Dialog_LoadTransporters members
        private static readonly FieldInfo tabField = AccessTools.Field(typeof(Dialog_LoadTransporters), "tab");
        private static readonly FieldInfo transferablesField = AccessTools.Field(typeof(Dialog_LoadTransporters), "transferables");
        private static readonly FieldInfo transportersField = AccessTools.Field(typeof(Dialog_LoadTransporters), "transporters");

        // Flag to track if reflection fields initialized successfully
        private static readonly bool reflectionInitialized = tabField != null && transferablesField != null && transportersField != null;

        static TransportPodLoadingState()
        {
            if (!reflectionInitialized)
            {
                Log.Error("RimWorld Access: TransportPodLoadingState failed to initialize reflection fields. Transport pod loading accessibility may not work correctly.");
            }
        }

        /// <summary>
        /// Gets whether transport pod loading keyboard navigation is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets whether an accept was attempted (used by PostClose to decide announcement).
        /// </summary>
        public static bool AcceptAttempted => acceptAttempted;

        /// <summary>
        /// Gets whether typeahead search is currently active.
        /// Used by Window.OnCancelKeyPressed patch to block dialog close.
        /// </summary>
        public static bool HasActiveTypeahead => typeahead.HasActiveSearch;

        /// <summary>
        /// Gets whether we're in the middle of accepting from our code.
        /// Used by OnAcceptKeyPressed patch to allow our Accept() call through.
        /// </summary>
        public static bool AcceptingFromOurCode => acceptingFromOurCode;

        /// <summary>
        /// Opens keyboard navigation for the specified Dialog_LoadTransporters.
        /// </summary>
        public static void Open(Dialog_LoadTransporters dialog)
        {
            if (dialog == null)
            {
                TolkHelper.Speak("No loading dialog available", SpeechPriority.High);
                return;
            }

            if (!reflectionInitialized)
            {
                TolkHelper.Speak("Transport pod loading accessibility unavailable due to game update. Please check for mod updates.", SpeechPriority.High);
                return;
            }

            isActive = true;
            currentDialog = dialog;
            currentTab = Tab.Pawns;
            selectedIndex = 0;
            acceptAttempted = false;
            tabPositions.Clear();
            typeahead.ClearSearch();

            // Get pod count and capacity info
            var transporters = GetTransporters();
            int podCount = transporters?.Count ?? 0;
            float capacity = GetMassCapacity();

            string podType = podCount == 1 ? "pod" : "pods";
            TolkHelper.Speak($"Load transport {podType}. {podCount} {podType}, {capacity:F0} kg capacity. Left/Right for tabs, Enter to adjust.");

            AnnounceCurrentTab();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Closes keyboard navigation.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentDialog = null;
            currentTab = Tab.Pawns;
            selectedIndex = 0;
            acceptAttempted = false;
            tabPositions.Clear();
            typeahead.ClearSearch();
            showingSummary = false;
            summaryItems.Clear();
            summaryIndex = 0;
        }

        /// <summary>
        /// Handles keyboard input for the loading dialog.
        /// Returns true if the input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive || currentDialog == null)
                return false;

            // Left arrow - previous tab (not in summary view)
            if (key == KeyCode.LeftArrow && !shift && !ctrl && !alt && !showingSummary)
            {
                PreviousTab();
                return true;
            }

            // Right arrow - next tab (not in summary view)
            if (key == KeyCode.RightArrow && !shift && !ctrl && !alt && !showingSummary)
            {
                NextTab();
                return true;
            }

            // Up arrow - previous item or summary stat
            if (key == KeyCode.UpArrow && !shift && !ctrl && !alt)
            {
                if (showingSummary)
                    SelectPreviousSummaryItem();
                else
                    SelectPrevious();
                return true;
            }

            // Down arrow - next item or summary stat
            if (key == KeyCode.DownArrow && !shift && !ctrl && !alt)
            {
                if (showingSummary)
                    SelectNextSummaryItem();
                else
                    SelectNext();
                return true;
            }

            // Enter or Space - open quantity menu for items, toggle for pawns
            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space) && !shift && !ctrl && !alt)
            {
                ActivateSelected();
                return true;
            }

            // Tab or Shift+Tab - toggle summary view
            if (key == KeyCode.Tab && !ctrl && !alt)
            {
                ToggleSummaryView();
                return true;
            }

            // Shift+Enter - add maximum quantity
            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && shift && !ctrl && !alt)
            {
                AddMaximum();
                return true;
            }

            // Delete - remove all of selected item
            if (key == KeyCode.Delete && !shift && !ctrl && !alt)
            {
                RemoveSelected();
                return true;
            }

            // Alt+S - accept (start loading) - matches caravan formation/split dialogs
            if (key == KeyCode.S && alt && !shift && !ctrl)
            {
                Accept();
                return true;
            }

            // Alt+R - reset
            if (key == KeyCode.R && alt && !shift && !ctrl)
            {
                Reset();
                return true;
            }

            // Alt+I - inspect selected item or stat breakdown in summary mode
            if (key == KeyCode.I && alt && !shift && !ctrl)
            {
                if (showingSummary)
                {
                    // In summary mode: show stat breakdown
                    var statInfo = GetCurrentStatExplanation();
                    if (statInfo.HasValue)
                    {
                        StatBreakdownState.Open(statInfo.Value.name, statInfo.Value.explanation);
                    }
                    else
                    {
                        TolkHelper.Speak("No breakdown available for this item");
                    }
                }
                else
                {
                    // In tab mode: inspect current item
                    InspectSelected();
                }
                return true;
            }

            // Alt+H/M/N - pawn info shortcuts (health, mood, needs)
            if (CaravanInputHelper.HandlePawnInfoShortcuts(key, GetSelectedPawn(), alt, shift, ctrl))
            {
                return true;
            }

            // Home - jump to first item
            if (key == KeyCode.Home && !shift && !ctrl && !alt)
            {
                JumpToFirst();
                return true;
            }

            // End - jump to last item
            if (key == KeyCode.End && !shift && !ctrl && !alt)
            {
                JumpToLast();
                return true;
            }

            // Escape - clear search or let game close dialog
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearch();
                    TolkHelper.Speak("Search cleared");
                    AnnounceCurrentItem();
                    return true;
                }

                // For regular Escape, let the game handle it via Window.OnCancelKeyPressed
                // This ensures proper dialog lifecycle - PostClose will fire and call our Close()
                // Note: Event.current.Use() does NOT block RimWorld's KeyBindingDef.Cancel handling!
                return false;
            }

            // Backspace - remove last typeahead character
            if (key == KeyCode.Backspace && !shift && !ctrl && !alt)
            {
                if (typeahead.HasActiveSearch)
                {
                    List<TransferableOneWay> transferables = GetCurrentTabTransferables();
                    var labels = CaravanUIHelper.GetTransferableLabels(transferables);
                    if (typeahead.ProcessBackspace(labels, out int newIndex))
                    {
                        if (newIndex >= 0)
                            selectedIndex = newIndex;
                        AnnounceWithSearch();
                        return true;
                    }
                }
            }

            // Handle inline quantity adjustment (Items tab and grouped Pawns)
            if (!showingSummary)
            {
                // Check if quantity shortcuts should be enabled for this tab/item
                bool allowQuantityShortcuts = currentTab == Tab.Items;

                // For Pawns tab, allow quantity shortcuts only for grouped animals (MaxCount > 1)
                if (!allowQuantityShortcuts)
                {
                    var transferable = GetCurrentTransferableForQuantity();
                    allowQuantityShortcuts = transferable != null && transferable.MaxCount > 1;
                }

                if (allowQuantityShortcuts)
                {
                    if (TransferableQuantityHelper.HandleQuantityInput(key, shift, ctrl, alt,
                        GetCurrentTransferableForQuantity, NotifyTransferablesChanged))
                    {
                        return true;
                    }
                }
            }

            // Alphanumeric - typeahead search
            if (!shift && !ctrl && !alt && !showingSummary)
            {
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if (isLetter || isNumber)
                {
                    return true;
                }
            }

            // Block ALL unhandled keys to prevent game's native handlers from processing them
            // This makes the overlay screen modal - it captures all keyboard input while active
            return true;
        }

        #region Tab Navigation

        /// <summary>
        /// Switches to the next tab.
        /// </summary>
        public static void NextTab()
        {
            // Save current position before switching
            tabPositions[currentTab] = selectedIndex;

            currentTab = currentTab == Tab.Pawns ? Tab.Items : Tab.Pawns;

            // Restore saved position for new tab
            RestoreTabPosition();

            SyncGameTab();
            typeahead.ClearSearch();
            AnnounceCurrentTab();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Switches to the previous tab.
        /// </summary>
        public static void PreviousTab()
        {
            // Save current position before switching
            tabPositions[currentTab] = selectedIndex;

            currentTab = currentTab == Tab.Pawns ? Tab.Items : Tab.Pawns;

            // Restore saved position for new tab
            RestoreTabPosition();

            SyncGameTab();
            typeahead.ClearSearch();
            AnnounceCurrentTab();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Restores the selected index for the current tab from saved positions.
        /// </summary>
        private static void RestoreTabPosition()
        {
            if (tabPositions.TryGetValue(currentTab, out int savedPos))
            {
                List<TransferableOneWay> transferables = GetCurrentTabTransferables();
                selectedIndex = Math.Min(savedPos, Math.Max(0, transferables.Count - 1));
            }
            else
            {
                selectedIndex = 0;
            }
        }

        /// <summary>
        /// Syncs our tab state with the game's dialog tab.
        /// </summary>
        private static void SyncGameTab()
        {
            if (currentDialog == null || tabField == null)
                return;

            try
            {
                // Dialog_LoadTransporters.Tab enum matches our Tab enum (0=Pawns, 1=Items)
                tabField.SetValue(currentDialog, (int)currentTab);
            }
            catch (Exception ex)
            {
                Log.Error($"RimWorld Access: Failed to sync tab: {ex.Message}");
            }
        }

        #endregion

        #region Item Navigation

        /// <summary>
        /// Selects the next item in the current tab.
        /// </summary>
        public static void SelectNext()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0)
            {
                TolkHelper.Speak("No items in this tab");
                return;
            }

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int nextMatch = typeahead.GetNextMatch(selectedIndex);
                if (nextMatch >= 0)
                {
                    selectedIndex = nextMatch;
                    AnnounceWithSearch();
                }
                return;
            }

            selectedIndex = MenuHelper.SelectNext(selectedIndex, transferables.Count);
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Selects the previous item in the current tab.
        /// </summary>
        public static void SelectPrevious()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0)
            {
                TolkHelper.Speak("No items in this tab");
                return;
            }

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int prevMatch = typeahead.GetPreviousMatch(selectedIndex);
                if (prevMatch >= 0)
                {
                    selectedIndex = prevMatch;
                    AnnounceWithSearch();
                }
                return;
            }

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, transferables.Count);
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Jumps to the first item.
        /// </summary>
        public static void JumpToFirst()
        {
            selectedIndex = 0;
            typeahead.ClearSearch();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Jumps to the last item.
        /// </summary>
        public static void JumpToLast()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();
            selectedIndex = Math.Max(0, transferables.Count - 1);
            typeahead.ClearSearch();
            AnnounceCurrentItem();
        }

        #endregion

        #region Item Actions

        /// <summary>
        /// Activates the selected item (toggle for single pawns, quantity menu for grouped pawns and items).
        /// </summary>
        private static void ActivateSelected()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0 || selectedIndex < 0 || selectedIndex >= transferables.Count)
            {
                TolkHelper.Speak("No item selected");
                return;
            }

            TransferableOneWay transferable = transferables[selectedIndex];

            if (currentTab == Tab.Pawns)
            {
                // For grouped pawns (multiple animals), use quantity menu
                if (transferable.MaxCount > 1)
                {
                    OpenQuantityMenu(transferable);
                }
                else
                {
                    // Single pawn - toggle selection
                    TogglePawnSelection(transferable);
                }
            }
            else
            {
                // Open quantity menu for items
                OpenQuantityMenu(transferable);
            }
        }

        /// <summary>
        /// Toggles selection of a pawn.
        /// </summary>
        private static void TogglePawnSelection(TransferableOneWay transferable)
        {
            string label = GetTransferableLabel(transferable);
            if (transferable.CountToTransfer > 0)
            {
                transferable.AdjustTo(0);
                TolkHelper.Speak($"{label} unchecked");
            }
            else
            {
                transferable.AdjustTo(transferable.MaxCount);
                TolkHelper.Speak($"{label} checked");
            }

            NotifyTransferablesChanged();
        }

        /// <summary>
        /// Opens the quantity menu for an item.
        /// </summary>
        private static void OpenQuantityMenu(TransferableOneWay transferable)
        {
            QuantityMenuState.Open(transferable, (newQuantity) =>
            {
                transferable.AdjustTo(newQuantity);
                NotifyTransferablesChanged();
                AnnounceCurrentItem();
            });
        }

        /// <summary>
        /// Adds the maximum amount of the selected item that will fit.
        /// </summary>
        private static void AddMaximum()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0 || selectedIndex < 0 || selectedIndex >= transferables.Count)
            {
                TolkHelper.Speak("No item selected");
                return;
            }

            TransferableOneWay transferable = transferables[selectedIndex];

            // Calculate remaining capacity
            float remainingCapacity = GetMassCapacity() - GetMassUsage();

            var result = CaravanQuantityHelper.CalculateMaxToAdd(transferable, remainingCapacity);

            if (result.ToAdd > 0)
            {
                transferable.AdjustTo(result.NewCount);
                NotifyTransferablesChanged();
                TolkHelper.Speak(result.Announcement);
            }
            else
            {
                TolkHelper.Speak(result.Announcement);
            }
        }

        /// <summary>
        /// Removes all of the selected item.
        /// </summary>
        private static void RemoveSelected()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0 || selectedIndex < 0 || selectedIndex >= transferables.Count)
            {
                TolkHelper.Speak("No item selected");
                return;
            }

            TransferableOneWay transferable = transferables[selectedIndex];

            if (transferable.CountToTransfer == 0)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Zero);
                return;
            }

            transferable.AdjustTo(0);
            NotifyTransferablesChanged();

            string label = GetTransferableLabel(transferable);
            TolkHelper.Speak($"{label} removed");
        }

        /// <summary>
        /// Opens inspection for the selected item.
        /// </summary>
        private static void InspectSelected()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0 || selectedIndex < 0 || selectedIndex >= transferables.Count)
            {
                TolkHelper.Speak("No item to inspect");
                return;
            }

            TransferableOneWay transferable = transferables[selectedIndex];

            // Try to get a thing to inspect
            Thing thingToInspect = transferable.AnyThing;
            if (thingToInspect != null)
            {
                // Use Dialog_InfoCard instead of WindowlessInspectionState to avoid tab discovery
                // errors for world pawns (which don't have the same tabs as map pawns)
                Dialog_InfoCard infoCard = new Dialog_InfoCard(thingToInspect);
                Find.WindowStack.Add(infoCard);
            }
            else
            {
                TolkHelper.Speak("Cannot inspect this item");
            }
        }

        #endregion

        #region Dialog Actions

        /// <summary>
        /// Handles typeahead character input from the layout-aware dispatcher.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive) return;
            if (showingSummary) return;

            List<TransferableOneWay> transferables = GetCurrentTabTransferables();
            var labels = CaravanUIHelper.GetTransferableLabels(transferables);
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                typeahead.SpeakNoMatches();
            }
        }

        /// <summary>
        /// Accepts the current loading configuration and starts hauling.
        /// </summary>
        public static void Accept()
        {
            if (currentDialog == null)
                return;

            acceptAttempted = true;

            // Set flag to bypass our OnAcceptKeyPressed patch
            acceptingFromOurCode = true;
            try
            {
                // Trigger the game's accept logic
                currentDialog.OnAcceptKeyPressed();
            }
            finally
            {
                acceptingFromOurCode = false;
            }
            // Note: Don't reset acceptAttempted here even if dialog is still open.
            // The dialog may stay open because a confirmation dialog appeared (e.g., "caravan will be immobile").
            // acceptAttempted is reset in Close() which is called when the dialog actually closes.
        }

        /// <summary>
        /// Resets all selections to zero.
        /// </summary>
        public static void Reset()
        {
            List<TransferableOneWay> allTransferables = GetAllTransferables();

            foreach (var transferable in allTransferables)
            {
                transferable.AdjustTo(0);
            }

            NotifyTransferablesChanged();
            TolkHelper.Speak("All items reset to zero");
            AnnounceCurrentItem();
        }

        #endregion

        #region Announcements

        /// <summary>
        /// Announces the current tab.
        /// </summary>
        private static void AnnounceCurrentTab()
        {
            string tabName = currentTab == Tab.Pawns ? "Pawns" : "Items";
            List<TransferableOneWay> tabTransferables = GetCurrentTabTransferables();
            TolkHelper.Speak($"{tabName} tab, {tabTransferables.Count} items");
        }

        /// <summary>
        /// Announces the currently selected item.
        /// </summary>
        private static void AnnounceCurrentItem()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0)
            {
                TolkHelper.Speak("No items in this tab");
                return;
            }

            if (selectedIndex < 0 || selectedIndex >= transferables.Count)
            {
                selectedIndex = 0;
            }

            TransferableOneWay transferable = transferables[selectedIndex];
            string announcement = CaravanAnnouncementHelper.BuildItemAnnouncement(
                transferable, selectedIndex, transferables.Count);
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Announces the current item with typeahead search info.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0 || selectedIndex < 0 || selectedIndex >= transferables.Count)
            {
                CaravanAnnouncementHelper.AnnounceNoItems();
                return;
            }

            TransferableOneWay transferable = transferables[selectedIndex];
            string announcement = CaravanAnnouncementHelper.BuildSearchAnnouncement(
                transferable, typeahead.SearchBuffer, typeahead.CurrentMatchPosition, typeahead.MatchCount);
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Announces the mass summary.
        /// </summary>
        public static void AnnounceMassSummary()
        {
            float capacity = GetMassCapacity();
            float usage = GetMassUsage();
            float remaining = capacity - usage;

            string status;
            if (usage > capacity)
            {
                float over = usage - capacity;
                status = $"OVERLOADED by {over:F1} kg";
            }
            else
            {
                status = $"{remaining:F1} kg remaining";
            }

            TolkHelper.Speak($"Mass: {usage:F1} of {capacity:F1} kg. {status}");
        }

        #endregion

        #region Data Access

        /// <summary>
        /// Gets all transferables from the dialog.
        /// </summary>
        private static List<TransferableOneWay> GetAllTransferables()
        {
            if (currentDialog == null || transferablesField == null)
                return new List<TransferableOneWay>();

            try
            {
                var transferables = transferablesField.GetValue(currentDialog) as List<TransferableOneWay>;
                return transferables ?? new List<TransferableOneWay>();
            }
            catch (Exception ex)
            {
                Log.Error($"RimWorld Access: Failed to get transferables: {ex.Message}");
                return new List<TransferableOneWay>();
            }
        }

        /// <summary>
        /// Gets transferables for the current tab.
        /// NOTE: Transport pods have only 2 tabs (Pawns, Items) unlike caravans which have 3.
        /// The Items tab includes EVERYTHING that's not a pawn (including food/medicine).
        /// This matches Dialog_LoadTransporters.itemsTransfer which filters by ThingDef.category != Pawn.
        /// </summary>
        private static List<TransferableOneWay> GetCurrentTabTransferables()
        {
            List<TransferableOneWay> allTransferables = GetAllTransferables();

            if (currentTab == Tab.Pawns)
            {
                // Pawns tab - same as caravans
                return CaravanUIHelper.FilterByCategory(allTransferables, CaravanUIHelper.TransferableCategory.Pawns);
            }
            else
            {
                // Items tab - EVERYTHING that's not a pawn (includes food/medicine)
                // This differs from caravans which have a separate Travel Supplies tab
                return allTransferables
                    .Where(t => t.ThingDef.category != ThingCategory.Pawn)
                    .ToList();
            }
        }

        /// <summary>
        /// Gets the transporters from the dialog.
        /// </summary>
        private static List<CompTransporter> GetTransporters()
        {
            if (currentDialog == null || transportersField == null)
                return new List<CompTransporter>();

            try
            {
                var transporters = transportersField.GetValue(currentDialog) as List<CompTransporter>;
                return transporters ?? new List<CompTransporter>();
            }
            catch (Exception ex)
            {
                Log.Error($"RimWorld Access: Failed to get transporters: {ex.Message}");
                return new List<CompTransporter>();
            }
        }

        /// <summary>
        /// Gets the total mass capacity from transporters.
        /// </summary>
        private static float GetMassCapacity()
        {
            var transporters = GetTransporters();
            if (transporters == null || transporters.Count == 0)
                return 0f;

            float total = 0f;
            foreach (var transporter in transporters)
            {
                if (transporter?.Props != null)
                {
                    total += transporter.Props.massCapacity;
                }
            }
            return total;
        }

        /// <summary>
        /// Gets the current mass usage from transferables.
        /// </summary>
        private static float GetMassUsage()
        {
            var transferables = GetAllTransferables();
            if (transferables == null || transferables.Count == 0)
                return 0f;

            float total = 0f;
            foreach (var t in transferables)
            {
                if (t.CountToTransfer > 0 && t.AnyThing != null)
                {
                    float mass = t.AnyThing.GetStatValue(StatDefOf.Mass);
                    total += mass * t.CountToTransfer;
                }
            }
            return total;
        }

        /// <summary>
        /// Notifies the dialog that transferables have changed.
        /// </summary>
        private static void NotifyTransferablesChanged()
        {
            if (currentDialog == null)
                return;

            try
            {
                MethodInfo method = AccessTools.Method(typeof(Dialog_LoadTransporters), "CountToTransferChanged");
                if (method != null)
                {
                    method.Invoke(currentDialog, null);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to call CountToTransferChanged: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the currently selected transferable for inline quantity adjustment.
        /// </summary>
        private static TransferableOneWay GetCurrentTransferableForQuantity()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();
            if (transferables.Count == 0 || selectedIndex < 0 || selectedIndex >= transferables.Count)
                return null;
            return transferables[selectedIndex];
        }

        /// <summary>
        /// Gets the currently selected pawn, if any.
        /// Works on Pawns tab or when a pawn-type transferable is selected.
        /// </summary>
        private static Pawn GetSelectedPawn()
        {
            return CaravanUIHelper.GetSelectedPawn(GetCurrentTabTransferables(), selectedIndex);
        }

        /// <summary>
        /// Gets a label for a transferable item.
        /// For grouped animals (multiple pawns in one transferable), returns label with gender/life stage.
        /// </summary>
        private static string GetTransferableLabel(TransferableOneWay transferable)
        {
            if (transferable == null)
                return "";

            if (transferable.AnyThing is Pawn pawn)
            {
                // Check if multiple pawns are grouped together (animals with numerical names)
                if (transferable.MaxCount > 1)
                {
                    return PawnLabelHelper.BuildGroupedPawnLabel(pawn, transferable.MaxCount);
                }
                return pawn.LabelShortCap.StripTags();
            }
            return transferable.LabelCap.StripTags();
        }

        #endregion

        #region Summary View

        /// <summary>
        /// Toggles between the current tab and the Summary view.
        /// </summary>
        private static void ToggleSummaryView()
        {
            if (showingSummary)
            {
                showingSummary = false;
                currentTab = savedTab;
                selectedIndex = savedIndex;
                typeahead.ClearSearch();

                string tabName = currentTab == Tab.Pawns ? "Pawns" : "Items";
                TolkHelper.Speak($"Returned to {tabName} tab");
                AnnounceCurrentItem();
            }
            else
            {
                savedTab = currentTab;
                savedIndex = selectedIndex;
                showingSummary = true;
                typeahead.ClearSearch();

                TolkHelper.Speak("Summary view. Up/Down navigates stats. Tab to return.");
                BuildSummaryItems();
                AnnounceCurrentSummaryItem();
            }
        }

        /// <summary>
        /// Builds the summary items list.
        /// Shows only what the game displays via CaravanUIUtility.DrawCaravanInfo.
        /// For transport pods: Mass, Speed, Food, Foraging, Visibility
        /// For shuttles: Mass, Food only (Speed, Foraging, Visibility hidden)
        /// This matches exactly what a sighted player would see.
        /// </summary>
        private static void BuildSummaryItems()
        {
            summaryItems.Clear();
            // Don't reset summaryIndex - preserve position across summary views

            if (currentDialog == null)
            {
                summaryItems.Add("No data available");
                return;
            }

            try
            {
                var transporters = GetTransporters();
                if (transporters == null || transporters.Count == 0)
                {
                    summaryItems.Add("No transporters");
                    return;
                }

                // Check if this is a shuttle (Royalty DLC) - shuttles show fewer stats
                bool isShuttle = TransportPodHelper.IsShuttle(transporters[0]);

                float massUsage = GetMassUsage();
                float massCapacity = GetMassCapacity();
                bool isOverloaded = massUsage > massCapacity;

                // 1. Mass - always shown
                summaryItems.Add(CaravanStatFormatter.FormatMass(massUsage, massCapacity));

                // 2. Speed - only for non-shuttles
                if (!isShuttle)
                {
                    var tilesInfo = HarmonyLib.AccessTools.Property(typeof(Dialog_LoadTransporters), "TilesPerDay");
                    if (tilesInfo != null)
                    {
                        float tilesPerDay = (float)tilesInfo.GetValue(currentDialog);
                        summaryItems.Add(CaravanStatFormatter.FormatSpeed(tilesPerDay, isOverloaded));
                    }
                }

                // 3. Food - always shown
                var foodInfo = HarmonyLib.AccessTools.Property(typeof(Dialog_LoadTransporters), "DaysWorthOfFood");
                if (foodInfo != null)
                {
                    var foodObj = foodInfo.GetValue(currentDialog);
                    var food = (ValueTuple<float, float>)foodObj;
                    summaryItems.Add(CaravanStatFormatter.FormatFood(food.Item1, food.Item2));
                }

                // 4. Foraging - only for non-shuttles
                if (!isShuttle)
                {
                    var forageInfo = HarmonyLib.AccessTools.Property(typeof(Dialog_LoadTransporters), "ForagedFoodPerDay");
                    if (forageInfo != null)
                    {
                        var forageObj = forageInfo.GetValue(currentDialog);
                        var forage = (ValueTuple<ThingDef, float>)forageObj;
                        summaryItems.Add(CaravanStatFormatter.FormatForaging(forage.Item1, forage.Item2));
                    }
                }

                // 5. Visibility - only for non-shuttles
                if (!isShuttle)
                {
                    var visInfo = HarmonyLib.AccessTools.Property(typeof(Dialog_LoadTransporters), "Visibility");
                    if (visInfo != null)
                    {
                        float visibility = (float)visInfo.GetValue(currentDialog);
                        summaryItems.Add(CaravanStatFormatter.FormatVisibility(visibility));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to get pod stats: {ex.Message}");
                summaryItems.Add("Stats unavailable");
            }
        }

        /// <summary>
        /// Announces the currently selected summary item.
        /// </summary>
        private static void AnnounceCurrentSummaryItem()
        {
            if (summaryItems.Count == 0)
            {
                TolkHelper.Speak("No summary data");
                return;
            }

            if (summaryIndex < 0 || summaryIndex >= summaryItems.Count)
            {
                summaryIndex = 0;
            }

            string item = summaryItems[summaryIndex];
            string position = MenuHelper.FormatPosition(summaryIndex, summaryItems.Count);
            TolkHelper.Speak($"{item}. {position}");
        }

        /// <summary>
        /// Moves to the next summary item.
        /// </summary>
        private static void SelectNextSummaryItem()
        {
            if (summaryItems.Count == 0)
                return;

            summaryIndex = MenuHelper.SelectNext(summaryIndex, summaryItems.Count);
            AnnounceCurrentSummaryItem();
        }

        /// <summary>
        /// Moves to the previous summary item.
        /// </summary>
        private static void SelectPreviousSummaryItem()
        {
            if (summaryItems.Count == 0)
                return;

            summaryIndex = MenuHelper.SelectPrevious(summaryIndex, summaryItems.Count);
            AnnounceCurrentSummaryItem();
        }

        /// <summary>
        /// Gets the explanation text for the currently selected summary stat.
        /// Uses reflection to access the cached explanation fields from the dialog.
        /// Detects stat type from the summary item text since order varies (shuttles vs pods).
        /// </summary>
        /// <returns>A tuple of (stat name, explanation text) or null if no explanation available.</returns>
        private static (string name, string explanation)? GetCurrentStatExplanation()
        {
            if (currentDialog == null || summaryItems.Count == 0)
                return null;

            if (summaryIndex < 0 || summaryIndex >= summaryItems.Count)
                return null;

            string currentItem = summaryItems[summaryIndex];

            try
            {
                // Detect stat type from the summary item text prefix
                // IMPORTANT: We must access the property first to trigger recalculation of the cached explanation.
                string fieldName = null;
                string propertyName = null;
                string statName = null;

                if (currentItem.StartsWith("Mass:"))
                {
                    fieldName = "cachedCaravanMassCapacityExplanation";
                    propertyName = "CaravanMassCapacity";
                    statName = "Mass Capacity";
                }
                else if (currentItem.StartsWith("Speed:"))
                {
                    fieldName = "cachedTilesPerDayExplanation";
                    propertyName = "TilesPerDay";
                    statName = "Speed";
                }
                else if (currentItem.StartsWith("Food:"))
                {
                    // Food doesn't have a breakdown explanation in the game
                    // The tooltip is just "DaysWorthOfFoodTooltip" which we already include
                    return null;
                }
                else if (currentItem.StartsWith("Foraging:"))
                {
                    fieldName = "cachedForagedFoodPerDayExplanation";
                    propertyName = "ForagedFoodPerDay";
                    statName = "Foraging";
                }
                else if (currentItem.StartsWith("Visibility:"))
                {
                    fieldName = "cachedVisibilityExplanation";
                    propertyName = "Visibility";
                    statName = "Visibility";
                }
                else
                {
                    return null;
                }

                if (fieldName == null)
                    return null;

                // Access the property first to trigger recalculation of the cached explanation
                if (propertyName != null)
                {
                    var prop = HarmonyLib.AccessTools.Property(typeof(Dialog_LoadTransporters), propertyName);
                    if (prop != null)
                    {
                        // Just access the property getter - we don't need the value,
                        // this triggers the game to recalculate the cached explanation
                        prop.GetValue(currentDialog);
                    }
                }

                var field = HarmonyLib.AccessTools.Field(typeof(Dialog_LoadTransporters), fieldName);
                if (field == null)
                    return null;

                string explanation = field.GetValue(currentDialog) as string;
                if (string.IsNullOrEmpty(explanation))
                    return null;

                return (statName, explanation);
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to get stat explanation: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Typeahead Support
        #endregion
    }
}
