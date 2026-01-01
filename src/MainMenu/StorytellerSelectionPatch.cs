using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(Page_SelectStoryteller))]
    [HarmonyPatch("DoWindowContents")]
    public class StorytellerSelectionPatch
    {
        private static bool patchActive = false;
        private static bool hasAnnouncedTitle = false;
        private enum NavigationMode { Storyteller, Difficulty, Permadeath }
        private static NavigationMode currentMode = NavigationMode.Storyteller;

        // Prefix: Initialize state and handle keyboard input
        static void Prefix(Page_SelectStoryteller __instance, Rect rect)
        {
            try
            {
                // Initialize navigation state
                StorytellerNavigationState.Initialize();

                // Announce window title and initial selection once
                if (!hasAnnouncedTitle)
                {
                    string pageTitle = "Choose AI Storyteller";
                    StorytellerDef storyteller = StorytellerNavigationState.SelectedStoryteller;
                    if (storyteller != null)
                    {
                        TolkHelper.Speak($"{pageTitle} - {storyteller.label} - {storyteller.description}. Use Tab to switch between Storyteller, Difficulty, and Permadeath modes.");
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

                    if (keyCode == KeyCode.Tab)
                    {
                        // Cycle through navigation modes
                        CycleNavigationMode();
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.UpArrow)
                    {
                        HandleUpArrow(__instance);
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.DownArrow)
                    {
                        HandleDownArrow(__instance);
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter)
                    {
                        if (currentMode == NavigationMode.Permadeath)
                        {
                            StorytellerNavigationState.TogglePermadeath();
                            Event.current.Use();
                            patchActive = true;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in StorytellerSelectionPatch Prefix: {ex}");
            }
        }

        private static void CycleNavigationMode()
        {
            switch (currentMode)
            {
                case NavigationMode.Storyteller:
                    currentMode = NavigationMode.Difficulty;
                    TolkHelper.Speak("Navigation: Difficulty selection (Use Up/Down arrows)");
                    break;
                case NavigationMode.Difficulty:
                    currentMode = NavigationMode.Permadeath;
                    TolkHelper.Speak("Navigation: Permadeath/Reload mode (Press Enter to toggle)");
                    break;
                case NavigationMode.Permadeath:
                    currentMode = NavigationMode.Storyteller;
                    TolkHelper.Speak("Navigation: Storyteller selection (Use Up/Down arrows)");
                    break;
            }
        }

        private static void HandleUpArrow(Page_SelectStoryteller instance)
        {
            switch (currentMode)
            {
                case NavigationMode.Storyteller:
                    StorytellerNavigationState.NavigateStorytellerUp();
                    UpdatePageStoryteller(instance);
                    break;
                case NavigationMode.Difficulty:
                    StorytellerNavigationState.NavigateDifficultyUp();
                    UpdatePageDifficulty(instance);
                    break;
                case NavigationMode.Permadeath:
                    // No up/down for permadeath, just toggle with Enter
                    break;
            }
        }

        private static void HandleDownArrow(Page_SelectStoryteller instance)
        {
            switch (currentMode)
            {
                case NavigationMode.Storyteller:
                    StorytellerNavigationState.NavigateStorytellerDown();
                    UpdatePageStoryteller(instance);
                    break;
                case NavigationMode.Difficulty:
                    StorytellerNavigationState.NavigateDifficultyDown();
                    UpdatePageDifficulty(instance);
                    break;
                case NavigationMode.Permadeath:
                    // No up/down for permadeath, just toggle with Enter
                    break;
            }
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
                        modeText = "[Selecting: Storyteller]";
                        break;
                    case NavigationMode.Difficulty:
                        modeText = "[Selecting: Difficulty]";
                        break;
                    case NavigationMode.Permadeath:
                        modeText = "[Selecting: Permadeath Mode]";
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
