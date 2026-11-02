using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using RimWorld;
using RimWorld.Planet;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// The notification (messages/letters/alerts) data model and domain actions. Navigation,
    /// typeahead, detail-view position and every announcement live on
    /// <see cref="RimWorldAccess.Shell.NotificationScope"/>; this class owns the collection, the
    /// letter/alert button extraction, and the public surface external callers depend on
    /// (<see cref="IsActive"/>, <see cref="Open"/>, <see cref="Close"/>). Reflection into vanilla's
    /// private members resolves through the shared <see cref="VanillaAccess"/> cache.
    /// </summary>
    public static class NotificationMenuState
    {
        private static bool isActive = false;
        private static List<NotificationItem> notifications = null;
        private static int openGeneration = 0;

        public static bool IsActive => isActive;

        /// <summary>The live letter/message/alert model; <see cref="RimWorldAccess.Shell.NotificationScope"/> owns the cursor into it.</summary>
        internal static List<NotificationItem> Notifications => notifications;

        /// <summary>
        /// Bumped once per genuine <see cref="Open"/> call, never by <see cref="Refresh"/>, so
        /// <see cref="RimWorldAccess.Shell.NotificationScope.OnPush"/> can tell a fresh open apart
        /// from a pop/re-push caused by an info card opened over an active session.
        /// </summary>
        internal static int OpenGeneration => openGeneration;

        /// <summary>
        /// Collects all messages, letters and alerts, refusing (with an announcement) when there are
        /// none. Cursor reset and the opening announcement belong to the scope's <c>OnFocus</c>.
        /// </summary>
        public static void Open()
        {
            if (!GuardHelper.RequireMap()) return;

            notifications = CollectNotifications();

            if (notifications.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Notifications.Menu.None".Loc());
                return;
            }

            isActive = true;
            openGeneration++;
        }

        /// <summary>
        /// Closes silently: callers speak their OWN message afterward (a jump, a button action that
        /// emptied the list), so this must never announce on its own.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            notifications = null;
        }

        /// <summary>Re-collects the live model after a mutation (delete, button action).</summary>
        internal static void Refresh()
        {
            notifications = CollectNotifications();
        }

        /// <summary>The human-readable label for a notification type.</summary>
        internal static string GetTypeLabel(NotificationType type)
        {
            return (type == NotificationType.Message ? "RimWorldAccess.Notifications.Type.Message" :
                    type == NotificationType.Letter ? "RimWorldAccess.Notifications.Type.Letter" :
                    "RimWorldAccess.Notifications.Type.Alert").Translate();
        }

        /// <summary>Populates the detail-view buttons for one item, for the scope's TwoLevelMenuHelper.</summary>
        internal static void PopulateButtons(NotificationItem item, List<ButtonInfo> buttons)
        {
            if (item == null)
                return;

            switch (item.Type)
            {
                case NotificationType.Letter:
                    ExtractLetterButtons(item, buttons);
                    break;
                case NotificationType.Alert:
                    ExtractAlertButtons(item, buttons);
                    break;
                // Messages don't have buttons.
            }
        }

        /// <summary>All live messages, letters and alerts, newest first.</summary>
        private static List<NotificationItem> CollectNotifications()
        {
            List<NotificationItem> items = new List<NotificationItem>();

            try
            {
                FieldInfo messagesField = VanillaAccess.GetField(typeof(Messages), "liveMessages");
                List<Message> liveMessages = messagesField?.GetValue(null) as List<Message>;
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
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to collect messages: {ex.Message}");
            }

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

            try
            {
                if (Find.Alerts != null)
                {
                    FieldInfo activeAlertsField = VanillaAccess.GetField(typeof(AlertsReadout), "activeAlerts");
                    List<Alert> activeAlerts = activeAlertsField?.GetValue(Find.Alerts) as List<Alert>;
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
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to collect alerts: {ex.Message}");
            }

            items.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));

            return items;
        }

        /// <summary>The buttons of a letter notification.</summary>
        private static void ExtractLetterButtons(NotificationItem item, List<ButtonInfo> buttons)
        {
            Letter letter = item.GetSourceLetter();
            if (letter == null)
                return;

            // ChoiceLetter_GrowthMoment extends LetterWithTimeout, NOT ChoiceLetter.
            if (letter is ChoiceLetter_GrowthMoment growthLetter)
            {
                string label = (growthLetter.ArchiveView
                    ? "RimWorldAccess.Notifications.Button.ViewChoices"
                    : "RimWorldAccess.Notifications.Button.OpenGrowthMoment").Translate();
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

            if (letter is ChoiceLetter choiceLetter)
            {
                try
                {
                    IEnumerable<DiaOption> choices = choiceLetter.Choices;
                    if (choices != null)
                    {
                        FieldInfo textField = VanillaAccess.GetField(typeof(DiaOption), "text");
                        foreach (DiaOption option in choices)
                        {
                            string label = textField?.GetValue(option)?.ToString()
                                ?? "RimWorldAccess.Notifications.Type.Unknown".Translate();

                            // The Delete key already covers Close.
                            if (IsCloseAction(label))
                                continue;

                            ButtonInfo buttonInfo = new ButtonInfo
                            {
                                Label = label,
                                Action = CreateAccessibleAction(option, letter, label, item),
                                IsDisabled = option.disabled,
                                DisabledReason = option.disabledReason
                            };

                            buttons.Add(buttonInfo);
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
        /// The buttons of an alert notification. Alerts with culprit targets get a "jump to" button
        /// per target; alerts with none fall back to the alert's own <c>OnClick</c>, which already
        /// reproduces vanilla's click behavior without a per-alert-type special case.
        /// </summary>
        private static void ExtractAlertButtons(NotificationItem item, List<ButtonInfo> buttons)
        {
            Alert alert = item.GetSourceAlert();
            if (alert == null)
                return;

            try
            {
                AlertReport alertReport = alert.GetReport();

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

                if (targets.Count == 1)
                {
                    var target = targets[0];
                    buttons.Add(new ButtonInfo
                    {
                        Label = "RimWorldAccess.Notifications.Button.JumpTo".Translate(GetTargetDescription(target)),
                        Action = CreateJumpToTargetAction(target, item),
                        IsDisabled = false,
                        IsJumpAction = true
                    });
                }
                else if (targets.Count > 1)
                {
                    int count = Math.Min(targets.Count, 5);
                    for (int i = 0; i < count; i++)
                    {
                        var target = targets[i];
                        int index = i + 1;
                        buttons.Add(new ButtonInfo
                        {
                            Label = "RimWorldAccess.Notifications.Button.JumpToWithIndex".Translate(
                                GetTargetDescription(target), index, targets.Count),
                            Action = CreateJumpToTargetAction(target, item),
                            IsDisabled = false,
                            IsJumpAction = true
                        });
                    }
                }

                if (buttons.Count == 0)
                {
                    MethodInfo onClickMethod = VanillaAccess.GetMethod(typeof(Alert), "OnClick");

                    if (onClickMethod != null)
                    {
                        buttons.Add(new ButtonInfo
                        {
                            Label = "RimWorldAccess.Notifications.Button.Activate".Translate(),
                            Action = () => {
                                try
                                {
                                    // A window OnClick opens is deliberately driven, even non-modal.
                                    Shell.ScopeForWindow.ArmDeliberateGenericAttach();
                                    onClickMethod.Invoke(alert, null);
                                    TolkHelper.Speak("RimWorldAccess.Notifications.Action.AlertActivated".Loc());
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

        /// <summary>An action that jumps to a target and moves the map or world cursor onto it.</summary>
        private static Action CreateJumpToTargetAction(GlobalTargetInfo target, NotificationItem item)
        {
            return () => {
                if (target.IsValid)
                {
                    // The pending tile must be set BEFORE CameraJumper opens the world view:
                    // WorldNavigationState.Open() runs a frame later, once WorldNavigationPatch sees
                    // the mode change, and would otherwise default to the colony tile.
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

                    // The world view may already be open, in which case Open() never runs.
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
                        if (target.HasThing)
                            MapNavigationState.CurrentCursorPosition = target.Thing.Position;
                        else if (target.Cell.IsValid)
                            MapNavigationState.CurrentCursorPosition = target.Cell;
                    }

                    ArmReturn(item);
                    Close();
                    MapNavigationState.SpeakJumpedTo(GetTargetDescription(target));
                }
            };
        }

        private static void ArmReturn(NotificationItem item)
        {
            ScreenReturn.Arm(ScreenReturn.Binding.Map, NotificationScope.ScreenName,
                new ListReturnCursor(item, notifications != null ? notifications.IndexOf(item) : -1), Open);
        }

        /// <summary>The accessible action for a letter button: jump to location, view quest, research hyperlink, or the option's own action.</summary>
        private static Action CreateAccessibleAction(DiaOption option, Letter letter, string label, NotificationItem item)
        {
            if (IsJumpAction(label))
            {
                return () => {
                    GlobalTargetInfo target = letter.lookTargets?.TryGetPrimaryTarget() ?? GlobalTargetInfo.Invalid;
                    if (target.IsValid)
                    {
                        // The pending tile must be set BEFORE CameraJumper opens the world view:
                        // WorldNavigationState.Open() runs a frame later and would otherwise default
                        // to the colony tile.
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

                        // Only the option's own action knows whether the jump consumes the letter:
                        // Option_JumpToLocation removes it, Option_JumpToLocationAndPostpone (offer
                        // letters) must not, since a removed offer is unrecoverable.
                        option.action?.Invoke();

                        // The world view may already be open, in which case Open() never runs.
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
                                if (target.HasThing)
                                MapNavigationState.CurrentCursorPosition = target.Thing.Position;
                            else if (target.Cell.IsValid)
                                MapNavigationState.CurrentCursorPosition = target.Cell;
                        }

                        ArmReturn(item);
                        Close();
                        MapNavigationState.SpeakJumpedTo(GetTargetDescription(target));
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Notifications.Menu.TargetNotValid".Loc());
                    }
                };
            }

            if (IsViewQuestAction(label) && letter is ChoiceLetter choiceLetter && choiceLetter.quest != null)
            {
                Quest quest = choiceLetter.quest;
                return () => {
                    Close();

                    // Only the option's own action decides whether the letter survives:
                    // Option_ViewInQuestsTab removes it, its postpone form keeps it.
                    option.action?.Invoke();

                    QuestMenuState.OpenAndSelectQuest(quest);
                };
            }

            if (option.hyperlink.def is ResearchProjectDef researchDef)
            {
                return () => {
                    Close();
                    WindowlessResearchMenuState.OpenAndSelectProject(researchDef);
                };
            }

            // Item/hediff info cards: announce, then let vanilla open the card.
            if (option.hyperlink.def != null)
            {
                return () => {
                    TolkHelper.Speak("RimWorldAccess.Notifications.Action.OpeningInfoCard".Loc(option.hyperlink.Label));
                    option.action?.Invoke();
                };
            }

            return () => {
                option.action?.Invoke();
            };
        }

        /// <summary>Whether the label is Option_Close's, matched on the exact translated key, never an English substring.</summary>
        private static bool IsCloseAction(string label)
        {
            if (string.IsNullOrEmpty(label))
                return false;

            return label == "Close".Translate();
        }

        /// <summary>Whether the label is a jump-to-location option's, matched on the exact translated key, never an English substring.</summary>
        private static bool IsJumpAction(string label)
        {
            if (string.IsNullOrEmpty(label))
                return false;

            return label == "JumpToLocation".Translate();
        }

        /// <summary>
        /// Whether the label is Option_ViewInQuestsTab's. Its label is the "ViewRelatedQuest" or
        /// "ViewQuest" key, optionally suffixed with ": " + quest name, so this matches on prefix.
        /// </summary>
        private static bool IsViewQuestAction(string label)
        {
            if (string.IsNullOrEmpty(label))
                return false;

            return label.StartsWith("ViewRelatedQuest".Translate()) ||
                   label.StartsWith("ViewQuest".Translate());
        }

        /// <summary>A human-readable description of a target location.</summary>
        private static string GetTargetDescription(GlobalTargetInfo target)
        {
            if (!target.IsValid)
                return "RimWorldAccess.Notifications.Target.Unknown".Translate();

            if (target.HasThing)
            {
                Thing thing = target.Thing;
                if (thing is Pawn pawn)
                    return pawn.LabelShort;
                return thing.LabelShort ?? thing.def?.label
                    ?? "RimWorldAccess.Notifications.Target.Thing".Translate().ToString();
            }

            if (target.Cell.IsValid)
            {
                return "RimWorldAccess.Notifications.Target.Position".Translate(target.Cell.x, target.Cell.z);
            }

            if (target.HasWorldObject)
            {
                return target.WorldObject.LabelShort
                    ?? "RimWorldAccess.Notifications.Target.WorldLocation".Translate().ToString();
            }

            return "RimWorldAccess.Notifications.Target.Generic".Translate();
        }

        /// <summary>One message, letter or alert. Internal because <see cref="RimWorldAccess.Shell.NotificationScope"/> reads it.</summary>
        internal class NotificationItem
        {
            public NotificationType Type { get; private set; }
            public string Label { get; private set; }
            public string Explanation { get; private set; }
            public bool HasValidTarget { get; private set; }
            public int Timestamp { get; private set; } // Game tick or arrival tick, for sorting.
            public string[] ExplanationLines { get; private set; }

            private object sourceObject;

            /// <summary>Fills <see cref="ExplanationLines"/> with the tag-stripped, non-blank lines of the explanation.</summary>
            private void ProcessExplanation()
            {
                if (string.IsNullOrEmpty(Explanation))
                {
                    ExplanationLines = new string[0];
                    return;
                }

                string cleanedExplanation = StripTags(Explanation);

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

            /// <summary>Text with XML-style tags (colors and the like), self-closing or paired, removed.</summary>
            private string StripTags(string text)
            {
                if (string.IsNullOrEmpty(text))
                    return text;

                System.Text.RegularExpressions.Regex tagRegex =
                    new System.Text.RegularExpressions.Regex(@"</?[a-zA-Z][^>]*>");

                return tagRegex.Replace(text, "");
            }

            public NotificationItem(Message message)
            {
                Type = NotificationType.Message;
                Label = StripTags(message.text);
                Explanation = "";
                HasValidTarget = message.lookTargets != null && message.lookTargets.IsValid();
                Timestamp = message.startingFrame;
                sourceObject = message;
                ProcessExplanation();
            }

            public NotificationItem(Letter letter)
            {
                Type = NotificationType.Letter;
                Label = StripTags(letter.Label);

                // Letter.GetMouseoverText is protected abstract.
                try
                {
                    MethodInfo getMouseoverTextMethod = VanillaAccess.GetMethod(typeof(Letter), "GetMouseoverText");
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
                Timestamp = letter.arrivalTick;
                sourceObject = letter;
                ProcessExplanation();
            }

            public NotificationItem(Alert alert)
            {
                Type = NotificationType.Alert;
                Label = StripTags(alert.Label);

                try
                {
                    Explanation = alert.GetExplanation();
                }
                catch
                {
                    Explanation = "";
                }

                try
                {
                    AlertReport report = alert.GetReport();
                    HasValidTarget = report.AnyCulpritValid;
                }
                catch
                {
                    HasValidTarget = false;
                }

                Timestamp = Find.TickManager?.TicksGame ?? 0; // Alerts are ongoing, so they sort as "now".
                sourceObject = alert;
                ProcessExplanation();
            }

            internal bool SameSource(NotificationItem other)
            {
                return other != null && ReferenceEquals(other.sourceObject, sourceObject);
            }

            /// <summary>The source Letter, or null when this is not a letter.</summary>
            public Letter GetSourceLetter()
            {
                return sourceObject as Letter;
            }

            /// <summary>The source Alert, or null when this is not an alert.</summary>
            public Alert GetSourceAlert()
            {
                return sourceObject as Alert;
            }

            /// <summary>The first valid target to jump to, or <see cref="GlobalTargetInfo.Invalid"/>.</summary>
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
                    }
                }

                return GlobalTargetInfo.Invalid;
            }
        }

        /// <summary>Internal because <see cref="RimWorldAccess.Shell.NotificationScope"/> reads it.</summary>
        internal enum NotificationType
        {
            Message,
            Letter,
            Alert
        }
    }
}
