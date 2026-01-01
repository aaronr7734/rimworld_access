using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;
using System.Reflection;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(Dialog_GiveName))]
    [HarmonyPatch("DoWindowContents")]
    public class GiveNameDialogPatch
    {
        private static readonly Color HighlightColor = new Color(1f, 1f, 0f, 0.5f);

        // Postfix to handle keyboard navigation and clipboard reading
        static void Postfix(Dialog_GiveName __instance, Rect rect)
        {
            // Initialize navigation state for this dialog
            GiveNameDialogState.Initialize(__instance);

            // Get private fields using reflection
            FieldInfo nameMessageKeyField = typeof(Dialog_GiveName).GetField("nameMessageKey", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo secondNameMessageKeyField = typeof(Dialog_GiveName).GetField("secondNameMessageKey", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo useSecondNameField = typeof(Dialog_GiveName).GetField("useSecondName", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo suggestingPawnField = typeof(Dialog_GiveName).GetField("suggestingPawn", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo nameGeneratorField = typeof(Dialog_GiveName).GetField("nameGenerator", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo secondNameGeneratorField = typeof(Dialog_GiveName).GetField("secondNameGenerator", BindingFlags.NonPublic | BindingFlags.Instance);

            if (nameMessageKeyField == null || useSecondNameField == null || suggestingPawnField == null)
            {
                Log.Message("Failed to get required fields from Dialog_GiveName");
                return;
            }

            string nameMessageKey = (string)nameMessageKeyField.GetValue(__instance);
            string secondNameMessageKey = (string)secondNameMessageKeyField?.GetValue(__instance);
            bool useSecondName = (bool)useSecondNameField.GetValue(__instance);
            Pawn suggestingPawn = (Pawn)suggestingPawnField.GetValue(__instance);
            bool hasNameGenerator = nameGeneratorField.GetValue(__instance) != null;
            bool hasSecondNameGenerator = secondNameGeneratorField?.GetValue(__instance) != null;

            // Announce the dialog content on first display
            if (!GiveNameDialogState.HasAnnounced())
            {
                string firstPrompt = nameMessageKey.Translate(suggestingPawn.LabelShort, suggestingPawn).CapitalizeFirst();
                string announcement = "Name dialog. " + firstPrompt;

                if (useSecondName)
                {
                    string secondPrompt = secondNameMessageKey.Translate(suggestingPawn.LabelShort, suggestingPawn);
                    announcement += ". " + secondPrompt;
                }

                announcement += ". Use Tab to navigate between fields. Press Enter to confirm.";
                TolkHelper.Speak(announcement);
                GiveNameDialogState.MarkAsAnnounced();
                Log.Message($"Dialog announced: {announcement.Substring(0, Mathf.Min(100, announcement.Length))}...");
            }

            // Handle keyboard navigation
            if (Event.current.type == EventType.KeyDown)
            {
                int focusIndex = GiveNameDialogState.GetFocusIndex();

                if (Event.current.keyCode == KeyCode.Tab)
                {
                    if (Event.current.shift)
                    {
                        GiveNameDialogState.MovePrevious(useSecondName);
                    }
                    else
                    {
                        GiveNameDialogState.MoveNext(useSecondName);
                    }

                    focusIndex = GiveNameDialogState.GetFocusIndex();
                    AnnounceCurrentFocus(__instance, focusIndex, useSecondName, hasNameGenerator, hasSecondNameGenerator);
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.UpArrow)
                {
                    GiveNameDialogState.MovePrevious(useSecondName);
                    focusIndex = GiveNameDialogState.GetFocusIndex();
                    AnnounceCurrentFocus(__instance, focusIndex, useSecondName, hasNameGenerator, hasSecondNameGenerator);
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.DownArrow)
                {
                    GiveNameDialogState.MoveNext(useSecondName);
                    focusIndex = GiveNameDialogState.GetFocusIndex();
                    AnnounceCurrentFocus(__instance, focusIndex, useSecondName, hasNameGenerator, hasSecondNameGenerator);
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                {
                    // Handle Enter key on focused element
                    ActivateFocusedElement(__instance, focusIndex, useSecondName, hasNameGenerator, hasSecondNameGenerator);
                    Event.current.Use();
                }
            }

            // Draw highlight on focused element
            DrawFocusHighlight(__instance, rect, useSecondName, hasNameGenerator, hasSecondNameGenerator);
        }

        private static void ActivateFocusedElement(Dialog_GiveName dialog, int focusIndex, bool useSecondName, bool hasNameGenerator, bool hasSecondNameGenerator)
        {
            FieldInfo curNameField = typeof(Dialog_GiveName).GetField("curName", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo curSecondNameField = typeof(Dialog_GiveName).GetField("curSecondName", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo nameGeneratorField = typeof(Dialog_GiveName).GetField("nameGenerator", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo secondNameGeneratorField = typeof(Dialog_GiveName).GetField("secondNameGenerator", BindingFlags.NonPublic | BindingFlags.Instance);

            if (!useSecondName)
            {
                // Simple mode: 0 = text field, 1 = randomize button, 2 = OK button
                switch (focusIndex)
                {
                    case 0:
                        // Text field - do nothing, let native text input handle it
                        Log.Message("Enter pressed on text field (no action)");
                        break;
                    case 1:
                        if (hasNameGenerator)
                        {
                            // Randomize button
                            var generator = (System.Func<string>)nameGeneratorField.GetValue(dialog);
                            if (generator != null)
                            {
                                string newName = generator();
                                curNameField.SetValue(dialog, newName);
                                TolkHelper.Speak($"Randomized: {newName}");
                                Log.Message($"Randomized name to: {newName}");
                            }
                        }
                        else
                        {
                            // OK button
                            SubmitDialog(dialog, useSecondName);
                        }
                        break;
                    case 2:
                        // OK button
                        SubmitDialog(dialog, useSecondName);
                        break;
                }
            }
            else
            {
                // Two-name mode: 0 = first text field, 1 = first randomize, 2 = second text field, 3 = second randomize, 4 = OK
                switch (focusIndex)
                {
                    case 0:
                    case 2:
                        // Text fields - do nothing, let native text input handle it
                        Log.Message("Enter pressed on text field (no action)");
                        break;
                    case 1:
                        // First randomize button
                        var generator1 = (System.Func<string>)nameGeneratorField.GetValue(dialog);
                        if (generator1 != null)
                        {
                            string newName = generator1();
                            curNameField.SetValue(dialog, newName);
                            TolkHelper.Speak($"Randomized first name: {newName}");
                            Log.Message($"Randomized first name to: {newName}");
                        }
                        break;
                    case 3:
                        // Second randomize button
                        var generator2 = (System.Func<string>)secondNameGeneratorField.GetValue(dialog);
                        if (generator2 != null)
                        {
                            string newName = generator2();
                            curSecondNameField.SetValue(dialog, newName);
                            TolkHelper.Speak($"Randomized second name: {newName}");
                            Log.Message($"Randomized second name to: {newName}");
                        }
                        break;
                    case 4:
                        // OK button
                        SubmitDialog(dialog, useSecondName);
                        break;
                }
            }
        }

        private static void SubmitDialog(Dialog_GiveName dialog, bool useSecondName)
        {
            // Get validation methods
            MethodInfo isValidNameMethod = typeof(Dialog_GiveName).GetMethod("IsValidName", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo isValidSecondNameMethod = typeof(Dialog_GiveName).GetMethod("IsValidSecondName", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo namedMethod = typeof(Dialog_GiveName).GetMethod("Named", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo namedSecondMethod = typeof(Dialog_GiveName).GetMethod("NamedSecond", BindingFlags.NonPublic | BindingFlags.Instance);

            // Get fields
            FieldInfo curNameField = typeof(Dialog_GiveName).GetField("curName", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo curSecondNameField = typeof(Dialog_GiveName).GetField("curSecondName", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo gainedNameMessageKeyField = typeof(Dialog_GiveName).GetField("gainedNameMessageKey", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo invalidNameMessageKeyField = typeof(Dialog_GiveName).GetField("invalidNameMessageKey", BindingFlags.NonPublic | BindingFlags.Instance);

            string text = ((string)curNameField.GetValue(dialog))?.Trim();
            string text2 = ((string)curSecondNameField?.GetValue(dialog))?.Trim();

            // Validate names
            bool isValid = (bool)isValidNameMethod.Invoke(dialog, new object[] { text });
            bool isSecondValid = !useSecondName || (bool)isValidSecondNameMethod.Invoke(dialog, new object[] { text2 });

            if (isValid && isSecondValid)
            {
                if (useSecondName)
                {
                    namedMethod.Invoke(dialog, new object[] { text });
                    namedSecondMethod.Invoke(dialog, new object[] { text2 });
                    string gainedNameMessageKey = (string)gainedNameMessageKeyField.GetValue(dialog);
                    Messages.Message(gainedNameMessageKey.Translate(text, text2), MessageTypeDefOf.TaskCompletion, historical: false);
                    TolkHelper.Speak($"Names accepted: {text}, {text2}");
                }
                else
                {
                    namedMethod.Invoke(dialog, new object[] { text });
                    string gainedNameMessageKey = (string)gainedNameMessageKeyField.GetValue(dialog);
                    Messages.Message(gainedNameMessageKey.Translate(text), MessageTypeDefOf.TaskCompletion, historical: false);
                    TolkHelper.Speak($"Name accepted: {text}");
                }

                Find.WindowStack.TryRemove(dialog);
                Log.Message("Dialog submitted successfully");
            }
            else
            {
                string invalidNameMessageKey = (string)invalidNameMessageKeyField.GetValue(dialog);
                Messages.Message(invalidNameMessageKey.Translate(), MessageTypeDefOf.RejectInput, historical: false);
                TolkHelper.Speak("Invalid name");
                Log.Message("Dialog submission rejected: invalid name");
            }
        }

        private static void AnnounceCurrentFocus(Dialog_GiveName dialog, int focusIndex, bool useSecondName, bool hasNameGenerator, bool hasSecondNameGenerator)
        {
            FieldInfo curNameField = typeof(Dialog_GiveName).GetField("curName", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo curSecondNameField = typeof(Dialog_GiveName).GetField("curSecondName", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo nameMessageKeyField = typeof(Dialog_GiveName).GetField("nameMessageKey", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo secondNameMessageKeyField = typeof(Dialog_GiveName).GetField("secondNameMessageKey", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo suggestingPawnField = typeof(Dialog_GiveName).GetField("suggestingPawn", BindingFlags.NonPublic | BindingFlags.Instance);

            string curName = (string)curNameField?.GetValue(dialog);
            string curSecondName = (string)curSecondNameField?.GetValue(dialog);
            string nameMessageKey = (string)nameMessageKeyField?.GetValue(dialog);
            string secondNameMessageKey = (string)secondNameMessageKeyField?.GetValue(dialog);
            Pawn suggestingPawn = (Pawn)suggestingPawnField?.GetValue(dialog);

            string announcement = "";

            if (!useSecondName)
            {
                // Simple mode: 0 = text field, 1 = randomize button, 2 = OK button
                switch (focusIndex)
                {
                    case 0:
                        string prompt = nameMessageKey.Translate(suggestingPawn.LabelShort, suggestingPawn).CapitalizeFirst();
                        announcement = $"Text field: {prompt}. Current value: {curName}";
                        break;
                    case 1:
                        announcement = hasNameGenerator ? "Randomize button" : "OK button";
                        break;
                    case 2:
                        announcement = "OK button";
                        break;
                }
            }
            else
            {
                // Two-name mode: 0 = first text field, 1 = first randomize, 2 = second text field, 3 = second randomize, 4 = OK
                switch (focusIndex)
                {
                    case 0:
                        string prompt1 = nameMessageKey.Translate(suggestingPawn.LabelShort, suggestingPawn).CapitalizeFirst();
                        announcement = $"First text field: {prompt1}. Current value: {curName}";
                        break;
                    case 1:
                        announcement = "Randomize first name button";
                        break;
                    case 2:
                        string prompt2 = secondNameMessageKey.Translate(suggestingPawn.LabelShort, suggestingPawn);
                        announcement = $"Second text field: {prompt2}. Current value: {curSecondName}";
                        break;
                    case 3:
                        announcement = "Randomize second name button";
                        break;
                    case 4:
                        announcement = "OK button";
                        break;
                }
            }

            TolkHelper.Speak(announcement);
            Log.Message($"Focus changed to: {announcement}");
        }

        private static void DrawFocusHighlight(Dialog_GiveName dialog, Rect rect, bool useSecondName, bool hasNameGenerator, bool hasSecondNameGenerator)
        {
            int focusIndex = GiveNameDialogState.GetFocusIndex();
            Rect highlightRect = Rect.zero;
            bool shouldDraw = false;

            if (!useSecondName)
            {
                // Simple mode layout
                switch (focusIndex)
                {
                    case 0: // First text field
                        highlightRect = new Rect(0f, 80f, rect.width / 2f + 70f, 35f);
                        shouldDraw = true;
                        break;
                    case 1: // Randomize button or OK button
                        if (hasNameGenerator)
                        {
                            highlightRect = new Rect(rect.width / 2f + 90f, 80f, rect.width / 2f - 90f, 35f);
                        }
                        else
                        {
                            highlightRect = new Rect(rect.width / 2f + 90f, rect.height - 35f, rect.width / 2f - 90f, 35f);
                        }
                        shouldDraw = true;
                        break;
                    case 2: // OK button
                        highlightRect = new Rect(rect.width / 2f + 90f, rect.height - 35f, rect.width / 2f - 90f, 35f);
                        shouldDraw = true;
                        break;
                }
            }
            else
            {
                // Two-name mode layout
                FieldInfo nameMessageKeyField = typeof(Dialog_GiveName).GetField("nameMessageKey", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo suggestingPawnField = typeof(Dialog_GiveName).GetField("suggestingPawn", BindingFlags.NonPublic | BindingFlags.Instance);
                string nameMessageKey = (string)nameMessageKeyField?.GetValue(dialog);
                Pawn suggestingPawn = (Pawn)suggestingPawnField?.GetValue(dialog);

                float num = 0f;
                string text = nameMessageKey.Translate(suggestingPawn.LabelShort, suggestingPawn).CapitalizeFirst();
                num += Text.CalcHeight(text, rect.width) + 10f;

                switch (focusIndex)
                {
                    case 0: // First text field
                        highlightRect = new Rect(0f, num, rect.width / 2f + 70f, 35f);
                        shouldDraw = true;
                        break;
                    case 1: // First randomize button
                        highlightRect = new Rect(rect.width / 2f + 90f, num, rect.width / 2f - 90f, 35f);
                        shouldDraw = true;
                        break;
                    case 2: // Second text field
                        num += 60f;
                        FieldInfo secondNameMessageKeyField = typeof(Dialog_GiveName).GetField("secondNameMessageKey", BindingFlags.NonPublic | BindingFlags.Instance);
                        string secondNameMessageKey = (string)secondNameMessageKeyField?.GetValue(dialog);
                        text = secondNameMessageKey.Translate(suggestingPawn.LabelShort, suggestingPawn);
                        num += Text.CalcHeight(text, rect.width) + 10f;
                        highlightRect = new Rect(0f, num, rect.width / 2f + 70f, 35f);
                        shouldDraw = true;
                        break;
                    case 3: // Second randomize button
                        num += 60f;
                        FieldInfo secondNameMessageKeyField2 = typeof(Dialog_GiveName).GetField("secondNameMessageKey", BindingFlags.NonPublic | BindingFlags.Instance);
                        string secondNameMessageKey2 = (string)secondNameMessageKeyField2?.GetValue(dialog);
                        text = secondNameMessageKey2.Translate(suggestingPawn.LabelShort, suggestingPawn);
                        num += Text.CalcHeight(text, rect.width) + 10f;
                        highlightRect = new Rect(rect.width / 2f + 90f, num, rect.width / 2f - 90f, 35f);
                        shouldDraw = true;
                        break;
                    case 4: // OK button
                        float num2 = rect.width / 2f - 90f;
                        highlightRect = new Rect(rect.width / 2f - num2 / 2f, rect.height - 35f, num2, 35f);
                        shouldDraw = true;
                        break;
                }
            }

            if (shouldDraw)
            {
                Color prevColor = GUI.color;
                GUI.color = HighlightColor;
                GUI.DrawTexture(highlightRect, BaseContent.WhiteTex);
                GUI.color = prevColor;
            }
        }
    }

    // Separate patch for Window.PostClose to clean up state when dialog closes
    [HarmonyPatch(typeof(Window))]
    [HarmonyPatch("PostClose")]
    public class GiveNameDialogCleanupPatch
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            // Only reset if the closing window is a Dialog_GiveName
            if (__instance is Dialog_GiveName)
            {
                GiveNameDialogState.Reset();
                Log.Message("Dialog_GiveName closed, state reset");
            }
        }
    }
}
