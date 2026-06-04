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
                Label = "RimWorldAccess.Inspection.Tree.Root".Translate(),
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
                    // Leave inspection active underneath; BuildingInspectPatch returns early
                    // when InfoCardState.IsActive, and Window_PostClose_Patch re-announces
                    // the inspection row on close.
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
                ExpandedLabel = displayName, // Short form for submenu mode section announcements
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
                        if (categoryKey == "Meditation Focus")
                        {
                            // No focus objects nearby - show non-expandable warning
                            item.Label = "RimWorldAccess.Inspection.Tree.MeditationFocusNoFocus".Translate();
                            item.IsExpandable = false;
                        }
                        else
                        {
                            // Fallback to detailed info display
                            item.IsExpandable = true;
                            item.IsExpanded = false;
                            item.OnActivate = () => BuildDetailedInfoChildren(item, obj, categoryKey);
                        }
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
                    Label = "RimWorldAccess.Inspection.Tree.NoTabInfo".Translate(),
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
                    Label = "RimWorldAccess.Inspection.Tree.TabNoKeyboardContent".Translate(categoryInfo.Name),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                });

                // Add a hint if tab is known but not rich-supported
                if (!categoryInfo.IsKnown)
                {
                    AddChild(parentItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = "RimWorldAccess.Inspection.Tree.UnrecognizedTab".Translate(),
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
                ExpandedLabel = category, // Short form for submenu mode section announcements
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
                       (category == "Owner Assignment" && !(building is Building_Bed) && building.TryGetComp<CompAssignableToPawn>() != null) ||
                       (category == "Temperature" && building.TryGetComp<CompTempControl>() != null) ||
                       (category == "Storage" && building is IStoreSettingsParent) ||
                       (category == "Shells" && building is Building_TurretGun) ||
                       (category == "Plant Selection" && building is IPlantToGrowSettable) ||
                       (category == "Pen Animals" && building.TryGetComp<CompAnimalPenMarker>() != null) ||
                       (category == "Rename" && building.TryGetComp<CompAnimalPenMarker>() != null) ||
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
                category == "Job Queue" ||
                category == "Feeding" ||
                category == "Guest" ||
                category == "Art" ||
                category == "Book" ||
                category == "Books" ||
                category == "Auto-Cut Plants")
                return true;

            // Genes tab is expandable for pawns with gene data or GeneSetHolderBase items (Biotech DLC)
            if (category == "Genes")
            {
                Pawn genePawn = GetPawnFromThing(obj);
                if (genePawn?.genes != null && ModsConfig.BiotechActive)
                    return true;

                // Also expandable for GeneSetHolderBase items (embryos, genepacks, xenogerms)
                if (obj is GeneSetHolderBase holder && holder.GeneSet != null && ModsConfig.BiotechActive)
                    return true;

                return false;
            }

            // Pen Food and Pen Auto-Cut are expandable if building has pen marker
            if ((category == "Pen Food" || category == "Pen Auto-Cut") && obj is Building building)
                return building.TryGetComp<CompAnimalPenMarker>() != null;

            // Contents is expandable for transporters
            if (category == "Contents" && obj is Thing contentsThing)
                return contentsThing.TryGetComp<CompTransporter>() != null;

            // Meditation Focus is expandable only if there are nearby focus objects
            if (category == "Meditation Focus" && obj is Building meditationBuilding && meditationBuilding.Spawned)
                return HasNearbyMeditationFocusObjects(meditationBuilding);

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
                    PrisonerTabState.Open(pawn);
                    return;
                }
            }

            // Handle zone-specific actions
            if (obj is Zone zone)
            {
                if (category == "Rename")
                {
                    ZoneRenameState.Open(zone);
                    return;
                }

                if (category == "Storage" && zone is IStoreSettingsParent zoneStorageParent)
                {
                    var settings = zoneStorageParent.GetStoreSettings();
                    if (settings != null)
                    {
                        StorageSettingsMenuState.Open(settings);
                    }
                    return;
                }

                if (category == "Fishing" && zone.GetType().Name == "Zone_Fishing")
                {
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
                    StorageSettingsMenuState.Open(settings);
                }
                return;
            }

            // Handle building-specific actions
            if (!(obj is Building building))
                return;

            if (category == "Bills" && building is IBillGiver billGiver)
            {
                BillsMenuState.Open(billGiver, building.Position);
            }
            else if (category == "Bed Assignment" && building is Building_Bed bed)
            {
                BedAssignmentState.Open(bed);
            }
            else if (category == "Owner Assignment")
            {
                var comp = (building as ThingWithComps)?.TryGetComp<CompAssignableToPawn>();
                if (comp != null)
                {
                    BuildingOwnerAssignmentState.Open(building as ThingWithComps, comp);
                }
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
                        ThingFilterMenuState.Open(settings.filter, parentSettings?.filter, "TabShells".Translate(),
                            forceHideHitPointsConfig: true, forceHideQualityConfig: true);
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
            else if (category == "Rename")
            {
                var penMarker = building.TryGetComp<CompAnimalPenMarker>();
                if (penMarker != null)
                {
                    PenRenameState.Open(penMarker);
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
                if (category == "Pen Auto-Cut")
                {
                    BuildPenAutoCutChildren(categoryItem, building);
                    return;
                }
                if (category == "Linked Facilities")
                {
                    BuildFacilityChildren(categoryItem, building);
                    return;
                }
                if (category == "Meditation Focus")
                {
                    BuildMeditationFocusChildren(categoryItem, building);
                    return;
                }
                if (category == "Auto-Cut Plants")
                {
                    BuildWindTurbineAutoCutChildren(categoryItem, building, mode);
                    return;
                }
                if (category == "Books" && building is Building_Bookcase bookcase)
                {
                    BuildContentsBooksChildren(categoryItem, bookcase, mode);
                    return;
                }
                if (category == "Contents" && building.TryGetComp<CompTransporter>() != null)
                {
                    BuildContentsTransporterChildren(categoryItem, building, mode);
                    return;
                }
            }

            // Art — works on any Thing with CompArt
            if (category == "Art" && obj is Thing artThing)
            {
                BuildArtChildren(categoryItem, artThing);
                return;
            }

            // Book — works on Book things
            if (category == "Book" && obj is Book book)
            {
                BuildBookChildren(categoryItem, book);
                return;
            }

            // Handle GeneSetHolderBase (embryos, genepacks, xenogerms) gene category
            if (category == "Genes" && obj is GeneSetHolderBase geneHolder)
            {
                BuildGeneSetHolderGenesChildren(categoryItem, geneHolder);
                return;
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
                BuildNeedsChildren(categoryItem, pawn);
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
                BuildTrainingChildren(categoryItem, pawn, mode);
            }
            else if (category == "Character")
            {
                BuildDetailedInfoChildren(categoryItem, obj, category);

                // Favorite color — visual-only in vanilla, add as text for accessibility
                if (pawn != null && ModsConfig.IdeologyActive
                    && !pawn.DevelopmentalStage.Baby()
                    && pawn.story?.favoriteColor != null)
                {
                    string orIdeoColor = string.Empty;
                    if (pawn.Ideo != null && !pawn.Ideo.classicMode)
                    {
                        orIdeoColor = "OrIdeoColor".Translate(pawn.Named("PAWN"));
                    }
                    string colorLabel = "FavoriteColorTooltip".Translate(
                        pawn.Named("PAWN"),
                        pawn.story.favoriteColor.label.Named("COLOR"),
                        0.6f.ToStringPercent().Named("PERCENTAGE"),
                        orIdeoColor.Named("ORIDEO")
                    ).Resolve();
                    AddChild(categoryItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = colorLabel,
                        IndentLevel = categoryItem.IndentLevel + 1,
                        IsExpandable = false
                    });
                }
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
            else if (category == "Feeding")
            {
                BuildFeedingChildren(categoryItem, pawn, mode);
            }
            else if (category == "Guest")
            {
                BuildGuestChildren(categoryItem, pawn, mode);
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
                string unknownJob = "RimWorldAccess.Inspection.Tree.JobUnknown".Translate();
                string currentJobReport;
                try
                {
                    currentJobReport = jobTracker.curJob.GetReport(pawn)?.CapitalizeFirst() ?? unknownJob;
                }
                catch
                {
                    currentJobReport = jobTracker.curJob.def?.label?.CapitalizeFirst() ?? unknownJob;
                }

                var currentItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "RimWorldAccess.Inspection.Tree.JobCurrent".Translate(currentJobReport),
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
                    Label = "RimWorldAccess.Inspection.Tree.JobCurrentIdle".Translate(),
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
                    string unknownJobLabel = "RimWorldAccess.Inspection.Tree.JobUnknown".Translate();
                    try
                    {
                        jobReport = queuedJob.job.GetReport(pawn)?.CapitalizeFirst() ?? unknownJobLabel;
                    }
                    catch
                    {
                        jobReport = queuedJob.job.def?.label?.CapitalizeFirst() ?? unknownJobLabel;
                    }

                    var queuedItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = "RimWorldAccess.Inspection.Tree.JobQueued".Translate(queueIndex, jobReport),
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
                            TolkHelper.Speak("RimWorldAccess.Inspection.Tree.JobCancelled".Loc(jobLabel), SpeechPriority.High);

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
                string localCat = gearCat;
                var gearItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = gearCat.Translate().ToString(),
                    Data = pawn,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };

                gearItem.OnActivate = () => BuildGearItemsChildren(gearItem, pawn, localCat, mode);
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
                var capturedAction = action;
                var actionItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = InteractiveGearHelper.GetActionLabel(capturedAction),
                    Data = new { Pawn = pawn, Gear = gear, Action = capturedAction },
                    IndentLevel = gearItem.IndentLevel + 1,
                    IsExpandable = false
                };

                actionItem.OnActivate = () => ExecuteGearAction(pawn, gear, capturedAction);
                AddChild(gearItem, actionItem);
            }
        }

        /// <summary>
        /// Executes a gear action.
        /// </summary>
        private static void ExecuteGearAction(Pawn pawn, InteractiveGearHelper.GearItem gear, GearAction action)
        {
            bool success = false;

            switch (action)
            {
                case GearAction.Drop:
                    success = InteractiveGearHelper.ExecuteDropAction(gear, pawn);
                    if (success)
                    {
                        // Rebuild tree to reflect changes
                        WindowlessInspectionState.RebuildTree();
                    }
                    break;
                case GearAction.Consume:
                    success = InteractiveGearHelper.ExecuteConsumeAction(gear, pawn);
                    if (success)
                    {
                        // Rebuild tree to reflect changes
                        WindowlessInspectionState.RebuildTree();
                    }
                    break;
                case GearAction.ViewInfo:
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
        /// <summary>
        /// Builds children for the Needs category.
        /// Lists all visible needs sorted by urgency, with learning desires shown
        /// immediately after the Learning need (Biotech children only).
        /// </summary>
        private static void BuildNeedsChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            if (pawn.needs == null)
                return;

            int indent = parentItem.IndentLevel + 1;

            var needs = pawn.needs.AllNeeds;
            if (needs == null || needs.Count == 0)
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Inspection.Tree.NoNeedsToDisplay".Translate(),
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            // Filter to visible needs and sort by percentage (lowest first = most urgent)
            var sortedNeeds = needs
                .Where(n => n.def.showOnNeedList)
                .OrderBy(n => n.CurLevelPercentage)
                .ToList();

            foreach (var need in sortedNeeds)
            {
                float percentage = need.CurLevelPercentage * 100f;
                string label = $"{need.LabelCap}: {percentage:F0}%";

                string needDescription = need.def.description;
                if (!string.IsNullOrEmpty(needDescription))
                {
                    string cleanDesc = needDescription.StripTags().Trim();
                    cleanDesc = System.Text.RegularExpressions.Regex.Replace(cleanDesc, @"\s+", " ");
                    label += $". {cleanDesc}";
                }

                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = label,
                    Data = need,
                    IndentLevel = indent,
                    IsExpandable = false
                });

                // After the Learning need, add learning desires with label and description
                if (need.def == NeedDefOf.Learning && pawn.learning?.ActiveLearningDesires != null)
                {
                    foreach (var desire in pawn.learning.ActiveLearningDesires)
                    {
                        string desireDesc = desire.description ?? "";
                        AddChild(parentItem, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = $"{desire.LabelCap}. {desireDesc}".TrimEnd(),
                            IndentLevel = indent + 1,
                            IsExpandable = false
                        });
                    }
                }
            }
        }

        private static void BuildSkillsChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (pawn.skills?.skills == null)
                return;

            var skills = pawn.skills.skills.OrderByDescending(s => s.Level).ToList();

            foreach (var skill in skills)
            {
                string skillName = skill.def.skillLabel.CapitalizeFirst();

                var skillItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = skillName,
                    ExpandedLabel = skillName,
                    Data = skill,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };

                // Build children eagerly for collapsed summary
                BuildSkillDetailChildren(skillItem, skill);
                var skillChildLabels = skillItem.Children.Select(c => c.Label).ToList();
                if (skillChildLabels.Count > 0)
                    skillItem.Label += $": {string.Join(". ", skillChildLabels)}";

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

            int childIndent = skillItem.IndentLevel + 1;

            // Level
            AddChild(skillItem, new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = $"{"Level".Translate()} {skill.Level}",
                IndentLevel = childIndent,
                IsExpandable = false
            });

            // Passion
            if (skill.passion != Passion.None)
            {
                string passionKey = skill.passion == Passion.Major ? "PassionMajor" : "PassionMinor";
                AddChild(skillItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = passionKey.Translate().ToString(),
                    IndentLevel = childIndent,
                    IsExpandable = false
                });
            }

            // Disabled
            if (skill.TotallyDisabled)
            {
                AddChild(skillItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "DisabledLower".Translate().ToString().ToUpper(),
                    IndentLevel = childIndent,
                    IsExpandable = false
                });
            }

            // XP progress
            AddChild(skillItem, new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = "RimWorldAccess.Inspection.Tree.SkillXpProgress".Translate(
                    skill.xpSinceLastLevel.ToString("F0"),
                    skill.XpRequiredForLevelUp.ToString("F0")),
                IndentLevel = childIndent,
                IsExpandable = false
            });

            // Description
            if (!string.IsNullOrEmpty(skill.def.description))
            {
                AddChild(skillItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = skill.def.description,
                    IndentLevel = childIndent,
                    IsExpandable = false
                });
            }
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
                Label = "Relations".Translate().ToString(),
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
                    Label = "StatsReport_Ideoligion".Translate().ToString(),
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
                    Label = "RimWorldAccess.Pawns.Social.Relation.NoRelations".Translate(),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                AddChild(parentItem, noRelationsItem);
                return;
            }

            foreach (var relation in relations)
            {
                string relationsStr = relation.Relations.Count > 0
                    ? string.Join(", ", relation.Relations)
                    : (string)"Acquaintance".Translate();
                var relationItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "RimWorldAccess.Pawns.Social.Relation.Entry".Translate(
                        relation.OtherPawnName,
                        relationsStr,
                        relation.MyOpinion.ToString("+0;-0;0")),
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

            bool pregnancyApproachInserted = false;
            int childIndent = relationItem.IndentLevel + 1;

            for (int i = 0; i < relation.DetailLines.Count; i++)
            {
                var detailItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = relation.DetailLines[i].StripTags(),
                    IndentLevel = childIndent,
                    IsExpandable = false
                };
                AddChild(relationItem, detailItem);

                // Insert pregnancy approach right after the Relationship line
                if (!pregnancyApproachInserted && i == relation.RelationshipLineIndex
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
                        string targetLabel = "RimWorldAccess.Pawns.Social.Romance.TargetEntry".Translate(
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
                                TolkHelper.Speak("RimWorldAccess.Pawns.Social.Romance.WillTry"
                                    .Translate(pawn.LabelShort, capturedTarget.TargetName));
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
                                "RimWorldAccess.Pawns.Social.Romance.BreakdownHeader".Translate(
                                    capturedTarget.TargetName,
                                    "RomanceChance".Translate(),
                                    capturedTarget.Chance.ToStringPercent()),
                                breakdown);
                        };

                        AddChild(romanceItem, targetItem);
                    }
                    else
                    {
                        AddChild(romanceItem, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = "RimWorldAccess.Pawns.Social.Romance.TargetUnavailable".Translate(
                                target.TargetName, target.Reason),
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
                    Label = "RimWorldAccess.Pawns.Social.Ideology.NotAvailable".Translate(),
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
                Label = "RimWorldAccess.Pawns.Social.Ideology.Header".Translate(ideologyInfo.IdeoName),
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
                string roleLabel = "RimWorldAccess.Pawns.Social.Role.LabelWithHolder"
                    .Translate(role.LabelCap, holderName);

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
                    Label = "RimWorldAccess.Pawns.Social.Role.Assign".Translate(pawn.LabelShort.StripTags()),
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
                    roleItem.Label = "RimWorldAccess.Pawns.Social.Role.LabelWithHolder"
                        .Translate(role.LabelCap, pawn.LabelShort.StripTags());
                };
                AddChild(roleItem, assignItem);
            }

            // Add Unassign action if pawn holds this role
            if (pawnHoldsRole)
            {
                var unassignItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = "RimWorldAccess.Pawns.Social.Role.Unassign".Translate(pawn.LabelShort.StripTags()),
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
                    roleItem.Label = "RimWorldAccess.Pawns.Social.Role.LabelWithHolder"
                        .Translate(role.LabelCap, "NoRoleAssigned".Translate());
                };
                AddChild(roleItem, unassignItem);
            }

            // Show why pawn can't be assigned if not eligible
            if (!pawnHoldsRole && !pawnIsEligible)
            {
                var unmetReq = role.GetFirstUnmetRequirement(pawn);
                string reason = unmetReq != null
                    ? (string)"RimWorldAccess.Pawns.Social.Role.CannotAssignReason".Translate(unmetReq.GetLabelCap(role).StripTags())
                    : (string)"RimWorldAccess.Pawns.Social.Role.CannotAssignDefault".Translate();
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
                    Label = "RimWorldAccess.Pawns.Social.Role.RequirementsHeader".Translate(),
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
        /// Builds children for Training category with interactive controls.
        /// Shows trainability, wildness, master assignment, follow toggles, and trainable skills.
        /// </summary>
        private static void BuildTrainingChildren(InspectionTreeItem parentItem, Pawn pawn, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            if (pawn?.training == null)
                return;

            int indent = parentItem.IndentLevel + 1;
            bool isReadOnly = (mode == InspectionMode.ReadOnly);

            // Trainability header
            TrainabilityDef trainability = TrainableUtility.GetTrainability(pawn);
            if (trainability != null)
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "CreatureTrainability".Translate(pawn.def.label).CapitalizeFirst()
                            + ": " + trainability.LabelCap,
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }

            // Wildness with stat explanation inline via ExpandedLabel
            float wildness = pawn.GetStatValue(StatDefOf.Wildness);
            string wildnessShort = ("CreatureWildness".Translate(pawn.def.label).CapitalizeFirst()
                        + ": " + wildness.ToStringPercent()).Resolve();
            string wildnessExplanation = StatDefOf.Wildness.Worker.GetExplanationFull(
                StatRequest.For(pawn), StatDefOf.Wildness.toStringNumberSense, wildness);
            // Flatten multiline explanation into sentence form
            string flatExplanation = string.Join(". ",
                wildnessExplanation.StripTags()
                    .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrEmpty(l)));
            AddChild(parentItem, new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = wildnessShort + ". " + flatExplanation,
                ExpandedLabel = wildnessShort,
                IndentLevel = indent,
                IsExpandable = false
            });

            // Master section and follow toggles (only if Obedience learned)
            if (pawn.training.HasLearned(TrainableDefOf.Obedience))
            {
                BuildTrainingMasterSection(parentItem, pawn, indent, isReadOnly);
                BuildTrainingFollowToggles(parentItem, pawn, indent, isReadOnly);
            }

            // Odyssey DLC behavior toggles
            if (ModsConfig.OdysseyActive)
            {
                BuildTrainingOdysseyToggles(parentItem, pawn, indent, isReadOnly);
            }

            // Trainable skills list
            if (pawn.RaceProps.showTrainables)
            {
                BuildTrainingSkillsList(parentItem, pawn, indent, isReadOnly);
            }
        }

        /// <summary>
        /// Builds the master assignment section for the training tab.
        /// </summary>
        private static void BuildTrainingMasterSection(
            InspectionTreeItem parentItem, Pawn pawn, int indent, bool isReadOnly)
        {
            bool canChangeMaster = pawn.RaceProps.playerCanChangeMaster || !ModsConfig.IdeologyActive;
            string masterLabel = TrainableUtility.MasterString(pawn);

            if (!canChangeMaster || isReadOnly)
            {
                string label = "Master".Translate() + ": " + masterLabel;
                string tooltip = null;
                if (!canChangeMaster && pawn.playerSettings?.Master != null)
                {
                    tooltip = "DryadCannotChangeMaster".Translate(
                        pawn.Named("ANIMAL"),
                        pawn.playerSettings.Master.Named("MASTER")).CapitalizeFirst();
                }
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = label,
                    Tooltip = tooltip,
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            // Interactive: expandable master selector
            var masterItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = "Master".Translate() + ": " + masterLabel,
                Data = pawn,
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };

            masterItem.OnActivate = () =>
            {
                if (masterItem.Children.Count > 0)
                    return;

                int childIndent = masterItem.IndentLevel + 1;
                var candidates = TrainingTabHelper.GetMasterCandidates(pawn);

                foreach (var candidate in candidates)
                {
                    if (candidate.CanBeMaster)
                    {
                        if (candidate.IsCurrent)
                        {
                            // Current master shown as non-actionable
                            AddChild(masterItem, new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.DetailText,
                                Label = candidate.Label,
                                IndentLevel = childIndent,
                                IsExpandable = false
                            });
                        }
                        else
                        {
                            var capturedColonist = candidate.Colonist;
                            var optionItem = new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.Action,
                                Label = candidate.Label,
                                IndentLevel = childIndent,
                                IsExpandable = false
                            };
                            optionItem.OnActivate = () =>
                            {
                                TrainingTabHelper.SetMaster(pawn, capturedColonist);
                                masterItem.Children.Clear();
                                masterItem.IsExpanded = false;
                                masterItem.Label = "Master".Translate() + ": "
                                                   + TrainableUtility.MasterString(pawn);
                            };
                            AddChild(masterItem, optionItem);
                        }
                    }
                    else
                    {
                        string reasonSuffix = !string.IsNullOrEmpty(candidate.DisabledReason)
                            ? " (" + candidate.DisabledReason + ")"
                            : "";
                        AddChild(masterItem, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = candidate.Label + reasonSuffix,
                            IndentLevel = childIndent,
                            IsExpandable = false
                        });
                    }
                }
            };

            AddChild(parentItem, masterItem);
        }

        /// <summary>
        /// Builds follow drafted/fieldwork toggle items.
        /// </summary>
        private static void BuildTrainingFollowToggles(
            InspectionTreeItem parentItem, Pawn pawn, int indent, bool isReadOnly)
        {
            // Follow Drafted
            string draftedState = pawn.playerSettings.followDrafted
                ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
            var draftedItem = new InspectionTreeItem
            {
                Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText
                                  : InspectionTreeItem.ItemType.Action,
                Label = "CreatureFollowDrafted".Translate() + ": " + draftedState,
                IndentLevel = indent,
                IsExpandable = false
            };
            if (!isReadOnly)
            {
                draftedItem.OnActivate = () =>
                {
                    TrainingTabHelper.ToggleFollowDrafted(pawn);
                    string newState = pawn.playerSettings.followDrafted
                        ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
                    draftedItem.Label = "CreatureFollowDrafted".Translate() + ": " + newState;
                };
            }
            AddChild(parentItem, draftedItem);

            // Follow Fieldwork
            string fieldworkState = pawn.playerSettings.followFieldwork
                ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
            var fieldworkItem = new InspectionTreeItem
            {
                Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText
                                  : InspectionTreeItem.ItemType.Action,
                Label = "CreatureFollowFieldwork".Translate() + ": " + fieldworkState,
                IndentLevel = indent,
                IsExpandable = false
            };
            if (!isReadOnly)
            {
                fieldworkItem.OnActivate = () =>
                {
                    TrainingTabHelper.ToggleFollowFieldwork(pawn);
                    string newState = pawn.playerSettings.followFieldwork
                        ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
                    fieldworkItem.Label = "CreatureFollowFieldwork".Translate() + ": " + newState;
                };
            }
            AddChild(parentItem, fieldworkItem);
        }

        /// <summary>
        /// Builds Odyssey DLC behavior toggles (forage, dig) if the skills are learned.
        /// </summary>
        private static void BuildTrainingOdysseyToggles(
            InspectionTreeItem parentItem, Pawn pawn, int indent, bool isReadOnly)
        {
            if (pawn.training.HasLearned(TrainableDefOf.Forage))
            {
                string forageState = pawn.playerSettings.animalForage
                    ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
                var forageItem = new InspectionTreeItem
                {
                    Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText
                                      : InspectionTreeItem.ItemType.Action,
                    Label = "ForageEnabled".Translate() + ": " + forageState,
                    IndentLevel = indent,
                    IsExpandable = false
                };
                if (!isReadOnly)
                {
                    forageItem.OnActivate = () =>
                    {
                        TrainingTabHelper.ToggleForaging(pawn);
                        string newState = pawn.playerSettings.animalForage
                            ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
                        forageItem.Label = "ForageEnabled".Translate() + ": " + newState;
                    };
                }
                AddChild(parentItem, forageItem);
            }

            if (pawn.training.HasLearned(TrainableDefOf.Dig))
            {
                string digState = pawn.playerSettings.animalDig
                    ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
                var digItem = new InspectionTreeItem
                {
                    Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText
                                      : InspectionTreeItem.ItemType.Action,
                    Label = "DigEnabled".Translate() + ": " + digState,
                    IndentLevel = indent,
                    IsExpandable = false
                };
                if (!isReadOnly)
                {
                    digItem.OnActivate = () =>
                    {
                        TrainingTabHelper.ToggleDigging(pawn);
                        string newState = pawn.playerSettings.animalDig
                            ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
                        digItem.Label = "DigEnabled".Translate() + ": " + newState;
                    };
                }
                AddChild(parentItem, digItem);
            }
        }

        /// <summary>
        /// Builds the expandable trainable skills list with toggle capability.
        /// </summary>
        private static void BuildTrainingSkillsList(
            InspectionTreeItem parentItem, Pawn pawn, int indent, bool isReadOnly)
        {
            var skillsItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = "RimWorldAccess.Inspection.Tree.SkillsSubcategory".Translate(),
                Data = pawn,
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };

            skillsItem.OnActivate = () =>
            {
                if (skillsItem.Children.Count > 0)
                    return;

                int childIndent = skillsItem.IndentLevel + 1;
                var trainables = TrainingTabHelper.GetTrainableInfos(pawn);

                if (trainables.Count == 0)
                {
                    AddChild(skillsItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = "RimWorldAccess.Inspection.Tree.NoTrainableSkills".Translate(),
                        IndentLevel = childIndent,
                        IsExpandable = false
                    });
                    return;
                }

                foreach (var info in trainables)
                {
                    string progress = info.CurrentSteps + " / " + info.TotalSteps;
                    string description = TrainingTabHelper.GetTrainableDescription(pawn, info);

                    if (!info.CanTrain)
                    {
                        // Cannot train — show reason with description inline
                        string shortLabel = info.Def.LabelCap + ": " +
                            (!string.IsNullOrEmpty(info.DisabledReason) ? info.DisabledReason : "Cannot train");
                        AddChild(skillsItem, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = shortLabel + ". " + description,
                            ExpandedLabel = shortLabel,
                            Data = info.Def,
                            IndentLevel = childIndent,
                            IsExpandable = false
                        });
                    }
                    else if (isReadOnly)
                    {
                        string status = info.IsLearned ? "Learned"
                                       : info.IsWanted ? "Wanted" : "Not wanted";
                        string shortLabel = info.Def.LabelCap + ": " + status + ", " + progress;
                        AddChild(skillsItem, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = shortLabel + ". " + description,
                            ExpandedLabel = shortLabel,
                            Data = info.Def,
                            IndentLevel = childIndent,
                            IsExpandable = false
                        });
                    }
                    else
                    {
                        // Interactive toggle with description inline
                        var capturedDef = info.Def;
                        string status = info.IsLearned ? "Learned"
                                       : info.IsWanted ? "Wanted" : "Not wanted";
                        string shortLabel = info.Def.LabelCap + ": " + status + ", " + progress;
                        var skillItem = new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.Action,
                            Label = shortLabel + ". " + description,
                            ExpandedLabel = shortLabel,
                            Data = info.Def,
                            IndentLevel = childIndent,
                            IsExpandable = false
                        };

                        skillItem.OnActivate = () =>
                        {
                            TrainingTabHelper.ToggleTrainable(pawn, capturedDef);
                            // Refresh all sibling labels since SetWantedRecursive cascades
                            RefreshTrainingSkillLabels(skillsItem, pawn);
                        };

                        AddChild(skillsItem, skillItem);
                    }
                }
            };

            AddChild(parentItem, skillsItem);
        }

        /// <summary>
        /// Refreshes all training skill labels after a toggle, since SetWantedRecursive
        /// cascades to prerequisites and dependents.
        /// </summary>
        private static void RefreshTrainingSkillLabels(InspectionTreeItem skillsItem, Pawn pawn)
        {
            foreach (var child in skillsItem.Children)
            {
                if (child.Data is TrainableDef td && child.Type == InspectionTreeItem.ItemType.Action)
                {
                    bool wanted = pawn.training.GetWanted(td);
                    bool learned = pawn.training.HasLearned(td);
                    string status = learned ? "Learned"
                                   : wanted ? "Wanted" : "Not wanted";
                    int steps = TrainingTabHelper.GetSteps(pawn, td);
                    string shortLabel = td.LabelCap + ": " + status + ", " + steps + " / " + td.steps;
                    // Rebuild full label with description
                    var info = new TrainingTabHelper.TrainableInfo
                    {
                        Def = td,
                        CanTrain = true,
                        DisabledReason = null
                    };
                    string description = TrainingTabHelper.GetTrainableDescription(pawn, info);
                    child.ExpandedLabel = shortLabel;
                    child.Label = shortLabel + ". " + description;
                }
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
                    Label = "RimWorldAccess.Inspection.Tree.NoGeneInfo".Translate(),
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

            // Build collapsed summary from xenotype info and children
            string genesLabel = "TabGenes".Translate().ToString();
            categoryItem.ExpandedLabel = genesLabel;
            string xenotypeLabel = pawn.genes.XenotypeLabelCap;
            var geneChildLabels = categoryItem.Children.Select(c => c.Label).ToList();
            if (geneChildLabels.Count > 0)
                categoryItem.Label = $"{genesLabel}, {xenotypeLabel}: {string.Join(". ", geneChildLabels)}";
            else
                categoryItem.Label = $"{genesLabel}: {xenotypeLabel}";
        }

        /// <summary>
        /// Builds children for Genes category for GeneSetHolderBase items (embryos, genepacks, xenogerms).
        /// Uses GeneTreeBuilder.BuildTree() to create the gene tree from the item's GeneSet.
        /// </summary>
        private static void BuildGeneSetHolderGenesChildren(InspectionTreeItem categoryItem, GeneSetHolderBase holder)
        {
            if (categoryItem.Children.Count > 0)
                return; // Already built

            if (holder.GeneSet == null || !ModsConfig.BiotechActive)
            {
                AddChild(categoryItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Inspection.Tree.NoGeneInfo".Translate(),
                    IndentLevel = categoryItem.IndentLevel + 1,
                    IsExpandable = false
                });
                return;
            }

            // Get parent names for embryos (HumanEmbryo has Mother/Father via CompHasPawnSources)
            string motherName = null;
            string fatherName = null;
            if (holder is HumanEmbryo embryo)
            {
                try
                {
                    motherName = embryo.Mother?.LabelShort;
                    fatherName = embryo.Father?.LabelShort;
                }
                catch { /* CompHasPawnSources may not be available */ }
            }

            var geneTree = GeneTreeBuilder.BuildTree(holder.GeneSet, motherName, fatherName);

            // Copy children from the gene tree root into our category item
            foreach (var child in geneTree.Children)
            {
                child.Parent = categoryItem;
                child.IndentLevel = categoryItem.IndentLevel + 1;
                AdjustChildIndents(child, categoryItem.IndentLevel + 1);
                categoryItem.Children.Add(child);
            }

            // Build collapsed summary from children
            string holderGenesLabel = "TabGenes".Translate().ToString();
            categoryItem.ExpandedLabel = holderGenesLabel;
            string xenotype = holder.GeneSet.Label;
            var holderGeneChildLabels = categoryItem.Children.Select(c => c.Label).ToList();
            if (holderGeneChildLabels.Count > 0)
            {
                string prefix = !string.IsNullOrEmpty(xenotype) && xenotype != "ERR"
                    ? $"{holderGenesLabel}, {xenotype}"
                    : holderGenesLabel;
                categoryItem.Label = $"{prefix}: {string.Join(". ", holderGeneChildLabels)}";
            }
            else if (!string.IsNullOrEmpty(xenotype) && xenotype != "ERR")
            {
                categoryItem.Label = $"{holderGenesLabel}: {xenotype}";
            }
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

            // Operations action (Full mode only)
            if (mode == InspectionMode.Full)
            {
                // Use mech-specific label for mechanoids
                string operationsLabel = (pawn.RaceProps.IsMechanoid
                    ? "MedicalOperationsMechanoidsShort"
                    : "MedicalOperationsShort").Translate();

                var operationsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = operationsLabel,
                    Data = pawn,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                operationsItem.OnActivate = () =>
                {
                    HealthTabState.OpenOperations(pawn);
                };
                AddChild(parentItem, operationsItem);

                // Medical care settings action (opens the Overview tab)
                var healthSettingsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = "RimWorldAccess.Inspection.Tree.HealthSettings".Translate(),
                    Data = pawn,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                healthSettingsItem.OnActivate = () =>
                {
                    HealthTabState.OpenMedicalSettings(pawn);
                };
                AddChild(parentItem, healthSettingsItem);
            }

            // Pain level (flesh pawns only, skip if no pain)
            string painLabel = HealthTabHelper.GetPainLabel(pawn);
            if (painLabel != null)
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = painLabel,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                });
            }

            // Bleeding rate with time-to-death
            string bleedingLabel = HealthTabHelper.GetBleedingLabel(pawn);
            if (bleedingLabel != null)
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = bleedingLabel,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                });
            }

            // Body part nodes — flat list using vanilla's hediff filtering and sort order
            var visibleHediffs = HealthTabHelper.GetVisibleHediffs(pawn).ToList();

            if (visibleHediffs.Count > 0)
            {
                // Group by body part, sorted by vanilla's height/coverage priority
                var hediffsByPart = visibleHediffs
                    .GroupBy(h => h.Part)
                    .OrderByDescending(g => HealthTabHelper.GetHediffListPriority(g.Key));

                foreach (var group in hediffsByPart)
                {
                    var part = group.Key;
                    var partHediffs = group.ToList();
                    string partLabel = part != null ? part.LabelCap.ToString() : "WholeBody".Translate().ToString();

                    var bodyPartItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = partLabel,
                        ExpandedLabel = partLabel,
                        IndentLevel = parentItem.IndentLevel + 1,
                        IsExpandable = true,
                        IsExpanded = false
                    };
                    // Build children eagerly so collapsed labels include full content immediately
                    BuildBodyPartHediffChildren(bodyPartItem, pawn, part, partHediffs);
                    AddChild(parentItem, bodyPartItem);
                }
            }
            else
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = $"({"NoHealthConditions".Translate()})",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                });
            }

            // Capacities subcategory — build children eagerly for collapsed summary
            if (pawn.health.capacities != null && !pawn.Dead)
            {
                var capacities = HealthTabHelper.GetCapacities(pawn);
                if (capacities.Count > 0)
                {
                    string capacitiesLabel = "RimWorldAccess.Inspection.Tree.Capacities".Translate();
                    var capacitiesItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.SubCategory,
                        Label = capacitiesLabel,
                        ExpandedLabel = capacitiesLabel,
                        Data = pawn,
                        IndentLevel = parentItem.IndentLevel + 1,
                        IsExpandable = true,
                        IsExpanded = false
                    };
                    BuildCapacitiesChildren(capacitiesItem, pawn);
                    // Build collapsed summary from children
                    var capChildLabels = capacitiesItem.Children.Select(c => c.Label).ToList();
                    if (capChildLabels.Count > 0)
                        capacitiesItem.Label += $": {string.Join(". ", capChildLabels)}";
                    AddChild(parentItem, capacitiesItem);
                }
            }
        }

        /// <summary>
        /// Builds children for a body part node — groups hediffs by UIGroupKey (like vanilla)
        /// so identical conditions show as "Gunshot wound x3" instead of 3 separate items.
        /// </summary>
        private static void BuildBodyPartHediffChildren(InspectionTreeItem parentItem, Pawn pawn, BodyPartRecord part, List<Hediff> hediffs)
        {
            if (parentItem.Children.Count > 0)
                return;

            // Add body part condition/HP child if damaged
            if (part != null)
            {
                float partHealth = pawn.health.hediffSet.GetPartHealth(part);
                float maxHealth = part.def.GetMaxHealth(pawn);
                if (partHealth < maxHealth * 0.999f)
                {
                    var conditionLabel = HealthUtility.GetPartConditionLabel(pawn, part);
                    string conditionText = $"{conditionLabel.First}, {partHealth} / {maxHealth}";
                    float efficiency = PawnCapacityUtility.CalculatePartEfficiency(pawn.health.hediffSet, part);
                    if (efficiency != 1f)
                        conditionText += $", {"Efficiency".Translate()}: {efficiency.ToStringPercent()}";

                    AddChild(parentItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = conditionText,
                        IndentLevel = parentItem.IndentLevel + 1,
                        IsExpandable = false
                    });
                }
            }

            var groups = hediffs.GroupBy(h => h.UIGroupKey).ToList();

            // Single hediff group: skip intermediate node, attach details directly to body part
            if (groups.Count == 1)
            {
                var representative = groups[0].First();
                int count = groups[0].Count();

                // Build hediff name as first child so it leads the collapsed summary
                string hediffName = representative.LabelCap.StripTags();
                if (count > 1)
                    hediffName += $" x{count}";

                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = hediffName,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                });

                // Build detail children (TipStringExtra lines + description)
                int childrenBefore = parentItem.Children.Count;
                BuildHediffDetailChildren(parentItem, representative, pawn);
                bool hasDetailChildren = parentItem.Children.Count > childrenBefore;

                if (!hasDetailChildren)
                {
                    // No detail content beyond hediff name — not expandable
                    parentItem.IsExpandable = false;
                }

                // Build collapsed summary from all children
                var childLabels = parentItem.Children.Select(c => c.Label).ToList();
                if (childLabels.Count > 0)
                {
                    parentItem.Label += $": {string.Join(". ", childLabels)}";
                }
                return;
            }

            // Multiple hediff groups: create a child node for each
            foreach (var hediffGroup in groups)
            {
                var representative = hediffGroup.First();
                int count = hediffGroup.Count();

                string hediffName = representative.LabelCap.StripTags();
                if (count > 1)
                    hediffName += $" x{count}";

                bool hasExpandableContent = !string.IsNullOrWhiteSpace(representative.TipStringExtra)
                                         || !string.IsNullOrWhiteSpace(representative.Description);

                var hediffItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = hediffName,
                    ExpandedLabel = hediffName,
                    Data = representative,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = hasExpandableContent,
                    IsExpanded = false
                };

                if (hasExpandableContent)
                {
                    // Build children eagerly for collapsed summary
                    BuildHediffDetailChildren(hediffItem, representative, pawn);
                    var hediffChildLabels = hediffItem.Children.Select(c => c.Label).ToList();
                    if (hediffChildLabels.Count > 0)
                        hediffItem.Label += $": {string.Join(". ", hediffChildLabels)}";
                }
                AddChild(parentItem, hediffItem);
            }

            // Build collapsed summary from all children
            var multiChildLabels = parentItem.Children.Select(c => c.Label).ToList();
            if (multiChildLabels.Count > 0)
            {
                parentItem.Label += $": {string.Join(". ", multiChildLabels)}";
            }
        }

        /// <summary>
        /// Builds detail children for a specific hediff showing vanilla tooltip content and description.
        /// </summary>
        private static void BuildHediffDetailChildren(InspectionTreeItem hediffItem, Hediff hediff, Pawn pawn)
        {
            // Show comprehensive effects (vanilla's TipStringExtra content)
            string effectsText = HealthTabHelper.GetComprehensiveHediffEffects(hediff, pawn);

            if (!string.IsNullOrEmpty(effectsText))
            {
                string[] effectLines = effectsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in effectLines)
                {
                    string trimmedLine = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedLine))
                    {
                        AddChild(hediffItem, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = trimmedLine,
                            IndentLevel = hediffItem.IndentLevel + 1,
                            IsExpandable = false
                        });
                    }
                }
            }

            // Description at the end
            string description = hediff.Description;
            if (!string.IsNullOrEmpty(description))
            {
                description = description.StripTags().Trim();
                description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ");

                AddChild(hediffItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = description,
                    IndentLevel = hediffItem.IndentLevel + 1,
                    IsExpandable = false
                });
            }
        }

        /// <summary>
        /// Builds children for Capacities subcategory.
        /// Uses vanilla's filtering, sorting, and pawn-type-specific labels.
        /// </summary>
        private static void BuildCapacitiesChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return;

            var capacities = HealthTabHelper.GetCapacities(pawn);

            foreach (var capacity in capacities)
            {
                string capName = capacity.Label;
                bool hasBreakdown = !string.IsNullOrEmpty(capacity.DetailedBreakdown);

                var capacityItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = capName,
                    ExpandedLabel = capName,
                    Data = capacity,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = hasBreakdown,
                    IsExpanded = false
                };

                if (hasBreakdown)
                {
                    // Add level as first child
                    AddChild(capacityItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = capacity.LevelLabel,
                        IndentLevel = capacityItem.IndentLevel + 1,
                        IsExpandable = false
                    });
                    // Build breakdown children eagerly
                    BuildCapacityDetailChildren(capacityItem, capacity);
                    // Build collapsed summary from children
                    var capDetailLabels = capacityItem.Children.Select(c => c.Label).ToList();
                    if (capDetailLabels.Count > 0)
                        capacityItem.Label += $": {string.Join(". ", capDetailLabels)}";
                }
                else
                {
                    // No breakdown — show level inline (non-expandable)
                    capacityItem.Label = $"{capName}: {capacity.LevelLabel}";
                }
                AddChild(parentItem, capacityItem);
            }
        }

        /// <summary>
        /// Builds detail children for a capacity showing impactors.
        /// </summary>
        private static void BuildCapacityDetailChildren(InspectionTreeItem capacityItem, HealthTabHelper.CapacityInfo capacity)
        {
            if (!string.IsNullOrEmpty(capacity.DetailedBreakdown))
            {
                var lines = capacity.DetailedBreakdown.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedLine))
                    {
                        AddChild(capacityItem, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = trimmedLine,
                            IndentLevel = capacityItem.IndentLevel + 1,
                            IsExpandable = false
                        });
                    }
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
                    Label = "RimWorldAccess.Inspection.Tree.NoMoodInfo".Translate(),
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
                    Label = "RimWorldAccess.Inspection.Tree.BreakThresholds".Translate(),
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
                    Label = "RimWorldAccess.Inspection.Tree.NoThoughts".Translate(),
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

            string MakeBreakLine(string vanillaIntensityKey, float percent) =>
                "RimWorldAccess.Inspection.Tree.BreakThresholdLine".Translate(
                    vanillaIntensityKey.Translate().ToString().CapitalizeFirst(),
                    percent.ToString("F0"));

            var minorItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = MakeBreakLine("MentalBreakIntensityMinor", minor),
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = false
            };
            AddChild(parentItem, minorItem);

            var majorItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = MakeBreakLine("MentalBreakIntensityMajor", major),
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = false
            };
            AddChild(parentItem, majorItem);

            var extremeItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = MakeBreakLine("MentalBreakIntensityExtreme", extreme),
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
                Label = "RimWorldAccess.Inspection.Tree.CombatLog".Translate(),
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
                Label = "RimWorldAccess.Inspection.Tree.SocialLog".Translate(),
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
                        string displayText = "RimWorldAccess.Inspection.Tree.LogEntryFormat".Translate(timestamp, entryText);

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
                    Label = "RimWorldAccess.Inspection.Tree.NoCombatEntries".Translate(),
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
                        MapNavigationState.SpeakJumpedTo(null);
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
                    string displayText = "RimWorldAccess.Inspection.Tree.LogEntryFormat".Translate(timestamp, entryText);

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
                    Label = "RimWorldAccess.Inspection.Tree.NoSocialEntries".Translate(),
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
                        MapNavigationState.SpeakJumpedTo(null);
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

            // Unenclosed check - game shows only this message when pen is not enclosed
            if (calculator.Unenclosed)
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "RimWorldAccess.Inspection.Tree.PenNotEnclosedDescription".Translate(
                        "AutocutUnenclosedPen".Translate()),
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            // Pen size description
            string penSize = calculator.PenSizeDescription();
            if (!string.IsNullOrEmpty(penSize))
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "RimWorldAccess.Inspection.Tree.PenSize".Translate(penSize),
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }

            // Nutrition balance summary
            float growth = calculator.NutritionPerDayToday;
            float consumption = calculator.SumNutritionConsumptionPerDay;
            float balance = growth - consumption;
            string balanceStr = balance >= 0 ? $"+{balance:F1}" : $"{balance:F1}";
            string summaryText = "RimWorldAccess.Inspection.Tree.PenBalance".Translate(
                balanceStr, growth.ToString("F1"), consumption.ToString("F1"));

            AddChild(parentItem, new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = summaryText,
                IndentLevel = indent,
                IsExpandable = false
            });

            // Stockpiled food
            if (calculator.sumStockpiledNutritionAvailableNow > 0)
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "RimWorldAccess.Inspection.Tree.PenStockpiled".Translate(
                        calculator.sumStockpiledNutritionAvailableNow.ToString("F1")),
                    IndentLevel = indent,
                    IsExpandable = false
                });

                // Days until stockpile is empty (only when in deficit)
                if (balance < 0)
                {
                    float daysUntilEmpty = calculator.sumStockpiledNutritionAvailableNow / (-balance);
                    AddChild(parentItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = "RimWorldAccess.Inspection.Tree.PenStockpileLasts".Translate(daysUntilEmpty.ToString("F1")),
                        IndentLevel = indent,
                        IsExpandable = false
                    });
                }
            }

            // Animals in pen
            var animalInfos = calculator.ActualAnimalInfos;
            if (animalInfos != null && animalInfos.Count > 0)
            {
                var animalsCategory = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = "RimWorldAccess.Inspection.Tree.PenAnimalsHeader".Translate(animalInfos.Count),
                    IndentLevel = indent,
                    IsExpandable = true,
                    IsExpanded = false
                };
                animalsCategory.OnActivate = () =>
                {
                    if (animalsCategory.Children.Count == 0)
                    {
                        string unknownAnimal = "RimWorldAccess.Inspection.Tree.PenUnknownAnimal".Translate();
                        foreach (var info in animalInfos)
                        {
                            string animalLabel = info.animalDef?.label?.CapitalizeFirst() ?? unknownAnimal;
                            float animalConsumption = info.nutritionConsumptionPerDay;
                            int count = info.count;
                            string animalText = "RimWorldAccess.Inspection.Tree.PenAnimalRow".Translate(
                                animalLabel, count, animalConsumption.ToString("F2"));

                            AddChild(animalsCategory, new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.Item,
                                Label = animalText,
                                IndentLevel = indent + 1,
                                IsExpandable = false
                            });
                        }
                    }
                };
                AddChild(parentItem, animalsCategory);
            }

            // Example Animals and Add Example Animal — these cross-reference each other
            // so changes in one rebuild both sections and refresh the visible list.
            var examplesCategory = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = "RimWorldAccess.Inspection.Tree.PenExamplesHeader".Translate(),
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };

            var addExampleCategory = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = "RimWorldAccess.Inspection.Tree.PenAddExamplesHeader".Translate(),
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };

            // Shared helper to rebuild both sections' children from current data
            System.Action rebuildExampleSections = null;
            rebuildExampleSections = () =>
            {
                // Rebuild "Example Animals" children
                examplesCategory.Children.Clear();
                var currentDefs = penMarker.ForceDisplayedAnimalDefs;
                if (currentDefs != null && currentDefs.Count > 0)
                {
                    var infos = calculator.ComputeExampleAnimals(currentDefs);
                    Quadrum bestQuadrum = calculator.GetSummerOrBestQuadrum();
                    examplesCategory.Label = "RimWorldAccess.Inspection.Tree.PenExamplesHeaderWithCount".Translate(infos?.Count ?? 0);
                    if (infos != null)
                    {
                        string unknownAnimal = "RimWorldAccess.Inspection.Tree.PenUnknownAnimal".Translate();
                        foreach (var info in infos)
                        {
                            if (info.animalDef == null) continue;
                            string label = info.animalDef.label?.CapitalizeFirst() ?? unknownAnimal;
                            float capacity = calculator.CapacityOf(bestQuadrum, info.animalDef);
                            float perAnimal = info.nutritionConsumptionPerDay;
                            string text = "RimWorldAccess.Inspection.Tree.PenExampleEntry".Translate(
                                label, capacity.ToString("F0"), perAnimal.ToString("F2"));

                            var exampleItem = new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.Action,
                                Label = text,
                                IndentLevel = indent + 1,
                                IsExpandable = false
                            };
                            ThingDef capturedDef = info.animalDef;
                            exampleItem.OnActivate = () =>
                            {
                                penMarker.RemoveForceDisplayedAnimal(capturedDef);
                                string removedName = capturedDef.label?.CapitalizeFirst() ?? unknownAnimal;
                                TolkHelper.Speak("RimWorldAccess.Inspection.Tree.PenRemovedExample".Loc(removedName));
                                rebuildExampleSections();
                            };
                            AddChild(examplesCategory, exampleItem);
                        }
                    }
                }
                else
                {
                    examplesCategory.Label = "RimWorldAccess.Inspection.Tree.PenExamplesHeaderWithCount".Translate(0);
                }

                // Rebuild "Add Example Animal" children
                addExampleCategory.Children.Clear();
                var map = building.Map;
                if (map != null)
                {
                    var grazingAnimals = map.plantGrowthRateCalculator.GrazingAnimals;
                    var currentExamples = penMarker.ForceDisplayedAnimalDefs ?? new List<ThingDef>();
                    var available = new List<ThingDef>();
                    foreach (var animal in grazingAnimals)
                    {
                        if (!currentExamples.Contains(animal))
                            available.Add(animal);
                    }

                    if (available.Count == 0)
                    {
                        AddChild(addExampleCategory, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.Item,
                            Label = "RimWorldAccess.Inspection.Tree.PenNoMoreAnimalsAvailable".Translate(),
                            IndentLevel = indent + 1,
                            IsExpandable = false
                        });
                    }
                    else
                    {
                        string unknownAnimal = "RimWorldAccess.Inspection.Tree.PenUnknownAnimal".Translate();
                        foreach (var animal in available)
                        {
                            string animalName = animal.label?.CapitalizeFirst() ?? unknownAnimal;
                            ThingDef capturedAnimal = animal;
                            var animalChoice = new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.Action,
                                Label = animalName,
                                IndentLevel = indent + 1,
                                IsExpandable = false
                            };
                            animalChoice.OnActivate = () =>
                            {
                                penMarker.AddForceDisplayedAnimal(capturedAnimal);
                                TolkHelper.Speak("RimWorldAccess.Inspection.Tree.PenAddedExample".Loc(animalName));
                                rebuildExampleSections();
                            };
                            AddChild(addExampleCategory, animalChoice);
                        }
                    }
                }

                // Re-flatten the visible items list so the UI reflects changes
                WindowlessInspectionState.RefreshVisibleList();
            };

            // Wire up OnActivate to lazy-load via the shared rebuild
            examplesCategory.OnActivate = () =>
            {
                if (examplesCategory.Children.Count == 0)
                    rebuildExampleSections();
            };
            addExampleCategory.OnActivate = () =>
            {
                if (addExampleCategory.Children.Count == 0)
                    rebuildExampleSections();
            };

            // Update the example animals label with current count
            var initialDefs = penMarker.ForceDisplayedAnimalDefs;
            int initialCount = (initialDefs != null) ? initialDefs.Count : 0;
            examplesCategory.Label = "RimWorldAccess.Inspection.Tree.PenExamplesHeaderWithCount".Translate(initialCount);

            AddChild(parentItem, examplesCategory);
            AddChild(parentItem, addExampleCategory);

            // Stockpiled items breakdown
            var stockpileInfos = calculator.AllStockpiledInfos;
            if (stockpileInfos != null && stockpileInfos.Count > 0)
            {
                var foodCategory = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = "RimWorldAccess.Inspection.Tree.PenStockpiledItemsHeader".Translate(stockpileInfos.Count),
                    IndentLevel = indent,
                    IsExpandable = true,
                    IsExpanded = false
                };
                foodCategory.OnActivate = () =>
                {
                    if (foodCategory.Children.Count == 0)
                    {
                        string unknownFoodItem = "RimWorldAccess.Inspection.Tree.PenUnknownAnimal".Translate();
                        foreach (var info in stockpileInfos)
                        {
                            string foodLabel = info.itemDef?.label?.CapitalizeFirst() ?? unknownFoodItem;
                            float nutrition = info.totalNutritionAvailable;
                            string foodText = "RimWorldAccess.Inspection.Tree.PenStockpiledItemRow".Translate(
                                foodLabel, nutrition.ToString("F1"));

                            AddChild(foodCategory, new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.Item,
                                Label = foodText,
                                IndentLevel = indent + 1,
                                IsExpandable = false
                            });
                        }
                    }
                };
                AddChild(parentItem, foodCategory);
            }
        }

        /// <summary>
        /// Builds children for the Pen Auto-Cut category.
        /// Shows auto-cut toggle, Cut Now button, and plant filter access.
        /// </summary>
        private static void BuildPenAutoCutChildren(InspectionTreeItem parentItem, Building building)
        {
            var penMarker = building.TryGetComp<CompAnimalPenMarker>();
            if (penMarker == null)
                return;

            int indent = parentItem.IndentLevel + 1;
            bool penEnclosed = penMarker.PenState.Enclosed;

            // Auto-cut toggle
            string PenStateWord(bool enabled) => enabled
                ? "Enabled".Translate().ToString()
                : "Disabled".Translate().ToString();
            string PenUnenclosedSuffix(bool enclosed) => enclosed
                ? ""
                : "RimWorldAccess.Inspection.Tree.PenNotEnclosedSuffix".Translate().ToString();
            string PenAutoCutLabel(bool enabled, bool enclosed) =>
                "RimWorldAccess.Inspection.Tree.PenAutoCutPlantsLabel".Translate(
                    PenStateWord(enabled), PenUnenclosedSuffix(enclosed));

            var toggleItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                Label = PenAutoCutLabel(penMarker.autoCut, penEnclosed),
                IndentLevel = indent,
                IsExpandable = false
            };
            toggleItem.OnActivate = () =>
            {
                penMarker.autoCut = !penMarker.autoCut;
                string stateWord = PenStateWord(penMarker.autoCut);
                toggleItem.Label = PenAutoCutLabel(penMarker.autoCut, penMarker.PenState.Enclosed);
                TolkHelper.Speak("RimWorldAccess.Inspection.Tree.PenAutoCutToggleAnnouncement".Loc(stateWord));
            };
            AddChild(parentItem, toggleItem);

            // Cut Now button
            var cutNowItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                Label = "RimWorldAccess.Inspection.Tree.PenCutNowLabel".Translate(
                    "AutoCutNow".Translate(),
                    PenUnenclosedSuffix(penEnclosed)),
                IndentLevel = indent,
                IsExpandable = false
            };
            cutNowItem.OnActivate = () =>
            {
                if (penMarker.PenState.Enclosed)
                {
                    penMarker.DesignatePlantsToCut();
                    TolkHelper.Speak("RimWorldAccess.Inspection.Tree.PenDesignatedPlantsForCutting".Loc());
                }
                else
                {
                    TolkHelper.Speak("AutocutUnenclosedPen".Loc());
                }
            };
            AddChild(parentItem, cutNowItem);

            // Plant filter action
            var filterItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                Label = "RimWorldAccess.Inspection.Tree.PenPlantFilter".Translate(),
                IndentLevel = indent,
                IsExpandable = false
            };
            filterItem.OnActivate = () =>
            {
                var fixedFilter = penMarker.parent.Map?.animalPenManager?.GetFixedAutoCutFilter();
                ThingFilterMenuState.Open(penMarker.AutoCutFilter, fixedFilter, "RimWorldAccess.Inspection.Tree.PenAutoCutMenuTitle".Translate());
            };
            AddChild(parentItem, filterItem);
        }

        /// <summary>
        /// Builds children for the Linked Facilities category.
        /// Shows facility provider info, consumer info, linked buildings, and compatible facilities.
        /// </summary>
        private static void BuildFacilityChildren(InspectionTreeItem parentItem, Building building)
        {
            if (parentItem.Children.Count > 0)
                return;

            int indent = parentItem.IndentLevel + 1;
            var entries = FacilityLinkHelper.GetFacilityEntries(building);

            if (entries.Count == 0)
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Inspection.Tree.NoFacilityInfo".Translate(),
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            foreach (var entry in entries)
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = entry.Label,
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }
        }

        /// <summary>
        /// Builds children for the Meditation Focus category, listing nearby focus objects.
        /// </summary>
        private static void BuildMeditationFocusChildren(InspectionTreeItem parentItem, Building building)
        {
            if (parentItem.Children.Count > 0)
                return;

            int indent = parentItem.IndentLevel + 1;

            if (!building.Spawned)
                return;

            var map = building.Map;
            var center = building.Position;
            float searchRadius = MeditationUtility.FocusObjectSearchRadius;

            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(center, map, searchRadius, useCenter: false))
            {
                CompMeditationFocus focusComp = thing.TryGetComp<CompMeditationFocus>();
                if (focusComp == null)
                    continue;

                if (thing is Building_Throne)
                    continue;

                var sb = new System.Text.StringBuilder();
                sb.Append(thing.LabelCap.ToString().StripTags());

                // Focus types
                if (focusComp.Props.focusTypes != null && focusComp.Props.focusTypes.Count > 0)
                {
                    string types = string.Join(", ",
                        focusComp.Props.focusTypes.Select(f => f.label.CapitalizeFirst()));
                    sb.Append("RimWorldAccess.Inspection.Tree.MeditationFocusTypesSuffix".Translate(types));
                }

                // Focus strength
                float strength = thing.GetStatValue(StatDefOf.MeditationFocusStrength);
                sb.Append("RimWorldAccess.Inspection.Tree.MeditationFocusStrengthSuffix".Translate(strength.ToStringPercent()));

                // Distance
                float distance = center.DistanceTo(thing.Position);
                sb.Append("RimWorldAccess.Inspection.Tree.MeditationFocusDistanceSuffix".Translate(distance.ToString("F1")));

                // Line of sight
                if (!GenSight.LineOfSightToThing(center, thing, map))
                {
                    sb.Append("RimWorldAccess.Inspection.Tree.MeditationFocusNoLineOfSightSuffix".Translate());
                }

                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = sb.ToString(),
                    IndentLevel = indent,
                    IsExpandable = false,
                    LinkedDef = thing.def,
                    Data = thing
                });
            }

            if (parentItem.Children.Count == 0)
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Inspection.Tree.PenMeditationNoFocusObjects".Translate(searchRadius.ToString("F0")),
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }
        }

        /// <summary>
        /// Checks if there are any meditation focus objects near a building.
        /// </summary>
        private static bool HasNearbyMeditationFocusObjects(Building building)
        {
            if (!building.Spawned)
                return false;

            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(
                building.Position, building.Map, MeditationUtility.FocusObjectSearchRadius, useCenter: false))
            {
                if (thing is Building_Throne)
                    continue;

                if (thing.TryGetComp<CompMeditationFocus>() != null)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Builds children for the Feeding tab (Biotech babies).
        /// Two sections: auto-breastfeed feeder list and baby food consumables.
        /// Matches vanilla ITab_Pawn_Feeding exactly.
        /// </summary>
        private static void BuildFeedingChildren(InspectionTreeItem parentItem, Pawn baby, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return;

            if (!ModsConfig.BiotechActive)
                return;

            int indent = parentItem.IndentLevel + 1;
            bool isReadOnly = (mode == InspectionMode.ReadOnly);

            // === Auto-breastfeed section ===
            string autoHeader = "AutofeedSectionHeader".Translate().CapitalizeFirst();
            var autoSection = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = autoHeader,
                ExpandedLabel = autoHeader,
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };
            autoSection.OnActivate = () => BuildAutobreastfeedChildren(autoSection, baby, isReadOnly);
            AddChild(parentItem, autoSection);

            // === Baby Food Consumables section ===
            string foodHeader = "BabyFoodConsumables".Translate().CapitalizeFirst();
            var foodSection = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = foodHeader,
                ExpandedLabel = foodHeader,
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };
            foodSection.OnActivate = () => BuildBabyFoodChildren(foodSection, baby, isReadOnly);
            AddChild(parentItem, foodSection);
        }

        /// <summary>
        /// Builds the auto-breastfeed feeder list, matching vanilla's filtering and sorting.
        /// </summary>
        private static void BuildAutobreastfeedChildren(InspectionTreeItem parentItem, Pawn baby, bool isReadOnly)
        {
            if (parentItem.Children.Count > 0)
                return;

            int indent = parentItem.IndentLevel + 1;

            // Gather feeders using vanilla's exact filtering logic
            var feeders = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction
                .Where(f => f != baby
                    && f.RaceProps.Humanlike
                    && !ChildcareUtility.CanSuckle(f, out _)
                    && !f.IsWorkTypeDisabledByAge(WorkTypeDefOf.Childcare, out _))
                .ToList();

            if (feeders.Count == 0)
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "AutofeedNone".Translate(),
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            // Sort using vanilla's exact order: lactating first, then mother, father, surrogate
            Pawn mother = baby.GetMother();
            Pawn father = baby.GetFather();
            Pawn surrogate = baby.GetBirthParent();

            feeders.Sort((lhs, rhs) =>
            {
                int cmp = rhs.health.hediffSet.HasHediff(HediffDefOf.Lactating)
                    .CompareTo(lhs.health.hediffSet.HasHediff(HediffDefOf.Lactating));
                if (cmp != 0) return cmp;
                if (lhs == mother) return -1;
                if (rhs == mother) return 1;
                if (lhs == father) return -1;
                if (rhs == father) return 1;
                if (lhs == surrogate) return -1;
                if (rhs == surrogate) return 1;
                return 0;
            });

            foreach (var feeder in feeders)
            {
                var localFeeder = feeder;
                AutofeedMode currentMode = baby.mindState.AutofeedSetting(localFeeder);

                // Build feeder name with lactation status
                string feederName = localFeeder.LabelShortCap;
                var lactatingHediff = localFeeder.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Lactating);
                if (lactatingHediff != null)
                    feederName += $" ({lactatingHediff.LabelBaseCap})";

                // Build relation label
                string relation = "";
                if (localFeeder == mother)
                    relation = $", {PawnRelationDefOf.Parent.labelFemale.CapitalizeFirst()}";
                else if (localFeeder == father)
                    relation = $", {PawnRelationDefOf.Parent.label.CapitalizeFirst()}";
                else if (localFeeder == surrogate)
                    relation = $", {PawnRelationDefOf.ParentBirth.GetGenderSpecificLabelCap(localFeeder)}";

                string modeLabel = currentMode.Translate().CapitalizeFirst();
                string fullLabel = $"{feederName}: {modeLabel}{relation}";

                var feederItem = new InspectionTreeItem
                {
                    Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText : InspectionTreeItem.ItemType.Action,
                    Label = fullLabel,
                    Data = localFeeder,
                    IndentLevel = indent,
                    IsExpandable = false
                };

                if (!isReadOnly)
                {
                    feederItem.OnActivate = () =>
                    {
                        // Open float menu with all three mode options
                        var options = new List<FloatMenuOption>();
                        foreach (AutofeedMode modeOption in System.Enum.GetValues(typeof(AutofeedMode)))
                        {
                            var localMode = modeOption;
                            string optLabel = localMode.Translate().CapitalizeFirst();
                            string tooltip = localMode.GetTooltip(baby, localFeeder);
                            options.Add(new FloatMenuOption(
                                $"{optLabel}. {tooltip}",
                                () =>
                                {
                                    baby.mindState.SetAutofeeder(localFeeder, localMode);
                                    string newModeLabel = localMode.Translate().CapitalizeFirst();
                                    feederItem.Label = $"{feederName}: {newModeLabel}{relation}";
                                }));
                        }
                        WindowlessFloatMenuState.Open(options, false);
                    };
                }

                AddChild(parentItem, feederItem);
            }
        }

        /// <summary>
        /// Builds the baby food consumables list with toggleable food allowances.
        /// </summary>
        private static void BuildBabyFoodChildren(InspectionTreeItem parentItem, Pawn baby, bool isReadOnly)
        {
            if (parentItem.Children.Count > 0)
                return;

            int indent = parentItem.IndentLevel + 1;

            var foods = ITab_Pawn_Feeding.BabyConsumableFoods;
            if (foods == null || foods.Count == 0)
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "NoneLower".Translate(),
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            string allowedStr = "On".Translate().ToString();
            string notAllowedStr = "Off".Translate().ToString();

            foreach (var food in foods)
            {
                var localFood = food;
                bool allowed = baby.foodRestriction?.BabyFoodAllowed(localFood) ?? true;
                string stateStr = allowed ? allowedStr : notAllowedStr;

                var foodItem = new InspectionTreeItem
                {
                    Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText : InspectionTreeItem.ItemType.Action,
                    Label = $"{localFood.LabelCap}: {stateStr}",
                    IndentLevel = indent,
                    IsExpandable = false
                };

                if (!isReadOnly)
                {
                    foodItem.OnActivate = () =>
                    {
                        if (baby.foodRestriction == null) return;
                        bool current = baby.foodRestriction.BabyFoodAllowed(localFood);
                        baby.foodRestriction.SetBabyFoodAllowed(localFood, !current);
                        string newState = !current ? allowedStr : notAllowedStr;
                        foodItem.Label = $"{localFood.LabelCap}: {newState}";
                        TolkHelper.Speak(newState);
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    };
                }

                AddChild(parentItem, foodItem);
            }
        }

        /// <summary>
        /// Builds children for the Art tab. Read-only: title and description.
        /// </summary>
        private static void BuildArtChildren(InspectionTreeItem parentItem, Thing thing)
        {
            if (parentItem.Children.Count > 0)
                return;

            int indent = parentItem.IndentLevel + 1;

            // Handle minified (uninstalled) things
            Thing inner = thing is MinifiedThing mini ? mini.InnerThing : thing;
            var artComp = inner?.TryGetComp<CompArt>();

            if (artComp == null || !artComp.Active)
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "(" + "NoneLower".Translate() + ")",
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            // Title
            string title = artComp.Title;
            if (!title.NullOrEmpty())
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = title,
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }

            // Description
            string desc = artComp.GenerateImageDescription();
            if (!desc.NullOrEmpty())
            {
                desc = desc.StripTags().Trim();
                desc = System.Text.RegularExpressions.Regex.Replace(desc, @"\s+", " ");
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = desc,
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }
        }

        /// <summary>
        /// Builds children for the Book tab. Read-only: title, benefits, dangers, description.
        /// </summary>
        private static void BuildBookChildren(InspectionTreeItem parentItem, Book book)
        {
            if (parentItem.Children.Count > 0)
                return;

            int indent = parentItem.IndentLevel + 1;

            // Benefits
            if (book.BookComp?.Doers != null)
            {
                foreach (var doer in book.BookComp.Doers)
                {
                    string benefits = doer.GetBenefitsString();
                    if (!benefits.NullOrEmpty())
                    {
                        string cleaned = benefits.StripTags().Trim();
                        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ");
                        if (!cleaned.NullOrEmpty())
                        {
                            AddChild(parentItem, new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.DetailText,
                                Label = cleaned,
                                IndentLevel = indent,
                                IsExpandable = false
                            });
                        }
                    }
                }
            }

            // Dangers
            if (book.MentalBreakChancePerHour > 0f)
            {
                string dangerLabel = $"{"Dangers".Translate()}: {"BookMentalBreak".Translate()}, {book.MentalBreakChancePerHour.ToStringPercent("0.0")} {"PerHour".Translate()}";
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = dangerLabel,
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }

            // Description / flavor text
            string flavor = book.FlavorUI;
            if (!flavor.NullOrEmpty())
            {
                flavor = flavor.StripTags().Trim();
                flavor = System.Text.RegularExpressions.Regex.Replace(flavor, @"\s+", " ");
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = flavor,
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }
        }

        /// <summary>
        /// Builds children for the Contents (Books) tab on bookcases.
        /// Lists books with eject action.
        /// </summary>
        private static void BuildContentsBooksChildren(InspectionTreeItem parentItem, Building_Bookcase bookcase, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return;

            int indent = parentItem.IndentLevel + 1;
            bool isReadOnly = (mode == InspectionMode.ReadOnly);

            var books = bookcase.GetDirectlyHeldThings()?.OfType<Book>().ToList();
            if (books == null || books.Count == 0)
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "(" + "NoneLower".Translate() + ")",
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            foreach (var book in books)
            {
                var localBook = book;
                // DescriptionDetailed already starts with the title + quality, so use it as the full label
                string label = localBook.DescriptionDetailed ?? localBook.LabelCap;
                label = label.StripTags().Trim();
                label = System.Text.RegularExpressions.Regex.Replace(label, @"\s+", " ");

                var bookItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = label,
                    Data = localBook,
                    IndentLevel = indent,
                    IsExpandable = false
                };

                if (!isReadOnly)
                {
                    bookItem.OnDelete = () =>
                    {
                        // Eject book to adjacent walkable cell
                        IntVec3 dropCell = bookcase.Position;
                        if (bookcase.Spawned)
                        {
                            foreach (var cell in bookcase.OccupiedRect().AdjacentCells)
                            {
                                if (cell.Walkable(bookcase.Map))
                                {
                                    dropCell = cell;
                                    break;
                                }
                            }
                        }

                        bookcase.GetDirectlyHeldThings().TryDrop(localBook, dropCell, bookcase.Map, ThingPlaceMode.Near, 1, out var dropped);
                        if (dropped?.TryGetComp<CompForbiddable>() is CompForbiddable forbiddable)
                            forbiddable.Forbidden = true;

                        TolkHelper.Speak("RimWorldAccess.Inspection.Tree.BookEjected".Loc(
                            "EjectBookTooltip".Translate(), localBook.LabelCap));
                        SoundDefOf.Click.PlayOneShotOnCamera();

                        // Rebuild children
                        parentItem.Children.Clear();
                        BuildContentsBooksChildren(parentItem, bookcase, mode);
                    };
                }

                AddChild(parentItem, bookItem);
            }
        }

        /// <summary>
        /// Builds children for the Wind Turbine Auto-Cut tab.
        /// Toggle for auto-cut, Cut Now action, and plant filter.
        /// </summary>
        private static void BuildWindTurbineAutoCutChildren(InspectionTreeItem parentItem, Building building, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return;

            var autoCut = building.TryGetComp<CompAutoCut>();
            if (autoCut == null)
                return;

            int indent = parentItem.IndentLevel + 1;
            bool isReadOnly = (mode == InspectionMode.ReadOnly);

            // Auto-cut toggle
            string toggleLabel = "WindTurbineAutoCut_EnabledCheckbox".Translate();
            string stateStr = autoCut.autoCut ? "On".Translate().ToString() : "Off".Translate().ToString();

            var toggleItem = new InspectionTreeItem
            {
                Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText : InspectionTreeItem.ItemType.Action,
                Label = $"{toggleLabel}: {stateStr}",
                IndentLevel = indent,
                IsExpandable = false
            };

            if (!isReadOnly)
            {
                toggleItem.OnActivate = () =>
                {
                    autoCut.autoCut = !autoCut.autoCut;
                    string newState = autoCut.autoCut ? "On".Translate().ToString() : "Off".Translate().ToString();
                    toggleItem.Label = $"{toggleLabel}: {newState}";
                    TolkHelper.Speak(newState);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                };
            }
            AddChild(parentItem, toggleItem);

            // Cut Now action
            if (!isReadOnly)
            {
                var cutNowItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = "AutoCutNow".Translate(),
                    IndentLevel = indent,
                    IsExpandable = false
                };
                cutNowItem.OnActivate = () =>
                {
                    autoCut.DesignatePlantsToCut();
                    TolkHelper.Speak("AutoCutNow".Loc());
                    SoundDefOf.Designate_PlanAdd.PlayOneShotOnCamera();
                };
                AddChild(parentItem, cutNowItem);
            }
        }

        /// <summary>
        /// Builds children for the Guest tab (non-prisoner, non-slave guests).
        /// Shows medical care selector.
        /// </summary>
        private static void BuildGuestChildren(InspectionTreeItem parentItem, Pawn pawn, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return;

            int indent = parentItem.IndentLevel + 1;
            bool isReadOnly = (mode == InspectionMode.ReadOnly);

            if (pawn.playerSettings == null)
                return;

            // Medical care selector
            string careLabel = "AllowMedicine".Translate();
            string currentCare = pawn.playerSettings.medCare.GetLabel();

            var careItem = new InspectionTreeItem
            {
                Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText : InspectionTreeItem.ItemType.Action,
                Label = $"{careLabel}: {currentCare}",
                IndentLevel = indent,
                IsExpandable = false
            };

            if (!isReadOnly)
            {
                careItem.OnActivate = () =>
                {
                    // Open float menu with all medical care options
                    var options = new List<FloatMenuOption>();
                    foreach (MedicalCareCategory care in System.Enum.GetValues(typeof(MedicalCareCategory)))
                    {
                        var localCare = care;
                        options.Add(new FloatMenuOption(localCare.GetLabel(), () =>
                        {
                            pawn.playerSettings.medCare = localCare;
                            careItem.Label = $"{careLabel}: {localCare.GetLabel()}";
                        }));
                    }
                    WindowlessFloatMenuState.Open(options, false);
                };
            }
            AddChild(parentItem, careItem);
        }

        /// <summary>
        /// Builds children for the Contents (Transporter) tab.
        /// Two sections: items to load and contained items.
        /// </summary>
        private static void BuildContentsTransporterChildren(InspectionTreeItem parentItem, Building building, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return;

            var transporter = building.TryGetComp<CompTransporter>();
            if (transporter == null)
                return;

            int indent = parentItem.IndentLevel + 1;
            bool isReadOnly = (mode == InspectionMode.ReadOnly);

            // Items to Load section
            string toLoadHeader = "ItemsToLoad".Translate();
            var toLoadSection = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = toLoadHeader,
                ExpandedLabel = toLoadHeader,
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };
            toLoadSection.OnActivate = () =>
            {
                if (toLoadSection.Children.Count > 0) return;
                int childIndent = toLoadSection.IndentLevel + 1;

                if (transporter.leftToLoad != null)
                {
                    foreach (var transferable in transporter.leftToLoad)
                    {
                        if (transferable.CountToTransfer <= 0 || !transferable.HasAnyThing)
                            continue;

                        string itemLabel = $"{transferable.ThingDef.LabelCap} x{transferable.CountToTransfer}";
                        AddChild(toLoadSection, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = itemLabel,
                            IndentLevel = childIndent,
                            IsExpandable = false
                        });
                    }
                }

                if (toLoadSection.Children.Count == 0)
                {
                    AddChild(toLoadSection, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = "(" + "NoneLower".Translate() + ")",
                        IndentLevel = childIndent,
                        IsExpandable = false
                    });
                }
            };
            AddChild(parentItem, toLoadSection);

            // Contained Items section
            string containedHeader = "ContainedItems".Translate();
            var containedSection = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = containedHeader,
                ExpandedLabel = containedHeader,
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };
            containedSection.OnActivate = () =>
            {
                if (containedSection.Children.Count > 0) return;
                int childIndent = containedSection.IndentLevel + 1;

                if (transporter.innerContainer != null && transporter.innerContainer.Count > 0)
                {
                    foreach (var thing in transporter.innerContainer.ToList())
                    {
                        var localThing = thing;
                        string itemLabel = localThing is Pawn p ? p.LabelShortCap : localThing.LabelCap;
                        if (localThing.stackCount > 1)
                            itemLabel += $" x{localThing.stackCount}";

                        var containedItem = new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.Item,
                            Label = itemLabel,
                            Data = localThing,
                            IndentLevel = childIndent,
                            IsExpandable = false
                        };

                        if (!isReadOnly)
                        {
                            containedItem.OnDelete = () =>
                            {
                                GenDrop.TryDropSpawn(localThing.SplitOff(localThing.stackCount),
                                    building.Position, building.Map, ThingPlaceMode.Near, out _);
                                transporter.Notify_ThingRemoved(localThing);
                                TolkHelper.Speak(itemLabel);
                                SoundDefOf.Click.PlayOneShotOnCamera();

                                // Rebuild
                                containedSection.Children.Clear();
                                containedSection.OnActivate();
                            };
                        }

                        AddChild(containedSection, containedItem);
                    }
                }

                if (containedSection.Children.Count == 0)
                {
                    AddChild(containedSection, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = "(" + "NoneLower".Translate() + ")",
                        IndentLevel = childIndent,
                        IsExpandable = false
                    });
                }
            };
            AddChild(parentItem, containedSection);
        }
    }
}
