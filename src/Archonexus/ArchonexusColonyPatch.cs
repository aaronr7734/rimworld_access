using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Patches for the Archonexus relocation selection screen
    /// (Dialog_ChooseThingsForNewColony). Routes input to ArchonexusColonyState and
    /// keeps the dialog's escape hatch open: vanilla OnCancelKeyPressed is only
    /// intercepted to clear an active typeahead search; otherwise it closes the
    /// dialog and fires the questline's cancel callback as it should.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ChooseThingsForNewColony), "DoWindowContents")]
    public static class ArchonexusColonyPatch
    {
        // Reclaim IMGUI focus when the Alt+I info card opened over this dialog closes. This dialog
        // drives input from its own DoWindowContents pass, so it only receives KeyDown while
        // focused; without this it stays dead to the keyboard after the card closes (same root
        // cause as the ideoligion load picker — see HostFocusReturn). The confirmation prompt does
        // not need tracking here: it is intercepted into a windowless dialog and never takes a window.
        private static readonly HostFocusReturn childFocus = new HostFocusReturn();

        static bool Prefix(Window __instance)
        {
            try
            {
                if (!(__instance is Dialog_ChooseThingsForNewColony d))
                    return true;

                ArchonexusColonyState.EnsureOpen(d);

                // Must run every frame in the dialog's own GUI pass for the focus reclaim
                // to take effect (see HostFocusReturn).
                childFocus.Track(d);

                // An overlay owns the keyboard — yield so we don't also process the keys:
                //  - A windowless confirmation prompt. The accept-confirmation Dialog_MessageBox is
                //    intercepted by DialogInterceptionPatch into WindowlessDialogState, so there is
                //    no message-box window to detect; we must check the state flags.
                //  - The Alt+I info card (a real Dialog_InfoCard window, routed by UnifiedKeyboardPatch).
                // childFocus.Track restores focus when a tracked child window closes.
                if (WindowlessDialogState.IsActive || WindowlessConfirmationState.IsActive)
                    return true;
                if (Find.WindowStack != null && Find.WindowStack.WindowOfType<Dialog_InfoCard>() != null)
                    return true;

                if (Event.current.type == EventType.KeyDown)
                {
                    if (ArchonexusColonyState.HandleInput(Event.current))
                        Event.current.Use();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in ArchonexusColonyPatch.Prefix: {ex}");
            }
            return true;
        }
    }

    /// <summary>
    /// Block vanilla Window.OnAcceptKeyPressed for this dialog: its base behavior
    /// is Close+Use, which would discard selections silently when Enter is pressed.
    /// Our state handles Enter as Accept (running ConfirmArchonexusSettlementConsequences).
    /// </summary>
    [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
    public static class ArchonexusColonyPatch_OnAccept
    {
        [HarmonyPrefix]
        static bool Prefix(Window __instance)
        {
            if (__instance is Dialog_ChooseThingsForNewColony && ArchonexusColonyState.IsActive)
                return false;
            return true;
        }
    }

    /// <summary>
    /// Intercept vanilla OnCancelKeyPressed only when a typeahead search is active —
    /// then our state clears the search. Otherwise let vanilla close the dialog and
    /// fire the questline's cancel callback (closeOnCancel + the dialog's own override
    /// already wire that up correctly).
    /// </summary>
    [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
    public static class ArchonexusColonyPatch_OnCancel
    {
        [HarmonyPrefix]
        static bool Prefix(Window __instance)
        {
            if (__instance is Dialog_ChooseThingsForNewColony && ArchonexusColonyState.IsActive && ArchonexusColonyState.HasActiveSearch)
                return false;
            return true;
        }
    }

    /// <summary>
    /// Grabs IMGUI keyboard focus when the dialog opens. The dialog sets
    /// forceCatchAcceptAndCancelEventEvenIfUnfocused, but our DoWindowContents
    /// prefix still needs to receive raw KeyDown events, which only flow to the
    /// focused window in RimWorld's window stack.
    ///
    /// We patch the dialog's OWN PostOpen, not Window.PostOpen, because the
    /// dialog overrides PostOpen and does NOT call base.PostOpen() — so a patch
    /// on Window.PostOpen never fires for this dialog. (See feedback memory
    /// on Harmony inherited-method patching: this is safe precisely because
    /// the subtype declares its own override.) The first open often grabs focus
    /// naturally; on Escape + re-accept-quest the new instance previously did
    /// NOT regain focus, leaving the dialog dead to keystrokes.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ChooseThingsForNewColony), "PostOpen")]
    public static class ArchonexusColonyPatch_PostOpen
    {
        [HarmonyPostfix]
        static void Postfix(Dialog_ChooseThingsForNewColony __instance)
        {
            Find.WindowStack.Notify_ManuallySetFocus(__instance);
        }
    }

    [HarmonyPatch(typeof(Window), "PostClose")]
    public static class ArchonexusColonyPatch_PostClose
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ChooseThingsForNewColony)
                ArchonexusColonyState.Close();
        }
    }
}
