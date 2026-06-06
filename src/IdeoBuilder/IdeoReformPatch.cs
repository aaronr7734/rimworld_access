using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Patches for the in-game two-stage reform dialog (Dialog_ReformIdeo). Mirrors the hub
    /// patch pattern: a Prefix on DoWindowContents handles keyboard, yielding to the meme
    /// picker, windowless float menus, modal text input, and the shared overlay editors
    /// (precept / typed-precept / deity) when any of those own the keyboard.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ReformIdeo), "DoWindowContents")]
    public static class IdeoReformPatch
    {
        private static bool memeWasOpen;
        private static bool overlayWasOpen;
        private static readonly HostFocusReturn infoCardFocus = new HostFocusReturn();

        static bool Prefix(Dialog_ReformIdeo __instance)
        {
            try
            {
                IdeoReformState.EnsureOpen(__instance);

                // Reclaim IMGUI focus when an info card (Alt+I) opened over the dialog closes —
                // must run in the dialog's own GUI.Window pass to take effect.
                infoCardFocus.Track(__instance);

                // Modal text input owns all keys.
                if (TextInputManager.Active != null)
                    return true;
                // A confirmation / message box (e.g. confirm reform changes) owns input.
                if (WindowlessDialogState.IsActive || WindowlessConfirmationState.IsActive)
                    return true;

                // The meme picker (Dialog_ChooseMemes) opens on top during stage 1. Yield to it
                // and refresh our sections when it closes (memes may have changed).
                if (Find.WindowStack.WindowOfType<Dialog_ChooseMemes>() != null)
                {
                    memeWasOpen = true;
                    // Swallow Tab so the reform dialog (drawn beneath the meme picker) can't cycle
                    // IMGUI focus to its own controls and steal focus from the picker.
                    if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Tab)
                        Event.current.Use();
                    return true;
                }
                if (memeWasOpen)
                {
                    memeWasOpen = false;
                    // Reclaim IMGUI focus now that the meme picker closed (otherwise the reform
                    // dialog is dead to input — the builder-wide focus-loss fix).
                    Find.WindowStack.Notify_ManuallySetFocus(__instance);
                    IdeoReformState.RefreshSections();
                }

                bool floatMenuOpen = WindowlessFloatMenuState.IsActive;

                // Overlay editors (precept / typed-precept / deity / appearance) operating on the
                // working copy.
                if (IdeoBuilderOverlays.AnyActive)
                {
                    overlayWasOpen = true;
                    if (floatMenuOpen)
                    {
                        IdeoBuilderOverlays.NoteFloatMenuOpen();
                        return true;
                    }
                    IdeoBuilderOverlays.RefreshIfReturnedFromFloatMenu();
                    if (Event.current.type == EventType.KeyDown && IdeoBuilderOverlays.RouteKeyDown(Event.current))
                        Event.current.Use();
                    return true;
                }

                // Just returned from an overlay editor — its edits may have changed the working
                // copy, so rebuild the stage-2 section summaries and re-announce (parity with the
                // hub, which rebuilds on sub-editor return).
                if (overlayWasOpen)
                {
                    overlayWasOpen = false;
                    IdeoReformState.RefreshSections();
                }

                // A bare float menu (e.g. the stage-1 style picker) owns the keyboard.
                if (floatMenuOpen)
                {
                    IdeoBuilderOverlays.NoteFloatMenuOpen();
                    return true;
                }
                IdeoBuilderOverlays.RefreshIfReturnedFromFloatMenu();

                if (Event.current.type == EventType.KeyDown)
                {
                    if (IdeoReformState.HandleInput(Event.current))
                        Event.current.Use();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in IdeoReformPatch.Prefix: {ex}");
            }
            return true; // run original so visuals render
        }
    }

    [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
    public static class IdeoReformPatch_OnAccept
    {
        [HarmonyPrefix]
        static bool Prefix(Window __instance)
        {
            if (__instance is Dialog_ReformIdeo && IdeoReformState.IsActive)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
    public static class IdeoReformPatch_OnCancel
    {
        [HarmonyPrefix]
        static bool Prefix(Window __instance)
        {
            if (__instance is Dialog_ReformIdeo && IdeoReformState.IsActive)
                return false;
            return true;
        }
    }

    /// <summary>
    /// Grabs IMGUI keyboard focus when the reform dialog opens, so it receives keystrokes even
    /// when opened mid-keystroke (the builder-wide focus fix; see IdeoMemeSelectionPatch_PostOpen).
    /// </summary>
    [HarmonyPatch(typeof(Window), "PostOpen")]
    public static class IdeoReformPatch_PostOpen
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ReformIdeo)
                Find.WindowStack.Notify_ManuallySetFocus(__instance);
        }
    }

    [HarmonyPatch(typeof(Window), "PostClose")]
    public static class IdeoReformPatch_PostClose
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ReformIdeo)
                IdeoReformState.Close();
        }
    }
}
