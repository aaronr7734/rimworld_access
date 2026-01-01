using System;
using Verse;
using RimWorld;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for power switch control (CompFlickable).
    /// Allows toggling power on/off via keyboard shortcuts.
    /// </summary>
    public static class FlickableComponentState
    {
        private static CompFlickable flickable = null;
        private static Building building = null;
        private static bool isActive = false;

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the power control menu for the given building.
        /// </summary>
        public static void Open(Building targetBuilding)
        {
            if (targetBuilding == null)
            {
                TolkHelper.Speak("No building to configure");
                return;
            }

            CompFlickable comp = targetBuilding.TryGetComp<CompFlickable>();
            if (comp == null)
            {
                TolkHelper.Speak("Building does not have power control");
                return;
            }

            building = targetBuilding;
            flickable = comp;
            isActive = true;
            MapNavigationState.SuppressMapNavigation = true;

            AnnounceCurrentStatus();
        }

        /// <summary>
        /// Closes the power control menu.
        /// </summary>
        public static void Close()
        {
            flickable = null;
            building = null;
            isActive = false;
            MapNavigationState.SuppressMapNavigation = false;
        }

        /// <summary>
        /// Toggles the power switch on/off.
        /// </summary>
        public static void TogglePower()
        {
            if (flickable == null || building == null)
                return;

            // Use the same logic as the game's native flick command
            flickable.DoFlick();

            // Announce the new status
            AnnounceCurrentStatus();
        }

        /// <summary>
        /// Explicitly turns power on.
        /// </summary>
        public static void TurnOn()
        {
            if (flickable == null || building == null)
                return;

            if (!flickable.SwitchIsOn)
            {
                flickable.DoFlick();
                AnnounceCurrentStatus();
            }
            else
            {
                TolkHelper.Speak("Power is already on");
            }
        }

        /// <summary>
        /// Explicitly turns power off.
        /// </summary>
        public static void TurnOff()
        {
            if (flickable == null || building == null)
                return;

            if (flickable.SwitchIsOn)
            {
                flickable.DoFlick();
                AnnounceCurrentStatus();
            }
            else
            {
                TolkHelper.Speak("Power is already off");
            }
        }

        /// <summary>
        /// Announces the current power status to the clipboard for screen readers.
        /// </summary>
        private static void AnnounceCurrentStatus()
        {
            if (flickable == null || building == null)
                return;

            string status = flickable.SwitchIsOn ? "On" : "Off";
            string announcement = $"{building.LabelCap} - Power: {status}";

            // Check if connected to power grid
            var powerComp = building.TryGetComp<CompPowerTrader>();
            if (powerComp != null)
            {
                if (!powerComp.PowerOn && flickable.SwitchIsOn)
                {
                    announcement += " (No power available)";
                }
                else if (powerComp.PowerOn && flickable.SwitchIsOn)
                {
                    float powerUsage = -powerComp.PowerOutput;
                    if (powerUsage > 0)
                    {
                        announcement += $" - Consuming: {powerUsage:F0}W";
                    }
                    else if (powerUsage < 0)
                    {
                        announcement += $" - Producing: {-powerUsage:F0}W";
                    }
                }
            }

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Gets a detailed status report including power consumption/production.
        /// </summary>
        public static void AnnounceDetailedStatus()
        {
            if (flickable == null || building == null)
                return;

            string status = flickable.SwitchIsOn ? "On" : "Off";
            string details = $"{building.LabelCap} - Power switch: {status}";

            var powerComp = building.TryGetComp<CompPowerTrader>();
            if (powerComp != null)
            {
                details += $"\nConnected to power grid";

                if (flickable.SwitchIsOn)
                {
                    if (powerComp.PowerOn)
                    {
                        details += " - Active";
                        float powerUsage = -powerComp.PowerOutput;
                        if (powerUsage > 0)
                        {
                            details += $"\nConsuming: {powerUsage:F0}W";
                        }
                        else if (powerUsage < 0)
                        {
                            details += $"\nProducing: {-powerUsage:F0}W";
                        }
                    }
                    else
                    {
                        details += " - No power available";
                    }
                }
                else
                {
                    details += " - Switched off";
                }
            }
            else
            {
                details += "\nNot connected to power grid";
            }

            TolkHelper.Speak(details);
        }
    }
}
