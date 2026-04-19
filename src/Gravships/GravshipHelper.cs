using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public static class GravshipHelper
    {
        /// <summary>
        /// Gets a formatted fuel summary for a gravship engine.
        /// </summary>
        public static string GetFuelSummary(Building_GravEngine engine)
        {
            if (engine == null) return "";

            var sb = new StringBuilder();
            sb.Append($"{"StoredChemfuel".Translate().CapitalizeFirst()}: {engine.TotalFuel:F0} / {engine.MaxFuel:F0}");
            sb.Append($". {"FuelConsumption".Translate().CapitalizeFirst()}: {"FuelPerTile".Translate(engine.FuelPerTile.ToString("0.#")).CapitalizeFirst()}");
            sb.Append($". {"GravshipRange".Translate().CapitalizeFirst()}: {engine.MaxLaunchDistance}");

            if (engine.FuelSavingsPercent > 0f)
            {
                sb.Append($". {"RimWorldAccess.Gravships.Engine.FuelSavings".Translate(engine.FuelSavingsPercent.ToString("P0"))}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets the component status for a gravship engine.
        /// </summary>
        public static string GetComponentStatus(Building_GravEngine engine)
        {
            if (engine == null) return "";

            var sb = new StringBuilder();
            int componentCount = engine.GravshipComponents.Count;
            string connectedKey = componentCount == 1
                ? "RimWorldAccess.Gravships.Engine.ComponentsConnectedOne"
                : "RimWorldAccess.Gravships.Engine.ComponentsConnectedMany";
            sb.Append(connectedKey.Translate(componentCount));

            var missing = engine.MissingComponents;
            if (missing.Count > 0)
            {
                sb.Append($". {"Requires".Translate()}: ");
                for (int i = 0; i < missing.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(missing[i].label);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets the launch readiness status for a gravship engine.
        /// </summary>
        public static string GetLaunchReadiness(Building_GravEngine engine)
        {
            if (engine == null) return "";

            var sb = new StringBuilder();
            sb.Append($"{"ConnectedSubstructure".Translate()}: {engine.AllConnectedSubstructure.Count}");

            var missing = engine.MissingComponents;
            if (missing.Count > 0)
            {
                sb.Append($". {"RimWorldAccess.Gravships.Engine.NotReadyMissing".Translate()} ");
                for (int i = 0; i < missing.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(missing[i].label);
                }
            }
            else
            {
                sb.Append($". {"RimWorldAccess.Gravships.Engine.AllConnected".Translate()}");
            }

            if (GenTicks.TicksGame < engine.cooldownCompleteTick)
            {
                sb.Append($". {"CannotLaunchOnCooldown".Translate((engine.cooldownCompleteTick - GenTicks.TicksGame).ToStringTicksToPeriod()).CapitalizeFirst()}");
            }

            return sb.ToString();
        }
    }
}
