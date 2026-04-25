using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the windowless research menu state with hierarchical tree navigation.
    /// Organizes research projects by tab (Main/Anomaly) → status (Completed/Available/Locked/In Progress).
    /// Uses TreeNavigationHelper for all navigation logic.
    /// </summary>
    public static class WindowlessResearchMenuState
    {
        private static bool isActive = false;
        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("ResearchMenu");

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => treeNav.HasActiveSearch;
        public static bool HasNoMatches => treeNav.HasNoMatches;

        static WindowlessResearchMenuState()
        {
            treeNav.FormatItemAnnouncement = FormatAnnouncement;
            treeNav.FormatSearchAnnouncement = FormatSearchAnnouncement;
            treeNav.OnActivate = HandleActivate;
            treeNav.OnInfo = HandleInfoCard;
            treeNav.TrackLastChild = true;
        }

        /// <summary>
        /// Opens the research menu and builds the category tree.
        /// </summary>
        public static void Open()
        {
            isActive = true;
            var root = BuildCategoryTree();
            treeNav.Initialize(root);
            TolkHelper.Speak("Research menu");
            treeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Closes the research menu.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            treeNav.Reset();
            TolkHelper.Speak("Research menu closed");
        }

        /// <summary>
        /// Opens the research menu and navigates to a specific project.
        /// Called when activating a research hyperlink from a letter.
        ///
        /// Strategy: build the tree, walk it to locate the project's leaf node, expand every
        /// ancestor on the path so the leaf is visible, then initialize tree-nav and put the
        /// cursor on the leaf. This is more reliable than blanket-expanding all categories
        /// (the prior implementation occasionally landed the cursor on the parent tab/category
        /// instead of the project itself, leaving the user with a collapsed-looking node).
        /// </summary>
        public static void OpenAndSelectProject(ResearchProjectDef project)
        {
            if (project == null)
            {
                TolkHelper.Speak("Research project not available");
                return;
            }

            isActive = true;
            var root = BuildCategoryTree();

            var targetNode = FindProjectNode(root, project);
            if (targetNode != null)
            {
                for (var p = targetNode.Parent; p != null; p = p.Parent)
                {
                    if (p.IsExpandable) p.IsExpanded = true;
                }
            }
            treeNav.Initialize(root);

            int foundIndex = -1;
            if (targetNode != null)
            {
                for (int i = 0; i < treeNav.VisibleItems.Count; i++)
                {
                    if (ReferenceEquals(treeNav.VisibleItems[i], targetNode))
                    {
                        foundIndex = i;
                        break;
                    }
                }
            }

            if (foundIndex >= 0)
            {
                treeNav.SetSelectedIndex(foundIndex);
                TolkHelper.Speak("Research menu");
                treeNav.ReannounceCurrentItem();
            }
            else
            {
                TolkHelper.Speak($"Research project {project.LabelCap} not found in menu");
            }
        }

        /// <summary>
        /// Recursively walks the tree to find the leaf node carrying the given research project
        /// in its Data field.
        /// </summary>
        private static InspectionTreeItem FindProjectNode(InspectionTreeItem node, ResearchProjectDef target)
        {
            if (node.Data is ResearchProjectDef proj && proj == target) return node;
            foreach (var child in node.Children)
            {
                var found = FindProjectNode(child, target);
                if (found != null) return found;
            }
            return null;
        }

        #region Navigation Wrappers (called by UnifiedKeyboardPatch)

        /// <summary>
        /// Navigates to the next item in the flat navigation list.
        /// </summary>
        public static void SelectNext()
        {
            treeNav.SelectNext();
        }

        /// <summary>
        /// Navigates to the previous item in the flat navigation list.
        /// </summary>
        public static void SelectPrevious()
        {
            treeNav.SelectPrevious();
        }

        /// <summary>
        /// Expands the currently selected category (right arrow).
        /// </summary>
        public static void ExpandCategory()
        {
            treeNav.ExpandOrDrillDown();
        }

        /// <summary>
        /// Collapses the currently selected category (left arrow).
        /// </summary>
        public static void CollapseCategory()
        {
            treeNav.CollapseOrDrillUp();
        }

        /// <summary>
        /// Expands all sibling categories at the same level as the current item.
        /// WCAG tree view pattern: * key expands all siblings.
        /// </summary>
        public static void ExpandAllSiblings()
        {
            treeNav.ExpandAllSiblings();
        }

        /// <summary>
        /// Executes the action for the currently selected item (Enter key).
        /// Opens detail view for projects.
        /// </summary>
        public static void ExecuteSelected()
        {
            if (treeNav.SelectedItem == null) return;
            HandleActivate(treeNav.SelectedItem);
        }

        /// <summary>
        /// Opens an info card for the currently selected research project.
        /// For category nodes, announces that no info card is available.
        /// </summary>
        public static void OpenInfoCard()
        {
            if (treeNav.SelectedItem == null) return;
            HandleInfoCard(treeNav.SelectedItem);
        }

        /// <summary>
        /// Jumps to the first sibling at the same level within the current node (Home key).
        /// </summary>
        public static void JumpToFirst()
        {
            treeNav.JumpToFirst(false);
        }

        /// <summary>
        /// Jumps to the last item in the current scope (End key).
        /// </summary>
        public static void JumpToLast()
        {
            treeNav.JumpToLast(false);
        }

        /// <summary>
        /// Jumps to the absolute first item in the entire tree (Ctrl+Home).
        /// </summary>
        public static void JumpToAbsoluteFirst()
        {
            treeNav.JumpToFirst(true);
        }

        /// <summary>
        /// Jumps to the absolute last item in the entire tree (Ctrl+End).
        /// </summary>
        public static void JumpToAbsoluteLast()
        {
            treeNav.JumpToLast(true);
        }

        /// <summary>
        /// Clears the current typeahead search (used by Escape key handler).
        /// </summary>
        public static void ClearTypeaheadSearch()
        {
            treeNav.Typeahead.ClearSearchAndAnnounce();
            treeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Processes a backspace key for typeahead search.
        /// </summary>
        /// <returns>True if backspace was handled.</returns>
        public static bool ProcessBackspace()
        {
            if (!treeNav.HasActiveSearch) return false;

            var labels = treeNav.VisibleItems.Select(item => item.Label).ToList();
            if (treeNav.Typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0) treeNav.SetSelectedIndex(newIndex);
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceWithSearch();
            }
            return true;
        }

        /// <summary>
        /// Processes a character input for typeahead search.
        /// </summary>
        /// <param name="c">The character typed.</param>
        /// <returns>True if the character was processed.</returns>
        public static bool ProcessTypeaheadCharacter(char c)
        {
            var labels = treeNav.VisibleItems.Select(item => item.Label).ToList();
            if (treeNav.Typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0) { treeNav.SetSelectedIndex(newIndex); AnnounceWithSearch(); }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{treeNav.Typeahead.LastFailedSearch}'");
            }
            return true;
        }

        /// <summary>
        /// Navigates to the next matching item when search is active.
        /// </summary>
        /// <returns>True if navigation occurred.</returns>
        public static bool SelectNextMatch()
        {
            if (!treeNav.HasActiveSearch) return false;

            int next = treeNav.Typeahead.GetNextMatch(treeNav.SelectedIndex);
            if (next >= 0)
            {
                treeNav.SetSelectedIndex(next);
                AnnounceWithSearch();
            }
            return true;
        }

        /// <summary>
        /// Navigates to the previous matching item when search is active.
        /// </summary>
        /// <returns>True if navigation occurred.</returns>
        public static bool SelectPreviousMatch()
        {
            if (!treeNav.HasActiveSearch) return false;

            int prev = treeNav.Typeahead.GetPreviousMatch(treeNav.SelectedIndex);
            if (prev >= 0)
            {
                treeNav.SetSelectedIndex(prev);
                AnnounceWithSearch();
            }
            return true;
        }

        /// <summary>
        /// Announces the current selection with search context.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            var item = treeNav.SelectedItem;
            if (item == null) return;

            if (treeNav.HasActiveSearch)
            {
                TolkHelper.Speak($"{item.Label}, {treeNav.Typeahead.CurrentMatchPosition} of {treeNav.Typeahead.MatchCount} matches for '{treeNav.Typeahead.SearchBuffer}'");
            }
            else
            {
                treeNav.ReannounceCurrentItem();
            }
        }

        #endregion

        #region Tree Building

        /// <summary>
        /// Builds the hierarchical category tree structure using InspectionTreeItem.
        /// Organization: Tab → Status Group → Individual Projects
        /// When only one tab exists, skips the tab wrapper and shows status groups directly.
        /// </summary>
        private static InspectionTreeItem BuildCategoryTree()
        {
            var root = new InspectionTreeItem
            {
                Label = "Research",
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            // Get research projects visible under current difficulty/playstyle settings.
            // Mirrors MainTabWindow_Research.VisibleResearchProjects so anomaly-disabled or
            // otherwise hidden projects don't leak through to keyboard navigation.
            var difficulty = Find.Storyteller?.difficulty;
            var researchManager = Find.ResearchManager;
            var allProjects = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => difficulty == null
                    || difficulty.AllowedBy(p.hideWhen)
                    || (researchManager != null && researchManager.IsCurrentProject(p)))
                .ToList();

            // Group by research tab (Main, Anomaly, etc.)
            var projectsByTab = allProjects.GroupBy(p => p.tab ?? ResearchTabDefOf.Main).ToList();
            bool singleTab = projectsByTab.Count == 1;

            foreach (var tabGroup in projectsByTab.OrderBy(g => g.Key.defName))
            {
                var tab = tabGroup.Key;
                var tabProjects = tabGroup.ToList();

                // Group projects by status within this tab
                var inProgress = GetInProgressProjects(tabProjects);
                var completed = tabProjects.Where(p => p.IsFinished).ToList();
                var available = tabProjects.Where(p => !p.IsFinished && p.CanStartNow).ToList();
                var locked = tabProjects.Where(p => !p.IsFinished && !p.CanStartNow).ToList();

                // When only one tab exists, skip the tab wrapper and add status groups directly
                if (singleTab)
                {
                    if (inProgress.Count > 0)
                        root.Children.Add(CreateStatusGroupNode("In Progress", inProgress, 0, root));
                    if (available.Count > 0)
                        root.Children.Add(CreateStatusGroupNode("Available", available, 0, root));
                    if (completed.Count > 0)
                        root.Children.Add(CreateStatusGroupNode("Completed", completed, 0, root));
                    if (locked.Count > 0)
                        root.Children.Add(CreateStatusGroupNode("Locked", locked, 0, root));
                }
                else
                {
                    // Multiple tabs - keep the tab wrapper structure
                    var tabNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Category,
                        Label = tab.LabelCap.ToString(),
                        IndentLevel = 0,
                        IsExpandable = true,
                        IsExpanded = false,
                        Parent = root
                    };

                    // Add status group nodes (only if they have projects)
                    if (inProgress.Count > 0)
                        tabNode.Children.Add(CreateStatusGroupNode("In Progress", inProgress, 1, tabNode));
                    if (available.Count > 0)
                        tabNode.Children.Add(CreateStatusGroupNode("Available", available, 1, tabNode));
                    if (completed.Count > 0)
                        tabNode.Children.Add(CreateStatusGroupNode("Completed", completed, 1, tabNode));
                    if (locked.Count > 0)
                        tabNode.Children.Add(CreateStatusGroupNode("Locked", locked, 1, tabNode));

                    root.Children.Add(tabNode);
                }
            }

            return root;
        }

        /// <summary>
        /// Gets the list of in-progress research projects.
        /// Handles both standard research and anomaly knowledge research.
        /// </summary>
        private static List<ResearchProjectDef> GetInProgressProjects(List<ResearchProjectDef> tabProjects)
        {
            var inProgress = new List<ResearchProjectDef>();

            // Check standard research
            var currentProject = Find.ResearchManager.GetProject();
            if (currentProject != null && tabProjects.Contains(currentProject))
            {
                inProgress.Add(currentProject);
            }

            // Check anomaly knowledge research (if Anomaly DLC active)
            if (ModsConfig.AnomalyActive)
            {
                var knowledgeCategories = DefDatabase<KnowledgeCategoryDef>.AllDefsListForReading;
                foreach (var category in knowledgeCategories)
                {
                    var categoryProject = Find.ResearchManager.GetProject(category);
                    if (categoryProject != null && tabProjects.Contains(categoryProject))
                    {
                        inProgress.Add(categoryProject);
                    }
                }
            }

            return inProgress;
        }

        /// <summary>
        /// Creates a status group node (Completed, Available, Locked, In Progress).
        /// </summary>
        private static InspectionTreeItem CreateStatusGroupNode(string label, List<ResearchProjectDef> projects, int level, InspectionTreeItem parent)
        {
            var statusNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = $"{label} ({projects.Count})",
                IndentLevel = level,
                IsExpandable = true,
                IsExpanded = false,
                Parent = parent
            };

            // Add individual project nodes
            foreach (var project in projects.OrderBy(p => p.LabelCap.ToString()))
            {
                var projectNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = FormatProjectLabel(project),
                    IndentLevel = level + 1,
                    IsExpandable = false,
                    IsExpanded = false,
                    Data = project,
                    LinkedDef = project,
                    Parent = statusNode
                };
                statusNode.Children.Add(projectNode);
            }

            return statusNode;
        }

        /// <summary>
        /// Formats a research project label with cost and progress information.
        /// </summary>
        private static string FormatProjectLabel(ResearchProjectDef project)
        {
            string label = project.LabelCap.ToString();

            // Anomaly knowledge tier (basic / advanced) — the visual UI shows this as a colored
            // category icon on every project tile. It's load-bearing for screen reader users
            // because anomaly projects only progress when studying an entity of the matching tier.
            if (project.knowledgeCategory != null)
            {
                label += $" - {project.knowledgeCategory.LabelCap}";
            }

            // Add cost information
            float cost = project.CostApparent;
            if (cost > 0)
            {
                label += $" - cost: {cost:F0}";
            }
            else if (project.knowledgeCost > 0)
            {
                label += $" - knowledge: {project.knowledgeCost:F0}";
            }

            // Add progress if in progress
            if (Find.ResearchManager.IsCurrentProject(project))
            {
                float progress = project.ProgressPercent * 100f;
                label += $" - {progress:F0}% complete";
            }

            // Add status indicator
            if (project.IsFinished)
            {
                label += " - Completed";
            }
            else if (project.CanStartNow)
            {
                label += " - Available";
            }
            else
            {
                label += " - Locked";
            }

            return label;
        }

        #endregion

        #region Announcement Formatters

        private static string FormatAnnouncement(InspectionTreeItem item)
        {
            // Build announcement: "{name} {state}. {X of Y}. level N"
            string announcement = item.Label;

            // Add state for expandable nodes (categories)
            if (item.Type == InspectionTreeItem.ItemType.Category && item.IsExpandable)
            {
                announcement += item.IsExpanded ? " expanded" : " collapsed";
            }

            // Add sibling position (X of Y among siblings at same level)
            var (position, total) = treeNav.GetSiblingPosition(item);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);
            announcement += string.IsNullOrEmpty(positionPart) ? "." : $". {positionPart}.";

            // Add level suffix at the end (only announced when level changes)
            announcement += MenuHelper.GetLevelSuffix("ResearchMenu", item.IndentLevel);

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
            if (item.Type == InspectionTreeItem.ItemType.Item && item.Data is ResearchProjectDef project)
            {
                // Open detail view for this project
                WindowlessResearchDetailState.Open(project);
                return true;
            }

            if (item.Type == InspectionTreeItem.ItemType.Category)
            {
                // Toggle expansion (default behavior handles this)
                return false;
            }

            return false;
        }

        private static bool HandleInfoCard(InspectionTreeItem item)
        {
            if (item.Type == InspectionTreeItem.ItemType.Item && item.Data is ResearchProjectDef project)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(project));
                return true;
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("No info card available for categories");
            return true;
        }

        #endregion
    }
}
