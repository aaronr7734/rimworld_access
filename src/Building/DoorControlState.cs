using System;
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

        /// <summary>
        /// Opens the door control menu for the given door.
        /// </summary>
        public static void Open(Building targetBuilding)
        {
            if (targetBuilding == null)
            {
                TolkHelper.Speak("No building to configure");
                return;
            }

            Building_Door doorBuilding = targetBuilding as Building_Door;
            if (doorBuilding == null)
            {
                TolkHelper.Speak("Building is not a door");
                return;
            }

            door = doorBuilding;
            isActive = true;
            MapNavigationState.SuppressMapNavigation = true;

            AnnounceCurrentStatus();
        }

        /// <summary>
        /// Closes the door control menu.
        /// </summary>
        public static void Close()
        {
            door = null;
            isActive = false;
            MapNavigationState.SuppressMapNavigation = false;
        }

        /// <summary>
        /// Toggles the hold-open setting for the door.
        /// </summary>
        public static void ToggleHoldOpen()
        {
            if (door == null)
                return;

            // Access the holdOpenInt field via reflection since it's protected
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
                TolkHelper.Speak("Error: Could not access door hold-open setting", SpeechPriority.High);
            }
        }

        /// <summary>
        /// Announces the current door status to the clipboard for screen readers.
        /// </summary>
        private static void AnnounceCurrentStatus()
        {
            if (door == null)
                return;

            string doorLabel = door.LabelCap;
            string status = door.HoldOpen ? "Hold open: On" : "Hold open: Off";
            string openStatus = door.Open ? "(Currently open)" : "(Currently closed)";

            string announcement = string.Format("{0} - {1} {2}", doorLabel, status, openStatus);

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Gets a detailed status report for the door.
        /// </summary>
        public static void AnnounceDetailedStatus()
        {
            if (door == null)
                return;

            string details = string.Format("{0}\n", door.LabelCap);

            details += string.Format("Hold open: {0}\n", door.HoldOpen ? "On" : "Off");
            details += string.Format("Currently: {0}\n", door.Open ? "Open" : "Closed");

            // Check if door is powered
            if (door.powerComp != null)
            {
                if (door.powerComp.PowerOn)
                {
                    details += "Status: Powered - opens faster";
                }
                else
                {
                    details += "Status: No power - opens slower";
                }
            }
            else
            {
                details += "Status: Manual door (no power required)";
            }

            TolkHelper.Speak(details);
        }
    }
}
