using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Shared in-draw MASK for the browse/edit conversion of real vanilla text dialogs
    /// (Dialog_Rename, Dialog_GiveName, Dialog_NamePawn, Dialog_SaveFileList, …). Those
    /// dialogs poll <see cref="Event.current"/> for a Return/KeypadEnter KeyDown at the very
    /// top of their own <c>DoWindowContents</c> body and treat it as "submit" unconditionally —
    /// entirely bypassing <c>Window.OnAcceptKeyPressed</c>.
    ///
    /// R6 canon: the focused window's own GUI pass (this <c>DoWindowContents</c> call) can run
    /// BEFORE the shell dispatcher's main pass in the same frame, and it shares
    /// <see cref="Event.current"/> state with that pass — an <c>Event.current.Use()</c> here
    /// would make the triggering KeyDown arrive at the dispatcher already <c>Used</c>, starving
    /// whichever scope claim was supposed to act on it this same press (opening the edit
    /// session, or the session's own Enter-confirm/Escape-cancel claim). So this mechanism never
    /// calls <c>Use()</c>. Instead it stashes <c>Event.current.keyCode</c> and sets it to
    /// <see cref="KeyCode.None"/> for exactly the vanilla body that runs between
    /// <see cref="MaskAcceptPoll"/> and <see cref="RestoreAcceptPoll"/> — vanilla's raw poll
    /// (which tests <c>keyCode</c>) sees no Return/KeypadEnter, while every other window's pass
    /// and the dispatcher's main pass see the pristine KeyDown once it is restored (the same
    /// prefix-stash/postfix-restore shape as ModListPatch's arrow-key twin guard).
    ///
    /// Under the old always-live model those scopes deliberately LEFT Enter unclaimed so that
    /// raw poll was their submit path. Under browse/edit, Enter means something else on a field
    /// row — it opens the modal edit session (<see cref="TextFieldEditSession.EnterEdit"/>), and
    /// while editing Enter/Escape are the session's. This guard masks the raw poll on exactly
    /// those frames so vanilla cannot ALSO submit behind the session's back, while leaving a
    /// genuine browse-mode Enter on an OK/Accept button untouched (vanilla's poll submits as
    /// before).
    ///
    /// <b>Call shape.</b> <see cref="MaskAcceptPoll"/> is called from the owning scope's
    /// <c>DoWindowContents</c> PREFIX (before the vanilla body runs), passing state the CALLER
    /// already owns and can read at window-pass time — typically
    /// <c>TextInputManager.IsActive</c> (a modal session owns the keyboard) OR "the scope's
    /// cursor sits on a text-field row in browse mode" (Enter there is about to begin an edit).
    /// That caller-supplied bit is the load-bearing gate; it does not depend on anything the
    /// shell dispatcher's main pass sets, so it is correct whether the window pass runs before
    /// or after that main pass. <see cref="RestoreAcceptPoll"/> must be called from the matching
    /// POSTFIX every time <see cref="MaskAcceptPoll"/> was called, even when it masked nothing,
    /// so a masked keyCode never survives past the one call it was meant to hide from.
    ///
    /// <b>Nesting.</b> Two masking brackets on one window pass nest; <see cref="maskDepth"/> lets
    /// only the outermost pair stash and restore, so an inner pair never clobbers the outer stash.
    /// </summary>
    internal static class TextFieldRawPollGuard
    {
        private static KeyCode maskedKeyCode = KeyCode.None;
        private static int maskDepth;

        /// <param name="scopeOwnsThisEnter">
        /// True when the caller's own state means this frame's Enter belongs to the scope, not
        /// to vanilla's raw poll — e.g. a modal edit session is live, or browse-mode focus sits
        /// on a field row (Enter there opens edit). False leaves a browse-mode OK/Accept-button
        /// Enter reaching vanilla's poll untouched.
        /// </param>
        public static void MaskAcceptPoll(bool scopeOwnsThisEnter)
        {
            if (maskDepth > 0)
            {
                // An outer pair already owns this pass's mask; see class remarks.
                maskDepth++;
                return;
            }
            maskedKeyCode = KeyCode.None;
            Event e = Event.current;
            if (e == null)
            {
                return;
            }
            // Deliberately NOT gated on e.type == KeyDown. Type-blind raw polls (RT's
            // DLG_Chat.CheckForEnterKey tests keyCode alone) also fire on the Return KeyUp,
            // and Unity reuses the shared Event instance across passes, so a Return keyCode
            // can linger on Layout/Repaint events long after the press (live-proven 2026-08-04:
            // the poll fired on a KeyUp handed to it during a Layout pass). Type-checked
            // vanilla polls only act on KeyDown, so masking every type is behavior-preserving
            // for them and the postfix restore keeps other windows' passes pristine.
            bool enter = e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;
            if (!enter)
            {
                return;
            }
            // The caller's own state is the load-bearing gate (readable at window-pass time,
            // ordering-immune). The dispatch-time stamps are kept as a harmless additional OR:
            // they can never fire on a window-first frame (they aren't set yet), but do no harm
            // when the window pass runs second and they happen to already be set.
            bool mask = scopeOwnsThisEnter
                || ShellFrameStamps.AcceptConsumedThisFrame
                || TextInputManager.HandledEventThisFrame;
            if (!mask)
            {
                return;
            }
            maskedKeyCode = e.keyCode;
            e.keyCode = KeyCode.None;
            maskDepth = 1;
        }

        /// <summary>Restores the keyCode masked by <see cref="MaskAcceptPoll"/> above, if any.</summary>
        public static void RestoreAcceptPoll()
        {
            if (maskDepth == 0)
            {
                // Mask never engaged this pass (today's no-op behavior, preserved).
                return;
            }
            maskDepth--;
            if (maskDepth > 0)
            {
                // Still nested inside an outer pair -- defer the actual restore to it.
                return;
            }
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
}
