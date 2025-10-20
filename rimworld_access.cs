using MelonLoader;
using HarmonyLib;

[assembly: MelonInfo(typeof(RimWorldAccess.RimWorldAccessMod), "RimWorld Access", "1.0.0", "Your Name")]
[assembly: MelonGame("Ludeon Studios", "RimWorld by Ludeon Studios")]

namespace RimWorldAccess
{
    public class RimWorldAccessMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("RimWorld Access Mod - Initializing accessibility features...");

            // Apply Harmony patches
            var harmony = new HarmonyLib.Harmony("com.rimworldaccess.mainmenukeyboard");
            harmony.PatchAll();

            LoggerInstance.Msg("RimWorld Access Mod - Main menu keyboard navigation enabled!");
            LoggerInstance.Msg("Use Arrow keys to navigate, Enter to select. Selected items copy to clipboard.");
        }
    }
}
