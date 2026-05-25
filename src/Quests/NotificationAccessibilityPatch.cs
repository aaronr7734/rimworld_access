using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Patches RimWorld's notification systems to announce events via screen reader.
    /// Patches three systems: Messages (floating text), Letters (bottom-right stack), and Alerts (right-side warnings).
    /// </summary>
    [HarmonyPatch]
    public class NotificationAccessibilityPatch
    {
        // Track which alerts we've already announced to avoid duplicates
        private static HashSet<string> announcedAlerts = new HashSet<string>();

        /// <summary>
        /// Monotonic counter incremented every time Messages.Message announces something
        /// via this patch. Callers that wrap a game operation (e.g., ValidateTarget) can
        /// sample it before and after to detect whether the game itself emitted a message,
        /// and skip their own fallback announcement when it did. Avoids predicting which
        /// abilities/verbs/comps speak vs. stay silent — we just measure.
        /// </summary>
        public static long MessageEmissionCount { get; private set; } = 0;

        /// <summary>
        /// Monotonic counter incremented every time the game *attempts* a message, including
        /// ones RimWorld suppresses as duplicates. Paired with <see cref="LastAttemptedMessageText"/>,
        /// this lets callers recover a rejection reason (e.g. "no healable injuries") that
        /// <see cref="MessageEmissionCount"/> misses because the suppressed message never reaches
        /// the Message(Message, bool) funnel.
        /// </summary>
        public static long MessageAttemptCount { get; private set; } = 0;

        /// <summary>Cleaned text of the most recent attempted message (shown or suppressed).</summary>
        public static string LastAttemptedMessageText { get; private set; }

        /// <summary>
        /// Patches the Messages.Message(Message msg, bool historical) method to announce messages.
        /// This is the core method that all message variants funnel through.
        /// Location: Verse.Messages line 55
        /// </summary>
        [HarmonyPatch(typeof(Verse.Messages), "Message", new[] { typeof(Message), typeof(bool) })]
        [HarmonyPrefix]
        public static void Messages_Message_Prefix(Message msg)
        {
            if (msg != null && !string.IsNullOrEmpty(msg.text))
            {
                // Strip rich text tags like (*Name)...(/Name) and Unity tags
                string cleanText = msg.text.StripTags();
                string announcement = $"Message: {cleanText}";
                TolkHelper.Speak(announcement);
                Log.Message($"[Notification] {announcement}");
                MessageEmissionCount++;
            }
        }

        /// <summary>
        /// Patches Messages.AcceptsMessage, the single gate every message overload passes
        /// through before display. It runs even when the message is about to be suppressed as
        /// a duplicate, so it captures the text of rejection messages RimWorld dedupes. We only
        /// record here (no announcement) — the Message(Message, bool) prefix above still handles
        /// announcing messages that are actually shown.
        /// </summary>
        [HarmonyPatch(typeof(Verse.Messages), "AcceptsMessage")]
        [HarmonyPrefix]
        public static void Messages_AcceptsMessage_Prefix(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                LastAttemptedMessageText = text.StripTags();
                MessageAttemptCount++;
            }
        }

        /// <summary>
        /// Patches the LetterStack.ReceiveLetter(Letter let, string debugInfo, int delayTicks, bool playSound) method.
        /// This is the core method that all letter variants funnel through.
        /// Location: Verse.LetterStack line 56
        /// </summary>
        [HarmonyPatch(typeof(LetterStack), "ReceiveLetter", new[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) })]
        [HarmonyPrefix]
        public static void LetterStack_ReceiveLetter_Prefix(Letter let)
        {
            if (let != null && let.CanShowInLetterStack)
            {
                // Strip rich text tags like (*Name)...(/Name) and Unity tags
                // Label is TaggedString, so resolve it to string first
                string cleanLabel = let.Label.Resolve().StripTags();
                string announcement = $"Letter: {cleanLabel}";
                TolkHelper.Speak(announcement);
                Log.Message($"[Notification] {announcement}");
            }
        }

        /// <summary>
        /// Patches the AlertsReadout.AlertsReadoutUpdate method which processes alerts each frame.
        /// This approach avoids triggering the Alert class static constructor prematurely.
        /// Location: RimWorld.AlertsReadout line 92
        /// </summary>
        [HarmonyPatch(typeof(RimWorld.AlertsReadout), "AlertsReadoutUpdate")]
        [HarmonyPostfix]
        public static void AlertsReadout_AlertsReadoutUpdate_Postfix(List<Alert> ___activeAlerts)
        {
            if (___activeAlerts == null)
            {
                return;
            }

            // Build set of currently active alert labels
            HashSet<string> currentAlerts = new HashSet<string>();

            foreach (var alert in ___activeAlerts)
            {
                if (alert != null && alert.Active)
                {
                    // Strip rich text tags from alert label
                    string label = alert.Label?.StripTags();
                    if (!string.IsNullOrEmpty(label))
                    {
                        currentAlerts.Add(label);

                        // Only announce if this is a new alert we haven't seen before
                        if (!announcedAlerts.Contains(label))
                        {
                            string announcement = $"Alert: {label}";
                            TolkHelper.Speak(announcement);
                            Log.Message($"[Notification] {announcement}");
                        }
                    }
                }
            }

            // Update our tracking to match current state
            announcedAlerts = currentAlerts;
        }
    }
}
