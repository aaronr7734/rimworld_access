using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper for detecting and describing animal pens at the cursor position.
    /// Pens are outdoors enclosures, so they need separate handling from room stats.
    /// </summary>
    public static class PenInfoHelper
    {
        private const int MaxPenCells = 20000;

        private sealed class PenData
        {
            public Building Marker { get; set; }
            public CompAnimalPenMarker MarkerComp { get; set; }
            public HashSet<IntVec3> Cells { get; set; }
            public bool IsEnclosed { get; set; }
        }

        /// <summary>
        /// Gets pen statistics for a position, or null if the position is not part of a detected pen.
        /// </summary>
        public static string GetPenStatsInfo(IntVec3 position, Map map)
        {
            PenData pen = FindContainingPen(position, map);
            if (pen == null)
                return null;

            PenFoodCalculator calculator = pen.MarkerComp?.PenFoodCalculator;
            List<string> warnings = GetWarnings(pen, calculator);
            string animalComposition = GetAnimalComposition(calculator);

            var sb = new StringBuilder();
            sb.Append("Pen");
            sb.Append(": ");
            sb.Append(GetPenLabel(pen.Marker));
            sb.Append($", size: {pen.Cells.Count} cells");

            if (warnings.Count > 0)
            {
                sb.Append(", warnings: ");
                sb.Append(string.Join("; ", warnings));
            }

            if (calculator == null)
                return sb.ToString();

            float growth = calculator.NutritionPerDayToday;
            float consumption = calculator.SumNutritionConsumptionPerDay;
            float balance = growth - consumption;
            string balanceSign = balance >= 0f ? "+" : "";

            sb.Append($", nutrition balance: {balanceSign}{balance:F1} per day");
            sb.Append($", growth: {growth:F1}");
            sb.Append($", consumption: {consumption:F1}");
            sb.Append($", stockpiled: {calculator.sumStockpiledNutritionAvailableNow:F1}");

            int animalCount = calculator.ActualAnimalInfos?.Sum(info => info.count) ?? 0;
            int animalTypes = calculator.ActualAnimalInfos?.Count ?? 0;

            if (animalCount > 0)
            {
                sb.Append($", animals: {animalCount}");
                if (animalTypes > 1)
                    sb.Append($" ({animalTypes} types)");

                if (!string.IsNullOrEmpty(animalComposition))
                {
                    sb.Append(", composition: ");
                    sb.Append(animalComposition);
                }
            }
            else
            {
                sb.Append(", no animals");
            }

            return sb.ToString();
        }

        private static PenData FindContainingPen(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return null;

            List<IntVec3> targetCells = GetCandidateTargetCells(position, map);
            if (targetCells.Count == 0)
                return null;

            foreach (Building building in map.listerBuildings.allBuildingsColonist)
            {
                CompAnimalPenMarker markerComp = building.TryGetComp<CompAnimalPenMarker>();
                if (markerComp == null)
                    continue;

                PenData pen = BuildPenData(building, markerComp, map);
                if (pen == null)
                    continue;

                if (position == building.Position)
                    return pen;

                if (targetCells.Any(cell => pen.Cells.Contains(cell)))
                    return pen;
            }

            return null;
        }

        private static List<IntVec3> GetCandidateTargetCells(IntVec3 position, Map map)
        {
            var cells = new List<IntVec3>();

            if (CanOccupyPenCell(position, map))
                cells.Add(position);

            foreach (IntVec3 offset in GenAdj.CardinalDirections)
            {
                IntVec3 adjacent = position + offset;
                if (adjacent.InBounds(map) && CanOccupyPenCell(adjacent, map) && !cells.Contains(adjacent))
                    cells.Add(adjacent);
            }

            return cells;
        }

        private static PenData BuildPenData(Building marker, CompAnimalPenMarker markerComp, Map map)
        {
            if (marker == null || markerComp == null || map == null || !marker.Spawned)
                return null;

            var visited = new HashSet<IntVec3>();
            var queue = new Queue<IntVec3>();
            bool reachedEdge = false;

            if (!marker.Position.InBounds(map))
                return null;

            queue.Enqueue(marker.Position);
            visited.Add(marker.Position);

            while (queue.Count > 0)
            {
                IntVec3 current = queue.Dequeue();

                if (visited.Count >= MaxPenCells)
                {
                    reachedEdge = true;
                    break;
                }

                if (current.x == 0 || current.z == 0 || current.x == map.Size.x - 1 || current.z == map.Size.z - 1)
                {
                    reachedEdge = true;
                }

                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 next = current + offset;
                    if (!next.InBounds(map) || visited.Contains(next))
                        continue;

                    if (!CanOccupyPenCell(next, map))
                        continue;

                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            return new PenData
            {
                Marker = marker,
                MarkerComp = markerComp,
                Cells = visited,
                IsEnclosed = !reachedEdge
            };
        }

        private static bool CanOccupyPenCell(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map))
                return false;

            if (cell.Impassable(map))
                return false;

            foreach (Thing thing in cell.GetThingList(map))
            {
                if (!(thing is Building building))
                    continue;

                if (building.TryGetComp<CompAnimalPenMarker>() != null)
                    continue;

                BuildingProperties props = building.def?.building;
                if (props == null)
                    continue;

                // Treat pen boundaries like rooms treat walls: fences, walls, and doors block the flood fill.
                if (props.isFence || props.isWall || building is Building_Door)
                    return false;
            }

            return true;
        }

        private static string GetPenLabel(Building marker)
        {
            if (marker == null)
                return "Animal pen";

            PropertyInfo renamableLabelProp = marker.GetType().GetProperty("RenamableLabel");
            string label = renamableLabelProp?.GetValue(marker) as string;
            if (string.IsNullOrWhiteSpace(label))
                label = marker.LabelCap;

            return string.IsNullOrWhiteSpace(label) ? "Animal pen" : label;
        }

        private static List<string> GetWarnings(PenData pen, PenFoodCalculator calculator)
        {
            var warnings = new List<string>();

            if (pen == null)
                return warnings;

            if (!pen.IsEnclosed)
                warnings.Add("AlertAnimalPenNotEnclosed".Translate().RawText);

            if (calculator == null)
                return warnings;

            float balance = calculator.NutritionPerDayToday - calculator.SumNutritionConsumptionPerDay;
            if (balance < 0f)
                warnings.Add($"losing nutrition ({balance:F1} per day)");

            if (calculator.sumStockpiledNutritionAvailableNow <= 0f)
                warnings.Add("no stockpiled food");

            int animalCount = calculator.ActualAnimalInfos?.Sum(info => info.count) ?? 0;
            if (animalCount == 0)
                warnings.Add("no animals assigned");

            return warnings;
        }

        private static string GetAnimalComposition(PenFoodCalculator calculator)
        {
            if (calculator?.ActualAnimalInfos == null || calculator.ActualAnimalInfos.Count == 0)
                return null;

            var parts = new List<string>();
            foreach (var info in calculator.ActualAnimalInfos.OrderByDescending(info => info.count))
            {
                string animalLabel = info.animalDef?.label?.CapitalizeFirst() ?? "Unknown";
                parts.Add($"{animalLabel} {info.count}");
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Jumps to the pen marker for the pen at the given position.
        /// Returns a spoken message describing the result.
        /// </summary>
        public static string JumpToPenMarker(IntVec3 position, Map map)
        {
            PenData pen = FindContainingPen(position, map);
            if (pen?.Marker == null || map == null)
                return "No pen at cursor";

            IntVec3 markerPosition = pen.Marker.Position;
            if (!markerPosition.InBounds(map))
                return "Pen marker not found";

            MapNavigationState.CurrentCursorPosition = markerPosition;
            Find.CameraDriver?.JumpToCurrentMapLoc(markerPosition);

            return $"Jumped to pen marker: {GetPenLabel(pen.Marker)}";
        }
    }
}
