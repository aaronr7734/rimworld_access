using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Data/lifecycle facade for the windowless research menu: organizes projects by tab
    /// (Main/Anomaly) then status (Completed/Available/Locked/In Progress) into an
    /// <see cref="InspectionTreeItem"/> tree, and owns <see cref="isActive"/>, label formatting
    /// and the info-card/dev-menu mutations. Cursor, typeahead and announcements live on
    /// <see cref="RimWorldAccess.Shell.ResearchMenuScope"/> instead, reached through the bridge
    /// callbacks the scope wires once, so <see cref="Open"/>/<see cref="OpenAndSelectProject"/>
    /// drive the tree build and announcement synchronously whether or not the scope is pushed.
    /// </summary>
    public static class WindowlessResearchMenuState
    {
        private static bool isActive = false;

        public static bool IsActive => isActive;

        /// <summary>Wired once by ResearchScopeMirror's static constructor to ResearchMenuScope.RebuildFresh.</summary>
        internal static Action RebuildCallback;

        /// <summary>Wired once by ResearchScopeMirror's static constructor to ResearchMenuScope.FocusOnProject.</summary>
        internal static Action<ResearchProjectDef> FocusProjectCallback;

        /// <summary>
        /// Opens the research menu, always rebuilding the tree and re-speaking the title even
        /// when it was already open.
        /// </summary>
        public static void Open()
        {
            isActive = true;
            TolkHelper.Speak("RimWorldAccess.Research.Menu.Title".Loc());
            RebuildCallback?.Invoke();
        }

        /// <summary>Closes the research menu.</summary>
        public static void Close()
        {
            isActive = false;
            TolkHelper.Speak("RimWorldAccess.Research.Menu.Closed".Loc());
            Shell.MainTabWindowLink.CloseTab(Shell.MainTabWindowLink.Research);
        }

        /// <summary>
        /// Silent hard reset at a session boundary; unlike <see cref="Close"/> it announces
        /// nothing. Windowless, so there is no deterministic close to rely on instead.
        /// </summary>
        internal static void ResetHard()
        {
            isActive = false;
        }

        /// <summary>
        /// Opens the research menu with the cursor on one project, for research hyperlinks in
        /// letters. Only the ancestors on the leaf's own path are expanded: blanket-expanding
        /// every category can land the cursor on a parent tab instead of the project.
        /// </summary>
        public static void OpenAndSelectProject(ResearchProjectDef project)
        {
            if (project == null)
            {
                TolkHelper.Speak("RimWorldAccess.Research.Menu.ProjectNotAvailable".Loc());
                return;
            }

            isActive = true;
            FocusProjectCallback?.Invoke(project);
            // This opener runs straight off a hyperlink, so it must pair the tab window itself:
            // otherwise MainTabWindowLink reads "state active, window closed" and resets the
            // session a frame later.
            Shell.MainTabWindowLink.EnsureTabOpen(Shell.MainTabWindowLink.Research);
        }

        /// <summary>Finds the leaf node carrying the given project in its Data field.</summary>
        internal static InspectionTreeItem FindProjectNode(InspectionTreeItem node, ResearchProjectDef target)
        {
            if (node.Data is ResearchProjectDef proj && proj == target) return node;
            foreach (var child in node.Children)
            {
                var found = FindProjectNode(child, target);
                if (found != null) return found;
            }
            return null;
        }

        #region Tree Building

        /// <summary>
        /// Builds the tab → status group → project tree. A single tab is dropped and its status
        /// groups become the roots.
        /// </summary>
        internal static InspectionTreeItem BuildCategoryTree()
        {
            var root = new InspectionTreeItem
            {
                Label = "RimWorldAccess.Research.Tree.RootLabel".Translate(),
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            // Mirrors MainTabWindow_Research.VisibleResearchProjects so anomaly-disabled or
            // otherwise hidden projects don't leak through to keyboard navigation.
            var difficulty = Find.Storyteller?.difficulty;
            var researchManager = Find.ResearchManager;
            var allProjects = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => difficulty == null
                    || difficulty.AllowedBy(p.hideWhen)
                    || (researchManager != null && researchManager.IsCurrentProject(p)))
                .ToList();

            var projectsByTab = allProjects.GroupBy(p => p.tab ?? ResearchTabDefOf.Main).ToList();
            bool singleTab = projectsByTab.Count == 1;

            foreach (var tabGroup in projectsByTab.OrderBy(g => g.Key.defName))
            {
                var tab = tabGroup.Key;
                var tabProjects = tabGroup.ToList();

                // A tab (Anomaly, pre-monolith) can be undiscovered entirely — vanilla replaces
                // the whole pane with "ResearchNotDiscovered" instead of listing any of its
                // projects (MainTabWindow_Research.cs:788, 838; ResearchManager.TabInfoVisible).
                bool tabInfoVisible = researchManager == null || researchManager.TabInfoVisible(tab);

                var inProgress = tabInfoVisible ? GetInProgressProjects(tabProjects) : new List<ResearchProjectDef>();
                var completed = tabInfoVisible ? tabProjects.Where(p => p.IsFinished).ToList() : new List<ResearchProjectDef>();
                var available = tabInfoVisible ? tabProjects.Where(p => !p.IsFinished && p.CanStartNow).ToList() : new List<ResearchProjectDef>();
                var locked = tabInfoVisible ? tabProjects.Where(p => !p.IsFinished && !p.CanStartNow).ToList() : new List<ResearchProjectDef>();

                if (singleTab)
                {
                    if (!tabInfoVisible)
                    {
                        root.Children.Add(CreateTabNotDiscoveredNode(0, root));
                        continue;
                    }
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
                    var tabNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Category,
                        Label = tab.LabelCap.ToString(),
                        IndentLevel = 0,
                        IsExpandable = true,
                        IsExpanded = false,
                        Parent = root
                    };

                    if (!tabInfoVisible)
                    {
                        tabNode.Children.Add(CreateTabNotDiscoveredNode(1, tabNode));
                    }
                    else
                    {
                        if (inProgress.Count > 0)
                            tabNode.Children.Add(CreateStatusGroupNode(StatusLabel.InProgress, inProgress, 1, tabNode));
                        if (available.Count > 0)
                            tabNode.Children.Add(CreateStatusGroupNode(StatusLabel.Available, available, 1, tabNode));
                        if (completed.Count > 0)
                            tabNode.Children.Add(CreateStatusGroupNode(StatusLabel.Completed, completed, 1, tabNode));
                        if (locked.Count > 0)
                            tabNode.Children.Add(CreateStatusGroupNode(StatusLabel.Locked, locked, 1, tabNode));
                    }

                    root.Children.Add(tabNode);
                }
            }

            return root;
        }

        /// <summary>
        /// Builds the leaf node shown in place of a tab's contents when
        /// <see cref="ResearchManager.TabInfoVisible"/> is false for it (e.g. Anomaly before the
        /// monolith is activated), mirroring vanilla's "ResearchNotDiscovered" placeholder.
        /// </summary>
        private static InspectionTreeItem CreateTabNotDiscoveredNode(int level, InspectionTreeItem parent)
        {
            return new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = "ResearchNotDiscovered".Translate(),
                IndentLevel = level,
                IsExpandable = false,
                Parent = parent
            };
        }

        /// <summary>In-progress projects, standard research and anomaly knowledge alike.</summary>
        private static List<ResearchProjectDef> GetInProgressProjects(List<ResearchProjectDef> tabProjects)
        {
            var inProgress = new List<ResearchProjectDef>();

            var currentProject = Find.ResearchManager.GetProject();
            if (currentProject != null && tabProjects.Contains(currentProject))
            {
                inProgress.Add(currentProject);
            }

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

        /// <summary>The status group key behind an "InProgress (N)"-style category header.</summary>
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

        /// <summary>Creates a status group node; with tech-level grouping, a multi-level group nests one sub-group per level.</summary>
        private static InspectionTreeItem CreateStatusGroupNode(StatusLabel status, List<ResearchProjectDef> projects, int level, InspectionTreeItem parent)
        {
            string statusLabel = FormatStatusGroupLabel(status, projects.Count);
            var statusNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = statusLabel,
                IndentLevel = level,
                IsExpandable = true,
                IsExpanded = false,
                Parent = parent
            };

            if (!ResearchTechLevelGrouping.Active || projects.Count == 0)
            {
                AddProjectNodes(statusNode, projects, level + 1);
                return statusNode;
            }

            var byTechLevel = projects
                .GroupBy(p => p.techLevel)
                .OrderBy(g => g.Key == TechLevel.Undefined ? int.MaxValue : (int)g.Key)
                .ToList();

            statusNode.Label = "RimWorldAccess.Research.TechLevel.StatusSummary".Translate(
                statusLabel,
                string.Join(", ", byTechLevel.Select(g =>
                    "RimWorldAccess.Research.TechLevel.Count".Translate(g.Count(), TechLevelLabel(g.Key)).ToString())));

            if (byTechLevel.Count == 1)
            {
                AddProjectNodes(statusNode, projects, level + 1);
                return statusNode;
            }

            statusNode.ExpandedLabel = statusLabel;
            foreach (var techGroup in byTechLevel)
            {
                var techNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = "RimWorldAccess.Research.TechLevel.Group".Translate(TechLevelLabel(techGroup.Key), techGroup.Count()),
                    IndentLevel = level + 1,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = statusNode
                };
                AddProjectNodes(techNode, techGroup.ToList(), level + 2);
                statusNode.Children.Add(techNode);
            }

            return statusNode;
        }

        private static string TechLevelLabel(TechLevel techLevel)
        {
            return techLevel.ToStringHuman().CapitalizeFirst();
        }

        private static void AddProjectNodes(InspectionTreeItem parentNode, List<ResearchProjectDef> projects, int level)
        {
            foreach (var project in projects.OrderBy(p => p.LabelCap.ToString()))
            {
                parentNode.Children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = FormatProjectLabel(project),
                    IndentLevel = level,
                    IsExpandable = false,
                    IsExpanded = false,
                    Data = project,
                    LinkedDef = project,
                    Parent = parentNode
                });
            }
        }

        /// <summary>Formats a project label with its cost and progress.</summary>
        private static string FormatProjectLabel(ResearchProjectDef project)
        {
            // A hidden Anomaly project loses its whole label and shows no cost, progress or
            // status, as vanilla's ListProjects does (MainTabWindow_Research.cs:915-918, 1015).
            if (project.IsHidden)
            {
                return string.Format("({0})", "UnknownResearch".Translate());
            }

            string label = project.LabelCap.ToString();

            // The knowledge tier is a colored icon on the tile visually, and load-bearing:
            // anomaly projects only progress while studying an entity of the matching tier.
            if (project.knowledgeCategory != null)
            {
                label += $" - {project.knowledgeCategory.LabelCap}";
            }

            float cost = project.CostApparent;
            if (cost > 0)
            {
                label += "RimWorldAccess.Research.Label.CostSuffix".Translate(cost.ToString("F0"));
            }
            else if (project.knowledgeCost > 0)
            {
                label += "RimWorldAccess.Research.Label.KnowledgeSuffix".Translate(project.knowledgeCost.ToString("F0"));
            }

            if (Find.ResearchManager.IsCurrentProject(project))
            {
                float progress = project.ProgressPercent * 100f;
                label += "RimWorldAccess.Research.Label.ProgressSuffix".Translate(progress.ToString("F0"));
            }

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

        #region Custom Actions

        /// <summary>
        /// Alt+I on the focused item. Carries vanilla's spoiler gate: a hidden Anomaly project's
        /// real name and description must never reach the info card (ResearchProjectDef.IsHidden).
        /// </summary>
        internal static void HandleInfoCard(InspectionTreeItem item)
        {
            if (item.Type == InspectionTreeItem.ItemType.Item && item.Data is ResearchProjectDef project)
            {
                if (project.IsHidden)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.SpeakData(string.Format("({0})", "UnknownResearch".Translate()), SpeechPriority.High);
                    return;
                }
                Find.WindowStack.Add(new Dialog_InfoCard(project));
                return;
            }

            InfoCardState.SpeakNoInfoCardAvailable();
        }

        /// <summary>
        /// Opens the DEV context menu for a project (null when the cursor sits on a category
        /// node), mirroring MainTabWindow_Research's embedded debug buttons: only the actions
        /// vanilla would draw are listed, with its dev-tool labels verbatim. The list reflects the
        /// pre-action tree and the change surfaces on the next reopen.
        /// </summary>
        internal static void OpenDevContextMenu(ResearchProjectDef project)
        {
            if (!Prefs.DevMode)
                return;

            var options = new List<FloatMenuOption>();
            if (project != null)
            {
                if (!Find.ResearchManager.IsCurrentProject(project) && !project.IsFinished)
                {
                    ResearchProjectDef captured = project;
                    options.Add(new FloatMenuOption("Debug: Finish now", delegate
                    {
                        Find.ResearchManager.SetCurrentProject(captured);
                        Find.ResearchManager.FinishProject(captured);
                        TolkHelper.Speak("RimWorldAccess.Dev.ResearchFinished".Loc(captured.LabelCap.Resolve()));
                    }));
                }

                if (!project.TechprintRequirementMet)
                {
                    ResearchProjectDef captured = project;
                    options.Add(new FloatMenuOption("Debug: Apply techprint", delegate
                    {
                        Find.ResearchManager.ApplyTechprint(captured, null);
                        SoundDefOf.TechprintApplied.PlayOneShotOnCamera();
                        TolkHelper.Speak("RimWorldAccess.Dev.ResearchTechprintApplied".Loc(captured.LabelCap.Resolve()));
                    }));
                }
            }

            if (options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Dev.NoActions".Loc());
                return;
            }

            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        #endregion
    }
}
