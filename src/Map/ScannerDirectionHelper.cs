using System;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Compass-direction math shared by the map and world scanners and the world cursor.</summary>
    public static class ScannerDirectionHelper
    {
        // Ordered clockwise from North, one entry per 45-degree bucket. Bucket index 0 = North.
        // Kept as the single source of truth for compass-label ordering so GetCompassDirection(IntVec3, IntVec3)
        // and GetCompassDirection(double) can never drift apart.
        private static readonly string[] CompassKeys =
        {
            "RimWorldAccess.Map.Direction.North",
            "RimWorldAccess.Map.Direction.Northeast",
            "RimWorldAccess.Map.Direction.East",
            "RimWorldAccess.Map.Direction.Southeast",
            "RimWorldAccess.Map.Direction.South",
            "RimWorldAccess.Map.Direction.Southwest",
            "RimWorldAccess.Map.Direction.West",
            "RimWorldAccess.Map.Direction.Northwest",
        };

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
            return GetCompassDirection(NormalizeAngleDegrees(angle));
        }

        /// <summary>
        /// Translates an already-normalized [0, 360) compass angle (0 = north, 90 = east) into
        /// one of the 8 compass-direction translation strings. Shared bucket boundaries: an
        /// exact 22.5-multiple angle rounds into the next-clockwise bucket (22.5 is already
        /// Northeast, not North; 337.5 is still North).
        /// </summary>
        public static string GetCompassDirection(double angleDegrees)
        {
            return CompassKeys[GetCompassBucketIndex(angleDegrees)].Translate();
        }

        /// <summary>
        /// 0-based compass-bucket index (0 = North, 1 = Northeast, ... 7 = Northwest) for an
        /// already-normalized [0, 360) angle. Shared by callers that need the same 8 sectors
        /// mapped to a different label set (e.g. the world scanner's pole-relative
        /// Ahead/Right/Behind/Left labels instead of compass labels).
        /// </summary>
        public static int GetCompassBucketIndex(double angleDegrees)
        {
            if (angleDegrees >= 337.5 || angleDegrees < 22.5) return 0;
            if (angleDegrees >= 22.5 && angleDegrees < 67.5) return 1;
            if (angleDegrees >= 67.5 && angleDegrees < 112.5) return 2;
            if (angleDegrees >= 112.5 && angleDegrees < 157.5) return 3;
            if (angleDegrees >= 157.5 && angleDegrees < 202.5) return 4;
            if (angleDegrees >= 202.5 && angleDegrees < 247.5) return 5;
            if (angleDegrees >= 247.5 && angleDegrees < 292.5) return 6;
            return 7;
        }

        /// <summary>
        /// Compass angle (0 = north, 90 = east) of <paramref name="direction"/> as seen from a
        /// point on the world sphere whose surface position is <paramref name="fromPos"/>. Builds
        /// a local north/east tangent-plane basis from the sphere's "up" (the position vector
        /// itself), then projects <paramref name="direction"/> onto it. This is the world-map
        /// equivalent of the flat IntVec3 offset angle above (world tiles vs map cells), so it
        /// gets its own entry point rather than being folded into the IntVec3 overload.
        /// </summary>
        public static double GetSphericalAngleDegrees(Vector3 fromPos, Vector3 direction)
        {
            TangentBasis(fromPos, out Vector3 north, out Vector3 east);
            Vector3 flatDir = Vector3.ProjectOnPlane(direction, fromPos.normalized).normalized;

            float dotNorth = Vector3.Dot(flatDir, north);
            float dotEast = Vector3.Dot(flatDir, east);
            double angle = Math.Atan2(dotEast, dotNorth) * (180.0 / Math.PI);
            return NormalizeAngleDegrees(angle);
        }

        /// <summary>Local north and east on the world sphere's tangent plane at <paramref name="surfacePos"/>.</summary>
        public static void TangentBasis(Vector3 surfacePos, out Vector3 north, out Vector3 east)
        {
            Vector3 up = surfacePos.normalized;
            north = Vector3.ProjectOnPlane(Vector3.up, up).normalized;
            east = Vector3.Cross(up, north).normalized;
        }

        private static double NormalizeAngleDegrees(double angleDegrees)
        {
            if (angleDegrees < 0) angleDegrees += 360;
            return angleDegrees;
        }
    }
}
