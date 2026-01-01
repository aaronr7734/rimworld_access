using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for viewing caravan stats (I key when caravan selected in world view).
    /// Displays comprehensive caravan information in a scrollable format.
    /// </summary>
    public static class CaravanStatsState
    {
        private static bool isActive = false;
        private static Caravan currentCaravan = null;
        private static List<string> infoLines = new List<string>();
        private static int currentLineIndex = 0;

        /// <summary>
        /// Gets whether the caravan stats viewer is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the caravan stats viewer for the specified caravan.
        /// </summary>
        public static void Open(Caravan caravan)
        {
            if (caravan == null)
            {
                TolkHelper.Speak("No caravan specified", SpeechPriority.High);
                return;
            }

            isActive = true;
            currentCaravan = caravan;
            currentLineIndex = 0;

            BuildInfoLines();

            if (infoLines.Count == 0)
            {
                TolkHelper.Speak("No caravan information available");
                Close();
                return;
            }

            // Announce opening and first line
            TolkHelper.Speak($"Caravan stats for {caravan.Name}. {infoLines.Count} sections. Use Up/Down to navigate, Escape to close.");
            AnnounceCurrentLine();
        }

        /// <summary>
        /// Closes the caravan stats viewer.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentCaravan = null;
            infoLines.Clear();
            currentLineIndex = 0;
            TolkHelper.Speak("Caravan stats closed");
        }

        /// <summary>
        /// Builds the list of information lines for the current caravan.
        /// </summary>
        private static void BuildInfoLines()
        {
            infoLines.Clear();

            if (currentCaravan == null)
                return;

            // Section 1: Caravan Name and Location
            infoLines.Add($"Caravan: {currentCaravan.Name}");

            if (currentCaravan.Tile.Valid && Find.WorldGrid != null)
            {
                Vector2 coords = Find.WorldGrid.LongLatOf(currentCaravan.Tile);
                infoLines.Add($"Location: Tile {currentCaravan.Tile}, {coords.y:F1} degrees latitude, {coords.x:F1} degrees longitude");
            }

            // Section 2: Pawns
            infoLines.Add("--- Pawns ---");
            List<Pawn> pawns = currentCaravan.PawnsListForReading;
            if (pawns != null && pawns.Count > 0)
            {
                var colonists = pawns.Where(p => p.IsColonist).ToList();
                var animals = pawns.Where(p => p.RaceProps.Animal).ToList();
                var prisoners = pawns.Where(p => p.IsPrisoner).ToList();

                if (colonists.Count > 0)
                {
                    infoLines.Add($"Colonists: {colonists.Count}");
                    foreach (Pawn colonist in colonists)
                    {
                        StringBuilder pawnInfo = new StringBuilder();
                        pawnInfo.Append($"  - {colonist.LabelShortCap}");

                        if (colonist.story != null && !colonist.story.TitleCap.NullOrEmpty())
                        {
                            pawnInfo.Append($", {colonist.story.TitleCap}");
                        }

                        if (colonist.health != null && colonist.health.summaryHealth != null)
                        {
                            if (colonist.Downed)
                            {
                                pawnInfo.Append(", Downed");
                            }
                            else if (colonist.health.summaryHealth.SummaryHealthPercent < 1f)
                            {
                                float healthPercent = colonist.health.summaryHealth.SummaryHealthPercent * 100f;
                                pawnInfo.Append($", Health: {healthPercent:F0}%");
                            }
                        }

                        infoLines.Add(pawnInfo.ToString());
                    }
                }

                if (animals.Count > 0)
                {
                    infoLines.Add($"Animals: {animals.Count}");
                    // Group animals by kind
                    var animalGroups = animals.GroupBy(a => a.kindDef).OrderByDescending(g => g.Count());
                    foreach (var group in animalGroups)
                    {
                        int count = group.Count();
                        string kindLabel = count > 1 ? group.Key.GetLabelPlural() : group.Key.label;
                        infoLines.Add($"  - {count} {kindLabel}");
                    }
                }

                if (prisoners.Count > 0)
                {
                    infoLines.Add($"Prisoners: {prisoners.Count}");
                    foreach (Pawn prisoner in prisoners)
                    {
                        infoLines.Add($"  - {prisoner.LabelShortCap}");
                    }
                }
            }
            else
            {
                infoLines.Add("No pawns in caravan");
            }

            // Section 3: Mass and Capacity
            infoLines.Add("--- Mass ---");
            float massUsage = currentCaravan.MassUsage;
            float massCapacity = currentCaravan.MassCapacity;
            float massPercent = massCapacity > 0 ? (massUsage / massCapacity) * 100f : 0f;

            infoLines.Add($"Mass: {massUsage:F1} / {massCapacity:F1} kg ({massPercent:F0}%)");

            if (currentCaravan.ImmobilizedByMass)
            {
                infoLines.Add("WARNING: Immobilized by mass! Caravan cannot move.");
            }

            // Section 4: Movement
            infoLines.Add("--- Movement ---");

            if (currentCaravan.CantMove)
            {
                infoLines.Add("Status: Cannot move");

                if (currentCaravan.AllOwnersDowned)
                {
                    infoLines.Add("Reason: All colonists are downed");
                }
                else if (currentCaravan.AllOwnersHaveMentalBreak)
                {
                    infoLines.Add("Reason: All colonists have mental breaks");
                }
                else if (currentCaravan.ImmobilizedByMass)
                {
                    infoLines.Add("Reason: Immobilized by excessive mass");
                }
            }
            else
            {
                if (currentCaravan.pather != null && currentCaravan.pather.Moving)
                {
                    infoLines.Add("Status: Moving");
                }
                else if (currentCaravan.NightResting)
                {
                    infoLines.Add("Status: Resting (night)");
                }
                else
                {
                    infoLines.Add("Status: Stopped");
                }

                float tilesPerDay = TilesPerDayCalculator.ApproxTilesPerDay(currentCaravan);
                infoLines.Add($"Speed: {tilesPerDay:F2} tiles per day");
            }

            // Section 5: Food
            infoLines.Add("--- Food ---");

            if (currentCaravan.needs != null)
            {
                try
                {
                    float daysWorth = DaysWorthOfFoodCalculator.ApproxDaysWorthOfFood(currentCaravan);

                    if (daysWorth < 1000f)
                    {
                        infoLines.Add($"Food: {daysWorth:F1} days worth");
                    }
                    else
                    {
                        infoLines.Add("Food: Effectively infinite");
                    }

                    // Foraging info
                    if (currentCaravan.forage != null)
                    {
                        var foragedFoodData = currentCaravan.forage.ForagedFoodPerDay;
                        if (foragedFoodData.food != null && foragedFoodData.perDay > 0f)
                        {
                            infoLines.Add($"Foraging: {foragedFoodData.perDay:F1} {foragedFoodData.food.label} per day");
                        }
                    }
                }
                catch
                {
                    infoLines.Add("Food information unavailable");
                }
            }

            // Section 6: Destination and ETA
            if (currentCaravan.pather != null && currentCaravan.pather.Moving)
            {
                infoLines.Add("--- Destination ---");

                PlanetTile destTile = currentCaravan.pather.Destination;
                if (destTile.Valid)
                {
                    infoLines.Add($"Destination: Tile {destTile}");

                    // Find what's at the destination
                    Settlement destSettlement = Find.WorldObjects?.SettlementAt(destTile);
                    if (destSettlement != null)
                    {
                        infoLines.Add($"  {destSettlement.Label} ({destSettlement.Faction.Name})");
                    }

                    // Calculate ETA
                    float ticksToArrive = currentCaravan.pather.nextTile == currentCaravan.pather.Destination ?
                        currentCaravan.pather.nextTileCostLeft :
                        CaravanArrivalTimeEstimator.EstimatedTicksToArrive(currentCaravan.Tile, destTile, currentCaravan);

                    if (ticksToArrive > 0)
                    {
                        float hoursToArrive = ticksToArrive / 2500f;
                        float daysToArrive = hoursToArrive / 24f;

                        if (daysToArrive >= 1f)
                        {
                            infoLines.Add($"ETA: {daysToArrive:F1} days");
                        }
                        else
                        {
                            infoLines.Add($"ETA: {hoursToArrive:F1} hours");
                        }
                    }
                }

                // Arrival action
                if (currentCaravan.pather.ArrivalAction != null)
                {
                    string actionLabel = currentCaravan.pather.ArrivalAction.Label;
                    if (!actionLabel.NullOrEmpty())
                    {
                        infoLines.Add($"Action on arrival: {actionLabel}");
                    }
                }
            }

            // Section 7: Visibility
            infoLines.Add("--- Visibility ---");
            float visibility = currentCaravan.Visibility;
            infoLines.Add($"Visibility: {visibility:F1}%");
            infoLines.Add("(Lower visibility reduces chance of being detected by enemies)");

            // Section 8: Trading capability
            if (currentCaravan.trader != null && currentCaravan.trader.CanTradeNow)
            {
                infoLines.Add("--- Trading ---");
                infoLines.Add("This caravan can trade with settlements");
            }
        }

        /// <summary>
        /// Announces the current line.
        /// </summary>
        private static void AnnounceCurrentLine()
        {
            if (infoLines.Count == 0)
            {
                TolkHelper.Speak("No information available");
                return;
            }

            if (currentLineIndex < 0 || currentLineIndex >= infoLines.Count)
                return;

            string line = infoLines[currentLineIndex];

            TolkHelper.Speak($"{line}. {MenuHelper.FormatPosition(currentLineIndex, infoLines.Count)}");
        }

        /// <summary>
        /// Selects the next line.
        /// </summary>
        public static void SelectNext()
        {
            if (infoLines.Count == 0)
            {
                TolkHelper.Speak("No information available");
                return;
            }

            currentLineIndex = MenuHelper.SelectNext(currentLineIndex, infoLines.Count);

            AnnounceCurrentLine();
        }

        /// <summary>
        /// Selects the previous line.
        /// </summary>
        public static void SelectPrevious()
        {
            if (infoLines.Count == 0)
            {
                TolkHelper.Speak("No information available");
                return;
            }

            currentLineIndex = MenuHelper.SelectPrevious(currentLineIndex, infoLines.Count);

            AnnounceCurrentLine();
        }

        /// <summary>
        /// Handles keyboard input for the caravan stats viewer.
        /// Returns true if the input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key)
        {
            if (!isActive)
                return false;

            switch (key)
            {
                case KeyCode.UpArrow:
                    SelectPrevious();
                    return true;

                case KeyCode.DownArrow:
                    SelectNext();
                    return true;

                case KeyCode.Escape:
                    Close();
                    return true;

                default:
                    return false;
            }
        }
    }
}
