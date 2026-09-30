using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed class AdaptiveStorageModule : CompatModule
    {
        public override string TargetPackageId => "adaptive.storage.framework";

        public override void Activate(Harmony harmony)
        {
            CompatRegistration.TabAdapter("AdaptiveStorage.ContentsITab",
                t => new AdaptiveStorageContentsAdapter(t), "Adaptive Storage contents adapter");
        }
    }
}
