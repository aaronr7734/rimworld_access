using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
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
        // Concepts activated but not yet announced. The game can activate several concepts in the
        // same frame (e.g. the scanner and the world map both taught when the site-selection page
        // opens), each firing this postfix synchronously. We buffer them and flush one combined
        // utterance the following frame (see FlushPending), so the player hears
        // "Learning helper: the scanner and the world map" instead of two clipped announcements.
        private static readonly List<ConceptDef> pending = new List<ConceptDef>();
        private static int pendingFrame = -1;

        /// <summary>
        /// Buffers newly activated concepts; the actual announcement is coalesced and spoken by
        /// <see cref="FlushPending"/> on the next frame.
        /// The game already plays SoundDefOf.Lesson_Activated; this supplements with text.
        /// No settings check needed — TryActivateConcept is only called when the tutor is active.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(ConceptDef conc, bool __result)
        {
            if (!__result || conc == null)
                return;

            pending.Add(conc);
            pendingFrame = Time.frameCount;
        }

        /// <summary>
        /// Speaks any buffered lesson activations as a single announcement. Called every frame from
        /// <see cref="UnifiedKeyboardPatch"/>; it waits until the activation frame has passed so all
        /// same-frame activations coalesce, then combines their (override-aware) labels into one line.
        /// </summary>
        public static void FlushPending()
        {
            if (pending.Count == 0 || Time.frameCount == pendingFrame)
                return;

            // De-duplicate defensively while preserving activation order.
            var seen = new HashSet<ConceptDef>();
            var labels = new List<string>();
            foreach (ConceptDef conc in pending)
            {
                if (conc != null && seen.Add(conc))
                    labels.Add(ConceptHelpOverrides.DisplayLabel(conc));
            }
            pending.Clear();
            pendingFrame = -1;

            if (labels.Count == 0)
                return;

            string lessonText = JoinLessons(labels);
            string announcement = "RimWorldAccess.Learning.NewLessonAnnouncement".Translate("LearningHelper".Translate(), lessonText);

            // A new player has no way to know they can press Shift Slash to read the lesson that
            // was just announced — and the Learning Helper chapter can't teach that without already
            // being open. So append a one-time hint to the FIRST few announcements a player ever
            // sees (up to 3, across onboarding — not 3x in a row) so a missed one still lands, then
            // stop. The first announcement appears on the starting-site screen, so the hint begins
            // there. A combined announcement counts as one appearance.
            var settings = RimWorldAccessMod_Settings.Settings;
            if (settings != null && settings.LearningHintShownCount < 3)
            {
                announcement = announcement + " " + "RimWorldAccess.Learning.HelperHint".Translate();
                settings.LearningHintShownCount++;
                settings.Write();
            }

            // Low priority so a contextual lesson alert never interrupts navigation/selection
            // speech already in progress — it queues behind it. (Only High interrupts today, but
            // the SpeechPriority enum documents Normal as "interrupt low priority", so be explicit.)
            TolkHelper.SpeakData(announcement, SpeechPriority.Low);
        }

        /// <summary>
        /// Joins lesson labels into a comma-separated list ("A" / "A, B" / "A, B, C"). A plain comma
        /// list rather than "A and B" reads unambiguously even when a lesson's own name contains
        /// "and" (e.g. "Forbid and allow" would turn "the scanner and forbid and allow" into a
        /// run-on), and the commas give the screen reader a natural pause between entries.
        /// </summary>
        private static string JoinLessons(List<string> labels)
        {
            return string.Join(", ", labels);
        }
    }
}
