using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
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
    /// Save mode has two focus zones: a text field for the new save name, and a
    /// list of existing saves available for overwriting. Load mode is list-only.
    /// Matches vanilla's Dialog_SaveFileList_Save visual layout: field at the bottom,
    /// list above it. Down arrow from the field enters the list; Up from list index 0
    /// returns to the field.
    /// </summary>
    public static class WindowlessSaveMenuState
    {
        private enum SaveFocus { TextField, List }

        private static List<SaveFileInfo> saveFiles = null;
        private static int selectedIndex = 0;
        private static bool isActive = false;
        private static SaveLoadMode currentMode = SaveLoadMode.Load;
        private static SaveFocus saveFocus = SaveFocus.TextField;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // Embedded text-input session for the save-name field. modal: false means the
        // surrounding state still owns Up/Down (file-list navigation) and the controller
        // only sees keys this state explicitly forwards to it.
        private static readonly TextInputController nameController = new TextInputController();
        private static readonly TextFieldSpec nameSpec = new TextFieldSpec(
            labelKey: "RimWorldAccess.TextInput.LabelFilename",
            maxLength: 64,
            minLength: 1,
            mustBeFilename: true);

        // When CheckVersionAndLoadGame queues a mod- or version-mismatch dialog, we
        // suspend (rather than close) this menu and wait for the dialog to resolve.
        // LoadDialogWatcherPatch notifies us via OnWindowClosed when any Window closes,
        // and we reactivate ourselves if the load didn't actually fire.
        private static System.WeakReference<Window> pendingDialog = null;

        public static bool IsActive => isActive;

        /// <summary>
        /// True when the user is typing into the save-name field. While true, the input
        /// router sends printable characters and Backspace straight to the field
        /// (bypassing typeahead) and treats Down as "enter the overwrite list".
        /// </summary>
        public static bool IsInTextField => isActive && currentMode == SaveLoadMode.Save && saveFocus == SaveFocus.TextField;

        public static bool HasActiveSearch => !IsInTextField && typeahead.HasActiveSearch;
        public static bool HasNoMatches => !IsInTextField && typeahead.HasNoMatches;

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

            if (mode == SaveLoadMode.Save)
            {
                saveFocus = SaveFocus.TextField;
                string prefilled = Faction.OfPlayer.HasName
                    ? Faction.OfPlayer.Name
                    : SaveGameFilesUtility.UnusedDefaultFileName(Faction.OfPlayer.def.LabelCap);
                nameController.Begin(prefilled, nameSpec, _ => { }, null, replaceOnType: true, modal: false);
            }
            else
            {
                saveFocus = SaveFocus.List;
                nameController.Cancel();
            }

            AnnounceCurrentState();
        }

        public static void Close()
        {
            saveFiles = null;
            selectedIndex = 0;
            isActive = false;
            nameController.Cancel();
            saveFocus = SaveFocus.TextField;
            typeahead.ClearSearch();
            pendingDialog = null;
        }

        /// <summary>
        /// Down arrow. In save-mode text field: enters the overwrite list. In the list:
        /// moves to next item (wrap-aware).
        /// </summary>
        public static void SelectNext()
        {
            if (!isActive || saveFiles == null)
                return;

            if (IsInTextField)
            {
                if (saveFiles.Count == 0)
                {
                    TolkHelper.Speak("No existing saves to overwrite.");
                    return;
                }
                saveFocus = SaveFocus.List;
                selectedIndex = 0;
                AnnounceCurrentState();
                return;
            }

            if (saveFiles.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, saveFiles.Count);
            AnnounceCurrentState();
        }

        /// <summary>
        /// Up arrow. In the list at index 0 (save mode): returns to the text field.
        /// Otherwise: moves to previous item (wrap-aware in load mode; no wrap across
        /// the field boundary in save mode).
        /// </summary>
        public static void SelectPrevious()
        {
            if (!isActive || saveFiles == null)
                return;

            if (IsInTextField)
            {
                // Already at the top of the save-mode layout. Inert.
                return;
            }

            if (saveFiles.Count == 0)
                return;

            if (currentMode == SaveLoadMode.Save && selectedIndex == 0)
            {
                saveFocus = SaveFocus.TextField;
                AnnounceCurrentState();
                return;
            }

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, saveFiles.Count);
            AnnounceCurrentState();
        }

        public static void ExecuteSelected()
        {
            if (!isActive)
                return;

            if (currentMode == SaveLoadMode.Save)
                ExecuteSave();
            else
                ExecuteLoad();
        }

        /// <summary>
        /// Delete key. Routes through a standard Dialog_MessageBox confirmation so the
        /// user gets the same dialog-box UX as other confirmations (mod mismatch,
        /// version warning). WindowlessDialogState handles it, and the shared
        /// OnWindowClosed path below reactivates the save menu when the user is done.
        /// Inert when focused on the text field (there's no file selected).
        /// </summary>
        public static void DeleteSelected()
        {
            if (!isActive || saveFiles == null || saveFiles.Count == 0)
                return;

            if (IsInTextField)
                return;

            if (selectedIndex < 0 || selectedIndex >= saveFiles.Count)
                return;

            SaveFileInfo selectedFile = saveFiles[selectedIndex];
            FileInfo fileInfo = selectedFile.FileInfo;
            string fileName = Path.GetFileNameWithoutExtension(selectedFile.FileName);

            Action confirmedAct = () =>
            {
                fileInfo.Delete();
                ReloadFiles();
                if (saveFiles.Count == 0)
                {
                    if (currentMode == SaveLoadMode.Save)
                    {
                        saveFocus = SaveFocus.TextField;
                    }
                    selectedIndex = 0;
                }
                else if (selectedIndex >= saveFiles.Count)
                {
                    selectedIndex = saveFiles.Count - 1;
                }
            };

            var msgBox = Dialog_MessageBox.CreateConfirmation(
                "ConfirmDelete".Translate(fileName),
                confirmedAct,
                destructive: true);

            isActive = false;
            pendingDialog = new System.WeakReference<Window>(msgBox);
            Find.WindowStack.Add(msgBox);
        }

        private static void ExecuteSave()
        {
            string saveName;

            if (IsInTextField)
            {
                saveName = nameController.CurrentText;
            }
            else if (saveFiles != null && selectedIndex >= 0 && selectedIndex < saveFiles.Count)
            {
                // Overwrite: use the existing file's name.
                saveName = Path.GetFileNameWithoutExtension(saveFiles[selectedIndex].FileName);
            }
            else
            {
                TolkHelper.Speak("Invalid save selection");
                return;
            }

            if (string.IsNullOrEmpty(saveName))
            {
                TolkHelper.Speak("NeedAName".Translate());
                return;
            }

            saveName = GenFile.SanitizedFileName(saveName);

            Close();

            LongEventHandler.QueueLongEvent(delegate
            {
                GameDataSaveLoader.SaveGame(saveName);
            }, "SavingLongEvent", doAsynchronously: false, null);

            Messages.Message("SavedAs".Translate(saveName), MessageTypeDefOf.SilentInput, historical: false);
            PlayerKnowledgeDatabase.Save();
            // Messages.Message is announced by the mod's general message handler, so
            // no extra TolkHelper.Speak here — that would double-announce the save.
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

            // Suspend — don't Close — so we can reactivate if the user dismisses
            // a compatibility dialog without loading. Snapshot the window stack so
            // we can identify any dialog CheckVersionAndLoadGame adds.
            var beforeWindows = new HashSet<Window>(Find.WindowStack.Windows);
            isActive = false;
            pendingDialog = null;

            GameDataSaveLoader.CheckVersionAndLoadGame(fileName);

            Window added = null;
            foreach (Window w in Find.WindowStack.Windows)
            {
                if (!beforeWindows.Contains(w) && (w is Dialog_ModMismatch || w is Dialog_MessageBox))
                {
                    added = w;
                    break;
                }
            }

            // Swap Dialog_ModMismatch for a Dialog_MessageBox so it flows through our
            // existing MessageBoxAccessibilityPatch — which gives the user a real
            // dialog-box UX (title + message + Enter/Escape announcements confirming
            // the button taken) instead of a one-shot announcement plus a silent
            // vanilla window they can't see. Version-mismatch Dialog_MessageBox
            // instances are already handled natively by that patch.
            if (added is Dialog_ModMismatch modMismatch)
            {
                added = ReplaceModMismatchDialog(modMismatch);
            }

            if (added != null)
            {
                pendingDialog = new System.WeakReference<Window>(added);
            }
            else
            {
                // No compat dialog queued — load is in flight. Clean up fully.
                Close();
            }
        }

        private static Window ReplaceModMismatchDialog(Dialog_ModMismatch original)
        {
            Action loadAction = AccessTools.Field(typeof(Dialog_ModMismatch), "loadAction").GetValue(original) as Action;
            var addedMods = AccessTools.Field(typeof(Dialog_ModMismatch), "addedModsList").GetValue(original) as List<string>;
            var missingMods = AccessTools.Field(typeof(Dialog_ModMismatch), "missingModsList").GetValue(original) as List<string>;

            bool anyAdded = addedMods != null && addedMods.Count > 0;
            bool anyMissing = missingMods != null && missingMods.Count > 0;

            var parts = new List<string> { "ModsMismatchWarningText".Translate().ToString() };
            if (!anyAdded && !anyMissing)
            {
                parts.Add("ModsMismatchOrderChanged".Translate().ToString());
            }
            else
            {
                if (anyAdded)
                {
                    parts.Add($"{"AddedModsList".Translate()}: {string.Join(", ", addedMods)}.");
                }
                if (anyMissing)
                {
                    parts.Add($"{"MissingModsList".Translate()}: {string.Join(", ", missingMods)}.");
                }
            }
            string message = string.Join(" ", parts);

            Find.WindowStack.TryRemove(original, doCloseSound: false);

            var msgBox = Dialog_MessageBox.CreateConfirmation(
                message,
                loadAction,
                destructive: false,
                title: "ModsMismatchWarningTitle".Translate());
            msgBox.buttonAText = "LoadAnyway".Translate();

            Find.WindowStack.Add(msgBox);
            return msgBox;
        }

        /// <summary>
        /// Called from LoadDialogWatcherPatch whenever a Window closes. If it matches
        /// the compat dialog we were waiting on, either reactivate the menu (user
        /// dismissed without loading) or fully close (load was actually queued).
        /// </summary>
        internal static void OnWindowClosed(Window closedWindow)
        {
            if (pendingDialog == null || closedWindow == null)
                return;

            if (!pendingDialog.TryGetTarget(out Window tracked) || tracked != closedWindow)
                return;

            pendingDialog = null;

            if (LongEventHandler.AnyEventNowOrWaiting)
            {
                // Load was queued (user accepted the compat warning). Let the loading
                // screen take over — don't re-announce our menu.
                Close();
                return;
            }

            // User backed out without loading. Resume the menu where we left off.
            // High priority so the announcement reliably interrupts any queued speech
            // from the dialog that just closed.
            isActive = true;
            TolkHelper.Speak(GetCurrentItemDescription(), SpeechPriority.High);
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

            saveFiles = saveFiles.OrderByDescending(f => f.LastWriteTime).ToList();

            // Mirror vanilla: populate version metadata on a background task so the
            // menu doesn't block while reading save headers. SaveFileInfo.GameVersion
            // reports "LoadingVersionInfo" until the per-file lock releases.
            var snapshot = saveFiles;
            Task.Run(() =>
            {
                Parallel.ForEach(snapshot, file =>
                {
                    try
                    {
                        file.LoadData();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Exception loading save header for {file.FileName}: {ex}");
                    }
                });
            });
        }

        private static void AnnounceCurrentState()
        {
            TolkHelper.Speak(GetCurrentItemDescription());
        }

        public static void GoBack()
        {
            Close();

            if (Current.ProgramState == ProgramState.Playing)
            {
                WindowlessPauseMenuState.Open();
            }
            else
            {
                // Returning to the main menu. Re-announce the menu item that brought
                // us here so the user knows where the cursor is — otherwise Escape
                // lands in silence on a screen with no visual change.
                var selected = MenuNavigationState.GetCurrentSelection();
                if (selected != null)
                {
                    TolkHelper.Speak(selected.label);
                }
            }
        }

        public static void JumpToFirst()
        {
            if (!isActive || saveFiles == null)
                return;

            if (IsInTextField)
                return;

            if (saveFiles.Count == 0)
                return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int first = typeahead.GetFirstMatch();
                if (first >= 0)
                {
                    selectedIndex = first;
                    AnnounceWithSearch();
                }
                return;
            }

            selectedIndex = MenuHelper.JumpToFirst();
            typeahead.ClearSearch();
            AnnounceCurrentState();
        }

        public static void JumpToLast()
        {
            if (!isActive || saveFiles == null)
                return;

            if (IsInTextField)
                return;

            if (saveFiles.Count == 0)
                return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int last = typeahead.GetLastMatch();
                if (last >= 0)
                {
                    selectedIndex = last;
                    AnnounceWithSearch();
                }
                return;
            }

            selectedIndex = MenuHelper.JumpToLast(saveFiles.Count);
            typeahead.ClearSearch();
            AnnounceCurrentState();
        }

        /// <summary>
        /// Layout-aware character entry; forwarded by the typeahead dispatcher when
        /// the field has focus. Controller validates against <see cref="nameSpec"/>
        /// (filename rules, max length) and arms replaceOnType so the first keystroke
        /// replaces the prefilled faction name.
        /// </summary>
        public static void AppendChar(char c)
        {
            if (!IsInTextField) return;
            nameController.HandleCharacter(c);
        }

        /// <summary>Backspace inside the text field — delegates to the controller.</summary>
        public static void BackspaceInField()
        {
            if (!IsInTextField) return;
            nameController.HandleBackspace();
        }

        /// <summary>Left-arrow cursor review while in the text field.</summary>
        public static void HandleArrowLeftInField(bool shift, bool ctrl)
        {
            if (!IsInTextField) return;
            nameController.HandleArrowLeft(shift, ctrl);
        }

        /// <summary>Right-arrow cursor review while in the text field.</summary>
        public static void HandleArrowRightInField(bool shift, bool ctrl)
        {
            if (!IsInTextField) return;
            nameController.HandleArrowRight(shift, ctrl);
        }

        /// <summary>Ctrl+C copy from the field's selection.</summary>
        public static void HandleCopyInField()
        {
            if (!IsInTextField) return;
            nameController.HandleCopy();
        }

        /// <summary>Ctrl+X cut from the field's selection.</summary>
        public static void HandleCutInField()
        {
            if (!IsInTextField) return;
            nameController.HandleCut();
        }

        /// <summary>Ctrl+V paste at the field's cursor.</summary>
        public static void HandlePasteInField()
        {
            if (!IsInTextField) return;
            nameController.HandlePaste();
        }

        /// <summary>Ctrl+A select all in the field.</summary>
        public static void HandleSelectAllInField()
        {
            if (!IsInTextField) return;
            nameController.HandleSelectAll();
        }

        public static bool ClearTypeaheadSearch()
        {
            if (IsInTextField)
                return false;
            return typeahead.ClearSearchAndAnnounce();
        }

        /// <summary>
        /// Routes the layout-aware character from <see cref="TypeaheadDispatcher"/>
        /// to either the save-name field or the file-list typeahead, depending on
        /// which focus zone is active. Modifier-held chars are passed through to
        /// global shortcuts (Alt+M, Ctrl+S, etc.) by ignoring them here.
        /// </summary>
        public static void HandleCharacter(char c)
        {
            if (!isActive)
                return;

            if (KeyboardHelper.IsAltHeld || KeyboardHelper.IsCtrlHeld)
                return;

            if (IsInTextField)
            {
                AppendChar(c);
            }
            else
            {
                ProcessTypeaheadCharacter(c);
            }
        }

        public static void ProcessTypeaheadCharacter(char c)
        {
            if (!isActive || saveFiles == null || IsInTextField)
                return;

            var labels = GetItemLabels();
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
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        public static void ProcessBackspace()
        {
            if (!isActive || saveFiles == null || IsInTextField || !typeahead.HasActiveSearch)
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

        public static void SelectNextMatch()
        {
            if (!isActive || saveFiles == null)
                return;

            if (!IsInTextField && typeahead.HasActiveSearch && !typeahead.HasNoMatches)
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

        public static void SelectPreviousMatch()
        {
            if (!isActive || saveFiles == null)
                return;

            if (!IsInTextField && typeahead.HasActiveSearch && !typeahead.HasNoMatches)
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

        private static List<string> GetItemLabels()
        {
            var labels = new List<string>();
            if (saveFiles == null)
                return labels;

            foreach (var file in saveFiles)
            {
                labels.Add(Path.GetFileNameWithoutExtension(file.FileName) ?? "");
            }
            return labels;
        }

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

        private static string GetCurrentItemDescription()
        {
            if (IsInTextField)
            {
                return $"{"SaveGameButton".Translate()}: {nameController.CurrentText}. Type a new name, Down arrow for existing saves, Enter to save.";
            }

            if (saveFiles == null || saveFiles.Count == 0)
            {
                return "No save files available";
            }

            if (selectedIndex < 0 || selectedIndex >= saveFiles.Count)
            {
                return "No save files available";
            }

            SaveFileInfo file = saveFiles[selectedIndex];
            string fileName = Path.GetFileNameWithoutExtension(file.FileName);
            string prefix = currentMode == SaveLoadMode.Save
                ? "OverwriteButton".Translate().ToString()
                : "LoadGameButton".Translate().ToString();

            var parts = new List<string> { $"{prefix}: {fileName}" };

            if (SaveGameFilesUtility.IsAutoSave(fileName))
            {
                parts.Add("autosave");
            }

            parts.Add(FormatDateTime(file.LastWriteTime));

            string version = file.GameVersion;
            if (!string.IsNullOrEmpty(version) && version != "???" && version != "LoadingVersionInfo".Translate().ToString())
            {
                parts.Add($"version {version}");
            }

            parts.Add(MenuHelper.FormatPosition(selectedIndex, saveFiles.Count));

            return string.Join(" - ", parts.Where(p => !string.IsNullOrEmpty(p)));
        }

        private static string FormatDateTime(DateTime dateTime)
        {
            if (Prefs.TwelveHourClockMode)
            {
                return dateTime.ToString("yyyy-MM-dd h:mm tt");
            }
            return dateTime.ToString("yyyy-MM-dd HH:mm");
        }
    }

}
