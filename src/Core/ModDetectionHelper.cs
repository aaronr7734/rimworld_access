using System;
using System.Reflection;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Provides safe detection and access to third-party mods.
    /// All methods are designed to fail gracefully when mods are not installed.
    /// </summary>
    public static class ModDetectionHelper
    {
        // Cached reflection results for performance
        private static bool? _isQualityBuilderActive = null;
        private static Type _qualityBuilderType = null;
        private static MethodInfo _hasDesignationMethod = null;
        private static MethodInfo _setSkilledMethod = null;
        private static MethodInfo _getDesignationOnThingMethod = null;

        /// <summary>
        /// Checks if the Quality Builder mod is active.
        /// Uses ModsConfig.IsActive for reliable detection.
        /// </summary>
        public static bool IsQualityBuilderActive()
        {
            if (_isQualityBuilderActive.HasValue)
                return _isQualityBuilderActive.Value;

            // First try: ModsConfig check (most reliable)
            _isQualityBuilderActive = ModsConfig.IsActive("QualityBuilder");

            // Second try: Type existence check (fallback)
            if (!_isQualityBuilderActive.Value)
            {
                _qualityBuilderType = Type.GetType("QualityBuilder.QualityBuilder, QualityBuilder");
                _isQualityBuilderActive = _qualityBuilderType != null;
            }

            return _isQualityBuilderActive.Value;
        }

        /// <summary>
        /// Gets the QualityBuilder type via reflection.
        /// Returns null if mod is not active.
        /// </summary>
        public static Type GetQualityBuilderType()
        {
            if (!IsQualityBuilderActive())
                return null;

            if (_qualityBuilderType == null)
            {
                _qualityBuilderType = Type.GetType("QualityBuilder.QualityBuilder, QualityBuilder");
            }

            return _qualityBuilderType;
        }

        /// <summary>
        /// Safely checks if a thing has a Quality Builder designation.
        /// Returns false if mod is not active or on error.
        /// </summary>
        public static bool HasQualityBuilderDesignation(Thing thing)
        {
            try
            {
                if (!IsQualityBuilderActive() || thing == null)
                    return false;

                if (_hasDesignationMethod == null)
                {
                    var type = GetQualityBuilderType();
                    if (type == null) return false;
                    _hasDesignationMethod = type.GetMethod("hasDesignation", BindingFlags.Public | BindingFlags.Static);
                }

                if (_hasDesignationMethod == null)
                    return false;

                return (bool)_hasDesignationMethod.Invoke(null, new object[] { thing });
            }
            catch
            {
                // Graceful degradation: if anything fails, assume no designation
                return false;
            }
        }

        /// <summary>
        /// Safely gets the current quality designation level for a thing.
        /// Returns null if no designation or mod not active.
        /// </summary>
        public static string GetCurrentQualityLevel(Thing thing)
        {
            try
            {
                if (!IsQualityBuilderActive() || thing == null)
                    return null;

                if (_getDesignationOnThingMethod == null)
                {
                    var type = GetQualityBuilderType();
                    if (type == null) return null;
                    _getDesignationOnThingMethod = type.GetMethod("getDesignationOnThing", BindingFlags.Public | BindingFlags.Static);
                }

                if (_getDesignationOnThingMethod == null)
                    return null;

                var designation = _getDesignationOnThingMethod.Invoke(null, new object[] { thing });
                if (designation == null)
                    return null;

                // Extract quality level from designation def name
                var defProperty = designation.GetType().GetProperty("def");
                if (defProperty == null) return null;

                var def = defProperty.GetValue(designation);
                if (def == null) return null;

                var defNameProperty = def.GetType().GetProperty("defName");
                if (defNameProperty == null) return null;

                string defName = defNameProperty.GetValue(def) as string;
                if (string.IsNullOrEmpty(defName))
                    return null;

                // Parse quality from "SkilledBuilderX" where X is optional number
                if (defName.StartsWith("SkilledBuilder"))
                {
                    if (defName == "SkilledBuilder") return "Normal"; // Default

                    string numberPart = defName.Substring("SkilledBuilder".Length);
                    if (int.TryParse(numberPart, out int level))
                    {
                        switch (level)
                        {
                            case 2: return "Poor";
                            case 3: return "Normal";
                            case 4: return "Good";
                            case 5: return "Excellent";
                            case 6: return "Masterwork";
                            case 7: return "Legendary";
                            default: return "Unknown";
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Safely gets the quality category enum value for a thing.
        /// Returns null if no designation or mod not active.
        /// </summary>
        public static int? GetCurrentQualityCategory(Thing thing)
        {
            try
            {
                if (!IsQualityBuilderActive() || thing == null)
                    return null;

                var level = GetCurrentQualityLevel(thing);
                if (level == null) return null;

                switch (level)
                {
                    case "Poor": return 2;
                    case "Normal": return 3;
                    case "Good": return 4;
                    case "Excellent": return 5;
                    case "Masterwork": return 6;
                    case "Legendary": return 7;
                    default: return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Safely sets or removes a quality designation on a thing.
        /// </summary>
        /// <param name="thing">Target thing (blueprint/frame)</param>
        /// <param name="qualityCategory">QualityCategory enum value (2-7), or null to remove</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool SetQualityDesignation(Thing thing, int? qualityCategory)
        {
            try
            {
                if (!IsQualityBuilderActive() || thing == null)
                    return false;

                if (_setSkilledMethod == null)
                {
                    var type = GetQualityBuilderType();
                    if (type == null) return false;
                    _setSkilledMethod = type.GetMethod("setSkilled", BindingFlags.Public | BindingFlags.Static);
                }

                if (_setSkilledMethod == null)
                    return false;

                // Convert int? to QualityCategory? enum for the mod's method
                if (qualityCategory.HasValue)
                {
                    // The mod expects QualityCategory enum from RimWorld
                    // QualityCategory is in RimWorld namespace, not Verse
                    object enumValue = null;
                    try
                    {
                        // Try to create the enum value directly
                        enumValue = Enum.ToObject(typeof(QualityCategory), qualityCategory.Value);
                    }
                    catch
                    {
                        // Fallback: use reflection
                        var rimworldAssembly = typeof(QualityCategory).Assembly;
                        var qualityCategoryType = rimworldAssembly.GetType("RimWorld.QualityCategory");
                        if (qualityCategoryType == null) return false;
                        enumValue = Enum.ToObject(qualityCategoryType, qualityCategory.Value);
                    }

                    if (enumValue == null) return false;

                    _setSkilledMethod.Invoke(null, new object[] { thing, enumValue, true });
                }
                else
                {
                    // Remove designation (pass null for category and false for add)
                    _setSkilledMethod.Invoke(null, new object[] { thing, null, false });
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the localized label for a quality level.
        /// Uses game's localization for quality categories when available.
        /// Falls back to English labels.
        /// </summary>
        public static string GetQualityLabel(int qualityCategory)
        {
            // Try to use game's localization for quality categories
            try
            {
                string key = "QualityCategory_" + qualityCategory;
                string translated = key.Translate();
                if (translated != key) // Translation exists
                    return translated;
            }
            catch
            {
                // Fall through to English defaults
            }

            // English fallback (as per user requirement for untranslated terms)
            switch (qualityCategory)
            {
                case 2: return "Poor";
                case 3: return "Normal";
                case 4: return "Good";
                case 5: return "Excellent";
                case 6: return "Masterwork";
                case 7: return "Legendary";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// Gets the full announcement for a quality designation.
        /// Includes both the label and description.
        /// </summary>
        public static string GetQualityAnnouncement(int qualityCategory)
        {
            string label = GetQualityLabel(qualityCategory);
            string description = GetQualityDescription(qualityCategory);
            return $"{label}. {description}";
        }

        /// <summary>
        /// Gets the description for a quality level.
        /// Uses English descriptions as per user requirement.
        /// </summary>
        public static string GetQualityDescription(int qualityCategory)
        {
            switch (qualityCategory)
            {
                case 2: return "Poor quality - basic functionality";
                case 3: return "Normal quality - standard construction";
                case 4: return "Good quality - above average work";
                case 5: return "Excellent quality - skilled craftsmanship";
                case 6: return "Masterwork quality - exceptional artistry";
                case 7: return "Legendary quality - unparalleled masterpiece";
                default: return "Unknown quality level";
            }
        }

        /// <summary>
        /// Checks if a thing can have quality designations (is a blueprint or frame).
        /// </summary>
        public static bool CanHaveQualityDesignation(Thing thing)
        {
            if (thing == null) return false;

            // Quality Builder works on blueprints and frames
            if (thing.def.IsBlueprint || thing.def.IsFrame)
                return true;

            // Check for blueprint types
            Type thingType = thing.GetType();

            // Check for direct blueprint types
            if (typeof(Blueprint).IsAssignableFrom(thingType))
                return true;

            // Check for specific blueprint classes
            if (thingType.Name.Contains("Blueprint"))
                return true;

            // Check for Blueprint_Build, Blueprint_Install, etc.
            if (thingType.FullName?.Contains("Blueprint") == true)
                return true;

            return false;
        }

        /// <summary>
        /// Gets the announcement for current quality on a thing.
        /// Returns empty string if no quality designation or mod not active.
        /// </summary>
        public static string GetCurrentQualityAnnouncement(Thing thing)
        {
            if (!IsQualityBuilderActive() || !CanHaveQualityDesignation(thing))
                return string.Empty;

            var currentLevel = GetCurrentQualityLevel(thing);
            if (string.IsNullOrEmpty(currentLevel))
                return string.Empty;

            int? category = GetCurrentQualityCategory(thing);
            if (!category.HasValue)
                return string.Empty;

            string label = GetQualityLabel(category.Value);
            return $"Required quality: {label}";
        }
    }
}
