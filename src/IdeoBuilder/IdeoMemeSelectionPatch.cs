using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Patches for Dialog_ChooseMemes (structure + normal meme picker).
    ///
    /// We follow the same pattern as IdeoBuilderHubPatch — Prefix on DoWindowContents
    /// handles keyboard directly because pre-game windows are unreliable through
    /// UnifiedKeyboardPatch. We also block vanilla's OnAcceptKeyPressed and
    /// OnCancelKeyPressed so Enter and Escape don't trigger TryAccept / Close before
    /// our state can handle them.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ChooseMemes), "DoWindowContents")]
    public static class IdeoMemeSelectionPatch
    {
        static bool Prefix(Dialog_ChooseMemes __instance, Rect rect)
        {
            try
            {
                if (WindowlessFloatMenuState.IsActive)
                    return false;
                if (TextInputManager.Active != null)
                    return false;
                // A confirmation/message box (e.g. "changing memes randomizes precepts") owns input.
                if (WindowlessDialogState.IsActive || WindowlessConfirmationState.IsActive)
                    return true;

                IdeoMemeSelectionState.EnsureOpen(__instance);

                if (Event.current.type == EventType.KeyDown)
                {
                    if (IdeoMemeSelectionState.HandleInput(Event.current))
                        Event.current.Use();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in IdeoMemeSelectionPatch.Prefix: {ex}");
            }
            return true; // run original so visuals render
        }
    }

    /// <summary>
    /// Block vanilla's OnAcceptKeyPressed (Enter triggers TryAccept). Enter is repurposed
    /// for tree toggle (meme leaves) or expand/collapse (group nodes); Alt+S accepts.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ChooseMemes), "OnAcceptKeyPressed")]
    public static class IdeoMemeSelectionPatch_OnAccept
    {
        [HarmonyPrefix]
        static bool Prefix()
        {
            return !IdeoMemeSelectionState.IsActive;
        }
    }

    /// <summary>
    /// Block vanilla's OnCancelKeyPressed (Escape triggers Window.Close when closeOnCancel
    /// is true, which Dialog_ChooseMemes sets based on whether the ideo already has memes).
    /// Escape is repurposed for our "Back" (which chains correctly into the structure picker
    /// when needed) and for clearing the typeahead.
    /// </summary>
    [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
    public static class IdeoMemeSelectionPatch_OnCancel
    {
        [HarmonyPrefix]
        static bool Prefix(Window __instance)
        {
            if (__instance is Dialog_ChooseMemes && IdeoMemeSelectionState.IsActive)
                return false;
            return true;
        }
    }

    /// <summary>
    /// Restores IMGUI keyboard focus to the meme dialog when it opens. A Dialog_ChooseMemes
    /// opened mid-keystroke — structure -> normal via TryAccept, or Back chaining normal ->
    /// structure — does not reliably become the focused window, so WindowStack.GetsInput treats
    /// it as not receiving input and consumes every key before our DoWindowContents prefix can
    /// run (the screen appears locked). Grabbing focus here is the same fix the world-gen pages
    /// use (see IdeologySelectionPatch_PostOpen). PostOpen is declared on Window, so we patch it
    /// there and filter by instance type.
    /// </summary>
    [HarmonyPatch(typeof(Window), "PostOpen")]
    public static class IdeoMemeSelectionPatch_PostOpen
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ChooseMemes)
                Find.WindowStack.Notify_ManuallySetFocus(__instance);
        }
    }

    /// <summary>
    /// Resets the meme-picker state when the dialog closes.
    /// </summary>
    [HarmonyPatch(typeof(Window), "PostClose")]
    public static class IdeoMemeSelectionPatch_PostClose
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ChooseMemes)
                IdeoMemeSelectionState.Close();
        }
    }
}
