using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public static class SubstructureOverlayState
    {
        private static bool hasTempCategory;

        public static bool IsOverlayActive(Map map)
        {
            if (map == null) return false;

            var engine = map.listerBuildings.AllBuildingsColonistOfClass<Building_GravEngine>().FirstOrDefault();
            if (engine == null) return false;

            var comp = engine.TryGetComp<CompSubstructureFootprint>();
            return comp != null && comp.DisplaySubstructureOverlay;
        }

        public static bool IsDisconnectedAt(IntVec3 position, Map map)
        {
            if (map == null) return false;

            var foundation = map.terrainGrid.FoundationAt(position);
            if (foundation == null || !foundation.IsSubstructure) return false;

            var engine = map.listerBuildings.AllBuildingsColonistOfClass<Building_GravEngine>().FirstOrDefault();
            if (engine == null) return false;

            return !engine.ValidSubstructureAt(position);
        }

        public static void CheckOverlayState()
        {
            var map = Find.CurrentMap;
            bool isActive = IsOverlayActive(map);

            if (isActive && !hasTempCategory)
            {
                CreateDisconnectedCategory(map);
            }
            else if (!isActive && hasTempCategory)
            {
                ScannerState.RemoveTemporaryCategory();
                hasTempCategory = false;
            }
        }

        private static void CreateDisconnectedCategory(Map map)
        {
            var engine = map.listerBuildings.AllBuildingsColonistOfClass<Building_GravEngine>().FirstOrDefault();
            if (engine == null) return;

            var disconnectedCells = new List<IntVec3>();
            foreach (IntVec3 cell in map.AllCells)
            {
                var foundation = map.terrainGrid.FoundationAt(cell);
                if (foundation != null && foundation.IsSubstructure && !engine.ValidSubstructureAt(cell))
                {
                    disconnectedCells.Add(cell);
                }
            }

            if (disconnectedCells.Count > 0)
            {
                var cursorPos = MapNavigationState.CurrentCursorPosition;
                var regions = ScannerHelper.GroupTerrainByAdjacency(disconnectedCells, cursorPos);
                var item = new ScannerItem(regions, "RimWorldAccess.Gravships.Substructure.ItemLabel".Translate(), cursorPos);
                ScannerState.CreateTemporaryCategory("RimWorldAccess.Gravships.Substructure.CategoryLabel".Translate(), new List<ScannerItem> { item });
                hasTempCategory = true;
            }
        }
    }
}
