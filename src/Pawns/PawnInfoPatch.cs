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
            else if (key == KeyCode.C && Event.current.alt)
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
        /// </summary>
        private static void HandleJumpToSelectedPawn()
        {
            Pawn selectedPawn = GetSelectedPawn();
            if (selectedPawn == null)
                return;

            // Get the camera driver
            CameraDriver cameraDriver = Find.CameraDriver;
            if (cameraDriver == null)
                return;

            // Move camera to center on the pawn
            IntVec3 pawnPosition = selectedPawn.Position;
            cameraDriver.JumpToCurrentMapLoc(pawnPosition);

            // Update map navigation cursor to match
            MapNavigationState.CurrentCursorPosition = pawnPosition;

            // Announce
            string announcement = $"Jumped to {selectedPawn.LabelShort}";
            TolkHelper.Speak(announcement);
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
                    info = "Unknown info type";
                    break;
            }

            TolkHelper.Speak(info);
        }

        /// <summary>
        /// Gets the currently selected pawn, if any.
        /// Returns null and announces appropriate message if no pawn is selected.
        /// </summary>
        private static Pawn GetSelectedPawn()
        {
            if (Find.Selector == null || Find.Selector.NumSelected == 0)
            {
                TolkHelper.Speak("No pawn selected");
                return null;
            }

            Pawn selectedPawn = Find.Selector.FirstSelectedObject as Pawn;
            if (selectedPawn == null)
            {
                TolkHelper.Speak("Selected object is not a pawn");
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
