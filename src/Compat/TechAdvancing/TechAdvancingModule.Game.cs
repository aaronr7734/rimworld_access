using HarmonyLib;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Tech Advancing. Its config window and settings page ride the generic reader, raw GUI calls
    /// included. The window's only in-game entry point, an icon button the mod patches onto the
    /// research tab that our research menu cannot see, is mirrored here as a
    /// <see cref="CompatScreenActions"/> row.
    /// </summary>
    internal sealed class TechAdvancingModule : CompatModule
    {
        public override string TargetPackageId => "GHXX.TechAdvancing";

        public override bool ShouldActivate =>
            AccessTools.TypeByName("TechAdvancing.TechAdvancing_Config_Tab") != null;

        public override void Activate(Harmony harmony)
        {
            if (!TechAdvancingCompat.Gate.Ensure())
            {
                return;
            }

            // The button sits on the research window's left pane, visible from either view.
            CompatScreenActions.Register("research-menu", ConfigAction);
            CompatScreenActions.Register("research-detail", ConfigAction);
        }

        private static ScreenAction ConfigAction()
        {
            if (!TechAdvancingCompat.CanOpenConfig())
            {
                return null;
            }
            return new ScreenAction(CompatText.ModText("TAcfgmenulabel"), TechAdvancingCompat.OpenConfig);
        }
    }
}
