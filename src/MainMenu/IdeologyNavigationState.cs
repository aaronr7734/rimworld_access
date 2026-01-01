using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation state for the ideology selection page (Page_ChooseIdeoPreset).
    /// Similar to ScenarioNavigationState but handles the more complex ideology preset system.
    /// </summary>
    public static class IdeologyNavigationState
    {
        private static bool initialized = false;

        // Main selection type
        private enum SelectionType
        {
            Classic,
            CustomFluid,
            CustomFixed,
            Load,
            Preset
        }

        private static SelectionType currentSelectionType = SelectionType.Classic;

        // For preset browsing
        private static List<IdeoPresetDef> flatPresetList = new List<IdeoPresetDef>();
        private static int selectedPresetIndex = 0;

        public static void Initialize()
        {
            if (!initialized || flatPresetList.Count == 0)
            {
                // Build flat list of all ideology presets (excluding special categories)
                flatPresetList.Clear();

                var categories = DefDatabase<IdeoPresetCategoryDef>.AllDefsListForReading
                    .Where(c => c != IdeoPresetCategoryDefOf.Classic &&
                                c != IdeoPresetCategoryDefOf.Custom &&
                                c != IdeoPresetCategoryDefOf.Fluid);

                foreach (var category in categories)
                {
                    var presetsInCategory = DefDatabase<IdeoPresetDef>.AllDefs
                        .Where(i => i.categoryDef == category)
                        .ToList();

                    flatPresetList.AddRange(presetsInCategory);
                }

                currentSelectionType = SelectionType.Classic;
                selectedPresetIndex = 0;
                initialized = true;
            }
        }

        public static void Reset()
        {
            initialized = false;
            currentSelectionType = SelectionType.Classic;
            selectedPresetIndex = 0;
            flatPresetList.Clear();
        }

        public static void NavigateUp()
        {
            if (currentSelectionType == SelectionType.Preset)
            {
                // Navigate through presets
                if (flatPresetList.Count == 0) return;

                selectedPresetIndex--;
                if (selectedPresetIndex < 0)
                    selectedPresetIndex = flatPresetList.Count - 1;

                AnnounceCurrentPreset();
            }
            else
            {
                // Navigate through main options
                int currentIndex = (int)currentSelectionType;
                currentIndex--;
                if (currentIndex < 0)
                    currentIndex = 4; // Preset is last
                currentSelectionType = (SelectionType)currentIndex;

                AnnounceCurrentSelection();
            }
        }

        public static void NavigateDown()
        {
            if (currentSelectionType == SelectionType.Preset)
            {
                // Navigate through presets
                if (flatPresetList.Count == 0) return;

                selectedPresetIndex++;
                if (selectedPresetIndex >= flatPresetList.Count)
                    selectedPresetIndex = 0;

                AnnounceCurrentPreset();
            }
            else
            {
                // Navigate through main options
                int currentIndex = (int)currentSelectionType;
                currentIndex++;
                if (currentIndex > 4) // Preset is 4
                    currentIndex = 0;
                currentSelectionType = (SelectionType)currentIndex;

                AnnounceCurrentSelection();
            }
        }

        public static void TogglePresetBrowsing()
        {
            if (currentSelectionType == SelectionType.Preset)
            {
                // Exit preset browsing - go back to Classic
                currentSelectionType = SelectionType.Classic;
                AnnounceCurrentSelection();
            }
            else
            {
                // Enter preset browsing
                if (flatPresetList.Count > 0)
                {
                    currentSelectionType = SelectionType.Preset;
                    selectedPresetIndex = 0;
                    TolkHelper.Speak($"Browsing ideology presets. {flatPresetList.Count} available. Press Tab to return to main options.");
                    AnnounceCurrentPreset();
                }
                else
                {
                    TolkHelper.Speak("No ideology presets available");
                }
            }
        }

        private static void AnnounceCurrentSelection()
        {
            string announcement = "";

            switch (currentSelectionType)
            {
                case SelectionType.Classic:
                    announcement = $"Play Classic - {IdeoPresetCategoryDefOf.Classic.description}";
                    break;
                case SelectionType.CustomFluid:
                    announcement = $"Create Custom Fluid Ideology - {IdeoPresetCategoryDefOf.Fluid.description}";
                    break;
                case SelectionType.CustomFixed:
                    announcement = $"Create Custom Fixed Ideology - {IdeoPresetCategoryDefOf.Custom.description}";
                    break;
                case SelectionType.Load:
                    announcement = "Load Saved Ideology - Load a previously saved ideology from disk";
                    break;
                case SelectionType.Preset:
                    announcement = "Browse Ideology Presets";
                    break;
            }

            TolkHelper.Speak(announcement);
        }

        private static void AnnounceCurrentPreset()
        {
            if (flatPresetList.Count == 0 || selectedPresetIndex < 0 || selectedPresetIndex >= flatPresetList.Count)
                return;

            var preset = flatPresetList[selectedPresetIndex];
            string categoryName = preset.categoryDef?.LabelCap ?? "Unknown";
            string announcement = $"{preset.LabelCap} - {categoryName} - {preset.description} ({selectedPresetIndex + 1} of {flatPresetList.Count})";

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Gets the current selection type string for updating the page.
        /// </summary>
        public static string GetCurrentSelectionTypeString()
        {
            return currentSelectionType.ToString();
        }

        /// <summary>
        /// Gets the currently selected ideology preset (or null if not browsing presets).
        /// </summary>
        public static IdeoPresetDef GetSelectedPreset()
        {
            if (currentSelectionType != SelectionType.Preset)
                return null;

            if (selectedPresetIndex < 0 || selectedPresetIndex >= flatPresetList.Count)
                return null;

            return flatPresetList[selectedPresetIndex];
        }

        public static int PresetCount => flatPresetList.Count;
    }
}
