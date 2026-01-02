using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimWorldAccess
{
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
        /// Builds the root tree for all objects at a position.
        /// </summary>
        public static InspectionTreeItem BuildTree(List<object> objects)
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
                AddChild(root, BuildObjectItem(obj, 0));
            }

            return root;
        }

        /// <summary>
        /// Builds a tree item for a single object (pawn, building, etc.).
        /// </summary>
        private static InspectionTreeItem BuildObjectItem(object obj, int indent)
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
            item.OnActivate = () => BuildObjectChildren(item);

            return item;
        }

        /// <summary>
        /// Builds category children for an object when it's expanded.
        /// </summary>
        private static void BuildObjectChildren(InspectionTreeItem objectItem)
        {
            if (objectItem.Children.Count > 0)
                return; // Already built

            var obj = objectItem.Data;
            var categories = InspectionInfoHelper.GetAvailableCategories(obj);

            foreach (var category in categories)
            {
                AddChild(objectItem, BuildCategoryItem(obj, category, objectItem.IndentLevel + 1));
            }
        }

        /// <summary>
        /// Gets the label for a category, potentially with additional info.
        /// </summary>
        private static string GetCategoryLabel(object obj, string category)
        {
            // Special handling for Mood category to show percentage and descriptor
            if (category == "Mood" && obj is Pawn pawn && pawn.needs?.mood != null)
            {
                float moodPercentage = pawn.needs.mood.CurLevelPercentage * 100f;
                string moodDescriptor = pawn.needs.mood.MoodString;
                return $"{category}: {moodPercentage:F0}% ({moodDescriptor})";
            }

            // Special handling for Job Queue category to show count
            if (category == "Job Queue" && obj is Pawn jobPawn && jobPawn.jobs?.jobQueue != null)
            {
                int queueCount = jobPawn.jobs.jobQueue.Count;
                return $"Job Queue ({queueCount} queued)";
            }

            return category;
        }

        /// <summary>
        /// Builds a tree item for a category.
        /// </summary>
        private static InspectionTreeItem BuildCategoryItem(object obj, string category, int indent)
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
                item.IsExpandable = false;
                item.OnActivate = () => ExecuteCategoryAction(obj, category);
            }
            else if (IsExpandableCategory(obj, category))
            {
                // This category has sub-items (Gear, Skills, etc.)
                item.IsExpandable = true;
                item.IsExpanded = false;
                item.OnActivate = () => BuildCategoryChildren(item, obj, category);
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
                       (category == "Plant Selection" && building is IPlantToGrowSettable) ||
                       BuildingComponentsHelper.GetDiscoverableComponents(building).Any(c => c.CategoryName == category && !c.IsReadOnly);
            }

            // Check for zone-specific actionable categories
            if (obj is Zone zone)
            {
                string renameLabel = "Rename".Translate().ToString();
                return (category == "Storage" && zone is IStoreSettingsParent)
                    || category == renameLabel;
            }

            return false;
        }

        /// <summary>
        /// Checks if a category has expandable sub-items.
        /// </summary>
        private static bool IsExpandableCategory(object obj, string category)
        {
            return category == "Gear" ||
                   category == "Skills" ||
                   category == "Health" ||
                   category == "Needs" ||
                   category == "Mood" ||
                   category == "Social" ||
                   category == "Training" ||
                   category == "Character" ||
                   category == "Log" ||
                   category == "Job Queue";
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
                string renameLabel = "Rename".Translate().ToString();
                if (category == renameLabel)
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
            else if (category == "Plant Selection" && building is IPlantToGrowSettable plantGrower)
            {
                PlantSelectionMenuState.Open(plantGrower);
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
        private static void BuildCategoryChildren(InspectionTreeItem categoryItem, object obj, string category)
        {
            if (categoryItem.Children.Count > 0)
                return; // Already built

            if (!(obj is Pawn pawn))
                return;

            if (category == "Gear")
            {
                BuildGearChildren(categoryItem, pawn);
            }
            else if (category == "Skills")
            {
                BuildSkillsChildren(categoryItem, pawn);
            }
            else if (category == "Health")
            {
                BuildHealthChildren(categoryItem, pawn);
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
                BuildSocialChildren(categoryItem, pawn);
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
                BuildJobQueueChildren(categoryItem, pawn);
            }
        }

        /// <summary>
        /// Builds children for Job Queue category.
        /// Shows current job and all queued jobs with delete capability.
        /// </summary>
        private static void BuildJobQueueChildren(InspectionTreeItem parentItem, Pawn pawn)
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

            // Add queued jobs (deletable)
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
                        BuildJobQueueChildren(parentItem, pawn);
                        WindowlessInspectionState.RebuildAfterAction();
                    };

                    parentItem.Children.Add(queuedItem);
                    queueIndex++;
                }
            }
        }

        /// <summary>
        /// Builds children for Gear category.
        /// </summary>
        private static void BuildGearChildren(InspectionTreeItem parentItem, Pawn pawn)
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

                gearItem.OnActivate = () => BuildGearItemsChildren(gearItem, pawn, gearCat);
                AddChild(parentItem, gearItem);
            }
        }

        /// <summary>
        /// Builds children for a specific gear category (Equipment/Apparel/Inventory).
        /// </summary>
        private static void BuildGearItemsChildren(InspectionTreeItem gearCatItem, Pawn pawn, string gearCategory)
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
                    IsExpandable = true,
                    IsExpanded = false
                };

                item.OnActivate = () => BuildGearActionChildren(item, pawn, gearItem);
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
        private static void BuildSocialChildren(InspectionTreeItem parentItem, Pawn pawn)
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
                relationItem.OnActivate = () => BuildRelationDetailChildren(relationItem, relation);
                AddChild(parentItem, relationItem);
            }
        }

        /// <summary>
        /// Builds detail children for a specific relation.
        /// </summary>
        private static void BuildRelationDetailChildren(InspectionTreeItem relationItem, SocialTabHelper.RelationInfo relation)
        {
            if (relationItem.Children.Count > 0)
                return; // Already built

            string detailedInfo = relation.DetailedInfo.StripTags();
            var lines = detailedInfo.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var detailItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = line.Trim(),
                    IndentLevel = relationItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(relationItem, detailItem);
            }
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

            // Add ideology name
            var ideoNameItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = $"Ideology: {ideologyInfo.IdeoName}",
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = false
            };
            AddChild(parentItem, ideoNameItem);

            // Add certainty
            var certaintyItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = $"Certainty: {ideologyInfo.Certainty:P0}",
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = false
            };
            AddChild(parentItem, certaintyItem);

            // Add role if available
            if (!string.IsNullOrEmpty(ideologyInfo.RoleName))
            {
                var roleItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = $"Role: {ideologyInfo.RoleName}",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(parentItem, roleItem);
            }

            // Add detailed certainty info
            if (!string.IsNullOrEmpty(ideologyInfo.CertaintyDetails))
            {
                var certaintyDetails = ideologyInfo.CertaintyDetails.StripTags();
                var lines = certaintyDetails.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var detailItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = line.Trim(),
                        IndentLevel = parentItem.IndentLevel + 1,
                        IsExpandable = false
                    };
                    AddChild(parentItem, detailItem);
                }
            }

            // Add role details if available
            if (!string.IsNullOrEmpty(ideologyInfo.RoleDetails))
            {
                var roleDetails = ideologyInfo.RoleDetails.StripTags();
                var lines = roleDetails.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var detailItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = line.Trim(),
                        IndentLevel = parentItem.IndentLevel + 1,
                        IsExpandable = false
                    };
                    AddChild(parentItem, detailItem);
                }
            }
        }

        /// <summary>
        /// Builds children for Health category.
        /// </summary>
        private static void BuildHealthChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            // Add Operations option
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
                int visibleHediffCount = hediffs.Count(h => h.Visible);

                var conditionsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = $"Conditions ({visibleHediffCount})",
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

                var hediffItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = hediffLabel,
                    Data = hediff,
                    IndentLevel = bodyPartItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };

                hediffItem.OnActivate = () => BuildHediffDetailChildren(hediffItem, hediff, pawn);
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
        /// </summary>
        private static void BuildCapacitiesChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            var keyCapacities = new[]
            {
                PawnCapacityDefOf.Consciousness,
                PawnCapacityDefOf.Moving,
                PawnCapacityDefOf.Manipulation,
                PawnCapacityDefOf.Sight,
                PawnCapacityDefOf.Hearing,
                PawnCapacityDefOf.Talking,
                PawnCapacityDefOf.Breathing,
                PawnCapacityDefOf.BloodFiltration,
                PawnCapacityDefOf.BloodPumping
            };

            // Build list of capacities with their levels, then sort by level (lowest first)
            var capacityLevels = new List<(PawnCapacityDef capacity, float level)>();
            foreach (var capacity in keyCapacities)
            {
                if (capacity != null && pawn.health.capacities.CapableOf(capacity))
                {
                    float level = pawn.health.capacities.GetLevel(capacity);
                    capacityLevels.Add((capacity, level));
                }
            }

            // Sort by level ascending (most impaired first)
            capacityLevels.Sort((a, b) => a.level.CompareTo(b.level));

            foreach (var (capacity, level) in capacityLevels)
            {
                string status = $"{level:P0}";

                var capacityItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = $"{capacity.LabelCap}: {status}",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(parentItem, capacityItem);
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

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

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
    }
}
