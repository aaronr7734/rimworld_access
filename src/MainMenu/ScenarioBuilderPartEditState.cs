using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State for editing individual part fields in the Scenario Builder.
    /// Handles dropdown selection and quantity editing.
    /// </summary>
    public static class ScenarioBuilderPartEditState
    {
        public static bool IsActive { get; private set; }

        private static ScenarioBuilderState.PartField currentField;
        private static Action onComplete;

        // Dropdown state
        private static List<(string label, object value)> dropdownOptions;
        private static int selectedOptionIndex = 0;
        private static TypeaheadSearchHelper dropdownTypeahead = new TypeaheadSearchHelper();

        // Quantity state
        private static int quantityValue;
        private static int quantityMin;
        private static int quantityMax;
        private static float floatQuantityValue;
        private static float floatQuantityMin;
        private static float floatQuantityMax;
        private static bool isFloatQuantity;
        private static bool isPercentQuantity; // True for 0-1 percentage values, false for raw float values
        private static bool isIntegerDisplay; // True for floats that should display without decimals (e.g., days)
        private static string quantityTypedBuffer = "";

        // Embedded text-edit controller (uses TextInputController without TextInputManager
        // registration so the surrounding navigation keys still flow to HandleInput).
        private static readonly TextInputController textController = new TextInputController();

        /// <summary>
        /// Opens the field editor for the given field.
        /// </summary>
        public static void Open(ScenarioBuilderState.PartField field, Action onCompleteCallback)
        {
            // Clear any stale state first
            currentField = null;
            onComplete = null;
            dropdownOptions = null;
            dropdownTypeahead.ClearSearch();
            IsActive = false;

            if (field.Type == ScenarioBuilderState.FieldType.Dropdown)
            {
                // Initialize dropdown
                var options = field.Data as List<(string label, object value)>;
                if (options == null || options.Count == 0)
                {
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.NoOptionsForField".Loc());
                    onCompleteCallback?.Invoke(); // Call callback so parent state refreshes
                    return;
                }

                currentField = field;
                onComplete = onCompleteCallback;
                dropdownOptions = options;

                // Find current selection
                selectedOptionIndex = 0;
                for (int i = 0; i < dropdownOptions.Count; i++)
                {
                    if (dropdownOptions[i].label == field.CurrentValue)
                    {
                        selectedOptionIndex = i;
                        break;
                    }
                }

                IsActive = true;
                AnnounceDropdownOption();
            }
            else if (field.Type == ScenarioBuilderState.FieldType.Quantity)
            {
                currentField = field;
                onComplete = onCompleteCallback;

                // Initialize quantity
                if (field.Data is int[] intRange)
                {
                    quantityMin = intRange[0];
                    quantityMax = intRange[1];
                    isFloatQuantity = false;
                    isIntegerDisplay = false; // Not applicable for int[] data

                    // Parse current value
                    string valueStr = field.CurrentValue.Replace("%", "").Trim();
                    if (int.TryParse(valueStr, out int parsed))
                        quantityValue = parsed;
                    else
                        quantityValue = quantityMin;
                }
                else if (field.Data is float[] floatRange)
                {
                    isFloatQuantity = true;
                    floatQuantityMin = floatRange[0];
                    floatQuantityMax = floatRange[1];

                    // Check if this is a percentage display field
                    // Use explicit flag if set, otherwise infer from range (0-1 = percentage)
                    isPercentQuantity = field.IsPercentDisplay || (floatRange[1] <= 1.0f);

                    // Check if this should display as integer (no decimals)
                    isIntegerDisplay = field.IsIntegerDisplay;

                    // Parse current value
                    string valueStr = field.CurrentValue.Replace("%", "").Trim();
                    if (float.TryParse(valueStr, out float parsed))
                    {
                        if (isPercentQuantity)
                            floatQuantityValue = parsed / 100f; // Convert from display percentage to stored value
                        else
                            floatQuantityValue = parsed; // Raw float value
                    }
                    else
                    {
                        floatQuantityValue = floatRange[0];
                    }
                }
                else
                {
                    // Default range
                    quantityMin = 1;
                    quantityMax = 100;
                    isFloatQuantity = false;
                    isIntegerDisplay = false;
                    quantityValue = 1;
                }

                quantityTypedBuffer = ""; // Clear any previous typed input
                IsActive = true;
                AnnounceQuantity();
            }
            else if (field.Type == ScenarioBuilderState.FieldType.Text)
            {
                currentField = field;
                onComplete = onCompleteCallback;

                string fullText = field.Data as string ?? "";
                var spec = new TextFieldSpec(
                    labelKey: "RimWorldAccess.TextInput.LabelDefault",
                    maxLength: null,
                    minLength: 0,
                    forbidGrammarSpecials: true,
                    multiLine: true);
                // Embedded — local input handlers route to controller below.
                textController.Begin(fullText, spec, _ => { }, null, replaceOnType: false, modal: false);

                IsActive = true;
                AnnounceText();
            }
            else if (field.Type == ScenarioBuilderState.FieldType.Checkbox)
            {
                currentField = field;
                onComplete = onCompleteCallback;

                // Checkbox is simple - just toggle and close. BoolValue is the
                // language-independent state; CurrentValue is only the display string.
                bool newValue = !field.BoolValue;

                field.BoolValue = newValue;
                field.CurrentValue = ScenarioBuilderState.BoolDisplay(newValue);
                field.SetValue?.Invoke(newValue);
                ScenarioBuilderState.SetDirty();

                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.CheckboxToggled".Loc(field.Name, newValue
                    ? (string)"RimWorldAccess.ScenarioBuilder.PartEdit.Checked".Translate()
                    : (string)"RimWorldAccess.ScenarioBuilder.PartEdit.Unchecked".Translate()));
                onCompleteCallback?.Invoke();
                return; // Don't set IsActive - immediate toggle
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.UnsupportedFieldType".Loc());
                onCompleteCallback?.Invoke();
            }
        }

        /// <summary>
        /// Closes the field editor.
        /// </summary>
        public static void Close(bool applyChanges)
        {
            if (applyChanges && currentField != null)
            {
                ApplyCurrentValue();
            }

            IsActive = false;
            currentField = null;
            dropdownOptions = null;
            dropdownTypeahead.ClearSearch();
            textController.Cancel();

            onComplete?.Invoke();
        }

        /// <summary>
        /// Applies the current value to the field.
        /// </summary>
        private static void ApplyCurrentValue()
        {
            if (currentField == null) return;

            if (currentField.Type == ScenarioBuilderState.FieldType.Dropdown && dropdownOptions != null)
            {
                var selected = dropdownOptions[selectedOptionIndex];
                currentField.SetValue?.Invoke(selected.value);
                currentField.CurrentValue = selected.label;
                ScenarioBuilderState.SetDirty();
            }
            else if (currentField.Type == ScenarioBuilderState.FieldType.Quantity)
            {
                if (isFloatQuantity)
                {
                    currentField.SetValue?.Invoke(floatQuantityValue);

                    // If GetValue is available, read back the actual stored value (may differ due to validation)
                    if (currentField.GetValue != null)
                    {
                        var actualValue = currentField.GetValue();
                        if (actualValue is float actualFloat)
                            floatQuantityValue = actualFloat;
                    }

                    // Use appropriate format based on field type
                    if (isPercentQuantity)
                        currentField.CurrentValue = $"{floatQuantityValue * 100:F0}%";
                    else if (isIntegerDisplay)
                        currentField.CurrentValue = floatQuantityValue.ToString("F0");
                    else
                        currentField.CurrentValue = floatQuantityValue.ToString("F1");
                }
                else
                {
                    currentField.SetValue?.Invoke(quantityValue);

                    // If GetValue is available, read back the actual stored value (may differ due to validation)
                    if (currentField.GetValue != null)
                    {
                        var actualValue = currentField.GetValue();
                        if (actualValue is int actualInt)
                            quantityValue = actualInt;
                    }

                    currentField.CurrentValue = quantityValue.ToString();
                }
                ScenarioBuilderState.SetDirty();
            }
            else if (currentField.Type == ScenarioBuilderState.FieldType.Text)
            {
                string newText = textController.CurrentText;
                currentField.SetValue?.Invoke(newText);

                // Update display value (truncated for tree view)
                if (string.IsNullOrEmpty(newText))
                {
                    currentField.CurrentValue = "(empty)";
                }
                else if (newText.Contains("\n"))
                {
                    int newlineIndex = newText.IndexOf('\n');
                    currentField.CurrentValue = newText.Substring(0, Math.Min(newlineIndex, 60)) + "...";
                }
                else if (newText.Length > 60)
                {
                    currentField.CurrentValue = newText.Substring(0, 60) + "...";
                }
                else
                {
                    currentField.CurrentValue = newText;
                }

                ScenarioBuilderState.SetDirty();
            }
        }

        #region Dropdown Navigation

        private static void AnnounceDropdownOption()
        {
            if (dropdownOptions == null || dropdownOptions.Count == 0) return;

            var option = dropdownOptions[selectedOptionIndex];
            string positionPart = MenuHelper.FormatPosition(selectedOptionIndex, dropdownOptions.Count);

            string text = $"{option.label}";

            // Add description from Def objects if available (only when game UI shows tooltips)
            // Special handling for FactionDef which has a computed Description property
            if (option.value is FactionDef fd && !string.IsNullOrEmpty(fd.Description))
            {
                string desc = fd.Description;
                int newlineIndex = desc.IndexOf('\n');
                if (newlineIndex > 0)
                    desc = desc.Substring(0, newlineIndex);
                text += $". {desc.Trim()}";
            }
            else if (option.value is Def def && !string.IsNullOrEmpty(def.description))
            {
                string desc = def.description;
                int newlineIndex = desc.IndexOf('\n');
                if (newlineIndex > 0)
                    desc = desc.Substring(0, newlineIndex);
                text += $". {desc.Trim()}";
            }

            if (!string.IsNullOrEmpty(positionPart))
            {
                text += $" ({positionPart})";
            }

            text += dropdownTypeahead.BuildSearchContextSuffix();

            // Game-supplied def label and description; the search suffix is already localized.
            TolkHelper.SpeakData(text);
        }

        private static void DropdownNext()
        {
            if (dropdownOptions == null || dropdownOptions.Count == 0) return;

            dropdownTypeahead.ClearSearch();
            selectedOptionIndex = MenuHelper.SelectNext(selectedOptionIndex, dropdownOptions.Count);
            AnnounceDropdownOption();
        }

        private static void DropdownPrevious()
        {
            if (dropdownOptions == null || dropdownOptions.Count == 0) return;

            dropdownTypeahead.ClearSearch();
            selectedOptionIndex = MenuHelper.SelectPrevious(selectedOptionIndex, dropdownOptions.Count);
            AnnounceDropdownOption();
        }

        private static void DropdownHome()
        {
            if (dropdownOptions == null || dropdownOptions.Count == 0) return;

            dropdownTypeahead.ClearSearch();
            selectedOptionIndex = 0;
            AnnounceDropdownOption();
        }

        private static void DropdownEnd()
        {
            if (dropdownOptions == null || dropdownOptions.Count == 0) return;

            dropdownTypeahead.ClearSearch();
            selectedOptionIndex = dropdownOptions.Count - 1;
            AnnounceDropdownOption();
        }

        private static void DropdownNextMatch()
        {
            if (!dropdownTypeahead.HasActiveSearch) return;
            int next = dropdownTypeahead.GetNextMatch(selectedOptionIndex);
            if (next >= 0)
            {
                selectedOptionIndex = next;
                AnnounceDropdownOption();
            }
        }

        private static void DropdownPreviousMatch()
        {
            if (!dropdownTypeahead.HasActiveSearch) return;
            int prev = dropdownTypeahead.GetPreviousMatch(selectedOptionIndex);
            if (prev >= 0)
            {
                selectedOptionIndex = prev;
                AnnounceDropdownOption();
            }
        }

        private static bool HandleDropdownTypeahead(char character)
        {
            if (dropdownOptions == null || dropdownOptions.Count == 0) return false;

            var labels = dropdownOptions.Select(o => o.label).ToList();

            if (dropdownTypeahead.ProcessCharacterInput(character, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedOptionIndex = newIndex;
                    AnnounceDropdownOption();
                }
            }
            else
            {
                dropdownTypeahead.SpeakNoMatches();
            }

            return true;
        }

        private static bool HandleDropdownBackspace()
        {
            if (!dropdownTypeahead.HasActiveSearch) return false;

            var labels = dropdownOptions.Select(o => o.label).ToList();

            if (dropdownTypeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedOptionIndex = newIndex;
                    AnnounceDropdownOption();
                }
            }

            return true;
        }

        #endregion

        #region Quantity Navigation

        /// <summary>
        /// Formats the current float value for display based on type flags.
        /// </summary>
        private static string FormatFloatValue(float value)
        {
            if (isPercentQuantity)
                return $"{value * 100:F0}%";
            else if (isIntegerDisplay)
                return value.ToString("F0");
            else
                return value.ToString("F1");
        }

        private static void AnnounceQuantity()
        {
            string value;
            if (isFloatQuantity)
            {
                value = FormatFloatValue(floatQuantityValue);
            }
            else
            {
                value = quantityValue.ToString();
            }

            TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.QuantityHint".Loc(
                currentField?.Name ?? (string)"RimWorldAccess.ScenarioBuilder.PartEdit.QuantityFallback".Translate(),
                value));
        }

        private static void AnnounceText()
        {
            string text = textController.CurrentText;
            int lineCount = text.Split('\n').Length;
            int wordCount = text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

            string summary;
            if (string.IsNullOrEmpty(text))
                summary = "RimWorldAccess.ScenarioBuilder.PartEdit.TextEmpty".Translate();
            else if (lineCount > 1)
                summary = "RimWorldAccess.ScenarioBuilder.PartEdit.TextSummaryLines".Translate(lineCount, wordCount);
            else if (text.Length > 50)
                summary = "RimWorldAccess.ScenarioBuilder.PartEdit.TextSummaryWords".Translate(wordCount);
            else
                summary = text;

            TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.TextHint".Loc(
                currentField?.Name ?? (string)"RimWorldAccess.ScenarioBuilder.PartEdit.TextFallback".Translate(),
                summary));
        }

        private static void QuantityIncrease(int amount = 1)
        {
            quantityTypedBuffer = ""; // Clear typed input when using arrows
            if (isFloatQuantity)
            {
                if (isPercentQuantity)
                {
                    // Increment by percentage points (amount=1 means 1%)
                    floatQuantityValue = Mathf.Clamp(floatQuantityValue + (amount * 0.01f), floatQuantityMin, floatQuantityMax);
                }
                else
                {
                    // Raw float - increment by 1 (or amount)
                    floatQuantityValue = Mathf.Clamp(floatQuantityValue + amount, floatQuantityMin, floatQuantityMax);
                }
                TolkHelper.SpeakData(FormatFloatValue(floatQuantityValue));
            }
            else
            {
                quantityValue = Mathf.Clamp(quantityValue + amount, quantityMin, quantityMax);
                TolkHelper.SpeakData(quantityValue.ToString());
            }
        }

        private static void QuantityDecrease(int amount = 1)
        {
            quantityTypedBuffer = ""; // Clear typed input when using arrows
            if (isFloatQuantity)
            {
                if (isPercentQuantity)
                {
                    // Decrement by percentage points (amount=1 means 1%)
                    floatQuantityValue = Mathf.Clamp(floatQuantityValue - (amount * 0.01f), floatQuantityMin, floatQuantityMax);
                }
                else
                {
                    // Raw float - decrement by 1 (or amount)
                    floatQuantityValue = Mathf.Clamp(floatQuantityValue - amount, floatQuantityMin, floatQuantityMax);
                }
                TolkHelper.SpeakData(FormatFloatValue(floatQuantityValue));
            }
            else
            {
                quantityValue = Mathf.Clamp(quantityValue - amount, quantityMin, quantityMax);
                TolkHelper.SpeakData(quantityValue.ToString());
            }
        }

        private static void QuantityMin()
        {
            quantityTypedBuffer = ""; // Clear typed input when using Home
            if (isFloatQuantity)
            {
                floatQuantityValue = floatQuantityMin;
                TolkHelper.SpeakData(FormatFloatValue(floatQuantityValue));
            }
            else
            {
                quantityValue = quantityMin;
                TolkHelper.SpeakData(quantityValue.ToString());
            }
        }

        private static void QuantityMax()
        {
            quantityTypedBuffer = ""; // Clear typed input when using End
            if (isFloatQuantity)
            {
                floatQuantityValue = floatQuantityMax;
                TolkHelper.SpeakData(FormatFloatValue(floatQuantityValue));
            }
            else
            {
                quantityValue = quantityMax;
                TolkHelper.SpeakData(quantityValue.ToString());
            }
        }

        #endregion

        #region Input Handling

        /// <summary>
        /// Handles keyboard input for the field editor.
        /// Returns true if the input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!IsActive) return false;

            if (currentField?.Type == ScenarioBuilderState.FieldType.Dropdown)
            {
                return HandleDropdownInput(key, shift, ctrl);
            }
            else if (currentField?.Type == ScenarioBuilderState.FieldType.Quantity)
            {
                return HandleQuantityInput(key, shift, ctrl);
            }
            else if (currentField?.Type == ScenarioBuilderState.FieldType.Text)
            {
                return HandleTextInput(key, shift, ctrl);
            }

            return false;
        }

        private static bool HandleDropdownInput(KeyCode key, bool shift, bool ctrl)
        {
            switch (key)
            {
                case KeyCode.UpArrow:
                    if (dropdownTypeahead.HasActiveSearch)
                        DropdownPreviousMatch();
                    else
                        DropdownPrevious();
                    return true;
                case KeyCode.DownArrow:
                    if (dropdownTypeahead.HasActiveSearch)
                        DropdownNextMatch();
                    else
                        DropdownNext();
                    return true;
                case KeyCode.Home:
                    DropdownHome();
                    return true;
                case KeyCode.End:
                    DropdownEnd();
                    return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    string selectedLabel = dropdownOptions[selectedOptionIndex].label;
                    Close(applyChanges: true);
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.SelectedLabel".Loc(selectedLabel));
                    return true;
                case KeyCode.Escape:
                    Close(applyChanges: false);
                    TolkHelper.Speak("RimWorldAccess.UI.Cancelled".Loc());
                    return true;
                case KeyCode.Backspace:
                    if (dropdownTypeahead.HasActiveSearch)
                    {
                        HandleDropdownBackspace();
                    }
                    // Always consume backspace in dropdown mode
                    return true;
                // CRITICAL: Consume all navigation keys to prevent them leaking to parent state
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                case KeyCode.Tab:
                case KeyCode.Delete:
                    // Silently consume - these keys don't apply to dropdown editing
                    return true;
            }

            return false;
        }

        private static bool HandleQuantityInput(KeyCode key, bool shift, bool ctrl)
        {
            // Determine increment amount based on modifiers
            // Shift = 10, Ctrl = 100, Both = 100, None = 1
            int increment = 1;
            if (ctrl) increment = 100;
            else if (shift) increment = 10;

            switch (key)
            {
                case KeyCode.UpArrow:
                    QuantityIncrease(increment);
                    return true;
                case KeyCode.DownArrow:
                    QuantityDecrease(increment);
                    return true;
                case KeyCode.Plus:
                case KeyCode.KeypadPlus:
                case KeyCode.Equals: // Unshifted + key on most keyboards
                    QuantityIncrease(1);
                    return true;
                case KeyCode.Minus:
                case KeyCode.KeypadMinus:
                    QuantityDecrease(1);
                    return true;
                case KeyCode.Home:
                    QuantityMin();
                    return true;
                case KeyCode.End:
                    QuantityMax();
                    return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Close(applyChanges: true);
                    string finalValue;
                    if (isFloatQuantity)
                        finalValue = FormatFloatValue(floatQuantityValue);
                    else
                        finalValue = quantityValue.ToString();
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.SetTo".Loc(finalValue));
                    return true;
                case KeyCode.Escape:
                    Close(applyChanges: false);
                    TolkHelper.Speak("RimWorldAccess.UI.Cancelled".Loc());
                    return true;
                case KeyCode.Backspace:
                    // Handle backspace for typed number input
                    HandleQuantityBackspace();
                    return true;
                // CRITICAL: Consume all navigation keys to prevent them leaking to parent state
                // Without this, Left/Right/Tab would be handled by ScenarioBuilderState
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                case KeyCode.Tab:
                case KeyCode.Delete:
                    // Silently consume - these keys don't apply to quantity editing
                    return true;
            }

            return false;
        }

        private static bool HandleTextInput(KeyCode key, bool shift, bool ctrl)
        {
            // Cursor review: Left/Right/Home/End/Delete (with Shift/Ctrl variants) let the
            // user audit and edit the current text without leaving the field. Up/Down still
            // own list nav (handled in the outer switch below).
            if (textController.HandleCursorNavEvent(Event.current))
            {
                return true;
            }

            switch (key)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (shift)
                    {
                        // Shift+Enter inserts a newline. HandleCharacter announces
                        // "New line" itself in multi-line mode.
                        textController.HandleCharacter('\n');
                        return true;
                    }
                    else
                    {
                        // Enter confirms the edit
                        Close(applyChanges: true);
                        TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.TextSaved".Loc());
                        return true;
                    }
                case KeyCode.Escape:
                    Close(applyChanges: false);
                    TolkHelper.Speak("RimWorldAccess.UI.Cancelled".Loc());
                    return true;
                case KeyCode.Backspace:
                    textController.HandleBackspace();
                    return true;
                case KeyCode.Insert:
                    textController.ReadCurrentText();
                    return true;
                case KeyCode.C:
                    if (ctrl)
                    {
                        textController.HandleCopy();
                        return true;
                    }
                    break;
                case KeyCode.V:
                    if (ctrl)
                    {
                        textController.HandlePaste();
                        return true;
                    }
                    break;
                // Multi-line text fields: Up/Down navigate lines within the buffer.
                // The enclosing parts editor owns Up/Down at the list level, but once
                // the user has committed to editing a text field, line nav here is
                // more useful than list nav (they can't jump between fields without
                // Enter/Escape anyway). Consumes the key so it doesn't leak upward.
                case KeyCode.UpArrow:
                    textController.HandleArrowUp(shift);
                    return true;
                case KeyCode.DownArrow:
                    textController.HandleArrowDown(shift);
                    return true;
                case KeyCode.Tab:
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Handles character input for typeahead in dropdown mode or number input in quantity mode.
        /// </summary>
        public static bool HandleCharacterInput(char character)
        {
            if (!IsActive) return false;

            if (currentField?.Type == ScenarioBuilderState.FieldType.Dropdown)
            {
                if (char.IsLetterOrDigit(character))
                {
                    return HandleDropdownTypeahead(character);
                }
            }
            else if (currentField?.Type == ScenarioBuilderState.FieldType.Quantity)
            {
                // Allow digits for all quantity fields
                if (char.IsDigit(character))
                {
                    return HandleQuantityDigitInput(character);
                }
                // Allow decimal point for float fields that are NOT integer-display and NOT percentage
                if ((character == '.' || character == ',') && isFloatQuantity && !isIntegerDisplay && !isPercentQuantity)
                {
                    return HandleQuantityDecimalInput();
                }
            }
            else if (currentField?.Type == ScenarioBuilderState.FieldType.Text)
            {
                // Accept all printable characters for text editing
                if (character >= ' ')
                {
                    textController.HandleCharacter(character);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Handles decimal point input for float quantity fields.
        /// </summary>
        private static bool HandleQuantityDecimalInput()
        {
            // Don't allow multiple decimal points
            if (quantityTypedBuffer.Contains("."))
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.AlreadyHasDecimal".Loc());
                return true;
            }

            // If buffer is empty, start with "0."
            if (string.IsNullOrEmpty(quantityTypedBuffer))
            {
                quantityTypedBuffer = "0.";
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.ZeroPoint".Loc());
            }
            else
            {
                quantityTypedBuffer += ".";
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.NumberPoint".Loc(quantityTypedBuffer.TrimEnd('.')));
            }

            return true;
        }

        /// <summary>
        /// Handles digit input for quantity fields - builds a number from typed digits.
        /// </summary>
        private static bool HandleQuantityDigitInput(char digit)
        {
            quantityTypedBuffer += digit;

            // For float fields that support decimals, parse as float
            if (isFloatQuantity && !isPercentQuantity && !isIntegerDisplay && quantityTypedBuffer.Contains("."))
            {
                // Parse as float for decimal values
                if (float.TryParse(quantityTypedBuffer, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float typedFloat))
                {
                    float clampedFloat = Mathf.Clamp(typedFloat, floatQuantityMin, floatQuantityMax);
                    floatQuantityValue = clampedFloat;

                    if (typedFloat != clampedFloat)
                    {
                        // Value was clamped
                        quantityTypedBuffer = clampedFloat.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                        string limitType = typedFloat > floatQuantityMax
                            ? (string)"RimWorldAccess.ScenarioBuilder.PartEdit.LimitMaximum".Translate()
                            : (string)"RimWorldAccess.ScenarioBuilder.PartEdit.LimitMinimum".Translate();
                        TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.ValueWithLimit".Loc(clampedFloat.ToString("F1"), limitType));
                    }
                    else
                    {
                        TolkHelper.SpeakData(typedFloat.ToString("F1"));
                    }
                }
                return true;
            }

            // Parse as integer for whole numbers
            if (int.TryParse(quantityTypedBuffer, out int typedValue))
            {
                if (isFloatQuantity)
                {
                    if (isPercentQuantity)
                    {
                        // For percentages, typed value is the display percentage
                        // Convert to stored value by dividing by 100
                        float storedValue = typedValue / 100f;
                        float clampedFloat = Mathf.Clamp(storedValue, floatQuantityMin, floatQuantityMax);
                        floatQuantityValue = clampedFloat;
                        int clampedPercent = Mathf.RoundToInt(clampedFloat * 100);
                        int maxPercent = Mathf.RoundToInt(floatQuantityMax * 100);

                        if (typedValue != clampedPercent)
                        {
                            // Value was clamped - clear buffer and use friendly message
                            quantityTypedBuffer = clampedPercent.ToString();
                            string limitType = typedValue > maxPercent
                                ? (string)"RimWorldAccess.ScenarioBuilder.PartEdit.LimitMaximum".Translate()
                                : (string)"RimWorldAccess.ScenarioBuilder.PartEdit.LimitMinimum".Translate();
                            TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.PercentWithLimit".Loc(clampedPercent, limitType));
                        }
                        else
                        {
                            TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.PercentBare".Loc(typedValue));
                        }
                    }
                    else
                    {
                        // For raw floats (like days or radius), treat typed value directly
                        float clampedFloat = Mathf.Clamp(typedValue, floatQuantityMin, floatQuantityMax);
                        floatQuantityValue = clampedFloat;

                        if (typedValue != (int)clampedFloat)
                        {
                            // Value was clamped
                            quantityTypedBuffer = ((int)clampedFloat).ToString();
                            string limitType = typedValue > floatQuantityMax
                                ? (string)"RimWorldAccess.ScenarioBuilder.PartEdit.LimitMaximum".Translate()
                                : (string)"RimWorldAccess.ScenarioBuilder.PartEdit.LimitMinimum".Translate();
                            TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.ValueWithLimit".Loc(FormatFloatValue(clampedFloat), limitType));
                        }
                        else
                        {
                            TolkHelper.SpeakData(FormatFloatValue(floatQuantityValue));
                        }
                    }
                }
                else
                {
                    // Clamp to valid range
                    quantityValue = Mathf.Clamp(typedValue, quantityMin, quantityMax);
                    if (typedValue != quantityValue)
                    {
                        // Value was clamped - clear buffer to clamped value and use friendly message
                        quantityTypedBuffer = quantityValue.ToString();
                        string limitType = typedValue > quantityMax
                            ? (string)"RimWorldAccess.ScenarioBuilder.PartEdit.LimitMaximum".Translate()
                            : (string)"RimWorldAccess.ScenarioBuilder.PartEdit.LimitMinimum".Translate();
                        TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.ValueWithLimit".Loc(quantityValue, limitType));
                    }
                    else
                    {
                        TolkHelper.SpeakData(quantityValue.ToString());
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Handles backspace in quantity mode - removes last typed character.
        /// </summary>
        private static bool HandleQuantityBackspace()
        {
            if (string.IsNullOrEmpty(quantityTypedBuffer))
            {
                return false; // Nothing to delete
            }

            // Remove last character
            quantityTypedBuffer = quantityTypedBuffer.Substring(0, quantityTypedBuffer.Length - 1);

            if (string.IsNullOrEmpty(quantityTypedBuffer))
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.Cleared".Loc());
                // Reset to min value
                if (isFloatQuantity)
                    floatQuantityValue = floatQuantityMin;
                else
                    quantityValue = quantityMin;
            }
            else if (quantityTypedBuffer.EndsWith("."))
            {
                // Buffer ends with decimal point - just announce what's before it
                string beforeDecimal = quantityTypedBuffer.TrimEnd('.');
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.NumberPoint".Loc(beforeDecimal));
            }
            else if (quantityTypedBuffer.Contains(".") && float.TryParse(quantityTypedBuffer,
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float typedFloat))
            {
                // Buffer has decimal - parse as float
                floatQuantityValue = Mathf.Clamp(typedFloat, floatQuantityMin, floatQuantityMax);
                TolkHelper.SpeakData(typedFloat.ToString("F1"));
            }
            else if (int.TryParse(quantityTypedBuffer, out int typedValue))
            {
                if (isFloatQuantity)
                {
                    if (isPercentQuantity)
                    {
                        // Convert typed percentage to stored value and clamp to actual range
                        floatQuantityValue = Mathf.Clamp(typedValue / 100f, floatQuantityMin, floatQuantityMax);
                        TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.PercentBare".Loc(typedValue));
                    }
                    else
                    {
                        floatQuantityValue = Mathf.Clamp(typedValue, floatQuantityMin, floatQuantityMax);
                        TolkHelper.SpeakData(FormatFloatValue(floatQuantityValue));
                    }
                }
                else
                {
                    quantityValue = Mathf.Clamp(typedValue, quantityMin, quantityMax);
                    TolkHelper.SpeakData(quantityValue.ToString());
                }
            }

            return true;
        }

        #endregion
    }
}
