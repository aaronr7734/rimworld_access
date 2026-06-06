using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to add accessibility gizmos to zones (Rename + Storage settings).
    /// </summary>
    [HarmonyPatch(typeof(Zone))]
    [HarmonyPatch("GetGizmos")]
    public static class Zone_GetGizmos_Patch
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Zone __instance)
        {
            // Yield all original gizmos first
            foreach (var gizmo in __result)
                yield return gizmo;

            // === Gizmo: Rename zone ===
            string renameLabel = !string.IsNullOrEmpty(__instance.label)
                ? $"Rename {__instance.label}"
                : "Rename";

            var renameGizmo = new Command_Action
            {
                defaultLabel = renameLabel,
                defaultDesc = "",
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/Rename", true),
                action = delegate { ZoneRenameState.Open(__instance); }
            };
            yield return renameGizmo;

            // === Gizmo: Storage settings (only for stockpile zones) ===
            if (__instance is IStoreSettingsParent storageParent)
            {
                var settingsGizmo = new Command_Action
                {
                    defaultLabel = (string)"RimWorldAccess.Building.Shelf.StorageSettingsLabel".Translate(),
                    defaultDesc = "",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/SetTargetFuelLevel", true),
                    action = delegate
                    {
                        var settings = storageParent.GetStoreSettings();
                        if (settings != null)
                            StorageSettingsMenuState.Open(settings);
                    }
                };
                yield return settingsGizmo;
            }
        }
    }
}
