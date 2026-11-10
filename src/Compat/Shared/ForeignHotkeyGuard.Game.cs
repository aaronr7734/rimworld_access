using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Keeps third-party key bindings off keys the shell owns. Mods poll their own
    /// <see cref="KeyBindingDef"/>s from Update or OnGUI passes the dispatcher never sees, and both
    /// getters match the bare key whatever modifiers are held, so one press could fire a mod
    /// hotkey on top of a shell action, or behind a menu or text field. A foreign binding reads
    /// as not pressed while a shell surface owns input, a text session is live, or a live shell
    /// claim takes the exact chord pressed. Official, our own and packless defs pass untouched.
    /// IsDown and IsDownEvent stay unpatched: no mod polls a foreign press through them.
    /// </summary>
    internal static class ForeignHotkeyGuard
    {
        private static ModContentPack ownContent;

        [HarmonyPatch(typeof(KeyBindingDef), nameof(KeyBindingDef.JustPressed), MethodType.Getter)]
        public static class JustPressedPatch
        {
            [HarmonyPostfix]
            public static void Postfix(KeyBindingDef __instance, ref bool __result)
            {
                if (__result && IsForeign(__instance) && ShellOwnsPress(DownSlotKey(__instance), ShiftHeld()))
                {
                    __result = false;
                }
            }
        }

        [HarmonyPatch(typeof(KeyBindingDef), nameof(KeyBindingDef.KeyDownEvent), MethodType.Getter)]
        public static class KeyDownEventPatch
        {
            [HarmonyPostfix]
            public static void Postfix(KeyBindingDef __instance, ref bool __result)
            {
                if (__result && IsForeign(__instance) && ShellOwnsPress(Event.current.keyCode, Event.current.shift))
                {
                    __result = false;
                }
            }
        }

        private static bool IsForeign(KeyBindingDef def)
        {
            ModContentPack pack = def.modContentPack;
            if (pack == null || pack.IsOfficialMod)
            {
                return false;
            }
            if (ownContent == null)
            {
                ownContent = LoadedModManager.GetMod<RimWorldAccessMod_Settings>()?.Content;
            }
            return pack != ownContent;
        }

        private static bool ShellOwnsPress(KeyCode key, bool shift)
        {
            if (ShellGuards.MenuOwnsInput() || TextInputManager.IsActive)
            {
                return true;
            }
            if (key == KeyCode.None)
            {
                return false;
            }
            KeyEventSnapshot pressed = ShellDispatcherPatch.Snapshot(key, shift);
            return FocusStack.ClaimedLiveBelowModals(KeyChord.Of(pressed.Key, pressed.Ctrl, pressed.Shift, pressed.Alt));
        }

        private static KeyCode DownSlotKey(KeyBindingDef def)
        {
            KeyCode a = VanillaBindings.SlotKey(def, KeyPrefs.BindingSlot.A);
            return a != KeyCode.None && Input.GetKeyDown(a) ? a : VanillaBindings.SlotKey(def, KeyPrefs.BindingSlot.B);
        }

        private static bool ShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }
    }
}
