using System;
using System.Collections.Generic;

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
        /// </summary>
        public static string FormatPosition(int index, int total)
        {
            return $"{index + 1} of {total}";
        }

        /// <summary>
        /// Gets level prefix if level changed. Returns "level N. " or empty string.
        /// Call at START of announcement, not end.
        /// </summary>
        /// <param name="menuKey">Unique key for this menu (e.g., "StorageSettings", "ThingFilter")</param>
        /// <param name="currentLevel">0-indexed indent level</param>
        /// <param name="skipLevelOne">If true, don't announce level 1 (for menus always starting at level 1)</param>
        public static string GetLevelPrefix(string menuKey, int currentLevel, bool skipLevelOne = true)
        {
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

            return $"level {displayLevel}. ";
        }

        /// <summary>
        /// Resets level tracking for a menu (call on Open/Close).
        /// </summary>
        public static void ResetLevel(string menuKey)
        {
            lastAnnouncedLevels.Remove(menuKey);
        }

        // ===== NAVIGATION (NO WRAPPING) =====

        /// <summary>
        /// Moves to next item. Returns new index. Does NOT wrap.
        /// </summary>
        public static int SelectNext(int currentIndex, int count)
        {
            if (count == 0) return 0;
            if (currentIndex < count - 1)
                return currentIndex + 1;
            return currentIndex; // Stay at end
        }

        /// <summary>
        /// Moves to previous item. Returns new index. Does NOT wrap.
        /// </summary>
        public static int SelectPrevious(int currentIndex, int count)
        {
            if (count == 0) return 0;
            if (currentIndex > 0)
                return currentIndex - 1;
            return currentIndex; // Stay at start
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
    }
}
