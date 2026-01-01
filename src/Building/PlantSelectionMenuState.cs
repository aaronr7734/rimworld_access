using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a windowless plant selection menu for growing zones.
    /// Provides keyboard navigation through available plants with detailed information.
    /// </summary>
    public static class PlantSelectionMenuState
    {
        private static List<PlantOption> availablePlants = null;
        private static int selectedIndex = 0;
        private static bool isActive = false;
        private static IPlantToGrowSettable currentSettable = null;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        private class PlantOption
        {
            public ThingDef plantDef;
            public string displayText;
            public string detailedInfo;

            public PlantOption(ThingDef def, Map map)
            {
                plantDef = def;

                // Build display text with skill requirements
                displayText = def.LabelCap;
                if (def.plant.sowMinSkill > 0)
                {
                    displayText += $" (Min Skill: {def.plant.sowMinSkill})";
                }

                // Build detailed info with game description and stats
                List<string> details = new List<string>();

                // Add the game's description if available
                if (!string.IsNullOrEmpty(def.description))
                {
                    details.Add(def.description);
                }

                // Skill requirement
                if (def.plant.sowMinSkill > 0)
                {
                    details.Add($"Requires Plants skill {def.plant.sowMinSkill}");
                }

                // Growth time
                float growDays = def.plant.growDays;
                if (growDays > 0)
                {
                    details.Add($"Grows in {growDays:F1} days");
                }

                // Yield information
                if (def.plant.harvestedThingDef != null)
                {
                    string yieldInfo = $"Yields {def.plant.harvestedThingDef.LabelCap}";
                    if (def.plant.harvestYield > 0)
                    {
                        yieldInfo += $" (x{def.plant.harvestYield})";
                    }
                    details.Add(yieldInfo);
                }

                // Plant purpose
                string purpose;
                switch (def.plant.purpose)
                {
                    case PlantPurpose.Food:
                        purpose = "Food crop";
                        break;
                    case PlantPurpose.Health:
                        purpose = "Medical plant";
                        break;
                    case PlantPurpose.Beauty:
                        purpose = "Decorative plant";
                        break;
                    case PlantPurpose.Misc:
                        purpose = "Miscellaneous";
                        break;
                    default:
                        purpose = "Unknown";
                        break;
                }
                details.Add(purpose);

                // Check for special requirements
                if (def.plant.interferesWithRoof)
                {
                    bool hasRoof = false;
                    foreach (IntVec3 cell in currentSettable.Cells)
                    {
                        if (cell.Roofed(map))
                        {
                            hasRoof = true;
                            break;
                        }
                    }
                    if (hasRoof)
                    {
                        details.Add("WARNING: Requires no roof, but zone has roofed cells");
                    }
                }

                if (def.plant.cavePlant)
                {
                    details.Add("Cave plant - requires darkness");
                }

                detailedInfo = string.Join(". ", details);
            }
        }

        /// <summary>
        /// Gets whether the plant selection menu is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the plant selection menu for the given growing zone.
        /// </summary>
        public static void Open(IPlantToGrowSettable settable)
        {
            if (settable == null)
            {
                Log.Error("Cannot open plant selection menu: settable is null");
                return;
            }

            currentSettable = settable;
            availablePlants = new List<PlantOption>();
            selectedIndex = 0;
            isActive = true;
            typeahead.ClearSearch();

            // Get list of available plants
            List<IPlantToGrowSettable> settables = new List<IPlantToGrowSettable> { settable };
            List<ThingDef> validPlants = new List<ThingDef>();

            foreach (ThingDef plantDef in PlantUtility.ValidPlantTypesForGrowers(settables))
            {
                if (IsPlantAvailable(plantDef, settable.Map))
                {
                    validPlants.Add(plantDef);
                }
            }

            // Sort plants by priority (Food > Health > Beauty > Misc), then alphabetically
            validPlants.SortBy(
                (ThingDef x) => 0f - GetPlantListPriority(x),
                (ThingDef x) => x.label
            );

            // Build plant options with detailed information
            foreach (ThingDef plantDef in validPlants)
            {
                availablePlants.Add(new PlantOption(plantDef, settable.Map));
            }

            // Find currently selected plant
            ThingDef currentPlant = settable.GetPlantDefToGrow();
            string currentPlantName = "None";
            if (currentPlant != null)
            {
                currentPlantName = currentPlant.LabelCap;
                for (int i = 0; i < availablePlants.Count; i++)
                {
                    if (availablePlants[i].plantDef == currentPlant)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            // Announce menu opening with current crop
            TolkHelper.Speak($"Plant selection. Current crop: {currentPlantName}");

            // Announce first/current plant
            AnnounceCurrentSelection();

            Log.Message($"Opened plant selection menu with {availablePlants.Count} plants. Current: {currentPlantName}");
        }

        /// <summary>
        /// Closes the plant selection menu.
        /// </summary>
        public static void Close()
        {
            availablePlants = null;
            selectedIndex = 0;
            isActive = false;
            currentSettable = null;
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Moves selection to the next plant.
        /// </summary>
        public static void SelectNext()
        {
            if (availablePlants == null || availablePlants.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, availablePlants.Count);
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Moves selection to the previous plant.
        /// </summary>
        public static void SelectPrevious()
        {
            if (availablePlants == null || availablePlants.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, availablePlants.Count);
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Selects the currently highlighted plant.
        /// </summary>
        public static void ConfirmSelection()
        {
            if (availablePlants == null || availablePlants.Count == 0)
            {
                Close();
                return;
            }

            if (selectedIndex < 0 || selectedIndex >= availablePlants.Count)
            {
                Close();
                return;
            }

            PlantOption selected = availablePlants[selectedIndex];
            ThingDef plantDef = selected.plantDef;

            // Set the plant
            currentSettable.SetPlantDefToGrow(plantDef);

            // Check for warnings
            CheckAndWarnAboutPlant(plantDef);

            TolkHelper.Speak($"Selected: {selected.displayText}");
            Log.Message($"Set plant to: {plantDef.label}");

            Close();
        }

        /// <summary>
        /// Jumps to the first plant in the list.
        /// </summary>
        public static void JumpToFirst()
        {
            if (availablePlants == null || availablePlants.Count == 0)
                return;

            selectedIndex = MenuHelper.JumpToFirst();
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the last plant in the list.
        /// </summary>
        public static void JumpToLast()
        {
            if (availablePlants == null || availablePlants.Count == 0)
                return;

            selectedIndex = MenuHelper.JumpToLast(availablePlants.Count);
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Handles typeahead character input for the plant selection menu.
        /// Called from StorageSettingsMenuPatch to process alphanumeric characters.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive || availablePlants == null || availablePlants.Count == 0)
                return;

            var labels = GetPlantLabels();
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

        /// <summary>
        /// Handles backspace key for typeahead search.
        /// Called from StorageSettingsMenuPatch.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!isActive || availablePlants == null || availablePlants.Count == 0)
                return;

            if (!typeahead.HasActiveSearch)
                return;

            var labels = GetPlantLabels();
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
        /// Gets whether typeahead search is active.
        /// </summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Handles keyboard input for the plant selection menu, including typeahead search.
        /// </summary>
        /// <returns>True if input was handled, false otherwise.</returns>
        public static bool HandleInput()
        {
            if (!isActive || availablePlants == null || availablePlants.Count == 0)
                return false;

            if (Event.current.type != EventType.KeyDown)
                return false;

            KeyCode key = Event.current.keyCode;

            // Handle Home - jump to first
            if (key == KeyCode.Home)
            {
                JumpToFirst();
                Event.current.Use();
                return true;
            }

            // Handle End - jump to last
            if (key == KeyCode.End)
            {
                JumpToLast();
                Event.current.Use();
                return true;
            }

            // Handle Escape - clear search FIRST, then close
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentSelection();
                    Event.current.Use();
                    return true;
                }
                // Let the caller handle normal escape (close menu)
                return false;
            }

            // Handle Backspace for search
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = GetPlantLabels();
                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0)
                        selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
                Event.current.Use();
                return true;
            }

            // Handle Up arrow - navigate with search awareness
            if (key == KeyCode.UpArrow)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    // Navigate through matches only when there ARE matches
                    int prevIndex = typeahead.GetPreviousMatch(selectedIndex);
                    if (prevIndex >= 0)
                    {
                        selectedIndex = prevIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    // Navigate normally (either no search active, OR search with no matches)
                    SelectPrevious();
                }
                Event.current.Use();
                return true;
            }

            // Handle Down arrow - navigate with search awareness
            if (key == KeyCode.DownArrow)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    // Navigate through matches only when there ARE matches
                    int nextIndex = typeahead.GetNextMatch(selectedIndex);
                    if (nextIndex >= 0)
                    {
                        selectedIndex = nextIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    // Navigate normally (either no search active, OR search with no matches)
                    SelectNext();
                }
                Event.current.Use();
                return true;
            }

            // Handle Enter - confirm selection
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                ConfirmSelection();
                Event.current.Use();
                return true;
            }

            // Handle typeahead characters
            // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if (isLetter || isNumber)
            {
                char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                var labels = GetPlantLabels();
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
                Event.current.Use();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the list of labels for all plants.
        /// </summary>
        private static List<string> GetPlantLabels()
        {
            var labels = new List<string>();
            if (availablePlants != null)
            {
                foreach (var plant in availablePlants)
                {
                    string label = plant.plantDef.LabelCap;
                    labels.Add(string.IsNullOrEmpty(label) ? "" : label);
                }
            }
            return labels;
        }

        /// <summary>
        /// Announces the current selection with search context if applicable.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (availablePlants == null || availablePlants.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= availablePlants.Count)
                return;

            PlantOption current = availablePlants[selectedIndex];

            if (typeahead.HasActiveSearch)
            {
                TolkHelper.Speak($"{current.displayText}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
            }
            else
            {
                AnnounceCurrentSelection();
            }
        }

        private static void AnnounceCurrentSelection()
        {
            if (selectedIndex >= 0 && selectedIndex < availablePlants.Count)
            {
                PlantOption current = availablePlants[selectedIndex];
                string announcement = $"{current.displayText}. {current.detailedInfo}";
                TolkHelper.Speak(announcement);
            }
        }

        private static bool IsPlantAvailable(ThingDef plantDef, Map map)
        {
            // Check research prerequisites
            List<ResearchProjectDef> sowResearchPrerequisites = plantDef.plant.sowResearchPrerequisites;
            if (sowResearchPrerequisites != null)
            {
                for (int i = 0; i < sowResearchPrerequisites.Count; i++)
                {
                    if (!sowResearchPrerequisites[i].IsFinished)
                    {
                        return false;
                    }
                }
            }

            // Check if requires permanent darkness
            if (plantDef.plant.mustBePermanentDarknessToSow && !map.gameConditionManager.IsAlwaysDarkOutside)
            {
                return false;
            }

            // Check if must be wild
            if (plantDef.plant.mustBeWildToSow && !map.wildPlantSpawner.AllWildPlants.Contains(plantDef))
            {
                return false;
            }

            return true;
        }

        private static float GetPlantListPriority(ThingDef plantDef)
        {
            if (plantDef.plant.IsTree)
            {
                return 1f;
            }

            switch (plantDef.plant.purpose)
            {
                case PlantPurpose.Food:
                    return 4f;
                case PlantPurpose.Health:
                    return 3f;
                case PlantPurpose.Beauty:
                    return 2f;
                case PlantPurpose.Misc:
                    return 0f;
                default:
                    return 0f;
            }
        }

        private static void CheckAndWarnAboutPlant(ThingDef plantDef)
        {
            // Check if any colonist can plant it
            if (plantDef.plant.sowMinSkill > 0)
            {
                bool hasSkilled = false;
                foreach (Pawn colonist in currentSettable.Map.mapPawns.FreeColonistsSpawned)
                {
                    if (colonist.skills.GetSkill(SkillDefOf.Plants).Level >= plantDef.plant.sowMinSkill
                        && !colonist.Downed
                        && colonist.workSettings.WorkIsActive(WorkTypeDefOf.Growing))
                    {
                        hasSkilled = true;
                        break;
                    }
                }

                if (!hasSkilled)
                {
                    // Check for mechanoids if Biotech is active
                    bool hasMech = false;
                    if (ModsConfig.BiotechActive)
                    {
                        hasMech = MechanitorUtility.AnyPlayerMechCanDoWork(WorkTypeDefOf.Growing, plantDef.plant.sowMinSkill, out var _);
                    }

                    if (!hasMech)
                    {
                        TolkHelper.Speak($"WARNING: No colonist can plant {plantDef.label} (requires Plants skill {plantDef.plant.sowMinSkill})");
                    }
                }
            }

            // Check for roof/light warnings for cave plants
            if (plantDef.plant.cavePlant || plantDef.plant.diesToLight)
            {
                IntVec3 problemCell = IntVec3.Invalid;
                bool isAlwaysDark = currentSettable.Map.gameConditionManager.IsAlwaysDarkOutside;

                foreach (IntVec3 cell in currentSettable.Cells)
                {
                    bool isRoofed = !isAlwaysDark || cell.Roofed(currentSettable.Map);
                    bool isDark = currentSettable.Map.glowGrid.GroundGlowAt(cell, ignoreCavePlants: true) <= 0f;

                    if (!isRoofed || !isDark)
                    {
                        problemCell = cell;
                        break;
                    }
                }

                if (problemCell.IsValid)
                {
                    TolkHelper.Speak($"WARNING: {plantDef.LabelCap} is a cave plant but zone has cells exposed to light");
                }
            }
        }
    }
}
