using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Blocks RimWorld's <c>Window.OnAcceptKeyPressed</c> and <c>Window.OnCancelKeyPressed</c>
    /// while a modal text edit is live (or was live during the current frame).
    ///
    /// Why: the game checks <c>KeyBindingDefOf.Accept.KeyDownEvent</c> from multiple code
    /// paths (<c>WindowStack.HandleEventsHighPriority</c>, each window's own <c>WindowOnGUI</c>,
    /// <c>Page.DoBottomButtons</c>). <c>Event.current.Use()</c> should suppress those, but in
    /// practice some paths still call <c>OnAcceptKeyPressed</c> — often fire-and-forget —
    /// resulting in the page advancing even though the modal controller already confirmed.
    /// Patching the virtual hook is the reliable way to stop it, matching the pattern already
    /// used in <see cref="CaravanFormationPatch"/> and <see cref="TradeNavigationPatch"/>.
    ///
    /// The <c>HandledEventThisFrame</c> check is what catches the post-close race: the
    /// controller's <c>HandleEnter</c> clears <see cref="TextInputManager.IsActive"/> BEFORE
    /// the window's Accept hook fires, so an <c>IsActive</c>-only check leaks. Tagging the
    /// frame on every handled event closes that window.
    /// </summary>
    [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
    public static class Window_OnAcceptKeyPressed_TextInputBlock
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (TextInputManager.IsActive || TextInputManager.HandledEventThisFrame)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
    public static class Window_OnCancelKeyPressed_TextInputBlock
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (TextInputManager.IsActive || TextInputManager.HandledEventThisFrame)
                return false;
            return true;
        }
    }
}
