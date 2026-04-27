using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class that provides cell-specific context for buildings and blueprints.
    /// When the cursor is on a multi-cell building, this helper identifies
    /// which part of the building the cursor is on (e.g., head vs foot of bed).
    /// Works for both completed buildings AND blueprints/frames.
    /// </summary>
    public static class BuildingCellHelper
    {
        /// <summary>
        /// Gets cell-specific prefix for a Thing (Building, Blueprint, or Frame).
        /// For beds: "Head" or "Foot"
        /// For pod launchers: "fuel port east" (direction to the fuel port)
        /// For watchable buildings (TVs): "watch from south"
        /// For interaction cell buildings: "use from south"
        /// </summary>
        /// <param name="thing">The thing to check (Building, Blueprint, or Frame)</param>
        /// <param name="cursorPosition">The current cursor position</param>
        /// <returns>A prefix string to prepend to the building label, or null if no special info</returns>
        public static string GetCellPrefix(Thing thing, IntVec3 cursorPosition)
        {
            if (thing == null)
                return null;

            // Get the ThingDef (for blueprints/frames, get the def they will become)
            ThingDef thingDef = GetActualThingDef(thing);
            if (thingDef == null)
                return null;

            // Get position and rotation
            IntVec3 position = thing.Position;
            Rot4 rotation = thing.Rotation;

            // Check beds - return "Head" or "Foot"
            if (thingDef.IsBed)
            {
                return GetBedCellPrefix(thingDef, position, rotation, cursorPosition);
            }

            // Check transport pod launchers - return "fuel port east" etc.
            if (thingDef.building?.hasFuelingPort == true)
            {
                return GetFuelPortInfo(position, rotation, cursorPosition);
            }

            // Check coolers - return "cooling west, heating east" etc.
            if (IsCooler(thingDef))
            {
                return GetCoolerDirectionInfo(rotation);
            }

            // Check vents - return "connects north to south" etc.
            if (IsVent(thingDef))
            {
                return GetVentDirectionInfo(rotation);
            }

            // Check thrones - return "faces north" etc.
            if (IsThrone(thingDef))
            {
                return GetThroneDirectionInfo(rotation);
            }

            // Check nutrient paste dispensers - return hopper position info
            if (IsNutrientPasteDispenser(thingDef))
            {
                return GetDispenserInfo(thingDef, rotation);
            }

            // Check turrets - return firing direction
            if (IsTurret(thingDef))
            {
                return GetTurretDirectionInfo(rotation);
            }

            // Check mech chargers - return where mech stands
            if (IsMechCharger(thingDef))
            {
                return GetMechChargerInfo(thingDef, rotation);
            }

            // Check bookcases - return reading spot direction
            if (IsBookcase(thingDef))
            {
                return GetBookcaseInfo(rotation);
            }

            // Check watchable buildings (TVs, etc.) - use game's own property
            if (IsWatchableBuilding(thingDef))
            {
                return GetWatchableInfo(thingDef, rotation);
            }

            // Check buildings with multiple interaction cells (school desks, etc.)
            if (!thingDef.multipleInteractionCellOffsets.NullOrEmpty())
            {
                return GetMultiInteractionCellInfo(thingDef, position, rotation, cursorPosition);
            }

            // Check buildings with interaction cells (fallback for workbenches, etc.)
            if (thingDef.hasInteractionCell)
            {
                return GetInteractionCellInfo(thingDef, position, rotation, cursorPosition);
            }

            return null;
        }

        /// <summary>
        /// Gets the actual ThingDef for a thing.
        /// For blueprints and frames, returns the def they will become.
        /// For regular buildings, returns def directly.
        /// </summary>
        private static ThingDef GetActualThingDef(Thing thing)
        {
            if (thing == null)
                return null;

            // Check if this is a blueprint
            if (thing is Blueprint blueprint)
            {
                return blueprint.def.entityDefToBuild as ThingDef;
            }

            // Check if this is a frame (building under construction)
            if (thing is Frame frame)
            {
                return frame.def.entityDefToBuild as ThingDef;
            }

            // Regular building - just return def
            return thing.def as ThingDef;
        }

        /// <summary>
        /// Gets the head/foot prefix for a bed based on cursor position.
        /// Works for both placed beds and blueprints.
        /// </summary>
        private static string GetBedCellPrefix(ThingDef bedDef, IntVec3 bedPosition, Rot4 bedRotation, IntVec3 cursorPosition)
        {
            // Get the number of sleeping slots (width of bed)
            int slots = BedUtility.GetSleepingSlotsCount(bedDef.size);

            // Check each slot to see if cursor is on head or foot
            for (int i = 0; i < slots; i++)
            {
                IntVec3 slotHead = BedUtility.GetSleepingSlotPos(i, bedPosition, bedRotation, bedDef.size);
                IntVec3 slotFoot = BedUtility.GetFeetSlotPos(i, bedPosition, bedRotation, bedDef.size);

                if (cursorPosition == slotHead)
                    return "RimWorldAccess.Map.BuildingCell.Head".Translate();
                if (cursorPosition == slotFoot)
                    return "RimWorldAccess.Map.BuildingCell.Foot".Translate();
            }

            // Cursor is on bed but not exactly on a head or foot cell
            return null;
        }

        /// <summary>
        /// Gets fuel port direction info for a building with a fuel port.
        /// Only returns info when cursor is on the tile directly adjacent to the fuel port.
        /// </summary>
        private static string GetFuelPortInfo(IntVec3 position, Rot4 rotation, IntVec3 cursorPosition)
        {
            // Calculate fuel port cell location using game's utility
            IntVec3 fuelPortCell = FuelingPortUtility.GetFuelingPortCell(position, rotation);

            // Only show fuel port info when cursor is directly adjacent to the fuel port cell
            IntVec3 offsetFromCursor = fuelPortCell - cursorPosition;

            // Check if fuel port is exactly one tile away in a cardinal direction
            bool isAdjacent = (System.Math.Abs(offsetFromCursor.x) + System.Math.Abs(offsetFromCursor.z)) == 1;

            if (isAdjacent)
            {
                string direction = GetCardinalDirection(offsetFromCursor);
                if (!string.IsNullOrEmpty(direction))
                    return "RimWorldAccess.Map.BuildingCell.FuelPort".Translate(direction);
            }

            return null;
        }

        /// <summary>
        /// Checks if a ThingDef is a cooler (temperature control with directional cooling/heating).
        /// Uses type inheritance check for safety with subclasses.
        /// </summary>
        private static bool IsCooler(ThingDef def)
        {
            if (def?.thingClass == null)
                return false;

            // Use IsAssignableFrom to handle subclasses properly
            return typeof(Building_Cooler).IsAssignableFrom(def.thingClass);
        }

        /// <summary>
        /// Gets cooling/heating direction info for a cooler based on its rotation.
        /// Verified against Building_Cooler.TickRare() in game code:
        /// - South side (relative to rotation) is the cooling side
        /// - North side (relative to rotation) is the heating/exhaust side
        /// </summary>
        private static string GetCoolerDirectionInfo(Rot4 rotation)
        {
            // From Building_Cooler.TickRare():
            // IntVec3 intVec = base.Position + IntVec3.South.RotatedBy(base.Rotation);  // cooling side
            // IntVec3 intVec2 = base.Position + IntVec3.North.RotatedBy(base.Rotation); // heating side
            IntVec3 coolingSide = IntVec3.South.RotatedBy(rotation);
            string coolingDir = GetCardinalDirection(coolingSide);

            IntVec3 heatingSide = IntVec3.North.RotatedBy(rotation);
            string heatingDir = GetCardinalDirection(heatingSide);

            return "RimWorldAccess.Map.BuildingCell.Cooler".Translate(coolingDir, heatingDir);
        }

        /// <summary>
        /// Checks if a ThingDef is a vent.
        /// Uses type inheritance check for safety with subclasses.
        /// </summary>
        private static bool IsVent(ThingDef def)
        {
            if (def?.thingClass == null)
                return false;

            // Use IsAssignableFrom to handle subclasses (e.g., Building_AncientVent)
            return typeof(Building_Vent).IsAssignableFrom(def.thingClass);
        }

        /// <summary>
        /// Gets vent direction info - vents connect two rooms on opposite sides.
        /// Verified against GenTemperature.EqualizeTemperaturesThroughBuilding():
        /// When twoWay=true, it uses b.Rotation.FacingCell and -b.Rotation.FacingCell
        /// which corresponds to north and south relative to rotation.
        /// </summary>
        private static string GetVentDirectionInfo(Rot4 rotation)
        {
            // From GenTemperature.EqualizeTemperaturesThroughBuilding():
            // IntVec3 intVec = ((i == 0) ? (item + b.Rotation.FacingCell) : (item - b.Rotation.FacingCell));
            // This means vents connect the facing direction with the opposite direction
            IntVec3 northSide = IntVec3.North.RotatedBy(rotation);
            IntVec3 southSide = IntVec3.South.RotatedBy(rotation);

            string northDir = GetCardinalDirection(northSide);
            string southDir = GetCardinalDirection(southSide);

            return "RimWorldAccess.Map.BuildingCell.Vent".Translate(northDir, southDir);
        }

        /// <summary>
        /// Checks if a ThingDef is a throne.
        /// Uses type inheritance check for safety with subclasses.
        /// </summary>
        private static bool IsThrone(ThingDef def)
        {
            if (def?.thingClass == null)
                return false;

            // Use IsAssignableFrom to handle subclasses
            return typeof(Building_Throne).IsAssignableFrom(def.thingClass);
        }

        /// <summary>
        /// Gets throne direction info - thrones face a specific direction.
        /// Since the main announcement already says "Facing X", no additional info needed.
        /// </summary>
        private static string GetThroneDirectionInfo(Rot4 rotation)
        {
            // Thrones are 1x1 and the main announcement already says "Facing X"
            // No additional directional info needed
            return null;
        }

        /// <summary>
        /// Checks if a ThingDef is a nutrient paste dispenser or similar hopper-using building.
        /// Uses the game's wantsHopperAdjacent property for proper detection.
        /// </summary>
        private static bool IsNutrientPasteDispenser(ThingDef def)
        {
            if (def == null)
                return false;

            // Use wantsHopperAdjacent property - this is how the game identifies hopper-adjacent buildings
            return def.building?.wantsHopperAdjacent == true;
        }

        /// <summary>
        /// Gets nutrient paste dispenser info - where hoppers should be placed.
        /// Hoppers can go on any of the 4 adjacent cardinal cells.
        /// </summary>
        private static string GetDispenserInfo(ThingDef def, Rot4 rotation)
        {
            // The dispenser accepts hoppers on any adjacent cardinal cell (north, south, east, west)
            // Per Building_NutrientPasteDispenser.AdjCellsCardinalInBounds - all 4 sides work
            return "RimWorldAccess.Map.BuildingCell.HopperAdjacent".Translate();
        }

        /// <summary>
        /// Checks if a ThingDef is a turret.
        /// Uses the game's IsTurret property (checks for turretGunDef) for proper detection.
        /// </summary>
        private static bool IsTurret(ThingDef def)
        {
            if (def == null)
                return false;

            // Use the game's IsTurret property which checks if turretGunDef is set
            return def.building?.IsTurret == true;
        }

        /// <summary>
        /// Gets turret direction info - turrets can fire in all directions.
        /// Since they rotate to fire in any direction, no special directional info is needed.
        /// The main announcement already includes "Facing X" which is sufficient.
        /// </summary>
        private static string GetTurretDirectionInfo(Rot4 rotation)
        {
            // Turrets can fire in any direction - no additional directional info needed
            // The main "Facing X" announcement is sufficient
            return null;
        }

        /// <summary>
        /// Checks if a ThingDef is a mech charger.
        /// Uses type inheritance check for safety with subclasses.
        /// </summary>
        private static bool IsMechCharger(ThingDef def)
        {
            if (def?.thingClass == null)
                return false;

            // Use IsAssignableFrom to handle subclasses
            return typeof(Building_MechCharger).IsAssignableFrom(def.thingClass);
        }

        /// <summary>
        /// Gets mech charger info - where the mech stands to charge.
        /// The mech stands at the interaction cell, which is derived from the ThingDef.
        /// </summary>
        private static string GetMechChargerInfo(ThingDef def, Rot4 rotation)
        {
            // Mech chargers use the interaction cell for mech positioning
            // Get the interaction cell offset from the def and rotate it
            if (def.hasInteractionCell)
            {
                IntVec3 offset = def.interactionCellOffset.RotatedBy(rotation);
                string standDir = GetCardinalDirection(offset);
                if (!string.IsNullOrEmpty(standDir))
                    return "RimWorldAccess.Map.BuildingCell.MechCharger".Translate(standDir);
            }
            return null;
        }

        /// <summary>
        /// Checks if a ThingDef is a bookcase.
        /// Uses type inheritance check for safety with subclasses.
        /// </summary>
        private static bool IsBookcase(ThingDef def)
        {
            if (def?.thingClass == null)
                return false;

            // Use IsAssignableFrom to handle subclasses
            return typeof(Building_Bookcase).IsAssignableFrom(def.thingClass);
        }

        /// <summary>
        /// Gets bookcase info - where pawns access books from.
        /// Note: Bookcases don't have a strict interaction cell; pawns access from the facing direction.
        /// </summary>
        private static string GetBookcaseInfo(Rot4 rotation)
        {
            // Bookcases are accessed from the front (facing direction)
            // Books are rendered based on rotation, access is from that side
            string accessDir = GetRotationName(rotation);
            return "RimWorldAccess.Map.BuildingCell.Bookcase".Translate(accessDir);
        }

        /// <summary>
        /// Checks if a ThingDef is a watchable building (TV, etc.) by checking for PlaceWorker_WatchArea.
        /// Only buildings with this PlaceWorker are genuinely watchable for joy purposes.
        /// </summary>
        private static bool IsWatchableBuilding(ThingDef def)
        {
            if (def == null)
                return false;

            // Check if the building has PlaceWorker_WatchArea in its placeWorkers list
            // This is the definitive way to identify buildings that pawns can watch for joy
            var placeWorkers = def.placeWorkers;
            if (placeWorkers != null)
            {
                foreach (var workerType in placeWorkers)
                {
                    if (workerType == typeof(PlaceWorker_WatchArea))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets watch direction info for watchable buildings (TVs, etc.)
        /// </summary>
        private static string GetWatchableInfo(ThingDef def, Rot4 rotation)
        {
            // For rotatable watchable buildings, pawns watch from the facing direction
            if (def.rotatable)
            {
                string direction = GetCardinalDirection(rotation.FacingCell);
                if (!string.IsNullOrEmpty(direction))
                    return "RimWorldAccess.Map.BuildingCell.Watchable".Translate(direction);
            }
            // Non-rotatable watchable buildings can be watched from any direction
            return null;
        }

        /// <summary>
        /// Gets interaction cell info for buildings with interaction cells.
        /// Only shows info when cursor is ON or directly adjacent to the interaction cell.
        /// </summary>
        private static string GetInteractionCellInfo(ThingDef thingDef, IntVec3 position, Rot4 rotation, IntVec3 cursorPosition)
        {
            // Calculate interaction cell using game's utility
            IntVec3 interactionCell = ThingUtility.InteractionCellWhenAt(thingDef, position, rotation, null);

            // Check if cursor is ON the interaction cell
            if (cursorPosition == interactionCell)
            {
                return "RimWorldAccess.Map.BuildingCell.InteractionSpot".Translate();
            }

            // Only show "use from" when cursor is directly adjacent to the interaction cell
            IntVec3 offset = interactionCell - cursorPosition;
            bool isAdjacent = (System.Math.Abs(offset.x) + System.Math.Abs(offset.z)) == 1;

            if (isAdjacent)
            {
                string direction = GetCardinalDirection(offset);
                if (!string.IsNullOrEmpty(direction))
                    return "RimWorldAccess.Map.BuildingCell.UseFrom".Translate(direction);
            }

            return null;
        }

        /// <summary>
        /// Converts an IntVec3 offset to a cardinal direction string.
        /// Handles all 8 directions (cardinals and diagonals).
        /// </summary>
        internal static string GetCardinalDirection(IntVec3 offset)
        {
            // Normalize to unit direction
            if (offset.z > 0 && offset.x == 0) return "RimWorldAccess.Map.Direction.Lower.North".Translate();
            if (offset.z < 0 && offset.x == 0) return "RimWorldAccess.Map.Direction.Lower.South".Translate();
            if (offset.x > 0 && offset.z == 0) return "RimWorldAccess.Map.Direction.Lower.East".Translate();
            if (offset.x < 0 && offset.z == 0) return "RimWorldAccess.Map.Direction.Lower.West".Translate();

            // Handle diagonals
            if (offset.z > 0 && offset.x > 0) return "RimWorldAccess.Map.Direction.Lower.Northeast".Translate();
            if (offset.z > 0 && offset.x < 0) return "RimWorldAccess.Map.Direction.Lower.Northwest".Translate();
            if (offset.z < 0 && offset.x > 0) return "RimWorldAccess.Map.Direction.Lower.Southeast".Translate();
            if (offset.z < 0 && offset.x < 0) return "RimWorldAccess.Map.Direction.Lower.Southwest".Translate();

            return null;
        }

        /// <summary>
        /// Gets special position info for a ThingDef during placement.
        /// Used by ArchitectState for placement announcements.
        /// </summary>
        /// <param name="def">The ThingDef being placed</param>
        /// <param name="rotation">The current rotation</param>
        /// <returns>Special position info string, or null if none</returns>
        public static string GetPlacementPositionInfo(ThingDef def, Rot4 rotation)
        {
            if (def == null)
                return null;

            // Beds - announce head position
            if (def.IsBed)
            {
                return GetBedHeadInfo(def, rotation);
            }

            // Fuel port buildings
            if (def.building?.hasFuelingPort == true)
            {
                return GetFuelPortDirectionInfo(rotation);
            }

            // Coolers
            if (IsCooler(def))
            {
                return GetCoolerDirectionInfo(rotation);
            }

            // Vents
            if (IsVent(def))
            {
                return GetVentDirectionInfo(rotation);
            }

            // Thrones
            if (IsThrone(def))
            {
                return GetThroneDirectionInfo(rotation);
            }

            // Nutrient paste dispensers
            if (IsNutrientPasteDispenser(def))
            {
                return GetDispenserInfo(def, rotation);
            }

            // Turrets
            if (IsTurret(def))
            {
                return GetTurretDirectionInfo(rotation);
            }

            // Mech chargers
            if (IsMechCharger(def))
            {
                return GetMechChargerInfo(def, rotation);
            }

            // Bookcases
            if (IsBookcase(def))
            {
                return GetBookcaseInfo(rotation);
            }

            // Watchable buildings (TVs, etc.) - use game's own property
            if (IsWatchableBuilding(def))
            {
                if (def.rotatable)
                {
                    string direction = GetRotationName(rotation);
                    return "RimWorldAccess.Map.Placement.Watchable".Translate(direction);
                }
            }

            // Multiple interaction cell buildings (school desks, etc.)
            if (!def.multipleInteractionCellOffsets.NullOrEmpty())
            {
                return GetMultiInteractionPlacementInfo(def, rotation);
            }

            // Interaction cell buildings (fallback)
            if (def.hasInteractionCell)
            {
                IntVec3 offset = def.interactionCellOffset.RotatedBy(rotation);
                int distance = System.Math.Abs(offset.x) + System.Math.Abs(offset.z);
                string direction = GetCardinalDirection(offset);

                if (!string.IsNullOrEmpty(direction))
                {
                    // Be specific about distance if more than 1 tile away
                    if (distance == 1)
                        return "RimWorldAccess.Map.Placement.InteractFromAdjacent".Translate(direction);
                    else
                        return "RimWorldAccess.Map.Placement.InteractFromDistant".Translate(distance, direction);
                }
            }

            // Facility linking info (for buildings that provide or receive facility bonuses)
            string facilityInfo = FacilityLinkHelper.GetPlacementInfo(def);
            if (!string.IsNullOrEmpty(facilityInfo))
                return facilityInfo;

            return null;
        }

        /// <summary>
        /// Gets bed head position info for placement.
        /// RimWorld places beds with the head (pillow end) opposite to the facing direction.
        /// For double beds (2x2), there are two head positions side by side along one edge.
        /// Animal sleeping spots don't have meaningful head/foot distinction.
        /// </summary>
        private static string GetBedHeadInfo(ThingDef def, Rot4 rotation)
        {
            // Skip head info for animal beds - they don't have head/foot distinction
            if (def.building?.bed_humanlike == false)
            {
                return null;
            }

            // Head is opposite to facing direction
            // North-facing bed: head on south edge
            // East-facing bed: head on west edge
            // etc.
            Rot4 headDirection = rotation.Opposite;
            string headEdge = GetRotationName(headDirection);

            // Get bed size to determine number of sleeping slots
            IntVec2 size = def.Size;
            int slots = BedUtility.GetSleepingSlotsCount(size);

            if (slots >= 2)
            {
                // Double bed - head is an edge, not a single tile
                return "RimWorldAccess.Map.Placement.BedHeadEdge".Translate(headEdge);
            }

            // Single bed - head is on one side
            return "RimWorldAccess.Map.Placement.BedHeadSide".Translate(headEdge);
        }

        /// <summary>
        /// Gets fuel port direction info for placement.
        /// Calculates the actual fuel port cell position relative to cursor.
        /// </summary>
        private static string GetFuelPortDirectionInfo(Rot4 rotation)
        {
            // Get cursor position and calculate actual fuel port location
            IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;
            IntVec3 fuelPortCell = FuelingPortUtility.GetFuelingPortCell(cursorPosition, rotation);

            // Calculate offset from cursor to fuel port
            IntVec3 offset = fuelPortCell - cursorPosition;
            int distance = System.Math.Abs(offset.x) + System.Math.Abs(offset.z);

            string direction = GetCardinalDirection(offset);
            if (string.IsNullOrEmpty(direction))
                return null;

            // Be specific about distance if more than 1 tile away
            if (distance == 1)
            {
                return "RimWorldAccess.Map.Placement.FuelPortAdjacent".Translate(direction);
            }
            else
            {
                return "RimWorldAccess.Map.Placement.FuelPortDistant".Translate(distance, direction);
            }
        }

        /// <summary>
        /// Gets interaction cell info for buildings with multiple interaction cells.
        /// Handles school desks (student/teacher spots) and other multi-cell buildings generically.
        /// </summary>
        private static string GetMultiInteractionCellInfo(ThingDef thingDef, IntVec3 position, Rot4 rotation, IntVec3 cursorPosition)
        {
            var cells = ThingUtility.InteractionCellsWhenAt(thingDef, position, rotation, null);

            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                string spotLabel = GetInteractionSpotLabel(thingDef, i);

                if (cursorPosition == cell)
                {
                    return spotLabel;
                }

                IntVec3 offset = cell - cursorPosition;
                bool isAdjacent = (System.Math.Abs(offset.x) + System.Math.Abs(offset.z)) == 1;
                if (isAdjacent)
                {
                    string direction = GetCardinalDirection(offset);
                    if (!string.IsNullOrEmpty(direction))
                        return "RimWorldAccess.Map.BuildingCell.SpotWithDirection".Translate(spotLabel, direction);
                }
            }

            return null;
        }

        /// <summary>
        /// Gets placement info for buildings with multiple interaction cells.
        /// Announces the direction of each interaction spot relative to the building.
        /// </summary>
        private static string GetMultiInteractionPlacementInfo(ThingDef def, Rot4 rotation)
        {
            var parts = new List<string>();

            for (int i = 0; i < def.multipleInteractionCellOffsets.Count; i++)
            {
                IntVec3 offset = def.multipleInteractionCellOffsets[i].RotatedBy(rotation);
                string direction = GetCardinalDirection(offset);
                string label = GetInteractionSpotLabel(def, i);

                if (!string.IsNullOrEmpty(direction))
                {
                    parts.Add("RimWorldAccess.Map.BuildingCell.SpotWithDirection".Translate(label, direction));
                }
            }

            if (parts.Count > 0)
                return string.Join(", ", parts);

            return null;
        }

        /// <summary>
        /// Gets a human-readable label for a specific interaction cell index.
        /// School desks have known spots: index 0 = student, index 1 = teacher.
        /// Other buildings get generic numbered labels.
        /// </summary>
        private static string GetInteractionSpotLabel(ThingDef thingDef, int index)
        {
            if (thingDef == ThingDefOf.SchoolDesk)
            {
                if (index == 0) return "RimWorldAccess.Map.BuildingCell.StudentSpot".Translate();
                if (index == 1) return "RimWorldAccess.Map.BuildingCell.TeacherSpot".Translate();
            }

            if (thingDef.multipleInteractionCellOffsets != null && thingDef.multipleInteractionCellOffsets.Count == 1)
                return "RimWorldAccess.Map.BuildingCell.InteractionSpot".Translate();

            return "RimWorldAccess.Map.BuildingCell.NumberedSpot".Translate(index + 1);
        }

        /// <summary>
        /// Gets a human-readable name for a rotation (lowercase).
        /// Delegates to ArchitectState for shared implementation.
        /// </summary>
        private static string GetRotationName(Rot4 rotation)
        {
            return ArchitectState.GetRotationName(rotation).ToLower();
        }
    }
}
