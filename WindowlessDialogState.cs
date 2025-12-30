using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a windowless, keyboard-accessible dialog system.
    /// Replaces visual dialogs with screen reader announcements and keyboard navigation.
    /// </summary>
    public static class WindowlessDialogState
    {
        private static Window currentDialog;
        private static List<DialogElement> elements = new List<DialogElement>();
        private static int selectedIndex = 0;
        private static DialogElement editingElement = null;

        public static bool IsActive => currentDialog != null;
        public static bool IsEditingTextField => editingElement != null;

        /// <summary>
        /// Tracks whether the current intercepted dialog has forcePause set.
        /// Used by WindowsForcePausePatch to maintain game pause behavior.
        /// </summary>
        public static bool ShouldForcePause { get; private set; }

        /// <summary>
        /// Opens a windowless version of the given dialog.
        /// </summary>
        public static void Open(Window dialog)
        {
            if (dialog == null)
                return;

            Close();

            // Pause the game when a dialog opens
            if (Current.ProgramState == ProgramState.Playing && Find.TickManager != null)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            }

            // Close any open menus that might interfere
            CloseActiveMenus();

            currentDialog = dialog;

            // Track if this dialog should force pause - used by WindowsForcePausePatch
            // to prevent game systems from thinking no modal dialog is open
            ShouldForcePause = dialog.forcePause;

            elements = DialogElementExtractor.ExtractElements(dialog);
            selectedIndex = 0;
            editingElement = null;

            // Announce dialog opened
            string announcement = BuildDialogAnnouncement();
            TolkHelper.Speak(announcement, SpeechPriority.High);

            // Announce first element
            if (elements.Count > 0)
            {
                AnnounceCurrentElement();
            }
        }

        /// <summary>
        /// Closes any active menus that might interfere with dialog navigation.
        /// </summary>
        private static void CloseActiveMenus()
        {
            // Close architect menu
            if (ArchitectState.IsActive)
            {
                Find.DesignatorManager?.Deselect();
            }

            // Close zone creation mode
            if (ZoneCreationState.IsInCreationMode)
            {
                // Zone creation will be blocked by MapNavigationState.SuppressMapNavigation
                // which checks WindowlessDialogState.IsActive
            }

            // Most other states will be automatically blocked by their checks for
            // WindowlessDialogState.IsActive or will be suppressed by MapNavigationState
            // No need to explicitly close them
        }

        /// <summary>
        /// Closes the current windowless dialog.
        /// </summary>
        public static void Close()
        {
            if (currentDialog != null)
            {
                // Remove from window stack if still present
                Find.WindowStack.TryRemove(currentDialog, doCloseSound: false);
                currentDialog = null;
            }

            elements.Clear();
            selectedIndex = 0;
            editingElement = null;
            ShouldForcePause = false;
        }

        /// <summary>
        /// Handles keyboard input for the windowless dialog.
        /// Returns true if the event was consumed.
        /// </summary>
        public static bool HandleInput(Event evt)
        {
            if (!IsActive)
                return false;

            if (evt.type != EventType.KeyDown)
                return false;

            KeyCode key = evt.keyCode;

            // If we're editing a text field, handle differently
            if (editingElement != null && editingElement is TextFieldElement textField)
            {
                return HandleTextFieldInput(textField, evt);
            }

            // Navigation
            if (key == KeyCode.UpArrow)
            {
                SelectPrevious();
                evt.Use();
                return true;
            }
            else if (key == KeyCode.DownArrow)
            {
                SelectNext();
                evt.Use();
                return true;
            }
            else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                ActivateCurrentElement();
                evt.Use();
                return true;
            }
            else if (key == KeyCode.Escape)
            {
                // Try to find cancel button, otherwise just close
                ButtonElement cancelButton = elements.Find(e => e is ButtonElement btn && btn.IsCancel) as ButtonElement;
                if (cancelButton != null)
                {
                    cancelButton.Execute();
                }
                Close();
                evt.Use();
                return true;
            }

            return false;
        }

        private static bool HandleTextFieldInput(TextFieldElement textField, Event evt)
        {
            KeyCode key = evt.keyCode;

            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                // Close editing mode
                editingElement = null;
                TolkHelper.Speak($"Editing complete. Current value: {textField.Value}");
                evt.Use();
                return true;
            }
            else if (key == KeyCode.Escape)
            {
                // Cancel editing
                editingElement = null;
                TolkHelper.Speak("Editing cancelled");
                evt.Use();
                return true;
            }
            else if (key == KeyCode.Backspace)
            {
                if (textField.Value.Length > 0)
                {
                    textField.Value = textField.Value.Substring(0, textField.Value.Length - 1);
                    TolkHelper.Speak(textField.Value.Length > 0 ? textField.Value[textField.Value.Length - 1].ToString() : "Empty");
                }
                evt.Use();
                return true;
            }
            else if (key == KeyCode.Delete)
            {
                textField.Value = "";
                TolkHelper.Speak("Cleared");
                evt.Use();
                return true;
            }
            else if (evt.character != '\0' && evt.character != '\n' && evt.character != '\r')
            {
                // Add character to text field
                if (textField.Value.Length < textField.MaxLength)
                {
                    textField.Value += evt.character;
                    TolkHelper.Speak(evt.character.ToString());
                }
                evt.Use();
                return true;
            }

            return false;
        }

        private static void SelectNext()
        {
            if (elements.Count == 0)
                return;

            selectedIndex = (selectedIndex + 1) % elements.Count;
            AnnounceCurrentElement();
        }

        private static void SelectPrevious()
        {
            if (elements.Count == 0)
                return;

            selectedIndex--;
            if (selectedIndex < 0)
                selectedIndex = elements.Count - 1;

            AnnounceCurrentElement();
        }

        private static void ActivateCurrentElement()
        {
            if (selectedIndex < 0 || selectedIndex >= elements.Count)
                return;

            DialogElement element = elements[selectedIndex];

            if (element is ButtonElement button)
            {
                button.Execute();

                // If it's a confirm or close button, close the dialog
                if (button.IsConfirm || button.IsClose)
                {
                    Close();
                }
            }
            else if (element is TextFieldElement textField)
            {
                // Enter editing mode
                editingElement = textField;
                TolkHelper.Speak($"Editing {textField.Label}. Current value: {textField.Value}. Type to change, Enter to confirm, Escape to cancel.");
            }
        }

        private static void AnnounceCurrentElement()
        {
            if (selectedIndex < 0 || selectedIndex >= elements.Count)
                return;

            DialogElement element = elements[selectedIndex];
            string announcement = $"{selectedIndex + 1} of {elements.Count}. {element.GetAnnouncement()}";
            TolkHelper.Speak(announcement);
        }

        private static string BuildDialogAnnouncement()
        {
            string title = DialogElementExtractor.GetDialogTitle(currentDialog);
            string message = DialogElementExtractor.GetDialogMessage(currentDialog);

            string announcement = "Dialog opened. ";

            if (!string.IsNullOrEmpty(title))
            {
                announcement += title + ". ";
            }

            if (!string.IsNullOrEmpty(message))
            {
                announcement += message + ". ";
            }

            announcement += $"{elements.Count} elements. Use arrow keys to navigate, Enter to activate, Escape to cancel.";

            return announcement;
        }

        public static Window GetCurrentDialog()
        {
            return currentDialog;
        }
    }

    /// <summary>
    /// Base class for dialog elements that can be interacted with.
    /// </summary>
    public abstract class DialogElement
    {
        public string Label { get; set; }

        public abstract string GetAnnouncement();
    }

    /// <summary>
    /// Represents a button in the dialog.
    /// </summary>
    public class ButtonElement : DialogElement
    {
        public Action Action { get; set; }
        public bool IsConfirm { get; set; }
        public bool IsCancel { get; set; }
        public bool IsClose { get; set; }
        public bool Disabled { get; set; }
        public string DisabledReason { get; set; }

        public override string GetAnnouncement()
        {
            string announcement = $"Button: {Label}";

            if (Disabled && !string.IsNullOrEmpty(DisabledReason))
            {
                announcement += $" (Disabled: {DisabledReason})";
            }

            return announcement;
        }

        public void Execute()
        {
            if (Disabled)
            {
                TolkHelper.Speak($"Cannot activate: {DisabledReason}");
                return;
            }

            TolkHelper.Speak($"Activated {Label}");
            Action?.Invoke();
        }
    }

    /// <summary>
    /// Represents a text field in the dialog.
    /// </summary>
    public class TextFieldElement : DialogElement
    {
        private string backingValue;
        private Action<string> onValueChanged;

        public string Value
        {
            get => backingValue;
            set
            {
                backingValue = value;
                onValueChanged?.Invoke(value);
            }
        }

        public int MaxLength { get; set; } = 1000;

        public TextFieldElement(string label, string initialValue, Action<string> onValueChanged)
        {
            Label = label;
            backingValue = initialValue;
            this.onValueChanged = onValueChanged;
        }

        public override string GetAnnouncement()
        {
            string valueAnnouncement = string.IsNullOrEmpty(Value) ? "Empty" : Value;
            return $"Text field: {Label}. Current value: {valueAnnouncement}. Press Enter to edit.";
        }
    }

    /// <summary>
    /// Represents a label or message in the dialog.
    /// </summary>
    public class LabelElement : DialogElement
    {
        public string Text { get; set; }

        public override string GetAnnouncement()
        {
            return $"Label: {Text}";
        }
    }
}
