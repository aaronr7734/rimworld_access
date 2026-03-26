using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Sound;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a treeview-style architect menu with expandable categories.
    /// Level 1: Categories (Orders, Structure, etc.) - expandable
    /// Level 2: Designators (Wall, Door, etc.) - activates tool
    /// Uses TreeNavigationHelper for all navigation logic.
    /// </summary>
    public static class ArchitectTreeState
    {
        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("Architect");
        private static bool isActive = false;

        // Callback for when a designator is activated
        private static Action<Designator> onDesignatorActivated;

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => treeNav.HasActiveSearch;
        public static bool HasNoMatches => treeNav.HasNoMatches;

        static ArchitectTreeState()
        {
            treeNav.FormatItemAnnouncement = FormatAnnouncement;
            treeNav.FormatSearchAnnouncement = FormatSearchAnnouncement;
            treeNav.OnActivate = HandleActivate;
        }

        /// <summary>
        /// Opens the architect tree menu.
        /// </summary>
        /// <param name="onActivated">Callback when a designator is selected for activation.</param>
        public static void Open(Action<Designator> onActivated)
        {
            onDesignatorActivated = onActivated;
            isActive = true;

            var root = BuildTree();

            if (root.Children.Count == 0)
            {
                TolkHelper.Speak("No architect categories available");
                Close();
                return;
            }

            treeNav.Initialize(root);

            TolkHelper.Speak("Architect menu");
            treeNav.ReannounceCurrentItem();

            Log.Message($"Opened architect tree menu with {treeNav.Count} items");
        }

        /// <summary>
        /// Closes the architect tree menu.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            onDesignatorActivated = null;
            treeNav.Reset();
        }

        /// <summary>
        /// Handles keyboard input for the architect tree.
        /// Returns true if input was consumed; false for unhandled events.
        /// </summary>
        public static bool HandleInput(Event ev)
        {
            if (!isActive)
                return false;

            return treeNav.HandleInput(ev);
        }

        /// <summary>
        /// Gets the currently selected designator, or null if a category is selected.
        /// </summary>
        public static Designator GetSelectedDesignator()
        {
            var item = treeNav.SelectedItem;
            if (item != null && item.Data is Designator designator)
                return designator;
            return null;
        }

        #region Tree Building

        /// <summary>
        /// Builds the InspectionTreeItem tree from categories and their designators.
        /// </summary>
        private static InspectionTreeItem BuildTree()
        {
            var root = new InspectionTreeItem
            {
                Label = "Architect",
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            List<DesignationCategoryDef> categories = ArchitectHelper.GetAllCategories();

            foreach (DesignationCategoryDef category in categories)
            {
                var catItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = category.LabelCap,
                    Data = category,
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = root
                };

                // Pre-populate children (designators are lightweight)
                List<Designator> designators = ArchitectHelper.GetDesignatorsForCategory(category);
                foreach (Designator designator in designators)
                {
                    string label = GetDesignatorLabel(designator);
                    var desItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = label,
                        Data = designator,
                        IndentLevel = 1,
                        IsExpandable = false,
                        IsExpanded = false,
                        Parent = catItem
                    };
                    catItem.Children.Add(desItem);
                }

                root.Children.Add(catItem);
            }

            return root;
        }

        /// <summary>
        /// Gets a label for a designator including cost and description.
        /// Format: "Name: cost. description" for build designators.
        /// Format: "Name. description" for order designators.
        /// If the designator has right-click options, adds "(right bracket for more options)" hint after the name.
        /// </summary>
        private static string GetDesignatorLabel(Designator designator)
        {
            string label = designator.LabelCap;

            // Check if designator has right-click options and add hint
            bool hasRightClickOptions = designator.RightClickFloatMenuOptions.Any();
            string rightClickHint = hasRightClickOptions ? " (right bracket for more options)" : "";

            // Add cost, skill, and description for build designators
            if (designator is Designator_Build buildDesignator)
            {
                BuildableDef buildable = buildDesignator.PlacingDef;
                if (buildable != null)
                {
                    string costInfo = ArchitectHelper.GetBriefCostInfo(buildable);
                    string skillInfo = ArchitectHelper.GetSkillRequirement(buildable);
                    string description = ArchitectHelper.GetDescription(buildable);

                    // Build combined info (cost, skill)
                    var infoParts = new List<string>();
                    if (!string.IsNullOrEmpty(costInfo))
                        infoParts.Add(costInfo);
                    if (!string.IsNullOrEmpty(skillInfo))
                        infoParts.Add(skillInfo);
                    string combinedInfo = string.Join(", ", infoParts);

                    // Format: "Name (right bracket hint): cost, skill. description"
                    label += rightClickHint;
                    if (!string.IsNullOrEmpty(combinedInfo) && !string.IsNullOrEmpty(description))
                    {
                        label += $": {combinedInfo}. {description}";
                    }
                    else if (!string.IsNullOrEmpty(combinedInfo))
                    {
                        label += $": {combinedInfo}";
                    }
                    else if (!string.IsNullOrEmpty(description))
                    {
                        label += $". {description}";
                    }
                }
                else
                {
                    label += rightClickHint;
                }
            }
            else
            {
                // For non-build designators (orders), add hint and description if available
                label += rightClickHint;
                string description = ArchitectHelper.GetDesignatorDescriptionText(designator);
                if (!string.IsNullOrEmpty(description))
                {
                    label += $". {description}";
                }
            }

            return label;
        }

        #endregion

        #region Announcement Formatters

        private static string FormatAnnouncement(InspectionTreeItem item)
        {
            var (position, total) = treeNav.GetSiblingPosition(item);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);

            string announcement;

            if (item.Type == InspectionTreeItem.ItemType.Category)
            {
                // Categories: "Label, expanded/collapsed. position."
                string expandState = item.IsExpanded ? "expanded" : "collapsed";
                string positionSection = string.IsNullOrEmpty(positionPart) ? "." : $". {positionPart}.";
                announcement = $"{item.Label}, {expandState}{positionSection}";
            }
            else
            {
                // Designator: "Label. position." or "Label position." if label ends with punctuation
                string labelText = item.Label;
                bool labelEndsWithPunctuation = !string.IsNullOrEmpty(labelText) &&
                    (labelText.EndsWith(".") || labelText.EndsWith("!") || labelText.EndsWith("?"));

                string positionSection;
                if (string.IsNullOrEmpty(positionPart))
                {
                    positionSection = labelEndsWithPunctuation ? "" : ".";
                }
                else
                {
                    positionSection = labelEndsWithPunctuation ? $" {positionPart}." : $". {positionPart}.";
                }

                announcement = $"{item.Label}{positionSection}";
            }

            // Add level suffix at the end (only announced when level changes)
            announcement += MenuHelper.GetLevelSuffix("Architect", item.IndentLevel);

            return announcement;
        }

        private static string FormatSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead)
        {
            if (typeahead.HasActiveSearch)
            {
                return $"{item.Label}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";
            }
            return FormatAnnouncement(item);
        }

        #endregion

        #region Custom Actions

        private static bool HandleActivate(InspectionTreeItem item)
        {
            if (item.Type == InspectionTreeItem.ItemType.Category)
            {
                // Toggle expansion (default behavior handles this)
                return false;
            }

            if (item.Data is Designator designator && onDesignatorActivated != null)
            {
                isActive = false;
                treeNav.Reset();
                var callback = onDesignatorActivated;
                onDesignatorActivated = null;
                callback(designator);
                return true;
            }

            return false;
        }

        #endregion
    }
}
