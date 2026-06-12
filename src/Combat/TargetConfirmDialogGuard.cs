using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Guards against a confirmation dialog being auto-confirmed by the same Enter that opened it.
    ///
    /// When an action-based targeting confirmation (e.g. CompPlantable planting a Gauranlen seed
    /// near artificial buildings) opens a Dialog_MessageBox during the SAME Enter keypress that
    /// confirmed the target cell, that Enter leaks into the freshly-opened dialog and the game's
    /// Window.OnAcceptKeyPressed runs the dialog's acceptAction — placing the order even though the
    /// user never chose "Confirm" and may have meant to cancel.
    ///
    /// Event.current.Use() does NOT stop OnAcceptKeyPressed (see the keyboard-isolation notes in
    /// the project CLAUDE.md), so TargetingPatch marks the frame via MarkDialogOpenedThisFrame()
    /// and the prefix below blocks the game's accept for that one frame only. Subsequent frames are
    /// untouched, so the user navigates the dialog and confirms or cancels normally.
    /// </summary>
    public static class TargetConfirmDialogGuard
    {
        private static int markedFrame = -1;

        /// <summary>Records that a targeting-confirmation dialog opened on the current frame.</summary>
        public static void MarkDialogOpenedThisFrame() => markedFrame = Time.frameCount;

        /// <summary>True only during the frame a targeting-confirmation dialog opened.</summary>
        public static bool DialogOpenedThisFrame => Time.frameCount == markedFrame;
    }

    /// <summary>
    /// Blocks RimWorld's Enter-to-accept on the single frame a targeting-confirmation dialog opened,
    /// so the Enter that opened the dialog can't immediately auto-confirm it.
    /// </summary>
    [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
    public static class Window_OnAcceptKeyPressed_TargetConfirmBlock
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !TargetConfirmDialogGuard.DialogOpenedThisFrame;
        }
    }
}
