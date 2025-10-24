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

            LoggerInstance.Msg("Applying Harmony patches...");
            harmony.PatchAll();

            // Log which patches were applied
            var patchedMethods = harmony.GetPatchedMethods();
            int patchCount = 0;
            foreach (var method in patchedMethods)
            {
                patchCount++;
                LoggerInstance.Msg($"Patched: {method.DeclaringType?.Name}.{method.Name}");
            }
            LoggerInstance.Msg($"Total patches applied: {patchCount}");

            LoggerInstance.Msg("RimWorld Access Mod - Main menu keyboard navigation enabled!");
            LoggerInstance.Msg("Use Arrow keys to navigate, Enter to select. Selected items copy to clipboard.");
        }
    }
}
