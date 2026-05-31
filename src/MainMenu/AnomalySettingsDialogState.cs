using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard navigation for Dialog_AnomalySettings — the popup opened from the
    /// "AnomalySettings..." button on Page_SelectStoryteller during character creation.
    ///
    /// Mirrors vanilla's actual UI structure: a single scrolling list. Top row is the
    /// playstyle selector (Left/Right cycles options); below are the conditional
    /// anomaly sliders (override / inactive / active / study, only the relevant ones
    /// shown). Up/Down navigates rows; Enter toggles. Edits are stored in local copies
    /// and only committed to the underlying Difficulty on Accept (Alt+S).
    /// </summary>
    public static class AnomalySettingsDialogState
    {
        private static readonly FieldInfo DifficultyField =
            AccessTools.Field(typeof(Dialog_AnomalySettings), "difficulty");
        private static readonly FieldInfo InactiveField =
            AccessTools.Field(typeof(Dialog_AnomalySettings), "anomalyThreatsInactiveFraction");
        private static readonly FieldInfo ActiveField =
            AccessTools.Field(typeof(Dialog_AnomalySettings), "anomalyThreatsActiveFraction");
        private static readonly FieldInfo StudyField =
            AccessTools.Field(typeof(Dialog_AnomalySettings), "studyEfficiencyFactor");
        private static readonly FieldInfo OverrideField =
            AccessTools.Field(typeof(Dialog_AnomalySettings), "overrideAnomalyThreatsFraction");
        private static readonly FieldInfo PlaystyleField =
            AccessTools.Field(typeof(Dialog_AnomalySettings), "anomalyPlaystyleDef");

        private static bool isActive;
        private static Dialog_AnomalySettings currentDialog;

        /// <summary>
        /// Frame number when we last handled Escape to close the dialog. Mirrors the
        /// FactionLandingState pattern — used by AnomalySettingsDialogPatch's Page prefix to
        /// block Page_SelectStoryteller from also receiving the Cancel keystroke this frame
        /// (Event.current.Use() does not stop HandleEventsHighPriority from firing).
        /// </summary>
        internal static int escapeHandledOnFrame = -1;

        /// <summary>
        /// True when the close was triggered by Accept (Alt+S). Read by the PostClose patch
        /// to pick the right announcement ("saved" vs "closed") and to decide whether to
        /// reset the parent page's tab cursor to the Storyteller row (so Enter advances
        /// naturally to the next page).
        /// </summary>
        internal static bool wasAcceptClose;

        // Single flat list of items, just like the Anomaly section in custom difficulty:
        //   index 0          → AnomalyPlaystyleSetting (Left/Right cycles playstyles)
        //   index 1..n-1     → conditional sliders (only the visible ones for the current playstyle)
        // Rebuilt whenever the playstyle changes.
        private static List<DifficultySetting> items = new List<DifficultySetting>();
        private static int selectedIndex;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // Local copies of the dialog's private fields. We mutate these freely; they only get
        // committed back to the underlying Difficulty when the user activates Accept (Alt+S).
        private static AnomalyPlaystyleDef localPlaystyle;
        private static float localInactive;
        private static float localActive;
        private static float localStudy;
        private static float localOverride;
        private static Difficulty localDifficulty;

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        public static void Open(Dialog_AnomalySettings dialog)
        {
            if (dialog == null) return;
            try
            {
                currentDialog = dialog;

                // Critical setup — mirrors FactionLandingState pattern. Without these, Unity
                // IMGUI focus stalls keyboard events when this absorbInputAroundWindow modal opens.
                //   - closeOnAccept/closeOnCancel: stop the WindowStack from auto-closing on
                //     Enter/Escape; we drive close ourselves via TryRemove.
                //   - focusWhenOpened: stop Unity from grabbing keyboard focus into the GUI.Window,
                //     which would block UnifiedKeyboardPatch from ever seeing our keys.
                dialog.closeOnAccept = false;
                dialog.closeOnCancel = false;
                dialog.focusWhenOpened = false;

                localDifficulty = DifficultyField?.GetValue(dialog) as Difficulty;
                localInactive = (float)(InactiveField?.GetValue(dialog) ?? 0f);
                localActive = (float)(ActiveField?.GetValue(dialog) ?? 0f);
                localStudy = (float)(StudyField?.GetValue(dialog) ?? 1f);
                localOverride = (float)(OverrideField?.GetValue(dialog) ?? 0.15f);
                localPlaystyle = PlaystyleField?.GetValue(dialog) as AnomalyPlaystyleDef;

                selectedIndex = 0;
                typeahead.ClearSearch();
                RebuildItems();
                wasAcceptClose = false;
                isActive = true;

                TolkHelper.Speak("AnomalySettings".Translate().Resolve());
                AnnounceCurrent();
            }
            catch (Exception ex)
            {
                Log.Error($"[AnomalySettingsDialogState] Open failed: {ex.Message}");
                Close();
            }
        }

        public static void Close()
        {
            isActive = false;
            currentDialog = null;
            localDifficulty = null;
            localPlaystyle = null;
            items.Clear();
            typeahead.ClearSearch();
        }

        // ===== KEYBOARD HANDLER =====

        public static bool HandleInput(Event evt)
        {
            if (!isActive || currentDialog == null) return false;
            if (evt.type != EventType.KeyDown) return false;

            var key = evt.keyCode;
            bool alt = KeyboardHelper.IsAltHeld;
            bool shift = evt.shift;
            bool ctrl = evt.control;

            // Alt+S = Accept (commit + close).
            if (alt && key == KeyCode.S)
            {
                Accept();
                return true;
            }

            // Alt+R = Set to Standard Playstyle preset menu.
            if (alt && key == KeyCode.R)
            {
                OpenStandardPlaystyleMenu();
                return true;
            }

            switch (key)
            {
                case KeyCode.Escape:
                    if (typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearchAndAnnounce();
                        AnnounceCurrent();
                        return true;
                    }
                    CloseDialog();
                    return true;

                case KeyCode.UpArrow:
                    NavigatePrevious();
                    return true;

                case KeyCode.DownArrow:
                    NavigateNext();
                    return true;

                case KeyCode.Home:
                    NavigateHome();
                    return true;

                case KeyCode.End:
                    NavigateEnd();
                    return true;

                case KeyCode.LeftArrow:
                    AdjustCurrent(-1, shift);
                    return true;

                case KeyCode.RightArrow:
                    AdjustCurrent(1, shift);
                    return true;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Space:
                    ToggleCurrent();
                    return true;

                case KeyCode.Backspace:
                    if (typeahead.HasActiveSearch)
                    {
                        var labels = items.Select(s => s.Label).ToList();
                        if (typeahead.ProcessBackspace(labels, out int newIndex) && newIndex >= 0)
                        {
                            selectedIndex = newIndex;
                            AnnounceCurrent();
                        }
                    }
                    return true;

                default:
                    // Modal: consume all unhandled keys; typeahead routed via TypeaheadDispatcher.
                    return true;
            }
        }

        /// <summary>
        /// Layout-aware typeahead character entry; called by <see cref="TypeaheadDispatcher"/>.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive) return;

            var labels = items.Select(s => s.Label).ToList();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIdx) && newIdx >= 0)
            {
                selectedIndex = newIdx;
                AnnounceCurrent();
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        // ===== NAVIGATION =====

        private static void NavigateNext()
        {
            if (items.Count == 0) return;
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                selectedIndex = typeahead.GetNextMatch(selectedIndex);
            else
            {
                typeahead.ClearSearch();
                selectedIndex = MenuHelper.SelectNext(selectedIndex, items.Count);
            }
            AnnounceCurrent();
        }

        private static void NavigatePrevious()
        {
            if (items.Count == 0) return;
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                selectedIndex = typeahead.GetPreviousMatch(selectedIndex);
            else
            {
                typeahead.ClearSearch();
                selectedIndex = MenuHelper.SelectPrevious(selectedIndex, items.Count);
            }
            AnnounceCurrent();
        }

        private static void NavigateHome()
        {
            if (items.Count == 0) return;
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                selectedIndex = typeahead.GetFirstMatch();
            else
            {
                typeahead.ClearSearch();
                selectedIndex = 0;
            }
            AnnounceCurrent();
        }

        private static void NavigateEnd()
        {
            if (items.Count == 0) return;
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                selectedIndex = typeahead.GetLastMatch();
            else
            {
                typeahead.ClearSearch();
                selectedIndex = items.Count - 1;
            }
            AnnounceCurrent();
        }

        private static void AdjustCurrent(int direction, bool shift)
        {
            if (items.Count == 0 || selectedIndex < 0 || selectedIndex >= items.Count) return;
            var setting = items[selectedIndex];
            if (shift && setting is DifficultySliderSetting slider)
            {
                slider.AdjustByPercentOfPositions(0.1f * direction);
            }
            else
            {
                setting.Adjust(direction);
            }
            // The playstyle row's onChanged rebuilds items in place; the playstyle row is
            // always at index 0, so selectedIndex stays valid. Re-fetch from items in case
            // the rebuild swapped instances.
            if (selectedIndex < items.Count)
                TolkHelper.Speak(items[selectedIndex].GetAdjustmentAnnouncement());
            else
                TolkHelper.Speak(setting.GetAdjustmentAnnouncement());
        }

        private static void ToggleCurrent()
        {
            if (items.Count == 0 || selectedIndex < 0 || selectedIndex >= items.Count) return;
            items[selectedIndex].Toggle();
            if (selectedIndex < items.Count)
                TolkHelper.Speak(items[selectedIndex].GetAdjustmentAnnouncement());
        }

        // ===== ITEM LIST =====

        private static void RebuildItems()
        {
            items.Clear();
            if (localPlaystyle == null) return;

            // Playstyle row at the top (Left/Right cycles).
            items.Add(new AnomalyPlaystyleSetting(
                getter: () => localPlaystyle,
                setter: v => localPlaystyle = v,
                onTransitionToOverride: () =>
                {
                    // Mirror vanilla DrawPlaystyles: when entering an override-style playstyle,
                    // seed the override fraction so the slider has a sensible starting value.
                    localOverride = 0.15f;
                },
                onChanged: RebuildItems));

            // Conditional sliders. useEnabledConditions=false → only return the sliders relevant
            // to the current playstyle (rather than always-show + per-row enable conditions).
            items.AddRange(DifficultySettingsHelper.BuildAnomalySliders(
                playstyleGetter: () => localPlaystyle,
                overrideGetter: () => localOverride, overrideSetter: v => localOverride = v,
                inactiveGetter: () => localInactive, inactiveSetter: v => localInactive = v,
                activeGetter: () => localActive, activeSetter: v => localActive = v,
                studyGetter: () => localStudy, studySetter: v => localStudy = v,
                useEnabledConditions: false));
        }

        // ===== ANNOUNCEMENTS =====

        private static void AnnounceCurrent()
        {
            if (items.Count == 0)
            {
                TolkHelper.Speak("None".Translate().Resolve());
                return;
            }
            if (selectedIndex < 0 || selectedIndex >= items.Count) return;
            var setting = items[selectedIndex];
            string position = MenuHelper.FormatPosition(selectedIndex, items.Count);
            string suffix = string.IsNullOrEmpty(position) ? "" : $" ({position})";

            // For the playstyle row, append the scenario-block hint if applicable.
            if (setting is AnomalyPlaystyleSetting && localPlaystyle != null
                && Find.Scenario != null && Find.Scenario.standardAnomalyPlaystyleOnly
                && localPlaystyle != AnomalyPlaystyleDefOf.Standard)
            {
                TolkHelper.Speak($"{setting.GetAnnouncement()}. {"DisabledByScenario".Translate()}: {Find.Scenario.name}{suffix}");
                return;
            }

            TolkHelper.Speak($"{setting.GetAnnouncement()}{suffix}");
        }

        // ===== ACCEPT / RESET =====

        private static void Accept()
        {
            if (Find.Scenario != null && Find.Scenario.standardAnomalyPlaystyleOnly
                && localPlaystyle != null && localPlaystyle != AnomalyPlaystyleDefOf.Standard)
            {
                TolkHelper.Speak($"{"DisabledByScenario".Translate()}: {Find.Scenario.name}");
                return;
            }

            try
            {
                if (localDifficulty == null)
                {
                    TolkHelper.Speak("Cannot accept: difficulty not loaded.");
                    return;
                }

                if (localPlaystyle != null && localPlaystyle.overrideThreatFraction)
                    localDifficulty.overrideAnomalyThreatsFraction = localOverride;
                else
                    localDifficulty.overrideAnomalyThreatsFraction = null;

                localDifficulty.anomalyThreatsInactiveFraction = localInactive;
                localDifficulty.anomalyThreatsActiveFraction = localActive;
                localDifficulty.studyEfficiencyFactor = localStudy;
                if (localPlaystyle != null) localDifficulty.AnomalyPlaystyleDef = localPlaystyle;

                // Mark as Accept-close so PostClose announces "saved" (not "closed") and
                // bumps the parent page's tab cursor back to Storyteller.
                wasAcceptClose = true;
                CloseDialog();
            }
            catch (Exception ex)
            {
                Log.Error($"[AnomalySettingsDialogState] Accept failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Closes the dialog via WindowStack.TryRemove (mirrors FactionLandingState pattern).
        /// Sets escapeHandledOnFrame so AnomalySettingsDialogPatch can block the underlying
        /// Page_SelectStoryteller from also processing the same Cancel key this frame.
        /// </summary>
        private static void CloseDialog()
        {
            escapeHandledOnFrame = Time.frameCount;
            if (currentDialog != null)
            {
                Find.WindowStack.TryRemove(currentDialog, doCloseSound: false);
            }
            Close();
        }

        private static void OpenStandardPlaystyleMenu()
        {
            var options = new List<FloatMenuOption>();
            foreach (DifficultyDef d in DefDatabase<DifficultyDef>.AllDefs)
            {
                if (d.isCustom) continue;
                var captured = d;
                options.Add(new FloatMenuOption(captured.LabelCap, () =>
                {
                    localInactive = captured.anomalyThreatsInactiveFraction;
                    localActive = captured.anomalyThreatsActiveFraction;
                    localStudy = captured.studyEfficiencyFactor;
                    localPlaystyle = AnomalyPlaystyleDefOf.Standard;
                    selectedIndex = 0;
                    RebuildItems();
                    TolkHelper.Speak($"{captured.LabelCap}");
                    AnnounceCurrent();
                }));
            }
            if (options.Count == 0) return;
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
            TolkHelper.Speak($"{"SetToStandardPlaystyle".Translate()}");
        }
    }
}
