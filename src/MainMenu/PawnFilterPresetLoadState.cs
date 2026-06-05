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
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.NoSavedPresets".Loc());
                onPresetLoaded?.Invoke(null);
                return;
            }

            selectedIndex = 0;
            IsActive = true;

            TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.OpenInstructions".Loc(presetNames.Count));
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
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.NoPresetsAvailable".Loc());
                return;
            }

            string name = presetNames[selectedIndex];
            if (string.IsNullOrEmpty(name))
                name = "RimWorldAccess.PawnFilter.PresetLoad.UnnamedFallback".Translate();

            string positionPart = MenuHelper.FormatPosition(selectedIndex, presetNames.Count);

            string text = name;

            if (typeaheadHelper.HasActiveSearch)
            {
                text += typeaheadHelper.BuildSearchContextSuffix();
            }
            else if (!string.IsNullOrEmpty(positionPart))
            {
                text += "RimWorldAccess.PawnFilter.PresetLoad.WithPositionSuffix".Translate(positionPart);
            }

            TolkHelper.SpeakData(text);
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
                typeaheadHelper.SpeakNoMatches();
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
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.NoPresetSelected".Loc());
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
                    TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.Loaded".Loc(name));
                    callback?.Invoke(loadedFilter);
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.ErrorLoading".Loc());
                    callback?.Invoke(null);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error loading preset: {ex}");
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.ErrorLoadingDetail".Loc(ex.Message));
            }
        }

        private static void DeleteSelected()
        {
            if (presetNames.Count == 0 || selectedIndex >= presetNames.Count)
                return;

            string name = presetNames[selectedIndex];

            TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.DeleteConfirm".Loc(name));
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
                    TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.NoneRemaining".Loc());
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
                        TolkHelper.Speak("RimWorldAccess.UI.Cancelled".Loc());
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
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.Deleted".Loc(nameToDelete));
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error deleting preset: {ex}");
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.ErrorDeleting".Loc(ex.Message));
            }

            Close();
            onComplete?.Invoke();
        }

        public static void Cancel()
        {
            if (!IsActive) return;

            TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.DeleteCancelled".Loc());
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
