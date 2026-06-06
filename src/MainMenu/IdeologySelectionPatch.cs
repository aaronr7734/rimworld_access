using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(Page_ChooseIdeoPreset), "DoWindowContents")]
    public static class IdeologySelectionPatch
    {
        private static bool hasAnnouncedTitle = false;
        // Reclaim page focus after either an info card OR the saved-ideoligion load picker closes
        // over this page. Without the load-picker case, closing Dialog_IdeoList_Load (especially via
        // vanilla's Escape on an empty list) leaves the page dead to keyboard input — a full lockup.
        private static readonly HostFocusReturn hostFocus =
            new HostFocusReturn(typeof(Dialog_InfoCard), typeof(Dialog_IdeoList_Load));

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
                // Reclaim IMGUI focus when a child window (info card or load picker) opened over
                // this page closes (must run in the page's own GUI.Window pass to take effect).
                hostFocus.Track(__instance);

                // Skip entire DoWindowContents while float menu is open.
                // This prevents DoBottomButtons from processing Enter/Escape
                // in the Page's IMGUI context (separate from UnifiedKeyboardPatch's context).
                if (WindowlessFloatMenuState.IsActive)
                    return false;

                // The Load button opens Dialog_IdeoList_Load on top of this page. Both windows
                // share WindowLayer.Dialog, but the page is added first → its DoWindowContents
                // runs BEFORE the dialog's in WindowStackOnGUI's i=0..count-1 iteration. Without
                // this yield the page's KeyDown handlers (arrow keys, typeahead, Backspace)
                // consume every keystroke before the load dialog's own prefix can route it to
                // IdeoLoadState, and the empty-list case strands the user with no interactable
                // controls. Same pattern as IdeoBuilderHubPatch.IsSubEditorOpen.
                if (Find.WindowStack?.WindowOfType<Dialog_IdeoList_Load>() != null)
                    return true;

                EnsureReflectionCached();

                IdeologyNavigationState.Initialize();

                // First-time announcement
                if (!hasAnnouncedTitle)
                {
                    hasAnnouncedTitle = true;
                    TolkHelper.SpeakData(IdeologyNavigationState.BuildOpeningAnnouncement());
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

            // Enter — do NOT consume, let game's Accept keybinding handle DoNext (which
            // routes to the now-accessible Classic / Custom / Load / Preset flows).
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
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

    // All ideoligion-creation paths (Classic, Custom Fixed, Custom Fluid, Load, Presets)
    // are now accessible via the IdeoBuilder, so the former "Accessibility coming soon"
    // DoNext safety-net block has been removed.
}
