using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    internal sealed class VseModule : CompatModule
    {
        public override string TargetPackageId => "vanillaexpanded.skills";

        public override bool ShouldActivate =>
            ModsConfig.IsActive(TargetPackageId)
            || AccessTools.TypeByName("VSE.ExpertiseTracker") != null;

        public override void Activate(Harmony harmony)
        {
            VseExpertiseCompat.Register(harmony);
        }
    }
}
