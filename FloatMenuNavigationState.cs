using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Maintains the state of keyboard navigation for FloatMenu instances.
    /// Tracks the currently open menu and selected option index for accessibility.
    /// </summary>
    public static class FloatMenuNavigationState
    {
        private static FloatMenu currentMenu = null;
        private static int selectedIndex = 0;
        private static string lastAnnouncedText = "";
        private static bool isTargetSelectionMode = false;

        /// <summary>
        /// Gets the currently tracked FloatMenu instance.
        /// </summary>
        public static FloatMenu CurrentMenu
        {
            get => currentMenu;
            set
            {
                currentMenu = value;
                if (value != null)
                {
                    selectedIndex = 0; // Reset to first option when new menu opens
                    lastAnnouncedText = "";
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected option index.
        /// </summary>
        public static int SelectedIndex
        {
            get => selectedIndex;
            set => selectedIndex = value;
        }

        /// <summary>
        /// Gets or sets the last announced option text to avoid repetition.
        /// </summary>
        public static string LastAnnouncedText
        {
            get => lastAnnouncedText;
            set => lastAnnouncedText = value;
        }

        /// <summary>
        /// Indicates whether we're in target selection mode (first stage of two-stage flow).
        /// </summary>
        public static bool IsTargetSelectionMode
        {
            get => isTargetSelectionMode;
            set => isTargetSelectionMode = value;
        }

        /// <summary>
        /// Checks if there is a currently tracked menu.
        /// </summary>
        public static bool HasActiveMenu => currentMenu != null;

        /// <summary>
        /// Moves selection to the next option, wrapping around to the start if at the end.
        /// Automatically skips disabled options.
        /// Returns the new selected index, or -1 if no valid options exist.
        /// </summary>
        public static int SelectNext(int optionCount, System.Func<int, bool> isOptionEnabled)
        {
            if (optionCount <= 0)
                return -1;

            int startIndex = selectedIndex;
            int attempts = 0;

            do
            {
                selectedIndex = (selectedIndex + 1) % optionCount;
                attempts++;

                // Check if this option is enabled, or if we've checked all options
                if (isOptionEnabled(selectedIndex) || attempts >= optionCount)
                    break;
            }
            while (selectedIndex != startIndex);

            // If we cycled through all options and none are enabled, stay at current
            if (attempts >= optionCount && !isOptionEnabled(selectedIndex))
            {
                return -1;
            }

            return selectedIndex;
        }

        /// <summary>
        /// Moves selection to the previous option, wrapping around to the end if at the start.
        /// Automatically skips disabled options.
        /// Returns the new selected index, or -1 if no valid options exist.
        /// </summary>
        public static int SelectPrevious(int optionCount, System.Func<int, bool> isOptionEnabled)
        {
            if (optionCount <= 0)
                return -1;

            int startIndex = selectedIndex;
            int attempts = 0;

            do
            {
                selectedIndex = (selectedIndex - 1 + optionCount) % optionCount;
                attempts++;

                // Check if this option is enabled, or if we've checked all options
                if (isOptionEnabled(selectedIndex) || attempts >= optionCount)
                    break;
            }
            while (selectedIndex != startIndex);

            // If we cycled through all options and none are enabled, stay at current
            if (attempts >= optionCount && !isOptionEnabled(selectedIndex))
            {
                return -1;
            }

            return selectedIndex;
        }

        /// <summary>
        /// Ensures the selected index is valid for the given option count.
        /// Clamps the index to valid range and skips disabled options if necessary.
        /// </summary>
        public static void ValidateSelection(int optionCount, System.Func<int, bool> isOptionEnabled)
        {
            if (optionCount <= 0)
            {
                selectedIndex = 0;
                return;
            }

            // Clamp to valid range
            if (selectedIndex >= optionCount)
            {
                selectedIndex = 0;
            }
            else if (selectedIndex < 0)
            {
                selectedIndex = 0;
            }

            // If current selection is disabled, find next enabled option
            if (!isOptionEnabled(selectedIndex))
            {
                int startIndex = selectedIndex;
                int attempts = 0;

                do
                {
                    selectedIndex = (selectedIndex + 1) % optionCount;
                    attempts++;

                    if (isOptionEnabled(selectedIndex) || attempts >= optionCount)
                        break;
                }
                while (selectedIndex != startIndex);
            }
        }

        /// <summary>
        /// Resets the navigation state (useful when menu closes).
        /// </summary>
        public static void Reset()
        {
            currentMenu = null;
            selectedIndex = 0;
            lastAnnouncedText = "";
            isTargetSelectionMode = false;
        }
    }
}
