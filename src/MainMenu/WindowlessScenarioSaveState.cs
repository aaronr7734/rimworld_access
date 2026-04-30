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
    /// State for saving scenarios in the Scenario Builder.
    /// Provides keyboard navigation and filename input for saving.
    /// </summary>
    public static class WindowlessScenarioSaveState
    {
        public static bool IsActive { get; private set; }

        private static Scenario scenarioToSave;
        private static Action onSaveComplete;
        private static List<SaveFileInfo> existingFiles = new List<SaveFileInfo>();
        private static int selectedIndex = 0;
        private static bool isTypingFilename = true;
        private static TypeaheadSearchHelper typeaheadHelper = new TypeaheadSearchHelper();
        private static readonly TextInputController filenameController = new TextInputController();
        private static readonly TextFieldSpec filenameSpec = new TextFieldSpec(
            labelKey: "RimWorldAccess.TextInput.LabelFilename",
            maxLength: 64,
            minLength: 1,
            mustBeFilename: true);

        /// <summary>
        /// Opens the scenario save menu.
        /// </summary>
        public static void Open(Scenario scenario, Action onComplete)
        {
            scenarioToSave = scenario;
            onSaveComplete = onComplete;
            typeaheadHelper.ClearSearch();

            string initialName = GenFile.SanitizedFileName(scenario.name ?? "NewScenario");
            // Embedded controller — Up/Down arrows must reach the surrounding list, so
            // we own routing rather than going through TextInputManager.
            filenameController.Begin(initialName, filenameSpec, _ => { }, null, replaceOnType: true, modal: false);

            ReloadFiles();

            selectedIndex = 0;
            isTypingFilename = true;
            IsActive = true;

            TolkHelper.Speak("RimWorldAccess.ScenarioSave.OpenInstructions".Translate(filenameController.CurrentText));
        }

        /// <summary>
        /// Closes the scenario save menu.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            scenarioToSave = null;
            onSaveComplete = null;
            existingFiles.Clear();
            typeaheadHelper.ClearSearch();
            filenameController.Cancel();
        }

        /// <summary>
        /// Reloads the list of existing scenario files.
        /// </summary>
        private static void ReloadFiles()
        {
            existingFiles.Clear();

            foreach (FileInfo file in GenFilePaths.AllCustomScenarioFiles)
            {
                try
                {
                    var saveInfo = new SaveFileInfo(file);
                    saveInfo.LoadData();
                    existingFiles.Add(saveInfo);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimWorld Access] Exception loading scenario file {file.Name}: {ex}");
                }
            }

            // Sort by last write time, most recent first
            existingFiles = existingFiles.OrderByDescending(f => f.LastWriteTime).ToList();
        }

        /// <summary>
        /// Gets the total count including the "Create New" option.
        /// </summary>
        private static int TotalCount => existingFiles.Count + 1;

        /// <summary>
        /// Announces the current state.
        /// </summary>
        private static void AnnounceCurrentState()
        {
            if (selectedIndex == 0)
            {
                // Create new with typed name
                TolkHelper.Speak("RimWorldAccess.ScenarioSave.SaveAs".Translate(filenameController.CurrentText, MenuHelper.FormatPosition(0, TotalCount)));
            }
            else if (selectedIndex > 0 && selectedIndex <= existingFiles.Count)
            {
                // Overwrite existing
                var file = existingFiles[selectedIndex - 1];
                string fileName = Path.GetFileNameWithoutExtension(file.FileName);
                string dateStr = FormatDateTime(file.LastWriteTime);
                TolkHelper.Speak("RimWorldAccess.ScenarioSave.Overwrite".Translate(fileName, dateStr, MenuHelper.FormatPosition(selectedIndex, TotalCount)));
            }
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
            typeaheadHelper.ClearSearch();
            isTypingFilename = false;

            if (selectedIndex < existingFiles.Count)
            {
                selectedIndex++;
                AnnounceCurrentState();
            }
        }

        private static void SelectPrevious()
        {
            typeaheadHelper.ClearSearch();

            if (selectedIndex > 0)
            {
                selectedIndex--;
                if (selectedIndex == 0)
                {
                    isTypingFilename = true;
                }
                AnnounceCurrentState();
            }
        }

        private static void JumpToFirst()
        {
            typeaheadHelper.ClearSearch();
            selectedIndex = 0;
            isTypingFilename = true;
            AnnounceCurrentState();
        }

        private static void JumpToLast()
        {
            typeaheadHelper.ClearSearch();
            selectedIndex = existingFiles.Count;
            isTypingFilename = false;
            AnnounceCurrentState();
        }

        #endregion

        #region Actions

        private static void SaveSelected()
        {
            string fileName;

            if (selectedIndex == 0)
            {
                // Save with typed name
                fileName = filenameController.CurrentText;
            }
            else if (selectedIndex > 0 && selectedIndex <= existingFiles.Count)
            {
                // Overwrite existing file
                var file = existingFiles[selectedIndex - 1];
                fileName = Path.GetFileNameWithoutExtension(file.FileName);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioSave.InvalidSelection".Translate());
                return;
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioSave.FilenameEmpty".Translate());
                return;
            }

            fileName = GenFile.SanitizedFileName(fileName);

            // Check for overwrite
            string fullPath = GenFilePaths.AbsPathForScenario(fileName);
            bool fileExists = File.Exists(fullPath);

            if (fileExists && selectedIndex == 0)
            {
                // Trying to create new but file exists - confirm overwrite
                TolkHelper.Speak("RimWorldAccess.ScenarioSave.FileExists".Translate(fileName));
                // For simplicity, just save anyway on next Enter
            }

            try
            {
                scenarioToSave.name = scenarioToSave.name ?? fileName;
                GameDataSaveLoader.SaveScenario(scenarioToSave, fullPath);

                // Reset dirty flag after successful save
                ScenarioBuilderState.ResetDirty();

                Close();
                TolkHelper.Speak("RimWorldAccess.ScenarioSave.SavedAs".Translate(fileName));
                onSaveComplete?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error saving scenario: {ex}");
                TolkHelper.Speak("RimWorldAccess.ScenarioSave.ErrorSaving".Translate(ex.Message));
            }
        }

        #endregion

        #region Input Handling

        /// <summary>
        /// Handles keyboard input for the save menu.
        /// Returns true if the input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!IsActive) return false;

            // Cursor review: Left/Right (with Shift/Ctrl) let the user audit the filename
            // while they're actually typing. When browsing the existing-file list
            // (isTypingFilename == false), arrows aren't used by the state either, so we leave
            // them untouched rather than steal them for a hidden filename cursor move.
            if (isTypingFilename && selectedIndex == 0)
            {
                if (key == KeyCode.LeftArrow)
                {
                    filenameController.HandleArrowLeft(shift, ctrl);
                    return true;
                }
                if (key == KeyCode.RightArrow)
                {
                    filenameController.HandleArrowRight(shift, ctrl);
                    return true;
                }
            }

            switch (key)
            {
                case KeyCode.UpArrow:
                    SelectPrevious();
                    return true;

                case KeyCode.DownArrow:
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
                    SaveSelected();
                    return true;

                case KeyCode.Escape:
                    Close();
                    TolkHelper.Speak("RimWorldAccess.UI.Cancelled".Translate());
                    return true;

                case KeyCode.Backspace:
                    if (isTypingFilename && selectedIndex == 0)
                    {
                        filenameController.HandleBackspace();
                        return true;
                    }
                    break;
                case KeyCode.C:
                    if (ctrl && isTypingFilename && selectedIndex == 0)
                    {
                        filenameController.HandleCopy();
                        return true;
                    }
                    break;
                case KeyCode.V:
                    if (ctrl && isTypingFilename && selectedIndex == 0)
                    {
                        filenameController.HandlePaste();
                        return true;
                    }
                    break;
            }

            return false;
        }

        /// <summary>
        /// Handles character input for filename typing.
        /// </summary>
        public static bool HandleCharacterInput(char character)
        {
            if (!IsActive) return false;

            // Only allow character input when on the "Create New" option
            if (selectedIndex == 0 && isTypingFilename)
            {
                // Filter out invalid filename characters
                if (char.IsLetterOrDigit(character) || character == ' ' || character == '-' || character == '_')
                {
                    filenameController.HandleCharacter(character);
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
