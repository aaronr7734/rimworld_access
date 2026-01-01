using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Discovered component information that can be exposed to the user.
    /// </summary>
    public class DiscoveredComponent
    {
        public string ComponentType { get; set; }
        public string DisplayName { get; set; }
        public string CategoryName { get; set; }
        public ThingComp Component { get; set; }
        public bool IsReadOnly { get; set; }
    }

    /// <summary>
    /// Helper class for dynamically discovering and managing building component settings.
    /// Uses a whitelist approach to expose common, safe-to-modify components.
    /// </summary>
    public static class BuildingComponentsHelper
    {
        // Whitelist of component types that are safe to expose
        private static readonly HashSet<Type> SupportedComponentTypes = new HashSet<Type>
        {
            typeof(CompFlickable),
            typeof(CompRefuelable),
            typeof(CompBreakdownable)
        };

        /// <summary>
        /// Gets all discoverable components from a building that are in the whitelist.
        /// </summary>
        public static List<DiscoveredComponent> GetDiscoverableComponents(Building building)
        {
            if (building == null)
                return new List<DiscoveredComponent>();

            var discovered = new List<DiscoveredComponent>();

            // Check for CompFlickable (power switch)
            var flickable = building.TryGetComp<CompFlickable>();
            if (flickable != null)
            {
                discovered.Add(new DiscoveredComponent
                {
                    ComponentType = "CompFlickable",
                    DisplayName = "Power Control",
                    CategoryName = "Power Control",
                    Component = flickable,
                    IsReadOnly = false
                });
            }

            // Check for CompRefuelable (fuel management)
            var refuelable = building.TryGetComp<CompRefuelable>();
            if (refuelable != null)
            {
                discovered.Add(new DiscoveredComponent
                {
                    ComponentType = "CompRefuelable",
                    DisplayName = "Fuel Settings",
                    CategoryName = "Fuel Settings",
                    Component = refuelable,
                    IsReadOnly = false
                });
            }

            // Check for CompBreakdownable (breakdown status - read only)
            var breakdownable = building.TryGetComp<CompBreakdownable>();
            if (breakdownable != null)
            {
                discovered.Add(new DiscoveredComponent
                {
                    ComponentType = "CompBreakdownable",
                    DisplayName = "Breakdown Status",
                    CategoryName = "Breakdown Status",
                    Component = breakdownable,
                    IsReadOnly = true
                });
            }
            // Check for Building_Door (hold open toggle)
            if (building is Building_Door door)
            {
                discovered.Add(new DiscoveredComponent
                {
                    ComponentType = "Building_Door",
                    DisplayName = "Door Controls",
                    CategoryName = "Door Controls",
                    Component = null,  // Not a component, it's the building itself
                    IsReadOnly = false
                });
            }
            // Check for CompForbiddable (forbid/unforbid toggle)
            var forbiddable = building.TryGetComp<CompForbiddable>();
            if (forbiddable != null)
            {
                discovered.Add(new DiscoveredComponent
                {
                    ComponentType = "CompForbiddable",
                    DisplayName = "Forbid Controls",
                    CategoryName = "Forbid Controls",
                    Component = forbiddable,
                    IsReadOnly = false
                });
            }




            return discovered;
        }

        /// <summary>
        /// Gets all components from a building (for debugging/extensibility).
        /// </summary>
        public static List<ThingComp> GetAllComponents(Building building)
        {
            if (building == null || building.AllComps == null)
                return new List<ThingComp>();

            return building.AllComps.ToList();
        }

        /// <summary>
        /// Checks if a building has any discoverable components.
        /// </summary>
        public static bool HasDiscoverableComponents(Building building)
        {
            if (building == null)
                return false;

            return GetDiscoverableComponents(building).Count > 0;
        }

        /// <summary>
        /// Gets a specific component by type name.
        /// </summary>
        public static DiscoveredComponent GetComponentByType(Building building, string componentType)
        {
            return GetDiscoverableComponents(building)
                .FirstOrDefault(c => c.ComponentType == componentType);
        }

        /// <summary>
        /// Gets readable status text for CompFlickable.
        /// </summary>
        public static string GetFlickableStatus(CompFlickable flickable)
        {
            if (flickable == null)
                return "No power control";

            return flickable.SwitchIsOn ? "Power: On" : "Power: Off";
        }

        /// <summary>
        /// Gets readable status text for CompRefuelable.
        /// </summary>
        public static string GetRefuelableStatus(CompRefuelable refuelable)
        {
            if (refuelable == null)
                return "No fuel system";

            float fuel = refuelable.Fuel;
            float maxFuel = refuelable.Props.fuelCapacity;
            float percent = (maxFuel > 0) ? (fuel / maxFuel * 100f) : 0f;
            string autoRefuel = refuelable.allowAutoRefuel ? "Auto-refuel: On" : "Auto-refuel: Off";

            return $"Fuel: {fuel:F1}/{maxFuel:F1} ({percent:F0}%) - {autoRefuel}";
        }

        /// <summary>
        /// Gets readable status text for CompBreakdownable.
        /// </summary>
        public static string GetBreakdownableStatus(CompBreakdownable breakdownable)
        {
            if (breakdownable == null)
                return "No breakdown system";

            return breakdownable.BrokenDown ? "Status: Broken down" : "Status: Operational";
        }

        /// <summary>
        /// Gets a summary of all discoverable components for a building.
        /// </summary>
        public static string GetComponentsSummary(Building building)
        {
            var components = GetDiscoverableComponents(building);
            if (components.Count == 0)
                return "No configurable components";

            var summary = $"{components.Count} configurable component(s): ";
            summary += string.Join(", ", components.Select(c => c.DisplayName));
            return summary;
        }

        /// <summary>
        /// Checks if a component type is supported for modification.
        /// </summary>
        public static bool IsComponentSupported(Type componentType)
        {
            return SupportedComponentTypes.Contains(componentType);
        }

        /// <summary>
        /// Gets all supported component type names (for debugging).
        /// </summary>
        public static List<string> GetSupportedComponentTypes()
        {
            return SupportedComponentTypes.Select(t => t.Name).ToList();
        }
    }
}
