using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
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
        private static readonly FieldInfo placingRotField =
            AccessTools.Field(typeof(Designator_Place), "placingRot");

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
                Rot4 rotation = Rot4.North;
                if (placingRotField != null)
                {
                    rotation = (Rot4)placingRotField.GetValue(placeDesignator);
                }
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

            foreach (Thing focus in MeditationUtility
                .GetMeditationFociAffectedByBuilding(map, def, faction, pos, rotation))
            {
                if (IsToggleEnabled(focus))
                {
                    result.IsProtected = true;
                    string label = focus.LabelShort ?? focus.def?.label ?? "meditation focus";
                    if (!result.AffectedThingLabels.Contains(label))
                    {
                        result.AffectedThingLabels.Add(label);
                    }
                }
            }

            foreach (Thing tree in GauranlenUtility
                .GetConnectionsAffectedByBuilding(map, def, faction, pos, rotation))
            {
                if (IsToggleEnabled(tree))
                {
                    result.IsProtected = true;
                    string label = tree.LabelShort ?? tree.def?.label ?? "tree";
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
            return $"Protected by {thingList}. Disable warning toggle on the tree to build here.";
        }

        public static string FormatShapeSummary(int protectedCount, HashSet<string> allAffectedLabels)
        {
            if (protectedCount <= 0)
                return string.Empty;

            string thingList = string.Join(", ", allAffectedLabels);
            return $"{protectedCount} skipped, protected by {thingList}";
        }
    }
}
