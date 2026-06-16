using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Explicit type-ahead search mode for CJK/IME players.
    ///
    /// Direct-layout players (the overwhelming majority) get instant type-ahead: they just start
    /// typing in a menu and the list jumps. That cannot work for composition-based IME languages
    /// (Simplified/Traditional Chinese, Japanese, Korean), because composing pinyin requires the IME
    /// funnel (<see cref="ImeInputHost"/>) to be armed BEFORE the first keystroke — and arming it
    /// across a whole open menu makes the OS IME swallow the letter of every Alt/Ctrl shortcut as
    /// composition (the trade/work-priority regression). So for those languages, type-ahead in
    /// always-on menus (the trade list, animals, research, …) is invoked EXPLICITLY: the player
    /// presses '/' to open this search prompt, which arms the funnel for the duration of the search
    /// only. Normal navigation outside the prompt keeps the funnel off, so Alt shortcuts keep working.
    ///
    /// While open, this state is registered as an IME text sink (so the funnel composes pinyin) and
    /// suppresses the type-ahead auto-reset timeout (so a slow multi-character query is not wiped
    /// between commits). It does not own character routing itself: committed characters already flow
    /// through <see cref="UnifiedKeyboardPatch.RouteImeCommittedChar"/> →
    /// <see cref="TypeaheadDispatcher.TryDispatchChar"/> to whichever menu consumer is active, exactly
    /// as instant type-ahead does. This state only manages the session (open/close, backspace,
    /// timeout suppression) so the existing per-menu matching and announcements are fully reused.
    ///
    /// Scanner search (Z) and other deliberately-opened search prompts do NOT use this state: they
    /// are already discrete prompts whose own IsActive is added to the IME sink directly.
    /// </summary>
    public static class MenuSearchState
    {
        private static bool isActive;
        // The consumer the prompt opened over. If the active consumer changes (the menu closed or a
        // different one took over) the prompt auto-closes rather than leaving the IME funnel armed in
        // a menu the player didn't open it for — which would re-break Alt shortcuts there.
        private static TypeaheadConsumer openedOverConsumer;
        // Frame on which the session last closed. Escape closes the prompt in the UIRoot prefix,
        // which clears isActive BEFORE the window's OnCancelKeyPressed hook fires later in the same
        // frame; the block patch below uses this tag to still suppress that close (the same
        // post-close-race fix used by TextInputModalProtectPatch).
        private static int closedFrame = -1;

        /// <summary>True while the explicit search prompt is open.</summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// True while the prompt is open OR it closed earlier this frame — used by the Accept/Cancel
        /// block patch so Enter/Escape that dismissed the prompt can't also reach the window beneath.
        /// </summary>
        public static bool BlockWindowKeysThisFrame => isActive || Time.frameCount == closedFrame;

        /// <summary>
        /// True when the prompt is open but the menu it was opened over is no longer the active
        /// type-ahead consumer (it closed, or a different menu took over). The input handler closes
        /// the prompt in this case so the funnel never lingers into an unrelated menu.
        /// </summary>
        public static bool UnderlyingMenuChanged => isActive && TypeaheadDispatcher.ActiveConsumer != openedOverConsumer;

        /// <summary>
        /// Open the explicit search prompt over the currently-active type-ahead menu. Arms the IME
        /// funnel (via the sink check in <see cref="UnifiedKeyboardPatch"/>) and holds the type-ahead
        /// buffer open across slow IME commits.
        /// </summary>
        public static void Open()
        {
            if (isActive) return;
            isActive = true;
            openedOverConsumer = TypeaheadDispatcher.ActiveConsumer;
            TypeaheadSearchHelper.SuppressAutoReset = true;
            TolkHelper.Speak("RimWorldAccess.Search.Prompt".Loc(), SpeechPriority.High);
        }

        /// <summary>
        /// Close the search prompt. The menu's selection stays on the current match. Safe to call
        /// when not active.
        /// </summary>
        public static void Close()
        {
            if (!isActive) return;
            isActive = false;
            openedOverConsumer = null;
            closedFrame = Time.frameCount;
            TypeaheadSearchHelper.SuppressAutoReset = false;
            TolkHelper.Speak("RimWorldAccess.Search.Closed".Loc(), SpeechPriority.Normal);
        }

        /// <summary>
        /// Close without announcing — used when the underlying menu has gone away so there is nothing
        /// left to search. Keeps the suppression flag from leaking into the next menu.
        /// </summary>
        public static void ForceCloseSilently()
        {
            if (!isActive) return;
            isActive = false;
            openedOverConsumer = null;
            closedFrame = Time.frameCount;
            TypeaheadSearchHelper.SuppressAutoReset = false;
        }
    }

    /// <summary>
    /// Blocks RimWorld's <c>Window.OnAcceptKeyPressed</c> / <c>OnCancelKeyPressed</c> while the
    /// explicit search prompt is open (or closed this frame). The prompt consumes Enter/Escape in the
    /// UIRoot prefix to close itself; this stops the same keypress from also reaching the dialog
    /// underneath (e.g. closing the trade window or advancing a page). Mirrors
    /// <see cref="Window_OnCancelKeyPressed_TextInputBlock"/>.
    /// </summary>
    [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
    public static class Window_OnAcceptKeyPressed_MenuSearchBlock
    {
        [HarmonyPrefix]
        public static bool Prefix() => !MenuSearchState.BlockWindowKeysThisFrame;
    }

    [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
    public static class Window_OnCancelKeyPressed_MenuSearchBlock
    {
        [HarmonyPrefix]
        public static bool Prefix() => !MenuSearchState.BlockWindowKeysThisFrame;
    }
}
