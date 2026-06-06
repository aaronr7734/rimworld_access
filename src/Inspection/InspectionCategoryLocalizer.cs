using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Maps the English category identifiers used throughout the inspection
    /// system to localized display strings.
    ///
    /// The English identifiers (e.g. "Health", "Work Priorities") remain the
    /// internal identity of a category: they are used for switch dispatch in
    /// <see cref="InspectionInfoHelper.GetCategoryInfo"/>, for the
    /// <c>OriginalCategoryName</c> field, and for the de-duplication checks in
    /// <see cref="InspectionInfoHelper.GetDynamicCategories"/>. This mapping is
    /// applied only at the point a category name is rendered for the user, so
    /// dispatch never depends on a translated string.
    ///
    /// Mirrors the ScannerNameLocalizer pattern used by the map scanner.
    /// Category identifiers that are not in the table (for example, dynamically
    /// discovered component names whose label already comes from the game) fall
    /// through unchanged.
    /// </summary>
    public static class InspectionCategoryLocalizer
    {
        private static readonly Dictionary<string, string> categoryKeys = new Dictionary<string, string>
        {
            { "Overview", "RimWorldAccess.Inspection.CategoryName.Overview" },
            { "Health", "RimWorldAccess.Inspection.CategoryName.Health" },
            { "Needs", "RimWorldAccess.Inspection.CategoryName.Needs" },
            { "Mood", "RimWorldAccess.Inspection.CategoryName.Mood" },
            { "Gear", "RimWorldAccess.Inspection.CategoryName.Gear" },
            { "Skills", "RimWorldAccess.Inspection.CategoryName.Skills" },
            { "Social", "RimWorldAccess.Inspection.CategoryName.Social" },
            { "Character", "RimWorldAccess.Inspection.CategoryName.Character" },
            { "Work Priorities", "RimWorldAccess.Inspection.CategoryName.WorkPriorities" },
            { "Log", "RimWorldAccess.Inspection.CategoryName.Log" },
            { "Job Queue", "RimWorldAccess.Inspection.CategoryName.JobQueue" },
            { "Prisoner", "RimWorldAccess.Inspection.CategoryName.Prisoner" },
            { "Slave", "RimWorldAccess.Inspection.CategoryName.Slave" },
            { "Guest", "RimWorldAccess.Inspection.CategoryName.Guest" },
            { "Feeding", "RimWorldAccess.Inspection.CategoryName.Feeding" },
            { "Forming Caravan", "RimWorldAccess.Inspection.CategoryName.FormingCaravan" },
            { "Training", "RimWorldAccess.Inspection.CategoryName.Training" },
            { "Bills", "RimWorldAccess.Inspection.CategoryName.Bills" },
            { "Bed Assignment", "RimWorldAccess.Inspection.CategoryName.BedAssignment" },
            { "Owner Assignment", "RimWorldAccess.Inspection.CategoryName.OwnerAssignment" },
            { "Meditation Focus", "RimWorldAccess.Inspection.CategoryName.MeditationFocus" },
            { "Temperature", "RimWorldAccess.Inspection.CategoryName.Temperature" },
            { "Storage", "RimWorldAccess.Inspection.CategoryName.Storage" },
            { "Nutrition Storage", "RimWorldAccess.Inspection.CategoryName.NutritionStorage" },
            { "Auto-Cut Plants", "RimWorldAccess.Inspection.CategoryName.AutoCutPlants" },
            { "Shells", "RimWorldAccess.Inspection.CategoryName.Shells" },
            { "Plant Selection", "RimWorldAccess.Inspection.CategoryName.PlantSelection" },
            { "Power", "RimWorldAccess.Inspection.CategoryName.Power" },
            { "Art", "RimWorldAccess.Inspection.CategoryName.Art" },
            { "Contents", "RimWorldAccess.Inspection.CategoryName.Contents" },
            { "Books", "RimWorldAccess.Inspection.CategoryName.Books" },
            { "Book", "RimWorldAccess.Inspection.CategoryName.Book" },
            { "Genepacks", "RimWorldAccess.Inspection.CategoryName.Genepacks" },
            { "Genes", "RimWorldAccess.Inspection.CategoryName.Genes" },
            { "Pregnancy Genes", "RimWorldAccess.Inspection.CategoryName.PregnancyGenes" },
            { "Entity", "RimWorldAccess.Inspection.CategoryName.Entity" },
            { "Study Notes", "RimWorldAccess.Inspection.CategoryName.StudyNotes" },
            { "Fishing", "RimWorldAccess.Inspection.CategoryName.Fishing" },
            { "Pen", "RimWorldAccess.Inspection.CategoryName.Pen" },
            { "Pen Animals", "RimWorldAccess.Inspection.CategoryName.PenAnimals" },
            { "Pen Food", "RimWorldAccess.Inspection.CategoryName.PenFood" },
            { "Pen Auto-Cut", "RimWorldAccess.Inspection.CategoryName.PenAutoCut" },
            { "Linked Facilities", "RimWorldAccess.Inspection.CategoryName.LinkedFacilities" },
            { "Rename", "RimWorldAccess.Inspection.CategoryName.Rename" },
            { "Growth Info", "RimWorldAccess.Inspection.CategoryName.GrowthInfo" },
            { "Plant Info", "RimWorldAccess.Inspection.CategoryName.PlantInfo" },
            { "Power Control", "RimWorldAccess.Inspection.CategoryName.PowerControl" },
            { "Breakdown Status", "RimWorldAccess.Inspection.CategoryName.BreakdownStatus" },
            { "Door Controls", "RimWorldAccess.Inspection.CategoryName.DoorControls" },
            { "Forbid Controls", "RimWorldAccess.Inspection.CategoryName.ForbidControls" },
            { "Unknown", "RimWorldAccess.Inspection.CategoryName.Unknown" },
        };

        /// <summary>
        /// Returns the localized display name for an English category identifier.
        /// Identifiers not present in the table are returned unchanged.
        /// </summary>
        public static string Localize(string englishCategoryName)
        {
            if (string.IsNullOrEmpty(englishCategoryName))
                return englishCategoryName;
            if (categoryKeys.TryGetValue(englishCategoryName, out string translationKey))
                return translationKey.Translate();
            return englishCategoryName;
        }
    }
}
