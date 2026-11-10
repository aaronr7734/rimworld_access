using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Keeps the mod's wealth breakdown hotkey off keys that belong to our menus, text fields and
    /// map keys. The mod polls its bindings with <c>KeyBindingDef.JustPressed</c> from a
    /// UIRootUpdate postfix, so the dispatcher never sees it: Shift+4 typed as "$" would open the
    /// window over whatever owns the keyboard, and on the map the same press would also fire our
    /// own Shift+4 claim (dev-mode ultrafast). The poll stands down whenever a shell surface owns
    /// input or a live shell claim takes the chord just pressed.
    /// </summary>
    internal static class VwHotkeyGuard
    {
        private static KeyBindingDef plainBinding;
        private static KeyBindingDef shiftBinding;

        public static void Register(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method("VisibleWealth.Patch_UIRoot_Play_UIRootUpdate:Postfix");
            if (target == null)
            {
                ModLogger.Error("Visible Wealth compat: Patch_UIRoot_Play_UIRootUpdate.Postfix not found; hotkey guard inactive");
                return;
            }
            plainBinding = DefDatabase<KeyBindingDef>.GetNamedSilentFail("VisibleWealth_WealthBreakdown");
            shiftBinding = DefDatabase<KeyBindingDef>.GetNamedSilentFail("VisibleWealth_WealthBreakdownShift");

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(VwHotkeyGuard), nameof(Prefix)));
        }

        public static bool Prefix()
        {
            if (ShellGuards.MenuOwnsInput() || TextInputManager.IsActive)
            {
                return false;
            }
            return !ShellClaimsPress(plainBinding) && !ShellClaimsPress(shiftBinding);
        }

        private static bool ShellClaimsPress(KeyBindingDef def)
        {
            if (def == null || !KeyPrefs.KeyPrefsData.keyPrefs.TryGetValue(def, out KeyBindingData data))
            {
                return false;
            }
            return ShellClaimsKey(data.keyBindingA) || ShellClaimsKey(data.keyBindingB);
        }

        private static bool ShellClaimsKey(KeyCode key)
        {
            if (key == KeyCode.None || !Input.GetKeyDown(key))
            {
                return false;
            }
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            return FocusStack.ClaimedLiveBelowModals(
                KeyChord.Of(key, ctrl: KeyboardHelper.IsCtrlHeld, shift: shift, alt: KeyboardHelper.IsAltHeld));
        }
    }
}
