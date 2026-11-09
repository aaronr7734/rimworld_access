using HarmonyLib;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    internal sealed class AnimalTraitsModule : CompatModule
    {
        public override string TargetPackageId => "luved.animaltraits";

        public override void Activate(Harmony harmony)
        {
            AnimalTraitsSpareColumnHandler.Register();
            AnimalTraitsTraitColumnHandler.Register();
            // Opened over the absorbing mod-settings dialog, but absorbs no input itself.
            ScopeForWindow.TryRegisterGenericReaderForWindow("AnimalTraitExtension.Window_TraitConfiguration");
        }
    }
}
