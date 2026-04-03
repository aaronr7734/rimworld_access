using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// State class for keyboard navigation of the Dialog_AutoSlaughter.
    /// Provides row/column navigation with value adjustment, numeric input,
    /// and vanilla-matching animal counting logic.
    /// </summary>
    public static class AutoSlaughterState
    {
        #region Data Structures

        private struct AnimalCounts
        {
            public int total;
            public int males;
            public int malesYoung;
            public int females;
            public int femalesYoung;
            public int pregnant;
            public int bonded;
        }

        private enum Column
        {
            MaxTotal = 0,
            MaxMales = 1,
            MaxMalesYoung = 2,
            MaxFemales = 3,
            MaxFemalesYoung = 4,
            AllowPregnant = 5,
            AllowBonded = 6
        }

        private static readonly string[] ColumnNames = new[]
        {
            "Maximum allowed population",
            "Maximum allowed males",
            "Maximum allowed young males",
            "Maximum allowed females",
            "Maximum allowed young females",
            "Allow pregnant slaughter",
            "Allow bonded slaughter"
        };

        #endregion

        #region State Fields

        public static bool IsActive { get; private set; } = false;

        private static Dialog_AutoSlaughter currentDialog;
        private static List<AutoSlaughterConfig> configs = new List<AutoSlaughterConfig>();
        private static Dictionary<ThingDef, AnimalCounts> cachedCounts = new Dictionary<ThingDef, AnimalCounts>();
        private static int currentRowIndex = 0;
        private static int currentColumnIndex = 0;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        private static bool isNumericInputMode = false;
        private static string numericBuffer = "";

        public static TypeaheadSearchHelper Typeahead => typeahead;
        public static int CurrentRowIndex => currentRowIndex;
        public static bool IsNumericInputMode => isNumericInputMode;

        #endregion

        #region Lifecycle

        public static void Open(Dialog_AutoSlaughter dialog)
        {
            if (IsActive) return;

            currentDialog = dialog;
            RefreshConfigs();

            if (configs.Count == 0)
            {
                TolkHelper.Speak("No animals available for auto-slaughter settings");
                return;
            }

            currentRowIndex = 0;
            currentColumnIndex = 0;
            typeahead.ClearSearch();
            isNumericInputMode = false;
            numericBuffer = "";
            IsActive = true;

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            TolkHelper.Speak($"Auto-slaughter settings, {configs.Count} animal types");
            AnnounceCurrentCell(includeAnimalName: true);
        }

        public static void Close()
        {
            // Build slaughter summary before clearing state
            string slaughterSummary = BuildSlaughterSummary();

            IsActive = false;
            currentDialog = null;
            configs.Clear();
            cachedCounts.Clear();
            typeahead.ClearSearch();
            isNumericInputMode = false;
            numericBuffer = "";

            if (!string.IsNullOrEmpty(slaughterSummary))
                TolkHelper.Speak($"Auto-slaughter closed. {slaughterSummary}");
            else
                TolkHelper.Speak("Auto-slaughter closed");
        }

        /// <summary>
        /// Builds a summary of all animals marked for slaughter across all species.
        /// Called when closing the dialog to give the user a final overview.
        /// </summary>
        private static string BuildSlaughterSummary()
        {
            var manager = Find.CurrentMap?.autoSlaughterManager;
            if (manager == null) return null;

            var slaughterList = manager.AnimalsToSlaughter;
            if (slaughterList == null || slaughterList.Count == 0) return null;

            // Group by animal type
            var groups = slaughterList
                .GroupBy(p => p.def)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} {g.Key.label}")
                .ToList();

            return $"Marked for slaughter: {string.Join(", ", groups)}";
        }

        #endregion

        #region Config and Count Management

        private static void RefreshConfigs()
        {
            configs.Clear();
            cachedCounts.Clear();
            if (Find.CurrentMap?.autoSlaughterManager?.configs == null) return;

            var manager = Find.CurrentMap.autoSlaughterManager;

            // Compute counts for all configs
            foreach (var config in manager.configs)
            {
                cachedCounts[config.animal] = ComputeCounts(config);
            }

            // Sort by count descending, then by label (matching vanilla's sorting)
            configs = manager.configs
                .OrderByDescending(c => cachedCounts.TryGetValue(c.animal, out var counts) ? counts.total : 0)
                .ThenBy(c => c.animal.label)
                .ToList();
        }

        /// <summary>
        /// Computes animal counts matching vanilla's Dialog_AutoSlaughter.CountPlayerAnimals logic exactly.
        /// Bonded animals excluded from category counts when allowSlaughterBonded is false.
        /// Pregnant females excluded from female count and total when allowSlaughterPregnant is false.
        /// </summary>
        private static AnimalCounts ComputeCounts(AutoSlaughterConfig config)
        {
            var counts = new AnimalCounts();
            var map = Find.CurrentMap;
            if (map?.mapPawns?.SpawnedColonyAnimals == null) return counts;

            foreach (Pawn pawn in map.mapPawns.SpawnedColonyAnimals)
            {
                if (pawn.def != config.animal)
                    continue;

                if (!AutoSlaughterManager.CanEverAutoSlaughter(pawn))
                    continue;

                // Bonded animals are always counted in bonded tally,
                // but skip all category counts when bonded slaughter is disabled
                if (pawn.relations != null && pawn.relations.GetDirectRelationsCount(PawnRelationDefOf.Bond) > 0)
                {
                    counts.bonded++;
                    if (!config.allowSlaughterBonded)
                        continue;
                }

                // Gender and age categorization
                if (pawn.gender == Gender.Male)
                {
                    if (pawn.ageTracker?.CurLifeStage?.reproductive == true)
                        counts.males++;
                    else
                        counts.malesYoung++;
                }
                else if (pawn.gender == Gender.Female)
                {
                    if (pawn.ageTracker?.CurLifeStage?.reproductive == true)
                    {
                        // Pregnancy check uses Visible (not just HasHediff) to match vanilla
                        Hediff pregnancyHediff = pawn.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Pregnant);
                        if (pregnancyHediff != null && pregnancyHediff.Visible)
                        {
                            counts.pregnant++;
                            if (!config.allowSlaughterPregnant)
                                continue;
                            counts.females++;
                        }
                        else
                        {
                            counts.females++;
                        }
                    }
                    else
                    {
                        counts.femalesYoung++;
                    }
                }

                counts.total++;
            }

            return counts;
        }

        /// <summary>
        /// Refreshes cached counts for all configs without re-sorting.
        /// Called after checkbox toggles since counts depend on allow flags.
        /// </summary>
        private static void RefreshAllCounts()
        {
            cachedCounts.Clear();
            foreach (var config in configs)
            {
                cachedCounts[config.animal] = ComputeCounts(config);
            }
        }

        private static AnimalCounts GetCounts(AutoSlaughterConfig config)
        {
            if (cachedCounts.TryGetValue(config.animal, out var counts))
                return counts;
            counts = ComputeCounts(config);
            cachedCounts[config.animal] = counts;
            return counts;
        }

        #endregion

        #region Column Abstraction Helpers

        private static int GetLimitForColumn(AutoSlaughterConfig config, Column column)
        {
            switch (column)
            {
                case Column.MaxTotal: return config.maxTotal;
                case Column.MaxMales: return config.maxMales;
                case Column.MaxMalesYoung: return config.maxMalesYoung;
                case Column.MaxFemales: return config.maxFemales;
                case Column.MaxFemalesYoung: return config.maxFemalesYoung;
                default: return -1;
            }
        }

        private static void SetLimitForColumn(AutoSlaughterConfig config, Column column, int value)
        {
            switch (column)
            {
                case Column.MaxTotal: config.maxTotal = value; break;
                case Column.MaxMales: config.maxMales = value; break;
                case Column.MaxMalesYoung: config.maxMalesYoung = value; break;
                case Column.MaxFemales: config.maxFemales = value; break;
                case Column.MaxFemalesYoung: config.maxFemalesYoung = value; break;
            }
        }

        private static int GetCountForColumn(AnimalCounts counts, Column column)
        {
            switch (column)
            {
                case Column.MaxTotal: return counts.total;
                case Column.MaxMales: return counts.males;
                case Column.MaxMalesYoung: return counts.malesYoung;
                case Column.MaxFemales: return counts.females;
                case Column.MaxFemalesYoung: return counts.femalesYoung;
                default: return 0;
            }
        }

        private static bool IsNumericColumn(Column column)
        {
            return column != Column.AllowPregnant && column != Column.AllowBonded;
        }

        #endregion

        #region Navigation

        public static void SelectNextRow()
        {
            if (configs.Count == 0) return;

            currentRowIndex = (currentRowIndex + 1) % configs.Count;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        public static void SelectPreviousRow()
        {
            if (configs.Count == 0) return;

            currentRowIndex = (currentRowIndex - 1 + configs.Count) % configs.Count;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        public static void SelectNextColumn()
        {
            currentColumnIndex = (currentColumnIndex + 1) % ColumnNames.Length;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: false);
        }

        public static void SelectPreviousColumn()
        {
            currentColumnIndex = (currentColumnIndex - 1 + ColumnNames.Length) % ColumnNames.Length;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: false);
        }

        public static void JumpToFirst()
        {
            if (configs.Count == 0) return;

            currentRowIndex = 0;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        public static void JumpToLast()
        {
            if (configs.Count == 0) return;

            currentRowIndex = configs.Count - 1;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        #endregion

        #region Value Adjustment

        /// <summary>
        /// Adjusts the current column's limit by the specified delta.
        /// Unlimited-to-limit transition defaults to current count (matching vanilla).
        /// Single decrement from 0 goes to unlimited; multi-step decrements clamp at 0.
        /// </summary>
        private static void AdjustValue(int delta)
        {
            if (configs.Count == 0) return;

            var config = configs[currentRowIndex];
            var column = (Column)currentColumnIndex;

            if (!IsNumericColumn(column))
            {
                ToggleBoolean();
                return;
            }

            int currentLimit = GetLimitForColumn(config, column);

            if (currentLimit == -1)
            {
                // Unlimited: start from current count and apply delta
                // + → current count + 1 (allow one more than you have)
                // - → current count - 1 (slaughter one)
                // Shift+Down → current count - 10, etc.
                var counts = GetCounts(config);
                int currentCount = GetCountForColumn(counts, column);
                int newLimit = currentCount + delta;
                if (newLimit < 0) newLimit = 0;
                SetLimitForColumn(config, column, newLimit);
            }
            else
            {
                int newLimit = currentLimit + delta;

                if (delta == -1 && currentLimit == 0)
                {
                    // Single decrement from 0 → unlimited
                    newLimit = -1;
                }
                else if (newLimit < 0)
                {
                    // Multi-step decrement clamps at 0 (don't accidentally wrap to unlimited)
                    newLimit = 0;
                }

                SetLimitForColumn(config, column, newLimit);
            }

            Find.CurrentMap?.autoSlaughterManager?.Notify_ConfigChanged();
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void ToggleBoolean()
        {
            if (configs.Count == 0) return;

            var config = configs[currentRowIndex];
            var column = (Column)currentColumnIndex;

            switch (column)
            {
                case Column.AllowPregnant:
                    config.allowSlaughterPregnant = !config.allowSlaughterPregnant;
                    if (config.allowSlaughterPregnant)
                        SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                    else
                        SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                    break;
                case Column.AllowBonded:
                    config.allowSlaughterBonded = !config.allowSlaughterBonded;
                    if (config.allowSlaughterBonded)
                        SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                    else
                        SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                    break;
                default:
                    return;
            }

            Find.CurrentMap?.autoSlaughterManager?.Notify_ConfigChanged();
            RefreshAllCounts();
            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void SetToUnlimited()
        {
            if (configs.Count == 0) return;

            var config = configs[currentRowIndex];
            var column = (Column)currentColumnIndex;

            if (!IsNumericColumn(column))
                return;

            SetLimitForColumn(config, column, -1);

            Find.CurrentMap?.autoSlaughterManager?.Notify_ConfigChanged();
            SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void SetToZero()
        {
            if (configs.Count == 0) return;

            var config = configs[currentRowIndex];
            var column = (Column)currentColumnIndex;

            if (!IsNumericColumn(column))
                return;

            SetLimitForColumn(config, column, 0);

            Find.CurrentMap?.autoSlaughterManager?.Notify_ConfigChanged();
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: false);
        }

        #endregion

        #region Slaughter Warning

        /// <summary>
        /// Queries the game's actual slaughter computation to get the total animals
        /// of this type marked for slaughter across all limit interactions.
        /// </summary>
        private static int GetTotalMarkedForSlaughter(AutoSlaughterConfig config)
        {
            var manager = Find.CurrentMap?.autoSlaughterManager;
            if (manager == null) return 0;
            return manager.AnimalsToSlaughter.Count(p => p.def == config.animal);
        }

        #endregion

        #region Announcements

        private static void AnnounceCurrentCell(bool includeAnimalName)
        {
            if (configs.Count == 0) return;

            var config = configs[currentRowIndex];
            var column = (Column)currentColumnIndex;
            string columnName = ColumnNames[currentColumnIndex];

            string value = GetColumnValueString(config, column);
            string position = MenuHelper.FormatPosition(currentRowIndex, configs.Count);

            string announcement;
            if (includeAnimalName)
            {
                announcement = $"{config.animal.LabelCap}, {columnName}: {value}. {position}";
            }
            else
            {
                announcement = $"{columnName}: {value}";
            }

            TolkHelper.Speak(announcement);
        }

        private static string GetColumnValueString(AutoSlaughterConfig config, Column column)
        {
            var counts = GetCounts(config);

            switch (column)
            {
                case Column.MaxTotal:
                    return FormatCurrentOfMax(counts.total, config.maxTotal);
                case Column.MaxMales:
                    return FormatCurrentOfMax(counts.males, config.maxMales);
                case Column.MaxMalesYoung:
                    return FormatCurrentOfMax(counts.malesYoung, config.maxMalesYoung);
                case Column.MaxFemales:
                    return FormatCurrentOfMax(counts.females, config.maxFemales);
                case Column.MaxFemalesYoung:
                    return FormatCurrentOfMax(counts.femalesYoung, config.maxFemalesYoung);
                case Column.AllowPregnant:
                    return $"{counts.pregnant} pregnant, slaughter {(config.allowSlaughterPregnant ? "allowed" : "not allowed")}";
                case Column.AllowBonded:
                    return $"{counts.bonded} bonded, slaughter {(config.allowSlaughterBonded ? "allowed" : "not allowed")}";
                default:
                    return "Unknown";
            }
        }

        private static string FormatCurrentOfMax(int current, int max)
        {
            if (max == -1)
            {
                return $"unlimited. Current population: {current}";
            }
            else if (current > max)
            {
                int toSlaughter = current - max;
                return $"{max}. Current population: {current}. {toSlaughter} will be slaughtered";
            }
            else
            {
                return $"{max}. Current population: {current}";
            }
        }

        #endregion

        #region Numeric Input Mode

        private static void EnterNumericMode()
        {
            if (configs.Count == 0) return;
            var column = (Column)currentColumnIndex;

            if (!IsNumericColumn(column))
            {
                // Boolean column: toggle instead
                ToggleBoolean();
                return;
            }

            numericBuffer = "";
            isNumericInputMode = true;
            TolkHelper.Speak("Type a number, then press Enter to confirm or Escape to cancel");
        }

        private static void HandleNumericDigit(char digit)
        {
            if (!isNumericInputMode) return;

            numericBuffer += digit;
            TolkHelper.Speak(numericBuffer, SpeechPriority.Low);
        }

        private static void HandleNumericBackspace()
        {
            if (!isNumericInputMode || numericBuffer.Length == 0) return;

            numericBuffer = numericBuffer.Substring(0, numericBuffer.Length - 1);
            if (numericBuffer.Length > 0)
                TolkHelper.Speak(numericBuffer, SpeechPriority.Low);
            else
                TolkHelper.Speak("Empty", SpeechPriority.Low);
        }

        private static void ConfirmNumericInput()
        {
            if (!isNumericInputMode) return;

            if (int.TryParse(numericBuffer, out int value) && (value >= 0 || value == -1))
            {
                var config = configs[currentRowIndex];
                var column = (Column)currentColumnIndex;
                SetLimitForColumn(config, column, value);
                Find.CurrentMap?.autoSlaughterManager?.Notify_ConfigChanged();
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            }
            else
            {
                TolkHelper.Speak("Invalid number. Use minus 1 for unlimited");
            }

            isNumericInputMode = false;
            numericBuffer = "";
            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void CancelNumericInput()
        {
            isNumericInputMode = false;
            numericBuffer = "";
            TolkHelper.Speak("Cancelled");
        }

        #endregion

        #region Typeahead Search

        public static List<string> GetItemLabels()
        {
            return configs.Select(c => c.animal.LabelCap.ToString()).ToList();
        }

        public static void SetCurrentRowIndex(int index)
        {
            if (index >= 0 && index < configs.Count)
            {
                currentRowIndex = index;
            }
        }

        public static void HandleTypeahead(char c)
        {
            var labels = GetItemLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    currentRowIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        public static void HandleBackspace()
        {
            if (!typeahead.HasActiveSearch) return;

            var labels = GetItemLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                    currentRowIndex = newIndex;
                AnnounceWithSearch();
            }
        }

        public static void AnnounceWithSearch()
        {
            if (configs.Count == 0)
            {
                TolkHelper.Speak("No animals");
                return;
            }

            var config = configs[currentRowIndex];
            var column = (Column)currentColumnIndex;
            string columnName = ColumnNames[currentColumnIndex];
            string value = GetColumnValueString(config, column);
            string position = MenuHelper.FormatPosition(currentRowIndex, configs.Count);

            string announcement = $"{config.animal.LabelCap}, {columnName}: {value}. {position}";

            if (typeahead.HasActiveSearch)
            {
                announcement += $", match {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} for '{typeahead.SearchBuffer}'";
            }

            TolkHelper.Speak(announcement);
        }

        #endregion

        #region Input Handling

        /// <summary>
        /// Handles keyboard input for the auto-slaughter dialog.
        /// Returns true if input was handled, false otherwise.
        /// </summary>
        public static bool HandleInput(Event evt)
        {
            if (!IsActive || evt.type != EventType.KeyDown) return false;

            KeyCode key = evt.keyCode;
            bool shift = evt.shift;
            bool ctrl = evt.control;

            // Numeric input mode captures all relevant keys
            if (isNumericInputMode)
            {
                if (key == KeyCode.Escape)
                {
                    CancelNumericInput();
                    return true;
                }
                if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    ConfirmNumericInput();
                    return true;
                }
                if (key == KeyCode.Backspace)
                {
                    HandleNumericBackspace();
                    return true;
                }
                // Minus key: allow typing negative sign for -1 (unlimited)
                if ((key == KeyCode.Minus || key == KeyCode.KeypadMinus) && numericBuffer.Length == 0)
                {
                    numericBuffer = "-";
                    TolkHelper.Speak("minus", SpeechPriority.Low);
                    return true;
                }
                if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
                {
                    HandleNumericDigit((char)('0' + (key - KeyCode.Alpha0)));
                    return true;
                }
                if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
                {
                    HandleNumericDigit((char)('0' + (key - KeyCode.Keypad0)));
                    return true;
                }
                // Consume all other keys in numeric mode
                return true;
            }

            // Escape: clear search first, then close everything (auto-slaughter + animals menu)
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                }
                else
                {
                    string slaughterSummary = BuildSlaughterSummary();
                    var dialog = currentDialog;

                    // Clear state BEFORE removing dialog to prevent PostClose from calling Close() again
                    IsActive = false;
                    currentDialog = null;
                    configs.Clear();
                    cachedCounts.Clear();
                    typeahead.ClearSearch();
                    isNumericInputMode = false;
                    numericBuffer = "";

                    if (dialog != null)
                        Find.WindowStack.TryRemove(dialog, doCloseSound: false);

                    // Close animals menu entirely
                    bool hasSlaughter = !string.IsNullOrEmpty(slaughterSummary);
                    AnimalsMenuState.Close(silent: hasSlaughter);

                    // If slaughtering, announce with summary
                    if (hasSlaughter)
                        TolkHelper.Speak($"Animals menu closed. {slaughterSummary}");
                }
                return true;
            }

            // Enter: confirm typeahead search if active, otherwise numeric mode
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearch();
                    AnnounceCurrentCell(includeAnimalName: true);
                    return true;
                }
                EnterNumericMode();
                return true;
            }

            // Backspace: search backspace
            if (key == KeyCode.Backspace)
            {
                HandleBackspace();
                return true;
            }

            // Home/End with modifier variants
            if (key == KeyCode.Home)
            {
                if (shift && !ctrl)
                    SetToZero();
                else
                    JumpToFirst();
                return true;
            }
            if (key == KeyCode.End)
            {
                if (shift && !ctrl)
                    SetToUnlimited();
                else
                    JumpToLast();
                return true;
            }

            // Arrow keys: navigation and modifier-based quantity adjustment
            if (key == KeyCode.DownArrow)
            {
                if (shift && !ctrl)
                {
                    AdjustValue(-10);
                }
                else if (ctrl && !shift)
                {
                    AdjustValue(-100);
                }
                else if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int newIndex = typeahead.GetNextMatch(currentRowIndex);
                    if (newIndex >= 0)
                    {
                        currentRowIndex = newIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectNextRow();
                }
                return true;
            }
            if (key == KeyCode.UpArrow)
            {
                if (shift && !ctrl)
                {
                    AdjustValue(10);
                }
                else if (ctrl && !shift)
                {
                    AdjustValue(100);
                }
                else if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int newIndex = typeahead.GetPreviousMatch(currentRowIndex);
                    if (newIndex >= 0)
                    {
                        currentRowIndex = newIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectPreviousRow();
                }
                return true;
            }
            if (key == KeyCode.RightArrow)
            {
                SelectNextColumn();
                return true;
            }
            if (key == KeyCode.LeftArrow)
            {
                SelectPreviousColumn();
                return true;
            }

            // +/- for single-step value adjustment
            if (key == KeyCode.Plus || key == KeyCode.KeypadPlus || key == KeyCode.Equals)
            {
                AdjustValue(1);
                return true;
            }
            if (key == KeyCode.Minus || key == KeyCode.KeypadMinus)
            {
                AdjustValue(-1);
                return true;
            }

            // Space: toggle boolean columns
            if (key == KeyCode.Space)
            {
                var column = (Column)currentColumnIndex;
                if (column == Column.AllowPregnant || column == Column.AllowBonded)
                {
                    ToggleBoolean();
                    return true;
                }
            }

            // Tab/Shift+Tab: close auto-slaughter, return to animals menu
            if (key == KeyCode.Tab)
            {
                string slaughterSummary = BuildSlaughterSummary();
                var dialog = currentDialog;

                // Clear state BEFORE removing dialog to prevent PostClose from calling Close() again
                IsActive = false;
                currentDialog = null;
                configs.Clear();
                cachedCounts.Clear();
                typeahead.ClearSearch();
                isNumericInputMode = false;
                numericBuffer = "";

                if (dialog != null)
                    Find.WindowStack.TryRemove(dialog, doCloseSound: false);

                // Announce slaughter summary if any, otherwise just the menu name
                if (!string.IsNullOrEmpty(slaughterSummary))
                    TolkHelper.Speak(slaughterSummary);
                else
                    TolkHelper.Speak("Animals menu");

                return true;
            }

            // Typeahead: letter keys only
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            if (isLetter && !KeyboardHelper.IsAltHeld)
            {
                TypeaheadCharacterBuffer.RequestCharacter(c => HandleTypeahead(c));
                return true;
            }

            return false;
        }

        #endregion
    }
}
