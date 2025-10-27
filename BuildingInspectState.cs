using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for building inspection and ITabs.
    /// Handles switching between different inspect tabs and opening tab-specific menus.
    /// </summary>
    public static class BuildingInspectState
    {
        private static Thing selectedBuilding = null;
        private static List<InspectTabBase> availableTabs = null;
        private static int selectedTabIndex = 0;
        private static bool isActive = false;

        public static bool IsActive => isActive;
        public static Thing SelectedBuilding => selectedBuilding;

        /// <summary>
        /// Opens the building inspect menu for the given building.
        /// </summary>
        public static void Open(Thing building)
        {
            if (building == null)
            {
                ClipboardHelper.CopyToClipboard("No building to inspect");
                return;
            }

            selectedBuilding = building;
            isActive = true;

            // Get all available tabs for this building
            availableTabs = building.GetInspectTabs().ToList();
            selectedTabIndex = 0;

            if (availableTabs.Count == 0)
            {
                ClipboardHelper.CopyToClipboard($"{building.LabelCap} - No available tabs");
                Close();
                return;
            }

            // Announce the building and first tab
            string announcement = $"Inspecting: {building.LabelCap}";
            if (availableTabs.Count > 0)
            {
                announcement += $" - Tab: {availableTabs[selectedTabIndex].labelKey.Translate()}";
            }
            ClipboardHelper.CopyToClipboard(announcement);
        }

        /// <summary>
        /// Closes the building inspect menu.
        /// </summary>
        public static void Close()
        {
            selectedBuilding = null;
            availableTabs = null;
            selectedTabIndex = 0;
            isActive = false;
        }

        /// <summary>
        /// Selects the next tab.
        /// </summary>
        public static void SelectNextTab()
        {
            if (availableTabs == null || availableTabs.Count == 0)
                return;

            selectedTabIndex = (selectedTabIndex + 1) % availableTabs.Count;
            AnnounceCurrentTab();
        }

        /// <summary>
        /// Selects the previous tab.
        /// </summary>
        public static void SelectPreviousTab()
        {
            if (availableTabs == null || availableTabs.Count == 0)
                return;

            selectedTabIndex = (selectedTabIndex - 1 + availableTabs.Count) % availableTabs.Count;
            AnnounceCurrentTab();
        }

        /// <summary>
        /// Opens the currently selected tab's menu (if supported).
        /// </summary>
        public static void OpenCurrentTab()
        {
            if (availableTabs == null || selectedTabIndex >= availableTabs.Count)
                return;

            InspectTabBase currentTab = availableTabs[selectedTabIndex];

            // Check if this is a bills tab
            if (currentTab is ITab_Bills billsTab)
            {
                // Get the building's bill stack
                if (selectedBuilding is IBillGiver billGiver)
                {
                    // Save position before closing
                    IntVec3 pos = selectedBuilding.Position;
                    // Close the building inspect state before opening bills menu
                    Close();
                    BillsMenuState.Open(billGiver, pos);
                    return;
                }
            }

            // Check if this is a storage tab
            if (currentTab.GetType().Name == "ITab_Storage")
            {
                // Get the storage settings
                if (selectedBuilding is IStoreSettingsParent storageParent)
                {
                    StorageSettings settings = storageParent.GetStoreSettings();
                    if (settings != null)
                    {
                        // Close the building inspect state before opening storage menu
                        Close();
                        StorageSettingsMenuState.Open(settings);
                        return;
                    }
                }
            }

            // For other tabs, just announce that they're not yet supported
            ClipboardHelper.CopyToClipboard($"Tab {currentTab.labelKey.Translate()} not yet supported for keyboard access");
        }

        /// <summary>
        /// Opens settings for the building directly without going through tabs.
        /// Used for buildings with simple settings like temperature control.
        /// </summary>
        public static void OpenBuildingSettings()
        {
            if (selectedBuilding == null)
                return;

            // Check if building has temperature control
            if (selectedBuilding is Building building)
            {
                CompTempControl tempControl = building.TryGetComp<CompTempControl>();
                if (tempControl != null)
                {
                    // Close building inspect and open temperature control menu
                    Close();
                    TempControlMenuState.Open(building);
                    return;
                }
            }

            // If no recognized settings, announce
            ClipboardHelper.CopyToClipboard($"{selectedBuilding.LabelCap} has no keyboard-accessible settings");
        }

        private static void AnnounceCurrentTab()
        {
            if (availableTabs == null || selectedTabIndex >= availableTabs.Count)
                return;

            string tabName = availableTabs[selectedTabIndex].labelKey.Translate();
            ClipboardHelper.CopyToClipboard($"Tab: {tabName}");
        }
    }
}
