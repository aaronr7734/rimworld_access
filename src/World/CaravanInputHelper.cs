using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared input handling helpers for caravan formation and splitting dialogs.
    /// Contains handlers for common keyboard shortcuts.
    /// </summary>
    public static class CaravanInputHelper
    {
        /// <summary>
        /// Handles Alt+H (health), Alt+M (mood), Alt+N (needs), Alt+K (skills) pawn info shortcuts.
        /// </summary>
        /// <param name="key">The key pressed</param>
        /// <param name="selectedPawn">The currently selected pawn (can be null)</param>
        /// <param name="alt">Whether Alt is held</param>
        /// <param name="shift">Whether Shift is held</param>
        /// <param name="ctrl">Whether Ctrl is held</param>
        /// <returns>True if the key was handled, false otherwise</returns>
        public static bool HandlePawnInfoShortcuts(KeyCode key, Pawn selectedPawn, bool alt, bool shift, bool ctrl)
        {
            if (!alt || shift || ctrl)
                return false;

            switch (key)
            {
                case KeyCode.H:
                    if (selectedPawn != null)
                    {
                        TolkHelper.Speak(PawnInfoHelper.GetHealthInfo(selectedPawn));
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Caravan.Input.NoPawnSelected".Translate());
                    }
                    return true;

                case KeyCode.M:
                    if (selectedPawn != null)
                    {
                        TolkHelper.Speak(PawnInfoHelper.GetMoodInfo(selectedPawn));
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Caravan.Input.NoPawnSelected".Translate());
                    }
                    return true;

                case KeyCode.N:
                    if (selectedPawn != null)
                    {
                        TolkHelper.Speak(PawnInfoHelper.GetNeedsInfo(selectedPawn));
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Caravan.Input.NoPawnSelected".Translate());
                    }
                    return true;

                case KeyCode.G:
                    if (selectedPawn != null)
                    {
                        TolkHelper.Speak(PawnInfoHelper.GetGearInfo(selectedPawn));
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Caravan.Input.NoPawnSelected".Translate());
                    }
                    return true;

                case KeyCode.K:
                    if (selectedPawn != null)
                    {
                        TolkHelper.Speak(PawnInfoHelper.GetTopSkillsInfo(selectedPawn));
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Caravan.Input.NoPawnSelected".Translate());
                    }
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Handles the Delete key to remove items from the caravan.
        /// </summary>
        /// <param name="transferable">The selected transferable</param>
        /// <param name="isPawnTab">Whether we're on the Pawns tab</param>
        /// <param name="isSuppliesTabLocked">Whether the supplies tab is locked (auto-provision)</param>
        /// <param name="notifyChanged">Callback to notify the dialog of changes</param>
        /// <returns>True if handled, false if nothing to remove</returns>
        public static bool HandleDeleteKey(
            TransferableOneWay transferable,
            bool isPawnTab,
            bool isSuppliesTabLocked,
            Action notifyChanged)
        {
            if (transferable == null)
                return false;

            int currentCount = transferable.CountToTransfer;
            string itemName = transferable.LabelCap.StripTags();

            if (isPawnTab)
            {
                // For pawns, deselect all of this type
                if (currentCount > 0)
                {
                    transferable.AdjustTo(0);
                    notifyChanged?.Invoke();
                    string msg = currentCount == 1
                        ? (string)"RimWorldAccess.Caravan.Input.RemovedSingle".Translate(itemName)
                        : (string)"RimWorldAccess.Caravan.Input.RemovedAllOf".Translate(currentCount, itemName);
                    TolkHelper.Speak(msg);
                    return true;
                }
                else
                {
                    MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Zero);
                    return true;
                }
            }
            else
            {
                // For items
                if (isSuppliesTabLocked)
                {
                    TolkHelper.Speak("RimWorldAccess.Caravan.Form.SuppliesTabLocked".Translate());
                    return true;
                }

                if (currentCount > 0)
                {
                    transferable.AdjustTo(0);
                    notifyChanged?.Invoke();
                    string msg = currentCount == 1
                        ? (string)"RimWorldAccess.Caravan.Input.RemovedSingle".Translate(itemName)
                        : (string)"RimWorldAccess.Caravan.Input.RemovedAllOf".Translate(currentCount, itemName);
                    TolkHelper.Speak(msg);
                    return true;
                }
                else
                {
                    MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Zero);
                    return true;
                }
            }
        }
    }
}
