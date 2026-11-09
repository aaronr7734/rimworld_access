using System;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Bad Hygiene's per-cell ground layers, read straight from its <c>MapComponent_Hygiene</c>
    /// grids: the shallow and deep water tables that wells draw from, subsurface ice, sewage
    /// contamination, sprinkler irrigation and spread fertilizer. A sighted player sees them as
    /// tinted overlays while placing or selecting the buildings that use them.
    ///
    /// Presence follows whoever consumes the grid: the well grids (water, deep water, ice) use the
    /// place workers' int-truncated <c>value &gt; 0</c>, so "groundwater" means a well can go there;
    /// sewage and irrigation use the overlay's own <c>GetCellBool</c> (<c>value &gt; 0f</c>), which is
    /// also the fertility patch's irrigation test. Strength is the value over the grid's own
    /// <c>GridLimit</c>, clamped, exactly what the overlay's tint shows. Lite mode switches the grids
    /// off, and every reading here goes silent with it.
    /// </summary>
    internal sealed class DubsGroundwater
    {
        private readonly Type hygieneType;
        private readonly FieldInfo waterGrid;
        private readonly FieldInfo deepWaterGrid;
        private readonly FieldInfo iceGrid;
        private readonly FieldInfo waterUserGrid;
        private readonly FieldInfo deepUserGrid;
        private readonly FieldInfo iceUserGrid;
        private readonly FieldInfo sewageGrid;
        private readonly FieldInfo irrigationGrid;
        private readonly FieldInfo fertilizerGrid;
        private readonly FieldInfo gridValues;
        private readonly FieldInfo gridLimit;
        private readonly PropertyInfo liteMode;

        public bool Ready { get; }

        public DubsGroundwater()
        {
            var surface = new ReflectionSurface("DubsGroundwater");
            hygieneType = surface.Type("DubsBadHygiene.MapComponent_Hygiene");
            Type gridType = surface.Type("DubsBadHygiene.GridLayer");
            Type settingsType = surface.Type("DubsBadHygiene.Settings");
            waterGrid = surface.Field(hygieneType, "WaterGrid");
            deepWaterGrid = surface.Field(hygieneType, "DeepWaterGrid");
            iceGrid = surface.Field(hygieneType, "IceGrid");
            waterUserGrid = surface.Field(hygieneType, "WaterUserGrid");
            deepUserGrid = surface.Field(hygieneType, "DeepUserGrid");
            iceUserGrid = surface.Field(hygieneType, "IceUserGrid");
            sewageGrid = surface.Field(hygieneType, "SewageGrid");
            irrigationGrid = surface.Field(hygieneType, "IrrigationGrid");
            fertilizerGrid = surface.Field(hygieneType, "FertilizerGrid");
            gridValues = surface.Field(gridType, "Grid");
            gridLimit = surface.Field(gridType, "GridLimit");
            liteMode = surface.Property(settingsType, "LiteMode");
            Ready = surface.Ready;
        }

        /// <summary>One of the mod's grids, snapshotted for reads: its value array and the limit its tint saturates at.</summary>
        internal struct Grid
        {
            public float[] Values;
            public float Limit;

            public bool IsValid => Values != null;

            public float ValueAt(Map map, IntVec3 cell) => Values[map.cellIndices.CellToIndex(cell)];

            /// <summary>The place workers' presence test: <c>GridLayer.ValueAt</c> truncates to int before comparing.</summary>
            public bool HasAt(Map map, IntVec3 cell) => IntAt(map, cell) > 0;

            /// <summary><c>GridLayer.ValueAt</c>'s int-truncated reading, which the wells' capacity math uses.</summary>
            public int IntAt(Map map, IntVec3 cell) => (int)ValueAt(map, cell);

            public bool TintedAt(Map map, IntVec3 cell) => ValueAt(map, cell) > 0f;

            // A zero limit (sewage capacity set to 0) saturates the tint, as GetCellExtraColor's clamped lerp does.
            public string PercentAt(Map map, IntVec3 cell)
            {
                return (Limit > 0f ? Mathf.Clamp01(ValueAt(map, cell) / Limit) : 1f).ToStringPercent();
            }
        }

        public Grid Water(Map map) => Read(map, waterGrid);
        public Grid DeepWater(Map map) => Read(map, deepWaterGrid);
        public Grid Ice(Map map) => Read(map, iceGrid);
        public Grid Sewage(Map map) => Read(map, sewageGrid);

        /// <summary>How many built wells draw on each cell of the matching water layer.</summary>
        public Grid WaterUsers(Map map) => Read(map, waterUserGrid);
        public Grid DeepWaterUsers(Map map) => Read(map, deepUserGrid);
        public Grid IceUsers(Map map) => Read(map, iceUserGrid);

        private Grid Read(Map map, FieldInfo gridField)
        {
            if (!Ready || map == null || (bool)liteMode.GetValue(null, null))
                return default(Grid);
            MapComponent component = map.GetComponent(hygieneType);
            object layer = component == null ? null : gridField.GetValue(component);
            if (layer == null)
                return default(Grid);
            return new Grid
            {
                Values = gridValues.GetValue(layer) as float[],
                Limit = (float)gridLimit.GetValue(layer),
            };
        }

        /// <summary>The resources-key lines for a cell: both water tables always, the other layers only where present.</summary>
        public string TileReadout(IntVec3 cell, Map map)
        {
            Grid water = Water(map);
            if (!water.IsValid || !cell.InBounds(map))
                return null;

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Comma);
            builder.Add(LevelPhrase(water, map, cell,
                "RimWorldAccess.Compat.Dubs.Groundwater.Shallow", "RimWorldAccess.Compat.Dubs.Groundwater.ShallowNone"));
            builder.Add(LevelPhrase(DeepWater(map), map, cell,
                "RimWorldAccess.Compat.Dubs.Groundwater.Deep", "RimWorldAccess.Compat.Dubs.Groundwater.DeepNone"));
            AddIfPresent(builder, PresentPhrase(Ice(map), map, cell, "RimWorldAccess.Compat.Dubs.Groundwater.Ice", tintOnly: false));
            AddIfPresent(builder, PresentPhrase(Sewage(map), map, cell, "RimWorldAccess.Compat.Dubs.Groundwater.Sewage", tintOnly: true));
            AddIfPresent(builder, PresentPhrase(Read(map, irrigationGrid), map, cell, "RimWorldAccess.Compat.Dubs.Groundwater.Irrigation", tintOnly: true));

            // Fertilizer is all-or-nothing to the fertility patch, so it has no strength to speak.
            Grid fertilizer = Read(map, fertilizerGrid);
            if (fertilizer.IsValid && fertilizer.ValueAt(map, cell) > 0f)
                builder.Add("RimWorldAccess.Compat.Dubs.Groundwater.Fertilized".Translate());

            return builder.Build().CapitalizeFirst();
        }

        /// <summary>The level phrase for a grid at a cell, or its "none" phrase; null when the grid is unreadable.</summary>
        public static string LevelPhrase(Grid grid, Map map, IntVec3 cell, string levelKey, string noneKey)
        {
            if (!grid.IsValid || !cell.InBounds(map))
                return null;
            return grid.HasAt(map, cell)
                ? levelKey.Translate(grid.PercentAt(map, cell)).ToString()
                : noneKey.Translate().ToString();
        }

        /// <summary>The level phrase for a grid at a cell, or null where the layer is absent; <paramref name="tintOnly"/> picks the overlay's presence test over the well grids'.</summary>
        public static string PresentPhrase(Grid grid, Map map, IntVec3 cell, string levelKey, bool tintOnly)
        {
            if (!grid.IsValid || !cell.InBounds(map))
                return null;
            if (!(tintOnly ? grid.TintedAt(map, cell) : grid.HasAt(map, cell)))
                return null;
            return levelKey.Translate(grid.PercentAt(map, cell)).ToString();
        }

        private static void AddIfPresent(AnnouncementBuilder builder, string phrase)
        {
            if (phrase != null)
                builder.Add(phrase);
        }

        /// <summary>The scanner's Groundwater category: the shallow and deep water tables and subsurface ice.</summary>
        public ScannerCellLayer ScannerLayer()
        {
            return new ScannerCellLayer(
                "Groundwater",
                "RimWorldAccess.Compat.Dubs.Scanner.Category",
                new[]
                {
                    new ScannerCellLayer.Sublayer("Shallow",
                        "RimWorldAccess.Compat.Dubs.Scanner.Sub.Shallow", "RimWorldAccess.Compat.Dubs.Scanner.Item.Shallow"),
                    new ScannerCellLayer.Sublayer("Deep",
                        "RimWorldAccess.Compat.Dubs.Scanner.Sub.Deep", "RimWorldAccess.Compat.Dubs.Scanner.Item.Deep"),
                    new ScannerCellLayer.Sublayer("Ice",
                        "RimWorldAccess.Compat.Dubs.Scanner.Sub.Ice", "RimWorldAccess.Compat.Dubs.Scanner.Item.Ice"),
                },
                BeginScannerSweep);
        }

        private Func<IntVec3, bool>[] BeginScannerSweep(Map map)
        {
            Grid water = Water(map);
            if (!water.IsValid)
                return null;
            Grid deep = DeepWater(map);
            Grid ice = Ice(map);
            return new[] { PresenceTest(water, map), PresenceTest(deep, map), PresenceTest(ice, map) };
        }

        private static Func<IntVec3, bool> PresenceTest(Grid grid, Map map)
        {
            if (!grid.IsValid)
                return cell => false;
            return cell => grid.HasAt(map, cell);
        }
    }
}
