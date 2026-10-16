using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public struct MeditationProtectionResult
    {
        public bool IsProtected;
        public List<string> AffectedThingLabels;
    }

    public static class MeditationProtectionHelper
    {
        public static bool IsArtificialBuilding(ThingDef def, Faction faction)
        {
            if (def == null || faction == null)
                return false;
            return MeditationUtility.CountsAsArtificialBuilding(def, faction);
        }

        public static (ThingDef def, Rot4 rotation) GetPlacementInfo(Designator designator)
        {
            if (designator is Designator_Place placeDesignator &&
                placeDesignator.PlacingDef is ThingDef thingDef)
            {
                Rot4 rotation = BuildingReflection.GetPlacingRot(placeDesignator);
                return (thingDef, rotation);
            }
            return (null, Rot4.North);
        }

        public static MeditationProtectionResult CheckProtection(
            Map map, ThingDef def, Faction faction, IntVec3 pos, Rot4 rotation)
        {
            var result = new MeditationProtectionResult
            {
                IsProtected = false,
                AffectedThingLabels = new List<string>()
            };

            if (map == null || def == null || faction == null)
                return result;

            IEnumerable<Thing> affected = MeditationUtility
                .GetMeditationFociAffectedByBuilding(map, def, faction, pos, rotation)
                .Concat(GauranlenUtility.GetConnectionsAffectedByBuilding(map, def, faction, pos, rotation));
            foreach (Thing thing in affected)
            {
                if (IsToggleEnabled(thing))
                {
                    result.IsProtected = true;
                    string label = thing.LabelShort.NullOrEmpty()
                        ? "RimWorldAccess.Building.Meditation.FocusFallback".Translate().ToString()
                        : thing.LabelShort;
                    if (!result.AffectedThingLabels.Contains(label))
                    {
                        result.AffectedThingLabels.Add(label);
                    }
                }
            }

            return result;
        }

        private static bool IsToggleEnabled(Thing thing)
        {
            var comp = thing.TryGetComp<CompToggleDrawAffectedMeditationFoci>();
            return comp != null && comp.Enabled;
        }

        public static string FormatManualBlockMessage(MeditationProtectionResult result)
        {
            if (!result.IsProtected || result.AffectedThingLabels.Count == 0)
                return string.Empty;

            string thingList = string.Join(", ", result.AffectedThingLabels);
            return "RimWorldAccess.Building.Place.MeditationProtectedBy".Translate(thingList, DisableHint());
        }

        public static string DisableHint()
        {
            return "RimWorldAccess.Building.Place.WarnToggleDisableHint".Translate("CommandWarnInBuildingRadius".Translate());
        }

        public static string FormatShapeSummary(int protectedCount, HashSet<string> allAffectedLabels)
        {
            if (protectedCount <= 0)
                return string.Empty;

            string thingList = string.Join(", ", allAffectedLabels);
            return (string)"RimWorldAccess.Building.Meditation.SkippedProtected".Translate(protectedCount, thingList);
        }
    }
}
