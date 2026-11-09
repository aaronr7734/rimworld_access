using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// A per-cell map layer a compat module adds to the scanner as a category of its own. It is
    /// collected in the same fog-gated AllCells walk as natural roofs and grouped the same way: one
    /// item per sublayer, carrying its contiguous patches as regions, cached and invalidated with
    /// the rest of the cell walk. Like every scanner category it drops out while empty, so a layer
    /// the current map does not have never shows.
    /// </summary>
    internal sealed class ScannerCellLayer
    {
        /// <summary>One subcategory of the layer: its stable internal name, its subcategory label key, and the label its item is announced with.</summary>
        internal sealed class Sublayer
        {
            public string Name { get; }
            public string SubcategoryLabelKey { get; }
            public string ItemLabelKey { get; }

            public Sublayer(string name, string subcategoryLabelKey, string itemLabelKey)
            {
                Name = name;
                SubcategoryLabelKey = subcategoryLabelKey;
                ItemLabelKey = itemLabelKey;
            }
        }

        /// <summary>Stable internal category name; persists as a scanner identifier, so never localized.</summary>
        public string CategoryName { get; }
        public string CategoryLabelKey { get; }
        public IReadOnlyList<Sublayer> Sublayers { get; }

        /// <summary>
        /// Called once per cell walk. Returns one cell test per sublayer, in <see cref="Sublayers"/>
        /// order, or null when the map has no such layer at all. Tests run on every unfogged cell,
        /// so they must be plain reads, never a per-cell cache or hover API.
        /// </summary>
        public Func<Map, Func<IntVec3, bool>[]> BeginSweep { get; }

        public ScannerCellLayer(string categoryName, string categoryLabelKey, Sublayer[] sublayers,
            Func<Map, Func<IntVec3, bool>[]> beginSweep)
        {
            CategoryName = categoryName;
            CategoryLabelKey = categoryLabelKey;
            Sublayers = sublayers;
            BeginSweep = beginSweep;
        }

        public ScannerCategorySchema Schema()
        {
            var names = new string[Sublayers.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = Sublayers[i].Name;
            return new ScannerCategorySchema(CategoryName, names);
        }
    }

    internal static class ScannerCellLayerRegistry
    {
        private static readonly List<ScannerCellLayer> layers = new List<ScannerCellLayer>();

        public static IReadOnlyList<ScannerCellLayer> Layers => layers;

        public static void Register(ScannerCellLayer layer)
        {
            if (layer == null || Find(layer.CategoryName) != null)
                return;
            layers.Add(layer);
            ScannerHelper.InvalidateCache();
        }

        public static ScannerCellLayer Find(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName))
                return null;
            foreach (var layer in layers)
            {
                if (layer.CategoryName == categoryName)
                    return layer;
            }
            return null;
        }

        /// <summary>The sublayer's subcategory label key, or null when the name is not one of the layer's sublayers.</summary>
        public static string SubcategoryLabelKey(ScannerCellLayer layer, string subName)
        {
            foreach (var sublayer in layer.Sublayers)
            {
                if (sublayer.Name == subName)
                    return sublayer.SubcategoryLabelKey;
            }
            return null;
        }
    }
}
