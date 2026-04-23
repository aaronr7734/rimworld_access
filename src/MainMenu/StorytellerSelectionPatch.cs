using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch for Page_SelectStoryteller (main menu difficulty selection).
    /// Provides keyboard navigation for storyteller, difficulty, and permadeath selection.
    /// </summary>
    [HarmonyPatch(typeof(Page_SelectStoryteller))]
    [HarmonyPatch("DoWindowContents")]
    public class StorytellerSelectionPatch
    {
        private static bool patchActive = false;
        private static bool hasAnnouncedTitle = false;
        private enum NavigationMode { Storyteller, Difficulty, Permadeath, AnomalySettings }
        private static NavigationMode currentMode = NavigationMode.Storyteller;

        // The "Anomaly Settings..." button is only drawn by StorytellerUI when Anomaly is active
        // (StorytellerUI.cs:133). Mirror that here so the Tab cycle skips a non-existent button.
        private static bool AnomalySettingsAvailable => ModsConfig.AnomalyActive;

        // Prefix: Initialize state and handle keyboard input
        static void Prefix(Page_SelectStoryteller __instance, Rect rect)
        {
            try
            {
                // Initialize navigation state
                StorytellerNavigationState.Initialize();

                // Restore IMGUI focus to this page. After closing certain dialogs
                // (e.g., faction relations from site selection), IMGUI focus may be
                // lost to a deleted window, preventing KeyDown events from arriving.
                // Same pattern used in IdeologySelectionPatch and StartingPawnPatch.
                //
                // CRITICAL: Skip when an absorbing modal child dialog is on top —
                // otherwise we steal focus away every frame and the dialog can never
                // receive keyboard input. Dialog_AnomalySettings is absorbInputAroundWindow=true,
                // so it must own focus while it's open.
                if (!AnomalySettingsDialogState.IsActive)
                {
                    Find.WindowStack.Notify_ManuallySetFocus(__instance);
                }

                // Announce window title and initial selection once
                if (!hasAnnouncedTitle)
                {
                    string pageTitle = "ChooseAIStoryteller".Translate();
                    StorytellerDef storyteller = StorytellerNavigationState.SelectedStoryteller;
                    if (storyteller != null)
                    {
                        string position = MenuHelper.FormatPosition(0, StorytellerNavigationState.StorytellerCount);
                        string description = storyteller.description.TrimEnd('.');
                        string positionPart = string.IsNullOrEmpty(position) ? "" : $" ({position})";
                        string tabHint = AnomalySettingsAvailable
                            ? "Tab and Shift+Tab to move between Storyteller, Difficulty, Save Mode, and Anomaly Settings."
                            : "Tab and Shift+Tab to move between Storyteller, Difficulty, and Save Mode.";
                        TolkHelper.Speak($"{pageTitle} - {storyteller.label} - {description}{positionPart}. {tabHint}");
                    }
                    else
                    {
                        TolkHelper.Speak(pageTitle);
                    }
                    hasAnnouncedTitle = true;
                }

                // Handle keyboard input
                if (Event.current.type == EventType.KeyDown)
                {
                    KeyCode keyCode = Event.current.keyCode;
                    bool handled = false;

                    // Custom difficulty edit mode takes priority
                    if (CustomDifficultyEditState.IsActive)
                    {
                        handled = HandleCustomDifficultyInput(keyCode);
                        if (handled)
                        {
                            Event.current.Use();
                            patchActive = true;
                        }
                        return; // Don't process other keys when in custom edit mode
                    }

                    // Tab navigation between modes
                    if (keyCode == KeyCode.Tab && !Event.current.shift)
                    {
                        CycleNavigationModeForward(__instance);
                        handled = true;
                    }
                    else if (keyCode == KeyCode.Tab && Event.current.shift)
                    {
                        CycleNavigationModeBackward(__instance);
                        handled = true;
                    }
                    // Enter/Space - open custom difficulty if selected
                    else if ((keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter || keyCode == KeyCode.Space) &&
                             currentMode == NavigationMode.Difficulty)
                    {
                        handled = HandleEnterOnDifficulty(__instance);
                    }
                    // Enter/Space on the Anomaly Settings row opens Dialog_AnomalySettings.
                    // The dialog itself is keyboard-driven by AnomalySettingsDialogState.
                    else if ((keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter || keyCode == KeyCode.Space) &&
                             currentMode == NavigationMode.AnomalySettings)
                    {
                        handled = HandleEnterOnAnomalySettings(__instance);
                    }
                    // Arrow key navigation
                    else if (keyCode == KeyCode.UpArrow)
                    {
                        handled = HandleUpArrow(__instance);
                    }
                    else if (keyCode == KeyCode.DownArrow)
                    {
                        handled = HandleDownArrow(__instance);
                    }
                    // Home/End navigation
                    else if (keyCode == KeyCode.Home)
                    {
                        handled = HandleHome(__instance);
                    }
                    else if (keyCode == KeyCode.End)
                    {
                        handled = HandleEnd(__instance);
                    }
                    // Escape - clear typeahead search
                    else if (keyCode == KeyCode.Escape)
                    {
                        handled = HandleEscape();
                    }
                    // Backspace - delete last search character
                    else if (keyCode == KeyCode.Backspace)
                    {
                        handled = HandleBackspace(__instance);
                    }
                    // Typeahead search (letters/digits)
                    else if (Event.current.character != '\0' &&
                             !Event.current.control && !KeyboardHelper.IsAltHeld &&
                             char.IsLetterOrDigit(Event.current.character))
                    {
                        handled = HandleTypeahead(Event.current.character, __instance);
                    }

                    if (handled)
                    {
                        Event.current.Use();
                        patchActive = true;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in StorytellerSelectionPatch Prefix: {ex}");
            }
        }

        private static bool HandleCustomDifficultyInput(KeyCode keyCode)
        {
            // Navigation
            if (keyCode == KeyCode.UpArrow)
            {
                if (CustomDifficultyEditState.HasActiveSearch)
                    CustomDifficultyEditState.SelectPreviousMatch();
                else
                    CustomDifficultyEditState.SelectPrevious();
                return true;
            }
            else if (keyCode == KeyCode.DownArrow)
            {
                if (CustomDifficultyEditState.HasActiveSearch)
                    CustomDifficultyEditState.SelectNextMatch();
                else
                    CustomDifficultyEditState.SelectNext();
                return true;
            }
            else if (keyCode == KeyCode.LeftArrow)
            {
                CustomDifficultyEditState.AdjustLeft();
                return true;
            }
            else if (keyCode == KeyCode.RightArrow)
            {
                CustomDifficultyEditState.AdjustRight();
                return true;
            }
            else if (keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter || keyCode == KeyCode.Space)
            {
                CustomDifficultyEditState.ExecuteOrEnter();
                return true;
            }
            else if (keyCode == KeyCode.Escape)
            {
                // Clear search first, then go back
                if (CustomDifficultyEditState.HasActiveSearch)
                {
                    CustomDifficultyEditState.ClearSearch();
                }
                else if (!CustomDifficultyEditState.GoBack())
                {
                    CustomDifficultyEditState.Close();
                    TolkHelper.Speak("Difficulty".Translate());
                    StorytellerNavigationState.AnnounceDifficulty();
                }
                return true;
            }
            else if (keyCode == KeyCode.Home)
            {
                CustomDifficultyEditState.NavigateHome();
                return true;
            }
            else if (keyCode == KeyCode.End)
            {
                CustomDifficultyEditState.NavigateEnd();
                return true;
            }
            // Alt+R - Jump to reset/playstyle section
            else if (keyCode == KeyCode.R && KeyboardHelper.IsAltHeld)
            {
                CustomDifficultyEditState.JumpToResetSection();
                return true;
            }
            else if (keyCode == KeyCode.Backspace)
            {
                return CustomDifficultyEditState.HandleBackspace();
            }
            // Typeahead (letters/digits)
            else if (Event.current.character != '\0' &&
                     !Event.current.control && !KeyboardHelper.IsAltHeld &&
                     char.IsLetterOrDigit(Event.current.character))
            {
                return CustomDifficultyEditState.HandleTypeahead(Event.current.character);
            }

            return false;
        }

        private static bool HandleEnterOnDifficulty(Page_SelectStoryteller instance)
        {
            DifficultyDef selected = StorytellerNavigationState.SelectedDifficulty;
            if (selected != null && selected.isCustom)
            {
                // Open custom difficulty edit mode
                CustomDifficultyEditState.Open(instance);
                return true;
            }
            // For non-custom difficulties, don't consume Enter - let page proceed
            return false;
        }

        private static void CycleNavigationModeForward(Page_SelectStoryteller instance)
        {
            switch (currentMode)
            {
                case NavigationMode.Storyteller:
                    currentMode = NavigationMode.Difficulty;
                    AnnounceDifficultyMode(instance);
                    break;
                case NavigationMode.Difficulty:
                    currentMode = NavigationMode.Permadeath;
                    AnnouncePermadeathMode();
                    break;
                case NavigationMode.Permadeath:
                    if (AnomalySettingsAvailable)
                    {
                        currentMode = NavigationMode.AnomalySettings;
                        AnnounceAnomalySettingsMode();
                    }
                    else
                    {
                        currentMode = NavigationMode.Storyteller;
                        AnnounceStorytellerMode(instance);
                    }
                    break;
                case NavigationMode.AnomalySettings:
                    currentMode = NavigationMode.Storyteller;
                    AnnounceStorytellerMode(instance);
                    break;
            }
        }

        private static void CycleNavigationModeBackward(Page_SelectStoryteller instance)
        {
            switch (currentMode)
            {
                case NavigationMode.Storyteller:
                    if (AnomalySettingsAvailable)
                    {
                        currentMode = NavigationMode.AnomalySettings;
                        AnnounceAnomalySettingsMode();
                    }
                    else
                    {
                        currentMode = NavigationMode.Permadeath;
                        AnnouncePermadeathMode();
                    }
                    break;
                case NavigationMode.Difficulty:
                    currentMode = NavigationMode.Storyteller;
                    AnnounceStorytellerMode(instance);
                    break;
                case NavigationMode.Permadeath:
                    currentMode = NavigationMode.Difficulty;
                    AnnounceDifficultyMode(instance);
                    break;
                case NavigationMode.AnomalySettings:
                    currentMode = NavigationMode.Permadeath;
                    AnnouncePermadeathMode();
                    break;
            }
        }

        private static void AnnounceStorytellerMode(Page_SelectStoryteller instance)
        {
            StorytellerNavigationState.EnsureStorytellerSelected();
            UpdatePageStoryteller(instance);
            TolkHelper.Speak("ChooseAIStoryteller".Translate());
            StorytellerNavigationState.AnnounceStoryteller();
        }

        private static void AnnounceDifficultyMode(Page_SelectStoryteller instance)
        {
            StorytellerNavigationState.EnsureDifficultySelected();
            UpdatePageDifficulty(instance);
            TolkHelper.Speak("Difficulty".Translate());
            StorytellerNavigationState.AnnounceDifficulty();
        }

        private static void AnnouncePermadeathMode()
        {
            StorytellerNavigationState.EnsurePermadeathSelected();
            TolkHelper.Speak("Save Mode");
            StorytellerNavigationState.AnnouncePermadeath();
        }

        private static void AnnounceAnomalySettingsMode()
        {
            // The button is the entire "row" — there's nothing to navigate Up/Down within it,
            // so the announcement tells the user how to activate it.
            string label = "AnomalySettings".Translate();
            TolkHelper.Speak($"{label}. Press Enter to open.");
        }

        private static bool HandleEnterOnAnomalySettings(Page_SelectStoryteller instance)
        {
            // Mirror StorytellerUI.cs:138-145: refuse if no difficulty is chosen yet, otherwise
            // open Dialog_AnomalySettings against the page's difficultyValues. Our PostOpen
            // patch on Window then wires AnomalySettingsDialogState to drive the dialog.
            try
            {
                DifficultyDef chosen = StorytellerNavigationState.SelectedDifficulty;
                if (chosen == null)
                {
                    TolkHelper.Speak("MustChooseDifficulty".Translate().Resolve());
                    return true;
                }
                Difficulty difficultyValues = (Difficulty)AccessTools.Field(typeof(Page_SelectStoryteller), "difficultyValues").GetValue(instance);
                if (difficultyValues == null)
                {
                    Log.Error("[StorytellerSelectionPatch] Could not read difficultyValues from page");
                    return true;
                }
                Find.WindowStack.Add(new Dialog_AnomalySettings(difficultyValues));
            }
            catch (System.Exception ex)
            {
                Log.Error($"[StorytellerSelectionPatch] Failed to open Dialog_AnomalySettings: {ex.Message}");
            }
            return true;
        }

        private static bool HandleUpArrow(Page_SelectStoryteller instance)
        {
            switch (currentMode)
            {
                case NavigationMode.Storyteller:
                    if (StorytellerNavigationState.HasActiveStorytellerSearch)
                        StorytellerNavigationState.SelectPreviousStorytellerMatch();
                    else
                        StorytellerNavigationState.NavigateStorytellerUp();
                    UpdatePageStoryteller(instance);
                    return true;

                case NavigationMode.Difficulty:
                    if (StorytellerNavigationState.HasActiveDifficultySearch)
                        StorytellerNavigationState.SelectPreviousDifficultyMatch();
                    else
                        StorytellerNavigationState.NavigateDifficultyUp();
                    UpdatePageDifficulty(instance);
                    return true;

                case NavigationMode.Permadeath:
                    StorytellerNavigationState.NavigatePermadeathUp();
                    return true;
            }
            return false;
        }

        private static bool HandleDownArrow(Page_SelectStoryteller instance)
        {
            switch (currentMode)
            {
                case NavigationMode.Storyteller:
                    if (StorytellerNavigationState.HasActiveStorytellerSearch)
                        StorytellerNavigationState.SelectNextStorytellerMatch();
                    else
                        StorytellerNavigationState.NavigateStorytellerDown();
                    UpdatePageStoryteller(instance);
                    return true;

                case NavigationMode.Difficulty:
                    if (StorytellerNavigationState.HasActiveDifficultySearch)
                        StorytellerNavigationState.SelectNextDifficultyMatch();
                    else
                        StorytellerNavigationState.NavigateDifficultyDown();
                    UpdatePageDifficulty(instance);
                    return true;

                case NavigationMode.Permadeath:
                    StorytellerNavigationState.NavigatePermadeathDown();
                    return true;
            }
            return false;
        }

        private static bool HandleHome(Page_SelectStoryteller instance)
        {
            switch (currentMode)
            {
                case NavigationMode.Storyteller:
                    StorytellerNavigationState.NavigateStorytellerHome();
                    UpdatePageStoryteller(instance);
                    return true;

                case NavigationMode.Difficulty:
                    StorytellerNavigationState.NavigateDifficultyHome();
                    UpdatePageDifficulty(instance);
                    return true;

                case NavigationMode.Permadeath:
                    StorytellerNavigationState.NavigatePermadeathHome();
                    return true;
            }
            return false;
        }

        private static bool HandleEnd(Page_SelectStoryteller instance)
        {
            switch (currentMode)
            {
                case NavigationMode.Storyteller:
                    StorytellerNavigationState.NavigateStorytellerEnd();
                    UpdatePageStoryteller(instance);
                    return true;

                case NavigationMode.Difficulty:
                    StorytellerNavigationState.NavigateDifficultyEnd();
                    UpdatePageDifficulty(instance);
                    return true;

                case NavigationMode.Permadeath:
                    StorytellerNavigationState.NavigatePermadeathEnd();
                    return true;
            }
            return false;
        }

        private static bool HandleEscape()
        {
            // Only handle if there's an active search to clear
            switch (currentMode)
            {
                case NavigationMode.Storyteller:
                    return StorytellerNavigationState.ClearStorytellerTypeaheadSearch();

                case NavigationMode.Difficulty:
                    return StorytellerNavigationState.ClearDifficultyTypeaheadSearch();

                case NavigationMode.Permadeath:
                    // No typeahead for permadeath
                    return false;
            }
            return false;
        }

        private static bool HandleBackspace(Page_SelectStoryteller instance)
        {
            switch (currentMode)
            {
                case NavigationMode.Storyteller:
                    if (StorytellerNavigationState.HandleStorytellerTypeaheadBackspace())
                    {
                        UpdatePageStoryteller(instance);
                        return true;
                    }
                    return false;

                case NavigationMode.Difficulty:
                    if (StorytellerNavigationState.HandleDifficultyTypeaheadBackspace())
                    {
                        UpdatePageDifficulty(instance);
                        return true;
                    }
                    return false;

                case NavigationMode.Permadeath:
                    // No typeahead for permadeath
                    return false;
            }
            return false;
        }

        private static bool HandleTypeahead(char character, Page_SelectStoryteller instance)
        {
            switch (currentMode)
            {
                case NavigationMode.Storyteller:
                    if (StorytellerNavigationState.HandleStorytellerTypeahead(character))
                    {
                        UpdatePageStoryteller(instance);
                        return true;
                    }
                    return false;

                case NavigationMode.Difficulty:
                    if (StorytellerNavigationState.HandleDifficultyTypeahead(character))
                    {
                        UpdatePageDifficulty(instance);
                        return true;
                    }
                    return false;

                case NavigationMode.Permadeath:
                    // No typeahead for permadeath (only 2 options)
                    return false;
            }
            return false;
        }

        private static void UpdatePageStoryteller(Page_SelectStoryteller instance)
        {
            StorytellerDef selected = StorytellerNavigationState.SelectedStoryteller;
            if (selected != null)
            {
                AccessTools.Field(typeof(Page_SelectStoryteller), "storyteller").SetValue(instance, selected);
            }
        }

        private static void UpdatePageDifficulty(Page_SelectStoryteller instance)
        {
            DifficultyDef selected = StorytellerNavigationState.SelectedDifficulty;
            if (selected != null)
            {
                AccessTools.Field(typeof(Page_SelectStoryteller), "difficulty").SetValue(instance, selected);

                // Also update difficultyValues if not custom
                if (!selected.isCustom)
                {
                    Difficulty difficultyValues = (Difficulty)AccessTools.Field(typeof(Page_SelectStoryteller), "difficultyValues").GetValue(instance);
                    difficultyValues.CopyFrom(selected);
                }
            }
        }

        // Postfix: Draw visual highlight (simplified - we'll just indicate active mode)
        static void Postfix(Page_SelectStoryteller __instance, Rect rect)
        {
            try
            {
                if (!patchActive) return;

                // Draw a simple indicator of current navigation mode at the top
                Rect modeIndicatorRect = new Rect(rect.x + 10f, rect.y + 10f, 300f, 30f);
                string modeText = "";

                switch (currentMode)
                {
                    case NavigationMode.Storyteller:
                        modeText = $"[{"ChooseAIStoryteller".Translate()}]";
                        break;
                    case NavigationMode.Difficulty:
                        modeText = $"[{"Difficulty".Translate()}]";
                        break;
                    case NavigationMode.Permadeath:
                        modeText = "[Save Mode]";
                        break;
                }

                // Draw semi-transparent background
                Widgets.DrawBoxSolid(modeIndicatorRect, new Color(0.2f, 0.2f, 0.2f, 0.8f));

                // Draw text
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(modeIndicatorRect, modeText);
                Text.Anchor = TextAnchor.UpperLeft;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in StorytellerSelectionPatch Postfix: {ex}");
            }
        }

        public static void ResetAnnouncement()
        {
            hasAnnouncedTitle = false;
            currentMode = NavigationMode.Storyteller;
        }

        /// <summary>
        /// Resets the in-page tab cursor back to the Storyteller row and announces it.
        /// Called by AnomalySettingsDialogPatch on Accept-close so the user lands on a
        /// row where pressing Enter advances the wizard naturally (rather than re-opening
        /// the Anomaly Settings dialog they just confirmed).
        /// </summary>
        public static void ReturnToStorytellerMode()
        {
            currentMode = NavigationMode.Storyteller;
            var page = Find.WindowStack?.Windows.OfType<Page_SelectStoryteller>().FirstOrDefault();
            if (page != null)
            {
                AnnounceStorytellerMode(page);
            }
        }
    }

    // Separate patch to reset state when page opens
    [HarmonyPatch(typeof(Page_SelectStoryteller), "PreOpen")]
    public class StorytellerSelectionPatch_PreOpen
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            StorytellerSelectionPatch.ResetAnnouncement();
            StorytellerNavigationState.Reset();
            CustomDifficultyEditState.Close();
        }
    }

    // Reset custom difficulty state when page closes
    // Note: We patch Window.PreClose because Page_SelectStoryteller doesn't override it
    [HarmonyPatch(typeof(Window), "PreClose")]
    public class StorytellerSelectionPatch_PreClose
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Page_SelectStoryteller)
            {
                CustomDifficultyEditState.Close();
            }
        }
    }

    // Block page advancement when editing custom difficulty settings
    [HarmonyPatch(typeof(Page), "DoNext")]
    public class PageDoNextBlockPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Page __instance)
        {
            // Only intercept for Page_SelectStoryteller
            if (__instance is Page_SelectStoryteller)
            {
                if (CustomDifficultyEditState.IsActive)
                {
                    // Block advancement - user is editing custom settings
                    return false;
                }
            }
            return true;
        }
    }

    // ==== Patches for IN-GAME storyteller selection ====

    /// <summary>
    /// Opens keyboard navigation when the in-game storyteller page opens.
    /// </summary>
    [HarmonyPatch(typeof(Page_SelectStorytellerInGame), "PreOpen")]
    public static class StorytellerInGamePatch_PreOpen
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            StorytellerSelectionState.Open();
        }
    }

    /// <summary>
    /// Closes keyboard navigation when the in-game storyteller page closes.
    /// </summary>
    [HarmonyPatch(typeof(Page_SelectStorytellerInGame), "PreClose")]
    public static class StorytellerInGamePatch_PreClose
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            StorytellerSelectionState.Close();
        }
    }
}
