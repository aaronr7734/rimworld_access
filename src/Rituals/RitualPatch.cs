using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_BeginLordJob and its subclasses (Dialog_BeginRitual,
    /// Dialog_BeginPsychicRitual, Dialog_BeginGravshipLaunch). Keeps LordJobDialogState
    /// synchronized with the dialog lifecycle and blocks the game's default Accept/Cancel
    /// handling while our keyboard navigation is active.
    /// </summary>
    public static class RitualPatch
    {
        // ===== Open hooks =====

        /// <summary>
        /// Dialog_BeginRitual overrides PostOpen and runs assignments.FillPawns + the per-comp
        /// notify loop before returning. Our Postfix here catches that completed state for
        /// ideology rituals AND gravship launches (gravship inherits the same PostOpen).
        /// </summary>
        [HarmonyPatch(typeof(Dialog_BeginRitual), "PostOpen")]
        public static class Dialog_BeginRitual_PostOpen_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Dialog_BeginRitual __instance)
            {
                try
                {
                    LordJobDialogState.Open(__instance);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RitualPatch] Error in Dialog_BeginRitual.PostOpen: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Dialog_BeginPsychicRitual does not override PostOpen, so we patch the base
        /// Window.PostOpen and filter. The psychic dialog's constructor populates assignments
        /// fully before PostOpen fires, so our state can read them safely here.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostOpen")]
        public static class Window_PostOpen_PsychicRitual_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_BeginPsychicRitual psychic)) return;
                try
                {
                    LordJobDialogState.Open(psychic);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RitualPatch] Error in Window.PostOpen psychic: {ex.Message}");
                }
            }
        }

        // ===== Close hook =====

        /// <summary>
        /// Fires regardless of HOW the dialog closes (Cancel, Start, click outside, etc.).
        /// Our state MUST close when the game's dialog closes, and we announce via the
        /// adapter's localized closing text so each dialog type gets the right phrase.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_LordJob_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_BeginLordJob)) return;
                try
                {
                    if (LordJobDialogState.IsActive)
                    {
                        string announcement = LordJobDialogState.ClosingAnnouncement;
                        LordJobDialogState.Close();
                        if (!string.IsNullOrEmpty(announcement))
                            TolkHelper.SpeakData(announcement);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[RitualPatch] Error in PostClose: {ex.Message}");
                }
            }
        }

        // ===== Start prefixes =====
        //
        // Start() is virtual on Dialog_BeginLordJob and overridden by each subclass. Harmony
        // patches on virtual methods only intercept calls dispatched through the patched
        // method itself, so we patch each concrete subclass separately. The shared prefix
        // returns false (skip original) while LordJobDialogState is active, so Enter / OK
        // button presses don't begin the job behind the user's back; Alt+S sets IsActive=false
        // before invoking OnAcceptKeyPressed → Start so the prefix lets it through.

        public static bool LordJobStartPrefix()
        {
            if (LordJobDialogState.IsActive) return false;
            return true;
        }

        [HarmonyPatch(typeof(Dialog_BeginRitual), "Start")]
        public static class Dialog_BeginRitual_Start_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix() => LordJobStartPrefix();
        }

        // Dialog_BeginPsychicRitual.Start and Dialog_BeginGravshipLaunch.Start are patched
        // manually in Core/rimworld_access.cs via AccessTools.TypeByName so the mod loads
        // cleanly when the relevant DLC types are present (they ship in RimWorld.dll, but
        // applying the patches lazily lets us keep DLC handling consistent with EntityCodex).

        // ===== OnAcceptKeyPressed / OnCancelKeyPressed =====

        /// <summary>
        /// Catches Enter at the Window level BEFORE it reaches the dialog. Without this,
        /// RimWorld's KeyBindingDefOf.Accept.KeyDownEvent triggers Window.OnAcceptKeyPressed
        /// independently of UnifiedKeyboardPatch, which can fire Start().
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
        public static class Window_OnAcceptKeyPressed_LordJob_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (!(__instance is Dialog_BeginLordJob)) return true;

                if (LordJobDialogState.IsActive) return false;
                if (StatBreakdownState.IsActive || WindowlessInspectionState.IsActive) return false;
                return true;
            }
        }

        /// <summary>
        /// Block Cancel handling while we're in a submenu (pawn selection / quality stats)
        /// or while overlay states like StatBreakdown are open over the dialog.
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        public static class Window_OnCancelKeyPressed_LordJob_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (!(__instance is Dialog_BeginLordJob)) return true;

                if (StatBreakdownState.IsActive || WindowlessInspectionState.IsActive) return false;

                if (LordJobDialogState.IsActive)
                {
                    var mode = LordJobDialogState.CurrentNavigationMode;
                    if (mode != LordJobDialogState.NavigationMode.RoleList) return false;
                    if (LordJobDialogState.HasActiveTypeahead) return false;
                }
                return true;
            }
        }

        // ===== Visual indicator =====

        /// <summary>
        /// Draw a small "Keyboard Mode Active" badge in the dialog when our state is driving
        /// it. Patches Dialog_BeginLordJob.DoWindowContents so all subclasses share it.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_BeginLordJob), "DoWindowContents")]
        public static class Dialog_BeginLordJob_DoWindowContents_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Dialog_BeginLordJob __instance, Rect inRect)
            {
                if (!LordJobDialogState.IsActive) return;
                try
                {
                    DrawKeyboardModeIndicator(inRect);
                }
                catch
                {
                    // tolerate drawing errors
                }
            }

            private static void DrawKeyboardModeIndicator(Rect inRect)
            {
                float w = 250f;
                float h = 30f;
                Rect r = new Rect(inRect.x + 10f, inRect.y + 10f, w, h);

                Widgets.DrawBoxSolid(r, new Color(0.2f, 0.4f, 0.6f, 0.85f));
                Widgets.DrawBox(r, 1);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(r, "Keyboard Mode Active");
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
        }
    }
}
