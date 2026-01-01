using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// State class for announcing current time, date, and season information.
    /// Triggered by T key.
    /// </summary>
    public static class TimeAnnouncementState
    {
        /// <summary>
        /// Announces the current in-game time, date, and season.
        /// </summary>
        public static void AnnounceTime()
        {
            // Check if we're in-game
            if (Current.ProgramState != ProgramState.Playing)
            {
                TolkHelper.Speak("Not in game");
                return;
            }

            // Check if there's a tick manager
            if (Find.TickManager == null)
            {
                TolkHelper.Speak("Time information not available", SpeechPriority.High);
                return;
            }

            // Get current tick count
            int absTicks = Find.TickManager.TicksAbs;

            // Get longitude for time zone calculation
            Vector2 longLat;
            if (WorldRendererUtility.WorldSelected && Find.WorldSelector.SelectedTile >= 0)
            {
                longLat = Find.WorldGrid.LongLatOf(Find.WorldSelector.SelectedTile);
            }
            else if (WorldRendererUtility.WorldSelected && Find.WorldSelector.NumSelectedObjects > 0)
            {
                longLat = Find.WorldGrid.LongLatOf(Find.WorldSelector.FirstSelectedObject.Tile);
            }
            else if (Find.CurrentMap != null)
            {
                longLat = Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile);
            }
            else
            {
                // World selected but no specific location - use default (0,0)
                longLat = new Vector2(0f, 0f);
            }

            float longitude = longLat.x;
            float latitude = longLat.y;

            // Get time components
            // DayTick returns ticks within the current day (0-59999)
            // Each hour = 2500 ticks, so we can calculate hours and minutes
            int dayTick = GenDate.DayTick(absTicks, longitude);
            int totalMinutesInDay = Mathf.FloorToInt((float)dayTick / 2500f * 60f);
            int hour = totalMinutesInDay / 60;
            int minute = totalMinutesInDay % 60;

            int dayOfTwelfth = GenDate.DayOfTwelfth(absTicks, longitude);
            int year = GenDate.Year(absTicks, longitude);
            Season season = GenDate.Season(absTicks, longLat);
            Quadrum quadrum = GenDate.Quadrum(absTicks, longitude);

            // Build announcement string
            StringBuilder sb = new StringBuilder();

            // Time of day (using user's preference for 12/24 hour format)
            string timeString;
            if (Prefs.TwelveHourClockMode)
            {
                // 12-hour format
                if (hour == 0)
                {
                    timeString = $"12:{minute:D2} AM";
                }
                else if (hour < 12)
                {
                    timeString = $"{hour}:{minute:D2} AM";
                }
                else if (hour == 12)
                {
                    timeString = $"12:{minute:D2} PM";
                }
                else
                {
                    timeString = $"{hour - 12}:{minute:D2} PM";
                }
            }
            else
            {
                // 24-hour format
                timeString = $"{hour:D2}:{minute:D2}";
            }

            sb.Append($"Time: {timeString}");

            // Weather (only if on a map)
            if (Find.CurrentMap?.weatherManager?.curWeather != null)
            {
                WeatherDef weather = Find.CurrentMap.weatherManager.curWeather;
                sb.Append($", Weather: {weather.LabelCap}");
            }

            // Date
            string dateString = GenDate.DateReadoutStringAt(absTicks, longLat);
            sb.Append($", Date: {dateString}");

            // Season (only if on a map, not on world view)
            if (Find.CurrentMap != null && season != Season.Undefined)
            {
                string seasonLabel = season.LabelCap();
                sb.Append($", Season: {seasonLabel}");
            }

            // Days passed since game start (useful context)
            int daysPassed = GenDate.DaysPassed;
            sb.Append($", Days passed: {daysPassed}");

            // Copy to clipboard for screen reader
            TolkHelper.Speak(sb.ToString());

            // Play audio feedback
            SoundDefOf.Click.PlayOneShotOnCamera();
        }
    }
}
