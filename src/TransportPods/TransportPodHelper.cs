using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper utilities for transport pod accessibility.
    /// Provides pod detection, grouping, fuel calculations, and announcement formatting.
    /// </summary>
    public static class TransportPodHelper
    {
        // Cached reflection fields
        private static readonly System.Reflection.FieldInfo groupIDField =
            AccessTools.Field(typeof(CompTransporter), "groupID");

        /// <summary>
        /// Gets all transport pods at the specified map position.
        /// </summary>
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

        /// <summary>
        /// Gets all transport pods on the map that are not currently loading or loaded.
        /// </summary>
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

        /// <summary>
        /// Gets the CompLaunchable for a transport pod, if it has one.
        /// </summary>
        public static CompLaunchable GetLaunchable(CompTransporter transporter)
        {
            return transporter?.parent?.TryGetComp<CompLaunchable>();
        }

        /// <summary>
        /// Gets all available pods that could be grouped with the source pod.
        /// Uses the game's flood fill logic: launchers must be connected through adjacent fueling port givers.
        /// This matches Command_LoadToTransporter.ProcessInput behavior.
        /// </summary>
        public static List<CompTransporter> GetGroupablePodsFor(CompTransporter source, Map map)
        {
            var result = new List<CompTransporter>();
            if (source?.parent == null || map == null)
                return result;

            // Get the source's fueling port giver (the launcher building)
            var sourceLaunchable = source.Launchable as CompLaunchable_TransportPod;
            if (sourceLaunchable?.FuelingPortSource?.parent == null)
                return result;

            Building sourceFuelingPortGiver = sourceLaunchable.FuelingPortSource.parent as Building;
            if (sourceFuelingPortGiver == null)
                return result;

            // Use flood fill to find all connected fueling port givers
            // This matches the game's logic in Command_LoadToTransporter.ProcessInput
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

            // Find other transporters whose fueling port givers are in the connected set
            foreach (var other in GetAllAvailablePods(map))
            {
                if (other == source)
                    continue;

                var otherLaunchable = other.Launchable as CompLaunchable_TransportPod;
                if (otherLaunchable?.FuelingPortSource?.parent == null)
                    continue;

                if (connectedFuelingPortGivers.Contains(otherLaunchable.FuelingPortSource.parent))
                {
                    result.Add(other);
                }
            }

            return result;
        }

        /// <summary>
        /// Checks if a transporter is connected to a fueled launcher.
        /// </summary>
        public static bool IsConnectedToFuel(CompTransporter transporter)
        {
            var launchable = GetLaunchable(transporter);
            if (launchable == null)
                return false;

            // Check if it's a transport pod launchable with fuel connection
            if (launchable is CompLaunchable_TransportPod podLaunchable)
            {
                return podLaunchable.ConnectedToFuelingPort;
            }

            return false;
        }

        /// <summary>
        /// Gets the current fuel level for a transport pod.
        /// Uses the public FuelLevel property on CompLaunchable.
        /// </summary>
        public static float GetFuelLevel(CompLaunchable launchable)
        {
            if (launchable == null)
                return 0f;

            // FuelLevel is a public virtual property on CompLaunchable
            // For transport pods (CompLaunchable_TransportPod), it returns the fuel from the connected launcher
            // For shuttles that don't require fueling ports, it returns PositiveInfinity
            return launchable.FuelLevel;
        }

        /// <summary>
        /// Calculates the fuel needed to launch to a destination at the given distance.
        /// </summary>
        public static float CalculateFuelCost(CompLaunchable launchable, float distanceInTiles)
        {
            if (launchable == null)
                return float.MaxValue;

            var props = launchable.Props;
            if (props == null)
                return float.MaxValue;

            return distanceInTiles * props.fuelPerTile;
        }

        /// <summary>
        /// Gets the maximum launch distance at current fuel level.
        /// </summary>
        public static float GetMaxLaunchDistance(CompLaunchable launchable)
        {
            if (launchable == null)
                return 0f;

            float fuel = GetFuelLevel(launchable);
            return launchable.MaxLaunchDistanceAtFuelLevel(fuel);
        }

        /// <summary>
        /// Checks if a destination is within launch range.
        /// </summary>
        public static bool CanReachDestination(CompLaunchable launchable, float distanceInTiles)
        {
            float maxRange = GetMaxLaunchDistance(launchable);
            return distanceInTiles <= maxRange;
        }

        /// <summary>
        /// Gets the group ID for a transporter (uses reflection).
        /// </summary>
        public static int GetGroupID(CompTransporter transporter)
        {
            if (transporter == null || groupIDField == null)
                return -1;

            return (int)groupIDField.GetValue(transporter);
        }

        /// <summary>
        /// Sets the group ID for a transporter (uses reflection).
        /// </summary>
        public static void SetGroupID(CompTransporter transporter, int groupID)
        {
            if (transporter == null || groupIDField == null)
                return;

            groupIDField.SetValue(transporter, groupID);
        }

        /// <summary>
        /// Generates a new unique group ID for pod grouping.
        /// </summary>
        public static int GenerateNewGroupID()
        {
            return Find.UniqueIDsManager.GetNextTransporterGroupID();
        }

        /// <summary>
        /// Groups the specified transporters together with a new group ID.
        /// </summary>
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

        /// <summary>
        /// Gets the total mass capacity of a group of transporters.
        /// </summary>
        public static float GetTotalMassCapacity(List<CompTransporter> transporters)
        {
            if (transporters == null)
                return 0f;

            return transporters.Sum(t => t.Props.massCapacity);
        }

        /// <summary>
        /// Builds an announcement string for a transport pod.
        /// </summary>
        public static string BuildPodAnnouncement(CompTransporter transporter, int index, int total, bool isSelected)
        {
            var parts = new List<string>();

            // Pod identification
            string podName = transporter.parent.LabelShort ?? ((string)"RimWorldAccess.TransportPods.Pod.TypeTransportPod".Translate());
            parts.Add(podName);

            // Selection status
            if (isSelected)
            {
                parts.Add((string)"RimWorldAccess.TransportPods.Pod.Selected".Translate());
            }

            // Fuel connection status
            if (IsConnectedToFuel(transporter))
            {
                var launchable = GetLaunchable(transporter);
                float fuel = GetFuelLevel(launchable);
                parts.Add((string)"RimWorldAccess.TransportPods.Pod.FuelLevel".Translate(fuel.ToString("F0")));
            }
            else
            {
                parts.Add((string)"RimWorldAccess.TransportPods.Pod.NotConnectedToFuel".Translate());
            }

            // Mass capacity
            parts.Add((string)"RimWorldAccess.TransportPods.Pod.Capacity".Translate(transporter.Props.massCapacity.ToString("F0")));

            // Position in list
            if (total > 1)
            {
                parts.Add((string)"RimWorldAccess.TransportPods.Pod.PositionInList".Translate(index + 1, total));
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Builds an announcement for fuel cost to a destination.
        /// </summary>
        public static string BuildFuelCostAnnouncement(CompLaunchable launchable, float distanceInTiles)
        {
            if (launchable == null)
                return (string)"RimWorldAccess.TransportPods.Fuel.NoLauncher".Translate();

            float fuelCost = CalculateFuelCost(launchable, distanceInTiles);

            // Check for invalid fuel cost (happens if Props is null or calculation fails)
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

        /// <summary>
        /// Checks if this is a shuttle (Royalty DLC) rather than a transport pod.
        /// </summary>
        public static bool IsShuttle(CompTransporter transporter)
        {
            if (transporter?.parent == null)
                return false;

            return transporter.parent.TryGetComp<CompShuttle>() != null;
        }

        /// <summary>
        /// Gets a descriptive label for the pod type (transport pod or shuttle).
        /// </summary>
        public static string GetPodTypeLabel(CompTransporter transporter)
        {
            string key = IsShuttle(transporter)
                ? "RimWorldAccess.TransportPods.Pod.TypeShuttle"
                : "RimWorldAccess.TransportPods.Pod.TypeTransportPod";
            return (string)key.Translate();
        }
    }
}
