using System;
using System.Linq;
using Verse;
using RimWorld;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for fuel settings (CompRefuelable).
    /// Allows viewing fuel status and toggling auto-refuel setting.
    /// </summary>
    public static class RefuelableComponentState
    {
        private static CompRefuelable refuelable = null;
        private static Building building = null;
        private static bool isActive = false;
        private static int currentOption = 0;
        private static readonly int optionCount = 3; // View status, Toggle auto-refuel, Adjust target fuel

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the fuel management menu for the given building.
        /// </summary>
        public static void Open(Building targetBuilding)
        {
            if (targetBuilding == null)
            {
                TolkHelper.Speak("No building to configure");
                return;
            }

            CompRefuelable comp = targetBuilding.TryGetComp<CompRefuelable>();
            if (comp == null)
            {
                TolkHelper.Speak("Building does not have fuel system");
                return;
            }

            MapNavigationState.SuppressMapNavigation = true;
            building = targetBuilding;
            refuelable = comp;
            isActive = true;
            currentOption = 0;

            AnnounceCurrentStatus();
        }

        /// <summary>
        /// Closes the fuel management menu.
        /// </summary>
        public static void Close()
        {
            MapNavigationState.SuppressMapNavigation = false;
            refuelable = null;
            building = null;
            isActive = false;
            currentOption = 0;
        }

        /// <summary>
        /// Moves to the next option in the menu.
        /// </summary>
        public static void SelectNext()
        {
            currentOption = (currentOption + 1) % optionCount;
            AnnounceCurrentOption();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Moves to the previous option in the menu.
        /// </summary>
        public static void SelectPrevious()
        {
            currentOption--;
            if (currentOption < 0)
                currentOption = optionCount - 1;
            AnnounceCurrentOption();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Executes the currently selected option.
        /// </summary>
        public static void ExecuteSelected()
        {
            if (refuelable == null || building == null)
                return;

            switch (currentOption)
            {
                case 0: // View detailed status
                    AnnounceDetailedStatus();
                    break;
                case 1: // Toggle auto-refuel
                    ToggleAutoRefuel();
                    break;
                case 2: // Adjust target fuel level
                    AnnounceTargetFuelHelp();
                    break;
            }
        }

        /// <summary>
        /// Toggles the auto-refuel setting.
        /// </summary>
        public static void ToggleAutoRefuel()
        {
            if (refuelable == null || building == null)
                return;

            // Check if auto-refuel toggle is available for this building
            if (!refuelable.Props.showAllowAutoRefuelToggle)
            {
                TolkHelper.Speak("Auto-refuel not available for this building", SpeechPriority.High);
                return;
            }

            refuelable.allowAutoRefuel = !refuelable.allowAutoRefuel;
            string status = refuelable.allowAutoRefuel ? "enabled" : "disabled";
            TolkHelper.Speak($"Auto-refuel {status}");
            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Increases target fuel level.
        /// </summary>
        public static void IncreaseTargetFuel()
        {
            if (refuelable == null || building == null)
                return;

            if (!refuelable.Props.targetFuelLevelConfigurable)
            {
                TolkHelper.Speak("Target fuel level cannot be configured", SpeechPriority.High);
                return;
            }

            float increment = refuelable.Props.fuelCapacity * 0.1f; // 10% increments
            refuelable.TargetFuelLevel += increment;
            AnnounceTargetFuelLevel();
        }

        /// <summary>
        /// Decreases target fuel level.
        /// </summary>
        public static void DecreaseTargetFuel()
        {
            if (refuelable == null || building == null)
                return;

            if (!refuelable.Props.targetFuelLevelConfigurable)
            {
                TolkHelper.Speak("Target fuel level cannot be configured", SpeechPriority.High);
                return;
            }

            float decrement = refuelable.Props.fuelCapacity * 0.1f; // 10% increments
            refuelable.TargetFuelLevel -= decrement;
            AnnounceTargetFuelLevel();
        }

        private static void AnnounceTargetFuelLevel()
        {
            if (refuelable == null)
                return;

            float percent = (refuelable.TargetFuelLevel / refuelable.Props.fuelCapacity) * 100f;
            TolkHelper.Speak(
                $"Target fuel: {refuelable.TargetFuelLevel:F1}/{refuelable.Props.fuelCapacity:F1} ({percent:F0}%)");
        }

        private static void AnnounceTargetFuelHelp()
        {
            if (refuelable == null)
                return;

            if (refuelable.Props.targetFuelLevelConfigurable)
            {
                string help = "Use Left/Right arrows to adjust target fuel level. " +
                             $"Current: {refuelable.TargetFuelLevel:F1}/{refuelable.Props.fuelCapacity:F1}";
                TolkHelper.Speak(help);
            }
            else
            {
                TolkHelper.Speak("Target fuel level cannot be configured for this building", SpeechPriority.High);
            }
        }

        /// <summary>
        /// Announces the current option in the menu.
        /// </summary>
        private static void AnnounceCurrentOption()
        {
            if (refuelable == null)
                return;

            string option;
            switch (currentOption)
            {
                case 0:
                    option = "View detailed fuel status";
                    break;
                case 1:
                    option = "Toggle auto-refuel";
                    break;
                case 2:
                    option = "Adjust target fuel level";
                    break;
                default:
                    option = "Unknown option";
                    break;
            }

            TolkHelper.Speak($"Option {currentOption + 1}/{optionCount}: {option}");
        }

        /// <summary>
        /// Announces the current fuel status to the clipboard for screen readers.
        /// </summary>
        private static void AnnounceCurrentStatus()
        {
            if (refuelable == null || building == null)
                return;

            float fuel = refuelable.Fuel;
            float maxFuel = refuelable.Props.fuelCapacity;
            float percent = (maxFuel > 0) ? (fuel / maxFuel * 100f) : 0f;

            string announcement = $"{building.LabelCap} - Fuel: {percent:F0}% ({fuel:F1}/{maxFuel:F1})";

            if (refuelable.Props.showAllowAutoRefuelToggle)
            {
                string autoRefuel = refuelable.allowAutoRefuel ? "On" : "Off";
                announcement += $" - Auto-refuel: {autoRefuel}";
            }

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Gets a detailed status report including fuel type and consumption.
        /// </summary>
        public static void AnnounceDetailedStatus()
        {
            if (refuelable == null || building == null)
                return;

            float fuel = refuelable.Fuel;
            float maxFuel = refuelable.Props.fuelCapacity;
            float percent = (maxFuel > 0) ? (fuel / maxFuel * 100f) : 0f;

            string details = $"{building.LabelCap}\n";
            details += $"Fuel: {fuel:F1}/{maxFuel:F1} ({percent:F0}%)";

            // Fuel type
            if (refuelable.Props.fuelFilter.AllowedDefCount == 1)
            {
                var fuelDef = refuelable.Props.fuelFilter.AllowedThingDefs.First();
                details += $"\nFuel type: {fuelDef.label}";
            }

            // Auto-refuel status
            if (refuelable.Props.showAllowAutoRefuelToggle)
            {
                string autoRefuel = refuelable.allowAutoRefuel ? "Enabled" : "Disabled";
                details += $"\nAuto-refuel: {autoRefuel}";
            }

            // Target fuel level
            if (refuelable.Props.targetFuelLevelConfigurable)
            {
                float targetPercent = (refuelable.TargetFuelLevel / maxFuel) * 100f;
                details += $"\nTarget: {refuelable.TargetFuelLevel:F1} ({targetPercent:F0}%)";
            }

            // Consumption rate
            if (!refuelable.Props.consumeFuelOnlyWhenUsed && refuelable.HasFuel)
            {
                float consumptionPerDay = refuelable.Props.fuelConsumptionRate;
                if (consumptionPerDay > 0)
                {
                    float daysRemaining = fuel / consumptionPerDay;
                    details += $"\nConsumption: {consumptionPerDay:F2}/day";
                    details += $"\nTime remaining: {daysRemaining:F1} days";
                }
            }

            // Out of fuel warning
            if (!refuelable.HasFuel && !string.IsNullOrEmpty(refuelable.Props.outOfFuelMessage))
            {
                details += $"\nWarning: {refuelable.Props.outOfFuelMessage}";
            }

            TolkHelper.Speak(details);
        }
    }
}
