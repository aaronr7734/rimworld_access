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
                // Announce the message to screen reader via clipboard
                string announcement = $"Message: {msg.text}";
                TolkHelper.Speak(announcement);
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
                // Announce the letter to screen reader via clipboard
                string announcement = $"Letter: {let.Label}";
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
                    string label = alert.Label;
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
