using RimWorld.Planet;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The wealth breakdown button the mod adds to the play-settings strip
    /// (<c>Patch_PlaySettings</c>), a bare icon outside any window, offered through the shared
    /// mod-toolbar menu in the pause menu.
    /// </summary>
    internal static class VwHudToolbar
    {
        private const string ToolbarId = "visiblewealth.1trickPwnyta.playSettings"; // l10n-exempt: registry id
        private const string PackageId = "visiblewealth.1trickPwnyta"; // l10n-exempt: package id

        public static void Register()
        {
            ModHudToolbarRegistry.Register(ToolbarId, Title, Visible);
            ModHudToolbarRegistry.AddEntry(ToolbarId, Label, VwCompat.OpenBreakdown);
        }

        private static string Title()
        {
            return CompatText.ModTitle(PackageId);
        }

        /// <summary>The postfix's own draw gate: the setting on and a map in view.</summary>
        private static bool Visible()
        {
            return Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null
                && !WorldRendererUtility.WorldSelected && VwCompat.PlaySettingsButton;
        }

        /// <summary>The icon's tooltip: the window's name and the hotkey the postfix names, plain binding first.</summary>
        private static string Label()
        {
            string name = CompatText.ModText("VisibleWealth_WealthBreakdown");
            string hotkey = Hotkey();
            return hotkey == null ? name : (string)"RimWorldAccess.Compat.VisibleWealth.OpenWithHotkey".Translate(name, hotkey);
        }

        private static string Hotkey()
        {
            string plain = SlotALabel("VisibleWealth_WealthBreakdown");
            if (plain != null)
            {
                return plain;
            }
            string shifted = SlotALabel("VisibleWealth_WealthBreakdownShift");
            return shifted != null
                ? (string)"RimWorldAccess.Compat.VisibleWealth.ShiftChord".Translate(shifted)
                : null;
        }

        private static string SlotALabel(string defName)
        {
            return VanillaBindings.HotkeyLabel(DefDatabase<KeyBindingDef>.GetNamedSilentFail(defName), KeyPrefs.BindingSlot.A);
        }
    }
}
