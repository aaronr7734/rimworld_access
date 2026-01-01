using System;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages zone renaming with text input.
    /// Allows typing a new zone name with Enter to confirm and Escape to cancel.
    /// </summary>
    public static class ZoneRenameState
    {
        private static bool isActive = false;
        private static Zone currentZone = null;
        private static string currentText = "";
        private static string originalName = "";

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the rename dialog for the specified zone.
        /// </summary>
        public static void Open(Zone zone)
        {
            if (zone == null)
            {
                Log.Error("Cannot open rename dialog: zone is null");
                return;
            }

            currentZone = zone;
            originalName = zone.label;
            currentText = zone.label;
            isActive = true;

            TolkHelper.Speak($"Renaming {originalName}. Current name: {currentText}. Type new name and press Enter to confirm, Escape to cancel.");
            Log.Message($"Opened rename dialog for zone: {originalName}");
        }

        /// <summary>
        /// Closes the rename dialog without saving.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentZone = null;
            currentText = "";
            originalName = "";
        }

        /// <summary>
        /// Handles character input for text entry.
        /// </summary>
        public static void HandleCharacter(char character)
        {
            if (!isActive)
                return;

            // Add character to current text
            currentText += character;

            // Announce the character
            TolkHelper.Speak(character.ToString(), SpeechPriority.High);
        }

        /// <summary>
        /// Handles backspace key to delete last character.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!isActive || string.IsNullOrEmpty(currentText))
                return;

            // Remove last character
            if (currentText.Length > 0)
            {
                char removed = currentText[currentText.Length - 1];
                currentText = currentText.Substring(0, currentText.Length - 1);
                TolkHelper.Speak($"Deleted {removed}", SpeechPriority.High);
            }
        }

        /// <summary>
        /// Reads the current text.
        /// </summary>
        public static void ReadCurrentText()
        {
            if (!isActive)
                return;

            if (string.IsNullOrEmpty(currentText))
            {
                TolkHelper.Speak("Empty");
            }
            else
            {
                TolkHelper.Speak(currentText);
            }
        }

        /// <summary>
        /// Confirms the rename and applies the new name.
        /// </summary>
        public static void Confirm()
        {
            if (!isActive || currentZone == null)
                return;

            // Validate name
            if (string.IsNullOrWhiteSpace(currentText))
            {
                TolkHelper.Speak("Cannot set empty name. Enter a name or press Escape to cancel.", SpeechPriority.High);
                return;
            }

            try
            {
                // Set the new name
                currentZone.label = currentText;
                TolkHelper.Speak($"Renamed to {currentText}", SpeechPriority.High);
                Log.Message($"Renamed zone from '{originalName}' to '{currentText}'");
            }
            catch (Exception ex)
            {
                TolkHelper.Speak($"Error renaming zone: {ex.Message}", SpeechPriority.High);
                Log.Error($"Error renaming zone: {ex}");
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

            TolkHelper.Speak("Cancelled rename");
            Log.Message("Cancelled zone rename");
            Close();
        }
    }
}
