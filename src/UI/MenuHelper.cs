using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Centralized helper for common menu behaviors.
    /// Provides navigation, announcements, and treeview operations.
    /// </summary>
    public static class MenuHelper
    {
        // ===== LEVEL TRACKING =====
        private static Dictionary<string, int> lastAnnouncedLevels = new Dictionary<string, int>();

        /// <summary>
        /// Formats position as "X of Y" (1-indexed).
        /// Returns empty string if AnnouncePosition setting is disabled.
        /// </summary>
        public static string FormatPosition(int index, int total)
        {
            if (RimWorldAccessMod_Settings.Settings?.AnnouncePosition == false)
                return "";
            return "RimWorldAccess.Menu.Position".Translate(index + 1, total).ToString();
        }

        /// <summary>
        /// Gets level suffix if level changed. Returns " level N" or empty string.
        /// Call at END of announcement, not start.
        /// </summary>
        /// <param name="menuKey">Unique key for this menu (e.g., "StorageSettings", "ThingFilter")</param>
        /// <param name="currentLevel">0-indexed indent level</param>
        /// <param name="skipLevelOne">If true, don't announce level 1 (for menus always starting at level 1)</param>
        public static string GetLevelSuffix(string menuKey, int currentLevel, bool skipLevelOne = true)
        {
            if (RimWorldAccessMod_Settings.Settings?.AnnounceLevels == false)
                return "";

            int displayLevel = currentLevel + 1; // 1-indexed for users

            if (!lastAnnouncedLevels.TryGetValue(menuKey, out int lastLevel))
                lastLevel = -1;

            if (currentLevel == lastLevel)
                return "";

            lastAnnouncedLevels[menuKey] = currentLevel;

            // Skip level 1 only on initial announcement (lastLevel == -1)
            // If returning from a deeper level, announce level 1 so user knows they're back at root
            if (skipLevelOne && displayLevel == 1 && lastLevel == -1)
                return "";

            return "RimWorldAccess.Menu.LevelSuffix".Translate(displayLevel).ToString();
        }

        /// <summary>
        /// Resets level tracking for a menu (call on Open/Close).
        /// </summary>
        public static void ResetLevel(string menuKey)
        {
            lastAnnouncedLevels.Remove(menuKey);
        }

        // ===== EDGE ANNOUNCEMENTS =====

        /// <summary>
        /// Directions for <see cref="SpeakAlreadyAtEdge"/>. Each value maps to a single
        /// canonical announcement phrasing so menus stay consistent when the user
        /// tries to navigate past the end.
        /// </summary>
        public enum EdgeDirection
        {
            Top,
            Bottom,
            First,
            Last,
            Minimum,
            Maximum,
            TopLevel,
            Zero,
        }

        /// <summary>
        /// Announces that navigation reached an edge and can go no further.
        /// Produces canonical phrasings ("Already at top", "Already at minimum", etc.)
        /// so every menu emits the same message for the same edge.
        /// </summary>
        public static void SpeakAlreadyAtEdge(EdgeDirection direction, SpeechPriority priority = SpeechPriority.Normal)
        {
            string key;
            switch (direction)
            {
                case EdgeDirection.Top: key = "RimWorldAccess.Edge.Top"; break;
                case EdgeDirection.Bottom: key = "RimWorldAccess.Edge.Bottom"; break;
                case EdgeDirection.First: key = "RimWorldAccess.Edge.First"; break;
                case EdgeDirection.Last: key = "RimWorldAccess.Edge.Last"; break;
                case EdgeDirection.Minimum: key = "RimWorldAccess.Edge.Minimum"; break;
                case EdgeDirection.Maximum: key = "RimWorldAccess.Edge.Maximum"; break;
                case EdgeDirection.TopLevel: key = "RimWorldAccess.Edge.TopLevel"; break;
                case EdgeDirection.Zero: key = "RimWorldAccess.Edge.Zero"; break;
                default: key = "RimWorldAccess.Edge.Generic"; break;
            }
            TolkHelper.Speak(key.Loc(), priority);
        }

        // ===== PAINT REJECT =====

        /// <summary>
        /// Announces that the current column cannot be painted and plays the
        /// canonical reject sound. Used by tabular menus (Animals, Wildlife,
        /// Mechs, Assign) when a user tries to paint a non-paintable column.
        /// </summary>
        public static void SpeakCannotPaintColumn(SpeechPriority priority = SpeechPriority.Normal)
        {
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Menu.CannotPaintColumn".Loc(), priority);
        }

        // ===== NAVIGATION =====

        /// <summary>
        /// Moves to next item. Returns new index.
        /// Wraps to beginning if WrapNavigation setting is enabled.
        /// </summary>
        public static int SelectNext(int currentIndex, int count)
        {
            if (count == 0) return 0;
            if (currentIndex < count - 1)
                return currentIndex + 1;

            // At end: wrap or stay based on setting
            if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
                return 0;
            return currentIndex;
        }

        /// <summary>
        /// Moves to previous item. Returns new index.
        /// Wraps to end if WrapNavigation setting is enabled.
        /// </summary>
        public static int SelectPrevious(int currentIndex, int count)
        {
            if (count == 0) return 0;
            if (currentIndex > 0)
                return currentIndex - 1;

            // At start: wrap or stay based on setting
            if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
                return count - 1;
            return currentIndex;
        }

        /// <summary>
        /// Jumps to first item. Returns 0.
        /// </summary>
        public static int JumpToFirst()
        {
            return 0;
        }

        /// <summary>
        /// Jumps to last item. Returns last valid index.
        /// </summary>
        public static int JumpToLast(int count)
        {
            if (count == 0) return 0;
            return count - 1;
        }

        // ===== TREEVIEW OPERATIONS =====

        /// <summary>
        /// Finds parent index for a node in a flattened tree.
        /// Parent is the nearest preceding node with a lower indent level.
        /// </summary>
        /// <typeparam name="T">Node type with IndentLevel property</typeparam>
        /// <param name="nodes">Flattened node list</param>
        /// <param name="currentIndex">Index of current node</param>
        /// <param name="getIndentLevel">Function to get indent level from node</param>
        /// <returns>Parent index, or -1 if at root</returns>
        public static int FindParentIndex<T>(IList<T> nodes, int currentIndex, Func<T, int> getIndentLevel)
        {
            if (currentIndex <= 0 || currentIndex >= nodes.Count)
                return -1;

            int currentLevel = getIndentLevel(nodes[currentIndex]);
            if (currentLevel <= 0)
                return -1;

            // Search backwards for a node with lower indent level
            for (int i = currentIndex - 1; i >= 0; i--)
            {
                if (getIndentLevel(nodes[i]) < currentLevel)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Gets sibling position (1-indexed) and total count at the same level.
        /// </summary>
        public static (int position, int total) GetSiblingPosition<T>(
            IList<T> nodes, int currentIndex, Func<T, int> getIndentLevel)
        {
            if (nodes.Count == 0 || currentIndex < 0 || currentIndex >= nodes.Count)
                return (1, 1);

            int indentLevel = getIndentLevel(nodes[currentIndex]);

            // Find range of siblings
            int startIndex = 0;
            int endIndex = nodes.Count - 1;

            // Scan backwards until we hit a lower indent level
            for (int i = currentIndex - 1; i >= 0; i--)
            {
                if (getIndentLevel(nodes[i]) < indentLevel)
                {
                    startIndex = i + 1;
                    break;
                }
            }

            // Scan forwards until we hit a lower indent level
            for (int i = currentIndex + 1; i < nodes.Count; i++)
            {
                if (getIndentLevel(nodes[i]) < indentLevel)
                {
                    endIndex = i - 1;
                    break;
                }
            }

            // Count siblings at the same indent level
            int position = 0;
            int total = 0;
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (getIndentLevel(nodes[i]) == indentLevel)
                {
                    total++;
                    if (i <= currentIndex)
                        position = total;
                }
            }

            return (position, total);
        }

        /// <summary>
        /// Finds the first sibling at the same indent level within the current node.
        /// </summary>
        /// <returns>Index of first sibling, or currentIndex if already at first</returns>
        public static int JumpToFirstSibling<T>(IList<T> nodes, int currentIndex, Func<T, int> getIndentLevel)
        {
            if (nodes.Count == 0 || currentIndex < 0 || currentIndex >= nodes.Count)
                return 0;

            int indentLevel = getIndentLevel(nodes[currentIndex]);

            // Scan backwards until we hit a lower indent level or start
            for (int i = currentIndex - 1; i >= 0; i--)
            {
                if (getIndentLevel(nodes[i]) < indentLevel)
                {
                    // Found parent - first sibling is at i + 1
                    return i + 1;
                }
            }

            // No parent found - at root level, find first item at this indent
            for (int i = 0; i < nodes.Count; i++)
            {
                if (getIndentLevel(nodes[i]) == indentLevel)
                    return i;
            }

            return 0;
        }

        /// <summary>
        /// Finds the last sibling at the same indent level within the current node.
        /// </summary>
        /// <returns>Index of last sibling, or currentIndex if already at last</returns>
        public static int JumpToLastSibling<T>(IList<T> nodes, int currentIndex, Func<T, int> getIndentLevel)
        {
            if (nodes.Count == 0 || currentIndex < 0 || currentIndex >= nodes.Count)
                return nodes.Count - 1;

            int indentLevel = getIndentLevel(nodes[currentIndex]);

            // Scan forwards until we hit a lower indent level or end
            int lastSibling = currentIndex;
            for (int i = currentIndex + 1; i < nodes.Count; i++)
            {
                int level = getIndentLevel(nodes[i]);
                if (level < indentLevel)
                {
                    // Found end of siblings
                    break;
                }
                if (level == indentLevel)
                {
                    lastSibling = i;
                }
            }

            return lastSibling;
        }

        /// <summary>
        /// Handles Home key navigation for treeview states.
        /// Jumps to first sibling at current level, or absolute first with Ctrl.
        /// </summary>
        public static void HandleTreeHomeKey<T>(
            IList<T> items,
            ref int selectedIndex,
            Func<T, int> getIndentLevel,
            bool ctrlPressed,
            Action onNavigate)
            where T : class
        {
            if (items == null || items.Count == 0)
                return;

            selectedIndex = ctrlPressed ? 0 : JumpToFirstSibling(items, selectedIndex, getIndentLevel);
            onNavigate?.Invoke();
        }

        /// <summary>
        /// Handles End key navigation for treeview states.
        /// For expanded nodes with children: jumps to last visible descendant.
        /// For collapsed/leaf nodes: jumps to last sibling at current level.
        /// Ctrl+End jumps to absolute last.
        /// </summary>
        public static void HandleTreeEndKey<T>(
            IList<T> items,
            ref int selectedIndex,
            Func<T, int> getIndentLevel,
            Func<T, bool> isExpanded,
            Func<T, bool> hasChildren,
            bool ctrlPressed,
            Action onNavigate)
            where T : class
        {
            if (items == null || items.Count == 0)
                return;

            if (ctrlPressed)
            {
                selectedIndex = items.Count - 1;
            }
            else
            {
                T currentItem = items[selectedIndex];
                if (isExpanded(currentItem) && hasChildren(currentItem))
                {
                    int currentLevel = getIndentLevel(currentItem);
                    int lastDescendantIndex = selectedIndex;

                    for (int i = selectedIndex + 1; i < items.Count; i++)
                    {
                        if (getIndentLevel(items[i]) <= currentLevel)
                            break;
                        lastDescendantIndex = i;
                    }

                    selectedIndex = lastDescendantIndex;
                }
                else
                {
                    selectedIndex = JumpToLastSibling(items, selectedIndex, getIndentLevel);
                }
            }

            onNavigate?.Invoke();
        }

        // ===== FORMATTING =====

        /// <summary>
        /// Formats a temperature with the degree symbol.
        /// RimWorld's ToStringTemperature returns "21.5C" but we want "21.5°C" for clarity.
        /// </summary>
        /// <param name="celsiusTemp">Temperature in Celsius</param>
        /// <param name="format">Format string (e.g., "F0" for no decimals, "F1" for one decimal)</param>
        /// <returns>Formatted temperature like "21.5°C", "70.2°F", or "294.6°K"</returns>
        /// <summary>
        /// Formats a list of strings with natural English joining.
        /// Examples: "A", "A and B", "A, B, and C".
        /// Used by bulk painting announcements across all tabular menus.
        /// </summary>
        public static string FormatNameList(List<string> items)
        {
            if (items.Count == 0) return "";
            if (items.Count == 1) return items[0];
            if (items.Count == 2) return $"{items[0]} and {items[1]}";
            return string.Join(", ", items.Take(items.Count - 1)) + ", and " + items.Last();
        }

        public static string FormatTemperature(float celsiusTemp, string format = "F1")
        {
            string temp = celsiusTemp.ToStringTemperature(format);
            // Insert degree symbol before the unit letter (C, F, or K)
            if (temp.Length > 1)
            {
                return temp.Insert(temp.Length - 1, "°");
            }
            return temp;
        }
    }
}
