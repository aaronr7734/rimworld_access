using RimWorld.Planet;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The panic button the mod adds to the play-settings strip
    /// (<c>PlaySettings_DoPlaySettingsGlobalControls_Patch</c>), a bare icon outside any window,
    /// offered through the shared mod-toolbar menu in the pause menu.
    /// </summary>
    internal static class ClPanicToolbar
    {
        private const string ToolbarId = "Wiri.compositableloadouts.panic"; // l10n-exempt: registry id
        private const string PackageId = "Wiri.compositableloadouts"; // l10n-exempt: package id
        // The mod's patch stands down for Better Pawn Control, whose own emergency toggle drives the panic state instead.
        private const string BetterPawnControlId = "voult.betterpawncontrol"; // l10n-exempt: package id

        public static void Register()
        {
            ModHudToolbarRegistry.Register(ToolbarId, Title, Visible);
            ModHudToolbarRegistry.AddEntry(ToolbarId, Label, Toggle);
        }

        private static string Title()
        {
            return CompatText.ModTitle(PackageId);
        }

        /// <summary>The strip's own draw gate: a map in view, and Better Pawn Control absent.</summary>
        private static bool Visible()
        {
            return Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null
                && !WorldRendererUtility.WorldSelected && !ModsConfig.IsActive(BetterPawnControlId);
        }

        /// <summary>The button's own tooltip, which names what a click will do.</summary>
        private static string Label()
        {
            if (ClCompat.PanicActive())
            {
                return CompatText.Flatten(CompatText.ModText("CompositableLoadouts.DeactivatePanicState"));
            }
            object state = ClCompat.PanicState();
            string name = state != null ? ClCompat.StateName(state) : CompatText.ModText("CompositableLoadouts.DefaultStateName");
            return CompatText.Flatten(CompatText.ModArgs("CompositableLoadouts.ActivatePanicState", name));
        }

        private static void Toggle()
        {
            // MUTATION-C: mirrors PlaySettings_DoPlaySettingsGlobalControls_Patch.cs:36-38; the click body is inline in the postfix.
            ClCompat.Settings.TogglePanicMode();
            TolkHelper.Speak((ClCompat.PanicActive()
                ? "RimWorldAccess.Compat.CompositableLoadouts.PanicOn"
                : "RimWorldAccess.Compat.CompositableLoadouts.PanicOff").Loc());
        }
    }
}
