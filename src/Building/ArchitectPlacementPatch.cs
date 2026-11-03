using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Placement handler bodies for architect/designator cell placement. Keyboard dispatch
    /// lives in <see cref="RimWorldAccess.Shell.PlacementScope"/>, which calls the thin public
    /// routers below; each router recomputes the shared designator/mode locals itself.
    /// </summary>
    public static class ArchitectPlacementInputPatch
    {
        private static float lastSpaceTime = 0f;
        private const float SpaceCooldown = 0.2f;

        // Manual-mode Enter places the building itself when nothing has been placed yet under
        // the current selection; these track "yet". Reconciled per frame, so any selection
        // change (including deselect) starts a fresh count.
        private static Designator manualSessionDesignator;
        private static int manualSessionPlacements;

        internal static bool InArchitectMode()
        {
            return ArchitectState.IsInPlacementMode;
        }

        internal static bool HasActiveDesignator()
        {
            return Find.DesignatorManager != null && Find.DesignatorManager.SelectedDesignator != null;
        }

        /// <summary>
        /// True while the targeter is picking a transport-pod landing spot. Distinguished from
        /// weapon/ability targeting by comparing <c>Targeter</c>'s private mouseAttachment texture
        /// against <see cref="CompLaunchable.TargeterMouseAttachment"/>.
        /// </summary>
        internal static bool InTransportPodTargeting()
        {
            if (Find.Targeter == null || !Find.Targeter.IsTargeting)
                return false;

            var mouseAttachmentField = AccessTools.Field(typeof(Targeter), "mouseAttachment");
            if (mouseAttachmentField == null)
                return false;

            Texture2D mouseAttachment = mouseAttachmentField.GetValue(Find.Targeter) as Texture2D;
            return mouseAttachment != null
                && CompLaunchable.TargeterMouseAttachment != null
                && mouseAttachment == CompLaunchable.TargeterMouseAttachment;
        }

        /// <summary>
        /// The map modes where the shell owns input but the player is still choosing a
        /// CELL, so ambient map navigation — the scanner's browse keys, its Z search, and
        /// Ctrl+G — must keep working alongside the cell cursor. Transport-pod/shuttle landing
        /// belongs here too: it has no designator and no architect mode, so omitting
        /// <see cref="InTransportPodTargeting"/> lets the modal backstop swallow every scanner
        /// key while picking a landing spot.
        /// </summary>
        internal static bool InCellSelectionMode()
        {
            return InArchitectMode()
                || ViewingModeState.IsActive
                || ShapePlacementState.IsActive
                || InTransportPodTargeting()
                || (Find.CurrentMap != null && HasActiveDesignator());
        }

        internal static Designator GetActiveDesignator()
        {
            // A live shape-placement session is the authority on what is being placed. Selecting a
            // new building from a gizmo while the placement review UI is still up leaves the prior
            // architect session pointing at the old designator; without this, Space would place
            // that stale one instead of the just-selected building.
            if (ShapePlacementState.IsActive && ShapePlacementState.ActiveDesignator != null)
            {
                return ShapePlacementState.ActiveDesignator;
            }
            return InArchitectMode() ? ArchitectState.SelectedDesignator : Find.DesignatorManager?.SelectedDesignator;
        }

        /// <summary>
        /// Side-effect-free mirror of <c>HandleTabKey</c>'s <c>canUseShapes</c> local, used as the
        /// shell claim's when: gate so Tab/Shift+Tab fall through to vanilla colonist cycling for
        /// a designator with no shapes to offer.
        /// </summary>
        internal static bool CanUseShapes()
        {
            Designator activeDesignator = GetActiveDesignator();
            if (activeDesignator == null)
                return false;
            return ShapeHelper.GetAvailableShapes(activeDesignator).Count >= 1;
        }

        /// <summary>
        /// Side-effect-free mirror of <c>HandleEnterKey</c>'s branch conditions, used as the shell
        /// claim's when: gate so Enter falls through for a gizmo-selected orders/cells designator.
        /// </summary>
        internal static bool CanHandleEnter()
        {
            Designator activeDesignator = GetActiveDesignator();
            if (activeDesignator == null)
                return false;
            if (ShapePlacementState.IsActive)
                return true;

            bool inArchitectMode = InArchitectMode();
            bool isPlaceDesignator = ShapeHelper.IsPlaceDesignator(activeDesignator);
            DesignatorClassification classification = ShapeHelper.ClassifyDesignator(activeDesignator);
            bool isZoneDesignator = classification.IsZone;
            bool isOrderDesignator = classification.IsOrder;
            bool isCellsDesignator = ShapeHelper.IsCellsDesignator(activeDesignator);
            return isPlaceDesignator
                || isZoneDesignator
                || (inArchitectMode && (isOrderDesignator || isCellsDesignator));
        }

        // Per-frame housekeeping, called from PlacementScopeMirror.Reconcile().

        /// <summary>Cancels placement state when the map has gone away.</summary>
        internal static void CleanupIfMapMissing()
        {
            if (Find.CurrentMap != null)
                return;
            if (InArchitectMode())
                ArchitectState.Cancel();
            else if (HasActiveDesignator())
                Find.DesignatorManager.Deselect();
        }

        /// <summary>Resets placement state left behind after the designator went away.</summary>
        internal static void CleanupStaleState()
        {
            if (Find.CurrentMap == null)
                return;
            if (ShapePlacementState.CurrentPhase != PlacementPhase.Inactive ||
                ArchitectState.CurrentMode == ArchitectMode.PlacementMode)
            {
                if (Find.DesignatorManager?.SelectedDesignator == null)
                {
                    ShapePlacementState.Reset();
                    ArchitectState.Reset();
                }
            }
        }

        /// <summary>Restarts the manual-placement count whenever the active designator changes.</summary>
        internal static void ReconcileManualSession()
        {
            Designator current = Find.CurrentMap == null ? null : GetActiveDesignator();
            if (!ReferenceEquals(current, manualSessionDesignator))
            {
                manualSessionDesignator = current;
                manualSessionPlacements = 0;
            }
        }

        // Key routers — called directly by PlacementScope's claims.

        public static void HandleTab(bool shiftHeld)
        {
            Designator activeDesignator = GetActiveDesignator();
            if (activeDesignator == null)
                return;
            bool inArchitectMode = InArchitectMode();
            var availableShapes = ShapeHelper.GetAvailableShapes(activeDesignator);
            bool supportsShapes = availableShapes.Count >= 1;
            HandleTabKey(activeDesignator, shiftHeld, supportsShapes, inArchitectMode, availableShapes);
        }

        /// <summary>
        /// The ']' menu for the tool currently being placed, so the architect tree's
        /// designator-options menu stays reachable once the tool is in hand — the keyboard's only
        /// route to Allow Tool's "select strip mine tool" entry, which lives on Mine's menu.
        /// </summary>
        public static void HandleDesignatorOptions()
        {
            DesignatorOptionsOpener.Open(GetActiveDesignator());
        }

        public static void HandleCtrlA()
        {
            HandleCtrlAKey();
        }

        public static void HandleCtrlShiftA()
        {
            HandleCtrlShiftAKey();
        }

        public static void HandleShiftSpace()
        {
            HandleShiftSpaceKey();
        }

        public static void HandleRotate(RotationDirection direction)
        {
            Designator activeDesignator = GetActiveDesignator();
            if (activeDesignator == null)
                return;
            HandleRotateKey(activeDesignator, InArchitectMode(), direction);
        }

        public static void HandleSpace()
        {
            Designator activeDesignator = GetActiveDesignator();
            if (activeDesignator == null)
                return;

            // Anti-rapid-toggle cooldown. It must stay inside the handler rather than move to the
            // claim's when: gate — a cooldown-blocked Space still consumes the key.
            if (Time.time - lastSpaceTime < SpaceCooldown)
                return;
            lastSpaceTime = Time.time;

            bool inArchitectMode = InArchitectMode();
            bool isPlaceDesignator = ShapeHelper.IsPlaceDesignator(activeDesignator);
            DesignatorClassification classification = ShapeHelper.ClassifyDesignator(activeDesignator);
            bool isZoneDesignator = classification.IsZone;
            bool isOrderDesignator = classification.IsOrder;
            bool isCellsDesignator = ShapeHelper.IsCellsDesignator(activeDesignator);
            IntVec3 currentPosition = MapNavigationState.CurrentCursorPosition;
            HandleSpaceKey(activeDesignator, currentPosition, inArchitectMode, isPlaceDesignator, isZoneDesignator, isOrderDesignator, isCellsDesignator);
        }

        public static void HandleEnter()
        {
            Designator activeDesignator = GetActiveDesignator();
            if (activeDesignator == null)
                return;
            bool inArchitectMode = InArchitectMode();
            bool isPlaceDesignator = ShapeHelper.IsPlaceDesignator(activeDesignator);
            DesignatorClassification classification = ShapeHelper.ClassifyDesignator(activeDesignator);
            bool isZoneDesignator = classification.IsZone;
            bool isOrderDesignator = classification.IsOrder;
            bool isCellsDesignator = ShapeHelper.IsCellsDesignator(activeDesignator);
            HandleEnterKey(activeDesignator, inArchitectMode, isPlaceDesignator, isZoneDesignator, isOrderDesignator, isCellsDesignator, classification.IsDelete);
        }

        public static void HandleEscape()
        {
            HandleEscapeKey(InArchitectMode());
        }

        /// <summary>
        /// Transport-pod-landing confirm (Space/Enter/KeypadEnter — all three take the same
        /// branch). The shell's exact-modifier chord matching supplies the no-Shift guard.
        /// </summary>
        public static void HandleTransportPodConfirm()
        {
            HandleTargetingModeInput(KeyCode.Space, shiftHeld: false);
        }

        public static void HandleTransportPodCancel()
        {
            HandleTargetingModeInput(KeyCode.Escape, shiftHeld: false);
        }

        /// <summary>
        /// Tab opens the shape selection menu, Shift+Tab switches to manual mode, for designators
        /// selected via the architect menu or via a gizmo. Returns true if the key was handled.
        /// </summary>
        private static bool HandleTabKey(Designator activeDesignator, bool shiftHeld, bool supportsShapes, bool inArchitectMode, List<ShapeType> availableShapes)
        {
            bool canUseShapes = supportsShapes && activeDesignator != null;

            if (!shiftHeld)
            {
                if (canUseShapes)
                {
                    // Changing shape mid-placement is blocked; Escape clears the selection first.
                    if (ShapePlacementState.IsPlacementInProgress)
                    {
                        TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.CannotChangeShapeWhilePlacing".Loc());
                        return true;
                    }

                    if (availableShapes.Count == 1 && availableShapes[0] == ShapeType.Manual)
                    {
                        TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.NoShapesAvailable".Loc());
                        return true;
                    }

                    if (ShapePlacementState.IsActive)
                    {
                        ShapePlacementState.Reset();
                    }

                    if (availableShapes.Count > 1)
                    {
                        ShapeSelectionMenuState.Open(activeDesignator);
                    }
                    else
                    {
                        ShapePlacementState.Enter(activeDesignator, availableShapes[0]);
                    }
                    return true;
                }
            }
            else if (canUseShapes)
            {
                if (ShapePlacementState.IsPlacementInProgress)
                {
                    TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.CannotChangeShapeWhilePlacing".Loc());
                    return true;
                }

                if (ShapePlacementState.IsActive && ShapePlacementState.CurrentShape == ShapeType.Manual)
                {
                    TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.AlreadyInManualMode".Loc());
                    return true;
                }

                if (ShapePlacementState.IsActive)
                {
                    ShapePlacementState.Reset(); // Reset, not Cancel: Cancel would announce.
                }
                ShapePlacementState.Enter(activeDesignator, ShapeType.Manual);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Rotates the building being placed, announcing when the def is not rotatable (doors and
        /// the like auto-detect their orientation). Returns true if the key was handled.
        /// </summary>
        private static bool HandleRotateKey(Designator activeDesignator, bool inArchitectMode, RotationDirection direction)
        {
            bool canRotate = true;
            string buildingLabel = null;

            if (activeDesignator is Designator_Build buildDesignator)
            {
                if (buildDesignator.PlacingDef is ThingDef thingDef)
                {
                    canRotate = thingDef.rotatable;
                    buildingLabel = thingDef.label;
                }
            }
            else if (activeDesignator is Designator_Place designatorPlace)
            {
                if (designatorPlace.PlacingDef is ThingDef thingDef)
                {
                    canRotate = thingDef.rotatable;
                    buildingLabel = thingDef.label;
                }
            }

            if (!canRotate)
            {
                string name = buildingLabel ?? activeDesignator.Label
                    ?? "RimWorldAccess.Building.ArchitectPlace.RotateFallbackSubject".Translate();
                if (!string.IsNullOrEmpty(name))
                {
                    name = char.ToUpper(name[0]) + name.Substring(1);
                }
                TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.CannotBeRotated".Loc(name));
                return true;
            }

            if (inArchitectMode)
            {
                ArchitectState.RotateBuilding(direction);
            }
            else if (BuildingReflection.IsGravshipDesignator(activeDesignator))
            {
                var marker = BuildingReflection.GetGravshipMarker(activeDesignator);
                if (marker != null)
                {
                    Rot4 currentRot = marker.GravshipRotation;
                    currentRot.Rotate(direction);
                    marker.GravshipRotation = currentRot;

                    string dirName = currentRot == Rot4.North ? "RimWorldAccess.Map.Direction.North".Translate().ToString() :
                                     currentRot == Rot4.East ? "RimWorldAccess.Map.Direction.East".Translate().ToString() :
                                     currentRot == Rot4.South ? "RimWorldAccess.Map.Direction.South".Translate().ToString() :
                                     "RimWorldAccess.Map.Direction.West".Translate().ToString();
                    TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.GravshipFacing".Loc(dirName));
                }
            }
            else if (activeDesignator is Designator_Place designatorPlace)
            {
                if (BuildingReflection.HasPlacingRotField)
                {
                    Rot4 currentRot = BuildingReflection.GetPlacingRot(designatorPlace);
                    currentRot.Rotate(direction);
                    BuildingReflection.SetPlacingRot(designatorPlace, currentRot);

                    string announcement = GetDesignatorRotationAnnouncement(designatorPlace, currentRot);
                    TolkHelper.SpeakData(announcement);
                }
            }
            return true;
        }

        /// <summary>
        /// Ctrl+A steps outward through selection scopes: enclosure (room or blueprint flood),
        /// then entire map, jumping straight to entire map when no enclosure is detected. Only
        /// rectangle/oval shapes qualify — a line cannot meaningfully fill a room. Returns true
        /// if the key was handled.
        /// </summary>
        private static bool HandleCtrlAKey()
        {
            if (!ShapePlacementState.IsActive)
                return false;

            ShapeType shape = ShapePlacementState.CurrentShape;
            if (shape == ShapeType.Manual)
            {
                TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.CtrlA.RequiresShape".Loc());
                return true;
            }
            if (shape == ShapeType.Line || shape == ShapeType.AngledLine)
            {
                TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.CtrlA.NotAvailableForLines".Loc());
                return true;
            }

            Map map = Find.CurrentMap;
            if (map == null)
                return false;

            CtrlAStage currentStage = ShapePlacementState.CurrentCtrlAStage;

            if (currentStage == CtrlAStage.EntireMap)
            {
                TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.CtrlA.EntireMapAlreadySelected".Loc());
                return true;
            }

            if (currentStage == CtrlAStage.Enclosure)
            {
                ApplyEntireMapScope(map, shape);
                return true;
            }

            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            bool cursorValid = cursor.IsValid && cursor.InBounds(map);

            IntVec3 cornerA;
            IntVec3 cornerB;
            string scopeLabel;

            // RimWorld's Room system already identifies enclosures made of finished walls.
            Room room = cursorValid ? cursor.GetRoom(map) : null;
            bool useRoom = room != null
                && !room.PsychologicallyOutdoors
                && !room.TouchesMapEdge
                && room.CellCount > 0;

            if (useRoom)
            {
                CellRect rect = room.ExtentsClose;
                cornerA = new IntVec3(rect.minX, 0, rect.minZ);
                cornerB = new IntVec3(rect.maxX, 0, rect.maxZ);
                scopeLabel = (string)"RimWorldAccess.Building.ArchitectPlace.CtrlA.ScopeRoom".Translate(rect.Width.ToString(), rect.Height.ToString());
            }
            else if (cursorValid && TryBlueprintEnclosureBounds(cursor, map, out cornerA, out cornerB, out scopeLabel))
            {
                // Corners come from the out parameters.
            }
            else
            {
                ApplyEntireMapScope(map, shape, isNoEnclosure: true);
                return true;
            }

            ShapePlacementState.PushCtrlAHistory();
            ShapePlacementState.SetBothPoints(cornerA, cornerB);
            ShapePlacementState.SetCtrlAStage(CtrlAStage.Enclosure);

            int cellCount = ShapePlacementState.PreviewCells?.Count ?? 0;
            string shapeName = ShapeHelper.GetShapeName(shape);
            TolkHelper.SpeakData((string)"RimWorldAccess.Building.ArchitectPlace.CtrlA.SelectedScope".Translate(scopeLabel, shapeName, cellCount.ToString()));
            return true;
        }

        /// <summary>
        /// Applies entire-map corners and announces the new scope.
        /// <paramref name="isNoEnclosure"/> selects the "No enclosure detected, selected …" phrasing
        /// (true) vs. the "Expanded to …" phrasing (false).
        /// </summary>
        private static void ApplyEntireMapScope(Map map, ShapeType shape, bool isNoEnclosure = false)
        {
            IntVec3 cornerA = new IntVec3(0, 0, 0);
            IntVec3 cornerB = new IntVec3(map.Size.x - 1, 0, map.Size.z - 1);
            string scopeLabel = (string)"RimWorldAccess.Building.ArchitectPlace.CtrlA.ScopeEntireMap".Translate(map.Size.x.ToString(), map.Size.z.ToString());

            ShapePlacementState.PushCtrlAHistory();
            ShapePlacementState.SetBothPoints(cornerA, cornerB);
            ShapePlacementState.SetCtrlAStage(CtrlAStage.EntireMap);

            int cellCount = ShapePlacementState.PreviewCells?.Count ?? 0;
            string shapeName = ShapeHelper.GetShapeName(shape);
            string announceKey = isNoEnclosure
                ? "RimWorldAccess.Building.ArchitectPlace.CtrlA.NoEnclosureSelectedScope"
                : "RimWorldAccess.Building.ArchitectPlace.CtrlA.ExpandedToScope";
            TolkHelper.SpeakData((string)announceKey.Translate(scopeLabel, shapeName, cellCount.ToString()));
        }

        /// <summary>
        /// Ctrl+Shift+A pops the most recent Ctrl+A scope and restores the prior selection, or
        /// clears it when the prior step had no points. Returns true if the key was handled.
        /// </summary>
        private static bool HandleCtrlShiftAKey()
        {
            if (!ShapePlacementState.IsActive)
                return false;

            ShapeType shape = ShapePlacementState.CurrentShape;
            if (shape == ShapeType.Manual)
            {
                TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.CtrlA.RequiresShape".Loc());
                return true;
            }
            if (shape == ShapeType.Line || shape == ShapeType.AngledLine)
            {
                TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.CtrlA.NotAvailableForLines".Loc());
                return true;
            }

            if (!ShapePlacementState.HasCtrlAHistory)
            {
                TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.CtrlA.NoPreviousScope".Loc());
                return true;
            }

            if (!ShapePlacementState.TryUndoCtrlA())
            {
                TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.CtrlA.NoPreviousScope".Loc());
                return true;
            }

            CtrlAStage stage = ShapePlacementState.CurrentCtrlAStage;
            string shapeName = ShapeHelper.GetShapeName(shape);

            if (stage == CtrlAStage.None)
            {
                if (!ShapePlacementState.HasFirstPoint)
                {
                    TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.CtrlA.ClearedMoveToFirstPoint".Loc());
                }
                else
                {
                    int cells = ShapePlacementState.PreviewCells?.Count ?? 0;
                    TolkHelper.SpeakData((string)"RimWorldAccess.Building.ArchitectPlace.CtrlA.ReturnedToPreviousSelection".Translate(shapeName, cells.ToString()));
                }
                return true;
            }

            int cellCount = ShapePlacementState.PreviewCells?.Count ?? 0;
            string scopeName = stage == CtrlAStage.Enclosure
                ? (string)"RimWorldAccess.Building.ArchitectPlace.CtrlA.ScopeNameEnclosure".Translate()
                : (string)"RimWorldAccess.Building.ArchitectPlace.CtrlA.ScopeNameEntireMap".Translate();
            TolkHelper.SpeakData((string)"RimWorldAccess.Building.ArchitectPlace.CtrlA.ReturnedToScope".Translate(scopeName, shapeName, cellCount.ToString()));
            return true;
        }

        /// <summary>
        /// Detects a blueprint-walled enclosure around <paramref name="cursor"/>, which the Room
        /// system misses because it only recognises finished walls: flood-fills treating wall
        /// blueprints and frames as boundaries, and returns the interior's bounding box as shape
        /// corners when the fill does not escape to the map edge.
        /// </summary>
        private static bool TryBlueprintEnclosureBounds(IntVec3 cursor, Map map,
            out IntVec3 cornerA, out IntVec3 cornerB, out string scopeLabel)
        {
            cornerA = default;
            cornerB = default;
            scopeLabel = null;

            var (isEnclosed, interior) = EnclosureDetector.TryFloodFillFromCell(cursor, map);
            if (!isEnclosed || interior == null || interior.Count == 0)
                return false;

            int minX = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxZ = int.MinValue;
            foreach (IntVec3 c in interior)
            {
                if (c.x < minX) minX = c.x;
                if (c.x > maxX) maxX = c.x;
                if (c.z < minZ) minZ = c.z;
                if (c.z > maxZ) maxZ = c.z;
            }

            cornerA = new IntVec3(minX, 0, minZ);
            cornerB = new IntVec3(maxX, 0, maxZ);
            scopeLabel = (string)"RimWorldAccess.Building.ArchitectPlace.CtrlA.ScopeBlueprintEnclosure".Translate((maxX - minX + 1).ToString(), (maxZ - minZ + 1).ToString());
            return true;
        }

        /// <summary>
        /// Shift+Space removes the last shape point, or cancels the blueprint under the cursor
        /// when no shape points are pending. Returns true if the key was handled.
        /// </summary>
        private static bool HandleShiftSpaceKey()
        {
            if (ShapePlacementState.IsActive &&
                ShapePlacementState.CurrentShape != ShapeType.Manual &&
                ShapePlacementState.HasFirstPoint)
            {
                ShapePlacementState.RemoveLastPoint();
                return true;
            }
            else if (ShapePlacementState.IsActive &&
                     ShapePlacementState.CurrentShape != ShapeType.Manual &&
                     !ShapePlacementState.HasFirstPoint)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Place.NoPointsToRemove".Loc());
                return true;
            }
            else
            {
                IntVec3 currentPosition = MapNavigationState.CurrentCursorPosition;
                CancelBlueprintAtPosition(currentPosition);
                return true;
            }
        }

        /// <summary>
        /// Space places a cell, sets the next shape point, or toggles a selection depending on
        /// designator type and placement phase. Returns true if the key was handled.
        /// </summary>
        private static bool HandleSpaceKey(Designator activeDesignator, IntVec3 currentPosition, bool inArchitectMode, bool isPlaceDesignator, bool isZoneDesignator, bool isOrderDesignator, bool isCellsDesignator)
        {
            // The two-point shape workflow is the same for every designator type.
            if (ShapePlacementState.IsActive && ShapePlacementState.CurrentShape != ShapeType.Manual)
            {
                if (ShapePlacementState.CurrentPhase == PlacementPhase.SettingFirstCorner)
                {
                    ShapePlacementState.SetFirstPoint(currentPosition);
                }
                else if (ShapePlacementState.CurrentPhase == PlacementPhase.SettingSecondCorner)
                {
                    ShapePlacementState.SetSecondPoint(currentPosition);
                }
            }
            else
            {
                // A rect designator's unit of work is a rectangle; a mouse click without a drag is a
                // 1x1 one, so Space is exactly that.
                if (RectDesignationRouter.IsRectDesignator(activeDesignator))
                {
                    ShapePlacementState.PlaceSingleCellRect(currentPosition);
                    return true;
                }

                if (isPlaceDesignator)
                {
                    string placedPhrase;
                    if (TryManualSinglePlacement(activeDesignator, currentPosition, inArchitectMode, out placedPhrase))
                    {
                        // Space stays in placement mode; Enter confirms and exits.
                        TolkHelper.SpeakData(placedPhrase);
                    }
                }
                else if (isZoneDesignator)
                {
                    if (inArchitectMode)
                    {
                        ArchitectState.ToggleCell(currentPosition);
                    }
                    else
                    {
                        // From a zone gizmo: GizmoZoneEditState carries the connectivity and
                        // adjacency checks the architect-mode selection list does not need.
                        GizmoZoneEditState.ToggleZoneCellAtCursor(activeDesignator);
                    }
                }
                else if ((isOrderDesignator || isCellsDesignator) && inArchitectMode)
                {
                    ArchitectState.ToggleCell(currentPosition);
                }
                else if (BuildingReflection.IsGravshipDesignator(activeDesignator))
                {
                    // Compensate for a vanilla rounding bug in even-sized gravships:
                    // GetSizeRotAdjustedCell uses size/2 but PrefabUtility.GetRoot uses (size-1)/2,
                    // causing a +1 offset per axis when that dimension is even.
                    IntVec3 placementPos = currentPosition;
                    var marker = BuildingReflection.GetGravshipMarker(activeDesignator);
                    if (marker != null)
                    {
                        IntVec2 size = marker.gravship.Bounds.Size;
                        Rot4 rot = marker.GravshipRotation;

                        IntVec3 halfX = new IntVec3(size.x / 2, 0, 0);
                        IntVec3 halfZ = new IntVec3(0, 0, size.z / 2);
                        IntVec3 adjusted = currentPosition;
                        if (rot == Rot4.North) adjusted += halfX + halfZ;
                        else if (rot == Rot4.East) adjusted += halfX - halfZ;
                        else if (rot == Rot4.South) adjusted -= halfX + halfZ;
                        else if (rot == Rot4.West) adjusted += -halfX + halfZ;

                        IntVec3 wouldPlace = PrefabUtility.GetRoot(adjusted, size, rot);
                        IntVec3 offset = wouldPlace - currentPosition;
                        if (offset != IntVec3.Zero)
                        {
                            placementPos = currentPosition - offset;
                        }
                    }

                    AcceptanceReport report = activeDesignator.CanDesignateCell(placementPos);
                    if (report.Accepted)
                    {
                        activeDesignator.DesignateSingleCell(placementPos);
                        TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.GravshipPositioned".Loc(
                            currentPosition.x, currentPosition.z));
                        // DesignateSingleCell auto-deselects the designator
                    }
                    else
                    {
                        TolkHelper.Speak(PlacementDescriber.DescribeRejection(report));
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// The manual single-building placement Space runs and first-press Enter borrows:
        /// vanilla's own CanDesignateCell gate, the meditation-protection and god-mode wipe
        /// guards, then DesignateSingleCell + Finalize. True only when the building was placed,
        /// with the "placed at" sentence in <paramref name="placedPhrase"/> for the caller to
        /// speak; every refusal speaks for itself and returns false.
        /// </summary>
        private static bool TryManualSinglePlacement(Designator activeDesignator, IntVec3 currentPosition, bool inArchitectMode, out string placedPhrase)
        {
            placedPhrase = null;
            AcceptanceReport report = activeDesignator.CanDesignateCell(currentPosition);
            if (!report.Accepted)
            {
                TolkHelper.Speak(PlacementDescriber.DescribeRejection(report));
                return false;
            }

            var (placingDef, placingRot) =
                MeditationProtectionHelper.GetPlacementInfo(activeDesignator);
            if (placingDef != null &&
                MeditationProtectionHelper.IsArtificialBuilding(placingDef, Faction.OfPlayer))
            {
                var protection = MeditationProtectionHelper.CheckProtection(
                    Find.CurrentMap, placingDef, Faction.OfPlayer,
                    currentPosition, placingRot);
                if (protection.IsProtected)
                {
                    TolkHelper.SpeakData(
                        MeditationProtectionHelper.FormatManualBlockMessage(protection));
                    return false;
                }
            }

            if (!GodModeWipeWarning.WarnBeforeInstantPlace(
                activeDesignator, currentPosition, Find.CurrentMap))
            {
                return false;
            }

            try
            {
                activeDesignator.DesignateSingleCell(currentPosition);
                activeDesignator.Finalize(true);

                // The new blueprint occupies cells and blocks interaction spots, so the
                // listed placement spots are now stale.
                PlacementSpotScanner.NotifyPlacementCompleted();
                manualSessionPlacements++;

                if (inArchitectMode)
                {
                    ArchitectState.ClearSelectedCells();
                }
                placedPhrase = "RimWorldAccess.Building.View.PlacedAt"
                    .Translate(activeDesignator.Label, currentPosition.x, currentPosition.z)
                    .ToString();
                return true;
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.ErrorPlacing".Loc(ex.Message), SpeechPriority.High);
                Log.Error($"Error in single cell designation: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Enter confirms and executes the pending designation. Returns true if the key was
        /// handled.
        /// </summary>
        private static bool HandleEnterKey(Designator activeDesignator, bool inArchitectMode, bool isPlaceDesignator, bool isZoneDesignator, bool isOrderDesignator, bool isCellsDesignator, bool isDeleteDesignator)
        {
            if (ShapePlacementState.IsActive && ShapePlacementState.CurrentPhase == PlacementPhase.Previewing)
            {
                // Kept for restore after undo.
                ShapeType currentShape = ShapePlacementState.CurrentShape;

                try
                {
                    // Silent because ViewingModeState.Enter announces the placement.
                    var result = ShapePlacementState.PlaceDesignations(silent: true);

                    if (result.NeedsFullDeletionConfirmation)
                    {
                        ShowZoneDeletionConfirmation(result, activeDesignator, currentShape);
                        return true;
                    }
                    else if (result.PlacedCount > 0)
                    {
                        // Viewing mode over a selection change is wrong, and the branch below would
                        // otherwise enter it. The exit may reset the active designator, so this is
                        // the only place the silent result can speak.
                        if (result.WasRectDesignation)
                        {
                            ShapePlacementState.AnnounceResult(result);
                            ShapePlacementState.ExitAfterRectDesignation(result);
                            return true;
                        }

                        // God-mode terrain is set directly, leaving no things for viewing mode
                        // to track; announce the result and stay in placement mode instead.
                        if (isPlaceDesignator && (result.PlacedBlueprints == null || result.PlacedBlueprints.Count == 0))
                        {
                            ShapePlacementState.AnnounceResult(result);
                            ShapePlacementState.ClearSelectionAndStay(silent: true);
                        }
                        else
                        {
                            ViewingModeState.Enter(result, activeDesignator, currentShape);
                            ShapePlacementState.Reset();
                        }
                    }
                    else
                    {
                        // Nothing placed: report why and stay in placement mode so the next attempt
                        // needs no re-entry.
                        if (isDeleteDesignator)
                        {
                            TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.NoZoneCellsTryAgain".Loc());
                        }
                        else if (result.ProtectedCount > 0 || result.ObstacleCount > 0)
                        {
                            ShapePlacementState.AnnounceResult(result);
                        }
                        else
                        {
                            TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.NoValidCellsTryAgain".Loc());
                        }
                        ShapePlacementState.ClearSelectionAndStay(silent: true);
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[ArchitectPlacementPatch] Exception during placement: {ex}");
                    TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.PlacementFailedClearing".Loc());
                    ShapePlacementState.Reset();
                }
                return true;
            }
            // A half-built shape prompts for its second point; with no point yet, Enter falls to the completion branch below.
            else if (ShapePlacementState.IsActive && ShapePlacementState.CurrentShape != ShapeType.Manual
                && ShapePlacementState.CurrentPhase == PlacementPhase.SettingSecondCorner)
            {
                TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.PlaceSecondPoint".Loc());
                return true;
            }
            else if (isPlaceDesignator)
            {
                string announcement = "RimWorldAccess.Building.ArchitectPlace.PlacementCompleted"
                    .Translate().ToString();
                // Manual mode with nothing placed yet: Enter stands in for the forgotten Space,
                // placing at the cursor before closing. A refused cell speaks its reason and
                // stays in placement mode, leaving Escape as the way out.
                bool manualFirstPress = manualSessionPlacements == 0
                    && (!ShapePlacementState.IsActive || ShapePlacementState.CurrentShape == ShapeType.Manual)
                    && !RectDesignationRouter.IsRectDesignator(activeDesignator);
                if (manualFirstPress)
                {
                    string placedPhrase;
                    if (!TryManualSinglePlacement(
                            activeDesignator, MapNavigationState.CurrentCursorPosition,
                            inArchitectMode, out placedPhrase))
                    {
                        return true;
                    }
                    announcement = placedPhrase + ". " + announcement;
                }
                TolkHelper.SpeakData(announcement);
                if (ShapePlacementState.IsActive)
                {
                    ShapePlacementState.Reset();
                }
                if (inArchitectMode)
                    ArchitectState.Reset();
                else
                    Find.DesignatorManager.Deselect();
                return true;
            }
            else if (isZoneDesignator && inArchitectMode)
            {
                Map map = Find.CurrentMap;
                ExecuteZonePlacement(activeDesignator, map);
                if (ShapePlacementState.IsActive)
                {
                    ShapePlacementState.Reset();
                }
                return true;
            }
            else if (isZoneDesignator && !inArchitectMode)
            {
                // GizmoZoneEditState already applied each change, so Enter only exits.
                TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.ZoneEditingCompleted".Loc());

                if (ShapePlacementState.IsActive)
                {
                    ShapePlacementState.Reset();
                }
                if (GizmoZoneEditState.IsActive)
                {
                    GizmoZoneEditState.Reset();
                }
                Find.DesignatorManager.Deselect();
                return true;
            }
            else if (inArchitectMode && (isOrderDesignator || isCellsDesignator))
            {
                ArchitectState.ExecutePlacement(Find.CurrentMap);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Escape steps back one level: clear the pending points, else return to viewing mode,
        /// else leave placement entirely. Returns true if the key was handled.
        /// </summary>
        private static bool HandleEscapeKey(bool inArchitectMode)
        {
            if (ShapePlacementState.IsActive)
            {
                if (ShapePlacementState.HasFirstPoint)
                {
                    // Silent so the announcement below is the only one.
                    ShapePlacementState.ClearSelectionAndStay(silent: true);
                    TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.SelectionCleared".Loc());
                }
                else if (ShapePlacementState.HasViewingModeOnStack)
                {
                    ShapePlacementState.Reset();
                    ViewingModeState.Reactivate();
                }
                else
                {
                    // No pending points and no viewing-mode stack: leaving discards nothing,
                    // so say "exited" — "cancelled" would imply placed work was undone.
                    TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.ExitedPlacement".Loc());
                    ShapePlacementState.Reset();

                    if (inArchitectMode)
                    {
                        ArchitectState.Reset();
                    }
                    else
                    {
                        Find.DesignatorManager.Deselect();
                    }
                }
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.ExitedPlacement".Loc());

                if (inArchitectMode)
                {
                    ArchitectState.Cancel();
                }
                else
                {
                    Find.DesignatorManager.Deselect();
                }
            }
            return true;
        }

        /// <summary>
        /// Cancels any blueprint or frame at the specified position.
        /// </summary>
        private static void CancelBlueprintAtPosition(IntVec3 position)
        {
            Map map = Find.CurrentMap;
            if (map == null)
                return;

            List<Thing> thingList = position.GetThingList(map);

            bool foundAndCanceled = false;
            for (int i = thingList.Count - 1; i >= 0; i--)
            {
                Thing thing = thingList[i];

                if (thing.Faction == Faction.OfPlayer && (thing is Frame || thing is Blueprint))
                {
                    string thingLabel = thing.LabelShort;
                    thing.Destroy(DestroyMode.Cancel);
                    TolkHelper.Speak("RimWorldAccess.Building.View.CancelledBlueprint".Loc(thingLabel));
                    SoundDefOf.Designate_Cancel.PlayOneShotOnCamera();
                    foundAndCanceled = true;
                    break; // Only cancel one blueprint per keypress
                }
            }

            if (!foundAndCanceled)
            {
                TolkHelper.Speak("RimWorldAccess.Building.View.NoBlueprintHere".Loc());
            }
        }

        /// <summary>
        /// Confirms or cancels local map targeting (transport pod landing). Returns true if the
        /// input was handled.
        /// </summary>
        private static bool HandleTargetingModeInput(KeyCode key, bool shiftHeld)
        {
            if (key == KeyCode.Space || key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                if (shiftHeld)
                    return false;

                IntVec3 targetCell = MapNavigationState.CurrentCursorPosition;
                Map map = Find.CurrentMap;

                if (map == null || !targetCell.InBounds(map))
                {
                    TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.InvalidTargetPosition".Loc(), SpeechPriority.High);
                    return true;
                }

                if (!DropCellFinder.IsGoodDropSpot(targetCell, map, allowFogged: false, canRoofPunch: true))
                {
                    string reason = GetLandingInvalidReason(targetCell, map);
                    TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.CannotLandHere".Loc(reason), SpeechPriority.High);
                    return true;
                }

                LocalTargetInfo target = new LocalTargetInfo(targetCell);

                // StopTargeting clears the action, so read it first.
                var actionField = HarmonyLib.AccessTools.Field(typeof(Targeter), "action");
                System.Action<LocalTargetInfo> action = null;
                if (actionField != null)
                {
                    action = actionField.GetValue(Find.Targeter) as System.Action<LocalTargetInfo>;
                }

                if (action != null)
                {
                    Find.Targeter.StopTargeting();
                    action.Invoke(target);
                    TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.LandingConfirmed".Loc(
                        targetCell.x, targetCell.z), SpeechPriority.Normal);
                }
                else
                {
                    Find.Targeter.StopTargeting();
                    TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.CouldNotConfirmTarget".Loc(), SpeechPriority.High);
                }

                return true;
            }

            if (key == KeyCode.Escape)
            {
                Find.Targeter.StopTargeting();
                TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.TargetingCancelled".Loc(), SpeechPriority.Normal);
                return true;
            }

            return false;
        }

        /// <summary>
        /// A human-readable reason why a landing spot is invalid. Only thick roofs (overhead
        /// mountain) block landing; pods punch through thin ones.
        /// </summary>
        private static string GetLandingInvalidReason(IntVec3 cell, Map map)
        {
            if (map == null || !cell.InBounds(map))
                return "RimWorldAccess.Building.ArchitectPlace.LandReason.OutOfBounds".Translate();

            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain != null)
            {
                if (terrain.IsWater)
                    return "RimWorldAccess.Building.ArchitectPlace.LandReason.Water".Translate();
            }

            RoofDef roof = cell.GetRoof(map);
            if (roof != null && roof.isThickRoof)
                return "RimWorldAccess.Building.ArchitectPlace.LandReason.OverheadMountain".Translate();

            if (!cell.Walkable(map))
                return "RimWorldAccess.Building.ArchitectPlace.LandReason.Impassable".Translate();

            Building building = cell.GetEdifice(map);
            if (building != null)
            {
                // Clearable free buildings (conduits and the like) do not block landing.
                if (!building.IsClearableFreeBuilding)
                    return building.LabelCap;
            }

            if (cell.Fogged(map))
                return "RimWorldAccess.Building.ArchitectPlace.LandReason.Fogged".Translate();

            List<Thing> things = cell.GetThingList(map);
            foreach (Thing thing in things)
            {
                if (thing is IActiveTransporter)
                    return "RimWorldAccess.Building.ArchitectPlace.LandReason.AnotherTransportPod".Translate();
                if (thing is Skyfaller)
                    return "RimWorldAccess.Building.ArchitectPlace.LandReason.IncomingSkyfaller".Translate();
            }

            return "RimWorldAccess.Building.ArchitectPlace.LandReason.InvalidSpot".Translate();
        }

        /// <summary>
        /// Confirms a zone shrink that would delete the whole zone, which Escape in viewing mode
        /// cannot undo.
        /// </summary>
        private static void ShowZoneDeletionConfirmation(PlacementResult pendingResult, Designator designator, ShapeType currentShape)
        {
            string zoneName = pendingResult.ZonePendingDeletion?.label
                ?? "RimWorldAccess.Building.ArchitectPlace.DeleteZoneFallbackName".Translate();

            // The visual text keeps its paragraph break; the spoken form is phrase fragments.
            string message = "RimWorldAccess.Building.ArchitectPlace.DeleteZoneDialogText".Translate(zoneName);

            string announcement = new AnnouncementBuilder()
                .Add("RimWorldAccess.Building.ArchitectPlace.DeleteZoneConfirmHeading".Translate())
                .Add("RimWorldAccess.Building.ArchitectPlace.DeleteZoneWillDelete".Translate(zoneName))
                .Add("RimWorldAccess.Building.ArchitectPlace.DeleteZoneCannotUndo".Translate())
                .Add("RimWorldAccess.Building.ArchitectPlace.DeleteZoneUseGizmoHint".Translate())
                .Build();
            TolkHelper.SpeakData(announcement);

            Dialog_MessageBox dialog = new Dialog_MessageBox(
                message,
                "RimWorldAccess.Building.ArchitectPlace.DeleteZoneConfirmTitle".Translate(),
                () =>
                {
                    var result = ShapePlacementState.ExecuteConfirmedZoneDeletion(pendingResult, silent: true);
                    TolkHelper.Speak("RimWorldAccess.Building.Place.ZoneDeleted".Loc(zoneName));

                    // Deletion cannot be undone, so leave the whole build/zone interface.
                    ShapePlacementState.Reset();
                    ArchitectState.Reset();
                    Find.DesignatorManager.Deselect();
                },
                "CancelButton".Translate(),
                () =>
                {
                    TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.DeletionCancelled".Loc());
                },
                null,  // title
                true   // buttonADestructive
            );

            Find.WindowStack.Add(dialog);
        }

        /// <summary>
        /// Executes zone placement with all selected cells.
        /// </summary>
        private static void ExecuteZonePlacement(Designator designator, Map map)
        {
            if (ArchitectState.SelectedCells.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Place.NoCellsSelected".Loc());
                ArchitectState.Reset();
                return;
            }

            try
            {
                // The cell under the cursor decides expand vs. create.
                IntVec3 referenceCell = ArchitectState.SelectedCells[0];
                ZoneSelectionResult selectionResult = ZoneSelectionHelper.SelectZoneAtCell(designator, referenceCell);

                designator.DesignateMultiCell(ArchitectState.SelectedCells);

                string label = designator.Label ?? "RimWorldAccess.Building.ArchitectPlace.ZoneFallbackName".Translate();
                string zoneName = selectionResult.IsExpansion && selectionResult.TargetZone != null
                    ? selectionResult.TargetZone.label
                    : label;
                int cellCount = ArchitectState.SelectedCells.Count;
                // Whole-phrase per case (no glue particle for the expand/create verb).
                string announcement = selectionResult.IsExpansion
                    ? "RimWorldAccess.Building.ArchitectPlace.ZoneExpandedWithCells".Translate(zoneName, cellCount)
                    : "RimWorldAccess.Building.ArchitectPlace.ZoneCreatedWithCells".Translate(zoneName, cellCount);
                TolkHelper.SpeakData(announcement);
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Create.ErrorCreatingZone".Loc(ex.Message), SpeechPriority.High);
                Log.Error($"ExecuteZonePlacement error: {ex}");
            }
            finally
            {
                ArchitectState.Reset();
            }
        }

        /// <summary>Rotation announcement for a Designator_Place (reinstall gizmo and the like).</summary>
        private static string GetDesignatorRotationAnnouncement(Designator_Place designatorPlace, Rot4 rotation)
        {
            return ArchitectState.GetRotationAnnouncementForDef(designatorPlace.PlacingDef, rotation);
        }
    }

    /// <summary>Keeps Space from pausing the game while architect placement mode is active.</summary>
    [HarmonyPatch(typeof(TimeControls))]
    [HarmonyPatch("DoTimeControlsGUI")]
    public static class ArchitectPlacementTimeControlsPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix()
        {
            if (!ArchitectState.IsInPlacementMode)
                return true;

            if (Event.current.type == EventType.KeyDown &&
                KeyBindingDefOf.TogglePause.KeyDownEvent)
            {
                Event.current.Use();
                return false;
            }

            return true;
        }
    }

    /// <summary>Draws the selected cells and cursor cell during architect placement.</summary>
    [HarmonyPatch(typeof(SelectionDrawer))]
    [HarmonyPatch("DrawSelectionOverlays")]
    public static class ArchitectPlacementVisualizationPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!ArchitectState.IsInPlacementMode)
                return;

            Map map = Find.CurrentMap;
            if (map == null)
                return;

            foreach (IntVec3 cell in ArchitectState.SelectedCells)
            {
                if (cell.InBounds(map))
                {
                    Graphics.DrawMesh(
                        MeshPool.plane10,
                        cell.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays),
                        Quaternion.identity,
                        GenDraw.InteractionCellMaterial,
                        0
                    );
                }
            }

            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
            if (cursorPos.InBounds(map))
            {
                Graphics.DrawMesh(
                    MeshPool.plane10,
                    cursorPos.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays),
                    Quaternion.identity,
                    GenDraw.InteractionCellMaterial,
                    0
                );
            }
        }
    }
}
