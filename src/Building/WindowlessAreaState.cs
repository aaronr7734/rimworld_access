using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the state for the windowless area management interface.
    /// Provides keyboard navigation for creating, editing, and managing allowed areas.
    /// </summary>
    public static class WindowlessAreaState
    {
        private static bool isActive = false;
        private static Area selectedArea = null;
        private static int selectedAreaIndex = 0;
        private static List<Area> allAreas = new List<Area>();
        private static Map currentMap = null;
        private static Action onCloseCallback = null;

        // Pending return callback - deferred when Expand/Shrink spawns placement mode
        private static Action pendingReturnCallback = null;

        public static bool HasPendingReturn => pendingReturnCallback != null;

        // Navigation state
        public enum NavigationMode
        {
            AreaList,        // Navigating the list of areas
            AreaActions      // Selecting actions (New, Rename, Expand, Shrink, etc.)
        }

        private static NavigationMode currentMode = NavigationMode.AreaList;
        private static int selectedActionIndex = 0;
        private static TypeaheadSearchHelper actionsTypeahead = new TypeaheadSearchHelper();

        // Available actions - filtered based on whether an area is selected
        private static readonly string[] actionsWithArea = new string[]
        {
            "New Area",
            "Rename Area",
            "Expand Area",
            "Shrink Area",
            "Invert Area",
            "Copy Area",
            "Delete Area",
            "Close"
        };

        private static readonly string[] actionsWithoutArea = new string[]
        {
            "New Area",
            "Close"
        };

        /// <summary>
        /// Gets the available actions based on whether an area is selected.
        /// </summary>
        private static string[] GetAvailableActions()
        {
            return (selectedArea != null) ? actionsWithArea : actionsWithoutArea;
        }

        /// <summary>
        /// Gets the action labels as a list for typeahead search.
        /// </summary>
        private static List<string> GetActionLabels()
        {
            return GetAvailableActions().ToList();
        }

        public static bool IsActive => isActive;
        public static Area SelectedArea => selectedArea;
        public static NavigationMode CurrentMode => currentMode;

        /// <summary>
        /// Opens the area management interface.
        /// </summary>
        public static void Open(Map map, Action onClose = null)
        {
            if (map == null)
                return;

            isActive = true;
            currentMap = map;
            currentMode = NavigationMode.AreaList;
            selectedActionIndex = 0;
            onCloseCallback = onClose;

            LoadAreas();

            // Select the first area if available
            if (allAreas.Count > 0)
            {
                selectedAreaIndex = 0;
                selectedArea = allAreas[0];
            }

            UpdateClipboard();
        }

        /// <summary>
        /// Closes the area management interface.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            selectedArea = null;
            selectedAreaIndex = 0;
            allAreas.Clear();
            currentMap = null;
            currentMode = NavigationMode.AreaList;

            // Invoke callback if set (returns to AreaSelectionMenuState)
            var callback = onCloseCallback;
            onCloseCallback = null;
            callback?.Invoke();

            TolkHelper.Speak("Area manager closed");
        }

        /// <summary>
        /// Invokes and clears any pending return callback.
        /// Called by ViewingModeState when placement completes or is cancelled.
        /// </summary>
        public static void CompletePendingReturn()
        {
            if (pendingReturnCallback != null)
            {
                var callback = pendingReturnCallback;
                pendingReturnCallback = null;
                callback.Invoke();
            }
        }

        /// <summary>
        /// Loads all mutable areas from the map.
        /// </summary>
        private static void LoadAreas()
        {
            allAreas.Clear();
            if (currentMap?.areaManager != null)
            {
                allAreas = currentMap.areaManager.AllAreas
                    .Where(a => a.Mutable)
                    .ToList();
            }
        }

        /// <summary>
        /// Moves selection to the next area in the list.
        /// </summary>
        public static void SelectNextArea()
        {
            if (allAreas.Count == 0)
                return;

            selectedAreaIndex = MenuHelper.SelectNext(selectedAreaIndex, allAreas.Count);
            selectedArea = allAreas[selectedAreaIndex];
            selectedArea?.MarkForDraw();
            UpdateClipboard();
        }

        /// <summary>
        /// Moves selection to the previous area in the list.
        /// </summary>
        public static void SelectPreviousArea()
        {
            if (allAreas.Count == 0)
                return;

            selectedAreaIndex = MenuHelper.SelectPrevious(selectedAreaIndex, allAreas.Count);
            selectedArea = allAreas[selectedAreaIndex];
            selectedArea?.MarkForDraw();
            UpdateClipboard();
        }

        /// <summary>
        /// Switches from area list to actions mode.
        /// </summary>
        public static void EnterActionsMode()
        {
            if (currentMode == NavigationMode.AreaList)
            {
                currentMode = NavigationMode.AreaActions;
                selectedActionIndex = 0;
                actionsTypeahead.ClearSearch();
                UpdateClipboard();
            }
        }

        /// <summary>
        /// Returns to area list mode from actions mode.
        /// </summary>
        public static void ReturnToAreaList()
        {
            currentMode = NavigationMode.AreaList;
            actionsTypeahead.ClearSearch();
            LoadAreas(); // Reload in case areas changed

            // Reselect area if still valid
            if (selectedArea != null && allAreas.Contains(selectedArea))
            {
                selectedAreaIndex = allAreas.IndexOf(selectedArea);
            }
            else if (allAreas.Count > 0)
            {
                selectedAreaIndex = 0;
                selectedArea = allAreas[0];
            }
            else
            {
                selectedArea = null;
                selectedAreaIndex = 0;
            }

            UpdateClipboard();
        }

        /// <summary>
        /// Moves to the next action in the actions menu.
        /// </summary>
        public static void SelectNextAction()
        {
            var actions = GetAvailableActions();
            selectedActionIndex = MenuHelper.SelectNext(selectedActionIndex, actions.Length);
            UpdateClipboard();
        }

        /// <summary>
        /// Moves to the previous action in the actions menu.
        /// </summary>
        public static void SelectPreviousAction()
        {
            var actions = GetAvailableActions();
            selectedActionIndex = MenuHelper.SelectPrevious(selectedActionIndex, actions.Length);
            UpdateClipboard();
        }

        /// <summary>
        /// Executes the currently selected action.
        /// </summary>
        public static void ExecuteAction()
        {
            if (currentMode == NavigationMode.AreaActions)
            {
                var actions = GetAvailableActions();
                string action = actions[selectedActionIndex];

                switch (action)
                {
                    case "New Area":
                        CreateNewArea();
                        break;
                    case "Rename Area":
                        RenameArea();
                        break;
                    case "Expand Area":
                        ExpandArea();
                        break;
                    case "Shrink Area":
                        ShrinkArea();
                        break;
                    case "Invert Area":
                        InvertArea();
                        break;
                    case "Copy Area":
                        CopyArea();
                        break;
                    case "Delete Area":
                        DeleteArea();
                        break;
                    case "Close":
                        Close();
                        break;
                }
            }
        }

        /// <summary>
        /// Creates a new allowed area.
        /// </summary>
        private static void CreateNewArea()
        {
            if (currentMap?.areaManager != null)
            {
                if (currentMap.areaManager.TryMakeNewAllowed(out Area_Allowed newArea))
                {
                    LoadAreas();
                    selectedAreaIndex = allAreas.IndexOf(newArea);
                    selectedArea = newArea;
                    TolkHelper.Speak($"Created new area: {newArea.Label}");
                    ReturnToAreaList();
                }
                else
                {
                    TolkHelper.Speak("Cannot create area. Maximum of 10 areas reached.", SpeechPriority.High);
                }
            }
        }

        /// <summary>
        /// Opens the rename dialog for the selected area.
        /// </summary>
        private static void RenameArea()
        {
            if (selectedArea != null)
            {
                Find.WindowStack.Add(new Dialog_RenameArea(selectedArea));
                TolkHelper.Speak($"Rename area: {selectedArea.Label}. Enter new name and press Enter.");
            }
        }

        /// <summary>
        /// Activates the expand mode for the selected area.
        /// </summary>
        private static void ExpandArea()
        {
            if (selectedArea == null) return;

            // Save reference before Close() clears it
            Area areaToExpand = selectedArea;

            // DEFER the callback - don't fire it when Close() is called
            // It will be invoked by ViewingModeState when placement completes
            pendingReturnCallback = onCloseCallback;
            onCloseCallback = null;  // Clear so Close() doesn't fire it

            // Close the area manager UI (won't fire callback now)
            Close();

            // Set the area BEFORE selecting the designator
            // This tells DesignatorManagerPatch to skip the area selection menu
            Designator_AreaAllowed.selectedArea = areaToExpand;

            // Create and select the expand designator
            // DesignatorManagerPatch will route this to ShapePlacementState
            var designator = new Designator_AreaAllowedExpand();
            Find.DesignatorManager.Select(designator);
        }

        /// <summary>
        /// Activates the shrink mode for the selected area.
        /// </summary>
        private static void ShrinkArea()
        {
            if (selectedArea == null) return;

            // Save reference before Close() clears it
            Area areaToShrink = selectedArea;

            // DEFER the callback - don't fire it when Close() is called
            // It will be invoked by ViewingModeState when placement completes
            pendingReturnCallback = onCloseCallback;
            onCloseCallback = null;  // Clear so Close() doesn't fire it

            // Close the area manager UI (won't fire callback now)
            Close();

            // Set the area BEFORE selecting the designator
            Designator_AreaAllowed.selectedArea = areaToShrink;

            // Create and select the clear designator
            var designator = new Designator_AreaAllowedClear();
            Find.DesignatorManager.Select(designator);
        }

        /// <summary>
        /// Inverts the selected area.
        /// </summary>
        private static void InvertArea()
        {
            if (selectedArea != null)
            {
                selectedArea.Invert();
                TolkHelper.Speak($"Inverted area: {selectedArea.Label}. All cells toggled.");
            }
        }

        /// <summary>
        /// Copies the selected area.
        /// </summary>
        private static void CopyArea()
        {
            if (selectedArea != null && currentMap?.areaManager != null)
            {
                if (currentMap.areaManager.TryMakeNewAllowed(out Area_Allowed newArea))
                {
                    foreach (IntVec3 cell in selectedArea.ActiveCells)
                    {
                        newArea[cell] = true;
                    }
                    LoadAreas();
                    selectedAreaIndex = allAreas.IndexOf(newArea);
                    selectedArea = newArea;
                    TolkHelper.Speak($"Copied area to: {newArea.Label}");
                }
                else
                {
                    TolkHelper.Speak("Cannot copy area. Maximum of 10 areas reached.", SpeechPriority.High);
                }
            }
        }

        /// <summary>
        /// Deletes the selected area.
        /// </summary>
        private static void DeleteArea()
        {
            if (selectedArea == null)
                return;

            string deletedName = selectedArea.Label;
            selectedArea.Delete();
            LoadAreas();

            // Select another area
            if (allAreas.Count > 0)
            {
                selectedAreaIndex = 0;
                selectedArea = allAreas[0];
            }
            else
            {
                selectedArea = null;
                selectedAreaIndex = 0;
            }

            TolkHelper.Speak($"Deleted area: {deletedName}");
        }

        /// <summary>
        /// Selects the first action in the actions menu.
        /// </summary>
        public static void SelectFirstAction()
        {
            selectedActionIndex = 0;
            actionsTypeahead.ClearSearch();
            AnnounceCurrentAction();
        }

        /// <summary>
        /// Selects the last action in the actions menu.
        /// </summary>
        public static void SelectLastAction()
        {
            var actions = GetAvailableActions();
            selectedActionIndex = actions.Length - 1;
            actionsTypeahead.ClearSearch();
            AnnounceCurrentAction();
        }

        /// <summary>
        /// Announces the current action.
        /// </summary>
        private static void AnnounceCurrentAction()
        {
            var actions = GetAvailableActions();
            string action = actions[selectedActionIndex];
            string position = MenuHelper.FormatPosition(selectedActionIndex, actions.Length);
            string announcement = string.IsNullOrEmpty(position) ? action : $"{action}, {position}";
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Announces the current action with search match information.
        /// </summary>
        private static void AnnounceActionWithSearch()
        {
            var actions = GetAvailableActions();
            if (selectedActionIndex >= 0 && selectedActionIndex < actions.Length)
            {
                string action = actions[selectedActionIndex];
                TolkHelper.Speak(actionsTypeahead.BuildItemAnnouncement(action));
            }
        }

        /// <summary>
        /// Handles typeahead character input for actions menu.
        /// </summary>
        /// <param name="c">The character typed</param>
        /// <returns>True if input was handled</returns>
        public static bool HandleActionsTypeahead(char c)
        {
            var labels = GetActionLabels();
            if (actionsTypeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedActionIndex = newIndex;
                    AnnounceActionWithSearch();
                }
                return true;
            }
            else
            {
                actionsTypeahead.SpeakNoMatches();
                return true;
            }
        }

        /// <summary>
        /// Handles backspace for actions typeahead search.
        /// </summary>
        /// <returns>True if backspace was handled</returns>
        public static bool HandleActionsBackspace()
        {
            if (!actionsTypeahead.HasActiveSearch)
                return false;

            var labels = GetActionLabels();
            if (actionsTypeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                    selectedActionIndex = newIndex;
                AnnounceActionWithSearch();
            }
            return true;
        }

        /// <summary>
        /// Clears the actions typeahead search and announces "Search cleared".
        /// </summary>
        /// <returns>True if there was an active search to clear</returns>
        public static bool ClearActionsSearch()
        {
            return actionsTypeahead.ClearSearchAndAnnounce();
        }

        /// <summary>
        /// Gets whether there is an active typeahead search in actions mode.
        /// </summary>
        public static bool HasActiveActionsSearch => actionsTypeahead.HasActiveSearch;

        /// <summary>
        /// Gets whether there is an active search with no matches.
        /// </summary>
        public static bool HasNoActionsMatches => actionsTypeahead.HasNoMatches;

        /// <summary>
        /// Selects the next action that matches the current search.
        /// </summary>
        public static void SelectNextActionMatch()
        {
            if (actionsTypeahead.HasActiveSearch && !actionsTypeahead.HasNoMatches)
            {
                int nextIndex = actionsTypeahead.GetNextMatch(selectedActionIndex);
                if (nextIndex >= 0)
                {
                    selectedActionIndex = nextIndex;
                    AnnounceActionWithSearch();
                }
            }
            else
            {
                SelectNextAction();
            }
        }

        /// <summary>
        /// Selects the previous action that matches the current search.
        /// </summary>
        public static void SelectPreviousActionMatch()
        {
            if (actionsTypeahead.HasActiveSearch && !actionsTypeahead.HasNoMatches)
            {
                int prevIndex = actionsTypeahead.GetPreviousMatch(selectedActionIndex);
                if (prevIndex >= 0)
                {
                    selectedActionIndex = prevIndex;
                    AnnounceActionWithSearch();
                }
            }
            else
            {
                SelectPreviousAction();
            }
        }

        /// <summary>
        /// Updates the clipboard with the current selection.
        /// </summary>
        private static void UpdateClipboard()
        {
            if (currentMode == NavigationMode.AreaList)
            {
                if (selectedArea != null)
                {
                    int cellCount = selectedArea.TrueCount;
                    string position = MenuHelper.FormatPosition(selectedAreaIndex, allAreas.Count);
                    string positionPart = string.IsNullOrEmpty(position) ? "" : $", {position}";
                    TolkHelper.Speak($"{selectedArea.Label} ({cellCount} cells){positionPart}. Press right bracket for actions.");
                }
                else
                {
                    TolkHelper.Speak("No areas available. Press right bracket to create one.");
                }
            }
            else if (currentMode == NavigationMode.AreaActions)
            {
                AnnounceCurrentAction();
            }

            selectedArea?.MarkForDraw();
        }
    }
}
