using System.Collections.Generic;
using Verse;
using Verse.Sound;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared helper for shape preview functionality.
    /// Centralizes two-point selection logic used by ShapePlacementState, ZoneCreationState, and AreaPaintingState.
    /// </summary>
    public class ShapePreviewHelper
    {
        // State fields
        private IntVec3? firstCorner = null;
        private IntVec3? secondCorner = null;
        private List<IntVec3> previewCells = new List<IntVec3>();
        private ShapeType currentShape = ShapeType.FilledRectangle;

        // Sound feedback tracking
        private int lastCellCount = 0;
        private float lastDragRealTime = 0f;

        // Properties
        public IntVec3? FirstCorner => firstCorner;
        public IntVec3? SecondCorner => secondCorner;
        public IReadOnlyList<IntVec3> PreviewCells => previewCells;
        public ShapeType CurrentShape => currentShape;
        public bool HasFirstCorner => firstCorner.HasValue;
        public bool IsInPreviewMode => firstCorner.HasValue && secondCorner.HasValue;

        public void SetCurrentShape(ShapeType shape)
        {
            currentShape = shape;
        }

        public void SetFirstCorner(IntVec3 cell, string context = "", bool silent = false)
        {
            firstCorner = cell;
            secondCorner = null;
            previewCells.Clear();
            lastCellCount = 0;
            lastDragRealTime = Time.realtimeSinceStartup;
            if (!silent)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Preview.FirstPoint".Loc(cell.x, cell.z));
            }
            if (!string.IsNullOrEmpty(context))
                Log.Message($"{context}: First point set at {cell}");
        }

        public void SetSecondCorner(IntVec3 cell, string context = "")
        {
            if (!firstCorner.HasValue)
            {
                Log.Warning($"{context}: SetSecondCorner called without first point set");
                return;
            }

            secondCorner = cell;
            previewCells = ShapeHelper.CalculateCells(currentShape, firstCorner.Value, cell);

            string sizeText = ShapeHelper.FormatShapeSize(previewCells);
            // For regular rectangles, add cell count; for irregular shapes it's already in the size text
            string announcement = ShapeHelper.IsRegularRectangle(previewCells)
                ? (string)"RimWorldAccess.Building.Preview.SecondPointRegular".Translate(sizeText, previewCells.Count)
                : (string)"RimWorldAccess.Building.Preview.SecondPointIrregular".Translate(sizeText);

            TolkHelper.SpeakData(announcement);
            if (!string.IsNullOrEmpty(context))
                Log.Message($"{context}: Second point at {cell}. {previewCells.Count} cells");
        }

        public void UpdatePreview(IntVec3 cursor)
        {
            if (!firstCorner.HasValue) return;

            secondCorner = cursor;
            previewCells = ShapeHelper.CalculateCells(currentShape, firstCorner.Value, cursor);

            int cellCount = previewCells.Count;

            if (cellCount != lastCellCount)
            {
                PlayDragSound();
                lastCellCount = cellCount;

                // During drag we use corners since we don't know actual cells yet
                string sizeText = ShapeHelper.FormatShapeSizeFromCorners(firstCorner.Value, cursor);
                TolkHelper.SpeakData(sizeText, SpeechPriority.Low);
            }
        }

        public List<IntVec3> ConfirmShape(string context = "")
        {
            if (!IsInPreviewMode)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Paint.NoShapeToConfirm".Loc());
                return new List<IntVec3>();
            }

            var confirmedCells = new List<IntVec3>(previewCells);
            // FormatShapeSize returns "W by H" for regular rectangles, "N cells" for irregular shapes
            string sizeText = ShapeHelper.FormatShapeSize(confirmedCells);

            TolkHelper.Speak("RimWorldAccess.Building.Preview.SizeConfirmed".Loc(sizeText));
            if (!string.IsNullOrEmpty(context))
                Log.Message($"{context}: Confirmed {confirmedCells.Count} cells");

            // Reset for next selection
            Reset();

            return confirmedCells;
        }

        public void Cancel()
        {
            if (!HasFirstCorner) return;

            Reset();
            TolkHelper.Speak("RimWorldAccess.Building.Preview.ShapeCancelled".Loc());
        }

        public void Reset()
        {
            firstCorner = null;
            secondCorner = null;
            previewCells.Clear();
            lastCellCount = 0;
            lastDragRealTime = Time.realtimeSinceStartup;
            // Keep currentShape as is
        }

        public void FullReset()
        {
            Reset();
            currentShape = ShapeType.FilledRectangle;
        }

        private void PlayDragSound()
        {
            SoundInfo info = SoundInfo.OnCamera();
            info.SetParameter("TimeSinceDrag", Time.realtimeSinceStartup - lastDragRealTime);
            SoundDefOf.Designate_DragStandard_Changed.PlayOneShot(info);
            lastDragRealTime = Time.realtimeSinceStartup;
        }
    }
}
