using System;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the trade confirmation dialog that shows trade summary before executing.
    /// Shows what you get vs what you lose, with options to confirm or cancel.
    /// </summary>
    public static class TradeConfirmationState
    {
        private static bool isActive = false;
        private static string tradeSummary = "";
        private static Action onConfirm = null;

        /// <summary>
        /// Gets whether the trade confirmation dialog is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the trade confirmation dialog with a summary of the proposed trade.
        /// </summary>
        /// <param name="summary">The trade summary showing items gained/lost</param>
        /// <param name="confirmAction">Action to execute if user confirms</param>
        public static void Open(string summary, Action confirmAction)
        {
            isActive = true;
            tradeSummary = summary;
            onConfirm = confirmAction;

            // Announce the trade summary with instructions
            string announcement = tradeSummary + "\n\nPress Enter to confirm trade, Escape to cancel and return to trading";
            TolkHelper.Speak(announcement);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Confirms the trade and executes it.
        /// </summary>
        public static void Confirm()
        {
            if (!isActive)
                return;

            Action actionToExecute = onConfirm;
            Close();

            // Execute the confirmed trade
            actionToExecute?.Invoke();
        }

        /// <summary>
        /// Cancels the trade confirmation and returns to the trade menu.
        /// </summary>
        public static void Cancel()
        {
            if (!isActive)
                return;

            Close();
            TolkHelper.Speak("Trade cancelled - returned to trading menu");
            SoundDefOf.Click.PlayOneShotOnCamera();

            // Announce current selection in trade menu
            if (TradeNavigationState.IsActive)
            {
                // The trade navigation state is still active, just announce current item
                // This will be handled by the state itself when it regains focus
            }
        }

        /// <summary>
        /// Closes the confirmation dialog.
        /// </summary>
        private static void Close()
        {
            isActive = false;
            tradeSummary = "";
            onConfirm = null;
        }
    }
}
