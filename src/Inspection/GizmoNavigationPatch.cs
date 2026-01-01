using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch for GizmoGridDrawer to add visual highlighting for selected gizmos
    /// during keyboard navigation.
    /// </summary>
    [HarmonyPatch(typeof(GizmoGridDrawer))]
    [HarmonyPatch("DrawGizmoGrid")]
    public static class GizmoNavigationPatch
    {
        /// <summary>
        /// Postfix patch that draws a highlight box around the currently selected gizmo
        /// when gizmo navigation is active.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPriority(Priority.High)]
        public static void Postfix()
        {
            if (!GizmoNavigationState.IsActive)
                return;

            if (GizmoNavigationState.AvailableGizmos.Count == 0)
                return;

            if (GizmoNavigationState.SelectedGizmoIndex < 0 ||
                GizmoNavigationState.SelectedGizmoIndex >= GizmoNavigationState.AvailableGizmos.Count)
                return;

            // Calculate the position of the selected gizmo
            // This must match the layout logic in GizmoGridDrawer.DrawGizmoGrid()
            Rect gizmoRect = CalculateGizmoRect(GizmoNavigationState.SelectedGizmoIndex);

            // Draw highlight box
            DrawHighlight(gizmoRect);
        }

        /// <summary>
        /// Calculates the screen rect for a gizmo at the given index.
        /// This replicates the layout logic from GizmoGridDrawer.DrawGizmoGrid().
        /// </summary>
        private static Rect CalculateGizmoRect(int index)
        {
            // Get screen dimensions
            float screenWidth = UI.screenWidth;
            float screenHeight = UI.screenHeight;

            // Constants from GizmoGridDrawer
            const float gizmoSize = 75f;
            const float gizmoSpacingHorizontal = 5f;
            const float gizmoSpacingVertical = 14f;

            // Starting position (bottom-right of screen, left of inspect pane)
            // InspectPaneUtility.InspectTabButtonSize returns 72f
            float inspectPaneWidth = 72f;
            float startX = 14f + inspectPaneWidth;
            float startY = screenHeight - 35f - gizmoSize;

            // Calculate how many gizmos fit per row
            float availableWidth = screenWidth - startX - 20f; // 20f right margin
            int gizmosPerRow = Mathf.Max(1, Mathf.FloorToInt((availableWidth + gizmoSpacingHorizontal) / (gizmoSize + gizmoSpacingHorizontal)));

            // Calculate row and column for this index
            int row = index / gizmosPerRow;
            int col = index % gizmosPerRow;

            // Calculate position
            float x = startX + col * (gizmoSize + gizmoSpacingHorizontal);
            float y = startY - row * (gizmoSize + gizmoSpacingVertical);

            return new Rect(x, y, gizmoSize, gizmoSize);
        }

        /// <summary>
        /// Draws a highlight box around the specified rect.
        /// </summary>
        private static void DrawHighlight(Rect rect)
        {
            // Expand the rect slightly for the highlight
            Rect highlightRect = rect.ExpandedBy(3f);

            // Draw a colored border
            Color highlightColor = new Color(0.5f, 0.8f, 1.0f, 0.8f); // Light blue
            Widgets.DrawBox(highlightRect, 3);

            // Draw a semi-transparent overlay
            Color overlayColor = new Color(0.5f, 0.8f, 1.0f, 0.2f);
            Widgets.DrawBoxSolid(highlightRect, overlayColor);
        }
    }
}
