using System;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a simple yes/no confirmation dialog without rendering UI.
    /// Used for confirming destructive actions like quitting.
    /// </summary>
    public static class WindowlessConfirmationState
    {
        private static bool isActive = false;
        private static string message = "";
        private static Action onConfirm = null;

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens a confirmation prompt.
        /// </summary>
        public static void Open(string confirmationMessage, Action confirmAction)
        {
            isActive = true;
            message = confirmationMessage.StripTags();
            onConfirm = confirmAction;

            // Announce the confirmation prompt
            TolkHelper.Speak(message + " - Press Enter to confirm, Escape to cancel");
        }

        /// <summary>
        /// Confirms and executes the action.
        /// </summary>
        public static void Confirm()
        {
            if (!isActive)
                return;

            Action actionToExecute = onConfirm;
            Close();

            // Execute the confirmed action
            actionToExecute?.Invoke();
        }

        /// <summary>
        /// Cancels the confirmation.
        /// </summary>
        public static void Cancel()
        {
            if (!isActive)
                return;

            Close();
            TolkHelper.Speak("Cancelled");

            // Reopen the pause menu
            WindowlessPauseMenuState.Open();
        }

        /// <summary>
        /// Closes the confirmation state.
        /// </summary>
        private static void Close()
        {
            isActive = false;
            message = "";
            onConfirm = null;
        }
    }
}
