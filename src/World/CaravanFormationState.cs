using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for keyboard navigation in Dialog_FormCaravan.
    /// Provides four-tab interface for selecting pawns, items, travel supplies, and viewing stats.
    /// </summary>
    public static class CaravanFormationState
    {
        private enum Tab
        {
            Pawns,
            Items,
            TravelSupplies,
            Stats
        }

        private const int TabCount = 4;

        private static bool isActive = false;
        private static Dialog_FormCaravan currentDialog = null;
        private static Tab currentTab = Tab.Pawns;
        private static int selectedIndex = 0;
        private static bool isChoosingDestination = false;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // Stats tab entries for navigation
        private static List<string> statsEntries = new List<string>();
        private static int statsIndex = 0;

        /// <summary>
        /// Gets whether caravan formation keyboard navigation is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets whether we're currently choosing a destination for the caravan.
        /// </summary>
        public static bool IsChoosingDestination => isChoosingDestination;

        /// <summary>
        /// Triggers caravan reformation from the current map (for temporary encounter maps after ambushes).
        /// This checks if the current map is a temporary map and opens Dialog_FormCaravan with reform=true.
        /// </summary>
        public static void TriggerReformation()
        {
            Map currentMap = Find.CurrentMap;

            if (currentMap == null)
            {
                TolkHelper.Speak("No map loaded", SpeechPriority.High);
                return;
            }

            // Check if this is a temporary map that requires reformation
            if (currentMap.IsPlayerHome)
            {
                TolkHelper.Speak("Cannot reform caravan from home settlement. Use world map to form new caravans.", SpeechPriority.High);
                return;
            }

            // Get the FormCaravanComp from the map's parent world object
            MapParent mapParent = currentMap.Parent;
            if (mapParent == null)
            {
                TolkHelper.Speak("Map has no parent world object", SpeechPriority.High);
                return;
            }

            FormCaravanComp formCaravanComp = mapParent.GetComponent<FormCaravanComp>();
            if (formCaravanComp == null)
            {
                TolkHelper.Speak("This map does not support caravan reformation", SpeechPriority.High);
                return;
            }

            // Check if reformation is allowed (no active threats, etc.)
            if (!formCaravanComp.CanFormOrReformCaravanNow)
            {
                // Try to get the reason why reformation is blocked
                if (GenHostility.AnyHostileActiveThreatToPlayer(currentMap, countDormantPawnsAsHostile: false))
                {
                    TolkHelper.Speak("Cannot reform caravan while enemies are present", SpeechPriority.High);
                }
                else
                {
                    TolkHelper.Speak("Cannot reform caravan at this time", SpeechPriority.High);
                }
                return;
            }

            // Open Dialog_FormCaravan in reform mode
            Dialog_FormCaravan reformDialog = new Dialog_FormCaravan(currentMap, reform: true);
            Find.WindowStack.Add(reformDialog);

            TolkHelper.Speak("Opening caravan reformation dialog", SpeechPriority.Normal);
        }

        /// <summary>
        /// Opens keyboard navigation for the specified Dialog_FormCaravan.
        /// </summary>
        public static void Open(Dialog_FormCaravan dialog)
        {
            if (dialog == null)
            {
                TolkHelper.Speak("No caravan formation dialog available", SpeechPriority.High);
                return;
            }

            isActive = true;
            currentDialog = dialog;
            currentTab = Tab.Pawns;
            selectedIndex = 0;
            typeahead.ClearSearch();

            // Disable auto-select travel supplies to prevent it from resetting our manual selections
            DisableAutoSelectTravelSupplies();

            TolkHelper.Speak("Caravan formation dialog opened. Use Left/Right to switch tabs, Up/Down to navigate, +/- or Enter to adjust. Press Alt+D to choose destination, Alt+T to send, Alt+R to reset, Escape to cancel.");
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
            statsIndex = 0;
            statsEntries.Clear();
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Gets the transferables list from the current dialog using reflection.
        /// Note: While transferables appears public in decompiled code, using reflection
        /// ensures we don't interfere with the game's internal state management.
        /// </summary>
        private static List<TransferableOneWay> GetTransferables()
        {
            if (currentDialog == null)
                return new List<TransferableOneWay>();

            try
            {
                FieldInfo field = AccessTools.Field(typeof(Dialog_FormCaravan), "transferables");
                if (field != null)
                {
                    var result = field.GetValue(currentDialog);
                    if (result is List<TransferableOneWay> transferables)
                    {
                        return transferables;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"RimWorld Access: Failed to get transferables from Dialog_FormCaravan: {ex.Message}");
            }

            return new List<TransferableOneWay>();
        }

        /// <summary>
        /// Gets transferables for the current tab.
        /// </summary>
        private static List<TransferableOneWay> GetCurrentTabTransferables()
        {
            List<TransferableOneWay> allTransferables = GetTransferables();

            switch (currentTab)
            {
                case Tab.Pawns:
                    // Pawns: anything that's a Pawn
                    return allTransferables
                        .Where(t => t.ThingDef.category == ThingCategory.Pawn)
                        .ToList();

                case Tab.TravelSupplies:
                    // Travel Supplies: use RimWorld's official filtering logic from CaravanUIUtility.GetTransferableCategory
                    return allTransferables
                        .Where(t => GetTransferableCategory(t) == TransferableCategory.TravelSupplies)
                        .ToList();

                case Tab.Items:
                    // Items: everything that's not a pawn and not travel supplies
                    return allTransferables
                        .Where(t => GetTransferableCategory(t) == TransferableCategory.Item)
                        .ToList();

                default:
                    return allTransferables;
            }
        }

        /// <summary>
        /// Replicates RimWorld's CaravanUIUtility.GetTransferableCategory logic.
        /// This determines whether an item is a Pawn, Travel Supply, or regular Item.
        /// </summary>
        private static TransferableCategory GetTransferableCategory(TransferableOneWay t)
        {
            if (t.ThingDef.category == ThingCategory.Pawn)
            {
                return TransferableCategory.Pawn;
            }

            // Travel Supplies include:
            // 1. Medicine (in the Medicine thing category)
            // 2. Food (ingestible, not drug, not corpse, not tree)
            // 3. Bedrolls (beds that caravans can use)
            if ((!t.ThingDef.thingCategories.NullOrEmpty() && t.ThingDef.thingCategories.Contains(ThingCategoryDefOf.Medicine)) ||
                (t.ThingDef.IsIngestible && !t.ThingDef.IsDrug && !t.ThingDef.IsCorpse && (t.ThingDef.plant == null || !t.ThingDef.plant.IsTree)) ||
                (t.AnyThing.GetInnerIfMinified().def.IsBed && t.AnyThing.GetInnerIfMinified().def.building != null && t.AnyThing.GetInnerIfMinified().def.building.bed_caravansCanUse))
            {
                return TransferableCategory.TravelSupplies;
            }

            return TransferableCategory.Item;
        }

        /// <summary>
        /// Enum matching RimWorld's internal TransferableCategory enum.
        /// </summary>
        private enum TransferableCategory
        {
            Pawn,
            Item,
            TravelSupplies
        }

        /// <summary>
        /// Announces the current tab.
        /// </summary>
        private static void AnnounceCurrentTab()
        {
            if (currentTab == Tab.Stats)
            {
                TolkHelper.Speak("Stats tab");
                return;
            }

            string tabName = "";
            switch (currentTab)
            {
                case Tab.Pawns:
                    tabName = "Pawns tab";
                    break;
                case Tab.Items:
                    tabName = "Items tab";
                    break;
                case Tab.TravelSupplies:
                    tabName = "Travel Supplies tab";
                    break;
            }

            List<TransferableOneWay> tabTransferables = GetCurrentTabTransferables();
            TolkHelper.Speak($"{tabName}, {tabTransferables.Count} items");
        }

        /// <summary>
        /// Announces the currently selected item.
        /// </summary>
        private static void AnnounceCurrentItem()
        {
            // Stats tab has special handling
            if (currentTab == Tab.Stats)
            {
                AnnounceStats();
                return;
            }

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

            StringBuilder announcement = new StringBuilder();

            if (transferable.AnyThing is Pawn pawn)
            {
                // Pawn announcement
                announcement.Append(pawn.LabelShortCap.StripTags());

                if (pawn.story != null && !pawn.story.TitleCap.NullOrEmpty())
                {
                    announcement.Append($", {pawn.story.TitleCap.StripTags()}");
                }

                if (transferable.CountToTransfer > 0)
                {
                    announcement.Append(" - Selected");
                }
                else
                {
                    announcement.Append(" - Not selected");
                }
            }
            else
            {
                // Item announcement
                announcement.Append(transferable.LabelCap.StripTags());

                int current = transferable.CountToTransfer;
                int max = transferable.GetMaximumToTransfer();

                announcement.Append($" - {current} of {max}");

                // Add mass information if significant
                if (current > 0)
                {
                    float totalMass = transferable.AnyThing.GetStatValue(StatDefOf.Mass) * current;
                    if (totalMass >= 1f)
                    {
                        announcement.Append($", {totalMass:F1} kg");
                    }
                }
            }

            // Add position at the end
            announcement.Append($". {MenuHelper.FormatPosition(selectedIndex, transferables.Count)}");

            TolkHelper.Speak(announcement.ToString());
        }

        /// <summary>
        /// Selects the next item in the current tab.
        /// </summary>
        public static void SelectNext()
        {
            // Stats tab has special navigation
            if (currentTab == Tab.Stats)
            {
                RebuildStatsEntries();
                if (statsEntries.Count > 0)
                {
                    statsIndex = MenuHelper.SelectNext(statsIndex, statsEntries.Count);
                    AnnounceStats();
                }
                return;
            }

            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0)
            {
                TolkHelper.Speak("No items in this tab");
                return;
            }

            // If typeahead is active with matches, navigate to next match
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

            // Navigate normally (either no search active, OR search with no matches)
            selectedIndex = MenuHelper.SelectNext(selectedIndex, transferables.Count);

            AnnounceCurrentItem();
        }

        /// <summary>
        /// Selects the previous item in the current tab.
        /// </summary>
        public static void SelectPrevious()
        {
            // Stats tab has special navigation
            if (currentTab == Tab.Stats)
            {
                RebuildStatsEntries();
                if (statsEntries.Count > 0)
                {
                    statsIndex = MenuHelper.SelectPrevious(statsIndex, statsEntries.Count);
                    AnnounceStats();
                }
                return;
            }

            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0)
            {
                TolkHelper.Speak("No items in this tab");
                return;
            }

            // If typeahead is active with matches, navigate to previous match
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

            // Navigate normally (either no search active, OR search with no matches)
            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, transferables.Count);

            AnnounceCurrentItem();
        }

        /// <summary>
        /// Jumps to the first item in the current tab.
        /// </summary>
        public static void JumpToFirst()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0)
            {
                TolkHelper.Speak("No items in this tab");
                return;
            }

            selectedIndex = MenuHelper.JumpToFirst();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Jumps to the last item in the current tab.
        /// </summary>
        public static void JumpToLast()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0)
            {
                TolkHelper.Speak("No items in this tab");
                return;
            }

            selectedIndex = MenuHelper.JumpToLast(transferables.Count);
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Switches to the next tab.
        /// </summary>
        public static void NextTab()
        {
            currentTab = (Tab)(((int)currentTab + 1) % TabCount);
            selectedIndex = 0;
            statsIndex = 0;
            typeahead.ClearSearch();
            AnnounceCurrentTab();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Switches to the previous tab.
        /// </summary>
        public static void PreviousTab()
        {
            currentTab = (Tab)(((int)currentTab + TabCount - 1) % TabCount);
            selectedIndex = 0;
            statsIndex = 0;
            typeahead.ClearSearch();
            AnnounceCurrentTab();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Adjusts the quantity of the selected item.
        /// </summary>
        public static void AdjustQuantity(int delta)
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0)
            {
                TolkHelper.Speak("No items in this tab");
                return;
            }

            if (selectedIndex < 0 || selectedIndex >= transferables.Count)
                return;

            TransferableOneWay transferable = transferables[selectedIndex];

            if (transferable.AnyThing is Pawn)
            {
                // For pawns, toggle selection (0 or max)
                if (transferable.CountToTransfer > 0)
                {
                    transferable.AdjustTo(0);
                    TolkHelper.Speak("Deselected");
                }
                else
                {
                    int max = transferable.GetMaximumToTransfer();
                    transferable.AdjustTo(max);
                    TolkHelper.Speak("Selected");
                }
            }
            else
            {
                // For items, adjust by delta
                // Check if the item is interactive first
                if (!transferable.Interactive)
                {
                    TolkHelper.Speak("This item cannot be adjusted");
                    return;
                }

                AcceptanceReport canAdjust = transferable.CanAdjustBy(delta);
                if (canAdjust.Accepted)
                {
                    transferable.AdjustBy(delta);
                    NotifyTransferablesChanged();
                    AnnounceCurrentItem();
                }
                else
                {
                    // Report the specific reason why adjustment failed
                    string reason = canAdjust.Reason.NullOrEmpty() ? "Cannot adjust quantity" : canAdjust.Reason;
                    TolkHelper.Speak(reason);
                }
            }

            NotifyTransferablesChanged();
        }

        /// <summary>
        /// Toggles selection for the current item (same as Enter key).
        /// </summary>
        public static void ToggleSelection()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0)
            {
                TolkHelper.Speak("No items in this tab");
                return;
            }

            if (selectedIndex < 0 || selectedIndex >= transferables.Count)
                return;

            TransferableOneWay transferable = transferables[selectedIndex];

            if (transferable.AnyThing is Pawn)
            {
                // For pawns, toggle between 0 and max
                if (transferable.CountToTransfer > 0)
                {
                    transferable.AdjustTo(0);
                    TolkHelper.Speak($"Deselected {transferable.LabelCap.StripTags()}");
                }
                else
                {
                    int max = transferable.GetMaximumToTransfer();
                    transferable.AdjustTo(max);
                    TolkHelper.Speak($"Selected {transferable.LabelCap.StripTags()}");
                }
            }
            else
            {
                // For items, increment by 1
                // Check if the item is interactive first
                if (!transferable.Interactive)
                {
                    TolkHelper.Speak("This item cannot be adjusted");
                    return;
                }

                AcceptanceReport canAdjust = transferable.CanAdjustBy(1);
                if (canAdjust.Accepted)
                {
                    transferable.AdjustBy(1);
                    NotifyTransferablesChanged();
                    AnnounceCurrentItem();
                }
                else
                {
                    // Report the specific reason why adjustment failed
                    string reason = canAdjust.Reason.NullOrEmpty() ? "Cannot increase quantity" : canAdjust.Reason;
                    TolkHelper.Speak(reason);
                }
            }

            NotifyTransferablesChanged();
        }

        /// <summary>
        /// Opens the route planner to choose a destination for the caravan.
        /// Switches to world view and enables destination selection mode.
        /// </summary>
        public static void ChooseRoute()
        {
            if (currentDialog == null)
            {
                TolkHelper.Speak("No dialog available", SpeechPriority.High);
                return;
            }

            try
            {
                // Enter destination selection mode BEFORE removing dialog
                // (PostClose checks this flag to avoid clearing currentDialog)
                isChoosingDestination = true;

                // Close the dialog temporarily (don't clear currentDialog - we need it to return)
                if (Find.WindowStack != null)
                {
                    Find.WindowStack.TryRemove(currentDialog, doCloseSound: false);
                }

                // Switch to world view
                CameraJumper.TryShowWorld();

                // Make sure world navigation is active
                if (!WorldNavigationState.IsActive)
                {
                    WorldNavigationState.Open();
                }

                TolkHelper.Speak("Choosing caravan destination. Use arrow keys to navigate the world map, Enter to select destination, or Escape to cancel.");
            }
            catch (Exception ex)
            {
                TolkHelper.Speak($"Failed to open route planner: {ex.Message}", SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to start route planner: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the destination for the caravan and returns to the formation dialog.
        /// </summary>
        public static void SetDestination(PlanetTile destinationTile)
        {
            if (currentDialog == null)
            {
                TolkHelper.Speak("No dialog available", SpeechPriority.High);
                Log.Warning("RimWorld Access: SetDestination called but currentDialog is null");
                isChoosingDestination = false;
                return;
            }

            if (!destinationTile.Valid)
            {
                TolkHelper.Speak("Invalid destination tile", SpeechPriority.High);
                Log.Warning($"RimWorld Access: SetDestination called with invalid tile: {destinationTile}");
                isChoosingDestination = false;
                return;
            }

            try
            {
                // Get the method before we do anything
                MethodInfo notifyChoseRouteMethod = AccessTools.Method(typeof(Dialog_FormCaravan), "Notify_ChoseRoute");
                if (notifyChoseRouteMethod == null)
                {
                    TolkHelper.Speak("Failed to access Notify_ChoseRoute method", SpeechPriority.High);
                    isChoosingDestination = false;
                    return;
                }

                // Return to map view first
                CameraJumper.TryHideWorld();

                // Reopen the dialog BEFORE calling Notify_ChoseRoute (matches game behavior)
                if (Find.WindowStack != null)
                {
                    Find.WindowStack.Add(currentDialog);
                }

                // Exit destination selection mode BEFORE calling Notify_ChoseRoute
                // (so PostOpen doesn't think we're still choosing)
                isChoosingDestination = false;

                // Now call Notify_ChoseRoute to set destination and calculate exit tile
                notifyChoseRouteMethod.Invoke(currentDialog, new object[] { destinationTile });

                // Announce destination set
                string tileInfo = WorldInfoHelper.GetTileSummary(destinationTile);
                TolkHelper.Speak($"Destination set to {tileInfo}.");
            }
            catch (Exception ex)
            {
                TolkHelper.Speak($"Failed to set destination: {ex.Message}", SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to set caravan destination: {ex}");
                isChoosingDestination = false;
            }
        }

        /// <summary>
        /// Cancels destination selection and returns to the formation dialog.
        /// </summary>
        public static void CancelDestinationSelection()
        {
            if (currentDialog == null)
            {
                isChoosingDestination = false;
                return;
            }

            try
            {
                // Return to map view
                CameraJumper.TryHideWorld();

                // Reopen the dialog
                if (Find.WindowStack != null)
                {
                    Find.WindowStack.Add(currentDialog);
                }

                // Exit destination selection mode
                isChoosingDestination = false;

                TolkHelper.Speak("Destination selection cancelled. Returning to caravan formation dialog.");
            }
            catch (Exception ex)
            {
                TolkHelper.Speak($"Failed to cancel destination selection: {ex.Message}", SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to cancel destination selection: {ex.Message}");
                isChoosingDestination = false;
            }
        }

        /// <summary>
        /// Attempts to send the caravan using Dialog_FormCaravan.OnAcceptKeyPressed().
        /// </summary>
        public static void Send()
        {
            if (currentDialog == null)
            {
                TolkHelper.Speak("No dialog available", SpeechPriority.High);
                return;
            }

            try
            {
                // Temporarily deactivate keyboard navigation so confirmation dialogs can be accessed
                // (OnAcceptKeyPressed may show "low food" or other warnings that require confirmation)
                bool wasActive = isActive;
                isActive = false;

                // OnAcceptKeyPressed is the public method that calls TrySend internally
                currentDialog.OnAcceptKeyPressed();

                // If the dialog is still in the window stack, reactivate keyboard navigation
                // (This happens when a confirmation dialog is shown)
                // If the dialog closed successfully, PostClose will have been called already
                if (currentDialog != null && Find.WindowStack != null && Find.WindowStack.IsOpen(currentDialog))
                {
                    isActive = wasActive;
                }
                // OnAcceptKeyPressed will show error messages if validation fails
                // If successful, the dialog will close and CaravanFormationPatch.PostClose will be called
            }
            catch (Exception ex)
            {
                TolkHelper.Speak($"Failed to send caravan: {ex.Message}", SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to send caravan: {ex.Message}");
                // Reactivate on error
                if (currentDialog != null)
                {
                    isActive = true;
                }
            }
        }

        /// <summary>
        /// Resets all selections by calling Dialog_FormCaravan.CalculateAndRecacheTransferables() via reflection.
        /// </summary>
        public static void Reset()
        {
            if (currentDialog == null)
            {
                TolkHelper.Speak("No dialog available", SpeechPriority.High);
                return;
            }

            try
            {
                MethodInfo method = AccessTools.Method(typeof(Dialog_FormCaravan), "CalculateAndRecacheTransferables");
                if (method != null)
                {
                    method.Invoke(currentDialog, null);
                    selectedIndex = 0;
                    TolkHelper.Speak("Selections reset");
                    AnnounceCurrentItem();
                }
                else
                {
                    TolkHelper.Speak("Failed to reset - method not found", SpeechPriority.High);
                }
            }
            catch (Exception ex)
            {
                TolkHelper.Speak($"Failed to reset: {ex.Message}", SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to call CalculateAndRecacheTransferables: {ex.Message}");
            }
        }

        /// <summary>
        /// Disables auto-select travel supplies feature to prevent it from resetting manual selections.
        /// </summary>
        private static void DisableAutoSelectTravelSupplies()
        {
            if (currentDialog == null)
                return;

            try
            {
                FieldInfo field = AccessTools.Field(typeof(Dialog_FormCaravan), "autoSelectTravelSupplies");
                if (field != null)
                {
                    field.SetValue(currentDialog, false);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to disable auto-select travel supplies: {ex.Message}");
            }
        }

        /// <summary>
        /// Notifies the dialog that transferables have changed, which recalculates mass/food stats.
        /// </summary>
        private static void NotifyTransferablesChanged()
        {
            if (currentDialog == null)
                return;

            try
            {
                MethodInfo method = AccessTools.Method(typeof(Dialog_FormCaravan), "Notify_TransferablesChanged");
                if (method != null)
                {
                    method.Invoke(currentDialog, null);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to call Notify_TransferablesChanged: {ex.Message}");
            }
        }

        /// <summary>
        /// Rebuilds the stats entries list from current caravan data.
        /// </summary>
        private static void RebuildStatsEntries()
        {
            statsEntries.Clear();

            if (currentDialog == null)
                return;

            try
            {
                // Get mass usage and capacity (public properties - access directly)
                float massUsage = currentDialog.MassUsage;
                float massCapacity = currentDialog.MassCapacity;
                float massRemaining = massCapacity - massUsage;

                string massEntry = $"Mass: {massUsage:F1} of {massCapacity:F1} kg";
                if (massUsage > massCapacity)
                {
                    massEntry += " - OVERLOADED!";
                }
                else
                {
                    massEntry += $", {massRemaining:F1} kg remaining";
                }
                statsEntries.Add(massEntry);

                // Get days worth of food (private property - must use reflection)
                try
                {
                    PropertyInfo daysWorthProp = AccessTools.Property(typeof(Dialog_FormCaravan), "DaysWorthOfFood");
                    if (daysWorthProp != null)
                    {
                        var daysWorthObj = daysWorthProp.GetValue(currentDialog);
                        // Cast to ValueTuple explicitly to match the boxed type
                        var daysWorth = (ValueTuple<float, float>)daysWorthObj;
                        float days = daysWorth.Item1;
                        float tillRot = daysWorth.Item2;

                        string foodEntry;
                        if (days < 0.1f)
                        {
                            foodEntry = "Food: None!";
                        }
                        else
                        {
                            foodEntry = $"Food: {days:F1} days";
                            if (tillRot < days && tillRot > 0)
                            {
                                foodEntry += $", spoils in {tillRot:F1} days";
                            }
                        }
                        statsEntries.Add(foodEntry);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"RimWorld Access: Failed to get food stats: {ex.Message}");
                }

                // Get tiles per day (private property)
                try
                {
                    PropertyInfo tilesPerDayProp = AccessTools.Property(typeof(Dialog_FormCaravan), "TilesPerDay");
                    if (tilesPerDayProp != null)
                    {
                        float tilesPerDay = (float)tilesPerDayProp.GetValue(currentDialog);
                        string speedEntry = tilesPerDay > 0
                            ? $"Speed: {tilesPerDay:F1} tiles per day"
                            : "Speed: Cannot move!";
                        statsEntries.Add(speedEntry);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"RimWorld Access: Failed to get speed stats: {ex.Message}");
                }

                // Get foraging info (private property - must use reflection)
                try
                {
                    PropertyInfo forageProp = AccessTools.Property(typeof(Dialog_FormCaravan), "ForagedFoodPerDay");
                    if (forageProp != null)
                    {
                        var forageObj = forageProp.GetValue(currentDialog);
                        // Cast to ValueTuple explicitly to match the boxed type
                        var forageInfo = (ValueTuple<ThingDef, float>)forageObj;
                        ThingDef foodDef = forageInfo.Item1;
                        float perDay = forageInfo.Item2;

                        if (foodDef != null && perDay > 0)
                        {
                            statsEntries.Add($"Foraging: {perDay:F1} {foodDef.label} per day");
                        }
                        else
                        {
                            statsEntries.Add("Foraging: None available");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"RimWorld Access: Failed to get foraging stats: {ex.Message}");
                }

                // Get visibility (private property)
                try
                {
                    PropertyInfo visibilityProp = AccessTools.Property(typeof(Dialog_FormCaravan), "Visibility");
                    if (visibilityProp != null)
                    {
                        float visibility = (float)visibilityProp.GetValue(currentDialog);
                        statsEntries.Add($"Visibility: {visibility:P0}");
                    }
                }
                catch { /* Skip visibility stats on error */ }

                // Get destination info if set
                try
                {
                    FieldInfo destTileField = AccessTools.Field(typeof(Dialog_FormCaravan), "destinationTile");
                    if (destTileField != null)
                    {
                        // Cast boxed value directly to PlanetTile struct
                        PlanetTile destTile = (PlanetTile)destTileField.GetValue(currentDialog);

                        if (destTile.Valid && Find.WorldGrid != null)
                        {
                            string tileName = WorldInfoHelper.GetTileSummary(destTile);
                            statsEntries.Add($"Destination: {tileName}");

                            // Get ETA - note: this can fail if world path finding has issues
                            PropertyInfo ticksToArriveProp = AccessTools.Property(typeof(Dialog_FormCaravan), "TicksToArrive");
                            if (ticksToArriveProp != null)
                            {
                                try
                                {
                                    int ticksToArrive = (int)ticksToArriveProp.GetValue(currentDialog);
                                    if (ticksToArrive > 0)
                                    {
                                        float daysToArrive = ticksToArrive / 60000f;
                                        statsEntries.Add($"ETA: {daysToArrive:F1} days");
                                    }
                                    else
                                    {
                                        // Game's path calculation may have failed - show fallback
                                        statsEntries.Add("ETA: Calculating...");
                                    }
                                }
                                catch (Exception etaEx)
                                {
                                    Log.Warning($"RimWorld Access: Failed to get TicksToArrive: {etaEx.Message}");
                                    statsEntries.Add("ETA: Unable to calculate");
                                }
                            }
                        }
                        else
                        {
                            statsEntries.Add("Destination: Not set (use Alt+D to choose)");
                        }
                    }
                }
                catch (Exception destEx)
                {
                    Log.Warning($"RimWorld Access: Failed to get destination info: {destEx.Message}");
                    statsEntries.Add("Destination: Not set");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"RimWorld Access: Failed to rebuild caravan stats: {ex}");
            }
        }

        /// <summary>
        /// Announces the current stat entry.
        /// </summary>
        private static void AnnounceStats()
        {
            if (currentDialog == null)
            {
                TolkHelper.Speak("No caravan data available");
                return;
            }

            // Rebuild stats entries each time to get current values
            RebuildStatsEntries();

            if (statsEntries.Count == 0)
            {
                TolkHelper.Speak("Could not read caravan stats");
                return;
            }

            if (statsIndex < 0 || statsIndex >= statsEntries.Count)
            {
                statsIndex = 0;
            }

            string entry = statsEntries[statsIndex];
            string position = MenuHelper.FormatPosition(statsIndex, statsEntries.Count);
            TolkHelper.Speak($"{entry}. {position}");
        }

        /// <summary>
        /// Gets the list of transferable labels for the current tab for typeahead search.
        /// </summary>
        private static List<string> GetTransferableLabels()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();
            var labels = new List<string>();
            foreach (var t in transferables)
            {
                if (t.AnyThing is Pawn pawn)
                {
                    labels.Add(pawn.LabelShortCap);
                }
                else
                {
                    labels.Add(t.LabelCap);
                }
            }
            return labels;
        }

        /// <summary>
        /// Announces the current item with search information.
        /// </summary>
        private static void AnnounceWithSearch()
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
            StringBuilder announcement = new StringBuilder();

            if (transferable.AnyThing is Pawn pawn)
            {
                // Pawn announcement
                announcement.Append(pawn.LabelShortCap.StripTags());

                if (pawn.story != null && !pawn.story.TitleCap.NullOrEmpty())
                {
                    announcement.Append($", {pawn.story.TitleCap.StripTags()}");
                }

                if (transferable.CountToTransfer > 0)
                {
                    announcement.Append(" - Selected");
                }
                else
                {
                    announcement.Append(" - Not selected");
                }
            }
            else
            {
                // Item announcement
                announcement.Append(transferable.LabelCap.StripTags());

                int current = transferable.CountToTransfer;
                int max = transferable.GetMaximumToTransfer();

                announcement.Append($" - {current} of {max}");

                // Add mass information if significant
                if (current > 0)
                {
                    float totalMass = transferable.AnyThing.GetStatValue(StatDefOf.Mass) * current;
                    if (totalMass >= 1f)
                    {
                        announcement.Append($", {totalMass:F1} kg");
                    }
                }
            }

            // Add search info at the end
            announcement.Append($". '{typeahead.SearchBuffer}' match {typeahead.CurrentMatchPosition} of {typeahead.MatchCount}");

            TolkHelper.Speak(announcement.ToString());
        }

        /// <summary>
        /// Handles keyboard input for caravan formation.
        /// Returns true if the input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive)
                return false;

            // When choosing destination, let world navigation handle the input
            if (isChoosingDestination)
                return false;

            // Handle Home - jump to first
            if (key == KeyCode.Home)
            {
                JumpToFirst();
                Event.current.Use();
                return true;
            }

            // Handle End - jump to last
            if (key == KeyCode.End)
            {
                JumpToLast();
                Event.current.Use();
                return true;
            }

            // Handle Escape - clear search FIRST, then close dialog
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentItem();
                    return true;
                }
                // Force close the dialog
                if (Find.WindowStack != null)
                {
                    // Try to close by instance first
                    if (currentDialog != null && Find.WindowStack.IsOpen(currentDialog))
                    {
                        Find.WindowStack.TryRemove(currentDialog, doCloseSound: true);
                    }
                    // Also try by type in case reference is stale
                    Find.WindowStack.TryRemove(typeof(Dialog_FormCaravan), doCloseSound: false);
                }
                // Always clean up our state
                Close();
                TolkHelper.Speak("Caravan formation cancelled");
                return true;
            }

            // Handle Backspace for search
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = GetTransferableLabels();
                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0) selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
                Event.current.Use();
                return true;
            }

            // Handle * key - consume to prevent passthrough
            bool isStar = key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8);
            if (isStar)
            {
                Event.current.Use();
                return true;
            }

            // Handle typeahead characters (letters and numbers without Alt modifier)
            // Stats tab doesn't support typeahead - consume keys but don't process
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if ((isLetter || isNumber) && !alt)
            {
                if (currentTab == Tab.Stats)
                {
                    // Stats tab doesn't support typeahead search
                    Event.current.Use();
                    return true;
                }

                char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                var labels = GetTransferableLabels();
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
                Event.current.Use();
                return true;
            }

            switch (key)
            {
                case KeyCode.UpArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        SelectPrevious();
                        return true;
                    }
                    break;

                case KeyCode.DownArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        SelectNext();
                        return true;
                    }
                    break;

                case KeyCode.LeftArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        PreviousTab();
                        return true;
                    }
                    break;

                case KeyCode.RightArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        NextTab();
                        return true;
                    }
                    break;

                case KeyCode.Plus:
                case KeyCode.KeypadPlus:
                case KeyCode.Equals: // Shift+Equals is usually +
                    if (!ctrl && !alt)
                    {
                        if (currentTab == Tab.Stats)
                            return true; // Consume but do nothing
                        AdjustQuantity(1);
                        return true;
                    }
                    break;

                case KeyCode.Minus:
                case KeyCode.KeypadMinus:
                    if (!ctrl && !alt)
                    {
                        if (currentTab == Tab.Stats)
                            return true; // Consume but do nothing
                        AdjustQuantity(-1);
                        return true;
                    }
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (!shift && !ctrl && !alt)
                    {
                        // Stats tab doesn't have selectable items
                        if (currentTab == Tab.Stats)
                        {
                            return true; // Consume key but do nothing
                        }
                        ToggleSelection();
                        return true;
                    }
                    break;

                case KeyCode.D:
                    if (!shift && !ctrl && alt)
                    {
                        ChooseRoute();
                        return true;
                    }
                    break;

                case KeyCode.T:
                    if (!shift && !ctrl && alt)
                    {
                        Send();
                        return true;
                    }
                    break;

                case KeyCode.R:
                    if (!shift && !ctrl && alt)
                    {
                        Reset();
                        return true;
                    }
                    break;

                default:
                    return false;
            }

            return false;
        }
    }
}
