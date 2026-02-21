using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper for inline quantity adjustment of TransferableOneWay items.
    /// Used by caravan formation, caravan splitting, and transport pod loading screens.
    /// Provides keyboard shortcuts for quick quantity changes without opening a modal menu.
    /// </summary>
    public static class TransferableQuantityHelper
    {
        /// <summary>
        /// Gets the appropriate label for a transferable.
        /// For grouped pawns (animals with MaxCount > 1), uses PawnLabelHelper.
        /// For other items, uses the standard LabelCap.
        /// </summary>
        private static string GetTransferableLabel(TransferableOneWay transferable)
        {
            if (transferable == null)
                return "";

            // Check if this is a grouped pawn (multiple animals in one transferable)
            if (transferable.AnyThing is Pawn pawn && transferable.MaxCount > 1)
            {
                return PawnLabelHelper.BuildGroupedPawnLabel(pawn, transferable.MaxCount);
            }

            return transferable.LabelCap.StripTags();
        }

        /// <summary>
        /// Handles keyboard input for quantity adjustment.
        /// Returns true if the input was handled.
        /// </summary>
        /// <param name="key">The key pressed</param>
        /// <param name="shift">Whether shift is held</param>
        /// <param name="ctrl">Whether ctrl is held</param>
        /// <param name="alt">Whether alt is held</param>
        /// <param name="getTransferable">Function to get the currently selected transferable</param>
        /// <param name="onChanged">Callback after quantity changes (to notify the dialog)</param>
        /// <returns>True if input was handled</returns>
        public static bool HandleQuantityInput(
            KeyCode key, bool shift, bool ctrl, bool alt,
            Func<TransferableOneWay> getTransferable,
            Action onChanged)
        {
            // Plus/Equals key - increase by 1
            if ((key == KeyCode.Plus || key == KeyCode.KeypadPlus || key == KeyCode.Equals) && !ctrl && !alt)
            {
                AdjustQuantity(getTransferable, 1, onChanged);
                return true;
            }

            // Minus key - decrease by 1
            if ((key == KeyCode.Minus || key == KeyCode.KeypadMinus) && !shift && !ctrl && !alt)
            {
                AdjustQuantity(getTransferable, -1, onChanged);
                return true;
            }

            // Shift+Up - increase by 10
            if (key == KeyCode.UpArrow && shift && !ctrl && !alt)
            {
                AdjustQuantity(getTransferable, 10, onChanged);
                return true;
            }

            // Shift+Down - decrease by 10
            if (key == KeyCode.DownArrow && shift && !ctrl && !alt)
            {
                AdjustQuantity(getTransferable, -10, onChanged);
                return true;
            }

            // Ctrl+Up - increase by 100
            if (key == KeyCode.UpArrow && ctrl && !shift && !alt)
            {
                AdjustQuantity(getTransferable, 100, onChanged);
                return true;
            }

            // Ctrl+Down - decrease by 100
            if (key == KeyCode.DownArrow && ctrl && !shift && !alt)
            {
                AdjustQuantity(getTransferable, -100, onChanged);
                return true;
            }

            // Shift+Home - set to zero (minimum)
            if (key == KeyCode.Home && shift && !ctrl && !alt)
            {
                SetToZero(getTransferable, onChanged);
                return true;
            }

            // Shift+End - set to maximum
            if (key == KeyCode.End && shift && !ctrl && !alt)
            {
                SetToMax(getTransferable, onChanged);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Adjusts the quantity of a transferable by a delta amount.
        /// </summary>
        public static void AdjustQuantity(Func<TransferableOneWay> getTransferable, int delta, Action onChanged)
        {
            TransferableOneWay transferable = getTransferable?.Invoke();
            if (transferable == null)
                return;

            int currentQty = transferable.CountToTransfer;
            int newQty = Mathf.Clamp(currentQty + delta, 0, transferable.MaxCount);

            if (newQty == currentQty)
            {
                // Already at limit
                if (delta > 0)
                    TolkHelper.Speak("Maximum");
                else
                    TolkHelper.Speak("Minimum");
                return;
            }

            transferable.AdjustTo(newQty);
            onChanged?.Invoke();

            // Announce the new quantity
            string itemName = GetTransferableLabel(transferable);
            TolkHelper.Speak($"{newQty} {itemName}");
        }

        /// <summary>
        /// Sets the quantity of a transferable to its maximum.
        /// </summary>
        public static void SetToMax(Func<TransferableOneWay> getTransferable, Action onChanged)
        {
            TransferableOneWay transferable = getTransferable?.Invoke();
            if (transferable == null)
                return;

            int maxQty = transferable.MaxCount;

            if (transferable.CountToTransfer == maxQty)
            {
                TolkHelper.Speak("Already at maximum");
                return;
            }

            transferable.AdjustTo(maxQty);
            onChanged?.Invoke();

            string itemName = GetTransferableLabel(transferable);
            float itemMass = transferable.AnyThing?.GetStatValue(RimWorld.StatDefOf.Mass)
                ?? transferable.ThingDef?.BaseMass ?? 0f;
            float totalMass = maxQty * itemMass;
            string massStr = totalMass > 0 ? $", {totalMass:F1} kg" : "";
            TolkHelper.Speak($"{maxQty} {itemName}, maximum{massStr}");
        }

        /// <summary>
        /// Sets the quantity of a transferable to zero.
        /// </summary>
        public static void SetToZero(Func<TransferableOneWay> getTransferable, Action onChanged)
        {
            TransferableOneWay transferable = getTransferable?.Invoke();
            if (transferable == null)
                return;

            if (transferable.CountToTransfer == 0)
            {
                TolkHelper.Speak("Already at zero");
                return;
            }

            transferable.AdjustTo(0);
            onChanged?.Invoke();

            string itemName = GetTransferableLabel(transferable);
            TolkHelper.Speak($"0 {itemName}");
        }
    }
}
