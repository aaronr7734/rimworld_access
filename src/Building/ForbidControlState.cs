using System;
using Verse;
using RimWorld;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for forbid/unforbid controls (CompForbiddable).
    /// Allows toggling forbidden status via keyboard shortcuts.
    /// </summary>
    public static class ForbidControlState
    {
        private static CompForbiddable forbiddable = null;
        private static Building building = null;
        private static bool isActive = false;

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the forbid control menu for the given building/item.
        /// </summary>
        public static void Open(Building targetBuilding)
        {
            if (targetBuilding == null)
            {
                TolkHelper.Speak("No item to configure");
                return;
            }

            CompForbiddable comp = targetBuilding.TryGetComp<CompForbiddable>();
            if (comp == null)
            {
                TolkHelper.Speak("Item cannot be forbidden", SpeechPriority.High);
                return;
            }

            building = targetBuilding;
            forbiddable = comp;
            isActive = true;
            MapNavigationState.SuppressMapNavigation = true;

            AnnounceCurrentStatus();
        }

        /// <summary>
        /// Closes the forbid control menu.
        /// </summary>
        public static void Close()
        {
            forbiddable = null;
            building = null;
            isActive = false;
            MapNavigationState.SuppressMapNavigation = false;
        }

        /// <summary>
        /// Toggles the forbidden status.
        /// </summary>
        public static void ToggleForbidden()
        {
            if (forbiddable == null || building == null)
                return;

            forbiddable.Forbidden = !forbiddable.Forbidden;
            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            AnnounceCurrentStatus();
        }

        /// <summary>
        /// Announces the current forbidden status to the clipboard for screen readers.
        /// </summary>
        private static void AnnounceCurrentStatus()
        {
            if (forbiddable == null || building == null)
                return;

            string itemLabel = building.LabelCap;
            string status = forbiddable.Forbidden ? "Forbidden" : "Allowed";

            string announcement = string.Format("{0} - Status: {1}", itemLabel, status);

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Gets a detailed status report.
        /// </summary>
        public static void AnnounceDetailedStatus()
        {
            if (forbiddable == null || building == null)
                return;

            string details = string.Format("{0}\n", building.LabelCap);

            if (forbiddable.Forbidden)
            {
                details += "Status: Forbidden\n";
                details += "Colonists will not interact with this item.\n";
                details += "They will not haul, use, or equip it.";
            }
            else
            {
                details += "Status: Allowed\n";
                details += "Colonists can interact with this item normally.";
            }

            TolkHelper.Speak(details);
        }
    }
}
