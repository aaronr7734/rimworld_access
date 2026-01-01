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

        // Navigation state
        public enum NavigationMode
        {
            AreaList,        // Navigating the list of areas
            AreaActions      // Selecting actions (New, Rename, Expand, Shrink, etc.)
        }

        private static NavigationMode currentMode = NavigationMode.AreaList;
        private static int selectedActionIndex = 0;

        // Available actions
        private static readonly string[] areaActions = new string[]
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

        public static bool IsActive => isActive;
        public static Area SelectedArea => selectedArea;
        public static NavigationMode CurrentMode => currentMode;

        /// <summary>
        /// Opens the area management interface.
        /// </summary>
        public static void Open(Map map)
        {
            if (map == null)
                return;

            isActive = true;
            currentMap = map;
            currentMode = NavigationMode.AreaList;
            selectedActionIndex = 0;

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

            TolkHelper.Speak("Area manager closed");
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
                UpdateClipboard();
            }
        }

        /// <summary>
        /// Returns to area list mode from actions mode.
        /// </summary>
        public static void ReturnToAreaList()
        {
            currentMode = NavigationMode.AreaList;
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
            selectedActionIndex = MenuHelper.SelectNext(selectedActionIndex, areaActions.Length);
            UpdateClipboard();
        }

        /// <summary>
        /// Moves to the previous action in the actions menu.
        /// </summary>
        public static void SelectPreviousAction()
        {
            selectedActionIndex = MenuHelper.SelectPrevious(selectedActionIndex, areaActions.Length);
            UpdateClipboard();
        }

        /// <summary>
        /// Executes the currently selected action.
        /// </summary>
        public static void ExecuteAction()
        {
            if (currentMode == NavigationMode.AreaActions)
            {
                string action = areaActions[selectedActionIndex];

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
            Log.Message($"RimWorld Access: ExpandArea called, selectedArea = {selectedArea?.Label ?? "null"}");

            if (selectedArea != null)
            {
                // Save reference before Close() clears it
                Area areaToExpand = selectedArea;

                Log.Message("RimWorld Access: Closing area manager");
                Close();
                Log.Message($"RimWorld Access: Calling EnterExpandMode with {areaToExpand.Label}");
                AreaPaintingState.EnterExpandMode(areaToExpand);
            }
            else
            {
                Log.Message("RimWorld Access: selectedArea is null, cannot expand");
            }
        }

        /// <summary>
        /// Activates the shrink mode for the selected area.
        /// </summary>
        private static void ShrinkArea()
        {
            if (selectedArea != null)
            {
                // Save reference before Close() clears it
                Area areaToShrink = selectedArea;

                Close();
                AreaPaintingState.EnterShrinkMode(areaToShrink);
            }
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
        /// Updates the clipboard with the current selection.
        /// </summary>
        private static void UpdateClipboard()
        {
            if (currentMode == NavigationMode.AreaList)
            {
                if (selectedArea != null)
                {
                    int cellCount = selectedArea.TrueCount;
                    TolkHelper.Speak($"Area {selectedAreaIndex + 1}/{allAreas.Count}: {selectedArea.Label} ({cellCount} cells). Press Tab for actions.");
                }
                else
                {
                    TolkHelper.Speak("No areas available. Press Tab to create one.");
                }
            }
            else if (currentMode == NavigationMode.AreaActions)
            {
                string action = areaActions[selectedActionIndex];
                TolkHelper.Speak($"Action {selectedActionIndex + 1}/{areaActions.Length}: {action}. Press Enter to execute, Tab/Shift+Tab or arrows to navigate, Escape to return to area list.");
            }
        }
    }
}
