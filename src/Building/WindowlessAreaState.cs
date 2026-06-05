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

        private enum AreaAction
        {
            NewArea,
            RenameArea,
            ExpandArea,
            ShrinkArea,
            InvertArea,
            CopyArea,
            DeleteArea,
            Close,
        }

        // Dispatch order; localized labels resolve via GetActionLabel.
        private static readonly AreaAction[] actionsWithArea = new[]
        {
            AreaAction.NewArea,
            AreaAction.RenameArea,
            AreaAction.ExpandArea,
            AreaAction.ShrinkArea,
            AreaAction.InvertArea,
            AreaAction.CopyArea,
            AreaAction.DeleteArea,
            AreaAction.Close,
        };

        private static readonly AreaAction[] actionsWithoutArea = new[]
        {
            AreaAction.NewArea,
            AreaAction.Close,
        };

        private static AreaAction[] GetAvailableActions()
        {
            return (selectedArea != null) ? actionsWithArea : actionsWithoutArea;
        }

        private static string GetActionLabel(AreaAction action)
        {
            switch (action)
            {
                case AreaAction.NewArea:    return "RimWorldAccess.Building.AreaMgr.Action.NewArea".Translate();
                case AreaAction.RenameArea: return "RimWorldAccess.Building.AreaMgr.Action.RenameArea".Translate();
                case AreaAction.ExpandArea: return "RimWorldAccess.Building.AreaMgr.Action.ExpandArea".Translate();
                case AreaAction.ShrinkArea: return "RimWorldAccess.Building.AreaMgr.Action.ShrinkArea".Translate();
                case AreaAction.InvertArea: return "RimWorldAccess.Building.AreaMgr.Action.InvertArea".Translate();
                case AreaAction.CopyArea:   return "RimWorldAccess.Building.AreaMgr.Action.CopyArea".Translate();
                case AreaAction.DeleteArea: return "RimWorldAccess.Building.AreaMgr.Action.DeleteArea".Translate();
                case AreaAction.Close:      return "RimWorldAccess.Building.AreaMgr.Action.Close".Translate();
                default:                    return string.Empty;
            }
        }

        private static List<string> GetActionLabels()
        {
            return GetAvailableActions().Select(GetActionLabel).ToList();
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

            TolkHelper.Speak("RimWorldAccess.Building.AreaMgr.Closed".Loc());
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
                AreaAction action = actions[selectedActionIndex];

                switch (action)
                {
                    case AreaAction.NewArea:    CreateNewArea(); break;
                    case AreaAction.RenameArea: RenameArea();    break;
                    case AreaAction.ExpandArea: ExpandArea();    break;
                    case AreaAction.ShrinkArea: ShrinkArea();    break;
                    case AreaAction.InvertArea: InvertArea();    break;
                    case AreaAction.CopyArea:   CopyArea();      break;
                    case AreaAction.DeleteArea: DeleteArea();    break;
                    case AreaAction.Close:      Close();         break;
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
                    TolkHelper.Speak("RimWorldAccess.Building.AreaMgr.CreatedNewArea".Loc(newArea.Label));
                    ReturnToAreaList();
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Building.AreaMgr.MaxAreasReached".Loc(), SpeechPriority.High);
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
                TolkHelper.Speak("RimWorldAccess.Building.AreaMgr.RenamePrompt".Loc(selectedArea.Label));
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
                TolkHelper.Speak("RimWorldAccess.Building.AreaMgr.Inverted".Loc(selectedArea.Label));
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
                    TolkHelper.Speak("RimWorldAccess.Building.AreaMgr.CopiedTo".Loc(newArea.Label));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Building.AreaMgr.MaxAreasReachedCopy".Loc(), SpeechPriority.High);
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

            TolkHelper.Speak("RimWorldAccess.Building.AreaMgr.Deleted".Loc(deletedName));
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
            string label = GetActionLabel(actions[selectedActionIndex]);
            string position = MenuHelper.FormatPosition(selectedActionIndex, actions.Length);
            string announcement = string.IsNullOrEmpty(position)
                ? label
                : (string)"RimWorldAccess.Building.AreaMgr.ActionWithPosition".Translate(label, position);
            TolkHelper.SpeakData(announcement);
        }

        /// <summary>
        /// Announces the current action with search match information.
        /// </summary>
        private static void AnnounceActionWithSearch()
        {
            var actions = GetAvailableActions();
            if (selectedActionIndex >= 0 && selectedActionIndex < actions.Length)
            {
                string label = GetActionLabel(actions[selectedActionIndex]);
                TolkHelper.SpeakData(actionsTypeahead.BuildItemAnnouncement(label));
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
                    string announcement = string.IsNullOrEmpty(position)
                        ? (string)"RimWorldAccess.Building.AreaMgr.AreaWithCellsNoPosition".Translate(selectedArea.Label, cellCount)
                        : (string)"RimWorldAccess.Building.AreaMgr.AreaWithCellsAndPosition".Translate(selectedArea.Label, cellCount, position);
                    TolkHelper.SpeakData(announcement);
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Building.AreaMgr.NoAreasAvailable".Loc());
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
