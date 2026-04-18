using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(Page_ChooseIdeoPreset), "DoWindowContents")]
    public static class IdeologySelectionPatch
    {
        private static bool hasAnnouncedTitle = false;

        // Cached reflection fields (initialized once)
        private static Type presetSelectionEnumType;
        private static FieldInfo presetSelectionField;
        private static FieldInfo selectedIdeoField;
        private static FieldInfo selectedStructureField;

        private static void EnsureReflectionCached()
        {
            if (presetSelectionEnumType != null)
                return;

            presetSelectionEnumType = AccessTools.Inner(typeof(Page_ChooseIdeoPreset), "PresetSelection");
            presetSelectionField = AccessTools.Field(typeof(Page_ChooseIdeoPreset), "presetSelection");
            selectedIdeoField = AccessTools.Field(typeof(Page_ChooseIdeoPreset), "selectedIdeo");
            selectedStructureField = AccessTools.Field(typeof(Page_ChooseIdeoPreset), "selectedStructure");
        }

        static bool Prefix(Page_ChooseIdeoPreset __instance, Rect inRect)
        {
            try
            {
                // Skip entire DoWindowContents while float menu is open.
                // This prevents DoBottomButtons from processing Enter/Escape
                // in the Page's IMGUI context (separate from UnifiedKeyboardPatch's context).
                if (WindowlessFloatMenuState.IsActive)
                    return false;

                EnsureReflectionCached();

                IdeologyNavigationState.Initialize();

                // First-time announcement
                if (!hasAnnouncedTitle)
                {
                    hasAnnouncedTitle = true;
                    TolkHelper.Speak(IdeologyNavigationState.BuildOpeningAnnouncement());
                }

                // Handle keyboard input
                if (Event.current.type == EventType.KeyDown)
                {
                    KeyCode key = Event.current.keyCode;
                    bool shift = Event.current.shift;
                    bool ctrl = Event.current.control;
                    bool alt = KeyboardHelper.IsAltHeld;

                    bool handled = false;

                    // === Shared keys (both tabs) ===

                    // Tab / Shift+Tab — switch tabs
                    if (key == KeyCode.Tab)
                    {
                        IdeologyNavigationState.SwitchTab();
                        handled = true;
                    }
                    // Space — re-announce
                    else if (key == KeyCode.Space && !alt && !ctrl)
                    {
                        if (IdeologyNavigationState.CurrentTab == 0)
                            IdeologyNavigationState.AnnounceCurrentOption();
                        else
                            IdeologyNavigationState.AnnounceCurrentTreeItem();
                        handled = true;
                    }
                    // Alt+S — structure menu
                    else if (key == KeyCode.S && alt && !ctrl)
                    {
                        IdeologyNavigationState.OpenStructureMenu(__instance);
                        handled = true;
                    }
                    // Alt+Y — style menu
                    else if (key == KeyCode.Y && alt && !ctrl)
                    {
                        IdeologyNavigationState.OpenStyleMenu(__instance);
                        handled = true;
                    }
                    // Delegate to current tab
                    else if (IdeologyNavigationState.CurrentTab == 0)
                    {
                        handled = HandleOptionsInput(key, shift, ctrl, alt);
                    }
                    else
                    {
                        handled = HandlePresetsInput(key, shift, ctrl, alt);
                    }

                    // Always sync selection state so the page reflects our navigation,
                    // especially before Enter passes through to trigger DoNext
                    SyncPageSelection(__instance);

                    if (handled)
                    {
                        Event.current.Use();
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in IdeologySelectionPatch Prefix: {ex}");
            }
            return true; // Run original DoWindowContents
        }

        private static bool HandleOptionsInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (key == KeyCode.UpArrow)
            {
                IdeologyNavigationState.NavigateOptionUp();
                return true;
            }
            if (key == KeyCode.DownArrow)
            {
                IdeologyNavigationState.NavigateOptionDown();
                return true;
            }
            if (key == KeyCode.Home)
            {
                IdeologyNavigationState.NavigateOptionHome();
                return true;
            }
            if (key == KeyCode.End)
            {
                IdeologyNavigationState.NavigateOptionEnd();
                return true;
            }
            if (key == KeyCode.Escape)
            {
                if (IdeologyNavigationState.HasOptionsSearch)
                {
                    IdeologyNavigationState.ClearOptionsSearch();
                    return true;
                }
                // Let game handle Escape (go back)
                return false;
            }
            if (key == KeyCode.Backspace)
            {
                return IdeologyNavigationState.HandleOptionBackspace();
            }

            // Enter — do NOT consume, let game's Accept keybinding handle DoNext.
            // Exception: if the current option is disabled (e.g. CustomFluid/CustomFixed
            // which lack accessibility support), consume Enter and announce the reason
            // so the user isn't dropped into an inaccessible page.
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                if (IdeologyNavigationState.IsCurrentOptionDisabled)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    string reason = IdeologyNavigationState.CurrentOptionDisabledReason;
                    TolkHelper.Speak(string.IsNullOrEmpty(reason) ? "Unavailable" : reason);
                    return true;
                }
                return false;
            }

            // Typeahead
            if (!alt && !ctrl)
            {
                char c = Event.current.character;
                if (c != '\0' && char.IsLetterOrDigit(c))
                {
                    return IdeologyNavigationState.HandleOptionTypeahead(c);
                }
            }

            return false;
        }

        private static bool HandlePresetsInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            // Escape — clear search or let game handle
            if (key == KeyCode.Escape)
            {
                if (IdeologyNavigationState.HasPresetsSearch)
                {
                    IdeologyNavigationState.ClearPresetsSearch();
                    return true;
                }
                return false;
            }

            // Delegate all other input to TreeNavigationHelper via state
            return IdeologyNavigationState.HandlePresetsInput(Event.current);
        }

        private static void SyncPageSelection(Page_ChooseIdeoPreset instance)
        {
            if (presetSelectionEnumType == null || presetSelectionField == null || selectedIdeoField == null)
                return;

            try
            {
                if (IdeologyNavigationState.CurrentTab == 0) // Options tab
                {
                    int enumValue = IdeologyNavigationState.CurrentOptionEnumValue;
                    presetSelectionField.SetValue(instance, Enum.ToObject(presetSelectionEnumType, enumValue));
                    selectedIdeoField.SetValue(instance, null);
                }
                else // Presets tab
                {
                    IdeoPresetDef preset = IdeologyNavigationState.GetSelectedPresetDef();
                    if (preset != null)
                    {
                        // Preset (enum value 4)
                        presetSelectionField.SetValue(instance, Enum.ToObject(presetSelectionEnumType, 4));
                        selectedIdeoField.SetValue(instance, preset);
                    }
                    else
                    {
                        // On a category node — safe default to Classic
                        presetSelectionField.SetValue(instance, Enum.ToObject(presetSelectionEnumType, 0));
                        selectedIdeoField.SetValue(instance, null);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error syncing page selection: {ex}");
            }
        }

        public static void ResetAnnouncement()
        {
            hasAnnouncedTitle = false;
        }
    }

    [HarmonyPatch(typeof(Page_ChooseIdeoPreset), "PostOpen")]
    public static class IdeologySelectionPatch_PostOpen
    {
        [HarmonyPostfix]
        static void Postfix(Page_ChooseIdeoPreset __instance)
        {
            IdeologySelectionPatch.ResetAnnouncement();
            IdeologyNavigationState.Reset();

            // Restore IMGUI focus to this page. After closing certain dialogs,
            // IMGUI focus may be lost, preventing DoWindowContents from receiving KeyDown events.
            Find.WindowStack.Notify_ManuallySetFocus(__instance);
        }
    }

    // Safety net: block DoNext for CustomFluid (1), CustomFixed (2), and Load (3)
    // since the pages/dialogs they lead to don't yet have accessibility support.
    // This catches mouse clicks and any Enter paths that bypass UnifiedKeyboardPatch.
    [HarmonyPatch(typeof(Page_ChooseIdeoPreset), "DoNext")]
    public static class IdeologySelectionPatch_DoNextBlock
    {
        [HarmonyPrefix]
        public static bool Prefix(Page_ChooseIdeoPreset __instance)
        {
            try
            {
                var presetSelectionField = AccessTools.Field(typeof(Page_ChooseIdeoPreset), "presetSelection");
                int value = Convert.ToInt32(presetSelectionField.GetValue(__instance));
                // 1 = CustomFluid, 2 = CustomFixed, 3 = Load
                if (value == 1 || value == 2 || value == 3)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("Accessibility coming soon");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in IdeologySelectionPatch_DoNextBlock: {ex}");
            }
            return true;
        }
    }
}
