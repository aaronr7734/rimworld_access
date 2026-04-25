using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the detail view for a specific research project.
    /// Uses a tree structure with expandable categories for prerequisites, unlocks, and dependents.
    /// Uses TreeNavigationHelper for all navigation logic.
    /// </summary>
    public static class WindowlessResearchDetailState
    {
        private static bool isActive = false;
        private static ResearchProjectDef currentProject = null;
        private static Stack<ResearchProjectDef> navigationStack = new Stack<ResearchProjectDef>();
        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("ResearchDetail");

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => treeNav.HasActiveSearch;
        public static bool HasNoMatches => treeNav.HasNoMatches;

        static WindowlessResearchDetailState()
        {
            treeNav.FormatItemAnnouncement = FormatAnnouncement;
            treeNav.FormatSearchAnnouncement = FormatSearchAnnouncement;
            treeNav.OnActivate = HandleActivate;
            treeNav.OnInfo = HandleInfoCard;
            treeNav.TrackLastChild = true;
        }

        /// <summary>
        /// Opens the detail view for a specific research project.
        /// </summary>
        public static void Open(ResearchProjectDef project)
        {
            currentProject = project;
            isActive = true;

            var root = BuildDetailTree(project);
            treeNav.Initialize(root);

            treeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Opens the detail view for a project, pushing the current one onto the navigation stack.
        /// </summary>
        public static void OpenWithBackNavigation(ResearchProjectDef project)
        {
            if (currentProject != null)
            {
                navigationStack.Push(currentProject);
            }
            Open(project);
        }

        /// <summary>
        /// Closes the detail view. If there's a project in the stack, goes back to it.
        /// </summary>
        public static void Close()
        {
            if (navigationStack.Count > 0)
            {
                var previousProject = navigationStack.Pop();
                Open(previousProject);
                TolkHelper.Speak($"Back to {previousProject.LabelCap}");
            }
            else
            {
                isActive = false;
                currentProject = null;
                treeNav.Reset();
                navigationStack.Clear();
                TolkHelper.Speak("Returned to research menu");
            }
        }

        #region Navigation Wrappers (called by UnifiedKeyboardPatch)

        /// <summary>
        /// Navigates to the next item.
        /// </summary>
        public static void SelectNext()
        {
            treeNav.SelectNext();
        }

        /// <summary>
        /// Navigates to the previous item.
        /// </summary>
        public static void SelectPrevious()
        {
            treeNav.SelectPrevious();
        }

        /// <summary>
        /// Expands the current category (Right arrow).
        /// </summary>
        public static void Expand()
        {
            treeNav.ExpandOrDrillDown();
        }

        /// <summary>
        /// Collapses the current category or navigates to parent (Left arrow).
        /// </summary>
        public static void Collapse()
        {
            treeNav.CollapseOrDrillUp();
        }

        /// <summary>
        /// Expands all sibling categories at the same level as the current item.
        /// </summary>
        public static void ExpandAllSiblings()
        {
            treeNav.ExpandAllSiblings();
        }

        /// <summary>
        /// Executes the action for the current item (Enter key).
        /// </summary>
        public static void ExecuteCurrentItem()
        {
            if (treeNav.SelectedItem == null || currentProject == null) return;

            var item = treeNav.SelectedItem;

            // Try custom activate first (OnActivate handles all types)
            if (HandleActivate(item))
                return;

            // Default: toggle expand/collapse for categories
            if (item.IsExpandable)
            {
                if (item.IsExpanded)
                    treeNav.CollapseOrDrillUp();
                else
                    treeNav.ExpandOrDrillDown();
            }
        }

        /// <summary>
        /// Opens an info card for the currently selected item.
        /// </summary>
        public static void OpenInfoCard()
        {
            if (treeNav.SelectedItem == null || currentProject == null) return;
            HandleInfoCard(treeNav.SelectedItem);
        }

        /// <summary>
        /// Jumps to the first sibling at the same level (Home key).
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
        /// Jumps to the absolute first item (Ctrl+Home).
        /// </summary>
        public static void JumpToAbsoluteFirst()
        {
            treeNav.JumpToFirst(true);
        }

        /// <summary>
        /// Jumps to the absolute last item (Ctrl+End).
        /// </summary>
        public static void JumpToAbsoluteLast()
        {
            treeNav.JumpToLast(true);
        }

        /// <summary>
        /// Clears the typeahead search and announces "Search cleared".
        /// </summary>
        public static void ClearTypeaheadSearch()
        {
            treeNav.Typeahead.ClearSearchAndAnnounce();
            treeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Processes backspace for typeahead search.
        /// </summary>
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
        public static bool ProcessTypeaheadCharacter(char c)
        {
            var labels = treeNav.VisibleItems.Select(item => item.Label).ToList();
            if (treeNav.Typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    treeNav.SetSelectedIndex(newIndex);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceWithSearch();
                }
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak($"No matches for '{treeNav.Typeahead.LastFailedSearch}'");
            }
            return true;
        }

        /// <summary>
        /// Navigates to the next match in the typeahead search results.
        /// </summary>
        public static bool SelectNextMatch()
        {
            if (!treeNav.HasActiveSearch) return false;

            int next = treeNav.Typeahead.GetNextMatch(treeNav.SelectedIndex);
            if (next >= 0)
            {
                treeNav.SetSelectedIndex(next);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceWithSearch();
            }
            return true;
        }

        /// <summary>
        /// Navigates to the previous match in the typeahead search results.
        /// </summary>
        public static bool SelectPreviousMatch()
        {
            if (!treeNav.HasActiveSearch) return false;

            int prev = treeNav.Typeahead.GetPreviousMatch(treeNav.SelectedIndex);
            if (prev >= 0)
            {
                treeNav.SetSelectedIndex(prev);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceWithSearch();
            }
            return true;
        }

        /// <summary>
        /// Announces the current selection with typeahead search info.
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
        /// Builds the tree structure for the detail view.
        /// </summary>
        private static InspectionTreeItem BuildDetailTree(ResearchProjectDef project)
        {
            var root = new InspectionTreeItem
            {
                Label = project.LabelCap,
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            // Node 1: Description (Info, non-expandable)
            string descContent = BuildDescriptionContent(project);
            var descNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = "Description",
                Description = descContent,
                Data = DetailNodeType.Info,
                IndentLevel = 0,
                IsExpandable = false,
                Parent = root
            };
            root.Children.Add(descNode);

            // Node 2: Prerequisites (Category, expandable)
            var prereqNode = BuildPrerequisitesNode(project, root);
            if (prereqNode != null)
                root.Children.Add(prereqNode);

            // Node 3: Unlocks (Category, expandable)
            var unlocksNode = BuildUnlocksNode(project, root);
            if (unlocksNode != null)
                root.Children.Add(unlocksNode);

            // Node 4: Dependents (Category, expandable)
            var dependentsNode = BuildDependentsNode(project, root);
            if (dependentsNode != null)
                root.Children.Add(dependentsNode);

            // Node 5: Start/Stop Research (Action)
            var actionNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                Label = Find.ResearchManager.IsCurrentProject(project) ? "Stop Research" : "Start Research",
                Description = Find.ResearchManager.IsCurrentProject(project)
                    ? "Press Enter to stop this research"
                    : "Press Enter to start this research",
                Data = DetailNodeType.Action,
                IndentLevel = 0,
                IsExpandable = false,
                Parent = root
            };
            root.Children.Add(actionNode);

            return root;
        }

        /// <summary>
        /// Builds the prerequisites node with children.
        /// </summary>
        private static InspectionTreeItem BuildPrerequisitesNode(ResearchProjectDef project, InspectionTreeItem root)
        {
            var children = new List<InspectionTreeItem>();

            // Research prerequisites (visible)
            if (project.prerequisites != null && project.prerequisites.Count > 0)
            {
                foreach (var prereq in project.prerequisites.OrderBy(p => p.LabelCap.ToString()))
                {
                    string status = prereq.IsFinished ? "Completed" : "Locked";
                    children.Add(new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = $"{prereq.LabelCap} - cost: {prereq.CostApparent:F0} {status}",
                        Data = DetailNodeType.ResearchItem,
                        LinkedDef = prereq,
                        IndentLevel = 1,
                        IsExpandable = false
                    });
                }
            }

            // Hidden prerequisites - just show count without revealing what they are
            if (project.hiddenPrerequisites != null && project.hiddenPrerequisites.Count > 0)
            {
                int missingHiddenCount = project.hiddenPrerequisites.Count(p => !p.IsFinished);
                int totalHiddenCount = project.hiddenPrerequisites.Count;

                string hiddenStatus = missingHiddenCount == 0 ? "All completed" : $"{missingHiddenCount} incomplete";
                string hiddenLabel = totalHiddenCount == 1
                    ? $"1 hidden prerequisite - {hiddenStatus}"
                    : $"{totalHiddenCount} hidden prerequisites - {hiddenStatus}";

                children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = hiddenLabel,
                    Data = DetailNodeType.Info,
                    IndentLevel = 1,
                    IsExpandable = false
                });
            }

            // Required research building
            if (project.requiredResearchBuilding != null)
            {
                bool hasBench = project.PlayerHasAnyAppropriateResearchBench;
                string benchStatus = hasBench ? "Available" : "Not available";
                children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = $"Requires bench: {project.requiredResearchBuilding.LabelCap} - {benchStatus}",
                    Data = DetailNodeType.Info,
                    IndentLevel = 1,
                    IsExpandable = false
                });
            }

            // Required research facilities
            if (project.requiredResearchFacilities != null && project.requiredResearchFacilities.Count > 0)
            {
                foreach (var facility in project.requiredResearchFacilities)
                {
                    children.Add(new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = $"Requires facility: {facility.LabelCap}",
                        Data = DetailNodeType.Info,
                        IndentLevel = 1,
                        IsExpandable = false
                    });
                }
            }

            // Techprint requirement (Royalty DLC)
            if (project.TechprintCount > 0)
            {
                int applied = project.TechprintsApplied;
                int required = project.TechprintCount;
                string techprintStatus = applied >= required ? "Complete" : $"{applied}/{required}";
                children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = $"Requires techprints: {techprintStatus}",
                    Data = DetailNodeType.Info,
                    IndentLevel = 1,
                    IsExpandable = false
                });
            }

            // Mechanitor requirement (Biotech DLC)
            if (project.requiresMechanitor)
            {
                string mechStatus = project.PlayerMechanitorRequirementMet ? "Met" : "Not met";
                children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = $"Requires mechanitor - {mechStatus}",
                    Data = DetailNodeType.Info,
                    IndentLevel = 1,
                    IsExpandable = false
                });
            }

            // Required analyzed things (Biotech DLC)
            if (project.requiredAnalyzed != null && project.requiredAnalyzed.Count > 0)
            {
                int completed = project.AnalyzedThingsCompleted;
                int required = project.RequiredAnalyzedThingCount;
                string analyzeStatus = completed >= required ? "Complete" : $"{completed}/{required}";
                string thingNames = string.Join(", ", project.requiredAnalyzed.Select(t => t.LabelCap.ToString()));
                children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = $"Requires analyzing: {thingNames} - {analyzeStatus}",
                    Data = DetailNodeType.Info,
                    IndentLevel = 1,
                    IsExpandable = false
                });
            }

            // Grav engine inspection (Odyssey DLC)
            if (project.requireGravEngineInspected)
            {
                string inspectStatus = project.InspectionRequirementsMet ? "Inspected" : "Not inspected";
                children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = $"Requires grav engine inspection - {inspectStatus}",
                    Data = DetailNodeType.Info,
                    IndentLevel = 1,
                    IsExpandable = false
                });
            }

            // Even if no prerequisites, show the node with a message
            var node = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = children.Count > 0
                    ? $"Prerequisites ({children.Count} items)"
                    : "Prerequisites (none)",
                Data = DetailNodeType.Category,
                IndentLevel = 0,
                IsExpandable = children.Count > 0,
                Parent = root
            };

            // Set parent references and add children
            foreach (var child in children)
            {
                child.Parent = node;
                node.Children.Add(child);
            }

            return node;
        }

        /// <summary>
        /// Builds the unlocks node with children.
        /// </summary>
        private static InspectionTreeItem BuildUnlocksNode(ResearchProjectDef project, InspectionTreeItem root)
        {
            var children = new List<InspectionTreeItem>();

            // Get all unlocked things
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.researchPrerequisites != null && def.researchPrerequisites.Contains(project))
                {
                    string category = def.building != null ? "Building" :
                                     def.plant != null ? "Plant" : "Item";
                    var itemNode = CreateUnlockedItemNode(def.LabelCap, category, def.description, def);
                    children.Add(itemNode);
                }
            }

            // Get unlocked recipes
            foreach (var def in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (def.researchPrerequisite == project ||
                    (def.researchPrerequisites != null && def.researchPrerequisites.Contains(project)))
                {
                    // Use recipe description, or product description if available
                    string description = def.description;
                    if (string.IsNullOrEmpty(description) && def.ProducedThingDef != null)
                    {
                        description = def.ProducedThingDef.description;
                    }
                    var itemNode = CreateUnlockedItemNode(def.LabelCap, "Recipe", description, def);
                    children.Add(itemNode);
                }
            }

            // Sort by label
            children = children.OrderBy(c => c.Label).ToList();

            var node = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = children.Count > 0
                    ? $"Unlocks ({children.Count} items)"
                    : "Unlocks (none)",
                Data = DetailNodeType.Category,
                IndentLevel = 0,
                IsExpandable = children.Count > 0,
                Parent = root
            };

            // Set parent references and add children
            foreach (var child in children)
            {
                child.Parent = node;
                node.Children.Add(child);
            }

            return node;
        }

        /// <summary>
        /// Creates an unlocked item node with description inline in the label.
        /// </summary>
        private static InspectionTreeItem CreateUnlockedItemNode(string label, string category, string description, Def linkedDef = null)
        {
            // Clean up description
            string cleanDesc = "";
            if (!string.IsNullOrEmpty(description))
            {
                cleanDesc = description;
                if (cleanDesc.Contains("<"))
                {
                    cleanDesc = System.Text.RegularExpressions.Regex.Replace(cleanDesc, "<[^>]+>", "");
                }
                cleanDesc = cleanDesc.Trim();
                cleanDesc = System.Text.RegularExpressions.Regex.Replace(cleanDesc, @"\s+", " ");
            }

            // Build label with description inline
            string fullLabel = $"{label} ({category})";
            if (!string.IsNullOrEmpty(cleanDesc))
            {
                fullLabel += $" - {cleanDesc}";
            }

            return new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = fullLabel,
                Data = DetailNodeType.UnlockedItem,
                LinkedDef = linkedDef,
                IndentLevel = 1,
                IsExpandable = false
            };
        }

        /// <summary>
        /// Builds the dependents node with children.
        /// </summary>
        private static InspectionTreeItem BuildDependentsNode(ResearchProjectDef project, InspectionTreeItem root)
        {
            var children = new List<InspectionTreeItem>();

            var dependents = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => p.prerequisites != null && p.prerequisites.Contains(project))
                .OrderBy(p => p.LabelCap.ToString())
                .ToList();

            foreach (var dep in dependents)
            {
                string status = dep.IsFinished ? "Completed" :
                               dep.CanStartNow ? "Available" : "Locked";
                children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = $"{dep.LabelCap} - cost: {dep.CostApparent:F0} {status}",
                    Data = DetailNodeType.ResearchItem,
                    LinkedDef = dep,
                    IndentLevel = 1,
                    IsExpandable = false
                });
            }

            var node = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = children.Count > 0
                    ? $"Dependents ({children.Count} items)"
                    : "Dependents (none)",
                Data = DetailNodeType.Category,
                IndentLevel = 0,
                IsExpandable = children.Count > 0,
                Parent = root
            };

            // Set parent references and add children
            foreach (var child in children)
            {
                child.Parent = node;
                node.Children.Add(child);
            }

            return node;
        }

        /// <summary>
        /// Builds the description content.
        /// </summary>
        private static string BuildDescriptionContent(ResearchProjectDef project)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Project: {project.LabelCap}");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(project.description))
            {
                sb.AppendLine(project.description);
                sb.AppendLine();
            }

            if (project.CostApparent > 0)
            {
                sb.AppendLine($"Research Cost: {project.CostApparent:F0}");
            }
            else if (project.knowledgeCost > 0)
            {
                sb.AppendLine($"Knowledge Cost: {project.knowledgeCost:F0}");
            }

            if (project.knowledgeCategory != null)
            {
                sb.AppendLine($"{"KnowledgeCategory".Translate()}: {project.knowledgeCategory.LabelCap}");
            }

            if (Find.ResearchManager.IsCurrentProject(project))
            {
                float progress = project.ProgressPercent * 100f;
                sb.AppendLine($"Progress: {progress:F1}%");
            }
            else if (project.IsFinished)
            {
                sb.AppendLine("Status: Completed");
            }
            else if (project.CanStartNow)
            {
                sb.AppendLine("Status: Available to research");
            }
            else
            {
                sb.AppendLine("Status: Locked");
            }

            if (project.requiredResearchBuilding != null)
            {
                sb.AppendLine($"Required Bench: {project.requiredResearchBuilding.LabelCap}");
            }

            if (project.requiredResearchFacilities != null && project.requiredResearchFacilities.Count > 0)
            {
                sb.Append("Required Facilities: ");
                sb.AppendLine(string.Join(", ", project.requiredResearchFacilities.Select(f => f.LabelCap)));
            }

            return sb.ToString().TrimEnd();
        }

        #endregion

        #region Announcement Formatters

        private static string FormatAnnouncement(InspectionTreeItem item)
        {
            var nodeType = item.Data is DetailNodeType dt ? dt : DetailNodeType.Info;

            // Build announcement: "{name} {state}. {X of Y}. level N"
            var sb = new StringBuilder();
            sb.Append(item.Label.TrimEnd('.', '!', '?'));

            // Add expand/collapse state for expandable categories
            if (nodeType == DetailNodeType.Category && item.IsExpandable)
            {
                sb.Append(item.IsExpanded ? " expanded" : " collapsed");
            }

            // Add position
            var (position, total) = treeNav.GetSiblingPosition(item);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);
            sb.Append(string.IsNullOrEmpty(positionPart) ? "." : $". {positionPart}.");

            // Add level suffix at the end (only announced when level changes)
            sb.Append(MenuHelper.GetLevelSuffix("ResearchDetail", item.IndentLevel));

            // For info nodes, append content after main announcement
            if (nodeType == DetailNodeType.Info && !string.IsNullOrEmpty(item.Description))
            {
                sb.Append("\n\n");
                sb.Append(item.Description);
            }

            return sb.ToString();
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
            if (currentProject == null) return false;

            var nodeType = item.Data is DetailNodeType dt ? dt : DetailNodeType.Info;

            switch (nodeType)
            {
                case DetailNodeType.Category:
                    // Let default toggle expand/collapse handle it
                    return false;

                case DetailNodeType.ResearchItem:
                    // Drill into this research project
                    if (item.LinkedDef is ResearchProjectDef linkedProject)
                    {
                        OpenWithBackNavigation(linkedProject);
                        return true;
                    }
                    return false;

                case DetailNodeType.UnlockedItem:
                    // Re-read the label (which contains the description inline)
                    TolkHelper.Speak(item.Label);
                    return true;

                case DetailNodeType.Action:
                    ExecuteResearchAction();
                    return true;

                case DetailNodeType.Info:
                    // Read the content
                    if (!string.IsNullOrEmpty(item.Description))
                    {
                        TolkHelper.Speak(item.Description);
                        return true;
                    }
                    return false;
            }

            return false;
        }

        private static bool HandleInfoCard(InspectionTreeItem item)
        {
            if (currentProject == null) return false;

            var nodeType = item.Data is DetailNodeType dt ? dt : DetailNodeType.Info;

            switch (nodeType)
            {
                case DetailNodeType.ResearchItem:
                    if (item.LinkedDef is ResearchProjectDef linkedProject)
                    {
                        Find.WindowStack.Add(new Dialog_InfoCard(linkedProject));
                        return true;
                    }
                    return false;

                case DetailNodeType.UnlockedItem:
                    if (item.LinkedDef != null)
                    {
                        InfoCardState.OpenInfoCardForDef(item.LinkedDef);
                        return true;
                    }
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("No info card available for this item");
                    return true;

                case DetailNodeType.Info:
                case DetailNodeType.Action:
                    Find.WindowStack.Add(new Dialog_InfoCard(currentProject));
                    return true;

                case DetailNodeType.Category:
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("No info card for this section");
                    return true;
            }

            return false;
        }

        #endregion

        #region Research Actions

        /// <summary>
        /// Starts or stops research on the current project.
        /// </summary>
        private static void ExecuteResearchAction()
        {
            if (currentProject == null) return;

            // Check if already researching this project
            if (Find.ResearchManager.IsCurrentProject(currentProject))
            {
                Find.ResearchManager.StopProject(currentProject);
                TolkHelper.Speak($"Stopped research on {currentProject.LabelCap}");
                RefreshTree();
                return;
            }

            // Check if already completed
            if (currentProject.IsFinished)
            {
                TolkHelper.Speak($"{currentProject.LabelCap} is already completed");
                return;
            }

            // Check prerequisites
            if (!currentProject.PrerequisitesCompleted)
            {
                var missingPrereqs = GetMissingPrerequisites();
                TolkHelper.Speak($"Cannot start research: Missing prerequisites - {missingPrereqs}", SpeechPriority.High);
                return;
            }

            // Check techprint requirements
            if (currentProject.TechprintCount > 0 && !currentProject.TechprintRequirementMet)
            {
                int applied = Find.ResearchManager.GetTechprints(currentProject);
                TolkHelper.Speak($"Cannot start research: Need {currentProject.TechprintCount} techprints, only {applied} applied", SpeechPriority.High);
                return;
            }

            // Check study requirements
            if (currentProject.requiredAnalyzed != null && currentProject.requiredAnalyzed.Count > 0)
            {
                if (!currentProject.AnalyzedThingsRequirementsMet)
                {
                    TolkHelper.Speak($"Cannot start research: Must study required items first", SpeechPriority.High);
                    return;
                }
            }

            // Capture previous project from the same track (regular research and each anomaly
            // knowledge category occupy independent slots, so starting an anomaly project does
            // not stop a regular one and vice versa).
            var previousProject = Find.ResearchManager.GetProject(currentProject.knowledgeCategory);

            // Start research
            Find.ResearchManager.SetCurrentProject(currentProject);

            // Announce with replacement info if applicable
            if (previousProject != null && previousProject != currentProject)
            {
                float previousProgress = previousProject.ProgressPercent * 100f;
                TolkHelper.Speak($"Started research on {currentProject.LabelCap}. Stopped {previousProject.LabelCap} at {previousProgress:F0}% progress.");
            }
            else
            {
                TolkHelper.Speak($"Started research on {currentProject.LabelCap}");
            }
            RefreshTree();
        }

        /// <summary>
        /// Refreshes the tree after changes (like starting/stopping research).
        /// </summary>
        private static void RefreshTree()
        {
            int previousIndex = treeNav.SelectedIndex;
            var root = BuildDetailTree(currentProject);
            treeNav.Initialize(root, previousIndex);
            treeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Gets a formatted list of missing prerequisites.
        /// </summary>
        private static string GetMissingPrerequisites()
        {
            if (currentProject == null)
                return "Unknown";

            var parts = new List<string>();

            // Visible prerequisites
            if (currentProject.prerequisites != null)
            {
                var missing = currentProject.prerequisites
                    .Where(p => !p.IsFinished)
                    .Select(p => p.LabelCap.ToString());
                parts.AddRange(missing);
            }

            // Hidden prerequisites - just mention they exist
            if (currentProject.hiddenPrerequisites != null)
            {
                int missingHiddenCount = currentProject.hiddenPrerequisites.Count(p => !p.IsFinished);
                if (missingHiddenCount > 0)
                {
                    string hiddenText = missingHiddenCount == 1
                        ? "1 hidden prerequisite"
                        : $"{missingHiddenCount} hidden prerequisites";
                    parts.Add(hiddenText);
                }
            }

            return parts.Count > 0 ? string.Join(", ", parts) : "Unknown";
        }

        #endregion
    }

    /// <summary>
    /// Type of detail node.
    /// </summary>
    public enum DetailNodeType
    {
        Info,           // Description section
        Category,       // Prerequisites, Unlocks, Dependents headers
        ResearchItem,   // A research project that can be drilled into
        UnlockedItem,   // A building/recipe that can be inspected
        Action          // Start/Stop research button
    }
}
