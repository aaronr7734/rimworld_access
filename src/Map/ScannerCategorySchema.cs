using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Maps internal scanner category and subcategory names to localized display names.
    /// The internal Name fields on ScannerCategory/ScannerSubcategory stay English — they
    /// are dictionary keys and persistent identifiers — so only announcement paths call in
    /// here.
    /// </summary>
    internal static class ScannerNameLocalizer
    {
        // Canonical English category name -> XML key suffix under
        // RimWorldAccess.Map.Scanner.CatName.*.
        private static readonly Dictionary<string, string> CategoryKeys = new Dictionary<string, string>
        {
            ["All"] = "All",
            ["Pawns"] = "Pawns",
            ["Tame"] = "Tame",
            ["Wild"] = "Wild",
            ["Hazards"] = "Hazards",
            ["Buildings"] = "Buildings",
            ["Trees"] = "Trees",
            ["Plants"] = "Plants",
            ["Items"] = "Items",
            ["Plans"] = "Plans",
            ["Terrain"] = "Terrain",
            ["Roofs"] = "Roofs",
            ["Mineable"] = "Mineable",
            ["Orders"] = "Orders",
            ["Zones"] = "Zones",
            ["Rooms"] = "Rooms",
            ["Unexplored"] = "Unexplored",
            ["Uncategorized"] = "Uncategorized",
        };

        // Specialized subcategory English -> XML key suffix.
        private static readonly Dictionary<string, string> SubcategoryKeys = new Dictionary<string, string>
        {
            ["All"] = "All",
            ["Colonists"] = "Colonists",
            ["Prisoners"] = "Prisoners",
            ["Slaves"] = "Slaves",
            ["Guests"] = "Guests",
            ["Hostile"] = "Hostile",
            ["Player Mechs"] = "PlayerMechs",
            ["Controllable"] = "Controllable",
            ["Hostile Mechs"] = "HostileMechs",
            ["Pen"] = "Pen",
            ["NonPen"] = "NonPen",
            ["Passive"] = "Passive",
            ["Fire"] = "Fire",
            ["Blight"] = "Blight",
            ["Structure"] = "Structure",
            ["Production"] = "Production",
            ["Furniture"] = "Furniture",
            ["Power"] = "Power",
            ["Security"] = "Security",
            ["Misc"] = "Misc",
            ["Recreation"] = "Recreation",
            ["Ship"] = "Ship",
            ["Temperature"] = "Temperature",
            ["Traveling"] = "Traveling",
            ["Harvestable"] = "Harvestable",
            ["NonHarvestable"] = "NonHarvestable",
            ["Debris"] = "Debris",
            ["Stored"] = "Stored",
            ["Scattered"] = "Scattered",
            ["Forbidden"] = "Forbidden",
            ["Natural"] = "Natural",
            ["Constructed"] = "Constructed",
            ["Polluted"] = "Polluted",
            ["ThickRoof"] = "ThickRoof",
            ["ThinRoof"] = "ThinRoof",
            ["Rare"] = "Rare",
            ["Stone"] = "Stone",
            ["Chunks"] = "Chunks",
            ["Scanned Ore"] = "ScannedOre",
            ["Construction"] = "Construction",
            ["Haul"] = "Haul",
            ["Hunt"] = "Hunt",
            ["Mine"] = "Mine",
            ["Deconstruct"] = "Deconstruct",
            ["Uninstall"] = "Uninstall",
            ["Cut"] = "Cut",
            ["Smooth"] = "Smooth",
            ["Slaughter"] = "Slaughter",
            ["Other"] = "Other",
            ["Growing"] = "Growing",
            ["Stockpile"] = "Stockpile",
            ["Fishing"] = "Fishing",
            ["Tame"] = "Tame",
            ["Harvest"] = "Harvest",
        };

        /// <summary>
        /// The localized display name for a top-level scanner category, falling back to the
        /// raw name for strings outside the static schema (dynamic uncategorized buckets).
        /// </summary>
        public static string LocalizeCategoryName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (CategoryKeys.TryGetValue(name, out string suffix))
                return ("RimWorldAccess.Map.Scanner.CatName." + suffix).Translate().ToString();
            ScannerCellLayer layer = ScannerCellLayerRegistry.Find(name);
            return layer != null ? layer.CategoryLabelKey.Translate().ToString() : name;
        }

        /// <summary>
        /// The localized display name for a subcategory, stored internally as "{Cat}-{Sub}".
        /// A "-All" suffix announces as just the category name; specialized suffixes compose
        /// as "{LocalizedCat}: {LocalizedSub}". Names outside the schema (search filter
        /// labels, dynamic Uncategorized buckets) come back unchanged.
        /// </summary>
        public static string LocalizeSubcategoryName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return fullName;

            int dash = fullName.IndexOf('-');
            if (dash <= 0 || dash == fullName.Length - 1)
                return fullName;

            string catPart = fullName.Substring(0, dash);
            string subPart = fullName.Substring(dash + 1);

            ScannerCellLayer layer = ScannerCellLayerRegistry.Find(catPart);
            if (layer != null)
            {
                if (subPart == "All")
                    return LocalizeCategoryName(catPart);
                string layerSubKey = ScannerCellLayerRegistry.SubcategoryLabelKey(layer, subPart);
                return layerSubKey == null
                    ? fullName
                    : "RimWorldAccess.Map.Scanner.SubFullName".Translate(
                        LocalizeCategoryName(catPart), layerSubKey.Translate().ToString()).ToString();
            }

            // Anything dynamic (search "Search: foo-All", "Uncategorized-{defName}") falls
            // through to the original string.
            if (!CategoryKeys.ContainsKey(catPart))
                return fullName;

            string localizedCat = LocalizeCategoryName(catPart);

            if (subPart == "All")
                return localizedCat;

            // Plans subcategories are per-color: subPart is a planning-color suffix whose
            // display name comes from the Building PlanColor keys, not the SubName table.
            if (catPart == "Plans")
            {
                string colorKey = "RimWorldAccess.Building.PlanColor." + subPart;
                string localizedColor = colorKey.CanTranslate() ? colorKey.Translate().ToString() : subPart;
                return "RimWorldAccess.Map.Scanner.SubFullName".Translate(localizedCat, localizedColor).ToString();
            }

            if (!SubcategoryKeys.TryGetValue(subPart, out string subSuffix))
                return fullName;

            string localizedSub = ("RimWorldAccess.Map.Scanner.SubName." + subSuffix).Translate().ToString();
            return "RimWorldAccess.Map.Scanner.SubFullName".Translate(localizedCat, localizedSub).ToString();
        }
    }


    /// <summary>
    /// Container for all scanner categories during CollectMapItems: O(1) lookup by category
    /// name ("Pawns") or full subcategory name ("Pawns-Colonists"), plus an AddItem helper
    /// that routes an item to both the specialized subcategory and the category's "All".
    /// </summary>
    internal sealed class ScannerBuckets
    {
        public List<ScannerCategory> Categories { get; } = new List<ScannerCategory>();
        private readonly Dictionary<string, ScannerCategory> _categoriesByName = new Dictionary<string, ScannerCategory>();
        private readonly Dictionary<string, ScannerSubcategory> _subcatsByFullName = new Dictionary<string, ScannerSubcategory>();
        private readonly Dictionary<ScannerSubcategory, ScannerCategory> _parentByChild = new Dictionary<ScannerSubcategory, ScannerCategory>();

        /// <summary>
        /// Builds every category and subcategory from the declarative schema, then one per
        /// registered <see cref="ScannerCellLayer"/>. Uncategorized is created empty; the caller populates its def-driven subcategories at runtime.
        /// </summary>
        public static ScannerBuckets BuildFromSchema()
        {
            var buckets = new ScannerBuckets();
            foreach (var schema in ScannerCategorySchemas.All)
                buckets.AddCategory(schema.Build());
            foreach (var layer in ScannerCellLayerRegistry.Layers)
                buckets.AddCategory(layer.Schema().Build());

            // Still gets an "All" subcategory at index 0 via ScannerCategory.Create.
            buckets.AddCategory(ScannerCategory.Create("Uncategorized"));

            return buckets;
        }

        private void AddCategory(ScannerCategory category)
        {
            Categories.Add(category);
            _categoriesByName[category.Name] = category;
            foreach (var sub in category.Subcategories)
            {
                _subcatsByFullName[sub.Name] = sub;
                _parentByChild[sub] = category;
            }
        }

        /// <summary>Subcategory by full name ("Pawns-Colonists"); throws KeyNotFoundException, so callers pass static names.</summary>
        public ScannerSubcategory Sub(string fullName) => _subcatsByFullName[fullName];

        public ScannerCategory Cat(string name) => _categoriesByName[name];

        /// <summary>Adds an item to the specialized subcategory AND its parent category's "All" subcategory.</summary>
        public void AddItem(ScannerSubcategory specialized, ScannerItem item)
        {
            specialized.Items.Add(item);
            _parentByChild[specialized].Subcategories[0].Items.Add(item);
        }

        public void AddItem(string subFullName, ScannerItem item)
        {
            AddItem(_subcatsByFullName[subFullName], item);
        }

        /// <summary>Registers a dynamically-created subcategory (Uncategorized's per-def buckets).</summary>
        public void RegisterDynamicSubcategory(ScannerCategory parent, ScannerSubcategory sub)
        {
            parent.Subcategories.Add(sub);
            _subcatsByFullName[sub.Name] = sub;
            _parentByChild[sub] = parent;
        }
    }


    /// <summary>
    /// Declarative schema for a scanner category: its name and its specialized subcategory
    /// names. Build() always inserts an "All" subcategory at index 0. Uncategorized is built
    /// separately because its subcategories are def-driven and dynamic.
    /// </summary>
    internal sealed class ScannerCategorySchema
    {
        public string Name { get; }
        public IReadOnlyList<string> SpecializedSubcategories { get; }

        public ScannerCategorySchema(string name, params string[] subcategories)
        {
            Name = name;
            SpecializedSubcategories = subcategories;
        }

        /// <summary>A fresh ScannerCategory with "All" at index 0 followed by the specialized subcategories in declaration order.</summary>
        public ScannerCategory Build()
        {
            var cat = ScannerCategory.Create(Name); // inserts "{Name}-All" at index 0
            foreach (var sub in SpecializedSubcategories)
                cat.Subcategories.Add(new ScannerSubcategory($"{Name}-{sub}"));
            return cat;
        }
    }

    /// <summary>The canonical list of scanner category schemas; a new category or subcategory is a one-line addition here.</summary>
    internal static class ScannerCategorySchemas
    {
        public static readonly ScannerCategorySchema[] All = new[]
        {
            // A flat cross-category view of every scanner item on the map, sorted by
            // distance, populated in CollectMapItems by flattening each other category's
            // "-All" subcategory with reference-based deduplication.
            new ScannerCategorySchema("All"),
            new ScannerCategorySchema("Pawns",
                "Colonists", "Prisoners", "Slaves", "Guests", "Hostile", "Player Mechs", "Controllable", "Hostile Mechs",
                // Catch-all for pawns whose race is none of humanlike, animal, mechanoid
                // or anomaly entity and that carry no draft controller — see ClassifyPawn.
                "Other"),
            new ScannerCategorySchema("Entities", "Hostile", "Captured"),
            new ScannerCategorySchema("Tame", "Pen", "NonPen"),
            new ScannerCategorySchema("Wild", "Hostile", "Passive"),
            new ScannerCategorySchema("Hazards", "Fire", "Blight"),
            new ScannerCategorySchema("Buildings",
                "Structure", "Production", "Furniture", "Power", "Security", "Misc",
                "Recreation", "Ship", "Temperature", "Traveling"),
            new ScannerCategorySchema("Trees", "Harvestable", "NonHarvestable"),
            new ScannerCategorySchema("Plants", "Harvestable", "Debris"),
            new ScannerCategorySchema("Items", "Stored", "Furniture", "Scattered", "Forbidden"),
            new ScannerCategorySchema("Terrain", "Natural", "Constructed", "Polluted"),
            new ScannerCategorySchema("Roofs", "ThickRoof", "ThinRoof"),
            new ScannerCategorySchema("Mineable", "Rare", "Stone", "Chunks", "Scanned Ore"),
            new ScannerCategorySchema("Orders",
                "Construction", "Haul", "Hunt", "Mine", "Deconstruct", "Uninstall",
                "Cut", "Harvest", "Smooth", "Tame", "Slaughter", "Other"),
            new ScannerCategorySchema("Zones", "Growing", "Stockpile", "Fishing", "Other"),
            new ScannerCategorySchema("Plans"), // only gets "All"
            new ScannerCategorySchema("Rooms"), // only gets "All"
            new ScannerCategorySchema("Unexplored"),
            // Uncategorized is built separately — its subcategories are discovered at runtime.
        };
    }
}
