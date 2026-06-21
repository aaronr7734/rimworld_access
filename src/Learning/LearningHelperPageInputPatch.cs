using HarmonyLib;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Keeps the Learning Helper's Escape/Enter from leaking into the setup page it is opened over.
    ///
    /// The Learning Helper (Shift+Slash) can open on top of a setup <see cref="Page"/> — most
    /// importantly Page_SelectStartingSite. The WindowStack calls a window's
    /// OnCancelKeyPressed / OnAcceptKeyPressed independently of our UIRoot keyboard handler, and
    /// Event.current.Use() does NOT stop it (see the project's keyboard-isolation notes). Without
    /// this, pressing Escape to back out of a lesson would also close the page, and pressing Enter
    /// to open a lesson would also advance the page.
    ///
    /// We patch <see cref="Page"/>'s declarations: OnCancelKeyPressed is inherited unchanged by
    /// Page_SelectStartingSite, so patching the base type covers it. OnAcceptKeyPressed is
    /// overridden by Page_SelectStartingSite (and does not call base), so that one is additionally
    /// guarded in StartingSitePatch.OnAcceptKeyPressed_Prefix; this base patch covers any other
    /// page that inherits the accept handler.
    /// </summary>
    [HarmonyPatch(typeof(Page), "OnCancelKeyPressed")]
    public static class LearningHelperPageCancelPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            // Block the page's cancel while the overlay is open, and also on the very frame the
            // overlay closed itself via Escape (IsActive has already flipped false by the time the
            // WindowStack reaches this), so the page does not close on the menu-closing press.
            if (LearningHelperState.IsActive || LearningHelperState.EscapeConsumedThisFrame)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Page), "OnAcceptKeyPressed")]
    public static class LearningHelperPageAcceptPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (LearningHelperState.IsActive)
                return false;
            return true;
        }
    }
}
