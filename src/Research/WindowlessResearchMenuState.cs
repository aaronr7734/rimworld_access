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
    /// </summary>
    public static class WindowlessResearchMenuState
    {
        private static bool isActive = false;
        private static List<ResearchMenuNode> rootNodes = new List<ResearchMenuNode>();
        private static List<ResearchMenuNode> flatNavigationList = new List<ResearchMenuNode>();
        private static int currentIndex = 0;
        private static HashSet<string> expandedNodes = new HashSet<string>();
        private static Dictionary<string, string> lastChildIdPerParent = new Dictionary<string, string>();
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the research menu and builds the category tree.
        /// </summary>
        public static void Open()
        {
            isActive = true;
            expandedNodes.Clear();
            lastChildIdPerParent.Clear();
            MenuHelper.ResetLevel("ResearchMenu");
            typeahead.ClearSearch();
            rootNodes = BuildCategoryTree();
            flatNavigationList = BuildFlatNavigationList();
            currentIndex = 0;
            TolkHelper.Speak("Research menu");
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Closes the research menu.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            rootNodes.Clear();
            flatNavigationList.Clear();
            expandedNodes.Clear();
            lastChildIdPerParent.Clear();
            MenuHelper.ResetLevel("ResearchMenu");
            typeahead.ClearSearch();
            TolkHelper.Speak("Research menu closed");
        }

        /// <summary>
        /// Navigates to the next item in the flat navigation list.
        /// </summary>
        public static void SelectNext()
        {
            if (flatNavigationList.Count == 0) return;

            currentIndex = MenuHelper.SelectNext(currentIndex, flatNavigationList.Count);
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Navigates to the previous item in the flat navigation list.
        /// </summary>
        public static void SelectPrevious()
        {
            if (flatNavigationList.Count == 0) return;

            currentIndex = MenuHelper.SelectPrevious(currentIndex, flatNavigationList.Count);

            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Expands the currently selected category (right arrow).
        /// WCAG behavior:
        /// - On closed node: Open node, focus stays
        /// - On open node: Move to first child
        /// - On end node: Reject feedback
        /// </summary>
        public static void ExpandCategory()
        {
            if (flatNavigationList.Count == 0) return;

            var current = flatNavigationList[currentIndex];

            // Case 1: Collapsed category - expand it, focus STAYS on current item
            if (current.Type == ResearchMenuNodeType.Category && !current.IsExpanded)
            {
                current.IsExpanded = true;
                expandedNodes.Add(current.Id);
                flatNavigationList = BuildFlatNavigationList();
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentSelection(); // Focus stays, just announce new state
                return;
            }

            // Case 2: Already expanded category with children - move to first child
            if (current.Type == ResearchMenuNodeType.Category && current.IsExpanded && current.Children.Count > 0)
            {
                MoveToFirstChild();
                return;
            }

            // Case 3: End node (Project) or empty category - reject
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("Cannot expand this item.", SpeechPriority.High);
        }

        /// <summary>
        /// Moves focus to the first child of the current node.
        /// Used when pressing Right on an already-expanded category.
        /// </summary>
        private static void MoveToFirstChild()
        {
            var current = flatNavigationList[currentIndex];

            // Restore last selected child position if available
            if (lastChildIdPerParent.TryGetValue(current.Id, out string savedChildId))
            {
                int savedIndex = flatNavigationList.FindIndex(n => n.Id == savedChildId);
                if (savedIndex >= 0)
                {
                    currentIndex = savedIndex;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    return;
                }
            }

            // No saved position - move to first child (which is immediately after current in flat list)
            int firstChildIndex = flatNavigationList.IndexOf(current) + 1;
            if (firstChildIndex < flatNavigationList.Count)
            {
                currentIndex = firstChildIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Expands all sibling categories at the same level as the current item.
        /// WCAG tree view pattern: * key expands all siblings.
        /// </summary>
        public static void ExpandAllSiblings()
        {
            if (flatNavigationList.Count == 0) return;

            var current = flatNavigationList[currentIndex];

            // Get siblings (nodes at the same level with the same parent)
            List<ResearchMenuNode> siblings;
            if (current.Parent == null)
            {
                // Top level - siblings are root nodes
                siblings = rootNodes;
            }
            else
            {
                // Inside a category - siblings are parent's children
                siblings = current.Parent.Children;
            }

            // Find all collapsed category siblings
            var collapsedCategories = siblings
                .Where(n => n.Type == ResearchMenuNodeType.Category && !expandedNodes.Contains(n.Id))
                .ToList();

            // Check if there are any expandable items at this level
            var allCategories = siblings.Where(n => n.Type == ResearchMenuNodeType.Category).ToList();
            if (allCategories.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No categories to expand at this level", SpeechPriority.High);
                return;
            }

            // Check if all are already expanded
            if (collapsedCategories.Count == 0)
            {
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                TolkHelper.Speak("All categories already expanded at this level", SpeechPriority.High);
                return;
            }

            // Expand all collapsed siblings
            foreach (var node in collapsedCategories)
            {
                expandedNodes.Add(node.Id);
                node.IsExpanded = true;
            }

            // Clear typeahead search
            typeahead.ClearSearch();

            // Rebuild the flat navigation list
            flatNavigationList = BuildFlatNavigationList();

            // Announce result
            string message = collapsedCategories.Count == 1
                ? "Expanded 1 category"
                : $"Expanded {collapsedCategories.Count} categories";
            SoundDefOf.Click.PlayOneShotOnCamera();
            TolkHelper.Speak(message, SpeechPriority.High);
        }

        /// <summary>
        /// Collapses the currently selected category (left arrow).
        /// WCAG behavior:
        /// - On open node: Close node, focus stays
        /// - On closed node: Move to parent
        /// - On end node: Move to parent
        /// </summary>
        public static void CollapseCategory()
        {
            if (flatNavigationList.Count == 0) return;

            var current = flatNavigationList[currentIndex];

            // Case 1: Current is an expanded category - collapse it, focus STAYS
            if (current.Type == ResearchMenuNodeType.Category && current.IsExpanded)
            {
                current.IsExpanded = false;
                expandedNodes.Remove(current.Id);
                flatNavigationList = BuildFlatNavigationList();
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
                return;
            }

            // Case 2: Find parent to navigate to (do NOT collapse parent, just move to it)
            var parent = current.Parent;

            // Skip to find an expandable parent (categories only)
            while (parent != null && parent.Type != ResearchMenuNodeType.Category)
            {
                parent = parent.Parent;
            }

            if (parent == null)
            {
                // No parent to navigate to - we're at the top level
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Already at top level.", SpeechPriority.High);
                return;
            }

            // Save current child position for this parent before moving away
            lastChildIdPerParent[parent.Id] = current.Id;

            // Move selection to the parent (do NOT collapse it)
            int parentIndex = flatNavigationList.IndexOf(parent);
            if (parentIndex >= 0)
            {
                currentIndex = parentIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Executes the action for the currently selected item (Enter key).
        /// Opens detail view for projects.
        /// </summary>
        public static void ExecuteSelected()
        {
            if (flatNavigationList.Count == 0) return;

            var current = flatNavigationList[currentIndex];

            if (current.Type == ResearchMenuNodeType.Project && current.Project != null)
            {
                // Open detail view for this project
                WindowlessResearchDetailState.Open(current.Project);
            }
            else if (current.Type == ResearchMenuNodeType.Category)
            {
                // Toggle expansion
                if (current.IsExpanded)
                    CollapseCategory();
                else
                    ExpandCategory();
            }
        }

        /// <summary>
        /// Jumps to the first item in the navigation list.
        /// </summary>
        public static void JumpToFirst()
        {
            if (flatNavigationList.Count == 0) return;
            currentIndex = 0;
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the last item in the navigation list.
        /// </summary>
        public static void JumpToLast()
        {
            if (flatNavigationList.Count == 0) return;
            currentIndex = flatNavigationList.Count - 1;
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Checks if typeahead search has an active search buffer.
        /// </summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Checks if typeahead search has no matches.
        /// </summary>
        public static bool HasNoMatches => typeahead.HasNoMatches;

        /// <summary>
        /// Clears the current typeahead search (used by Escape key handler).
        /// </summary>
        public static void ClearTypeaheadSearch()
        {
            typeahead.ClearSearchAndAnnounce();
        }

        /// <summary>
        /// Processes a backspace key for typeahead search.
        /// </summary>
        /// <returns>True if backspace was handled.</returns>
        public static bool ProcessBackspace()
        {
            if (!typeahead.HasActiveSearch) return false;

            var labels = GetVisibleItemLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0) currentIndex = newIndex;
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
            // Character validation is now done by the caller using KeyCode
            // Accept the character as-is since it was already validated
            var labels = GetVisibleItemLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0) { currentIndex = newIndex; AnnounceWithSearch(); }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
            return true;
        }

        /// <summary>
        /// Navigates to the next matching item when search is active.
        /// </summary>
        /// <returns>True if navigation occurred.</returns>
        public static bool SelectNextMatch()
        {
            if (!typeahead.HasActiveSearch) return false;

            int next = typeahead.GetNextMatch(currentIndex);
            if (next >= 0)
            {
                currentIndex = next;
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
            if (!typeahead.HasActiveSearch) return false;

            int prev = typeahead.GetPreviousMatch(currentIndex);
            if (prev >= 0)
            {
                currentIndex = prev;
                AnnounceWithSearch();
            }
            return true;
        }

        /// <summary>
        /// Gets labels from visible items for typeahead search.
        /// </summary>
        private static List<string> GetVisibleItemLabels()
        {
            var labels = new List<string>();
            foreach (var node in flatNavigationList)
            {
                labels.Add(node.Label);
            }
            return labels;
        }

        /// <summary>
        /// Announces the current selection with search context.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (flatNavigationList.Count == 0) return;

            var current = flatNavigationList[currentIndex];
            string label = current.Label;

            if (typeahead.HasActiveSearch)
            {
                TolkHelper.Speak($"{label}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
            }
            else
            {
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Builds the hierarchical category tree structure.
        /// Organization: Tab → Status Group → Individual Projects
        /// When only one tab exists, skips the tab wrapper and shows status groups directly.
        /// </summary>
        private static List<ResearchMenuNode> BuildCategoryTree()
        {
            var tree = new List<ResearchMenuNode>();

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
                    // Add status groups directly to tree root (Level 0)
                    if (inProgress.Count > 0)
                    {
                        tree.Add(CreateStatusGroupNode($"Tab_{tab.defName}_InProgress", "In Progress", inProgress, 0));
                    }

                    if (available.Count > 0)
                    {
                        tree.Add(CreateStatusGroupNode($"Tab_{tab.defName}_Available", "Available", available, 0));
                    }

                    if (completed.Count > 0)
                    {
                        tree.Add(CreateStatusGroupNode($"Tab_{tab.defName}_Completed", "Completed", completed, 0));
                    }

                    if (locked.Count > 0)
                    {
                        tree.Add(CreateStatusGroupNode($"Tab_{tab.defName}_Locked", "Locked", locked, 0));
                    }
                }
                else
                {
                    // Multiple tabs - keep the tab wrapper structure
                    var tabNode = new ResearchMenuNode
                    {
                        Id = $"Tab_{tab.defName}",
                        Type = ResearchMenuNodeType.Category,
                        Label = tab.LabelCap.ToString(),
                        Level = 0,
                        Children = new List<ResearchMenuNode>()
                    };

                    // Add status group nodes (only if they have projects)
                    if (inProgress.Count > 0)
                    {
                        var node = CreateStatusGroupNode("InProgress", "In Progress", inProgress, 1);
                        node.Parent = tabNode;
                        tabNode.Children.Add(node);
                    }

                    if (available.Count > 0)
                    {
                        var node = CreateStatusGroupNode("Available", "Available", available, 1);
                        node.Parent = tabNode;
                        tabNode.Children.Add(node);
                    }

                    if (completed.Count > 0)
                    {
                        var node = CreateStatusGroupNode("Completed", "Completed", completed, 1);
                        node.Parent = tabNode;
                        tabNode.Children.Add(node);
                    }

                    if (locked.Count > 0)
                    {
                        var node = CreateStatusGroupNode("Locked", "Locked", locked, 1);
                        node.Parent = tabNode;
                        tabNode.Children.Add(node);
                    }

                    tree.Add(tabNode);
                }
            }

            return tree;
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
        private static ResearchMenuNode CreateStatusGroupNode(string id, string label, List<ResearchProjectDef> projects, int level)
        {
            var statusNode = new ResearchMenuNode
            {
                Id = id,
                Type = ResearchMenuNodeType.Category,
                Label = $"{label} ({projects.Count})",
                Level = level,
                Children = new List<ResearchMenuNode>()
            };

            // Add individual project nodes
            foreach (var project in projects.OrderBy(p => p.LabelCap.ToString()))
            {
                var projectNode = new ResearchMenuNode
                {
                    Id = $"Project_{project.defName}",
                    Type = ResearchMenuNodeType.Project,
                    Label = FormatProjectLabel(project),
                    Level = level + 1,
                    Project = project,
                    Children = new List<ResearchMenuNode>(),
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

        /// <summary>
        /// Flattens the hierarchical tree into a navigation list based on expanded categories.
        /// </summary>
        private static List<ResearchMenuNode> BuildFlatNavigationList()
        {
            var flatList = new List<ResearchMenuNode>();

            foreach (var node in rootNodes)
            {
                AddNodeToFlatList(node, flatList);
            }

            return flatList;
        }

        /// <summary>
        /// Recursively adds nodes to the flat navigation list.
        /// Only adds children of expanded nodes.
        /// </summary>
        private static void AddNodeToFlatList(ResearchMenuNode node, List<ResearchMenuNode> flatList)
        {
            flatList.Add(node);

            // Update expansion state based on expandedNodes set
            node.IsExpanded = expandedNodes.Contains(node.Id);

            if (node.IsExpanded && node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    AddNodeToFlatList(child, flatList);
                }
            }
        }

        /// <summary>
        /// Announces the currently selected item to the clipboard for screen reader access.
        /// WCAG format: "level N. {name} {state}. {X of Y}." or "{name} {state}. {X of Y}."
        /// Level is only announced when it changes.
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            if (flatNavigationList.Count == 0)
            {
                TolkHelper.Speak("Research menu - No research projects available");
                return;
            }

            var current = flatNavigationList[currentIndex];

            // Get level prefix (only announced when level changes)
            string levelPrefix = MenuHelper.GetLevelPrefix("ResearchMenu", current.Level);

            // Build announcement: "level N. {name} {state}. {X of Y}."
            string announcement = levelPrefix + current.Label;

            // Add state for expandable nodes (categories)
            if (current.Type == ResearchMenuNodeType.Category)
            {
                announcement += current.IsExpanded ? " expanded" : " collapsed";
            }

            // Add sibling position (X of Y among siblings at same level)
            List<ResearchMenuNode> siblings;
            if (current.Parent == null)
            {
                // Top level - siblings are root nodes
                siblings = rootNodes;
            }
            else
            {
                // Inside a category - siblings are parent's children
                siblings = current.Parent.Children;
            }
            int siblingPosition = siblings.IndexOf(current) + 1;
            announcement += $". {MenuHelper.FormatPosition(siblingPosition - 1, siblings.Count)}.";

            TolkHelper.Speak(announcement);
        }
    }

    /// <summary>
    /// Represents a node in the research menu tree (either a category or a project).
    /// </summary>
    public class ResearchMenuNode
    {
        public string Id { get; set; }
        public ResearchMenuNodeType Type { get; set; }
        public string Label { get; set; }
        public int Level { get; set; }
        public bool IsExpanded { get; set; }
        public ResearchProjectDef Project { get; set; }
        public List<ResearchMenuNode> Children { get; set; }
        public ResearchMenuNode Parent { get; set; }  // Reference to parent node for upward navigation
    }

    /// <summary>
    /// Type of node in the research menu tree.
    /// </summary>
    public enum ResearchMenuNodeType
    {
        Category,
        Project
    }
}
