using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches that drive the IdeoBuilder hub on top of Page_ConfigureIdeo
    /// and its Fluid subclass.
    ///
    /// We follow the world-gen patching pattern (see IdeologySelectionPatch): a Prefix
    /// on DoWindowContents reads Event.current directly, since Pages run during
    /// ProgramState.Entry and routing through UnifiedKeyboardPatch is less reliable
    /// when IMGUI focus has not been established on the Page window.
    ///
    /// Phase 1 limitations:
    /// - Editors for each section are not yet wired up (Phases 2-6 fill them in).
    /// - The auto-popup Dialog_ChooseMemes that vanilla shows for a fresh empty ideo
    ///   is suppressed and replaced with a one-shot randomization so the hub has a
    ///   valid ideo to display. Phase 2 replaces the suppression with proper
    ///   accessibility for the meme picker.
    /// </summary>
    [HarmonyPatch(typeof(Page_ConfigureIdeo), "DoWindowContents")]
    public static class IdeoBuilderHubPatch
    {
        private static MethodInfo doNextMethod;
        private static MethodInfo doBackMethod;
        private static MethodInfo canDoNextMethod;
        private static MethodInfo canDoBackMethod;

        private static bool subEditorWasOpen;

        private static void EnsureReflectionCached()
        {
            if (doNextMethod != null) return;
            doNextMethod = AccessTools.Method(typeof(Page), "DoNext");
            doBackMethod = AccessTools.Method(typeof(Page), "DoBack");
            canDoNextMethod = AccessTools.Method(typeof(Page), "CanDoNext");
            canDoBackMethod = AccessTools.Method(typeof(Page), "CanDoBack");
        }

        static bool Prefix(Page_ConfigureIdeo __instance, Rect rect)
        {
            try
            {
                // While the modal text input controller is active, skip — keys are owned by it
                // and any Page-level Enter/Escape handling would interfere.
                if (TextInputManager.Active != null)
                    return false;

                // A confirmation / message box owns input while open — let it handle its own keys.
                // The DoNext / DoBack guards stop the page from advancing on a stray keypress; we
                // must NOT consume Enter/Escape here or the dialog's own buttons stop working.
                if (WindowlessDialogState.IsActive || WindowlessConfirmationState.IsActive)
                    return true;

                EnsureReflectionCached();

                if (__instance.ideo == null)
                    return true; // Nothing to do; let the page draw its empty state.

                IdeoBuilderHubState.EnsureOpen(__instance.ideo);
                // Keep any ritual-sound preview alive even while a context menu / editor is open.
                IdeoBuilderHubState.MaintainRitualPreview();

                bool floatMenuOpen = WindowlessFloatMenuState.IsActive;

                // A windowless overlay editor (precept / typed-precept / deity) takes keyboard
                // priority over the hub.
                if (IdeoBuilderOverlays.AnyActive)
                {
                    subEditorWasOpen = true;

                    // While a sub-picker float menu is open, it owns the keyboard (routed by
                    // UnifiedKeyboardPatch). Skip the page so DoBottomButtons can't grab keys.
                    if (floatMenuOpen)
                    {
                        IdeoBuilderOverlays.NoteFloatMenuOpen();
                        return false;
                    }

                    IdeoBuilderOverlays.RefreshIfReturnedFromFloatMenu();

                    if (Event.current.type == EventType.KeyDown && IdeoBuilderOverlays.RouteKeyDown(Event.current))
                        Event.current.Use();
                    return true;
                }

                // No overlay: a bare float menu (rare here) still owns the keyboard.
                if (floatMenuOpen)
                    return false;

                // If a sub-editor dialog (meme picker, etc.) is on top, don't process hub
                // input — it owns the keyboard. Mark so we can rebuild sections on return.
                if (IsSubEditorOpen())
                {
                    subEditorWasOpen = true;
                    // Swallow Tab while the dialog owns the keyboard. The page is drawn beneath the
                    // dialog, so if vanilla's page handles Tab it cycles IMGUI focus to its own
                    // controls and steals keyboard focus away from the dialog (the dialog then goes
                    // unresponsive / appears to jump back). Consuming Tab here keeps focus put.
                    if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Tab)
                        Event.current.Use();
                    return true;
                }

                // Just returned from a sub-editor / overlay — the ideo may have been mutated.
                if (subEditorWasOpen)
                {
                    subEditorWasOpen = false;
                    // The sub-editor window closed and the page is revealed again; reclaim IMGUI
                    // focus so the page receives keystrokes. Without this the hub is dead to input
                    // (same focus-loss bug fixed for the meme dialog's PostOpen).
                    Find.WindowStack.Notify_ManuallySetFocus(__instance);
                    IdeoBuilderHubState.RebuildSections();
                    // If the opening announcement hasn't fired yet (first arrival, e.g. straight
                    // from the initial meme picker), let it announce instead of double-speaking.
                    if (IdeoBuilderHubState.HasAnnouncedOpening)
                        IdeoBuilderHubState.AnnounceCurrentSection();
                }

                IdeoBuilderHubState.AnnounceOpeningIfNeeded();

                if (Event.current.type == EventType.KeyDown)
                {
                    HandleKeyDown(__instance);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in IdeoBuilderHubPatch.Prefix: {ex}");
            }
            return true; // Run original DoWindowContents so visuals still render.
        }

        /// <summary>
        /// True if one of the builder's sub-editor dialogs is currently open on top of the
        /// configure page. While true, the hub yields keyboard control to that dialog.
        /// </summary>
        private static bool IsSubEditorOpen()
        {
            var stack = Find.WindowStack;
            return stack.WindowOfType<Dialog_ChooseMemes>() != null;
        }

        private static void HandleKeyDown(Page_ConfigureIdeo page)
        {
            KeyCode key = Event.current.keyCode;
            bool ctrl = Event.current.control;
            bool alt = KeyboardHelper.IsAltHeld;
            bool shift = Event.current.shift;

            // Alt+S — continue (DoNext, with validation)
            if (key == KeyCode.S && alt && !ctrl)
            {
                TryDoNext(page);
                Event.current.Use();
                return;
            }

            // Alt+R — randomize the whole ideoligion (vanilla "Randomize all" button)
            if (key == KeyCode.R && alt && !ctrl)
            {
                TryRandomizeAll(page);
                Event.current.Use();
                return;
            }

            // Up / Down / Home / End — section navigation
            if (key == KeyCode.UpArrow)
            {
                IdeoBuilderHubState.NavigateUp();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.DownArrow)
            {
                IdeoBuilderHubState.NavigateDown();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.Home)
            {
                IdeoBuilderHubState.NavigateHome();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.End)
            {
                IdeoBuilderHubState.NavigateEnd();
                Event.current.Use();
                return;
            }

            // Enter — activate the selected section (opens editor; Phase 1 = placeholder)
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                IdeoBuilderHubState.ActivateSelected();
                Event.current.Use();
                return;
            }

            // Space — re-announce current section
            if (key == KeyCode.Space && !alt && !ctrl)
            {
                IdeoBuilderHubState.AnnounceCurrentSection();
                Event.current.Use();
                return;
            }

            // ] — builder context menu (save ideoligion to file, preview ritual sound)
            if (key == KeyCode.RightBracket && !alt && !ctrl)
            {
                IdeoBuilderHubState.OpenContextMenu();
                Event.current.Use();
                return;
            }

            // Escape — clear typeahead, else go back to the previous page
            if (key == KeyCode.Escape)
            {
                if (IdeoBuilderHubState.HasActiveSearch)
                {
                    IdeoBuilderHubState.ClearSearch();
                    Event.current.Use();
                    return;
                }
                TryDoBack(page);
                Event.current.Use();
                return;
            }

            // Backspace — typeahead backspace
            if (key == KeyCode.Backspace)
            {
                if (IdeoBuilderHubState.HandleBackspace())
                {
                    Event.current.Use();
                    return;
                }
            }

            // Typeahead — letters/digits without modifiers
            if (!alt && !ctrl)
            {
                char c = Event.current.character;
                if (c != '\0' && char.IsLetterOrDigit(c))
                {
                    IdeoBuilderHubState.HandleTypeaheadChar(c);
                    Event.current.Use();
                    return;
                }
            }
        }

        #region DoNext / DoBack via reflection

        private static void TryDoNext(Page_ConfigureIdeo page)
        {
            string err = IdeoBuilderHelper.BuildValidationSummary(page.ideo);
            if (!string.IsNullOrEmpty(err))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak(err, SpeechPriority.High);
                return;
            }

            bool canNext = true;
            if (canDoNextMethod != null)
                canNext = (bool)canDoNextMethod.Invoke(page, null);
            if (!canNext)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            explicitDoNext = true;
            try { doNextMethod?.Invoke(page, null); }
            finally { explicitDoNext = false; }
        }

        // Set true around our own intentional DoNext (Alt+S) so the Page.DoNext guard lets it
        // through; any other DoNext (a stray Enter falling through to the page's Next button) is
        // blocked while a dialog / editor owns the keyboard.
        internal static bool explicitDoNext;

        private static void TryDoBack(Page_ConfigureIdeo page)
        {
            bool canBack = true;
            if (canDoBackMethod != null)
                canBack = (bool)canDoBackMethod.Invoke(page, null);
            if (!canBack)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            // Leaving discards the custom ideoligion the player just built, so confirm first
            // (mirrors the colonist-creation discard guard).
            RequestBackConfirm(page);
        }

        // Set true around our own confirmed DoBack so the Page.DoBack guard lets it through.
        internal static bool explicitDoBack;

        /// <summary>
        /// Shows a discard-confirmation before leaving the builder. Continue performs the real
        /// DoBack; Cancel keeps the page. No-op if a dialog is already open (double-Escape).
        /// </summary>
        internal static void RequestBackConfirm(Page_ConfigureIdeo page)
        {
            if (WindowlessDialogState.IsActive || WindowlessConfirmationState.IsActive)
                return;

            // Consume the triggering Escape so the dialog doesn't immediately catch the same press.
            if (Event.current != null && Event.current.type == EventType.KeyDown)
                Event.current.Use();

            Action confirm = () => DoBackConfirmed(page);
            Find.WindowStack.Add(new Dialog_MessageBox(
                "Going back will discard this custom ideoligion. Continue?",
                buttonAText: "Continue",
                buttonAAction: confirm,
                buttonBText: "Cancel",
                buttonBAction: null,
                title: null,
                buttonADestructive: true,
                acceptAction: confirm,
                cancelAction: delegate { }));
        }

        private static void DoBackConfirmed(Page_ConfigureIdeo page)
        {
            explicitDoBack = true;
            try { doBackMethod?.Invoke(page, null); }
            catch (Exception ex) { Log.Error($"[RimWorld Access] Error in ideo DoBack: {ex}"); }
            finally { explicitDoBack = false; }
        }

        /// <summary>
        /// Leaves the builder after the player abandoned an unconfigured ideo — e.g. backing out of
        /// the initial structure picker, which makes vanilla remove the empty ideo while leaving
        /// page.ideo dangling at it. Clears that dangling reference and returns to the previous page
        /// (preset selection) without the discard confirmation, since nothing was built to discard.
        /// </summary>
        internal static void LeaveBuilderAbandoned(Page_ConfigureIdeo page)
        {
            if (page == null) return;
            EnsureReflectionCached();
            IdeoBuilderHubState.Close();
            page.ideo = null;
            IdeoUIUtility.UnselectCurrent();
            DoBackConfirmed(page);
        }

        private static void TryRandomizeAll(Page_ConfigureIdeo page)
        {
            if (page.ideo == null) return;
            try
            {
                if (!TutorSystem.AllowAction("ConfiguringIdeo"))
                    return;
                var parms = new IdeoGenerationParms(
                    IdeoUIUtility.FactionForRandomization(page.ideo),
                    forceNoExpansionIdeo: false,
                    null, null, null,
                    classicExtra: false,
                    forceNoWeaponPreference: false,
                    page.ideo.Fluid);
                page.ideo.foundation.Init(parms);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                IdeoBuilderHubState.RebuildSections();
                IdeoBuilderHubState.AnnounceCurrentSection();
                IdeoBuilderHubState.AnnounceValidationOrImpact();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error randomizing ideoligion: {ex}");
            }
        }

        #endregion
    }

    /// <summary>
    /// Guards the custom-ideoligion builder against an accidental Escape/Back that would silently
    /// discard the whole ideoligion. Page.DoBottomButtons calls DoBack directly off
    /// KeyBindingDefOf.Cancel (bypassing closeOnCancel), so we intercept DoBack and route through a
    /// confirmation. Our own confirmed back sets explicitDoBack to pass; a sub-editor owning the
    /// keyboard blocks DoBack entirely (Escape there belongs to the editor, not "leave builder").
    /// </summary>
    [HarmonyPatch(typeof(Page), "DoBack")]
    public static class IdeoBuilderHubPatch_DoBackGuard
    {
        [HarmonyPrefix]
        static bool Prefix(Page __instance)
        {
            if (!(__instance is Page_ConfigureIdeo page))
                return true;
            if (IdeoBuilderHubPatch.explicitDoBack)
                return true;
            if (TextInputManager.Active != null || WindowlessFloatMenuState.IsActive
                || WindowlessDialogState.IsActive || WindowlessConfirmationState.IsActive
                || IdeoBuilderOverlays.AnyActive
                || Find.WindowStack.WindowOfType<Dialog_ChooseMemes>() != null)
                return false;
            IdeoBuilderHubPatch.RequestBackConfirm(page);
            return false;
        }
    }

    /// <summary>
    /// Mirror of the DoBack guard for the forward direction. Page.DoBottomButtons fires DoNext on
    /// the Accept key (Enter), so a stray Enter — confirming/cancelling the discard dialog, or while
    /// an editor/float-menu owns the keyboard — would otherwise advance the player to the next page
    /// (character creation). Block DoNext for the configure page unless it's our own Alt+S advance
    /// (explicitDoNext) or nothing is owning input.
    /// </summary>
    [HarmonyPatch(typeof(Page), "DoNext")]
    public static class IdeoBuilderHubPatch_DoNextGuard
    {
        [HarmonyPrefix]
        static bool Prefix(Page __instance)
        {
            if (!(__instance is Page_ConfigureIdeo))
                return true;
            if (IdeoBuilderHubPatch.explicitDoNext)
                return true;
            if (TextInputManager.Active != null || WindowlessFloatMenuState.IsActive
                || WindowlessDialogState.IsActive || WindowlessConfirmationState.IsActive
                || IdeoBuilderOverlays.AnyActive
                || Find.WindowStack.WindowOfType<Dialog_ChooseMemes>() != null)
                return false;
            return true;
        }
    }

    /// <summary>
    /// Closes the hub state when the configure page closes, regardless of which subclass
    /// (Fixed or Fluid) was open. PostClose is declared on Window (not overridden by the
    /// page), so we patch it there and filter by instance type — Page_ConfigureFluidIdeo
    /// derives from Page_ConfigureIdeo, so the single check covers both.
    /// </summary>
    [HarmonyPatch(typeof(Window), "PostClose")]
    public static class IdeoBuilderHubPatch_Close
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Page_ConfigureIdeo)
                IdeoBuilderHubState.Close();
        }
    }

    /// <summary>
    /// PostOpen hook. For Custom Fixed entry, vanilla's Page_ConfigureIdeo.PostOpen only
    /// creates an ideoligion when IdeoUIUtility.selected was already set (which only happens
    /// when coming back from a later page). On first entry, ideo stays null and the player is
    /// supposed to click a "create new ideoligion" button. We call SelectOrMakeNewIdeo()
    /// ourselves so a live ideo exists; vanilla's SelectOrMakeNewIdeo then auto-opens the
    /// (now accessible) Dialog_ChooseMemes structure picker, which flows into the normal-meme
    /// picker and finally lands the player on the hub.
    /// </summary>
    [HarmonyPatch(typeof(Page_ConfigureIdeo), "PostOpen")]
    public static class IdeoBuilderHubPatch_PostOpen
    {
        [HarmonyPostfix]
        static void Postfix(Page_ConfigureIdeo __instance)
        {
            try
            {
                if (__instance.ideo == null)
                {
                    __instance.SelectOrMakeNewIdeo();
                }

                // Reclaim IMGUI focus for the hub, but only if SelectOrMakeNewIdeo did NOT spawn a
                // meme picker on top (first entry) — in that case the picker should keep focus.
                // This covers arriving at the hub via Back from a later page.
                if (Find.WindowStack.WindowOfType<Dialog_ChooseMemes>() == null)
                    Find.WindowStack.Notify_ManuallySetFocus(__instance);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in IdeoBuilderHubPatch_PostOpen: {ex}");
            }
        }
    }
}
