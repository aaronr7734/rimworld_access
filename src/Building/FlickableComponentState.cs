using Verse;
using RimWorld;

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

        public static void Open(Building targetBuilding)
        {
            if (!GuardHelper.RequireBuilding(targetBuilding)) return;

            CompFlickable comp = targetBuilding.TryGetComp<CompFlickable>();
            if (comp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Flickable.NoFlickComponent".Loc());
                return;
            }

            building = targetBuilding;
            flickable = comp;
            isActive = true;
            MapNavigationState.SuppressMapNavigation = true;

            AnnounceCurrentStatus();
        }

        public static void Close()
        {
            flickable = null;
            building = null;
            isActive = false;
            MapNavigationState.SuppressMapNavigation = false;
        }

        public static void TogglePower()
        {
            if (flickable == null || building == null)
                return;

            flickable.DoFlick();
            AnnounceCurrentStatus();
        }

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
                TolkHelper.Speak("RimWorldAccess.Building.Flickable.AlreadyOn".Loc());
            }
        }

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
                TolkHelper.Speak("RimWorldAccess.Building.Flickable.AlreadyOff".Loc());
            }
        }

        private static void AnnounceCurrentStatus()
        {
            if (flickable == null || building == null)
                return;

            string onOff = flickable.SwitchIsOn ? (string)"On".Translate() : (string)"Off".Translate();
            string announcement = "RimWorldAccess.Building.Flickable.LabelPowerStatus".Translate(building.LabelCap, onOff);

            var powerComp = building.TryGetComp<CompPowerTrader>();
            if (powerComp != null)
            {
                if (!powerComp.PowerOn && flickable.SwitchIsOn)
                {
                    announcement += "RimWorldAccess.Building.Flickable.NoPowerAvailableSuffix".Translate();
                }
                else if (powerComp.PowerOn && flickable.SwitchIsOn)
                {
                    float powerUsage = -powerComp.PowerOutput;
                    if (powerUsage > 0)
                    {
                        announcement += "RimWorldAccess.Building.Flickable.ConsumingSuffix".Translate(powerUsage.ToString("F0"));
                    }
                    else if (powerUsage < 0)
                    {
                        announcement += "RimWorldAccess.Building.Flickable.ProducingSuffix".Translate((-powerUsage).ToString("F0"));
                    }
                }
            }

            TolkHelper.Speak(announcement);
        }

        public static void AnnounceDetailedStatus()
        {
            if (flickable == null || building == null)
                return;

            string onOff = flickable.SwitchIsOn ? (string)"On".Translate() : (string)"Off".Translate();
            var b = new AnnouncementBuilder();
            b.Add("RimWorldAccess.Building.Flickable.LabelPowerSwitch".Translate(building.LabelCap, onOff));

            var powerComp = building.TryGetComp<CompPowerTrader>();
            if (powerComp != null)
            {
                string connected = "RimWorldAccess.Building.Flickable.DetailConnected".Translate();
                if (flickable.SwitchIsOn)
                {
                    if (powerComp.PowerOn)
                    {
                        connected += "RimWorldAccess.Building.Flickable.DetailActiveSuffix".Translate();
                        b.Add(connected);

                        float powerUsage = -powerComp.PowerOutput;
                        if (powerUsage > 0)
                        {
                            b.Add("RimWorldAccess.Building.Flickable.DetailConsuming".Translate(powerUsage.ToString("F0")));
                        }
                        else if (powerUsage < 0)
                        {
                            b.Add("RimWorldAccess.Building.Flickable.DetailProducing".Translate((-powerUsage).ToString("F0")));
                        }
                    }
                    else
                    {
                        connected += "RimWorldAccess.Building.Flickable.DetailNoPowerSuffix".Translate();
                        b.Add(connected);
                    }
                }
                else
                {
                    connected += "RimWorldAccess.Building.Flickable.DetailSwitchedOffSuffix".Translate();
                    b.Add(connected);
                }
            }
            else
            {
                b.Add("RimWorldAccess.Building.Flickable.DetailNotConnected".Translate());
            }

            TolkHelper.Speak(b.Build());
        }
    }
}
