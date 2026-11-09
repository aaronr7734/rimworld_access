using HarmonyLib;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Tech Advancing. Its config window is modal and rides the generic reader, but three of its
    /// surfaces draw outside any vanilla widget: the window's prose (raw GUI.Label), the main-menu
    /// settings page (raw GUI.Label, TextArea and Button), and the window's only in-game entry
    /// point, an icon button the mod patches onto the research tab that our research menu cannot
    /// see, mirrored here as a <see cref="CompatScreenActions"/> row.
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

            TechAdvancingCompat.InstallCapture(harmony);

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
