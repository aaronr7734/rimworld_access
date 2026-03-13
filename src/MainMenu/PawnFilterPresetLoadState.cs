using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static class PawnFilterPresetLoadState
    {
        public static bool IsActive { get; private set; }

        private static List<string> presetNames = new List<string>();
        private static int selectedIndex = 0;
        private static Action<PawnFilter> onPresetLoaded;
        private static TypeaheadSearchHelper typeaheadHelper = new TypeaheadSearchHelper();

        public static void Open(Action<PawnFilter> onLoaded)
        {
            onPresetLoaded = onLoaded;
            typeaheadHelper.ClearSearch();

            ReloadPresets();

            if (presetNames.Count == 0)
            {
                TolkHelper.Speak("No saved presets found.");
                onPresetLoaded?.Invoke(null);
                return;
            }

            selectedIndex = 0;
            IsActive = true;

            TolkHelper.Speak($"Load filter preset. {presetNames.Count} presets available. Type to search.");
            AnnounceCurrentPreset();
        }

        public static void Close()
        {
            IsActive = false;
            presetNames.Clear();
            typeaheadHelper.ClearSearch();
            onPresetLoaded = null;
        }

        private static void ReloadPresets()
        {
            presetNames = PawnFilterPresetSerializer.GetPresetNames();
        }

        private static void AnnounceCurrentPreset()
        {
            if (presetNames.Count == 0)
            {
                TolkHelper.Speak("No presets available.");
                return;
            }

            string name = presetNames[selectedIndex];
            if (string.IsNullOrEmpty(name))
                name = "(unnamed)";

            string positionPart = MenuHelper.FormatPosition(selectedIndex, presetNames.Count);

            string text = name;

            if (typeaheadHelper.HasActiveSearch)
            {
                text += $", {typeaheadHelper.CurrentMatchPosition} of {typeaheadHelper.MatchCount} matches for '{typeaheadHelper.SearchBuffer}'";
            }
            else if (!string.IsNullOrEmpty(positionPart))
            {
                text += $" ({positionPart})";
            }

            TolkHelper.Speak(text);
        }

        #region Navigation

        private static void SelectNext()
        {
            if (presetNames.Count == 0) return;

            typeaheadHelper.ClearSearch();
            selectedIndex = MenuHelper.SelectNext(selectedIndex, presetNames.Count);
            AnnounceCurrentPreset();
        }

        private static void SelectPrevious()
        {
            if (presetNames.Count == 0) return;

            typeaheadHelper.ClearSearch();
            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, presetNames.Count);
            AnnounceCurrentPreset();
        }

        private static void JumpToFirst()
        {
            if (presetNames.Count == 0) return;

            typeaheadHelper.ClearSearch();
            selectedIndex = 0;
            AnnounceCurrentPreset();
        }

        private static void JumpToLast()
        {
            if (presetNames.Count == 0) return;

            typeaheadHelper.ClearSearch();
            selectedIndex = presetNames.Count - 1;
            AnnounceCurrentPreset();
        }

        private static void SelectNextMatch()
        {
            if (!typeaheadHelper.HasActiveSearch) return;

            int next = typeaheadHelper.GetNextMatch(selectedIndex);
            if (next >= 0)
            {
                selectedIndex = next;
                AnnounceCurrentPreset();
            }
        }

        private static void SelectPreviousMatch()
        {
            if (!typeaheadHelper.HasActiveSearch) return;

            int prev = typeaheadHelper.GetPreviousMatch(selectedIndex);
            if (prev >= 0)
            {
                selectedIndex = prev;
                AnnounceCurrentPreset();
            }
        }

        #endregion

        #region Typeahead

        private static bool HandleTypeahead(char character)
        {
            if (presetNames.Count == 0) return false;

            if (typeaheadHelper.ProcessCharacterInput(character, presetNames, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceCurrentPreset();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeaheadHelper.LastFailedSearch}'");
            }

            return true;
        }

        private static bool HandleTypeaheadBackspace()
        {
            if (!typeaheadHelper.HasActiveSearch) return false;

            if (typeaheadHelper.ProcessBackspace(presetNames, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceCurrentPreset();
                }
            }

            return true;
        }

        private static bool ClearTypeahead()
        {
            if (typeaheadHelper.ClearSearchAndAnnounce())
            {
                AnnounceCurrentPreset();
                return true;
            }
            return false;
        }

        #endregion

        #region Actions

        private static void LoadSelected()
        {
            if (presetNames.Count == 0 || selectedIndex >= presetNames.Count)
            {
                TolkHelper.Speak("No preset selected.");
                return;
            }

            string name = presetNames[selectedIndex];

            try
            {
                var loadedFilter = PawnFilterPresetSerializer.LoadPreset(selectedIndex);
                var callback = onPresetLoaded;
                Close();
                if (loadedFilter != null)
                {
                    TolkHelper.Speak($"Loaded {name}");
                    callback?.Invoke(loadedFilter);
                }
                else
                {
                    TolkHelper.Speak("Error loading preset");
                    callback?.Invoke(null);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error loading preset: {ex}");
                TolkHelper.Speak($"Error loading preset: {ex.Message}");
            }
        }

        private static void DeleteSelected()
        {
            if (presetNames.Count == 0 || selectedIndex >= presetNames.Count)
                return;

            string name = presetNames[selectedIndex];

            TolkHelper.Speak($"Delete {name}? Press Enter to confirm, Escape to cancel.");
            PawnFilterPresetDeleteConfirmState.Open(selectedIndex, name, () =>
            {
                ReloadPresets();
                if (selectedIndex >= presetNames.Count)
                {
                    selectedIndex = Math.Max(0, presetNames.Count - 1);
                }
                IsActive = true;

                if (presetNames.Count == 0)
                {
                    TolkHelper.Speak("No presets remaining.");
                    var callback = onPresetLoaded;
                    Close();
                    callback?.Invoke(null);
                }
                else
                {
                    AnnounceCurrentPreset();
                }
            });

            IsActive = false;
        }

        #endregion

        #region Input Handling

        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!IsActive) return false;

            switch (key)
            {
                case KeyCode.UpArrow:
                    if (typeaheadHelper.HasActiveSearch)
                        SelectPreviousMatch();
                    else
                        SelectPrevious();
                    return true;

                case KeyCode.DownArrow:
                    if (typeaheadHelper.HasActiveSearch)
                        SelectNextMatch();
                    else
                        SelectNext();
                    return true;

                case KeyCode.Home:
                    JumpToFirst();
                    return true;

                case KeyCode.End:
                    JumpToLast();
                    return true;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    LoadSelected();
                    return true;

                case KeyCode.Delete:
                    DeleteSelected();
                    return true;

                case KeyCode.Escape:
                    if (typeaheadHelper.HasActiveSearch)
                    {
                        ClearTypeahead();
                    }
                    else
                    {
                        var callback = onPresetLoaded;
                        Close();
                        TolkHelper.Speak("Cancelled");
                        callback?.Invoke(null);
                    }
                    return true;

                case KeyCode.Backspace:
                    if (typeaheadHelper.HasActiveSearch)
                    {
                        HandleTypeaheadBackspace();
                        return true;
                    }
                    break;
            }

            return false;
        }

        public static bool HandleCharacterInput(char character)
        {
            if (!IsActive) return false;

            if (char.IsLetterOrDigit(character))
            {
                return HandleTypeahead(character);
            }

            return false;
        }

        #endregion
    }

    public static class PawnFilterPresetDeleteConfirmState
    {
        public static bool IsActive { get; private set; }

        private static int indexToDelete;
        private static string nameToDelete;
        private static Action onComplete;

        public static void Open(int index, string name, Action onCompleteCallback)
        {
            indexToDelete = index;
            nameToDelete = name;
            onComplete = onCompleteCallback;
            IsActive = true;
        }

        public static void Confirm()
        {
            if (!IsActive) return;

            try
            {
                PawnFilterPresetSerializer.DeletePreset(indexToDelete);
                TolkHelper.Speak($"Deleted {nameToDelete}");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error deleting preset: {ex}");
                TolkHelper.Speak($"Error deleting preset: {ex.Message}");
            }

            Close();
            onComplete?.Invoke();
        }

        public static void Cancel()
        {
            if (!IsActive) return;

            TolkHelper.Speak("Delete cancelled");
            Close();
            onComplete?.Invoke();
        }

        private static void Close()
        {
            IsActive = false;
            nameToDelete = null;
            onComplete = null;
        }

        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!IsActive) return false;

            switch (key)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Confirm();
                    return true;

                case KeyCode.Escape:
                    Cancel();
                    return true;
            }

            return false;
        }
    }
}
