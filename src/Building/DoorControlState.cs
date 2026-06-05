using Verse;
using RimWorld;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for door controls (Building_Door).
    /// Allows toggling hold-open setting via keyboard shortcuts.
    /// </summary>
    public static class DoorControlState
    {
        private static Building_Door door = null;
        private static bool isActive = false;

        public static bool IsActive => isActive;

        public static void Open(Building targetBuilding)
        {
            if (!GuardHelper.RequireBuilding(targetBuilding)) return;

            Building_Door doorBuilding = targetBuilding as Building_Door;
            if (doorBuilding == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Door.NotADoor".Loc());
                return;
            }

            door = doorBuilding;
            isActive = true;
            MapNavigationState.SuppressMapNavigation = true;

            AnnounceCurrentStatus();
        }

        public static void Close()
        {
            door = null;
            isActive = false;
            MapNavigationState.SuppressMapNavigation = false;
        }

        public static void ToggleHoldOpen()
        {
            if (door == null)
                return;

            var holdOpenField = typeof(Building_Door).GetField("holdOpenInt",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (holdOpenField != null)
            {
                bool currentValue = (bool)holdOpenField.GetValue(door);
                holdOpenField.SetValue(door, !currentValue);

                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                AnnounceCurrentStatus();
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Building.Door.HoldOpenAccessError".Loc(), SpeechPriority.High);
            }
        }

        private static void AnnounceCurrentStatus()
        {
            if (door == null)
                return;

            string holdOpenLabel = (string)"CommandToggleDoorHoldOpen".Translate();
            string onOff = door.HoldOpen ? (string)"On".Translate() : (string)"Off".Translate();
            string holdOpen = "RimWorldAccess.Building.Door.HoldOpenWithValue".Translate(holdOpenLabel, onOff);

            string openness = door.Open
                ? "RimWorldAccess.Building.Door.CurrentlyOpen".Translate()
                : "RimWorldAccess.Building.Door.CurrentlyClosed".Translate();

            TolkHelper.Speak("RimWorldAccess.Building.Door.LabelStatusOpenness".Loc(door.LabelCap, holdOpen, openness));
        }

        public static void AnnounceDetailedStatus()
        {
            if (door == null)
                return;

            string holdOpenLabel = (string)"CommandToggleDoorHoldOpen".Translate();
            string onOff = door.HoldOpen ? (string)"On".Translate() : (string)"Off".Translate();

            var b = new AnnouncementBuilder();
            b.Add(door.LabelCap);
            b.Add("RimWorldAccess.Building.Door.HoldOpenWithValue".Translate(holdOpenLabel, onOff));
            b.Add(door.Open
                ? "RimWorldAccess.Building.Door.DetailStateOpen".Translate()
                : "RimWorldAccess.Building.Door.DetailStateClosed".Translate());

            if (door.powerComp != null)
            {
                b.Add(door.powerComp.PowerOn
                    ? "RimWorldAccess.Building.Door.DetailPoweredFast".Translate()
                    : "RimWorldAccess.Building.Door.DetailNoPowerSlow".Translate());
            }
            else
            {
                b.Add("RimWorldAccess.Building.Door.DetailManual".Translate());
            }

            TolkHelper.SpeakData(b.Build());
        }
    }
}
