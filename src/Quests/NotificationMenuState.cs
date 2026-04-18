using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the windowless notification menu state for viewing messages, letters, and alerts.
    /// Two-level navigation: Press L to open, Up/Down to navigate list (title only),
    /// Enter to open detail view, then Up/Down through header, content lines, and buttons.
    /// Left/Right navigates between buttons when in buttons section. Enter activates button.
    /// </summary>
    public static class NotificationMenuState
    {
        private static bool isActive = false;
        private static List<NotificationItem> notifications = null;
        private static int currentIndex = 0;
        private static TwoLevelMenuHelper detailHelper = null;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        /// <summary>
        /// Gets whether the notification menu is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets whether we are in detail view (viewing a specific letter's content).
        /// </summary>
        public static bool IsInDetailView => detailHelper?.IsInDetailView ?? false;

        /// <summary>
        /// Gets whether we are currently in the buttons section of detail view.
        /// </summary>
        public static bool IsInButtonsSection => detailHelper?.IsInButtonsSection ?? false;

        public static TypeaheadSearchHelper Typeahead => typeahead;
        public static int CurrentIndex => currentIndex;

        /// <summary>
        /// Opens the notification menu and collects all messages, letters, and alerts.
        /// </summary>
        public static void Open()
        {
            if (!GuardHelper.RequireMap()) return;

            // Collect all notifications
            notifications = CollectNotifications();

            if (notifications.Count == 0)
            {
                TolkHelper.Speak("No notifications available");
                return;
            }

            isActive = true;
            currentIndex = 0;
            typeahead.ClearSearch();

            // Initialize the detail helper
            detailHelper = new TwoLevelMenuHelper(
                getContentLineCount: () => notifications != null && currentIndex >= 0 && currentIndex < notifications.Count
                    ? notifications[currentIndex].ExplanationLines.Length
                    : 0,
                populateButtons: PopulateButtonsForCurrentItem,
                getHeaderAnnouncement: () => notifications != null && currentIndex >= 0 && currentIndex < notifications.Count
                    ? $"{GetTypeLabel(notifications[currentIndex].Type)}: {notifications[currentIndex].Label}"
                    : "",
                getContentLineAnnouncement: (idx) => {
                    if (notifications == null || currentIndex < 0 || currentIndex >= notifications.Count)
                        return "";
                    var lines = notifications[currentIndex].ExplanationLines;
                    return idx >= 0 && idx < lines.Length ? lines[idx] : "";
                },
                endOfItemMessage: "End of letter",
                startOfItemMessage: "Start of letter"
            );
            detailHelper.RefreshButtons();

            // Announce the first notification (list view - title only)
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Closes the notification menu.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            notifications = null;
            currentIndex = 0;
            typeahead.ClearSearch();
            detailHelper?.Reset();
        }

        /// <summary>
        /// Moves selection to the next item. In list view, moves to next notification.
        /// In detail view, moves down through header, content lines, then buttons.
        /// </summary>
        public static void SelectNext()
        {
            if (notifications == null || notifications.Count == 0)
                return;

            if (detailHelper.IsInDetailView)
            {
                // In detail view, navigate down through header, lines, buttons
                detailHelper.SelectNextDetailPosition();
            }
            else
            {
                // In list view, navigate to next notification
                currentIndex = MenuHelper.SelectNext(currentIndex, notifications.Count);
                detailHelper.ResetDetailPosition();
                detailHelper.RefreshButtons();
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Moves selection to the previous item. In list view, moves to previous notification.
        /// In detail view, moves up through buttons, content lines, then header.
        /// </summary>
        public static void SelectPrevious()
        {
            if (notifications == null || notifications.Count == 0)
                return;

            if (detailHelper.IsInDetailView)
            {
                // In detail view, navigate up through buttons, lines, header
                detailHelper.SelectPreviousDetailPosition();
            }
            else
            {
                // In list view, navigate to previous notification
                currentIndex = MenuHelper.SelectPrevious(currentIndex, notifications.Count);
                detailHelper.ResetDetailPosition();
                detailHelper.RefreshButtons();
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Navigates to the next button (Right arrow). Only works in buttons section of detail view.
        /// </summary>
        public static void SelectNextButton()
        {
            detailHelper.SelectNextButton();
        }

        /// <summary>
        /// Navigates to the previous button (Left arrow). Only works in buttons section of detail view.
        /// </summary>
        public static void SelectPreviousButton()
        {
            detailHelper.SelectPreviousButton();
        }

        /// <summary>
        /// Activates the currently selected button (Enter key). Only works in buttons section of detail view.
        /// </summary>
        public static void ActivateCurrentButton()
        {
            if (!detailHelper.ActivateCurrentButton())
                return;

            ButtonInfo button = detailHelper.GetCurrentButton();
            if (button == null) return;

            string buttonLabel = button.Label;

            // Execute the button action
            try
            {
                button.Action?.Invoke();

                // CRITICAL: Check if the action closed our state (e.g., opened another dialog
                // like Dialog_NamePawn which closes NotificationMenuState in its Open())
                if (!isActive)
                {
                    // State was closed by the action - don't do any post-action processing
                    return;
                }

                // After executing action, refresh notifications (button may have removed the letter)
                notifications = CollectNotifications();

                if (notifications.Count == 0)
                {
                    Close();
                    TolkHelper.Speak($"Activated {buttonLabel}. No notifications remaining");
                    return;
                }

                // Adjust current index if needed
                if (currentIndex >= notifications.Count)
                {
                    currentIndex = notifications.Count - 1;
                }

                // Check if the action was a jump (common case)
                if (buttonLabel.ToLower().Contains("jump") || buttonLabel.ToLower().Contains("location"))
                {
                    // For jump actions, close the menu after jumping
                    Close();
                    TolkHelper.Speak($"Jumped to location");
                }
                else
                {
                    // For other actions, go back to list view and announce
                    detailHelper.GoBackToList();
                    detailHelper.RefreshButtons();
                    TolkHelper.Speak($"Activated {buttonLabel}. Back to list");
                    AnnounceCurrentSelection();
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to activate button: {ex.Message}");
                TolkHelper.Speak("Failed to activate button");
            }
        }

        /// <summary>
        /// Closes the notification menu (Escape key).
        /// </summary>
        public static void CloseMenu()
        {
            Close();
            TolkHelper.Speak("Notification menu closed");
        }

        /// <summary>
        /// Enters detail view for the current notification.
        /// Shows full letter content starting with the header.
        /// </summary>
        public static void EnterDetailView()
        {
            if (notifications == null || notifications.Count == 0)
                return;

            if (currentIndex < 0 || currentIndex >= notifications.Count)
                return;

            typeahead.ClearSearch();
            detailHelper.RefreshButtons();
            detailHelper.EnterDetailView();
            detailHelper.AnnounceDetailPosition();
        }

        /// <summary>
        /// Goes back from detail view to list view.
        /// </summary>
        public static void GoBackToList()
        {
            if (!detailHelper.IsInDetailView)
            {
                // Already in list view, close the menu
                CloseMenu();
                return;
            }

            detailHelper.GoBackToList();
            typeahead.ClearSearch();

            // Announce current notification in list format
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Handles Escape key: goes back from detail view, or closes menu from list view.
        /// </summary>
        public static void HandleEscape()
        {
            if (detailHelper.IsInDetailView)
            {
                GoBackToList();
                TwoLevelMenuHelper.SpeakReturnToList();
            }
            else
            {
                CloseMenu();
            }
        }

        /// <summary>
        /// Gets the human-readable type label for a notification type.
        /// </summary>
        private static string GetTypeLabel(NotificationType type)
        {
            return type == NotificationType.Message ? "Message" :
                   type == NotificationType.Letter ? "Letter" :
                   "Alert";
        }

        /// <summary>
        /// Populates buttons for the current notification item.
        /// Called by TwoLevelMenuHelper.RefreshButtons().
        /// </summary>
        private static void PopulateButtonsForCurrentItem(List<ButtonInfo> buttons)
        {
            if (notifications == null || notifications.Count == 0) return;
            if (currentIndex < 0 || currentIndex >= notifications.Count) return;

            NotificationItem item = notifications[currentIndex];

            switch (item.Type)
            {
                case NotificationType.Letter:
                    ExtractLetterButtons(item, buttons);
                    break;
                case NotificationType.Alert:
                    ExtractAlertButtons(item, buttons);
                    break;
                // Messages don't have buttons
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

            // Reset to list view and refresh buttons for new current notification
            detailHelper.GoBackToList();
            detailHelper.ResetDetailPosition();
            detailHelper.RefreshButtons();

            // Announce deletion and new current item in single announcement
            NotificationItem newItem = notifications[currentIndex];
            string typeLabel = GetTypeLabel(newItem.Type);
            string position = MenuHelper.FormatPosition(currentIndex, notifications.Count);
            TolkHelper.Speak($"Deleted {deletedLabel}. {typeLabel}: {newItem.Label}. {position}");
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
        /// Announces the currently selected notification.
        /// In list view, announces only type and title.
        /// In detail view, delegates to AnnounceDetailPosition.
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            if (notifications == null || notifications.Count == 0)
                return;

            if (currentIndex < 0 || currentIndex >= notifications.Count)
                return;

            if (detailHelper.IsInDetailView)
            {
                // In detail view, use the detail position announcement
                detailHelper.AnnounceDetailPosition();
                return;
            }

            // In list view, announce only type and title
            NotificationItem item = notifications[currentIndex];

            // Build announcement with just type and title
            string announcement = $"{GetTypeLabel(item.Type)}: {item.Label}. {MenuHelper.FormatPosition(currentIndex, notifications.Count)}";

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Extracts buttons from a letter notification.
        /// </summary>
        private static void ExtractLetterButtons(NotificationItem item, List<ButtonInfo> buttons)
        {
            Letter letter = item.GetSourceLetter();
            if (letter == null)
                return;

            // Handle growth moment letters (ChoiceLetter_GrowthMoment extends LetterWithTimeout, NOT ChoiceLetter)
            if (letter is ChoiceLetter_GrowthMoment growthLetter)
            {
                string label = growthLetter.ArchiveView ? "View choices" : "Open growth moment";
                buttons.Add(new ButtonInfo
                {
                    Label = label,
                    Action = () => {
                        Close();
                        growthLetter.OpenLetter();
                    },
                    IsDisabled = false
                });
                return;
            }

            // Extract buttons from ChoiceLetter
            if (letter is ChoiceLetter choiceLetter)
            {
                try
                {
                    // Get the Choices property
                    PropertyInfo choicesProperty = typeof(ChoiceLetter).GetProperty("Choices",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (choicesProperty != null)
                    {
                        var choices = choicesProperty.GetValue(choiceLetter) as IEnumerable<DiaOption>;
                        if (choices != null)
                        {
                            foreach (DiaOption option in choices)
                            {
                                // Get the text field (protected)
                                FieldInfo textField = typeof(DiaOption).GetField("text",
                                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                                string label = textField?.GetValue(option)?.ToString() ?? "Unknown";

                                // Skip "Close" button (already handled by Delete key)
                                if (label.ToLower().Contains("close") || label == "Close".Translate())
                                    continue;

                                // Create button info with accessible action wrapper
                                ButtonInfo buttonInfo = new ButtonInfo
                                {
                                    Label = label,
                                    Action = CreateAccessibleAction(option, letter, label),
                                    IsDisabled = option.disabled,
                                    DisabledReason = option.disabledReason
                                };

                                buttons.Add(buttonInfo);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"RimWorld Access: Failed to extract letter buttons: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Extracts buttons from an alert notification.
        /// Alerts can have targets to jump to or custom click actions (like opening research).
        /// </summary>
        private static void ExtractAlertButtons(NotificationItem item, List<ButtonInfo> buttons)
        {
            Alert alert = item.GetSourceAlert();
            if (alert == null)
                return;

            try
            {
                // Check alert type and create appropriate button
                string alertTypeName = alert.GetType().Name;

                // Research alerts - open research menu
                if (alertTypeName.Contains("Research") || alertTypeName.Contains("NeedResearch"))
                {
                    buttons.Add(new ButtonInfo
                    {
                        Label = "Open Research",
                        Action = () => {
                            Close();
                            WindowlessResearchMenuState.Open();
                        },
                        IsDisabled = false
                    });
                    return;
                }

                // Check if alert has targets to jump to
                var reportMethod = alert.GetType().GetMethod("GetReport",
                    BindingFlags.Public | BindingFlags.Instance);

                if (reportMethod != null)
                {
                    var report = reportMethod.Invoke(alert, null);
                    if (report is AlertReport alertReport)
                    {
                        // Collect all valid targets
                        var targets = new List<GlobalTargetInfo>();
                        if (alertReport.culpritsThings != null)
                        {
                            targets.AddRange(
                                alertReport.culpritsThings
                                    .Where(thing => thing != null)
                                    .Select(thing => new GlobalTargetInfo(thing)));
                        }
                        if (alertReport.culpritsPawns != null)
                        {
                            targets.AddRange(
                                alertReport.culpritsPawns
                                    .Where(pawn => pawn != null)
                                    .Select(pawn => new GlobalTargetInfo(pawn)));
                        }
                        if (alertReport.culpritsTargets != null)
                        {
                            targets.AddRange(alertReport.culpritsTargets.Where(t => t.IsValid));
                        }
                        if (alertReport.culpritTarget.HasValue && alertReport.culpritTarget.Value.IsValid)
                        {
                            targets.Add(alertReport.culpritTarget.Value);
                        }

                        // Create a button for each target or a single "Jump to target" button
                        if (targets.Count == 1)
                        {
                            var target = targets[0];
                            buttons.Add(new ButtonInfo
                            {
                                Label = $"Jump to {GetTargetDescription(target)}",
                                Action = CreateJumpToTargetAction(target),
                                IsDisabled = false
                            });
                        }
                        else if (targets.Count > 1)
                        {
                            // Multiple targets - create buttons for each (up to first 5)
                            int count = Math.Min(targets.Count, 5);
                            for (int i = 0; i < count; i++)
                            {
                                var target = targets[i];
                                int index = i + 1;
                                buttons.Add(new ButtonInfo
                                {
                                    Label = $"Jump to {GetTargetDescription(target)} ({index} of {targets.Count})",
                                    Action = CreateJumpToTargetAction(target),
                                    IsDisabled = false
                                });
                            }
                        }
                    }
                }

                // If no buttons were created, try to use the alert's default OnClick
                if (buttons.Count == 0)
                {
                    var onClickMethod = alert.GetType().GetMethod("OnClick",
                        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                    if (onClickMethod != null)
                    {
                        buttons.Add(new ButtonInfo
                        {
                            Label = "Activate",
                            Action = () => {
                                try
                                {
                                    onClickMethod.Invoke(alert, null);
                                    TolkHelper.Speak("Alert activated");
                                }
                                catch (Exception ex)
                                {
                                    Log.Warning($"RimWorld Access: Failed to activate alert: {ex.Message}");
                                }
                            },
                            IsDisabled = false
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to extract alert buttons: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates an action that jumps to a target and updates the cursor.
        /// </summary>
        private static Action CreateJumpToTargetAction(GlobalTargetInfo target)
        {
            return () => {
                if (target.IsValid)
                {
                    // For world targets, set pending tile BEFORE CameraJumper opens world view.
                    // This is critical because WorldNavigationState.Open() is called in the next frame
                    // when WorldNavigationPatch detects the mode change, and it would otherwise
                    // default to the colony tile. PendingStartTile is checked first in Open().
                    if (target.HasWorldObject)
                    {
                        PlanetTile tile = target.WorldObject.Tile;
                        if (tile.Valid)
                        {
                            WorldNavigationState.PendingStartTile = tile;
                        }
                    }
                    else if (target.Tile.Valid && !target.HasThing && !target.Cell.IsValid)
                    {
                        WorldNavigationState.PendingStartTile = target.Tile;
                    }

                    CameraJumper.TryJumpAndSelect(target);

                    // Also set current tile in case world view was already open (Open() won't be called)
                    if (target.HasWorldObject)
                    {
                        PlanetTile tile = target.WorldObject.Tile;
                        if (tile.Valid)
                        {
                            WorldNavigationState.CurrentSelectedTile = tile;
                        }
                    }
                    else if (target.Tile.Valid && !target.HasThing && !target.Cell.IsValid)
                    {
                        WorldNavigationState.CurrentSelectedTile = target.Tile;
                    }
                    else if (MapNavigationState.IsInitialized)
                    {
                        // Map target - update map cursor
                        if (target.HasThing)
                            MapNavigationState.CurrentCursorPosition = target.Thing.Position;
                        else if (target.Cell.IsValid)
                            MapNavigationState.CurrentCursorPosition = target.Cell;
                    }

                    Close();
                    TolkHelper.Speak($"Jumped to {GetTargetDescription(target)}");
                }
            };
        }

        /// <summary>
        /// Creates an accessible action wrapper for a letter button.
        /// Handles Jump to Location, View Quest, Research hyperlinks, etc.
        /// </summary>
        private static Action CreateAccessibleAction(DiaOption option, Letter letter, string label)
        {
            // Check for Jump to Location
            if (IsJumpAction(label))
            {
                return () => {
                    GlobalTargetInfo target = letter.lookTargets?.TryGetPrimaryTarget() ?? GlobalTargetInfo.Invalid;
                    if (target.IsValid)
                    {
                        // For world targets, set pending tile BEFORE CameraJumper opens world view.
                        // This is critical because WorldNavigationState.Open() is called in the next frame
                        // when WorldNavigationPatch detects the mode change, and it would otherwise
                        // default to the colony tile. PendingStartTile is checked first in Open().
                        if (target.HasWorldObject)
                        {
                            PlanetTile tile = target.WorldObject.Tile;
                            if (tile.Valid)
                            {
                                WorldNavigationState.PendingStartTile = tile;
                            }
                        }
                        else if (target.Tile.Valid && !target.HasThing && !target.Cell.IsValid)
                        {
                            WorldNavigationState.PendingStartTile = target.Tile;
                        }

                        CameraJumper.TryJumpAndSelect(target);

                        // Also set current tile in case world view was already open (Open() won't be called)
                        if (target.HasWorldObject)
                        {
                            PlanetTile tile = target.WorldObject.Tile;
                            if (tile.Valid)
                            {
                                WorldNavigationState.CurrentSelectedTile = tile;
                            }
                        }
                        else if (target.Tile.Valid && !target.HasThing && !target.Cell.IsValid)
                        {
                            WorldNavigationState.CurrentSelectedTile = target.Tile;
                        }
                        else if (MapNavigationState.IsInitialized)
                        {
                            // Map target - update map cursor
                            if (target.HasThing)
                                MapNavigationState.CurrentCursorPosition = target.Thing.Position;
                            else if (target.Cell.IsValid)
                                MapNavigationState.CurrentCursorPosition = target.Cell;
                        }

                        Find.LetterStack.RemoveLetter(letter);
                        Close();
                        TolkHelper.Speak($"Jumped to {GetTargetDescription(target)}");
                    }
                    else
                    {
                        TolkHelper.Speak("Target location is not valid");
                    }
                };
            }

            // Check for View in Quests Tab
            if (IsViewQuestAction(label) && letter is ChoiceLetter choiceLetter && choiceLetter.quest != null)
            {
                Quest quest = choiceLetter.quest;
                return () => {
                    Close();
                    QuestMenuState.OpenAndSelectQuest(quest);
                };
            }

            // Check for Research hyperlinks
            if (option.hyperlink.def is ResearchProjectDef researchDef)
            {
                return () => {
                    Close();
                    WindowlessResearchMenuState.OpenAndSelectProject(researchDef);
                };
            }

            // Check for item/hediff info cards - announce and use vanilla for now
            if (option.hyperlink.def != null)
            {
                return () => {
                    TolkHelper.Speak($"Opening info card for {option.hyperlink.Label}");
                    option.action?.Invoke();
                };
            }

            // Default: use vanilla action
            return () => {
                option.action?.Invoke();
            };
        }

        /// <summary>
        /// Checks if the button label indicates a Jump to Location action.
        /// </summary>
        private static bool IsJumpAction(string label)
        {
            if (string.IsNullOrEmpty(label))
                return false;

            string lowerLabel = label.ToLower();
            return lowerLabel.Contains("jump") ||
                   lowerLabel.Contains("location") ||
                   label == "JumpToLocation".Translate();
        }

        /// <summary>
        /// Checks if the button label indicates a View Quest action.
        /// </summary>
        private static bool IsViewQuestAction(string label)
        {
            if (string.IsNullOrEmpty(label))
                return false;

            string lowerLabel = label.ToLower();
            return lowerLabel.Contains("quest") ||
                   (lowerLabel.Contains("view") && lowerLabel.Contains("related")) ||
                   label.Contains("ViewRelatedQuest".Translate());
        }

        /// <summary>
        /// Gets a human-readable description of a target location.
        /// </summary>
        private static string GetTargetDescription(GlobalTargetInfo target)
        {
            if (!target.IsValid)
                return "unknown location";

            if (target.HasThing)
            {
                Thing thing = target.Thing;
                if (thing is Pawn pawn)
                    return pawn.LabelShort;
                return thing.LabelShort ?? thing.def?.label ?? "thing";
            }

            if (target.Cell.IsValid)
            {
                return $"position {target.Cell.x}, {target.Cell.z}";
            }

            if (target.HasWorldObject)
            {
                return target.WorldObject.LabelShort ?? "world location";
            }

            return "target location";
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
        /// Jumps to the first item in the list. Returns to list view if in detail view.
        /// </summary>
        public static void JumpToFirst()
        {
            if (notifications == null || notifications.Count == 0)
                return;

            bool wasInDetailView = detailHelper.IsInDetailView;
            detailHelper.GoBackToList();
            currentIndex = MenuHelper.JumpToFirst();
            detailHelper.ResetDetailPosition();
            typeahead.ClearSearch();
            detailHelper.RefreshButtons();

            if (wasInDetailView)
                TwoLevelMenuHelper.SpeakReturnToList();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the last item in the list. Returns to list view if in detail view.
        /// </summary>
        public static void JumpToLast()
        {
            if (notifications == null || notifications.Count == 0)
                return;

            bool wasInDetailView = detailHelper.IsInDetailView;
            detailHelper.GoBackToList();
            currentIndex = MenuHelper.JumpToLast(notifications.Count);
            detailHelper.ResetDetailPosition();
            typeahead.ClearSearch();
            detailHelper.RefreshButtons();

            if (wasInDetailView)
                TwoLevelMenuHelper.SpeakReturnToList();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the start of detail view (header position).
        /// </summary>
        public static void JumpToDetailStart()
        {
            detailHelper?.JumpToDetailStart();
        }

        /// <summary>
        /// Jumps to the end of detail view (buttons section).
        /// </summary>
        public static void JumpToDetailEnd()
        {
            detailHelper?.JumpToDetailEnd();
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

            string announcement = $"{GetTypeLabel(item.Type)}: {item.Label}. {MenuHelper.FormatPosition(currentIndex, notifications.Count)}";

            // Add search context if active
            if (typeahead.HasActiveSearch)
            {
                announcement += typeahead.BuildSearchContextSuffix();
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
                typeahead.SpeakNoMatches();
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
            /// Gets the source Alert object if this is an alert notification.
            /// </summary>
            public Alert GetSourceAlert()
            {
                return sourceObject as Alert;
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
