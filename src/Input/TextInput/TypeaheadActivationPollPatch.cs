using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony prefix on <c>UIRoot.UIRootUpdate</c> — runs once per Unity frame,
    /// before <c>UIRootOnGUI</c> for the frame. Polls every registered typeahead
    /// consumer for <c>IsActive</c> transitions so letter-key menu openers (I, G,
    /// L, Z, etc.) automatically suppress the twin char event that would otherwise
    /// become the first character of the freshly-opened menu's typeahead buffer.
    /// See <see cref="TypeaheadDispatcher.PollActivations"/> for the full rationale.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot), "UIRootUpdate")]
    public static class UIRoot_UIRootUpdate_TypeaheadPollPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            TypeaheadDispatcher.PollActivations();
        }
    }
}
