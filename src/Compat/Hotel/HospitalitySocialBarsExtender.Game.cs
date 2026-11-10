using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Hospitality's SocialCardUtility.DrawPawnRow prefix paints a coloured bar behind each colonist
    /// row of a guest's Social tab: green filling toward the guest's minimum recruit opinion, red
    /// filling toward -20. Colour and width are the whole datum, so the Social category gains one
    /// section speaking each bar, under the patch's own conditions.
    /// </summary>
    internal static class HospitalitySocialBarsExtender
    {
        // DrawPawnRow_Patch's enemy scale.
        private const float EnemyOpinion = -20f;

        private static MethodInfo isGuest;
        private static MethodInfo minRecruitOpinion;

        public static void Register()
        {
            try
            {
                var surface = new ReflectionSurface("HospitalitySocialBarsExtender");
                Type guestUtility = surface.Type("Hospitality.Utilities.GuestUtility");
                isGuest = surface.Method(guestUtility, "IsGuest", new[] { typeof(Pawn) });
                minRecruitOpinion = surface.Method(guestUtility, "GetMinRecruitOpinion", new[] { typeof(Pawn) });
                if (surface.Ready)
                {
                    InspectNodeRegistry.RegisterCategoryExtender("Social", AddSection);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Hospitality social bars registration failed: {ex.Message}");
            }
        }

        private static void AddSection(InspectionTreeItem categoryItem, object obj)
        {
            Pawn guest = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (guest?.relations == null || guest.MapHeld == null || !(bool)isGuest.Invoke(null, new object[] { guest }))
            {
                return;
            }
            InspectNodeFactory.Section(categoryItem, "RimWorldAccess.Compat.Hospitality.FriendshipBars".Translate(), guest, section =>
            {
                float needed = (int)minRecruitOpinion.Invoke(null, new object[] { guest });
                bool guestRoyal = HospitalityCompat.HasRoyalTitle(guest);
                foreach (Pawn other in guest.MapHeld.mapPawns.FreeColonists)
                {
                    // The patch's own skips: non-royal colonists for a royal guest, and slaves.
                    if (other.IsSlave || (guestRoyal && !HospitalityCompat.HasRoyalTitle(other)))
                    {
                        continue;
                    }
                    float opinion = guest.relations.OpinionOf(other);
                    if (opinion == 0f)
                    {
                        continue;
                    }
                    string line = opinion > 0f
                        ? "RimWorldAccess.Compat.Hospitality.FriendBar".Translate(other.LabelShort, opinion.ToString("+0;-0"), Mathf.Clamp01(opinion / needed).ToStringPercent())
                        : "RimWorldAccess.Compat.Hospitality.EnemyBar".Translate(other.LabelShort, opinion.ToString("+0;-0"), Mathf.Clamp01(opinion / EnemyOpinion).ToStringPercent());
                    InspectNodeFactory.DetailLine(section, line);
                }
                if (section.Children.Count == 0)
                {
                    InspectNodeFactory.DetailLine(section, "RimWorldAccess.Compat.Hospitality.NoFriendshipBars".Translate());
                }
            });
        }
    }
}
