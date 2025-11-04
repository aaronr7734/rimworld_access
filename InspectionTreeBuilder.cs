using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Builds the inspection tree for objects.
    /// </summary>
    public static class InspectionTreeBuilder
    {
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
                root.Children.Add(BuildObjectItem(obj, 0));
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
                objectItem.Children.Add(BuildCategoryItem(obj, category, objectItem.IndentLevel + 1));
            }
        }

        /// <summary>
        /// Builds a tree item for a category.
        /// </summary>
        private static InspectionTreeItem BuildCategoryItem(object obj, string category, int indent)
        {
            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = category,
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
                   category == "Mood" ||
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
            if (!(obj is Building building))
                return false;

            return (category == "Bills" && building is IBillGiver) ||
                   (category == "Bed Assignment" && building is Building_Bed) ||
                   (category == "Temperature" && building.TryGetComp<CompTempControl>() != null) ||
                   (category == "Storage" && building is IStoreSettingsParent) ||
                   BuildingComponentsHelper.GetDiscoverableComponents(building).Any(c => c.CategoryName == category && !c.IsReadOnly);
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
                   category == "Social" ||
                   category == "Training" ||
                   category == "Character" ||
                   category == "Prisoner";
        }

        /// <summary>
        /// Executes the action for an actionable category.
        /// </summary>
        private static void ExecuteCategoryAction(object obj, string category)
        {
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
            else if (category == "Social")
            {
                BuildDetailedInfoChildren(categoryItem, obj, category);
            }
            else if (category == "Training")
            {
                BuildDetailedInfoChildren(categoryItem, obj, category);
            }
            else if (category == "Character")
            {
                BuildDetailedInfoChildren(categoryItem, obj, category);
            }
            else if (category == "Prisoner")
            {
                BuildDetailedInfoChildren(categoryItem, obj, category);
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
                parentItem.Children.Add(gearItem);
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
                gearCatItem.Children.Add(item);
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
                gearItem.Children.Add(actionItem);
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
                parentItem.Children.Add(skillItem);
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
            sb.Append($"XP: {skill.XpTotalEarned:F0}");

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

            skillItem.Children.Add(detailItem);
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
            parentItem.Children.Add(operationsItem);

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
            parentItem.Children.Add(healthSettingsItem);

            // Add health information as detail text
            string healthInfo = InspectionInfoHelper.GetCategoryInfo(pawn, "Health");
            if (!string.IsNullOrEmpty(healthInfo))
            {
                healthInfo = healthInfo.StripTags();
                var lines = healthInfo.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

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
                    parentItem.Children.Add(detailItem);
                }
            }
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

                categoryItem.Children.Add(detailItem);
            }
        }
    }
}
