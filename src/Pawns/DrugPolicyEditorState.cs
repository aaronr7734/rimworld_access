using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    public static class DrugPolicyEditorState
    {
        public enum NavigationMode
        {
            DrugList,
            DrugSettings
        }

        private static bool isActive = false;
        private static DrugPolicy policy = null;
        private static int selectedDrugIndex = 0;
        private static int selectedSettingIndex = 0;
        private static NavigationMode currentMode = NavigationMode.DrugList;

        // Dynamic settings list built per-drug based on vanilla conditional visibility
        private static List<DrugSetting> currentSettings = new List<DrugSetting>();

        private static System.Action onCloseCallback = null;

        // Typeahead search over the drug list (active only in DrugList mode).
        private static readonly TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;
        public static DrugPolicy Policy => policy;
        public static NavigationMode CurrentMode => currentMode;

        /// <summary>True when a typeahead search is active in the drug list.</summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Clears the active typeahead search and re-announces the current drug. Used by Escape
        /// so the first Escape cancels the search rather than closing the editor.
        /// </summary>
        public static void ClearSearch()
        {
            typeahead.ClearSearch();
            AnnounceDrugList();
        }

        private enum SettingType
        {
            TakeToInventory,
            AllowForAddiction,
            AllowForJoy,
            AllowScheduled,
            Frequency,
            MoodThreshold,
            JoyThreshold
        }

        private class DrugSetting
        {
            public SettingType Type;
            public string Label;
            public string Tooltip;
        }

        public static void Open(DrugPolicy drugPolicy, System.Action onClose)
        {
            isActive = true;
            policy = drugPolicy;
            selectedDrugIndex = 0;
            selectedSettingIndex = 0;
            currentMode = NavigationMode.DrugList;
            onCloseCallback = onClose;
            typeahead.ClearSearch();

            AnnounceDrugList();
        }

        public static void Close()
        {
            isActive = false;
            policy = null;
            selectedDrugIndex = 0;
            selectedSettingIndex = 0;
            currentMode = NavigationMode.DrugList;
            currentSettings.Clear();
            typeahead.ClearSearch();

            var callback = onCloseCallback;
            onCloseCallback = null;
            callback?.Invoke();
        }

        // === Drug List Navigation ===

        public static void SelectNextDrug()
        {
            if (policy == null || policy.Count == 0) return;
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                selectedDrugIndex = typeahead.GetNextMatch(selectedDrugIndex);
            else
                selectedDrugIndex = MenuHelper.SelectNext(selectedDrugIndex, policy.Count);
            AnnounceDrugList();
        }

        public static void SelectPreviousDrug()
        {
            if (policy == null || policy.Count == 0) return;
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                selectedDrugIndex = typeahead.GetPreviousMatch(selectedDrugIndex);
            else
                selectedDrugIndex = MenuHelper.SelectPrevious(selectedDrugIndex, policy.Count);
            AnnounceDrugList();
        }

        public static void JumpToFirstDrug()
        {
            if (policy == null || policy.Count == 0) return;
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                selectedDrugIndex = typeahead.GetFirstMatch();
            else
                selectedDrugIndex = 0;
            AnnounceDrugList();
        }

        public static void JumpToLastDrug()
        {
            if (policy == null || policy.Count == 0) return;
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                selectedDrugIndex = typeahead.GetLastMatch();
            else
                selectedDrugIndex = policy.Count - 1;
            AnnounceDrugList();
        }

        /// <summary>
        /// Typeahead jump within the drug list (DrugList mode only): type letters to jump to
        /// the next drug whose name matches. Mirrors the PlantSelectionMenuState pattern.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive || currentMode != NavigationMode.DrugList || policy == null || policy.Count == 0)
                return;

            var labels = GetDrugLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedDrugIndex = newIndex;
                    AnnounceDrugList();
                }
            }
            else
            {
                typeahead.SpeakNoMatches();
            }
        }

        private static List<string> GetDrugLabels()
        {
            var labels = new List<string>();
            if (policy == null) return labels;
            for (int i = 0; i < policy.Count; i++)
            {
                ThingDef drug = policy[i].drug;
                labels.Add(drug != null ? drug.LabelCap.ToString() : "");
            }
            return labels;
        }

        public static void EnterDrugSettings()
        {
            if (policy == null || selectedDrugIndex < 0 || selectedDrugIndex >= policy.Count) return;

            BuildSettingsForCurrentDrug();
            if (currentSettings.Count == 0) return;

            currentMode = NavigationMode.DrugSettings;
            selectedSettingIndex = 0;
            AnnounceDrugSetting();
        }

        public static void ReturnToDrugList()
        {
            currentMode = NavigationMode.DrugList;
            currentSettings.Clear();
            AnnounceDrugList();
        }

        // === Drug Settings Navigation ===

        public static void SelectNextSetting()
        {
            if (currentSettings.Count == 0) return;
            selectedSettingIndex = MenuHelper.SelectNext(selectedSettingIndex, currentSettings.Count);
            AnnounceDrugSetting();
        }

        public static void SelectPreviousSetting()
        {
            if (currentSettings.Count == 0) return;
            selectedSettingIndex = MenuHelper.SelectPrevious(selectedSettingIndex, currentSettings.Count);
            AnnounceDrugSetting();
        }

        public static void ToggleSetting()
        {
            if (policy == null || selectedDrugIndex < 0 || selectedDrugIndex >= policy.Count) return;
            if (currentSettings.Count == 0 || selectedSettingIndex < 0 || selectedSettingIndex >= currentSettings.Count) return;

            DrugPolicyEntry entry = policy[selectedDrugIndex];
            var setting = currentSettings[selectedSettingIndex];

            switch (setting.Type)
            {
                case SettingType.AllowForAddiction:
                    entry.allowedForAddiction = !entry.allowedForAddiction;
                    break;
                case SettingType.AllowForJoy:
                    entry.allowedForJoy = !entry.allowedForJoy;
                    break;
                case SettingType.AllowScheduled:
                    bool wasScheduled = entry.allowScheduled;
                    entry.allowScheduled = !entry.allowScheduled;
                    // Rebuild settings list since frequency/thresholds visibility depends on this
                    BuildSettingsForCurrentDrug();
                    // Clamp index if settings list shrank
                    if (selectedSettingIndex >= currentSettings.Count)
                        selectedSettingIndex = currentSettings.Count - 1;
                    break;
                default:
                    // Non-boolean settings don't toggle
                    return;
            }

            AnnounceDrugSetting();
        }

        public static void AdjustSetting(int direction)
        {
            if (policy == null || selectedDrugIndex < 0 || selectedDrugIndex >= policy.Count) return;
            if (currentSettings.Count == 0 || selectedSettingIndex < 0 || selectedSettingIndex >= currentSettings.Count) return;

            DrugPolicyEntry entry = policy[selectedDrugIndex];
            var setting = currentSettings[selectedSettingIndex];

            switch (setting.Type)
            {
                case SettingType.Frequency:
                    // Vanilla range: 0.1 to 25, rounded to int
                    float newFreq = entry.daysFrequency + direction;
                    newFreq = Mathf.Clamp(Mathf.Round(newFreq), 1f, 25f);
                    entry.daysFrequency = newFreq;
                    break;

                case SettingType.MoodThreshold:
                    entry.onlyIfMoodBelow += direction * 0.05f;
                    entry.onlyIfMoodBelow = Mathf.Clamp(entry.onlyIfMoodBelow, 0.01f, 1f);
                    break;

                case SettingType.JoyThreshold:
                    entry.onlyIfJoyBelow += direction * 0.05f;
                    entry.onlyIfJoyBelow = Mathf.Clamp(entry.onlyIfJoyBelow, 0.01f, 1f);
                    break;

                case SettingType.TakeToInventory:
                    int maxPickup = PawnUtility.GetMaxAllowedToPickUp(entry.drug);
                    entry.takeToInventory = Mathf.Clamp(entry.takeToInventory + direction, 0, maxPickup);
                    break;

                default:
                    return;
            }

            AnnounceSettingValue(entry, setting);
        }

        // === Settings List Builder ===

        private static void BuildSettingsForCurrentDrug()
        {
            currentSettings.Clear();

            if (policy == null || selectedDrugIndex < 0 || selectedDrugIndex >= policy.Count) return;
            DrugPolicyEntry entry = policy[selectedDrugIndex];

            // Take to inventory (always shown)
            currentSettings.Add(new DrugSetting
            {
                Type = SettingType.TakeToInventory,
                Label = "TakeToInventoryColumnLabel".Translate(),
                Tooltip = "TakeToInventoryColumnDesc".Translate()
            });

            // Addiction checkbox - only if drug is addictive (matching vanilla DoEntryRow line 190)
            if (entry.drug.IsAddictiveDrug)
            {
                currentSettings.Add(new DrugSetting
                {
                    Type = SettingType.AllowForAddiction,
                    Label = "DrugUsageTipForAddiction".Translate().Resolve().Split('\n')[0],
                    Tooltip = GetTooltipWithoutLabel("DrugUsageTipForAddiction".Translate().Resolve())
                });
            }

            // Joy checkbox - only if drug is a pleasure drug (matching vanilla DoEntryRow line 195)
            if (entry.drug.IsPleasureDrug)
            {
                currentSettings.Add(new DrugSetting
                {
                    Type = SettingType.AllowForJoy,
                    Label = "DrugUsageTipForJoy".Translate().Resolve().Split('\n')[0],
                    Tooltip = GetTooltipWithoutLabel("DrugUsageTipForJoy".Translate().Resolve())
                });
            }

            // Scheduled checkbox (always shown)
            currentSettings.Add(new DrugSetting
            {
                Type = SettingType.AllowScheduled,
                Label = "DrugUsageTipScheduled".Translate().Resolve().Split('\n')[0],
                Tooltip = GetTooltipWithoutLabel("DrugUsageTipScheduled".Translate().Resolve())
            });

            // Frequency, mood threshold, joy threshold - only shown if scheduled is enabled
            // (matching vanilla DoEntryRow line 202)
            if (entry.allowScheduled)
            {
                currentSettings.Add(new DrugSetting
                {
                    Type = SettingType.Frequency,
                    Label = "FrequencyColumnLabel".Translate(),
                    Tooltip = "FrequencyColumnDesc".Translate()
                });

                currentSettings.Add(new DrugSetting
                {
                    Type = SettingType.MoodThreshold,
                    Label = "MoodThresholdColumnLabel".Translate(),
                    Tooltip = "MoodThresholdColumnDesc".Translate()
                });

                currentSettings.Add(new DrugSetting
                {
                    Type = SettingType.JoyThreshold,
                    Label = "JoyThresholdColumnLabel".Translate(),
                    Tooltip = "JoyThresholdColumnDesc".Translate()
                });
            }
        }

        // === Announcements ===

        private static void AnnounceDrugList()
        {
            if (policy == null || policy.Count == 0)
            {
                TolkHelper.Speak("NoDrugs".Loc());
                return;
            }

            DrugPolicyEntry entry = policy[selectedDrugIndex];
            string drugName = entry.drug.LabelCap;
            string status = GetDrugStatusSummary(entry);
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                TolkHelper.Speak($"{drugName}. {status}. match {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} for '{typeahead.SearchBuffer}'");
            }
            else
            {
                string position = MenuHelper.FormatPosition(selectedDrugIndex, policy.Count);
                TolkHelper.Speak($"{drugName}. {status}. {position}");
            }
        }

        private static void AnnounceDrugSetting()
        {
            if (currentSettings.Count == 0 || selectedSettingIndex < 0 || selectedSettingIndex >= currentSettings.Count) return;
            if (policy == null || selectedDrugIndex < 0 || selectedDrugIndex >= policy.Count) return;

            DrugPolicyEntry entry = policy[selectedDrugIndex];
            var setting = currentSettings[selectedSettingIndex];
            string value = GetSettingValueString(entry, setting);
            string position = MenuHelper.FormatPosition(selectedSettingIndex, currentSettings.Count);
            if (setting.Tooltip != null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.DrugPolicy.SettingWithTooltip".Loc(setting.Label, value, setting.Tooltip, position));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.DrugPolicy.SettingNoTooltip".Loc(setting.Label, value, position));
            }
        }

        private static void AnnounceSettingValue(DrugPolicyEntry entry, DrugSetting setting)
        {
            string value = GetSettingValueString(entry, setting);
            TolkHelper.Speak("RimWorldAccess.Pawns.DrugPolicy.SettingChange".Loc(setting.Label, value));
        }

        private static string GetSettingValueString(DrugPolicyEntry entry, DrugSetting setting)
        {
            switch (setting.Type)
            {
                case SettingType.TakeToInventory:
                    return entry.takeToInventory.ToString();

                case SettingType.AllowForAddiction:
                    return entry.allowedForAddiction ? "On".Translate() : "Off".Translate();

                case SettingType.AllowForJoy:
                    return entry.allowedForJoy ? "On".Translate() : "Off".Translate();

                case SettingType.AllowScheduled:
                    return entry.allowScheduled ? "On".Translate() : "Off".Translate();

                case SettingType.Frequency:
                    return FormatFrequency(entry.daysFrequency);

                case SettingType.MoodThreshold:
                    if (entry.onlyIfMoodBelow >= 1f)
                        return "NoDrugUseRequirement".Translate();
                    return entry.onlyIfMoodBelow.ToStringPercent();

                case SettingType.JoyThreshold:
                    if (entry.onlyIfJoyBelow >= 1f)
                        return "NoDrugUseRequirement".Translate();
                    return entry.onlyIfJoyBelow.ToStringPercent();

                default:
                    return "";
            }
        }

        private static string GetTooltipWithoutLabel(string fullText)
        {
            int idx = fullText.IndexOf('\n');
            if (idx < 0) return null;
            return fullText.Substring(idx + 1).TrimStart('\n', ' ');
        }

        private static string FormatFrequency(float freq)
        {
            // Matches vanilla Widgets.FrequencyHorizontalSlider label formatting
            if (freq == 1f)
                return "EveryDay".Translate();
            if (freq < 1f)
                return "TimesPerDay".Translate((1f / freq).ToString("0.##"));
            return "EveryDays".Translate(freq.ToString("0.##"));
        }

        private static string GetDrugStatusSummary(DrugPolicyEntry entry)
        {
            List<string> parts = new List<string>();

            if (entry.drug.IsAddictiveDrug && entry.allowedForAddiction)
                parts.Add("DrugUsageTipForAddiction".Translate().Resolve().Split('\n')[0]);
            if (entry.drug.IsPleasureDrug && entry.allowedForJoy)
                parts.Add("DrugUsageTipForJoy".Translate().Resolve().Split('\n')[0]);
            if (entry.allowScheduled)
                parts.Add("DrugUsageTipScheduled".Translate().Resolve().Split('\n')[0]);
            if (entry.takeToInventory > 0)
                parts.Add($"{"TakeToInventoryColumnLabel".Translate()}: {entry.takeToInventory}");

            if (parts.Count == 0)
                return "None".Translate();

            if (parts.Count == 1)
                return parts[0];
            if (parts.Count == 2)
                return $"{parts[0]} and {parts[1]}";
            // Oxford comma for 3+
            return string.Join(", ", parts.Take(parts.Count - 1)) + ", and " + parts[parts.Count - 1];
        }
    }
}
