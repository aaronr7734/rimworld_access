using UnityEngine;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// One-shot announcer for actual game performance vs requested time speed.
    /// Triggered by Shift+T. Mirrors RimWorld's internal TPS counter formula.
    /// </summary>
    public static class PerformanceAnnouncementState
    {
        public static void AnnouncePerformance()
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                TolkHelper.Speak("Not in game");
                return;
            }

            TickManager tm = Find.TickManager;
            if (tm == null)
            {
                TolkHelper.Speak("Performance data not available", SpeechPriority.High);
                return;
            }

            TimeSpeed speed = tm.CurTimeSpeed;
            float multiplier = tm.TickRateMultiplier;

            // Paused: no meaningful performance data (multiplier is 0, would divide by zero).
            if (speed == TimeSpeed.Paused || multiplier <= 0f)
            {
                TolkHelper.Speak("Game paused");
                return;
            }

            float meanTickTime = tm.MeanTickTime;
            // Guard against divide-by-zero immediately after load, before any ticks run.
            if (meanTickTime <= 0.0001f)
            {
                TolkHelper.Speak("Performance data not available");
                return;
            }

            // Mirror RimWorld's GlobalControlsUtility.DrawTpsCounter exactly:
            //   actualTps = 1000 / meanTickTime
            //   targetTps = 60 * TickRateMultiplier
            //   displayed = min(actual, target)
            float actualTps = 1000f / meanTickTime;
            float targetTps = 60f * multiplier;
            float clampedTps = Mathf.Min(actualTps, targetTps);

            int actualTpsInt = Mathf.RoundToInt(clampedTps);
            int targetTpsInt = Mathf.RoundToInt(targetTps);

            int percent = Mathf.RoundToInt(clampedTps / targetTps * 100f);
            if (percent > 100) percent = 100;
            if (percent < 0) percent = 0;

            string speedName = TimeControlAccessibilityPatch.LocalizedSpeedName(speed);
            bool slowedByThreat = tm.slower != null && tm.slower.ForcedNormalSpeed;
            string threatClause = slowedByThreat ? ", slowed by threat" : "";
            string announcement =
                $"{speedName} speed{threatClause}, {actualTpsInt} of {targetTpsInt} ticks per second, {percent} percent";

            TolkHelper.Speak(announcement);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }
    }
}
