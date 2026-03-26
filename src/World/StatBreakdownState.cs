using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for viewing stat breakdown tree views.
    /// Parses game explanation strings into navigable tree structures.
    /// Used when pressing Alt+I on caravan stats in summary view.
    /// Uses TreeNavigationHelper for all standard treeview keyboard handling.
    /// </summary>
    public static class StatBreakdownState
    {
        private static bool isActive = false;
        private static string statName = "";
        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("Breakdown");

        /// <summary>
        /// Gets whether the stat breakdown viewer is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        static StatBreakdownState()
        {
            treeNav.FormatItemAnnouncement = FormatItemAnnouncement;
            treeNav.AnnounceChildCounts = false;
        }

        /// <summary>
        /// Opens the stat breakdown viewer with the given explanation text.
        /// </summary>
        /// <param name="name">The name of the stat (e.g., "Visibility", "Speed")</param>
        /// <param name="explanation">The explanation text from the game</param>
        public static void Open(string name, string explanation)
        {
            if (string.IsNullOrEmpty(explanation))
            {
                TolkHelper.Speak($"No breakdown available for {name}");
                return;
            }

            isActive = true;
            statName = name;

            var root = ParseExplanation(explanation);

            if (root.Children.Count == 0)
            {
                TolkHelper.Speak($"No breakdown factors for {name}");
                Close();
                return;
            }

            treeNav.Initialize(root);

            // Only say "treeview" if there are expandable nodes
            bool hasExpandableNodes = root.Children.Exists(item => item.IsExpandable);
            string suffix = hasExpandableNodes ? " treeview" : "";
            TolkHelper.Speak($"{name} breakdown ({root.Children.Count} factors){suffix}.");
            treeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Closes the stat breakdown viewer.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            statName = "";
            treeNav.Reset();
            TolkHelper.Speak("Breakdown closed");
        }

        /// <summary>
        /// Parses the explanation string into a tree of InspectionTreeItems.
        /// Detects indentation to create hierarchy.
        /// Result lines (starting with "= ") are attached to their parent section.
        /// </summary>
        private static InspectionTreeItem ParseExplanation(string explanation)
        {
            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            if (string.IsNullOrEmpty(explanation))
                return root;

            string[] lines = explanation.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            // First pass: build the tree structure using temporary data
            InspectionTreeItem currentParent = null;
            int lastIndentLevel = 0;
            List<InspectionTreeItem> allItems = new List<InspectionTreeItem>();

            foreach (string rawLine in lines)
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                // Count leading spaces/tabs
                int leadingSpaces = 0;
                foreach (char c in rawLine)
                {
                    if (c == ' ') leadingSpaces++;
                    else if (c == '\t') leadingSpaces += 4;
                    else break;
                }

                // Determine indent level (roughly 2 spaces = 1 level)
                int indentLevel = leadingSpaces / 2;

                // Trim the line
                string line = rawLine.Trim();

                // Skip empty lines after trim
                if (string.IsNullOrEmpty(line))
                    continue;

                // Check if this is a result line (starts with "= ")
                bool isResultLine = line.StartsWith("= ");
                if (isResultLine)
                {
                    // This is the result value for the current parent section
                    string resultValue = line.Substring(2).Trim();

                    // Find the appropriate parent to attach this result to
                    InspectionTreeItem targetParent = currentParent;
                    while (targetParent != null && targetParent.IndentLevel >= indentLevel)
                    {
                        targetParent = targetParent.Parent;
                    }

                    if (targetParent != null && targetParent != root)
                    {
                        targetParent.Tooltip = resultValue;
                    }
                    else if (root.Children.Count > 0)
                    {
                        // Attach to the last top-level item
                        root.Children[root.Children.Count - 1].Tooltip = resultValue;
                    }
                    continue; // Don't add result lines as separate items
                }

                // Handle dash prefix for sub-items
                if (line.StartsWith("- "))
                {
                    line = line.Substring(2);
                    indentLevel = Math.Max(1, indentLevel);
                }

                // Remove trailing colon for cleaner display (section headers often end with ":")
                bool isSection = line.EndsWith(":");
                if (isSection)
                {
                    line = line.Substring(0, line.Length - 1);
                }

                var item = new InspectionTreeItem
                {
                    Label = line,
                    IndentLevel = indentLevel,
                    IsExpandable = false,
                    IsExpanded = false // Default to collapsed
                };

                allItems.Add(item);

                // Build hierarchy based on indentation
                if (indentLevel == 0)
                {
                    // Top-level item
                    item.Parent = root;
                    root.Children.Add(item);
                    currentParent = item;
                }
                else if (currentParent == null)
                {
                    // First item starts at non-zero indent (e.g., "  - Abby: +35 kg")
                    // Treat as root item since there's no parent to attach to
                    item.Parent = root;
                    root.Children.Add(item);
                    currentParent = item;
                }
                else if (indentLevel > lastIndentLevel)
                {
                    // Child of current parent
                    item.Parent = currentParent;
                    currentParent.Children.Add(item);
                    currentParent.IsExpandable = true;
                    currentParent = item;
                }
                else if (indentLevel <= lastIndentLevel)
                {
                    // Find appropriate parent
                    InspectionTreeItem parent = currentParent;
                    while (parent != null && parent != root && parent.IndentLevel >= indentLevel)
                    {
                        parent = parent.Parent;
                    }

                    if (parent != null && parent != root)
                    {
                        item.Parent = parent;
                        parent.Children.Add(item);
                        parent.IsExpandable = true;
                    }
                    else
                    {
                        // Top-level item
                        item.Parent = root;
                        root.Children.Add(item);
                    }
                    currentParent = item;
                }

                lastIndentLevel = indentLevel;
            }

            // Second pass: inline single children into their parents
            InlineSingleChildren(root.Children);

            // Third pass: extract result values from children that look like results
            foreach (var item in allItems)
            {
                if (item.IsExpandable && string.IsNullOrEmpty(item.Tooltip) && item.Children.Count > 0)
                {
                    // Look for the last child that contains a result-like pattern
                    for (int i = item.Children.Count - 1; i >= 0; i--)
                    {
                        var child = item.Children[i];
                        string childText = child.Label;

                        // Check for patterns like "= X.X tiles/day" in the text
                        var equalsMatch = Regex.Match(childText, @"=\s*([\d.]+\s*\S+.*?)$");
                        if (equalsMatch.Success)
                        {
                            item.Tooltip = equalsMatch.Groups[1].Value.Trim();
                            break;
                        }

                        // Check for "Final" or "Total" or result-like lines with colon
                        if (childText.StartsWith("Final", StringComparison.OrdinalIgnoreCase) ||
                            childText.StartsWith("Total", StringComparison.OrdinalIgnoreCase) ||
                            childText.Contains(" speed:") ||
                            childText.Contains(" Speed:"))
                        {
                            var colonMatch = Regex.Match(childText, @":\s*(.+)$");
                            if (colonMatch.Success)
                            {
                                item.Tooltip = colonMatch.Groups[1].Value.Trim();
                                break;
                            }
                        }
                    }
                }
            }

            return root;
        }

        /// <summary>
        /// Recursively inlines single children into their parent nodes.
        /// When a node has exactly one child, the child's text is appended to the parent
        /// and any grandchildren become the parent's children.
        /// </summary>
        private static void InlineSingleChildren(List<InspectionTreeItem> items)
        {
            foreach (var item in items)
            {
                // Keep inlining as long as there's exactly one child
                while (item.Children.Count == 1)
                {
                    var singleChild = item.Children[0];

                    // Append the child's text to the parent
                    item.Label += ", " + singleChild.Label;

                    // Merge result value if the child has one and parent doesn't
                    if (!string.IsNullOrEmpty(singleChild.Tooltip) && string.IsNullOrEmpty(item.Tooltip))
                    {
                        item.Tooltip = singleChild.Tooltip;
                    }

                    // Promote grandchildren to become children
                    item.Children = singleChild.Children;
                    foreach (var grandchild in item.Children)
                    {
                        grandchild.Parent = item;
                    }

                    // Update expandability based on new children count
                    item.IsExpandable = item.Children.Count > 0;
                }

                // Recursively process remaining children
                if (item.Children.Count > 0)
                {
                    InlineSingleChildren(item.Children);
                }
            }
        }

        #region Announcement Formatters

        /// <summary>
        /// Custom item announcement formatter.
        /// </summary>
        private static string FormatItemAnnouncement(InspectionTreeItem item)
        {
            string announcement = item.Label;

            // Add result value for expandable items (sections with a calculated result)
            // Tooltip is used to store ResultValue
            if (!string.IsNullOrEmpty(item.Tooltip))
            {
                announcement += $" ({item.Tooltip})";
            }

            // Add expand/collapse indicator for items with children
            if (item.IsExpandable)
            {
                announcement += item.IsExpanded ? ", expanded" : ", collapsed";
            }

            // Add sibling position
            var (position, total) = treeNav.GetSiblingPosition(item);
            string positionStr = MenuHelper.FormatPosition(position - 1, total);
            if (!string.IsNullOrEmpty(positionStr))
            {
                announcement += $". {positionStr}";
            }

            // Add level suffix if level changed
            string levelSuffix = MenuHelper.GetLevelSuffix("Breakdown", item.IndentLevel);
            if (!string.IsNullOrEmpty(levelSuffix))
            {
                announcement += levelSuffix;
            }

            return announcement;
        }

        #endregion

        /// <summary>
        /// Selects the next item.
        /// </summary>
        public static void SelectNext()
        {
            treeNav.SelectNext();
        }

        /// <summary>
        /// Selects the previous item.
        /// </summary>
        public static void SelectPrevious()
        {
            treeNav.SelectPrevious();
        }

        /// <summary>
        /// Expands the current item if it's expandable.
        /// </summary>
        public static void Expand()
        {
            if (treeNav.SelectedItem == null)
                return;

            var item = treeNav.SelectedItem;
            if (!item.IsExpandable)
            {
                TolkHelper.Speak("Not expandable");
                return;
            }

            if (item.IsExpanded)
            {
                // Already expanded - drill down to first child
                if (item.Children.Count > 0)
                {
                    treeNav.ExpandOrDrillDown();
                }
                else
                {
                    TolkHelper.Speak("Already expanded");
                }
                return;
            }

            treeNav.ExpandOrDrillDown();
        }

        /// <summary>
        /// Collapses the current item or goes to parent.
        /// </summary>
        public static void CollapseOrGoToParent()
        {
            if (treeNav.SelectedItem == null)
                return;

            var item = treeNav.SelectedItem;

            if (item.IsExpanded && item.IsExpandable)
            {
                treeNav.CollapseOrDrillUp();
            }
            else if (item.Parent != null && item.Parent != treeNav.RootItem)
            {
                treeNav.CollapseOrDrillUp();
            }
            else
            {
                TolkHelper.Speak("Already at top level");
            }
        }

        /// <summary>
        /// Handles keyboard input for the stat breakdown viewer.
        /// Returns true if the input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key)
        {
            if (!isActive)
                return false;

            // Tab always closes (not a standard tree key)
            if (key == KeyCode.Tab)
            {
                Close();
                return true;
            }

            // Delegate to TreeNavigationHelper for all standard tree keys
            if (treeNav.HandleInput(Event.current))
                return true;

            // HandleInput returned false = Escape with no active search
            if (key == KeyCode.Escape)
            {
                Close();
                return true;
            }

            // Consume all other keys to prevent pass-through
            return true;
        }
    }
}
