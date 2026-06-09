using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared announcement helper for caravan formation and splitting dialogs.
    /// Builds consistent announcement strings for transferables.
    /// </summary>
    public static class CaravanAnnouncementHelper
    {
        /// <summary>
        /// Builds an announcement string for a transferable item.
        /// </summary>
        /// <param name="transferable">The transferable to announce</param>
        /// <param name="selectedIndex">Current selection index</param>
        /// <param name="totalCount">Total items in the list</param>
        /// <param name="includePosition">Whether to include position info (X of Y)</param>
        /// <returns>The announcement string</returns>
        public static string BuildItemAnnouncement(
            TransferableOneWay transferable,
            int selectedIndex,
            int totalCount,
            bool includePosition = true)
        {
            if (transferable == null)
                return "No item selected";

            StringBuilder announcement = new StringBuilder();

            if (VehicleFrameworkHelper.IsVehiclePawn(transferable.AnyThing))
            {
                string vehicleAnnouncement = VehicleFrameworkHelper.BuildVehicleAnnouncement(transferable);
                if (!vehicleAnnouncement.NullOrEmpty())
                {
                    announcement.Append(vehicleAnnouncement);
                    if (includePosition)
                    {
                        announcement.Append($". {MenuHelper.FormatPosition(selectedIndex, totalCount)}");
                    }
                    return announcement.ToString();
                }
            }

            if (transferable.AnyThing is Pawn pawn)
            {
                int maxCount = transferable.MaxCount;
                int toTransfer = transferable.CountToTransfer;

                // Check if multiple pawns are grouped together (animals with numerical names)
                if (maxCount > 1)
                {
                    // Build label with distinguishing info (gender, life stage)
                    string label = PawnLabelHelper.BuildGroupedPawnLabel(pawn, maxCount);
                    announcement.Append(label);

                    if (toTransfer > 0)
                    {
                        announcement.Append($". Taking {toTransfer} of {maxCount}");
                    }
                    else
                    {
                        announcement.Append($". {maxCount} available");
                    }
                }
                else
                {
                    // Single pawn - show individual details
                    announcement.Append(pawn.LabelShortCap.StripTags());

                    if (pawn.story != null && !pawn.story.TitleCap.NullOrEmpty())
                    {
                        announcement.Append($", {pawn.story.TitleCap.StripTags()}");
                    }

                    // Add equipped weapon if any
                    if (pawn.equipment?.Primary != null)
                    {
                        announcement.Append($" (wielding {pawn.equipment.Primary.LabelCap})");
                    }

                    // Only say "checked" if they're going, nothing if staying
                    if (toTransfer > 0)
                    {
                        announcement.Append(" - checked");
                    }
                }
            }
            else
            {
                announcement.Append(transferable.LabelCap.StripTags());

                int toTransfer = transferable.CountToTransfer;
                int max = transferable.MaxCount;

                // Only say "Taking X of Y" if taking some, otherwise just say how many available
                if (toTransfer > 0)
                {
                    announcement.Append($". Taking {toTransfer} of {max}");

                    // Add mass information
                    float totalMass = transferable.AnyThing.GetStatValue(StatDefOf.Mass) * toTransfer;
                    if (totalMass >= 0.1f)
                    {
                        announcement.Append($". Mass: {totalMass:F1} kg");
                    }
                }
                else
                {
                    announcement.Append($". {max} available");
                }
            }

            if (includePosition)
            {
                announcement.Append($". {MenuHelper.FormatPosition(selectedIndex, totalCount)}");
            }

            return announcement.ToString();
        }

        /// <summary>
        /// Builds a shorter announcement for typeahead search results.
        /// </summary>
        /// <param name="transferable">The transferable to announce</param>
        /// <param name="searchBuffer">The current search string</param>
        /// <param name="matchPosition">Current match position</param>
        /// <param name="matchCount">Total match count</param>
        /// <returns>The announcement string</returns>
        public static string BuildSearchAnnouncement(
            TransferableOneWay transferable,
            string searchBuffer,
            int matchPosition,
            int matchCount)
        {
            if (transferable == null)
                return "No item selected";

            StringBuilder announcement = new StringBuilder();

            if (VehicleFrameworkHelper.IsVehiclePawn(transferable.AnyThing))
            {
                string vehicleAnnouncement = VehicleFrameworkHelper.BuildVehicleAnnouncement(transferable);
                if (!vehicleAnnouncement.NullOrEmpty())
                {
                    announcement.Append(vehicleAnnouncement);
                    announcement.Append($". '{searchBuffer}' match {matchPosition} of {matchCount}");
                    return announcement.ToString();
                }
            }

            if (transferable.AnyThing is Pawn pawn)
            {
                int maxCount = transferable.MaxCount;
                int toTransfer = transferable.CountToTransfer;

                // Check if multiple pawns are grouped together (animals with numerical names)
                if (maxCount > 1)
                {
                    string label = PawnLabelHelper.BuildGroupedPawnLabel(pawn, maxCount);
                    announcement.Append(label);

                    if (toTransfer > 0)
                    {
                        announcement.Append($". Taking {toTransfer} of {maxCount}");
                    }
                    else
                    {
                        announcement.Append($". {maxCount} available");
                    }
                }
                else
                {
                    announcement.Append(pawn.LabelShortCap.StripTags());
                    // Only say "checked" if they're going
                    if (toTransfer > 0)
                    {
                        announcement.Append(" - checked");
                    }
                }
            }
            else
            {
                announcement.Append(transferable.LabelCap.StripTags());
                int toTransfer = transferable.CountToTransfer;
                int max = transferable.MaxCount;
                // Only say "Taking X of Y" if taking some
                if (toTransfer > 0)
                {
                    announcement.Append($". Taking {toTransfer} of {max}");
                }
                else
                {
                    announcement.Append($". {max} available");
                }
            }

            announcement.Append($". '{searchBuffer}' match {matchPosition} of {matchCount}");
            return announcement.ToString();
        }

        /// <summary>
        /// Announces when there are no items in a tab.
        /// </summary>
        public static void AnnounceNoItems()
        {
            TolkHelper.Speak("No items in this tab");
        }
    }
}
