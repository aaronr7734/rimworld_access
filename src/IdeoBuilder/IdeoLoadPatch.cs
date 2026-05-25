using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Patches for the saved-ideoligion load picker (Dialog_IdeoList_Load). Yields to the
    /// windowless confirmation dialog (delete confirmation) and otherwise routes input to
    /// IdeoLoadState.
    /// </summary>
    // DoWindowContents is declared on Dialog_FileList (not on Dialog_IdeoList_Load), so we
    // patch the base method and filter to our dialog. __instance is typed as Window so the
    // patch is harmless for the other Dialog_FileList subclasses (save/load game pickers).
    [HarmonyPatch(typeof(Dialog_FileList), "DoWindowContents")]
    public static class IdeoLoadPatch
    {
        static bool Prefix(Window __instance)
        {
            try
            {
                if (!(__instance is Dialog_IdeoList_Load loadDialog))
                    return true;

                IdeoLoadState.EnsureOpen(loadDialog);

                // A confirmation dialog (delete) or message box is on top — let it own input.
                if (WindowlessDialogState.IsActive || WindowlessConfirmationState.IsActive)
                    return true;

                if (Event.current.type == EventType.KeyDown)
                {
                    if (IdeoLoadState.HandleInput(Event.current))
                        Event.current.Use();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in IdeoLoadPatch.Prefix: {ex}");
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
    public static class IdeoLoadPatch_OnCancel
    {
        [HarmonyPrefix]
        static bool Prefix(Window __instance)
        {
            if (__instance is Dialog_IdeoList_Load && IdeoLoadState.IsActive)
                return false;
            return true;
        }
    }

    /// <summary>
    /// Grabs IMGUI keyboard focus when the load picker opens, so it receives keystrokes even when
    /// opened mid-keystroke (the builder-wide focus fix; see IdeoMemeSelectionPatch_PostOpen).
    /// </summary>
    [HarmonyPatch(typeof(Window), "PostOpen")]
    public static class IdeoLoadPatch_PostOpen
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_IdeoList_Load)
                Find.WindowStack.Notify_ManuallySetFocus(__instance);
        }
    }

    [HarmonyPatch(typeof(Window), "PostClose")]
    public static class IdeoLoadPatch_PostClose
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_IdeoList_Load)
                IdeoLoadState.Close();
        }
    }
}
