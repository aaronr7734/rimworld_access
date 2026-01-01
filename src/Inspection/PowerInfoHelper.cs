using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class to extract and format power-related information for accessibility.
    /// Provides detailed power status for generators, consumers, batteries, and power networks.
    /// </summary>
    public static class PowerInfoHelper
    {
        /// <summary>
        /// Gets comprehensive power information for a building.
        /// Returns null if the building has no power components.
        /// </summary>
        public static string GetPowerInfo(Thing thing)
        {
            if (thing == null)
                return null;

            var sb = new StringBuilder();

            // Check for battery first (most specific)
            CompPowerBattery battery = thing.TryGetComp<CompPowerBattery>();
            if (battery != null)
            {
                AppendBatteryInfo(sb, battery);
                return sb.ToString();
            }

            // Check for power plant (generator)
            CompPowerPlant plant = thing.TryGetComp<CompPowerPlant>();
            if (plant != null)
            {
                AppendGeneratorInfo(sb, plant);
                return sb.ToString();
            }

            // Check for power trader (consumer/producer)
            CompPowerTrader trader = thing.TryGetComp<CompPowerTrader>();
            if (trader != null)
            {
                AppendPowerTraderInfo(sb, trader);
                return sb.ToString();
            }

            // Check for power transmitter (conduit)
            CompPowerTransmitter transmitter = thing.TryGetComp<CompPowerTransmitter>();
            if (transmitter != null)
            {
                AppendTransmitterInfo(sb, transmitter);
                return sb.ToString();
            }

            // Check for basic power component
            CompPower power = thing.TryGetComp<CompPower>();
            if (power != null)
            {
                AppendBasicPowerInfo(sb, power);
                return sb.ToString();
            }

            return null;
        }

        /// <summary>
        /// Appends battery-specific information.
        /// </summary>
        private static void AppendBatteryInfo(StringBuilder sb, CompPowerBattery battery)
        {
            // Battery charge status
            float chargePercent = battery.StoredEnergyPct * 100f;
            sb.Append($"{chargePercent:F0}% charged");

            // Stored energy details
            sb.Append($" ({battery.StoredEnergy:F0} / {battery.Props.storedEnergyMax:F0} Wd)");

            // EMP status
            CompStunnable stunnable = battery.parent.TryGetComp<CompStunnable>();
            if (stunnable != null && stunnable.StunHandler.Stunned && stunnable.StunHandler.StunFromEMP)
            {
                sb.Append(", Stunned by EMP");
            }

            // Efficiency
            if (battery.Props.efficiency < 1f)
            {
                sb.Append($", {battery.Props.efficiency * 100f:F0}% efficiency");
            }

            // Network status
            PowerNet net = battery.PowerNet;
            if (net != null)
            {
                sb.Append(", ");
                AppendNetworkSummary(sb, net);
            }
            else
            {
                sb.Append(", Not connected to power network");
            }
        }

        /// <summary>
        /// Appends generator-specific information.
        /// </summary>
        private static void AppendGeneratorInfo(StringBuilder sb, CompPowerPlant plant)
        {
            Thing building = plant.parent;

            // Get relevant components for status checks
            CompBreakdownable breakdownComp = building.TryGetComp<CompBreakdownable>();
            CompRefuelable refuelComp = building.TryGetComp<CompRefuelable>();
            CompFlickable flickComp = building.TryGetComp<CompFlickable>();
            CompStunnable stunnableComp = building.TryGetComp<CompStunnable>();

            // Power status
            if (plant.PowerOn)
            {
                // Generating power
                float output = plant.PowerOutput; // Generators have positive PowerOutput
                sb.Append($"Generating {output:F0}W");

                // Check for issues that might stop generation
                if (breakdownComp != null && breakdownComp.BrokenDown)
                {
                    sb.Append(", Broken down");
                }
                else if (refuelComp != null && !refuelComp.HasFuel)
                {
                    sb.Append(", Out of fuel");
                }
                else if (flickComp != null && !flickComp.SwitchIsOn)
                {
                    sb.Append(", Switched off");
                }
            }
            else
            {
                sb.Append("Not generating");

                // Explain why
                if (breakdownComp != null && breakdownComp.BrokenDown)
                {
                    sb.Append(" (Broken down)");
                }
                else if (refuelComp != null && !refuelComp.HasFuel)
                {
                    sb.Append(" (Out of fuel)");
                }
                else if (flickComp != null && !flickComp.SwitchIsOn)
                {
                    sb.Append(" (Switched off)");
                }
                else if (stunnableComp != null && stunnableComp.StunHandler.Stunned && stunnableComp.StunHandler.StunFromEMP)
                {
                    sb.Append(" (Stunned by EMP)");
                }
            }

            // Network status
            PowerNet net = plant.PowerNet;
            if (net != null)
            {
                sb.Append(", ");
                AppendNetworkSummary(sb, net);
            }
            else
            {
                sb.Append(", Not connected to power network");
            }
        }

        /// <summary>
        /// Appends power trader information (consumers or non-plant producers).
        /// </summary>
        private static void AppendPowerTraderInfo(StringBuilder sb, CompPowerTrader trader)
        {
            Thing building = trader.parent;

            // Get relevant components for status checks
            CompFlickable flickComp = building.TryGetComp<CompFlickable>();
            CompStunnable stunnableComp = building.TryGetComp<CompStunnable>();

            if (trader.PowerOutput < 0)
            {
                // This is a consumer
                float consumption = -trader.PowerOutput;

                if (trader.PowerOn)
                {
                    sb.Append($"Consuming {consumption:F0}W, Powered on");
                }
                else
                {
                    sb.Append($"Requires {consumption:F0}W, Powered off");
                }
            }
            else if (trader.PowerOutput > 0)
            {
                // This is a producer (non-plant)
                sb.Append($"Producing {trader.PowerOutput:F0}W");

                if (!trader.PowerOn)
                {
                    sb.Append(", Powered off");
                }
            }
            else
            {
                // No power usage
                sb.Append("No power usage");
            }

            // EMP status
            if (stunnableComp != null && stunnableComp.StunHandler.Stunned && stunnableComp.StunHandler.StunFromEMP)
            {
                sb.Append(", Stunned by EMP");
            }

            // Flick switch status
            if (flickComp != null && !flickComp.SwitchIsOn)
            {
                sb.Append(", Switched off");
            }

            // Network status
            PowerNet net = trader.PowerNet;
            if (net != null)
            {
                sb.Append(", ");
                AppendNetworkSummary(sb, net);
            }
            else
            {
                sb.Append(", Not connected to power network");
            }
        }

        /// <summary>
        /// Appends transmitter (conduit) information.
        /// </summary>
        private static void AppendTransmitterInfo(StringBuilder sb, CompPowerTransmitter transmitter)
        {
            if (transmitter.TransmitsPowerNow)
            {
                sb.Append("Transmitting power");
            }
            else
            {
                sb.Append("Not transmitting power");
            }

            // Network status
            PowerNet net = transmitter.PowerNet;
            if (net != null)
            {
                sb.Append(", ");
                AppendNetworkSummary(sb, net);
            }
            else
            {
                sb.Append(", Not connected to power network");
            }
        }

        /// <summary>
        /// Appends basic power component information (fallback).
        /// </summary>
        private static void AppendBasicPowerInfo(StringBuilder sb, CompPower power)
        {
            if (power.PowerNet != null)
            {
                sb.Append("Connected to power");
                sb.Append(", ");
                AppendNetworkSummary(sb, power.PowerNet);
            }
            else
            {
                sb.Append("Not connected to power network");
            }
        }

        /// <summary>
        /// Appends a summary of the power network status.
        /// </summary>
        private static void AppendNetworkSummary(StringBuilder sb, PowerNet net)
        {
            if (net == null)
                return;

            // Calculate net power balance
            float netPower = net.CurrentEnergyGainRate() / CompPower.WattsToWattDaysPerTick;

            if (netPower > 0.1f)
            {
                sb.Append($"Network: +{netPower:F0}W surplus");
            }
            else if (netPower < -0.1f)
            {
                sb.Append($"Network: {netPower:F0}W deficit");
            }
            else
            {
                sb.Append("Network: Balanced");
            }

            // Add stored energy if batteries exist
            if (net.batteryComps.Count > 0)
            {
                float storedEnergy = net.CurrentStoredEnergy();
                sb.Append($", {storedEnergy:F0}Wd stored");
            }

            // Add connected buildings summary
            AppendConnectedBuildingsSummary(sb, net);
        }

        /// <summary>
        /// Appends a categorized summary of connected buildings on the network.
        /// Format: "X generators producing YW, Z consumers using WW"
        /// </summary>
        private static void AppendConnectedBuildingsSummary(StringBuilder sb, PowerNet net)
        {
            if (net == null || net.powerComps.Count == 0)
                return;

            // Categorize power components
            int generatorCount = 0;
            float totalGeneration = 0f;
            int consumerCount = 0;
            float totalConsumption = 0f;

            foreach (CompPowerTrader trader in net.powerComps)
            {
                if (trader == null)
                    continue;

                float output = trader.PowerOutput;

                if (output > 0.1f)
                {
                    // This is a generator
                    generatorCount++;
                    totalGeneration += output;
                }
                else if (output < -0.1f)
                {
                    // This is a consumer
                    consumerCount++;
                    totalConsumption += -output; // Make positive for display
                }
            }

            // Build summary
            var parts = new List<string>();

            if (generatorCount > 0)
            {
                string genText = generatorCount == 1
                    ? $"1 generator producing {totalGeneration:F0}W"
                    : $"{generatorCount} generators producing {totalGeneration:F0}W";
                parts.Add(genText);
            }

            if (consumerCount > 0)
            {
                string conText = consumerCount == 1
                    ? $"1 consumer using {totalConsumption:F0}W"
                    : $"{consumerCount} consumers using {totalConsumption:F0}W";
                parts.Add(conText);
            }

            if (net.batteryComps.Count > 0)
            {
                string batText = net.batteryComps.Count == 1
                    ? "1 battery"
                    : $"{net.batteryComps.Count} batteries";
                parts.Add(batText);
            }

            if (parts.Count > 0)
            {
                sb.Append(", Connected: ");
                sb.Append(string.Join(", ", parts));
            }
        }
    }
}
