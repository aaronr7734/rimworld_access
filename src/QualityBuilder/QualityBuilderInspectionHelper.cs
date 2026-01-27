using System;
using System.Collections.Generic;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Provides inspection support for Quality Builder mod.
    /// Adds Quality Builder category to inspection tree when mod is active.
    /// </summary>
    public static class QualityBuilderInspectionHelper
    {
        /// <summary>
        /// Gets the Quality Builder category info for a thing if applicable.
        /// Returns null if mod not active or thing cannot have quality designations.
        /// </summary>
        public static TabCategoryInfo GetQualityBuilderCategory(Thing thing)
        {
            if (!ModDetectionHelper.IsQualityBuilderActive() ||
                !ModDetectionHelper.CanHaveQualityDesignation(thing))
            {
                return null;
            }

            return new TabCategoryInfo
            {
                Name = "Quality Builder",
                Tab = null, // Synthetic category, not a real RimWorld tab
                Handler = TabHandlerType.Action,
                IsKnown = true,
                OriginalCategoryName = "Quality Builder"
            };
        }

        /// <summary>
        /// Opens the Quality Builder menu for a thing.
        /// Called when user selects the Quality Builder category in inspection tree.
        /// </summary>
        public static void OpenQualityBuilderMenu(Thing thing)
        {
            if (!ModDetectionHelper.IsQualityBuilderActive() || thing == null)
                return;

            bool opened = QualityBuilderMenuState.Open(thing);
            if (!opened)
            {
                TolkHelper.Speak("Cannot open quality settings for this object", SpeechPriority.Normal);
            }
        }

        /// <summary>
        /// Gets the inspection string for Quality Builder info.
        /// Used when displaying quality information in the inspection tree.
        /// </summary>
        public static string GetQualityBuilderInfoString(Thing thing)
        {
            if (!ModDetectionHelper.IsQualityBuilderActive() || thing == null)
                return string.Empty;

            var currentLevel = ModDetectionHelper.GetCurrentQualityLevel(thing);
            if (string.IsNullOrEmpty(currentLevel))
                return "No quality requirement set";

            int? category = ModDetectionHelper.GetCurrentQualityCategory(thing);
            if (!category.HasValue)
                return "Unknown quality level";

            string label = ModDetectionHelper.GetQualityLabel(category.Value);
            return $"Required quality: {label}";
        }

        /// <summary>
        /// Builds the tree item for Quality Builder category.
        /// Used by InspectionTreeBuilder to create the navigation tree.
        /// </summary>
        public static InspectionTreeItem BuildQualityBuilderTreeItem(Thing thing)
        {
            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                Label = "Quality Builder",
                Description = GetQualityBuilderInfoString(thing),
                IsExpandable = false,
                OnActivate = () => OpenQualityBuilderMenu(thing)
            };

            return item;
        }

        /// <summary>
        /// Checks if a thing currently has a Quality Builder designation.
        /// Used for quick status display.
        /// </summary>
        public static bool HasActiveQualityDesignation(Thing thing)
        {
            return ModDetectionHelper.HasQualityBuilderDesignation(thing);
        }

        /// <summary>
        /// Gets the quick status announcement for quality.
        /// Used when announcing tile info or quick inspections.
        /// </summary>
        public static string GetQualityStatusAnnouncement(Thing thing)
        {
            if (!ModDetectionHelper.IsQualityBuilderActive() || thing == null)
                return string.Empty;

            var currentLevel = ModDetectionHelper.GetCurrentQualityLevel(thing);
            if (string.IsNullOrEmpty(currentLevel))
                return string.Empty;

            int? category = ModDetectionHelper.GetCurrentQualityCategory(thing);
            if (!category.HasValue)
                return string.Empty;

            string label = ModDetectionHelper.GetQualityLabel(category.Value);
            return $", Quality requirement: {label}";
        }

        /// <summary>
        /// Toggles quality requirement on/off for a thing.
        /// If no quality is set, defaults to Normal. If quality is set, removes it.
        /// </summary>
        public static void ToggleQualityRequirement(Thing thing)
        {
            if (!ModDetectionHelper.IsQualityBuilderActive() || thing == null)
                return;

            bool hasDesignation = ModDetectionHelper.HasQualityBuilderDesignation(thing);

            if (hasDesignation)
            {
                // Remove quality requirement
                bool success = ModDetectionHelper.SetQualityDesignation(thing, null);
                if (success)
                {
                    TolkHelper.Speak("Quality requirement removed", SpeechPriority.Normal);
                }
                else
                {
                    TolkHelper.Speak("Failed to remove quality requirement", SpeechPriority.High);
                }
            }
            else
            {
                // Add default quality requirement (Normal)
                bool success = ModDetectionHelper.SetQualityDesignation(thing, 3); // Normal quality
                if (success)
                {
                    TolkHelper.Speak("Normal quality requirement set", SpeechPriority.Normal);
                }
                else
                {
                    TolkHelper.Speak("Failed to set quality requirement", SpeechPriority.High);
                }
            }
        }

        /// <summary>
        /// Gets all quality levels as tree items for selection.
        /// Used when building the quality selection menu tree.
        /// </summary>
        public static List<InspectionTreeItem> GetQualityLevelTreeItems(Thing thing)
        {
            var items = new List<InspectionTreeItem>();

            if (!ModDetectionHelper.IsQualityBuilderActive() || thing == null)
                return items;

            // Add "Remove quality" option
            items.Add(new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                Label = "Remove quality requirement",
                Description = "Remove any quality requirement from this blueprint/frame",
                OnActivate = () =>
                {
                    ModDetectionHelper.SetQualityDesignation(thing, null);
                    TolkHelper.Speak("Quality requirement removed", SpeechPriority.Normal);
                }
            });

            // Add quality level options
            for (int quality = 2; quality <= 7; quality++) // Poor (2) to Legendary (7)
            {
                int qualityLevel = quality; // Capture for lambda
                string label = ModDetectionHelper.GetQualityLabel(qualityLevel);
                string description = ModDetectionHelper.GetQualityDescription(qualityLevel);

                items.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = label,
                    Description = description,
                    OnActivate = () =>
                    {
                        bool success = ModDetectionHelper.SetQualityDesignation(thing, qualityLevel);
                        if (success)
                        {
                            TolkHelper.Speak($"Quality set to {label}", SpeechPriority.Normal);
                        }
                        else
                        {
                            TolkHelper.Speak($"Failed to set quality to {label}", SpeechPriority.High);
                        }
                    }
                });
            }

            return items;
        }
    }
}
