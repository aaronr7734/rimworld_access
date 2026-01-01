using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Page_ModsConfig to add keyboard accessibility.
    /// </summary>
    [HarmonyPatch]
    public static class ModListPatch
    {
        /// <summary>
        /// Patch PreOpen to initialize our state when the mod list opens.
        /// Page_ModsConfig overrides PreOpen, not PostOpen.
        /// </summary>
        [HarmonyPatch(typeof(Page_ModsConfig), "PreOpen")]
        [HarmonyPostfix]
        public static void PreOpen_Postfix(Page_ModsConfig __instance)
        {
            ModListState.Open(__instance);
        }

        /// <summary>
        /// Patch OnCloseRequest to clean up our state when the mod list closes.
        /// Page_ModsConfig doesn't override PostClose, so we use OnCloseRequest.
        /// </summary>
        [HarmonyPatch(typeof(Page_ModsConfig), "OnCloseRequest")]
        [HarmonyPostfix]
        public static void OnCloseRequest_Postfix()
        {
            ModListState.Close();
        }

        /// <summary>
        /// Patch DoWindowContents to intercept keyboard input before the game processes it.
        /// We use a Prefix so we can consume events before the game's built-in Up/Down handling.
        /// </summary>
        [HarmonyPatch(typeof(Page_ModsConfig), "DoWindowContents")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        public static void DoWindowContents_Prefix(Page_ModsConfig __instance, Rect rect)
        {
            if (!ModListState.IsActive) return;

            // Handle our keyboard input
            if (ModListState.HandleInput())
            {
                // Consume the event to prevent default handling
                Event.current.Use();
            }
        }
    }
}
