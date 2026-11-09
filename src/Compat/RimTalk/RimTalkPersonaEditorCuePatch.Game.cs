using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Postfix on <c>PersonaEditorWindow.DoWindowContents</c>. A sighted player sees Smart Gen's
    /// and Persona Director's Evolve buttons read "Generating" and then revert as the text box
    /// fills, so the end of either run is spoken once, off the window's own busy state.
    /// </summary>
    internal static class RimTalkPersonaEditorCuePatch
    {
        private static Window watched;
        private static bool wasGenerating;
        private static bool wasEvolving;

        public static void Postfix(Window __instance)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            bool generating = RimTalkPersonaDialogCompat.IsGenerating(__instance);
            bool evolving = PersonaDirectorCompat.IsEvolving(__instance);
            if (!ReferenceEquals(watched, __instance))
            {
                watched = __instance;
            }
            else
            {
                if (wasGenerating && !generating)
                {
                    TolkHelper.SpeakData("RimWorldAccess.Compat.RimTalk.PersonaEditor.GenerationComplete".Translate());
                }
                if (wasEvolving && !evolving)
                {
                    TolkHelper.SpeakData("RimWorldAccess.Compat.RimTalk.PersonaEditor.EvolveComplete".Translate());
                }
            }
            wasGenerating = generating;
            wasEvolving = evolving;
        }
    }
}
