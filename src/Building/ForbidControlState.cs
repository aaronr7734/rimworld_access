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

        public static void Open(Building targetBuilding)
        {
            if (!GuardHelper.RequireBuilding(targetBuilding)) return;

            CompForbiddable comp = targetBuilding.TryGetComp<CompForbiddable>();
            if (comp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Forbid.NoForbidComponent".Translate(), SpeechPriority.High);
                return;
            }

            building = targetBuilding;
            forbiddable = comp;
            isActive = true;
            MapNavigationState.SuppressMapNavigation = true;

            AnnounceCurrentStatus();
        }

        public static void Close()
        {
            forbiddable = null;
            building = null;
            isActive = false;
            MapNavigationState.SuppressMapNavigation = false;
        }

        public static void ToggleForbidden()
        {
            if (forbiddable == null || building == null)
                return;

            forbiddable.Forbidden = !forbiddable.Forbidden;
            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            AnnounceCurrentStatus();
        }

        private static void AnnounceCurrentStatus()
        {
            if (forbiddable == null || building == null)
                return;

            string status = forbiddable.Forbidden
                ? "RimWorldAccess.Building.Forbid.StatusForbidden".Translate()
                : "RimWorldAccess.Building.Forbid.StatusAllowed".Translate();

            TolkHelper.Speak("RimWorldAccess.Building.Forbid.LabelStatus".Translate(building.LabelCap, status));
        }

        public static void AnnounceDetailedStatus()
        {
            if (forbiddable == null || building == null)
                return;

            var b = new AnnouncementBuilder();
            b.Add(building.LabelCap);

            if (forbiddable.Forbidden)
            {
                b.Add("RimWorldAccess.Building.Forbid.DetailStatusForbidden".Translate());
                b.Add("RimWorldAccess.Building.Forbid.DetailNoInteract".Translate());
                b.Add("RimWorldAccess.Building.Forbid.DetailNoHaulUseEquip".Translate());
            }
            else
            {
                b.Add("RimWorldAccess.Building.Forbid.DetailStatusAllowed".Translate());
                b.Add("RimWorldAccess.Building.Forbid.DetailCanInteract".Translate());
            }

            TolkHelper.Speak(b.Build());
        }
    }
}
