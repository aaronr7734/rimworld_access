using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Keeps the mod's wealth breakdown hotkey off keys that belong to our menus and text fields.
    /// The mod polls its binding with <c>KeyBindingDef.JustPressed</c> (Input.GetKeyDown) from a
    /// UIRootUpdate postfix, so the dispatcher's swallow never reaches it and Shift+4 typed as "$"
    /// would open the window over whatever owns the keyboard. The poll stands down while a shell
    /// surface or a text session owns input; on the map it works as the mod set it.
    /// </summary>
    internal static class VwHotkeyGuard
    {
        public static void Register(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method("VisibleWealth.Patch_UIRoot_Play_UIRootUpdate:Postfix");
            if (target == null)
            {
                ModLogger.Error("Visible Wealth compat: Patch_UIRoot_Play_UIRootUpdate.Postfix not found; hotkey guard inactive");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(VwHotkeyGuard), nameof(Prefix)));
        }

        public static bool Prefix()
        {
            return !ShellGuards.MenuOwnsInput() && !TextInputManager.IsActive;
        }
    }
}
