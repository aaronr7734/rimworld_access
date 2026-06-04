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
            TolkHelper.Speak("RimWorldAccess.Research.Menu.Title".Loc());
            treeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Closes the research menu.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            treeNav.Reset();
            TolkHelper.Speak("RimWorldAccess.Research.Menu.Closed".Loc());
        }

        /// <summary>
        /// Opens the research menu and navigates to a specific project.
        /// Called when activating a research hyperlink from a letter.
        /// </summary>
        public static void OpenAndSelectProject(ResearchProjectDef project)
        {
            if (project == null)
            {
                TolkHelper.Speak("RimWorldAccess.Research.Menu.ProjectNotAvailable".Loc());
                return;
            }

            // Open the menu normally first
            isActive = true;
            var root = BuildCategoryTree();

            // Expand all categories to find the project
            ExpandAllCategoriesRecursive(root.Children);
            treeNav.Initialize(root);

            // Find the project in the visible items list
            int foundIndex = -1;
            for (int i = 0; i < treeNav.VisibleItems.Count; i++)
            {
                if (treeNav.VisibleItems[i].Data is ResearchProjectDef proj && proj == project)
                {
                    foundIndex = i;
                    break;
                }
            }

            if (foundIndex >= 0)
            {
                treeNav.SetSelectedIndex(foundIndex);
                TolkHelper.Speak("RimWorldAccess.Research.Menu.Title".Loc());
                treeNav.ReannounceCurrentItem();
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Research.Menu.ProjectNotFound".Loc(project.LabelCap));
            }
        }

        /// <summary>
        /// Recursively expands all category nodes.
        /// </summary>
        private static void ExpandAllCategoriesRecursive(List<InspectionTreeItem> items)
        {
            foreach (var item in items)
            {
                if (item.Type == InspectionTreeItem.ItemType.Category && item.IsExpandable)
                {
                    item.IsExpanded = true;
                    if (item.Children.Count > 0)
                    {
                        ExpandAllCategoriesRecursive(item.Children);
                    }
                }
            }
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
                treeNav.Typeahead.SpeakNoMatches();
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
                TolkHelper.Speak(treeNav.Typeahead.BuildItemAnnouncement(item.Label));
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
                Label = "RimWorldAccess.Research.Tree.RootLabel".Translate(),
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            // Get all research projects
            var allProjects = DefDatabase<ResearchProjectDef>.AllDefsListForReading;

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
                        root.Children.Add(CreateStatusGroupNode(StatusLabel.InProgress, inProgress, 0, root));
                    if (available.Count > 0)
                        root.Children.Add(CreateStatusGroupNode(StatusLabel.Available, available, 0, root));
                    if (completed.Count > 0)
                        root.Children.Add(CreateStatusGroupNode(StatusLabel.Completed, completed, 0, root));
                    if (locked.Count > 0)
                        root.Children.Add(CreateStatusGroupNode(StatusLabel.Locked, locked, 0, root));
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
                        tabNode.Children.Add(CreateStatusGroupNode(StatusLabel.InProgress, inProgress, 1, tabNode));
                    if (available.Count > 0)
                        tabNode.Children.Add(CreateStatusGroupNode(StatusLabel.Available, available, 1, tabNode));
                    if (completed.Count > 0)
                        tabNode.Children.Add(CreateStatusGroupNode(StatusLabel.Completed, completed, 1, tabNode));
                    if (locked.Count > 0)
                        tabNode.Children.Add(CreateStatusGroupNode(StatusLabel.Locked, locked, 1, tabNode));

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
        /// Identifies which status group label to use when composing the
        /// "InProgress (N)"-style localized category header.
        /// </summary>
        private enum StatusLabel { InProgress, Available, Completed, Locked }

        private static string FormatStatusGroupLabel(StatusLabel status, int count)
        {
            switch (status)
            {
                case StatusLabel.InProgress: return "RimWorldAccess.Research.Status.InProgress".Translate(count);
                case StatusLabel.Available:  return "RimWorldAccess.Research.Status.Available".Translate(count);
                case StatusLabel.Completed:  return "RimWorldAccess.Research.Status.Completed".Translate(count);
                case StatusLabel.Locked:     return "RimWorldAccess.Research.Status.Locked".Translate(count);
                default: return count.ToString();
            }
        }

        /// <summary>
        /// Creates a status group node (Completed, Available, Locked, In Progress).
        /// </summary>
        private static InspectionTreeItem CreateStatusGroupNode(StatusLabel status, List<ResearchProjectDef> projects, int level, InspectionTreeItem parent)
        {
            var statusNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = FormatStatusGroupLabel(status, projects.Count),
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

            // Add cost information
            float cost = project.CostApparent;
            if (cost > 0)
            {
                label += "RimWorldAccess.Research.Label.CostSuffix".Translate(cost.ToString("F0"));
            }
            else if (project.knowledgeCost > 0)
            {
                label += "RimWorldAccess.Research.Label.KnowledgeSuffix".Translate(project.knowledgeCost.ToString("F0"));
            }

            // Add progress if in progress
            if (Find.ResearchManager.IsCurrentProject(project))
            {
                float progress = project.ProgressPercent * 100f;
                label += "RimWorldAccess.Research.Label.ProgressSuffix".Translate(progress.ToString("F0"));
            }

            // Add status indicator
            string statusWord;
            if (project.IsFinished)
            {
                statusWord = "RimWorldAccess.Research.Status.CompletedWord".Translate();
            }
            else if (project.CanStartNow)
            {
                statusWord = "RimWorldAccess.Research.Status.AvailableWord".Translate();
            }
            else
            {
                statusWord = "RimWorldAccess.Research.Status.LockedWord".Translate();
            }
            label += "RimWorldAccess.Research.Label.StatusSuffix".Translate(statusWord);

            return label;
        }

        #endregion

        #region Announcement Formatters

        private static string FormatAnnouncement(InspectionTreeItem item)
        {
            // Build announcement: "{name} {state}. {X of Y}. level N"
            string announcement = item.Label;

            // Add state for expandable nodes (categories)
            if (item.Type == InspectionTreeItem.ItemType.Category)
            {
                announcement += TreeNavigationHelper.FormatExpansionSpaceSuffix(item);
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
                return typeahead.BuildItemAnnouncement(item.Label);
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

            InfoCardState.SpeakNoInfoCardAvailable();
            return true;
        }

        #endregion
    }
}
