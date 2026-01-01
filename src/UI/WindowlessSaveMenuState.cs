using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Mode for save menu - either saving or loading.
    /// </summary>
    public enum SaveLoadMode
    {
        Save,
        Load
    }

    /// <summary>
    /// Manages a windowless save/load file browser.
    /// Provides keyboard navigation through save files without rendering UI.
    /// </summary>
    public static class WindowlessSaveMenuState
    {
        private static List<SaveFileInfo> saveFiles = null;
        private static int selectedIndex = 0;
        private static bool isActive = false;
        private static SaveLoadMode currentMode = SaveLoadMode.Load;
        private static string typedSaveName = "";
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => typeahead.HasActiveSearch;
        public static bool HasNoMatches => typeahead.HasNoMatches;

        /// <summary>
        /// Opens the save/load menu.
        /// </summary>
        public static void Open(SaveLoadMode mode)
        {
            currentMode = mode;
            ReloadFiles();
            selectedIndex = 0;
            isActive = true;
            typeahead.ClearSearch();

            // For save mode, initialize with default name
            if (mode == SaveLoadMode.Save)
            {
                if (Faction.OfPlayer.HasName)
                {
                    typedSaveName = Faction.OfPlayer.Name;
                }
                else
                {
                    typedSaveName = SaveGameFilesUtility.UnusedDefaultFileName(Faction.OfPlayer.def.LabelCap);
                }
            }

            // Announce first file or save mode
            AnnounceCurrentState();
        }

        /// <summary>
        /// Closes the save/load menu.
        /// </summary>
        public static void Close()
        {
            saveFiles = null;
            selectedIndex = 0;
            isActive = false;
            typedSaveName = "";
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Moves selection to next file.
        /// </summary>
        public static void SelectNext()
        {
            if (saveFiles == null)
                return;

            // In save mode, we have "Create New Save" at index 0, then existing files at indices 1+
            // In load mode, we only have existing files starting at index 0
            int maxIndex = currentMode == SaveLoadMode.Save ? saveFiles.Count : saveFiles.Count - 1;

            if (maxIndex < 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, maxIndex + 1);
            AnnounceCurrentState();
        }

        /// <summary>
        /// Moves selection to previous file.
        /// </summary>
        public static void SelectPrevious()
        {
            if (saveFiles == null)
                return;

            // In save mode, we have "Create New Save" at index 0, then existing files at indices 1+
            // In load mode, we only have existing files starting at index 0
            int maxIndex = currentMode == SaveLoadMode.Save ? saveFiles.Count : saveFiles.Count - 1;

            if (maxIndex < 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, maxIndex + 1);
            AnnounceCurrentState();
        }

        /// <summary>
        /// Executes save or load on the selected file.
        /// </summary>
        public static void ExecuteSelected()
        {
            if (currentMode == SaveLoadMode.Save)
            {
                ExecuteSave();
            }
            else
            {
                ExecuteLoad();
            }
        }

        /// <summary>
        /// Deletes the currently selected save file.
        /// </summary>
        public static void DeleteSelected()
        {
            if (saveFiles == null || saveFiles.Count == 0)
                return;

            // In save mode, index 0 is "Create New Save" which can't be deleted
            if (currentMode == SaveLoadMode.Save && selectedIndex == 0)
            {
                TolkHelper.Speak("Cannot delete 'Create New Save' option", SpeechPriority.High);
                return;
            }

            // Adjust index for save mode (where index 0 is "Create New Save")
            int fileIndex = currentMode == SaveLoadMode.Save ? selectedIndex - 1 : selectedIndex;

            if (fileIndex < 0 || fileIndex >= saveFiles.Count)
                return;

            SaveFileInfo selectedFile = saveFiles[fileIndex];
            string fileName = Path.GetFileNameWithoutExtension(selectedFile.FileName);

            // Open confirmation
            TolkHelper.Speak($"Delete {fileName}? Press Enter to confirm, Escape to cancel");
            WindowlessDeleteConfirmationState.Open(selectedFile.FileInfo, () => {
                // After deletion, reload and reopen this menu
                ReloadFiles();

                // Adjust selected index after deletion
                int maxIndex = currentMode == SaveLoadMode.Save ? saveFiles.Count : saveFiles.Count - 1;
                if (selectedIndex > maxIndex)
                {
                    selectedIndex = Math.Max(0, maxIndex);
                }

                isActive = true; // Reactivate this menu
                AnnounceCurrentState();
            });

            // Temporarily deactivate while confirmation is active
            isActive = false;
        }

        private static void ExecuteSave()
        {
            string saveName;

            // Check if we're on "Create New Save" option (index 0) or an existing save file
            if (selectedIndex == 0)
            {
                // Create new save with typed name
                saveName = typedSaveName;
            }
            else if (saveFiles != null && selectedIndex > 0 && selectedIndex <= saveFiles.Count)
            {
                // Overwrite existing save file
                SaveFileInfo selectedFile = saveFiles[selectedIndex - 1]; // Adjust for "Create New Save" at index 0
                saveName = Path.GetFileNameWithoutExtension(selectedFile.FileName);
            }
            else
            {
                TolkHelper.Speak("Invalid save selection");
                return;
            }

            if (string.IsNullOrEmpty(saveName))
            {
                TolkHelper.Speak("Need a name for the save file");
                return;
            }

            saveName = GenFile.SanitizedFileName(saveName);

            // Close menu before saving
            Close();

            // Perform the save
            LongEventHandler.QueueLongEvent(delegate
            {
                GameDataSaveLoader.SaveGame(saveName);
            }, "SavingLongEvent", doAsynchronously: false, null);

            Messages.Message("SavedAs".Translate(saveName), MessageTypeDefOf.SilentInput, historical: false);
            PlayerKnowledgeDatabase.Save();

            TolkHelper.Speak($"Saved as {saveName}");
        }

        private static void ExecuteLoad()
        {
            if (saveFiles == null || saveFiles.Count == 0)
            {
                TolkHelper.Speak("No save files available");
                return;
            }

            if (selectedIndex < 0 || selectedIndex >= saveFiles.Count)
                return;

            SaveFileInfo selectedFile = saveFiles[selectedIndex];
            string fileName = Path.GetFileNameWithoutExtension(selectedFile.FileName);

            // Close menu before loading
            Close();

            // Perform the load
            LongEventHandler.QueueLongEvent(delegate
            {
                GameDataSaveLoader.LoadGame(fileName);
            }, "LoadingLongEvent", doAsynchronously: true, GameAndMapInitExceptionHandlers.ErrorWhileLoadingGame);

            TolkHelper.Speak($"Loading {fileName}");
        }

        private static void ReloadFiles()
        {
            saveFiles = new List<SaveFileInfo>();

            foreach (FileInfo file in GenFilePaths.AllSavedGameFiles)
            {
                try
                {
                    saveFiles.Add(new SaveFileInfo(file));
                }
                catch (Exception ex)
                {
                    Log.Error($"Exception loading save file {file.Name}: {ex}");
                }
            }

            // Sort by last write time, most recent first
            saveFiles = saveFiles.OrderByDescending(f => f.LastWriteTime).ToList();
        }

        private static void AnnounceCurrentState()
        {
            if (currentMode == SaveLoadMode.Save)
            {
                // In save mode, we have "Create New Save" at index 0 (virtual entry), then existing files
                int totalCount = saveFiles != null ? saveFiles.Count + 1 : 1;

                // Index 0 is "Create New Save", indices 1+ are existing files
                if (selectedIndex == 0)
                {
                    TolkHelper.Speak($"Create New Save: {typedSaveName}. {MenuHelper.FormatPosition(selectedIndex, totalCount)}");
                }
                else if (saveFiles != null && selectedIndex > 0 && selectedIndex <= saveFiles.Count)
                {
                    SaveFileInfo file = saveFiles[selectedIndex - 1]; // Adjust for "Create New Save" at index 0
                    string fileName = Path.GetFileNameWithoutExtension(file.FileName);
                    TolkHelper.Speak($"Overwrite: {fileName} - {file.LastWriteTime:yyyy-MM-dd HH:mm}. {MenuHelper.FormatPosition(selectedIndex, totalCount)}");
                }
                else
                {
                    TolkHelper.Speak($"Create New Save: {typedSaveName}. {MenuHelper.FormatPosition(selectedIndex, totalCount)}");
                }
            }
            else // Load mode
            {
                if (saveFiles != null && saveFiles.Count > 0 && selectedIndex >= 0 && selectedIndex < saveFiles.Count)
                {
                    SaveFileInfo file = saveFiles[selectedIndex];
                    string fileName = Path.GetFileNameWithoutExtension(file.FileName);
                    TolkHelper.Speak($"Load: {fileName} - {file.LastWriteTime:yyyy-MM-dd HH:mm}. {MenuHelper.FormatPosition(selectedIndex, saveFiles.Count)}");
                }
                else
                {
                    TolkHelper.Speak("No save files available");
                }
            }
        }

        /// <summary>
        /// Goes back to the pause menu.
        /// </summary>
        public static void GoBack()
        {
            Close();
            WindowlessPauseMenuState.Open();
        }

        /// <summary>
        /// Jumps to the first item in the list.
        /// </summary>
        public static void JumpToFirst()
        {
            if (saveFiles == null)
                return;

            selectedIndex = MenuHelper.JumpToFirst();
            typeahead.ClearSearch();
            AnnounceCurrentState();
        }

        /// <summary>
        /// Jumps to the last item in the list.
        /// </summary>
        public static void JumpToLast()
        {
            if (saveFiles == null)
                return;

            int maxIndex = currentMode == SaveLoadMode.Save ? saveFiles.Count : saveFiles.Count - 1;
            if (maxIndex < 0)
                return;

            selectedIndex = MenuHelper.JumpToLast(maxIndex + 1);
            typeahead.ClearSearch();
            AnnounceCurrentState();
        }

        /// <summary>
        /// Clears the typeahead search and announces.
        /// Returns true if there was an active search to clear.
        /// </summary>
        public static bool ClearTypeaheadSearch()
        {
            return typeahead.ClearSearchAndAnnounce();
        }

        /// <summary>
        /// Processes a typeahead character input.
        /// </summary>
        public static void ProcessTypeaheadCharacter(char c)
        {
            if (!isActive || saveFiles == null)
                return;

            var labels = GetItemLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    // In save mode, newIndex is already correct (0 = Create New Save, 1+ = files)
                    // In load mode, newIndex maps directly to save files
                    selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        /// <summary>
        /// Processes backspace for typeahead search.
        /// </summary>
        public static void ProcessBackspace()
        {
            if (!isActive || saveFiles == null || !typeahead.HasActiveSearch)
                return;

            var labels = GetItemLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                }
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Navigates to the next match in typeahead search, or next item if no search.
        /// </summary>
        public static void SelectNextMatch()
        {
            if (saveFiles == null)
                return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int nextIndex = typeahead.GetNextMatch(selectedIndex);
                if (nextIndex >= 0)
                {
                    selectedIndex = nextIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                SelectNext();
            }
        }

        /// <summary>
        /// Navigates to the previous match in typeahead search, or previous item if no search.
        /// </summary>
        public static void SelectPreviousMatch()
        {
            if (saveFiles == null)
                return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int prevIndex = typeahead.GetPreviousMatch(selectedIndex);
                if (prevIndex >= 0)
                {
                    selectedIndex = prevIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                SelectPrevious();
            }
        }

        /// <summary>
        /// Gets the list of labels for typeahead matching.
        /// In save mode, index 0 is "Create New Save", indices 1+ are file names.
        /// In load mode, all indices are file names.
        /// </summary>
        private static List<string> GetItemLabels()
        {
            var labels = new List<string>();
            if (saveFiles == null)
                return labels;

            if (currentMode == SaveLoadMode.Save)
            {
                // Add "Create New Save" as the first entry
                labels.Add(typedSaveName);
            }

            foreach (var file in saveFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(file.FileName);
                labels.Add(fileName ?? "");
            }

            return labels;
        }

        /// <summary>
        /// Announces the current selection with search context if applicable.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (saveFiles == null)
                return;

            string baseMessage = GetCurrentItemDescription();

            if (typeahead.HasActiveSearch)
            {
                TolkHelper.Speak($"{baseMessage}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
            }
            else
            {
                TolkHelper.Speak(baseMessage);
            }
        }

        /// <summary>
        /// Gets a description of the currently selected item.
        /// </summary>
        private static string GetCurrentItemDescription()
        {
            if (currentMode == SaveLoadMode.Save)
            {
                int totalCount = saveFiles != null ? saveFiles.Count + 1 : 1;

                if (selectedIndex == 0)
                {
                    return $"Create New Save: {typedSaveName}. {MenuHelper.FormatPosition(selectedIndex, totalCount)}";
                }
                else if (saveFiles != null && selectedIndex > 0 && selectedIndex <= saveFiles.Count)
                {
                    SaveFileInfo file = saveFiles[selectedIndex - 1];
                    string fileName = Path.GetFileNameWithoutExtension(file.FileName);
                    return $"Overwrite: {fileName} - {file.LastWriteTime:yyyy-MM-dd HH:mm}. {MenuHelper.FormatPosition(selectedIndex, totalCount)}";
                }
                else
                {
                    return $"Create New Save: {typedSaveName}. {MenuHelper.FormatPosition(selectedIndex, totalCount)}";
                }
            }
            else // Load mode
            {
                if (saveFiles != null && saveFiles.Count > 0 && selectedIndex >= 0 && selectedIndex < saveFiles.Count)
                {
                    SaveFileInfo file = saveFiles[selectedIndex];
                    string fileName = Path.GetFileNameWithoutExtension(file.FileName);
                    return $"Load: {fileName} - {file.LastWriteTime:yyyy-MM-dd HH:mm}. {MenuHelper.FormatPosition(selectedIndex, saveFiles.Count)}";
                }
                else
                {
                    return "No save files available";
                }
            }
        }
    }

    /// <summary>
    /// Handles confirmation for deleting save files.
    /// </summary>
    public static class WindowlessDeleteConfirmationState
    {
        private static bool isActive = false;
        private static FileInfo fileToDelete = null;
        private static Action onDeleteComplete = null;

        public static bool IsActive => isActive;

        public static void Open(FileInfo file, Action onComplete)
        {
            isActive = true;
            fileToDelete = file;
            onDeleteComplete = onComplete;
        }

        public static void Confirm()
        {
            if (!isActive || fileToDelete == null)
                return;

            string fileName = fileToDelete.Name;
            fileToDelete.Delete();
            TolkHelper.Speak($"Deleted {fileName}");

            Action callback = onDeleteComplete;
            Close();
            callback?.Invoke();
        }

        public static void Cancel()
        {
            if (!isActive)
                return;

            TolkHelper.Speak("Delete cancelled");

            Action callback = onDeleteComplete;
            Close();
            callback?.Invoke();
        }

        private static void Close()
        {
            isActive = false;
            fileToDelete = null;
            onDeleteComplete = null;
        }
    }
}
