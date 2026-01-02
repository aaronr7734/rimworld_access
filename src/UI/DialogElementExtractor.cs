using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Extracts interactive elements (buttons, text fields, labels) from RimWorld dialogs using reflection.
    /// </summary>
    public static class DialogElementExtractor
    {
        /// <summary>
        /// Extracts all interactive elements from a dialog window.
        /// </summary>
        public static List<DialogElement> ExtractElements(Window dialog)
        {
            List<DialogElement> elements = new List<DialogElement>();

            if (dialog == null)
                return elements;

            Type dialogType = dialog.GetType();

            // Extract text fields first (they should be navigated to before buttons)
            ExtractTextFields(dialog, dialogType, elements);

            // Extract buttons (including from Dialog_MessageBox, Dialog_NodeTree, etc.)
            ExtractButtons(dialog, dialogType, elements);

            return elements;
        }

        /// <summary>
        /// Gets the dialog title if available.
        /// </summary>
        public static string GetDialogTitle(Window dialog)
        {
            if (dialog == null)
                return null;

            Type dialogType = dialog.GetType();

            // Try to get "title" field
            FieldInfo titleField = dialogType.GetField("title", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (titleField != null)
            {
                object titleValue = titleField.GetValue(dialog);
                if (titleValue != null)
                {
                    return titleValue.ToString();
                }
            }

            // Try optionalTitle property
            if (!string.IsNullOrEmpty(dialog.optionalTitle))
            {
                return dialog.optionalTitle;
            }

            return null;
        }

        /// <summary>
        /// Gets the dialog message/text if available.
        /// </summary>
        public static string GetDialogMessage(Window dialog)
        {
            if (dialog == null)
                return null;

            Type dialogType = dialog.GetType();

            // For Dialog_MessageBox
            if (dialog is Dialog_MessageBox messageBox)
            {
                return messageBox.text.ToString();
            }

            // For Dialog_NodeTree and all subclasses
            if (IsDialogNodeTreeOrSubclass(dialogType))
            {
                FieldInfo curNodeField = dialogType.GetField("curNode", BindingFlags.NonPublic | BindingFlags.Instance);
                if (curNodeField == null)
                {
                    // Try looking in base type for subclasses
                    curNodeField = dialogType.BaseType?.GetField("curNode", BindingFlags.NonPublic | BindingFlags.Instance);
                }

                if (curNodeField != null)
                {
                    object curNode = curNodeField.GetValue(dialog);
                    if (curNode != null)
                    {
                        // Try both public and private text field
                        FieldInfo textField = curNode.GetType().GetField("text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (textField != null)
                        {
                            object text = textField.GetValue(curNode);
                            if (text != null)
                            {
                                string textStr = text.ToString();
                                Log.Message($"RimWorld Access: Extracted Dialog_NodeTree text: {textStr.Substring(0, Math.Min(100, textStr.Length))}");
                                return textStr;
                            }
                        }
                        else
                        {
                            Log.Warning($"RimWorld Access: Dialog_NodeTree curNode has no 'text' field. Node type: {curNode.GetType().Name}");
                        }
                    }
                    else
                    {
                        Log.Warning($"RimWorld Access: Dialog_NodeTree curNode is null for dialog type: {dialogType.Name}");
                    }
                }
                else
                {
                    Log.Warning($"RimWorld Access: Dialog_NodeTree has no 'curNode' field for dialog type: {dialogType.Name}");
                }
            }

            // For Dialog_GiveName and subclasses
            if (dialogType.BaseType != null && dialogType.BaseType.Name == "Dialog_GiveName")
            {
                // Get the nameMessageKey field
                FieldInfo nameMessageKeyField = dialogType.GetField("nameMessageKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo suggestingPawnField = dialogType.GetField("suggestingPawn", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (nameMessageKeyField != null && suggestingPawnField != null)
                {
                    string messageKey = nameMessageKeyField.GetValue(dialog) as string;
                    object pawn = suggestingPawnField.GetValue(dialog);

                    if (!string.IsNullOrEmpty(messageKey) && pawn != null)
                    {
                        // Get pawn label
                        PropertyInfo labelProp = pawn.GetType().GetProperty("LabelShort");
                        string pawnLabel = labelProp != null ? (string)labelProp.GetValue(pawn, null) : "colonist";

                        // Translate the message key with pawn parameter (using TaggedString extension)
                        TaggedString taggedMessage = messageKey.Translate(pawnLabel, pawn);
                        string translatedMessage = taggedMessage.ToString();

                        // Check if there's a second message as well
                        FieldInfo useSecondNameField = dialogType.GetField("useSecondName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (useSecondNameField != null && (bool)useSecondNameField.GetValue(dialog))
                        {
                            FieldInfo secondNameMessageKeyField = dialogType.GetField("secondNameMessageKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (secondNameMessageKeyField != null)
                            {
                                string secondMessageKey = secondNameMessageKeyField.GetValue(dialog) as string;
                                if (!string.IsNullOrEmpty(secondMessageKey))
                                {
                                    TaggedString taggedSecond = secondMessageKey.Translate(pawnLabel, pawn);
                                    string secondMessage = taggedSecond.ToString();
                                    translatedMessage += " " + secondMessage;
                                }
                            }
                        }

                        return translatedMessage;
                    }
                }
            }

            // Try to find any "text" or "message" field
            FieldInfo messageField = dialogType.GetField("text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (messageField == null)
            {
                messageField = dialogType.GetField("message", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }

            if (messageField != null)
            {
                object messageValue = messageField.GetValue(dialog);
                if (messageValue != null)
                {
                    return messageValue.ToString();
                }
            }

            return null;
        }

        private static void ExtractTextFields(Window dialog, Type dialogType, List<DialogElement> elements)
        {
            // Check if this is a Dialog_GiveName (for better label names)
            bool isDialogGiveName = dialogType.BaseType != null && dialogType.BaseType.Name == "Dialog_GiveName";

            // Check if using second name (for proper labeling)
            FieldInfo useSecondNameField = dialogType.GetField("useSecondName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            bool useSecondName = useSecondNameField != null && (bool)useSecondNameField.GetValue(dialog);

            // Check for curName field (used in Dialog_Rename and subclasses)
            FieldInfo curNameField = dialogType.GetField("curName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (curNameField != null && curNameField.FieldType == typeof(string))
            {
                string currentValue = (string)curNameField.GetValue(dialog);
                if (currentValue == null)
                    currentValue = "";

                // Get max length if available
                int maxLength = 1000;
                PropertyInfo maxLengthProp = dialogType.GetProperty("MaxNameLength", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (maxLengthProp == null)
                {
                    maxLengthProp = dialogType.GetProperty("FirstCharLimit", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                if (maxLengthProp != null)
                {
                    maxLength = (int)maxLengthProp.GetValue(dialog, null);
                }

                // Determine appropriate label
                string label = "Name";
                if (isDialogGiveName && useSecondName)
                {
                    // If there are two names, first is faction, second is settlement
                    label = "Faction Name";
                }
                else if (isDialogGiveName)
                {
                    // Single name dialog - could be faction, colony, etc.
                    label = "Faction Name";
                }

                TextFieldElement textField = new TextFieldElement(
                    label,
                    currentValue,
                    (newValue) => curNameField.SetValue(dialog, newValue)
                );
                textField.MaxLength = maxLength;

                elements.Add(textField);
            }

            // Check for curSecondName field (used in Dialog_GiveName with useSecondName = true)
            FieldInfo curSecondNameField = dialogType.GetField("curSecondName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (curSecondNameField != null && useSecondName)
            {
                string currentValue = (string)curSecondNameField.GetValue(dialog);
                if (currentValue == null)
                    currentValue = "";

                // Get max length for second field
                int maxLength = 1000;
                PropertyInfo secondCharLimitProp = dialogType.GetProperty("SecondCharLimit", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (secondCharLimitProp != null)
                {
                    maxLength = (int)secondCharLimitProp.GetValue(dialog, null);
                }

                // For Dialog_GiveName with two names: first is faction, second is settlement
                string label = isDialogGiveName ? "Settlement Name" : "Second Name";

                TextFieldElement secondTextField = new TextFieldElement(
                    label,
                    currentValue,
                    (newValue) => curSecondNameField.SetValue(dialog, newValue)
                );
                secondTextField.MaxLength = maxLength;

                elements.Add(secondTextField);
            }
        }

        private static void ExtractButtons(Window dialog, Type dialogType, List<DialogElement> elements)
        {
            // For Dialog_MessageBox
            if (dialog is Dialog_MessageBox messageBox)
            {
                // Button A (usually Confirm/OK)
                // Note: Only call buttonAAction, not acceptAction. The game's CreateConfirmation
                // passes the same action to both, so calling both would execute it twice.
                // buttonAAction is for button clicks, acceptAction is for Enter key handling.
                if (!string.IsNullOrEmpty(messageBox.buttonAText))
                {
                    ButtonElement buttonA = new ButtonElement
                    {
                        Label = messageBox.buttonAText,
                        Action = () =>
                        {
                            messageBox.buttonAAction?.Invoke();
                        },
                        IsConfirm = messageBox.buttonAText.ToLower().Contains("confirm") || messageBox.buttonAText.ToLower().Contains("ok"),
                        IsClose = true
                    };
                    elements.Add(buttonA);
                }

                // Button B (usually Cancel/Go Back)
                // Note: Only call buttonBAction, not cancelAction. Same reason as Button A above.
                if (!string.IsNullOrEmpty(messageBox.buttonBText))
                {
                    ButtonElement buttonB = new ButtonElement
                    {
                        Label = messageBox.buttonBText,
                        Action = () =>
                        {
                            messageBox.buttonBAction?.Invoke();
                        },
                        IsCancel = messageBox.buttonBText.ToLower().Contains("cancel") || messageBox.buttonBText.ToLower().Contains("back"),
                        IsClose = true
                    };
                    elements.Add(buttonB);
                }

                // Button C (if present)
                if (!string.IsNullOrEmpty(messageBox.buttonCText))
                {
                    ButtonElement buttonC = new ButtonElement
                    {
                        Label = messageBox.buttonCText,
                        Action = () =>
                        {
                            messageBox.buttonCAction?.Invoke();
                        },
                        IsClose = messageBox.buttonCClose
                    };
                    elements.Add(buttonC);
                }
            }
            // For Dialog_NodeTree and all subclasses
            else if (IsDialogNodeTreeOrSubclass(dialogType))
            {
                FieldInfo curNodeField = dialogType.GetField("curNode", BindingFlags.NonPublic | BindingFlags.Instance);
                if (curNodeField == null)
                {
                    // Try looking in base type for subclasses
                    curNodeField = dialogType.BaseType?.GetField("curNode", BindingFlags.NonPublic | BindingFlags.Instance);
                }

                if (curNodeField != null)
                {
                    object curNode = curNodeField.GetValue(dialog);
                    if (curNode != null)
                    {
                        // Get options list
                        Type diaNodeType = curNode.GetType();
                        FieldInfo optionsField = diaNodeType.GetField("options", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (optionsField != null)
                        {
                            var options = optionsField.GetValue(curNode) as System.Collections.IList;
                            if (options != null && options.Count > 0)
                            {
                                Log.Message($"RimWorld Access: Found {options.Count} options in Dialog_NodeTree");

                                foreach (var option in options)
                                {
                                    // Extract option text
                                    Type optionType = option.GetType();
                                    FieldInfo textField = optionType.GetField("text", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                                    FieldInfo disabledField = optionType.GetField("disabled", BindingFlags.Public | BindingFlags.Instance);
                                    FieldInfo disabledReasonField = optionType.GetField("disabledReason", BindingFlags.Public | BindingFlags.Instance);

                                    if (textField != null)
                                    {
                                        string optionText = textField.GetValue(option) as string;
                                        bool disabled = disabledField != null && (bool)disabledField.GetValue(option);
                                        string disabledReason = disabledReasonField != null ? disabledReasonField.GetValue(option) as string : null;

                                        Log.Message($"RimWorld Access: Dialog option: '{optionText}' (disabled: {disabled})");

                                        ButtonElement button = new ButtonElement
                                        {
                                            Label = optionText,
                                            Action = () =>
                                            {
                                                // Set dialog reference
                                                FieldInfo dialogField = optionType.GetField("dialog", BindingFlags.Public | BindingFlags.Instance);
                                                if (dialogField != null)
                                                {
                                                    dialogField.SetValue(option, dialog);
                                                }

                                                // Call Activate method
                                                MethodInfo activateMethod = optionType.GetMethod("Activate", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                                                if (activateMethod != null)
                                                {
                                                    activateMethod.Invoke(option, null);
                                                }
                                            },
                                            Disabled = disabled,
                                            DisabledReason = disabledReason,
                                            IsClose = true
                                        };

                                        elements.Add(button);
                                    }
                                    else
                                    {
                                        Log.Warning($"RimWorld Access: Dialog option has no 'text' field. Option type: {optionType.Name}");
                                    }
                                }
                            }
                            else
                            {
                                Log.Warning($"RimWorld Access: Dialog_NodeTree options list is null or empty for dialog type: {dialogType.Name}");
                            }
                        }
                        else
                        {
                            Log.Warning($"RimWorld Access: Dialog_NodeTree curNode has no 'options' field. Node type: {diaNodeType.Name}");
                        }
                    }
                    else
                    {
                        Log.Warning($"RimWorld Access: Dialog_NodeTree curNode is null in ExtractButtons for dialog type: {dialogType.Name}");
                    }
                }
                else
                {
                    Log.Warning($"RimWorld Access: Dialog_NodeTree has no 'curNode' field in ExtractButtons for dialog type: {dialogType.Name}");
                }
            }
            // For Dialog_Rename and Dialog_GiveName - extract OK button
            else
            {
                // Look for methods that validate and apply the name change
                // We'll create a generic OK button that calls the appropriate methods

                // For Dialog_Rename<T>
                if (dialogType.BaseType != null && dialogType.BaseType.Name.StartsWith("Dialog_Rename"))
                {
                    ButtonElement okButton = new ButtonElement
                    {
                        Label = "OK",
                        Action = () => ExecuteDialogRenameOK(dialog, dialogType),
                        IsConfirm = true,
                        IsClose = false // We'll close it manually if validation passes
                    };
                    elements.Add(okButton);
                }
                // For Dialog_GiveName
                else if (dialogType.BaseType != null && dialogType.BaseType.Name == "Dialog_GiveName")
                {
                    ButtonElement okButton = new ButtonElement
                    {
                        Label = "OK",
                        Action = () => ExecuteDialogGiveNameOK(dialog, dialogType),
                        IsConfirm = true,
                        IsClose = false // We'll close it manually if validation passes
                    };
                    elements.Add(okButton);
                }
            }

            // Add a generic Cancel button if no cancel button was found
            if (!elements.Exists(e => e is ButtonElement btn && btn.IsCancel))
            {
                ButtonElement cancelButton = new ButtonElement
                {
                    Label = "Cancel",
                    Action = () => { },
                    IsCancel = true,
                    IsClose = true
                };
                elements.Add(cancelButton);
            }
        }

        private static void ExecuteDialogRenameOK(Window dialog, Type dialogType)
        {
            // Get curName field
            FieldInfo curNameField = dialogType.GetField("curName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (curNameField == null)
                return;

            string name = (string)curNameField.GetValue(dialog);

            // Call NameIsValid method
            MethodInfo nameIsValidMethod = dialogType.GetMethod("NameIsValid", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (nameIsValidMethod != null)
            {
                object result = nameIsValidMethod.Invoke(dialog, new object[] { name });

                // AcceptanceReport handling
                Type acceptanceReportType = result.GetType();
                PropertyInfo acceptedProp = acceptanceReportType.GetProperty("Accepted");
                PropertyInfo reasonProp = acceptanceReportType.GetProperty("Reason");

                if (acceptedProp != null)
                {
                    bool accepted = (bool)acceptedProp.GetValue(result, null);

                    if (!accepted)
                    {
                        string reason = reasonProp?.GetValue(result, null) as string;
                        if (string.IsNullOrEmpty(reason))
                        {
                            TolkHelper.Speak("Name is invalid", SpeechPriority.High);
                        }
                        else
                        {
                            TolkHelper.Speak(reason, SpeechPriority.High);
                        }
                        return;
                    }
                }
            }

            // Get the renaming object and set the name
            FieldInfo renamingField = dialogType.GetField("renaming", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (renamingField != null)
            {
                object renamingObj = renamingField.GetValue(dialog);
                if (renamingObj != null)
                {
                    // Set RenamableLabel property
                    PropertyInfo labelProp = renamingObj.GetType().GetProperty("RenamableLabel");
                    if (labelProp != null && labelProp.CanWrite)
                    {
                        labelProp.SetValue(renamingObj, name, null);
                    }
                }
            }

            // Call OnRenamed method
            MethodInfo onRenamedMethod = dialogType.GetMethod("OnRenamed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (onRenamedMethod != null)
            {
                onRenamedMethod.Invoke(dialog, new object[] { name });
            }

            TolkHelper.Speak($"Renamed to {name}", SpeechPriority.High);

            // Close the windowless dialog
            WindowlessDialogState.Close();
        }

        private static void ExecuteDialogGiveNameOK(Window dialog, Type dialogType)
        {
            // Get curName field
            FieldInfo curNameField = dialogType.GetField("curName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (curNameField == null)
                return;

            string name = ((string)curNameField.GetValue(dialog))?.Trim();

            // Check for second name
            FieldInfo curSecondNameField = dialogType.GetField("curSecondName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo useSecondNameField = dialogType.GetField("useSecondName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            bool useSecondName = useSecondNameField != null && (bool)useSecondNameField.GetValue(dialog);
            string secondName = null;

            if (useSecondName && curSecondNameField != null)
            {
                secondName = ((string)curSecondNameField.GetValue(dialog))?.Trim();
            }

            // Validate name
            MethodInfo isValidNameMethod = dialogType.GetMethod("IsValidName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (isValidNameMethod != null)
            {
                bool isValid = (bool)isValidNameMethod.Invoke(dialog, new object[] { name });

                if (!isValid)
                {
                    FieldInfo invalidNameMessageKeyField = dialogType.GetField("invalidNameMessageKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    string invalidMessage = "Invalid name";
                    if (invalidNameMessageKeyField != null)
                    {
                        string key = (string)invalidNameMessageKeyField.GetValue(dialog);
                        if (!string.IsNullOrEmpty(key))
                        {
                            invalidMessage = key.Translate();
                        }
                    }
                    TolkHelper.Speak(invalidMessage, SpeechPriority.High);
                    return;
                }
            }

            // Validate second name if used
            if (useSecondName)
            {
                MethodInfo isValidSecondNameMethod = dialogType.GetMethod("IsValidSecondName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (isValidSecondNameMethod != null)
                {
                    bool isValid = (bool)isValidSecondNameMethod.Invoke(dialog, new object[] { secondName });

                    if (!isValid)
                    {
                        FieldInfo invalidSecondNameMessageKeyField = dialogType.GetField("invalidSecondNameMessageKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        string invalidMessage = "Invalid second name";
                        if (invalidSecondNameMessageKeyField != null)
                        {
                            string key = (string)invalidSecondNameMessageKeyField.GetValue(dialog);
                            if (!string.IsNullOrEmpty(key))
                            {
                                invalidMessage = key.Translate();
                            }
                        }
                        TolkHelper.Speak(invalidMessage, SpeechPriority.High);
                        return;
                    }
                }
            }

            // Call Named method
            MethodInfo namedMethod = dialogType.GetMethod("Named", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (namedMethod != null)
            {
                namedMethod.Invoke(dialog, new object[] { name });
            }

            // Call NamedSecond method if using second name
            if (useSecondName)
            {
                MethodInfo namedSecondMethod = dialogType.GetMethod("NamedSecond", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (namedSecondMethod != null)
                {
                    namedSecondMethod.Invoke(dialog, new object[] { secondName });
                }

                TolkHelper.Speak($"Named {name} {secondName}", SpeechPriority.High);
            }
            else
            {
                TolkHelper.Speak($"Named {name}", SpeechPriority.High);
            }

            // Close the windowless dialog
            WindowlessDialogState.Close();
        }

        /// <summary>
        /// Checks if the given type is Dialog_NodeTree or a subclass of it.
        /// Walks up the entire type hierarchy to catch indirect subclasses.
        /// </summary>
        private static bool IsDialogNodeTreeOrSubclass(Type type)
        {
            if (type == null)
                return false;

            // Check current type name
            if (type.Name == "Dialog_NodeTree")
                return true;

            // Walk up the inheritance chain
            Type currentType = type.BaseType;
            while (currentType != null)
            {
                if (currentType.Name == "Dialog_NodeTree")
                    return true;
                currentType = currentType.BaseType;
            }

            return false;
        }
    }
}
