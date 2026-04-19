using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches to enable keyboard accessibility for ritual dialogs.
    ///
    /// Key principle: Our state MUST stay synchronized with the game's dialog.
    /// If the dialog closes for ANY reason, our state closes too.
    /// </summary>
    public static class RitualPatch
    {
        /// <summary>
        /// Activate keyboard navigation when Dialog_BeginRitual opens.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_BeginRitual), "PostOpen")]
        public static class Dialog_BeginRitual_PostOpen_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Dialog_BeginRitual __instance)
            {
                try
                {
                    RitualState.Open(__instance);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RitualPatch] Error in PostOpen: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// CRITICAL: Deactivate keyboard navigation when Dialog_BeginRitual closes.
        /// This fires regardless of HOW the dialog closes (Cancel, Start ritual, click outside, etc.)
        /// Our state MUST close when the game's dialog closes.
        ///
        /// Note: We patch Window.PostClose because Dialog_BeginRitual does NOT override PostClose.
        /// Patching a non-existent method on the derived class causes Harmony errors.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_Ritual_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                // Only handle Dialog_BeginRitual
                if (!(__instance is Dialog_BeginRitual))
                    return;

                try
                {
                    // Always close our state when the dialog closes, no matter what
                    if (RitualState.IsActive)
                    {
                        RitualState.Close();
                        TolkHelper.Speak("RimWorldAccess.Rituals.Dialog.Closed".Translate());
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[RitualPatch] Error in PostClose: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// CRITICAL: Block Start() when RitualState is active.
        /// This is the ONLY method that actually begins the ritual.
        /// All paths lead here: OnAcceptKeyPressed, OK button click, etc.
        ///
        /// When user wants to start via Alt+S, RitualState sets IsActive=false
        /// BEFORE calling OnAcceptKeyPressed, so Start() will be allowed through.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_BeginRitual), "Start")]
        public static class Dialog_BeginRitual_Start_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                // Block Start() when our keyboard navigation is active
                // This prevents Enter key (or any other path) from starting the ritual
                // Alt+S sets IsActive=false before triggering, so it passes through
                if (RitualState.IsActive)
                {
                    // Don't announce anything - the Enter key was handled by our navigation
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Runtime prefix for Dialog_BeginGravshipLaunch.Start().
        /// Applied via manual Harmony patch in rimworld_access.cs (DLC-safe).
        /// Dialog_BeginGravshipLaunch overrides Start(), so the base class patch above doesn't intercept it.
        /// </summary>
        public static bool GravshipStartPrefix()
        {
            if (RitualState.IsActive)
                return false;
            return true;
        }

        /// <summary>
        /// CRITICAL: Block Window.OnAcceptKeyPressed when RitualState is active.
        /// This catches Enter at the Window level BEFORE it reaches the dialog.
        ///
        /// This is the key fix - CaravanFormationPatch uses this same pattern (lines 60-70).
        /// Without this, RimWorld's KeyBindingDefOf.Accept.KeyDownEvent triggers
        /// Window.OnAcceptKeyPressed independently of our UnifiedKeyboardPatch.
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
        public static class Window_OnAcceptKeyPressed_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                // Only intercept for Dialog_BeginRitual
                if (__instance is Dialog_BeginRitual)
                {
                    // Block game's Accept handling when our keyboard nav is active
                    if (RitualState.IsActive)
                    {
                        return false;
                    }

                    // Also block if overlay states are active
                    if (StatBreakdownState.IsActive || WindowlessInspectionState.IsActive)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Block Window.OnCancelKeyPressed when our state is active and in a submenu.
        /// This prevents Escape from closing the dialog when we're in pawn selection or quality stats.
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        public static class Window_OnCancelKeyPressed_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                // Only intercept for Dialog_BeginRitual
                if (__instance is Dialog_BeginRitual)
                {
                    // Block game's Cancel handling when our overlay states are active
                    if (StatBreakdownState.IsActive || WindowlessInspectionState.IsActive)
                    {
                        return false;
                    }

                    // Block if RitualState is in a submenu (not RoleList) or has typeahead
                    if (RitualState.IsActive)
                    {
                        var mode = RitualState.CurrentNavigationMode;

                        // If in pawn selection or quality stats, block game's Escape
                        if (mode != RitualState.NavigationMode.RoleList)
                        {
                            return false;
                        }

                        // If typeahead is active, block game's Escape
                        if (RitualState.HasActiveTypeahead)
                        {
                            return false;
                        }
                    }
                }

                return true; // Let original method run
            }
        }

        /// <summary>
        /// Draw visual indicator that keyboard mode is active.
        /// Note: DoWindowContents is defined in Dialog_BeginLordJob, not Dialog_BeginRitual.
        /// We patch the parent class and check if the instance is Dialog_BeginRitual.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_BeginLordJob), "DoWindowContents")]
        public static class Dialog_BeginRitual_DoWindowContents_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Dialog_BeginLordJob __instance, Rect inRect)
            {
                // Only handle Dialog_BeginRitual
                if (!(__instance is Dialog_BeginRitual))
                    return;

                if (!RitualState.IsActive) return;

                try
                {
                    DrawKeyboardModeIndicator(inRect);
                }
                catch
                {
                    // Ignore drawing errors
                }
            }

            private static void DrawKeyboardModeIndicator(Rect inRect)
            {
                float indicatorWidth = 250f;
                float indicatorHeight = 30f;
                Rect indicatorRect = new Rect(inRect.x + 10f, inRect.y + 10f, indicatorWidth, indicatorHeight);

                Color backgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.85f);
                Widgets.DrawBoxSolid(indicatorRect, backgroundColor);
                Widgets.DrawBox(indicatorRect, 1);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(indicatorRect, "RimWorldAccess.Rituals.Dialog.KeyboardModeIndicator".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
        }
    }
}
