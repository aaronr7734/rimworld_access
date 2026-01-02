using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to render visual preview of keyboard-based selection.
    /// Uses RimWorld's native highlighting materials so sighted observers can see what's being selected.
    /// </summary>
    [HarmonyPatch(typeof(MapInterface))]
    [HarmonyPatch("MapInterfaceUpdate")]
    public static class SelectionPreviewPatch
    {
        /// <summary>
        /// Postfix patch to draw selection preview after normal map interface update.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            Map map = Find.CurrentMap;
            if (map == null)
                return;

            // Render zone creation mode preview
            if (ZoneCreationState.IsInCreationMode)
            {
                // Draw preview cells using native green highlight material
                if (ZoneCreationState.PreviewCells.Count > 0)
                {
                    RenderCells(ZoneCreationState.PreviewCells.ToList(), DesignatorUtility.DragHighlightCellMat);
                }

                // Draw already-selected cells
                if (ZoneCreationState.SelectedCells.Count > 0)
                {
                    RenderCells(ZoneCreationState.SelectedCells, DesignatorUtility.DragHighlightCellMat);
                }

                // Draw rectangle outline if in preview mode
                if (ZoneCreationState.IsInPreviewMode)
                {
                    RenderRectangleOutline(ZoneCreationState.RectangleStart.Value,
                                           ZoneCreationState.RectangleEnd.Value);
                }
            }

            // Render area painting mode preview
            if (AreaPaintingState.IsActive)
            {
                // Draw preview cells using native green highlight material
                if (AreaPaintingState.PreviewCells.Count > 0)
                {
                    RenderCells(AreaPaintingState.PreviewCells.ToList(), DesignatorUtility.DragHighlightCellMat);
                }

                // Draw already-staged cells
                if (AreaPaintingState.StagedCells.Count > 0)
                {
                    RenderCells(AreaPaintingState.StagedCells, DesignatorUtility.DragHighlightCellMat);
                }

                // Draw rectangle outline if in preview mode
                if (AreaPaintingState.IsInPreviewMode)
                {
                    RenderRectangleOutline(AreaPaintingState.RectangleStart.Value,
                                           AreaPaintingState.RectangleEnd.Value);
                }
            }

            // Render architect mode zone placement preview
            if (ArchitectState.IsInPlacementMode && ArchitectState.IsZoneDesignator())
            {
                // Draw preview cells using native green highlight material
                if (ArchitectState.PreviewCells.Count > 0)
                {
                    RenderCells(ArchitectState.PreviewCells.ToList(), DesignatorUtility.DragHighlightCellMat);
                }

                // Draw already-selected cells
                if (ArchitectState.SelectedCells.Count > 0)
                {
                    RenderCells(ArchitectState.SelectedCells, DesignatorUtility.DragHighlightCellMat);
                }

                // Draw rectangle outline if in preview mode
                if (ArchitectState.IsInPreviewMode)
                {
                    RenderRectangleOutline(ArchitectState.RectangleStart.Value,
                                           ArchitectState.RectangleEnd.Value);
                }
            }
        }

        /// <summary>
        /// Renders cell highlights using native RimWorld materials.
        /// </summary>
        private static void RenderCells(List<IntVec3> cells, Material material)
        {
            foreach (var cell in cells)
            {
                Vector3 pos = cell.ToVector3Shifted();
                pos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                Graphics.DrawMesh(MeshPool.plane10, pos, Quaternion.identity, material, 0);
            }
        }

        /// <summary>
        /// Renders rectangle outline using native GenDraw.
        /// </summary>
        private static void RenderRectangleOutline(IntVec3 start, IntVec3 end)
        {
            // Calculate the rectangle bounds
            int minX = Mathf.Min(start.x, end.x);
            int maxX = Mathf.Max(start.x, end.x);
            int minZ = Mathf.Min(start.z, end.z);
            int maxZ = Mathf.Max(start.z, end.z);

            // Use GenDraw.DrawFieldEdges for consistent look with native selection
            CellRect rect = CellRect.FromLimits(minX, minZ, maxX, maxZ);
            GenDraw.DrawFieldEdges(rect.Cells.ToList());
        }
    }
}
