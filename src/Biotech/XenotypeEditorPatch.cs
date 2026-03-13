using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_CreateXenotype to enable keyboard accessibility.
    /// Activates XenotypeEditorState when the dialog opens.
    /// Close and OnCancelKeyPressed handling is in XenogermPatch (shared patches).
    /// </summary>
    public static class XenotypeEditorPatch
    {
        /// <summary>
        /// Postfix patch for Dialog_CreateXenotype.PostOpen to activate keyboard navigation.
        /// This catches all entry points: the "Xenotype Editor" button on Page_ConfigureStartingPawns,
        /// the context menu "Xenotype editor..." option, and any other code that opens this dialog.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_CreateXenotype), "PostOpen")]
        public static class PostOpen_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Dialog_CreateXenotype __instance)
            {
                try
                {
                    XenotypeEditorState.Open(__instance);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimWorld Access] Error in XenotypeEditorPatch.PostOpen: {ex.Message}");
                }
            }
        }
    }
}
