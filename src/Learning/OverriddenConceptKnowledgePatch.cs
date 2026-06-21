using HarmonyLib;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Stops vanilla's "learned by doing" hooks from completing a concept whose documentation we
    /// have re-authored, before a screen-reader user can read our version.
    ///
    /// Several vanilla concepts are taught by performing a mouse/camera action: e.g.
    /// WorldCameraDriver calls <see cref="PlayerKnowledgeDatabase.KnowledgeDemonstrated"/> with
    /// SpecificInteraction (+0.4 each) on every camera dolly, so simply arrowing across the world
    /// map to choose a landing site completes WorldCameraMovement in two key presses. For our users
    /// the lesson IS our overridden, accessible chapter, learned by READING it (which sets knowledge
    /// directly via SetKnowledge), not by the vanilla action — so we suppress the auto-demonstration
    /// for any concept we override. Non-overridden concepts are untouched and behave normally.
    /// </summary>
    [HarmonyPatch(typeof(PlayerKnowledgeDatabase), "KnowledgeDemonstrated")]
    public static class OverriddenConceptKnowledgePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConceptDef conc)
        {
            if (conc != null && ConceptHelpOverrides.GetHelpText(conc) != null)
                return false;
            return true;
        }
    }
}
