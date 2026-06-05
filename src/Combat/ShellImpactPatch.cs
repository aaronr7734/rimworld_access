using HarmonyLib;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Announces mortar shell impacts for accessibility.
    /// </summary>
    [HarmonyPatch(typeof(Projectile_Explosive))]
    [HarmonyPatch("Explode")]
    public static class ShellImpactPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Projectile_Explosive __instance)
        {
            // Only announce overhead projectiles (mortars, artillery)
            if (__instance.def?.projectile == null || !__instance.def.projectile.flyOverhead)
                return;

            // Only announce on player's current map
            if (__instance.Map != Find.CurrentMap)
                return;

            IntVec3 pos = __instance.Position;

            // Check what's at the impact site
            string target = "";
            var things = pos.GetThingList(__instance.Map);
            foreach (var thing in things)
            {
                if (thing is Pawn pawn)
                {
                    target = pawn.LabelShort;
                    break;
                }
                if (thing is Building building && building.def.building != null)
                {
                    target = building.LabelShort;
                    break;
                }
            }

            // Determine source
            string sourceKey = "RimWorldAccess.Combat.Shell.SourceDefault";
            if (__instance.Launcher?.Faction == Faction.OfPlayer)
            {
                sourceKey = "RimWorldAccess.Combat.Shell.SourceOurs";
            }
            else if (__instance.Launcher?.Faction != null && __instance.Launcher.Faction.HostileTo(Faction.OfPlayer))
            {
                sourceKey = "RimWorldAccess.Combat.Shell.SourceEnemy";
            }
            string source = sourceKey.Translate();

            // Build announcement
            string announcement = string.IsNullOrEmpty(target)
                ? "RimWorldAccess.Combat.Shell.Landed".Translate(source, pos.x, pos.z).ToString()
                : "RimWorldAccess.Combat.Shell.Hit".Translate(source, target, pos.x, pos.z).ToString();

            TolkHelper.SpeakData(announcement, SpeechPriority.High);
        }
    }
}
