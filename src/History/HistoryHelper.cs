using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for extracting data from the History tab UI.
    /// Provides utilities for reading statistics and archive items.
    /// </summary>
    public static class HistoryHelper
    {
        // Regex to strip XML/color tags from text
        private static readonly Regex TagRegex = new Regex(@"</?[a-zA-Z][^>]*>");

        /// <summary>
        /// Represents a single statistic entry from the Statistics tab.
        /// </summary>
        public class StatisticEntry
        {
            public string Name { get; set; }
            public string Value { get; set; }
            public string Tooltip { get; set; }

            public StatisticEntry(string name, string value, string tooltip = null)
            {
                Name = name;
                Value = value;
                Tooltip = tooltip;
            }

            /// <summary>
            /// Formats the statistic for screen reader announcement.
            /// </summary>
            public string ToAnnouncement()
            {
                string announcement = $"{Name}: {Value}";
                if (!string.IsNullOrEmpty(Tooltip))
                {
                    announcement += $". {Tooltip}";
                }
                return announcement;
            }
        }

        /// <summary>
        /// Collects all statistics from the Statistics tab.
        /// Matches exactly what DoStatisticsPage() displays in the game.
        /// </summary>
        public static List<StatisticEntry> CollectStatistics()
        {
            var stats = new List<StatisticEntry>();

            try
            {
                // 1. Real playtime (not in-game simulation time)
                // Game uses: Find.GameInfo.RealPlayTimeInteracting (returns seconds as float)
                if (Find.GameInfo != null)
                {
                    float secondsPlayed = Find.GameInfo.RealPlayTimeInteracting;
                    TimeSpan realPlaytime = TimeSpan.FromSeconds(secondsPlayed);
                    string playtimeFormatted = FormatPlaytime(realPlaytime);
                    stats.Add(new StatisticEntry("Playtime".Translate(), playtimeFormatted));
                }

                // 2. Storyteller
                if (Find.Storyteller != null)
                {
                    string storyteller = Find.Storyteller.def?.LabelCap ?? "Unknown";
                    stats.Add(new StatisticEntry("Storyteller".Translate(), storyteller));

                    // 3. Difficulty
                    string difficultyName = Find.Storyteller.difficultyDef?.LabelCap ?? "Unknown";
                    stats.Add(new StatisticEntry("Difficulty".Translate(), difficultyName));
                }

                // Map-specific statistics (only if a map is loaded)
                Map currentMap = Find.CurrentMap;
                if (currentMap != null)
                {
                    // 4-7. Colony wealth breakdown
                    if (currentMap.wealthWatcher != null)
                    {
                        float totalWealth = currentMap.wealthWatcher.WealthTotal;
                        stats.Add(new StatisticEntry("ColonyWealthTotal".Translate(), totalWealth.ToString("F0")));

                        float itemWealth = currentMap.wealthWatcher.WealthItems;
                        stats.Add(new StatisticEntry("ColonyWealthItems".Translate(), itemWealth.ToString("F0")));

                        float buildingWealth = currentMap.wealthWatcher.WealthBuildings;
                        stats.Add(new StatisticEntry("ColonyWealthBuildings".Translate(), buildingWealth.ToString("F0")));

                        float pawnWealth = currentMap.wealthWatcher.WealthPawns;
                        stats.Add(new StatisticEntry("ColonyWealthColonistsAndTameAnimals".Translate(), pawnWealth.ToString("F0")));
                    }
                }

                // 8-9. Threat statistics (global, not map-specific)
                if (Find.StoryWatcher?.statsRecord != null)
                {
                    int numThreatBigs = Find.StoryWatcher.statsRecord.numThreatBigs;
                    stats.Add(new StatisticEntry("NumThreatBigs".Translate(), numThreatBigs.ToString()));

                    int numRaidsEnemy = Find.StoryWatcher.statsRecord.numRaidsEnemy;
                    stats.Add(new StatisticEntry("NumEnemyRaids".Translate(), numRaidsEnemy.ToString()));
                }

                // 10. Damage taken (map-specific)
                if (currentMap != null && currentMap.damageWatcher != null)
                {
                    float damage = currentMap.damageWatcher.DamageTakenEver;
                    stats.Add(new StatisticEntry("DamageTaken".Translate(), damage.ToString("F0")));
                }

                // 11-12. Colonist casualties (global)
                if (Find.StoryWatcher?.statsRecord != null)
                {
                    int colonistsKilled = Find.StoryWatcher.statsRecord.colonistsKilled;
                    stats.Add(new StatisticEntry("ColonistsKilled".Translate(), colonistsKilled.ToString()));

                    int colonistsLaunched = Find.StoryWatcher.statsRecord.colonistsLaunched;
                    stats.Add(new StatisticEntry("ColonistsLaunched".Translate(), colonistsLaunched.ToString()));
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to collect statistics: {ex.Message}");
            }

            return stats;
        }

        /// <summary>
        /// Formats a TimeSpan as playtime using the game's translation keys.
        /// Format: "X days, Y hours, Z minutes, W seconds" (matching game's format)
        /// </summary>
        private static string FormatPlaytime(TimeSpan playtime)
        {
            var parts = new List<string>();

            if (playtime.Days > 0)
            {
                parts.Add($"{playtime.Days}{"LetterDay".Translate()}");
            }
            if (playtime.Hours > 0)
            {
                parts.Add($"{playtime.Hours}{"LetterHour".Translate()}");
            }
            if (playtime.Minutes > 0)
            {
                parts.Add($"{playtime.Minutes}{"LetterMinute".Translate()}");
            }
            if (playtime.Seconds > 0 || parts.Count == 0)
            {
                parts.Add($"{playtime.Seconds}{"LetterSecond".Translate()}");
            }

            return string.Join(" ", parts);
        }

        /// <summary>
        /// Wrapper for IArchivable items with accessor properties.
        /// </summary>
        public class ArchiveItemWrapper
        {
            private readonly IArchivable source;

            public ArchiveItemWrapper(IArchivable archivable)
            {
                source = archivable;
            }

            public IArchivable Source => source;

            public string Label => StripTags(source.ArchivedLabel ?? "Unknown");

            public string Tooltip => StripTags(source.ArchivedTooltip ?? "");

            public string[] TooltipLines
            {
                get
                {
                    if (string.IsNullOrEmpty(Tooltip))
                        return new string[0];

                    string[] lines = Tooltip.Split('\n');
                    var nonEmpty = new List<string>();
                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            nonEmpty.Add(trimmed);
                    }
                    return nonEmpty.ToArray();
                }
            }

            public int Timestamp => source.CreatedTicksGame;

            public bool HasValidTarget => source.LookTargets?.IsValid ?? false;

            public GlobalTargetInfo PrimaryTarget => source.LookTargets?.TryGetPrimaryTarget() ?? GlobalTargetInfo.Invalid;

            public bool IsPinned => Find.Archive?.IsPinned(source) ?? false;

            public bool IsLetter => source is Letter;

            public bool IsMessage => source is Message;

            public string TypeLabel => IsLetter ? "Letter" : IsMessage ? "Message" : "Item";

            public string DateLabel
            {
                get
                {
                    if (source.CreatedTicksGame <= 0)
                        return "Unknown date";

                    try
                    {
                        return GenDate.DateShortStringAt(source.CreatedTicksGame, Find.WorldGrid?.LongLatOf(0) ?? default);
                    }
                    catch
                    {
                        return "Unknown date";
                    }
                }
            }

            /// <summary>
            /// Opens the archived item (shows its full content).
            /// </summary>
            public void Open()
            {
                try
                {
                    source.OpenArchived();
                }
                catch (Exception ex)
                {
                    Log.Warning($"RimWorld Access: Failed to open archived item: {ex.Message}");
                }
            }

            /// <summary>
            /// Toggles the pinned state of this item.
            /// </summary>
            public void TogglePin()
            {
                try
                {
                    if (Find.Archive == null)
                        return;

                    // Archive uses Pin() and Unpin() methods, not SetPinned
                    if (IsPinned)
                    {
                        Find.Archive.Unpin(source);
                    }
                    else
                    {
                        Find.Archive.Pin(source);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"RimWorld Access: Failed to toggle pin: {ex.Message}");
                }
            }

            /// <summary>
            /// Jumps the camera to this item's location.
            /// </summary>
            public void JumpTo()
            {
                if (!HasValidTarget)
                {
                    TolkHelper.Speak("No location available");
                    return;
                }

                try
                {
                    // For world targets, set pending tile BEFORE CameraJumper opens world view.
                    // This is critical because WorldNavigationState.Open() is called in the next frame
                    // when WorldNavigationPatch detects the mode change, and it would otherwise
                    // default to the colony tile. PendingStartTile is checked first in Open().
                    if (PrimaryTarget.HasWorldObject)
                    {
                        PlanetTile tile = PrimaryTarget.WorldObject.Tile;
                        if (tile.Valid)
                        {
                            WorldNavigationState.PendingStartTile = tile;
                        }
                    }
                    else if (PrimaryTarget.Tile.Valid && !PrimaryTarget.HasThing && !PrimaryTarget.Cell.IsValid)
                    {
                        WorldNavigationState.PendingStartTile = PrimaryTarget.Tile;
                    }

                    CameraJumper.TryJumpAndSelect(PrimaryTarget);

                    // Also set current tile in case world view was already open (Open() won't be called)
                    if (PrimaryTarget.HasWorldObject)
                    {
                        PlanetTile tile = PrimaryTarget.WorldObject.Tile;
                        if (tile.Valid)
                        {
                            WorldNavigationState.CurrentSelectedTile = tile;
                        }
                    }
                    else if (PrimaryTarget.Tile.Valid && !PrimaryTarget.HasThing && !PrimaryTarget.Cell.IsValid)
                    {
                        WorldNavigationState.CurrentSelectedTile = PrimaryTarget.Tile;
                    }
                    else if (MapNavigationState.IsInitialized && PrimaryTarget.HasThing)
                    {
                        // Map target with thing - update map cursor
                        MapNavigationState.CurrentCursorPosition = PrimaryTarget.Thing.Position;
                    }
                    else if (MapNavigationState.IsInitialized && PrimaryTarget.Cell.IsValid)
                    {
                        // Map target with cell - update map cursor
                        MapNavigationState.CurrentCursorPosition = PrimaryTarget.Cell;
                    }

                    TolkHelper.Speak($"Jumped to {GetTargetDescription()}");
                }
                catch (Exception ex)
                {
                    Log.Warning($"RimWorld Access: Failed to jump to location: {ex.Message}");
                    TolkHelper.Speak("Failed to jump to location");
                }
            }

            /// <summary>
            /// Gets a human-readable description of the target.
            /// </summary>
            public string GetTargetDescription()
            {
                if (!HasValidTarget)
                    return "unknown location";

                var target = PrimaryTarget;

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
            /// Builds the announcement for list view.
            /// Format: "Pinned, Label, Letter, Date. X of Y"
            /// Content first so users can stop listening once they've heard what they need.
            /// </summary>
            public string BuildListAnnouncement(int index, int total)
            {
                var parts = new List<string>();

                if (IsPinned)
                    parts.Add("Pinned");

                parts.Add(Label);
                parts.Add(TypeLabel);
                parts.Add(DateLabel);

                string announcement = string.Join(", ", parts);
                string position = MenuHelper.FormatPosition(index, total);
                if (!string.IsNullOrEmpty(position))
                    announcement += $". {position}";

                return announcement;
            }
        }

        /// <summary>
        /// Collects archive items from Find.Archive with filtering.
        /// </summary>
        /// <param name="includeLetters">Include Letter type items</param>
        /// <param name="includeMessages">Include Message type items</param>
        /// <returns>List of archive items sorted by creation time (newest first)</returns>
        public static List<ArchiveItemWrapper> CollectArchiveItems(bool includeLetters, bool includeMessages)
        {
            var items = new List<ArchiveItemWrapper>();

            try
            {
                if (Find.Archive == null || Find.Archive.ArchivablesListForReading == null)
                    return items;

                foreach (IArchivable archivable in Find.Archive.ArchivablesListForReading)
                {
                    bool include = false;

                    if (includeLetters && archivable is Letter)
                        include = true;
                    else if (includeMessages && archivable is Message)
                        include = true;

                    if (include)
                    {
                        items.Add(new ArchiveItemWrapper(archivable));
                    }
                }

                // Sort by timestamp (newest first)
                items.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to collect archive items: {ex.Message}");
            }

            return items;
        }

        /// <summary>
        /// Gets labels for typeahead search from archive items.
        /// </summary>
        public static List<string> GetArchiveLabels(List<ArchiveItemWrapper> items)
        {
            var labels = new List<string>();
            foreach (var item in items)
            {
                labels.Add(item.Label);
            }
            return labels;
        }

        /// <summary>
        /// Gets labels for typeahead search from statistic entries.
        /// </summary>
        public static List<string> GetStatisticLabels(List<StatisticEntry> stats)
        {
            var labels = new List<string>();
            foreach (var stat in stats)
            {
                labels.Add(stat.Name);
            }
            return labels;
        }

        /// <summary>
        /// Strips XML-style tags from text.
        /// </summary>
        public static string StripTags(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return TagRegex.Replace(text, "");
        }

        /// <summary>
        /// Formats play time as human-readable string.
        /// </summary>
        private static string FormatPlayTime(TimeSpan time)
        {
            if (time.TotalDays >= 1)
            {
                return $"{(int)time.TotalDays} days, {time.Hours} hours, {time.Minutes} minutes";
            }
            else if (time.TotalHours >= 1)
            {
                return $"{(int)time.TotalHours} hours, {time.Minutes} minutes";
            }
            else
            {
                return $"{(int)time.TotalMinutes} minutes";
            }
        }

        /// <summary>
        /// Formats a number as currency (e.g., "45,230").
        /// </summary>
        private static string FormatCurrency(float value)
        {
            return ((int)value).ToString("N0");
        }

        /// <summary>
        /// Formats a large number with commas.
        /// </summary>
        private static string FormatNumber(float value)
        {
            return ((int)value).ToString("N0");
        }

        /// <summary>
        /// Gets the currently open MainTabWindow_History, if any.
        /// </summary>
        public static MainTabWindow_History GetOpenHistoryWindow()
        {
            if (Find.WindowStack == null)
                return null;

            foreach (var window in Find.WindowStack.Windows)
            {
                if (window is MainTabWindow_History historyWindow)
                    return historyWindow;
            }

            return null;
        }

        /// <summary>
        /// Gets the current tab from the History window.
        /// Returns -1 if window not found.
        /// </summary>
        public static int GetCurrentTab()
        {
            var window = GetOpenHistoryWindow();
            if (window == null)
                return -1;

            try
            {
                // curTab is a static field in MainTabWindow_History
                FieldInfo curTabField = AccessTools.Field(typeof(MainTabWindow_History), "curTab");
                if (curTabField != null)
                {
                    object tabValue = curTabField.GetValue(null);
                    return (int)tabValue;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to get current tab: {ex.Message}");
            }

            return -1;
        }

        /// <summary>
        /// Sets the current tab on the History window (for visual sync).
        /// </summary>
        /// <param name="tabIndex">Tab index (0=Graph, 1=Messages, 2=Statistics in RimWorld's enum order)</param>
        public static void SetCurrentTab(int tabIndex)
        {
            try
            {
                // curTab is a static field in MainTabWindow_History
                FieldInfo curTabField = AccessTools.Field(typeof(MainTabWindow_History), "curTab");
                if (curTabField != null)
                {
                    // Get the HistoryTab enum type
                    Type historyTabType = curTabField.FieldType;
                    object tabValue = Enum.ToObject(historyTabType, tabIndex);
                    curTabField.SetValue(null, tabValue);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to set current tab: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the filter states from the History window (for Messages tab).
        /// </summary>
        public static (bool showLetters, bool showMessages) GetFilterStates()
        {
            bool showLetters = true;
            bool showMessages = false;

            try
            {
                // These are instance fields on MainTabWindow_History
                var window = GetOpenHistoryWindow();
                if (window != null)
                {
                    FieldInfo showLettersField = AccessTools.Field(typeof(MainTabWindow_History), "showLetters");
                    FieldInfo showMessagesField = AccessTools.Field(typeof(MainTabWindow_History), "showMessages");

                    if (showLettersField != null)
                        showLetters = (bool)showLettersField.GetValue(window);
                    if (showMessagesField != null)
                        showMessages = (bool)showMessagesField.GetValue(window);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to get filter states: {ex.Message}");
            }

            return (showLetters, showMessages);
        }

        /// <summary>
        /// Sets the filter states on the History window (for visual sync).
        /// </summary>
        public static void SetFilterStates(bool showLetters, bool showMessages)
        {
            try
            {
                var window = GetOpenHistoryWindow();
                if (window != null)
                {
                    FieldInfo showLettersField = AccessTools.Field(typeof(MainTabWindow_History), "showLetters");
                    FieldInfo showMessagesField = AccessTools.Field(typeof(MainTabWindow_History), "showMessages");

                    if (showLettersField != null)
                        showLettersField.SetValue(window, showLetters);
                    if (showMessagesField != null)
                        showMessagesField.SetValue(window, showMessages);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to set filter states: {ex.Message}");
            }
        }
    }
}
