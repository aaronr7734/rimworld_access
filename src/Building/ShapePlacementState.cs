using System.Collections.Generic;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>The phases of the shape placement workflow.</summary>
    public enum PlacementPhase
    {
        Inactive,

        SettingFirstCorner,

        SettingSecondCorner,

        Previewing
    }

    /// <summary>
    /// The outcome of a placement: what was placed, what blocked it, and what it cost.
    /// </summary>
    public class PlacementResult
    {
        public int PlacedCount { get; set; }

        public int ObstacleCount { get; set; }

        /// <summary>Set when a rect-designation handler, not the per-cell path, did the work.</summary>
        public bool WasRectDesignation { get; set; }

        /// <summary>How many things the rect designation added to the selection.</summary>
        public int SelectionAdded { get; set; }

        /// <summary>True when the handled designator emitted its own game message.</summary>
        public bool DesignatorSpoke { get; set; }

        /// <summary>True when the rect designator's product is a selection (see RectDesignationResult.IsSelection).</summary>
        public bool WasSelectionDesignation { get; set; }

        public List<IntVec3> PlacedCells { get; set; }

        public List<IntVec3> ObstacleCells { get; set; }

        /// <summary>Cost of the primary resource only; <see cref="ResourceCosts"/> carries the full breakdown.</summary>
        public int TotalResourceCost { get; set; }

        public string ResourceName { get; set; }

        /// <summary>Every resource consumed, in CostList order; populated for multi-ingredient fixed-cost buildables.</summary>
        public List<(int Count, string Name)> ResourceCosts { get; set; }

        /// <summary>The placed Things, for undo.</summary>
        public List<Thing> PlacedBlueprints { get; set; }

        /// <summary>True when the operation would delete a whole zone; the result then carries no placements and the caller must confirm first.</summary>
        public bool NeedsFullDeletionConfirmation { get; set; }

        /// <summary>The zone <see cref="NeedsFullDeletionConfirmation"/> refers to.</summary>
        public Zone ZonePendingDeletion { get; set; }

        /// <summary>The cells that would delete that zone.</summary>
        public List<IntVec3> PendingValidCells { get; set; }

        public int ProtectedCount { get; set; }

        /// <summary>Cells skipped by meditation focus/tree protection.</summary>
        public List<IntVec3> ProtectedCells { get; set; }

        /// <summary>Labels of the trees and foci that caused those blocks.</summary>
        public HashSet<string> ProtectedByLabels { get; set; }

        public PlacementResult()
        {
            PlacedCells = new List<IntVec3>();
            ObstacleCells = new List<IntVec3>();
            PlacedBlueprints = new List<Thing>();
            PendingValidCells = new List<IntVec3>();
            ProtectedCells = new List<IntVec3>();
            ProtectedByLabels = new HashSet<string>();
            ResourceName = string.Empty;
            ResourceCosts = new List<(int Count, string Name)>();
        }
    }

    /// <summary>
    /// The scope the last Ctrl+A applied, so repeated presses step outward and Ctrl+Shift+A
    /// steps back.
    /// </summary>
    public enum CtrlAStage
    {
        None,
        /// <summary>An enclosure: a room or blueprint flood.</summary>
        Enclosure,

        EntireMap
    }

    /// <summary>Placement state captured before a Ctrl+A, for Ctrl+Shift+A to step back to.</summary>
    internal struct CtrlASnapshot
    {
        public bool HadFirstPoint;
        public bool HadSecondPoint;
        public IntVec3 First;
        public IntVec3 Second;
        public CtrlAStage Stage;
    }

    /// <summary>
    /// State machine for two-point shape placement: Enter, SetFirstPoint,
    /// SetSecondPoint/UpdatePreview, PlaceBlueprints.
    /// </summary>
    public static class ShapePlacementState
    {
        private static readonly ShapePreviewHelper previewHelper = new ShapePreviewHelper();

        private static PlacementPhase currentPhase = PlacementPhase.Inactive;
        private static ShapeType currentShape = ShapeType.Manual;
        private static Designator activeDesignator = null;

        // Holds the next Enter()'s entry announcement so a color picker opening over the
        // placement speaks first; AnnouncePendingEntry() replays it when the picker closes.
        public static bool SuppressNextEntryAnnouncement { get; set; } = false;
        private static string pendingEntryAnnouncement = null;

        /// <summary>Speaks and clears any entry announcement held back for a color picker; a no-op when none is pending.</summary>
        public static void AnnouncePendingEntry()
        {
            if (pendingEntryAnnouncement == null)
                return;
            string text = pendingEntryAnnouncement;
            pendingEntryAnnouncement = null;
            TolkHelper.SpeakData(text);
        }

        /// <summary>
        /// Handles a paint/plan color picker closing. A selection replays the held entry so
        /// placing proceeds. A cancel with the entry still pending means this picker auto-opened
        /// with the tool, so the whole tool is cancelled; a picker reopened mid-placement has no
        /// pending entry and cancelling it simply returns to placing.
        /// </summary>
        public static void OnColorPickerClosed(bool cancelled)
        {
            if (!cancelled)
            {
                AnnouncePendingEntry();
                return;
            }

            if (pendingEntryAnnouncement != null)
            {
                pendingEntryAnnouncement = null;
                Find.DesignatorManager?.Deselect();
                TolkHelper.Speak("RimWorldAccess.Building.Place.CancelFromFirstCorner".Loc(), SpeechPriority.High);
            }
        }

        private static bool hasViewingModeOnStack = false;

        // The cursor position at entry drives the zone expand/create decision, so the behavior
        // matches what entry announced.
        private static IntVec3 entryCursorPosition = IntVec3.Invalid;

        // Ctrl+A steps none -> enclosure -> whole map; Ctrl+Shift+A steps back through the
        // stacked corner snapshots. Any manual point change clears the history, which would
        // otherwise no longer be coherent.
        private static readonly Stack<CtrlASnapshot> ctrlAHistory = new Stack<CtrlASnapshot>();
        private static CtrlAStage ctrlAStage = CtrlAStage.None;

        #region Properties

        /// <summary>
        /// Whether shape placement is active. Also verifies a designator really is selected, so
        /// an external deselect cannot leave this reporting stale state.
        /// </summary>
        public static bool IsActive =>
            currentPhase != PlacementPhase.Inactive &&
            Find.DesignatorManager?.SelectedDesignator != null;

        public static PlacementPhase CurrentPhase => currentPhase;

        public static ShapeType CurrentShape => currentShape;

        public static IntVec3? FirstPoint => previewHelper.FirstCorner;

        public static IntVec3? SecondPoint => previewHelper.SecondCorner;

        /// <summary>The current preview's cells, updated as the cursor moves.</summary>
        public static IReadOnlyList<IntVec3> PreviewCells => previewHelper.PreviewCells;

        public static bool HasFirstPoint => previewHelper.HasFirstCorner;

        /// <summary>Whether placement is in progress, with points set. Guards against external actions corrupting state.</summary>
        public static bool IsPlacementInProgress => IsActive && HasFirstPoint;

        public static bool IsInPreviewMode => previewHelper.IsInPreviewMode;

        public static Designator ActiveDesignator => activeDesignator;

        /// <summary>Whether a viewing-mode state is on the stack to return to.</summary>
        public static bool HasViewingModeOnStack => hasViewingModeOnStack;

        /// <summary>The scope last applied by Ctrl+A; any manual point change resets it to None.</summary>
        public static CtrlAStage CurrentCtrlAStage => ctrlAStage;

        /// <summary>Whether Ctrl+Shift+A has anything to step back to.</summary>
        public static bool HasCtrlAHistory => ctrlAHistory.Count > 0;

        #endregion

        #region State Management

        /// <summary>Enters shape placement mode with the given designator and shape.</summary>
        public static void Enter(Designator designator, ShapeType shape, bool fromViewingMode = false)
        {
            activeDesignator = designator;
            currentShape = shape;
            currentPhase = PlacementPhase.SettingFirstCorner;
            previewHelper.Reset();
            previewHelper.SetCurrentShape(shape);
            hasViewingModeOnStack = fromViewingMode;
            ClearCtrlAHistory();

            SyncShapeToGameStyle(designator, shape);

            entryCursorPosition = MapNavigationState.CurrentCursorPosition;

            // Classified once and reused by the zone check and the announcement build.
            DesignatorClassification classification = ShapeHelper.ClassifyDesignator(designator);

            // The expand/create decision must rest on cursor position alone, not on a zone left
            // selected by an earlier operation, so entry's announcement matches the behavior.
            // The gizmo-based expand flow uses GizmoZoneEditState and preserves its selection.
            if (classification.IsZone)
            {
                Find.Selector.ClearSelection();
            }

            string shapeName = ShapeHelper.GetShapeName(shape);
            string designatorLabel = ArchitectHelper.GetSanitizedLabel(designator);

            string announcement = BuildEnterAnnouncement(designator, designatorLabel, shape, shapeName, classification);

            // Hold the announcement when a color picker is about to open over this placement, so
            // the two do not run together; the picker replays it after the choice.
            bool suppress = SuppressNextEntryAnnouncement;
            SuppressNextEntryAnnouncement = false;
            if (suppress)
                pendingEntryAnnouncement = announcement;
            else
                TolkHelper.SpeakData(announcement);

        }

        /// <summary>
        /// The entry announcement: mode, shape, item, size, rotation, and key hints.
        /// </summary>
        private static string BuildEnterAnnouncement(Designator designator, string designatorLabel, ShapeType shape, string shapeName, DesignatorClassification classification)
        {
            List<string> parts = new List<string>();

            if (shape == ShapeType.Manual)
            {
                parts.Add("RimWorldAccess.Building.Place.ModeManual".Translate());
                if (classification.IsOrder || ShapeHelper.IsCellsDesignator(designator) || classification.IsZone)
                {
                    parts.Add(designatorLabel);
                }
                else
                {
                    parts.Add("RimWorldAccess.Building.Place.PlacingDesignator".Translate(designatorLabel));
                }
            }
            else
            {
                parts.Add("RimWorldAccess.Building.Place.ModeShape".Translate());
                parts.Add("RimWorldAccess.Building.ShapeSelect.ShapeSelected".Translate(shapeName));
                parts.Add(designatorLabel);
            }

            if (classification.IsZone && !classification.IsDelete)
            {
                IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
                string zoneModeInfo = ZoneSelectionHelper.GetZoneModeAnnouncement(designator, cursorPos);
                parts.Add(zoneModeInfo);
            }

            if (designator is Designator_Place placeDesignator && placeDesignator.PlacingDef != null)
            {
                BuildableDef def = placeDesignator.PlacingDef;
                IntVec2 size = def.Size;

                // A non-rotatable building auto-detects its orientation when placed.
                bool isRotatable = def is ThingDef thingDef && thingDef.rotatable;

                // A rotatable building's size comes from GetRotationAnnouncementForDef instead.
                if (!isRotatable)
                {
                    if (size.x == 1 && size.z == 1)
                    {
                        parts.Add("RimWorldAccess.Building.Place.SizeOneTile".Translate());
                    }
                    else
                    {
                        parts.Add("RimWorldAccess.Building.Place.SizeWxH".Translate(size.x, size.z));
                    }
                }

                if (isRotatable)
                {
                    Rot4 rotation = BuildingReflection.GetPlacingRot(placeDesignator);
                    string rotationInfo = ArchitectState.GetRotationAnnouncementForDef(def, rotation);
                    parts.Add(rotationInfo);
                }
            }

            if (shape == ShapeType.Manual)
            {
                if (classification.IsOrder || ShapeHelper.IsCellsDesignator(designator))
                {
                    parts.Add("RimWorldAccess.Building.Place.HintManualOrder".Translate());
                }
                else
                {
                    // Doors and their like auto-detect orientation and cannot be rotated by hand.
                    bool canRotate = false;
                    if (designator is Designator_Build buildDes)
                    {
                        if (buildDes.PlacingDef is ThingDef thingDef)
                        {
                            canRotate = thingDef.rotatable;
                        }
                    }

                    parts.Add(canRotate
                        ? (string)"RimWorldAccess.Building.Place.HintManualBuildRotatable".Translate()
                        : (string)"RimWorldAccess.Building.Place.HintManualBuildFixed".Translate());
                }
            }
            else
            {
                parts.Add("RimWorldAccess.Building.Place.HintShape".Translate());
            }

            return string.Join(". ", parts) + ".";
        }

        /// <summary>Sets the shape's first point.</summary>
        public static void SetFirstPoint(IntVec3 cell)
        {
            if (currentPhase != PlacementPhase.SettingFirstCorner)
            {
                Log.Warning($"[ShapePlacementState] SetFirstPoint called in wrong phase: {currentPhase}");
                return;
            }

            previewHelper.SetFirstCorner(cell, "[ShapePlacementState]");
            currentPhase = PlacementPhase.SettingSecondCorner;
            ClearCtrlAHistory();
        }

        /// <summary>Sets the shape's second point and moves to the previewing phase.</summary>
        public static void SetSecondPoint(IntVec3 cell)
        {
            if (currentPhase != PlacementPhase.SettingSecondCorner)
            {
                Log.Warning($"[ShapePlacementState] SetSecondPoint called in wrong phase: {currentPhase}");
                return;
            }

            if (!previewHelper.HasFirstCorner)
            {
                Log.Error("[ShapePlacementState] SetSecondPoint called without first point set");
                return;
            }

            List<IntVec3> cells = ShapeHelper.CalculateCells(
                previewHelper.CurrentShape, previewHelper.FirstCorner.Value, cell);
            string wipeInfo = GodModeWipeWarning.ShapeSuffix(activeDesignator, cells, Find.CurrentMap);
            previewHelper.SetSecondCorner(cell, "[ShapePlacementState]", extraInfo: wipeInfo);
            currentPhase = PlacementPhase.Previewing;
            ClearCtrlAHistory();
        }

        /// <summary>
        /// Sets both corners at once, going straight to Previewing and skipping the per-corner
        /// announcements so the caller can speak one summary.
        /// </summary>
        public static void SetBothPoints(IntVec3 first, IntVec3 second)
        {
            previewHelper.Reset();
            previewHelper.SetFirstCorner(first, "[ShapePlacementState]", silent: true);
            previewHelper.SetSecondCorner(second, "[ShapePlacementState]", silent: true);
            currentPhase = PlacementPhase.Previewing;
        }

        /// <summary>Snapshots the placement state for Ctrl+Shift+A. Call before applying a new Ctrl+A scope.</summary>
        public static void PushCtrlAHistory()
        {
            var snap = new CtrlASnapshot
            {
                HadFirstPoint = previewHelper.HasFirstCorner,
                HadSecondPoint = previewHelper.IsInPreviewMode,
                First = previewHelper.FirstCorner ?? IntVec3.Invalid,
                Second = previewHelper.SecondCorner ?? IntVec3.Invalid,
                Stage = ctrlAStage
            };
            ctrlAHistory.Push(snap);
        }

        /// <summary>Records the Ctrl+A stage. Call after a successful apply so the next press knows its step.</summary>
        public static void SetCtrlAStage(CtrlAStage stage)
        {
            ctrlAStage = stage;
        }

        /// <summary>Restores the most recent Ctrl+A snapshot; false when the history was empty.</summary>
        public static bool TryUndoCtrlA()
        {
            if (ctrlAHistory.Count == 0)
                return false;

            var snap = ctrlAHistory.Pop();
            previewHelper.Reset();

            if (snap.HadSecondPoint)
            {
                previewHelper.SetFirstCorner(snap.First, "[ShapePlacementState]", silent: true);
                previewHelper.SetSecondCorner(snap.Second, "[ShapePlacementState]", silent: true);
                currentPhase = PlacementPhase.Previewing;
            }
            else if (snap.HadFirstPoint)
            {
                previewHelper.SetFirstCorner(snap.First, "[ShapePlacementState]", silent: true);
                currentPhase = PlacementPhase.SettingSecondCorner;
            }
            else
            {
                currentPhase = PlacementPhase.SettingFirstCorner;
            }

            ctrlAStage = snap.Stage;
            return true;
        }

        /// <summary>Clears the Ctrl+A history and stage, since a manual edit breaks the chain.</summary>
        public static void ClearCtrlAHistory()
        {
            ctrlAHistory.Clear();
            ctrlAStage = CtrlAStage.None;
        }

        /// <summary>Grows the preview to the cursor, sounding on cell-count change. Returns the extent/count/failure readout for the caller to speak ahead of the cell, null when unchanged.</summary>
        public static string UpdatePreview(IntVec3 cursor)
        {
            if (currentPhase != PlacementPhase.SettingSecondCorner)
                return null;

            if (!previewHelper.HasFirstCorner)
                return null;

            string extent = previewHelper.UpdatePreview(cursor);
            return extent == null ? null : ComposeShapeReadout(cursor);
        }

        /// <summary>The mouse dragger's readout for the keyboard-grown shape, from the same CanDesignateCell gate (decompiled Verse/DesignationDragger.cs:253).</summary>
        private static string ComposeShapeReadout(IntVec3 cursor)
        {
            Designator designator = activeDesignator
                ?? (Find.DesignatorManager != null ? Find.DesignatorManager.SelectedDesignator : null);
            var (width, height) = ShapeHelper.GetDimensions(previewHelper.FirstCorner.Value, cursor);
            if (designator == null)
            {
                return "RimWorldAccess.Map.Drag.PaintExtent"
                    .Loc(width, height, previewHelper.PreviewCells.Count).ToString();
            }
            int accepted = 0;
            string failure = null;
            for (int i = 0; i < previewHelper.PreviewCells.Count; i++)
            {
                AcceptanceReport report = designator.CanDesignateCell(previewHelper.PreviewCells[i]);
                if (report.Accepted)
                    accepted++;
                else if (!report.Reason.NullOrEmpty())
                    failure = report.Reason;
            }
            string text = "RimWorldAccess.Map.Drag.PaintExtent".Loc(width, height, accepted).ToString();
            return failure == null ? text : text + ". " + failure;
        }

        /// <summary>
        /// Places designations for every cell of the preview, for any designator kind.
        /// With <paramref name="silent"/> the caller announces instead.
        /// </summary>
        public static PlacementResult PlaceDesignations(bool silent = false)
        {
            PlacementResult result = new PlacementResult();

            Map map = ValidatePrePlacement(result);
            if (map == null)
                return result;

            List<Thing> placedThisOperation = new List<Thing>();

            // A designator whose unit of work is a rectangle never passes the per-cell
            // CanDesignateCell gate below -- see RectDesignationRouter's remarks.
            if (RectDesignationRouter.IsRectDesignator(activeDesignator))
            {
                return PlaceRectDesignation(result, silent);
            }

            // Classified once and handed down, rather than re-derived by each placement path.
            DesignatorClassification classification = ShapeHelper.ClassifyDesignator(activeDesignator);

            if (classification.IsZone)
            {
                PlacementResult zoneResult = PlaceZoneDesignations(result, map, classification);
                if (zoneResult != null)
                    return zoneResult; // Early return for zone deletion confirmation
            }
            else
            {
                PlaceNonZoneDesignations(result, map, placedThisOperation, classification);
            }

            FinalizeAndAnnounce(result, placedThisOperation, silent);

            return result;
        }

        /// <summary>
        /// Applies a rect designator over one cell: the keyboard's equivalent of a mouse drag
        /// that starts and ends on the same cell. Only valid for a designator
        /// RectDesignationRouter claims.
        /// </summary>
        public static void PlaceSingleCellRect(IntVec3 cell)
        {
            if (activeDesignator == null)
                return;

            PlacementResult result = new PlacementResult();
            RectDesignationResult outcome = RectDesignationRouter.Designate(
                activeDesignator, CellRect.SingleCell(cell), cell, new List<IntVec3> { cell });

            if (!outcome.Handled)
                return;

            result.WasRectDesignation = true;
            result.SelectionAdded = outcome.SelectionAdded;
            result.DesignatorSpoke = outcome.SpokeForItself;
            result.WasSelectionDesignation = outcome.IsSelection;
            result.PlacedCells.Add(cell);
            result.PlacedCount = 1;

            AnnounceResult(result);
            // Designator_SelectSimilar.DesignateMultiCell ends in TryCloseArchitectMenu, closing
            // the tab out from under the live placement state; the hand-off below is what keeps
            // the player somewhere real.
            ExitAfterRectDesignation(result);
        }

        /// <summary>
        /// Leaves placement after a rect designation. A designator that selected things hands
        /// the player to gizmo navigation over the new selection, matching the mod's own
        /// architect-tab close; otherwise the tool stays selected and the selection clears so
        /// another rectangle can be drawn.
        /// </summary>
        public static void ExitAfterRectDesignation(PlacementResult result)
        {
            if (result == null)
                return;

            if (result.SelectionAdded > 0)
            {
                Find.DesignatorManager.Deselect();
                Reset();
                GizmoNavigationState.PawnJustSelected = true;
                GizmoNavigationState.Open();
            }
            else
            {
                ClearSelectionAndStay(silent: true);
            }
        }

        /// <summary>
        /// Validates the pre-conditions for placement, returning the map or null.
        /// </summary>
        private static Map ValidatePrePlacement(PlacementResult result)
        {
            if (activeDesignator == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Place.NoDesignatorActive".Loc(), SpeechPriority.High);
                return null;
            }

            if (previewHelper.PreviewCells.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Place.NoCellsSelected".Loc(), SpeechPriority.High);
                return null;
            }

            if (!GuardHelper.RequireMap(out Map map, SpeechPriority.High)) return null;
            return map;
        }

        /// <summary>Hands the preview's bounding rectangle to the designator's rect handler.</summary>
        private static PlacementResult PlaceRectDesignation(PlacementResult result, bool silent)
        {
            IReadOnlyList<IntVec3> cells = previewHelper.PreviewCells;

            int minX = 0, minZ = 0, maxX = 0, maxZ = 0;
            bool any = false;
            for (int i = 0; i < cells.Count; i++)
            {
                CellBounds.Accumulate(cells[i].x, cells[i].z, ref minX, ref minZ, ref maxX, ref maxZ, ref any);
            }

            CellRect rect = CellRect.FromLimits(new IntVec3(minX, 0, minZ), new IntVec3(maxX, 0, maxZ));

            // The strip-mine grid offset reads Dragger.SelectionStartCell, so the corner the
            // player set first must be reported, not the rect's minimum.
            IntVec3 firstCorner = previewHelper.FirstCorner ?? cells[0];

            RectDesignationResult outcome = RectDesignationRouter.Designate(activeDesignator, rect, firstCorner, cells);
            if (!outcome.Handled)
            {
                // Nothing was designated, so the caller's no-placement branch applies.
                return result;
            }

            result.WasRectDesignation = true;
            result.SelectionAdded = outcome.SelectionAdded;
            result.DesignatorSpoke = outcome.SpokeForItself;
            result.WasSelectionDesignation = outcome.IsSelection;
            result.PlacedCells.AddRange(cells);
            result.PlacedCount = cells.Count;

            FinalizeAndAnnounce(result, new List<Thing>(), silent);
            return result;
        }

        /// <summary>
        /// Places zone designations through DesignateMultiCell. Returns a result early only when
        /// a full-zone deletion needs confirmation.
        /// </summary>
        private static PlacementResult PlaceZoneDesignations(PlacementResult result, Map map, DesignatorClassification classification)
        {
            bool isDeleteDesignator = classification.IsDelete;

            List<IntVec3> validCells = new List<IntVec3>();
            foreach (IntVec3 cell in previewHelper.PreviewCells)
            {
                AcceptanceReport report = activeDesignator.CanDesignateCell(cell);
                if (report.Accepted)
                {
                    validCells.Add(cell);
                }
                else
                {
                    result.ObstacleCells.Add(cell);
                    result.ObstacleCount++;
                }
            }

            if (validCells.Count > 0)
            {
                try
                {
                    // The entry cursor position decides expand vs create, so the behavior
                    // matches what entry announced.
                    IntVec3 referenceCell = entryCursorPosition.IsValid ? entryCursorPosition : validCells[0];
                    ZoneSelectionResult selectionResult = ZoneSelectionHelper.SelectZoneAtCell(activeDesignator, referenceCell);
                    Zone targetZone = selectionResult.TargetZone;

                    // An expand keeps only cells adjacent to the zone or to already-valid cells,
                    // or the gizmo would create disconnected zones.
                    if (!isDeleteDesignator && targetZone != null && selectionResult.IsExpansion)
                    {
                        validCells = FilterCellsForExpansion(validCells, targetZone, map);
                        if (validCells.Count == 0)
                        {
                            return null;
                        }
                    }

                    if (isDeleteDesignator && targetZone != null)
                    {
                        if (ZoneUndoTracker.WouldDeleteEntireZone(targetZone, validCells))
                        {
                            result.NeedsFullDeletionConfirmation = true;
                            result.ZonePendingDeletion = targetZone;
                            result.PendingValidCells.AddRange(validCells);
                            result.ObstacleCells.Clear(); // Clear obstacles since we're not placing yet
                            result.ObstacleCount = 0;
                            return result;
                        }
                    }

                    ZoneUndoTracker.CaptureBeforeState(targetZone, map, isDeleteDesignator);

                    activeDesignator.DesignateMultiCell(validCells);

                    // The after-state detects splits.
                    ZoneUndoTracker.CaptureAfterState(map);

                    result.PlacedCells.AddRange(validCells);
                    result.PlacedCount = validCells.Count;
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[ShapePlacementState] Error placing zone: {ex.Message}");
                }
            }

            return null; // Continue with normal flow
        }

        /// <summary>
        /// Keeps only the cells forming a contiguous expansion of the target zone, flood-filled
        /// from those adjacent to it.
        /// </summary>
        private static List<IntVec3> FilterCellsForExpansion(List<IntVec3> candidateCells, Zone targetZone, Map map)
        {
            if (candidateCells.Count == 0 || targetZone == null)
                return candidateCells;

            HashSet<IntVec3> zoneCells = new HashSet<IntVec3>(targetZone.Cells);
            HashSet<IntVec3> candidateSet = new HashSet<IntVec3>(candidateCells);
            HashSet<IntVec3> validExpansionCells = new HashSet<IntVec3>();

            Queue<IntVec3> queue = new Queue<IntVec3>();
            foreach (IntVec3 cell in candidateCells)
            {
                foreach (IntVec3 dir in GenAdj.CardinalDirections)
                {
                    IntVec3 neighbor = cell + dir;
                    if (zoneCells.Contains(neighbor))
                    {
                        if (validExpansionCells.Add(cell))
                        {
                            queue.Enqueue(cell);
                        }
                        break;
                    }
                }
            }

            while (queue.Count > 0)
            {
                IntVec3 current = queue.Dequeue();
                foreach (IntVec3 dir in GenAdj.CardinalDirections)
                {
                    IntVec3 neighbor = current + dir;
                    if (candidateSet.Contains(neighbor) && validExpansionCells.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // Original order is preserved.
            List<IntVec3> result = new List<IntVec3>();
            foreach (IntVec3 cell in candidateCells)
            {
                if (validExpansionCells.Contains(cell))
                {
                    result.Add(cell);
                }
            }

            if (result.Count < candidateCells.Count)
            {
            }

            return result;
        }

        /// <summary>Places non-zone designations one cell at a time through DesignateSingleCell.</summary>
        private static void PlaceNonZoneDesignations(PlacementResult result, Map map, List<Thing> placedThisOperation, DesignatorClassification classification)
        {
            bool isBuildDesignator = classification.IsBuild;
            bool isAreaDesignator = classification.IsArea;
            bool isBuiltInAreaDesignator = classification.IsBuiltInArea;
            bool isOrderDesignator = classification.IsOrder;

            if (isAreaDesignator && Designator_AreaAllowed.selectedArea != null)
            {
                bool isExpanding = activeDesignator is Designator_AreaAllowedExpand;
                AreaUndoTracker.CaptureBeforeState(Designator_AreaAllowed.selectedArea, isExpanding);
            }
            else if (isBuiltInAreaDesignator)
            {
                // An ignore-roof stroke writes to two built-in Areas, so undo snapshots both.
                List<Area> builtInAreas = ShapeHelper.GetBuiltInAreasForDesignator(activeDesignator, map);
                if (builtInAreas.Count > 0)
                {
                    bool isExpanding = ShapeHelper.IsBuiltInAreaExpanding(activeDesignator);
                    AreaUndoTracker.CaptureBeforeState(builtInAreas, isExpanding);
                }
            }

            BuildableDef buildableDef = isBuildDesignator ? GetBuildableDefFromDesignator(activeDesignator) : null;

            bool checkMeditationProtection = false;
            ThingDef placingThingDef = null;
            Rot4 placingRotation = Rot4.North;

            if (isBuildDesignator || ShapeHelper.IsPlaceDesignator(activeDesignator))
            {
                var (def, rot) = MeditationProtectionHelper.GetPlacementInfo(activeDesignator);
                if (def != null)
                {
                    placingThingDef = def;
                    placingRotation = rot;
                    checkMeditationProtection =
                        MeditationProtectionHelper.IsArtificialBuilding(def, Faction.OfPlayer);
                }
            }

            // Diffed after placement to find exactly which designations were created.
            if (isOrderDesignator)
            {
                OrderUndoTracker.CaptureBeforeState(map);
            }

            foreach (IntVec3 cell in previewHelper.PreviewCells)
            {
                AcceptanceReport report = activeDesignator.CanDesignateCell(cell);

                if (report.Accepted)
                {
                    if (checkMeditationProtection)
                    {
                        var protection = MeditationProtectionHelper.CheckProtection(
                            map, placingThingDef, Faction.OfPlayer, cell, placingRotation);

                        if (protection.IsProtected)
                        {
                            result.ProtectedCells.Add(cell);
                            result.ProtectedCount++;
                            foreach (string label in protection.AffectedThingLabels)
                            {
                                result.ProtectedByLabels.Add(label);
                            }
                            continue;
                        }
                    }

                    try
                    {
                        // God mode and zero-work defs skip blueprints — DesignateSingleCell
                        // spawns the finished thing or sets terrain directly — so success means a
                        // new blueprint/frame, a new thing of the def, or a terrain change.
                        if (isBuildDesignator)
                        {
                            List<Thing> thingsBefore = new List<Thing>(cell.GetThingList(map));
                            TerrainDef terrainBefore = map.terrainGrid.TerrainAt(cell);
                            TerrainDef foundationBefore = map.terrainGrid.FoundationAt(cell);
                            activeDesignator.DesignateSingleCell(cell);
                            List<Thing> thingsAfter = cell.GetThingList(map);
                            Thing placedThing = null;
                            foreach (Thing thing in thingsAfter)
                            {
                                if (!thingsBefore.Contains(thing) &&
                                    (thing.def.IsBlueprint || thing.def.IsFrame || thing.def == buildableDef))
                                {
                                    placedThing = thing;
                                    break;
                                }
                            }

                            if (placedThing != null)
                            {
                                placedThisOperation.Add(placedThing);
                                result.PlacedCells.Add(cell);
                                result.PlacedCount++;
                            }
                            else if (map.terrainGrid.TerrainAt(cell) != terrainBefore ||
                                map.terrainGrid.FoundationAt(cell) != foundationBefore)
                            {
                                result.PlacedCells.Add(cell);
                                result.PlacedCount++;
                            }
                        }
                        else
                        {
                            activeDesignator.DesignateSingleCell(cell);
                            result.PlacedCells.Add(cell);
                            result.PlacedCount++;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error($"[ShapePlacementState] Error placing at {cell}: {ex.Message}");
                        result.ObstacleCells.Add(cell);
                        result.ObstacleCount++;
                    }
                }
                else
                {
                    result.ObstacleCells.Add(cell);
                    result.ObstacleCount++;
                }
            }

            if (isOrderDesignator)
            {
                OrderUndoTracker.CaptureAfterState(map);
            }

            // God-mode and zero-work placements charge nothing and leave no blueprints, so any
            // cost reported for them would be false.
            bool instantBuild = result.PlacedCount > 0;
            if (instantBuild)
            {
                foreach (Thing thing in placedThisOperation)
                {
                    if (thing.def.IsBlueprint || thing.def.IsFrame)
                    {
                        instantBuild = false;
                        break;
                    }
                }
            }
            if (isBuildDesignator && !instantBuild)
            {
                PopulateResourceCosts(result, buildableDef);
            }

            if (isAreaDesignator || isBuiltInAreaDesignator)
            {
                AreaUndoTracker.CaptureAfterState();
            }
        }

        /// <summary>Finalizes the designator and announces the result unless <paramref name="silent"/>.</summary>
        private static void FinalizeAndAnnounce(PlacementResult result, List<Thing> placedThisOperation, bool silent)
        {
            // A rect designation is excluded: the handled designator already finalized itself,
            // and a second Finalize would deselect the tool behind its back.
            if (result.PlacedCount > 0 && !result.WasRectDesignation)
            {
                try
                {
                    activeDesignator.Finalize(true);
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[ShapePlacementState] Error finalizing designator: {ex.Message}");
                }
            }

            result.PlacedBlueprints = placedThisOperation;

            if (!silent)
            {
                // The sanitized label drops a trailing "...", which would pluralize as "wall...s".
                string designatorName = ArchitectHelper.GetSanitizedLabel(activeDesignator);
                string announcement = BuildPlacementAnnouncement(result, designatorName, activeDesignator);
                if (!string.IsNullOrEmpty(announcement))
                    TolkHelper.SpeakData(announcement);
            }

        }

        /// <summary>
        /// Announces a result outside the ordinary non-silent path, as god-mode terrain
        /// placements need — they leave no things for viewing mode to track.
        /// </summary>
        public static void AnnounceResult(PlacementResult result)
        {
            if (activeDesignator == null || result == null)
                return;

            string designatorName = ArchitectHelper.GetSanitizedLabel(activeDesignator);
            string announcement = BuildPlacementAnnouncement(result, designatorName, activeDesignator);
            if (!string.IsNullOrEmpty(announcement))
                TolkHelper.SpeakData(announcement);
        }

        /// <summary>Performs the zone deletion once the player has confirmed it.</summary>
        public static PlacementResult ExecuteConfirmedZoneDeletion(PlacementResult pendingResult, bool silent = false)
        {
            if (pendingResult == null || !pendingResult.NeedsFullDeletionConfirmation)
            {
                Log.Warning("[ShapePlacementState] ExecuteConfirmedZoneDeletion called without pending confirmation");
                return pendingResult;
            }

            Zone targetZone = pendingResult.ZonePendingDeletion;
            List<IntVec3> validCells = pendingResult.PendingValidCells;
            Map map = Find.CurrentMap;

            if (targetZone == null || map == null)
            {
                Log.Error("[ShapePlacementState] ExecuteConfirmedZoneDeletion: missing zone or map");
                return pendingResult;
            }

            try
            {
                // Irreversible, so nothing is tracked for undo.
                string zoneName = targetZone.label;
                targetZone.Delete();

                pendingResult.PlacedCells.AddRange(validCells);
                pendingResult.PlacedCount = validCells.Count;
                pendingResult.NeedsFullDeletionConfirmation = false;

                if (!silent)
                {
                    TolkHelper.Speak("RimWorldAccess.Building.Place.ZoneDeleted".Loc(zoneName), SpeechPriority.Normal);
                }

            }
            catch (System.Exception ex)
            {
                Log.Error($"[ShapePlacementState] Error executing zone deletion: {ex.Message}");
            }

            return pendingResult;
        }

        /// <summary>Cancels the operation and exits shape mode.</summary>
        public static void Cancel()
        {
            PlacementPhase previousPhase = currentPhase;

            Reset();

            switch (previousPhase)
            {
                case PlacementPhase.SettingFirstCorner:
                    TolkHelper.Speak("RimWorldAccess.Building.Place.CancelFromFirstCorner".Loc());
                    break;
                case PlacementPhase.SettingSecondCorner:
                    TolkHelper.Speak("RimWorldAccess.Building.Place.CancelFromSecondCorner".Loc());
                    break;
                case PlacementPhase.Previewing:
                    TolkHelper.Speak("RimWorldAccess.Building.Place.CancelFromPreview".Loc());
                    break;
            }

        }

        /// <summary>
        /// Clears the selection but stays in shape mode with the same shape — Escape's restart
        /// behavior. False when there was nothing to clear.
        /// </summary>
        public static bool ClearSelectionAndStay(bool silent = false)
        {
            PlacementPhase previousPhase = currentPhase;

            if (previousPhase == PlacementPhase.SettingFirstCorner && !previewHelper.HasFirstCorner)
            {
                return false;
            }

            ShapeType savedShape = currentShape;

            if (!silent)
            {
                if (previousPhase == PlacementPhase.Previewing)
                {
                    TolkHelper.Speak("RimWorldAccess.Building.Place.SelectionClearedFromPreview".Loc());
                }
                else if (previousPhase == PlacementPhase.SettingSecondCorner)
                {
                    TolkHelper.Speak("RimWorldAccess.Building.Place.SelectionCancelledToFirstPoint".Loc());
                }
            }

            // The shape survives the reset.
            previewHelper.Reset();
            currentPhase = PlacementPhase.SettingFirstCorner;
            ClearCtrlAHistory();

            return true;
        }

        /// <summary>Removes the most recent point, stepping back a phase; false when there is none.</summary>
        public static bool RemoveLastPoint()
        {
            if (currentPhase == PlacementPhase.Previewing && previewHelper.IsInPreviewMode)
            {
                IntVec3 firstPointPos = previewHelper.FirstCorner.Value;
                previewHelper.Reset();
                previewHelper.SetFirstCorner(firstPointPos, "[ShapePlacementState]", silent: true);
                currentPhase = PlacementPhase.SettingSecondCorner;
                ClearCtrlAHistory();
                TolkHelper.Speak("RimWorldAccess.Building.Place.SecondPointRemoved".Loc());
                return true;
            }

            if (currentPhase == PlacementPhase.SettingSecondCorner && previewHelper.HasFirstCorner)
            {
                previewHelper.Reset();
                currentPhase = PlacementPhase.SettingFirstCorner;
                ClearCtrlAHistory();
                TolkHelper.Speak("RimWorldAccess.Building.Place.FirstPointRemoved".Loc());
                return true;
            }

            TolkHelper.Speak("RimWorldAccess.Building.Place.NoPointsToRemove".Loc());
            return false;
        }

        /// <summary>Resets every state variable. Idempotent.</summary>
        public static void Reset()
        {
            if (currentPhase == PlacementPhase.Inactive)
            {
                return;
            }

            // Phase goes Inactive before any other cleanup, or DesignatorManagerDeselectPatch
            // loops forever.
            currentPhase = PlacementPhase.Inactive;
            currentShape = ShapeType.Manual;
            pendingEntryAnnouncement = null;
            SuppressNextEntryAnnouncement = false;
            previewHelper.FullReset();
            activeDesignator = null;
            hasViewingModeOnStack = false;
            entryCursorPosition = IntVec3.Invalid;
            ClearCtrlAHistory();
            PlacementHelpSpeech.Reset();

        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Syncs the shape selection to the game's SelectedStyle, which is what makes RimWorld's
        /// "Remember Draw Styles" setting work; the setter updates previouslySelected itself.
        /// </summary>
        private static void SyncShapeToGameStyle(Designator designator, ShapeType shape)
        {
            if (designator == null)
                return;

            var designatorManager = Find.DesignatorManager;
            if (designatorManager == null)
                return;

            // Null for Manual mode.
            DrawStyleDef styleDef = ShapeHelper.GetDrawStyleDef(designator, shape);

            if (styleDef != null)
            {
                designatorManager.SelectedStyle = styleDef;
            }
        }

        /// <summary>The zone currently selected in Find.Selector, or null.</summary>
        private static Zone GetSelectedZone()
        {
            var selectedObjects = Find.Selector?.SelectedObjects;
            if (selectedObjects == null)
                return null;

            foreach (object obj in selectedObjects)
            {
                if (obj is Zone zone)
                    return zone;
            }

            return null;
        }

        /// <summary>The designator's BuildableDef, for cost calculation.</summary>
        private static BuildableDef GetBuildableDefFromDesignator(Designator designator)
        {
            if (designator is Designator_Build buildDesignator)
            {
                return buildDesignator.PlacingDef;
            }

            if (designator is Designator_Place placeDesignator)
            {
                return placeDesignator.PlacingDef;
            }

            return null;
        }

        /// <summary>
        /// Fills <see cref="PlacementResult.ResourceCosts"/>, and the legacy single-value fields,
        /// with what the placed blueprints consume. A stuff-based building has one
        /// player-selected material via CostStuffCount; a fixed-cost buildable enumerates every
        /// CostList entry, so multi-resource costs are all announced.
        /// </summary>
        private static void PopulateResourceCosts(PlacementResult result, BuildableDef buildable)
        {
            if (buildable == null || result.PlacedCount == 0)
                return;

            if (buildable is ThingDef thingDef && thingDef.MadeFromStuff)
            {
                string stuffName = ArchitectState.SelectedMaterial != null
                    ? ArchitectState.SelectedMaterial.label
                    : (string)"RimWorldAccess.Common.Material".Translate();

                result.TotalResourceCost = result.PlacedCount * buildable.CostStuffCount;
                result.ResourceName = stuffName;
                result.ResourceCosts.Add((result.TotalResourceCost, stuffName));
                return;
            }

            if (buildable.CostList == null)
                return;

            foreach (ThingDefCountClass cost in buildable.CostList)
            {
                if (cost?.thingDef == null || cost.count <= 0)
                    continue;

                result.ResourceCosts.Add((result.PlacedCount * cost.count, cost.thingDef.label));
            }

            // The legacy single-value fields keep the primary resource.
            if (result.ResourceCosts.Count > 0)
            {
                result.TotalResourceCost = result.ResourceCosts[0].Count;
                result.ResourceName = result.ResourceCosts[0].Name;
            }
        }

        /// <summary>The announcement for a placement result.</summary>
        private static string BuildPlacementAnnouncement(PlacementResult result, string designatorName, Designator designator)
        {
            if (result.WasRectDesignation)
            {
                // The designator's own success message already carries the count and the
                // Messages patch speaks it; adding ours would double-announce.
                if (result.DesignatorSpoke)
                    return null;
                if (result.SelectionAdded > 0)
                    return "RimWorldAccess.Compat.AllowTool.SelectionAdded".Translate(
                        result.SelectionAdded, Find.Selector.NumSelected);
                // A zero count is a miss, not an application, and the def carries no failure
                // message, so this is its only voice.
                if (result.WasSelectionDesignation)
                    return "RimWorldAccess.Compat.AllowTool.NothingSelected".Translate();
                return "RimWorldAccess.Compat.AllowTool.AreaApplied".Translate(
                    designatorName, result.PlacedCells.Count);
            }

            List<string> parts = new List<string>();
            DesignatorClassification classification = ShapeHelper.ClassifyDesignator(designator);
            bool isBuild = classification.IsBuild;
            bool isOrder = classification.IsOrder;

            if (result.PlacedCount > 0)
            {
                if (isBuild)
                {
                    string name = result.PlacedCount > 1
                        ? Find.ActiveLanguageWorker.Pluralize(designatorName, result.PlacedCount)
                        : designatorName;

                    string costInfo = BuildCostInfo(result);
                    parts.Add("RimWorldAccess.Building.Place.PlacedBuild".Translate(result.PlacedCount, name, costInfo));
                }
                else
                {
                    // Matches RimWorld's own "Designated X for [action]" terminology.
                    string action = PlacementDescriber.DescribeAction(designator, designatorName);
                    parts.Add("RimWorldAccess.Building.Place.DesignatedFor".Translate(result.PlacedCount, action));
                }
            }
            else
            {
                parts.Add(isBuild
                    ? (string)"RimWorldAccess.Building.Place.NoBlueprintsPlaced".Translate()
                    : (string)"RimWorldAccess.Building.Place.NoDesignationsPlaced".Translate());
            }

            bool isDelete = classification.IsDelete;
            if (!isOrder && !isDelete && result.ObstacleCount > 0)
            {
                parts.Add("RimWorldAccess.Building.Place.ObstaclesFound".Translate(result.ObstacleCount));
            }

            if (result.ProtectedCount > 0)
            {
                string protectionSummary = MeditationProtectionHelper.FormatShapeSummary(
                    result.ProtectedCount, result.ProtectedByLabels);
                parts.Add(protectionSummary);
                parts.Add(MeditationProtectionHelper.DisableHint());
            }

            return string.Join(". ", parts);
        }

        /// <summary>
        /// The "(N Resource)" suffix from the result's full breakdown; several resources join
        /// into one list so every one is spoken.
        /// </summary>
        private static string BuildCostInfo(PlacementResult result)
        {
            if (result.ResourceCosts.Count == 0)
                return string.Empty;

            if (result.ResourceCosts.Count == 1)
            {
                var (count, name) = result.ResourceCosts[0];
                return (string)"RimWorldAccess.Building.Place.PlacedCostSuffix".Translate(count, name);
            }

            List<string> items = new List<string>();
            foreach (var (count, name) in result.ResourceCosts)
            {
                items.Add("RimWorldAccess.Building.Place.PlacedCostItem".Translate(count, name));
            }

            return (string)"RimWorldAccess.Building.Place.PlacedCostSuffixMulti".Translate(items.ToCommaList());
        }

        /// <summary>Whether the current phase lets cursor movement update the preview.</summary>
        public static bool ShouldUpdatePreviewOnMove()
        {
            return currentPhase == PlacementPhase.SettingSecondCorner && previewHelper.HasFirstCorner;
        }

        /// <summary>The preview's width and height, or (0, 0) when there is none.</summary>
        public static (int width, int height) GetCurrentDimensions()
        {
            if (!previewHelper.HasFirstCorner || !MapNavigationState.IsInitialized)
                return (0, 0);

            IntVec3 target = previewHelper.SecondCorner ?? MapNavigationState.CurrentCursorPosition;
            return ShapeHelper.GetDimensions(previewHelper.FirstCorner.Value, target);
        }

        #endregion
    }
}
