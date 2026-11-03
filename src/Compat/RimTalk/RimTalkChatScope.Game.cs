using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard support for RimTalk's "chat with pawn" box, <c>RimTalk.UI.CustomDialogueWindow</c>.
    /// Same three-element browse/edit shape as <see cref="VfRenameDialogScope"/> -- one
    /// <see cref="TextFieldEditSession"/> field plus two captured buttons -- with one addition that
    /// shape didn't need: this dialog's own <c>OnAcceptKeyPressed</c> override requires a dedicated
    /// guard (see <see cref="RimTalkChatDialogCompat"/>).
    ///
    /// THREE rows in the dialog's own draw order: Field ("CustomTalkTextField", no length cap
    /// -- the mod imposes none), Send button (capture index 0), Cancel button (capture index 1).
    ///
    /// ENTER POSTURE, hazardous here for two independent reasons:
    /// <list type="bullet">
    /// <item>The window has a raw in-draw Return poll (DoWindowContents:59-67) gated on real
    /// Unity native focus sitting on "CustomTalkTextField". Masked unconditionally via
    /// <see cref="TextFieldRawPollGuard"/> in the draw prefix -- this scope drives Enter entirely
    /// itself, so that poll must never independently fire.</item>
    /// <item><c>OnAcceptKeyPressed</c> is overridden WITHOUT calling base, and <c>closeOnAccept</c>
    /// defaults true on the base <see cref="Window"/> class (never touched by this dialog's
    /// constructor) -- so <c>WindowStack.Notify_PressedAccept</c> invokes the override
    /// UNCONDITIONALLY on every real Enter press this window is open for, regardless of which row
    /// the cursor sits on. The existing <see cref="WindowAcceptKeyRouterPatch"/> (patched on the
    /// BASE Window method) can never see an override that skips base -- the same escape-base-
    /// patches gotcha as <c>MemeSelectionAcceptKeyRouterPatch</c> -- so
    /// <see cref="RimTalkChatDialogCompat"/> installs a type-specific twin.</item>
    /// </list>
    /// Both guards mean vanilla's own accept machinery never independently fires while this scope
    /// is live and attached (the chassis's OwnsAccept default, true, is what they require); the
    /// ONLY vehicle for "send" is this scope's own <see cref="ButtonTextCapture.RequestClick"/>
    /// call on the Send button's capture index -- used both for a direct Send-row activation AND
    /// as the field's edit-session <c>onConfirm</c> (typing then Enter mirrors clicking Send,
    /// exactly like vanilla's single-state model), so the real <c>SendDialogue</c> call only ever
    /// runs through the button's own click delegate, never duplicated.
    ///
    /// Escape keeps the chassis default (owned only while a typeahead search is live, which it
    /// clears): CustomDialogueWindow does not override <c>OnCancelKeyPressed</c> at all, so with
    /// no search running the base implementation runs untouched (closeOnCancel is the Window
    /// default true) -- no twin needed for Escape, matching VfRenameDialogScope.
    ///
    /// No "message sent" confirmation is invented: the mod itself shows no such feedback (no
    /// toast, no in-window status) for either the immediate-send or out-of-range-queues-a-Goto-job
    /// path -- per CLAUDE.md's present-everything-editorialize-nothing rule, the window closing is
    /// the correct, parity-matching feedback, exactly the same as what a sighted player sees.
    /// </summary>
    public sealed class RimTalkChatScope : RimTalkTextDialogScopeBase
    {
        private const string ActivateFocusedActionId = "rimTalkChatDialog.activateFocused";

        private enum ElementKind
        {
            Field,
            SendButton,
            CancelButton,
        }

        public RimTalkChatScope(Window dialog)
            : base(dialog)
        {
            // Space stays a same-shape alternate activation onto the shared activation path.
            Claim(ActivateFocusedActionId, e => ActivateCurrent(), when: NotForeign);
        }

        public override string Name
        {
            get { return "rimtalk-chat-dialog"; }
        }

        protected override string ContentRegionName(int region)
        {
            return (string)"RimWorldAccess.Compat.RimTalk.CustomDialogue.RegionName".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return 3;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            switch ((ElementKind)index)
            {
                case ElementKind.Field:
                    d.Label = ResolveFieldLabel();
                    d.Role = ElementRole.TextField;
                    string value = RimTalkChatDialogCompat.GetText(dialog);
                    if (string.IsNullOrEmpty(value))
                    {
                        d.ValueBlank = true;
                    }
                    else
                    {
                        d.Value = value;
                    }
                    break;
                case ElementKind.SendButton:
                    d.Label = ResolveSendLabel();
                    d.Role = ElementRole.Button;
                    break;
                default:
                    d.Label = ResolveCancelLabel();
                    d.Role = ElementRole.Button;
                    break;
            }
            return d;
        }

        protected override void ActivateRow(int index)
        {
            if ((ElementKind)index == ElementKind.Field)
            {
                BeginEdit(announcePrompt: true);
                return;
            }
            ClickButton(ButtonCaptureIndex(index));
        }

        protected override bool IsTextRow(int index)
        {
            return index == (int)ElementKind.Field;
        }

        // ---------------------------------------------------------------
        // Per-GUI-pass work, driven by the dialog's own draw
        // ---------------------------------------------------------------

        internal void BeginDrawPass()
        {
            TextDialogCapture.BeginPass();
        }

        internal void OnGuiPass()
        {
            RefreshModel();

            ShellTextFocus.ReleaseNativeFocus();
            session.MirrorLive();

            DrawFocusRing();

            FlushPendingAnnouncement();
        }

        private void DrawFocusRing()
        {
            Rect rect;
            int index = CurrentIndex;
            if (index < 0)
            {
                return;
            }
            if (IsTextRow(index))
            {
                if (TextFieldCapture.Items.Count == 0)
                {
                    return;
                }
                rect = TextFieldCapture.Items[0].Rect;
            }
            else
            {
                int captureIndex = ButtonCaptureIndex(index);
                if (ButtonTextCapture.Items.Count <= captureIndex)
                {
                    return;
                }
                rect = ButtonTextCapture.Items[captureIndex].Rect;
            }
            if (rect.width <= 0f)
            {
                return;
            }
            FocusRing.Draw(rect.ExpandedBy(FocusRingExpand));
        }

        /// <summary>Dialog draw order is Send first, then Cancel (CustomDialogueWindow.DoWindowContents) -- captured button index 0 / 1 respectively.</summary>
        private static int ButtonCaptureIndex(int index)
        {
            return (ElementKind)index == ElementKind.SendButton ? 0 : 1;
        }

        private void ClickButton(int captureIndex)
        {
            // THE RULE: this activation must never also reach vanilla's OnAcceptKeyPressed (see
            // RimTalkChatDialogCompat's twin patch, which blocks it outright while this scope owns
            // the window; the chassis's own activate path stamps the accept frame besides).
            if (ButtonTextCapture.Items.Count <= captureIndex)
            {
                return;
            }
            ButtonTextCapture.RequestClick(captureIndex);
        }

        // ---------------------------------------------------------------
        // Enter / type to edit (browse -> edit), via the shared session
        // ---------------------------------------------------------------

        protected override void BeginEdit(bool announcePrompt)
        {
            string current = RimTalkChatDialogCompat.GetText(dialog);

            // No length cap: the mod's own TextField call imposes none, so
            // neither do we. minLength 0 (not the TextFieldSpec.Unrestricted default of 1) matches
            // the mod's own permissiveness too -- an empty Send is a valid, intentional "close with
            // no message" gesture in CustomDialogueWindow, not a rejected submission.
            var spec = new TextFieldSpec(
                labelKey: "RimWorldAccess.TextInput.LabelDefault",
                maxLength: null,
                minLength: 0);

            session.EnterEdit(
                current,
                spec,
                ResolveFieldLabel(),
                ApplyValue,
                ReAnnounceRow,
                announcePrompt,
                onConfirm: ConfirmSend);
        }

        /// <summary>Writes the live buffer into the real _text so vanilla's Send button sends exactly what was typed (see RimTalkChatDialogCompat.SetText's MUTATION-C marker).</summary>
        private void ApplyValue(string value)
        {
            RimTalkChatDialogCompat.SetText(dialog, value ?? "");
        }

        /// <summary>
        /// The field's edit-session commit: mirrors vanilla's own single-state model (typing then
        /// Enter sends) by clicking the REAL Send button -- vehicle A, the same delegate a mouse
        /// click on Send runs (validate CanTalk, ExecuteDialogue or queue the Goto job, Close).
        /// Never calls window.OnAcceptKeyPressed directly: that method is unconditionally blocked
        /// by RimTalkChatDialogCompat's twin patch while this scope owns the window, so a direct
        /// call here would be self-blocked. Runs after the session's own state is torn down (see
        /// TextFieldEditSession.EnterEdit's onConfirm remarks), so closing the window here is safe.
        /// </summary>
        private void ConfirmSend()
        {
            ShellFrameStamps.MarkAcceptConsumed();
            ClickButton(0);
        }

        // ---------------------------------------------------------------
        // Announcements
        // ---------------------------------------------------------------

        /// <summary>
        /// First-focus context, spoken once before the field's own row description (matching
        /// who is chatting with whom, read from the dialog's own _initiator/_recipient fields
        /// rather than re-deriving RimTalk's "WhatToSayToSelf"/"WhatToSayToOther" distinction.
        /// Naming both pawns plainly reads correctly whether the initiator is a real pawn or the
        /// synthetic player persona.
        /// </summary>
        protected override string ComposeOpenedAnnouncement()
        {
            Pawn initiator = RimTalkChatDialogCompat.GetInitiator(dialog);
            Pawn recipient = RimTalkChatDialogCompat.GetRecipient(dialog);
            string initiatorName = initiator != null ? initiator.LabelShortCap : "";
            string recipientName = recipient != null ? recipient.LabelShortCap : "";
            return (string)"RimWorldAccess.Compat.RimTalk.CustomDialogue.Opened".Translate(initiatorName, recipientName);
        }

        private static string ResolveFieldLabel()
        {
            return (string)"RimWorldAccess.Compat.RimTalk.CustomDialogue.MessageField".Translate();
        }

        private string ResolveSendLabel()
        {
            if (ButtonTextCapture.Items.Count > 0)
            {
                string label = ButtonTextCapture.Items[0].Label;
                if (!string.IsNullOrEmpty(label))
                {
                    return label;
                }
            }
            return (string)"RimWorldAccess.Compat.RimTalk.CustomDialogue.SendFallback".Translate();
        }

        private string ResolveCancelLabel()
        {
            if (ButtonTextCapture.Items.Count > 1)
            {
                string label = ButtonTextCapture.Items[1].Label;
                if (!string.IsNullOrEmpty(label))
                {
                    return label;
                }
            }
            return (string)"RimWorldAccess.Compat.RimTalk.CustomDialogue.CancelFallback".Translate();
        }
    }
}
