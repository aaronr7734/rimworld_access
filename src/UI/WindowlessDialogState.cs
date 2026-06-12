using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

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
        // Embedded text-field controller used by every TextFieldElement in any
        // intercepted dialog. Spec is derived from the dialog type (via
        // RimWorldDialogIntrospector) plus the per-element max length.
        // Embedded text-field controller used by every TextFieldElement in any
        // intercepted dialog. Spec is derived from the dialog type (via
        // RimWorldDialogIntrospector) plus the per-element max length.
        private static readonly TextInputController dialogController = new TextInputController();

        /// <summary>
        /// Tracks the frame number when the dialog was opened.
        /// Used to skip input handling on the same frame to prevent the triggering key
        /// (e.g., Escape) from immediately activating a button in the dialog.
        /// </summary>
        private static int openedOnFrame = -1;

        /// <summary>
        /// Tracks the frame number when the dialog was closed.
        /// Used to prevent the same Escape key from re-triggering actions (like DoBack).
        /// </summary>
        private static int closedOnFrame = -1;

        public static bool IsActive => currentDialog != null;
        public static bool IsEditingTextField => editingElement != null;
        public static string CurrentDialogTypeName => currentDialog?.GetType().Name ?? "null";

        /// <summary>
        /// The dialog window currently presented windowlessly, or null. Intercepted dialogs never
        /// enter the WindowStack, so states that need to track a specific dialog's lifetime
        /// (e.g. PlantTargetingState watching a planting confirmation) must identify it here.
        /// </summary>
        public static Window CurrentDialog => currentDialog;

        /// <summary>
        /// Returns true if the dialog was closed on the current frame.
        /// Used to prevent Escape from immediately triggering other actions.
        /// </summary>
        public static bool WasClosedThisFrame => closedOnFrame == UnityEngine.Time.frameCount;

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

            // Track the frame when opened to skip input on this frame
            // This prevents the triggering key (e.g., Escape) from immediately activating Cancel
            openedOnFrame = Time.frameCount;

            // Track if this dialog should force pause - used by WindowsForcePausePatch
            // to prevent game systems from thinking no modal dialog is open
            ShouldForcePause = dialog.forcePause;

            elements = DialogElementExtractor.ExtractElements(dialog);

            // Insert dialog description as first element so users can re-read it
            string title = DialogElementExtractor.GetDialogTitle(dialog)?.StripTags() ?? "";
            string message = DialogElementExtractor.GetDialogMessage(dialog)?.StripTags() ?? "";
            string descriptionText = BuildDescriptionText(title, message);

            if (!string.IsNullOrEmpty(descriptionText))
            {
                elements.Insert(0, new DialogDescriptionElement { Title = title, Message = message });
            }

            // Start on the first action, skipping the description if one was added
            // Description is only added if descriptionText is not empty (line 62-65)
            // For Dialog_RenameArea and similar dialogs with no title/message,
            // the first element IS the action (text field), not the description
            bool hasDescription = !string.IsNullOrEmpty(descriptionText);
            selectedIndex = (hasDescription && elements.Count > 1) ? 1 : 0;
            editingElement = null;

            // Announce dialog opened with full description
            string announcement = BuildDialogAnnouncement(title, message);
            TolkHelper.SpeakData(announcement, SpeechPriority.High);

            // Announce the focused element (first action, not the description)
            if (elements.Count > 1)
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
            // Null currentDialog first to ensure IsActive returns false immediately,
            // preventing stale state from blocking arrow keys via SuppressMapNavigation
            var dialogToClose = currentDialog;
            currentDialog = null;

            if (dialogToClose != null)
            {
                // Remove from window stack if still present
                Find.WindowStack.TryRemove(dialogToClose, doCloseSound: false);
            }

            elements.Clear();
            selectedIndex = 0;
            editingElement = null;
            ShouldForcePause = false;
            openedOnFrame = -1;
            closedOnFrame = UnityEngine.Time.frameCount;

            // Notify any state that was waiting for a specific dialog to close.
            // DialogInterceptionPatch prevents Dialog_MessageBox from being added to
            // the WindowStack, so LoadDialogWatcherPatch (which hooks Window.PostClose)
            // never fires for those. Calling these here lets each state's pending
            // weak-ref check fire on the dialog it was actually waiting for.
            if (dialogToClose != null)
            {
                WindowlessSaveMenuState.OnWindowClosed(dialogToClose);
                WindowlessPauseMenuState.OnWindowClosed(dialogToClose);
            }
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

            // Skip input handling on the same frame the dialog was opened
            // This prevents the triggering key (e.g., Escape) from immediately activating Cancel
            if (Time.frameCount == openedOnFrame)
                return false;

            KeyCode key = evt.keyCode;

            // If we're editing a text field, handle differently
            if (editingElement != null && editingElement is TextFieldElement textField)
            {
                return HandleTextFieldInput(textField, evt);
            }

            // Alt+R: Randomize current field (for Dialog_NamePawn)
            if (key == KeyCode.R && evt.alt)
            {
                if (currentDialog?.GetType().Name == "Dialog_NamePawn" &&
                    selectedIndex >= 0 && selectedIndex < elements.Count &&
                    elements[selectedIndex] is TextFieldElement nameField)
                {
                    // Calculate field index (accounting for description element at index 0)
                    int fieldIndex = selectedIndex;
                    if (elements.Count > 0 && elements[0] is DialogDescriptionElement)
                    {
                        fieldIndex = selectedIndex - 1;
                    }

                    NamePawnDialogHelper.RandomizeField(currentDialog, nameField, fieldIndex);
                    evt.Use();
                    return true;
                }
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
            else if (key == KeyCode.LeftArrow)
            {
                SelectPrevious();
                evt.Use();
                return true;
            }
            else if (key == KeyCode.RightArrow)
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

                    // If cancelling a confirmation dialog over caravan formation, reset the send attempted flag
                    // so that a subsequent Escape will properly announce "Caravan formation cancelled"
                    if (CaravanFormationState.IsActive)
                    {
                        CaravanFormationState.ResetSendAttempted();
                    }
                }
                Close();
                evt.Use();
                return true;
            }

            // Consume any remaining KeyDown events so global shortcuts (T for time,
            // 1-7 for tile info, A for architect, etc.) can't fire while a windowless
            // dialog is modal. Polling patches like DetailInfoPatch guard themselves
            // via WindowlessDialogState.IsActive; this handles the IMGUI side.
            evt.Use();
            return true;
        }

        private static bool HandleTextFieldInput(TextFieldElement textField, Event evt)
        {
            // Embedded mode (modal: false): controller doesn't register with TextInputManager,
            // so Up/Down etc. flow back to the dialog's main HandleInput for navigation.
            // Delete does per-char delete (consistent with every other text field in the mod);
            // to clear the whole field, use Ctrl+A then Delete.
            if (dialogController.HandleEvent(evt))
            {
                evt.Use();
                return true;
            }
            return false;
        }

        private static void SelectNext()
        {
            if (elements.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, elements.Count);
            AnnounceCurrentElement();
        }

        private static void SelectPrevious()
        {
            if (elements.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, elements.Count);
            AnnounceCurrentElement();
        }

        private static void ActivateCurrentElement()
        {
            if (selectedIndex < 0 || selectedIndex >= elements.Count)
                return;

            DialogElement element = elements[selectedIndex];

            if (element is DialogDescriptionElement descElement)
            {
                // Re-read the description
                TolkHelper.SpeakData(descElement.GetAnnouncement());
            }
            else if (element is ButtonElement button)
            {
                // Save reference to detect re-entrant dialog opening.
                // A button's action may open a new dialog that gets intercepted by
                // DialogInterceptionPatch, which calls WindowlessDialogState.Open()
                // re-entrantly (e.g., "Name Baby" DiaOption opens Dialog_NamePawn).
                // In that case, currentDialog changes during Execute() and we must
                // NOT close the newly opened dialog.
                Window dialogBeforeExecute = currentDialog;

                button.Execute();

                // If the button's action opened a new dialog re-entrantly,
                // currentDialog will have changed. Don't close it.
                if (currentDialog != dialogBeforeExecute)
                    return;

                // If it's a confirm or close button, close the dialog
                if (button.IsConfirm || button.IsClose)
                {
                    Close();
                }
                // If this button navigates to a sub-dialog, refresh to show new options
                else if (button.NavigatesToSubDialog)
                {
                    RefreshDialogElements();
                }
            }
            else if (element is TextFieldElement textField)
            {
                editingElement = textField;
                // Spec from the underlying RimWorld dialog type (MaxNameLength / FirstCharLimit /
                // IsValidName via reflection). Fall back to the per-element MaxLength if the
                // introspector doesn't recognize the dialog type.
                var spec = TextFieldSpec.ForRimWorldDialog(currentDialog);
                bool needsTighterMax = spec.MaxLength == null || textField.MaxLength < (spec.MaxLength ?? int.MaxValue);
                bool needsCharsOverride = textField.AllowedChars != null && spec.AllowedChars == null;
                if (needsTighterMax || needsCharsOverride)
                {
                    spec = new TextFieldSpec(
                        labelKey: spec.LabelKey,
                        maxLength: needsTighterMax ? textField.MaxLength : spec.MaxLength,
                        minLength: spec.MinLength,
                        allowedChars: textField.AllowedChars ?? spec.AllowedChars,
                        forbidGrammarSpecials: spec.ForbidGrammarSpecials,
                        mustBeFilename: spec.MustBeFilename,
                        customValidator: spec.CustomValidator);
                }
                var captured = textField;
                dialogController.Begin(
                    textField.Value ?? string.Empty,
                    spec,
                    val => { captured.Value = val; editingElement = null; },
                    () => { editingElement = null; },
                    replaceOnType: true,
                    modal: false,
                    // The dialog field's own caption ("First name", etc.); the introspected spec
                    // only carries a generic label, so pass the per-field label for the prompt and
                    // the commit announcement.
                    displayLabel: textField.Label);
            }
        }

        /// <summary>
        /// Refreshes the dialog elements after navigating to a sub-dialog.
        /// Used when a DiaOption has a link to another DiaNode.
        /// </summary>
        private static void RefreshDialogElements()
        {
            if (currentDialog == null)
                return;

            // Re-extract elements from the dialog (now showing new DiaNode)
            elements = DialogElementExtractor.ExtractElements(currentDialog);

            // Get the new dialog text
            string title = DialogElementExtractor.GetDialogTitle(currentDialog)?.StripTags() ?? "";
            string message = DialogElementExtractor.GetDialogMessage(currentDialog)?.StripTags() ?? "";
            string descriptionText = BuildDescriptionText(title, message);

            // Insert description as first element
            if (!string.IsNullOrEmpty(descriptionText))
            {
                elements.Insert(0, new DialogDescriptionElement { Title = title, Message = message });
            }

            // Reset to first action (skip description)
            bool hasDescription = !string.IsNullOrEmpty(descriptionText);
            selectedIndex = (hasDescription && elements.Count > 1) ? 1 : 0;

            // Announce the new dialog state
            string announcement = BuildDialogAnnouncement(title, message);
            TolkHelper.SpeakData(announcement, SpeechPriority.High);

            // Announce the focused element
            if (elements.Count > 1)
            {
                AnnounceCurrentElement();
            }
        }

        private static void AnnounceCurrentElement()
        {
            if (selectedIndex < 0 || selectedIndex >= elements.Count)
                return;

            DialogElement element = elements[selectedIndex];

            // Description element doesn't show position
            if (element is DialogDescriptionElement)
            {
                TolkHelper.SpeakData(element.GetAnnouncement());
                return;
            }

            // For action elements, show position relative to action count (excluding description)
            // Description is always at index 0 if present, so actions start at index 1
            bool hasDescription = elements.Count > 0 && elements[0] is DialogDescriptionElement;
            int actionIndex = hasDescription ? selectedIndex - 1 : selectedIndex;
            int actionCount = hasDescription ? elements.Count - 1 : elements.Count;

            string position = MenuHelper.FormatPosition(actionIndex, actionCount);
            string baseAnnouncement = element.GetAnnouncement().TrimEnd('.', ' ');
            string announcement = string.IsNullOrEmpty(position)
                ? element.GetAnnouncement()
                : $"{baseAnnouncement}. {position}";
            TolkHelper.SpeakData(announcement);
        }

        private static string BuildDescriptionText(string title, string message)
        {
            string text = "";
            if (!string.IsNullOrEmpty(title))
            {
                text += title;
            }
            if (!string.IsNullOrEmpty(message))
            {
                if (!string.IsNullOrEmpty(text))
                    text += ". ";
                text += message;
            }
            return text;
        }

        private static string BuildDialogAnnouncement(string title, string message)
        {
            string announcement = (string)"RimWorldAccess.UI.Dialog.Opened".Translate() + " ";

            if (!string.IsNullOrEmpty(title))
            {
                announcement += title + ". ";
            }

            if (!string.IsNullOrEmpty(message))
            {
                announcement += message;
                // Only add period if message doesn't already end with punctuation
                if (!message.EndsWith(".") && !message.EndsWith("!") && !message.EndsWith("?"))
                {
                    announcement += ".";
                }
                announcement += " ";
            }

            // Subtract 1 from count since element 0 is the description, not an action
            int actionCount = elements.Count > 0 ? elements.Count - 1 : 0;
            announcement += "RimWorldAccess.UI.Dialog.NavInstructions".Translate(actionCount);

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
        /// <summary>
        /// True if this button navigates to a sub-dialog (has link or linkLateBind).
        /// When activated, the dialog should refresh to show the new node instead of closing.
        /// </summary>
        public bool NavigatesToSubDialog { get; set; }

        public override string GetAnnouncement()
        {
            string announcement = "RimWorldAccess.UI.Dialog.ButtonAnnouncement".Translate(Label);

            if (Disabled && !string.IsNullOrEmpty(DisabledReason))
            {
                announcement = "RimWorldAccess.UI.Dialog.ButtonDisabledSuffix".Translate(announcement, DisabledReason);
            }

            return announcement;
        }

        public void Execute()
        {
            if (Disabled)
            {
                TolkHelper.Speak("RimWorldAccess.UI.Dialog.ButtonCannotActivate".Loc(DisabledReason));
                return;
            }

            TolkHelper.Speak("RimWorldAccess.UI.Dialog.ButtonExecuted".Loc(Label));
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

        /// <summary>
        /// Optional per-field allowed-characters regex. Set by callers like
        /// <see cref="NamePawnDialogHelper"/> to enforce vanilla constraints
        /// (e.g. <c>CharacterCardUtility.ValidNameRegex</c> on pawn names).
        /// Null = no restriction.
        /// </summary>
        public System.Text.RegularExpressions.Regex AllowedChars { get; set; }

        public TextFieldElement(string label, string initialValue, Action<string> onValueChanged)
        {
            Label = label;
            backingValue = initialValue;
            this.onValueChanged = onValueChanged;
        }

        public override string GetAnnouncement()
        {
            string valueAnnouncement = string.IsNullOrEmpty(Value)
                ? "RimWorldAccess.UI.Dialog.TextFieldEmpty".Translate().ToString()
                : Value;
            return "RimWorldAccess.UI.Dialog.TextField".Translate(Label, valueAnnouncement);
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
            // Use the Label property (inherited from DialogElement) if available
            if (!string.IsNullOrEmpty(Label))
            {
                return (string)"RimWorldAccess.UI.Dialog.FieldCannotBeEdited".Translate(Label, Text);
            }
            return Text;
        }
    }

    /// <summary>
    /// Represents the dialog description (title + message) as a navigable element.
    /// Allows users to re-read the dialog text by navigating to this element.
    /// </summary>
    public class DialogDescriptionElement : DialogElement
    {
        public string Title { get; set; }
        public string Message { get; set; }

        public override string GetAnnouncement()
        {
            string text = (string)"RimWorldAccess.UI.Dialog.DescriptionLabel".Translate() + " ";
            if (!string.IsNullOrEmpty(Title))
            {
                text += Title;
            }
            if (!string.IsNullOrEmpty(Message))
            {
                if (!string.IsNullOrEmpty(Title))
                    text += ". ";
                text += Message;
            }
            return text;
        }
    }
}
