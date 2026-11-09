using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    internal sealed class VefModule : CompatModule
    {
        public override string TargetPackageId => "OskarPotocki.VanillaFactionsExpanded.Core";

        // MVCF is probed separately: VefGizmoCompat registers an MVCF handler, and MVCF can be
        // loaded without the framework.
        public override bool ShouldActivate =>
            ModsConfig.IsActive(TargetPackageId)
            || AccessTools.TypeByName("VEF.Abilities.Command_Ability") != null
            || AccessTools.TypeByName("MVCF.Commands.Command_VerbTargetExtended") != null;

        public override void Activate(Harmony harmony)
        {
            VefGizmoCompat.RegisterGizmoHandlers();
            VefProcessorTabCompat.RegisterTabAdapters();
            VefDialogCompat.RegisterDialogScopes();
            VefOutpostItemsAdapter.PatchClose(harmony);
            VefOutpostInspectCompat.Register();
        }
    }
}
