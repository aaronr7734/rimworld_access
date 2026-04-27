using System;
using System.Collections.Generic;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a windowless play settings menu for global game settings.
    /// Provides keyboard navigation for auto-rebuild and auto-home area settings.
    /// </summary>
    public static class PlaySettingsMenuState
    {
        private static List<MenuOption> currentOptions = null;
        private static int selectedIndex = 0;
        private static bool isActive = false;

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the play settings menu.
        /// </summary>
        public static void Open()
        {
            BuildMenuOptions();
            selectedIndex = 0;
            isActive = true;

            // Announce first option
            AnnounceCurrentOption();
        }

        /// <summary>
        /// Closes the play settings menu.
        /// </summary>
        public static void Close()
        {
            currentOptions = null;
            selectedIndex = 0;
            isActive = false;
        }

        /// <summary>
        /// Moves selection to next option.
        /// </summary>
        public static void SelectNext()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, currentOptions.Count);
            AnnounceCurrentOption();
        }

        /// <summary>
        /// Moves selection to previous option.
        /// </summary>
        public static void SelectPrevious()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, currentOptions.Count);
            AnnounceCurrentOption();
        }

        /// <summary>
        /// Executes the currently selected option.
        /// </summary>
        public static void ExecuteSelected()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= currentOptions.Count)
                return;

            MenuOption selected = currentOptions[selectedIndex];

            // Execute the action (don't close menu for toggles, only for Cancel)
            selected.Action?.Invoke();
        }

        private static void AnnounceCurrentOption()
        {
            if (selectedIndex >= 0 && selectedIndex < currentOptions.Count)
            {
                TolkHelper.Speak(currentOptions[selectedIndex].Label);
            }
        }

        /// <summary>
        /// Builds the list of menu options.
        /// </summary>
        private static void BuildMenuOptions()
        {
            currentOptions = new List<MenuOption>();

            // Auto-rebuild toggle
            currentOptions.Add(new MenuOption(
                (Find.PlaySettings.autoRebuild
                    ? "RimWorldAccess.Map.PlaySettings.AutoRebuild.LabelOn"
                    : "RimWorldAccess.Map.PlaySettings.AutoRebuild.LabelOff").Translate(),
                () => {
                    Find.PlaySettings.autoRebuild = !Find.PlaySettings.autoRebuild;
                    TolkHelper.Speak((Find.PlaySettings.autoRebuild
                        ? "RimWorldAccess.Map.PlaySettings.AutoRebuild.Enabled"
                        : "RimWorldAccess.Map.PlaySettings.AutoRebuild.Disabled").Translate());
                    BuildMenuOptions(); // Refresh to update labels
                    AnnounceCurrentOption(); // Re-announce with new state
                }
            ));

            // Auto-expand home area toggle
            currentOptions.Add(new MenuOption(
                (Find.PlaySettings.autoHomeArea
                    ? "RimWorldAccess.Map.PlaySettings.AutoHomeArea.LabelOn"
                    : "RimWorldAccess.Map.PlaySettings.AutoHomeArea.LabelOff").Translate(),
                () => {
                    Find.PlaySettings.autoHomeArea = !Find.PlaySettings.autoHomeArea;
                    TolkHelper.Speak((Find.PlaySettings.autoHomeArea
                        ? "RimWorldAccess.Map.PlaySettings.AutoHomeArea.Enabled"
                        : "RimWorldAccess.Map.PlaySettings.AutoHomeArea.Disabled").Translate());
                    BuildMenuOptions(); // Refresh
                    AnnounceCurrentOption();
                }
            ));

            // Cancel option (vanilla key)
            currentOptions.Add(new MenuOption(
                "CancelButton".Translate(),
                Close
            ));
        }

        /// <summary>
        /// Simple data structure for menu options.
        /// </summary>
        private class MenuOption
        {
            public string Label { get; }
            public Action Action { get; }

            public MenuOption(string label, Action action)
            {
                Label = label;
                Action = action;
            }
        }
    }
}
