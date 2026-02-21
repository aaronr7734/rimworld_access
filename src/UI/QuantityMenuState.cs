using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for the numeric quantity selection menu.
    /// Opens when pressing Enter on an item in the caravan formation screen.
    /// Supports typeahead to jump to any quantity by typing numbers.
    /// </summary>
    public static class QuantityMenuState
    {
        private static bool isActive = false;
        private static TransferableOneWay currentTransferable;
        private static int selectedQuantity = 1;
        private static List<int> quantityIncrements = new List<int>();

        /// <summary>
        /// Gets the current maximum quantity from the transferable.
        /// This is read dynamically to handle cases where items despawn or quantities change
        /// while the menu is open.
        /// </summary>
        private static int MaxQuantity => currentTransferable?.MaxCount ?? 0;
        private static int selectedIncrementIndex = 0;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private static Action<int> onConfirm;

        // Numeric buffer for multi-digit quantity input (separate from typeahead)
        private static string numericBuffer = "";
        private static float lastNumericInputTime = 0f;
        private const float NUMERIC_INPUT_TIMEOUT = 10.0f;

        /// <summary>
        /// Gets whether the quantity menu is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets the typeahead helper for input routing.
        /// </summary>
        public static TypeaheadSearchHelper Typeahead => typeahead;

        /// <summary>
        /// Gets whether there is active numeric input in progress.
        /// </summary>
        public static bool HasActiveNumericInput => !string.IsNullOrEmpty(numericBuffer);

        /// <summary>
        /// Opens the quantity menu for the specified transferable.
        /// </summary>
        /// <param name="transferable">The item to adjust quantity for</param>
        /// <param name="callback">Called when user confirms with the new quantity</param>
        public static void Open(TransferableOneWay transferable, Action<int> callback)
        {
            if (transferable == null)
            {
                TolkHelper.Speak("No item selected");
                return;
            }

            currentTransferable = transferable;
            onConfirm = callback;

            // Generate smart increments based on current max
            // Note: Increments are generated once on open - the MaxQuantity property
            // is used dynamically for clamping to handle items that despawn while menu is open
            quantityIncrements = GenerateIncrements(MaxQuantity);

            // Start at current count, or 1 if nothing selected
            int initialQty = transferable.CountToTransfer > 0 ? transferable.CountToTransfer : 1;
            selectedQuantity = initialQty;

            // Find closest increment to initial quantity
            selectedIncrementIndex = FindClosestIncrementIndex(initialQty);

            isActive = true;
            typeahead.ClearSearch();
            numericBuffer = "";
            SoundDefOf.Click.PlayOneShotOnCamera();

            string itemName = transferable.LabelCap.StripTags();
            TolkHelper.Speak($"Quantity menu for {itemName}. Use Up/Down to adjust, type a number, or press Enter to confirm.");
            AnnounceCurrentQuantity();
        }

        /// <summary>
        /// Closes the quantity menu without applying changes.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentTransferable = null;
            onConfirm = null;
            numericBuffer = "";
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Confirms the selection and applies the quantity.
        /// </summary>
        public static void Confirm()
        {
            if (!isActive || currentTransferable == null)
                return;

            int finalQuantity = selectedQuantity;
            var callback = onConfirm;

            string itemName = currentTransferable.LabelCap.StripTags();
            TolkHelper.Speak($"{finalQuantity} {itemName} selected");

            Close();

            // Invoke callback after closing
            callback?.Invoke(finalQuantity);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Cancels the quantity selection.
        /// </summary>
        public static void Cancel()
        {
            TolkHelper.Speak("Quantity selection cancelled");
            Close();
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Moves to the next increment (up arrow increases quantity).
        /// </summary>
        public static void SelectNext()
        {
            if (!isActive || quantityIncrements.Count == 0)
                return;

            // If typeahead is active, navigate through matches
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                var labels = GetQuantityLabels();
                int newIndex = typeahead.GetNextMatch(selectedIncrementIndex);
                if (newIndex >= 0 && newIndex < quantityIncrements.Count)
                {
                    selectedIncrementIndex = newIndex;
                    selectedQuantity = quantityIncrements[newIndex];
                    AnnounceWithSearch();
                }
                return;
            }

            // Move to next increment
            if (selectedIncrementIndex < quantityIncrements.Count - 1)
            {
                selectedIncrementIndex++;
                selectedQuantity = quantityIncrements[selectedIncrementIndex];
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentQuantity();
            }
            else
            {
                // Already at max
                TolkHelper.Speak("Maximum quantity");
            }
        }

        /// <summary>
        /// Moves to the previous increment (down arrow decreases quantity).
        /// </summary>
        public static void SelectPrevious()
        {
            if (!isActive || quantityIncrements.Count == 0)
                return;

            // If typeahead is active, navigate through matches
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                var labels = GetQuantityLabels();
                int newIndex = typeahead.GetPreviousMatch(selectedIncrementIndex);
                if (newIndex >= 0 && newIndex < quantityIncrements.Count)
                {
                    selectedIncrementIndex = newIndex;
                    selectedQuantity = quantityIncrements[newIndex];
                    AnnounceWithSearch();
                }
                return;
            }

            // Move to previous increment
            if (selectedIncrementIndex > 0)
            {
                selectedIncrementIndex--;
                selectedQuantity = quantityIncrements[selectedIncrementIndex];
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentQuantity();
            }
            else
            {
                // Already at min
                TolkHelper.Speak("Minimum quantity");
            }
        }

        /// <summary>
        /// Jumps to the maximum quantity (Home key).
        /// </summary>
        public static void JumpToMax()
        {
            if (!isActive || quantityIncrements.Count == 0)
                return;

            selectedIncrementIndex = quantityIncrements.Count - 1;
            selectedQuantity = quantityIncrements[selectedIncrementIndex];
            typeahead.ClearSearch();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentQuantity();
        }

        /// <summary>
        /// Jumps to the minimum quantity (End key).
        /// </summary>
        public static void JumpToMin()
        {
            if (!isActive || quantityIncrements.Count == 0)
                return;

            selectedIncrementIndex = 0;
            selectedQuantity = quantityIncrements[0];
            typeahead.ClearSearch();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentQuantity();
        }

        /// <summary>
        /// Handles typeahead character input.
        /// Uses a dedicated numeric buffer for multi-digit number entry.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive)
                return;

            float currentTime = Time.realtimeSinceStartup;

            // Check timeout for numeric buffer - reset if too much time has passed
            if (currentTime - lastNumericInputTime > NUMERIC_INPUT_TIMEOUT)
            {
                numericBuffer = "";
            }
            lastNumericInputTime = currentTime;

            // Build numeric buffer directly (not through typeahead helper)
            numericBuffer += c;

            // Try to parse as a number
            if (int.TryParse(numericBuffer, out int targetQty))
            {
                // Clamp to valid range (0 to max)
                targetQty = Mathf.Clamp(targetQty, 0, MaxQuantity);
                selectedQuantity = targetQty;

                // Find closest increment for display purposes
                selectedIncrementIndex = FindClosestIncrementIndex(targetQty);

                AnnounceWithBuffer();
            }
            else
            {
                // Invalid number - shouldn't happen since we only accept digit chars
                TolkHelper.Speak($"Invalid number: {numericBuffer}");
                numericBuffer = "";
            }
        }

        /// <summary>
        /// Handles backspace in numeric input mode.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!isActive || string.IsNullOrEmpty(numericBuffer))
                return;

            // Remove the last character from the buffer
            numericBuffer = numericBuffer.Substring(0, numericBuffer.Length - 1);

            // If buffer is now empty, stay at current quantity
            if (string.IsNullOrEmpty(numericBuffer))
            {
                AnnounceCurrentQuantity();
            }
            else
            {
                // Re-parse the remaining buffer
                if (int.TryParse(numericBuffer, out int targetQty))
                {
                    targetQty = Mathf.Clamp(targetQty, 0, MaxQuantity);
                    selectedQuantity = targetQty;
                    selectedIncrementIndex = FindClosestIncrementIndex(targetQty);
                }
                AnnounceWithBuffer();
            }
        }

        /// <summary>
        /// Generates smart increments for the given maximum quantity.
        /// </summary>
        private static List<int> GenerateIncrements(int max)
        {
            var increments = new List<int> { 0, 1 };
            int[] steps = { 2, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000 };

            foreach (int step in steps)
            {
                if (step < max && !increments.Contains(step))
                {
                    increments.Add(step);
                }
            }

            // Always include max
            if (!increments.Contains(max))
            {
                increments.Add(max);
            }

            increments.Sort();
            return increments;
        }

        /// <summary>
        /// Finds the closest increment index for a given quantity.
        /// </summary>
        private static int FindClosestIncrementIndex(int quantity)
        {
            if (quantityIncrements.Count == 0)
                return 0;

            // If exact match exists, use it
            int exactIndex = quantityIncrements.IndexOf(quantity);
            if (exactIndex >= 0)
                return exactIndex;

            // Find closest
            int closestIndex = 0;
            int closestDiff = int.MaxValue;

            for (int i = 0; i < quantityIncrements.Count; i++)
            {
                int diff = Math.Abs(quantityIncrements[i] - quantity);
                if (diff < closestDiff)
                {
                    closestDiff = diff;
                    closestIndex = i;
                }
            }

            return closestIndex;
        }

        /// <summary>
        /// Gets labels for typeahead search (just the numbers as strings).
        /// </summary>
        private static List<string> GetQuantityLabels()
        {
            var labels = new List<string>();
            foreach (int qty in quantityIncrements)
            {
                labels.Add(qty.ToString());
            }
            return labels;
        }

        /// <summary>
        /// Announces the current quantity with mass and nutrition info.
        /// </summary>
        private static void AnnounceCurrentQuantity()
        {
            if (currentTransferable == null)
                return;

            string announcement = BuildQuantityAnnouncement(selectedQuantity);
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Announces with search context (for typeahead-based navigation).
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (currentTransferable == null)
                return;

            StringBuilder sb = new StringBuilder();
            sb.Append(BuildQuantityAnnouncement(selectedQuantity));

            if (typeahead.HasActiveSearch)
            {
                sb.Append($" Typing: {typeahead.SearchBuffer}");
            }

            TolkHelper.Speak(sb.ToString());
        }

        /// <summary>
        /// Announces with numeric buffer context (for direct number entry).
        /// Does not include position since user is typing a custom quantity.
        /// </summary>
        private static void AnnounceWithBuffer()
        {
            if (currentTransferable == null)
                return;

            StringBuilder sb = new StringBuilder();

            // Build announcement without position (user is typing custom quantity)
            sb.Append(selectedQuantity.ToString());

            // Mass
            float massPerItem = currentTransferable.ThingDef.BaseMass;
            float totalMass = massPerItem * selectedQuantity;
            sb.Append($". Mass: {totalMass:F2} kilograms");

            // Nutrition (for food items)
            if (currentTransferable.ThingDef.IsNutritionGivingIngestible)
            {
                float nutritionPerItem = currentTransferable.ThingDef.ingestible.CachedNutrition;
                float totalNutrition = nutritionPerItem * selectedQuantity;
                sb.Append($". Nutrition: {totalNutrition:F2}");
            }

            if (!string.IsNullOrEmpty(numericBuffer))
            {
                sb.Append($". Typing: {numericBuffer}");
            }

            TolkHelper.Speak(sb.ToString());
        }

        /// <summary>
        /// Builds the announcement string for a quantity.
        /// Format: "5. Mass: 2.5 kilograms. Nutrition: 0.25. Position 3 of 8"
        /// </summary>
        private static string BuildQuantityAnnouncement(int quantity)
        {
            StringBuilder sb = new StringBuilder();

            // Quantity
            sb.Append(quantity.ToString());

            // Mass
            float massPerItem = currentTransferable.ThingDef.BaseMass;
            float totalMass = massPerItem * quantity;
            sb.Append($". Mass: {totalMass:F2} kilograms");

            // Nutrition (for food items)
            if (currentTransferable.ThingDef.IsNutritionGivingIngestible)
            {
                float nutritionPerItem = currentTransferable.ThingDef.ingestible.CachedNutrition;
                float totalNutrition = nutritionPerItem * quantity;
                sb.Append($". Nutrition: {totalNutrition:F2}");
            }

            // Position in increments list
            int position = selectedIncrementIndex + 1;
            int total = quantityIncrements.Count;
            sb.Append($". Position {position} of {total}");

            return sb.ToString();
        }

        /// <summary>
        /// Handles keyboard input for the quantity menu.
        /// Returns true if input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive)
                return false;

            // Escape - cancel
            if (key == KeyCode.Escape)
            {
                Cancel();
                return true;
            }

            // Enter - confirm
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                Confirm();
                return true;
            }

            // Up arrow - increase quantity
            if (key == KeyCode.UpArrow && !shift && !ctrl && !alt)
            {
                SelectNext();
                return true;
            }

            // Down arrow - decrease quantity
            if (key == KeyCode.DownArrow && !shift && !ctrl && !alt)
            {
                SelectPrevious();
                return true;
            }

            // Home - jump to min
            if (key == KeyCode.Home)
            {
                JumpToMin();
                return true;
            }

            // End - jump to max
            if (key == KeyCode.End)
            {
                JumpToMax();
                return true;
            }

            // Backspace - handle numeric input
            if (key == KeyCode.Backspace && HasActiveNumericInput)
            {
                HandleBackspace();
                return true;
            }

            // Number keys for typeahead
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;
            bool isKeypadNumber = key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9;

            if ((isNumber || isKeypadNumber) && !alt && !ctrl)
            {
                char c;
                if (isNumber)
                    c = (char)('0' + (key - KeyCode.Alpha0));
                else
                    c = (char)('0' + (key - KeyCode.Keypad0));

                HandleTypeahead(c);
                return true;
            }

            return false;
        }
    }
}
