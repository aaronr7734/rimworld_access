using System;
using HarmonyLib;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Result of zone selection based on cursor position.
    /// </summary>
    public struct ZoneSelectionResult
    {
        /// <summary>
        /// True if we're expanding an existing zone, false if creating a new one.
        /// </summary>
        public bool IsExpansion { get; set; }

        /// <summary>
        /// The zone being expanded (null if creating new).
        /// </summary>
        public Zone TargetZone { get; set; }
    }

    /// <summary>
    /// Helper for cursor-based zone selection.
    /// Determines whether to expand an existing zone or create a new one based on
    /// the cell where the user started their selection.
    /// </summary>
    public static class ZoneSelectionHelper
    {
        // Cached reflection accessor for Designator_ZoneAdd.zoneTypeToPlace
        private static readonly System.Reflection.FieldInfo zoneTypeToPlaceField =
            AccessTools.Field(typeof(Designator_ZoneAdd), "zoneTypeToPlace");

        /// <summary>
        /// Selects or clears zone selection based on the cell where the user started placement.
        /// Call this BEFORE calling DesignateMultiCell to ensure correct expand vs create behavior.
        /// </summary>
        /// <param name="designator">The active zone designator</param>
        /// <param name="referenceCell">The cell where the user started (first corner or first selected cell)</param>
        /// <returns>Result indicating whether this is an expansion and the target zone if any</returns>
        public static ZoneSelectionResult SelectZoneAtCell(Designator designator, IntVec3 referenceCell)
        {
            ZoneSelectionResult result = new ZoneSelectionResult
            {
                IsExpansion = false,
                TargetZone = null
            };

            // For delete/shrink designators, get the target zone from selection or cursor
            // Don't clear selection - the zone should already be selected from the gizmo
            if (ShapeHelper.IsDeleteDesignator(designator))
            {
                // First try currently selected zone
                Zone selectedZone = Find.Selector.SelectedZone;
                if (selectedZone != null)
                {
                    result.TargetZone = selectedZone;
                    return result;
                }

                // Fall back to zone at cursor
                Map currentMap = Find.CurrentMap;
                if (currentMap?.zoneManager != null)
                {
                    Zone zoneAtCursor = currentMap.zoneManager.ZoneAt(referenceCell);
                    if (zoneAtCursor != null)
                    {
                        // Select it so shrink operations work correctly
                        Find.Selector.ClearSelection();
                        Find.Selector.Select(zoneAtCursor, playSound: false, forceDesignatorDeselect: false);
                        result.TargetZone = zoneAtCursor;
                    }
                }
                return result;
            }

            // Only applies to Designator_ZoneAdd for expand/create logic
            Designator_ZoneAdd zoneAddDesignator = designator as Designator_ZoneAdd;
            if (zoneAddDesignator == null)
            {
                // Not a zone add designator - just clear selection and return
                Find.Selector.ClearSelection();
                return result;
            }

            // Get the type of zone this designator creates
            Type zoneTypeToPlace = zoneTypeToPlaceField?.GetValue(zoneAddDesignator) as Type;
            if (zoneTypeToPlace == null)
            {
                Log.Warning("[ZoneSelectionHelper] Could not get zoneTypeToPlace from designator");
                Find.Selector.ClearSelection();
                return result;
            }

            // Get the zone at the reference cell
            Map map = Find.CurrentMap;
            if (map?.zoneManager == null)
            {
                Find.Selector.ClearSelection();
                return result;
            }

            // FIRST check if there's already a selected zone of the correct type
            // This handles the gizmo expand case where the zone is already selected
            Zone alreadySelectedZone = Find.Selector.SelectedZone;
            if (alreadySelectedZone != null && alreadySelectedZone.GetType() == zoneTypeToPlace)
            {
                // Already have a matching zone selected (from gizmo) - use it
                result.IsExpansion = true;
                result.TargetZone = alreadySelectedZone;
                Log.Message($"[ZoneSelectionHelper] Using already-selected zone {alreadySelectedZone.label} for expansion");
                return result;
            }

            Zone zoneAtCell = map.zoneManager.ZoneAt(referenceCell);

            // Check if zone exists and matches the type we're trying to place
            if (zoneAtCell != null && zoneAtCell.GetType() == zoneTypeToPlace)
            {
                // Same type zone at cursor - select it to expand
                Find.Selector.ClearSelection();
                Find.Selector.Select(zoneAtCell, playSound: false, forceDesignatorDeselect: false);

                result.IsExpansion = true;
                result.TargetZone = zoneAtCell;

                Log.Message($"[ZoneSelectionHelper] Cursor inside {zoneAtCell.label} - will expand");
            }
            else
            {
                // No zone or different type - clear selection to create new
                Find.Selector.ClearSelection();

                if (zoneAtCell != null)
                {
                    Log.Message($"[ZoneSelectionHelper] Cursor in different zone type ({zoneAtCell.GetType().Name} vs {zoneTypeToPlace.Name}) - will create new");
                }
                else
                {
                    Log.Message($"[ZoneSelectionHelper] Cursor on empty ground - will create new");
                }
            }

            return result;
        }

        /// <summary>
        /// Previews what would happen if zone placement occurs at the given cell.
        /// Does NOT modify any selection state - use this for announcements.
        /// </summary>
        /// <param name="designator">The active zone designator</param>
        /// <param name="referenceCell">The cell to check (typically cursor position)</param>
        /// <returns>Result indicating whether this would be an expansion and the target zone if any</returns>
        public static ZoneSelectionResult PreviewZoneAtCell(Designator designator, IntVec3 referenceCell)
        {
            ZoneSelectionResult result = new ZoneSelectionResult
            {
                IsExpansion = false,
                TargetZone = null
            };

            // Only applies to Designator_ZoneAdd (not delete/shrink designators)
            Designator_ZoneAdd zoneAddDesignator = designator as Designator_ZoneAdd;
            if (zoneAddDesignator == null)
            {
                return result;
            }

            // Get the type of zone this designator creates
            Type zoneTypeToPlace = zoneTypeToPlaceField?.GetValue(zoneAddDesignator) as Type;
            if (zoneTypeToPlace == null)
            {
                return result;
            }

            // Get the zone at the reference cell
            Map map = Find.CurrentMap;
            if (map?.zoneManager == null)
            {
                return result;
            }

            Zone zoneAtCell = map.zoneManager.ZoneAt(referenceCell);

            // Check if zone exists and matches the type we're trying to place
            if (zoneAtCell != null && zoneAtCell.GetType() == zoneTypeToPlace)
            {
                result.IsExpansion = true;
                result.TargetZone = zoneAtCell;
            }

            return result;
        }

        /// <summary>
        /// Gets an announcement string describing what will happen with the zone tool.
        /// </summary>
        /// <param name="designator">The active zone designator</param>
        /// <param name="cursorPosition">Current cursor position</param>
        /// <returns>Announcement string like "will expand existing zone." or "will create new zone."</returns>
        public static string GetZoneModeAnnouncement(Designator designator, IntVec3 cursorPosition)
        {
            ZoneSelectionResult preview = PreviewZoneAtCell(designator, cursorPosition);

            if (preview.IsExpansion && preview.TargetZone != null)
            {
                return (string)"RimWorldAccess.Building.Zone.WillExpandExisting".Translate();
            }
            else
            {
                return (string)"RimWorldAccess.Building.Zone.WillCreateNew".Translate();
            }
        }
    }
}
