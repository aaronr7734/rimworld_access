using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Defines the selection mode for area painting.
    /// </summary>
    public enum AreaSelectionMode
    {
        BoxSelection,    // Space sets corners for shape selection
        SingleTile       // Space toggles individual tiles
    }

    /// <summary>
    /// Maintains state for area painting mode (expanding/shrinking areas with keyboard).
    /// Allows keyboard navigation and shape-based painting of areas.
    /// Supports all shape types: rectangles, ovals, lines via ShapeHelper.
    /// Uses RimWorld's native APIs for feedback.
    /// </summary>
    public static class AreaPaintingState
    {
        private static bool isActive = false;
        private static Area targetArea = null;
        private static bool isExpanding = true; // true = expand, false = shrink
        private static List<IntVec3> stagedCells = new List<IntVec3>(); // Cells staged for addition/removal
        private static AreaSelectionMode selectionMode = AreaSelectionMode.BoxSelection; // Default to box selection
        private static Designator currentDesignator = null; // The designator that initiated area painting (if any)

        // Shape preview helper for centralized shape selection logic
        private static readonly ShapePreviewHelper previewHelper = new ShapePreviewHelper();

        // Local shape tracking - synced with previewHelper
        private static ShapeType currentShape = ShapeType.FilledRectangle;

        /// <summary>
        /// Whether area painting mode is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// The area being painted.
        /// </summary>
        public static Area TargetArea => targetArea;

        /// <summary>
        /// Whether we're expanding (true) or shrinking (false) the area.
        /// </summary>
        public static bool IsExpanding => isExpanding;

        /// <summary>
        /// List of cells staged for addition/removal.
        /// </summary>
        public static List<IntVec3> StagedCells => stagedCells;

        /// <summary>
        /// The currently selected shape type for selection.
        /// </summary>
        public static ShapeType CurrentShape => currentShape;

        /// <summary>
        /// Whether a first corner has been set (shape start).
        /// </summary>
        public static bool HasFirstCorner => previewHelper.HasFirstCorner;

        /// <summary>
        /// Whether we are actively previewing a shape (first and second corners set).
        /// </summary>
        public static bool IsInPreviewMode => previewHelper.IsInPreviewMode;

        /// <summary>
        /// The first corner of the shape being selected.
        /// </summary>
        public static IntVec3? FirstCorner => previewHelper.FirstCorner;

        /// <summary>
        /// The second corner of the shape being selected.
        /// </summary>
        public static IntVec3? SecondCorner => previewHelper.SecondCorner;

        /// <summary>
        /// Cells in the current shape preview.
        /// </summary>
        public static IReadOnlyList<IntVec3> PreviewCells => previewHelper.PreviewCells;

        /// <summary>
        /// Gets the current selection mode (BoxSelection or SingleTile).
        /// </summary>
        public static AreaSelectionMode SelectionMode => selectionMode;

        // Backwards compatibility properties (map to previewHelper)
        /// <summary>
        /// Whether a rectangle start corner has been set (backwards compatibility).
        /// </summary>
        public static bool HasRectangleStart => previewHelper.HasFirstCorner;

        /// <summary>
        /// The start corner of the rectangle being selected (backwards compatibility).
        /// </summary>
        public static IntVec3? RectangleStart => previewHelper.FirstCorner;

        /// <summary>
        /// The end corner of the rectangle being selected (backwards compatibility).
        /// </summary>
        public static IntVec3? RectangleEnd => previewHelper.SecondCorner;

        /// <summary>
        /// Toggles between box selection and single tile selection modes.
        /// </summary>
        public static void ToggleSelectionMode()
        {
            selectionMode = (selectionMode == AreaSelectionMode.BoxSelection)
                ? AreaSelectionMode.SingleTile
                : AreaSelectionMode.BoxSelection;

            string modeName = (selectionMode == AreaSelectionMode.BoxSelection)
                ? "RimWorldAccess.Building.Paint.ModeBox".Translate()
                : "RimWorldAccess.Building.Paint.ModeSingle".Translate();
            TolkHelper.SpeakData(modeName);
        }

        /// <summary>
        /// Sets the current shape type for selection.
        /// Called when user selects a shape from the shape selection menu.
        /// Validates that the shape is allowed for the current context.
        /// </summary>
        /// <param name="shape">The shape type to use</param>
        public static void SetCurrentShape(ShapeType shape)
        {
            var allowed = GetAvailableShapes();
            if (allowed.Contains(shape))
            {
                currentShape = shape;
                previewHelper.SetCurrentShape(shape);
                string shapeName = ShapeHelper.GetShapeName(shape);
                TolkHelper.Speak("RimWorldAccess.Building.Paint.ShapeChanged".Loc(shapeName));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Building.Paint.ShapeUnavailable".Loc());
            }
        }

        /// <summary>
        /// Gets the available shapes for the current context.
        /// Uses the designator's DrawStyleCategory if available,
        /// otherwise falls back to FilledRectangle for direct area painting.
        /// </summary>
        /// <returns>List of available shape types</returns>
        public static List<ShapeType> GetAvailableShapes()
        {
            if (currentDesignator != null)
                return ShapeHelper.GetAvailableShapes(currentDesignator);
            // Fallback for direct area painting without designator - areas typically support filled rectangle
            return new List<ShapeType> { ShapeType.FilledRectangle };
        }

        /// <summary>
        /// Enters area painting mode for expanding an area.
        /// </summary>
        /// <param name="area">The area to expand</param>
        /// <param name="designator">Optional designator that initiated the area painting (for shape validation)</param>
        public static void EnterExpandMode(Area area, Designator designator = null)
        {
            isActive = true;
            targetArea = area;
            isExpanding = true;
            stagedCells.Clear();
            previewHelper.Reset();
            selectionMode = AreaSelectionMode.BoxSelection; // Default to box selection
            currentDesignator = designator;

            // Set default shape based on designator (if provided)
            currentShape = designator != null ? ShapeHelper.GetDefaultShape(designator) : ShapeType.FilledRectangle;
            previewHelper.SetCurrentShape(currentShape);

            // Ensure map navigation is initialized
            if (!MapNavigationState.IsInitialized && area.Map != null)
            {
                MapNavigationState.Initialize(area.Map);
                Log.Message("RimWorld Access: Initialized map navigation");
            }

            string shapeName = ShapeHelper.GetShapeName(currentShape);
            string hint = "RimWorldAccess.Building.Paint.PromptHint".Translate();
            TolkHelper.Speak("RimWorldAccess.Building.Paint.EnterExpand".Loc(area.Label, shapeName, hint));
        }

        /// <summary>
        /// Enters area painting mode for shrinking an area.
        /// </summary>
        /// <param name="area">The area to shrink</param>
        /// <param name="designator">Optional designator that initiated the area painting (for shape validation)</param>
        public static void EnterShrinkMode(Area area, Designator designator = null)
        {
            isActive = true;
            targetArea = area;
            isExpanding = false;
            stagedCells.Clear();
            previewHelper.Reset();
            selectionMode = AreaSelectionMode.BoxSelection; // Default to box selection
            currentDesignator = designator;

            // Set default shape based on designator (if provided)
            currentShape = designator != null ? ShapeHelper.GetDefaultShape(designator) : ShapeType.FilledRectangle;
            previewHelper.SetCurrentShape(currentShape);

            // Ensure map navigation is initialized
            if (!MapNavigationState.IsInitialized && area.Map != null)
            {
                MapNavigationState.Initialize(area.Map);
            }

            string shapeName = ShapeHelper.GetShapeName(currentShape);
            string hint = "RimWorldAccess.Building.Paint.PromptHint".Translate();
            TolkHelper.Speak("RimWorldAccess.Building.Paint.EnterShrink".Loc(area.Label, shapeName, hint));
        }

        /// <summary>
        /// Sets the first corner for shape selection.
        /// </summary>
        /// <param name="cell">The cell position for the first corner</param>
        public static void SetFirstCorner(IntVec3 cell)
        {
            previewHelper.SetFirstCorner(cell, "[AreaPaintingState]");
        }

        /// <summary>
        /// Sets the start corner for rectangle selection (backwards compatibility).
        /// </summary>
        public static void SetRectangleStart(IntVec3 cell)
        {
            SetFirstCorner(cell);
        }

        /// <summary>
        /// Sets the second corner of the shape.
        /// </summary>
        /// <param name="cell">The cell position for the second corner</param>
        public static void SetSecondCorner(IntVec3 cell)
        {
            previewHelper.SetSecondCorner(cell, "[AreaPaintingState]");
        }

        /// <summary>
        /// Updates the shape preview as the cursor moves.
        /// Plays native sound feedback when cell count changes.
        /// </summary>
        /// <param name="endCell">The current cursor position</param>
        public static void UpdatePreview(IntVec3 endCell)
        {
            previewHelper.UpdatePreview(endCell);
        }

        /// <summary>
        /// Confirms the current shape preview, adding all cells to staged list.
        /// </summary>
        public static void ConfirmShape()
        {
            if (!IsInPreviewMode)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Paint.NoShapeToConfirm".Loc());
                return;
            }

            // Get confirmed cells from previewHelper (this resets the helper's state)
            var confirmedCells = previewHelper.ConfirmShape("[AreaPaintingState]");

            // Find cells that aren't already staged
            int addedCount = 0;
            foreach (var cell in confirmedCells.Where(cell => !stagedCells.Contains(cell)))
            {
                stagedCells.Add(cell);
                addedCount++;
            }

            // Format shape size - uses "W by H" for regular rectangles, "N cells" for irregular shapes
            string shapeSize = ShapeHelper.FormatShapeSize(confirmedCells);

            // Regular: "5 by 7, 35 cells added. Total: 100"
            // Irregular: "32 cells added. Total: 100" (no duplicate dimension info)
            string announcement = ShapeHelper.IsRegularRectangle(confirmedCells)
                ? (string)"RimWorldAccess.Building.Paint.ConfirmShapeRegular".Translate(shapeSize, addedCount, stagedCells.Count)
                : (string)"RimWorldAccess.Building.Paint.ConfirmShapeIrregular".Translate(addedCount, stagedCells.Count);

            TolkHelper.SpeakData(announcement);
        }

        /// <summary>
        /// Confirms the current rectangle preview, adding all cells to staged list (backwards compatibility).
        /// </summary>
        public static void ConfirmRectangle()
        {
            ConfirmShape();
        }

        /// <summary>
        /// Cancels the current shape selection without adding cells.
        /// </summary>
        public static void CancelShape()
        {
            if (!previewHelper.HasFirstCorner)
            {
                return;
            }

            previewHelper.Cancel();
        }

        /// <summary>
        /// Cancels the current rectangle selection without adding cells (backwards compatibility).
        /// </summary>
        public static void CancelRectangle()
        {
            CancelShape();
        }

        /// <summary>
        /// Toggles staging of the cell at the current cursor position.
        /// </summary>
        public static void ToggleStageCell()
        {
            if (!isActive || targetArea == null)
            {
                return;
            }

            IntVec3 currentPos = MapNavigationState.CurrentCursorPosition;

            if (!currentPos.InBounds(targetArea.Map))
            {
                TolkHelper.Speak("RimWorldAccess.Building.Paint.PositionOutOfBounds".Loc());
                return;
            }

            // Toggle staging
            if (stagedCells.Contains(currentPos))
            {
                stagedCells.Remove(currentPos);
                TolkHelper.Speak("RimWorldAccess.Building.Paint.Deselected".Loc(currentPos.x, currentPos.z));
            }
            else
            {
                stagedCells.Add(currentPos);
                TolkHelper.SpeakData(MapNavigationState.FormatSelectedCell(currentPos));
            }
        }

        /// <summary>
        /// Confirms all staged changes and exits.
        /// </summary>
        public static void Confirm()
        {
            if (!isActive || targetArea == null)
            {
                return;
            }

            if (stagedCells.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Paint.NoCellsSelected".Loc());
                Log.Message("RimWorld Access: No selected cells");
                Exit();
                return;
            }

            // Apply all staged changes
            foreach (IntVec3 cell in stagedCells)
            {
                if (cell.InBounds(targetArea.Map))
                {
                    if (isExpanding)
                    {
                        targetArea[cell] = true;
                    }
                    else
                    {
                        targetArea[cell] = false;
                    }
                }
            }

            string key = isExpanding
                ? "RimWorldAccess.Building.Paint.AddedTo"
                : "RimWorldAccess.Building.Paint.RemovedFrom";
            TolkHelper.Speak(key.Loc(stagedCells.Count, targetArea.Label, targetArea.TrueCount));

            isActive = false;
            targetArea = null;
            stagedCells.Clear();
            previewHelper.Reset();
            selectionMode = AreaSelectionMode.BoxSelection; // Reset to default mode
            currentDesignator = null; // Clear designator reference
        }

        /// <summary>
        /// Cancels all staged changes and exits.
        /// </summary>
        public static void Cancel()
        {
            if (targetArea != null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Paint.Cancelled".Loc());
            }

            isActive = false;
            targetArea = null;
            stagedCells.Clear();
            previewHelper.Reset();
            selectionMode = AreaSelectionMode.BoxSelection; // Reset to default mode
            currentDesignator = null; // Clear designator reference
        }

        /// <summary>
        /// Exits area painting mode without applying changes.
        /// </summary>
        private static void Exit()
        {
            isActive = false;
            targetArea = null;
            stagedCells.Clear();
            previewHelper.Reset();
            selectionMode = AreaSelectionMode.BoxSelection; // Reset to default mode
            currentDesignator = null; // Clear designator reference
        }

        /// <summary>
        /// Gets the dimensions of the current shape preview.
        /// </summary>
        /// <returns>Tuple of (width, height) or (0, 0) if no preview</returns>
        public static (int width, int height) GetCurrentDimensions()
        {
            if (!previewHelper.HasFirstCorner || !MapNavigationState.IsInitialized)
                return (0, 0);

            IntVec3 target = previewHelper.SecondCorner ?? MapNavigationState.CurrentCursorPosition;
            return ShapeHelper.GetDimensions(previewHelper.FirstCorner.Value, target);
        }

    }
}
