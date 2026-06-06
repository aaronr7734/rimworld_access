using System;
using System.Collections.Generic;
using System.Linq;
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
        private static ITransferLoadDialog adapter = null;
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
        // Language-independent kind tag for each summaryItems entry, kept in
        // lockstep with summaryItems. Used to detect the stat type for Alt+I
        // breakdowns without matching the localized display string.
        private static List<string> summaryKinds = new List<string>();
        private static int summaryIndex = 0;

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
        /// Gets the announcement to speak when the current dialog closes without accepting.
        /// Captured by the PostClose patches before <see cref="Close"/> clears the adapter.
        /// </summary>
        public static string CancelAnnouncement => adapter?.CancelAnnouncement ?? "Loading cancelled";

        /// <summary>
        /// Opens keyboard navigation for a transport pod / shuttle loading dialog.
        /// </summary>
        public static void Open(Dialog_LoadTransporters dialog)
        {
            if (dialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.NoDialog".Loc(), SpeechPriority.High);
                return;
            }

            if (!LoadTransportersAdapter.ReflectionReady)
            {
                TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.ReflectionFailed".Loc(), SpeechPriority.High);
                return;
            }

            Open(new LoadTransportersAdapter(dialog));
        }

        /// <summary>
        /// Opens keyboard navigation for a map portal loading dialog (ancient complexes,
        /// pit gates, insect lairs, pocket-map exits).
        /// </summary>
        public static void Open(Dialog_EnterPortal dialog)
        {
            if (dialog == null)
            {
                TolkHelper.Speak("No portal dialog available", SpeechPriority.High);
                return;
            }

            if (!EnterPortalAdapter.ReflectionReady)
            {
                TolkHelper.Speak("Portal loading accessibility unavailable due to game update. Please check for mod updates.", SpeechPriority.High);
                return;
            }

            Open(new EnterPortalAdapter(dialog));
        }

        /// <summary>
        /// Shared open logic for either dialog type.
        /// </summary>
        private static void Open(ITransferLoadDialog newAdapter)
        {
            isActive = true;
            adapter = newAdapter;
            currentTab = Tab.Pawns;
            selectedIndex = 0;
            acceptAttempted = false;
            tabPositions.Clear();
            typeahead.ClearSearch();

            // Speak the open line, tab summary, and current item as one utterance so
            // SpeechSanitizer cleans the seams between them (separate Speak() calls are each
            // sanitized in isolation, leaving stray periods at the joins).
            TolkHelper.Speak($"{adapter.OpenAnnouncement}. {BuildCurrentTabText()}. {BuildCurrentItemText()}");
        }

        /// <summary>
        /// Closes keyboard navigation.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            adapter = null;
            currentTab = Tab.Pawns;
            selectedIndex = 0;
            acceptAttempted = false;
            tabPositions.Clear();
            typeahead.ClearSearch();
            showingSummary = false;
            summaryItems.Clear();
            summaryKinds.Clear();
            summaryIndex = 0;
        }

        /// <summary>
        /// Handles keyboard input for the loading dialog.
        /// Returns true if the input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive || adapter == null)
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
                        TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.NoBreakdown".Loc());
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
                    TolkHelper.Speak("RimWorldAccess.Search.Cleared".Loc());
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
            AnnounceTabAndItem();
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
            AnnounceTabAndItem();
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
            if (adapter == null)
                return;

            // Both dialogs use the same Tab enum layout (0=Pawns, 1=Items).
            adapter.GameTab = (int)currentTab;
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
                TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.NoItemsInTab".Loc());
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
                TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.NoItemsInTab".Loc());
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
                TolkHelper.Speak("RimWorldAccess.Guard.NoItemSelected".Loc());
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
                TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.ItemUnchecked".Loc(label));
            }
            else
            {
                transferable.AdjustTo(transferable.MaxCount);
                TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.ItemChecked".Loc(label));
            }

            NotifyTransferablesChanged();

            // Clear any active typeahead search so the next keystrokes start a fresh search.
            // This lets the user type one pawn's name, press Enter/Space to select them, then
            // immediately begin typing the next pawn's name.
            typeahead.ClearSearch();
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
                TolkHelper.Speak("RimWorldAccess.Guard.NoItemSelected".Loc());
                return;
            }

            TransferableOneWay transferable = transferables[selectedIndex];

            // Calculate remaining capacity (unlimited for map portals)
            float remainingCapacity = adapter.MassCapacity - GetMassUsage();

            var result = CaravanQuantityHelper.CalculateMaxToAdd(transferable, remainingCapacity);

            if (result.ToAdd > 0)
            {
                transferable.AdjustTo(result.NewCount);
                NotifyTransferablesChanged();
                TolkHelper.SpeakData(result.Announcement);
            }
            else
            {
                TolkHelper.SpeakData(result.Announcement);
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
                TolkHelper.Speak("RimWorldAccess.Guard.NoItemSelected".Loc());
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
            TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.ItemRemoved".Loc(label));
        }

        /// <summary>
        /// Opens inspection for the selected item.
        /// </summary>
        private static void InspectSelected()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0 || selectedIndex < 0 || selectedIndex >= transferables.Count)
            {
                TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.NoItemToInspect".Loc());
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
                TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.CannotInspectItem".Loc());
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
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        /// <summary>
        /// Accepts the current loading configuration and starts hauling.
        /// </summary>
        public static void Accept()
        {
            if (adapter == null)
                return;

            acceptAttempted = true;

            // Set flag to bypass our OnAcceptKeyPressed patch
            acceptingFromOurCode = true;
            try
            {
                // Trigger the game's accept logic
                adapter.TriggerAccept();
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
            TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.ResetAll".Loc());
            AnnounceCurrentItem();
        }

        #endregion

        #region Announcements

        /// <summary>
        /// Builds the current tab description, e.g. "Pawns tab, 83 items".
        /// </summary>
        private static string BuildCurrentTabText()
        {
            string tabName = currentTab == Tab.Pawns ? "Pawns" : "Items";
            return $"{tabName} tab, {GetCurrentTabTransferables().Count} items";
        }

        /// <summary>
        /// Builds the description of the currently selected item.
        /// </summary>
        private static string BuildCurrentItemText()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0)
            {
                return "No items in this tab";
            }

            if (selectedIndex < 0 || selectedIndex >= transferables.Count)
            {
                selectedIndex = 0;
            }

            TransferableOneWay transferable = transferables[selectedIndex];
            return CaravanAnnouncementHelper.BuildItemAnnouncement(
                transferable, selectedIndex, transferables.Count);
        }

        /// <summary>
        /// Announces the currently selected item.
        /// </summary>
        private static void AnnounceCurrentItem()
        {
            TolkHelper.Speak(BuildCurrentItemText());
        }

        /// <summary>
        /// Announces the current tab and selected item as a single utterance. Combining them into
        /// one Speak() call lets SpeechSanitizer clean the seam between the two parts; separate
        /// calls would each be sanitized in isolation, leaving a stray period at the join.
        /// </summary>
        private static void AnnounceTabAndItem()
        {
            TolkHelper.Speak($"{BuildCurrentTabText()}. {BuildCurrentItemText()}");
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
            TolkHelper.SpeakData(announcement);
        }

        /// <summary>
        /// Announces the mass summary.
        /// </summary>
        public static void AnnounceMassSummary()
        {
            float capacity = adapter?.MassCapacity ?? 0f;
            float usage = GetMassUsage();
            float remaining = capacity - usage;

            string status;
            if (usage > capacity)
            {
                float over = usage - capacity;
                status = (string)"RimWorldAccess.TransportPods.Loading.MassOverloaded".Translate(over.ToString("F1"));
            }
            else
            {
                status = (string)"RimWorldAccess.TransportPods.Loading.MassRemaining".Translate(remaining.ToString("F1"));
            }

            TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.MassSummary".Loc(usage.ToString("F1"), capacity.ToString("F1"), status));
        }

        #endregion

        #region Data Access

        /// <summary>
        /// Gets all transferables from the dialog.
        /// </summary>
        private static List<TransferableOneWay> GetAllTransferables()
        {
            return adapter?.GetAllTransferables() ?? new List<TransferableOneWay>();
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
            adapter?.NotifyTransferablesChanged();
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
            // Map portals show no stats panel in vanilla, so there is no summary to toggle into.
            if (!showingSummary && (adapter == null || !adapter.HasSummary))
            {
                TolkHelper.Speak("No summary available for this dialog");
                return;
            }

            if (showingSummary)
            {
                showingSummary = false;
                currentTab = savedTab;
                selectedIndex = savedIndex;
                typeahead.ClearSearch();

                string tabName = CurrentTabName();
                TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.ReturnedToTab".Loc(tabName));
                AnnounceCurrentItem();
            }
            else
            {
                savedTab = currentTab;
                savedIndex = selectedIndex;
                showingSummary = true;
                typeahead.ClearSearch();

                TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.SummaryEntry".Loc());
                BuildSummaryItems();
                AnnounceCurrentSummaryItem();
            }
        }

        /// <summary>
        /// Builds the summary items list via the active dialog adapter.
        /// Transport pods/shuttles show caravan-style stats (Mass, Speed, Food, Foraging,
        /// Visibility - shuttles omit all but Mass and Food); map portals show none.
        /// This matches exactly what a sighted player would see.
        /// </summary>
        private static void BuildSummaryItems()
        {
            summaryItems.Clear();
            summaryKinds.Clear();
            // Don't reset summaryIndex - preserve position across summary views

            if (adapter == null)
            {
                summaryItems.Add((string)"RimWorldAccess.TransportPods.Loading.SummaryNoData".Translate());
                summaryKinds.Add(null);
                return;
            }

            adapter.BuildSummaryItems(summaryItems, GetMassUsage());
        }

        /// <summary>
        /// Announces the currently selected summary item.
        /// </summary>
        private static void AnnounceCurrentSummaryItem()
        {
            if (summaryItems.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.SummaryNoSummary".Loc());
                return;
            }

            if (summaryIndex < 0 || summaryIndex >= summaryItems.Count)
            {
                summaryIndex = 0;
            }

            string item = summaryItems[summaryIndex];
            string position = MenuHelper.FormatPosition(summaryIndex, summaryItems.Count);
            TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.SummaryItemAnnouncement".Loc(item, position));
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
        /// Gets the (stat name, breakdown explanation) for the currently selected summary stat,
        /// delegating to the active dialog adapter. Returns null when no breakdown is available.
        /// </summary>
        private static (string name, string explanation)? GetCurrentStatExplanation()
        {
            if (adapter == null || summaryItems.Count == 0)
                return null;

            if (summaryIndex < 0 || summaryIndex >= summaryItems.Count)
                return null;

            return adapter.GetStatExplanation(summaryItems[summaryIndex]);
        }

        #endregion

        #region Typeahead Support
        #endregion
    }
}
