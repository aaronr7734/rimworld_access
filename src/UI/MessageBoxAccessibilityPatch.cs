using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch for Dialog_MessageBox to add screen reader announcements for confirmation dialogs.
    /// This handles caravan formation confirmations and other message boxes.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_MessageBox))]
    public static class MessageBoxAccessibilityPatch
    {
        // Track which dialog instances have been announced (automatically cleaned up when dialogs are garbage collected)
        private static ConditionalWeakTable<Dialog_MessageBox, object> announcedDialogs = new ConditionalWeakTable<Dialog_MessageBox, object>();

        /// <summary>
        /// Prefix patch for DoWindowContents to announce the message on first frame.
        /// </summary>
        [HarmonyPatch("DoWindowContents")]
        [HarmonyPrefix]
        public static void DoWindowContents_Prefix(Dialog_MessageBox __instance)
        {
            // Only announce once per dialog instance
            if (announcedDialogs.TryGetValue(__instance, out _))
                return;

            announcedDialogs.Add(__instance, null);

            try
            {
                // Get the message text and strip color/formatting tags
                string messageText = __instance.text.ToString().StripTags();
                string title = (__instance.title ?? "").StripTags();

                // Build announcement
                string announcement = "";
                if (!title.NullOrEmpty())
                {
                    announcement = title + ". ";
                }
                announcement += messageText;

                // Add button information
                string buttonAText = __instance.buttonAText ?? "";
                string buttonBText = __instance.buttonBText ?? "";

                if (!buttonAText.NullOrEmpty() && !buttonBText.NullOrEmpty())
                {
                    announcement += $". Press Enter for {buttonAText}, Escape for {buttonBText}.";
                }
                else if (!buttonAText.NullOrEmpty())
                {
                    announcement += $". Press Enter for {buttonAText}.";
                }

                // Announce with high priority to interrupt navigation
                TolkHelper.Speak(announcement, SpeechPriority.High);
            }
            catch (System.Exception ex)
            {
                Log.Error($"RimWorld Access: Failed to announce Dialog_MessageBox: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch for DoWindowContents to handle keyboard input.
        /// </summary>
        [HarmonyPatch("DoWindowContents")]
        [HarmonyPostfix]
        public static void DoWindowContents_Postfix(Dialog_MessageBox __instance)
        {
            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            KeyCode key = Event.current.keyCode;

            // Check for modifiers
            bool shift = Event.current.shift;
            bool ctrl = Event.current.control;
            bool alt = Event.current.alt;

            // Handle Enter key - execute button A (Confirm)
            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
            {
                if (__instance.buttonAAction != null)
                {
                    // Announce confirmation
                    string buttonText = __instance.buttonAText ?? "Confirm";
                    TolkHelper.Speak($"{buttonText}");

                    __instance.buttonAAction();
                    __instance.Close();
                    Event.current.Use();
                }
            }

            // Handle Escape key - execute button B (Cancel/Go Back)
            if (key == KeyCode.Escape && !shift && !ctrl && !alt)
            {
                if (__instance.buttonBAction != null)
                {
                    // Announce cancellation
                    string buttonText = __instance.buttonBText ?? "Cancel";
                    TolkHelper.Speak($"{buttonText}");

                    __instance.buttonBAction();
                    __instance.Close();
                    Event.current.Use();
                }
                else if (__instance.cancelAction != null)
                {
                    TolkHelper.Speak("Cancelled");
                    __instance.cancelAction();
                    __instance.Close();
                    Event.current.Use();
                }
            }
        }
    }
}
