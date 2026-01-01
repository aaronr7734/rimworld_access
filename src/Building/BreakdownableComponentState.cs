using System;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for breakdown status (CompBreakdownable).
    /// This is a read-only view showing breakdown information.
    /// </summary>
    public static class BreakdownableComponentState
    {
        private static CompBreakdownable breakdownable = null;
        private static Building building = null;
        private static bool isActive = false;

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the breakdown status view for the given building.
        /// </summary>
        public static void Open(Building targetBuilding)
        {
            if (targetBuilding == null)
            {
                TolkHelper.Speak("No building to inspect");
                return;
            }

            CompBreakdownable comp = targetBuilding.TryGetComp<CompBreakdownable>();
            if (comp == null)
            {
                TolkHelper.Speak("Building cannot break down", SpeechPriority.High);
                return;
            }

            building = targetBuilding;
            breakdownable = comp;
            isActive = true;

            MapNavigationState.SuppressMapNavigation = true;
            AnnounceDetailedStatus();
        }

        /// <summary>
        /// Closes the breakdown status view.
        /// </summary>
        public static void Close()
        {
            breakdownable = null;
            building = null;
            isActive = false;
            MapNavigationState.SuppressMapNavigation = false;
        }

        /// <summary>
        /// Gets a detailed status report including breakdown information.
        /// </summary>
        public static void AnnounceDetailedStatus()
        {
            if (breakdownable == null || building == null)
                return;

            string details = string.Format("{0}\n", building.LabelCap);

            if (breakdownable.BrokenDown)
            {
                details += "Status: Broken down\n";
                details += "This building requires repair before it can function.\n";
                details += "A colonist with the Construction skill can repair it.";
            }
            else
            {
                details += "Status: Operational\n";
                details += "This building is functioning normally.\n";
                details += "Buildings with breakdown components can break down over time.";

                // Check power status if applicable
                var powerComp = building.TryGetComp<CompPowerTrader>();
                if (powerComp != null && !powerComp.PowerOn)
                {
                    details += "\nNote: Building is not powered. Unpowered buildings do not break down.";
                }
            }

            TolkHelper.Speak(details);
        }

        /// <summary>
        /// Refreshes and re-announces the status (useful if status changed externally).
        /// </summary>
        public static void RefreshStatus()
        {
            if (isActive)
            {
                AnnounceDetailedStatus();
            }
        }
    }
}
