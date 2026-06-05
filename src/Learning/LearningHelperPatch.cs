using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for the Learning Helper accessibility feature.
    /// Announces new lesson activations via screen reader.
    /// </summary>
    [HarmonyPatch(typeof(LearningReadout))]
    [HarmonyPatch("TryActivateConcept")]
    public static class LearningHelperPatch
    {
        /// <summary>
        /// Announces newly activated concepts via screen reader.
        /// The game already plays SoundDefOf.Lesson_Activated; this supplements with text.
        /// No settings check needed — TryActivateConcept is only called when the tutor is active.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(ConceptDef conc, bool __result)
        {
            if (__result)
            {
                TolkHelper.Speak("RimWorldAccess.Learning.NewLessonAnnouncement".Loc("LearningHelper".Translate(), conc.LabelCap));
            }
        }
    }
}
