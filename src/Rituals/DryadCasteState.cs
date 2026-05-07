using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard accessibility state for Dialog_ChangeDryadCaste.
    /// Provides flat menu navigation through available dryad castes with typeahead search.
    /// </summary>
    public static class DryadCasteState
    {
        private static bool isActive;
        private static Dialog_ChangeDryadCaste currentDialog;
        private static List<GauranlenTreeModeDef> allModes = new List<GauranlenTreeModeDef>();
        private static int selectedIndex = 0;
        private static TypeaheadSearchHelper typeaheadHelper = new TypeaheadSearchHelper();

        // Cached reflection handles
        private static System.Reflection.FieldInfo selectedModeField;
        private static System.Reflection.FieldInfo currentModeField;
        private static System.Reflection.FieldInfo allModesField;
        private static System.Reflection.FieldInfo treeConnectionField;
        private static System.Reflection.FieldInfo connectedPawnField;
        private static System.Reflection.MethodInfo meetsRequirementsMethod;
        private static System.Reflection.MethodInfo meetsMemeRequirementsMethod;
        private static System.Reflection.MethodInfo startChangeMethod;

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => typeaheadHelper.HasActiveSearch;

        public static void Open(Dialog_ChangeDryadCaste dialog)
        {
            if (dialog == null)
                return;

            try
            {
                CacheReflection();

                currentDialog = dialog;
                allModes = (allModesField?.GetValue(dialog) as List<GauranlenTreeModeDef>) ?? new List<GauranlenTreeModeDef>();
                typeaheadHelper.ClearSearch();

                GauranlenTreeModeDef currentMode = currentModeField?.GetValue(currentDialog) as GauranlenTreeModeDef;

                // Seed selected index to the user's actual current caste so the first announcement matches.
                selectedIndex = 0;
                if (currentMode != null)
                {
                    int idx = allModes.IndexOf(currentMode);
                    if (idx >= 0) selectedIndex = idx;
                }

                isActive = true;

                Pawn connectedPawn = connectedPawnField?.GetValue(dialog) as Pawn;
                CompTreeConnection treeConnection = treeConnectionField?.GetValue(dialog) as CompTreeConnection;
                string connectedPawnLabel = connectedPawn?.LabelShortCap ?? "connected pawn";

                TolkHelper.Speak($"{"ChangeMode".Translate()}. {allModes.Count} castes for {connectedPawnLabel}.");

                if (connectedPawn != null && treeConnection?.parent != null)
                {
                    try
                    {
                        var cocoonProps = ThingDefOf.DryadCocoon?.GetCompProperties<CompProperties_DryadCocoon>();
                        int daysToComplete = (int)(cocoonProps?.daysToComplete ?? 0f);
                        string intro = "ChooseProductionModeInitialDesc".Translate(
                            connectedPawn.Named("PAWN"),
                            treeConnection.parent.Named("TREE"),
                            daysToComplete.Named("UPGRADEDURATION"));
                        TolkHelper.Speak(SanitizeText(intro));
                    }
                    catch (Exception introEx)
                    {
                        Log.Warning($"[DryadCasteState] Could not read initial intro text: {introEx.Message}");
                    }
                }

                AnnounceCurrentSelection();
            }
            catch (Exception ex)
            {
                Log.Error($"[DryadCasteState] Error opening: {ex.Message}");
                Close();
            }
        }

        public static void Close()
        {
            isActive = false;
            currentDialog = null;
            allModes.Clear();
            selectedIndex = 0;
            typeaheadHelper.ClearSearch();
        }

        public static bool HandleInput(Event ev)
        {
            if (!isActive || currentDialog == null)
                return false;

            if (ev.type != EventType.KeyDown)
                return false;

            KeyCode key = ev.keyCode;

            // Block Ctrl/Alt-modified keys so host shortcuts don't leak through the modal dialog.
            if (ev.control || KeyboardHelper.IsAltHeld)
                return true;

            if (key == KeyCode.Home)
            {
                typeaheadHelper.ClearSearch();
                if (allModes.Count > 0)
                {
                    selectedIndex = 0;
                    AnnounceCurrentSelection();
                }
                return true;
            }

            if (key == KeyCode.End)
            {
                typeaheadHelper.ClearSearch();
                if (allModes.Count > 0)
                {
                    selectedIndex = allModes.Count - 1;
                    AnnounceCurrentSelection();
                }
                return true;
            }

            if (key == KeyCode.Escape)
            {
                if (typeaheadHelper.HasActiveSearch)
                {
                    typeaheadHelper.ClearSearchAndAnnounce();
                    AnnounceCurrentSelection();
                    return true;
                }
                currentDialog.Close(doCloseSound: false);
                return true;
            }

            if (key == KeyCode.Backspace && typeaheadHelper.HasActiveSearch)
            {
                var labels = GetCasteLabels();
                if (typeaheadHelper.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0) selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
                return true;
            }

            if (key == KeyCode.UpArrow)
            {
                if (typeaheadHelper.HasActiveSearch && !typeaheadHelper.HasNoMatches)
                {
                    int prev = typeaheadHelper.GetPreviousMatch(selectedIndex);
                    if (prev >= 0)
                    {
                        selectedIndex = prev;
                        AnnounceWithSearch();
                    }
                }
                else if (allModes.Count > 0)
                {
                    selectedIndex = MenuHelper.SelectPrevious(selectedIndex, allModes.Count);
                    AnnounceCurrentSelection();
                }
                return true;
            }

            if (key == KeyCode.DownArrow)
            {
                if (typeaheadHelper.HasActiveSearch && !typeaheadHelper.HasNoMatches)
                {
                    int next = typeaheadHelper.GetNextMatch(selectedIndex);
                    if (next >= 0)
                    {
                        selectedIndex = next;
                        AnnounceWithSearch();
                    }
                }
                else if (allModes.Count > 0)
                {
                    selectedIndex = MenuHelper.SelectNext(selectedIndex, allModes.Count);
                    AnnounceCurrentSelection();
                }
                return true;
            }

            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                TrySelectCaste();
                return true;
            }

            // Typeahead character routing now handled by TypeaheadDispatcher upstream.

            return true; // modal window: consume everything else
        }

        /// <summary>
        /// Layout-aware typeahead character entry; called by <see cref="TypeaheadDispatcher"/>.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive) return;

            var labels = GetCasteLabels();
            if (typeaheadHelper.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeaheadHelper.LastFailedSearch}'");
            }
        }

        private static void AnnounceCurrentSelection()
        {
            if (allModes.Count == 0 || selectedIndex < 0 || selectedIndex >= allModes.Count)
                return;

            GauranlenTreeModeDef mode = allModes[selectedIndex];
            string announcement = FormatModeAnnouncement(mode);
            string position = MenuHelper.FormatPosition(selectedIndex, allModes.Count);

            string fullText = string.IsNullOrEmpty(position)
                ? announcement
                : $"{announcement}, {position}";
            TolkHelper.Speak(fullText, SpeechPriority.Normal);
        }

        private static void AnnounceWithSearch()
        {
            if (allModes.Count == 0 || selectedIndex < 0 || selectedIndex >= allModes.Count)
                return;

            if (!typeaheadHelper.HasActiveSearch)
            {
                AnnounceCurrentSelection();
                return;
            }

            GauranlenTreeModeDef mode = allModes[selectedIndex];
            TolkHelper.Speak(
                $"{FormatModeAnnouncement(mode)}, {typeaheadHelper.CurrentMatchPosition} of {typeaheadHelper.MatchCount} matches for '{typeaheadHelper.SearchBuffer}'");
        }

        private static string FormatModeAnnouncement(GauranlenTreeModeDef mode)
        {
            if (mode == null)
                return "Unknown caste";

            GauranlenTreeModeDef currentMode = currentModeField?.GetValue(currentDialog) as GauranlenTreeModeDef;
            Pawn connectedPawn = connectedPawnField?.GetValue(currentDialog) as Pawn;

            bool meetsMemes = meetsMemeRequirementsMethod != null
                && (bool)(meetsMemeRequirementsMethod.Invoke(currentDialog, new object[] { mode }) ?? false);
            bool meetsAll = meetsRequirementsMethod != null
                && (bool)(meetsRequirementsMethod.Invoke(currentDialog, new object[] { mode }) ?? false);

            string status;
            if (mode == currentMode)
            {
                status = "AlreadySelected".Translate();
            }
            else if (meetsAll)
            {
                status = "available";
            }
            else if (!meetsMemes)
            {
                status = $"{"Locked".Translate()}: {"MissingRequiredMemes".Translate()}";
            }
            else if (mode.previousStage != null && currentMode != mode.previousStage)
            {
                status = $"{"Locked".Translate()}: {"MissingRequiredCaste".Translate()}";
            }
            else
            {
                status = "Locked".Translate();
            }

            var parts = new List<string> { $"{mode.LabelCap}, {status}" };

            string description = SanitizeText(mode.Description);
            if (!string.IsNullOrEmpty(description))
                parts.Add(description);

            // Announce all required memes, marking missing ones so the user has the same info as a sighted player.
            if (connectedPawn?.Ideo != null
                && Find.IdeoManager != null
                && !Find.IdeoManager.classicMode
                && !mode.requiredMemes.NullOrEmpty())
            {
                var memeParts = new List<string>();
                foreach (var memeDef in mode.requiredMemes)
                {
                    bool has = connectedPawn.Ideo.HasMeme(memeDef);
                    // Short "(missing)" marker — status line already said which overall requirement
                    // is blocking; this just tags which specific memes are the problem.
                    memeParts.Add(has
                        ? memeDef.LabelCap.ToString()
                        : $"{memeDef.LabelCap} (missing)");
                }
                parts.Add($"{"RequiredMemes".Translate()}: {string.Join(", ", memeParts)}");
            }

            if (mode.previousStage != null)
            {
                string stageLabel = mode.previousStage.pawnKindDef?.LabelCap.ToString()
                    ?? mode.previousStage.LabelCap.ToString();
                parts.Add($"{"RequiredStage".Translate()}: {stageLabel}");
            }

            if (mode.displayedStats != null && mode.displayedStats.Count > 0 && mode.pawnKindDef?.race != null)
            {
                var statsLines = new List<string>();
                foreach (var statDef in mode.displayedStats)
                {
                    try
                    {
                        string statValue = statDef.ValueToString(
                            mode.pawnKindDef.race.GetStatValueAbstract(statDef),
                            statDef.toStringNumberSense);
                        statsLines.Add($"{statDef.LabelCap}: {statValue}");
                    }
                    catch { }
                }
                if (statsLines.Count > 0)
                    parts.Add("Stats: " + string.Join(", ", statsLines));
            }

            return string.Join(". ", parts);
        }

        private static void TrySelectCaste()
        {
            if (allModes.Count == 0 || selectedIndex < 0 || selectedIndex >= allModes.Count)
            {
                TolkHelper.Speak("No caste selected.");
                return;
            }

            GauranlenTreeModeDef mode = allModes[selectedIndex];
            GauranlenTreeModeDef currentMode = currentModeField?.GetValue(currentDialog) as GauranlenTreeModeDef;

            if (mode == currentMode)
            {
                TolkHelper.Speak("AlreadySelected".Translate());
                return;
            }

            bool meetsRequirements = meetsRequirementsMethod != null
                && (bool)(meetsRequirementsMethod.Invoke(currentDialog, new object[] { mode }) ?? false);
            if (!meetsRequirements)
            {
                bool meetsMemeRequirements = meetsMemeRequirementsMethod != null
                    && (bool)(meetsMemeRequirementsMethod.Invoke(currentDialog, new object[] { mode }) ?? false);
                if (!meetsMemeRequirements)
                {
                    TolkHelper.Speak("MissingRequiredMemes".Translate());
                    return;
                }

                if (mode.previousStage != null && currentMode != mode.previousStage)
                {
                    TolkHelper.Speak("MissingRequiredCaste".Translate());
                    return;
                }

                TolkHelper.Speak("Locked".Translate());
                return;
            }

            CompTreeConnection treeConnection = treeConnectionField?.GetValue(currentDialog) as CompTreeConnection;
            Pawn connectedPawn = connectedPawnField?.GetValue(currentDialog) as Pawn;
            if (treeConnection?.parent == null || connectedPawn == null)
            {
                TolkHelper.Speak("Cannot apply caste: missing tree data.");
                return;
            }

            // Update the dialog's selectedMode field so StartChange applies the right caste.
            selectedModeField?.SetValue(currentDialog, mode);
            SoundDefOf.Click.PlayOneShotOnCamera();

            // Capture locally so the confirmation callback is safe even if our state closes first.
            Dialog_ChangeDryadCaste capturedDialog = currentDialog;
            System.Reflection.MethodInfo capturedStartChange = startChangeMethod;

            float duration = ThingDefOf.DryadCocoon.GetCompProperties<CompProperties_DryadCocoon>().daysToComplete;
            string confirmRaw = "GauranlenModeChangeDescFull".Translate(
                treeConnection.parent.Named("TREE"),
                connectedPawn.Named("CONNECTEDPAWN"),
                duration.Named("DURATION"));

            Dialog_MessageBox confirm = Dialog_MessageBox.CreateConfirmation(confirmRaw, () =>
            {
                capturedStartChange?.Invoke(capturedDialog, null);
            });

            Find.WindowStack.Add(confirm);
            // WindowlessDialogState announces the Dialog_MessageBox automatically — no manual Speak needed.
        }

        private static List<string> GetCasteLabels()
        {
            var labels = new List<string>(allModes.Count);
            foreach (var mode in allModes)
                labels.Add(mode?.LabelCap.ToString() ?? "");
            return labels;
        }

        // Screen readers read "\n" as dead air or literally "newline"; flatten to sentence punctuation.
        private static string SanitizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string result = text.Replace("\r", "").Replace("\n\n", ". ").Replace("\n", ". ");
            while (result.Contains(". . ")) result = result.Replace(". . ", ". ");
            return result.Trim();
        }

        private static void CacheReflection()
        {
            if (selectedModeField == null)
                selectedModeField = AccessTools.Field(typeof(Dialog_ChangeDryadCaste), "selectedMode");
            if (currentModeField == null)
                currentModeField = AccessTools.Field(typeof(Dialog_ChangeDryadCaste), "currentMode");
            if (allModesField == null)
                allModesField = AccessTools.Field(typeof(Dialog_ChangeDryadCaste), "allDryadModes");
            if (treeConnectionField == null)
                treeConnectionField = AccessTools.Field(typeof(Dialog_ChangeDryadCaste), "treeConnection");
            if (connectedPawnField == null)
                connectedPawnField = AccessTools.Field(typeof(Dialog_ChangeDryadCaste), "connectedPawn");

            if (meetsRequirementsMethod == null)
                meetsRequirementsMethod = AccessTools.Method(typeof(Dialog_ChangeDryadCaste), "MeetsRequirements");
            if (meetsMemeRequirementsMethod == null)
                meetsMemeRequirementsMethod = AccessTools.Method(typeof(Dialog_ChangeDryadCaste), "MeetsMemeRequirements");
            if (startChangeMethod == null)
                startChangeMethod = AccessTools.Method(typeof(Dialog_ChangeDryadCaste), "StartChange");
        }
    }
}
