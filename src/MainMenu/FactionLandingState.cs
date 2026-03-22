using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation state for Dialog_FactionDuringLanding.
    /// Provides a treeview of factions with expandable sections and typeahead search,
    /// opened via F key during starting site selection.
    /// </summary>
    public static class FactionLandingState
    {
        public static bool IsActive { get; private set; }

        /// <summary>
        /// Frame number when we last handled Escape to close the dialog.
        /// Used by FactionLandingPatch.Page_OnCancelKeyPressed_Patch to block
        /// the game's Cancel handling in the same frame (since Event.current.Use()
        /// does not prevent HandleEventsHighPriority from firing).
        /// </summary>
        internal static int escapeHandledOnFrame = -1;

        private static Dialog_FactionDuringLanding currentDialog;
        private static FactionTreeNavigation navigation = new FactionTreeNavigation();

        /// <summary>
        /// Opens the faction landing state for a dialog.
        /// Called from FactionLandingPatch when Dialog_FactionDuringLanding opens.
        /// </summary>
        public static void Open(Dialog_FactionDuringLanding dialog)
        {
            if (dialog == null)
                return;

            currentDialog = dialog;
            IsActive = true;

            // Prevent RimWorld from closing on Enter/Escape — we handle both
            dialog.closeOnAccept = false;
            dialog.closeOnCancel = false;
            // Prevent the dialog from stealing Unity IMGUI keyboard focus.
            // Page_SelectStartingSite has InitialSize=Vector2.zero, so when this dialog
            // closes, TryRemove tries to GUI.FocusWindow on the zero-sized page, which
            // corrupts Unity's focus chain. Since we handle all input via
            // UnifiedKeyboardPatch (outside GUI.Window), this dialog doesn't need focus.
            dialog.focusWhenOpened = false;

            List<Faction> factions = FactionHelper.BuildFactionList();
            navigation.Initialize(factions);
        }

        /// <summary>
        /// Closes the faction landing state and resets all fields.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            currentDialog = null;
            navigation.Reset();
        }

        /// <summary>
        /// Handles keyboard input for the faction landing dialog.
        /// Returns true if input was handled.
        /// Called from UnifiedKeyboardPatch which handles Event.current.Use().
        /// </summary>
        public static bool HandleInput(Event ev)
        {
            if (!IsActive || ev.type != EventType.KeyDown)
                return false;

            // Delegate to shared tree navigation
            if (navigation.HandleInput(ev))
                return true;

            // Escape with no active search — close dialog
            if (ev.keyCode == KeyCode.Escape)
            {
                CloseDialog();
                return true;
            }

            return true;
        }

        #region Private Methods

        /// <summary>
        /// Closes the dialog via WindowStack and announces closure.
        /// </summary>
        private static void CloseDialog()
        {
            escapeHandledOnFrame = Time.frameCount;
            if (currentDialog != null)
            {
                Find.WindowStack.TryRemove(currentDialog, doCloseSound: false);
            }
            Close();
            TolkHelper.Speak("Faction relations closed.");
        }

        #endregion
    }
}
