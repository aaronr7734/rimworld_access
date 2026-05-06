using Verse;
using RimWorld;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for uninstall functionality (Minifiable buildings).
    /// Allows designating furniture for uninstallation via keyboard shortcuts.
    /// </summary>
    public static class UninstallControlState
    {
        private static Building building = null;
        private static bool isActive = false;

        public static bool IsActive => isActive;

        public static void Open(Building targetBuilding)
        {
            if (!GuardHelper.RequireBuilding(targetBuilding)) return;

            if (!targetBuilding.def.Minifiable)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Uninstall.NotMinifiable".Translate(), SpeechPriority.High);
                return;
            }

            building = targetBuilding;
            isActive = true;
            MapNavigationState.SuppressMapNavigation = true;

            AnnounceCurrentStatus();
        }

        public static void Close()
        {
            building = null;
            isActive = false;
            MapNavigationState.SuppressMapNavigation = false;
        }

        public static void ToggleUninstall()
        {
            if (building == null || building.Map == null)
                return;

            var designation = building.Map.designationManager.DesignationOn(building, DesignationDefOf.Uninstall);

            if (designation != null)
            {
                building.Map.designationManager.RemoveDesignation(designation);
                TolkHelper.Speak("RimWorldAccess.Building.Uninstall.DesignationRemoved".Translate(building.LabelCap));
            }
            else
            {
                bool instantUninstall = UnityEngine.Debug.isDebugBuild || building.GetStatValue(StatDefOf.WorkToBuild) == 0f || building.def.IsFrame;

                if (instantUninstall)
                {
                    building.Uninstall();
                    TolkHelper.Speak("RimWorldAccess.Building.Uninstall.InstantlyUninstalled".Translate(building.LabelCap));
                }
                else
                {
                    building.Map.designationManager.AddDesignation(new Designation(building, DesignationDefOf.Uninstall));
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.Building.Uninstall.DesignatedForUninstall".Translate(building.LabelCap));
                }
            }
        }

        private static void AnnounceCurrentStatus()
        {
            if (building == null || building.Map == null)
                return;

            var designation = building.Map.designationManager.DesignationOn(building, DesignationDefOf.Uninstall);
            string key = designation != null
                ? "RimWorldAccess.Building.Uninstall.LabelStatusDesignated"
                : "RimWorldAccess.Building.Uninstall.LabelStatusNotDesignated";

            TolkHelper.Speak(key.Translate(building.LabelCap));
        }

        public static void AnnounceDetailedStatus()
        {
            if (building == null || building.Map == null)
                return;

            var designation = building.Map.designationManager.DesignationOn(building, DesignationDefOf.Uninstall);
            var b = new AnnouncementBuilder();
            b.Add(building.LabelCap);

            if (designation != null)
            {
                b.Add("RimWorldAccess.Building.Uninstall.DetailDesignated".Translate());
                b.Add("RimWorldAccess.Building.Uninstall.DetailDisassemblerTip".Translate());
                b.Add("RimWorldAccess.Building.Uninstall.DetailMinifiedTip".Translate());
            }
            else
            {
                b.Add("RimWorldAccess.Building.Uninstall.DetailNotDesignated".Translate());
                b.Add("RimWorldAccess.Building.Uninstall.DetailCanUninstallTip".Translate());
                b.Add("RimWorldAccess.Building.Uninstall.DetailHotkeyTip".Translate());
            }

            TolkHelper.Speak(b.Build());
        }
    }
}
