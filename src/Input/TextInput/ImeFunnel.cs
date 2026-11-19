using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// The text/IME front door for the shell dispatcher, called ahead of
    /// <see cref="RimWorldAccess.Shell.FocusStack.OfferChar"/> and
    /// <see cref="RimWorldAccess.Shell.FocusStack.Dispatch"/>, so the IME composition funnel and the
    /// CJK menu-search prompt see every keystroke before any live scope's char sink or key claim.
    /// </summary>
    internal static class ImeFunnel
    {
        /// <summary>
        /// Arms/disarms the IME composition funnel and pumps its hidden field. Must run on EVERY OnGUI
        /// pass (Layout and Repaint included), the cadence the shell dispatcher provides.
        /// </summary>
        internal static void PumpEveryPass()
        {
            // IME composition funnel (CJK only; a no-op elsewhere). Engages ONLY for a genuine
            // text-edit session — a modal TextInputController or an editing windowless-dialog field —
            // and must run every OnGUI pass while engaged so the hidden field keeps focus and its
            // composition state alive between keystrokes.
            //
            // It deliberately does NOT engage merely because a typeahead menu is open: a typeahead
            // consumer reports IsActive for the whole time its menu is on screen, and an armed funnel
            // forces imeCompositionMode=On with a focused hidden field, so the OS IME swallows the
            // letter of every Alt/Ctrl shortcut as pinyin and the shortcut never fires. Disengaging
            // only while a modifier is held is unreliable — releasing the hidden field's IMGUI focus
            // mid-KeyDown does not take effect in time. Menus therefore stay funnel-free; the cost is
            // pinyin type-ahead inside a menu list, while CJK entry in real text boxes still works.
            // Explicit, deliberately-opened prompts DO arm the funnel, but only while the prompt is
            // open: the scanner search box (Z) and the '/'-triggered menu search. Both self-gate to
            // CJK inside Pump.
            bool imeSinkActive = TextInputManager.IsActive
                || ScannerSearchState.IsActive
                || MenuSearchState.IsActive;
            ImeInputHost.Pump(imeSinkActive, c => RouteImeCommittedChar(c));
            WaylithCharacterTap.PumpEditSession(imeSinkActive);
        }

        /// <summary>Diverts a composing IME keystroke to the hidden field. Returns whether the caller should consume the event; this method does not consume it.</summary>
        internal static bool TryRouteKeyDown(Event e)
        {
            // CJK only — ImeInputHost.IsActive is false otherwise. While composing, every key drives
            // the OS candidate window; otherwise only letters begin composition. Committed characters
            // return through RouteImeCommittedChar.
            // The divert MUST stay ahead of FocusStack.OfferChar: if OfferChar ran first, a composing
            // keystroke's character twin would feed a live scope's CharSink as a raw Latin letter,
            // reintroducing the pinyin leak this funnel exists to prevent.
            return ImeInputHost.TryRouteKeyDown(e, c => RouteImeCommittedChar(c));
        }

        /// <summary>Owns the CJK explicit menu-search prompt's session-control keys and its '/' trigger. Returns whether the caller should consume the event.</summary>
        internal static bool HandleMenuSearchKeyDown(Event e)
        {
            KeyCode key = e.keyCode;

            // CJK/IME players invoke always-on-menu type-ahead through an explicit '/' prompt rather
            // than instant type-ahead, which cannot compose pinyin without arming the funnel across the
            // whole menu and breaking Alt shortcuts. While the prompt is open the funnel already
            // consumes letters; only the session control keys belong here. Direct-layout players never
            // enter this mode, since the '/' trigger is CJK-gated.
            // This must run above the scopes' own Cancel/Accept claims: with the prompt open over a
            // live modal scope, Escape/Enter would otherwise hit the scope's claim and close the menu
            // or activate the item before the prompt could close.
            if (MenuSearchState.IsActive)
            {
                // The menu under the prompt closed or changed out from under us — do not leave the
                // funnel armed in an unrelated menu.
                if (MenuSearchState.UnderlyingMenuChanged)
                {
                    MenuSearchState.ForceCloseSilently();
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Escape)
                {
                    // Enter and Escape both just close the prompt, leaving the menu's selection on the
                    // current match. Consume so Enter cannot activate the item and Escape cannot close
                    // the menu underneath.
                    MenuSearchState.Close();
                    return true;
                }
                // Everything else falls through to the menu's own handlers: Backspace edits the query
                // through the same buffer the committed pinyin went into, arrows move among matches.
            }
            // '/' opens the explicit search prompt over whatever type-ahead menu is active. CJK only,
            // and not while a scanner prompt or a text-edit session already owns input.
            else if (key == KeyCode.Slash && !e.shift && !e.control
                && !KeyboardHelper.IsAltHeld
                && ImeInputHost.LanguageUsesIme()
                // Shell-native equivalent of "some menu that can receive typeahead is open".
                && RimWorldAccess.Shell.FocusStack.TopCharSinkScope != null
                && !ScannerSearchState.IsActive && !TextInputManager.IsActive)
            {
                MenuSearchState.Open();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Routes an IME-committed character to whichever text sink is active, mirroring the modal
        /// text-edit then typeahead dispatch order. Returns whether a sink accepted it.
        /// </summary>
        internal static bool RouteImeCommittedChar(char c)
        {
            if (char.IsControl(c)) return false;

            if (TextInputManager.IsActive)
            {
                TextInputManager.Active.HandleCharacter(c);
                return true;
            }
            // Committed IME chars feed the SAME CharSinks plain chars use.
            if (RimWorldAccess.Shell.FocusStack.OfferChar(c)) return true;
            return false;
        }
    }
}
