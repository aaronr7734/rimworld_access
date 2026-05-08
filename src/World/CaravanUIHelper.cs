using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared helper utilities for caravan formation and splitting dialogs.
    /// Contains stateless methods for filtering transferables, pawn selection, etc.
    /// </summary>
    public static class CaravanUIHelper
    {
        /// <summary>
        /// Category types for filtering transferables.
        /// </summary>
        public enum TransferableCategory
        {
            Vehicles,
            Pawns,
            FoodAndMedicine,  // Also known as TravelSupplies
            Items
        }

        /// <summary>
        /// Filters transferables by category.
        /// </summary>
        /// <param name="allTransferables">All transferables from the dialog</param>
        /// <param name="category">The category to filter for</param>
        /// <returns>Filtered list of transferables</returns>
        public static List<TransferableOneWay> FilterByCategory(
            List<TransferableOneWay> allTransferables,
            TransferableCategory category)
        {
            if (allTransferables == null)
                return new List<TransferableOneWay>();

            switch (category)
            {
                case TransferableCategory.Vehicles:
                    return allTransferables
                        .Where(t => VehicleFrameworkHelper.IsVehiclePawn(t.AnyThing))
                        .ToList();

                case TransferableCategory.Pawns:
                    // Filter to pawns and sort by type: Colonists, Slaves, Prisoners, Animals, Others
                    // This matches the visual section order in the game's UI
                    return allTransferables
                        .Where(t => t.ThingDef.category == ThingCategory.Pawn &&
                                    !VehicleFrameworkHelper.IsVehiclePawn(t.AnyThing))
                        .OrderBy(t => GetPawnSortOrder(t.AnyThing as Pawn))
                        .ToList();

                case TransferableCategory.FoodAndMedicine:
                    // Food and medicine - same logic as game's TravelSupplies tab
                    // Note: Matches CaravanUIUtility.cs exactly - no extra null check on building
                    return allTransferables
                        .Where(t => t.ThingDef.category != ThingCategory.Pawn &&
                                   ((!t.ThingDef.thingCategories.NullOrEmpty() && t.ThingDef.thingCategories.Contains(ThingCategoryDefOf.Medicine)) ||
                                    (t.ThingDef.IsIngestible && !t.ThingDef.IsDrug && !t.ThingDef.IsCorpse && (t.ThingDef.plant == null || !t.ThingDef.plant.IsTree)) ||
                                    (t.AnyThing.GetInnerIfMinified().def.IsBed && t.AnyThing.GetInnerIfMinified().def.building.bed_caravansCanUse)))
                        .ToList();

                case TransferableCategory.Items:
                    // Items: everything that's not a pawn and not food/medicine
                    // Note: Matches CaravanUIUtility.cs exactly - no extra null check on building
                    return allTransferables
                        .Where(t => t.ThingDef.category != ThingCategory.Pawn &&
                                   !((!t.ThingDef.thingCategories.NullOrEmpty() && t.ThingDef.thingCategories.Contains(ThingCategoryDefOf.Medicine)) ||
                                    (t.ThingDef.IsIngestible && !t.ThingDef.IsDrug && !t.ThingDef.IsCorpse && (t.ThingDef.plant == null || !t.ThingDef.plant.IsTree)) ||
                                    (t.AnyThing.GetInnerIfMinified().def.IsBed && t.AnyThing.GetInnerIfMinified().def.building.bed_caravansCanUse)))
                        .ToList();

                default:
                    return allTransferables;
            }
        }

        /// <summary>
        /// Gets the selected pawn from a list of transferables.
        /// </summary>
        /// <param name="transferables">The list of transferables</param>
        /// <param name="selectedIndex">The current selection index</param>
        /// <returns>The selected pawn, or null if not a pawn</returns>
        public static Pawn GetSelectedPawn(List<TransferableOneWay> transferables, int selectedIndex)
        {
            if (transferables == null || transferables.Count == 0 ||
                selectedIndex < 0 || selectedIndex >= transferables.Count)
                return null;

            return transferables[selectedIndex]?.AnyThing as Pawn;
        }

        /// <summary>
        /// Gets labels for all transferables (for typeahead search).
        /// </summary>
        /// <param name="transferables">The list of transferables</param>
        /// <returns>List of labels for each transferable</returns>
        public static List<string> GetTransferableLabels(List<TransferableOneWay> transferables)
        {
            var labels = new List<string>();
            if (transferables == null)
                return labels;

            foreach (var t in transferables)
            {
                if (t.AnyThing is Pawn pawn)
                {
                    labels.Add(pawn.LabelShortCap);
                }
                else
                {
                    labels.Add(t.LabelCap);
                }
            }
            return labels;
        }

        /// <summary>
        /// Toggles pawn selection (check/uncheck) for caravan membership.
        /// </summary>
        /// <param name="transferable">The pawn transferable to toggle</param>
        /// <param name="notifyChanged">Callback to notify the dialog of changes</param>
        /// <returns>True if toggled on (checked), false if toggled off (unchecked), null if not a pawn</returns>
        public static bool? TogglePawnSelection(TransferableOneWay transferable, Action notifyChanged)
        {
            if (transferable == null || !(transferable.AnyThing is Pawn))
                return null;

            bool nowChecked;
            if (transferable.CountToTransfer > 0)
            {
                transferable.AdjustTo(0);
                TolkHelper.Speak($"{transferable.LabelCap.StripTags()} unchecked");
                nowChecked = false;
            }
            else
            {
                int max = transferable.MaxCount;
                transferable.AdjustTo(max);
                TolkHelper.Speak($"{transferable.LabelCap.StripTags()} checked");
                nowChecked = true;
            }

            notifyChanged?.Invoke();
            return nowChecked;
        }

        /// <summary>
        /// Gets a sort order for a pawn based on its type.
        /// Matches the visual section order in the game's caravan UI:
        /// Colonists, Slaves, Prisoners, Downed (capture), Animals, Mechs, Entities, Others
        /// </summary>
        /// <param name="pawn">The pawn to get sort order for</param>
        /// <returns>Sort order (lower = earlier in list)</returns>
        private static int GetPawnSortOrder(Pawn pawn)
        {
            if (pawn == null)
                return 99;

            // Colonists first (free non-slave colonists)
            if (pawn.IsFreeNonSlaveColonist)
                return 0;

            // Slaves second (Ideology DLC)
            if (pawn.IsSlave)
                return 1;

            // Prisoners third
            if (pawn.IsPrisoner)
                return 2;

            // Downed pawns that can be captured
            if (pawn.Downed && CaravanUtility.ShouldAutoCapture(pawn, Faction.OfPlayer))
                return 3;

            // Animals fourth
            if (pawn.RaceProps?.Animal == true)
                return 4;

            // Mechs (Biotech DLC)
            if (pawn.RaceProps?.IsMechanoid == true)
                return 5;

            // Everything else last
            return 6;
        }
    }
}
