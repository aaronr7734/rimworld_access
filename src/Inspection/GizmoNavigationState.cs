using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.Sound;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Maintains the state of gizmo (command button) navigation for accessibility features.
    /// Tracks the currently selected gizmo when browsing available commands with the G key.
    /// </summary>
    public static class GizmoNavigationState
    {
        private static bool isActive = false;
        private static int selectedGizmoIndex = 0;
        private static List<Gizmo> availableGizmos = new List<Gizmo>();
        private static Dictionary<Gizmo, ISelectable> gizmoOwners = new Dictionary<Gizmo, ISelectable>();
        private static ISelectable lastAnnouncedOwner = null;
        private static bool pawnJustSelected = false;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private static bool isExecutingGizmo = false;

        /// <summary>
        /// Gets whether gizmo navigation is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets whether a gizmo is currently being executed.
        /// Used by DialogInterceptionPatch to know when to intercept FloatMenus.
        /// </summary>
        public static bool IsExecutingGizmo => isExecutingGizmo;

        /// <summary>
        /// Gets or sets whether a pawn was just selected via , or . keys.
        /// This flag is cleared when the user navigates the map with arrow keys.
        /// </summary>
        public static bool PawnJustSelected
        {
            get => pawnJustSelected;
            set => pawnJustSelected = value;
        }

        /// <summary>
        /// Gets the currently selected gizmo index.
        /// </summary>
        public static int SelectedGizmoIndex => selectedGizmoIndex;

        /// <summary>
        /// Gets the list of available gizmos.
        /// </summary>
        public static List<Gizmo> AvailableGizmos => availableGizmos;

        /// <summary>
        /// Opens the gizmo navigation menu by collecting gizmos from selected objects.
        /// </summary>
        public static void Open()
        {
            if (Find.Selector == null || Find.CurrentMap == null)
                return;

            // Collect gizmos from all selected objects
            availableGizmos.Clear();
            gizmoOwners.Clear();
            lastAnnouncedOwner = null;

            foreach (object obj in Find.Selector.SelectedObjects)
            {
                if (obj is ISelectable selectable)
                {
                    var gizmos = selectable.GetGizmos().ToList();
                    foreach (var gizmo in gizmos.Where(g => g != null && g.Visible && !ShouldSkipGizmo(g)))
                    {
                        availableGizmos.Add(gizmo);
                        gizmoOwners[gizmo] = selectable;
                    }
                }
            }

            // Sort by Order property (lower values appear first)
            availableGizmos = availableGizmos
                .OrderBy(g => g.Order)
                .ToList();

            if (availableGizmos.Count == 0)
            {
                TolkHelper.Speak("No commands available");
                return;
            }

            // Start at the first gizmo
            selectedGizmoIndex = 0;
            isActive = true;
            typeahead.ClearSearch();

            // Announce the first gizmo
            AnnounceCurrentGizmo();
        }

        /// <summary>
        /// Opens the gizmo navigation menu by collecting gizmos from objects at the cursor position.
        /// </summary>
        public static void OpenAtCursor(IntVec3 cursorPosition, Map map)
        {
            if (map == null)
                return;

            // Validate cursor position
            if (!cursorPosition.IsValid || !cursorPosition.InBounds(map))
            {
                TolkHelper.Speak("Invalid cursor position");
                return;
            }

            availableGizmos.Clear();
            gizmoOwners.Clear();

            // Store the current selection to restore it later
            var previousSelection = Find.Selector.SelectedObjects.ToList();

            try
            {
                // Check for zone at cursor position and collect its gizmos
                Zone zone = cursorPosition.GetZone(map);
                if (zone != null)
                {
                    // Temporarily select the zone so its gizmos' Visible property works correctly
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(zone, playSound: false, forceDesignatorDeselect: false);

                    var zoneGizmos = zone.GetGizmos().ToList();
                    foreach (Gizmo gizmo in zoneGizmos.Where(g => g != null && g.Visible && !ShouldSkipGizmo(g)))
                    {
                        availableGizmos.Add(gizmo);
                        gizmoOwners[gizmo] = zone;
                    }
                }

                // Get all things at the cursor position
                List<Thing> thingsAtPosition = cursorPosition.GetThingList(map);

                // Collect gizmos from all things at this position
                // Important: Temporarily select each thing before getting its gizmos,
                // because some gizmos (like Designator_Install) check if the thing is selected
                // to determine their Visible property
                if (thingsAtPosition != null)
                {
                    foreach (ISelectable selectable in thingsAtPosition.OfType<ISelectable>())
                    {
                        // Temporarily select this thing so its gizmos' Visible property works correctly
                        Find.Selector.ClearSelection();
                        Find.Selector.Select(selectable, playSound: false, forceDesignatorDeselect: false);

                        var gizmos = selectable.GetGizmos().ToList();
                        foreach (Gizmo gizmo in gizmos.Where(g => g != null && g.Visible && !ShouldSkipGizmo(g)))
                        {
                            // Check Visible NOW while thing is still selected
                            // (some gizmos like Designator_Install check selection state)
                            availableGizmos.Add(gizmo);
                            gizmoOwners[gizmo] = selectable;
                        }
                    }
                }
            }
            finally
            {
                // Restore previous selection (or clear if nothing was selected)
                Find.Selector.ClearSelection();
                foreach (var obj in previousSelection.OfType<ISelectable>())
                {
                    Find.Selector.Select(obj, playSound: false, forceDesignatorDeselect: false);
                }
            }

            // Sort by Order property (lower values appear first)
            availableGizmos = availableGizmos
                .OrderBy(g => g.Order)
                .ToList();

            if (availableGizmos.Count == 0)
            {
                TolkHelper.Speak("No commands available at cursor position");
                return;
            }

            // Start at the first gizmo
            selectedGizmoIndex = 0;
            isActive = true;
            typeahead.ClearSearch();
            lastAnnouncedOwner = null;

            // Announce the first gizmo (will include object name as prefix)
            AnnounceCurrentGizmo();
        }

        /// <summary>
        /// Opens the gizmo navigation menu by collecting gizmos from selected world objects (caravans, settlements, etc.).
        /// Used when pressing G on the world map.
        /// </summary>
        public static void OpenFromWorldObjects()
        {
            List<WorldObject> objectsToUse = new List<WorldObject>();
            int cursorTile = -1;

            // First priority: Check for world objects at our WorldNavigationState cursor position
            if (WorldNavigationState.IsActive && WorldNavigationState.CurrentSelectedTile.Valid)
            {
                cursorTile = WorldNavigationState.CurrentSelectedTile;
                var objectsAtTile = Find.WorldObjects?.ObjectsAt(cursorTile);
                if (objectsAtTile != null)
                {
                    objectsToUse.AddRange(objectsAtTile);
                }
            }

            // Second priority: Fall back to game's selection if we didn't find anything at cursor
            if (objectsToUse.Count == 0 && Find.WorldSelector != null)
            {
                var selectedObjects = Find.WorldSelector.SelectedObjects;
                if (selectedObjects != null)
                {
                    objectsToUse.AddRange(selectedObjects);
                }
            }

            if (objectsToUse.Count == 0)
            {
                TolkHelper.Speak("No world object at this tile");
                return;
            }

            // Check multi-selection - only use it if ALL multi-selected caravans are on the cursor tile
            // This allows merge when caravans are together, but shows single caravan gizmos when apart
            var multiSelected = WorldNavigationState.GetMultiSelectedCaravans();
            bool allOnSameTile = multiSelected.Count > 1 &&
                multiSelected.All(c => c != null && !c.Destroyed && c.Tile == cursorTile);

            if (allOnSameTile && Find.WorldSelector != null)
            {
                // All multi-selected caravans are on cursor tile - sync selection for merge gizmo
                Find.WorldSelector.ClearSelection();
                foreach (var caravan in multiSelected)
                {
                    Find.WorldSelector.Select(caravan, playSound: false);
                }

                // IMPORTANT: When we have multi-selected caravans, only collect gizmos from THOSE caravans
                // not from other objects at the tile. This prevents the merge gizmo from being associated
                // with an unselected caravan (which would cause all caravans at tile to merge).
                objectsToUse.Clear();
                objectsToUse.AddRange(multiSelected.Cast<WorldObject>());
            }

            availableGizmos.Clear();
            gizmoOwners.Clear();

            // When we have multi-selected caravans all on the same tile, keep them selected
            // so that gizmos like "Merge Selected Caravans" work correctly
            bool hasMultiSelection = allOnSameTile;

            // Collect gizmos from world objects (either all at tile, or just multi-selected)
            foreach (WorldObject worldObj in objectsToUse)
            {
                if (worldObj == null)
                    continue;

                // For multi-selection, don't clear - we need all caravans to stay selected
                // For single selection, temporarily select to get gizmos
                if (!hasMultiSelection && Find.WorldSelector != null)
                {
                    bool needsSelection = Find.WorldSelector.SingleSelectedObject != worldObj;
                    if (needsSelection)
                    {
                        Find.WorldSelector.ClearSelection();
                        Find.WorldSelector.Select(worldObj);
                    }
                }

                var gizmos = worldObj.GetGizmos();
                if (gizmos != null)
                {
                    foreach (Gizmo gizmo in gizmos.Where(g => g != null && g.Visible && !ShouldSkipGizmo(g)))
                    {
                        // Skip Gizmo_CaravanInfo - it's display-only with no click action
                        if (gizmo.GetType().Name == "Gizmo_CaravanInfo")
                            continue;

                        // Only skip true duplicate gizmos - ones that affect all selected objects together
                        // like "Merge Selected Caravans". Most gizmos (Settle, Split, etc.) are per-object
                        // and should be shown for each caravan even if they have the same label.
                        string gizmoLabel = (gizmo as Command)?.Label ?? gizmo.GetType().Name;
                        bool isMergeCommand = gizmoLabel.ToLower().Contains("merge");

                        bool isDuplicate = false;
                        if (isMergeCommand)
                        {
                            // Merge commands are truly shared - only show once
                            isDuplicate = availableGizmos.Any(g =>
                            {
                                string existingLabel = (g as Command)?.Label ?? g.GetType().Name;
                                return existingLabel.ToLower().Contains("merge");
                            });
                        }

                        if (!isDuplicate)
                        {
                            availableGizmos.Add(gizmo);
                            gizmoOwners[gizmo] = worldObj;
                        }
                    }
                }
            }

            // Sort by Order property (lower values appear first)
            availableGizmos = availableGizmos
                .OrderBy(g => g.Order)
                .ToList();

            if (availableGizmos.Count == 0)
            {
                string objName = objectsToUse.FirstOrDefault()?.LabelCap ?? "world object";
                TolkHelper.Speak($"No commands available for {objName}");
                return;
            }

            // Start at the first gizmo
            selectedGizmoIndex = 0;
            isActive = true;
            typeahead.ClearSearch();
            lastAnnouncedOwner = null;

            // Announce the first gizmo (will include object name as prefix)
            AnnounceCurrentGizmo();
        }

        /// <summary>
        /// Closes the gizmo navigation menu.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            selectedGizmoIndex = 0;
            availableGizmos.Clear();
            gizmoOwners.Clear();
            typeahead.ClearSearch();
            lastAnnouncedOwner = null;
        }

        /// <summary>
        /// Selects the next gizmo in the list.
        /// </summary>
        public static void SelectNext()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            selectedGizmoIndex = MenuHelper.SelectNext(selectedGizmoIndex, availableGizmos.Count);
            AnnounceCurrentGizmo();
        }

        /// <summary>
        /// Selects the previous gizmo in the list.
        /// </summary>
        public static void SelectPrevious()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            selectedGizmoIndex = MenuHelper.SelectPrevious(selectedGizmoIndex, availableGizmos.Count);
            AnnounceCurrentGizmo();
        }

        /// <summary>
        /// Executes the currently selected gizmo.
        /// </summary>
        public static void ExecuteSelected()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            if (selectedGizmoIndex < 0 || selectedGizmoIndex >= availableGizmos.Count)
                return;

            Gizmo selectedGizmo = availableGizmos[selectedGizmoIndex];
            string gizmoLabel = GetGizmoLabel(selectedGizmo);

            // Check if disabled
            if (selectedGizmo.Disabled)
            {
                string reason = selectedGizmo.disabledReason;
                if (string.IsNullOrEmpty(reason))
                    reason = "Command not available";

                string announcement = $"Disabled: {reason}";

                // Add helpful context for specific disabled scenarios (e.g., transport pod mass)
                ISelectable gizmoOwner = null;
                if (gizmoOwners.Count > 0)
                    gizmoOwners.TryGetValue(selectedGizmo, out gizmoOwner);
                string context = GetDisabledGizmoContext(selectedGizmo, gizmoOwner);
                if (!string.IsNullOrEmpty(context))
                    announcement += $". {context}";

                TolkHelper.Speak(announcement);
                return;
            }

            // Create a fake event to trigger the gizmo
            Event fakeEvent = new Event();
            fakeEvent.type = EventType.Used;

            // Set flag so DialogInterceptionPatch knows to intercept FloatMenus
            // (e.g., scanner mineral selection menu)
            isExecutingGizmo = true;

            try
            {
                // Special handling for different gizmo types

                // 1. Designator (like Reinstall, Copy) - enters placement mode
                if (selectedGizmo is Designator designator)
                {
                    // Check if this is a Designator_Build that needs material selection
                    if (designator is Designator_Build buildDesignator)
                    {
                        BuildableDef buildable = buildDesignator.PlacingDef;
                        if (ArchitectHelper.RequiresMaterialSelection(buildable))
                        {
                            // Handle material selection explicitly
                            HandleBuildDesignatorMaterialSelection(buildDesignator, buildable);
                            return;
                        }
                    }

                    // Check for zone expand/shrink designators - use accessible expansion mode
                    string designatorTypeName = designator.GetType().Name;

                    // Zone expand designators (Designator_ZoneAdd*_Expand)
                    // Route through normal designator flow for viewing mode support
                    if (designatorTypeName.Contains("_Expand")
                        && designatorTypeName.Contains("ZoneAdd"))
                    {
                        Close();
                        // Select the designator - DesignatorManagerPatch will route it to ShapePlacementState
                        Find.DesignatorManager.Select(designator);
                        return;
                    }

                    // Zone shrink designator - route through normal designator flow
                    if (designatorTypeName == "Designator_ZoneDelete_Shrink")
                    {
                        // Select the zone owner so GetSelectedZone() works in ShapePlacementState
                        if (gizmoOwners.ContainsKey(selectedGizmo))
                        {
                            ISelectable owner = gizmoOwners[selectedGizmo];
                            Find.Selector.ClearSelection();
                            Find.Selector.Select(owner, playSound: false, forceDesignatorDeselect: false);
                        }
                        Close();
                        // Select the designator - DesignatorManagerPatch will route it to ShapePlacementState
                        Find.DesignatorManager.Select(designator);
                        return;
                    }

                    // For Designators opened via cursor objects (not selected pawns),
                    // we need to ensure the correct object is selected
                    // so the Designator has proper context (e.g., Designator_Install needs to know what to reinstall)
                    if (!PawnJustSelected && gizmoOwners.ContainsKey(selectedGizmo))
                    {
                        ISelectable owner = gizmoOwners[selectedGizmo];
                        // Use WorldSelector for WorldObjects, Selector for map Things
                        if (owner is WorldObject worldObj && Find.WorldSelector != null)
                        {
                            Find.WorldSelector.ClearSelection();
                            Find.WorldSelector.Select(worldObj, playSound: false);
                        }
                        else if (Find.Selector != null)
                        {
                            Find.Selector.ClearSelection();
                            Find.Selector.Select(owner, playSound: false, forceDesignatorDeselect: false);
                        }
                    }

                    try
                    {
                        // Call ProcessInput to let the Designator do its preparation work
                        // (Designator_Install does setup like canceling existing blueprints)
                        selectedGizmo.ProcessInput(fakeEvent);

                        // Validate that the designator was actually selected
                        if (Find.DesignatorManager != null && Find.DesignatorManager.SelectedDesignator != null)
                        {
                            // Close the gizmo menu BEFORE entering placement mode
                            Close();

                            // Enter our accessible placement mode for consistent behavior
                            // (Space places blueprint, Enter confirms, allows repositioning)
                            ArchitectState.EnterPlacementMode(designator);
                            return;
                        }
                        else
                        {
                            TolkHelper.Speak($"Error: {gizmoLabel} could not be activated. Check if the item can be placed.", SpeechPriority.High);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        ModLogger.Error($"Exception in Designator execution: {ex.Message}");
                        TolkHelper.Speak($"Error executing {gizmoLabel}: {ex.Message}", SpeechPriority.High);
                    }

                    // Close the gizmo menu AFTER announcing (only reached on error)
                    Close();
                    return;
                }

                // For non-Designator gizmos, also select the owner so FloatMenu actions work correctly
                // (some actions check Find.Selector.SelectedObjects or Find.WorldSelector.SelectedObjects)
                if (!PawnJustSelected && gizmoOwners.ContainsKey(selectedGizmo))
                {
                    ISelectable owner = gizmoOwners[selectedGizmo];
                    // Use WorldSelector for WorldObjects, Selector for map Things
                    if (owner is WorldObject worldObj && Find.WorldSelector != null)
                    {
                        Find.WorldSelector.ClearSelection();

                        // For caravans, check if we have multi-selected caravans that need to be selected
                        // (e.g., for "Merge Selected Caravans" gizmo)
                        var multiSelected = WorldNavigationState.GetMultiSelectedCaravans();
                        if (multiSelected.Count > 0 && owner is Caravan ownerCaravan)
                        {
                            // IMPORTANT: Select the gizmo's owner caravan FIRST!
                            // RimWorld's merge command checks Find.WorldSelector.FirstSelectedObject == caravan
                            // where 'caravan' is the specific caravan that generated the gizmo.
                            // FirstSelectedObject is the first one added to the selection.
                            Find.WorldSelector.Select(ownerCaravan, playSound: false);

                            // Then select the other caravans
                            foreach (var caravan in multiSelected)
                            {
                                if (caravan != ownerCaravan)
                                {
                                    Find.WorldSelector.Select(caravan, playSound: false);
                                }
                            }
                        }
                        else
                        {
                            Find.WorldSelector.Select(worldObj, playSound: false);
                        }
                    }
                    else if (Find.Selector != null)
                    {
                        Find.Selector.ClearSelection();
                        Find.Selector.Select(owner, playSound: false, forceDesignatorDeselect: false);
                    }
                }

                // 2. Command_SetPlantToGrow - open accessible plant selection menu
                if (selectedGizmo is Command_SetPlantToGrow
                    && gizmoOwners.TryGetValue(selectedGizmo, out ISelectable plantOwner)
                    && plantOwner is IPlantToGrowSettable plantSettable)
                {
                    Close();
                    PlantSelectionMenuState.Open(plantSettable);
                    return;
                }

                // 3. PsychicEntropyGizmo - toggle neural heat limiter
                if (selectedGizmo.GetType().Name == "PsychicEntropyGizmo")
                {
                    bool newState = ToggleNeuralHeatLimiter(selectedGizmo);
                    string stateStr = newState ? "ON" : "OFF";
                    TolkHelper.Speak($"Neural heat limiter: {stateStr}");
                    return;
                }

                // 4. Command_Toggle - toggle and announce state
                if (selectedGizmo is Command_Toggle toggle)
                {
                    try
                    {
                        // Execute the toggle
                        selectedGizmo.ProcessInput(fakeEvent);

                        // Announce the new state
                        bool toggleActive = toggle.isActive?.Invoke() ?? false;
                        string state = toggleActive ? "ON" : "OFF";
                        string label = GetGizmoLabel(toggle);
                        TolkHelper.Speak($"{label}: {state}");
                    }
                    catch (System.Exception ex)
                    {
                        ModLogger.Error($"Exception in Command_Toggle execution: {ex.Message}");
                        TolkHelper.Speak($"Error executing {gizmoLabel}: {ex.Message}", SpeechPriority.High);
                    }
                }
                else
                {
                    // 5. Command_VerbTarget (weapon attacks) - announce targeting mode
                    if (selectedGizmo is Command_VerbTarget verbTarget)
                    {
                        try
                        {
                            // Execute the command
                            selectedGizmo.ProcessInput(fakeEvent);

                            string weaponName = verbTarget.ownerThing?.LabelCap ?? "weapon";
                            string verbLabel = verbTarget.verb?.ReportLabel ?? "attack";
                            TolkHelper.Speak($"{weaponName} {verbLabel} - Use map navigation to select target, then press Enter");
                        }
                        catch (System.Exception ex)
                        {
                            ModLogger.Error($"Exception in Command_VerbTarget execution: {ex.Message}");
                            TolkHelper.Speak($"Error executing {gizmoLabel}: {ex.Message}", SpeechPriority.High);
                        }
                    }
                    // 6. Command_Target - announce targeting mode
                    else if (selectedGizmo is Command_Target cmdTarget)
                    {
                        try
                        {
                            // Execute the command
                            selectedGizmo.ProcessInput(fakeEvent);

                            // Detect animal attack target commands (Odyssey DLC) by icon match.
                            // Both group (from master's Pawn_PlayerSettings) and individual (from animal's
                            // Pawn_TrainingTracker) use the same AttackTargetTexture icon.
                            if (cmdTarget.icon == Pawn_TrainingTracker.AttackTargetTexture
                                && gizmoOwners.TryGetValue(selectedGizmo, out ISelectable attackOwner)
                                && attackOwner is Pawn ownerPawn)
                            {
                                // Determine the master pawn:
                                // - Group command: owner IS the drafted master colonist
                                // - Individual command: owner is the animal, master is its assigned master
                                Pawn master = (ownerPawn.IsColonist && ownerPawn.Drafted)
                                    ? ownerPawn
                                    : ownerPawn.playerSettings?.Master;

                                if (master?.Position.IsValid == true)
                                {
                                    float range = Pawn_TrainingTracker.AttackTargetRange;
                                    TargetingPatch.SetTargetingContext(master.Position, range);
                                    TolkHelper.Speak(
                                        $"{gizmoLabel} targeting. Range: {range:F0} tiles from master. Press R to check distance.");
                                }
                                else
                                {
                                    TolkHelper.Speak(
                                        $"{gizmoLabel} - Use map navigation to select target, then press Enter");
                                }
                            }
                            else
                            {
                                TolkHelper.Speak(
                                    $"{gizmoLabel} - Use map navigation to select target, then press Enter");
                            }
                        }
                        catch (System.Exception ex)
                        {
                            ModLogger.Error($"Exception in Command_Target execution: {ex.Message}");
                            TolkHelper.Speak($"Error executing {gizmoLabel}: {ex.Message}", SpeechPriority.High);
                        }
                    }
                    // 7. Command_Ability (psycasts, abilities) - announce casting or targeting
                    else if (selectedGizmo is Command_Ability cmdAbility)
                    {
                        try
                        {
                            selectedGizmo.ProcessInput(fakeEvent);

                            // Self-cast abilities (targetRequired == false) don't enter targeting mode,
                            // so no AbilityTargetingPatch fires. Announce immediately.
                            if (!cmdAbility.Ability.def.targetRequired)
                            {
                                TolkHelper.Speak($"Casting {cmdAbility.Ability.def.LabelCap}");
                            }
                            // Targeted abilities will be announced by AbilityTargetingPatch
                        }
                        catch (System.Exception ex)
                        {
                            ModLogger.Error($"Exception in Command_Ability execution: {ex.Message}");
                            TolkHelper.Speak($"Error executing {gizmoLabel}: {ex.Message}", SpeechPriority.High);
                        }
                    }
                    // 8. Generic Command
                    else
                    {
                        try
                        {
                            // Execute the command
                            selectedGizmo.ProcessInput(fakeEvent);
                        }
                        catch (System.Exception ex)
                        {
                            ModLogger.Error($"Exception in generic Command execution: {ex.Message}");
                            TolkHelper.Speak($"Error executing {gizmoLabel}: {ex.Message}", SpeechPriority.High);
                        }
                    }
                }

                // Clear multi-selection after gizmo execution - operations like merge/split
                // change the world object list, so selection context is no longer valid
                WorldNavigationState.ClearMultiSelection();

                // Always close after executing (per user requirement)
                Close();
            }
            finally
            {
                // Clear flag after gizmo execution completes
                isExecutingGizmo = false;
            }
        }

        /// <summary>
        /// Jumps to the first gizmo in the list.
        /// </summary>
        public static void JumpToFirst()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            selectedGizmoIndex = MenuHelper.JumpToFirst();
            typeahead.ClearSearch();
            AnnounceCurrentGizmo();
        }

        /// <summary>
        /// Jumps to the last gizmo in the list.
        /// </summary>
        public static void JumpToLast()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            selectedGizmoIndex = MenuHelper.JumpToLast(availableGizmos.Count);
            typeahead.ClearSearch();
            AnnounceCurrentGizmo();
        }

        /// <summary>
        /// Handles typeahead character input for the gizmo menu.
        /// Called from UnifiedKeyboardPatch to process alphanumeric characters.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            var labels = GetGizmoLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedGizmoIndex = newIndex;
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
        /// Called from UnifiedKeyboardPatch.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            if (!typeahead.HasActiveSearch)
                return;

            var labels = GetGizmoLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedGizmoIndex = newIndex;
                }
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Gets whether typeahead search is active.
        /// </summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Handles keyboard input for the gizmo menu, including typeahead search.
        /// </summary>
        /// <returns>True if input was handled, false otherwise.</returns>
        public static bool HandleInput()
        {
            if (!isActive || availableGizmos.Count == 0)
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
                    AnnounceCurrentGizmo();
                    Event.current.Use();
                    return true;
                }
                // Let the caller handle normal escape (close menu)
                return false;
            }

            // Handle Backspace for search
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = GetGizmoLabels();
                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0)
                        selectedGizmoIndex = newIndex;
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
                    int prevIndex = typeahead.GetPreviousMatch(selectedGizmoIndex);
                    if (prevIndex >= 0)
                    {
                        selectedGizmoIndex = prevIndex;
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
                    int nextIndex = typeahead.GetNextMatch(selectedGizmoIndex);
                    if (nextIndex >= 0)
                    {
                        selectedGizmoIndex = nextIndex;
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

            // Handle Enter - execute selected
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                ExecuteSelected();
                Event.current.Use();
                return true;
            }

            // Handle Alt+I - open info card for current gizmo
            if (Event.current.alt && key == KeyCode.I)
            {
                OpenInfoCardForCurrentGizmo();
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
                var labels = GetGizmoLabels();
                if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
                {
                    if (newIndex >= 0)
                    {
                        selectedGizmoIndex = newIndex;
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
        /// Opens an info card for the currently selected gizmo, if applicable.
        /// Handles Command_Ability (ability def), Designator_Build (building def with optional stuff),
        /// and falls back to the gizmo's owner (Thing or WorldObject).
        /// </summary>
        private static void OpenInfoCardForCurrentGizmo()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            if (selectedGizmoIndex < 0 || selectedGizmoIndex >= availableGizmos.Count)
                return;

            Gizmo gizmo = availableGizmos[selectedGizmoIndex];

            // Command_Ability -> open info card for the ability def
            if (gizmo is Command_Ability cmdAbility && cmdAbility.Ability?.def != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(cmdAbility.Ability.def));
                return;
            }

            // Designator_Build -> open info card for the building def (with stuff if applicable)
            if (gizmo is Designator_Build buildDesignator)
            {
                BuildableDef placingDef = buildDesignator.PlacingDef;
                if (placingDef is ThingDef thingDef)
                {
                    ThingDef stuff = buildDesignator.StuffDef;
                    if (stuff != null)
                        Find.WindowStack.Add(new Dialog_InfoCard(thingDef, stuff));
                    else
                        InfoCardState.OpenInfoCardForDef(thingDef);
                    return;
                }
                if (placingDef is TerrainDef terrainDef)
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(terrainDef));
                    return;
                }
            }

            // Fallback: use gizmo owner (Thing or WorldObject)
            if (gizmoOwners.TryGetValue(gizmo, out ISelectable owner))
            {
                if (owner is Thing thing)
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(thing));
                    return;
                }
                if (owner is WorldObject worldObj)
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(worldObj));
                    return;
                }
            }

            // Nothing applicable
            TolkHelper.Speak("No info card available for this command");
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Gets the list of labels for all gizmos.
        /// </summary>
        private static List<string> GetGizmoLabels()
        {
            var labels = new List<string>();
            foreach (var gizmo in availableGizmos)
            {
                labels.Add(GetGizmoLabel(gizmo));
            }
            return labels;
        }

        /// <summary>
        /// Announces the current selection with search context if applicable.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            if (selectedGizmoIndex < 0 || selectedGizmoIndex >= availableGizmos.Count)
                return;

            Gizmo gizmo = availableGizmos[selectedGizmoIndex];
            string label = GetGizmoLabel(gizmo);

            if (typeahead.HasActiveSearch)
            {
                string announcement = $"{label}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";

                // Add disabled status if applicable
                if (gizmo.Disabled)
                {
                    string reason = gizmo.disabledReason;
                    if (string.IsNullOrEmpty(reason))
                        reason = "Not available";
                    announcement += $" Disabled: {reason}";

                    // Add helpful context for specific disabled scenarios (e.g., transport pod mass)
                    ISelectable gizmoOwner = null;
                    if (gizmoOwners.Count > 0)
                        gizmoOwners.TryGetValue(gizmo, out gizmoOwner);
                    string context = GetDisabledGizmoContext(gizmo, gizmoOwner);
                    if (!string.IsNullOrEmpty(context))
                        announcement += $". {context}";
                }

                TolkHelper.Speak(announcement);
            }
            else
            {
                AnnounceCurrentGizmo();
            }
        }

        /// <summary>
        /// Announces the currently selected gizmo to the user via clipboard.
        /// Format: "Name: Description (Hotkey)" or "Name: Description" if no hotkey.
        /// </summary>
        private static void AnnounceCurrentGizmo()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            if (selectedGizmoIndex < 0 || selectedGizmoIndex >= availableGizmos.Count)
                return;

            Gizmo gizmo = availableGizmos[selectedGizmoIndex];

            string label = GetGizmoLabel(gizmo);
            string description = GetGizmoDescription(gizmo);
            string hotkey = GetGizmoHotkey(gizmo);
            string statusValue = GetGizmoStatusValue(gizmo);

            // Add owner prefix when navigating gizmos from multiple objects
            string ownerPrefix = "";
            if (gizmoOwners.Count > 0 && gizmoOwners.TryGetValue(gizmo, out ISelectable owner))
            {
                if (owner != lastAnnouncedOwner)
                {
                    lastAnnouncedOwner = owner;
                    if (owner is Thing thing)
                        ownerPrefix = thing.LabelCap.StripTags() + ": ";
                    else if (owner is WorldObject worldObj)
                        ownerPrefix = worldObj.LabelCap.StripTags() + ": ";
                }
            }

            string announcement = ownerPrefix + label;

            // For Command_Toggle, include current ON/OFF state (sighted players see a checkbox)
            if (gizmo is Command_Toggle toggle)
            {
                bool isOn = toggle.isActive?.Invoke() ?? false;
                announcement += isOn ? ": ON" : ": OFF";
            }

            // Add status value for non-Command gizmos (progress bars, etc.)
            if (!string.IsNullOrEmpty(statusValue))
                announcement += $" - {statusValue}";

            // For non-ability gizmos, add description before hotkey
            bool isAbility = gizmo is Command_Ability;
            if (!string.IsNullOrEmpty(description) && !isAbility)
            {
                // Toggles already have ": ON/OFF", so use period separator instead of colon
                string descSep = (gizmo is Command_Toggle)
                    ? (announcement.EndsWith(".") ? " " : ". ")
                    : ": ";
                announcement += descSep + description;
            }

            // For animal attack Command_Target, add range info
            if (gizmo is Command_Target cmdTargetGizmo
                && cmdTargetGizmo.icon == Pawn_TrainingTracker.AttackTargetTexture)
            {
                float attackRange = Pawn_TrainingTracker.AttackTargetRange;
                string sep = announcement.EndsWith(".") ? " " : ". ";
                announcement += $"{sep}Range: {attackRange:F0} tiles from master";
            }

            // For abilities, hotkey goes at the end; for everything else, add it here
            if (!isAbility && !string.IsNullOrEmpty(hotkey))
                announcement += $" ({hotkey})";

            // For Command_Ability (psycasts, abilities), add cost, range, description, then hotkey
            if (isAbility && gizmo is Command_Ability commandAbility && commandAbility.Ability != null)
            {
                string costInfo = GetAbilityCostInfo(commandAbility.Ability);
                if (!string.IsNullOrEmpty(costInfo))
                    announcement += (announcement.EndsWith(".") ? " " : ". ") + costInfo;

                // Add range info after cost
                string rangeInfo = GetAbilityRangeInfo(commandAbility.Ability);
                if (!string.IsNullOrEmpty(rangeInfo))
                    announcement += (announcement.EndsWith(".") ? " " : ". ") + rangeInfo;

                // Add ability description after range info
                if (!string.IsNullOrEmpty(description))
                    announcement += (announcement.EndsWith(".") ? " " : ". ") + description;

                // Hotkey last for abilities
                if (!string.IsNullOrEmpty(hotkey))
                    announcement += $" ({hotkey})";
            }

            // Add disabled status if applicable
            if (gizmo.Disabled)
            {
                string reason = gizmo.disabledReason;
                if (string.IsNullOrEmpty(reason))
                    reason = "Not available";
                announcement += $" Disabled: {reason}";

                // Add helpful context for specific disabled scenarios (e.g., transport pod mass)
                ISelectable gizmoOwner = null;
                if (gizmoOwners.Count > 0)
                    gizmoOwners.TryGetValue(gizmo, out gizmoOwner);
                string context = GetDisabledGizmoContext(gizmo, gizmoOwner);
                if (!string.IsNullOrEmpty(context))
                    announcement += $". {context}";
            }

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Gets the label text for a gizmo.
        /// </summary>
        private static string GetGizmoLabel(Gizmo gizmo)
        {
            // Special handling for Command_VerbTarget (weapon attacks)
            if (gizmo is Command_VerbTarget verbTarget)
            {
                string weaponName = verbTarget.ownerThing?.LabelCap ?? "Unknown weapon";
                string verbLabel = verbTarget.verb?.ReportLabel ?? "attack";
                return $"{weaponName} - {verbLabel}";
            }

            if (gizmo is Command cmd)
            {
                string label = cmd.LabelCap;
                if (string.IsNullOrEmpty(label))
                    label = cmd.defaultLabel;
                if (string.IsNullOrEmpty(label))
                    label = "Unknown Command";
                // Strip color tags (e.g., <color=#...>text</color>) from gizmo labels
                return label.StripTags();
            }

            // Handle non-Command gizmos (status displays, bars, etc.)
            return GetNonCommandGizmoLabel(gizmo);
        }

        /// <summary>
        /// Gets a descriptive label for non-Command gizmos (status displays, bars, etc.)
        /// These gizmos don't have a standard Label property, so we identify them by type.
        /// </summary>
        private static string GetNonCommandGizmoLabel(Gizmo gizmo)
        {
            string typeName = gizmo.GetType().Name;

            // Gizmo_Slider and subclasses have a Title property
            if (gizmo is Verse.Gizmo_Slider slider)
            {
                try
                {
                    // Title is a protected abstract property, access via reflection
                    var titleProp = gizmo.GetType().GetProperty("Title",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);
                    if (titleProp != null)
                    {
                        string title = titleProp.GetValue(gizmo) as string;
                        if (!string.IsNullOrEmpty(title))
                            return title;
                    }
                }
                catch { }
            }

            // Handle specific known gizmo types by name
            switch (typeName)
            {
                case "Gizmo_EnergyShieldStatus":
                    return GetEnergyShieldLabel(gizmo);

                case "PsychicEntropyGizmo":
                    return "Neural Heat and Psyfocus";

                case "MechanitorBandwidthGizmo":
                    return "Mechanitor Bandwidth";

                case "Gizmo_GrowthTier":
                    return GetGrowthTierLabel(gizmo);

                case "Gizmo_RoomStats":
                    return GetRoomStatsLabel(gizmo);

                case "MechCarrierGizmo":
                    return GetMechCarrierLabel(gizmo);

                case "MechPowerCellGizmo":
                    return "Mech Power Cell";

                case "MechanitorControlGroupGizmo":
                    return "Mechanitor Control Groups";

                case "Gizmo_MechResurrectionCharges":
                    return "Resurrector Charges";

                case "Gizmo_ProjectileInterceptorHitPoints":
                    return "Shield Hit Points";

                case "Gizmo_PruningConfig":
                    return "Pruning Configuration";

                case "GuardianShipGizmo":
                    return "Guardian Ship";

                case "Gizmo_CaravanInfo":
                    return "Caravan Info";

                case "GeneGizmo_DeathrestCapacity":
                    return "Deathrest Capacity";

                case "ActivityGizmo":
                    return GetActivityGizmoLabel(gizmo);

                default:
                    // For unknown gizmos, return a cleaned-up type name
                    return CleanupGizmoTypeName(typeName);
            }
        }

        /// <summary>
        /// Gets the label for an energy shield gizmo by accessing its shield component.
        /// </summary>
        private static string GetEnergyShieldLabel(Gizmo gizmo)
        {
            try
            {
                var shieldField = gizmo.GetType().GetField("shield",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public);
                if (shieldField != null)
                {
                    var shield = shieldField.GetValue(gizmo);
                    if (shield != null)
                    {
                        // Check if it's apparel shield or inbuilt
                        var isApparelProp = shield.GetType().GetProperty("IsApparel");
                        bool isApparel = isApparelProp != null && (bool)isApparelProp.GetValue(shield);

                        var parentProp = shield.GetType().GetProperty("parent");
                        var parent = parentProp?.GetValue(shield) as Thing;

                        if (isApparel && parent != null)
                            return $"Shield: {parent.LabelCap}";
                        else
                            return "Shield (Inbuilt)";
                    }
                }
            }
            catch { }
            return "Energy Shield";
        }

        /// <summary>
        /// Gets the label for a growth tier gizmo by accessing the child pawn.
        /// </summary>
        private static string GetGrowthTierLabel(Gizmo gizmo)
        {
            try
            {
                var childField = gizmo.GetType().GetField("child",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                if (childField != null)
                {
                    var child = childField.GetValue(gizmo) as Pawn;
                    if (child != null)
                    {
                        int tier = child.ageTracker?.GrowthTier ?? 0;
                        return $"Growth Tier {tier}";
                    }
                }
            }
            catch { }
            return "Growth Tier";
        }

        /// <summary>
        /// Gets the label for a room stats gizmo by accessing the building and room.
        /// Uses shared TileInfoHelper.GetRoomStatsInfo() for consistent formatting.
        /// </summary>
        private static string GetRoomStatsLabel(Gizmo gizmo)
        {
            try
            {
                var buildingField = gizmo.GetType().GetField("building",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                if (buildingField != null)
                {
                    var building = buildingField.GetValue(gizmo) as Building;
                    Room room = Gizmo_RoomStats.GetRoomToShowStatsFor(building);
                    if (room != null)
                    {
                        return TileInfoHelper.GetRoomStatsInfo(room);
                    }
                }
            }
            catch { }
            return "Room Stats";
        }

        /// <summary>
        /// Gets the label for a mech carrier gizmo by accessing the carrier component.
        /// </summary>
        private static string GetMechCarrierLabel(Gizmo gizmo)
        {
            try
            {
                var carrierField = gizmo.GetType().GetField("carrier",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                if (carrierField != null)
                {
                    var carrier = carrierField.GetValue(gizmo);
                    if (carrier != null)
                    {
                        var propsField = carrier.GetType().GetProperty("Props");
                        var props = propsField?.GetValue(carrier);
                        if (props != null)
                        {
                            var ingredientField = props.GetType().GetField("fixedIngredient");
                            var ingredient = ingredientField?.GetValue(props) as Def;
                            if (ingredient != null)
                                return $"Mech Carrier: {ingredient.label?.CapitalizeFirst()}";
                        }
                    }
                }
            }
            catch { }
            return "Mech Carrier";
        }

        /// <summary>
        /// Gets the label for an activity gizmo by accessing its Title property.
        /// </summary>
        private static string GetActivityGizmoLabel(Gizmo gizmo)
        {
            try
            {
                var titleProp = gizmo.GetType().GetProperty("Title",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                if (titleProp != null)
                {
                    string title = titleProp.GetValue(gizmo) as string;
                    if (!string.IsNullOrEmpty(title))
                        return title;
                }
            }
            catch { }
            return "Activity";
        }

        /// <summary>
        /// Cleans up a gizmo type name to be more readable.
        /// Converts "Gizmo_SomethingCamelCase" to "Something Camel Case".
        /// </summary>
        private static string CleanupGizmoTypeName(string typeName)
        {
            // Remove common prefixes
            if (typeName.StartsWith("Gizmo_"))
                typeName = typeName.Substring(6);
            else if (typeName.StartsWith("GeneGizmo_"))
                typeName = typeName.Substring(10);
            else if (typeName.EndsWith("Gizmo"))
                typeName = typeName.Substring(0, typeName.Length - 5);

            // Add spaces before capital letters
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < typeName.Length; i++)
            {
                if (i > 0 && char.IsUpper(typeName[i]) && !char.IsUpper(typeName[i - 1]))
                    result.Append(' ');
                result.Append(typeName[i]);
            }

            string label = result.ToString().Trim();
            return string.IsNullOrEmpty(label) ? "Status Display" : label;
        }

        /// <summary>
        /// Gets the current status value for non-Command gizmos (progress bars, meters, etc.)
        /// Returns values like "5 / 12" for bandwidth or "75%" for shield energy.
        /// </summary>
        private static string GetGizmoStatusValue(Gizmo gizmo)
        {
            // Only get status for non-Command gizmos
            if (gizmo is Command)
                return "";

            string typeName = gizmo.GetType().Name;

            try
            {
                switch (typeName)
                {
                    case "MechanitorBandwidthGizmo":
                        return GetMechanitorBandwidthStatus(gizmo);

                    case "Gizmo_EnergyShieldStatus":
                        return GetEnergyShieldStatus(gizmo);

                    case "PsychicEntropyGizmo":
                        return GetPsychicEntropyStatus(gizmo);

                    case "Gizmo_GrowthTier":
                        return GetGrowthTierStatus(gizmo);

                    case "MechCarrierGizmo":
                        return GetMechCarrierStatus(gizmo);

                    case "MechPowerCellGizmo":
                        return GetMechPowerCellStatus(gizmo);

                    case "Gizmo_MechResurrectionCharges":
                        return GetMechResurrectionStatus(gizmo);

                    case "Gizmo_ProjectileInterceptorHitPoints":
                        return GetProjectileInterceptorStatus(gizmo);

                    default:
                        // Try to get status from Gizmo_Slider subclasses
                        if (gizmo is Verse.Gizmo_Slider)
                            return GetSliderGizmoStatus(gizmo);
                        return "";
                }
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Gets the bandwidth status (used / total).
        /// </summary>
        private static string GetMechanitorBandwidthStatus(Gizmo gizmo)
        {
            var trackerField = gizmo.GetType().GetField("tracker",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            if (trackerField == null) return "";

            var tracker = trackerField.GetValue(gizmo);
            if (tracker == null) return "";

            var usedProp = tracker.GetType().GetProperty("UsedBandwidth");
            var totalProp = tracker.GetType().GetProperty("TotalBandwidth");

            if (usedProp != null && totalProp != null)
            {
                int used = (int)usedProp.GetValue(tracker);
                int total = (int)totalProp.GetValue(tracker);
                return $"{used} / {total}";
            }
            return "";
        }

        /// <summary>
        /// Gets the shield energy status (current / max as percentage).
        /// </summary>
        private static string GetEnergyShieldStatus(Gizmo gizmo)
        {
            var shieldField = gizmo.GetType().GetField("shield",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public);
            if (shieldField == null) return "";

            var shield = shieldField.GetValue(gizmo);
            if (shield == null) return "";

            var energyProp = shield.GetType().GetProperty("Energy");
            var parentProp = shield.GetType().GetProperty("parent");

            if (energyProp != null && parentProp != null)
            {
                float energy = (float)energyProp.GetValue(shield);
                var parent = parentProp.GetValue(shield) as Thing;
                if (parent != null)
                {
                    float maxEnergy = parent.GetStatValue(RimWorld.StatDefOf.EnergyShieldEnergyMax);
                    if (maxEnergy > 0)
                    {
                        float percent = (energy / maxEnergy) * 100f;
                        return $"{percent:F0}% ({energy * 100:F0} / {maxEnergy * 100:F0})";
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// Gets the psychic entropy status (entropy, psyfocus, and limiter state).
        /// </summary>
        private static string GetPsychicEntropyStatus(Gizmo gizmo)
        {
            var tracker = GetPsychicEntropyTracker(gizmo);
            if (tracker == null) return "";

            var entropyProp = tracker.GetType().GetProperty("EntropyValue");
            var maxEntropyProp = tracker.GetType().GetProperty("MaxEntropy");
            var psyfocusProp = tracker.GetType().GetProperty("CurrentPsyfocus");
            var limitField = tracker.GetType().GetField("limitEntropyAmount",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public);

            if (entropyProp != null && maxEntropyProp != null && psyfocusProp != null)
            {
                float entropy = (float)entropyProp.GetValue(tracker);
                float maxEntropy = (float)maxEntropyProp.GetValue(tracker);
                float psyfocus = (float)psyfocusProp.GetValue(tracker);

                string status = $"Neural heat: {entropy:F0} / {maxEntropy:F0}, Psyfocus: {psyfocus * 100:F0}%";

                // Add limiter state
                if (limitField != null)
                {
                    bool isLimited = (bool)limitField.GetValue(tracker);
                    status += $", Limiter: {(isLimited ? "ON" : "OFF")}";
                }

                return status;
            }
            return "";
        }

        /// <summary>
        /// Gets the Pawn_PsychicEntropyTracker from a PsychicEntropyGizmo.
        /// </summary>
        private static object GetPsychicEntropyTracker(Gizmo gizmo)
        {
            var trackerField = gizmo.GetType().GetField("tracker",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            if (trackerField == null) return null;

            return trackerField.GetValue(gizmo);
        }

        /// <summary>
        /// Toggles the neural heat limiter on a PsychicEntropyGizmo.
        /// Returns the new state (true = limited, false = unlimited).
        /// </summary>
        private static bool ToggleNeuralHeatLimiter(Gizmo gizmo)
        {
            var tracker = GetPsychicEntropyTracker(gizmo);
            if (tracker == null) return true;

            var limitField = tracker.GetType().GetField("limitEntropyAmount",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public);
            if (limitField == null) return true;

            bool currentValue = (bool)limitField.GetValue(tracker);
            bool newValue = !currentValue;
            limitField.SetValue(tracker, newValue);

            // Play the appropriate sound (matching the game's behavior)
            if (newValue)
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            else
                SoundDefOf.Tick_High.PlayOneShotOnCamera();

            return newValue;
        }

        /// <summary>
        /// Gets cost information for an ability (psyfocus cost, minimum required, neural heat gain).
        /// Returns a string like "Costs 2% psyfocus, requires 50%, Neural heat: 25" or null if no costs.
        /// </summary>
        private static string GetAbilityCostInfo(Ability ability)
        {
            if (ability?.def == null)
                return null;

            var parts = new List<string>();

            // Psyfocus cost (what gets consumed)
            float psyfocusCost = ability.def.PsyfocusCost;

            // Minimum psyfocus required (band threshold based on ability level)
            // PsyfocusBandPercentages: 0%, 25%, 50% for bands 0, 1, 2
            // RequiredPsyfocusBand is calculated from ability level
            int requiredBand = ability.def.RequiredPsyfocusBand;
            float minRequired = 0f;
            if (requiredBand > 0 && requiredBand < Pawn_PsychicEntropyTracker.PsyfocusBandPercentages.Count)
            {
                minRequired = Pawn_PsychicEntropyTracker.PsyfocusBandPercentages[requiredBand];
            }

            // Build psyfocus string showing both cost and minimum if different
            if (psyfocusCost > float.Epsilon || minRequired > float.Epsilon)
            {
                if (psyfocusCost > float.Epsilon && minRequired > float.Epsilon)
                {
                    // Both cost and minimum requirement
                    parts.Add($"Costs {(psyfocusCost * 100f):F0}% psyfocus, requires {(minRequired * 100f):F0}%");
                }
                else if (psyfocusCost > float.Epsilon)
                {
                    // Just cost, no minimum band requirement
                    parts.Add($"Costs {(psyfocusCost * 100f):F0}% psyfocus");
                }
                else if (minRequired > float.Epsilon)
                {
                    // Just minimum requirement (unusual but possible)
                    parts.Add($"Requires {(minRequired * 100f):F0}% psyfocus");
                }
            }

            // Neural heat (entropy) gain
            float entropyGain = ability.def.EntropyGain;
            if (entropyGain > float.Epsilon)
            {
                parts.Add($"Neural heat: {entropyGain:F0}");
            }

            if (parts.Count == 0)
                return null;

            return string.Join(". ", parts);
        }

        /// <summary>
        /// Gets the range description for an ability gizmo.
        /// Returns "Range: N tiles", "Range: touch", "Range: self", or null.
        /// </summary>
        private static string GetAbilityRangeInfo(Ability ability)
        {
            if (ability?.def == null)
                return null;

            // Determine base range text
            string rangeText;
            if (!ability.def.targetRequired)
                rangeText = "Range: self";
            else if (ability.def.targetWorldCell)
                rangeText = "Range: world map";
            else if (AbilityTargetingHelper.IsTouchRange(ability))
                rangeText = "Range: touch";
            else
            {
                float range = AbilityTargetingHelper.GetRange(ability);
                if (range > 0f)
                    rangeText = $"Range: {range:F0} tiles";
                else
                    return null;
            }

            // Append effect radius if this ability has one
            float effectRadius = ability.def.EffectRadius;
            if (effectRadius > 0f)
                rangeText += $", {effectRadius:F0} tile radius";

            return rangeText;
        }

        /// <summary>
        /// Gets the growth tier status (current tier and progress).
        /// </summary>
        private static string GetGrowthTierStatus(Gizmo gizmo)
        {
            var childField = gizmo.GetType().GetField("child",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            if (childField == null) return "";

            var child = childField.GetValue(gizmo) as Pawn;
            if (child?.ageTracker == null) return "";

            int tier = child.ageTracker.GrowthTier;
            float percent = child.ageTracker.PercentToNextGrowthTier;

            if (child.ageTracker.AtMaxGrowthTier)
                return $"Tier {tier} (Max)";
            else
                return $"Tier {tier}, {percent * 100:F0}% to next";
        }

        /// <summary>
        /// Gets the mech carrier status (current / max resources).
        /// </summary>
        private static string GetMechCarrierStatus(Gizmo gizmo)
        {
            var carrierField = gizmo.GetType().GetField("carrier",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            if (carrierField == null) return "";

            var carrier = carrierField.GetValue(gizmo);
            if (carrier == null) return "";

            var countProp = carrier.GetType().GetProperty("IngredientCount");
            var propsProp = carrier.GetType().GetProperty("Props");

            if (countProp != null && propsProp != null)
            {
                int count = (int)countProp.GetValue(carrier);
                var props = propsProp.GetValue(carrier);
                if (props != null)
                {
                    var maxField = props.GetType().GetField("maxIngredientCount");
                    if (maxField != null)
                    {
                        int max = (int)maxField.GetValue(props);
                        return $"{count} / {max}";
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// Gets the mech power cell status.
        /// </summary>
        private static string GetMechPowerCellStatus(Gizmo gizmo)
        {
            // MechPowerCellGizmo has a mech field
            var mechField = gizmo.GetType().GetField("mech",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            if (mechField == null) return "";

            var mech = mechField.GetValue(gizmo) as Pawn;
            if (mech?.needs?.energy == null) return "";

            float energy = mech.needs.energy.CurLevel;
            float max = mech.needs.energy.MaxLevel;
            float percent = (energy / max) * 100f;

            return $"{percent:F0}% ({energy:F1} / {max:F1})";
        }

        /// <summary>
        /// Gets the mech resurrection charges status.
        /// </summary>
        private static string GetMechResurrectionStatus(Gizmo gizmo)
        {
            var geneField = gizmo.GetType().GetField("gene",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            if (geneField == null) return "";

            var gene = geneField.GetValue(gizmo);
            if (gene == null) return "";

            var chargesProp = gene.GetType().GetProperty("ChargesRemaining");
            var maxProp = gene.GetType().GetProperty("MaxCharges");

            if (chargesProp != null && maxProp != null)
            {
                int charges = (int)chargesProp.GetValue(gene);
                int max = (int)maxProp.GetValue(gene);
                return $"{charges} / {max} charges";
            }
            return "";
        }

        /// <summary>
        /// Gets the projectile interceptor (shield) hit points status.
        /// </summary>
        private static string GetProjectileInterceptorStatus(Gizmo gizmo)
        {
            var compField = gizmo.GetType().GetField("interceptor",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            if (compField == null) return "";

            var comp = compField.GetValue(gizmo);
            if (comp == null) return "";

            var hpProp = comp.GetType().GetProperty("currentHitPoints",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            var maxHpProp = comp.GetType().GetProperty("HitPointsMax");

            if (hpProp != null && maxHpProp != null)
            {
                int hp = (int)hpProp.GetValue(comp);
                int maxHp = (int)maxHpProp.GetValue(comp);
                return $"{hp} / {maxHp} HP";
            }
            return "";
        }

        /// <summary>
        /// Gets the status for Gizmo_Slider subclasses using their ValuePercent property.
        /// </summary>
        private static string GetSliderGizmoStatus(Gizmo gizmo)
        {
            var valueProp = gizmo.GetType().GetProperty("ValuePercent",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            if (valueProp != null)
            {
                float value = (float)valueProp.GetValue(gizmo);
                return $"{value * 100:F0}%";
            }
            return "";
        }

        /// <summary>
        /// Gets the description text for a gizmo.
        /// For Command_Ability (psycasts), pulls from ability.def.description since
        /// Command_Ability.Desc is only populated during mouse hover rendering.
        /// </summary>
        private static string GetGizmoDescription(Gizmo gizmo)
        {
            // Command_Ability.defaultDesc is only set during GizmoOnGUIInt on mouse hover,
            // so we pull directly from the ability definition instead
            if (gizmo is Command_Ability commandAbility && commandAbility.Ability?.def != null)
            {
                string abilityDesc = commandAbility.Ability.def.description;
                return (abilityDesc ?? "").StripTags();
            }

            if (gizmo is Command cmd)
            {
                string desc = cmd.Desc;
                if (string.IsNullOrEmpty(desc))
                    desc = cmd.defaultDesc;
                // Strip color tags from descriptions
                return (desc ?? "").StripTags();
            }
            return "";
        }

        /// <summary>
        /// Gets the hotkey text for a gizmo.
        /// </summary>
        private static string GetGizmoHotkey(Gizmo gizmo)
        {
            if (gizmo is Command cmd && cmd.hotKey != null)
            {
                KeyCode key = cmd.hotKey.MainKey;
                if (key != KeyCode.None)
                {
                    return key.ToStringReadable();
                }
            }
            return "";
        }

        /// <summary>
        /// Gets additional context for disabled gizmos, particularly for transport pod mass issues.
        /// Returns helpful information like group mass stats when a Launch gizmo is disabled due to
        /// individual pod overload but the group as a whole might be fine.
        /// </summary>
        private static string GetDisabledGizmoContext(Gizmo gizmo, ISelectable owner)
        {
            // Only handle disabled gizmos
            if (!gizmo.Disabled || string.IsNullOrEmpty(gizmo.disabledReason))
                return null;

            // Check if this is a Launch gizmo disabled due to mass capacity
            // The game's translation key is "CommandLaunchGroupFailOverMassCapacity"
            if (!gizmo.disabledReason.Contains("mass capacity") &&
                !gizmo.disabledReason.ToLower().Contains("mass"))
                return null;

            // Get the CompTransporter from the owner
            CompTransporter transporter = null;
            if (owner is Thing thing)
            {
                transporter = thing.TryGetComp<CompTransporter>();
            }

            if (transporter == null || transporter.parent?.Map == null)
                return null;

            // Get all transporters in the group
            var transportersInGroup = transporter.TransportersInGroup(transporter.parent.Map);
            if (transportersInGroup == null || transportersInGroup.Count <= 1)
                return null;

            // Calculate group totals
            float groupMassUsage = 0f;
            float groupMassCapacity = 0f;
            int overloadedCount = 0;
            int canLaunchCount = 0;

            foreach (var t in transportersInGroup)
            {
                groupMassUsage += t.MassUsage;
                groupMassCapacity += t.MassCapacity;

                if (t.OverMassCapacity)
                    overloadedCount++;

                // Check if this transporter's Launch gizmo is enabled
                var launchable = t.Launchable;
                if (launchable != null)
                {
                    var canLaunch = launchable.CanLaunch();
                    if (canLaunch.Accepted)
                        canLaunchCount++;
                }
            }

            // Build helpful context message
            var context = new StringBuilder();

            // Show group mass stats
            context.Append($"Group total: {groupMassUsage:F0} / {groupMassCapacity:F0} kg");

            // If group isn't overloaded but this pod is, explain the workaround
            if (groupMassUsage <= groupMassCapacity && overloadedCount > 0 && canLaunchCount > 0)
            {
                context.Append($". {canLaunchCount} of {transportersInGroup.Count} pods can launch the group.");
                context.Append(" Select another pod to launch.");
            }
            else if (groupMassUsage > groupMassCapacity)
            {
                float overBy = groupMassUsage - groupMassCapacity;
                context.Append($". Group is over capacity by {overBy:F0} kg.");
            }

            return context.ToString();
        }

        /// <summary>
        /// Determines if a gizmo should be skipped (not shown in our menu).
        /// Some gizmos don't integrate well with our cursor-based accessibility system.
        /// </summary>
        private static bool ShouldSkipGizmo(Gizmo gizmo)
        {
            if (!(gizmo is Command cmd))
                return false;

            string label = cmd.Label?.ToLower() ?? cmd.defaultLabel?.ToLower() ?? "";

            // Skip transporter/launch group navigation gizmos - they use CameraJumper.TryJumpAndSelect
            // which doesn't integrate with our cursor-based navigation system
            // Labels vary based on pod state:
            // - Before loading: "Select previous transporter", "Select next transporter", "Select all transporters"
            // - During/after loading: "Select previous in launch group", "Select next in launch group",
            //   "Select all in launch group", "Select launch group"
            if (label.Contains("transporter") || label.Contains("launch group"))
            {
                if (label.Contains("previous") || label.Contains("next") ||
                    (label.Contains("select") && label.Contains("all")) ||
                    label == "select launch group")
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Handles material selection for a Designator_Build gizmo.
        /// Opens a windowless float menu with material options.
        /// </summary>
        private static void HandleBuildDesignatorMaterialSelection(Designator_Build buildDesignator, BuildableDef buildable)
        {
            // Create material options using the existing helper
            List<FloatMenuOption> options = ArchitectHelper.CreateMaterialOptions(
                buildable,
                (material) => OnGizmoMaterialSelected(buildDesignator, buildable, material)
            );

            if (options.Count == 0)
            {
                TolkHelper.Speak($"No materials available for {buildable.label}");
                return;
            }

            // Announce and open material selection menu
            TolkHelper.Speak($"Select material for {buildable.label}");
            WindowlessFloatMenuState.Open(options, false);

            // Close gizmo navigation - material selection will handle the rest
            Close();
        }

        /// <summary>
        /// Callback when a material is selected for a gizmo's Designator_Build.
        /// Sets the material on the designator and selects it directly.
        /// DesignatorManagerPatch will automatically route to accessible placement.
        /// </summary>
        private static void OnGizmoMaterialSelected(Designator_Build designator, BuildableDef buildable, ThingDef material)
        {
            try
            {
                // Set the material on the designator
                designator.SetStuffDef(material);

                // Select the designator directly (like vanilla's FloatMenu callback does)
                // DesignatorManagerPatch will automatically route to accessible placement
                Find.DesignatorManager.Select(designator);

                TolkHelper.Speak($"{material.LabelCap} selected");
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception in gizmo material selection: {ex.Message}");
                TolkHelper.Speak($"Error: {ex.Message}", SpeechPriority.High);
            }
        }
    }
}
