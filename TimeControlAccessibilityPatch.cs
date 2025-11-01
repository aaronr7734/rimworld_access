using HarmonyLib;
using Verse;
using System.Reflection;

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
                ClipboardHelper.CopyToClipboard(announcement);
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
                ClipboardHelper.CopyToClipboard(announcement);
                lastAnnouncedSpeed = __instance.CurTimeSpeed;
            }
        }

        private static string GetTimeSpeedAnnouncement(TimeSpeed speed)
        {
            switch (speed)
            {
                case TimeSpeed.Paused:
                    return "Game paused";
                case TimeSpeed.Normal:
                    return "Time speed: Normal";
                case TimeSpeed.Fast:
                    return "Time speed: Fast";
                case TimeSpeed.Superfast:
                    return "Time speed: Superfast";
                case TimeSpeed.Ultrafast:
                    return "Time speed: Ultrafast";
                default:
                    return $"Time speed: {speed}";
            }
        }
    }
}
