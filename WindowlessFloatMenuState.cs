using System.Collections.Generic;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a "virtual" float menu without actually displaying a window.
    /// Stores FloatMenuOptions and handles keyboard navigation, then executes the selected option.
    /// </summary>
    public static class WindowlessFloatMenuState
    {
        private static List<FloatMenuOption> currentOptions = null;
        private static int selectedIndex = 0;
        private static bool isActive = false;
        private static bool givesColonistOrders = false;

        /// <summary>
        /// Gets whether the windowless menu is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the windowless menu with the given options.
        /// </summary>
        public static void Open(List<FloatMenuOption> options, bool colonistOrders)
        {
            currentOptions = options;
            selectedIndex = 0;
            isActive = true;
            givesColonistOrders = colonistOrders;

            // Find first enabled option
            for (int i = 0; i < options.Count; i++)
            {
                if (!options[i].Disabled)
                {
                    selectedIndex = i;
                    break;
                }
            }

            // Announce the first option
            if (selectedIndex >= 0 && selectedIndex < options.Count)
            {
                string optionText = options[selectedIndex].Label;
                if (options[selectedIndex].Disabled)
                {
                    optionText += " (unavailable)";
                }
                ClipboardHelper.CopyToClipboard(optionText);
            }
        }

        /// <summary>
        /// Closes the windowless menu.
        /// </summary>
        public static void Close()
        {
            currentOptions = null;
            selectedIndex = 0;
            isActive = false;
        }

        /// <summary>
        /// Moves selection to the next enabled option.
        /// </summary>
        public static void SelectNext()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            int startIndex = selectedIndex;
            int attempts = 0;

            do
            {
                selectedIndex = (selectedIndex + 1) % currentOptions.Count;
                attempts++;

                if (!currentOptions[selectedIndex].Disabled || attempts >= currentOptions.Count)
                    break;
            }
            while (selectedIndex != startIndex);

            // Announce the new selection
            if (selectedIndex >= 0 && selectedIndex < currentOptions.Count)
            {
                string optionText = currentOptions[selectedIndex].Label;
                if (currentOptions[selectedIndex].Disabled)
                {
                    optionText += " (unavailable)";
                }
                ClipboardHelper.CopyToClipboard(optionText);
            }
        }

        /// <summary>
        /// Moves selection to the previous enabled option.
        /// </summary>
        public static void SelectPrevious()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            int startIndex = selectedIndex;
            int attempts = 0;

            do
            {
                selectedIndex = (selectedIndex - 1 + currentOptions.Count) % currentOptions.Count;
                attempts++;

                if (!currentOptions[selectedIndex].Disabled || attempts >= currentOptions.Count)
                    break;
            }
            while (selectedIndex != startIndex);

            // Announce the new selection
            if (selectedIndex >= 0 && selectedIndex < currentOptions.Count)
            {
                string optionText = currentOptions[selectedIndex].Label;
                if (currentOptions[selectedIndex].Disabled)
                {
                    optionText += " (unavailable)";
                }
                ClipboardHelper.CopyToClipboard(optionText);
            }
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

            FloatMenuOption selectedOption = currentOptions[selectedIndex];

            if (selectedOption.Disabled)
            {
                ClipboardHelper.CopyToClipboard(selectedOption.Label + " - unavailable");
                return;
            }

            // Call the Chosen method to execute the option's action
            selectedOption.Chosen(givesColonistOrders, null);

            // Close the menu after execution
            Close();
        }
    }
}
