using HarmonyLib;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    internal sealed class HugsLibModule : CompatModule
    {
        public override string TargetPackageId => "UnlimitedHugs.HugsLib";

        public override bool ShouldActivate =>
            AccessTools.TypeByName("HugsLib.HugsLibController") != null;

        public override void Activate(Harmony harmony)
        {
            HugsLibCompat.Register(harmony);
            HugsLibNewsCompat.TryRegister();
            ScopeForWindow.TryRegisterGenericReaderForWindow("HugsLib.Logs.Dialog_PublishLogs");
            ScopeForWindow.TryRegisterGenericReaderForWindow("HugsLib.Quickstart.Dialog_QuickstartSettings");
        }
    }
}
