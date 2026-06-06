using System.Collections.Generic;
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
                    displayText += "RimWorldAccess.Building.Plant.MinSkillSuffix".Translate(def.plant.sowMinSkill);
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
                    details.Add("RimWorldAccess.Building.Plant.RequiresPlantsSkill".Translate(def.plant.sowMinSkill));
                }

                // Growth time
                float growDays = def.plant.growDays;
                if (growDays > 0)
                {
                    details.Add("RimWorldAccess.Building.Plant.GrowsInDays".Translate(growDays.ToString("F1")));
                }

                // Yield information
                if (def.plant.harvestedThingDef != null)
                {
                    string yieldInfo = "RimWorldAccess.Building.Plant.Yields".Translate(def.plant.harvestedThingDef.LabelCap);
                    if (def.plant.harvestYield > 0)
                    {
                        yieldInfo += "RimWorldAccess.Building.Plant.YieldMultiplier".Translate(def.plant.harvestYield);
                    }
                    details.Add(yieldInfo);
                }

                // Plant purpose
                string purpose;
                switch (def.plant.purpose)
                {
                    case PlantPurpose.Food:
                        purpose = "RimWorldAccess.Building.Plant.PurposeFood".Translate();
                        break;
                    case PlantPurpose.Health:
                        purpose = "RimWorldAccess.Building.Plant.PurposeHealth".Translate();
                        break;
                    case PlantPurpose.Beauty:
                        purpose = "RimWorldAccess.Building.Plant.PurposeBeauty".Translate();
                        break;
                    case PlantPurpose.Misc:
                        purpose = "RimWorldAccess.Building.Plant.PurposeMisc".Translate();
                        break;
                    default:
                        purpose = "RimWorldAccess.Building.Plant.PurposeUnknown".Translate();
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
                        details.Add("RimWorldAccess.Building.Plant.RoofWarning".Translate());
                    }
                }

                if (def.plant.cavePlant)
                {
                    details.Add("RimWorldAccess.Building.Plant.CavePlant".Translate());
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
            TolkHelper.Speak("RimWorldAccess.Building.PlantSelect.OpenPrompt".Loc(currentPlantName));

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
        /// Moves selection to the next plant among the current typeahead matches.
        /// Only meaningful while a search is active with matches.
        /// </summary>
        public static void SelectNextMatch()
        {
            if (availablePlants == null || availablePlants.Count == 0)
                return;

            int nextIndex = typeahead.GetNextMatch(selectedIndex);
            if (nextIndex >= 0)
            {
                selectedIndex = nextIndex;
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Moves selection to the previous plant among the current typeahead matches.
        /// Only meaningful while a search is active with matches.
        /// </summary>
        public static void SelectPreviousMatch()
        {
            if (availablePlants == null || availablePlants.Count == 0)
                return;

            int prevIndex = typeahead.GetPreviousMatch(selectedIndex);
            if (prevIndex >= 0)
            {
                selectedIndex = prevIndex;
                AnnounceWithSearch();
            }
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

            TolkHelper.Speak("RimWorldAccess.Building.PlantSelect.Selected".Loc(selected.displayText));
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

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                selectedIndex = typeahead.GetFirstMatch();
                AnnounceWithSearch();
                return;
            }

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

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                selectedIndex = typeahead.GetLastMatch();
                AnnounceWithSearch();
                return;
            }

            selectedIndex = MenuHelper.JumpToLast(availablePlants.Count);
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Opens an info card for the currently selected plant.
        /// </summary>
        public static void OpenInfoCard()
        {
            ThingDef plantDef = null;
            if (availablePlants != null && selectedIndex >= 0 && selectedIndex < availablePlants.Count)
            {
                plantDef = availablePlants[selectedIndex].plantDef;
            }
            InfoCardState.TryOpenInfoCardForDef(plantDef);
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
                typeahead.SpeakNoMatches();
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

        public static bool HasNoMatches => typeahead.HasNoMatches;

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

            // Handle Up arrow - navigate matches when searching, else normal navigation
            if (key == KeyCode.UpArrow)
            {
                if (HasActiveSearch && !HasNoMatches)
                    SelectPreviousMatch();
                else
                    SelectPrevious();
                Event.current.Use();
                return true;
            }

            // Handle Down arrow - navigate matches when searching, else normal navigation
            if (key == KeyCode.DownArrow)
            {
                if (HasActiveSearch && !HasNoMatches)
                    SelectNextMatch();
                else
                    SelectNext();
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

            // Handle Alt+I - open info card for selected plant
            if (key == KeyCode.I && KeyboardHelper.IsAltHeld)
            {
                OpenInfoCard();
                Event.current.Use();
                return true;
            }

            // Handle typeahead characters
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if ((isLetter || isNumber) && !KeyboardHelper.IsAltHeld)
            {
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
                TolkHelper.SpeakData(typeahead.BuildItemAnnouncement(current.displayText));
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
                string announcement = "RimWorldAccess.Building.PlantSelect.SelectionWithDetail".Translate(current.displayText, current.detailedInfo);
                TolkHelper.SpeakData(announcement);
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
                        TolkHelper.Speak("RimWorldAccess.Building.PlantSelect.NoColonistCanPlant".Loc(plantDef.label, plantDef.plant.sowMinSkill));
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
                    TolkHelper.Speak("RimWorldAccess.Building.PlantSelect.CavePlantExposed".Loc(plantDef.LabelCap));
                }
            }
        }
    }
}
