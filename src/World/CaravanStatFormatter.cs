using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared formatting for caravan/transport pod stat values.
    /// Handles edge cases like "Infinite" food, "Immobile" speed, etc.
    /// Used by CaravanFormationState, SplitCaravanState, and TransportPodLoadingState.
    /// </summary>
    public static class CaravanStatFormatter
    {
        /// <summary>
        /// Formats mass stat with overload warning.
        /// </summary>
        /// <param name="usage">Current mass usage in kg</param>
        /// <param name="capacity">Maximum mass capacity in kg</param>
        /// <returns>Formatted string like "Mass: 150.0 of 500.0 kg" or "Mass: 600.0 of 500.0 kg - OVERLOADED"</returns>
        public static string FormatMass(float usage, float capacity)
        {
            string usageStr = usage.ToString("F1");
            string capacityStr = capacity.ToString("F1");
            return usage > capacity
                ? "RimWorldAccess.Stat.MassOverloaded".Translate(usageStr, capacityStr)
                : "RimWorldAccess.Stat.Mass".Translate(usageStr, capacityStr);
        }

        /// <summary>
        /// Formats speed stat with immobile check for overloaded caravans.
        /// </summary>
        /// <param name="tilesPerDay">Movement speed in tiles per day</param>
        /// <param name="isOverloaded">Whether the caravan is over capacity</param>
        /// <param name="includeDescription">Whether to append the tooltip description</param>
        /// <returns>Formatted string like "Speed: 1.5 tiles per day" or "Speed: Immobile"</returns>
        public static string FormatSpeed(float tilesPerDay, bool isOverloaded, bool includeDescription = true)
        {
            string description = "CaravanMovementSpeedTip".Translate();

            if (isOverloaded)
            {
                string immobileLabel = "TilesPerDayImmobile".Translate();
                return includeDescription
                    ? "RimWorldAccess.Stat.SpeedImmobileWithDesc".Translate(immobileLabel, description)
                    : "RimWorldAccess.Stat.SpeedImmobile".Translate(immobileLabel);
            }
            else if (tilesPerDay > 0)
            {
                string speedStr = tilesPerDay.ToString("F1");
                return includeDescription
                    ? "RimWorldAccess.Stat.SpeedTilesPerDayWithDesc".Translate(speedStr, description)
                    : "RimWorldAccess.Stat.SpeedTilesPerDay".Translate(speedStr);
            }
            else
            {
                return includeDescription
                    ? "RimWorldAccess.Stat.SpeedCannotMoveWithDesc".Translate(description)
                    : "RimWorldAccess.Stat.SpeedCannotMove".Translate();
            }
        }

        /// <summary>
        /// Formats food stat with infinite/none edge cases and spoilage warning.
        /// </summary>
        /// <param name="days">Days worth of food (>= 600 means infinite)</param>
        /// <param name="tillRot">Days until food spoils</param>
        /// <param name="includeDescription">Whether to append the tooltip description</param>
        /// <returns>Formatted string like "Food: 5.2 days, spoils in 3.1 days" or "Food: Infinite"</returns>
        public static string FormatFood(float days, float tillRot, bool includeDescription = true)
        {
            string description = "DaysWorthOfFoodTooltip".Translate();

            // Match game behavior: >= 600 days means "Infinite" (no consumers or tons of food)
            if (days >= 600f)
            {
                string infiniteLabel = "InfiniteDaysWorthOfFoodInfo".Translate();
                return includeDescription
                    ? "RimWorldAccess.Stat.FoodInfiniteWithDesc".Translate(infiniteLabel, description)
                    : "RimWorldAccess.Stat.FoodInfinite".Translate(infiniteLabel);
            }
            else if (days < 0.1f)
            {
                return includeDescription
                    ? "RimWorldAccess.Stat.FoodNoneWithDesc".Translate(description)
                    : "RimWorldAccess.Stat.FoodNone".Translate();
            }
            else
            {
                string daysStr = days.ToString("F1");
                // Only show spoilage if food will rot before it's consumed AND it's not "infinite"
                bool showSpoil = tillRot < days && tillRot > 0 && tillRot < 600f;
                if (showSpoil)
                {
                    string rotStr = tillRot.ToString("F1");
                    return includeDescription
                        ? "RimWorldAccess.Stat.FoodDaysWithSpoilWithDesc".Translate(daysStr, rotStr, description)
                        : "RimWorldAccess.Stat.FoodDaysWithSpoil".Translate(daysStr, rotStr);
                }
                return includeDescription
                    ? "RimWorldAccess.Stat.FoodDaysWithDesc".Translate(daysStr, description)
                    : "RimWorldAccess.Stat.FoodDays".Translate(daysStr);
            }
        }

        /// <summary>
        /// Formats foraging stat with food type.
        /// </summary>
        /// <param name="foodType">The type of food foraged (can be null)</param>
        /// <param name="perDay">Amount foraged per day</param>
        /// <param name="includeDescription">Whether to append the tooltip description</param>
        /// <returns>Formatted string like "Foraging: 1.5 (berries) per day"</returns>
        public static string FormatForaging(ThingDef foodType, float perDay, bool includeDescription = true)
        {
            string description = "ForagedFoodPerDayTip".Translate();
            string perDayStr = perDay.ToString("F1");

            if (perDay > 0 && foodType != null)
            {
                return includeDescription
                    ? "RimWorldAccess.Stat.ForagingPerDayWithTypeWithDesc".Translate(perDayStr, foodType.label, description)
                    : "RimWorldAccess.Stat.ForagingPerDayWithType".Translate(perDayStr, foodType.label);
            }

            return includeDescription
                ? "RimWorldAccess.Stat.ForagingPerDayWithDesc".Translate(perDayStr, description)
                : "RimWorldAccess.Stat.ForagingPerDay".Translate(perDayStr);
        }

        /// <summary>
        /// Formats visibility stat as percentage.
        /// </summary>
        /// <param name="visibility">Visibility value (0-1 range)</param>
        /// <param name="includeDescription">Whether to append the tooltip description</param>
        /// <returns>Formatted string like "Visibility: 75%"</returns>
        public static string FormatVisibility(float visibility, bool includeDescription = true)
        {
            string description = "CaravanVisibilityTip".Translate();
            string percentStr = visibility.ToString("P0");
            return includeDescription
                ? "RimWorldAccess.Stat.VisibilityWithDesc".Translate(percentStr, description)
                : "RimWorldAccess.Stat.Visibility".Translate(percentStr);
        }
    }
}
