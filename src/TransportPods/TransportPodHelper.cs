using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimWorldAccess
{
    /// <summary>Transport pod detection, grouping, fuel calculations and announcement formatting.</summary>
    public static class TransportPodHelper
    {
        private static readonly System.Reflection.FieldInfo groupIDField =
            AccessTools.Field(typeof(CompTransporter), "groupID");

        /// <summary>Every transport pod at a map position.</summary>
        public static List<CompTransporter> GetTransportPodsAt(IntVec3 position, Map map)
        {
            var result = new List<CompTransporter>();
            if (map == null || !position.InBounds(map))
                return result;

            foreach (Thing thing in position.GetThingList(map))
            {
                var transporter = thing.TryGetComp<CompTransporter>();
                if (transporter != null)
                {
                    result.Add(transporter);
                }
            }

            return result;
        }

        /// <summary>Every pod on the map that is neither loading nor loaded.</summary>
        public static List<CompTransporter> GetAllAvailablePods(Map map)
        {
            var result = new List<CompTransporter>();
            if (map == null)
                return result;

            foreach (Building building in map.listerBuildings.allBuildingsColonist)
            {
                var transporter = building.TryGetComp<CompTransporter>();
                if (transporter != null && !transporter.LoadingInProgressOrReadyToLaunch)
                {
                    result.Add(transporter);
                }
            }

            return result;
        }

        /// <summary>The pod's CompLaunchable, if it has one.</summary>
        public static CompLaunchable GetLaunchable(CompTransporter transporter)
        {
            return transporter?.parent?.TryGetComp<CompLaunchable>();
        }

        /// <summary>
        /// Every pod that could be grouped with the source, by the game's own flood fill through
        /// adjacent fueling port givers. Applies the gates Command_LoadToTransporter applies before it
        /// will merge two pods — InheritInteractionsFrom's max1PerGroup and same-def gate, and
        /// ProcessInput's mutual reachability — without which the candidate list offers pods vanilla
        /// would refuse to load.
        /// </summary>
        public static List<CompTransporter> GetGroupablePodsFor(CompTransporter source, Map map)
        {
            var result = new List<CompTransporter>();
            if (source?.parent == null || map == null)
                return result;

            // A max1PerGroup transporter never inherits another pod's gizmo at all.
            if (source.Props != null && source.Props.max1PerGroup)
                return result;

            var sourceLaunchable = source.Launchable as CompLaunchable_TransportPod;
            if (sourceLaunchable?.FuelingPortSource?.parent == null)
                return result;

            Building sourceFuelingPortGiver = sourceLaunchable.FuelingPortSource.parent as Building;
            if (sourceFuelingPortGiver == null)
                return result;

            // The same flood fill Command_LoadToTransporter.ProcessInput runs.
            var connectedFuelingPortGivers = new HashSet<Building>();
            map.floodFiller.FloodFill(
                sourceFuelingPortGiver.Position,
                (IntVec3 cell) => FuelingPortUtility.AnyFuelingPortGiverAt(cell, map),
                delegate(IntVec3 cell)
                {
                    Building giver = FuelingPortUtility.FuelingPortGiverAt(cell, map);
                    if (giver != null)
                        connectedFuelingPortGivers.Add(giver);
                }
            );

            // Restricted to the same def and to mutual reachability, matching the two vanilla gates.
            foreach (var other in GetAllAvailablePods(map))
            {
                if (other == source)
                    continue;

                if (other.parent?.def != source.parent.def)
                    continue;

                var otherLaunchable = other.Launchable as CompLaunchable_TransportPod;
                if (otherLaunchable?.FuelingPortSource?.parent == null)
                    continue;

                if (!connectedFuelingPortGivers.Contains(otherLaunchable.FuelingPortSource.parent))
                    continue;

                if (!map.reachability.CanReach(source.parent.Position, other.parent, PathEndMode.Touch, TraverseParms.For(TraverseMode.PassDoors)))
                    continue;

                result.Add(other);
            }

            return result;
        }

        /// <summary>
        /// True only when this launchable draws fuel from an adjacent fueling-port building and is
        /// not next to one. The fueling-port concept belongs to CompLaunchable_TransportPod alone; a
        /// plain CompLaunchable, a shuttle's, carries its own tank and is never "disconnected".
        /// </summary>
        public static bool IsDisconnectedFromFuelingPort(CompLaunchable launchable)
        {
            var podLaunchable = launchable as CompLaunchable_TransportPod;
            return podLaunchable != null && !podLaunchable.ConnectedToFuelingPort;
        }

        /// <summary>The current fuel level, from CompLaunchable's own FuelLevel.</summary>
        public static float GetFuelLevel(CompLaunchable launchable)
        {
            if (launchable == null)
                return 0f;

            // The base CompLaunchable dereferences Refuelable unconditionally, so a launchable whose
            // parent carries no CompRefuelable would throw. The pod override guards itself.
            if (!(launchable is CompLaunchable_TransportPod) && launchable.Refuelable == null)
                return 0f;

            return launchable.FuelLevel;
        }

        private static readonly System.Reflection.PropertyInfo minFuelLevelInGroupProperty =
            HarmonyLib.AccessTools.Property(typeof(CompLaunchable), "MinFuelLevelInGroup");

        /// <summary>The lowest fuel level in the launch group, which bounds the whole group's range in vanilla's launch gate.</summary>
        public static float GetGroupFuelLevel(CompLaunchable launchable)
        {
            if (launchable == null)
                return 0f;
            if (!(launchable is CompLaunchable_TransportPod) && launchable.Refuelable == null)
                return 0f;
            if (minFuelLevelInGroupProperty != null)
                return (float)minFuelLevelInGroupProperty.GetValue(launchable);
            return launchable.FuelLevel;
        }

        /// <summary>The fuel needed to launch a given distance.</summary>
        public static float CalculateFuelCost(CompLaunchable launchable, float distanceInTiles)
        {
            if (launchable == null)
                return float.MaxValue;

            var props = launchable.Props;
            if (props == null)
                return float.MaxValue;

            return distanceInTiles * props.fuelPerTile;
        }

        /// <summary>The maximum launch distance at the current fuel level.</summary>
        public static float GetMaxLaunchDistance(CompLaunchable launchable)
        {
            if (launchable == null)
                return 0f;

            float fuel = GetFuelLevel(launchable);
            return launchable.MaxLaunchDistanceAtFuelLevel(fuel);
        }

        /// <summary>Whether a destination is within launch range.</summary>
        public static bool CanReachDestination(CompLaunchable launchable, float distanceInTiles)
        {
            float maxRange = GetMaxLaunchDistance(launchable);
            return distanceInTiles <= maxRange;
        }

        /// <summary>The transporter's group ID.</summary>
        public static int GetGroupID(CompTransporter transporter)
        {
            if (transporter == null || groupIDField == null)
                return -1;

            return (int)groupIDField.GetValue(transporter);
        }

        /// <summary>Sets the transporter's group ID.</summary>
        public static void SetGroupID(CompTransporter transporter, int groupID)
        {
            if (transporter == null || groupIDField == null)
                return;

            groupIDField.SetValue(transporter, groupID);
        }

        /// <summary>A new unique group ID.</summary>
        public static int GenerateNewGroupID()
        {
            return Find.UniqueIDsManager.GetNextTransporterGroupID();
        }

        /// <summary>Groups the transporters under a new group ID.</summary>
        public static void GroupTransporters(List<CompTransporter> transporters)
        {
            if (transporters == null || transporters.Count == 0)
                return;

            int newGroupID = GenerateNewGroupID();
            foreach (var transporter in transporters)
            {
                SetGroupID(transporter, newGroupID);
            }
        }

        /// <summary>The total mass capacity of a group.</summary>
        public static float GetTotalMassCapacity(List<CompTransporter> transporters)
        {
            if (transporters == null)
                return 0f;

            return transporters.Sum(t => t.Props.massCapacity);
        }

        /// <summary>The announcement for one transport pod row.</summary>
        public static string BuildPodAnnouncement(CompTransporter transporter, int index, int total, bool isSelected)
        {
            var parts = new List<string>();

            string podName = transporter.parent.LabelShort ?? ((string)"RimWorldAccess.TransportPods.Pod.TypeTransportPod".Translate());
            parts.Add(podName);

            if (isSelected)
            {
                parts.Add((string)"RimWorldAccess.TransportPods.Pod.Selected".Translate());
            }

            // A pod away from its fueling port has no fuel to report; a shuttle reads its own tank.
            // An object with no launchable or no refuelable has no fuel concept at all, so it gets no
            // fuel line rather than a misleading one.
            CompLaunchable launchable = GetLaunchable(transporter);
            if (IsDisconnectedFromFuelingPort(launchable))
            {
                parts.Add((string)"RimWorldAccess.TransportPods.Pod.NotConnectedToFuel".Translate());
            }
            else if (launchable != null && launchable.Refuelable != null)
            {
                float fuel = GetFuelLevel(launchable);
                parts.Add((string)"RimWorldAccess.TransportPods.Pod.FuelLevel".Translate(fuel.ToString("F0")));
            }

            parts.Add((string)"RimWorldAccess.TransportPods.Pod.Capacity".Translate(transporter.Props.massCapacity.ToString("F0")));

            if (total > 1)
            {
                parts.Add((string)"RimWorldAccess.TransportPods.Pod.PositionInList".Translate(index + 1, total));
            }

            return string.Join(", ", parts);
        }

        /// <summary>The fuel-cost announcement for a destination.</summary>
        public static string BuildFuelCostAnnouncement(CompLaunchable launchable, float distanceInTiles)
        {
            if (launchable == null)
                return (string)"RimWorldAccess.TransportPods.Fuel.NoLauncher".Translate();

            float fuelCost = CalculateFuelCost(launchable, distanceInTiles);

            if (fuelCost >= float.MaxValue || fuelCost < 0)
            {
                return (string)"RimWorldAccess.TransportPods.Fuel.CostUnavailable".Translate();
            }

            float availableFuel = GetFuelLevel(launchable);

            if (fuelCost > availableFuel)
            {
                return (string)"RimWorldAccess.TransportPods.Fuel.NotEnough".Translate(fuelCost.ToString("F0"), availableFuel.ToString("F0"));
            }

            return (string)"RimWorldAccess.TransportPods.Fuel.CostChemfuel".Translate(fuelCost.ToString("F0"));
        }

        /// <summary>Whether this is a shuttle rather than a transport pod.</summary>
        public static bool IsShuttle(CompTransporter transporter)
        {
            if (transporter?.parent == null)
                return false;

            return transporter.parent.TryGetComp<CompShuttle>() != null;
        }

        /// <summary>The pod type's label: transport pod or shuttle.</summary>
        public static string GetPodTypeLabel(CompTransporter transporter)
        {
            string key = IsShuttle(transporter)
                ? "RimWorldAccess.TransportPods.Pod.TypeShuttle"
                : "RimWorldAccess.TransportPods.Pod.TypeTransportPod";
            return (string)key.Translate();
        }
    }
}
