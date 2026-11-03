using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Spoken animal pen status: vanilla's own pen marker readout.</summary>
    internal static class AnimalPenInfo
    {
        /// <summary>The pen whose connected regions hold <paramref name="cell"/>, preferring an enclosed one.</summary>
        public static CompAnimalPenMarker FindPenAt(IntVec3 cell, Map map)
        {
            Region region = cell.GetRegion(map);
            if (region == null)
            {
                return null;
            }
            CompAnimalPenMarker unenclosed = null;
            foreach (Building building in map.listerBuildings.allBuildingsAnimalPenMarkers)
            {
                CompAnimalPenMarker marker = building.TryGetComp<CompAnimalPenMarker>();
                PenMarkerState state = marker?.PenState;
                if (state == null || !state.ContainsConnectedRegion(region))
                {
                    continue;
                }
                if (state.Enclosed)
                {
                    return marker;
                }
                if (unenclosed == null)
                {
                    unenclosed = marker;
                }
            }
            return unenclosed;
        }

        /// <summary>CompAnimalPenMarker.CompInspectStringExtra as one announcement.</summary>
        public static string Describe(CompAnimalPenMarker marker)
        {
            string inspect = marker.CompInspectStringExtra();
            return string.IsNullOrEmpty(inspect) ? null : SpeechFlatten.ToSentences(inspect.StripTags());
        }
    }
}
