using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed class AlphaMemesModule : CompatModule
    {
        public override string TargetPackageId => "Sarg.AlphaMemes";

        public override void Activate(Harmony harmony)
        {
            AlphaMemesDialogCompat.RegisterDialogScopes();
            AlphaMemesFuneralStuffCompat.Register();
        }
    }
}
