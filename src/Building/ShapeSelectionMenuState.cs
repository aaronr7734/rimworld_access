using System.Collections.Generic;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a windowless shape selection menu for building placement.
    /// Provides keyboard navigation through available shapes with typeahead support.
    /// </summary>
    public static class ShapeSelectionMenuState
    {
        /// <summary>
        /// Gets whether the shape selection menu is currently active.
        /// </summary>
        public static bool IsActive { get; private set; }

        /// <summary>
        /// Gets the currently selected shape type.
        /// </summary>
        public static ShapeType SelectedShape => selectedIndex >= 0 && selectedIndex < availableShapes.Count
            ? availableShapes[selectedIndex]
            : ShapeType.Manual;

        private static List<ShapeType> availableShapes = new List<ShapeType>();
        private static int selectedIndex = 0;
        private static Designator currentDesignator = null;
        private static TypeaheadSearchHelper typeaheadHelper = new TypeaheadSearchHelper();

        /// <summary>
        /// Opens the shape selection menu for the given designator.
        /// </summary>
        /// <param name="designator">The designator to get shapes for</param>
        public static void Open(Designator designator)
        {
            if (designator == null)
            {
                Log.Error("Cannot open shape selection menu: designator is null");
                return;
            }

            currentDesignator = designator;
            availableShapes = ShapeHelper.GetAvailableShapes(designator);
            selectedIndex = 0;
            IsActive = true;
            typeaheadHelper.ClearSearch();

            // Announce menu opening (NOT all shapes - just the menu name)
            TolkHelper.Speak("Shape menu");

            // Announce the first shape
            AnnounceCurrentShape();

            Log.Message($"Opened shape selection menu with {availableShapes.Count} shapes for {designator.Label}");
        }

        /// <summary>
        /// Closes the shape selection menu.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            currentDesignator = null;
            typeaheadHelper.ClearSearch();
        }

        /// <summary>
        /// Moves selection to the next shape.
        /// </summary>
        public static void SelectNext()
        {
            if (availableShapes == null || availableShapes.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, availableShapes.Count);
            AnnounceCurrentShape();
        }

        /// <summary>
        /// Moves selection to the previous shape.
        /// </summary>
        public static void SelectPrevious()
        {
            if (availableShapes == null || availableShapes.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, availableShapes.Count);
            AnnounceCurrentShape();
        }

        /// <summary>
        /// Announces the currently selected shape with position information and description.
        /// Format: "{shape name}, {position}. {description}" e.g., "Empty Rectangle, 2 of 5. Places only the border..."
        /// </summary>
        private static void AnnounceCurrentShape()
        {
            if (availableShapes == null || availableShapes.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= availableShapes.Count)
                return;

            ShapeType currentShape = availableShapes[selectedIndex];
            string shapeName = ShapeHelper.GetShapeName(currentShape);
            string description = ShapeHelper.GetShapeDescription(currentShape);
            string position = MenuHelper.FormatPosition(selectedIndex, availableShapes.Count);

            // Format: "Shape Name, position. Description"
            string announcement = string.IsNullOrEmpty(position)
                ? $"{shapeName}. {description}"
                : $"{shapeName}, {position}. {description}";

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Announces the current shape with typeahead search context.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (availableShapes == null || availableShapes.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= availableShapes.Count)
                return;

            string shapeName = ShapeHelper.GetShapeName(availableShapes[selectedIndex]);

            if (typeaheadHelper.HasActiveSearch)
            {
                TolkHelper.Speak($"{shapeName}, {typeaheadHelper.CurrentMatchPosition} of {typeaheadHelper.MatchCount} matches for '{typeaheadHelper.SearchBuffer}'");
            }
            else
            {
                AnnounceCurrentShape();
            }
        }

        /// <summary>
        /// Confirms the selection and returns the selected shape type.
        /// Announces "{shape name} selected" and closes the menu.
        /// </summary>
        /// <returns>The selected ShapeType</returns>
        public static ShapeType Confirm()
        {
            if (availableShapes == null || availableShapes.Count == 0)
            {
                Close();
                return ShapeType.Manual;
            }

            if (selectedIndex < 0 || selectedIndex >= availableShapes.Count)
            {
                Close();
                return ShapeType.Manual;
            }

            ShapeType selected = availableShapes[selectedIndex];
            string shapeName = ShapeHelper.GetShapeName(selected);

            TolkHelper.Speak($"{shapeName} selected");
            Log.Message($"Shape selected: {shapeName}");

            Close();
            return selected;
        }

        /// <summary>
        /// Gets the DrawStyleDef for the currently selected shape.
        /// Used to set the game's SelectedStyle when the user confirms.
        /// </summary>
        /// <returns>The DrawStyleDef, or null for Manual mode</returns>
        public static DrawStyleDef GetSelectedDrawStyleDef()
        {
            if (currentDesignator == null || selectedIndex < 0 || selectedIndex >= availableShapes.Count)
                return null;

            return ShapeHelper.GetDrawStyleDef(currentDesignator, availableShapes[selectedIndex]);
        }

        /// <summary>
        /// Jumps to the first shape in the list.
        /// </summary>
        public static void JumpToFirst()
        {
            if (availableShapes == null || availableShapes.Count == 0)
                return;

            selectedIndex = MenuHelper.JumpToFirst();
            typeaheadHelper.ClearSearch();
            AnnounceCurrentShape();
        }

        /// <summary>
        /// Jumps to the last shape in the list.
        /// </summary>
        public static void JumpToLast()
        {
            if (availableShapes == null || availableShapes.Count == 0)
                return;

            selectedIndex = MenuHelper.JumpToLast(availableShapes.Count);
            typeaheadHelper.ClearSearch();
            AnnounceCurrentShape();
        }

        /// <summary>
        /// Handles keyboard input for the shape selection menu.
        /// </summary>
        /// <param name="ev">The current event</param>
        /// <returns>True if input was handled, false otherwise</returns>
        public static bool HandleInput(Event ev)
        {
            if (!IsActive || availableShapes == null || availableShapes.Count == 0)
                return false;

            if (ev.type != EventType.KeyDown)
                return false;

            KeyCode key = ev.keyCode;

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

            // Handle Escape - clear search first, then cancel
            if (key == KeyCode.Escape)
            {
                if (typeaheadHelper.HasActiveSearch)
                {
                    typeaheadHelper.ClearSearchAndAnnounce();
                    AnnounceCurrentShape();
                    return true;
                }
                // Close without selecting
                TolkHelper.Speak("Shape selection cancelled");
                Close();
                return true;
            }

            // Handle Backspace for search
            if (key == KeyCode.Backspace && typeaheadHelper.HasActiveSearch)
            {
                var labels = GetShapeLabels();
                if (typeaheadHelper.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0)
                        selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
                return true;
            }

            // Handle Up arrow - navigate with search awareness
            if (key == KeyCode.UpArrow)
            {
                if (typeaheadHelper.HasActiveSearch && !typeaheadHelper.HasNoMatches)
                {
                    // Navigate through matches only
                    int prevIndex = typeaheadHelper.GetPreviousMatch(selectedIndex);
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
                return true;
            }

            // Handle Down arrow - navigate with search awareness
            if (key == KeyCode.DownArrow)
            {
                if (typeaheadHelper.HasActiveSearch && !typeaheadHelper.HasNoMatches)
                {
                    // Navigate through matches only
                    int nextIndex = typeaheadHelper.GetNextMatch(selectedIndex);
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
                return true;
            }

            // Handle Enter - confirm selection and enter shape placement mode
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                // Store the designator before Confirm() clears it
                Designator designatorForPlacement = currentDesignator;
                ShapeType selectedShape = Confirm();
                // Enter shape placement mode with the selected shape
                if (designatorForPlacement != null)
                {
                    ShapePlacementState.Enter(designatorForPlacement, selectedShape);
                }
                return true;
            }

            // Handle typeahead characters
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if (isLetter || isNumber)
            {
                TypeaheadCharacterBuffer.RequestCharacter(c =>
                {
                    var labels = GetShapeLabels();
                    if (typeaheadHelper.ProcessCharacterInput(c, labels, out int newIndex))
                    {
                        if (newIndex >= 0)
                        {
                            selectedIndex = newIndex;
                            AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        TolkHelper.Speak($"No matches for '{typeaheadHelper.LastFailedSearch}'");
                    }
                });
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the list of labels for all available shapes.
        /// Used for typeahead search.
        /// </summary>
        private static List<string> GetShapeLabels()
        {
            var labels = new List<string>();
            if (availableShapes != null)
            {
                foreach (var shape in availableShapes)
                {
                    labels.Add(ShapeHelper.GetShapeName(shape));
                }
            }
            return labels;
        }

        /// <summary>
        /// Gets whether typeahead search is currently active.
        /// </summary>
        public static bool HasActiveSearch => typeaheadHelper.HasActiveSearch;

        /// <summary>
        /// Gets the number of available shapes.
        /// </summary>
        public static int ShapeCount => availableShapes?.Count ?? 0;

        /// <summary>
        /// Gets the current designator that the menu was opened for.
        /// </summary>
        public static Designator CurrentDesignator => currentDesignator;
    }
}
