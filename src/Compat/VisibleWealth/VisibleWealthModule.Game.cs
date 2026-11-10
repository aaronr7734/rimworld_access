using HarmonyLib;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Visible Wealth (packageId <c>visiblewealth.1trickPwnyta</c>): the wealth breakdown window
    /// and its play-settings button; <see cref="ForeignHotkeyGuard"/> keeps its hotkey off shell keys.
    /// The main button and the settings page need nothing here: the
    /// F12 menu lists the mod's <c>MainButtonDef</c> whenever its own visibility allows, and the
    /// settings page is a plain <c>Listing_Standard</c> the generic settings reader covers.
    /// </summary>
    internal sealed class VisibleWealthModule : CompatModule
    {
        public override string TargetPackageId => "visiblewealth.1trickPwnyta";

        public override bool ShouldActivate =>
            ModsConfig.IsActive(TargetPackageId)
            || AccessTools.TypeByName("VisibleWealth.Dialog_WealthBreakdown") != null;

        public override void Activate(Harmony harmony)
        {
            if (!VwCompat.Ready)
            {
                return;
            }
            ScopeForWindow.Register(VwCompat.DialogType, w => new VisibleWealthScope(w));
            VwDrawPatch.Install(harmony);
            VwHudToolbar.Register();
        }
    }
}
