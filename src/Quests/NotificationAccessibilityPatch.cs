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
                string announcement = "RimWorldAccess.Notification.LiveMessage".Translate(cleanText).ToString();
                TolkHelper.SpeakData(announcement);
                Log.Message($"[Notification] {announcement}");
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
                string announcement = "RimWorldAccess.Notification.LiveLetter".Translate(cleanLabel).ToString();
                TolkHelper.SpeakData(announcement);
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
                            string announcement = "RimWorldAccess.Notification.LiveAlert".Translate(label).ToString();
                            TolkHelper.SpeakData(announcement);
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
