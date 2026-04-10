using System;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Centralized label formatting for scanner items. Consolidates label logic that was
    /// previously duplicated across ScannerItem constructors and RefreshLabel.
    /// </summary>
    public static class ScannerLabelBuilder
    {
        /// <summary>
        /// Builds a scanner label for a Thing, dispatching by type (Pawn/Door/Minified/other)
        /// and unwrapping the MinifiedThing wrapper when applicable so blind users hear the
        /// inner thing's name (e.g., "monument marker") instead of the generic "minified thing".
        /// </summary>
        public static string BuildThingLabel(Thing thing)
        {
            if (thing == null) return "Unknown";

            if (thing is Pawn pawn)
            {
                return pawn.LabelShort + TileInfoHelper.GetPawnSuffix(pawn);
            }

            try
            {
                string label = GetNoParenthesisLabel(thing) ?? thing.def.label ?? "Unknown";

                if (thing is Building_Door door)
                {
                    label += door.Open ? " (open)" : " (closed)";
                }

                return label;
            }
            catch (Exception)
            {
                // Handle corrupted things (e.g., Blueprint_Install with missing minified item)
                return thing.def?.label ?? "Corrupted object";
            }
        }

        /// <summary>
        /// Builds a scanner label for a Designation, formatted as "{target} ({designation type})".
        /// Uses the thing's LabelNoParenthesis when the designation targets a thing, otherwise
        /// the edifice/terrain at the target cell, with the current map for resolution.
        /// </summary>
        public static string BuildDesignationLabel(Designation designation, Map map)
        {
            if (designation == null) return "Unknown";

            string defLabel = ScannerHelper.GetLocalizedDesignationLabel(designation.def);

            if (designation.target.HasThing && designation.target.Thing != null)
            {
                try
                {
                    return $"{designation.target.Thing.LabelNoParenthesis} ({defLabel})";
                }
                catch (Exception)
                {
                    return $"{designation.target.Thing.def?.label ?? "Corrupted object"} ({defLabel})";
                }
            }

            if (map == null)
                return defLabel;

            IntVec3 cell = designation.target.Cell;
            var edifice = cell.GetEdifice(map);
            if (edifice != null)
            {
                try
                {
                    return $"{edifice.LabelNoParenthesis} ({defLabel})";
                }
                catch (Exception)
                {
                    return $"{edifice.def?.label ?? "Corrupted object"} ({defLabel})";
                }
            }

            var terrain = cell.GetTerrain(map);
            return terrain != null
                ? $"{terrain.LabelCap} ({defLabel})"
                : defLabel;
        }

        /// <summary>
        /// Builds a scanner label for a Zone. Growing zones get a suffix with the plant to grow.
        /// </summary>
        public static string BuildZoneLabel(Zone zone)
        {
            if (zone == null) return "Unknown";

            return zone is Zone_Growing growZone && growZone.PlantDefToGrow != null
                ? $"{zone.label} ({growZone.PlantDefToGrow.label})"
                : zone.label;
        }

        /// <summary>
        /// Builds a scanner label for a Room — uses its role (e.g., "Bedroom", "Dining room").
        /// </summary>
        public static string BuildRoomLabel(Room room)
        {
            if (room == null) return "Unknown";
            return room.GetRoomRoleLabel();
        }

        /// <summary>
        /// Returns the thing's LabelNoParenthesis, unwrapping MinifiedThing to expose the inner
        /// thing's name. MinifiedThing does not override LabelNoParenthesis, so without this
        /// unwrap it returns the generic "minified thing" from its own def instead of e.g.
        /// "monument marker" or "double bed".
        /// </summary>
        public static string GetNoParenthesisLabel(Thing thing)
        {
            if (thing is MinifiedThing minified && minified.InnerThing != null)
            {
                return minified.InnerThing.LabelNoParenthesis ?? minified.InnerThing.def.label;
            }
            return thing.LabelNoParenthesis;
        }
    }
}
