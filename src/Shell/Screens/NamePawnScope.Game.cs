using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Drives the real <see cref="Dialog_NamePawn"/> with an explicit BROWSE / EDIT model.
    /// One content region holds every name row in vanilla's draw order — read-only ones
    /// included (an adult's First/Last, announced "read only") — plus the automatic Buttons
    /// region for the dialog's own Cancel and Accept; Alt+R randomizes the focused row through
    /// the button vanilla draws beside it.
    ///
    /// The mode split exists because a native IMGUI TextField holding Unity keyboard focus
    /// swallows every arrow key and Tab before the dispatcher sees them: an always-focused
    /// field and arrow/Tab navigation are mutually exclusive. BROWSE (default) keeps every
    /// field unfocused so navigation keys reach the dispatcher; Enter opens EDIT on an editable
    /// row, activates a button, or reports a read-only row; Escape cancels the dialog. EDIT
    /// gives exactly one field vanilla's own focus, so typing, caret, selection, clipboard and
    /// IME are native; Enter or Escape returns to browse (the edit is already live); Tab is
    /// vanilla's and <see cref="SyncCursorWhileEditing"/> follows it. Navigation and activation
    /// stand down while editing.
    ///
    /// Vanilla-fighting seams, all guarded: its firstCall auto-focus is undone every browse
    /// pass by <see cref="MaintainFocus"/>; its raw Return and Tab polls at the top of
    /// DoWindowContents are masked by <see cref="NamePawnAcceptGuardPatch"/>; edit-mode Escape
    /// is intercepted by <see cref="NamePawnCancelKeyGuardPatch"/>. <see cref="OwnsCancel"/>
    /// stays the chassis default.
    ///
    /// Rows live in a List of a PRIVATE NESTED NameContext whose type can't be spelled here, so
    /// they are read via plain FieldInfo; textboxName is the raw, language-independent control
    /// key vanilla's own currentControl / focusControlOverride mechanism uses.
    /// </summary>
    public sealed class NamePawnScope : ScreenScope
    {
        private const float FocusRingExpand = 2f;

        private const string RandomizeFieldActionId = "nameDialog.randomizeField";
        private const string AcceptActionId = "nameDialog.accept";

        private static readonly Type nameContextType = AccessTools.Inner(typeof(Dialog_NamePawn), "NameContext");
        private static readonly FieldInfo namesField = AccessTools.Field(typeof(Dialog_NamePawn), "names");
        private static readonly FieldInfo ncCurrentField = AccessTools.Field(nameContextType, "current");
        private static readonly FieldInfo ncLabelField = AccessTools.Field(nameContextType, "label");
        private static readonly FieldInfo ncEditableField = AccessTools.Field(nameContextType, "editable");
        private static readonly FieldInfo ncNameIndexField = AccessTools.Field(nameContextType, "nameIndex");
        private static readonly FieldInfo ncTextboxNameField = AccessTools.Field(nameContextType, "textboxName");
        private static readonly FieldInfo ncSuggestedNamesField = AccessTools.Field(nameContextType, "suggestedNames");

        private static readonly AccessTools.FieldRef<Dialog_NamePawn, string> focusControlOverrideField =
            AccessTools.FieldRefAccess<Dialog_NamePawn, string>("focusControlOverride");
        private static readonly AccessTools.FieldRef<Dialog_NamePawn, string> currentControlField =
            AccessTools.FieldRefAccess<Dialog_NamePawn, string>("currentControl");
        private static readonly AccessTools.FieldRef<Dialog_NamePawn, TaggedString> descriptionTextField =
            AccessTools.FieldRefAccess<Dialog_NamePawn, TaggedString>("descriptionText");
        private static readonly AccessTools.FieldRef<Dialog_NamePawn, string> genderTextField =
            AccessTools.FieldRefAccess<Dialog_NamePawn, string>("genderText");
        private static readonly AccessTools.FieldRef<Dialog_NamePawn, TaggedString> renameTextField =
            AccessTools.FieldRefAccess<Dialog_NamePawn, TaggedString>("renameText");

        private sealed class RowInfo
        {
            public string TextboxName = "";
            public string Label = "";
            public bool Editable;
            public int NameIndex;
            public List<string> SuggestedNames;
            public string Current = "";
            public Rect FieldRect;
            public bool HasButton;
            public int ButtonCaptureIndex = -1;
        }

        private readonly Dialog_NamePawn dialog;
        private readonly List<RowInfo> allRows = new List<RowInfo>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private string lastAnnouncedControl = "";
        private bool pendingAnnounce;
        private bool cursorPlaced;
        private bool editing;
        private int editExitFrame = -1;
        private int pendingRandomizeRow = -1;
        private readonly NativeFieldEcho fieldEcho = new NativeFieldEcho();
        private string echoControl = "";

        private int cancelCaptureIndex = -1;
        private int acceptCaptureIndex = -1;
        private string cancelLabel = "";
        private string acceptLabel = "";

        public NamePawnScope(Window dialog)
        {
            this.dialog = (Dialog_NamePawn)dialog;

            // Claims are gated off while editing so the focused field owns those keys outright.
            Func<bool> browsing = delegate
            {
                return !TextDialogShared.ForeignWindowAbove(this.dialog) && !editing;
            };
            Func<bool> onFieldBrowsing = delegate
            {
                return !TextDialogShared.ForeignWindowAbove(this.dialog) && !editing && CurrentRowIndex() >= 0;
            };

            Claim(SharedMenuGrammar.Cancel, OnCancelBrowse, when: browsing);
            Claim(RandomizeFieldActionId, OnRandomizeField, when: onFieldBrowsing);
            // Escape in EDIT mode is deliberately NOT claimed: it flows to
            // NamePawnCancelKeyGuardPatch, which leaves edit mode instead of closing.
        }

        public override string Name
        {
            get { return "name-pawn-dialog"; }
        }

        public override bool IsModal
        {
            get { return !TextDialogShared.ForeignWindowAbove(dialog); }
        }

        /// <summary>Cancel and Accept are activated by the capture index this scope's own draw bracket recorded them at, and the name rows draw buttons of their own.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>Typing on a non-field row searches; on a field row it opens EDIT — see <see cref="HandleChar"/>.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        internal bool Editing
        {
            get { return editing; }
        }

        /// <summary>EDIT mode is a natively focused vanilla field; the dispatcher's foreign-focus release must stand down.</summary>
        public override bool OwnsNativeTextFocus
        {
            get { return editing; }
        }

        /// <summary>
        /// True on the exact frame this scope left edit mode. Vanilla calls
        /// Window.OnCancelKeyPressed twice for one Escape (decompiled Window.cs:218,293), so the
        /// cancel guard needs this stamp to block the second call's close as well.
        /// </summary>
        internal bool JustLeftEditThisFrame
        {
            get { return editExitFrame == Time.frameCount; }
        }

        /// <summary>
        /// Lands the cursor on the first editable row; stays at row 0 when nothing is editable.
        /// </summary>
        public override void OnFocus()
        {
            base.OnFocus();
            if (cursorPlaced)
            {
                return;
            }
            ListModel rows = Model.Region(0);
            if (rows == null || rows.IsEmpty)
            {
                return;
            }
            cursorPlaced = true;
            for (int i = 0; i < allRows.Count && i < rows.Count; i++)
            {
                if (allRows[i].Editable)
                {
                    rows.MoveTo(i);
                    return;
                }
            }
        }

        // Per-GUI-pass work, driven by the dialog's own draw

        internal void BeginDrawPass()
        {
            // The ring index is read from the pass just finished, before BeginPass clears it.
            TextDialogCapture.BeginPass(FocusedButtonCaptureIndex());
        }

        internal void OnGuiPass()
        {
            TextDialogCapture.EndPass();

            RefreshModel();

            if (pendingRandomizeRow >= 0)
            {
                int row = pendingRandomizeRow;
                pendingRandomizeRow = -1;
                AnnounceRandomizeResult(row);
                // The value was rewritten wholesale and already spoken; re-baseline the echo.
                echoControl = "";
            }

            SyncCursorWhileEditing();
            MaintainFocus();
            ObserveFocusedFieldEcho();
            DrawFieldRing();

            if (pendingAnnounce && !TextDialogShared.ForeignWindowAbove(dialog) && !ShellDispatcherPatch.LegacyKeyboardOverlayActive())
            {
                pendingAnnounce = false;
                AnnounceCurrentItem();
            }
        }

        /// <summary>
        /// Re-reflects the private names list every refresh, zipping the editable subset against
        /// TextFieldCapture.Items and the row-button subset against the head of
        /// ButtonTextCapture.Items in vanilla's own draw order (decompiled NameContext.MakeRow),
        /// so captured rects and labels line up without rect-hit-testing.
        /// </summary>
        protected override void RefreshContent()
        {
            allRows.Clear();
            cancelCaptureIndex = -1;
            acceptCaptureIndex = -1;

            IList raw = (IList)namesField.GetValue(dialog);
            if (raw == null)
            {
                return;
            }

            IReadOnlyList<TextFieldCapture.CapturedField> fields = TextFieldCapture.Items;
            IReadOnlyList<ButtonTextCapture.CapturedButton> buttons = ButtonTextCapture.Items;
            int fieldIndex = 0;
            int buttonIndex = 0;

            for (int i = 0; i < raw.Count; i++)
            {
                object nc = raw[i];
                RowInfo row = new RowInfo();
                row.TextboxName = (string)ncTextboxNameField.GetValue(nc) ?? "";
                object labelObj = ncLabelField.GetValue(nc);
                row.Label = labelObj != null ? labelObj.ToString() : "";
                row.Editable = (bool)ncEditableField.GetValue(nc);
                row.NameIndex = (int)ncNameIndexField.GetValue(nc);
                row.SuggestedNames = (List<string>)ncSuggestedNamesField.GetValue(nc);
                row.Current = (string)ncCurrentField.GetValue(nc) ?? "";

                if (row.Editable)
                {
                    if (fieldIndex < fields.Count)
                    {
                        row.FieldRect = fields[fieldIndex].Rect;
                    }
                    fieldIndex++;

                    // Mirrors vanilla's own button-drawing predicate exactly (decompiled
                    // NameContext.MakeRow); a non-null EMPTY suggestion list draws no button.
                    bool drawsButton = row.NameIndex >= 0
                        && (row.SuggestedNames == null || row.SuggestedNames.Count > 0);
                    if (drawsButton && buttonIndex < buttons.Count)
                    {
                        row.HasButton = true;
                        row.ButtonCaptureIndex = buttonIndex;
                        buttonIndex++;
                    }
                }

                allRows.Add(row);
            }

            // Cancel then Accept are always drawn last, exactly once each
            // (decompiled Dialog_NamePawn.DoWindowContents:341-345).
            if (buttons.Count - buttonIndex >= 2)
            {
                cancelCaptureIndex = buttonIndex;
                acceptCaptureIndex = buttonIndex + 1;
                cancelLabel = buttons[buttonIndex].Label;
                acceptLabel = buttons[buttonIndex + 1].Label;
            }
        }

        // Row model

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return renameTextField(dialog).ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return allRows.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (index < 0 || index >= allRows.Count)
            {
                return new ElementDescription();
            }
            RowInfo row = allRows[index];
            var d = new ElementDescription { Label = row.Label };
            if (row.Editable)
            {
                d.Role = ElementRole.TextField;
            }
            else
            {
                // Shown but never editable (adult First/Last): plain data, no role word.
                d.Role = ElementRole.None;
                d.ReadOnly = true;
            }
            if (string.IsNullOrEmpty(row.Current))
            {
                d.ValueBlank = true;
            }
            else
            {
                d.Value = row.Current;
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (editing || index < 0 || index >= allRows.Count)
            {
                return;
            }
            RowInfo row = allRows[index];
            if (!row.Editable)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.NameDialog.ReadOnlyField".Loc(row.Label), SpeechPriority.High);
                return;
            }
            EnterEditMode(row);
        }

        /// <summary>
        /// Cancel then Accept, each activated through vanilla's own click. Declared rather than
        /// captured because the name rows draw Randomize/Suggested buttons of their own.
        /// </summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                if (cancelCaptureIndex >= 0)
                {
                    actions.Add(new ScreenAction(
                        !string.IsNullOrEmpty(cancelLabel) ? cancelLabel : "Cancel".Translate().ToString(),
                        () => ClickButton(cancelCaptureIndex)));
                    actions.Add(new ScreenAction(
                        !string.IsNullOrEmpty(acceptLabel) ? acceptLabel : "Accept".Translate().ToString(),
                        () => ClickButton(acceptCaptureIndex),
                        AcceptActionId));
                }
                return actions;
            }
        }

        protected override string DefaultAcceptActionId
        {
            get { return AcceptActionId; }
        }

        private void ClickButton(int captureIndex)
        {
            if (captureIndex < 0 || ButtonTextCapture.Items.Count <= captureIndex)
            {
                return;
            }
            // A live edit is already committed (the native field writes through); leave it
            // so the click is not refused and browse claims come back.
            ExitEditMode();
            ButtonTextCapture.RequestClick(captureIndex);
        }

        /// <summary>The focused button's capture index, else -1 (ButtonTextCapture draws that ring itself).</summary>
        private int FocusedButtonCaptureIndex()
        {
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != ContentRegionCount || region == null || region.IsEmpty)
            {
                return -1;
            }
            return region.Index == 0 ? cancelCaptureIndex : acceptCaptureIndex;
        }

        /// <summary>The name row under the cursor, or -1 when the cursor is on a button row.</summary>
        private int CurrentRowIndex()
        {
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != 0 || region == null || region.IsEmpty || region.Index >= allRows.Count)
            {
                return -1;
            }
            return region.Index;
        }

        // Navigation stands down while a native field owns the keyboard

        protected override void MoveItem(int delta)
        {
            if (editing)
            {
                return;
            }
            base.MoveItem(delta);
        }

        protected override void MoveItemEdge(bool first)
        {
            if (editing)
            {
                return;
            }
            base.MoveItemEdge(first);
        }

        protected override void MoveRegion(bool forward)
        {
            if (editing)
            {
                return;
            }
            base.MoveRegion(forward);
        }

        // Edit mode: vanilla's own native focus on exactly one field

        /// <summary>
        /// EDIT mode only: vanilla's own Tab hops native focus between editable fields, so poll
        /// currentControl and keep the cursor and announcements following it, re-baselining the
        /// echo on the newly-focused field. No-op in browse mode.
        /// </summary>
        private void SyncCursorWhileEditing()
        {
            if (!editing)
            {
                return;
            }
            string current = currentControlField(dialog) ?? "";
            if (current.Length == 0 || current == lastAnnouncedControl)
            {
                return;
            }
            ListModel rows = Model.Region(0);
            if (rows == null)
            {
                return;
            }
            for (int i = 0; i < allRows.Count && i < rows.Count; i++)
            {
                if (allRows[i].TextboxName != current)
                {
                    continue;
                }
                lastAnnouncedControl = current;
                if (i != rows.Index)
                {
                    rows.MoveTo(i);
                    echoControl = "";
                    if (!pendingAnnounce)
                    {
                        AnnounceCurrentItem();
                    }
                }
                return;
            }
        }

        /// <summary>
        /// Owns Unity keyboard focus per mode. EDIT keeps exactly the cursor's editable field
        /// focused, re-staging focusControlOverride only when focus actually drifted so the
        /// caret and selection survive between keystrokes. BROWSE keeps every field unfocused,
        /// which also undoes vanilla's firstCall auto-focus from frame two on.
        /// </summary>
        private void MaintainFocus()
        {
            if (editing)
            {
                int row = CurrentRowIndex();
                if (row >= 0 && allRows[row].Editable)
                {
                    if ((currentControlField(dialog) ?? "") != allRows[row].TextboxName)
                    {
                        focusControlOverrideField(dialog) = allRows[row].TextboxName;
                    }
                    return;
                }
                // Defensive: navigation is gated off while editing, so this shouldn't arise.
                editing = false;
            }

            Verse.UI.UnfocusCurrentControl();
            lastAnnouncedControl = "";
        }

        /// <summary>
        /// Per-keystroke echo for the field being edited. Observes the focused row's live
        /// NameContext.current once per pass and speaks appends/deletes, re-baselining silently
        /// whenever the observed control changes or this scope is not the active top.
        /// </summary>
        private void ObserveFocusedFieldEcho()
        {
            string control = "";
            string value = "";
            int row = editing ? CurrentRowIndex() : -1;
            if (row >= 0 && allRows[row].Editable)
            {
                control = allRows[row].TextboxName;
                value = allRows[row].Current;
            }
            bool observable = control.Length > 0
                && control == echoControl
                && object.ReferenceEquals(FocusStack.Top, this)
                && !TextDialogShared.ForeignWindowAbove(dialog);
            if (observable)
            {
                fieldEcho.Observe(value);
            }
            else
            {
                fieldEcho.Reset(value);
                echoControl = control;
            }
        }

        /// <summary>The focused name row's own ring; the Buttons region's is drawn by the capture tap.</summary>
        private void DrawFieldRing()
        {
            int row = CurrentRowIndex();
            if (row < 0)
            {
                return;
            }
            Rect rect = allRows[row].FieldRect;
            if (rect.width <= 0f)
            {
                return;
            }
            FocusRing.Draw(rect.ExpandedBy(FocusRingExpand));
        }

        private void EnterEditMode(RowInfo row)
        {
            editing = true;
            focusControlOverrideField(dialog) = row.TextboxName;
            lastAnnouncedControl = row.TextboxName;
            echoControl = "";
            fieldEcho.Reset(row.Current);
            string value = string.IsNullOrEmpty(row.Current)
                ? "RimWorldAccess.TextInput.Empty".Translate().ToString()
                : row.Current;
            // Vanilla's row label carries a trailing colon; strip it so the template's own
            // colon isn't doubled.
            string label = row.Label != null ? row.Label.TrimEnd(' ', ':', '：') : "";
            TolkHelper.Speak("RimWorldAccess.Pawns.NameDialog.EditingField".Loc(label, value), SpeechPriority.High);
        }

        /// <summary>
        /// Leaves edit mode back to browse (the edit is already live), unfocusing the field so
        /// navigation keys reach the dispatcher again and queueing a re-announce of the row.
        /// </summary>
        internal void ExitEditMode()
        {
            if (!editing)
            {
                return;
            }
            editing = false;
            editExitFrame = Time.frameCount;
            Verse.UI.UnfocusCurrentControl();
            lastAnnouncedControl = "";
            echoControl = "";
            pendingAnnounce = true;
        }

        /// <summary>
        /// Typing a printable character on an editable name row opens EDIT mode and seeds the
        /// character, so the row is edited natively from the first keystroke. Every other row
        /// and every non-printable character falls through to the chassis typeahead; while
        /// editing the native field owns typing, so nothing here may consume the character.
        /// </summary>
        public override bool HandleChar(char c)
        {
            if (editing || TextDialogShared.ForeignWindowAbove(dialog))
            {
                return false;
            }
            int row = CurrentRowIndex();
            if (row >= 0 && allRows[row].Editable && !char.IsControl(c) && !char.IsWhiteSpace(c))
            {
                SeedTypedCharacter(row, c);
                return true;
            }
            return base.HandleChar(c);
        }

        /// <summary>
        /// Replaces the row's value with the typed character and enters edit mode on it. The
        /// write goes into the same private NameContext.current field vanilla's own
        /// Widgets.TextField assigns, which is what the dialog renders and what Accept commits.
        /// </summary>
        private void SeedTypedCharacter(int row, char c)
        {
            IList raw = (IList)namesField.GetValue(dialog);
            if (raw == null || row >= raw.Count)
            {
                return;
            }
            string seeded = c.ToString();
            // MUTATION-C: mirrors NameContext.MakeRow's own "current = Widgets.TextField(...)"
            // write-back (decompiled Dialog_NamePawn) — vanilla only ever assigns this scratch
            // field inside its own draw, from the widget's return value, so no method or
            // delegate exists to invoke; the name is validated on Accept, so nothing is skipped.
            ncCurrentField.SetValue(raw[row], seeded);
            allRows[row].Current = seeded;
            EnterEditMode(allRows[row]);
        }

        /// <summary>
        /// Browse-mode Escape cancels the naming dialog, mirroring the Cancel button (decompiled
        /// Dialog_NamePawn:341-343). This scope must close the dialog itself: the dialog absorbs
        /// the raw event before its own OnCancelKeyPressed poll runs, so leaning on vanilla's
        /// cancel would make Escape a no-op. doCloseSound true matches the Cancel button.
        /// </summary>
        private void OnCancelBrowse(KeyEventSnapshot e)
        {
            Find.WindowStack.TryRemove(dialog, doCloseSound: true);
        }

        /// <summary>Alt+R: click the Randomize/Suggested button vanilla draws beside the focused row.</summary>
        private void OnRandomizeField(KeyEventSnapshot e)
        {
            int index = CurrentRowIndex();
            if (index < 0)
            {
                return;
            }
            RowInfo row = allRows[index];
            if (!row.HasButton)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.NameDialog.RandomCannotRandomize".Loc(), SpeechPriority.High);
                return;
            }
            bool isSuggested = row.SuggestedNames != null && row.SuggestedNames.Count > 0;
            if (!isSuggested)
            {
                // Only the same-frame-mutating Randomize flavor needs an explicit resync; a
                // Suggested click opens a child FloatMenu and the refocus cycle re-reads the row.
                pendingRandomizeRow = index;
            }
            ButtonTextCapture.RequestClick(row.ButtonCaptureIndex);
        }

        private void AnnounceRandomizeResult(int row)
        {
            if (row < 0 || row >= allRows.Count)
            {
                return;
            }
            // allRows was rebuilt this pass, so Current is the freshly generated name.
            TolkHelper.Speak("RimWorldAccess.Pawns.NameDialog.RandomizedTo".Loc(allRows[row].Current), SpeechPriority.High);
        }

        // Announcements

        protected override string ComposeOpenAnnouncement()
        {
            string heading = renameTextField(dialog).ToString();
            string description = descriptionTextField(dialog).ToString();
            string gender = genderTextField(dialog) ?? "";

            List<string> parts = new List<string>();
            parts.Add(heading);
            if (!string.IsNullOrEmpty(description))
            {
                parts.Add(description);
            }
            if (!string.IsNullOrEmpty(gender))
            {
                parts.Add(gender);
            }
            // Read-only name parts are not enumerated here; they are navigable rows of their own.
            parts.Add("RimWorldAccess.Pawns.NameDialog.EditHint".Translate().ToString());
            return string.Join(". ", parts.ToArray());
        }
    }

    /// <summary>
    /// Brackets ButtonTextCapture/TextFieldCapture to the dialog's own draw and drives the
    /// scope's per-pass rebuild, focus sync, focus ring, and deferred announcements, all inside
    /// the window's own GUI pass. The focus-stack top is the right lookup: this dialog's edit
    /// mode is vanilla's native field focus, so no edit-session scope ever sits above the scope.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_NamePawn), "DoWindowContents")]
    public static class NamePawnDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_NamePawn __instance, out bool __state)
        {
            __state = false;
            try
            {
                NamePawnScope scope = FocusStack.Top as NamePawnScope;
                if (scope != null && scope.Owns(__instance))
                {
                    __state = true;
                    scope.BeginDrawPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("NamePawn dialog draw pass error", ex);
            }
        }

        [HarmonyFinalizer]
        public static void Finalizer(Dialog_NamePawn __instance, Exception __exception, bool __state)
        {
            if (!__state)
            {
                return;
            }
            try
            {
                NamePawnScope scope = __exception == null ? FocusStack.Top as NamePawnScope : null;
                if (scope != null && scope.Owns(__instance))
                {
                    scope.OnGuiPass();
                }
                else
                {
                    TextDialogCapture.EndPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("NamePawn dialog draw pass error", ex);
            }
        }
    }

    /// <summary>
    /// Guards the two raw Event.current polls at the top of Dialog_NamePawn.DoWindowContents:
    /// Return/KeypadEnter submits Accept unconditionally (decompiled :278-283) and Tab
    /// FocusNextControls over editable fields only (:286-291), both bypassing
    /// Window.OnAcceptKeyPressed.
    ///
    /// QA R6: the focused window's pass can run BEFORE the dispatcher's main pass in the same
    /// frame and shares Event.current with it, so an Event.current.Use() here would starve this
    /// scope's own claim for that same press. Hence:
    ///  - EDITING + Enter: ExitEditMode() and a real Use() — self-contained and ordering-immune,
    ///    since browse-mode navigation stands down while editing, so no claim is starved.
    ///  - BROWSE, Enter or Tab: MASKED, not consumed — keyCode stashed and set to None for
    ///    vanilla's body, restored by the postfix, leaving the pristine KeyDown for the
    ///    dispatcher and every other window's pass.
    ///  - EDITING + Tab: untouched; vanilla's FocusNextControl is what SyncCursorWhileEditing
    ///    expects to observe.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_NamePawn), "DoWindowContents")]
    public static class NamePawnAcceptGuardPatch
    {
        private static KeyCode maskedKeyCode = KeyCode.None;

        [HarmonyPrefix]
        public static void Prefix(Dialog_NamePawn __instance)
        {
            maskedKeyCode = KeyCode.None;
            NamePawnScope scope = FocusStack.Top as NamePawnScope;
            if (scope == null || !scope.Owns(__instance))
            {
                return;
            }
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown)
            {
                return;
            }
            bool enter = e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;

            if (scope.Editing && enter)
            {
                // The whole action completes before vanilla's body runs, so a real Use() (not a
                // mask) is correct under either dispatcher/window-pass ordering.
                scope.ExitEditMode();
                e.Use();
                return;
            }
            if (!scope.Editing && (enter || e.keyCode == KeyCode.Tab))
            {
                maskedKeyCode = e.keyCode;
                e.keyCode = KeyCode.None;
            }
        }

        /// <summary>Restores the keyCode masked by the prefix above, if any.</summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (maskedKeyCode == KeyCode.None)
            {
                return;
            }
            Event e = Event.current;
            if (e != null)
            {
                e.keyCode = maskedKeyCode;
            }
            maskedKeyCode = KeyCode.None;
        }
    }

    /// <summary>
    /// Escape while EDIT mode is active leaves edit mode instead of closing the dialog. Patches
    /// the declaring type Window and guards by type, since a subclass override escapes base
    /// patches and a guarded declaring-type prefix is the only reliable block of vanilla's
    /// cancel handling.
    ///
    /// The same-frame guard is essential: vanilla calls OnCancelKeyPressed twice for one Escape
    /// (decompiled Window.cs:218,293), and the first call clears Editing — without the
    /// JustLeftEditThisFrame stamp the second would close the dialog. Using the event also makes
    /// vanilla's own Cancel.KeyDownEvent poll go false.
    ///
    /// In browse mode this stands aside; vanilla's OnCancelKeyPressed does not fire there at all
    /// (OnCancelBrowse closes the dialog instead), so a second, separate Escape still cancels.
    /// </summary>
    [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
    public static class NamePawnCancelKeyGuardPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Window __instance)
        {
            if (!(__instance is Dialog_NamePawn))
            {
                return true;
            }
            NamePawnScope scope = FocusStack.Top as NamePawnScope;
            if (scope == null || !scope.Owns(__instance))
            {
                return true;
            }
            if (scope.Editing)
            {
                scope.ExitEditMode();
                if (Event.current != null)
                {
                    Event.current.Use();
                }
                return false;
            }
            if (scope.JustLeftEditThisFrame)
            {
                // Second OnCancelKeyPressed of the same Escape; the first already left edit mode.
                if (Event.current != null)
                {
                    Event.current.Use();
                }
                return false;
            }
            return true;
        }
    }
}
