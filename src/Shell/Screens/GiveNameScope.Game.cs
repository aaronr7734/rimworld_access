using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Drives the real <see cref="Dialog_GiveName"/> family, registered via
    /// ScopeForWindow.RegisterHierarchy so the base type and every subclass share this scope.
    ///
    /// One content region holds vanilla's own name field(s) — curName, plus curSecondName for the
    /// two-name flavors — and a Buttons region holds the dialog's buttons in vanilla's draw order:
    /// Randomize (per field, when that field has a generator) then OK. Each field row runs the
    /// mod-wide browse/edit grammar through a <see cref="TextFieldEditSession"/>. Vanilla's field
    /// has no GUI control name, so native focus is impossible; editing is a modal
    /// <see cref="TextInputController"/> session whose buffer is mirrored into whichever of
    /// curName/curSecondName the cursor sits on (<see cref="TextFieldEditSession.MirrorLive"/>),
    /// so vanilla renders the live value and its raw submit poll reads the true name.
    ///
    /// Enter posture: every row's Enter belongs to this scope. On a FIELD it opens the edit
    /// session; on a BUTTON it clicks through ButtonTextCapture.RequestClick — the same path Space
    /// takes — so OK runs vanilla's own submit body unmodified. Vanilla's top-of-DoWindowContents
    /// raw poll treats Enter as "submit OK" regardless of focus, so
    /// <see cref="ShouldMaskAcceptPoll"/> is always true and the draw prefix masks it every pass,
    /// including frames a modal edit session owns — which is why the patch resolves this scope
    /// through <see cref="TextDialogShared.ScopeOwning{T}"/> rather than the top of the focus
    /// stack. The mask covers Event.current.keyCode for vanilla's body rather than calling Use():
    /// the focused window's pass can run before the dispatcher's and shares Event state, so Use()
    /// would starve this scope's own Enter claims (TextFieldRawPollGuard's header carries the full
    /// R6 canon). Browse-type-to-edit ignores whitespace, so a space on a field neither edits nor
    /// leaks; a space typed WHILE editing reaches the controller normally.
    ///
    /// Escape is CLAIMED (<see cref="OwnsCancel"/> always true): vanilla has no close path here
    /// short of a valid submit, so this scope invents none either. It re-reads the dialog's prompt
    /// message(s) and current field value(s). While a typeahead search is live the chassis's own
    /// Escape claim wins first and clears the search.
    /// </summary>
    public sealed class GiveNameScope : ScreenScope
    {
        private const float FocusRingExpand = 2f;

        private const string ActivateFocusedActionId = "giveNameDialog.activateFocused";

        private static readonly AccessTools.FieldRef<Dialog_GiveName, string> curNameField =
            AccessTools.FieldRefAccess<Dialog_GiveName, string>("curName");
        private static readonly AccessTools.FieldRef<Dialog_GiveName, string> curSecondNameField =
            AccessTools.FieldRefAccess<Dialog_GiveName, string>("curSecondName");
        private static readonly AccessTools.FieldRef<Dialog_GiveName, Func<string>> nameGeneratorField =
            AccessTools.FieldRefAccess<Dialog_GiveName, Func<string>>("nameGenerator");
        private static readonly AccessTools.FieldRef<Dialog_GiveName, Func<string>> secondNameGeneratorField =
            AccessTools.FieldRefAccess<Dialog_GiveName, Func<string>>("secondNameGenerator");
        private static readonly AccessTools.FieldRef<Dialog_GiveName, string> nameMessageKeyField =
            AccessTools.FieldRefAccess<Dialog_GiveName, string>("nameMessageKey");
        private static readonly AccessTools.FieldRef<Dialog_GiveName, string> secondNameMessageKeyField =
            AccessTools.FieldRefAccess<Dialog_GiveName, string>("secondNameMessageKey");
        private static readonly AccessTools.FieldRef<Dialog_GiveName, bool> useSecondNameField =
            AccessTools.FieldRefAccess<Dialog_GiveName, bool>("useSecondName");
        private static readonly AccessTools.FieldRef<Dialog_GiveName, Pawn> suggestingPawnField =
            AccessTools.FieldRefAccess<Dialog_GiveName, Pawn>("suggestingPawn");
        private static readonly PropertyInfo firstCharLimitProperty =
            AccessTools.Property(typeof(Dialog_GiveName), "FirstCharLimit");
        private static readonly PropertyInfo secondCharLimitProperty =
            AccessTools.Property(typeof(Dialog_GiveName), "SecondCharLimit");

        private enum NameField
        {
            First,
            Second,
        }

        private readonly Dialog_GiveName dialog;
        private readonly bool useSecondName;
        private readonly bool hasRandomize1;
        private readonly bool hasRandomize2;
        private readonly TextFieldEditSession session = new TextFieldEditSession();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private NameField? pendingRandomizeResync;

        public GiveNameScope(Window dialog)
        {
            this.dialog = (Dialog_GiveName)dialog;
            useSecondName = useSecondNameField(this.dialog);
            hasRandomize1 = nameGeneratorField(this.dialog) != null;
            hasRandomize2 = useSecondName && secondNameGeneratorField(this.dialog) != null;

            Func<bool> notForeign = delegate { return !TextDialogShared.ForeignWindowAbove(this.dialog); };
            // Space stays a same-shape alternate activation onto the shared activation path.
            Claim(ActivateFocusedActionId, e => ActivateCurrent(), when: notForeign);
            Claim(SharedMenuGrammar.Cancel, OnCancel, when: notForeign);

            RegisterPopTeardown(session.CancelIfActive);
        }

        public override string Name
        {
            get { return "give-name-dialog"; }
        }

        public override bool IsModal
        {
            get { return !TextDialogShared.ForeignWindowAbove(dialog); }
        }

        /// <summary>Escape has nothing to fall through to here — see the class remarks.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>Each button is activated by the capture index this scope's own draw bracket recorded it at, and Randomize needs its result announced.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>
        /// The live scope owning <paramref name="window"/>, from anywhere on the focus stack —
        /// what this dialog's draw patch resolves with. See
        /// <see cref="TextDialogShared.ScopeOwning{T}"/> for why the top of the stack is wrong.
        /// </summary>
        internal static GiveNameScope OwningScope(Window window)
        {
            return TextDialogShared.ScopeOwning<GiveNameScope>(
                window, delegate(GiveNameScope s, Window w) { return s.Owns(w); });
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        // ---------------------------------------------------------------
        // Row model: the name field(s), then the dialog's own buttons.
        // ---------------------------------------------------------------

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return FieldLabel(NameField.First);
        }

        protected override int ContentItemCount(int region)
        {
            return useSecondName ? 2 : 1;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            NameField field = (NameField)index;
            var d = new ElementDescription { Label = FieldLabel(field), Role = ElementRole.TextField };
            string value = FieldValue(field);
            if (string.IsNullOrEmpty(value))
            {
                d.ValueBlank = true;
            }
            else
            {
                d.Value = value;
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            BeginEdit((NameField)index, announcePrompt: true);
        }

        /// <summary>
        /// The dialog's own buttons, each running vanilla's click. Vanilla's CODE draws a field's
        /// Randomize before the field itself and OK last; the capture indices follow that real
        /// call order so RequestClick addresses the right button, while the rows read in screen
        /// order.
        /// </summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                int captured = ButtonTextCapture.Items.Count;
                if (hasRandomize1 && captured > RandomizeCaptureIndex(NameField.First))
                {
                    actions.Add(new ScreenAction(
                        RandomizeButtonLabel(NameField.First),
                        () => ClickRandomize(NameField.First)));
                }
                if (hasRandomize2 && captured > RandomizeCaptureIndex(NameField.Second))
                {
                    actions.Add(new ScreenAction(
                        RandomizeButtonLabel(NameField.Second),
                        () => ClickRandomize(NameField.Second)));
                }
                if (captured > 0)
                {
                    actions.Add(new ScreenAction(OkLabel(), ClickOk));
                }
                return actions;
            }
        }

        private int RandomizeCaptureIndex(NameField field)
        {
            return field == NameField.Second && hasRandomize1 ? 1 : 0;
        }

        /// <summary>OK is always drawn last, regardless of layout.</summary>
        private static int OkCaptureIndex()
        {
            return ButtonTextCapture.Items.Count - 1;
        }

        private void ClickRandomize(NameField field)
        {
            pendingRandomizeResync = field;
            ClickButton(RandomizeCaptureIndex(field));
        }

        private void ClickOk()
        {
            ClickButton(OkCaptureIndex());
        }

        private void ClickButton(int captureIndex)
        {
            if (captureIndex < 0 || ButtonTextCapture.Items.Count <= captureIndex)
            {
                return;
            }
            // Vanilla's raw Return poll is masked for the whole draw pass, so this RequestClick
            // is the only submit path; the stamp keeps other accept handling off this frame.
            ShellFrameStamps.MarkAcceptConsumed();
            ButtonTextCapture.RequestClick(captureIndex);
        }

        // ---------------------------------------------------------------
        // Per-GUI-pass work, driven by the dialog's own draw
        // ---------------------------------------------------------------

        internal void BeginDrawPass()
        {
            // The ring index is read from the pass just finished, before BeginPass clears it.
            TextDialogCapture.BeginPass(FocusedButtonCaptureIndex());
        }

        internal void OnGuiPass()
        {
            // Keep the live buffer mirrored into the field vanilla draws and its raw Enter poll
            // reads on submit. No-op in browse mode.
            session.MirrorLive();

            if (pendingRandomizeResync.HasValue)
            {
                NameField resyncField = pendingRandomizeResync.Value;
                pendingRandomizeResync = null;
                ResyncAfterRandomize(resyncField);
            }

            DrawFieldRing();
        }

        /// <summary>The focused button's capture index, else -1 (ButtonTextCapture draws that ring itself).</summary>
        private int FocusedButtonCaptureIndex()
        {
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != ContentRegionCount || region == null || region.IsEmpty)
            {
                return -1;
            }
            int row = region.Index;
            if (hasRandomize1 && row == 0)
            {
                return RandomizeCaptureIndex(NameField.First);
            }
            if (hasRandomize2 && row == (hasRandomize1 ? 1 : 0))
            {
                return RandomizeCaptureIndex(NameField.Second);
            }
            return OkCaptureIndex();
        }

        /// <summary>The focused field row's own ring; the Buttons region's is drawn by the capture tap.</summary>
        private void DrawFieldRing()
        {
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != 0 || region == null || region.IsEmpty
                || TextFieldCapture.Items.Count <= region.Index)
            {
                return;
            }
            Rect rect = TextFieldCapture.Items[region.Index].Rect;
            if (rect.width <= 0f)
            {
                return;
            }
            FocusRing.Draw(rect.ExpandedBy(FocusRingExpand));
        }

        /// <summary>
        /// Caller state for <see cref="TextFieldRawPollGuard.MaskAcceptPoll"/>: always true.
        /// Every row's Enter belongs to this scope, so vanilla's focus-blind raw Return poll has
        /// no legitimate frame left to fire on — left live on a Randomize row it submits the
        /// whole dialog.
        /// </summary>
        internal bool ShouldMaskAcceptPoll
        {
            get { return true; }
        }

        // ---------------------------------------------------------------
        // Enter / type to edit (browse -> edit), via the shared session
        // ---------------------------------------------------------------

        private void BeginEdit(NameField field, bool announcePrompt)
        {
            int limit = field == NameField.Second ? SecondCharLimit() : FirstCharLimit();
            session.EnterEdit(
                FieldValue(field),
                FieldSpec(limit),
                FieldLabel(field),
                value => WriteField(field, value),
                AnnounceCurrentItem,
                announcePrompt);
        }

        private static TextFieldSpec FieldSpec(int maxLength)
        {
            return new TextFieldSpec(
                labelKey: "RimWorldAccess.TextInput.LabelDefault",
                maxLength: maxLength,
                minLength: 0);
        }

        private string FieldValue(NameField field)
        {
            return field == NameField.Second ? curSecondNameField(dialog) : curNameField(dialog);
        }

        private void WriteField(NameField field, string value)
        {
            if (field == NameField.Second)
            {
                curSecondNameField(dialog) = value ?? "";
            }
            else
            {
                curNameField(dialog) = value ?? "";
            }
        }

        /// <summary>
        /// Browse-type-to-edit: a printable key on a field row opens its edit session and
        /// inserts the character; anything else falls through to the shared typeahead. While
        /// editing, the dispatcher routes characters straight to the controller.
        /// </summary>
        public override bool HandleChar(char c)
        {
            if (TextDialogShared.ForeignWindowAbove(dialog))
            {
                return false;
            }
            ListModel region = Model.CurrentRegion;
            if (!session.Editing && Model.RegionIndex == 0 && region != null && !region.IsEmpty
                && !char.IsControl(c) && !char.IsWhiteSpace(c))
            {
                BeginEdit((NameField)region.Index, announcePrompt: false);
                session.FeedChar(c);
                return true;
            }
            return base.HandleChar(c);
        }

        private int FirstCharLimit()
        {
            return firstCharLimitProperty != null ? (int)firstCharLimitProperty.GetValue(dialog, null) : 64;
        }

        private int SecondCharLimit()
        {
            return secondCharLimitProperty != null ? (int)secondCharLimitProperty.GetValue(dialog, null) : 64;
        }

        // ---------------------------------------------------------------
        // Labels and announcements
        // ---------------------------------------------------------------

        /// <summary>
        /// One label per field, chosen by the RAW (language-independent) message key vanilla
        /// stored for it. Vanilla's prompts are whole sentences and the defs carry no short form,
        /// so known keys map to our own short labels; anything else falls back to that dialog's
        /// own prompt rather than an anonymous "edit box".
        /// </summary>
        private string FieldLabel(NameField field)
        {
            string messageKey = MessageKey(field);
            switch (messageKey)
            {
                case "NamePlayerFactionMessage":
                    return "RimWorldAccess.UI.Name.FieldFaction".Translate().ToString();
                case "NamePlayerFactionBaseMessage":
                case "NamePlayerFactionBaseMessage_NameFactionContinuation":
                    return "RimWorldAccess.UI.Name.FieldSettlement".Translate().ToString();
                case "NamePlayerGravshipMessage":
                    return "RimWorldAccess.UI.Name.FieldGravship".Translate().ToString();
            }
            if (string.IsNullOrEmpty(messageKey))
            {
                return "RimWorldAccess.TextInput.LabelDefault".Translate().ToString();
            }
            Pawn suggester = suggestingPawnField(dialog);
            return messageKey.Translate(suggester.LabelShort, suggester).CapitalizeFirst().ToString();
        }

        private string MessageKey(NameField field)
        {
            return field == NameField.Second ? secondNameMessageKeyField(dialog) : nameMessageKeyField(dialog);
        }

        /// <summary>
        /// Tells the two Randomize buttons in a two-field dialog apart by the message key of
        /// the field each one belongs to (same lookup as <see cref="FieldLabel"/>), falling
        /// back to vanilla's captured label — plain "Randomize" — for an unknown key.
        /// </summary>
        private string RandomizeButtonLabel(NameField field)
        {
            switch (MessageKey(field))
            {
                case "NamePlayerFactionMessage":
                    return "RimWorldAccess.UI.Name.RandomizeFaction".Translate().ToString();
                case "NamePlayerFactionBaseMessage":
                case "NamePlayerFactionBaseMessage_NameFactionContinuation":
                    return "RimWorldAccess.UI.Name.RandomizeSettlement".Translate().ToString();
                case "NamePlayerGravshipMessage":
                    return "RimWorldAccess.UI.Name.RandomizeGravship".Translate().ToString();
            }
            string captured = CapturedLabel(RandomizeCaptureIndex(field));
            return !string.IsNullOrEmpty(captured) ? captured : "Randomize".Translate().ToString();
        }

        private string OkLabel()
        {
            string captured = CapturedLabel(OkCaptureIndex());
            return !string.IsNullOrEmpty(captured) ? captured : "OK".Translate().ToString();
        }

        private static string CapturedLabel(int captureIndex)
        {
            if (captureIndex < 0 || ButtonTextCapture.Items.Count <= captureIndex)
            {
                return null;
            }
            return ButtonTextCapture.Items[captureIndex].Label;
        }

        /// <summary>A Randomize click rewrote the field's value externally; announce the fresh value (the row re-reads it too).</summary>
        private void ResyncAfterRandomize(NameField field)
        {
            TolkHelper.SpeakData(
                FieldLabel(field) + ". " + "RimWorldAccess.UI.Name.Randomized".Loc(FieldValue(field) ?? ""),
                SpeechPriority.High);
        }

        /// <summary>
        /// Reads the SAME message field(s) vanilla draws, with the same argument shape: the
        /// first message is CapitalizeFirst()'d, the second deliberately is not — replicated
        /// faithfully, not "fixed".
        /// </summary>
        private string BuildMessageAnnouncement()
        {
            Pawn suggester = suggestingPawnField(dialog);
            string first = nameMessageKeyField(dialog).Translate(suggester.LabelShort, suggester).CapitalizeFirst().ToString();
            if (!useSecondName)
            {
                return first;
            }
            string second = secondNameMessageKeyField(dialog).Translate(suggester.LabelShort, suggester).ToString();
            return first + " " + second;
        }

        protected override string ComposeOpenAnnouncement()
        {
            // No NavInstructions tail: this is a text-entry dialog, not a navigable element
            // list. A null elementCount tells DialogFrame to omit it.
            return DialogFrame.Opened("", BuildMessageAnnouncement(), null);
        }

        /// <summary>Escape: re-read the prompt and the current field state, since vanilla offers no close.</summary>
        private void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            TolkHelper.SpeakData(BuildMessageAndFieldStateAnnouncement(), SpeechPriority.High);
        }

        private string BuildMessageAndFieldStateAnnouncement()
        {
            string message = BuildMessageAnnouncement();
            string field1State = FieldStateFragment(NameField.First);
            if (!useSecondName)
            {
                return message + " " + field1State;
            }
            return message + " " + field1State + ". " + FieldStateFragment(NameField.Second);
        }

        private string FieldStateFragment(NameField field)
        {
            string value = FieldValue(field);
            return string.IsNullOrEmpty(value)
                ? "RimWorldAccess.TextInput.Empty".Translate().ToString()
                : value;
        }
    }

    /// <summary>
    /// Brackets TextDialogCapture to the dialog's own draw and drives the scope's per-pass mirror,
    /// focus ring, and randomize resync. Patches the exact base type: every Dialog_GiveName
    /// subclass inherits DoWindowContents without overriding it, so one patch covers the family.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_GiveName), "DoWindowContents")]
    public static class GiveNameDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_GiveName __instance, out GiveNameScope __state)
        {
            __state = null;
            try
            {
                GiveNameScope scope = GiveNameScope.OwningScope(__instance);
                if (scope != null)
                {
                    // Vanilla's raw Return poll is masked for the whole body: this scope claims
                    // Enter on every row, and OK submits through the button's own click.
                    TextFieldRawPollGuard.MaskAcceptPoll(scope.ShouldMaskAcceptPoll);
                    __state = scope;
                    scope.BeginDrawPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("GiveName dialog draw pass error", ex);
            }
        }

        [HarmonyFinalizer]
        public static void Finalizer(Exception __exception, GiveNameScope __state)
        {
            if (__state != null)
            {
                CapturePasses.Finish(__state, FocusStack.Contains(__state), __exception,
                    () => TextDialogCapture.EndMaskedPass(), s => s.OnGuiPass(), "GiveName dialog draw pass error");
            }
        }
    }
}
