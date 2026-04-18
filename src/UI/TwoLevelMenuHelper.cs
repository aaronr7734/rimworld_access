using System;
using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// Information about a button in a detail view.
    /// </summary>
    public class ButtonInfo
    {
        /// <summary>
        /// The display label for the button.
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// The action to execute when the button is activated.
        /// </summary>
        public Action Action { get; set; }

        /// <summary>
        /// Whether the button is currently disabled.
        /// </summary>
        public bool IsDisabled { get; set; }

        /// <summary>
        /// Optional reason why the button is disabled.
        /// </summary>
        public string DisabledReason { get; set; }
    }

    /// <summary>
    /// Shared helper for two-level menu navigation (list view and detail view).
    /// Used by NotificationMenuState and HistoryMessagesState via composition.
    ///
    /// Two-level navigation pattern:
    /// - Level 1 (List View): Up/Down navigates items, Enter opens detail view
    /// - Level 2 (Detail View): Up/Down navigates header -> content lines -> buttons,
    ///   Left/Right navigates between buttons, Enter activates button
    /// </summary>
    public class TwoLevelMenuHelper
    {
        // === State ===
        private bool isInDetailView = false;
        private int detailPosition = 0; // 0=header, 1-N=content lines, N+1+=buttons
        private int currentButtonIndex = 0;
        private readonly List<ButtonInfo> currentButtons = new List<ButtonInfo>();

        // === Delegates for data access ===
        private readonly Func<int> getContentLineCount;
        private readonly Action<List<ButtonInfo>> populateButtons;
        private readonly Func<string> getHeaderAnnouncement;
        private readonly Func<int, string> getContentLineAnnouncement;

        // === Customizable messages ===
        private readonly string endOfItemMessage;
        private readonly string startOfItemMessage;
        private readonly string openFirstMessage;
        private readonly string navigateDownMessage;
        private readonly string noButtonsMessage;

        // === Properties ===

        /// <summary>
        /// Gets whether we are currently in detail view.
        /// </summary>
        public bool IsInDetailView => isInDetailView;

        /// <summary>
        /// Gets whether the current position is in the buttons section.
        /// </summary>
        public bool IsInButtonsSection => isInDetailView && IsPositionInButtonsSection();

        /// <summary>
        /// Gets the current detail position (0=header, 1-N=content lines, N+1+=buttons).
        /// </summary>
        public int DetailPosition => detailPosition;

        /// <summary>
        /// Gets the current button index within the buttons section.
        /// </summary>
        public int CurrentButtonIndex => currentButtonIndex;

        /// <summary>
        /// Gets the number of buttons available.
        /// </summary>
        public int ButtonCount => currentButtons.Count;

        /// <summary>
        /// Gets the current buttons list (read-only).
        /// </summary>
        public IReadOnlyList<ButtonInfo> CurrentButtons => currentButtons;

        // === Constructor ===

        /// <summary>
        /// Creates a new TwoLevelMenuHelper with the specified data access delegates.
        /// </summary>
        /// <param name="getContentLineCount">Returns the number of content lines for the current item</param>
        /// <param name="populateButtons">Fills the button list for the current item</param>
        /// <param name="getHeaderAnnouncement">Returns the header text (e.g., "Letter: Raid")</param>
        /// <param name="getContentLineAnnouncement">Returns the content line at the given index</param>
        /// <param name="endOfItemMessage">Message to speak when at the last position (default: "End of letter")</param>
        /// <param name="startOfItemMessage">Message to speak when at the first position (default: "Start of letter")</param>
        /// <param name="openFirstMessage">Message when attempting button nav before entering detail view (default: "Press Enter to open letter first")</param>
        /// <param name="navigateDownMessage">Message when attempting button nav outside the button section (default: "Navigate down to buttons first")</param>
        /// <param name="noButtonsMessage">Message when the current item has no buttons (default: "No buttons available")</param>
        public TwoLevelMenuHelper(
            Func<int> getContentLineCount,
            Action<List<ButtonInfo>> populateButtons,
            Func<string> getHeaderAnnouncement,
            Func<int, string> getContentLineAnnouncement,
            string endOfItemMessage = "End of letter",
            string startOfItemMessage = "Start of letter",
            string openFirstMessage = "Press Enter to open letter first",
            string navigateDownMessage = "Navigate down to buttons first",
            string noButtonsMessage = "No buttons available")
        {
            this.getContentLineCount = getContentLineCount ?? throw new ArgumentNullException(nameof(getContentLineCount));
            this.populateButtons = populateButtons ?? throw new ArgumentNullException(nameof(populateButtons));
            this.getHeaderAnnouncement = getHeaderAnnouncement ?? throw new ArgumentNullException(nameof(getHeaderAnnouncement));
            this.getContentLineAnnouncement = getContentLineAnnouncement ?? throw new ArgumentNullException(nameof(getContentLineAnnouncement));
            this.endOfItemMessage = endOfItemMessage;
            this.startOfItemMessage = startOfItemMessage;
            this.openFirstMessage = openFirstMessage;
            this.navigateDownMessage = navigateDownMessage;
            this.noButtonsMessage = noButtonsMessage;
        }

        // === Public Methods ===

        /// <summary>
        /// Enters detail view, resetting position to 0 (header).
        /// </summary>
        public void EnterDetailView()
        {
            isInDetailView = true;
            detailPosition = 0;
            currentButtonIndex = 0;
        }

        /// <summary>
        /// Attempts to go back from detail view to list view.
        /// </summary>
        /// <returns>True if was in detail view and handled, false if already in list view</returns>
        public bool GoBackToList()
        {
            if (!isInDetailView)
            {
                return false;
            }

            isInDetailView = false;
            detailPosition = 0;
            return true;
        }

        /// <summary>
        /// Refreshes the buttons list by calling the populateButtons delegate.
        /// </summary>
        public void RefreshButtons()
        {
            currentButtons.Clear();
            currentButtonIndex = 0;
            populateButtons(currentButtons);
        }

        /// <summary>
        /// Moves to the next position in detail view.
        /// Navigates through header, content lines, then to first button.
        /// Once in buttons section, re-announces current button - use Left/Right to navigate buttons.
        /// </summary>
        public void SelectNextDetailPosition()
        {
            // If already in buttons section, re-announce current button
            // User must use Left/Right to navigate between buttons
            if (IsPositionInButtonsSection())
            {
                AnnounceCurrentButton();
                return;
            }

            int firstButtonPos = GetFirstButtonPosition();

            // If we're not in buttons section, we can move to next position (up to first button)
            if (detailPosition < firstButtonPos)
            {
                detailPosition++;

                // If we just entered buttons section, set button index
                if (IsPositionInButtonsSection())
                {
                    currentButtonIndex = 0;
                }

                AnnounceDetailPosition();
            }
            else
            {
                // No buttons available, we're at end of content - re-announce current position
                AnnounceDetailPosition();
            }
        }

        /// <summary>
        /// Moves to the previous position in detail view.
        /// If in buttons section, goes back to last content line.
        /// Navigates through content lines, then header.
        /// </summary>
        public void SelectPreviousDetailPosition()
        {
            // If in buttons section, jump back to last content line (exit buttons)
            if (IsPositionInButtonsSection())
            {
                int lineCount = getContentLineCount();
                detailPosition = lineCount; // Last content line (0=header, 1 to lineCount = content)
                currentButtonIndex = 0;

                // If no content lines, go to header
                if (lineCount == 0)
                {
                    detailPosition = 0;
                }

                AnnounceDetailPosition();
                return;
            }

            // Normal navigation through header and content lines
            if (detailPosition > 0)
            {
                detailPosition--;
                AnnounceDetailPosition();
            }
            else
            {
                // At the beginning - re-announce current position (header)
                AnnounceDetailPosition();
            }
        }

        /// <summary>
        /// Navigates to the next button.
        /// Wraps to first button if WrapNavigation setting is enabled.
        /// Only works when in buttons section of detail view.
        /// </summary>
        public void SelectNextButton()
        {
            if (!ValidateButtonNavigationState())
                return;

            if (currentButtonIndex < currentButtons.Count - 1)
            {
                currentButtonIndex++;
            }
            else if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                currentButtonIndex = 0;
            }
            // else: stay on current button and re-announce it

            // Update detail position to match button index
            detailPosition = GetFirstButtonPosition() + currentButtonIndex;
            AnnounceCurrentButton();
        }

        /// <summary>
        /// Navigates to the previous button.
        /// Wraps to last button if WrapNavigation setting is enabled.
        /// Only works when in buttons section of detail view.
        /// </summary>
        public void SelectPreviousButton()
        {
            if (!ValidateButtonNavigationState())
                return;

            if (currentButtonIndex > 0)
            {
                currentButtonIndex--;
            }
            else if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                currentButtonIndex = currentButtons.Count - 1;
            }
            // else: stay on current button and re-announce it

            // Update detail position to match button index
            detailPosition = GetFirstButtonPosition() + currentButtonIndex;
            AnnounceCurrentButton();
        }

        /// <summary>
        /// Activates the currently selected button.
        /// </summary>
        /// <returns>True if button was activated, false if disabled or invalid state</returns>
        public bool ActivateCurrentButton()
        {
            if (!ValidateButtonNavigationState())
                return false;

            ButtonInfo button = currentButtons[currentButtonIndex];

            if (button.IsDisabled)
            {
                string disabledMsg = string.IsNullOrEmpty(button.DisabledReason)
                    ? $"{button.Label} is disabled"
                    : $"{button.Label} is disabled: {button.DisabledReason}";
                TolkHelper.Speak(disabledMsg);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets the currently selected button, or null if not in buttons section.
        /// </summary>
        /// <returns>The current ButtonInfo or null</returns>
        public ButtonInfo GetCurrentButton()
        {
            if (!isInDetailView || !IsPositionInButtonsSection())
                return null;

            if (currentButtonIndex < 0 || currentButtonIndex >= currentButtons.Count)
                return null;

            return currentButtons[currentButtonIndex];
        }

        /// <summary>
        /// Jumps to the start of detail view (header position).
        /// </summary>
        public void JumpToDetailStart()
        {
            if (!isInDetailView)
            {
                TolkHelper.Speak(openFirstMessage);
                return;
            }

            detailPosition = 0;
            currentButtonIndex = 0;
            AnnounceDetailPosition();
        }

        /// <summary>
        /// Jumps to the end of detail view (buttons section, or last content line if no buttons).
        /// </summary>
        public void JumpToDetailEnd()
        {
            if (!isInDetailView)
            {
                TolkHelper.Speak(openFirstMessage);
                return;
            }

            // Jump to buttons section if available, otherwise to last content line
            if (currentButtons != null && currentButtons.Count > 0)
            {
                detailPosition = GetFirstButtonPosition();
                currentButtonIndex = 0;
            }
            else
            {
                // No buttons, go to last content line
                int lineCount = getContentLineCount();
                detailPosition = lineCount; // 0=header, so lineCount is last line position
                if (detailPosition < 0) detailPosition = 0;
            }

            AnnounceDetailPosition();
        }

        /// <summary>
        /// Announces the canonical "Back to list" message. Callers pass an optional
        /// suffix for context (e.g., "No lessons available").
        /// </summary>
        public static void SpeakReturnToList(string suffix = null)
        {
            string phrase = string.IsNullOrEmpty(suffix) ? "Back to list" : "Back to list. " + suffix;
            TolkHelper.Speak(phrase);
        }

        /// <summary>
        /// Fully resets all state (for menu close).
        /// </summary>
        public void Reset()
        {
            isInDetailView = false;
            detailPosition = 0;
            currentButtonIndex = 0;
            currentButtons.Clear();
        }

        /// <summary>
        /// Resets detail position only (for changing items in list view).
        /// Keeps button state intact.
        /// </summary>
        public void ResetDetailPosition()
        {
            detailPosition = 0;
            currentButtonIndex = 0;
        }

        /// <summary>
        /// Announces the current position in detail view (header, content line, or button).
        /// </summary>
        public void AnnounceDetailPosition()
        {
            int lineCount = getContentLineCount();

            if (detailPosition == 0)
            {
                // Header position
                string header = getHeaderAnnouncement();
                TolkHelper.Speak(header);
            }
            else if (detailPosition <= lineCount)
            {
                // Content line position (1-indexed into content lines)
                int lineIndex = detailPosition - 1;
                string line = getContentLineAnnouncement(lineIndex);
                if (!string.IsNullOrEmpty(line))
                {
                    TolkHelper.Speak(line);
                }
            }
            else if (IsPositionInButtonsSection())
            {
                // Button position
                AnnounceCurrentButton();
            }
        }

        // === Private Helper Methods ===

        /// <summary>
        /// Gets the detail position where buttons start (after header and content lines).
        /// Position 0 = header, 1 to N = content lines, N+1 = first button.
        /// </summary>
        private int GetFirstButtonPosition()
        {
            return 1 + getContentLineCount(); // 1 for header + line count
        }

        /// <summary>
        /// Gets the total number of positions in detail view (header + lines + buttons).
        /// </summary>
        private int GetTotalDetailPositions()
        {
            int buttonCount = currentButtons?.Count ?? 0;
            return 1 + getContentLineCount() + buttonCount; // header + lines + buttons
        }

        /// <summary>
        /// Checks if the current detail position is in the buttons section.
        /// </summary>
        private bool IsPositionInButtonsSection()
        {
            return detailPosition >= GetFirstButtonPosition() && currentButtons != null && currentButtons.Count > 0;
        }

        /// <summary>
        /// Validates that button navigation is allowed and announces errors if not.
        /// </summary>
        /// <returns>True if navigation is valid, false otherwise</returns>
        private bool ValidateButtonNavigationState()
        {
            if (!isInDetailView)
            {
                TolkHelper.Speak(openFirstMessage);
                return false;
            }

            if (!IsPositionInButtonsSection())
            {
                TolkHelper.Speak(navigateDownMessage);
                return false;
            }

            if (currentButtons == null || currentButtons.Count == 0)
            {
                TolkHelper.Speak(noButtonsMessage);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Announces the currently selected button.
        /// Format: "{Label}. Button. X of Y" or "{Label} (disabled). Button. X of Y"
        /// </summary>
        private void AnnounceCurrentButton()
        {
            if (currentButtons == null || currentButtons.Count == 0)
                return;

            if (currentButtonIndex < 0 || currentButtonIndex >= currentButtons.Count)
                return;

            ButtonInfo button = currentButtons[currentButtonIndex];
            string announcement = button.IsDisabled
                ? $"{button.Label} (disabled). Button. {MenuHelper.FormatPosition(currentButtonIndex, currentButtons.Count)}"
                : $"{button.Label}. Button. {MenuHelper.FormatPosition(currentButtonIndex, currentButtons.Count)}";

            TolkHelper.Speak(announcement);
        }
    }
}
