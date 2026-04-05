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
    /// Uses TwoLevelMenuHelper for detail view (list → Enter → navigable detail with buttons).
    /// Input routed through UnifiedKeyboardPatch for proper Dialog_MessageBox precedence.
    /// </summary>
    public static class ModListState
    {
        private static bool isActive = false;
        private static ModListColumn currentColumn = ModListColumn.Active;
        private static int selectedIndex = 0;
        private static Page_ModsConfig currentPage = null;

        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private static TwoLevelMenuHelper detailHelper = null;
        private static List<string> contentLines = new List<string>();

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
        public static bool IsInDetailView => detailHelper?.IsInDetailView ?? false;

        static ModListState()
        {
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

            // Initialize the detail helper
            detailHelper = new TwoLevelMenuHelper(
                getContentLineCount: () => contentLines.Count,
                populateButtons: PopulateButtons,
                getHeaderAnnouncement: () =>
                {
                    var mod = GetSelectedMod();
                    return mod?.Name ?? "";
                },
                getContentLineAnnouncement: (idx) =>
                    idx >= 0 && idx < contentLines.Count ? contentLines[idx] : "",
                endOfItemMessage: "End of mod details",
                startOfItemMessage: "Start of mod details"
            );

            // Sync with the page's selection
            var activeList = GetCurrentList();
            if (activeList != null && activeList.Count > 0)
            {
                var primaryMod = GetPrimarySelectedMod();
                if (primaryMod != null)
                {
                    currentColumn = primaryMod.Active ? ModListColumn.Active : ModListColumn.Inactive;
                    selectedIndex = activeList.IndexOf(primaryMod);
                    if (selectedIndex < 0) selectedIndex = 0;
                }
                BuildContentLines();
                detailHelper.RefreshButtons();
            }

            AnnounceOpening();
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
            contentLines.Clear();
            detailHelper?.Reset();
        }

        /// <summary>
        /// Handles keyboard input. Called from UnifiedKeyboardPatch.
        /// Returns true if input was consumed.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive || currentPage == null) return false;

            // Detail view input
            if (detailHelper != null && detailHelper.IsInDetailView)
            {
                return HandleDetailInput(key, shift, ctrl, alt);
            }

            // List view input
            return HandleListInput(key, shift, ctrl, alt);
        }

        // ============================================================
        // List-level input
        // ============================================================

        private static bool HandleListInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            // Home - jump to first
            if (key == KeyCode.Home)
            {
                JumpToFirst();
                return true;
            }

            // End - jump to last
            if (key == KeyCode.End)
            {
                JumpToLast();
                return true;
            }

            // Escape - clear search first, then let vanilla handle close
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceWithSearch();
                    return true;
                }
                return false;
            }

            // Backspace for search
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = GetModLabels();
                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0)
                    {
                        selectedIndex = newIndex;
                        SyncSelection();
                        BuildContentLines();
                        detailHelper.RefreshButtons();
                    }
                    AnnounceWithSearch();
                }
                return true;
            }

            // Space - quick enable/disable toggle
            if (key == KeyCode.Space)
            {
                QuickToggle();
                return true;
            }

            // Enter - open detail view
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                var mod = GetSelectedMod();
                if (mod != null)
                {
                    BuildContentLines();
                    detailHelper.RefreshButtons();
                    detailHelper.EnterDetailView();
                    detailHelper.AnnounceDetailPosition();
                }
                return true;
            }

            // Up/Down arrows with typeahead and reorder support
            if (key == KeyCode.UpArrow)
            {
                if (ctrl)
                {
                    MoveUp();
                }
                else if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int prev = typeahead.GetPreviousMatch(selectedIndex);
                    if (prev >= 0)
                    {
                        selectedIndex = prev;
                        SyncSelection();
                        BuildContentLines();
                        detailHelper.RefreshButtons();
                        AnnounceWithSearch();
                    }
                }
                else
                {
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
                    int next = typeahead.GetNextMatch(selectedIndex);
                    if (next >= 0)
                    {
                        selectedIndex = next;
                        SyncSelection();
                        BuildContentLines();
                        detailHelper.RefreshButtons();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectNext();
                }
                return true;
            }

            // Left/Right - switch column
            if (key == KeyCode.LeftArrow || key == KeyCode.RightArrow)
            {
                SwitchColumn();
                return true;
            }

            // Alt+R - auto-sort mods
            if (alt && key == KeyCode.R)
            {
                AutoSortMods();
                return true;
            }

            // Alt+S - save changes
            if (alt && key == KeyCode.S)
            {
                SaveChanges();
                return true;
            }

            // Consume * key to prevent passthrough
            if (key == KeyCode.KeypadMultiply || (shift && key == KeyCode.Alpha8))
            {
                return true;
            }

            // Typeahead characters (not when Alt or Ctrl is held)
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if ((isLetter || isNumber) && !alt && !ctrl)
            {
                TypeaheadCharacterBuffer.RequestCharacter(c =>
                {
                    var labels = GetModLabels();
                    if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
                    {
                        if (newIndex >= 0)
                        {
                            selectedIndex = newIndex;
                            SyncSelection();
                            BuildContentLines();
                            detailHelper.RefreshButtons();
                            AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
                    }
                });
                return true;
            }

            return false;
        }

        // ============================================================
        // Detail-level input
        // ============================================================

        private static bool HandleDetailInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            // Escape - go back to list
            if (key == KeyCode.Escape)
            {
                detailHelper.GoBackToList();
                AnnounceCurrentMod();
                return true;
            }

            // Up/Down - navigate detail positions
            if (key == KeyCode.UpArrow)
            {
                detailHelper.SelectPreviousDetailPosition();
                return true;
            }

            if (key == KeyCode.DownArrow)
            {
                detailHelper.SelectNextDetailPosition();
                return true;
            }

            // Left/Right - navigate buttons (TwoLevelMenuHelper announces
            // "Navigate down to buttons first" if not yet in buttons section)
            if (key == KeyCode.LeftArrow)
            {
                detailHelper.SelectPreviousButton();
                return true;
            }

            if (key == KeyCode.RightArrow)
            {
                detailHelper.SelectNextButton();
                return true;
            }

            // Home/End - jump to start/end of detail
            if (key == KeyCode.Home)
            {
                detailHelper.JumpToDetailStart();
                return true;
            }

            if (key == KeyCode.End)
            {
                detailHelper.JumpToDetailEnd();
                return true;
            }

            // Enter - activate button
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                if (detailHelper.IsInButtonsSection)
                {
                    ActivateCurrentButton();
                }
                return true;
            }

            // Consume all other keys in detail view to prevent passthrough
            return true;
        }

        // ============================================================
        // Content lines builder
        // ============================================================

        private static void BuildContentLines()
        {
            contentLines.Clear();
            var mod = GetSelectedMod();
            if (mod == null) return;

            // Author
            if (!mod.AuthorsString.NullOrEmpty())
            {
                contentLines.Add($"{"Author".Translate()}: {mod.AuthorsString}");
            }

            // Package ID
            contentLines.Add($"{"ModPackageId".Translate()}: {mod.packageIdLowerCase}");

            // Target versions with compatibility
            if (mod.SupportedVersionsReadOnly != null && mod.SupportedVersionsReadOnly.Any())
            {
                string versions = mod.SupportedVersionsReadOnly
                    .Select(v => v.Major + "." + v.Minor)
                    .ToCommaList();
                string compat = mod.VersionCompatible
                    ? "compatible"
                    : (mod.MadeForNewerVersion
                        ? "ModNotMadeForThisVersionShort".Translate().ToString()
                        : "ModNotMadeForThisVersionShort".Translate().ToString());
                contentLines.Add($"{"ModTargetVersion".Translate()}: {versions} - {compat}");
            }

            // Mod version
            if (!mod.ModVersion.NullOrEmpty())
            {
                contentLines.Add($"{"ModVersion".Translate()}: {mod.ModVersion}");
            }

            // Warning (version incompatibility)
            if (!mod.VersionCompatible)
            {
                string warning = mod.MadeForNewerVersion
                    ? "ModNotMadeForThisVersion_Newer".Translate().ToString()
                    : "ModNotMadeForThisVersion".Translate().ToString();
                contentLines.Add($"{"Warning".Translate()}: {warning}");
            }

            // Error (conflicts/ordering from cached warnings)
            var warnings = modWarningsCachedField?.GetValue(null) as Dictionary<string, string>;
            if (warnings != null && warnings.TryGetValue(mod.PackageId, out string errorText) && !errorText.NullOrEmpty())
            {
                contentLines.Add($"Error: {errorText}");
            }

            // Requirements (dependencies and incompatibilities)
            var requirements = mod.GetRequirements();
            if (requirements != null)
            {
                foreach (var req in requirements)
                {
                    string reqType = req.RequirementTypeLabel;
                    string status = req.IsSatisfied ? "satisfied" : "not satisfied";
                    contentLines.Add($"{reqType}: {req.displayName} - {status}");
                }
            }

            // Load order warning
            if (mod.Active && ModsConfig.ModHasAnyOrderingIssues(mod))
            {
                contentLines.Add("ModOrderingWarning".Translate().ToString());
            }

            // Description
            if (!mod.Description.NullOrEmpty())
            {
                string desc = System.Net.WebUtility.HtmlDecode(mod.Description).Trim();
                // Split long descriptions into chunks for navigability
                if (desc.Length > 300)
                {
                    var chunks = SplitDescription(desc, 300);
                    foreach (var chunk in chunks)
                    {
                        contentLines.Add(chunk);
                    }
                }
                else
                {
                    contentLines.Add(desc);
                }
            }
        }

        private static List<string> SplitDescription(string desc, int maxChunkLength)
        {
            var chunks = new List<string>();
            int start = 0;
            while (start < desc.Length)
            {
                int length = Math.Min(maxChunkLength, desc.Length - start);
                if (start + length < desc.Length)
                {
                    // Try to break at a sentence boundary or newline
                    int breakAt = desc.LastIndexOf('\n', start + length, length);
                    if (breakAt <= start)
                        breakAt = desc.LastIndexOf(". ", start + length, length);
                    if (breakAt > start)
                        length = breakAt - start + 1;
                }
                chunks.Add(desc.Substring(start, length).Trim());
                start += length;
            }
            return chunks;
        }

        // ============================================================
        // Button builder
        // ============================================================

        private static void PopulateButtons(List<ButtonInfo> buttons)
        {
            var mod = GetSelectedMod();
            if (mod == null) return;

            // Enable/Disable (always first)
            if (mod.Active)
            {
                buttons.Add(new ButtonInfo
                {
                    Label = "Disable".Translate().ToString(),
                    Action = () => ToggleMod(mod)
                });
            }
            else
            {
                buttons.Add(new ButtonInfo
                {
                    Label = "Enable".Translate().ToString(),
                    Action = () => ToggleMod(mod)
                });
            }

            // Mod Options (if mod has settings)
            var modHandle = primaryModHandleField?.GetValue(currentPage) as Mod;
            if (modHandle != null && !modHandle.SettingsCategory().NullOrEmpty())
            {
                buttons.Add(new ButtonInfo
                {
                    Label = "ModOptions".Translate().ToString(),
                    Action = () =>
                    {
                        Find.WindowStack.Add(new Dialog_ModSettings(modHandle));
                    }
                });
            }

            // Website (if mod has URL)
            if (!mod.Url.NullOrEmpty())
            {
                buttons.Add(new ButtonInfo
                {
                    Label = "ModWebsite".Translate().ToString(),
                    Action = () =>
                    {
                        Application.OpenURL(mod.Url);
                        TolkHelper.Speak($"Opening website for {mod.Name}");
                    }
                });
            }

            // Steam Workshop Page (if workshop mod)
            if (mod.OnSteamWorkshop)
            {
                buttons.Add(new ButtonInfo
                {
                    Label = "WorkshopPage".Translate().ToString(),
                    Action = () =>
                    {
                        SteamUtility.OpenWorkshopPage(mod.GetPublishedFileId());
                        TolkHelper.Speak($"Opening Workshop page for {mod.Name}");
                    }
                });
            }

            // Open Folder
            buttons.Add(new ButtonInfo
            {
                Label = "ModFolder".Translate().ToString(),
                Action = () =>
                {
                    string path = mod.RootDir.FullName;
                    if (Application.platform == RuntimePlatform.OSXPlayer ||
                        Application.platform == RuntimePlatform.OSXEditor)
                    {
                        System.Diagnostics.Process.Start("open", $"\"{path}\"");
                    }
                    else if (Application.platform == RuntimePlatform.LinuxPlayer ||
                             Application.platform == RuntimePlatform.LinuxEditor)
                    {
                        System.Diagnostics.Process.Start("xdg-open", $"\"{path}\"");
                    }
                    else
                    {
                        Application.OpenURL(path);
                    }
                    TolkHelper.Speak($"Opening folder for {mod.Name}");
                }
            });

            // Unsubscribe (if workshop mod)
            if (mod.Source == ContentSource.SteamWorkshop)
            {
                buttons.Add(new ButtonInfo
                {
                    Label = "Unsubscribe".Translate().ToString(),
                    Action = () =>
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            "ConfirmUnsubscribeFrom".Translate(mod.Name),
                            () =>
                            {
                                // Workshop.Unsubscribe is internal, use reflection
                                var unsubMethod = typeof(Workshop).GetMethod("Unsubscribe",
                                    BindingFlags.NonPublic | BindingFlags.Static,
                                    null,
                                    new[] { typeof(WorkshopUploadable) },
                                    null);
                                unsubMethod?.Invoke(null, new object[] { mod });
                                modListsDirtyField?.SetValue(currentPage, true);
                                detailHelper.GoBackToList();
                                TolkHelper.Speak($"Unsubscribed from {mod.Name}");
                            },
                            destructive: true
                        ));
                    }
                });
            }
        }

        // ============================================================
        // Mod operations
        // ============================================================

        private static void ToggleMod(ModMetaData mod)
        {
            bool wasActive = mod.Active;

            if (wasActive)
            {
                trySetModInactiveMethod?.Invoke(currentPage, new object[] { mod });
            }
            else
            {
                var result = trySetModActiveMethod?.Invoke(currentPage, new object[] { mod });
                if (result is bool success && !success)
                {
                    TolkHelper.Speak("Could not enable mod. A mod with the same package ID may already be active.", SpeechPriority.High);
                    return;
                }
            }

            modListsDirtyField?.SetValue(currentPage, true);

            // After toggling from detail view, go back to list
            detailHelper.GoBackToList();

            // Switch to the column the mod moved to
            currentColumn = wasActive ? ModListColumn.Inactive : ModListColumn.Active;

            var newList = GetCurrentList();
            if (newList != null)
            {
                int newIndex = newList.IndexOf(mod);
                selectedIndex = newIndex >= 0 ? newIndex : 0;
            }

            SyncSelection();
            BuildContentLines();
            detailHelper.RefreshButtons();

            string action = wasActive ? "disabled" : "enabled";
            TolkHelper.Speak($"{mod.Name} {action}");
        }

        /// <summary>
        /// Quick enable/disable toggle from list level (Space key).
        /// Stays in list view.
        /// </summary>
        private static void QuickToggle()
        {
            if (currentPage == null) return;

            var list = GetCurrentList();
            if (list == null || list.Count == 0 || selectedIndex >= list.Count) return;

            var mod = list[selectedIndex];
            bool wasActive = mod.Active;

            if (wasActive)
            {
                trySetModInactiveMethod?.Invoke(currentPage, new object[] { mod });
            }
            else
            {
                var result = trySetModActiveMethod?.Invoke(currentPage, new object[] { mod });
                if (result is bool success && !success)
                {
                    TolkHelper.Speak("Could not enable mod. A mod with the same package ID may already be active.", SpeechPriority.High);
                    return;
                }
            }

            modListsDirtyField?.SetValue(currentPage, true);

            // Stay in current column, adjust index since the mod left this list
            var currentList = GetCurrentList();
            if (currentList != null)
            {
                if (selectedIndex >= currentList.Count)
                {
                    selectedIndex = Math.Max(0, currentList.Count - 1);
                }
                if (currentList.Count > 0)
                {
                    SyncSelection();
                    BuildContentLines();
                    detailHelper.RefreshButtons();
                }
            }

            string action = wasActive ? "disabled" : "enabled";
            string nextModInfo = "";
            var nextMod = GetSelectedMod();
            if (nextMod != null && currentList != null && currentList.Count > 0)
            {
                nextModInfo = $" Now on: {nextMod.Name}, {MenuHelper.FormatPosition(selectedIndex, currentList.Count)}";
            }
            else if (currentList == null || currentList.Count == 0)
            {
                nextModInfo = " List is now empty.";
            }
            TolkHelper.Speak($"{mod.Name} {action}.{nextModInfo}");
        }

        private static void MoveUp()
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

            if (ModsConfig.TryReorder(currentPos, currentPos - 1, out string errorMessage))
            {
                modListsDirtyField?.SetValue(currentPage, true);
                selectedIndex--;
                SyncSelection();
                BuildContentLines();
                detailHelper.RefreshButtons();
                TolkHelper.Speak($"{mod.Name} moved to position {selectedIndex + 1}");
            }
            else if (!string.IsNullOrEmpty(errorMessage))
            {
                TolkHelper.Speak($"Cannot reorder: {errorMessage}", SpeechPriority.High);
            }
        }

        private static void MoveDown()
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

            if (ModsConfig.TryReorder(currentPos, currentPos + 2, out string errorMessage))
            {
                modListsDirtyField?.SetValue(currentPage, true);
                selectedIndex++;
                SyncSelection();
                BuildContentLines();
                detailHelper.RefreshButtons();
                TolkHelper.Speak($"{mod.Name} moved to position {selectedIndex + 1}");
            }
            else if (!string.IsNullOrEmpty(errorMessage))
            {
                TolkHelper.Speak($"Cannot reorder: {errorMessage}", SpeechPriority.High);
            }
        }

        private static void SaveChanges()
        {
            if (currentPage == null) return;

            var saveChangesField = typeof(Page_ModsConfig).GetField("saveChanges", BindingFlags.NonPublic | BindingFlags.Instance);
            saveChangesField?.SetValue(currentPage, true);
            ModsConfig.Save();
            TolkHelper.Speak("Mod changes saved");
        }

        private static void AutoSortMods()
        {
            if (currentPage == null) return;

            ModsConfig.TrySortMods();
            modListsDirtyField?.SetValue(currentPage, true);

            selectedIndex = 0;
            SyncSelection();
            BuildContentLines();
            detailHelper.RefreshButtons();
            TolkHelper.Speak("Mods automatically sorted");
        }

        private static void ActivateCurrentButton()
        {
            if (!detailHelper.ActivateCurrentButton())
                return;

            ButtonInfo button = detailHelper.GetCurrentButton();
            if (button == null) return;

            try
            {
                button.Action?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Failed to activate mod button: {ex.Message}");
                TolkHelper.Speak("Failed to activate button");
            }
        }

        // ============================================================
        // Navigation helpers
        // ============================================================

        private static void SelectNext()
        {
            var list = GetCurrentList();
            if (list == null || list.Count == 0) return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, list.Count);
            SyncSelection();
            BuildContentLines();
            detailHelper.RefreshButtons();
            AnnounceCurrentMod();
        }

        private static void SelectPrevious()
        {
            var list = GetCurrentList();
            if (list == null || list.Count == 0) return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, list.Count);
            SyncSelection();
            BuildContentLines();
            detailHelper.RefreshButtons();
            AnnounceCurrentMod();
        }

        private static void SwitchColumn()
        {
            currentColumn = (currentColumn == ModListColumn.Active) ? ModListColumn.Inactive : ModListColumn.Active;
            selectedIndex = 0;
            typeahead.ClearSearch();

            var list = GetCurrentList();
            if (list != null && list.Count > 0)
            {
                SyncSelection();
                BuildContentLines();
                detailHelper.RefreshButtons();
                AnnounceColumnSwitch();
            }
            else
            {
                string columnName = currentColumn == ModListColumn.Active
                    ? "Active".Translate().ToString()
                    : "Inactive".Translate().ToString();
                TolkHelper.Speak($"{columnName} mods list, empty");
            }
        }

        private static void JumpToFirst()
        {
            typeahead.ClearSearch();
            var list = GetCurrentList();
            if (list == null || list.Count == 0) return;

            selectedIndex = MenuHelper.JumpToFirst();
            SyncSelection();
            BuildContentLines();
            detailHelper.RefreshButtons();
            AnnounceCurrentMod();
        }

        private static void JumpToLast()
        {
            typeahead.ClearSearch();
            var list = GetCurrentList();
            if (list == null || list.Count == 0) return;

            selectedIndex = MenuHelper.JumpToLast(list.Count);
            SyncSelection();
            BuildContentLines();
            detailHelper.RefreshButtons();
            AnnounceCurrentMod();
        }

        // ============================================================
        // Data access helpers
        // ============================================================

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

        private static ModMetaData GetSelectedMod()
        {
            var list = GetCurrentList();
            if (list == null || list.Count == 0 || selectedIndex >= list.Count)
                return null;
            return list[selectedIndex];
        }

        private static ModMetaData GetPrimarySelectedMod()
        {
            if (currentPage == null) return null;
            return primarySelectedModField?.GetValue(currentPage) as ModMetaData;
        }

        private static int GetModLoadOrderIndex(ModMetaData mod)
        {
            var activeMods = ModsConfig.ActiveModsInLoadOrder.ToList();
            return activeMods.IndexOf(mod);
        }

        private static void SyncSelection()
        {
            if (currentPage == null) return;
            var mod = GetSelectedMod();
            if (mod != null)
            {
                selectModMethod?.Invoke(currentPage, new object[] { mod });
            }
        }

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

        // ============================================================
        // Announcements
        // ============================================================

        private static void AnnounceOpening()
        {
            var activeList = filteredActiveModListField?.GetValue(null) as List<ModMetaData>;
            var inactiveList = filteredInactiveModListField?.GetValue(null) as List<ModMetaData>;

            int activeCount = activeList?.Count ?? 0;
            int inactiveCount = inactiveList?.Count ?? 0;

            string columnName = currentColumn == ModListColumn.Active
                ? "Enabled".Translate().ToString()
                : "Disabled".Translate().ToString();
            int currentCount = currentColumn == ModListColumn.Active ? activeCount : inactiveCount;

            string announcement = $"{"Mods".Translate()}. {columnName}, {currentCount} mods. ";
            announcement += "ResolveModOrder".Translate() + ": Alt+R. " + "SaveModChanges".Translate() + ": Alt+S.";

            // Announce the menu info first, then the selected mod separately
            // so the screen reader pauses between them
            var mod = GetSelectedMod();
            if (mod != null)
            {
                announcement += " ... " + GetModListAnnouncement(mod);
            }

            TolkHelper.Speak(announcement);
        }

        private static void AnnounceColumnSwitch()
        {
            var list = GetCurrentList();
            string columnName = currentColumn == ModListColumn.Active
                ? "Enabled".Translate().ToString()
                : "Disabled".Translate().ToString();
            int count = list?.Count ?? 0;

            string announcement = $"{columnName} mods, {count} mods. ";

            var mod = GetSelectedMod();
            if (mod != null)
            {
                announcement += GetModListAnnouncement(mod);
            }

            TolkHelper.Speak(announcement);
        }

        private static void AnnounceCurrentMod()
        {
            var mod = GetSelectedMod();
            if (mod == null) return;
            TolkHelper.Speak(GetModListAnnouncement(mod));
        }

        /// <summary>
        /// Builds the list-level announcement for a mod.
        /// Format: "Name. {warning/error}. By Author. X of Y"
        /// </summary>
        private static string GetModListAnnouncement(ModMetaData mod)
        {
            var list = GetCurrentList();
            int total = list?.Count ?? 0;

            var parts = new List<string>();
            parts.Add(mod.Name);

            // Warning/error right after name for immediate awareness
            if (!mod.VersionCompatible)
            {
                parts.Add("ModNotMadeForThisVersionShort".Translate().ToString());
            }

            var warnings = modWarningsCachedField?.GetValue(null) as Dictionary<string, string>;
            if (warnings != null && warnings.TryGetValue(mod.PackageId, out string warning) && !string.IsNullOrEmpty(warning))
            {
                parts.Add("contains errors");
            }

            // Author
            if (!mod.AuthorsString.NullOrEmpty())
            {
                parts.Add($"by {mod.AuthorsString}");
            }

            // Position
            parts.Add(MenuHelper.FormatPosition(selectedIndex, total));

            return string.Join(". ", parts);
        }

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
                TolkHelper.Speak(GetModListAnnouncement(mod));
            }
        }
    }
}
