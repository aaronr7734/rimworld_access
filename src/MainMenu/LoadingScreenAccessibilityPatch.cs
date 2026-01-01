using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches to add screen reader announcements for loading screens and gameplay tips.
    /// Announces loading progress text, asset loading percentage, and rotating tips.
    /// </summary>
    [HarmonyPatch]
    public static class LoadingScreenAccessibilityPatch
    {
        // State tracking to avoid duplicate announcements
        private static string lastAnnouncedText = "";
        private static int lastAnnouncedTipIndex = -1;
        private static int lastAnnouncedThreshold = -1;

        // Reflection fields for LongEventHandler
        private static FieldInfo levelLoadOpField;
        private static FieldInfo currentEventField;
        private static FieldInfo eventTextField;

        // Reflection fields for GameplayTipWindow
        private static FieldInfo allTipsCachedField;
        private static FieldInfo currentTipIndexField;

        static LoadingScreenAccessibilityPatch()
        {
            // Initialize reflection fields for LongEventHandler
            levelLoadOpField = AccessTools.Field(typeof(LongEventHandler), "levelLoadOp");
            currentEventField = AccessTools.Field(typeof(LongEventHandler), "currentEvent");

            // The eventText field is inside the nested QueuedLongEvent class
            var queuedLongEventType = AccessTools.Inner(typeof(LongEventHandler), "QueuedLongEvent");
            if (queuedLongEventType != null)
            {
                eventTextField = AccessTools.Field(queuedLongEventType, "eventText");
            }

            // Initialize reflection fields for GameplayTipWindow
            allTipsCachedField = AccessTools.Field(typeof(GameplayTipWindow), "allTipsCached");
            currentTipIndexField = AccessTools.Field(typeof(GameplayTipWindow), "currentTipIndex");
        }

        /// <summary>
        /// Patch for SetCurrentEventText to announce when loading text changes dynamically.
        /// </summary>
        [HarmonyPatch(typeof(LongEventHandler), "SetCurrentEventText")]
        [HarmonyPostfix]
        public static void SetCurrentEventText_Postfix(string newText)
        {
            if (!newText.NullOrEmpty() && newText != lastAnnouncedText)
            {
                lastAnnouncedText = newText;
                TolkHelper.Speak(newText, SpeechPriority.Normal);
            }
        }

        /// <summary>
        /// Patch for DrawLongEventWindowContents to announce initial event text and loading percentage.
        /// </summary>
        [HarmonyPatch(typeof(LongEventHandler), "DrawLongEventWindowContents")]
        [HarmonyPostfix]
        public static void DrawLongEventWindowContents_Postfix()
        {
            try
            {
                // Get current event
                object currentEvent = currentEventField?.GetValue(null);
                if (currentEvent == null)
                {
                    // No event active, reset state
                    ResetState();
                    return;
                }

                // Check for asset loading progress
                AsyncOperation levelLoadOp = levelLoadOpField?.GetValue(null) as AsyncOperation;
                if (levelLoadOp != null)
                {
                    float progress = levelLoadOp.isDone ? 1f : levelLoadOp.progress;
                    int currentPercent = (int)(progress * 100);
                    int threshold = (currentPercent / 25) * 25; // Round down to nearest 25

                    if (threshold > lastAnnouncedThreshold)
                    {
                        lastAnnouncedThreshold = threshold;
                        string message = "LoadingAssets".Translate() + " " + threshold + "%";
                        TolkHelper.Speak(message, SpeechPriority.Normal);
                    }
                }
                else
                {
                    // No level load op - announce the event text if not already announced
                    string eventText = eventTextField?.GetValue(currentEvent) as string;
                    if (!eventText.NullOrEmpty() && eventText != lastAnnouncedText)
                    {
                        lastAnnouncedText = eventText;
                        TolkHelper.Speak(eventText, SpeechPriority.Normal);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in loading screen announcement: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch for GameplayTipWindow.DrawContents to announce tips when they rotate.
        /// </summary>
        [HarmonyPatch(typeof(GameplayTipWindow), "DrawContents")]
        [HarmonyPostfix]
        public static void DrawContents_Postfix()
        {
            try
            {
                // Get current tip index
                object currentIndexObj = currentTipIndexField?.GetValue(null);
                if (currentIndexObj == null)
                    return;

                int currentIndex = (int)currentIndexObj;

                // Check if tip changed
                if (currentIndex != lastAnnouncedTipIndex)
                {
                    lastAnnouncedTipIndex = currentIndex;

                    // Get the tips list
                    List<string> tips = allTipsCachedField?.GetValue(null) as List<string>;
                    if (tips != null && currentIndex >= 0 && currentIndex < tips.Count)
                    {
                        string tip = tips[currentIndex];
                        TolkHelper.Speak($"Tip: {tip}", SpeechPriority.Normal);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error announcing gameplay tip: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset state tracking when loading completes.
        /// </summary>
        private static void ResetState()
        {
            lastAnnouncedText = "";
            lastAnnouncedTipIndex = -1;
            lastAnnouncedThreshold = -1;
        }
    }
}
