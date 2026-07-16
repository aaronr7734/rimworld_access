using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Windowless keyboard menu overlaid on a still-live Dialog_ModSettings. Unlike most
    /// windowless menus (which replace a window outright), this one must keep the dialog
    /// open and drawing every frame - that draw is exactly what ModSettingsWidgetPatches
    /// intercepts to build <see cref="items"/> and to apply activations back into the
    /// mod's own widgets. Follows the ModListState pattern (overlay over a live window)
    /// rather than WindowlessDialogState (which removes the window).
    /// </summary>
    public static class ModSettingsMenuState
    {
        private static bool isActive;
        private static Dialog_ModSettings currentDialog;
        private static List<CapturedWidget> items = new List<CapturedWidget>();
        private static int selectedIndex;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // After a slider adjust, the mod redraws the slider's label with the new value a frame
        // or two later. Rather than announce a raw computed number now (which won't match how
        // the mod formats it - "1.3" vs "130%"), we wait for that refreshed label and announce
        // it, so the user hears the value exactly as the mod presents it.
        private static int sliderAnnounceDraw = -1;
        private static string sliderAnnouncePrevLabel;
        private static int sliderAnnounceTtl;

        // After activating a section/tab button, the mod swaps the content below it on the next
        // frame. We remember the pre-activation labels so we can detect that change, jump the
        // selection to the first control that actually changed (i.e. "into" the new section),
        // and announce it - otherwise focus would sit uselessly on the button.
        private static bool enterAfterActivate;
        private static List<string> preActivateLabels;
        private static int enterAfterActivateTtl;

        public static bool IsActive => isActive;

        /// <summary>The Dialog_ModSettings this menu is currently bound to (null when closed).</summary>
        public static Dialog_ModSettings CurrentDialog => currentDialog;

        /// <summary>
        /// Opens the menu on the dialog's first render. <paramref name="capturedItems"/> is
        /// the current frame's live snapshot (ModSettingsCaptureState.CurrentFrameSnapshot),
        /// already fully built at this point since the caller is a Postfix running after the
        /// original DoWindowContents (and therefore every widget in it) has executed.
        /// </summary>
        public static void Open(Dialog_ModSettings dialog, string modName, List<CapturedWidget> capturedItems)
        {
            currentDialog = dialog;
            items = capturedItems ?? new List<CapturedWidget>();
            selectedIndex = 0;
            isActive = true;
            typeahead.ClearSearch();
            AnnounceOpening(modName);
        }

        public static void Close()
        {
            isActive = false;
            currentDialog = null;
            items = new List<CapturedWidget>();
            selectedIndex = 0;
            typeahead.ClearSearch();
            sliderAnnounceDraw = -1;
            sliderAnnouncePrevLabel = null;
            enterAfterActivate = false;
            preActivateLabels = null;
        }

        /// <summary>
        /// Refreshes the shadow item list from the latest frame's capture. Called every
        /// frame from ModSettingsCaptureState.EndFrame() while the dialog is open, whether
        /// or not this menu is active. Preserves selectedIndex; never announces here - only
        /// explicit navigation announces, per the "refresh silently" requirement.
        /// </summary>
        public static void NotifyFrameCaptured(object dialog, List<CapturedWidget> capturedItems)
        {
            if (!isActive) return;
            // Ignore frames belonging to any other Dialog_ModSettings (e.g. a Mod Settings
            // Framework wrapper dialog that draws nothing we capture) so its empty frame can't
            // clobber the menu we bound to.
            if (!ReferenceEquals(dialog, currentDialog)) return;
            items = capturedItems ?? new List<CapturedWidget>();
            if (selectedIndex >= items.Count)
                selectedIndex = System.Math.Max(0, items.Count - 1);

            ProcessDeferredSliderAnnounce();
            ProcessDeferredSectionEnter();
        }

        /// <summary>
        /// Speaks a slider's value once the mod has redrawn its label to reflect a just-applied
        /// adjustment. Waits for the label to actually change (accurate, mod-formatted value) or
        /// gives up after a few frames (e.g. the value was clamped at a limit and didn't move).
        /// </summary>
        private static void ProcessDeferredSliderAnnounce()
        {
            if (sliderAnnounceDraw < 0) return;

            // If the user has already navigated off this slider, drop the pending announce
            // silently rather than speaking a control they've left.
            if (selectedIndex < 0 || selectedIndex >= items.Count ||
                items[selectedIndex].DrawIndex != sliderAnnounceDraw ||
                items[selectedIndex].WidgetKind != CapturedWidget.Kind.Slider)
            {
                sliderAnnounceDraw = -1;
                return;
            }

            var slider = items.FirstOrDefault(w => w.WidgetKind == CapturedWidget.Kind.Slider && w.DrawIndex == sliderAnnounceDraw);
            sliderAnnounceTtl--;

            if (slider != null && slider.Label != sliderAnnouncePrevLabel)
            {
                TolkHelper.SpeakData(BuildItemAnnouncement(slider, includePosition: false));
                sliderAnnounceDraw = -1;
            }
            else if (sliderAnnounceTtl <= 0)
            {
                if (slider != null)
                    TolkHelper.SpeakData(BuildItemAnnouncement(slider, includePosition: false));
                sliderAnnounceDraw = -1;
            }
        }

        /// <summary>
        /// After a section/tab button is activated, jumps to the first control whose label
        /// changed (the newly-shown section content) and announces it, so activating a tab
        /// actually moves the user "into" it instead of leaving focus on the button.
        /// </summary>
        private static void ProcessDeferredSectionEnter()
        {
            if (!enterAfterActivate) return;

            enterAfterActivateTtl--;
            int firstChanged = FirstChangedIndex(preActivateLabels, items);
            if (firstChanged >= 0)
            {
                selectedIndex = firstChanged;
                typeahead.ClearSearch();
                AnnounceCurrent();
                enterAfterActivate = false;
                preActivateLabels = null;
            }
            else if (enterAfterActivateTtl <= 0)
            {
                enterAfterActivate = false;
                preActivateLabels = null;
            }
        }

        /// <summary>Index of the first item whose label differs from <paramref name="before"/>
        /// (or the first item beyond the old list), or -1 if the lists match.</summary>
        private static int FirstChangedIndex(List<string> before, List<CapturedWidget> after)
        {
            if (before == null) return -1;
            for (int i = 0; i < after.Count; i++)
            {
                if (i >= before.Count || before[i] != after[i].Label)
                    return i;
            }
            return -1;
        }

        // ============================================================
        // Input handling
        // ============================================================

        /// <summary>Handles a KeyCode-level key event. Returns true if consumed.</summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive) return false;

            if (key == KeyCode.Home)
            {
                JumpToFirst();
                return true;
            }

            if (key == KeyCode.End)
            {
                JumpToLast();
                return true;
            }

            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrent();
                    return true;
                }
                CloseDialog();
                return true;
            }

            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = GetLabels();
                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0) selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
                return true;
            }

            if (key == KeyCode.UpArrow)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int prev = typeahead.GetPreviousMatch(selectedIndex);
                    if (prev >= 0)
                    {
                        selectedIndex = prev;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectPrevious();
                }
                return true;
            }

            if (key == KeyCode.DownArrow)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int next = typeahead.GetNextMatch(selectedIndex);
                    if (next >= 0)
                    {
                        selectedIndex = next;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectNext();
                }
                return true;
            }

            if (key == KeyCode.LeftArrow)
            {
                AdjustSlider(-1);
                return true;
            }

            if (key == KeyCode.RightArrow)
            {
                AdjustSlider(1);
                return true;
            }

            // Enter and Space both activate the current control (toggle a checkbox, press a
            // button, select a radio) - Space is the conventional screen-reader activation key.
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space)
            {
                Activate();
                return true;
            }

            // Consume * to prevent passthrough (matches convention in other flat menus).
            if (key == KeyCode.KeypadMultiply || (shift && key == KeyCode.Alpha8))
                return true;

            // Typeahead characters are dispatched separately via TypeaheadConsumerRegistry
            // (layout-aware Event.current.character); here we only need to swallow the
            // KeyCode-level keydown so it doesn't reach the mod's own widgets underneath.
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;
            if ((isLetter || isNumber) && !alt && !ctrl)
                return true;

            return false;
        }

        /// <summary>Handles a single typed character for typeahead search. Called from
        /// TypeaheadConsumerRegistry.</summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive || items.Count == 0) return;

            var labels = GetLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                typeahead.SpeakNoMatches();
            }
        }

        // ============================================================
        // Navigation
        // ============================================================

        private static void SelectNext()
        {
            if (items.Count == 0) return;
            selectedIndex = MenuHelper.SelectNext(selectedIndex, items.Count);
            AnnounceCurrent();
        }

        private static void SelectPrevious()
        {
            if (items.Count == 0) return;
            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, items.Count);
            AnnounceCurrent();
        }

        private static void JumpToFirst()
        {
            if (items.Count == 0) return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                selectedIndex = typeahead.GetFirstMatch();
                AnnounceWithSearch();
                return;
            }

            typeahead.ClearSearch();
            selectedIndex = MenuHelper.JumpToFirst();
            AnnounceCurrent();
        }

        private static void JumpToLast()
        {
            if (items.Count == 0) return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                selectedIndex = typeahead.GetLastMatch();
                AnnounceWithSearch();
                return;
            }

            typeahead.ClearSearch();
            selectedIndex = MenuHelper.JumpToLast(items.Count);
            AnnounceCurrent();
        }

        // ============================================================
        // Activation
        // ============================================================

        private static void Activate()
        {
            if (items.Count == 0 || selectedIndex < 0 || selectedIndex >= items.Count) return;
            var item = items[selectedIndex];

            switch (item.WidgetKind)
            {
                case CapturedWidget.Kind.Checkbox:
                {
                    bool newValue = !item.BoolValue;
                    ModSettingsCaptureState.SetPendingBool(item.DrawIndex, item.Label, CapturedWidget.Kind.Checkbox, newValue);
                    string state = newValue
                        ? "RimWorldAccess.ModSettings.Menu.On".Translate().ToString()
                        : "RimWorldAccess.ModSettings.Menu.Off".Translate().ToString();
                    TolkHelper.SpeakData("RimWorldAccess.ModSettings.Menu.CheckboxToggled".Translate(item.Label, state));
                    break;
                }
                case CapturedWidget.Kind.Button:
                {
                    ModSettingsCaptureState.SetPendingActivation(item.DrawIndex, item.Label, CapturedWidget.Kind.Button);
                    TolkHelper.SpeakData("RimWorldAccess.ModSettings.Menu.Activated".Translate(item.Label));
                    // Many mods use a row of buttons as section/tab switchers that swap the
                    // content below on the next frame; schedule a jump into that new content.
                    enterAfterActivate = true;
                    preActivateLabels = items.Select(i => i.Label).ToList();
                    enterAfterActivateTtl = 8;
                    break;
                }
                case CapturedWidget.Kind.RadioButton:
                {
                    ModSettingsCaptureState.SetPendingActivation(item.DrawIndex, item.Label, CapturedWidget.Kind.RadioButton);
                    TolkHelper.SpeakData("RimWorldAccess.ModSettings.Menu.RadioSelected".Translate(item.Label));
                    break;
                }
                case CapturedWidget.Kind.Slider:
                case CapturedWidget.Kind.Label:
                default:
                    // Sliders are adjusted with Left/Right; Labels are informational.
                    // Re-announce so pressing Enter here doesn't feel like a dead key.
                    AnnounceCurrent();
                    break;
            }
        }

        private static void AdjustSlider(int direction)
        {
            if (items.Count == 0 || selectedIndex < 0 || selectedIndex >= items.Count) return;
            var item = items[selectedIndex];
            if (item.WidgetKind != CapturedWidget.Kind.Slider) return;

            float range = item.Max - item.Min;
            if (range <= 0f) return;

            // Compound from the value we last requested (if a change is still pending) rather
            // than the last-captured value, so rapid Left/Right presses each move the slider
            // instead of repeatedly recomputing from the same lagging base and getting lost.
            float baseValue = item.FloatValue;
            if (ModSettingsCaptureState.TryGetPendingFloat(item.DrawIndex, out float pendingTarget))
                baseValue = pendingTarget;
            // Guard against a captured value that sits outside the slider's own range (some mods
            // report a value beyond [min,max]); clamp the base first so one press can't slam it.
            baseValue = Mathf.Clamp(baseValue, item.Min, item.Max);

            // Use a "nice" step (1/2/5 x 10^n near range/20) and snap the result onto a fixed
            // grid of multiples of that step measured from Min. This makes every press land on a
            // round, predictable value (e.g. a 50..500 slider moves 180 -> 200 -> 220, not
            // 200 -> 222.5) and keeps Left/Right exactly reversible regardless of the odd value
            // the mod may have started at.
            float step = NiceStep(range);
            if (step <= 0f) return;
            int gridPos = Mathf.RoundToInt(baseValue / step);
            float newValue = Mathf.Clamp((gridPos + direction) * step, item.Min, item.Max);

            if (Mathf.Approximately(newValue, baseValue))
            {
                // Already at the limit in this direction - just re-read the current value.
                TolkHelper.SpeakData(BuildItemAnnouncement(item, includePosition: false));
                return;
            }

            ModSettingsCaptureState.SetPendingFloat(item.DrawIndex, item.Label, CapturedWidget.Kind.Slider, newValue);

            // Don't announce a raw computed number now (it won't match the mod's own formatting,
            // e.g. "1.3" vs "130%"). Instead wait for the mod to redraw the slider's label with
            // the applied value and announce that - see ProcessDeferredSliderAnnounce.
            sliderAnnounceDraw = item.DrawIndex;
            sliderAnnouncePrevLabel = item.Label;
            sliderAnnounceTtl = 8;
        }

        /// <summary>
        /// A human-friendly step size near 1/20 of the slider's range, rounded to the nearest
        /// 1, 2 or 5 times a power of ten (so ranges give steps like 0.2, 5, 20, 50, 100).
        /// </summary>
        private static float NiceStep(float range)
        {
            float raw = range / 20f;
            if (raw <= 0f) return 0f;
            float mag = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(raw)));
            float norm = raw / mag; // in [1, 10)
            float niceNorm = norm < 1.5f ? 1f : norm < 3.5f ? 2f : norm < 7.5f ? 5f : 10f;
            return niceNorm * mag;
        }

        private static void CloseDialog()
        {
            if (currentDialog != null)
            {
                Find.WindowStack.TryRemove(currentDialog, doCloseSound: false);
            }
        }

        // ============================================================
        // Data access helpers
        // ============================================================

        private static List<string> GetLabels()
        {
            return items.Select(i => i.Label).ToList();
        }

        // ============================================================
        // Announcements
        // ============================================================

        private static void AnnounceOpening(string modName)
        {
            string opened = "RimWorldAccess.ModSettings.OpenedFor".Translate(modName).ToString();

            if (items.Count == 0)
            {
                TolkHelper.SpeakData(opened + " " + "RimWorldAccess.ModSettings.Menu.NoSettings".Translate());
                return;
            }

            string firstItem = BuildItemAnnouncement(items[0], includePosition: true);
            TolkHelper.SpeakData("RimWorldAccess.ModSettings.OpeningWithItem".Translate(opened, firstItem));
        }

        private static void AnnounceCurrent()
        {
            if (items.Count == 0 || selectedIndex < 0 || selectedIndex >= items.Count) return;
            TolkHelper.SpeakData(BuildItemAnnouncement(items[selectedIndex], includePosition: true));
        }

        private static void AnnounceWithSearch()
        {
            if (items.Count == 0 || selectedIndex < 0 || selectedIndex >= items.Count) return;

            if (typeahead.HasActiveSearch)
            {
                TolkHelper.SpeakData(typeahead.BuildItemAnnouncement(BuildItemAnnouncement(items[selectedIndex], includePosition: false)));
            }
            else
            {
                AnnounceCurrent();
            }
        }

        /// <summary>
        /// Builds "{label}, {kind}, {value}{, position}" - Labels skip the kind/value parts
        /// entirely and read as plain text.
        /// </summary>
        private static string BuildItemAnnouncement(CapturedWidget item, bool includePosition)
        {
            var parts = new List<string> { item.Label };

            switch (item.WidgetKind)
            {
                case CapturedWidget.Kind.Checkbox:
                    parts.Add("RimWorldAccess.ModSettings.Menu.Checkbox".Translate().ToString());
                    parts.Add(item.BoolValue
                        ? "RimWorldAccess.ModSettings.Menu.On".Translate().ToString()
                        : "RimWorldAccess.ModSettings.Menu.Off".Translate().ToString());
                    break;
                case CapturedWidget.Kind.Button:
                    parts.Add("RimWorldAccess.ModSettings.Menu.Button".Translate().ToString());
                    break;
                case CapturedWidget.Kind.RadioButton:
                    parts.Add("RimWorldAccess.ModSettings.Menu.RadioButton".Translate().ToString());
                    parts.Add(item.BoolValue
                        ? "RimWorldAccess.ModSettings.Menu.Selected".Translate().ToString()
                        : "RimWorldAccess.ModSettings.Menu.NotSelected".Translate().ToString());
                    break;
                case CapturedWidget.Kind.Slider:
                    // No separate value part: mods bake the current value into the slider's
                    // label ("Volume: 100%"), so item.Label already carries it. Appending our
                    // own raw number would just be a confusing, differently-formatted duplicate.
                    parts.Add("RimWorldAccess.ModSettings.Menu.Slider".Translate().ToString());
                    break;
                case CapturedWidget.Kind.Label:
                default:
                    // Plain text - no kind/value suffix.
                    break;
            }

            if (includePosition)
            {
                string position = MenuHelper.FormatPosition(selectedIndex, items.Count);
                if (!string.IsNullOrEmpty(position))
                    parts.Add(position);
            }

            return string.Join(", ", parts);
        }
    }
}
