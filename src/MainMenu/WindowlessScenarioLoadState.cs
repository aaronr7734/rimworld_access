using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State for loading scenarios in the Scenario Builder.
    /// Provides keyboard navigation through saved scenario files.
    /// </summary>
    public static class WindowlessScenarioLoadState
    {
        public static bool IsActive { get; private set; }

        private static List<SaveFileInfo> scenarioFiles = new List<SaveFileInfo>();
        private static int selectedIndex = 0;
        private static Action<Scenario> onScenarioLoaded;
        private static TypeaheadSearchHelper typeaheadHelper = new TypeaheadSearchHelper();

        /// <summary>
        /// Opens the scenario load menu.
        /// </summary>
        public static void Open(Action<Scenario> onLoaded)
        {
            onScenarioLoaded = onLoaded;
            typeaheadHelper.ClearSearch();

            ReloadFiles();

            if (scenarioFiles.Count == 0)
            {
                TolkHelper.Speak("No saved scenarios found.");
                onScenarioLoaded?.Invoke(null);
                return;
            }

            selectedIndex = 0;
            IsActive = true;

            TolkHelper.Speak($"Load Scenario. {scenarioFiles.Count} scenarios available. Type to search.");
            AnnounceCurrentFile();
        }

        /// <summary>
        /// Closes the scenario load menu.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            scenarioFiles.Clear();
            typeaheadHelper.ClearSearch();
            onScenarioLoaded = null;
        }

        /// <summary>
        /// Reloads the list of scenario files.
        /// </summary>
        private static void ReloadFiles()
        {
            scenarioFiles.Clear();

            foreach (FileInfo file in GenFilePaths.AllCustomScenarioFiles)
            {
                try
                {
                    var saveInfo = new SaveFileInfo(file);
                    saveInfo.LoadData();
                    scenarioFiles.Add(saveInfo);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimWorld Access] Exception loading scenario file {file.Name}: {ex}");
                }
            }

            // Sort by last write time, most recent first
            scenarioFiles = scenarioFiles.OrderByDescending(f => f.LastWriteTime).ToList();
        }

        /// <summary>
        /// Announces the currently selected file.
        /// </summary>
        private static void AnnounceCurrentFile()
        {
            if (scenarioFiles.Count == 0)
            {
                TolkHelper.Speak("No scenarios available.");
                return;
            }

            var file = scenarioFiles[selectedIndex];
            string fileName = Path.GetFileNameWithoutExtension(file.FileName);
            string positionPart = MenuHelper.FormatPosition(selectedIndex, scenarioFiles.Count);
            string dateStr = FormatDateTime(file.LastWriteTime);

            string text = $"{fileName} - {dateStr}";

            if (typeaheadHelper.HasActiveSearch)
            {
                text += typeaheadHelper.BuildSearchContextSuffix();
            }
            else if (!string.IsNullOrEmpty(positionPart))
            {
                text += $" ({positionPart})";
            }

            TolkHelper.Speak(text);
        }

        /// <summary>
        /// Formats a DateTime for display.
        /// </summary>
        private static string FormatDateTime(DateTime dateTime)
        {
            if (Prefs.TwelveHourClockMode)
            {
                return dateTime.ToString("yyyy-MM-dd h:mm tt");
            }
            else
            {
                return dateTime.ToString("yyyy-MM-dd HH:mm");
            }
        }

        #region Navigation

        private static void SelectNext()
        {
            if (scenarioFiles.Count == 0) return;

            typeaheadHelper.ClearSearch();
            selectedIndex = MenuHelper.SelectNext(selectedIndex, scenarioFiles.Count);
            AnnounceCurrentFile();
        }

        private static void SelectPrevious()
        {
            if (scenarioFiles.Count == 0) return;

            typeaheadHelper.ClearSearch();
            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, scenarioFiles.Count);
            AnnounceCurrentFile();
        }

        private static void JumpToFirst()
        {
            if (scenarioFiles.Count == 0) return;

            typeaheadHelper.ClearSearch();
            selectedIndex = 0;
            AnnounceCurrentFile();
        }

        private static void JumpToLast()
        {
            if (scenarioFiles.Count == 0) return;

            typeaheadHelper.ClearSearch();
            selectedIndex = scenarioFiles.Count - 1;
            AnnounceCurrentFile();
        }

        private static void SelectNextMatch()
        {
            if (!typeaheadHelper.HasActiveSearch) return;

            int next = typeaheadHelper.GetNextMatch(selectedIndex);
            if (next >= 0)
            {
                selectedIndex = next;
                AnnounceCurrentFile();
            }
        }

        private static void SelectPreviousMatch()
        {
            if (!typeaheadHelper.HasActiveSearch) return;

            int prev = typeaheadHelper.GetPreviousMatch(selectedIndex);
            if (prev >= 0)
            {
                selectedIndex = prev;
                AnnounceCurrentFile();
            }
        }

        #endregion

        #region Typeahead

        private static bool HandleTypeahead(char character)
        {
            if (scenarioFiles.Count == 0) return false;

            var labels = scenarioFiles.Select(f => Path.GetFileNameWithoutExtension(f.FileName)).ToList();

            if (typeaheadHelper.ProcessCharacterInput(character, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceCurrentFile();
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

            var labels = scenarioFiles.Select(f => Path.GetFileNameWithoutExtension(f.FileName)).ToList();

            if (typeaheadHelper.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceCurrentFile();
                }
            }

            return true;
        }

        private static bool ClearTypeahead()
        {
            if (typeaheadHelper.ClearSearchAndAnnounce())
            {
                AnnounceCurrentFile();
                return true;
            }
            return false;
        }

        #endregion

        #region Actions

        private static void LoadSelected()
        {
            if (scenarioFiles.Count == 0 || selectedIndex >= scenarioFiles.Count)
            {
                TolkHelper.Speak("No scenario selected.");
                return;
            }

            var file = scenarioFiles[selectedIndex];
            string fileName = Path.GetFileNameWithoutExtension(file.FileName);

            try
            {
                PreLoadUtility.CheckVersionAndLoad(file.FileInfo.FullName, ScribeMetaHeaderUtility.ScribeHeaderMode.Scenario, delegate
                {
                    if (GameDataSaveLoader.TryLoadScenario(file.FileInfo.FullName, ScenarioCategory.CustomLocal, out Scenario scenario))
                    {
                        Close();
                        onScenarioLoaded?.Invoke(scenario);
                    }
                    else
                    {
                        TolkHelper.Speak($"Failed to load scenario: {fileName}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error loading scenario: {ex}");
                TolkHelper.Speak($"Error loading scenario: {ex.Message}");
            }
        }

        private static void DeleteSelected()
        {
            if (scenarioFiles.Count == 0 || selectedIndex >= scenarioFiles.Count)
            {
                return;
            }

            var file = scenarioFiles[selectedIndex];
            string fileName = Path.GetFileNameWithoutExtension(file.FileName);

            TolkHelper.Speak($"Delete {fileName}? Press Enter to confirm, Escape to cancel.");
            WindowlessScenarioDeleteConfirmState.Open(file.FileInfo, () =>
            {
                ReloadFiles();
                if (selectedIndex >= scenarioFiles.Count)
                {
                    selectedIndex = Math.Max(0, scenarioFiles.Count - 1);
                }
                IsActive = true;
                AnnounceCurrentFile();
            });

            IsActive = false;
        }

        #endregion

        #region Input Handling

        /// <summary>
        /// Handles keyboard input for the load menu.
        /// Returns true if the input was handled.
        /// </summary>
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
                        Close();
                        TolkHelper.Speak("Cancelled");
                        onScenarioLoaded?.Invoke(null);
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

        /// <summary>
        /// Handles character input for typeahead.
        /// </summary>
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

    /// <summary>
    /// Handles confirmation for deleting scenario files.
    /// </summary>
    public static class WindowlessScenarioDeleteConfirmState
    {
        public static bool IsActive { get; private set; }

        private static FileInfo fileToDelete;
        private static Action onComplete;

        public static void Open(FileInfo file, Action onCompleteCallback)
        {
            fileToDelete = file;
            onComplete = onCompleteCallback;
            IsActive = true;
        }

        public static void Confirm()
        {
            if (!IsActive || fileToDelete == null) return;

            string fileName = fileToDelete.Name;
            fileToDelete.Delete();
            TolkHelper.Speak($"Deleted {fileName}");

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
            fileToDelete = null;
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
