using System.Collections.Generic;
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
            sb.Append((string)"RimWorldAccess.Inspection.Power.Charged".Translate(chargePercent.ToString("F0")));

            // Stored energy details
            sb.Append(" " + (string)"RimWorldAccess.Inspection.Power.StoredDetail".Translate(battery.StoredEnergy.ToString("F0"), battery.Props.storedEnergyMax.ToString("F0")));

            // EMP status
            CompStunnable stunnable = battery.parent.TryGetComp<CompStunnable>();
            if (stunnable != null && stunnable.StunHandler.Stunned && stunnable.StunHandler.StunFromEMP)
            {
                sb.Append(", " + (string)"RimWorldAccess.Inspection.Power.StunnedByEmp".Translate());
            }

            // Efficiency
            if (battery.Props.efficiency < 1f)
            {
                sb.Append(", " + (string)"RimWorldAccess.Inspection.Power.Efficiency".Translate((battery.Props.efficiency * 100f).ToString("F0")));
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
                sb.Append(", " + (string)"RimWorldAccess.Inspection.Power.NotConnected".Translate());
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
                sb.Append((string)"RimWorldAccess.Inspection.Power.Generating".Translate(output.ToString("F0")));

                // Check for issues that might stop generation
                if (breakdownComp != null && breakdownComp.BrokenDown)
                {
                    sb.Append(", " + (string)"RimWorldAccess.Inspection.Power.BrokenDown".Translate());
                }
                else if (refuelComp != null && !refuelComp.HasFuel)
                {
                    sb.Append(", " + (string)"RimWorldAccess.Inspection.Power.OutOfFuel".Translate());
                }
                else if (flickComp != null && !flickComp.SwitchIsOn)
                {
                    sb.Append(", " + (string)"RimWorldAccess.Inspection.Power.SwitchedOff".Translate());
                }
            }
            else
            {
                sb.Append((string)"RimWorldAccess.Inspection.Power.NotGenerating".Translate());

                // Explain why
                if (breakdownComp != null && breakdownComp.BrokenDown)
                {
                    sb.Append(" (" + (string)"RimWorldAccess.Inspection.Power.BrokenDown".Translate() + ")");
                }
                else if (refuelComp != null && !refuelComp.HasFuel)
                {
                    sb.Append(" (" + (string)"RimWorldAccess.Inspection.Power.OutOfFuel".Translate() + ")");
                }
                else if (flickComp != null && !flickComp.SwitchIsOn)
                {
                    sb.Append(" (" + (string)"RimWorldAccess.Inspection.Power.SwitchedOff".Translate() + ")");
                }
                else if (stunnableComp != null && stunnableComp.StunHandler.Stunned && stunnableComp.StunHandler.StunFromEMP)
                {
                    sb.Append(" (" + (string)"RimWorldAccess.Inspection.Power.StunnedByEmp".Translate() + ")");
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
                sb.Append(", " + (string)"RimWorldAccess.Inspection.Power.NotConnected".Translate());
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
                    sb.Append((string)"RimWorldAccess.Inspection.Power.Consuming".Translate(consumption.ToString("F0")));
                    sb.Append(", " + (string)"RimWorldAccess.Inspection.Power.PoweredOn".Translate());
                }
                else
                {
                    sb.Append((string)"RimWorldAccess.Inspection.Power.Requires".Translate(consumption.ToString("F0")));
                    sb.Append(", " + (string)"RimWorldAccess.Inspection.Power.PoweredOff".Translate());
                }
            }
            else if (trader.PowerOutput > 0)
            {
                // This is a producer (non-plant)
                sb.Append((string)"RimWorldAccess.Inspection.Power.Producing".Translate(trader.PowerOutput.ToString("F0")));

                if (!trader.PowerOn)
                {
                    sb.Append(", " + (string)"RimWorldAccess.Inspection.Power.PoweredOff".Translate());
                }
            }
            else
            {
                // No power usage
                sb.Append((string)"RimWorldAccess.Inspection.Power.NoPowerUsage".Translate());
            }

            // EMP status
            if (stunnableComp != null && stunnableComp.StunHandler.Stunned && stunnableComp.StunHandler.StunFromEMP)
            {
                sb.Append(", " + (string)"RimWorldAccess.Inspection.Power.StunnedByEmp".Translate());
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
                sb.Append(", " + (string)"RimWorldAccess.Inspection.Power.NotConnected".Translate());
            }
        }

        /// <summary>
        /// Appends transmitter (conduit) information.
        /// </summary>
        private static void AppendTransmitterInfo(StringBuilder sb, CompPowerTransmitter transmitter)
        {
            if (transmitter.TransmitsPowerNow)
            {
                sb.Append((string)"RimWorldAccess.Inspection.Power.Transmitting".Translate());
            }
            else
            {
                sb.Append((string)"RimWorldAccess.Inspection.Power.NotTransmitting".Translate());
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
                sb.Append(", " + (string)"RimWorldAccess.Inspection.Power.NotConnected".Translate());
            }
        }

        /// <summary>
        /// Appends basic power component information (fallback).
        /// </summary>
        private static void AppendBasicPowerInfo(StringBuilder sb, CompPower power)
        {
            if (power.PowerNet != null)
            {
                sb.Append((string)"RimWorldAccess.Inspection.Power.Connected".Translate());
                sb.Append(", ");
                AppendNetworkSummary(sb, power.PowerNet);
            }
            else
            {
                sb.Append((string)"RimWorldAccess.Inspection.Power.NotConnected".Translate());
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
                sb.Append((string)"RimWorldAccess.Inspection.Power.NetworkSurplus".Translate(netPower.ToString("F0")));
            }
            else if (netPower < -0.1f)
            {
                sb.Append((string)"RimWorldAccess.Inspection.Power.NetworkDeficit".Translate(netPower.ToString("F0")));
            }
            else
            {
                sb.Append((string)"RimWorldAccess.Inspection.Power.NetworkBalanced".Translate());
            }

            // Add stored energy if batteries exist
            if (net.batteryComps.Count > 0)
            {
                float storedEnergy = net.CurrentStoredEnergy();
                sb.Append(", " + (string)"RimWorldAccess.Inspection.Power.Stored".Translate(storedEnergy.ToString("F0")));
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
                    ? (string)"RimWorldAccess.Inspection.Power.GeneratorsOne".Translate(totalGeneration.ToString("F0"))
                    : (string)"RimWorldAccess.Inspection.Power.GeneratorsMany".Translate(generatorCount, totalGeneration.ToString("F0"));
                parts.Add(genText);
            }

            if (consumerCount > 0)
            {
                string conText = consumerCount == 1
                    ? (string)"RimWorldAccess.Inspection.Power.ConsumersOne".Translate(totalConsumption.ToString("F0"))
                    : (string)"RimWorldAccess.Inspection.Power.ConsumersMany".Translate(consumerCount, totalConsumption.ToString("F0"));
                parts.Add(conText);
            }

            if (net.batteryComps.Count > 0)
            {
                string batText = net.batteryComps.Count == 1
                    ? (string)"RimWorldAccess.Inspection.Power.BatteriesOne".Translate()
                    : (string)"RimWorldAccess.Inspection.Power.BatteriesMany".Translate(net.batteryComps.Count);
                parts.Add(batText);
            }

            if (parts.Count > 0)
            {
                sb.Append(", " + (string)"RimWorldAccess.Inspection.Power.ConnectedLabel".Translate(string.Join(", ", parts)));
            }
        }
    }
}
