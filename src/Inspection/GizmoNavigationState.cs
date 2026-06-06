using System.Collections.Generic;
using System.Linq;
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
        private static Dictionary<Gizmo, List<Gizmo>> gizmoGroups = new Dictionary<Gizmo, List<Gizmo>>();
        private static ISelectable lastAnnouncedOwner = null;
        private static bool pawnJustSelected = false;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private static bool isExecutingGizmo = false;

        // Slider adjustment mode state
        private static bool isAdjustingSlider = false;
        private static Gizmo sliderGizmo = null;
        private static float sliderValue = 0f;
        private static float sliderStep = 0.05f;
        private static float sliderMin = 0f;
        private static float sliderMax = 1f;

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
            var allGizmos = new List<Gizmo>();
            var allOwners = new Dictionary<Gizmo, ISelectable>();
            lastAnnouncedOwner = null;

            foreach (object obj in Find.Selector.SelectedObjects)
            {
                if (obj is ISelectable selectable)
                {
                    var gizmos = selectable.GetGizmos().ToList();
                    foreach (var gizmo in gizmos.Where(g => g != null && g.Visible && !ShouldSkipGizmo(g)))
                    {
                        allGizmos.Add(gizmo);
                        allOwners[gizmo] = selectable;
                    }

                    // Thing.GetGizmos() omits reverse designators (Cancel, Deconstruct,
                    // Uninstall, etc.) — vanilla's InspectGizmoGrid combines them at
                    // render time. Mirror that so the G menu shows Cancel on a selected
                    // designated building, matching what a sighted player would see.
                    if (selectable is Thing selectedThing)
                    {
                        List<Designator> reverseDesignators = Find.ReverseDesignatorDatabase.AllDesignators;
                        for (int i = 0; i < reverseDesignators.Count; i++)
                        {
                            Command_Action reverseGizmo = reverseDesignators[i].CreateReverseDesignationGizmo(selectedThing);
                            if (reverseGizmo == null || ShouldSkipGizmo(reverseGizmo))
                                continue;
                            allGizmos.Add(reverseGizmo);
                            allOwners[reverseGizmo] = selectable;
                        }
                    }
                }
            }

            // Group gizmos using vanilla's GroupsWith/MergeWith logic
            // (mirrors GizmoGridDrawer lines 148-169)
            var groups = new List<List<Gizmo>>();
            foreach (var gizmo in allGizmos)
            {
                bool grouped = false;
                for (int i = 0; i < groups.Count; i++)
                {
                    if (groups[i][0].GroupsWith(gizmo))
                    {
                        groups[i].Add(gizmo);
                        groups[i][0].MergeWith(gizmo);
                        grouped = true;
                        break;
                    }
                }
                if (!grouped)
                {
                    groups.Add(new List<Gizmo> { gizmo });
                }
            }

            // Store only the representative (first) gizmo of each group
            availableGizmos.Clear();
            gizmoOwners.Clear();
            gizmoGroups.Clear();

            foreach (var group in groups)
            {
                var representative = group[0];
                availableGizmos.Add(representative);
                gizmoGroups[representative] = group;

                // Map the representative to the first gizmo's owner
                if (allOwners.TryGetValue(representative, out var owner))
                {
                    gizmoOwners[representative] = owner;
                }
            }

            // Sort by Order property (lower values appear first)
            availableGizmos = availableGizmos
                .OrderBy(g => g.Order)
                .ToList();

            if (availableGizmos.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NoCommandsAvailable".Loc());
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
                TolkHelper.Speak("RimWorldAccess.Guard.InvalidCursorPosition".Loc());
                return;
            }

            availableGizmos.Clear();
            gizmoOwners.Clear();

            // Store the current selection to restore it later
            var previousSelection = Find.Selector.SelectedObjects.ToList();

            try
            {
                // Get all things at cursor, sorted by AltitudeLayer descending
                // (matches TileInfoHelper ordering - highest layer first)
                var sortedThings = cursorPosition.GetThingList(map)
                    .Where(t => !(t is Mote) && t.def.category != ThingCategory.Mote)
                    .OrderByDescending(t => (int)t.def.altitudeLayer)
                    .ToList();

                // Collect gizmos from things in altitude order (highest layer first)
                // Within each owner, gizmos are sorted by gizmo.Order
                // Important: Temporarily select each thing before getting its gizmos,
                // because some gizmos (like Designator_Install) check if the thing is selected
                // to determine their Visible property
                foreach (ISelectable selectable in sortedThings.OfType<ISelectable>())
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(selectable, playSound: false, forceDesignatorDeselect: false);

                    var gizmos = selectable.GetGizmos()
                        .Where(g => g != null && g.Visible && !ShouldSkipGizmo(g))
                        .OrderBy(g => g.Order)
                        .ToList();
                    foreach (Gizmo gizmo in gizmos)
                    {
                        availableGizmos.Add(gizmo);
                        gizmoOwners[gizmo] = selectable;
                    }

                    // Collect reverse designator gizmos (Mine, Mine Vein, Strip, etc.)
                    // Mirrors GizmoGridDrawer.DrawGizmoGridFor() behavior
                    if (selectable is Thing thing)
                    {
                        List<Designator> reverseDesignators = Find.ReverseDesignatorDatabase.AllDesignators;
                        for (int i = 0; i < reverseDesignators.Count; i++)
                        {
                            Command_Action reverseGizmo = reverseDesignators[i].CreateReverseDesignationGizmo(thing);
                            if (reverseGizmo != null && !ShouldSkipGizmo(reverseGizmo))
                            {
                                availableGizmos.Add(reverseGizmo);
                                gizmoOwners[reverseGizmo] = selectable;
                            }
                        }
                    }
                }

                // Check for zone AFTER things (matches TileInfoHelper ordering)
                Zone zone = cursorPosition.GetZone(map);
                if (zone != null)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(zone, playSound: false, forceDesignatorDeselect: false);

                    var zoneGizmos = zone.GetGizmos()
                        .Where(g => g != null && g.Visible && !ShouldSkipGizmo(g))
                        .OrderBy(g => g.Order)
                        .ToList();
                    foreach (Gizmo gizmo in zoneGizmos)
                    {
                        availableGizmos.Add(gizmo);
                        gizmoOwners[gizmo] = zone;
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

            if (availableGizmos.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NoCommandsAtCursor".Loc());
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
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NoWorldObject".Loc());
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
                string objName = objectsToUse.FirstOrDefault()?.LabelCap
                    ?? "RimWorldAccess.Inspection.Gizmo.WorldObjectFallbackName".Translate().ToString();
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NoCommandsForObject".Loc(objName));
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
            gizmoGroups.Clear();
            typeahead.ClearSearch();
            lastAnnouncedOwner = null;
            isAdjustingSlider = false;
            sliderGizmo = null;
        }

        /// <summary>
        /// Propagates a gizmo execution to all other gizmos in its group,
        /// matching vanilla GizmoGridDrawer behavior (lines 338-351).
        /// Called BEFORE the selected gizmo's own ProcessInput.
        /// </summary>
        private static void PropagateToGroupedGizmos(Gizmo selectedGizmo, Event fakeEvent)
        {
            if (!gizmoGroups.TryGetValue(selectedGizmo, out var group) || group.Count <= 1)
                return;

            for (int i = 0; i < group.Count; i++)
            {
                Gizmo other = group[i];
                if (other != selectedGizmo && !other.Disabled &&
                    selectedGizmo.InheritInteractionsFrom(other))
                {
                    try
                    {
                        other.ProcessInput(fakeEvent);
                    }
                    catch (System.Exception ex)
                    {
                        ModLogger.Error($"Exception propagating gizmo to grouped member: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Calls ProcessGroupInput on the gizmo with its full group list,
        /// matching vanilla GizmoGridDrawer behavior.
        /// Called AFTER the selected gizmo's own ProcessInput.
        /// </summary>
        private static void ProcessGroupInput(Gizmo selectedGizmo, Event fakeEvent)
        {
            if (gizmoGroups.TryGetValue(selectedGizmo, out var group))
            {
                selectedGizmo.ProcessGroupInput(fakeEvent, group);
            }
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

            // Resolve lazy properties (Label, Disabled, disabledReason) with the owner
            // selected so they don't read stale Find.Selector state.
            string gizmoLabel = WithGizmoOwnerSelected(selectedGizmo, null, () => GetGizmoLabel(selectedGizmo));
            bool gizmoDisabled = WithGizmoOwnerSelected(selectedGizmo, null, () => selectedGizmo.Disabled);

            // Check if disabled
            if (gizmoDisabled)
            {
                string reason = WithGizmoOwnerSelected(selectedGizmo, null, () => selectedGizmo.disabledReason);
                if (string.IsNullOrEmpty(reason))
                    reason = "RimWorldAccess.Inspection.Gizmo.DisabledExecuteFallback".Translate();

                string announcement = "RimWorldAccess.Inspection.Gizmo.DisabledSuffix".Translate(reason);

                // Add helpful context for specific disabled scenarios (e.g., transport pod mass)
                ISelectable gizmoOwner = null;
                if (gizmoOwners.Count > 0)
                    gizmoOwners.TryGetValue(selectedGizmo, out gizmoOwner);
                string context = GetDisabledGizmoContext(selectedGizmo, gizmoOwner);
                if (!string.IsNullOrEmpty(context))
                    announcement += $". {context}";

                TolkHelper.SpeakData(announcement);
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
                            TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorActivationFailed".Loc(gizmoLabel), SpeechPriority.High);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        ModLogger.Error($"Exception in Designator execution: {ex.Message}");
                        TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorExecuting".Loc(gizmoLabel, ex.Message), SpeechPriority.High);
                    }

                    // Close the gizmo menu AFTER announcing (only reached on error)
                    Close();
                    return;
                }

                // For non-Designator gizmos, also select the owner so FloatMenu actions work correctly
                // (some actions check Find.Selector.SelectedObjects or Find.WorldSelector.SelectedObjects)
                // Skip when multi-select is active — selection is already correct and must not be cleared
                if (!PawnJustSelected && !MultiSelectState.IsMultiSelectMode && gizmoOwners.ContainsKey(selectedGizmo))
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

                // 2b. MechanitorControlGroupGizmo - open accessible control group menu
                if (selectedGizmo.GetType().Name == "MechanitorControlGroupGizmo")
                {
                    var mcg = MechControlGroupState.GetControlGroupFromGizmo(selectedGizmo);
                    if (mcg != null)
                    {
                        Close();
                        MechControlGroupState.Open(mcg);
                        return;
                    }
                }

                // 3. PsychicEntropyGizmo - toggle neural heat limiter
                if (selectedGizmo.GetType().Name == "PsychicEntropyGizmo")
                {
                    bool newState = ToggleNeuralHeatLimiter(selectedGizmo);
                    string stateStr = (newState
                        ? "RimWorldAccess.Inspection.Gizmo.LimiterStateOn"
                        : "RimWorldAccess.Inspection.Gizmo.LimiterStateOff").Translate();
                    TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NeuralHeatLimiter".Loc(stateStr));
                    return;
                }

                // 3b. GeneGizmo_ResourceHemogen - toggle hemogenPacksAllowed on Enter
                // (right-bracket still adjusts the target slider value, mirroring
                // the PsychicEntropyGizmo dual-action pattern).
                if (selectedGizmo.GetType().Name == "GeneGizmo_ResourceHemogen")
                {
                    bool? newState = ToggleHemogenPacksAllowed(selectedGizmo);
                    if (newState.HasValue)
                    {
                        string stateStr = (newState.Value
                            ? "RimWorldAccess.Inspection.Gizmo.LimiterStateOn"
                            : "RimWorldAccess.Inspection.Gizmo.LimiterStateOff").Translate();
                        string label = "AllowHemogenPacks".Translate().CapitalizeFirst();
                        TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.LabelWithState".Loc(label, stateStr));
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.CouldNotToggleHemogen".Loc(), SpeechPriority.High);
                    }
                    return;
                }

                // 4. Command_Toggle - toggle and announce state
                if (selectedGizmo is Command_Toggle toggle)
                {
                    try
                    {
                        // Propagate to grouped gizmos first (vanilla order)
                        PropagateToGroupedGizmos(selectedGizmo, fakeEvent);
                        // Execute the toggle
                        selectedGizmo.ProcessInput(fakeEvent);
                        // Process group input
                        ProcessGroupInput(selectedGizmo, fakeEvent);

                        // Announce the new state
                        bool toggleActive = toggle.isActive?.Invoke() ?? false;
                        string state = (toggleActive
                            ? "RimWorldAccess.Inspection.Gizmo.LimiterStateOn"
                            : "RimWorldAccess.Inspection.Gizmo.LimiterStateOff").Translate();
                        string label = GetGizmoLabel(toggle);
                        TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.LabelWithState".Loc(label, state));
                    }
                    catch (System.Exception ex)
                    {
                        ModLogger.Error($"Exception in Command_Toggle execution: {ex.Message}");
                        TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorExecuting".Loc(gizmoLabel, ex.Message), SpeechPriority.High);
                    }
                }
                else
                {
                    // 5. Command_VerbTarget (weapon attacks) - announce targeting mode
                    if (selectedGizmo is Command_VerbTarget verbTarget)
                    {
                        try
                        {
                            // Propagate to grouped gizmos first — adds other pawns
                            // to targetingSourceAdditionalPawns for group targeting
                            PropagateToGroupedGizmos(selectedGizmo, fakeEvent);
                            // Execute the command (starts targeting for this pawn)
                            selectedGizmo.ProcessInput(fakeEvent);
                            ProcessGroupInput(selectedGizmo, fakeEvent);

                            string weaponName = verbTarget.ownerThing?.LabelCap ?? "RimWorldAccess.Inspection.Gizmo.WeaponFallback".Translate().ToString();
                            string verbLabel = verbTarget.verb?.ReportLabel ?? "RimWorldAccess.Inspection.Gizmo.AttackFallback".Translate().ToString();
                            TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.WeaponVerbTargeting".Loc(weaponName, verbLabel));
                        }
                        catch (System.Exception ex)
                        {
                            ModLogger.Error($"Exception in Command_VerbTarget execution: {ex.Message}");
                            TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorExecuting".Loc(gizmoLabel, ex.Message), SpeechPriority.High);
                        }
                    }
                    // 6. Command_Target - announce targeting mode
                    else if (selectedGizmo is Command_Target cmdTarget)
                    {
                        try
                        {
                            // Propagate to grouped gizmos first (vanilla order)
                            PropagateToGroupedGizmos(selectedGizmo, fakeEvent);
                            // Execute the command
                            selectedGizmo.ProcessInput(fakeEvent);
                            ProcessGroupInput(selectedGizmo, fakeEvent);

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
                                    TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.AnimalTargetingWithRange".Loc(
                                        gizmoLabel, range.ToString("F0")));
                                }
                                else
                                {
                                    TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.UseMapNavigationToTarget".Loc(gizmoLabel));
                                }
                            }
                            else
                            {
                                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.UseMapNavigationToTarget".Loc(gizmoLabel));
                            }
                        }
                        catch (System.Exception ex)
                        {
                            ModLogger.Error($"Exception in Command_Target execution: {ex.Message}");
                            TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorExecuting".Loc(gizmoLabel, ex.Message), SpeechPriority.High);
                        }
                    }
                    // 7. Command_Ability (psycasts, abilities) - announce casting or targeting
                    else if (selectedGizmo is Command_Ability cmdAbility)
                    {
                        try
                        {
                            PropagateToGroupedGizmos(selectedGizmo, fakeEvent);
                            selectedGizmo.ProcessInput(fakeEvent);
                            ProcessGroupInput(selectedGizmo, fakeEvent);

                            // Self-cast abilities (targetRequired == false) don't enter targeting mode,
                            // so no AbilityTargetingPatch fires. Announce immediately.
                            if (!cmdAbility.Ability.def.targetRequired)
                            {
                                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.CastingAbility".Loc(
                                    cmdAbility.Ability.def.LabelCap));
                            }
                            // Targeted abilities will be announced by AbilityTargetingPatch
                        }
                        catch (System.Exception ex)
                        {
                            ModLogger.Error($"Exception in Command_Ability execution: {ex.Message}");
                            TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorExecuting".Loc(gizmoLabel, ex.Message), SpeechPriority.High);
                        }
                    }
                    // 8. Generic Command
                    else
                    {
                        try
                        {
                            PropagateToGroupedGizmos(selectedGizmo, fakeEvent);
                            selectedGizmo.ProcessInput(fakeEvent);
                            ProcessGroupInput(selectedGizmo, fakeEvent);
                        }
                        catch (System.Exception ex)
                        {
                            ModLogger.Error($"Exception in generic Command execution: {ex.Message}");
                            TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorExecuting".Loc(gizmoLabel, ex.Message), SpeechPriority.High);
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

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                selectedGizmoIndex = typeahead.GetFirstMatch();
                AnnounceWithSearch();
                return;
            }

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

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                selectedGizmoIndex = typeahead.GetLastMatch();
                AnnounceWithSearch();
                return;
            }

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
                typeahead.SpeakNoMatches();
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

            // Slider adjustment mode takes over all input
            if (isAdjustingSlider)
            {
                bool shift = Event.current.shift;
                int multiplier = shift ? 5 : 1;

                if (key == KeyCode.LeftArrow) { AdjustSliderValue(-1, multiplier); Event.current.Use(); return true; }
                if (key == KeyCode.RightArrow) { AdjustSliderValue(1, multiplier); Event.current.Use(); return true; }
                if (key == KeyCode.Escape || key == KeyCode.Return || key == KeyCode.KeypadEnter)
                { ExitSliderAdjust(); Event.current.Use(); return true; }
                // Consume all other keys while in slider mode
                Event.current.Use();
                return true;
            }

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

            // Handle Enter - execute selected (skip if Alt held, Alt+Enter opens pawn inspection)
            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !KeyboardHelper.IsAltHeld)
            {
                // Slider gizmos with no other Enter action enter adjustment mode directly.
                // PsychicEntropyGizmo already has Enter = toggle neural heat limiter, and
                // GeneGizmo_ResourceHemogen has Enter = toggle hemogenPacksAllowed; both
                // keep their existing behavior and use right bracket for slider adjustment.
                Gizmo enterGizmo = availableGizmos[selectedGizmoIndex];
                string enterTypeName = enterGizmo.GetType().Name;
                if (IsAdjustableSlider(enterGizmo)
                    && enterTypeName != "PsychicEntropyGizmo"
                    && enterTypeName != "GeneGizmo_ResourceHemogen")
                {
                    EnterSliderAdjustMode(enterGizmo);
                    Event.current.Use();
                    return true;
                }
                ExecuteSelected();
                Event.current.Use();
                return true;
            }

            // Handle Alt+I - open info card for current gizmo
            if (KeyboardHelper.IsAltHeld && key == KeyCode.I)
            {
                OpenInfoCardForCurrentGizmo();
                Event.current.Use();
                return true;
            }

            // Handle right bracket - right-click options or slider adjustment
            if (key == KeyCode.RightBracket)
            {
                Gizmo rbGizmo = availableGizmos[selectedGizmoIndex];
                if (HasRightClickOptions(rbGizmo))
                    ExecuteRightClick();
                else if (IsAdjustableSlider(rbGizmo))
                    EnterSliderAdjustMode(rbGizmo);
                else
                    TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NoAdditionalOptions".Loc());
                Event.current.Use();
                return true;
            }

            // Handle typeahead characters
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if (isLetter || isNumber)
            {
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
            InfoCardState.SpeakNoInfoCardAvailable();
        }

        /// <summary>
        /// Gets the list of labels for all gizmos.
        /// </summary>
        private static List<string> GetGizmoLabels()
        {
            var labels = new List<string>();
            foreach (var gizmo in availableGizmos)
            {
                // Lazy labels (e.g. Designator_Install) require the owner to be the
                // single-selected Thing to resolve correctly.
                Gizmo captured = gizmo;
                labels.Add(WithGizmoOwnerSelected(captured, null, () => GetGizmoLabel(captured)));
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

            if (typeahead.HasActiveSearch)
            {
                // Lazy gizmo properties (Label, Disabled) check Find.Selector.SingleSelectedThing.
                WithGizmoOwnerSelected(gizmo, null, () =>
                {
                    string label = GetGizmoLabel(gizmo);
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

                    TolkHelper.SpeakData(announcement);
                });
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

            // Lazy gizmo properties (Label, Desc, Disabled) check Find.Selector.SingleSelectedThing.
            // Wrap the property-reading section so the owner is the single-selected Thing.
            WithGizmoOwnerSelected(gizmo, null, () => AnnounceCurrentGizmoInner(gizmo));
        }

        private static void AnnounceCurrentGizmoInner(Gizmo gizmo)
        {
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

            // Hotkey immediately after the title, before any description / stats.
            if (!string.IsNullOrEmpty(hotkey))
                announcement += $" ({hotkey})";

            // For Command_Toggle, include current ON/OFF state (sighted players see a checkbox)
            if (gizmo is Command_Toggle toggle)
            {
                bool isOn = toggle.isActive?.Invoke() ?? false;
                announcement += (isOn
                    ? "RimWorldAccess.Inspection.Gizmo.StateOn"
                    : "RimWorldAccess.Inspection.Gizmo.StateOff").Translate();
            }

            // Add status value for non-Command gizmos (progress bars, etc.)
            if (!string.IsNullOrEmpty(statusValue))
                announcement += $": {statusValue}";

            bool isAbility = gizmo is Command_Ability;

            // For non-ability gizmos, append description after the hotkey/state
            if (!string.IsNullOrEmpty(description) && !isAbility)
            {
                string descSep = (gizmo is Command_Toggle || !string.IsNullOrEmpty(statusValue))
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
                announcement += sep + "RimWorldAccess.Inspection.Gizmo.RangeFromMaster".Translate(attackRange.ToString("F0"));
            }

            // For Command_Ability (psycasts, abilities), append cost, range, cooldown, then description.
            // The hotkey was already spoken right after the title.
            if (isAbility && gizmo is Command_Ability commandAbility && commandAbility.Ability != null)
            {
                string costInfo = GetAbilityCostInfo(commandAbility.Ability);
                if (!string.IsNullOrEmpty(costInfo))
                    announcement += (announcement.EndsWith(".") ? " " : ". ") + costInfo;

                string rangeInfo = GetAbilityRangeInfo(commandAbility.Ability);
                if (!string.IsNullOrEmpty(rangeInfo))
                    announcement += (announcement.EndsWith(".") ? " " : ". ") + rangeInfo;

                string cooldownInfo = GetAbilityCooldownInfo(commandAbility.Ability);
                if (!string.IsNullOrEmpty(cooldownInfo))
                    announcement += (announcement.EndsWith(".") ? " " : ". ") + cooldownInfo;

                if (!string.IsNullOrEmpty(description))
                    announcement += (announcement.EndsWith(".") ? " " : ". ") + description;
            }

            // Add disabled status if applicable
            if (gizmo.Disabled)
            {
                string reason = gizmo.disabledReason;
                if (string.IsNullOrEmpty(reason))
                    reason = "RimWorldAccess.Inspection.Gizmo.DisabledNotAvailable".Translate();
                announcement += " " + "RimWorldAccess.Inspection.Gizmo.DisabledSuffix".Translate(reason);

                // Add helpful context for specific disabled scenarios (e.g., transport pod mass)
                ISelectable gizmoOwner = null;
                if (gizmoOwners.Count > 0)
                    gizmoOwners.TryGetValue(gizmo, out gizmoOwner);
                string context = GetDisabledGizmoContext(gizmo, gizmoOwner);
                if (!string.IsNullOrEmpty(context))
                    announcement += $". {context}";
            }

            // Add interaction hints for right-click options and adjustable sliders
            if (!gizmo.Disabled)
            {
                if (HasRightClickOptions(gizmo))
                    announcement += "RimWorldAccess.Inspection.Gizmo.HintRightClickOptions".Translate();
                else if (IsAdjustableSlider(gizmo))
                {
                    string hintTypeName = gizmo.GetType().Name;
                    // PsychicEntropyGizmo has two actions: Enter toggles limiter, right bracket adjusts psyfocus
                    if (hintTypeName == "PsychicEntropyGizmo")
                        announcement += ". Press Enter to toggle limiter, right bracket to set psyfocus target";
                    // GeneGizmo_ResourceHemogen mirrors that pattern: Enter toggles hemogen packs allowed,
                    // right bracket adjusts the desired hemogen target value.
                    else if (hintTypeName == "GeneGizmo_ResourceHemogen")
                        announcement += ". Press Enter to toggle hemogen packs allowed, right bracket to set target";
                    else
                        announcement += ". Press Enter to adjust";
                }
            }

            TolkHelper.SpeakData(announcement);
        }

        /// <summary>
        /// Runs <paramref name="body"/> with the gizmo's owning Thing temporarily set as
        /// <c>Find.Selector.SingleSelectedThing</c>, then restores the previous selection.
        /// Many vanilla gizmos lazy-evaluate properties (Label, Desc, Visible, Disabled)
        /// against the live selection — e.g. <c>Designator_Install.Label</c> returns
        /// "Reinstall at..." instead of "Install" when the MinifiedThing isn't selected.
        /// Owner is taken from <paramref name="explicitOwner"/>, falling back to the
        /// gizmoOwners dictionary. Selection is only swapped when the owner is a Thing
        /// and isn't already the single-selected Thing; otherwise body runs unchanged.
        /// </summary>
        private static T WithGizmoOwnerSelected<T>(Gizmo gizmo, ISelectable explicitOwner, System.Func<T> body)
        {
            ISelectable owner = explicitOwner;
            if (owner == null && gizmoOwners != null)
                gizmoOwners.TryGetValue(gizmo, out owner);

            if (Find.Selector == null || !(owner is Thing thingOwner))
                return body();

            if (Find.Selector.SingleSelectedThing == thingOwner)
                return body();

            var previousSelection = Find.Selector.SelectedObjects.ToList();
            try
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(thingOwner, playSound: false, forceDesignatorDeselect: false);
                return body();
            }
            finally
            {
                Find.Selector.ClearSelection();
                foreach (var obj in previousSelection.OfType<ISelectable>())
                    Find.Selector.Select(obj, playSound: false, forceDesignatorDeselect: false);
            }
        }

        private static void WithGizmoOwnerSelected(Gizmo gizmo, ISelectable explicitOwner, System.Action body)
        {
            WithGizmoOwnerSelected<bool>(gizmo, explicitOwner, () => { body(); return true; });
        }

        /// <summary>
        /// Gets the label text for a gizmo.
        /// </summary>
        private static string GetGizmoLabel(Gizmo gizmo)
        {
            // Special handling for Command_VerbTarget (weapon attacks)
            if (gizmo is Command_VerbTarget verbTarget)
            {
                string weaponName = verbTarget.ownerThing?.LabelCap ?? "RimWorldAccess.Inspection.Gizmo.UnknownWeaponFallback".Translate().ToString();
                string verbLabel = verbTarget.verb?.ReportLabel ?? "RimWorldAccess.Inspection.Gizmo.AttackFallback".Translate().ToString();
                return "RimWorldAccess.Inspection.Gizmo.WeaponVerbLabel".Translate(weaponName, verbLabel);
            }

            if (gizmo is Command cmd)
            {
                string label = cmd.LabelCap;
                if (string.IsNullOrEmpty(label))
                    label = cmd.defaultLabel;
                if (string.IsNullOrEmpty(label))
                    label = "RimWorldAccess.Inspection.Gizmo.UnknownCommand".Translate();
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
                    return "RimWorldAccess.Inspection.Gizmo.Type.PsychicEntropy".Translate();

                case "MechanitorBandwidthGizmo":
                    return "RimWorldAccess.Inspection.Gizmo.Type.MechanitorBandwidth".Translate();

                case "Gizmo_GrowthTier":
                    return GetGrowthTierLabel(gizmo);

                case "Gizmo_RoomStats":
                    return GetRoomStatsLabel(gizmo);

                case "MechCarrierGizmo":
                    return GetMechCarrierLabel(gizmo);

                case "MechPowerCellGizmo":
                    return "RimWorldAccess.Inspection.Gizmo.Type.MechPowerCell".Translate();

                case "MechanitorControlGroupGizmo":
                    return MechControlGroupState.GetGizmoLabel(gizmo);

                case "Gizmo_MechResurrectionCharges":
                    return "RimWorldAccess.Inspection.Gizmo.Type.ResurrectionCharges".Translate();

                case "Gizmo_ProjectileInterceptorHitPoints":
                    return "RimWorldAccess.Inspection.Gizmo.Type.ShieldHitPoints".Translate();

                case "Gizmo_PruningConfig":
                    return "RimWorldAccess.Inspection.Gizmo.Type.PruningConfig".Translate();

                case "GuardianShipGizmo":
                    return "RimWorldAccess.Inspection.Gizmo.Type.GuardianShip".Translate();

                case "Gizmo_CaravanInfo":
                    return "RimWorldAccess.Inspection.Gizmo.Type.CaravanInfo".Translate();

                case "GeneGizmo_DeathrestCapacity":
                    // Vanilla translation key; resolves to "deathrest capacity" in English.
                    return "DeathrestCapacity".Translate().ToString().CapitalizeFirst();

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
                            return "RimWorldAccess.Inspection.Gizmo.Type.ShieldApparel".Translate(parent.LabelCap);
                        else
                            return "RimWorldAccess.Inspection.Gizmo.Type.ShieldInbuilt".Translate();
                    }
                }
            }
            catch { }
            return "RimWorldAccess.Inspection.Gizmo.Type.EnergyShield".Translate();
        }

        /// <summary>
        /// Gets the child pawn from a Gizmo_GrowthTier via reflection.
        /// </summary>
        private static Pawn GetGrowthTierChild(Gizmo gizmo)
        {
            try
            {
                var childField = gizmo.GetType().GetField("child",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                if (childField != null)
                    return childField.GetValue(gizmo) as Pawn;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Gets the label for a growth tier gizmo by accessing the child pawn.
        /// </summary>
        private static string GetGrowthTierLabel(Gizmo gizmo)
        {
            var child = GetGrowthTierChild(gizmo);
            if (child?.ageTracker == null)
                return "RimWorldAccess.Inspection.Gizmo.Type.GrowthTier".Translate();

            int tier = child.ageTracker.GrowthTier;
            return "RimWorldAccess.Inspection.Gizmo.Type.GrowthTierWithLevel".Translate(tier);
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
            return "RimWorldAccess.Inspection.Gizmo.Type.RoomStatsFallback".Translate();
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
                                return "RimWorldAccess.Inspection.Gizmo.Type.MechCarrierWithIngredient".Translate(ingredient.label?.CapitalizeFirst());
                        }
                    }
                }
            }
            catch { }
            return "RimWorldAccess.Inspection.Gizmo.Type.MechCarrier".Translate();
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
            return "RimWorldAccess.Inspection.Gizmo.Type.Activity".Translate();
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
            return string.IsNullOrEmpty(label)
                ? "RimWorldAccess.Inspection.Gizmo.Type.StatusDisplayFallback".Translate().ToString()
                : label;
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

                    case "MechanitorControlGroupGizmo":
                        return MechControlGroupState.GetGizmoStatus(gizmo);

                    case "GeneGizmo_DeathrestCapacity":
                        return GetDeathrestCapacityStatus(gizmo);

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
                return "RimWorldAccess.Inspection.Gizmo.Status.UsedTotal".Translate(used, total);
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
                        return "RimWorldAccess.Inspection.Gizmo.Status.PercentEnergy".Translate(
                            percent.ToString("F0"),
                            (energy * 100).ToString("F0"),
                            (maxEnergy * 100).ToString("F0"));
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

                var parts = new List<string>
                {
                    "RimWorldAccess.Inspection.Gizmo.Status.NeuralHeat".Translate(
                        entropy.ToString("F0"), maxEntropy.ToString("F0")),
                    "RimWorldAccess.Inspection.Gizmo.Status.PsyfocusValue".Translate(
                        (psyfocus * 100).ToString("F0")),
                };

                // Add psyfocus target (sighted players see a target indicator on the bar)
                var targetPsyfocusProp = tracker.GetType().GetProperty("TargetPsyfocus");
                if (targetPsyfocusProp != null)
                {
                    float target = (float)targetPsyfocusProp.GetValue(tracker);
                    parts.Add("RimWorldAccess.Inspection.Gizmo.Status.PsyfocusTarget".Translate(
                        (target * 100).ToString("F0")));
                }

                // Add limiter state
                if (limitField != null)
                {
                    bool isLimited = (bool)limitField.GetValue(tracker);
                    string stateWord = isLimited
                        ? "RimWorldAccess.Inspection.Gizmo.LimiterStateOn".Translate()
                        : "RimWorldAccess.Inspection.Gizmo.LimiterStateOff".Translate();
                    parts.Add("RimWorldAccess.Inspection.Gizmo.Status.LimiterStatus".Translate(stateWord));
                }

                return string.Join(", ", parts);
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
        /// Toggles Gene_Hemogen.hemogenPacksAllowed on a GeneGizmo_ResourceHemogen and
        /// returns the new state. Returns null if the gene field cannot be read (sanguophage
        /// genes from another mod, or Biotech disabled). Reflection-based so this file
        /// compiles without a Biotech reference at runtime.
        /// </summary>
        private static bool? ToggleHemogenPacksAllowed(Gizmo gizmo)
        {
            try
            {
                var flags = System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public;

                // Walk up to the base GeneGizmo_Resource where the gene field lives.
                System.Type t = gizmo.GetType();
                System.Reflection.FieldInfo geneField = null;
                while (t != null && geneField == null)
                {
                    geneField = t.GetField("gene", flags);
                    t = t.BaseType;
                }
                if (geneField == null) return null;

                var gene = geneField.GetValue(gizmo);
                if (gene == null) return null;

                var allowedField = gene.GetType().GetField("hemogenPacksAllowed", flags);
                if (allowedField == null) return null;

                bool currentValue = (bool)allowedField.GetValue(gene);
                bool newValue = !currentValue;
                allowedField.SetValue(gene, newValue);

                // Match the game's checkbox feedback (low tick when turning off, high when on).
                if (newValue)
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                else
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();

                return newValue;
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception toggling hemogen packs allowed: {ex.Message}");
                return null;
            }
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
                    parts.Add("RimWorldAccess.Inspection.Gizmo.Ability.PsyfocusCostAndMin".Translate(
                        (psyfocusCost * 100f).ToString("F0"),
                        (minRequired * 100f).ToString("F0")));
                }
                else if (psyfocusCost > float.Epsilon)
                {
                    // Just cost, no minimum band requirement
                    parts.Add("RimWorldAccess.Inspection.Gizmo.Ability.PsyfocusCost".Translate(
                        (psyfocusCost * 100f).ToString("F0")));
                }
                else if (minRequired > float.Epsilon)
                {
                    // Just minimum requirement (unusual but possible)
                    parts.Add("RimWorldAccess.Inspection.Gizmo.Ability.PsyfocusMinRequired".Translate(
                        (minRequired * 100f).ToString("F0")));
                }
            }

            // Neural heat (entropy) gain
            float entropyGain = ability.def.EntropyGain;
            if (entropyGain > float.Epsilon)
            {
                parts.Add("RimWorldAccess.Inspection.Gizmo.Ability.NeuralHeatGain".Translate(
                    entropyGain.ToString("F0")));
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
                rangeText = "RimWorldAccess.Inspection.Gizmo.Ability.RangeSelf".Translate();
            else if (ability.def.targetWorldCell)
                rangeText = "RimWorldAccess.Inspection.Gizmo.Ability.RangeWorldMap".Translate();
            else if (AbilityTargetingHelper.IsTouchRange(ability))
                rangeText = "RimWorldAccess.Inspection.Gizmo.Ability.RangeTouch".Translate();
            else
            {
                float range = AbilityTargetingHelper.GetRange(ability);
                if (range > 0f)
                    rangeText = "RimWorldAccess.Inspection.Gizmo.Ability.RangeTiles".Translate(range.ToString("F0"));
                else
                    return null;
            }

            // Append effect radius if this ability has one
            float effectRadius = ability.def.EffectRadius;
            if (effectRadius > 0f)
                rangeText += "RimWorldAccess.Inspection.Gizmo.Ability.EffectRadiusSuffix".Translate(
                    effectRadius.ToString("F0"));

            return rangeText;
        }

        /// <summary>
        /// Gets cooldown information for an ability.
        /// When on cooldown, shows remaining time. Otherwise shows base cooldown duration.
        /// The disabled section separately announces the full "on cooldown" game text.
        /// </summary>
        private static string GetAbilityCooldownInfo(Ability ability)
        {
            if (ability?.def == null)
                return null;

            string cooldownLabel = "StatsReport_Cooldown".Translate();

            // When on cooldown, show remaining time (most actionable info).
            // The disabled section already announces the full "AbilityOnCooldown" text.
            if (ability.OnCooldown && ability.CooldownTicksRemaining > 0)
            {
                string remaining = ability.CooldownTicksRemaining.ToStringTicksToPeriod();
                return "RimWorldAccess.Inspection.Gizmo.Ability.CooldownLine".Translate(
                    cooldownLabel, remaining);
            }

            // When not on cooldown, show base cooldown duration (matches game's stats report).
            // Group abilities use groupDef.cooldownTicks unless overrideGroupCooldown is set.
            // Individual abilities use cooldownTicksRange when it's a fixed value (min == max).
            int baseCooldownTicks = 0;
            if (ability.def.groupDef != null && !ability.def.overrideGroupCooldown
                && ability.def.groupDef.cooldownTicks > 0)
            {
                baseCooldownTicks = ability.def.groupDef.cooldownTicks;
            }
            else if (ability.def.cooldownTicksRange.min == ability.def.cooldownTicksRange.max
                     && ability.def.cooldownTicksRange.min > 0)
            {
                baseCooldownTicks = ability.def.cooldownTicksRange.min;
            }

            if (baseCooldownTicks > 0)
            {
                string baseDuration = baseCooldownTicks.ToStringTicksToPeriod(
                    allowSeconds: true, shortForm: false, canUseDecimals: true, allowYears: false);
                return "RimWorldAccess.Inspection.Gizmo.Ability.CooldownLine".Translate(
                    cooldownLabel, baseDuration);
            }

            return null;
        }

        /// <summary>
        /// Gets the growth tier status including progress, learning, and tooltip details.
        /// Mirrors the information sighted players see in the gizmo bar and hover tooltip.
        /// </summary>
        private static string GetGrowthTierStatus(Gizmo gizmo)
        {
            var child = GetGrowthTierChild(gizmo);
            if (child?.ageTracker == null) return "";

            int tier = child.ageTracker.GrowthTier;
            var parts = new List<string>();

            // Growth points progress (at-a-glance bar text)
            if (child.ageTracker.AtMaxGrowthTier)
            {
                float maxPoints = GrowthUtility.GrowthTiers[GrowthUtility.GrowthTiers.Length - 1].pointsRequirement;
                parts.Add("RimWorldAccess.Inspection.Gizmo.Status.GrowthPointsMaxTier".Translate(maxPoints));
            }
            else
            {
                int currentPoints = Mathf.FloorToInt(child.ageTracker.growthPoints);
                float nextTierPoints = GrowthUtility.GrowthTiers[tier + 1].pointsRequirement;
                string pointsText = "RimWorldAccess.Inspection.Gizmo.Status.GrowthPointsCurrentOfNext".Translate(
                    currentPoints, nextTierPoints);
                if (child.ageTracker.canGainGrowthPoints)
                {
                    string perDay = "PerDay".Translate(child.ageTracker.GrowthPointsPerDay.ToStringByStyle(ToStringStyle.FloatMaxTwo));
                    pointsText += "RimWorldAccess.Inspection.Gizmo.Status.GrowthPointsPerDaySuffix".Translate(perDay);
                }
                parts.Add(pointsText);
            }

            // Learning need (at-a-glance bar)
            if (child.needs?.learning != null)
                parts.Add("RimWorldAccess.Inspection.Gizmo.Status.GrowthLearningProgress".Translate(
                    child.needs.learning.CurLevelPercentage.ToStringPercent()));

            // Next growth moment age (from tooltip)
            if (child.ageTracker.AgeBiologicalYears < 13)
            {
                for (int i = child.ageTracker.AgeBiologicalYears + 1; i <= 13; i++)
                {
                    if (GrowthUtility.IsGrowthBirthday(i))
                    {
                        parts.Add("RimWorldAccess.Inspection.Gizmo.Status.GrowthNextMomentLine".Translate(
                            "NextGrowthMomentAt".Translate(), i));
                        break;
                    }
                }
            }

            // Current tier rewards (from tooltip)
            var currentTier = GrowthUtility.GrowthTiers[tier];
            parts.Add(FormatTierRewards("ThisGrowthTier".Translate(tier).ToString().StripTags(), currentTier));

            // Next tier rewards (from tooltip)
            if (!child.ageTracker.AtMaxGrowthTier)
            {
                var nextTier = GrowthUtility.GrowthTiers[tier + 1];
                string nextHeader = "RimWorldAccess.Inspection.Gizmo.Status.GrowthNextTierHeader".Translate(tier + 1);
                parts.Add(FormatTierRewards(nextHeader, nextTier));
            }

            // Trim trailing periods from each part to avoid double periods in the joined result
            for (int i = 0; i < parts.Count; i++)
                parts[i] = parts[i].TrimEnd('.');
            return string.Join(". ", parts);
        }

        /// <summary>
        /// Formats tier reward text using the same translation keys as the game's tooltip.
        /// Joins multiple rewards with the localized RewardsAnd connective for natural
        /// speech flow.
        /// </summary>
        private static string FormatTierRewards(string header, GrowthUtility.GrowthTier tier)
        {
            var rewards = new List<string>();
            if (tier.passionGainsRange.TrueMax > 0)
                rewards.Add("NumPassionsFromOptions".Translate(tier.passionGainsRange.ToString(), tier.passionChoices).Resolve().StripTags().TrimEnd('.'));
            rewards.Add("NumTraitsFromOptions".Translate(tier.traitGains, tier.traitChoices).Resolve().StripTags().TrimEnd('.'));
            string connective = "RimWorldAccess.Inspection.Gizmo.Status.GrowthRewardsAnd".Translate();
            return "RimWorldAccess.Inspection.Gizmo.Status.GrowthTierRewardsLine".Translate(
                header,
                string.Join(connective, rewards));
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
                        return "RimWorldAccess.Inspection.Gizmo.Status.UsedTotal".Translate(count, max);
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

            return "RimWorldAccess.Inspection.Gizmo.Status.PercentEnergy".Translate(
                percent.ToString("F0"), energy.ToString("F1"), max.ToString("F1"));
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
                return "RimWorldAccess.Inspection.Gizmo.Status.Charges".Translate(charges, max);
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
                return "RimWorldAccess.Inspection.Gizmo.Status.HitPoints".Translate(hp, maxHp);
            }
            return "";
        }

        /// <summary>
        /// Gets the deathrest capacity status: progress percentage and currently-bound
        /// building count vs. capacity. Reads Gene_Deathrest via reflection so this file
        /// compiles cleanly when Biotech isn't loaded at runtime.
        /// </summary>
        private static string GetDeathrestCapacityStatus(Gizmo gizmo)
        {
            var geneField = gizmo.GetType().GetField("gene",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            if (geneField == null) return "";

            var gene = geneField.GetValue(gizmo);
            if (gene == null) return "";

            var percentProp = gene.GetType().GetProperty("DeathrestPercent");
            var currentProp = gene.GetType().GetProperty("CurrentCapacity");
            var maxProp = gene.GetType().GetProperty("DeathrestCapacity");

            string percentStr = "";
            if (percentProp != null)
            {
                float percent = (float)percentProp.GetValue(gene);
                percentStr = $"{percent * 100:F0}%";
            }

            string buildingStr = "";
            if (currentProp != null && maxProp != null)
            {
                int current = (int)currentProp.GetValue(gene);
                int max = (int)maxProp.GetValue(gene);
                string buildingsLabel = "Buildings".Translate().CapitalizeFirst();
                buildingStr = $"{buildingsLabel}: {current} / {max}";
            }

            if (!string.IsNullOrEmpty(percentStr) && !string.IsNullOrEmpty(buildingStr))
                return $"{percentStr}, {buildingStr}";
            return percentStr + buildingStr;
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
                return "RimWorldAccess.Inspection.Gizmo.Status.Percent".Translate((value * 100).ToString("F0"));
            }
            return "";
        }

        /// <summary>
        /// Gets the description text for a gizmo.
        /// For Command_Ability (psycasts), pulls from ability.def.description since
        /// Command_Ability.Desc is only populated during mouse hover rendering.
        /// For non-Command status gizmos (Gizmo_Slider, GeneGizmo_DeathrestCapacity),
        /// surfaces the protected GetTooltip() / equivalent translated description so
        /// blind users hear the same context sighted users see on hover.
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

            // Non-Command status gizmos: surface their hover tooltip text.
            return GetNonCommandGizmoDescription(gizmo);
        }

        /// <summary>
        /// Surfaces non-Command gizmo descriptions (status panels, resource bars). These
        /// types render their context only as hover tooltips; we extract the same text so
        /// it can be spoken. Newlines are flattened to periods (memory: never use newlines
        /// as separators in announcements).
        /// </summary>
        private static string GetNonCommandGizmoDescription(Gizmo gizmo)
        {
            string typeName = gizmo.GetType().Name;

            try
            {
                if (typeName == "GeneGizmo_DeathrestCapacity")
                {
                    var geneField = gizmo.GetType().GetField("gene",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);
                    var gene = geneField?.GetValue(gizmo);
                    string baseDesc = "DeathrestCapacityDesc".Translate();
                    if (gene != null)
                    {
                        var pawnField = gene.GetType().GetField("pawn",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Public);
                        var pawn = pawnField?.GetValue(gene) as Pawn;
                        var currentProp = gene.GetType().GetProperty("CurrentCapacity");
                        var maxProp = gene.GetType().GetProperty("DeathrestCapacity");
                        var boundProp = gene.GetType().GetProperty("BoundBuildings");
                        var parts = new List<string> { baseDesc };
                        if (pawn != null && currentProp != null && maxProp != null)
                        {
                            int current = (int)currentProp.GetValue(gene);
                            int max = (int)maxProp.GetValue(gene);
                            string connected = "PawnIsConnectedToBuildings".Translate(
                                pawn.Named("PAWN"),
                                current.Named("CURRENT"),
                                max.Named("MAX")).Resolve();
                            parts.Add(connected);
                        }
                        // Surface the names of bound buildings — sighted players see these via
                        // hose lines drawn from each building to the deathrester's bed.
                        if (boundProp != null && boundProp.GetValue(gene) is System.Collections.IEnumerable boundList)
                        {
                            var names = new List<string>();
                            foreach (var t in boundList)
                            {
                                if (t is Thing thing)
                                    names.Add(thing.LabelShortCap);
                            }
                            // No vanilla translation key for this label; "Bound buildings" is
                            // a mod-coined phrase. Localizable alternatives may be added later.
                            if (names.Count > 0)
                                parts.Add($"Bound buildings: {names.ToCommaList(useAnd: true)}");
                        }
                        return FlattenNewlines(string.Join(". ", parts));
                    }
                    return FlattenNewlines(baseDesc);
                }

                // Gizmo_Slider exposes a protected abstract GetTooltip() — surface it as the
                // description so blind users hear the rich tooltip (e.g., per-gene drain
                // breakdown on the Hemogen bar) that sighted players see on hover.
                if (gizmo is Verse.Gizmo_Slider)
                {
                    var tooltipMethod = gizmo.GetType().GetMethod("GetTooltip",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);
                    if (tooltipMethod != null)
                    {
                        string tooltip = tooltipMethod.Invoke(gizmo, null) as string;
                        return FlattenNewlines(StripRedundantSliderTitle(gizmo, (tooltip ?? "").StripTags()));
                    }
                }
            }
            catch { }

            return "";
        }

        /// <summary>
        /// Drops the leading "{Title}: ..." line from a Gizmo_Slider tooltip when that
        /// line just repeats the title — we already announce "{Title}: {percent}" as the
        /// status prefix, so leaving the tooltip's own header in would echo the title and
        /// the absolute value back to back ("Hemogen: 90%. Hemogen: 90 / 100. ..."). The
        /// rest of the tooltip (drain breakdown, description, etc.) is preserved.
        /// </summary>
        private static string StripRedundantSliderTitle(Gizmo gizmo, string tooltip)
        {
            if (string.IsNullOrEmpty(tooltip)) return tooltip;

            string title = null;
            try
            {
                var titleProp = gizmo.GetType().GetProperty("Title",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                title = titleProp?.GetValue(gizmo) as string;
            }
            catch { }

            if (string.IsNullOrEmpty(title)) return tooltip;

            // The title in the tooltip is rendered with the same casing the gizmo header
            // uses (CapitalizeFirst). Match leniently: if the first non-blank line begins
            // with the title (case-insensitive) followed by ":" or " ", drop that line.
            int firstBreak = tooltip.IndexOf('\n');
            string firstLine = firstBreak >= 0 ? tooltip.Substring(0, firstBreak) : tooltip;
            string trimmed = firstLine.TrimEnd();
            if (trimmed.StartsWith(title, System.StringComparison.OrdinalIgnoreCase))
            {
                int afterTitle = title.Length;
                if (afterTitle >= trimmed.Length
                    || trimmed[afterTitle] == ':'
                    || trimmed[afterTitle] == ' ')
                {
                    return firstBreak >= 0 ? tooltip.Substring(firstBreak + 1) : "";
                }
            }
            return tooltip;
        }

        /// <summary>
        /// Replaces newline runs with period+space so multi-line tooltip text reads as a
        /// single sentence. Memory: never use newlines as announcement separators.
        /// </summary>
        private static string FlattenNewlines(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new System.Text.StringBuilder(text.Length);
            bool inBreak = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n' || c == '\r')
                {
                    if (!inBreak)
                    {
                        // Trim trailing whitespace before the break.
                        while (sb.Length > 0 && char.IsWhiteSpace(sb[sb.Length - 1]))
                            sb.Length--;
                        if (sb.Length > 0 && sb[sb.Length - 1] != '.' && sb[sb.Length - 1] != '!' && sb[sb.Length - 1] != '?' && sb[sb.Length - 1] != ':' && sb[sb.Length - 1] != ',')
                            sb.Append('.');
                        sb.Append(' ');
                        inBreak = true;
                    }
                }
                else
                {
                    sb.Append(c);
                    inBreak = false;
                }
            }
            return sb.ToString().Trim();
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
                    string prefix = GizmoHotkeyShiftPatch.IsShiftExempt(cmd.hotKey)
                        ? ""
                        : GizmoHotkeyShiftPatch.ShiftPrefix;
                    return prefix + key.ToStringReadable();
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
            var transportersInGroup = transporter.TransportersInGroup(transporter.parent.Map)?.ToList();
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
            var fragments = new List<string>
            {
                "RimWorldAccess.Inspection.Gizmo.Launch.GroupTotal".Translate(
                    groupMassUsage.ToString("F0"), groupMassCapacity.ToString("F0")),
            };

            // If group isn't overloaded but this pod is, explain the workaround
            if (groupMassUsage <= groupMassCapacity && overloadedCount > 0 && canLaunchCount > 0)
            {
                fragments.Add("RimWorldAccess.Inspection.Gizmo.Launch.PodsCanLaunch".Translate(
                    canLaunchCount, transportersInGroup.Count));
                fragments.Add("RimWorldAccess.Inspection.Gizmo.Launch.SelectAnotherPod".Translate());
            }
            else if (groupMassUsage > groupMassCapacity)
            {
                float overBy = groupMassUsage - groupMassCapacity;
                fragments.Add("RimWorldAccess.Inspection.Gizmo.Launch.OverCapacity".Translate(
                    overBy.ToString("F0")));
            }

            return string.Join(". ", fragments);
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
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NoMaterialsForBuildable".Loc(buildable.label));
                return;
            }

            // Announce and open material selection menu
            TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.SelectMaterialFor".Loc(buildable.label));
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

                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.MaterialSelected".Loc(material.LabelCap));
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception in gizmo material selection: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorWithMessage".Loc(ex.Message), SpeechPriority.High);
            }
        }

        // ===== Right-click float menu support =====

        /// <summary>
        /// Checks whether the given gizmo has any right-click float menu options.
        /// </summary>
        private static bool HasRightClickOptions(Gizmo gizmo)
        {
            try
            {
                return gizmo.RightClickFloatMenuOptions.Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks whether the given gizmo is an adjustable slider.
        /// </summary>
        private static bool IsAdjustableSlider(Gizmo gizmo)
        {
            try
            {
                if (gizmo is Gizmo_Slider)
                {
                    var isDraggableProp = gizmo.GetType().GetProperty("IsDraggable",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);
                    if (isDraggableProp != null)
                        return (bool)isDraggableProp.GetValue(gizmo);
                    return false;
                }

                string typeName = gizmo.GetType().Name;
                if (typeName == "PsychicEntropyGizmo")
                {
                    // Only adjustable for colonist-player-controlled pawns
                    var tracker = GetPsychicEntropyTracker(gizmo);
                    if (tracker == null) return false;
                    var pawnProp = tracker.GetType().GetProperty("Pawn");
                    if (pawnProp == null) return false;
                    var pawn = pawnProp.GetValue(tracker) as Pawn;
                    return pawn != null && pawn.IsColonistPlayerControlled;
                }

                if (typeName == "Gizmo_PruningConfig" || typeName == "MechCarrierGizmo")
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Collects right-click float menu options from the gizmo and its grouped gizmos,
        /// mirroring vanilla GizmoGridDrawer behavior.
        /// </summary>
        private static List<FloatMenuOption> CollectRightClickOptions(Gizmo gizmo)
        {
            var options = new List<FloatMenuOption>();

            try
            {
                // Collect from the primary gizmo
                foreach (var opt in gizmo.RightClickFloatMenuOptions)
                    options.Add(opt);

                // Merge from grouped gizmos (mirrors vanilla GizmoGridDrawer lines 368-405)
                if (gizmoGroups.TryGetValue(gizmo, out var group))
                {
                    for (int i = 0; i < group.Count; i++)
                    {
                        Gizmo other = group[i];
                        if (other == gizmo || other.Disabled || !gizmo.InheritFloatMenuInteractionsFrom(other))
                            continue;

                        foreach (var opt in other.RightClickFloatMenuOptions)
                        {
                            var existing = options.FirstOrDefault(o => o.Label == opt.Label);
                            if (existing == null)
                            {
                                options.Add(opt);
                            }
                            else if (!opt.Disabled && existing.Disabled)
                            {
                                int idx = options.IndexOf(existing);
                                options[idx] = opt;
                            }
                            else if (!opt.Disabled && !existing.Disabled)
                            {
                                System.Action prevAction = existing.action;
                                System.Action localAction = opt.action;
                                existing.action = delegate { prevAction(); localAction(); };
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception collecting right-click options: {ex.Message}");
            }

            return options;
        }

        /// <summary>
        /// Executes the right-click action for the currently selected gizmo,
        /// opening its float menu options in the accessible WindowlessFloatMenuState.
        /// </summary>
        public static void ExecuteRightClick()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            Gizmo gizmo = availableGizmos[selectedGizmoIndex];

            if (gizmo.Disabled)
            {
                string reason = gizmo.disabledReason;
                if (string.IsNullOrEmpty(reason))
                    reason = "RimWorldAccess.Inspection.Gizmo.DisabledExecuteFallback".Translate();
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.DisabledSuffix".Loc(reason));
                return;
            }

            var options = CollectRightClickOptions(gizmo);
            if (options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NoAdditionalOptions".Loc());
                return;
            }

            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        // ===== Slider adjustment support =====

        /// <summary>
        /// Enters slider adjustment mode for the given gizmo.
        /// Reads the current target value, step size, and range via reflection.
        /// </summary>
        private static void EnterSliderAdjustMode(Gizmo gizmo)
        {
            sliderGizmo = gizmo;
            string title = "";

            try
            {
                if (gizmo is Gizmo_Slider)
                {
                    var flags = System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public;

                    var targetProp = gizmo.GetType().GetProperty("Target", flags);
                    var dragRangeProp = gizmo.GetType().GetProperty("DragRange", flags);
                    var incrementsProp = gizmo.GetType().GetProperty("Increments", flags);
                    var titleProp = gizmo.GetType().GetProperty("Title", flags);

                    if (targetProp == null) { TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.CouldNotReadSliderValue".Loc()); return; }

                    sliderValue = (float)targetProp.GetValue(gizmo);

                    if (dragRangeProp != null)
                    {
                        var range = (FloatRange)dragRangeProp.GetValue(gizmo);
                        sliderMin = range.min;
                        sliderMax = range.max;
                    }
                    else
                    {
                        sliderMin = 0f;
                        sliderMax = 1f;
                    }

                    int increments = 20;
                    if (incrementsProp != null)
                        increments = (int)incrementsProp.GetValue(gizmo);

                    sliderStep = (sliderMax - sliderMin) / increments;
                    if (titleProp != null)
                        title = (string)titleProp.GetValue(gizmo) ?? "";
                }
                else
                {
                    string typeName = gizmo.GetType().Name;
                    var flags = System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public;

                    if (typeName == "PsychicEntropyGizmo")
                    {
                        var targetField = gizmo.GetType().GetField("targetValue", flags);
                        if (targetField == null) { TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.CouldNotReadPsyfocusValue".Loc()); return; }

                        sliderValue = (float)targetField.GetValue(gizmo);
                        sliderMin = 0f;
                        sliderMax = 1f;
                        sliderStep = 1f / 16f;
                        title = "PsyfocusLabelGizmo".Translate();
                    }
                    else if (typeName == "Gizmo_PruningConfig")
                    {
                        var connectionField = gizmo.GetType().GetField("connection", flags);
                        if (connectionField == null) { TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.CouldNotReadPruningValue".Loc()); return; }
                        var connection = connectionField.GetValue(gizmo);
                        if (connection == null) { TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.CouldNotReadPruningValue".Loc()); return; }

                        var desiredProp = connection.GetType().GetProperty("DesiredConnectionStrength", flags);
                        if (desiredProp == null) { TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.CouldNotReadPruningValue".Loc()); return; }

                        sliderValue = (float)desiredProp.GetValue(connection);
                        sliderMin = 0f;
                        sliderMax = 1f;
                        sliderStep = 1f / 20f;
                        title = "ConnectionStrength".Translate();
                    }
                    else if (typeName == "MechCarrierGizmo")
                    {
                        var targetField = gizmo.GetType().GetField("targetValue", flags);
                        if (targetField == null) { TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.CouldNotReadCarrierValue".Loc()); return; }

                        sliderValue = (float)targetField.GetValue(gizmo);
                        sliderMin = 0f;
                        sliderMax = 1f;
                        sliderStep = 1f / 24f;
                        title = GetMechCarrierTitle(gizmo);
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.CouldNotAdjustControl".Loc());
                        return;
                    }
                }
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception entering slider mode: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.CouldNotAdjustControl".Loc());
                return;
            }

            isAdjustingSlider = true;
            string valueStr = $"{sliderValue * 100:F0}%";
            TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.SliderEnterPrompt".Loc(title, valueStr));
        }

        /// <summary>
        /// Adjusts the slider value by step * multiplier in the given direction.
        /// </summary>
        private static void AdjustSliderValue(int direction, int multiplier = 1)
        {
            if (sliderGizmo == null) return;

            float oldValue = sliderValue;
            sliderValue += direction * sliderStep * multiplier;
            sliderValue = Mathf.Clamp(sliderValue, sliderMin, sliderMax);

            // Snap to nearest step to avoid floating point drift
            float stepsFromMin = Mathf.Round((sliderValue - sliderMin) / sliderStep);
            sliderValue = sliderMin + stepsFromMin * sliderStep;
            sliderValue = Mathf.Clamp(sliderValue, sliderMin, sliderMax);

            if (Mathf.Approximately(sliderValue, oldValue))
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }

            // Play the drag slider sound (matches vanilla behavior)
            SoundDefOf.DragSlider.PlayOneShotOnCamera();

            // Write value back to the gizmo
            try
            {
                WriteSliderValue(sliderGizmo, sliderValue);
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception writing slider value: {ex.Message}");
            }

            // Announce the new value
            string announcement = GetSliderAnnouncement(sliderGizmo, sliderValue);
            TolkHelper.SpeakData(announcement);
        }

        /// <summary>
        /// Writes the adjusted slider value back to the gizmo via reflection.
        /// </summary>
        private static void WriteSliderValue(Gizmo gizmo, float value)
        {
            var flags = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public;

            if (gizmo is Gizmo_Slider)
            {
                var targetProp = gizmo.GetType().GetProperty("Target", flags);
                targetProp?.SetValue(gizmo, value);
            }
            else
            {
                string typeName = gizmo.GetType().Name;

                if (typeName == "PsychicEntropyGizmo")
                {
                    // Write to both the gizmo's targetValue and the tracker
                    var targetField = gizmo.GetType().GetField("targetValue", flags);
                    targetField?.SetValue(gizmo, value);

                    var tracker = GetPsychicEntropyTracker(gizmo);
                    if (tracker != null)
                    {
                        var setMethod = tracker.GetType().GetMethod("SetPsyfocusTarget", flags);
                        setMethod?.Invoke(tracker, new object[] { value });
                    }
                }
                else if (typeName == "Gizmo_PruningConfig")
                {
                    var connectionField = gizmo.GetType().GetField("connection", flags);
                    var connection = connectionField?.GetValue(gizmo);
                    if (connection != null)
                    {
                        var desiredProp = connection.GetType().GetProperty("DesiredConnectionStrength", flags);
                        desiredProp?.SetValue(connection, value);
                    }
                }
                else if (typeName == "MechCarrierGizmo")
                {
                    // Write targetValue on gizmo
                    var targetField = gizmo.GetType().GetField("targetValue", flags);
                    targetField?.SetValue(gizmo, value);

                    // Also update carrier.maxToFill = Round(value * maxIngredientCount)
                    var carrierField = gizmo.GetType().GetField("carrier", flags);
                    var carrier = carrierField?.GetValue(gizmo);
                    if (carrier != null)
                    {
                        var propsProp = carrier.GetType().GetProperty("Props");
                        var props = propsProp?.GetValue(carrier);
                        if (props != null)
                        {
                            var maxField = props.GetType().GetField("maxIngredientCount");
                            if (maxField != null)
                            {
                                int maxCount = (int)maxField.GetValue(props);
                                int newFill = Mathf.RoundToInt(value * maxCount);
                                var maxToFillField = carrier.GetType().GetField("maxToFill",
                                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                                maxToFillField?.SetValue(carrier, newFill);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets the announcement text for the current slider value.
        /// Uses BarLabel where available for richer info (e.g., "75 / 200 fuel").
        /// </summary>
        private static string GetSliderAnnouncement(Gizmo gizmo, float value)
        {
            var flags = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public;

            try
            {
                if (gizmo is Gizmo_Slider)
                {
                    // Try to get BarLabel for richer display
                    var barLabelProp = gizmo.GetType().GetProperty("BarLabel", flags);
                    if (barLabelProp != null)
                    {
                        string barLabel = (string)barLabelProp.GetValue(gizmo);
                        if (!string.IsNullOrEmpty(barLabel))
                            return "RimWorldAccess.Inspection.Gizmo.Status.SliderTargetWithLabel".Translate(
                                (value * 100).ToString("F0"), barLabel);
                    }
                }
                else if (gizmo.GetType().Name == "MechCarrierGizmo")
                {
                    // Show count / max for carrier
                    var carrierField = gizmo.GetType().GetField("carrier", flags);
                    var carrier = carrierField?.GetValue(gizmo);
                    if (carrier != null)
                    {
                        var propsProp = carrier.GetType().GetProperty("Props");
                        var props = propsProp?.GetValue(carrier);
                        if (props != null)
                        {
                            var maxField = props.GetType().GetField("maxIngredientCount");
                            if (maxField != null)
                            {
                                int maxCount = (int)maxField.GetValue(props);
                                int targetCount = Mathf.RoundToInt(value * maxCount);
                                return "RimWorldAccess.Inspection.Gizmo.Status.SliderTargetCount".Translate(
                                    targetCount, maxCount);
                            }
                        }
                    }
                }
                else if (gizmo.GetType().Name == "Gizmo_PruningConfig")
                {
                    // Show percentage and pruning hours
                    var connectionField = gizmo.GetType().GetField("connection", flags);
                    var connection = connectionField?.GetValue(gizmo);
                    if (connection != null)
                    {
                        var hoursMethod = connection.GetType().GetMethod("PruningHoursToMaintain", flags);
                        if (hoursMethod != null)
                        {
                            float hours = (float)hoursMethod.Invoke(connection, new object[] { value });
                            string hoursLabel = "PruningHoursToMaintain".Translate().ToString().Split(':')[0].Trim();
                            string trailing = "RimWorldAccess.Inspection.Gizmo.Status.PruningHoursValue".Translate(
                                hours.ToString("F1"), hoursLabel);
                            return "RimWorldAccess.Inspection.Gizmo.Status.SliderTargetWithLabel".Translate(
                                (value * 100).ToString("F0"), trailing);
                        }
                    }
                }
            }
            catch
            {
                // Fall through to default
            }

            return "RimWorldAccess.Inspection.Gizmo.Status.SliderTargetPercent".Translate((value * 100).ToString("F0"));
        }

        /// <summary>
        /// Gets the title/label for a MechCarrierGizmo (the ingredient name).
        /// </summary>
        private static string GetMechCarrierTitle(Gizmo gizmo)
        {
            try
            {
                var flags = System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public;
                var carrierField = gizmo.GetType().GetField("carrier", flags);
                var carrier = carrierField?.GetValue(gizmo);
                if (carrier != null)
                {
                    var propsProp = carrier.GetType().GetProperty("Props");
                    var props = propsProp?.GetValue(carrier);
                    if (props != null)
                    {
                        var ingredientField = props.GetType().GetField("fixedIngredient");
                        if (ingredientField != null)
                        {
                            var ingredient = ingredientField.GetValue(props) as Def;
                            if (ingredient != null)
                                return ingredient.LabelCap;
                        }
                    }
                }
            }
            catch { }
            return "RimWorldAccess.Inspection.Gizmo.Type.CarrierFallback".Translate();
        }

        /// <summary>
        /// Exits slider adjustment mode and re-announces the current gizmo.
        /// </summary>
        private static void ExitSliderAdjust()
        {
            isAdjustingSlider = false;
            sliderGizmo = null;
            AnnounceCurrentGizmo();
        }

        /// <summary>
        /// Tries to activate a gizmo whose hotkey matches the given key by searching
        /// both the current selection and the cursor-tile objects. Always returns true
        /// (event consumed): single match activates directly, multiple matches open a
        /// WindowlessFloatMenuState for disambiguation, zero matches plays a rejection
        /// sound and announces "no command for Shift+X" so the user knows the keypress
        /// was received.
        ///
        /// Collects cursor-tile gizmos using the same temp-select-then-restore pattern
        /// as OpenAtCursor, because some gizmos (e.g. Designator_Install) only expose
        /// themselves when their owning Thing is selected.
        /// </summary>
        public static bool TryHotkeyActivate(KeyCode key)
        {
            if (key == KeyCode.None)
                return false;
            if (Find.Selector == null || Find.CurrentMap == null)
                return false;

            var collected = CollectHotkeyCandidates(key);

            // Group matching gizmos using vanilla's GroupsWith/MergeWith logic so that
            // identical gizmos from multiple selected pawns collapse to a single entry.
            var rawGizmos = collected.Select(c => c.gizmo).ToList();
            var rawOwners = new Dictionary<Gizmo, ISelectable>();
            foreach (var (gizmo, owner) in collected)
            {
                if (!rawOwners.ContainsKey(gizmo))
                    rawOwners[gizmo] = owner;
            }

            var groups = new List<List<Gizmo>>();
            foreach (var gizmo in rawGizmos)
            {
                bool grouped = false;
                for (int i = 0; i < groups.Count; i++)
                {
                    if (groups[i][0].GroupsWith(gizmo))
                    {
                        groups[i].Add(gizmo);
                        groups[i][0].MergeWith(gizmo);
                        grouped = true;
                        break;
                    }
                }
                if (!grouped)
                    groups.Add(new List<Gizmo> { gizmo });
            }

            var representatives = groups
                .Select(g => g[0])
                .OrderBy(g => g.Order)
                .ToList();

            if (representatives.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NoCommandForShiftPlus".Loc(key.ToStringReadable()));
                return true;
            }

            if (representatives.Count == 1)
            {
                Gizmo only = representatives[0];
                ISelectable owner = rawOwners.TryGetValue(only, out var o) ? o : null;
                List<Gizmo> group = groups.First(g => g[0] == only);
                ActivateSingleGizmo(only, owner, group);
                return true;
            }

            // Multiple matches — open a WindowlessFloatMenuState so the user hears the
            // familiar menu-open sound and can pick one. Each option's Label is the full
            // rich gizmo announcement (owner, hotkey, description, stats, disabled reason)
            // so the user hears the same info they would in the G menu.
            var options = new List<FloatMenuOption>();
            foreach (var rep in representatives)
            {
                ISelectable owner = rawOwners.TryGetValue(rep, out var o) ? o : null;
                List<Gizmo> group = groups.First(g => g[0] == rep);
                string label = BuildGizmoMenuLabel(rep, owner);

                if (rep.Disabled)
                {
                    options.Add(new FloatMenuOption(label, null) { Disabled = true });
                    continue;
                }

                Gizmo capturedGizmo = rep;
                ISelectable capturedOwner = owner;
                List<Gizmo> capturedGroup = group;
                options.Add(new FloatMenuOption(label, () =>
                {
                    ActivateSingleGizmo(capturedGizmo, capturedOwner, capturedGroup);
                }));
            }

            WindowlessFloatMenuState.Open(options, colonistOrders: false);
            return true;
        }

        /// <summary>
        /// Seeds GizmoNavigationState with a single gizmo and invokes ExecuteSelected so
        /// the existing activation paths (designator, toggle, verb-target, ability, float-
        /// menu, etc) all apply unchanged. Closes the state afterward so we don't leak a
        /// phantom single-item G menu.
        /// </summary>
        private static void ActivateSingleGizmo(Gizmo gizmo, ISelectable owner, List<Gizmo> group)
        {
            availableGizmos.Clear();
            gizmoOwners.Clear();
            gizmoGroups.Clear();
            availableGizmos.Add(gizmo);
            if (owner != null)
                gizmoOwners[gizmo] = owner;
            gizmoGroups[gizmo] = group ?? new List<Gizmo> { gizmo };
            selectedGizmoIndex = 0;
            isActive = true;
            typeahead.ClearSearch();
            lastAnnouncedOwner = null;
            pawnJustSelected = false;

            ExecuteSelected();

            // ExecuteSelected closes the state for designator/build paths but leaves it
            // open for toggle/verb-target/target so the G menu can keep navigating. In
            // hotkey context the user is done, so close here unconditionally.
            if (isActive)
                Close();
        }

        /// <summary>
        /// Builds the rich announcement string for a gizmo, mirroring AnnounceCurrentGizmo
        /// but as a pure function that always includes the owner prefix. Used for
        /// FloatMenuOption labels when Shift+hotkey has multiple matches.
        /// </summary>
        private static string BuildGizmoMenuLabel(Gizmo gizmo, ISelectable owner)
        {
            // Hotkey collector swaps selection per-thing during collection but restores it
            // afterward. Lazy properties (Label, Desc, Disabled) here would otherwise see
            // the stale selection and resolve incorrectly (e.g. Install -> "Reinstall at...").
            return WithGizmoOwnerSelected(gizmo, owner, () => BuildGizmoMenuLabelInner(gizmo, owner));
        }

        private static string BuildGizmoMenuLabelInner(Gizmo gizmo, ISelectable owner)
        {
            string label = GetGizmoLabel(gizmo);
            string description = GetGizmoDescription(gizmo);
            string hotkey = GetGizmoHotkey(gizmo);
            string statusValue = GetGizmoStatusValue(gizmo);

            string ownerLabel = "";
            if (owner is Thing thing)
                ownerLabel = thing.LabelCap.StripTags();
            else if (owner is WorldObject worldObj)
                ownerLabel = worldObj.LabelCap.StripTags();

            string announcement = label;

            if (!string.IsNullOrEmpty(ownerLabel))
                announcement += $" ({ownerLabel})";

            if (!string.IsNullOrEmpty(hotkey))
                announcement += $" ({hotkey})";

            if (gizmo is Command_Toggle toggle)
            {
                bool isOn = toggle.isActive?.Invoke() ?? false;
                announcement += isOn ? ": ON" : ": OFF";
            }

            if (!string.IsNullOrEmpty(statusValue))
                announcement += $": {statusValue}";

            bool isAbility = gizmo is Command_Ability;

            if (!string.IsNullOrEmpty(description) && !isAbility)
            {
                string descSep = (gizmo is Command_Toggle || !string.IsNullOrEmpty(statusValue))
                    ? (announcement.EndsWith(".") ? " " : ". ")
                    : ": ";
                announcement += descSep + description;
            }

            if (gizmo is Command_Target cmdTargetGizmo
                && cmdTargetGizmo.icon == Pawn_TrainingTracker.AttackTargetTexture)
            {
                float attackRange = Pawn_TrainingTracker.AttackTargetRange;
                string sep = announcement.EndsWith(".") ? " " : ". ";
                announcement += $"{sep}Range: {attackRange:F0} tiles from master";
            }

            if (isAbility && gizmo is Command_Ability commandAbility && commandAbility.Ability != null)
            {
                string costInfo = GetAbilityCostInfo(commandAbility.Ability);
                if (!string.IsNullOrEmpty(costInfo))
                    announcement += (announcement.EndsWith(".") ? " " : ". ") + costInfo;

                string rangeInfo = GetAbilityRangeInfo(commandAbility.Ability);
                if (!string.IsNullOrEmpty(rangeInfo))
                    announcement += (announcement.EndsWith(".") ? " " : ". ") + rangeInfo;

                string cooldownInfo = GetAbilityCooldownInfo(commandAbility.Ability);
                if (!string.IsNullOrEmpty(cooldownInfo))
                    announcement += (announcement.EndsWith(".") ? " " : ". ") + cooldownInfo;

                if (!string.IsNullOrEmpty(description))
                    announcement += (announcement.EndsWith(".") ? " " : ". ") + description;
            }

            if (gizmo.Disabled)
            {
                string reason = gizmo.disabledReason;
                if (string.IsNullOrEmpty(reason))
                    reason = "Not available";
                announcement += $" Disabled: {reason}";

                string context = GetDisabledGizmoContext(gizmo, owner);
                if (!string.IsNullOrEmpty(context))
                    announcement += $". {context}";
            }

            return announcement;
        }

        /// <summary>
        /// Collects all visible gizmos whose hotkey.MainKey matches the given key,
        /// drawn from both the current selection and the objects at the cursor tile.
        /// Uses the same temporary-selection pattern as OpenAtCursor for the cursor tile
        /// so lazy gizmos (e.g. Designator_Install) become visible.
        /// </summary>
        private static List<(Gizmo gizmo, ISelectable owner)> CollectHotkeyCandidates(KeyCode key)
        {
            var results = new List<(Gizmo, ISelectable)>();
            var seenGizmos = new HashSet<Gizmo>();
            var ownersAlreadyProcessed = new HashSet<ISelectable>();

            // 1. Current selection — no need to mutate; these objects' gizmos are live.
            foreach (object obj in Find.Selector.SelectedObjects)
            {
                if (!(obj is ISelectable selectable))
                    continue;
                ownersAlreadyProcessed.Add(selectable);
                foreach (var gizmo in selectable.GetGizmos())
                {
                    if (gizmo == null || !gizmo.Visible || ShouldSkipGizmo(gizmo))
                        continue;
                    if (!MatchesHotkey(gizmo, key))
                        continue;
                    if (seenGizmos.Add(gizmo))
                        results.Add((gizmo, selectable));
                }

                // Thing.GetGizmos() does not include reverse designators (Cancel,
                // Deconstruct, Uninstall, etc.) — vanilla's InspectGizmoGrid combines
                // them at render time. Mirror that here so hotkeys keep working after
                // a selection lands on the Thing (e.g. Shift+X → Deconstruct selects
                // the torch, then Shift+C needs Cancel from the reverse database).
                if (selectable is Thing selectedThing)
                {
                    List<Designator> selectedReverseDesignators = Find.ReverseDesignatorDatabase.AllDesignators;
                    for (int i = 0; i < selectedReverseDesignators.Count; i++)
                    {
                        Command_Action reverseGizmo = selectedReverseDesignators[i].CreateReverseDesignationGizmo(selectedThing);
                        if (reverseGizmo == null || ShouldSkipGizmo(reverseGizmo))
                            continue;
                        if (!MatchesHotkey(reverseGizmo, key))
                            continue;
                        if (seenGizmos.Add(reverseGizmo))
                            results.Add((reverseGizmo, selectable));
                    }
                }
            }

            // 2. Cursor tile — temp-select each thing so lazy gizmos appear, then restore.
            if (!MapNavigationState.IsInitialized)
                return results;

            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            Map map = Find.CurrentMap;
            if (!cursor.IsValid || !cursor.InBounds(map))
                return results;

            var previousSelection = Find.Selector.SelectedObjects.ToList();
            try
            {
                var sortedThings = cursor.GetThingList(map)
                    .Where(t => !(t is Mote) && t.def.category != ThingCategory.Mote)
                    .OrderByDescending(t => (int)t.def.altitudeLayer)
                    .ToList();

                foreach (ISelectable selectable in sortedThings.OfType<ISelectable>())
                {
                    if (ownersAlreadyProcessed.Contains(selectable))
                        continue;

                    Find.Selector.ClearSelection();
                    Find.Selector.Select(selectable, playSound: false, forceDesignatorDeselect: false);

                    foreach (var gizmo in selectable.GetGizmos())
                    {
                        if (gizmo == null || !gizmo.Visible || ShouldSkipGizmo(gizmo))
                            continue;
                        if (!MatchesHotkey(gizmo, key))
                            continue;
                        if (seenGizmos.Add(gizmo))
                            results.Add((gizmo, selectable));
                    }

                    if (selectable is Thing thing)
                    {
                        List<Designator> reverseDesignators = Find.ReverseDesignatorDatabase.AllDesignators;
                        for (int i = 0; i < reverseDesignators.Count; i++)
                        {
                            Command_Action reverseGizmo = reverseDesignators[i].CreateReverseDesignationGizmo(thing);
                            if (reverseGizmo == null || ShouldSkipGizmo(reverseGizmo))
                                continue;
                            if (!MatchesHotkey(reverseGizmo, key))
                                continue;
                            if (seenGizmos.Add(reverseGizmo))
                                results.Add((reverseGizmo, selectable));
                        }
                    }
                }

                Zone zone = cursor.GetZone(map);
                if (zone != null && !ownersAlreadyProcessed.Contains(zone))
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(zone, playSound: false, forceDesignatorDeselect: false);

                    foreach (var gizmo in zone.GetGizmos())
                    {
                        if (gizmo == null || !gizmo.Visible || ShouldSkipGizmo(gizmo))
                            continue;
                        if (!MatchesHotkey(gizmo, key))
                            continue;
                        if (seenGizmos.Add(gizmo))
                            results.Add((gizmo, zone));
                    }
                }
            }
            finally
            {
                Find.Selector.ClearSelection();
                foreach (var obj in previousSelection.OfType<ISelectable>())
                {
                    Find.Selector.Select(obj, playSound: false, forceDesignatorDeselect: false);
                }
            }

            return results;
        }

        private static bool MatchesHotkey(Gizmo gizmo, KeyCode key)
        {
            if (!(gizmo is Command command))
                return false;
            if (command.hotKey == null)
                return false;
            if (GizmoHotkeyShiftPatch.IsShiftExempt(command.hotKey))
                return false;
            return command.hotKey.MainKey == key;
        }
    }
}
