using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for Quality Builder quality selection menu.
    /// Provides an accessible alternative to the native FloatMenu.
    /// </summary>
    public static class QualityBuilderMenuState
    {
        private static bool isActive = false;
        private static Thing targetThing = null;
        private static int selectedIndex = 0;
        private static List<int> qualityLevels = new List<int> { 2, 3, 4, 5, 6, 7 }; // Poor, Normal, Good, Excellent, Masterwork, Legendary

        // Store the original quality before changes for undo/escape
        private static int? originalQuality = null;

        public static bool IsActive => isActive;
        public static Thing TargetThing => targetThing;

        /// <summary>
        /// Opens the quality selection menu for a specific thing (blueprint/frame).
        /// Returns false if mod is not active or thing cannot have quality designations.
        /// </summary>
        public static bool Open(Thing thing)
        {
            if (!ModDetectionHelper.IsQualityBuilderActive() ||
                !ModDetectionHelper.CanHaveQualityDesignation(thing))
            {
                Log.Message($"[QualityBuilderMenuState] Open failed: mod active={ModDetectionHelper.IsQualityBuilderActive()}, can have designation={ModDetectionHelper.CanHaveQualityDesignation(thing)}");
                return false;
            }

            targetThing = thing;
            isActive = true;
            Log.Message($"[QualityBuilderMenuState] Menu opened for thing {thing}, isActive={isActive}");

            // Get current quality to restore on Escape
            originalQuality = ModDetectionHelper.GetCurrentQualityCategory(thing);
            Log.Message($"[QualityBuilderMenuState] Original quality: {originalQuality}");

            // Set selection to current quality if exists, otherwise to Normal (index 1)
            if (originalQuality.HasValue)
            {
                int index = qualityLevels.IndexOf(originalQuality.Value);
                selectedIndex = index >= 0 ? index : 1; // Default to Normal
            }
            else
            {
                selectedIndex = 1; // Normal quality
            }
            Log.Message($"[QualityBuilderMenuState] Selected index: {selectedIndex}");

            AnnounceCurrentSelection();
            return true;
        }

        /// <summary>
        /// Opens the quality selection menu for the thing at the current cursor position.
        /// </summary>
        public static bool OpenForCursor()
        {
            if (!ModDetectionHelper.IsQualityBuilderActive())
                return false;

            var map = Find.CurrentMap;
            if (map == null)
                return false;

            var cursorPos = MapNavigationState.CurrentCursorPosition;
            var thing = map.thingGrid.ThingAt(cursorPos, ThingCategory.Building);

            if (thing == null || !ModDetectionHelper.CanHaveQualityDesignation(thing))
            {
                TolkHelper.Speak("No blueprint or frame at cursor position", SpeechPriority.Normal);
                return false;
            }

            return Open(thing);
        }

        /// <summary>
        /// Closes the quality selection menu.
        /// </summary>
        public static void Close()
        {
            Log.Message($"[QualityBuilderMenuState] Close called, was active={isActive}");
            isActive = false;
            targetThing = null;
            selectedIndex = 0;
            originalQuality = null;
        }

        /// <summary>
        /// Moves selection to the next quality level.
        /// </summary>
        public static void SelectNext()
        {
            if (!isActive) return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, qualityLevels.Count);
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Moves selection to the previous quality level.
        /// </summary>
        public static void SelectPrevious()
        {
            if (!isActive) return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, qualityLevels.Count);
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Selects a specific quality level by index (0-5).
        /// </summary>
        public static void SelectIndex(int index)
        {
            if (!isActive) return;

            if (index >= 0 && index < qualityLevels.Count)
            {
                selectedIndex = index;
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Selects a quality level by numeric key (1-7).
        /// 1 = Remove designation, 2-7 = Poor-Legendary
        /// </summary>
        public static void SelectByNumberKey(int number)
        {
            if (!isActive) return;

            if (number == 1)
            {
                // Remove quality designation
                RemoveQuality();
                return;
            }

            // Map 2-7 to indices 0-5
            int index = number - 2;
            if (index >= 0 && index < qualityLevels.Count)
            {
                SelectIndex(index);
                ExecuteSelected();
            }
        }

        /// <summary>
        /// Executes the currently selected quality level.
        /// Applies the quality designation to the target thing.
        /// </summary>
        public static void ExecuteSelected()
        {
            if (!isActive || targetThing == null) return;

            int qualityCategory = qualityLevels[selectedIndex];
            bool success = ModDetectionHelper.SetQualityDesignation(targetThing, qualityCategory);

            if (success)
            {
                string announcement = ModDetectionHelper.GetQualityAnnouncement(qualityCategory);
                TolkHelper.Speak($"Quality set: {announcement}", SpeechPriority.Normal);

                // Keep menu open for further adjustments
                originalQuality = qualityCategory;
            }
            else
            {
                TolkHelper.Speak("Failed to set quality designation", SpeechPriority.High);
            }
        }

        /// <summary>
        /// Removes the quality designation from the target thing.
        /// </summary>
        public static void RemoveQuality()
        {
            if (!isActive || targetThing == null) return;

            bool success = ModDetectionHelper.SetQualityDesignation(targetThing, null);

            if (success)
            {
                TolkHelper.Speak("Quality requirement removed", SpeechPriority.Normal);
                originalQuality = null;

                // Keep menu open for new selection
                selectedIndex = 1; // Reset to Normal
            }
            else
            {
                TolkHelper.Speak("Failed to remove quality designation", SpeechPriority.High);
            }
        }

        /// <summary>
        /// Cancels changes and closes the menu.
        /// Restores original quality if it was changed during this session.
        /// </summary>
        public static void Cancel()
        {
            if (!isActive) return;

            // Check if quality was changed during this session
            int? currentQuality = ModDetectionHelper.GetCurrentQualityCategory(targetThing);
            if (originalQuality != currentQuality)
            {
                // Restore original quality
                ModDetectionHelper.SetQualityDesignation(targetThing, originalQuality);

                if (originalQuality.HasValue)
                {
                    string label = ModDetectionHelper.GetQualityLabel(originalQuality.Value);
                    TolkHelper.Speak($"Changes cancelled. Quality restored to {label}", SpeechPriority.Normal);
                }
                else
                {
                    TolkHelper.Speak("Changes cancelled. Quality requirement removed", SpeechPriority.Normal);
                }
            }
            else
            {
                TolkHelper.Speak("Quality selection cancelled", SpeechPriority.Normal);
            }

            Close();
        }

        /// <summary>
        /// Gets the current selected quality level.
        /// </summary>
        public static int GetSelectedQuality()
        {
            if (!isActive || selectedIndex < 0 || selectedIndex >= qualityLevels.Count)
                return 3; // Default to Normal

            return qualityLevels[selectedIndex];
        }

        /// <summary>
        /// Gets the announcement for the current selection.
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            if (!isActive) return;

            int quality = qualityLevels[selectedIndex];
            string label = ModDetectionHelper.GetQualityLabel(quality);
            string description = ModDetectionHelper.GetQualityDescription(quality);
            string position = MenuHelper.FormatPosition(selectedIndex, qualityLevels.Count);

            string announcement = $"{label}. {description}";
            if (!string.IsNullOrEmpty(position))
                announcement += $" {position}";

            announcement += " Press Enter to confirm, Escape to cancel, 1 to remove quality.";

            TolkHelper.Speak(announcement, SpeechPriority.Low);
        }

        /// <summary>
        /// Handles keyboard input for the quality selection menu.
        /// Returns true if input was handled.
        /// </summary>
        public static bool HandleInput()
        {
            Log.Message($"[QualityBuilderMenuState] HandleInput entry, isActive={isActive}, key={Event.current.keyCode}");

            if (!isActive)
            {
                Log.Message($"[QualityBuilderMenuState] HandleInput: menu not active, returning false");
                return false;
            }
            if (Event.current.type != EventType.KeyDown)
            {
                Log.Message($"[QualityBuilderMenuState] HandleInput: not KeyDown event, returning false");
                return false;
            }

            var key = Event.current.keyCode;
            Log.Message($"[QualityBuilderMenuState] HandleInput processing key={key}");

            bool handled = false;

            // Handle Escape - cancel
            if (key == KeyCode.Escape)
            {
                Log.Message($"[QualityBuilderMenuState] HandleInput: Escape pressed, calling Cancel");
                Cancel();
                handled = true;
            }
            // Handle Enter/Return - execute selection
            else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                Log.Message($"[QualityBuilderMenuState] HandleInput: Enter pressed, calling ExecuteSelected");
                ExecuteSelected();
                handled = true;
            }
            // Handle Up/Down arrows - navigation
            else if (key == KeyCode.UpArrow)
            {
                Log.Message($"[QualityBuilderMenuState] HandleInput: UpArrow pressed, calling SelectPrevious");
                SelectPrevious();
                handled = true;
            }
            else if (key == KeyCode.DownArrow)
            {
                Log.Message($"[QualityBuilderMenuState] HandleInput: DownArrow pressed, calling SelectNext");
                SelectNext();
                handled = true;
            }
            // Handle number keys 1-7 for quick selection
            else if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha7)
            {
                int number = (int)(key - KeyCode.Alpha1) + 1;
                Log.Message($"[QualityBuilderMenuState] HandleInput: Number key {number} pressed, calling SelectByNumberKey");
                SelectByNumberKey(number);
                handled = true;
            }
            // Handle keypad numbers 1-7
            else if (key >= KeyCode.Keypad1 && key <= KeyCode.Keypad7)
            {
                int number = (int)(key - KeyCode.Keypad1) + 1;
                Log.Message($"[QualityBuilderMenuState] HandleInput: Keypad number {number} pressed, calling SelectByNumberKey");
                SelectByNumberKey(number);
                handled = true;
            }
            // Handle Home - jump to first (Poor)
            else if (key == KeyCode.Home)
            {
                Log.Message($"[QualityBuilderMenuState] HandleInput: Home pressed, jumping to first");
                SelectIndex(0);
                handled = true;
            }
            // Handle End - jump to last (Legendary)
            else if (key == KeyCode.End)
            {
                Log.Message($"[QualityBuilderMenuState] HandleInput: End pressed, jumping to last");
                SelectIndex(qualityLevels.Count - 1);
                handled = true;
            }
            else
            {
                Log.Message($"[QualityBuilderMenuState] HandleInput: Key {key} not specifically handled");
            }

            // If menu is active, consume ALL key events to prevent them from leaking to the game
            if (isActive)
            {
                Log.Message($"[QualityBuilderMenuState] HandleInput: Menu active, consuming event and returning true");
                Event.current.Use();
                // Return true even for unhandled keys to block them
                return true;
            }

            Log.Message($"[QualityBuilderMenuState] HandleInput: Menu not active, returning handled={handled}");
            return handled;
        }
    }
}
