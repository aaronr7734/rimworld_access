using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.Steam;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Which column in the mod list is currently focused.
    /// </summary>
    public enum ModListColumn
    {
        Inactive,
        Active
    }

    /// <summary>
    /// Manages keyboard navigation state for the Page_ModsConfig dialog.
    /// Provides accessibility for the mod list/manager interface.
    /// </summary>
    public static class ModListState
    {
        private static bool isActive = false;
        private static ModListColumn currentColumn = ModListColumn.Active;
        private static int selectedIndex = 0;
        private static Page_ModsConfig currentPage = null;

        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // Cached reflection fields
        private static FieldInfo primarySelectedModField;
        private static FieldInfo activeModListField;
        private static FieldInfo inactiveModListField;
        private static FieldInfo filteredActiveModListField;
        private static FieldInfo filteredInactiveModListField;
        private static FieldInfo lastSelectedIndexField;
        private static FieldInfo modListsDirtyField;
        private static FieldInfo primaryModHandleField;
        private static FieldInfo modWarningsCachedField;
        private static MethodInfo trySetModActiveMethod;
        private static MethodInfo trySetModInactiveMethod;
        private static MethodInfo recacheSelectedModInfoMethod;
        private static MethodInfo selectModMethod;

        public static bool IsActive => isActive;
        public static ModListColumn CurrentColumn => currentColumn;

        static ModListState()
        {
            // Cache reflection info
            var pageType = typeof(Page_ModsConfig);
            var bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
            var staticFlags = BindingFlags.NonPublic | BindingFlags.Static;

            primarySelectedModField = pageType.GetField("primarySelectedMod", bindingFlags);
            lastSelectedIndexField = pageType.GetField("lastSelectedIndex", bindingFlags);
            modListsDirtyField = pageType.GetField("modListsDirty", bindingFlags);
            primaryModHandleField = pageType.GetField("primaryModHandle", bindingFlags);

            activeModListField = pageType.GetField("activeModListOrderCached", staticFlags);
            inactiveModListField = pageType.GetField("inactiveModListOrderCached", staticFlags);
            filteredActiveModListField = pageType.GetField("filteredActiveModListOrderCached", staticFlags);
            filteredInactiveModListField = pageType.GetField("filteredInactiveModListOrderCached", staticFlags);
            modWarningsCachedField = pageType.GetField("modWarningsCached", staticFlags);

            trySetModActiveMethod = pageType.GetMethod("TrySetModActive", bindingFlags);
            trySetModInactiveMethod = pageType.GetMethod("TrySetModInactive", bindingFlags);
            recacheSelectedModInfoMethod = pageType.GetMethod("RecacheSelectedModInfo", bindingFlags);
            selectModMethod = pageType.GetMethod("SelectMod", BindingFlags.Public | BindingFlags.Instance);
        }

        /// <summary>
        /// Opens the mod list state when Page_ModsConfig is opened.
        /// </summary>
        public static void Open(Page_ModsConfig page)
        {
            currentPage = page;
            isActive = true;
            currentColumn = ModListColumn.Active;
            selectedIndex = 0;

            typeahead.ClearSearch();

            // Get the active list and announce
            var activeList = GetCurrentList();
            if (activeList != null && activeList.Count > 0)
            {
                // Sync with the page's selection
                var primaryMod = GetPrimarySelectedMod();
                if (primaryMod != null)
                {
                    currentColumn = primaryMod.Active ? ModListColumn.Active : ModListColumn.Inactive;
                    selectedIndex = activeList.IndexOf(primaryMod);
                    if (selectedIndex < 0) selectedIndex = 0;
                }

                AnnounceCurrentState();
            }
            else
            {
                TolkHelper.Speak("Mod manager opened. No mods in active list.");
            }
        }

        /// <summary>
        /// Closes the mod list state when Page_ModsConfig is closed.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentPage = null;
            selectedIndex = 0;
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Moves selection to the next mod in the current list.
        /// </summary>
        public static void SelectNext()
        {
            var list = GetCurrentList();
            if (list == null || list.Count == 0) return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, list.Count);
            SyncSelection();
            AnnounceCurrentMod();
        }

        /// <summary>
        /// Moves selection to the previous mod in the current list.
        /// </summary>
        public static void SelectPrevious()
        {
            var list = GetCurrentList();
            if (list == null || list.Count == 0) return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, list.Count);
            SyncSelection();
            AnnounceCurrentMod();
        }

        /// <summary>
        /// Switches between active and inactive mod columns.
        /// </summary>
        public static void SwitchColumn()
        {
            currentColumn = (currentColumn == ModListColumn.Active) ? ModListColumn.Inactive : ModListColumn.Active;
            selectedIndex = 0;
            typeahead.ClearSearch();

            var list = GetCurrentList();
            if (list != null && list.Count > 0)
            {
                SyncSelection();
                AnnounceColumnSwitch();
            }
            else
            {
                string columnName = currentColumn == ModListColumn.Active ? "Active" : "Inactive";
                TolkHelper.Speak($"{columnName} mods list, empty");
            }
        }

        /// <summary>
        /// Toggles the enable/disable state of the selected mod.
        /// </summary>
        public static void ToggleSelected()
        {
            if (currentPage == null) return;

            var list = GetCurrentList();
            if (list == null || list.Count == 0 || selectedIndex >= list.Count) return;

            var mod = list[selectedIndex];
            bool wasActive = mod.Active;

            if (wasActive)
            {
                // Disable the mod
                trySetModInactiveMethod?.Invoke(currentPage, new object[] { mod });
            }
            else
            {
                // Enable the mod
                var result = trySetModActiveMethod?.Invoke(currentPage, new object[] { mod });
                if (result is bool success && !success)
                {
                    TolkHelper.Speak("Could not enable mod. A mod with the same package ID may already be active.", SpeechPriority.High);
                    return;
                }
            }

            // Mark lists as dirty to refresh
            modListsDirtyField?.SetValue(currentPage, true);

            // Switch to the other column and find the mod
            currentColumn = wasActive ? ModListColumn.Inactive : ModListColumn.Active;

            // Wait a frame for the lists to refresh, then find the mod
            var newList = GetCurrentList();
            if (newList != null)
            {
                int newIndex = newList.IndexOf(mod);
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                }
                else
                {
                    selectedIndex = 0;
                }
            }

            SyncSelection();

            string action = wasActive ? "disabled" : "enabled";
            TolkHelper.Speak($"{mod.Name} {action}");
        }

        /// <summary>
        /// Moves the selected mod up in the load order (active list only).
        /// </summary>
        public static void MoveUp()
        {
            if (currentColumn != ModListColumn.Active)
            {
                TolkHelper.Speak("Can only reorder active mods");
                return;
            }

            var list = GetCurrentList();
            if (list == null || list.Count == 0 || selectedIndex <= 0) return;

            var mod = list[selectedIndex];
            int currentPos = GetModLoadOrderIndex(mod);

            if (currentPos <= 0)
            {
                TolkHelper.Speak("Already at top of load order");
                return;
            }

            // Use ModsConfig.TryReorder
            if (ModsConfig.TryReorder(currentPos, currentPos - 1, out string errorMessage))
            {
                modListsDirtyField?.SetValue(currentPage, true);
                selectedIndex--;
                SyncSelection();
                TolkHelper.Speak($"{mod.Name} moved to position {selectedIndex + 1}");
            }
            else if (!string.IsNullOrEmpty(errorMessage))
            {
                TolkHelper.Speak($"Cannot reorder: {errorMessage}", SpeechPriority.High);
            }
        }

        /// <summary>
        /// Moves the selected mod down in the load order (active list only).
        /// </summary>
        public static void MoveDown()
        {
            if (currentColumn != ModListColumn.Active)
            {
                TolkHelper.Speak("Can only reorder active mods");
                return;
            }

            var list = GetCurrentList();
            if (list == null || list.Count == 0 || selectedIndex >= list.Count - 1) return;

            var mod = list[selectedIndex];
            int currentPos = GetModLoadOrderIndex(mod);
            int maxPos = ModsConfig.ActiveModsInLoadOrder.Count() - 1;

            if (currentPos >= maxPos)
            {
                TolkHelper.Speak("Already at bottom of load order");
                return;
            }

            // Use ModsConfig.TryReorder
            if (ModsConfig.TryReorder(currentPos, currentPos + 2, out string errorMessage))
            {
                modListsDirtyField?.SetValue(currentPage, true);
                selectedIndex++;
                SyncSelection();
                TolkHelper.Speak($"{mod.Name} moved to position {selectedIndex + 1}");
            }
            else if (!string.IsNullOrEmpty(errorMessage))
            {
                TolkHelper.Speak($"Cannot reorder: {errorMessage}", SpeechPriority.High);
            }
        }

        /// <summary>
        /// Opens the mod settings dialog for the selected mod (if available).
        /// </summary>
        public static void OpenSettings()
        {
            if (currentPage == null) return;

            var modHandle = primaryModHandleField?.GetValue(currentPage) as Mod;
            if (modHandle != null && !modHandle.SettingsCategory().NullOrEmpty())
            {
                Find.WindowStack.Add(new Dialog_ModSettings(modHandle));
                TolkHelper.Speak($"Opening settings for {modHandle.SettingsCategory()}");
            }
            else
            {
                TolkHelper.Speak("This mod has no settings");
            }
        }

        /// <summary>
        /// Saves mod changes and marks the page for saving on close.
        /// </summary>
        public static void SaveChanges()
        {
            if (currentPage == null) return;

            // Set saveChanges field to true
            var saveChangesField = typeof(Page_ModsConfig).GetField("saveChanges", BindingFlags.NonPublic | BindingFlags.Instance);
            saveChangesField?.SetValue(currentPage, true);

            // Also save immediately
            ModsConfig.Save();

            TolkHelper.Speak("Mod changes saved");
        }

        /// <summary>
        /// Automatically sorts mods to resolve load order issues.
        /// </summary>
        public static void AutoSortMods()
        {
            if (currentPage == null) return;

            ModsConfig.TrySortMods();
            modListsDirtyField?.SetValue(currentPage, true);

            // Reset selection to first item
            selectedIndex = 0;
            SyncSelection();

            TolkHelper.Speak("Mods automatically sorted");
        }

        /// <summary>
        /// Opens the folder containing the selected mod.
        /// </summary>
        public static void OpenModFolder()
        {
            var mod = GetSelectedMod();
            if (mod == null) return;

            if (mod.Source == ContentSource.SteamWorkshop)
            {
                // For Steam Workshop mods, open the workshop content folder
                string workshopPath = mod.RootDir.FullName;
                Application.OpenURL(workshopPath);
                TolkHelper.Speak($"Opening folder for {mod.Name}");
            }
            else if (mod.Source == ContentSource.ModsFolder)
            {
                // For local mods, open the mods folder
                string modPath = GenFilePaths.ModsFolderPath + "/" + mod.FolderName;
                Application.OpenURL(modPath);
                TolkHelper.Speak($"Opening folder for {mod.Name}");
            }
            else
            {
                TolkHelper.Speak("Cannot open folder for this mod");
            }
        }

        /// <summary>
        /// Opens the Steam Workshop page for the selected mod.
        /// </summary>
        public static void OpenWorkshopPage()
        {
            var mod = GetSelectedMod();
            if (mod == null) return;

            if (mod.Source == ContentSource.SteamWorkshop || mod.OnSteamWorkshop)
            {
                SteamUtility.OpenWorkshopPage(mod.GetPublishedFileId());
                TolkHelper.Speak($"Opening Workshop page for {mod.Name}");
            }
            else
            {
                TolkHelper.Speak("This mod is not on Steam Workshop");
            }
        }

        /// <summary>
        /// Uploads the selected mod to Steam Workshop (requires Dev Mode).
        /// </summary>
        public static void UploadToWorkshop()
        {
            var mod = GetSelectedMod();
            if (mod == null) return;

            if (!Prefs.DevMode)
            {
                TolkHelper.Speak("Upload to Workshop requires Dev Mode to be enabled");
                return;
            }

            if (!SteamManager.Initialized)
            {
                TolkHelper.Speak("Steam is not initialized");
                return;
            }

            if (!mod.CanToUploadToWorkshop())
            {
                TolkHelper.Speak("This mod cannot be uploaded to Workshop");
                return;
            }

            // Workshop.Upload is internal, so use reflection
            var uploadMethod = typeof(Workshop).GetMethod("Upload", BindingFlags.NonPublic | BindingFlags.Static);
            if (uploadMethod == null)
            {
                TolkHelper.Speak("Cannot access Workshop upload functionality");
                return;
            }

            // Create confirmation dialog
            string uploadLabel = Workshop.UploadButtonLabel(mod.GetPublishedFileId());
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                $"Upload {mod.Name} to Steam Workshop?",
                () => uploadMethod.Invoke(null, new object[] { mod }),
                destructive: false
            ));
            TolkHelper.Speak($"Confirm upload of {mod.Name} to Workshop");
        }

        /// <summary>
        /// Reads the full mod description.
        /// </summary>
        public static void ReadInfo()
        {
            var mod = GetSelectedMod();
            if (mod == null) return;

            var info = new List<string>();
            info.Add(mod.Name);

            if (!mod.AuthorsString.NullOrEmpty())
            {
                info.Add($"Author: {mod.AuthorsString}");
            }

            if (!mod.ModVersion.NullOrEmpty())
            {
                info.Add($"Version: {mod.ModVersion}");
            }

            if (!mod.VersionCompatible)
            {
                info.Add(mod.MadeForNewerVersion ? "Warning: Made for a newer version of RimWorld" : "Warning: Made for an older version of RimWorld");
            }

            // Check for warnings
            var warnings = modWarningsCachedField?.GetValue(null) as Dictionary<string, string>;
            if (warnings != null && warnings.TryGetValue(mod.PackageId, out string warning) && !warning.NullOrEmpty())
            {
                info.Add($"Error: {warning}");
            }

            if (!mod.Description.NullOrEmpty())
            {
                // Truncate very long descriptions
                string desc = mod.Description;
                if (desc.Length > 500)
                {
                    desc = desc.Substring(0, 500) + "... (description truncated)";
                }
                info.Add(desc);
            }

            TolkHelper.Speak(string.Join(". ", info));
        }

        /// <summary>
        /// Gets the current list based on the selected column.
        /// </summary>
        private static List<ModMetaData> GetCurrentList()
        {
            if (currentColumn == ModListColumn.Active)
            {
                return filteredActiveModListField?.GetValue(null) as List<ModMetaData>;
            }
            else
            {
                return filteredInactiveModListField?.GetValue(null) as List<ModMetaData>;
            }
        }

        /// <summary>
        /// Gets the currently selected mod.
        /// </summary>
        private static ModMetaData GetSelectedMod()
        {
            var list = GetCurrentList();
            if (list == null || list.Count == 0 || selectedIndex >= list.Count)
                return null;
            return list[selectedIndex];
        }

        /// <summary>
        /// Gets the primary selected mod from the page.
        /// </summary>
        private static ModMetaData GetPrimarySelectedMod()
        {
            if (currentPage == null) return null;
            return primarySelectedModField?.GetValue(currentPage) as ModMetaData;
        }

        /// <summary>
        /// Gets the mod's position in the actual load order (not filtered list).
        /// </summary>
        private static int GetModLoadOrderIndex(ModMetaData mod)
        {
            var activeMods = ModsConfig.ActiveModsInLoadOrder.ToList();
            return activeMods.IndexOf(mod);
        }

        /// <summary>
        /// Syncs our selection with the page's selection.
        /// </summary>
        private static void SyncSelection()
        {
            if (currentPage == null) return;

            var mod = GetSelectedMod();
            if (mod != null)
            {
                selectModMethod?.Invoke(currentPage, new object[] { mod });
            }
        }

        /// <summary>
        /// Announces the current state on open.
        /// </summary>
        private static void AnnounceCurrentState()
        {
            var activeList = filteredActiveModListField?.GetValue(null) as List<ModMetaData>;
            var inactiveList = filteredInactiveModListField?.GetValue(null) as List<ModMetaData>;

            int activeCount = activeList?.Count ?? 0;
            int inactiveCount = inactiveList?.Count ?? 0;

            string announcement = $"Mod manager. {activeCount} active mods, {inactiveCount} inactive mods. ";

            if (currentColumn == ModListColumn.Active && activeCount > 0)
            {
                announcement += "Active mods list. ";
            }
            else if (currentColumn == ModListColumn.Inactive && inactiveCount > 0)
            {
                announcement += "Inactive mods list. ";
            }

            var mod = GetSelectedMod();
            if (mod != null)
            {
                announcement += GetModAnnouncement(mod);
            }

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Announces switching between columns.
        /// </summary>
        private static void AnnounceColumnSwitch()
        {
            var list = GetCurrentList();
            string columnName = currentColumn == ModListColumn.Active ? "Active" : "Inactive";
            int count = list?.Count ?? 0;

            string announcement = $"{columnName} mods, {count} mods. ";

            var mod = GetSelectedMod();
            if (mod != null)
            {
                announcement += GetModAnnouncement(mod);
            }

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Announces the current mod selection.
        /// </summary>
        private static void AnnounceCurrentMod()
        {
            var mod = GetSelectedMod();
            if (mod == null) return;

            TolkHelper.Speak(GetModAnnouncement(mod));
        }

        /// <summary>
        /// Builds the announcement string for a mod.
        /// </summary>
        private static string GetModAnnouncement(ModMetaData mod)
        {
            var list = GetCurrentList();
            int position = selectedIndex + 1;
            int total = list?.Count ?? 0;

            var parts = new List<string>();
            parts.Add(mod.Name);
            parts.Add(MenuHelper.FormatPosition(selectedIndex, total));

            if (mod.Active)
            {
                parts.Add("enabled");
            }
            else
            {
                parts.Add("disabled");
            }

            // Add warnings
            if (!mod.VersionCompatible)
            {
                parts.Add("version incompatible");
            }

            var warnings = modWarningsCachedField?.GetValue(null) as Dictionary<string, string>;
            if (warnings != null && warnings.TryGetValue(mod.PackageId, out string warning) && !string.IsNullOrEmpty(warning))
            {
                parts.Add("has errors");
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Gets the list of mod name labels for typeahead search.
        /// </summary>
        private static List<string> GetModLabels()
        {
            var labels = new List<string>();
            var list = GetCurrentList();
            if (list != null)
            {
                foreach (var mod in list)
                {
                    labels.Add(mod.Name);
                }
            }
            return labels;
        }

        /// <summary>
        /// Jumps to the first mod in the current list.
        /// </summary>
        public static void JumpToFirst()
        {
            typeahead.ClearSearch();
            var list = GetCurrentList();
            if (list == null || list.Count == 0) return;

            selectedIndex = MenuHelper.JumpToFirst();
            SyncSelection();
            AnnounceCurrentMod();
        }

        /// <summary>
        /// Jumps to the last mod in the current list.
        /// </summary>
        public static void JumpToLast()
        {
            typeahead.ClearSearch();
            var list = GetCurrentList();
            if (list == null || list.Count == 0) return;

            selectedIndex = MenuHelper.JumpToLast(list.Count);
            SyncSelection();
            AnnounceCurrentMod();
        }

        /// <summary>
        /// Announces the current mod with search match info.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            var mod = GetSelectedMod();
            if (mod == null) return;

            var list = GetCurrentList();
            int position = selectedIndex + 1;
            int total = list?.Count ?? 0;

            if (typeahead.HasActiveSearch)
            {
                TolkHelper.Speak($"{mod.Name}, {position} of {total}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
            }
            else
            {
                TolkHelper.Speak(GetModAnnouncement(mod));
            }
        }

        /// <summary>
        /// Handles keyboard input for the mod list.
        /// Returns true if input was handled.
        /// </summary>
        public static bool HandleInput()
        {
            if (!isActive || currentPage == null) return false;

            if (Event.current.type != EventType.KeyDown) return false;

            var key = Event.current.keyCode;
            bool ctrl = Event.current.control;

            // Handle Home - jump to first
            if (key == KeyCode.Home)
            {
                JumpToFirst();
                return true;
            }

            // Handle End - jump to last
            if (key == KeyCode.End)
            {
                JumpToLast();
                return true;
            }

            // Handle Escape - clear search first, then close
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceWithSearch();
                    return true;
                }
                // Let escape pass through for dialog closing
                return false;
            }

            // Handle Backspace for search
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = GetModLabels();
                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0)
                    {
                        selectedIndex = newIndex;
                        SyncSelection();
                    }
                    AnnounceWithSearch();
                }
                return true;
            }

            // Handle * key - consume to prevent passthrough (reserved for future)
            // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
            if (key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8))
            {
                return true;
            }

            // Handle typeahead characters (but not when Alt is held - those are action keys)
            // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;
            bool alt = Event.current.alt;

            if ((isLetter || isNumber) && !alt && !ctrl)
            {
                char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                var labels = GetModLabels();
                if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
                {
                    if (newIndex >= 0)
                    {
                        selectedIndex = newIndex;
                        SyncSelection();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
                }
                return true;
            }

            // Handle Up/Down arrows with typeahead support
            if (key == KeyCode.UpArrow)
            {
                if (ctrl)
                {
                    MoveUp();
                }
                else if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    // Navigate through matches only when there ARE matches
                    int prev = typeahead.GetPreviousMatch(selectedIndex);
                    if (prev >= 0)
                    {
                        selectedIndex = prev;
                        SyncSelection();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    // Navigate normally (either no search active, OR search with no matches)
                    SelectPrevious();
                }
                return true;
            }

            if (key == KeyCode.DownArrow)
            {
                if (ctrl)
                {
                    MoveDown();
                }
                else if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    // Navigate through matches only when there ARE matches
                    int next = typeahead.GetNextMatch(selectedIndex);
                    if (next >= 0)
                    {
                        selectedIndex = next;
                        SyncSelection();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    // Navigate normally (either no search active, OR search with no matches)
                    SelectNext();
                }
                return true;
            }

            // Handle Left/Right arrows - switch column (clears search)
            if (key == KeyCode.LeftArrow || key == KeyCode.RightArrow)
            {
                SwitchColumn();
                return true;
            }

            // Handle Enter - toggle selected
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                ToggleSelected();
                return true;
            }

            // Handle other hotkeys (Alt+key to not conflict with typeahead)
            if (alt && key == KeyCode.M)
            {
                OpenSettings();
                return true;
            }

            if (alt && key == KeyCode.I)
            {
                ReadInfo();
                return true;
            }

            if (alt && key == KeyCode.S)
            {
                SaveChanges();
                return true;
            }

            if (alt && key == KeyCode.O)
            {
                OpenModFolder();
                return true;
            }

            if (alt && key == KeyCode.W)
            {
                OpenWorkshopPage();
                return true;
            }

            if (alt && key == KeyCode.U)
            {
                UploadToWorkshop();
                return true;
            }

            if (alt && key == KeyCode.R)
            {
                AutoSortMods();
                return true;
            }

            return false;
        }
    }
}
