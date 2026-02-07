using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Controls what type of inspection tree to build.
    /// </summary>
    public enum InspectionMode
    {
        /// <summary>
        /// Full inspection with all actions (operations, drop/consume, job cancellation, etc.)
        /// </summary>
        Full,

        /// <summary>
        /// Read-only inspection showing only data, no modifying actions.
        /// Used in contexts like caravan formation where you just want to view pawn info.
        /// </summary>
        ReadOnly
    }


    /// <summary>
    /// Builds the inspection tree for objects.
    /// </summary>
    public static class InspectionTreeBuilder
    {
        /// <summary>
        /// Helper method to add a child to a parent and set the parent reference.
        /// </summary>
        private static void AddChild(InspectionTreeItem parent, InspectionTreeItem child)
        {
            child.Parent = parent;
            parent.Children.Add(child);
        }

        /// <summary>
        /// Checks if a hediff is a missing part caused by surgical addition (bionic).
        /// These clutter the display since they're just side effects of having bionics.
        /// </summary>
        private static bool IsSurgicallyRemovedPart(Hediff hediff, Pawn pawn)
        {
            // Only filter Hediff_MissingPart
            if (!(hediff is Hediff_MissingPart missingPart))
                return false;

            // Filter if the parent part has a bionic/added part
            if (hediff.Part != null && pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(hediff.Part))
                return true;

            return false;
        }

        /// <summary>
        /// Extracts a pawn from a thing (pawn or corpse).
        /// Returns null if the thing is neither a pawn nor a corpse with an inner pawn.
        /// </summary>
        private static Pawn GetPawnFromThing(object obj)
        {
            if (obj is Pawn pawn)
                return pawn;

            if (obj is Corpse corpse)
                return corpse.InnerPawn;

            return null;
        }

        /// <summary>
        /// Builds the root tree for all objects at a position.
        /// </summary>
        /// <param name="objects">The objects to inspect.</param>
        /// <param name="mode">The inspection mode (Full or ReadOnly). Defaults to Full.</param>
        public static InspectionTreeItem BuildTree(List<object> objects, InspectionMode mode = InspectionMode.Full)
        {
            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = "Inspection",
                IsExpandable = true,
                IsExpanded = true,
                IndentLevel = -1  // Root is not shown
            };

            foreach (var obj in objects)
            {
                AddChild(root, BuildObjectItem(obj, 0, mode));
            }

            return root;
        }

        /// <summary>
        /// Builds a tree item for a single object (pawn, building, etc.).
        /// </summary>
        private static InspectionTreeItem BuildObjectItem(object obj, int indent, InspectionMode mode)
        {
            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = InspectionInfoHelper.GetObjectSummary(obj),
                Data = obj,
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };

            // We'll build children lazily when expanded
            item.OnActivate = () => BuildObjectChildren(item, mode);

            return item;
        }

        /// <summary>
        /// Builds category children for an object when it's expanded.
        /// Uses dynamic tab discovery for Things (pawns, buildings, items).
        /// </summary>
        private static void BuildObjectChildren(InspectionTreeItem objectItem, InspectionMode mode)
        {
            if (objectItem.Children.Count > 0)
                return; // Already built

            var obj = objectItem.Data;

            // Ensure the object is selected before discovering tabs.
            // Many tabs (like ITab_Storage) check IsVisible via Find.Selector.SingleSelectedThing.
            // The selection may have changed since the inspection panel opened.
            if (obj is Thing thingToSelect && !Find.Selector.IsSelected(thingToSelect))
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(thingToSelect, playSound: false, forceDesignatorDeselect: false);
            }
            else if (obj is Zone zoneToSelect && !Find.Selector.IsSelected(zoneToSelect))
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(zoneToSelect, playSound: false, forceDesignatorDeselect: false);
            }

            // Use new dynamic categories that discover tabs from the game
            var dynamicCategories = InspectionInfoHelper.GetDynamicCategories(obj);

            foreach (var categoryInfo in dynamicCategories)
            {
                // Skip actionable categories in read-only mode
                if (mode == InspectionMode.ReadOnly && categoryInfo.Handler == TabHandlerType.Action)
                    continue;

                AddChild(objectItem, BuildCategoryItemFromInfo(obj, categoryInfo, objectItem.IndentLevel + 1, mode));
            }

            // Add Info Card action for Things (pawns, buildings, items)
            // Info Card is read-only so it's available in all modes
            if (obj is Thing thing)
            {
                var infoCardItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = ConceptDefOf.InfoCard.label.CapitalizeFirst(),
                    Data = thing,
                    IndentLevel = objectItem.IndentLevel + 1,
                    IsExpandable = false
                };
                infoCardItem.OnActivate = () =>
                {
                    // Close inspection menu before opening Info Card
                    WindowlessInspectionState.Close();

                    // Open the visual Dialog_InfoCard (InfoCardPatch will activate InfoCardState)
                    var dialog = new Dialog_InfoCard(thing);
                    Find.WindowStack.Add(dialog);
                };
                AddChild(objectItem, infoCardItem);
            }
        }

        /// <summary>
        /// Builds a tree item from a TabCategoryInfo (dynamic tab discovery).
        /// </summary>
        private static InspectionTreeItem BuildCategoryItemFromInfo(object obj, TabCategoryInfo categoryInfo, int indent, InspectionMode mode = InspectionMode.Full)
        {
            // Use OriginalCategoryName (English) for internal logic
            string categoryKey = categoryInfo.OriginalCategoryName ?? categoryInfo.Name;
            // Use Name (translated) for display
            string displayName = categoryInfo.Name ?? categoryKey;

            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = GetCategoryLabel(obj, categoryKey, displayName),
                Data = obj,
                IndentLevel = indent
            };

            // Check if this is a single-item category (just show inline)
            if (IsSingleItemCategory(obj, categoryKey))
            {
                string content = GetSimplifiedCategoryContent(obj, categoryKey);
                if (!string.IsNullOrEmpty(content))
                {
                    item.Label = $"{displayName}: {content}";
                }
                else
                {
                    item.Label = displayName;
                }
                item.IsExpandable = false;
                return item;
            }

            // Use the handler type from the registry to determine behavior
            switch (categoryInfo.Handler)
            {
                case TabHandlerType.Action:
                    // Actionable category (Bills, Storage, etc.) - opens separate menu
                    item.IsExpandable = false;
                    item.OnActivate = () => ExecuteCategoryAction(obj, categoryKey);
                    break;

                case TabHandlerType.RichNavigation:
                    // Rich navigation with sub-items (Health, Gear, Skills, etc.)
                    if (IsExpandableCategory(obj, categoryKey))
                    {
                        item.IsExpandable = true;
                        item.IsExpanded = false;
                        item.OnActivate = () => BuildCategoryChildren(item, obj, categoryKey, mode);
                    }
                    else
                    {
                        // Fallback to detailed info display
                        item.IsExpandable = true;
                        item.IsExpanded = false;
                        item.OnActivate = () => BuildDetailedInfoChildren(item, obj, categoryKey);
                    }
                    break;

                case TabHandlerType.BasicInspectString:
                    // Basic fallback - show GetInspectString content or tab info
                    item.IsExpandable = true;
                    item.IsExpanded = false;
                    if (categoryInfo.Tab != null)
                    {
                        // This is an actual game tab - use dynamic tab info
                        item.OnActivate = () => BuildDynamicTabChildren(item, obj, categoryInfo);
                    }
                    else
                    {
                        // Synthetic category - use existing detailed info
                        item.OnActivate = () => BuildDetailedInfoChildren(item, obj, categoryKey);
                    }
                    break;

                default:
                    // Default behavior: show detailed info when expanded
                    item.IsExpandable = true;
                    item.IsExpanded = false;
                    item.OnActivate = () => BuildDetailedInfoChildren(item, obj, categoryKey);
                    break;
            }

            return item;
        }

        /// <summary>
        /// Builds children for a dynamic tab (tabs discovered from the game but not explicitly supported).
        /// Uses GetInspectString as fallback content.
        /// </summary>
        private static void BuildDynamicTabChildren(InspectionTreeItem parentItem, object obj, TabCategoryInfo categoryInfo)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            // Defensive null checks
            if (categoryInfo == null || categoryInfo.Tab == null || !(obj is Thing thing))
            {
                // Fallback to simple message
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "No information available for this tab.",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                });
                return;
            }

            // Get fallback info from the tab
            string info = TabRegistry.GetFallbackInfo(thing, categoryInfo.Tab);

            if (string.IsNullOrEmpty(info) || info == "No information available.")
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = $"Tab '{categoryInfo.Name}' has no keyboard-accessible content.",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                });

                // Add a hint if tab is known but not rich-supported
                if (!categoryInfo.IsKnown)
                {
                    AddChild(parentItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = "This is an unrecognized tab from a mod or DLC.",
                        IndentLevel = parentItem.IndentLevel + 1,
                        IsExpandable = false
                    });
                }
                return;
            }

            // Strip tags and split into lines
            info = info.StripTags();
            var lines = info.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = line.Trim(),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                });
            }
        }

        /// <summary>
        /// Gets the label for a category, potentially with additional info.
        /// </summary>
        /// <param name="obj">The object being inspected</param>
        /// <param name="categoryKey">English category name for logic comparisons</param>
        /// <param name="displayName">Translated category name for display (defaults to categoryKey if not provided)</param>
        private static string GetCategoryLabel(object obj, string categoryKey, string displayName = null)
        {
            displayName = displayName ?? categoryKey;

            // Special handling for Mood category to show percentage and descriptor
            if (categoryKey == "Mood" && obj is Pawn pawn && pawn.needs?.mood != null)
            {
                float moodPercentage = pawn.needs.mood.CurLevelPercentage * 100f;
                string moodDescriptor = pawn.needs.mood.MoodString;
                return $"{displayName}: {moodPercentage:F0}% ({moodDescriptor})";
            }

            // Special handling for Job Queue category to show count
            if (categoryKey == "Job Queue" && obj is Pawn jobPawn && jobPawn.jobs?.jobQueue != null)
            {
                int queueCount = jobPawn.jobs.jobQueue.Count;
                return $"{displayName} ({queueCount} queued)";
            }

            return displayName;
        }

        /// <summary>
        /// Builds a tree item for a category.
        /// </summary>
        private static InspectionTreeItem BuildCategoryItem(object obj, string category, int indent, InspectionMode mode)
        {
            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = GetCategoryLabel(obj, category),
                Data = obj,
                IndentLevel = indent
            };

            // Check if this is a single-item category (just show inline)
            if (IsSingleItemCategory(obj, category))
            {
                // Get simplified content for inline display
                string content = GetSimplifiedCategoryContent(obj, category);
                if (!string.IsNullOrEmpty(content))
                {
                    item.Label = $"{category}: {content}";
                }
                else
                {
                    item.Label = category;
                }
                item.IsExpandable = false;
            }
            else if (IsActionableCategory(obj, category))
            {
                // This is an actionable category (Bills, Storage, etc.)
                // Note: In ReadOnly mode, actionable categories are filtered out at the parent level
                item.IsExpandable = false;
                item.OnActivate = () => ExecuteCategoryAction(obj, category);
            }
            else if (IsExpandableCategory(obj, category))
            {
                // This category has sub-items (Gear, Skills, etc.)
                item.IsExpandable = true;
                item.IsExpanded = false;
                item.OnActivate = () => BuildCategoryChildren(item, obj, category, mode);
            }
            else
            {
                // Default: show detailed info when expanded
                item.IsExpandable = true;
                item.IsExpanded = false;
                item.OnActivate = () => BuildDetailedInfoChildren(item, obj, category);
            }

            return item;
        }

        /// <summary>
        /// Checks if a category is a single-item category (should be shown inline).
        /// </summary>
        private static bool IsSingleItemCategory(object obj, string category)
        {
            // Categories that just show simple text inline
            return category == "Overview" ||
                   category == "Work Priorities" ||
                   category == "Power";
        }

        /// <summary>
        /// Gets simplified content for inline display of single-item categories.
        /// </summary>
        private static string GetSimplifiedCategoryContent(object obj, string category)
        {
            // Get the full content
            string content = InspectionInfoHelper.GetCategoryInfo(obj, category);

            if (string.IsNullOrEmpty(content))
                return null;

            // Strip XML tags
            content = content.StripTags();

            // Flatten to single line
            content = content.Replace("\n", " ").Replace("\r", "").Trim();

            // Remove the pawn name if it's at the start (already shown in object label)
            if (obj is Pawn pawn)
            {
                string pawnName = pawn.LabelCap.StripTags();
                if (content.StartsWith(pawnName))
                {
                    content = content.Substring(pawnName.Length).Trim();
                }

                // Reformat age display to add label for chronological age
                // Pattern: "age 33 (63)" -> "age 33, chronological age: 63"
                var agePattern = new System.Text.RegularExpressions.Regex(@"age (\d+) \((\d+)\)");
                content = agePattern.Replace(content, "age $1, chronological age: $2");
            }
            else if (obj is Building building)
            {
                string buildingName = building.LabelCap.StripTags();
                if (content.StartsWith(buildingName))
                {
                    content = content.Substring(buildingName.Length).Trim();
                }
            }

            return content;
        }

        /// <summary>
        /// Checks if a category is actionable (opens a separate menu).
        /// </summary>
        private static bool IsActionableCategory(object obj, string category)
        {
            // Check for pawn-specific actionable categories
            if (obj is Pawn pawn)
            {
                return category == "Prisoner" && (pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony);
            }

            // Check for building-specific actionable categories
            if (obj is Building building)
            {
                return (category == "Bills" && building is IBillGiver) ||
                       (category == "Bed Assignment" && building is Building_Bed) ||
                       (category == "Temperature" && building.TryGetComp<CompTempControl>() != null) ||
                       (category == "Storage" && building is IStoreSettingsParent) ||
                       (category == "Shells" && building is Building_TurretGun) ||
                       (category == "Plant Selection" && building is IPlantToGrowSettable) ||
                       (category == "Pen Animals" && building.TryGetComp<CompAnimalPenMarker>() != null) ||
                       (category == "Pen Auto-Cut" && building.TryGetComp<CompAnimalPenMarker>() != null) ||
                       BuildingComponentsHelper.GetDiscoverableComponents(building).Any(c => c.CategoryName == category && !c.IsReadOnly);
            }

            // Check for zone-specific actionable categories
            if (obj is Zone zone)
            {
                return (category == "Storage" && zone is IStoreSettingsParent)
                    || category == "Rename"
                    || (category == "Fishing" && zone.GetType().Name == "Zone_Fishing");
            }

            return false;
        }

        /// <summary>
        /// Checks if a category has expandable sub-items.
        /// </summary>
        private static bool IsExpandableCategory(object obj, string category)
        {
            if (category == "Gear" ||
                category == "Skills" ||
                category == "Health" ||
                category == "Needs" ||
                category == "Mood" ||
                category == "Social" ||
                category == "Training" ||
                category == "Character" ||
                category == "Log" ||
                category == "Job Queue")
                return true;

            // Genes tab is expandable for pawns with gene data (Biotech DLC)
            if (category == "Genes")
            {
                Pawn genePawn = GetPawnFromThing(obj);
                return genePawn?.genes != null && ModsConfig.BiotechActive;
            }

            // Pen Food is expandable if building has pen marker
            if (category == "Pen Food" && obj is Building building)
                return building.TryGetComp<CompAnimalPenMarker>() != null;

            return false;
        }

        /// <summary>
        /// Executes the action for an actionable category.
        /// </summary>
        private static void ExecuteCategoryAction(object obj, string category)
        {
            // Handle pawn-specific actions
            if (obj is Pawn pawn)
            {
                if (category == "Prisoner" && (pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony))
                {
                    WindowlessInspectionState.Close();
                    PrisonerTabState.Open(pawn);
                    return;
                }
            }

            // Handle zone-specific actions
            if (obj is Zone zone)
            {
                if (category == "Rename")
                {
                    WindowlessInspectionState.Close();
                    ZoneRenameState.Open(zone);
                    return;
                }

                if (category == "Storage" && zone is IStoreSettingsParent zoneStorageParent)
                {
                    var settings = zoneStorageParent.GetStoreSettings();
                    if (settings != null)
                    {
                        WindowlessInspectionState.Close();
                        StorageSettingsMenuState.Open(settings);
                    }
                    return;
                }

                if (category == "Fishing" && zone.GetType().Name == "Zone_Fishing")
                {
                    WindowlessInspectionState.Close();
                    FishingZoneMenuState.Open(zone);
                    return;
                }
            }

            // Handle storage for IStoreSettingsParent things that aren't Buildings or Zones (e.g., Blueprint_Storage)
            if (category == "Storage" && obj is IStoreSettingsParent storeParent && !(obj is Building) && !(obj is Zone))
            {
                var settings = storeParent.GetStoreSettings();
                if (settings != null)
                {
                    WindowlessInspectionState.Close();
                    StorageSettingsMenuState.Open(settings);
                }
                return;
            }

            // Handle building-specific actions
            if (!(obj is Building building))
                return;

            WindowlessInspectionState.Close();

            if (category == "Bills" && building is IBillGiver billGiver)
            {
                BillsMenuState.Open(billGiver, building.Position);
            }
            else if (category == "Bed Assignment" && building is Building_Bed bed)
            {
                BedAssignmentState.Open(bed);
            }
            else if (category == "Temperature")
            {
                var tempControl = building.TryGetComp<CompTempControl>();
                if (tempControl != null)
                {
                    TempControlMenuState.Open(building);
                }
            }
            else if (category == "Storage" && building is IStoreSettingsParent storageParent)
            {
                var settings = storageParent.GetStoreSettings();
                if (settings != null)
                {
                    StorageSettingsMenuState.Open(settings);
                }
            }
            else if (category == "Shells" && building is Building_TurretGun turretGun)
            {
                var shellComp = turretGun.gun?.TryGetComp<CompChangeableProjectile>();
                if (shellComp != null)
                {
                    var settings = shellComp.GetStoreSettings();
                    var parentSettings = shellComp.GetParentStoreSettings();
                    if (settings != null)
                    {
                        ThingFilterMenuState.Open(settings.filter, parentSettings?.filter, "Ammunition");
                    }
                }
            }
            else if (category == "Plant Selection" && building is IPlantToGrowSettable plantGrower)
            {
                PlantSelectionMenuState.Open(plantGrower);
            }
            else if (category == "Pen Animals")
            {
                var penMarker = building.TryGetComp<CompAnimalPenMarker>();
                if (penMarker != null)
                {
                    ThingFilterMenuState.Open(penMarker.AnimalFilter, AnimalPenUtility.GetFixedAnimalFilter(), "Pen Animals");
                }
            }
            else if (category == "Pen Auto-Cut")
            {
                var penMarker = building.TryGetComp<CompAnimalPenMarker>();
                if (penMarker != null)
                {
                    var fixedFilter = penMarker.parent.Map?.animalPenManager?.GetFixedAutoCutFilter();
                    ThingFilterMenuState.Open(penMarker.AutoCutFilter, fixedFilter, "Pen Auto-Cut");
                }
            }
            else
            {
                // Check if this is a dynamically discovered component category
                var component = BuildingComponentsHelper.GetComponentByType(building, "CompFlickable");
                if (component != null && component.CategoryName == category)
                {
                    FlickableComponentState.Open(building);
                    return;
                }

                component = BuildingComponentsHelper.GetComponentByType(building, "CompRefuelable");
                if (component != null && component.CategoryName == category)
                {
                    RefuelableComponentState.Open(building);
                    return;
                }

                component = BuildingComponentsHelper.GetComponentByType(building, "CompBreakdownable");
                if (component != null && component.CategoryName == category)
                {
                    BreakdownableComponentState.Open(building);
                    return;
                }
                component = BuildingComponentsHelper.GetComponentByType(building, "Building_Door");
                if (component != null && component.CategoryName == category)
                {
                    DoorControlState.Open(building);
                    return;
                }
                component = BuildingComponentsHelper.GetComponentByType(building, "CompForbiddable");
                if (component != null && component.CategoryName == category)
                {
                    ForbidControlState.Open(building);
                    return;
                }

            }
        }

        /// <summary>
        /// Builds children for expandable categories (Gear, Skills, etc.).
        /// </summary>
        private static void BuildCategoryChildren(InspectionTreeItem categoryItem, object obj, string category, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0)
                return; // Already built

            // Handle Building-specific categories
            if (obj is Building building)
            {
                if (category == "Pen Food")
                {
                    BuildPenFoodChildren(categoryItem, building);
                    return;
                }
            }

            // Handle Pawn-specific categories (supports both live pawns and corpses)
            Pawn pawn = GetPawnFromThing(obj);
            if (pawn == null)
                return;

            if (category == "Gear")
            {
                BuildGearChildren(categoryItem, pawn, mode);
            }
            else if (category == "Skills")
            {
                BuildSkillsChildren(categoryItem, pawn);
            }
            else if (category == "Health")
            {
                BuildHealthChildren(categoryItem, pawn, mode);
            }
            else if (category == "Needs")
            {
                BuildDetailedInfoChildren(categoryItem, obj, category);
            }
            else if (category == "Mood")
            {
                BuildMoodChildren(categoryItem, pawn);
            }
            else if (category == "Social")
            {
                BuildSocialChildren(categoryItem, pawn, mode);
            }
            else if (category == "Training")
            {
                BuildDetailedInfoChildren(categoryItem, obj, category);
            }
            else if (category == "Character")
            {
                BuildDetailedInfoChildren(categoryItem, obj, category);
            }
            else if (category == "Log")
            {
                BuildLogChildren(categoryItem, pawn);
            }
            else if (category == "Job Queue")
            {
                BuildJobQueueChildren(categoryItem, pawn, mode);
            }
            else if (category == "Genes")
            {
                BuildGenesChildren(categoryItem, pawn);
            }
        }

        /// <summary>
        /// Builds children for Job Queue category.
        /// Shows current job and all queued jobs with delete capability.
        /// </summary>
        private static void BuildJobQueueChildren(InspectionTreeItem parentItem, Pawn pawn, InspectionMode mode)
        {
            if (pawn.jobs == null)
                return;

            var jobTracker = pawn.jobs;
            int indent = parentItem.IndentLevel + 1;

            // Add current job (not deletable)
            if (jobTracker.curJob != null)
            {
                string currentJobReport = "Idle";
                try
                {
                    currentJobReport = jobTracker.curJob.GetReport(pawn)?.CapitalizeFirst() ?? "Unknown job";
                }
                catch
                {
                    currentJobReport = jobTracker.curJob.def?.label?.CapitalizeFirst() ?? "Unknown job";
                }

                var currentItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = $"Current: {currentJobReport}",
                    Data = jobTracker.curJob,
                    IndentLevel = indent,
                    IsExpandable = false
                };
                parentItem.Children.Add(currentItem);
            }
            else
            {
                var idleItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "Current: Idle",
                    IndentLevel = indent,
                    IsExpandable = false
                };
                parentItem.Children.Add(idleItem);
            }

            // Add queued jobs (deletable in Full mode only)
            var jobQueue = jobTracker.jobQueue;
            if (jobQueue != null && jobQueue.Count > 0)
            {
                int queueIndex = 1;
                foreach (var queuedJob in jobQueue)
                {
                    if (queuedJob?.job == null)
                        continue;

                    string jobReport;
                    try
                    {
                        jobReport = queuedJob.job.GetReport(pawn)?.CapitalizeFirst() ?? "Unknown job";
                    }
                    catch
                    {
                        jobReport = queuedJob.job.def?.label?.CapitalizeFirst() ?? "Unknown job";
                    }

                    var queuedItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = $"Queued {queueIndex}: {jobReport}",
                        Data = queuedJob,
                        IndentLevel = indent,
                        IsExpandable = false
                    };

                    // Only add delete action in Full mode
                    if (mode == InspectionMode.Full)
                    {
                        // Capture the job for the closure
                        var jobToCancel = queuedJob.job;
                        var jobLabel = jobReport;
                        queuedItem.OnDelete = () =>
                        {
                            // Cancel the queued job
                            jobQueue.Extract(jobToCancel);
                            TolkHelper.Speak($"Cancelled: {jobLabel}", SpeechPriority.High);

                            // Rebuild the parent to reflect the change
                            parentItem.Children.Clear();
                            BuildJobQueueChildren(parentItem, pawn, mode);
                            WindowlessInspectionState.RebuildAfterAction();
                        };
                    }

                    parentItem.Children.Add(queuedItem);
                    queueIndex++;
                }
            }
        }

        /// <summary>
        /// Builds children for Gear category.
        /// </summary>
        private static void BuildGearChildren(InspectionTreeItem parentItem, Pawn pawn, InspectionMode mode)
        {
            var gearCategories = new[] { "Equipment", "Apparel", "Inventory" };

            foreach (var gearCat in gearCategories)
            {
                var gearItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = gearCat,
                    Data = pawn,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };

                gearItem.OnActivate = () => BuildGearItemsChildren(gearItem, pawn, gearCat, mode);
                AddChild(parentItem, gearItem);
            }
        }

        /// <summary>
        /// Builds children for a specific gear category (Equipment/Apparel/Inventory).
        /// </summary>
        private static void BuildGearItemsChildren(InspectionTreeItem gearCatItem, Pawn pawn, string gearCategory, InspectionMode mode)
        {
            if (gearCatItem.Children.Count > 0)
                return; // Already built

            List<InteractiveGearHelper.GearItem> items = null;

            switch (gearCategory)
            {
                case "Equipment":
                    items = InteractiveGearHelper.GetEquipmentItems(pawn);
                    break;
                case "Apparel":
                    items = InteractiveGearHelper.GetApparelItems(pawn);
                    break;
                case "Inventory":
                    items = InteractiveGearHelper.GetInventoryItems(pawn);
                    break;
            }

            if (items == null || items.Count == 0)
                return;

            foreach (var gearItem in items)
            {
                var item = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = gearItem.Label,
                    Data = gearItem,
                    IndentLevel = gearCatItem.IndentLevel + 1,
                    // In ReadOnly mode, gear items are not expandable (no actions)
                    IsExpandable = mode == InspectionMode.Full,
                    IsExpanded = false
                };

                // Only add action activation in Full mode
                if (mode == InspectionMode.Full)
                {
                    item.OnActivate = () => BuildGearActionChildren(item, pawn, gearItem);
                }

                AddChild(gearCatItem, item);
            }
        }

        /// <summary>
        /// Builds action children for a gear item.
        /// </summary>
        private static void BuildGearActionChildren(InspectionTreeItem gearItem, Pawn pawn, InteractiveGearHelper.GearItem gear)
        {
            if (gearItem.Children.Count > 0)
                return; // Already built

            var actions = InteractiveGearHelper.GetAvailableActions(gear, pawn);

            foreach (var action in actions)
            {
                var actionItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = action,
                    Data = new { Pawn = pawn, Gear = gear, Action = action },
                    IndentLevel = gearItem.IndentLevel + 1,
                    IsExpandable = false
                };

                actionItem.OnActivate = () => ExecuteGearAction(pawn, gear, action);
                AddChild(gearItem, actionItem);
            }
        }

        /// <summary>
        /// Executes a gear action.
        /// </summary>
        private static void ExecuteGearAction(Pawn pawn, InteractiveGearHelper.GearItem gear, string action)
        {
            bool success = false;

            switch (action)
            {
                case "Drop":
                    success = InteractiveGearHelper.ExecuteDropAction(gear, pawn);
                    if (success)
                    {
                        // Rebuild tree to reflect changes
                        WindowlessInspectionState.RebuildTree();
                    }
                    break;
                case "Consume":
                    success = InteractiveGearHelper.ExecuteConsumeAction(gear, pawn);
                    if (success)
                    {
                        // Rebuild tree to reflect changes
                        WindowlessInspectionState.RebuildTree();
                    }
                    break;
                case "View Info":
                    // Close current inspection menu and open new one for the item
                    // Pass the pawn as parent so Escape returns to the pawn's inspection
                    WindowlessInspectionState.Close();
                    WindowlessInspectionState.OpenForObject(gear.Thing, pawn);
                    break;
            }
        }

        /// <summary>
        /// Builds children for Skills category.
        /// </summary>
        private static void BuildSkillsChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (pawn.skills?.skills == null)
                return;

            var skills = pawn.skills.skills.OrderByDescending(s => s.Level).ToList();

            foreach (var skill in skills)
            {
                string passionText = skill.passion == Passion.None ? "" : $" ({skill.passion})";
                string disabledText = skill.TotallyDisabled ? " [DISABLED]" : "";

                var skillItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = $"{skill.def.skillLabel}: Level {skill.Level}{passionText}{disabledText}",
                    Data = skill,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };

                skillItem.OnActivate = () => BuildSkillDetailChildren(skillItem, skill);
                AddChild(parentItem, skillItem);
            }
        }

        /// <summary>
        /// Builds detail children for a skill.
        /// </summary>
        private static void BuildSkillDetailChildren(InspectionTreeItem skillItem, SkillRecord skill)
        {
            if (skillItem.Children.Count > 0)
                return; // Already built

            var sb = new StringBuilder();
            sb.Append($"XP: {skill.xpSinceLastLevel:F0} / {skill.XpRequiredForLevelUp:F0}");

            if (skill.passion != Passion.None)
            {
                sb.Append($", Passion: {skill.passion}");
            }

            if (skill.TotallyDisabled)
            {
                sb.Append(", Status: DISABLED");
            }

            if (!string.IsNullOrEmpty(skill.def.description))
            {
                sb.Append($". {skill.def.description}");
            }

            var detailItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = sb.ToString(),
                IndentLevel = skillItem.IndentLevel + 1,
                IsExpandable = false
            };

            AddChild(skillItem, detailItem);
        }

        /// <summary>
        /// Builds children for Social category.
        /// </summary>
        private static void BuildSocialChildren(InspectionTreeItem parentItem, Pawn pawn, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            // Add Relations as expandable item
            var relationsItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = "Relations",
                Data = pawn,
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false
            };
            relationsItem.OnActivate = () => BuildSocialRelationsChildren(relationsItem, pawn);
            AddChild(parentItem, relationsItem);

            // Add Ideology if applicable
            if (ModsConfig.IdeologyActive && pawn.ideo != null)
            {
                var ideologyItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = "Ideology & Role",
                    Data = pawn,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };
                ideologyItem.OnActivate = () => BuildIdeologyChildren(ideologyItem, pawn);
                AddChild(parentItem, ideologyItem);
            }

            // Add Try Romance if applicable (Biotech DLC, eligible pawn, full inspection mode)
            if (mode != InspectionMode.ReadOnly && SocialTabHelper.CanTryRomance(pawn))
            {
                BuildRomanceMenu(parentItem, pawn);
            }
        }

        /// <summary>
        /// Builds children for Relations sub-category.
        /// </summary>
        private static void BuildSocialRelationsChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            var relations = SocialTabHelper.GetRelations(pawn);

            if (relations.Count == 0)
            {
                var noRelationsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "No relations",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(parentItem, noRelationsItem);
                return;
            }

            foreach (var relation in relations)
            {
                string relationsStr = relation.Relations.Count > 0 ? string.Join(", ", relation.Relations) : "Acquaintance";
                var relationItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = $"{relation.OtherPawnName} ({relationsStr}, opinion: {relation.MyOpinion:+0;-0;0})",
                    Data = relation,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };
                relationItem.OnActivate = () => BuildRelationDetailChildren(relationItem, pawn, relation);
                AddChild(parentItem, relationItem);
            }
        }

        /// <summary>
        /// Builds detail children for a specific relation.
        /// </summary>
        private static void BuildRelationDetailChildren(InspectionTreeItem relationItem, Pawn inspectedPawn, SocialTabHelper.RelationInfo relation)
        {
            if (relationItem.Children.Count > 0)
                return; // Already built

            string detailedInfo = relation.DetailedInfo.StripTags();
            var lines = detailedInfo.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            bool pregnancyApproachInserted = false;

            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var detailItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = line.Trim(),
                    IndentLevel = relationItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(relationItem, detailItem);

                // Insert pregnancy approach right after the Relationship line
                if (!pregnancyApproachInserted && line.TrimStart().StartsWith("Relationship:")
                    && relation.CanChangePregnancyApproach && ModsConfig.BiotechActive)
                {
                    BuildPregnancyApproachMenu(relationItem, inspectedPawn, relation);
                    pregnancyApproachInserted = true;
                }
            }

            // Fallback: if no Relationship line was found, still add pregnancy approach at end
            if (!pregnancyApproachInserted && relation.CanChangePregnancyApproach && ModsConfig.BiotechActive)
            {
                BuildPregnancyApproachMenu(relationItem, inspectedPawn, relation);
            }
        }

        /// <summary>
        /// Builds a pregnancy approach sub-menu within a relation's detail children.
        /// </summary>
        private static void BuildPregnancyApproachMenu(InspectionTreeItem parentItem, Pawn pawn, SocialTabHelper.RelationInfo relation)
        {
            var currentApproach = relation.CurrentPregnancyApproach;

            var approachItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = $"{"PregnancyApproach".Translate()}: {currentApproach.GetLabel().CapitalizeFirst()}",
                Data = relation,
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false
            };

            approachItem.OnActivate = () =>
            {
                if (approachItem.Children.Count > 0)
                    return; // Already built

                int childIndent = approachItem.IndentLevel + 1;

                // Check if pregnancy is possible between these two pawns
                AcceptanceReport canProduce = PregnancyUtility.CanEverProduceChild(pawn, relation.OtherPawn);
                if (!canProduce.Accepted)
                {
                    AddChild(approachItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = $"{"PregnancyNotPossible".Translate()}: {canProduce.Reason.CapitalizeFirst()}",
                        IndentLevel = childIndent,
                        IsExpandable = false
                    });
                    return;
                }

                foreach (PregnancyApproach approach in Enum.GetValues(typeof(PregnancyApproach)))
                {
                    bool isCurrent = approach == relation.CurrentPregnancyApproach;
                    string optionLabel = isCurrent
                        ? $"{"Current".Translate()}: {approach.GetDescription()}"
                        : approach.GetDescription();

                    var optionItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Action,
                        Label = optionLabel,
                        IndentLevel = childIndent,
                        IsExpandable = false
                    };

                    if (!isCurrent)
                    {
                        var capturedApproach = approach;
                        optionItem.OnActivate = () =>
                        {
                            SocialTabHelper.SetPregnancyApproach(pawn, relation.OtherPawn, capturedApproach);
                            relation.CurrentPregnancyApproach = capturedApproach;
                            approachItem.Children.Clear();
                            approachItem.IsExpanded = false;
                            approachItem.Label = $"{"PregnancyApproach".Translate()}: {capturedApproach.GetLabel().CapitalizeFirst()}";
                        };
                    }

                    AddChild(approachItem, optionItem);
                }
            };

            AddChild(parentItem, approachItem);
        }

        /// <summary>
        /// Builds the Try Romance sub-menu within the Social category.
        /// Shows romance targets with success chance, gated behind Biotech DLC and pawn eligibility.
        /// Follows the same lazy-loading pattern as BuildPregnancyApproachMenu.
        /// </summary>
        private static void BuildRomanceMenu(InspectionTreeItem parentItem, Pawn pawn)
        {
            string romanceLabel = "TryRomanceButtonLabel".Translate();
            int childIndent = parentItem.IndentLevel + 1;

            // Check cooldown first
            if (SocialTabHelper.IsRomanceOnCooldown(pawn, out string cooldownText))
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = $"{romanceLabel}: {cooldownText}",
                    IndentLevel = childIndent,
                    IsExpandable = false
                });
                return;
            }

            // Check initiator eligibility
            var eligibility = SocialTabHelper.GetRomanceInitiatorEligibility(pawn);
            if (!eligibility.Accepted)
            {
                if (!eligibility.Reason.NullOrEmpty())
                {
                    AddChild(parentItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = $"{romanceLabel}: {eligibility.Reason}",
                        IndentLevel = childIndent,
                        IsExpandable = false
                    });
                }
                return;
            }

            // Eligible: create expandable SubCategory with lazy-loaded targets
            var romanceItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = romanceLabel,
                Data = pawn,
                IndentLevel = childIndent,
                IsExpandable = true,
                IsExpanded = false
            };

            romanceItem.OnActivate = () =>
            {
                if (romanceItem.Children.Count > 0)
                    return; // Already built

                var targets = SocialTabHelper.GetRomanceTargets(pawn);

                if (targets.Count == 0)
                {
                    AddChild(romanceItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = "TryRomanceNoOptsMessage".Translate(pawn),
                        IndentLevel = romanceItem.IndentLevel + 1,
                        IsExpandable = false
                    });
                    return;
                }

                int targetIndent = romanceItem.IndentLevel + 1;

                foreach (var target in targets)
                {
                    if (target.IsViable)
                    {
                        string targetLabel = string.Format("{0} ({1} {2})",
                            target.TargetName,
                            target.Chance.ToStringPercent(),
                            "chance".Translate());

                        var capturedTarget = target;
                        var targetItem = new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.Action,
                            Label = targetLabel,
                            Data = target.Target,
                            IndentLevel = targetIndent,
                            IsExpandable = false
                        };

                        targetItem.OnActivate = () =>
                        {
                            if (SocialTabHelper.InitiateRomance(pawn, capturedTarget.Target))
                            {
                                TolkHelper.Speak($"{pawn.LabelShort} will try to romance {capturedTarget.TargetName}");
                            }
                            else
                            {
                                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            }
                        };

                        targetItem.OnInfo = () =>
                        {
                            string breakdown = SocialTabHelper.BuildRomanceBreakdown(
                                pawn, capturedTarget.Target);
                            StatBreakdownState.Open(
                                $"{capturedTarget.TargetName} - {"RomanceChance".Translate()}: {capturedTarget.Chance.ToStringPercent()}",
                                breakdown);
                        };

                        AddChild(romanceItem, targetItem);
                    }
                    else
                    {
                        AddChild(romanceItem, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = $"{target.TargetName} ({target.Reason})",
                            IndentLevel = targetIndent,
                            IsExpandable = false
                        });
                    }
                }
            };

            AddChild(parentItem, romanceItem);
        }

        /// <summary>
        /// Builds children for Ideology sub-category.
        /// </summary>
        private static void BuildIdeologyChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            var ideologyInfo = SocialTabHelper.GetIdeologyInfo(pawn);
            if (ideologyInfo == null)
            {
                var noIdeologyItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "No ideology information available",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(parentItem, noIdeologyItem);
                return;
            }

            int childIndent = parentItem.IndentLevel + 1;

            // Add ideology name
            AddChild(parentItem, new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = $"Ideology: {ideologyInfo.IdeoName}",
                IndentLevel = childIndent,
                IsExpandable = false
            });

            // Add combined certainty with change rate (matches game tooltip format)
            string certaintyText = "Certainty".Translate().CapitalizeFirst();
            string certaintyLabel = $"{certaintyText}: {ideologyInfo.Certainty:P0}";
            float changePerDay = pawn.ideo.CertaintyChangePerDay;
            if (Math.Abs(changePerDay) > 0.001f)
            {
                string rateText = changePerDay.ToStringPercent();
                if (changePerDay > 0) rateText = "+" + rateText;
                certaintyLabel += $" ({"CertaintyChangePerDay".Translate()}: {rateText})";
            }
            AddChild(parentItem, new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = certaintyLabel,
                IndentLevel = childIndent,
                IsExpandable = false
            });

            // Add Roles expandable section with assign/unassign actions
            var availableRoles = SocialTabHelper.GetAvailableRoles(pawn);
            if (availableRoles.Count > 0)
            {
                var rolesItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = "IdeoRoles".Translate().CapitalizeFirst(),
                    Data = pawn,
                    IndentLevel = childIndent,
                    IsExpandable = true,
                    IsExpanded = false
                };
                rolesItem.OnActivate = () => BuildRolesChildren(rolesItem, pawn, availableRoles);
                AddChild(parentItem, rolesItem);
            }
        }

        /// <summary>
        /// Builds children for the Roles section under Ideology.
        /// Lists each active role with its current holder and assign/unassign actions.
        /// </summary>
        private static void BuildRolesChildren(InspectionTreeItem parentItem, Pawn pawn, List<Precept_Role> roles)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            int childIndent = parentItem.IndentLevel + 1;

            foreach (var role in roles)
            {
                Pawn currentHolder = role.ChosenPawnSingle();
                string holderName = currentHolder != null ? currentHolder.LabelShort.StripTags() : (string)"NoRoleAssigned".Translate();
                string roleLabel = $"{role.LabelCap}: {holderName}";

                var roleItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = roleLabel,
                    Data = role,
                    IndentLevel = childIndent,
                    IsExpandable = true,
                    IsExpanded = false
                };

                var capturedRole = role;
                roleItem.OnActivate = () => BuildRoleDetailChildren(roleItem, pawn, capturedRole);
                AddChild(parentItem, roleItem);
            }
        }

        /// <summary>
        /// Builds detail children for a specific role, including assign/unassign actions.
        /// </summary>
        private static void BuildRoleDetailChildren(InspectionTreeItem roleItem, Pawn pawn, Precept_Role role)
        {
            if (roleItem.Children.Count > 0)
                return; // Already built

            int childIndent = roleItem.IndentLevel + 1;
            bool pawnHoldsRole = role.IsAssigned(pawn);
            bool pawnIsEligible = SocialTabHelper.IsEligibleForRole(role, pawn);

            // Add Assign action if pawn is eligible and not already assigned
            if (!pawnHoldsRole && pawnIsEligible)
            {
                var assignItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = $"Assign {pawn.LabelShort.StripTags()}",
                    IndentLevel = childIndent,
                    IsExpandable = false
                };
                assignItem.OnActivate = () =>
                {
                    SocialTabHelper.AssignRole(role, pawn);
                    // Rebuild: clear children so they refresh with updated holder
                    roleItem.Children.Clear();
                    roleItem.IsExpanded = false;
                    // Update the role label
                    roleItem.Label = $"{role.LabelCap}: {pawn.LabelShort.StripTags()}";
                };
                AddChild(roleItem, assignItem);
            }

            // Add Unassign action if pawn holds this role
            if (pawnHoldsRole)
            {
                var unassignItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = $"Unassign {pawn.LabelShort.StripTags()}",
                    IndentLevel = childIndent,
                    IsExpandable = false
                };
                unassignItem.OnActivate = () =>
                {
                    SocialTabHelper.UnassignRole(role, pawn);
                    // Rebuild: clear children so they refresh with updated holder
                    roleItem.Children.Clear();
                    roleItem.IsExpanded = false;
                    // Update the role label
                    roleItem.Label = $"{role.LabelCap}: {"NoRoleAssigned".Translate()}";
                };
                AddChild(roleItem, unassignItem);
            }

            // Show why pawn can't be assigned if not eligible
            if (!pawnHoldsRole && !pawnIsEligible)
            {
                var unmetReq = role.GetFirstUnmetRequirement(pawn);
                string reason = unmetReq != null
                    ? $"Cannot assign: {unmetReq.GetLabelCap(role).StripTags()}"
                    : "Cannot assign: requirements not met";
                AddChild(roleItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = reason,
                    IndentLevel = childIndent,
                    IsExpandable = false
                });
            }

            // Add role description
            if (!string.IsNullOrEmpty(role.def.description))
            {
                AddChild(roleItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = role.def.description.StripTags(),
                    IndentLevel = childIndent,
                    IsExpandable = false
                });
            }

            // Add role requirements
            if (role.def.roleRequirements != null && role.def.roleRequirements.Count > 0)
            {
                var reqsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = "Requirements",
                    IndentLevel = childIndent,
                    IsExpandable = true,
                    IsExpanded = false
                };
                reqsItem.OnActivate = () =>
                {
                    if (reqsItem.Children.Count > 0) return;
                    foreach (var req in role.def.roleRequirements)
                    {
                        string reqLabel = req.GetLabelCap(role).StripTags();
                        if (!string.IsNullOrEmpty(reqLabel))
                        {
                            AddChild(reqsItem, new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.DetailText,
                                Label = reqLabel,
                                IndentLevel = reqsItem.IndentLevel + 1,
                                IsExpandable = false
                            });
                        }
                    }
                };
                AddChild(roleItem, reqsItem);
            }

            // Add role effects
            if (role.def.roleEffects != null && role.def.roleEffects.Count > 0)
            {
                var effectsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = "Effects".Translate().CapitalizeFirst(),
                    IndentLevel = childIndent,
                    IsExpandable = true,
                    IsExpanded = false
                };
                effectsItem.OnActivate = () =>
                {
                    if (effectsItem.Children.Count > 0) return;
                    foreach (var effect in role.def.roleEffects)
                    {
                        string effectLabel = effect.Label(pawn, role).StripTags();
                        if (!string.IsNullOrEmpty(effectLabel))
                        {
                            AddChild(effectsItem, new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.DetailText,
                                Label = effectLabel,
                                IndentLevel = effectsItem.IndentLevel + 1,
                                IsExpandable = false
                            });
                        }
                    }
                };
                AddChild(roleItem, effectsItem);
            }
        }

        /// <summary>
        /// Builds children for Genes category using GeneTreeBuilder.
        /// </summary>
        private static void BuildGenesChildren(InspectionTreeItem categoryItem, Pawn pawn)
        {
            if (categoryItem.Children.Count > 0)
                return; // Already built

            if (pawn?.genes == null || !ModsConfig.BiotechActive)
            {
                AddChild(categoryItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "No gene information available",
                    IndentLevel = categoryItem.IndentLevel + 1,
                    IsExpandable = false
                });
                return;
            }

            var geneTree = GeneTreeBuilder.BuildAdultGeneTree(pawn);

            // Copy children from the gene tree root into our category item
            foreach (var child in geneTree.Children)
            {
                child.Parent = categoryItem;
                child.IndentLevel = categoryItem.IndentLevel + 1;
                AdjustChildIndents(child, categoryItem.IndentLevel + 1);
                categoryItem.Children.Add(child);
            }

            // Update category label with xenotype info
            string xenotypeLabel = pawn.genes.XenotypeLabelCap;
            int geneCount = pawn.genes.GenesListForReading?.Count ?? 0;
            categoryItem.Label = $"Genes: {xenotypeLabel} ({geneCount} {(geneCount == 1 ? "gene" : "genes")})";
        }

        /// <summary>
        /// Recursively adjusts indent levels of children relative to a new base indent.
        /// </summary>
        private static void AdjustChildIndents(InspectionTreeItem item, int baseIndent)
        {
            item.IndentLevel = baseIndent;
            foreach (var child in item.Children)
            {
                AdjustChildIndents(child, baseIndent + 1);
            }
        }

        /// <summary>
        /// Builds children for Health category.
        /// </summary>
        private static void BuildHealthChildren(InspectionTreeItem parentItem, Pawn pawn, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            // Add Operations option (Full mode only)
            if (mode == InspectionMode.Full)
            {
                var operationsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = "Operations",
                    Data = pawn,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                operationsItem.OnActivate = () =>
                {
                    WindowlessInspectionState.Close();
                    HealthTabState.OpenOperations(pawn);
                };
                AddChild(parentItem, operationsItem);

                // Add Health Settings option
                var healthSettingsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = "Health Settings",
                    Data = pawn,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                healthSettingsItem.OnActivate = () =>
                {
                    WindowlessInspectionState.Close();
                    HealthTabState.OpenMedicalSettings(pawn);
                };
                AddChild(parentItem, healthSettingsItem);
            }

            // Add overall health state
            var stateItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = $"State: {pawn.health.State}",
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = false
            };
            AddChild(parentItem, stateItem);

            // Add bleeding info if applicable
            if (pawn.health.hediffSet.BleedRateTotal > 0.01f)
            {
                var bleedingItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = $"BLEEDING: {pawn.health.hediffSet.BleedRateTotal:F2} per day",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(parentItem, bleedingItem);
            }

            // Add blood loss level if applicable
            var bloodLoss = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            if (bloodLoss != null)
            {
                var bloodLossItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = $"Blood Loss: {bloodLoss.Severity:P0}",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(parentItem, bloodLossItem);
            }

            // Add pain level if applicable
            float painTotal = pawn.health.hediffSet.PainTotal;
            if (painTotal > 0.01f)
            {
                var painItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = $"Pain: {painTotal:P0}",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(parentItem, painItem);
            }

            // Add Conditions as expandable subcategory
            var hediffs = pawn.health.hediffSet.hediffs;
            if (hediffs != null && hediffs.Count > 0)
            {
                int totalVisible = hediffs.Count(h => h.Visible);
                int afterFiltering = hediffs.Count(h => h.Visible && !IsSurgicallyRemovedPart(h, pawn));
                int filteredCount = totalVisible - afterFiltering;

                string conditionsLabel = $"Conditions ({afterFiltering})";
                if (filteredCount > 0)
                {
                    conditionsLabel += $" ({filteredCount} filtered)";
                }

                var conditionsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = conditionsLabel,
                    Data = pawn,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };
                conditionsItem.OnActivate = () => BuildConditionsChildren(conditionsItem, pawn);
                AddChild(parentItem, conditionsItem);
            }
            else
            {
                var noConditionsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "No injuries or conditions",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(parentItem, noConditionsItem);
            }

            // Add key capacities
            if (pawn.health.capacities != null)
            {
                var capacitiesItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = "Capacities",
                    Data = pawn,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };
                capacitiesItem.OnActivate = () => BuildCapacitiesChildren(capacitiesItem, pawn);
                AddChild(parentItem, capacitiesItem);
            }
        }

        /// <summary>
        /// Builds children for Conditions subcategory, grouping hediffs by body part.
        /// </summary>
        private static void BuildConditionsChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            var hediffs = pawn.health.hediffSet.hediffs
                .Where(h => h.Visible)
                .Where(h => !IsSurgicallyRemovedPart(h, pawn))
                .ToList();

            // Group hediffs by body part (null for whole-body conditions)
            // Sort by: whole-body first, then by part health percentage (most damaged first)
            var hediffsByPart = hediffs
                .GroupBy(h => h.Part)
                .OrderBy(g => g.Key == null ? 0 : 1)
                .ThenBy(g => g.Key != null
                    ? pawn.health.hediffSet.GetPartHealth(g.Key) / g.Key.def.GetMaxHealth(pawn)
                    : 0f);

            foreach (var group in hediffsByPart)
            {
                var part = group.Key;
                var partHediffs = group.ToList();

                // Build label for this body part with health info and condition count
                string label;
                if (part == null)
                {
                    // Whole-body conditions (no specific body part)
                    // Get summary of effects for whole body
                    var effectTypes = new List<string>();
                    bool hasBleeding = false;
                    bool hasCapacityImpact = false;
                    bool hasPain = false;
                    bool hasLifeThreat = false;

                    foreach (var hediff in partHediffs)
                    {
                        if (hediff.Bleeding)
                            hasBleeding = true;
                        if (hediff.PainOffset > 0.01f)
                            hasPain = true;
                        if (hediff.IsCurrentlyLifeThreatening)
                            hasLifeThreat = true;
                        if (hediff.CapMods != null && hediff.CapMods.Count > 0)
                            hasCapacityImpact = true;
                    }

                    if (hasLifeThreat)
                        effectTypes.Add("Life Threatening");
                    if (hasBleeding)
                        effectTypes.Add("Bleeding");
                    if (hasCapacityImpact)
                        effectTypes.Add("Reduced Capacity");
                    if (hasPain)
                        effectTypes.Add("Painful");

                    string effectSummary = effectTypes.Count > 0 ? " : " + string.Join(", ", effectTypes) : "";
                    label = $"Whole body : Conditions: {partHediffs.Count}{effectSummary}";
                }
                else
                {
                    // Get part health
                    float partHealth = pawn.health.hediffSet.GetPartHealth(part);
                    float maxHealth = part.def.GetMaxHealth(pawn);

                    // Get summary of effects for this body part
                    var effectTypes = new List<string>();
                    bool hasBleeding = false;
                    bool hasCapacityImpact = false;
                    bool hasPain = false;
                    bool hasLifeThreat = false;

                    foreach (var hediff in partHediffs)
                    {
                        if (hediff.Bleeding)
                            hasBleeding = true;
                        if (hediff.PainOffset > 0.01f)
                            hasPain = true;
                        if (hediff.IsCurrentlyLifeThreatening)
                            hasLifeThreat = true;
                        if (hediff.CapMods != null && hediff.CapMods.Count > 0)
                            hasCapacityImpact = true;
                    }

                    if (hasLifeThreat)
                        effectTypes.Add("Life Threatening");
                    if (hasBleeding)
                        effectTypes.Add("Bleeding");
                    if (hasCapacityImpact)
                        effectTypes.Add("Reduced Capacity");
                    if (hasPain)
                        effectTypes.Add("Painful");

                    string effectSummary = effectTypes.Count > 0 ? " : " + string.Join(", ", effectTypes) : "";
                    label = $"{part.LabelCap} : Health: {partHealth:F0} / {maxHealth:F0} : Conditions: {partHediffs.Count}{effectSummary}";
                }

                var bodyPartItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = label,
                    Data = new { Pawn = pawn, BodyPart = part, Hediffs = partHediffs },
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };

                bodyPartItem.OnActivate = () => BuildBodyPartConditionsChildren(bodyPartItem, pawn, part, partHediffs);
                AddChild(parentItem, bodyPartItem);
            }
        }

        /// <summary>
        /// Builds children showing individual conditions for a specific body part.
        /// </summary>
        private static void BuildBodyPartConditionsChildren(InspectionTreeItem bodyPartItem, Pawn pawn, BodyPartRecord part, List<Hediff> hediffs)
        {
            if (bodyPartItem.Children.Count > 0)
                return; // Already built

            // Sort hediffs by severity (most severe first)
            var sortedHediffs = hediffs.OrderByDescending(h => h.Severity).ToList();

            foreach (var hediff in sortedHediffs)
            {
                // Get hediff label with inline impacts
                string hediffLabel = hediff.LabelCap.StripTags();
                string impacts = GetHediffImpactsSummary(hediff);
                if (!string.IsNullOrEmpty(impacts))
                {
                    hediffLabel += $". {impacts}";
                }

                // Expandable if TipStringExtra has effects OR Description exists
                bool hasExpandableContent = !string.IsNullOrWhiteSpace(hediff.TipStringExtra)
                                         || !string.IsNullOrWhiteSpace(hediff.Description);

                var hediffItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = hediffLabel,
                    Data = hediff,
                    IndentLevel = bodyPartItem.IndentLevel + 1,
                    IsExpandable = hasExpandableContent,
                    IsExpanded = false
                };

                if (hasExpandableContent)
                {
                    hediffItem.OnActivate = () => BuildHediffDetailChildren(hediffItem, hediff, pawn);
                }
                AddChild(bodyPartItem, hediffItem);
            }
        }

        /// <summary>
        /// Gets a compact summary of hediff impacts for inline display.
        /// </summary>
        private static string GetHediffImpactsSummary(Hediff hediff)
        {
            var impacts = new List<string>();

            // Bleeding
            if (hediff.Bleeding)
            {
                impacts.Add($"Bleeding {hediff.BleedRate:F1}/day");
            }

            // Pain
            float pain = hediff.PainOffset;
            if (pain > 0.01f)
            {
                impacts.Add($"Pain +{pain:P0}");
            }

            // Capacity impacts
            if (hediff.CapMods != null)
            {
                foreach (var capMod in hediff.CapMods)
                {
                    if (capMod.capacity == null)
                        continue;

                    string capName = capMod.capacity.LabelCap.ToString().StripTags();

                    if (capMod.offset != 0f)
                    {
                        string sign = capMod.offset > 0 ? "+" : "";
                        impacts.Add($"{capName} {sign}{capMod.offset:P0}");
                    }
                    else if (capMod.postFactor != 1f)
                    {
                        float percentChange = (capMod.postFactor - 1f) * 100f;
                        string sign = percentChange > 0 ? "+" : "";
                        impacts.Add($"{capName} {sign}{percentChange:F0}%");
                    }
                }
            }

            // Tend status
            var tendComp = hediff.TryGetComp<HediffComp_TendDuration>();
            if (tendComp != null)
            {
                if (tendComp.IsTended)
                {
                    impacts.Add($"Tended {tendComp.tendQuality:P0}");
                }
                else if (hediff.TendableNow())
                {
                    impacts.Add("Needs tending");
                }
            }

            if (impacts.Count == 0)
                return string.Empty;

            return string.Join(", ", impacts);
        }

        /// <summary>
        /// Builds detail children for a specific hediff (condition/wound).
        /// Shows comprehensive effects rather than raw health numbers.
        /// </summary>
        private static void BuildHediffDetailChildren(InspectionTreeItem hediffItem, Hediff hediff, Pawn pawn)
        {
            if (hediffItem.Children.Count > 0)
                return; // Already built

            // Get comprehensive effect information from helper
            string effectsText = HealthTabHelper.GetComprehensiveHediffEffects(hediff, pawn);

            if (!string.IsNullOrEmpty(effectsText))
            {
                // Split effects into individual lines for better navigation
                string[] effectLines = effectsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string line in effectLines)
                {
                    string trimmedLine = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedLine))
                    {
                        var effectItem = new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = trimmedLine,
                            IndentLevel = hediffItem.IndentLevel + 1,
                            IsExpandable = false
                        };
                        AddChild(hediffItem, effectItem);
                    }
                }
            }

            // Add description at the end for context
            string description = hediff.Description;
            if (!string.IsNullOrEmpty(description))
            {
                // Strip tags, replace newlines with spaces, and collapse multiple spaces
                description = description.StripTags().Trim();
                description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ");

                // Add a separator before description
                var separatorItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "---",
                    IndentLevel = hediffItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(hediffItem, separatorItem);

                var descItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = description,
                    IndentLevel = hediffItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(hediffItem, descItem);
            }
        }

        /// <summary>
        /// Builds children for Capacities subcategory.
        /// Uses HealthTabHelper for consistent capacity data with descriptions.
        /// </summary>
        private static void BuildCapacitiesChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            // Use HealthTabHelper for consistent capacity data (already sorted by level)
            var capacities = HealthTabHelper.GetCapacities(pawn);

            foreach (var capacity in capacities)
            {
                var capacityItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = $"{capacity.Label}: {capacity.LevelLabel}",
                    Data = capacity,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };

                capacityItem.OnActivate = () => BuildCapacityDetailChildren(capacityItem, capacity);
                AddChild(parentItem, capacityItem);
            }
        }

        /// <summary>
        /// Builds detail children for a capacity showing description and factors.
        /// </summary>
        private static void BuildCapacityDetailChildren(InspectionTreeItem capacityItem, HealthTabHelper.CapacityInfo capacity)
        {
            if (capacityItem.Children.Count > 0)
                return; // Already built

            // Add description if available
            if (!string.IsNullOrEmpty(capacity.Description))
            {
                var descItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = capacity.Description,
                    IndentLevel = capacityItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(capacityItem, descItem);
            }

            // Add breakdown factors
            if (!string.IsNullOrEmpty(capacity.DetailedBreakdown))
            {
                var lines = capacity.DetailedBreakdown.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine))
                        continue;

                    // Skip header and current level (already in parent label)
                    if (trimmedLine.EndsWith(":") && trimmedLine == $"{capacity.Label}:")
                        continue;
                    if (trimmedLine.StartsWith("Current level:"))
                        continue;

                    var detailItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = trimmedLine,
                        IndentLevel = capacityItem.IndentLevel + 1,
                        IsExpandable = false
                    };
                    AddChild(capacityItem, detailItem);
                }
            }
        }

        /// <summary>
        /// Builds children for Mood category.
        /// </summary>
        private static void BuildMoodChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            if (pawn.needs?.mood == null)
            {
                var noMoodItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "No mood information available",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(parentItem, noMoodItem);
                return;
            }

            Need_Mood mood = pawn.needs.mood;

            // Add Break Thresholds as expandable subcategory if pawn can have mental breaks
            if (pawn.mindState?.mentalBreaker != null &&
                pawn.mindState.mentalBreaker.CanDoRandomMentalBreaks)
            {
                var breakThresholdsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = "Break Thresholds",
                    Data = pawn,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };
                breakThresholdsItem.OnActivate = () => BuildBreakThresholdsChildren(breakThresholdsItem, pawn);
                AddChild(parentItem, breakThresholdsItem);
            }

            // Get thoughts affecting mood
            List<Thought> thoughtGroups = new List<Thought>();
            PawnNeedsUIUtility.GetThoughtGroupsInDisplayOrder(mood, thoughtGroups);

            if (thoughtGroups.Count == 0)
            {
                var noThoughtsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "No thoughts affecting mood",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(parentItem, noThoughtsItem);
            }

            // Process each thought group
            List<Thought> thoughtGroup = new List<Thought>();
            foreach (Thought group in thoughtGroups)
            {
                mood.thoughts.GetMoodThoughts(group, thoughtGroup);

                if (thoughtGroup.Count == 0)
                    continue;

                // Get the leading thought (most severe in the group)
                Thought leadingThought = PawnNeedsUIUtility.GetLeadingThoughtInGroup(thoughtGroup);

                if (leadingThought == null || !leadingThought.VisibleInNeedsTab)
                    continue;

                // Get mood offset for this thought group
                float moodOffset = mood.thoughts.MoodOffsetOfGroup(group);

                // Get the flavor text from thought Description (properly formatted with weapon names, etc.)
                string thoughtLabel = leadingThought.LabelCap.StripTags();
                string flavorText = "";
                if (leadingThought.CurStage != null && !string.IsNullOrEmpty(leadingThought.CurStage.description))
                {
                    // Use Description property which resolves placeholders like {WEAPON_indefinite}
                    // Extract just the first paragraph (before precept/nullified info)
                    string fullDescription = leadingThought.Description;
                    int splitIndex = fullDescription.IndexOf("\n\n");
                    string resolvedDescription = splitIndex > 0 ? fullDescription.Substring(0, splitIndex) : fullDescription;
                    flavorText = $"\"{resolvedDescription.StripTags()}\" ";
                }

                if (thoughtGroup.Count > 1)
                {
                    thoughtLabel = $"{thoughtLabel} x{thoughtGroup.Count}";
                }

                // Format mood offset with sign
                string offsetText = moodOffset.ToString("+0;-0;0");

                // Build expiry info if this is a memory-based thought
                string expiryText = "";
                int durationTicks = group.DurationTicks;
                if (durationTicks > 5 && leadingThought is Thought_Memory)
                {
                    if (thoughtGroup.Count == 1)
                    {
                        // Single thought - simple expiry
                        Thought_Memory memory = (Thought_Memory)leadingThought;
                        int remaining = durationTicks - memory.age;
                        expiryText = $" (expires in {remaining.ToStringTicksToPeriod()})";
                    }
                    else
                    {
                        // Multiple stacked thoughts - show range
                        int minAge = int.MaxValue;
                        int maxAge = int.MinValue;
                        foreach (Thought thought in thoughtGroup)
                        {
                            if (thought is Thought_Memory mem)
                            {
                                minAge = Math.Min(minAge, mem.age);
                                maxAge = Math.Max(maxAge, mem.age);
                            }
                        }
                        int firstExpires = durationTicks - maxAge;
                        int lastExpires = durationTicks - minAge;
                        expiryText = $" (expires in {firstExpires.ToStringTicksToPeriod()} to {lastExpires.ToStringTicksToPeriod()})";
                    }
                }

                var thoughtItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = $"{flavorText}{thoughtLabel}: {offsetText}{expiryText}.",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };

                AddChild(parentItem, thoughtItem);

                thoughtGroup.Clear();
            }
        }

        /// <summary>
        /// Builds children for Break Thresholds subcategory.
        /// </summary>
        private static void BuildBreakThresholdsChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            if (pawn.mindState?.mentalBreaker == null)
                return;

            var breaker = pawn.mindState.mentalBreaker;

            float minor = breaker.BreakThresholdMinor * 100f;
            float major = breaker.BreakThresholdMajor * 100f;
            float extreme = breaker.BreakThresholdExtreme * 100f;

            var minorItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = $"Minor: {minor:F0}%",
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = false
            };
            AddChild(parentItem, minorItem);

            var majorItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = $"Major: {major:F0}%",
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = false
            };
            AddChild(parentItem, majorItem);

            var extremeItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = $"Extreme: {extreme:F0}%",
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = false
            };
            AddChild(parentItem, extremeItem);
        }

        /// <summary>
        /// Builds detailed info children for a category.
        /// </summary>
        private static void BuildDetailedInfoChildren(InspectionTreeItem categoryItem, object obj, string category)
        {
            if (categoryItem.Children.Count > 0)
                return; // Already built

            string info = InspectionInfoHelper.GetCategoryInfo(obj, category);

            if (string.IsNullOrEmpty(info))
                return;

            // Strip XML tags
            info = info.StripTags();

            // Split into lines and create a detail item for each
            var lines = info.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var detailItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = line.Trim(),
                    IndentLevel = categoryItem.IndentLevel + 1,
                    IsExpandable = false
                };

                AddChild(categoryItem, detailItem);
            }
        }

        /// <summary>
        /// Builds children for Log category - creates Combat Log and Social Log subcategories.
        /// </summary>
        private static void BuildLogChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            // Add Combat Log as expandable subcategory
            var combatLogItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = "Combat Log",
                Data = pawn,
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false
            };
            combatLogItem.OnActivate = () => BuildCombatLogEntries(combatLogItem, pawn);
            AddChild(parentItem, combatLogItem);

            // Add Social Log as expandable subcategory
            var socialLogItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = "Social Log",
                Data = pawn,
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false
            };
            socialLogItem.OnActivate = () => BuildSocialLogEntries(socialLogItem, pawn);
            AddChild(parentItem, socialLogItem);
        }

        /// <summary>
        /// Builds combat log entries for a pawn.
        /// </summary>
        private static void BuildCombatLogEntries(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            var entries = new List<(int ageTicks, string text, LogEntry entry)>();

            if (Find.BattleLog != null)
            {
                foreach (Battle battle in Find.BattleLog.Battles)
                {
                    if (!battle.Concerns(pawn))
                        continue;

                    foreach (LogEntry entry in battle.Entries)
                    {
                        if (!entry.Concerns(pawn))
                            continue;

                        string entryText = entry.ToGameStringFromPOV(pawn).StripTags();
                        string timestamp = entry.Age.ToStringTicksToPeriod();
                        string displayText = $"{timestamp} ago - {entryText}";

                        entries.Add((entry.Age, displayText, entry));
                    }
                }
            }

            // Sort by age (most recent first)
            entries.Sort((a, b) => a.ageTicks.CompareTo(b.ageTicks));

            if (entries.Count == 0)
            {
                var noEntriesItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "No combat entries found",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(parentItem, noEntriesItem);
                return;
            }

            foreach (var (ageTicks, displayText, entry) in entries)
            {
                var logItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = displayText,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false,
                    Data = new { Pawn = pawn, Entry = entry }
                };

                if (entry.CanBeClickedFromPOV(pawn))
                {
                    logItem.OnActivate = () =>
                    {
                        entry.ClickedFromPOV(pawn);
                        TolkHelper.Speak("Jumped to target");
                    };
                }

                AddChild(parentItem, logItem);
            }
        }

        /// <summary>
        /// Builds social log entries for a pawn.
        /// </summary>
        private static void BuildSocialLogEntries(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            var entries = new List<(int ageTicks, string text, LogEntry entry)>();

            if (Find.PlayLog != null)
            {
                foreach (LogEntry entry in Find.PlayLog.AllEntries)
                {
                    if (!entry.Concerns(pawn))
                        continue;

                    string entryText = entry.ToGameStringFromPOV(pawn).StripTags();
                    string timestamp = entry.Age.ToStringTicksToPeriod();
                    string displayText = $"{timestamp} ago - {entryText}";

                    entries.Add((entry.Age, displayText, entry));
                }
            }

            // Sort by age (most recent first)
            entries.Sort((a, b) => a.ageTicks.CompareTo(b.ageTicks));

            if (entries.Count == 0)
            {
                var noEntriesItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "No social entries found",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(parentItem, noEntriesItem);
                return;
            }

            foreach (var (ageTicks, displayText, entry) in entries)
            {
                var logItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = displayText,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false,
                    Data = new { Pawn = pawn, Entry = entry }
                };

                if (entry.CanBeClickedFromPOV(pawn))
                {
                    logItem.OnActivate = () =>
                    {
                        entry.ClickedFromPOV(pawn);
                        TolkHelper.Speak("Jumped to target");
                    };
                }

                AddChild(parentItem, logItem);
            }
        }

        /// <summary>
        /// Builds children for Pen Food category showing nutrition info.
        /// </summary>
        private static void BuildPenFoodChildren(InspectionTreeItem parentItem, Building building)
        {
            var penMarker = building.TryGetComp<CompAnimalPenMarker>();
            if (penMarker == null)
                return;

            int indent = parentItem.IndentLevel + 1;
            var calculator = penMarker.PenFoodCalculator;

            // Summary item
            float growth = calculator.NutritionPerDayToday;
            float consumption = calculator.SumNutritionConsumptionPerDay;
            float balance = growth - consumption;
            string balanceStr = balance >= 0 ? $"+{balance:F1}" : $"{balance:F1}";
            string summaryText = $"Balance: {balanceStr} nutrition/day (growth: {growth:F1}, consumption: {consumption:F1})";

            var summaryItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = summaryText,
                IndentLevel = indent,
                IsExpandable = false
            };
            AddChild(parentItem, summaryItem);

            // Stockpiled food
            if (calculator.sumStockpiledNutritionAvailableNow > 0)
            {
                var stockpileItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = $"Stockpiled: {calculator.sumStockpiledNutritionAvailableNow:F1} nutrition",
                    IndentLevel = indent,
                    IsExpandable = false
                };
                AddChild(parentItem, stockpileItem);
            }

            // Animals category
            var animalInfos = calculator.ActualAnimalInfos;
            if (animalInfos != null && animalInfos.Count > 0)
            {
                var animalsCategory = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = $"Animals ({animalInfos.Count} types)",
                    IndentLevel = indent,
                    IsExpandable = true,
                    IsExpanded = false
                };
                animalsCategory.OnActivate = () =>
                {
                    if (animalsCategory.Children.Count == 0)
                    {
                        foreach (var info in animalInfos)
                        {
                            string animalLabel = info.animalDef?.label?.CapitalizeFirst() ?? "Unknown";
                            float animalConsumption = info.nutritionConsumptionPerDay;
                            int count = info.count;
                            string animalText = $"{animalLabel} ({count}): -{animalConsumption:F2}/day";

                            var animalItem = new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.Item,
                                Label = animalText,
                                IndentLevel = indent + 1,
                                IsExpandable = false
                            };
                            AddChild(animalsCategory, animalItem);
                        }
                    }
                };
                AddChild(parentItem, animalsCategory);
            }

            // Stockpiled items breakdown
            var stockpileInfos = calculator.AllStockpiledInfos;
            if (stockpileInfos != null && stockpileInfos.Count > 0)
            {
                var foodCategory = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = $"Stockpiled Items ({stockpileInfos.Count} types)",
                    IndentLevel = indent,
                    IsExpandable = true,
                    IsExpanded = false
                };
                foodCategory.OnActivate = () =>
                {
                    if (foodCategory.Children.Count == 0)
                    {
                        foreach (var info in stockpileInfos)
                        {
                            string foodLabel = info.itemDef?.label?.CapitalizeFirst() ?? "Unknown";
                            float nutrition = info.totalNutritionAvailable;
                            string foodText = $"{foodLabel}: {nutrition:F1} nutrition";

                            var foodItem = new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.Item,
                                Label = foodText,
                                IndentLevel = indent + 1,
                                IsExpandable = false
                            };
                            AddChild(foodCategory, foodItem);
                        }
                    }
                };
                AddChild(parentItem, foodCategory);
            }
        }
    }
}
