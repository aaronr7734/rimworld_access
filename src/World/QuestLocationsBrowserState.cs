using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Represents a quest location entry with quest and target information.
    /// </summary>
    public class QuestLocationEntry
    {
        public Quest Quest { get; set; }
        public GlobalTargetInfo Target { get; set; }
        public PlanetTile Tile { get; set; }
        public float DistanceFromOrigin { get; set; }
    }

    /// <summary>
    /// State management for the quest locations browser (Q key in world view).
    /// Shows all quest-related world locations and allows jumping to them.
    /// </summary>
    public static class QuestLocationsBrowserState
    {
        private static bool isActive = false;
        private static List<QuestLocationEntry> questLocations = new List<QuestLocationEntry>();
        private static int currentIndex = 0;
        private static PlanetTile originTile = PlanetTile.Invalid;

        /// <summary>
        /// Gets whether the quest locations browser is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the quest locations browser from the specified origin tile.
        /// </summary>
        public static void Open(PlanetTile origin)
        {
            if (Find.QuestManager == null || Find.WorldGrid == null)
            {
                TolkHelper.Speak("Quest system not available", SpeechPriority.High);
                return;
            }

            isActive = true;
            originTile = origin;
            currentIndex = 0;

            RefreshQuestLocationsList();

            if (questLocations.Count == 0)
            {
                TolkHelper.Speak("No quest locations found");
                return;
            }

            AnnounceCurrentLocation();
        }

        /// <summary>
        /// Closes the quest locations browser.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            questLocations.Clear();
            currentIndex = 0;
            originTile = PlanetTile.Invalid;
            TolkHelper.Speak("Quest locations browser closed");
        }

        /// <summary>
        /// Refreshes the quest locations list from all active quests.
        /// </summary>
        private static void RefreshQuestLocationsList()
        {
            questLocations.Clear();

            // Get all active (ongoing) quests
            List<Quest> activeQuests = Find.QuestManager.questsInDisplayOrder
                .Where(q => q.State == QuestState.Ongoing && !q.hidden && !q.hiddenInUI)
                .ToList();

            // Collect all world targets from these quests
            foreach (Quest quest in activeQuests)
            {
                // Get quest look targets (world locations associated with this quest)
                IEnumerable<GlobalTargetInfo> targets = quest.QuestLookTargets;

                foreach (GlobalTargetInfo target in targets)
                {
                    // Only include world targets (not map-specific targets)
                    if (!target.IsValid || !target.IsWorldTarget)
                        continue;

                    PlanetTile tile = PlanetTile.Invalid;

                    // Extract tile from target
                    if (target.HasWorldObject && target.WorldObject != null)
                    {
                        tile = target.WorldObject.Tile;
                    }
                    else if (target.Tile.Valid)
                    {
                        tile = target.Tile;
                    }

                    if (!tile.Valid)
                        continue;

                    // Calculate distance from origin
                    float distance = 0f;
                    if (originTile.Valid)
                    {
                        distance = Find.WorldGrid.ApproxDistanceInTiles(originTile, tile);
                    }

                    // Add to list
                    questLocations.Add(new QuestLocationEntry
                    {
                        Quest = quest,
                        Target = target,
                        Tile = tile,
                        DistanceFromOrigin = distance
                    });
                }
            }

            // Sort by distance from origin
            questLocations = questLocations.OrderBy(ql => ql.DistanceFromOrigin).ToList();

            // Validate current index
            if (currentIndex >= questLocations.Count)
                currentIndex = 0;
        }

        /// <summary>
        /// Selects the next quest location in the list.
        /// </summary>
        public static void SelectNext()
        {
            if (questLocations.Count == 0)
            {
                TolkHelper.Speak("No quest locations available");
                return;
            }

            currentIndex = MenuHelper.SelectNext(currentIndex, questLocations.Count);

            AnnounceCurrentLocation();
        }

        /// <summary>
        /// Selects the previous quest location in the list.
        /// </summary>
        public static void SelectPrevious()
        {
            if (questLocations.Count == 0)
            {
                TolkHelper.Speak("No quest locations available");
                return;
            }

            currentIndex = MenuHelper.SelectPrevious(currentIndex, questLocations.Count);

            AnnounceCurrentLocation();
        }

        /// <summary>
        /// Jumps the camera to the currently selected quest location and closes the browser.
        /// </summary>
        public static void JumpToSelected()
        {
            if (questLocations.Count == 0 || currentIndex < 0 || currentIndex >= questLocations.Count)
            {
                TolkHelper.Speak("No quest location selected");
                return;
            }

            QuestLocationEntry selected = questLocations[currentIndex];

            // Update world navigation state
            WorldNavigationState.CurrentSelectedTile = selected.Tile;

            // Sync with game's selection system
            if (Find.WorldSelector != null)
            {
                Find.WorldSelector.ClearSelection();

                // Select the world object if available
                if (selected.Target.HasWorldObject && selected.Target.WorldObject != null)
                {
                    Find.WorldSelector.Select(selected.Target.WorldObject);
                }

                Find.WorldSelector.SelectedTile = selected.Tile;
            }

            // Jump camera
            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(selected.Tile);
            }

            // Close browser first
            Close();

            // If we're choosing a destination for caravan, set it directly
            if (CaravanFormationState.IsChoosingDestination)
            {
                CaravanFormationState.SetDestination(selected.Tile);
            }
            else
            {
                // Announce the tile info
                WorldNavigationState.AnnounceTile();
            }
        }

        /// <summary>
        /// Announces the currently selected quest location.
        /// </summary>
        private static void AnnounceCurrentLocation()
        {
            if (questLocations.Count == 0)
            {
                TolkHelper.Speak("No quest locations available");
                return;
            }

            if (currentIndex < 0 || currentIndex >= questLocations.Count)
                return;

            QuestLocationEntry entry = questLocations[currentIndex];

            // Get quest name (strip XML tags)
            string questName = entry.Quest.name.StripTags();

            // Get location description
            string locationDesc = "";
            if (entry.Target.HasWorldObject && entry.Target.WorldObject != null)
            {
                locationDesc = entry.Target.WorldObject.LabelShort;
            }
            else
            {
                // Use tile biome if no world object
                Tile tile = entry.Tile.Tile;
                BiomeDef biome = tile?.PrimaryBiome;
                locationDesc = biome?.LabelCap ?? "Unknown location";
            }

            // Get tile summary for additional context
            string tileSummary = WorldInfoHelper.GetTileSummary(entry.Tile);

            string announcement = $"{questName} - {locationDesc}, {entry.DistanceFromOrigin:F1} tiles. {tileSummary}. {MenuHelper.FormatPosition(currentIndex, questLocations.Count)}";

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Handles keyboard input for the quest locations browser.
        /// Called from WorldNavigationPatch.
        /// </summary>
        public static bool HandleInput(UnityEngine.KeyCode key)
        {
            if (!isActive)
                return false;

            switch (key)
            {
                case UnityEngine.KeyCode.UpArrow:
                    SelectPrevious();
                    return true;

                case UnityEngine.KeyCode.DownArrow:
                    SelectNext();
                    return true;

                case UnityEngine.KeyCode.Return:
                case UnityEngine.KeyCode.KeypadEnter:
                    JumpToSelected();
                    return true;

                case UnityEngine.KeyCode.Escape:
                    Close();
                    // If we're in destination selection mode, cancel it and return to caravan dialog
                    if (CaravanFormationState.IsChoosingDestination)
                    {
                        CaravanFormationState.CancelDestinationSelection();
                    }
                    return true;

                default:
                    return false;
            }
        }
    }
}
