using System;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages pen marker renaming with text input.
    /// Follows the same pattern as ZoneRenameState.
    /// Uses TextInputHelper for shared text input logic.
    /// </summary>
    public static class PenRenameState
    {
        private static bool isActive = false;
        private static CompAnimalPenMarker currentMarker = null;
        private static string originalName = "";

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the rename dialog for the specified pen marker.
        /// </summary>
        public static void Open(CompAnimalPenMarker marker)
        {
            if (marker == null)
            {
                Log.Error("Cannot open rename dialog: pen marker is null");
                return;
            }

            currentMarker = marker;
            originalName = marker.RenamableLabel;
            TextInputHelper.SetText("");  // Start empty
            isActive = true;

            TolkHelper.Speak($"Renaming {originalName}. Type new name and press Enter, Escape to cancel.");
            Log.Message($"Opened rename dialog for pen marker: {originalName}");
        }

        /// <summary>
        /// Closes the rename dialog without saving.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentMarker = null;
            originalName = "";
            TextInputHelper.Clear();
        }

        /// <summary>
        /// Handles character input for text entry.
        /// </summary>
        public static void HandleCharacter(char character)
        {
            if (!isActive)
                return;

            TextInputHelper.HandleCharacter(character);
        }

        /// <summary>
        /// Handles backspace key to delete last character.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!isActive)
                return;

            TextInputHelper.HandleBackspace();
        }

        /// <summary>
        /// Reads the current text.
        /// </summary>
        public static void ReadCurrentText()
        {
            if (!isActive)
                return;

            TextInputHelper.ReadCurrentText();
        }

        /// <summary>
        /// Confirms the rename and applies the new name.
        /// </summary>
        public static void Confirm()
        {
            if (!isActive || currentMarker == null)
                return;

            string newName = TextInputHelper.CurrentText;

            // Validate name
            if (string.IsNullOrWhiteSpace(newName))
            {
                TolkHelper.Speak("Cannot set empty name. Enter a name or press Escape to cancel.", SpeechPriority.High);
                return;
            }

            try
            {
                currentMarker.RenamableLabel = newName;
                TolkHelper.Speak($"Renamed to {newName}", SpeechPriority.High);
                Log.Message($"Renamed pen marker from '{originalName}' to '{newName}'");
            }
            catch (Exception ex)
            {
                TolkHelper.Speak($"Error renaming pen marker: {ex.Message}", SpeechPriority.High);
                Log.Error($"Error renaming pen marker: {ex}");
            }
            finally
            {
                Close();
            }
        }

        /// <summary>
        /// Cancels the rename without saving.
        /// </summary>
        public static void Cancel()
        {
            if (!isActive)
                return;

            TolkHelper.Speak("Rename cancelled");
            Log.Message("Pen marker rename cancelled");
            Close();
        }
    }
}
