using System;
using UnityEngine;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared quantity calculation helpers for caravan formation and splitting dialogs.
    /// Handles Shift+Enter "add maximum" logic based on mass capacity.
    /// </summary>
    public static class CaravanQuantityHelper
    {
        /// <summary>
        /// Result of a max quantity addition calculation.
        /// </summary>
        public struct MaxAddResult
        {
            public int ToAdd;
            public int NewCount;
            public int MaxAvailable;
            public float TotalMass;
            public bool HitCapacityLimit;
            public bool AlreadyAtMax;
            public bool NoCapacity;
            public string Announcement;
        }

        /// <summary>
        /// Calculates how many items can be added based on remaining mass capacity.
        /// Does NOT modify the transferable - caller should apply the changes.
        /// </summary>
        /// <param name="transferable">The transferable to add to</param>
        /// <param name="remainingCapacity">Remaining mass capacity</param>
        /// <returns>Result containing quantity info and announcement text</returns>
        public static MaxAddResult CalculateMaxToAdd(TransferableOneWay transferable, float remainingCapacity)
        {
            var result = new MaxAddResult
            {
                ToAdd = 0,
                NewCount = 0,
                MaxAvailable = 0,
                HitCapacityLimit = false,
                AlreadyAtMax = false,
                NoCapacity = false,
                Announcement = ""
            };

            if (transferable == null)
                return result;

            // Use actual mass (with quality/condition modifiers) instead of base mass
            // Falls back to BaseMass if AnyThing is null (shouldn't happen in practice)
            float itemMass = transferable.AnyThing?.GetStatValue(StatDefOf.Mass)
                ?? transferable.ThingDef?.BaseMass ?? 0f;
            int maxAvailable = transferable.GetMaximumToTransfer();
            int currentCount = transferable.CountToTransfer;

            result.MaxAvailable = maxAvailable;

            int toAdd;
            bool hitCapacityLimit = false;

            if (itemMass <= 0f)
            {
                // Item has no mass (e.g., silver) - can add all available
                toAdd = maxAvailable - currentCount;
            }
            else
            {
                // Calculate how many can fit in remaining capacity
                int canFit = Mathf.FloorToInt(remainingCapacity / itemMass);
                int couldAdd = maxAvailable - currentCount;
                toAdd = Mathf.Min(canFit, couldAdd);
                hitCapacityLimit = canFit < couldAdd;
            }

            result.ToAdd = toAdd;
            result.HitCapacityLimit = hitCapacityLimit;

            if (toAdd <= 0)
            {
                if (remainingCapacity < itemMass && itemMass > 0f)
                {
                    result.NoCapacity = true;
                }
                else
                {
                    result.AlreadyAtMax = true;
                }
                return result;
            }

            int newCount = currentCount + toAdd;
            result.NewCount = newCount;

            // Calculate total mass of items being taken
            float totalMass = newCount * itemMass;
            result.TotalMass = totalMass;
            string massStr = totalMass > 0 ? $", {totalMass:F1} kg" : "";

            // Build announcement based on context
            bool tookAll = newCount == maxAvailable;

            if (currentCount == 0)
            {
                // Starting from zero
                if (tookAll)
                {
                    // "Taking all X, Y kg" but just "Taking 1, Y kg" if only one
                    result.Announcement = newCount == 1 ? $"Taking 1{massStr}" : $"Taking all {newCount}{massStr}";
                }
                else if (hitCapacityLimit)
                {
                    result.Announcement = $"Taking {newCount} of {maxAvailable}{massStr}, capacity limit";
                }
                else
                {
                    result.Announcement = $"Taking {newCount} of {maxAvailable}{massStr}";
                }
            }
            else
            {
                // Adding to existing amount
                if (tookAll)
                {
                    // "Added X, now taking all Y, Z kg" but simpler if only one added or total is one
                    if (newCount == 1)
                    {
                        result.Announcement = $"Added 1{massStr}";
                    }
                    else if (toAdd == 1)
                    {
                        result.Announcement = $"Added 1, now taking all {newCount}{massStr}";
                    }
                    else
                    {
                        result.Announcement = $"Added {toAdd}, now taking all {newCount}{massStr}";
                    }
                }
                else if (hitCapacityLimit)
                {
                    result.Announcement = $"Added {toAdd}, now {newCount} of {maxAvailable}{massStr}, capacity limit";
                }
                else
                {
                    result.Announcement = $"Added {toAdd}, now {newCount} of {maxAvailable}{massStr}";
                }
            }

            return result;
        }

        /// <summary>
        /// Applies the max add result to a transferable and announces the change.
        /// </summary>
        /// <param name="transferable">The transferable to modify</param>
        /// <param name="result">The calculation result</param>
        /// <param name="notifyChanged">Callback to notify the dialog of changes</param>
        public static void ApplyMaxAdd(
            TransferableOneWay transferable,
            MaxAddResult result,
            Action notifyChanged)
        {
            if (result.NoCapacity)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Quantity.NotEnoughCapacity".Loc());
            }
            else if (result.AlreadyAtMax)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
            }
            else if (!string.IsNullOrEmpty(result.Announcement))
            {
                TolkHelper.SpeakData(result.Announcement);
            }

            if (result.ToAdd > 0)
            {
                transferable.AdjustTo(result.NewCount);
            }

            notifyChanged?.Invoke();
        }

        /// <summary>
        /// Handles Shift+Enter for pawn selection (selects all of this pawn type).
        /// </summary>
        /// <param name="transferable">The pawn transferable</param>
        /// <param name="notifyChanged">Callback to notify the dialog of changes</param>
        /// <param name="announceItem">Callback to announce the current item</param>
        public static void SelectAllPawns(
            TransferableOneWay transferable,
            Action notifyChanged,
            Action announceItem)
        {
            if (transferable == null)
                return;

            int maxToTransfer = transferable.GetMaximumToTransfer();
            transferable.AdjustTo(maxToTransfer);
            notifyChanged?.Invoke();
            announceItem?.Invoke();
        }
    }
}
