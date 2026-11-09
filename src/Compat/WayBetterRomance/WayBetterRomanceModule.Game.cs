using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed class WayBetterRomanceModule : CompatModule
    {
        public override string TargetPackageId => "divineDerivative.Romance";

        public override bool ShouldActivate =>
            AccessTools.TypeByName("BetterRomance.ITab_Orientation") != null;

        public override void Activate(Harmony harmony)
        {
            WayBetterRomanceOrientationTab.Register();
        }
    }
}
