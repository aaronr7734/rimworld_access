using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed class AllowToolModule : CompatModule
    {
        public override string TargetPackageId => "unlimitedhugs.allowtool";

        public override bool ShouldActivate =>
            AccessTools.TypeByName("AllowTool.Designator_UnlimitedDragger") != null;

        public override void Activate(Harmony harmony)
        {
            AllowToolCompat.RegisterRectDesignationHandler();
            AllowToolCompat.RegisterContextMenuProvider();
            AllowToolMenuOptionFacets.Register(harmony);
        }
    }
}
