using System;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Centralized compass-direction math for the scanner. Consolidates the angle-to-compass
    /// formula that was previously duplicated between ScannerItem.GetDirectionFrom and
    /// ScannerState.GetDirectionFromCursor.
    /// </summary>
    public static class ScannerDirectionHelper
    {
        /// <summary>
        /// Returns the 8-direction compass direction from `from` to `to`, or null if the
        /// two positions are within 0.5 tiles of each other (i.e., "here").
        /// </summary>
        public static string GetCompassDirection(IntVec3 from, IntVec3 to)
        {
            IntVec3 offset = to - from;

            if (offset.LengthHorizontal < 0.5f)
                return null; // Same position / "here"

            // Calculate angle in degrees (0 = north, 90 = east)
            double angle = Math.Atan2(offset.x, offset.z) * (180.0 / Math.PI);
            if (angle < 0) angle += 360;

            if (angle >= 337.5 || angle < 22.5) return "RimWorldAccess.Map.Direction.North".Translate();
            if (angle >= 22.5 && angle < 67.5) return "RimWorldAccess.Map.Direction.Northeast".Translate();
            if (angle >= 67.5 && angle < 112.5) return "RimWorldAccess.Map.Direction.East".Translate();
            if (angle >= 112.5 && angle < 157.5) return "RimWorldAccess.Map.Direction.Southeast".Translate();
            if (angle >= 157.5 && angle < 202.5) return "RimWorldAccess.Map.Direction.South".Translate();
            if (angle >= 202.5 && angle < 247.5) return "RimWorldAccess.Map.Direction.Southwest".Translate();
            if (angle >= 247.5 && angle < 292.5) return "RimWorldAccess.Map.Direction.West".Translate();
            return "RimWorldAccess.Map.Direction.Northwest".Translate();
        }
    }
}
