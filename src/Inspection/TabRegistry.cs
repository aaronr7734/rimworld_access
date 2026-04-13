using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Defines how a tab should be handled for keyboard navigation.
    /// </summary>
    public enum TabHandlerType
    {
        /// <summary>Rich navigation with expandable sub-items (Health, Gear, Skills, etc.)</summary>
        RichNavigation,
        /// <summary>Opens a separate action menu (Bills, Storage, Prisoner, etc.)</summary>
        Action,
        /// <summary>Basic display using GetInspectString() for unknown tabs</summary>
        BasicInspectString
    }

    /// <summary>
    /// Contains information about a tab category for building the inspection tree.
    /// </summary>
    public class TabCategoryInfo
    {
        /// <summary>Display name of the category</summary>
        public string Name { get; set; }

        /// <summary>The underlying RimWorld tab (null for synthetic categories like Overview)</summary>
        public InspectTabBase Tab { get; set; }

        /// <summary>How this tab should be handled</summary>
        public TabHandlerType Handler { get; set; }

        /// <summary>Whether this is a known tab with rich support (vs fallback)</summary>
        public bool IsKnown { get; set; }

        /// <summary>The original category name used by existing helpers (for mapping)</summary>
        public string OriginalCategoryName { get; set; }
    }

    /// <summary>
    /// Central registry for mapping RimWorld inspect tabs to accessibility handlers.
    /// Provides dynamic tab discovery with graceful fallback for unknown tabs.
    /// </summary>
    public static class TabRegistry
    {
        // Maps tab type names to friendly category names
        private static readonly Dictionary<string, string> tabTypeToCategory = new Dictionary<string, string>
        {
            // Pawn tabs
            { "ITab_Pawn_Health", "Health" },
            { "ITab_Pawn_Needs", "Needs" },
            { "ITab_Pawn_Character", "Character" },
            { "ITab_Pawn_Gear", "Gear" },
            { "ITab_Pawn_Social", "Social" },
            { "ITab_Pawn_Training", "Training" },
            { "ITab_Pawn_Log", "Log" },
            { "ITab_Pawn_Prisoner", "Prisoner" },
            { "ITab_Pawn_Slave", "Slave" },
            { "ITab_Pawn_Guest", "Guest" },
            { "ITab_Pawn_Visitor", "Guest" },
            { "ITab_Pawn_Feeding", "Feeding" },
            { "ITab_Pawn_FormingCaravan", "Forming Caravan" },

            // Building tabs
            { "ITab_Bills", "Bills" },
            { "ITab_Storage", "Storage" },
            { "ITab_BiosculpterNutritionStorage", "Nutrition Storage" },
            { "ITab_WindTurbineAutoCut", "Auto-Cut Plants" },
            { "ITab_Art", "Art" },

            // Content tabs
            { "ITab_ContentsBase", "Contents" },
            { "ITab_ContentsCasket", "Contents" },
            { "ITab_ContentsTransporter", "Contents" },
            { "ITab_ContentsBooks", "Books" },
            { "ITab_ContentsGenepackHolder", "Genepacks" },
            { "ITab_ContentsOutfitStand", "Contents" },
            { "ITab_ContentsMapPortal", "Contents" },

            // DLC tabs - Biotech
            { "ITab_Genes", "Genes" },
            { "ITab_GenesPregnancy", "Pregnancy Genes" },

            // DLC tabs - Ideology/Anomaly
            { "ITab_Entity", "Entity" },
            { "ITab_StudyNotes", "Study Notes" },
            { "ITab_StudyNotesUnnaturalCorpse", "Study Notes" },
            { "ITab_StudyNotesVoidMonolith", "Study Notes" },

            // DLC tabs - Odyssey
            { "ITab_Fishing", "Fishing" },
            { "ITab_Book", "Book" },

            // Pen tabs
            { "ITab_PenBase", "Pen" },
            { "ITab_PenAnimals", "Pen Animals" },
            { "ITab_PenFood", "Pen Food" },
            { "ITab_PenAutoCut", "Pen Auto-Cut" },
        };

        // Maps tab type names to handler types
        private static readonly Dictionary<string, TabHandlerType> tabTypeToHandler = new Dictionary<string, TabHandlerType>
        {
            // Pawn tabs with rich navigation
            { "ITab_Pawn_Health", TabHandlerType.RichNavigation },
            { "ITab_Pawn_Needs", TabHandlerType.RichNavigation },
            { "ITab_Pawn_Character", TabHandlerType.RichNavigation },
            { "ITab_Pawn_Gear", TabHandlerType.RichNavigation },
            { "ITab_Pawn_Social", TabHandlerType.RichNavigation },
            { "ITab_Pawn_Training", TabHandlerType.RichNavigation },
            { "ITab_Pawn_Log", TabHandlerType.RichNavigation },

            // Action tabs (open separate menus)
            { "ITab_Bills", TabHandlerType.Action },
            { "ITab_Storage", TabHandlerType.Action },
            { "ITab_BiosculpterNutritionStorage", TabHandlerType.Action },
            { "ITab_Shells", TabHandlerType.Action },
            { "ITab_Pawn_Prisoner", TabHandlerType.Action },
            { "ITab_Pawn_Slave", TabHandlerType.Action },

            // Basic info tabs (use GetInspectString)
            { "ITab_Pawn_Guest", TabHandlerType.RichNavigation },
            { "ITab_Pawn_Visitor", TabHandlerType.RichNavigation },
            { "ITab_Pawn_Feeding", TabHandlerType.RichNavigation },
            { "ITab_Pawn_FormingCaravan", TabHandlerType.BasicInspectString },
            { "ITab_WindTurbineAutoCut", TabHandlerType.RichNavigation },
            { "ITab_Art", TabHandlerType.RichNavigation },
            { "ITab_ContentsBase", TabHandlerType.BasicInspectString },
            { "ITab_ContentsCasket", TabHandlerType.BasicInspectString },
            { "ITab_ContentsTransporter", TabHandlerType.RichNavigation },
            { "ITab_ContentsBooks", TabHandlerType.RichNavigation },
            { "ITab_ContentsGenepackHolder", TabHandlerType.BasicInspectString },
            { "ITab_ContentsOutfitStand", TabHandlerType.BasicInspectString },
            { "ITab_ContentsMapPortal", TabHandlerType.BasicInspectString },
            { "ITab_Genes", TabHandlerType.RichNavigation },
            { "ITab_GenesPregnancy", TabHandlerType.BasicInspectString },
            { "ITab_Entity", TabHandlerType.BasicInspectString },
            { "ITab_StudyNotes", TabHandlerType.BasicInspectString },
            { "ITab_StudyNotesUnnaturalCorpse", TabHandlerType.BasicInspectString },
            { "ITab_StudyNotesVoidMonolith", TabHandlerType.BasicInspectString },
            { "ITab_Fishing", TabHandlerType.Action },
            { "ITab_Book", TabHandlerType.RichNavigation },
            { "ITab_PenBase", TabHandlerType.BasicInspectString },
            { "ITab_PenAnimals", TabHandlerType.Action },
            { "ITab_PenFood", TabHandlerType.RichNavigation },
            { "ITab_PenAutoCut", TabHandlerType.RichNavigation },
        };

        // Maps original category names used in InspectionInfoHelper to handler types
        // This allows BuildCategoryItem to know how to handle existing categories
        private static readonly Dictionary<string, TabHandlerType> categoryNameToHandler = new Dictionary<string, TabHandlerType>
        {
            // Rich navigation categories
            { "Health", TabHandlerType.RichNavigation },
            { "Needs", TabHandlerType.RichNavigation },
            { "Mood", TabHandlerType.RichNavigation },
            { "Character", TabHandlerType.RichNavigation },
            { "Gear", TabHandlerType.RichNavigation },
            { "Skills", TabHandlerType.RichNavigation },
            { "Social", TabHandlerType.RichNavigation },
            { "Training", TabHandlerType.RichNavigation },
            { "Log", TabHandlerType.RichNavigation },
            { "Job Queue", TabHandlerType.RichNavigation },
            { "Genes", TabHandlerType.RichNavigation },

            // Action categories
            { "Bills", TabHandlerType.Action },
            { "Storage", TabHandlerType.Action },
            { "Bed Assignment", TabHandlerType.Action },
            { "Temperature", TabHandlerType.Action },
            { "Plant Selection", TabHandlerType.Action },
            { "Prisoner", TabHandlerType.Action },
            { "Fishing", TabHandlerType.Action },

            // Synthetic categories (not real tabs)
            { "Overview", TabHandlerType.RichNavigation },
            { "Growth Info", TabHandlerType.RichNavigation },
            { "Power", TabHandlerType.BasicInspectString },
            { "Work Priorities", TabHandlerType.BasicInspectString },
            { "Plant Info", TabHandlerType.RichNavigation },
        };

        /// <summary>
        /// Gets the user-friendly category name for a tab.
        /// Uses the game's translated label for i18n support.
        /// </summary>
        public static string GetCategoryNameForTab(InspectTabBase tab)
        {
            if (tab == null)
                return "Unknown";

            // First priority: use our explicit mapping for known tabs
            // This ensures dispatch works correctly (e.g., ITab_ContentsBooks -> "Books")
            string tabTypeName = tab.GetType().Name;
            if (tabTypeToCategory.TryGetValue(tabTypeName, out string category))
                return category;

            // Second priority: use the game's translated label key (supports i18n)
            if (!string.IsNullOrEmpty(tab.labelKey))
            {
                try
                {
                    string translated = tab.labelKey.Translate().ToString();
                    if (!string.IsNullOrEmpty(translated) && translated != tab.labelKey)
                        return translated;
                }
                catch
                {
                    // Translation failed, continue to fallbacks
                }
            }

            // Try base type match (for subclasses), stopping at InspectTabBase
            Type baseType = tab.GetType().BaseType;
            while (baseType != null && baseType != typeof(object) && baseType != typeof(InspectTabBase))
            {
                if (tabTypeToCategory.TryGetValue(baseType.Name, out category))
                    return category;
                baseType = baseType.BaseType;
            }

            // Last resort: use type name cleaned up
            return tabTypeName.Replace("ITab_", "").Replace("_", " ");
        }

        /// <summary>
        /// Checks if a tab type is known (has explicit mapping).
        /// </summary>
        public static bool IsKnownTab(Type tabType)
        {
            if (tabType == null)
                return false;

            // Check exact type
            if (tabTypeToHandler.ContainsKey(tabType.Name))
                return true;

            // Check base types, stopping at InspectTabBase
            Type baseType = tabType.BaseType;
            while (baseType != null && baseType != typeof(object) && baseType != typeof(InspectTabBase))
            {
                if (tabTypeToHandler.ContainsKey(baseType.Name))
                    return true;
                baseType = baseType.BaseType;
            }

            return false;
        }

        /// <summary>
        /// Gets the handler type for a tab.
        /// </summary>
        public static TabHandlerType GetHandlerForTab(InspectTabBase tab)
        {
            if (tab == null)
                return TabHandlerType.BasicInspectString;

            string tabTypeName = tab.GetType().Name;

            // Try exact match
            if (tabTypeToHandler.TryGetValue(tabTypeName, out TabHandlerType handler))
                return handler;

            // Try base type match, stopping at InspectTabBase
            Type baseType = tab.GetType().BaseType;
            while (baseType != null && baseType != typeof(object) && baseType != typeof(InspectTabBase))
            {
                if (tabTypeToHandler.TryGetValue(baseType.Name, out handler))
                    return handler;
                baseType = baseType.BaseType;
            }

            // Default to basic inspect string for unknown tabs
            return TabHandlerType.BasicInspectString;
        }

        /// <summary>
        /// Gets the handler type for a category name (used for synthetic categories).
        /// </summary>
        public static TabHandlerType GetHandlerForCategory(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName))
                return TabHandlerType.BasicInspectString;

            if (categoryNameToHandler.TryGetValue(categoryName, out TabHandlerType handler))
                return handler;

            return TabHandlerType.BasicInspectString;
        }

        /// <summary>
        /// Gets the original category name for a tab (used to map to existing helpers).
        /// </summary>
        public static string GetOriginalCategoryName(InspectTabBase tab)
        {
            if (tab == null)
                return null;

            string tabTypeName = tab.GetType().Name;

            // Map tab types to original category names used in InspectionInfoHelper
            switch (tabTypeName)
            {
                case "ITab_Pawn_Health": return "Health";
                case "ITab_Pawn_Needs": return "Needs";
                case "ITab_Pawn_Character": return "Character";
                case "ITab_Pawn_Gear": return "Gear";
                case "ITab_Pawn_Social": return "Social";
                case "ITab_Pawn_Training": return "Training";
                case "ITab_Pawn_Log": return "Log";
                case "ITab_Pawn_Prisoner": return "Prisoner";
                case "ITab_Bills": return "Bills";
                case "ITab_Storage": return "Storage";
                case "ITab_Shells": return "Shells";
                case "ITab_PenAnimals": return "Pen Animals";
                case "ITab_PenAutoCut": return "Pen Auto-Cut";
                case "ITab_PenFood": return "Pen Food";
                case "ITab_Fishing": return "Fishing";
                case "ITab_Genes": return "Genes";
                case "ITab_Pawn_Feeding": return "Feeding";
                case "ITab_Art": return "Art";
                case "ITab_Book": return "Book";
                case "ITab_ContentsBooks": return "Books";
                case "ITab_WindTurbineAutoCut": return "Auto-Cut Plants";
                case "ITab_Pawn_Guest": return "Guest";
                case "ITab_Pawn_Visitor": return "Guest";
                case "ITab_ContentsTransporter": return "Contents";
                default: return GetCategoryNameForTab(tab);
            }
        }

        /// <summary>
        /// Gets fallback information for a tab using GetInspectString().
        /// </summary>
        public static string GetFallbackInfo(Thing thing, InspectTabBase tab)
        {
            if (thing == null || tab == null)
                return "No information available.";

            try
            {
                // Get the inspect string from the thing
                string inspectString = thing.GetInspectString();

                if (!string.IsNullOrEmpty(inspectString))
                    return inspectString;

                // If no inspect string, provide a helpful message
                string tabName = GetCategoryNameForTab(tab);
                return $"Tab '{tabName}' is available but has no keyboard-accessible text content.";
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Error getting fallback info for tab {tab?.GetType()?.Name}: {ex.Message}");
                return "Error retrieving information.";
            }
        }

        /// <summary>
        /// Gets all visible tabs for a thing as TabCategoryInfo objects.
        /// </summary>
        public static List<TabCategoryInfo> GetTabCategories(Thing thing)
        {
            var categories = new List<TabCategoryInfo>();

            if (thing == null)
                return categories;

            try
            {
                var tabs = thing.GetInspectTabs();
                if (tabs == null)
                {
                    return categories;
                }

                int totalTabs = 0;
                int visibleTabs = 0;
                foreach (var tab in tabs)
                {
                    totalTabs++;
                    if (tab == null)
                    {
                        continue;
                    }

                    try
                    {
                        // Skip hidden or invisible tabs (but allow ITab_Genes which is hidden in vanilla UI)
                        bool isGeneTab = tab.GetType().Name == "ITab_Genes";
                        if (!tab.IsVisible || (tab.Hidden && !isGeneTab))
                        {
                            continue;
                        }
                        visibleTabs++;

                        string categoryName = GetCategoryNameForTab(tab);
                        TabHandlerType handler = GetHandlerForTab(tab);
                        bool isKnown = IsKnownTab(tab.GetType());
                        string originalName = GetOriginalCategoryName(tab);

                        categories.Add(new TabCategoryInfo
                        {
                            Name = categoryName,
                            Tab = tab,
                            Handler = handler,
                            IsKnown = isKnown,
                            OriginalCategoryName = originalName
                        });
                    }
                    catch (Exception tabEx)
                    {
                        // Log per-tab error but continue processing other tabs
                        Log.Warning($"[RimWorld Access] Error processing tab {tab.GetType().Name}: {tabEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Error getting tab categories for {thing.LabelCap}: {ex.Message}");
            }

            return categories;
        }

        /// <summary>
        /// Gets all visible tabs for a zone as TabCategoryInfo objects.
        /// Zones have their own GetInspectTabs() implementation separate from Things.
        /// </summary>
        public static List<TabCategoryInfo> GetZoneTabCategories(Zone zone)
        {
            var categories = new List<TabCategoryInfo>();

            if (zone == null)
                return categories;

            try
            {
                var tabs = zone.GetInspectTabs();
                if (tabs == null)
                {
                    return categories;
                }

                foreach (var tab in tabs)
                {
                    if (tab == null)
                    {
                        continue;
                    }

                    try
                    {
                        // Skip hidden or invisible tabs (but allow ITab_Genes which is hidden in vanilla UI)
                        bool isGeneTab = tab.GetType().Name == "ITab_Genes";
                        if (!tab.IsVisible || (tab.Hidden && !isGeneTab))
                        {
                            continue;
                        }

                        string categoryName = GetCategoryNameForTab(tab);
                        TabHandlerType handler = GetHandlerForTab(tab);
                        bool isKnown = IsKnownTab(tab.GetType());
                        string originalName = GetOriginalCategoryName(tab);

                        categories.Add(new TabCategoryInfo
                        {
                            Name = categoryName,
                            Tab = tab,
                            Handler = handler,
                            IsKnown = isKnown,
                            OriginalCategoryName = originalName
                        });
                    }
                    catch (Exception tabEx)
                    {
                        Log.Warning($"[RimWorld Access] Error processing zone tab {tab.GetType().Name}: {tabEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Error getting tab categories for zone {zone.label}: {ex.Message}");
            }

            return categories;
        }

        /// <summary>
        /// Checks if a tab type is one that should open an action menu.
        /// </summary>
        public static bool IsActionTab(InspectTabBase tab)
        {
            return GetHandlerForTab(tab) == TabHandlerType.Action;
        }

        /// <summary>
        /// Checks if a tab type has rich navigation support.
        /// </summary>
        public static bool IsRichNavigationTab(InspectTabBase tab)
        {
            return GetHandlerForTab(tab) == TabHandlerType.RichNavigation;
        }
    }
}
