using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the windowless notification menu state for viewing messages, letters, and alerts.
    /// Provides hierarchical navigation with detail views for each notification type.
    /// Press L to open, navigate with arrows, Enter to view details, Enter on detail to jump to target.
    /// </summary>
    public static class NotificationMenuState
    {
        private static bool isActive = false;
        private static bool isInDetailView = false;
        private static List<NotificationItem> notifications = null;
        private static int currentIndex = 0;
        private static int detailScrollIndex = 0; // For scrolling through long explanations
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        /// <summary>
        /// Gets whether the notification menu is currently active.
        /// </summary>
        public static bool IsActive => isActive;
        public static TypeaheadSearchHelper Typeahead => typeahead;
        public static int CurrentIndex => currentIndex;
        public static bool IsInDetailView => isInDetailView;

        /// <summary>
        /// Opens the notification menu and collects all messages, letters, and alerts.
        /// </summary>
        public static void Open()
        {
            if (Find.CurrentMap == null)
            {
                TolkHelper.Speak("No map available");
                return;
            }

            // Collect all notifications
            notifications = CollectNotifications();

            if (notifications.Count == 0)
            {
                TolkHelper.Speak("No notifications available");
                return;
            }

            isActive = true;
            isInDetailView = false;
            currentIndex = 0;
            detailScrollIndex = 0;
            typeahead.ClearSearch();

            // Announce the first notification
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Closes the notification menu.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            isInDetailView = false;
            notifications = null;
            currentIndex = 0;
            detailScrollIndex = 0;
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Moves selection to the next notification.
        /// </summary>
        public static void SelectNext()
        {
            if (notifications == null || notifications.Count == 0)
                return;

            // If in detail view, scroll down through explanation
            if (isInDetailView)
            {
                detailScrollIndex++;
                AnnounceCurrentSelection();
                return;
            }

            currentIndex = MenuHelper.SelectNext(currentIndex, notifications.Count);
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Moves selection to the previous notification.
        /// </summary>
        public static void SelectPrevious()
        {
            if (notifications == null || notifications.Count == 0)
                return;

            // If in detail view, scroll up through explanation
            if (isInDetailView)
            {
                detailScrollIndex = Math.Max(0, detailScrollIndex - 1);
                AnnounceCurrentSelection();
                return;
            }

            currentIndex = MenuHelper.SelectPrevious(currentIndex, notifications.Count);
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Opens the detail view for the current notification, or jumps to target if already in detail view.
        /// </summary>
        public static void OpenDetailOrJump()
        {
            if (notifications == null || notifications.Count == 0)
                return;

            if (currentIndex < 0 || currentIndex >= notifications.Count)
                return;

            NotificationItem item = notifications[currentIndex];

            // If already in detail view, try to jump to target
            if (isInDetailView)
            {
                JumpToTarget(item);
            }
            else
            {
                // Enter detail view
                isInDetailView = true;
                detailScrollIndex = 0;
                typeahead.ClearSearch();
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Goes back from detail view to list view.
        /// </summary>
        public static void GoBack()
        {
            if (isInDetailView)
            {
                isInDetailView = false;
                detailScrollIndex = 0;
                typeahead.ClearSearch();
                AnnounceCurrentSelection();
            }
            else
            {
                Close();
                TolkHelper.Speak("Notification menu closed");
            }
        }

        /// <summary>
        /// Deletes the currently selected letter. Only letters can be deleted.
        /// </summary>
        public static void DeleteSelected()
        {
            if (!isActive || notifications == null || notifications.Count == 0)
                return;

            if (currentIndex < 0 || currentIndex >= notifications.Count)
                return;

            NotificationItem item = notifications[currentIndex];

            // Only letters can be deleted
            if (item.Type != NotificationType.Letter)
            {
                TolkHelper.Speak("Only letters can be deleted", SpeechPriority.High);
                return;
            }

            // Get the letter from sourceObject
            Letter letter = item.GetSourceLetter();
            if (letter == null)
            {
                TolkHelper.Speak("Cannot delete this letter", SpeechPriority.High);
                return;
            }

            string deletedLabel = item.Label;

            // Delete immediately
            Find.LetterStack.RemoveLetter(letter);

            // Refresh the list
            notifications = CollectNotifications();

            // Adjust current index if needed
            if (notifications.Count == 0)
            {
                Close();
                TolkHelper.Speak($"Deleted {deletedLabel}. No notifications remaining");
                return;
            }

            if (currentIndex >= notifications.Count)
            {
                currentIndex = notifications.Count - 1;
            }

            // Reset detail view state
            isInDetailView = false;
            detailScrollIndex = 0;

            // Announce deletion and current position
            TolkHelper.Speak($"Deleted {deletedLabel}");
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Collects all notifications from messages, letters, and alerts.
        /// </summary>
        private static List<NotificationItem> CollectNotifications()
        {
            List<NotificationItem> items = new List<NotificationItem>();

            // Collect live messages
            try
            {
                FieldInfo messagesField = typeof(Messages).GetField("liveMessages",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (messagesField != null)
                {
                    List<Message> liveMessages = messagesField.GetValue(null) as List<Message>;
                    if (liveMessages != null)
                    {
                        foreach (Message msg in liveMessages)
                        {
                            if (!msg.Expired)
                            {
                                items.Add(new NotificationItem(msg));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to collect messages: {ex.Message}");
            }

            // Collect letters
            try
            {
                if (Find.LetterStack != null)
                {
                    List<Letter> letters = Find.LetterStack.LettersListForReading;
                    if (letters != null)
                    {
                        foreach (Letter letter in letters)
                        {
                            items.Add(new NotificationItem(letter));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to collect letters: {ex.Message}");
            }

            // Collect active alerts
            try
            {
                if (Find.Alerts != null)
                {
                    FieldInfo activeAlertsField = typeof(AlertsReadout).GetField("activeAlerts",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (activeAlertsField != null)
                    {
                        List<Alert> activeAlerts = activeAlertsField.GetValue(Find.Alerts) as List<Alert>;
                        if (activeAlerts != null)
                        {
                            foreach (Alert alert in activeAlerts)
                            {
                                if (alert.Active)
                                {
                                    items.Add(new NotificationItem(alert));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to collect alerts: {ex.Message}");
            }

            // Sort from newest to oldest (descending by timestamp)
            items.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));

            return items;
        }

        /// <summary>
        /// Announces the currently selected notification to the clipboard.
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            if (notifications == null || notifications.Count == 0)
                return;

            if (currentIndex < 0 || currentIndex >= notifications.Count)
                return;

            NotificationItem item = notifications[currentIndex];

            if (isInDetailView)
            {
                // In detail view, show full explanation
                string announcement = BuildDetailAnnouncement(item);
                TolkHelper.Speak(announcement);
            }
            else
            {
                // In list view, show summary
                string typeLabel = item.Type == NotificationType.Message ? "Message" :
                                  item.Type == NotificationType.Letter ? "Letter" :
                                  "Alert";
                string announcement = $"{typeLabel}: {item.Label}. {MenuHelper.FormatPosition(currentIndex, notifications.Count)}";
                TolkHelper.Speak(announcement);
            }
        }

        /// <summary>
        /// Builds the detail announcement for a notification.
        /// </summary>
        private static string BuildDetailAnnouncement(NotificationItem item)
        {
            string announcement = "";

            // Only show type label on first view (detailScrollIndex == 0)
            if (detailScrollIndex == 0)
            {
                string typeLabel = item.Type == NotificationType.Message ? "Message" :
                                  item.Type == NotificationType.Letter ? "Letter" :
                                  "Alert";
                announcement = $"{typeLabel} Details: {item.Label}";

                // If there are explanation lines, show the first one
                if (item.ExplanationLines.Length > 0)
                {
                    announcement += $"\n{item.ExplanationLines[0]}";
                }

                // Add navigation hints only on first view
                if (item.ExplanationLines.Length > 1)
                {
                    announcement += "\nPress Down for more";
                }

                // Announce jump availability
                if (item.HasValidTarget)
                {
                    announcement += "\nPress Enter to jump to target location";
                }
                else
                {
                    announcement += "\nNo target location available";
                }

                announcement += "\nPress Escape to go back";
            }
            else if (detailScrollIndex > 0 && detailScrollIndex < item.ExplanationLines.Length)
            {
                // Show subsequent explanation lines without the type header
                announcement = item.ExplanationLines[detailScrollIndex];
            }
            else if (detailScrollIndex >= item.ExplanationLines.Length && item.ExplanationLines.Length > 0)
            {
                // Past the end of content
                string typeLabel = item.Type == NotificationType.Message ? "message" :
                                  item.Type == NotificationType.Letter ? "letter" :
                                  "alert";
                announcement = $"At end of {typeLabel}";
            }

            return announcement;
        }

        /// <summary>
        /// Jumps to the target location of the notification and closes the menu.
        /// </summary>
        private static void JumpToTarget(NotificationItem item)
        {
            if (!item.HasValidTarget)
            {
                TolkHelper.Speak("No target location available");
                return;
            }

            try
            {
                GlobalTargetInfo target = item.GetPrimaryTarget();

                if (target.IsValid)
                {
                    // Jump camera to target
                    CameraJumper.TryJumpAndSelect(target);

                    // Update map navigation state if initialized
                    if (MapNavigationState.IsInitialized && target.HasThing)
                    {
                        MapNavigationState.CurrentCursorPosition = target.Thing.Position;
                    }
                    else if (MapNavigationState.IsInitialized && target.Cell.IsValid)
                    {
                        MapNavigationState.CurrentCursorPosition = target.Cell;
                    }

                    // Close the menu
                    Close();

                    // Announce the jump
                    string locationDesc = target.HasThing ? target.Thing.LabelShort :
                                         target.Cell.IsValid ? $"position {target.Cell.x}, {target.Cell.z}" :
                                         "target location";
                    TolkHelper.Speak($"Jumped to {locationDesc}");
                }
                else
                {
                    TolkHelper.Speak("Target location is not valid");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to jump to target: {ex.Message}");
                TolkHelper.Speak("Failed to jump to target", SpeechPriority.High);
            }
        }

        /// <summary>
        /// Jumps to the first item in the list.
        /// </summary>
        public static void JumpToFirst()
        {
            if (notifications == null || notifications.Count == 0)
                return;

            currentIndex = MenuHelper.JumpToFirst();
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the last item in the list.
        /// </summary>
        public static void JumpToLast()
        {
            if (notifications == null || notifications.Count == 0)
                return;

            currentIndex = MenuHelper.JumpToLast(notifications.Count);
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Gets a list of labels for all notifications for typeahead search.
        /// </summary>
        public static List<string> GetItemLabels()
        {
            List<string> labels = new List<string>();
            if (notifications != null)
            {
                foreach (var item in notifications)
                {
                    labels.Add(item.Label);
                }
            }
            return labels;
        }

        /// <summary>
        /// Sets the current index directly.
        /// </summary>
        public static void SetCurrentIndex(int index)
        {
            if (notifications != null && index >= 0 && index < notifications.Count)
            {
                currentIndex = index;
            }
        }

        /// <summary>
        /// Announces the current selection with search context if active.
        /// </summary>
        public static void AnnounceWithSearch()
        {
            if (notifications == null || notifications.Count == 0)
                return;

            if (currentIndex < 0 || currentIndex >= notifications.Count)
                return;

            NotificationItem item = notifications[currentIndex];

            string typeLabel = item.Type == NotificationType.Message ? "Message" :
                              item.Type == NotificationType.Letter ? "Letter" :
                              "Alert";
            string announcement = $"{typeLabel}: {item.Label}. {MenuHelper.FormatPosition(currentIndex, notifications.Count)}";

            // Add search context if active
            if (typeahead.HasActiveSearch)
            {
                announcement += $", match {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} for '{typeahead.SearchBuffer}'";
            }

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Handles backspace for typeahead search.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!typeahead.HasActiveSearch)
                return;

            var labels = GetItemLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                    currentIndex = newIndex;
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Handles character input for typeahead search.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            var labels = GetItemLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    currentIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        /// <summary>
        /// Represents a notification item (message, letter, or alert).
        /// </summary>
        private class NotificationItem
        {
            public NotificationType Type { get; private set; }
            public string Label { get; private set; }
            public string Explanation { get; private set; }
            public bool HasValidTarget { get; private set; }
            public int Timestamp { get; private set; } // Game tick or arrival tick for sorting
            public string[] ExplanationLines { get; private set; } // Non-blank lines for scrolling

            private object sourceObject; // Stores the original Message, Letter, or Alert

            /// <summary>
            /// Processes the explanation text to remove blank lines, strip color tags, and prepare for scrolling.
            /// </summary>
            private void ProcessExplanation()
            {
                if (string.IsNullOrEmpty(Explanation))
                {
                    ExplanationLines = new string[0];
                    return;
                }

                // Strip XML/color tags from the explanation text
                string cleanedExplanation = StripTags(Explanation);

                // Split by newlines and filter out blank/whitespace-only lines
                string[] allLines = cleanedExplanation.Split('\n');
                List<string> nonBlankLines = new List<string>();

                foreach (string line in allLines)
                {
                    string trimmedLine = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedLine))
                    {
                        nonBlankLines.Add(trimmedLine);
                    }
                }

                ExplanationLines = nonBlankLines.ToArray();
            }

            /// <summary>
            /// Strips XML-style tags (like color tags) from text.
            /// Handles both self-closing and paired tags.
            /// </summary>
            private string StripTags(string text)
            {
                if (string.IsNullOrEmpty(text))
                    return text;

                // Use regex to remove all XML-style tags: <tag>, </tag>, <tag attr="value">
                // Pattern matches: < followed by optional /, followed by tag name and optional attributes, followed by >
                System.Text.RegularExpressions.Regex tagRegex =
                    new System.Text.RegularExpressions.Regex(@"</?[a-zA-Z][^>]*>");

                return tagRegex.Replace(text, "");
            }

            public NotificationItem(Message message)
            {
                Type = NotificationType.Message;
                Label = StripTags(message.text);
                Explanation = ""; // Messages don't have extended explanations
                HasValidTarget = message.lookTargets != null && message.lookTargets.IsValid();
                Timestamp = message.startingFrame; // Use starting frame as timestamp
                sourceObject = message;
                ProcessExplanation();
            }

            public NotificationItem(Letter letter)
            {
                Type = NotificationType.Letter;
                Label = StripTags(letter.Label);

                // Get mouseover text as explanation using reflection
                try
                {
                    MethodInfo getMouseoverTextMethod = letter.GetType().GetMethod("GetMouseoverText",
                        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                    if (getMouseoverTextMethod != null)
                    {
                        object result = getMouseoverTextMethod.Invoke(letter, null);
                        Explanation = result?.ToString() ?? "";
                    }
                    else
                    {
                        Explanation = "";
                    }
                }
                catch
                {
                    Explanation = "";
                }

                HasValidTarget = letter.lookTargets != null && letter.lookTargets.IsValid();
                Timestamp = letter.arrivalTick; // Use arrival tick as timestamp
                sourceObject = letter;
                ProcessExplanation();
            }

            public NotificationItem(Alert alert)
            {
                Type = NotificationType.Alert;
                Label = StripTags(alert.Label);

                // Get explanation
                try
                {
                    Explanation = alert.GetExplanation();
                }
                catch
                {
                    Explanation = "";
                }

                // Check if alert has valid culprits
                try
                {
                    AlertReport report = alert.GetReport();
                    HasValidTarget = report.AnyCulpritValid;
                }
                catch
                {
                    HasValidTarget = false;
                }

                Timestamp = Find.TickManager?.TicksGame ?? 0; // Use current game tick as timestamp (alerts are ongoing)
                sourceObject = alert;
                ProcessExplanation();
            }

            /// <summary>
            /// Gets the source Letter object if this is a letter notification.
            /// </summary>
            public Letter GetSourceLetter()
            {
                return sourceObject as Letter;
            }

            /// <summary>
            /// Gets the primary target for jumping.
            /// </summary>
            public GlobalTargetInfo GetPrimaryTarget()
            {
                if (sourceObject is Message message)
                {
                    return message.lookTargets?.TryGetPrimaryTarget() ?? GlobalTargetInfo.Invalid;
                }
                else if (sourceObject is Letter letter)
                {
                    return letter.lookTargets?.TryGetPrimaryTarget() ?? GlobalTargetInfo.Invalid;
                }
                else if (sourceObject is Alert alert)
                {
                    try
                    {
                        AlertReport report = alert.GetReport();
                        foreach (GlobalTargetInfo culprit in report.AllCulprits)
                        {
                            if (culprit.IsValid)
                                return culprit;
                        }
                    }
                    catch
                    {
                        // Ignore exceptions
                    }
                }

                return GlobalTargetInfo.Invalid;
            }
        }

        /// <summary>
        /// Notification types.
        /// </summary>
        private enum NotificationType
        {
            Message,
            Letter,
            Alert
        }
    }
}
