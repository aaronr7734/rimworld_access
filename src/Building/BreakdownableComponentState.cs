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

        public static void Open(Building targetBuilding)
        {
            if (!GuardHelper.RequireBuilding(targetBuilding)) return;

            CompBreakdownable comp = targetBuilding.TryGetComp<CompBreakdownable>();
            if (comp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Breakdown.NoBreakdownComponent".Translate(), SpeechPriority.High);
                return;
            }

            building = targetBuilding;
            breakdownable = comp;
            isActive = true;

            MapNavigationState.SuppressMapNavigation = true;
            AnnounceDetailedStatus();
        }

        public static void Close()
        {
            breakdownable = null;
            building = null;
            isActive = false;
            MapNavigationState.SuppressMapNavigation = false;
        }

        public static void AnnounceDetailedStatus()
        {
            if (breakdownable == null || building == null)
                return;

            var b = new AnnouncementBuilder();
            b.Add(building.LabelCap);

            if (breakdownable.BrokenDown)
            {
                b.Add("RimWorldAccess.Building.Breakdown.StatusBrokenDown".Translate());
                b.Add("RimWorldAccess.Building.Breakdown.NeedsRepair".Translate());
                b.Add("RimWorldAccess.Building.Breakdown.RepairSkill".Translate());
            }
            else
            {
                b.Add("RimWorldAccess.Building.Breakdown.StatusOperational".Translate());
                b.Add("RimWorldAccess.Building.Breakdown.OperationalDescription".Translate());
                b.Add("RimWorldAccess.Building.Breakdown.BreakdownsOverTime".Translate());

                var powerComp = building.TryGetComp<CompPowerTrader>();
                if (powerComp != null && !powerComp.PowerOn)
                {
                    b.Add("RimWorldAccess.Building.Breakdown.UnpoweredImmune".Translate());
                }
            }

            TolkHelper.Speak(b.Build());
        }

        public static void RefreshStatus()
        {
            if (isActive)
            {
                AnnounceDetailedStatus();
            }
        }
    }
}
