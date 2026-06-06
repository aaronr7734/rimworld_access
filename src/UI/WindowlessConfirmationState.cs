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
        private static Action onCancel = null;

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens a confirmation prompt.
        /// When cancelAction is provided, Escape runs that callback instead of the default
        /// pause-menu reopen behavior — use this for contextual confirmations (e.g. slave
        /// execute warning) where returning to the pause menu would be disruptive.
        /// </summary>
        public static void Open(string confirmationMessage, Action confirmAction, Action cancelAction = null)
        {
            isActive = true;
            message = confirmationMessage.StripTags();
            onConfirm = confirmAction;
            onCancel = cancelAction;

            // Announce the confirmation prompt
            TolkHelper.Speak("RimWorldAccess.UI.Confirm.MessageWithInstructions".Loc(message));
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

            actionToExecute?.Invoke();
        }

        /// <summary>
        /// Cancels the confirmation. Runs the contextual cancel callback when provided;
        /// otherwise falls back to the default "Cancelled" announcement + pause-menu reopen.
        /// </summary>
        public static void Cancel()
        {
            if (!isActive)
                return;

            Action cancelCallback = onCancel;
            Close();
            if (cancelCallback != null)
            {
                cancelCallback();
                return;
            }

            TolkHelper.Speak("RimWorldAccess.UI.Cancelled".Loc());

            if (Current.ProgramState == ProgramState.Playing)
            {
                WindowlessPauseMenuState.Open();
            }
        }

        /// <summary>
        /// Closes the confirmation state.
        /// </summary>
        private static void Close()
        {
            isActive = false;
            message = "";
            onConfirm = null;
            onCancel = null;
        }
    }
}
