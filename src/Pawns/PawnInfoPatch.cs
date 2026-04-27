using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch for handling F1 (work menu) and Alt+key combinations to read pawn information.
    /// Provides hotkeys for jumping to selected pawn and reading various pawn attributes.
    /// Patches UIRootOnGUI to intercept events at the UI layer and consume them.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class PawnInfoPatch
    {
        /// <summary>
        /// Prefix patch that intercepts Alt+key combinations for pawn information accessibility.
        /// Uses Event system to properly consume events and prevent game handling.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix()
        {
            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            // Only process during normal gameplay with a valid map
            if (Find.CurrentMap == null)
                return;

            // Don't process if any dialog or window that prevents camera motion is open
            if (Find.WindowStack != null && Find.WindowStack.WindowsPreventCameraMotion)
                return;

            // Don't process if the work menu is active (except we still want to allow it to open)
            if (WorkMenuState.IsActive)
                return;

            KeyCode key = Event.current.keyCode;
            bool handled = false;

            // Handle F1: Work menu (interactive) - matches RimWorld default
            if (key == KeyCode.F1)
            {
                HandleWorkMenu();
                handled = true;
            }
            // Handle Alt+C: Jump to selected pawn
            else if (key == KeyCode.C && KeyboardHelper.IsAltHeld)
            {
                HandleJumpToSelectedPawn();
                handled = true;
            }
            // Note: Alt+H, Alt+N, Alt+G, Alt+S, Alt+T, Alt+R shortcuts have been removed.
            // All pawn information is now accessible through the 'i' key inspection menu (WindowlessInspectionState).

            // If we handled the key, consume the event to prevent game processing
            if (handled)
            {
                Event.current.Use();
            }
        }

        /// <summary>
        /// Handles Alt+C: Jump camera to the currently selected pawn.
        /// In multi-select mode, opens a pawn picker menu to choose which pawn to jump to.
        /// </summary>
        private static void HandleJumpToSelectedPawn()
        {
            // Multi-select: open pawn picker menu
            if (MultiSelectState.IsMultiSelectActive)
            {
                MultiSelectState.ValidateAndCleanupSelection();
                var options = new List<FloatMenuOption>();
                foreach (var pawn in MultiSelectState.SelectedPawns)
                {
                    string task = pawn.GetJobReport();
                    if (string.IsNullOrEmpty(task)) task = "RimWorldAccess.Pawns.MultiSelect.Idle".Translate();
                    string label = "RimWorldAccess.Pawns.Info.JumpPickerRow".Translate(pawn.LabelShort, task);
                    var p = pawn;
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        JumpToPawn(p);
                    }));
                }
                WindowlessFloatMenuState.Open(options, false);
                return;
            }

            Pawn selectedPawn = GetSelectedPawn();
            if (selectedPawn == null)
                return;

            JumpToPawn(selectedPawn);
        }

        /// <summary>
        /// Jumps the camera and cursor to a specific pawn.
        /// </summary>
        private static void JumpToPawn(Pawn pawn)
        {
            CameraDriver cameraDriver = Find.CameraDriver;
            if (cameraDriver == null)
                return;

            IntVec3 pawnPosition = pawn.Position;
            cameraDriver.JumpToCurrentMapLoc(pawnPosition);

            MapNavigationState.CurrentCursorPosition = pawnPosition;
            MapNavigationState.CurrentCameraMode = CameraFollowMode.Cursor;

            MapNavigationState.SpeakJumpedTo(pawn.LabelShort);
        }

        /// <summary>
        /// Handles Alt+W: Opens the interactive work assignment menu.
        /// </summary>
        private static void HandleWorkMenu()
        {
            Pawn selectedPawn = GetSelectedPawn();
            if (selectedPawn == null)
                return;

            // Open the work menu
            WorkMenuState.Open(selectedPawn);
        }

        /// <summary>
        /// Handles Alt+[key] information requests for the selected pawn.
        /// </summary>
        private static void HandlePawnInfo(PawnInfoType infoType)
        {
            Pawn selectedPawn = GetSelectedPawn();
            if (selectedPawn == null)
                return;

            string info;
            switch (infoType)
            {
                case PawnInfoType.Health:
                    info = PawnInfoHelper.GetHealthInfo(selectedPawn);
                    break;
                case PawnInfoType.Needs:
                    info = PawnInfoHelper.GetNeedsInfo(selectedPawn);
                    break;
                case PawnInfoType.Gear:
                    info = PawnInfoHelper.GetGearInfo(selectedPawn);
                    break;
                case PawnInfoType.Social:
                    info = PawnInfoHelper.GetSocialInfo(selectedPawn);
                    break;
                case PawnInfoType.Training:
                    info = PawnInfoHelper.GetTrainingInfo(selectedPawn);
                    break;
                case PawnInfoType.Character:
                    info = PawnInfoHelper.GetCharacterInfo(selectedPawn);
                    break;
                case PawnInfoType.Work:
                    info = PawnInfoHelper.GetWorkInfo(selectedPawn);
                    break;
                default:
                    info = "RimWorldAccess.Pawns.Info.UnknownInfoType".Translate();
                    break;
            }

            TolkHelper.Speak(info);
        }

        /// <summary>
        /// Gets the currently selected pawn from the game selector.
        /// </summary>
        private static Pawn GetSelectedPawn()
        {
            if (Find.Selector == null || Find.Selector.NumSelected == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Info.NoPawnSelected".Translate());
                return null;
            }

            Pawn selectedPawn = Find.Selector.FirstSelectedObject as Pawn;
            if (selectedPawn == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Info.NotAPawn".Translate());
                return null;
            }

            return selectedPawn;
        }

        /// <summary>
        /// Enum for different types of pawn information that can be requested.
        /// </summary>
        private enum PawnInfoType
        {
            Health,
            Needs,
            Gear,
            Social,
            Training,
            Character,
            Work
        }
    }
}
