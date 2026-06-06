using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to announce time speed changes for screen reader accessibility.
    ///
    /// Time controls are now bound to Shift+1/2/3 (handled in UnifiedKeyboardPatch):
    /// - Shift+1: Normal speed
    /// - Shift+2: Fast speed
    /// - Shift+3: Superfast speed
    ///
    /// This patch monitors all time speed changes (regardless of how they're triggered)
    /// and announces them to the screen reader via clipboard.
    /// </summary>
    [HarmonyPatch(typeof(TickManager))]
    public class TimeControlAccessibilityPatch
    {
        private static TimeSpeed lastAnnouncedSpeed = TimeSpeed.Normal;
        private static bool isInitialized = false;

        // Patch the CurTimeSpeed setter to announce when time speed changes
        [HarmonyPatch("CurTimeSpeed", MethodType.Setter)]
        [HarmonyPostfix]
        public static void CurTimeSpeed_Postfix(TickManager __instance)
        {
            // Initialize on first call
            if (!isInitialized)
            {
                lastAnnouncedSpeed = __instance.CurTimeSpeed;
                isInitialized = true;
                return;
            }

            // Only announce if speed actually changed
            if (__instance.CurTimeSpeed != lastAnnouncedSpeed)
            {
                string announcement = GetTimeSpeedAnnouncement(__instance.CurTimeSpeed);
                TolkHelper.SpeakData(announcement);
                lastAnnouncedSpeed = __instance.CurTimeSpeed;
            }
        }

        // Patch the TogglePaused method to catch pause/unpause
        // TogglePaused modifies the curTimeSpeed field directly, bypassing the setter
        [HarmonyPatch("TogglePaused")]
        [HarmonyPostfix]
        public static void TogglePaused_Postfix(TickManager __instance)
        {
            // Initialize on first call
            if (!isInitialized)
            {
                lastAnnouncedSpeed = __instance.CurTimeSpeed;
                isInitialized = true;
                return;
            }

            // Announce the new speed since TogglePaused bypasses the setter
            if (__instance.CurTimeSpeed != lastAnnouncedSpeed)
            {
                string announcement = GetTimeSpeedAnnouncement(__instance.CurTimeSpeed);
                TolkHelper.SpeakData(announcement);
                lastAnnouncedSpeed = __instance.CurTimeSpeed;
            }
        }

        private static string GetTimeSpeedAnnouncement(TimeSpeed speed)
        {
            if (speed == TimeSpeed.Paused)
                return "RimWorldAccess.Map.Game.Paused".Translate();
            return "RimWorldAccess.Map.Time.SpeedAnnouncement".Translate(LocalizedSpeedName(speed));
        }

        internal static string LocalizedSpeedName(TimeSpeed speed)
        {
            switch (speed)
            {
                case TimeSpeed.Paused:    return "RimWorldAccess.Map.Time.Speed.Paused".Translate();
                case TimeSpeed.Normal:    return "RimWorldAccess.Map.Time.Speed.Normal".Translate();
                case TimeSpeed.Fast:      return "RimWorldAccess.Map.Time.Speed.Fast".Translate();
                case TimeSpeed.Superfast: return "RimWorldAccess.Map.Time.Speed.Superfast".Translate();
                case TimeSpeed.Ultrafast: return "RimWorldAccess.Map.Time.Speed.Ultrafast".Translate();
                default: return speed.ToString();
            }
        }

        // Tracks the last observed ForcedNormalSpeed state so we can detect
        // transitions in either direction and announce them via Messages.Message.
        // The existing NotificationAccessibilityPatch automatically speaks any
        // Messages.Message() call, so emitting a message is enough.
        private static bool wasForcedNormalSpeed = false;

        [HarmonyPatch(nameof(TickManager.TickManagerUpdate))]
        [HarmonyPostfix]
        public static void TickManagerUpdate_ThreatSlowdown_Postfix(TickManager __instance)
        {
            if (__instance?.slower == null) return;
            if (Current.ProgramState != ProgramState.Playing) return;

            bool isForced = __instance.slower.ForcedNormalSpeed;

            // Gate announcements behind the user setting (off by default).
            // Still track the forced-state transitions so toggling the
            // setting on mid-threat doesn't cause a spurious announcement.
            if (!(RimWorldAccessMod_Settings.Settings?.AnnounceForcedSlowdowns ?? false))
            {
                wasForcedNormalSpeed = isForced;
                return;
            }

            // Only announce when the user's set speed is faster than Normal.
            // If they're on Paused or Normal, the forced slowdown has no
            // observable effect, so announcing would just be noise.
            bool userSpeedFasterThanNormal = __instance.CurTimeSpeed > TimeSpeed.Normal;

            if (isForced && !wasForcedNormalSpeed && userSpeedFasterThanNormal)
            {
                // OFF -> ON: a threat just triggered the combat slowdown.
                Messages.Message(
                    "RimWorldAccess.Map.Time.Threat.Slowed".Translate(),
                    MessageTypeDefOf.NeutralEvent,
                    historical: false);
            }
            else if (!isForced && wasForcedNormalSpeed && userSpeedFasterThanNormal)
            {
                // ON -> OFF: the threat slowdown has expired and the game
                // is about to resume the user's faster set speed.
                Messages.Message(
                    "RimWorldAccess.Map.Time.Threat.Passed".Translate(),
                    MessageTypeDefOf.NeutralEvent,
                    historical: false);
            }

            wasForcedNormalSpeed = isForced;
        }
    }
}
