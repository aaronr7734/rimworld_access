using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for zone editing when working from gizmos (not architect menu).
    /// Tracks the target zone, original cells, and modifications to enable proper
    /// cell toggling with connectivity checks and adjacency enforcement.
    ///
    /// This is used when:
    /// 1. User selects expand/shrink from a zone's gizmo
    /// 2. User enters manual mode (Shift+Tab) for cell-by-cell editing
    /// 3. User presses Space to toggle cells
    /// </summary>
    public static class GizmoZoneEditState
    {
        private static bool isActive = false;
        private static Zone targetZone = null;
        private static HashSet<IntVec3> originalZoneCells = new HashSet<IntVec3>();
        private static HashSet<Zone> createdZones = new HashSet<Zone>();
        private static bool isDeleteDesignator = false;
        private static Designator activeDesignator = null;

        #region Properties

        /// <summary>
        /// Whether gizmo zone edit state is active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// The zone being edited.
        /// </summary>
        public static Zone TargetZone => targetZone;

        #endregion

        #region State Management

        /// <summary>
        /// Initializes state for zone editing from a gizmo.
        /// Call this when entering manual mode with a zone designator from a gizmo.
        /// </summary>
        /// <param name="designator">The zone designator (expand or shrink)</param>
        public static void Initialize(Designator designator)
        {
            if (designator == null)
                return;

            activeDesignator = designator;
            isDeleteDesignator = ShapeHelper.IsDeleteDesignator(designator);

            // Get the target zone - for gizmo mode, it should be selected
            Zone selectedZone = Find.Selector?.SelectedZone;

            if (selectedZone == null)
            {
                // Try to get zone at cursor
                Map map = Find.CurrentMap;
                if (map?.zoneManager != null)
                {
                    IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
                    selectedZone = map.zoneManager.ZoneAt(cursorPos);
                }
            }

            if (selectedZone != null)
            {
                targetZone = selectedZone;

                // Capture original cells for shrink mode (allows re-adding removed cells)
                originalZoneCells.Clear();
                foreach (IntVec3 cell in selectedZone.Cells)
                {
                    originalZoneCells.Add(cell);
                }

                // Track this zone as being edited
                createdZones.Clear();
                createdZones.Add(selectedZone);

                isActive = true;

                Log.Message($"[GizmoZoneEditState] Initialized for {selectedZone.label}, {originalZoneCells.Count} original cells, isDelete={isDeleteDesignator}");
            }
            else
            {
                Log.Warning("[GizmoZoneEditState] Could not find target zone for editing");
                Reset();
            }
        }

        /// <summary>
        /// Resets all state.
        /// </summary>
        public static void Reset()
        {
            isActive = false;
            targetZone = null;
            originalZoneCells.Clear();
            createdZones.Clear();
            isDeleteDesignator = false;
            activeDesignator = null;
        }

        #endregion

        #region Zone Cell Operations

        /// <summary>
        /// Toggles a zone cell at the current cursor position.
        /// Uses the same logic as ViewingModeState via ZoneEditingHelper.
        /// </summary>
        public static void ToggleZoneCellAtCursor()
        {
            if (!isActive)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Zone.NoZoneBeingEdited".Translate(), SpeechPriority.Normal);
                return;
            }

            // Use ZoneEditingHelper for consistent behavior with viewing mode
            var result = ZoneEditingHelper.ToggleZoneCellAtCursor(
                ref targetZone,
                activeDesignator,
                createdZones,
                originalZoneCells,
                isDeleteDesignator);

            TolkHelper.Speak(result.Message, result.Priority);

            if (result.Success)
            {
                IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
                if (result.ZoneDeleted)
                {
                    Log.Message($"[GizmoZoneEditState] Zone cell operation at {cursorPos}: zone was deleted");
                    // Zone was deleted - reset state
                    Reset();
                }
                else
                {
                    Log.Message($"[GizmoZoneEditState] Zone cell operation at {cursorPos}: {result.Message}");
                }
            }
        }

        /// <summary>
        /// Checks if the state should be initialized for the given designator.
        /// Returns true if this is a zone designator selected from a gizmo (not architect mode).
        /// </summary>
        /// <param name="designator">The designator to check</param>
        /// <returns>True if this is a gizmo-selected zone designator</returns>
        public static bool ShouldInitializeFor(Designator designator)
        {
            if (designator == null)
                return false;

            // Must be a zone designator
            if (!ShapeHelper.IsZoneDesignator(designator))
                return false;

            // Must have a selected zone (indicates gizmo selection)
            // For architect menu, the zone isn't typically selected
            Zone selectedZone = Find.Selector?.SelectedZone;
            if (selectedZone != null)
                return true;

            // Also check if cursor is on a zone (for expand/shrink from any source)
            Map map = Find.CurrentMap;
            if (map?.zoneManager != null)
            {
                IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
                Zone zoneAtCursor = map.zoneManager.ZoneAt(cursorPos);
                if (zoneAtCursor != null)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Ensures state is initialized for the given designator if needed.
        /// Safe to call multiple times - will only initialize once.
        /// </summary>
        /// <param name="designator">The designator to initialize for</param>
        public static void EnsureInitialized(Designator designator)
        {
            // Already active with same designator - nothing to do
            if (isActive && activeDesignator == designator)
                return;

            // Check if we should initialize
            if (ShouldInitializeFor(designator))
            {
                Initialize(designator);
            }
        }

        #endregion
    }
}
