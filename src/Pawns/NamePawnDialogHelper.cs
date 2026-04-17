using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper for Dialog_NamePawn accessibility (baby naming, animal naming, mech naming).
    /// Works with WindowlessDialogState - the dialog is intercepted and never actually opens.
    /// </summary>
    public static class NamePawnDialogHelper
    {
        /// <summary>
        /// Extracts text fields from Dialog_NamePawn.
        /// Called by DialogElementExtractor.
        /// </summary>
        public static void ExtractFields(Window dialog, Type dialogType, List<DialogElement> elements)
        {
            // Get the 'names' field (List<NameContext>)
            FieldInfo namesField = dialogType.GetField("names", BindingFlags.NonPublic | BindingFlags.Instance);
            if (namesField == null)
            {
                Log.Warning("[NamePawnDialogHelper] 'names' field not found");
                return;
            }

            var namesList = namesField.GetValue(dialog) as System.Collections.IList;
            if (namesList == null || namesList.Count == 0)
            {
                Log.Warning("[NamePawnDialogHelper] 'names' list is null or empty");
                return;
            }

            // Get the NameContext type (private nested class)
            Type nameContextType = namesList[0].GetType();
            FieldInfo currentField = nameContextType.GetField("current", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo labelField = nameContextType.GetField("label", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo editableField = nameContextType.GetField("editable", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo maxLengthField = nameContextType.GetField("maximumNameLength", BindingFlags.Public | BindingFlags.Instance);

            if (currentField == null || labelField == null)
            {
                Log.Warning("[NamePawnDialogHelper] NameContext fields not found");
                return;
            }

            for (int i = 0; i < namesList.Count; i++)
            {
                object nameContext = namesList[i];
                string current = currentField.GetValue(nameContext) as string ?? "";
                object labelObj = labelField.GetValue(nameContext);
                string label = labelObj?.ToString()?.TrimEnd(':') ?? $"Name {i + 1}";
                bool editable = editableField != null && (bool)editableField.GetValue(nameContext);
                int maxLength = maxLengthField != null ? (int)maxLengthField.GetValue(nameContext) : 16;

                if (!editable)
                {
                    // Add as a label element for read-only fields
                    elements.Add(new LabelElement { Label = label, Text = current });
                }
                else
                {
                    // Create a text field element with callback to update the NameContext
                    int index = i; // Capture for closure
                    TextFieldElement textField = new TextFieldElement(
                        label,
                        current,
                        (newValue) =>
                        {
                            if (index < namesList.Count)
                            {
                                currentField.SetValue(namesList[index], newValue);
                            }
                        }
                    );
                    textField.MaxLength = maxLength;
                    // Vanilla pawn-name constraint: Unicode letters/digits/space/'-./
                    // (CharacterCardUtility.ValidNameRegex). Spec rejects other characters.
                    textField.AllowedChars = CharacterCardUtility.ValidNameRegex;
                    elements.Add(textField);
                }
            }
        }

        /// <summary>
        /// Gets the title for Dialog_NamePawn (e.g., "Rename Person", "Rename Animal").
        /// </summary>
        public static string GetTitle(Window dialog, Type dialogType)
        {
            FieldInfo renameTextField = dialogType.GetField("renameText", BindingFlags.NonPublic | BindingFlags.Instance);
            if (renameTextField != null)
            {
                object renameObj = renameTextField.GetValue(dialog);
                if (renameObj != null)
                {
                    return renameObj.ToString();
                }
            }
            return null;
        }

        /// <summary>
        /// Gets the description/message for Dialog_NamePawn (parent info for babies).
        /// </summary>
        public static string GetMessage(Window dialog, Type dialogType)
        {
            FieldInfo descTextField = dialogType.GetField("descriptionText", BindingFlags.NonPublic | BindingFlags.Instance);
            if (descTextField != null)
            {
                object descObj = descTextField.GetValue(dialog);
                if (descObj != null)
                {
                    string description = descObj.ToString();

                    // Don't show parent info if both parents are unknown (not useful for adult pawns)
                    // Baby naming dialogs will have at least one known parent
                    if (description.Contains("Unknown") &&
                        description.Count(c => c == '\n') <= 1)  // Only mother/father lines
                    {
                        // Check if BOTH lines contain "Unknown"
                        var lines = description.Split('\n');
                        if (lines.All(line => line.Contains("Unknown")))
                        {
                            return null;  // Don't show useless parent info
                        }
                    }

                    return description;
                }
            }
            return null;
        }

        /// <summary>
        /// Executes the Accept action - builds and applies the name to the pawn.
        /// </summary>
        public static void ExecuteAccept(Window dialog, Type dialogType)
        {
            // Get pawn
            FieldInfo pawnField = dialogType.GetField("pawn", BindingFlags.NonPublic | BindingFlags.Instance);
            Pawn pawn = pawnField?.GetValue(dialog) as Pawn;
            if (pawn == null)
            {
                TolkHelper.Speak("Error: Could not find pawn", SpeechPriority.High);
                return;
            }

            // Call BuildName method
            MethodInfo buildNameMethod = dialogType.GetMethod("BuildName", BindingFlags.NonPublic | BindingFlags.Instance);
            if (buildNameMethod == null)
            {
                TolkHelper.Speak("Error: BuildName method not found", SpeechPriority.High);
                return;
            }

            Name name = buildNameMethod.Invoke(dialog, null) as Name;
            if (name == null || !name.IsValid)
            {
                TolkHelper.Speak("Name is invalid. Please check the name fields.", SpeechPriority.High);
                return;
            }

            // Apply the name
            pawn.Name = name;
            pawn.babyNamingDeadline = -1;

            // Get nickname for announcement
            string displayName = "";
            if (name is NameTriple triple)
                displayName = triple.Nick;
            else if (name is NameSingle single)
                displayName = single.Name;

            TolkHelper.Speak($"Named {displayName}", SpeechPriority.High);

            // Close the windowless dialog
            WindowlessDialogState.Close();
        }

        /// <summary>
        /// Randomizes the specified text field with a generated name.
        /// </summary>
        /// <param name="dialog">The Dialog_NamePawn instance</param>
        /// <param name="textField">The text field element to randomize</param>
        /// <param name="fieldIndex">Index of the field in the names list (0-based, excluding description element)</param>
        public static void RandomizeField(Window dialog, TextFieldElement textField, int fieldIndex)
        {
            if (dialog == null || textField == null)
                return;

            try
            {
                Type dialogType = dialog.GetType();

                // Get pawn
                var pawnField = dialogType.GetField("pawn", BindingFlags.NonPublic | BindingFlags.Instance);
                Pawn pawn = pawnField?.GetValue(dialog) as Pawn;
                if (pawn == null)
                {
                    TolkHelper.Speak("Could not find pawn");
                    return;
                }

                // Get the names list to find the nameIndex
                var namesField = dialogType.GetField("names", BindingFlags.NonPublic | BindingFlags.Instance);
                var namesList = namesField?.GetValue(dialog) as System.Collections.IList;
                if (namesList == null || fieldIndex < 0 || fieldIndex >= namesList.Count)
                {
                    TolkHelper.Speak("Invalid field index");
                    return;
                }

                // Get the NameContext's nameIndex field (0=first, 1=nick, 2=last)
                object nameContext = namesList[fieldIndex];
                Type nameContextType = nameContext.GetType();
                var nameIndexField = nameContextType.GetField("nameIndex", BindingFlags.Public | BindingFlags.Instance);
                int tripleIndex = nameIndexField != null ? (int)nameIndexField.GetValue(nameContext) : -1;

                if (tripleIndex < 0)
                {
                    TolkHelper.Speak("This field cannot be randomized");
                    return;
                }

                // Generate a new name
                Name genName = PawnBioAndNameGenerator.GeneratePawnName(pawn, NameStyle.Full, null, false, pawn?.genes?.Xenotype);

                string newValue = "";
                if (genName is NameTriple triple)
                {
                    newValue = triple[tripleIndex];
                }
                else if (genName is NameSingle single)
                {
                    newValue = single.Name;
                }

                if (!string.IsNullOrEmpty(newValue))
                {
                    textField.Value = newValue;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    TolkHelper.Speak($"Randomized to {newValue}");
                }
                else
                {
                    TolkHelper.Speak("Could not generate random name");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[NamePawnDialogHelper] Error randomizing: {ex.Message}");
                TolkHelper.Speak("Could not randomize");
            }
        }
    }
}
