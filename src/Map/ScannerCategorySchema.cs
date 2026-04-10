using System.Collections.Generic;
using System.Linq;

namespace RimWorldAccess
{
    /// <summary>
    /// Container for all scanner categories during CollectMapItems. Provides O(1) lookup
    /// by name for both categories (e.g., "Pawns") and specialized subcategories
    /// (e.g., "Pawns-Colonists"), and a single AddItem helper that routes items to both
    /// the specialized subcategory and the category's "All" subcategory.
    /// </summary>
    internal sealed class ScannerBuckets
    {
        public List<ScannerCategory> Categories { get; } = new List<ScannerCategory>();
        private readonly Dictionary<string, ScannerCategory> _categoriesByName = new Dictionary<string, ScannerCategory>();
        private readonly Dictionary<string, ScannerSubcategory> _subcatsByFullName = new Dictionary<string, ScannerSubcategory>();
        private readonly Dictionary<ScannerSubcategory, ScannerCategory> _parentByChild = new Dictionary<ScannerSubcategory, ScannerCategory>();

        /// <summary>
        /// Builds all scanner categories + subcategories from the declarative schema.
        /// Uncategorized is created empty here and populated with def-driven subcategories
        /// at runtime by the caller.
        /// </summary>
        public static ScannerBuckets BuildFromSchema()
        {
            var buckets = new ScannerBuckets();
            foreach (var schema in ScannerCategorySchemas.All)
                buckets.AddCategory(schema.Build());

            // Uncategorized is special — dynamic per-def subcategories are added at runtime.
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

        /// <summary>
        /// Looks up a subcategory by its full name (e.g., "Pawns-Colonists").
        /// Throws KeyNotFoundException if missing — callers should pass static names.
        /// </summary>
        public ScannerSubcategory Sub(string fullName) => _subcatsByFullName[fullName];

        /// <summary>
        /// Looks up a category by name (e.g., "Pawns").
        /// </summary>
        public ScannerCategory Cat(string name) => _categoriesByName[name];

        /// <summary>
        /// Adds an item to the specialized subcategory AND its parent category's "All"
        /// subcategory. Returns the parent category so callers can still do additional
        /// bookkeeping (e.g., categorizedThings.Add).
        /// </summary>
        public void AddItem(ScannerSubcategory specialized, ScannerItem item)
        {
            specialized.Items.Add(item);
            _parentByChild[specialized].Subcategories[0].Items.Add(item);
        }

        /// <summary>
        /// Adds an item by subcategory full name. Equivalent to AddItem(Sub(fullName), item).
        /// </summary>
        public void AddItem(string subFullName, ScannerItem item)
        {
            AddItem(_subcatsByFullName[subFullName], item);
        }

        /// <summary>
        /// Registers a dynamically-created subcategory (used by Uncategorized for per-def buckets).
        /// </summary>
        public void RegisterDynamicSubcategory(ScannerCategory parent, ScannerSubcategory sub)
        {
            parent.Subcategories.Add(sub);
            _subcatsByFullName[sub.Name] = sub;
            _parentByChild[sub] = parent;
        }
    }


    /// <summary>
    /// Declarative schema for a scanner category: its name and the specialized subcategory
    /// names it contains. Every category built via this schema gets an "All" subcategory
    /// inserted at index 0 automatically by Build(). Uncategorized is built separately
    /// because its subcategories are def-driven and dynamic.
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

        /// <summary>
        /// Builds a fresh ScannerCategory with "All" at index 0 followed by the specialized
        /// subcategories in declaration order.
        /// </summary>
        public ScannerCategory Build()
        {
            var cat = ScannerCategory.Create(Name); // inserts "{Name}-All" at index 0
            foreach (var sub in SpecializedSubcategories)
                cat.Subcategories.Add(new ScannerSubcategory($"{Name}-{sub}"));
            return cat;
        }
    }

    /// <summary>
    /// The canonical list of scanner category schemas. Adding a new category or subcategory
    /// is a one-line change here — no more hunting through the 66-line manual-declaration block.
    /// </summary>
    internal static class ScannerCategorySchemas
    {
        public static readonly ScannerCategorySchema[] All = new[]
        {
            // Top-level "All" category — a flat cross-category view containing every
            // scanner item on the map, sorted by distance. Has no specialized subcategories
            // because the other top-level categories already serve that purpose.
            // Populated in CollectMapItems by flattening each other category's "-All"
            // subcategory with reference-based deduplication.
            new ScannerCategorySchema("All"),
            new ScannerCategorySchema("Pawns",
                "Colonists", "Prisoners", "Slaves", "Guests", "Hostile", "Player Mechs", "Hostile Mechs"),
            new ScannerCategorySchema("Tame", "Pen", "NonPen"),
            new ScannerCategorySchema("Wild", "Hostile", "Passive"),
            new ScannerCategorySchema("Hazards", "Fire", "Blight"),
            new ScannerCategorySchema("Buildings",
                "Structure", "Production", "Furniture", "Power", "Security", "Misc",
                "Recreation", "Ship", "Temperature", "Traveling"),
            new ScannerCategorySchema("Trees", "Harvestable", "NonHarvestable"),
            new ScannerCategorySchema("Plants", "Harvestable", "Debris"),
            new ScannerCategorySchema("Items", "Stored", "Furniture", "Scattered", "Forbidden"),
            new ScannerCategorySchema("Terrain", "Natural", "Constructed"),
            new ScannerCategorySchema("Mineable", "Rare", "Stone", "Chunks", "Scanned Ore"),
            new ScannerCategorySchema("Orders",
                "Construction", "Haul", "Hunt", "Mine", "Deconstruct", "Uninstall",
                "Cut", "Harvest", "Smooth", "Tame", "Slaughter", "Other"),
            new ScannerCategorySchema("Zones", "Growing", "Stockpile", "Fishing", "Other"),
            new ScannerCategorySchema("Rooms"), // only gets "All"
            // Uncategorized is built separately — its subcategories are discovered at runtime.
        };
    }
}
