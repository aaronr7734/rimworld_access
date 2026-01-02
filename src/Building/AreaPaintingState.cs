using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Sound;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Maintains state for area painting mode (expanding/shrinking areas with keyboard).
    /// Allows keyboard navigation and rectangle-based painting of areas.
    /// Uses RimWorld's native APIs for feedback.
    /// </summary>
    public static class AreaPaintingState
    {
        private static bool isActive = false;
        private static Area targetArea = null;
        private static bool isExpanding = true; // true = expand, false = shrink
        private static List<IntVec3> stagedCells = new List<IntVec3>(); // Cells staged for addition/removal

        // Rectangle selection state
        private static IntVec3? rectangleStart = null;
        private static IntVec3? rectangleEnd = null;
        private static List<IntVec3> previewCells = new List<IntVec3>();

        // For native sound feedback (matches DesignationDragger behavior)
        private static int lastCellCount = 0;
        private static float lastDragRealTime = 0f;

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
        /// Whether a rectangle start corner has been set.
        /// </summary>
        public static bool HasRectangleStart => rectangleStart.HasValue;

        /// <summary>
        /// Whether we are actively previewing a rectangle (start and end set).
        /// </summary>
        public static bool IsInPreviewMode => rectangleStart.HasValue && rectangleEnd.HasValue;

        /// <summary>
        /// The start corner of the rectangle being selected.
        /// </summary>
        public static IntVec3? RectangleStart => rectangleStart;

        /// <summary>
        /// The end corner of the rectangle being selected.
        /// </summary>
        public static IntVec3? RectangleEnd => rectangleEnd;

        /// <summary>
        /// Cells in the current rectangle preview.
        /// </summary>
        public static IReadOnlyList<IntVec3> PreviewCells => previewCells;

        /// <summary>
        /// Enters area painting mode for expanding an area.
        /// </summary>
        public static void EnterExpandMode(Area area)
        {
            Log.Message($"RimWorld Access: EnterExpandMode called for area: {area?.Label ?? "null"}");

            isActive = true;
            targetArea = area;
            isExpanding = true;
            stagedCells.Clear();
            rectangleStart = null;
            rectangleEnd = null;
            previewCells.Clear();
            lastCellCount = 0;
            lastDragRealTime = Time.realtimeSinceStartup;

            Log.Message($"RimWorld Access: isActive set to {isActive}, targetArea set to {targetArea?.Label}");

            // Ensure map navigation is initialized
            if (!MapNavigationState.IsInitialized && area.Map != null)
            {
                MapNavigationState.Initialize(area.Map);
                Log.Message("RimWorld Access: Initialized map navigation");
            }

            TolkHelper.Speak($"Expanding area: {area.Label}. Press Space to set corners, Enter to confirm, Escape to cancel.");
            Log.Message("RimWorld Access: Area painting mode entered");
        }

        /// <summary>
        /// Enters area painting mode for shrinking an area.
        /// </summary>
        public static void EnterShrinkMode(Area area)
        {
            isActive = true;
            targetArea = area;
            isExpanding = false;
            stagedCells.Clear();
            rectangleStart = null;
            rectangleEnd = null;
            previewCells.Clear();
            lastCellCount = 0;
            lastDragRealTime = Time.realtimeSinceStartup;

            // Ensure map navigation is initialized
            if (!MapNavigationState.IsInitialized && area.Map != null)
            {
                MapNavigationState.Initialize(area.Map);
            }

            TolkHelper.Speak($"Shrinking area: {area.Label}. Press Space to set corners, Enter to confirm, Escape to cancel.");
        }

        /// <summary>
        /// Sets the start corner for rectangle selection.
        /// </summary>
        public static void SetRectangleStart(IntVec3 cell)
        {
            rectangleStart = cell;
            rectangleEnd = null;
            previewCells.Clear();
            lastCellCount = 0;
            lastDragRealTime = Time.realtimeSinceStartup;
            TolkHelper.Speak($"Start at {cell.x}, {cell.z}");
        }

        /// <summary>
        /// Updates the rectangle preview as the cursor moves.
        /// Plays native sound feedback when cell count changes.
        /// </summary>
        public static void UpdatePreview(IntVec3 endCell)
        {
            if (!rectangleStart.HasValue) return;

            rectangleEnd = endCell;

            // Use native CellRect API for rectangle calculation
            CellRect rect = CellRect.FromLimits(rectangleStart.Value, endCell);
            previewCells = rect.Cells.ToList();

            int width = rect.Width;
            int height = rect.Height;
            int cellCount = previewCells.Count;

            // Play native sound when cell count changes (like DesignationDragger)
            if (cellCount != lastCellCount)
            {
                SoundInfo info = SoundInfo.OnCamera();
                info.SetParameter("TimeSinceDrag", Time.realtimeSinceStartup - lastDragRealTime);
                SoundDefOf.Designate_DragStandard_Changed.PlayOneShot(info);
                lastDragRealTime = Time.realtimeSinceStartup;
                lastCellCount = cellCount;

                // Announce dimensions like native display (only when >= 5 cells)
                if (width >= 5 || height >= 5)
                {
                    TolkHelper.Speak($"{width} by {height}", SpeechPriority.Low);
                }
                else if (cellCount >= 4)
                {
                    TolkHelper.Speak($"{cellCount}", SpeechPriority.Low);
                }
            }
        }

        /// <summary>
        /// Confirms the current rectangle preview, adding all cells to staged list.
        /// </summary>
        public static void ConfirmRectangle()
        {
            if (!IsInPreviewMode)
            {
                TolkHelper.Speak("No rectangle to confirm");
                return;
            }

            // Add preview cells to staged (avoiding duplicates)
            int addedCount = 0;
            foreach (var cell in previewCells)
            {
                if (!stagedCells.Contains(cell))
                {
                    stagedCells.Add(cell);
                    addedCount++;
                }
            }

            CellRect rect = CellRect.FromLimits(rectangleStart.Value, rectangleEnd.Value);
            TolkHelper.Speak($"{rect.Width} by {rect.Height}, {addedCount} cells added. Total staged: {stagedCells.Count}");

            // Clear rectangle state for next selection
            rectangleStart = null;
            rectangleEnd = null;
            previewCells.Clear();
            lastCellCount = 0;
        }

        /// <summary>
        /// Cancels the current rectangle selection without adding cells.
        /// </summary>
        public static void CancelRectangle()
        {
            if (!HasRectangleStart)
            {
                return;
            }

            rectangleStart = null;
            rectangleEnd = null;
            previewCells.Clear();
            lastCellCount = 0;
            TolkHelper.Speak("Rectangle cancelled");
        }

        /// <summary>
        /// Toggles staging of the cell at the current cursor position.
        /// </summary>
        public static void ToggleStageCell()
        {
            Log.Message($"RimWorld Access: ToggleStageCell called, isActive={isActive}, targetArea={targetArea?.Label ?? "null"}");

            if (!isActive || targetArea == null)
            {
                Log.Message("RimWorld Access: Not active or no target area");
                return;
            }

            IntVec3 currentPos = MapNavigationState.CurrentCursorPosition;
            Log.Message($"RimWorld Access: Current position: {currentPos}");

            if (!currentPos.InBounds(targetArea.Map))
            {
                TolkHelper.Speak("Position out of bounds");
                Log.Message("RimWorld Access: Position out of bounds");
                return;
            }

            // Toggle staging
            if (stagedCells.Contains(currentPos))
            {
                stagedCells.Remove(currentPos);
                TolkHelper.Speak($"Deselected, {currentPos.x}, {currentPos.z}");
                Log.Message($"RimWorld Access: Deselected cell at {currentPos}");
            }
            else
            {
                stagedCells.Add(currentPos);
                TolkHelper.Speak($"Selected, {currentPos.x}, {currentPos.z}");
                Log.Message($"RimWorld Access: Selected cell at {currentPos}");
            }
        }

        /// <summary>
        /// Confirms all staged changes and exits.
        /// </summary>
        public static void Confirm()
        {
            Log.Message("RimWorld Access: Confirm() called");

            if (!isActive || targetArea == null)
            {
                Log.Message("RimWorld Access: Not active or no target area");
                return;
            }

            if (stagedCells.Count == 0)
            {
                TolkHelper.Speak("No cells selected. Area unchanged.");
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

            string action = isExpanding ? "added to" : "removed from";
            TolkHelper.Speak($"{stagedCells.Count} cells {action} {targetArea.Label}. Total cells: {targetArea.TrueCount}");
            Log.Message($"RimWorld Access: Applied {stagedCells.Count} changes");

            isActive = false;
            targetArea = null;
            stagedCells.Clear();
            rectangleStart = null;
            rectangleEnd = null;
            previewCells.Clear();
        }

        /// <summary>
        /// Cancels all staged changes and exits.
        /// </summary>
        public static void Cancel()
        {
            Log.Message("RimWorld Access: Cancel() called");

            if (targetArea != null)
            {
                TolkHelper.Speak("Area editing cancelled");
            }

            isActive = false;
            targetArea = null;
            stagedCells.Clear();
            rectangleStart = null;
            rectangleEnd = null;
            previewCells.Clear();

            Log.Message("RimWorld Access: Area painting cancelled");
        }

        /// <summary>
        /// Exits area painting mode without applying changes.
        /// </summary>
        private static void Exit()
        {
            Log.Message("RimWorld Access: AreaPaintingState.Exit() called");

            isActive = false;
            targetArea = null;
            stagedCells.Clear();
            rectangleStart = null;
            rectangleEnd = null;
            previewCells.Clear();

            Log.Message("RimWorld Access: Area painting mode exited");
        }

    }
}
