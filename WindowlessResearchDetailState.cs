using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the detail view for a specific research project.
    /// Allows navigation through project information sections and starting/stopping research.
    /// </summary>
    public static class WindowlessResearchDetailState
    {
        private static bool isActive = false;
        private static ResearchProjectDef currentProject = null;
        private static List<DetailSection> sections = new List<DetailSection>();
        private static int currentSectionIndex = 0;

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the detail view for a specific research project.
        /// </summary>
        public static void Open(ResearchProjectDef project)
        {
            currentProject = project;
            isActive = true;
            sections = BuildDetailSections(project);
            currentSectionIndex = 0;
            AnnounceCurrentSection();
        }

        /// <summary>
        /// Closes the detail view and returns to the research menu.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentProject = null;
            sections.Clear();
            ClipboardHelper.CopyToClipboard("Returned to research menu");
        }

        /// <summary>
        /// Navigates to the next section.
        /// </summary>
        public static void SelectNext()
        {
            if (sections.Count == 0) return;

            currentSectionIndex = (currentSectionIndex + 1) % sections.Count;
            AnnounceCurrentSection();
        }

        /// <summary>
        /// Navigates to the previous section.
        /// </summary>
        public static void SelectPrevious()
        {
            if (sections.Count == 0) return;

            currentSectionIndex--;
            if (currentSectionIndex < 0)
                currentSectionIndex = sections.Count - 1;

            AnnounceCurrentSection();
        }

        /// <summary>
        /// Executes the action for the current section (e.g., starts research).
        /// </summary>
        public static void ExecuteCurrentSection()
        {
            if (sections.Count == 0 || currentProject == null) return;

            var section = sections[currentSectionIndex];

            if (section.Type == DetailSectionType.Action)
            {
                // Check if this is the start/stop research button
                if (section.Title.Contains("Start") || section.Title.Contains("Stop"))
                {
                    ExecuteResearchAction();
                }
            }
        }

        /// <summary>
        /// Starts or stops research on the current project.
        /// </summary>
        private static void ExecuteResearchAction()
        {
            if (currentProject == null) return;

            // Check if already researching this project
            if (Find.ResearchManager.IsCurrentProject(currentProject))
            {
                // Stop research
                Find.ResearchManager.StopProject(currentProject);
                ClipboardHelper.CopyToClipboard($"Stopped research on {currentProject.LabelCap}");

                // Rebuild sections to update button text
                sections = BuildDetailSections(currentProject);
                AnnounceCurrentSection();
                return;
            }

            // Check if already completed
            if (currentProject.IsFinished)
            {
                ClipboardHelper.CopyToClipboard($"{currentProject.LabelCap} is already completed");
                return;
            }

            // Check prerequisites
            if (!currentProject.PrerequisitesCompleted)
            {
                var missingPrereqs = GetMissingPrerequisites();
                ClipboardHelper.CopyToClipboard($"Cannot start research: Missing prerequisites - {missingPrereqs}");
                return;
            }

            // Check techprint requirements
            if (currentProject.TechprintCount > 0 && !currentProject.TechprintRequirementMet)
            {
                int applied = Find.ResearchManager.GetTechprints(currentProject);
                ClipboardHelper.CopyToClipboard($"Cannot start research: Need {currentProject.TechprintCount} techprints, only {applied} applied");
                return;
            }

            // Check study requirements (if applicable)
            if (currentProject.requiredAnalyzed != null && currentProject.requiredAnalyzed.Count > 0)
            {
                if (!currentProject.AnalyzedThingsRequirementsMet)
                {
                    ClipboardHelper.CopyToClipboard($"Cannot start research: Must study required items first");
                    return;
                }
            }

            // Start research
            Find.ResearchManager.SetCurrentProject(currentProject);
            ClipboardHelper.CopyToClipboard($"Started research on {currentProject.LabelCap}");

            // Rebuild sections to update button text and progress
            sections = BuildDetailSections(currentProject);
            AnnounceCurrentSection();
        }

        /// <summary>
        /// Gets a formatted list of missing prerequisites.
        /// </summary>
        private static string GetMissingPrerequisites()
        {
            if (currentProject == null || currentProject.prerequisites == null)
                return "Unknown";

            var missing = currentProject.prerequisites
                .Where(p => !p.IsFinished)
                .Select(p => p.LabelCap.ToString());

            return string.Join(", ", missing);
        }

        /// <summary>
        /// Builds the list of detail sections for the project.
        /// </summary>
        private static List<DetailSection> BuildDetailSections(ResearchProjectDef project)
        {
            var detailSections = new List<DetailSection>();

            // Section 1: Description and Cost
            detailSections.Add(new DetailSection
            {
                Type = DetailSectionType.Info,
                Title = "Description",
                Content = BuildDescriptionContent(project)
            });

            // Section 2: Prerequisites
            detailSections.Add(new DetailSection
            {
                Type = DetailSectionType.Info,
                Title = "Prerequisites",
                Content = BuildPrerequisitesContent(project)
            });

            // Section 3: What This Unlocks
            detailSections.Add(new DetailSection
            {
                Type = DetailSectionType.Info,
                Title = "Unlocks",
                Content = BuildUnlocksContent(project)
            });

            // Section 4: What Depends On This
            detailSections.Add(new DetailSection
            {
                Type = DetailSectionType.Info,
                Title = "Dependents",
                Content = BuildDependentsContent(project)
            });

            // Section 5: Start/Stop Research Button
            detailSections.Add(new DetailSection
            {
                Type = DetailSectionType.Action,
                Title = Find.ResearchManager.IsCurrentProject(project) ? "Stop Research" : "Start Research",
                Content = Find.ResearchManager.IsCurrentProject(project)
                    ? "Press Enter to stop this research"
                    : "Press Enter to start this research"
            });

            return detailSections;
        }

        /// <summary>
        /// Builds the description and cost content section.
        /// </summary>
        private static string BuildDescriptionContent(ResearchProjectDef project)
        {
            var sb = new StringBuilder();

            // Project name
            sb.AppendLine($"Project: {project.LabelCap}");
            sb.AppendLine();

            // Description
            if (!string.IsNullOrEmpty(project.description))
            {
                sb.AppendLine(project.description);
                sb.AppendLine();
            }

            // Cost
            if (project.CostApparent > 0)
            {
                sb.AppendLine($"Research Cost: {project.CostApparent:F0}");
            }
            else if (project.knowledgeCost > 0)
            {
                sb.AppendLine($"Knowledge Cost: {project.knowledgeCost:F0}");
                if (project.knowledgeCategory != null)
                {
                    sb.AppendLine($"Knowledge Category: {project.knowledgeCategory.LabelCap}");
                }
            }

            // Progress
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

            // Required research bench
            if (project.requiredResearchBuilding != null)
            {
                sb.AppendLine($"Required Bench: {project.requiredResearchBuilding.LabelCap}");
            }

            // Required facilities
            if (project.requiredResearchFacilities != null && project.requiredResearchFacilities.Count > 0)
            {
                sb.Append("Required Facilities: ");
                sb.AppendLine(string.Join(", ", project.requiredResearchFacilities.Select(f => f.LabelCap)));
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Builds the prerequisites content section.
        /// </summary>
        private static string BuildPrerequisitesContent(ResearchProjectDef project)
        {
            var sb = new StringBuilder();

            // Research prerequisites
            if (project.prerequisites != null && project.prerequisites.Count > 0)
            {
                sb.AppendLine("Research Prerequisites:");
                foreach (var prereq in project.prerequisites)
                {
                    string status = prereq.IsFinished ? "✓" : "✗";
                    sb.AppendLine($"  {status} {prereq.LabelCap}");
                }
            }
            else
            {
                sb.AppendLine("No research prerequisites");
            }

            // Techprint requirements
            if (project.TechprintCount > 0)
            {
                sb.AppendLine();
                int applied = Find.ResearchManager.GetTechprints(project);
                sb.AppendLine($"Techprints: {applied} of {project.TechprintCount} applied");

                if (!project.TechprintRequirementMet)
                {
                    sb.AppendLine($"Need {project.TechprintCount - applied} more techprints");
                }
            }

            // Study requirements
            if (project.requiredAnalyzed != null && project.requiredAnalyzed.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Must Analyze:");
                foreach (var thing in project.requiredAnalyzed)
                {
                    // Check if the thing has been analyzed
                    var analysisID = thing.GetCompProperties<CompProperties_CompAnalyzableUnlockResearch>()?.analysisID ?? -1;
                    Find.AnalysisManager.TryGetAnalysisProgress(analysisID, out var details);
                    bool analyzed = details != null && details.Satisfied;
                    string status = analyzed ? "✓" : "✗";
                    sb.AppendLine($"  {status} {thing.LabelCap}");
                }
            }

            // Overall status
            sb.AppendLine();
            if (project.PrerequisitesCompleted && project.TechprintRequirementMet && project.AnalyzedThingsRequirementsMet)
            {
                sb.AppendLine("All prerequisites met ✓");
            }
            else
            {
                sb.AppendLine("Prerequisites not met ✗");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Builds the unlocks content section.
        /// </summary>
        private static string BuildUnlocksContent(ResearchProjectDef project)
        {
            var sb = new StringBuilder();

            // Get all unlocked things
            var unlockedBuildings = new List<string>();
            var unlockedRecipes = new List<string>();
            var unlockedPlants = new List<string>();
            var unlockedOther = new List<string>();

            // Check buildings
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.researchPrerequisites != null && def.researchPrerequisites.Contains(project))
                {
                    if (def.building != null)
                        unlockedBuildings.Add(def.LabelCap.ToString());
                    else if (def.plant != null)
                        unlockedPlants.Add(def.LabelCap.ToString());
                    else
                        unlockedOther.Add(def.LabelCap.ToString());
                }
            }

            // Check recipes
            foreach (var def in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (def.researchPrerequisite == project ||
                    (def.researchPrerequisites != null && def.researchPrerequisites.Contains(project)))
                {
                    unlockedRecipes.Add(def.LabelCap.ToString());
                }
            }

            // Display unlocks
            int totalUnlocks = unlockedBuildings.Count + unlockedRecipes.Count + unlockedPlants.Count + unlockedOther.Count;

            if (totalUnlocks == 0)
            {
                sb.AppendLine("This research doesn't directly unlock any items");
                sb.AppendLine("(It may be a prerequisite for other research)");
            }
            else
            {
                sb.AppendLine($"This research unlocks {totalUnlocks} items:");
                sb.AppendLine();

                if (unlockedBuildings.Count > 0)
                {
                    sb.AppendLine("Buildings:");
                    foreach (var item in unlockedBuildings.OrderBy(x => x))
                        sb.AppendLine($"  • {item}");
                    sb.AppendLine();
                }

                if (unlockedRecipes.Count > 0)
                {
                    sb.AppendLine("Recipes:");
                    foreach (var item in unlockedRecipes.OrderBy(x => x))
                        sb.AppendLine($"  • {item}");
                    sb.AppendLine();
                }

                if (unlockedPlants.Count > 0)
                {
                    sb.AppendLine("Plants:");
                    foreach (var item in unlockedPlants.OrderBy(x => x))
                        sb.AppendLine($"  • {item}");
                    sb.AppendLine();
                }

                if (unlockedOther.Count > 0)
                {
                    sb.AppendLine("Other:");
                    foreach (var item in unlockedOther.OrderBy(x => x))
                        sb.AppendLine($"  • {item}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Builds the dependents content section (research that requires this).
        /// </summary>
        private static string BuildDependentsContent(ResearchProjectDef project)
        {
            var sb = new StringBuilder();

            // Find all research projects that depend on this one
            var dependents = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => p.prerequisites != null && p.prerequisites.Contains(project))
                .OrderBy(p => p.LabelCap.ToString())
                .ToList();

            if (dependents.Count == 0)
            {
                sb.AppendLine("No research projects require this as a prerequisite");
            }
            else
            {
                sb.AppendLine($"{dependents.Count} research projects require this:");
                sb.AppendLine();

                foreach (var dependent in dependents)
                {
                    string status = dependent.IsFinished ? "(Completed)" :
                                   dependent.CanStartNow ? "(Available)" :
                                   "(Locked)";
                    sb.AppendLine($"  • {dependent.LabelCap} {status}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Announces the current section to the clipboard.
        /// </summary>
        private static void AnnounceCurrentSection()
        {
            if (sections.Count == 0 || currentProject == null)
            {
                ClipboardHelper.CopyToClipboard("No detail sections available");
                return;
            }

            var section = sections[currentSectionIndex];
            int position = currentSectionIndex + 1;

            StringBuilder announcement = new StringBuilder();
            announcement.AppendLine($"Section {position} of {sections.Count}: {section.Title}");
            announcement.AppendLine();
            announcement.Append(section.Content);

            ClipboardHelper.CopyToClipboard(announcement.ToString());
        }
    }

    /// <summary>
    /// Represents a section in the research detail view.
    /// </summary>
    public class DetailSection
    {
        public DetailSectionType Type { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
    }

    /// <summary>
    /// Type of detail section.
    /// </summary>
    public enum DetailSectionType
    {
        Info,
        Action
    }
}
