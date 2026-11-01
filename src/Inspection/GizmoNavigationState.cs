using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Sound;
using RimWorld;
using RimWorld.Planet;
using RimWorldAccess.Shell;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>Gizmo (command button) navigation: the G-key menu's cursor over the commands available for the current selection or cursor tile.</summary>
    [StaticConstructorOnStartup]
    public static partial class GizmoNavigationState
    {
        private static bool isActive = false;
        private static int selectedGizmoIndex = 0;
        private static List<Gizmo> availableGizmos = new List<Gizmo>();
        private static Dictionary<Gizmo, ISelectable> gizmoOwners = new Dictionary<Gizmo, ISelectable>();
        private static Dictionary<Gizmo, List<Gizmo>> gizmoGroups = new Dictionary<Gizmo, List<Gizmo>>();

        /// <summary>
        /// The Designator each reverse-designation Command_Action was created from. Allow Tool's
        /// own Command-to-Designator dictionary cannot be used for this: its gizmo-grid
        /// transpiler clears it on every draw pass and refills it with vanilla's per-frame
        /// instances, never ours.
        /// </summary>
        private static readonly Dictionary<Gizmo, Designator> reverseGizmoSources =
            new Dictionary<Gizmo, Designator>();
        private static ISelectable lastAnnouncedOwner = null;
        private static bool pawnJustSelected = false;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private static bool isExecutingGizmo = false;
        private static bool menuInterceptedDuringExecution = false;

        /// <summary>Which opener built the current list, so a toggle can rebuild it in place.</summary>
        private enum MenuSource { None, Selection, CursorTile, World }
        private static MenuSource menuSource = MenuSource.None;
        private static IntVec3 cursorSourcePos;
        private static Map cursorSourceMap;

        /// <summary>True while a toggle rebuilds the list: openers stay silent and keep the cursor.</summary>
        private static bool rebuildingList = false;

        public static bool IsActive => isActive;

        /// <summary>True while a gizmo's own handler runs — DialogInterceptionPatch intercepts FloatMenus only then.</summary>
        public static bool IsExecutingGizmo => isExecutingGizmo;

        internal static void NoteMenuInterceptedDuringExecution()
        {
            menuInterceptedDuringExecution = true;
        }

        internal static bool LastExecutionOpenedMenu => menuInterceptedDuringExecution;

        /// <summary>Whether a pawn was just selected via the , or . keys; cleared when the map cursor moves.</summary>
        public static bool PawnJustSelected
        {
            get => pawnJustSelected;
            set => pawnJustSelected = value;
        }

        public static int SelectedGizmoIndex => selectedGizmoIndex;

        public static List<Gizmo> AvailableGizmos => availableGizmos;

        /// <summary>Opens the menu over the gizmos of every selected object.</summary>
        public static void Open()
        {
            if (Find.Selector == null || Find.CurrentMap == null)
                return;

            var allGizmos = new List<Gizmo>();
            var allOwners = new Dictionary<Gizmo, ISelectable>();
            lastAnnouncedOwner = null;
            // Cleared here, not beside the availableGizmos.Clear() below: that one runs after
            // CollectRawGizmos has already recorded this pass's sources.
            reverseGizmoSources.Clear();

            foreach (object obj in Find.Selector.SelectedObjects)
            {
                if (obj is ISelectable selectable)
                {
                    CollectRawGizmos(selectable, allGizmos, allOwners);
                }
            }

            // Only each group's representative, chosen the way vanilla chooses what to draw.
            availableGizmos.Clear();
            gizmoOwners.Clear();
            gizmoGroups.Clear();
            GroupIntoRepresentatives(allGizmos, allOwners, availableGizmos, gizmoOwners, gizmoGroups);

            if (availableGizmos.Count == 0)
            {
                if (!rebuildingList)
                    TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NoCommandsAvailable".Loc());
                return;
            }

            menuSource = MenuSource.Selection;
            selectedGizmoIndex = 0;
            isActive = true;
            typeahead.ClearSearch();
            if (!rebuildingList)
                AnnounceCurrentGizmo();
        }

        /// <summary>
        /// One selectable's raw gizmo contribution: its own visible, unfiltered commands plus —
        /// for a Thing — the reverse designators <c>Thing.GetGizmos()</c> omits but vanilla's
        /// InspectGizmoGrid combines in at render time (Cancel, Deconstruct, Uninstall, …).
        /// </summary>
        private static void CollectRawGizmos(ISelectable selectable, List<Gizmo> allGizmos,
            Dictionary<Gizmo, ISelectable> allOwners)
        {
            var gizmos = selectable.GetGizmos().ToList();
            foreach (var gizmo in gizmos.Where(g => g != null && g.Visible && !ShouldSkipGizmo(g)))
            {
                allGizmos.Add(gizmo);
                allOwners[gizmo] = selectable;
            }

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
                    reverseGizmoSources[reverseGizmo] = reverseDesignators[i];
                }
            }
        }

        /// <summary>
        /// Vanilla's own draw pipeline over a raw gizmo list: stable-sort by Order BEFORE
        /// grouping (so groups form and appear in vanilla's on-screen order), merge with
        /// GroupsWith/MergeWith, then keep each group's representative. Writes into caller-owned
        /// collections so a second consumer can pass locals without disturbing the menu.
        /// </summary>
        private static void GroupIntoRepresentatives(List<Gizmo> allGizmos,
            Dictionary<Gizmo, ISelectable> allOwners, List<Gizmo> representativesOut,
            Dictionary<Gizmo, ISelectable> ownersOut, Dictionary<Gizmo, List<Gizmo>> groupsOut)
        {
            allGizmos = allGizmos.OrderBy(g => g.Order).ToList();

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

            foreach (var group in groups)
            {
                var representative = SelectGroupRepresentative(group);
                if (representative == null)
                    continue;

                // Vanilla folds a group of ability commands into the drawn one to show the
                // group's shared state (remaining charges).
                if (representative is Command_Ability commandAbility)
                    commandAbility.GroupAbilityCommands(group);

                representativesOut.Add(representative);
                if (groupsOut != null)
                    groupsOut[representative] = group;

                if (ownersOut != null && allOwners.TryGetValue(representative, out var owner))
                {
                    ownersOut[representative] = owner;
                }
            }
        }

        // The tree seams below let the inspect tree's "Gizmos" node present the same commands as
        // the G menu, for an object that may not be the current selection. All are read-or-act-only
        // with respect to this class's menu state: none opens the menu, moves its cursor, or
        // advances its owner-dedup.

        /// <summary>
        /// The G menu's collection pipeline applied to ONE selectable, into caller-owned
        /// collections. Returns the group representatives in vanilla's draw order; the optional
        /// out-dictionaries receive the owner and group mappings the activation seam needs.
        /// Runs inside <see cref="WithSelectableSelected"/> because many gizmos lazy-evaluate
        /// Visible/Label/Disabled against the live selection.
        /// </summary>
        public static List<Gizmo> CollectGizmosFor(ISelectable owner,
            Dictionary<Gizmo, ISelectable> ownersOut = null,
            Dictionary<Gizmo, List<Gizmo>> groupsOut = null)
        {
            var representatives = new List<Gizmo>();
            if (owner == null)
                return representatives;

            WithSelectableSelected(owner, () =>
            {
                var allGizmos = new List<Gizmo>();
                var allOwners = new Dictionary<Gizmo, ISelectable>();
                CollectPresentedGizmos(owner, allGizmos, allOwners);
                GroupIntoRepresentatives(allGizmos, allOwners, representatives, ownersOut, groupsOut);
                return true;
            });

            return representatives;
        }

        /// <summary>
        /// What the G menu PRESENTS for a selectable: <see cref="CollectRawGizmos"/> except for
        /// plans, whose vanilla gizmos are a mouse-only color grid and copy tool and are replaced
        /// by the mod's own keyboard-usable plan commands.
        /// </summary>
        private static void CollectPresentedGizmos(ISelectable selectable, List<Gizmo> allGizmos,
            Dictionary<Gizmo, ISelectable> allOwners)
        {
            if (selectable is Plan plan)
            {
                foreach (Gizmo gizmo in PlanActionHelper.BuildGizmos(plan))
                {
                    allGizmos.Add(gizmo);
                    allOwners[gizmo] = plan;
                }
                return;
            }

            CollectRawGizmos(selectable, allGizmos, allOwners);
        }

        /// <summary>
        /// <see cref="DescribeGizmo"/> with the owner-selection wrap its contract requires. Pure
        /// with respect to the menu: no owner prefix, no position — the tree supplies its own.
        /// </summary>
        public static ElementDescription DescribeGizmoRow(Gizmo gizmo, ISelectable owner)
        {
            if (gizmo == null)
                return new ElementDescription();
            return WithGizmoOwnerSelected(gizmo, owner, () => DescribeGizmo(gizmo, owner));
        }

        /// <summary>
        /// Enter on a tree gizmo row, through the same path the G menu's Enter uses so the
        /// disabled refusal, handler registry and epilogue stay one implementation. The gizmo
        /// speaks for itself, so the row owns its own announcement.
        /// </summary>
        public static void ActivateGizmoFromTree(Gizmo gizmo, ISelectable owner, List<Gizmo> group)
        {
            if (gizmo == null)
                return;
            ActivateSingleGizmo(gizmo, owner, group);
        }

        /// <summary>
        /// Left/Right on a tree gizmo row carrying an adjustable slider. Returns false when the
        /// gizmo has no slider adapter, so the tree falls through to its own expand/collapse.
        /// </summary>
        public static bool AdjustGizmoSliderFromTree(Gizmo gizmo, ISelectable owner, int direction, bool bigStep)
        {
            if (gizmo == null)
                return false;

            return WithSelectableSelected(owner, () =>
            {
                if (!GizmoHandlerRegistry.TryResolveSliderAdapter(gizmo, out GizmoSliderAdapter adapter))
                    return false;

                AdjustSliderValue(adapter, direction, bigStep ? 5 : 1);
                return true;
            });
        }

        /// <summary>
        /// Picks the gizmo vanilla would draw for a merged group: the first non-disabled member
        /// (first member if all are disabled), then the toggle-ambiguity correction — a
        /// Command_Toggle disagreeing with activateIfAmbiguous yields to a non-disabled sibling in
        /// the opposite state, so the spoken state matches the checkbox a sighted player sees.
        /// </summary>
        private static Gizmo SelectGroupRepresentative(List<Gizmo> group)
        {
            Gizmo representative = null;
            for (int i = 0; i < group.Count; i++)
            {
                if (!group[i].Disabled)
                {
                    representative = group[i];
                    break;
                }
            }

            if (representative == null)
                return group.FirstOrDefault();

            if (representative is Command_Toggle toggle)
            {
                if (!toggle.activateIfAmbiguous && !toggle.isActive())
                {
                    for (int i = 0; i < group.Count; i++)
                    {
                        if (group[i] is Command_Toggle candidate && !candidate.Disabled && candidate.isActive())
                        {
                            representative = group[i];
                            break;
                        }
                    }
                }
                if (toggle.activateIfAmbiguous && toggle.isActive())
                {
                    for (int i = 0; i < group.Count; i++)
                    {
                        if (group[i] is Command_Toggle candidate && !candidate.Disabled && !candidate.isActive())
                        {
                            representative = group[i];
                            break;
                        }
                    }
                }
            }

            return representative;
        }

        /// <summary>Opens the menu over the gizmos of every object at the cursor tile.</summary>
        public static void OpenAtCursor(IntVec3 cursorPosition, Map map)
        {
            if (map == null)
                return;

            if (!cursorPosition.IsValid || !cursorPosition.InBounds(map))
            {
                TolkHelper.Speak("RimWorldAccess.Guard.InvalidCursorPosition".Loc());
                return;
            }

            availableGizmos.Clear();
            gizmoOwners.Clear();
            reverseGizmoSources.Clear();

            var previousSelection = Find.Selector.SelectedObjects.ToList();

            try
            {
                // Under fog only vanilla's click gate decides, so unseen ground reveals nothing.
                bool cursorFogged = cursorPosition.Fogged(map);

                // Highest altitude layer first, matching TileInfoHelper's ordering.
                var sortedThings = cursorPosition.GetThingList(map)
                    .Where(t => !(t is Mote) && t.def.category != ThingCategory.Mote)
                    .Where(t => !HiddenPawns.IsHidden(t))
                    .Where(t => !cursorFogged || ThingSelectionUtility.SelectableByMapClick(t))
                    .OrderByDescending(t => (int)t.def.altitudeLayer)
                    .ToList();

                // Each thing must be selected before its gizmos are read: some (Designator_Install)
                // decide Visible from whether the thing is selected.
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

                    // Reverse designators, mirroring GizmoGridDrawer.DrawGizmoGridFor.
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
                                reverseGizmoSources[reverseGizmo] = reverseDesignators[i];
                            }
                        }
                    }
                }

                // Zones come after things, matching TileInfoHelper's ordering.
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

                // A plan marker gets the mod's own plan commands: vanilla's change-color grid and
                // copy tool are unusable by keyboard.
                Plan plan = cursorPosition.GetPlan(map);
                if (plan != null)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(plan, playSound: false, forceDesignatorDeselect: false);

                    foreach (Gizmo gizmo in PlanActionHelper.BuildGizmos(plan))
                    {
                        availableGizmos.Add(gizmo);
                        gizmoOwners[gizmo] = plan;
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

            if (availableGizmos.Count == 0)
            {
                if (!rebuildingList)
                    TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NoCommandsAtCursor".Loc());
                return;
            }

            menuSource = MenuSource.CursorTile;
            cursorSourcePos = cursorPosition;
            cursorSourceMap = map;
            selectedGizmoIndex = 0;
            isActive = true;
            typeahead.ClearSearch();
            lastAnnouncedOwner = null;
            if (!rebuildingList)
                AnnounceCurrentGizmo();
        }

        /// <summary>Opens the menu over the selected world objects' gizmos (G on the world map).</summary>
        public static void OpenFromWorldObjects()
        {
            List<WorldObject> objectsToUse = new List<WorldObject>();
            int cursorTile = -1;

            // World objects at the navigation cursor take priority over the game's selection.
            if (WorldNavigationState.IsActive && WorldNavigationState.CurrentSelectedTile.Valid)
            {
                cursorTile = WorldNavigationState.CurrentSelectedTile;
                var objectsAtTile = Find.WorldObjects?.ObjectsAt(cursorTile);
                if (objectsAtTile != null)
                {
                    objectsToUse.AddRange(objectsAtTile);
                }
            }

            if (objectsToUse.Count == 0 && Find.WorldSelector != null)
            {
                var selectedObjects = Find.WorldSelector.SelectedObjects;
                if (selectedObjects != null)
                {
                    objectsToUse.AddRange(selectedObjects);
                }
            }

            // Do NOT bail on an empty object list: Form/Send caravan are global, owner-less
            // commands a sighted player sees regardless of selection, and TryAddGlobalWorldGizmos
            // below can still surface them. The empty-list check at the end covers the real case.

            // Multi-selection counts only when every selected caravan is on the cursor tile, so
            // merge is offered together but single-caravan gizmos show when they are apart.
            var multiSelected = WorldNavigationState.GetMultiSelectedCaravans();
            bool allOnSameTile = multiSelected.Count > 1 &&
                multiSelected.All(c => c != null && !c.Destroyed && c.Tile == cursorTile);

            if (allOnSameTile && Find.WorldSelector != null)
            {
                Find.WorldSelector.ClearSelection();
                foreach (var caravan in multiSelected)
                {
                    Find.WorldSelector.Select(caravan, playSound: false);
                }

                // Only the multi-selected caravans contribute gizmos: otherwise the merge command
                // binds to an unselected caravan and merges everything on the tile.
                objectsToUse.Clear();
                objectsToUse.AddRange(multiSelected.Cast<WorldObject>());
            }

            availableGizmos.Clear();
            gizmoOwners.Clear();
            reverseGizmoSources.Clear();

            // Multi-selected caravans stay selected so "Merge Selected Caravans" works.
            bool hasMultiSelection = allOnSameTile;

            foreach (WorldObject worldObj in objectsToUse)
            {
                if (worldObj == null)
                    continue;

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
                        // Display-only, no click action.
                        if (gizmo is Gizmo_CaravanInfo)
                            continue;

                        // Only commands acting on ALL selected objects at once dedupe; per-object
                        // ones (Settle, Split) show per caravan even with identical labels. Merge is
                        // identified by its icon — CaravanMergeUtility caches one Texture2D
                        // instance, so reference identity is a language-free signal.
                        bool isMergeCommand = IsMergeCaravansCommand(gizmo);

                        bool isDuplicate = false;
                        if (isMergeCommand)
                        {
                            isDuplicate = availableGizmos.Any(IsMergeCaravansCommand);
                        }

                        if (!isDuplicate)
                        {
                            availableGizmos.Add(gizmo);
                            gizmoOwners[gizmo] = worldObj;
                        }
                    }
                }
            }

            // Skipped under a multi-caravan merge selection: the caravan command suppresses itself
            // there anyway, and that selection must not be disturbed.
            if (!hasMultiSelection)
            {
                TryAddGlobalWorldGizmos();
            }

            availableGizmos = availableGizmos
                .OrderBy(g => g.Order)
                .ToList();

            if (availableGizmos.Count == 0)
            {
                if (rebuildingList)
                    return;
                if (objectsToUse.Count == 0)
                {
                    TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NoWorldObject".Loc());
                }
                else
                {
                    string objName = objectsToUse.FirstOrDefault()?.LabelCap
                        ?? "RimWorldAccess.Inspection.Gizmo.WorldObjectFallbackName".Translate().ToString();
                    TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NoCommandsForObject".Loc(objName));
                }
                return;
            }

            menuSource = MenuSource.World;
            selectedGizmoIndex = 0;
            isActive = true;
            typeahead.ClearSearch();
            lastAnnouncedOwner = null;
            if (!rebuildingList)
                AnnounceCurrentGizmo();
        }

        /// <summary>
        /// The owner-less world-view gizmos vanilla draws in the world command bar
        /// (<see cref="WorldGizmoUtility.WorldUIOnGUI"/>): the context caravan command, world-grid
        /// commands, and the cursor tile's own gizmos. Having no owning <see cref="WorldObject"/>,
        /// they are appended directly and execute via their own action delegate.
        /// </summary>
        private static void TryAddGlobalWorldGizmos()
        {
            PlanetTile tile = (WorldNavigationState.IsActive && WorldNavigationState.CurrentSelectedTile.Valid)
                ? WorldNavigationState.CurrentSelectedTile
                : (Find.WorldSelector?.SelectedTile ?? PlanetTile.Invalid);

            // Sync the game's selected tile to the cursor so the context caravan command is
            // computed for where the cursor is (Form vs Send depends on the tile/selection).
            // MUTATION-C: mirrors WorldSelector.SelectedTile's bare setter (RimWorld.Planet/WorldSelector.cs:36-50)
            // and WorldSelector.SelectUnderMouse's bare selectedTile write (WorldSelector.cs:391) — vanilla
            // writes this field unconditionally from a world-map click; we sync it here so gizmo computation
            // (Form/Send caravan) sees the keyboard cursor's tile.
            if (Find.WorldSelector != null && tile.Valid)
            {
                Find.WorldSelector.SelectedTile = tile;
            }

            if (WorldGizmoUtility.TryGetCaravanGizmo(out Gizmo caravanGizmo))
            {
                AddGlobalWorldGizmoIfNew(caravanGizmo);
            }

            if (Find.WorldGrid != null)
            {
                foreach (Gizmo gizmo in Find.WorldGrid.GetGizmos())
                {
                    AddGlobalWorldGizmoIfNew(gizmo);
                }
            }

            // Planet-layer / landmark actions owned by the cursor tile itself.
            if (tile.Valid)
            {
                foreach (Gizmo gizmo in tile.Tile.GetGizmos())
                {
                    AddGlobalWorldGizmoIfNew(gizmo);
                }
            }
        }

        /// <summary>
        /// Adds an owner-less world gizmo if visible, unfiltered and not already present (deduped
        /// by label). No <see cref="gizmoOwners"/> entry: these carry their own action.
        /// </summary>
        private static void AddGlobalWorldGizmoIfNew(Gizmo gizmo)
        {
            if (gizmo == null || !gizmo.Visible || ShouldSkipGizmo(gizmo))
                return;

            string label = (gizmo as Command)?.Label ?? gizmo.GetType().Name;
            bool isDuplicate = availableGizmos.Any(g =>
                ((g as Command)?.Label ?? g.GetType().Name) == label);

            if (!isDuplicate)
                availableGizmos.Add(gizmo);
        }

        /// <summary>Closes the menu.</summary>
        public static void Close()
        {
            isActive = false;
            selectedGizmoIndex = 0;
            availableGizmos.Clear();
            gizmoOwners.Clear();
            gizmoGroups.Clear();
            reverseGizmoSources.Clear();
            typeahead.ClearSearch();
            lastAnnouncedOwner = null;
            menuSource = MenuSource.None;
            cursorSourceMap = null;
        }

        /// <summary>True when the open menu was built by a re-runnable opener, so a toggle keeps it open.</summary>
        internal static bool MenuSupportsInPlaceRefresh => isActive && menuSource != MenuSource.None;

        /// <summary>
        /// Rebuilds the list through the opener that created it (a toggle can add or remove rows)
        /// and re-lands the cursor on the acted-on gizmo, matched by group key then label since
        /// openers hand out fresh instances. An empty rebuild closes the menu.
        /// </summary>
        internal static void RefreshListKeepingCursor(Gizmo anchor)
        {
            if (!MenuSupportsInPlaceRefresh)
                return;

            int oldIndex = selectedGizmoIndex;
            int anchorGroupKey = (anchor as Command)?.groupKey ?? 0;
            string anchorLabel = (anchor as Command)?.Label;
            ISelectable announcedOwner = lastAnnouncedOwner;
            MenuSource source = menuSource;

            rebuildingList = true;
            try
            {
                switch (source)
                {
                    case MenuSource.Selection:
                        Open();
                        break;
                    case MenuSource.CursorTile:
                        OpenAtCursor(cursorSourcePos, cursorSourceMap);
                        break;
                    case MenuSource.World:
                        OpenFromWorldObjects();
                        break;
                }
            }
            finally
            {
                rebuildingList = false;
            }

            if (availableGizmos.Count == 0)
            {
                Close();
                return;
            }

            lastAnnouncedOwner = announcedOwner;
            int landing = -1;
            for (int i = 0; i < availableGizmos.Count && landing < 0; i++)
            {
                if (ReferenceEquals(availableGizmos[i], anchor))
                    landing = i;
            }
            if (landing < 0 && anchorGroupKey != 0)
            {
                for (int i = 0; i < availableGizmos.Count && landing < 0; i++)
                {
                    if ((availableGizmos[i] as Command)?.groupKey == anchorGroupKey)
                        landing = i;
                }
            }
            if (landing < 0 && !string.IsNullOrEmpty(anchorLabel))
            {
                for (int i = 0; i < availableGizmos.Count && landing < 0; i++)
                {
                    if ((availableGizmos[i] as Command)?.Label == anchorLabel)
                        landing = i;
                }
            }
            selectedGizmoIndex = landing >= 0
                ? landing
                : Mathf.Clamp(oldIndex, 0, availableGizmos.Count - 1);
        }

        /// <summary>
        /// Clears state at a game session boundary (new game, save load, main menu). Silent,
        /// unlike a player-driven <see cref="Close"/>.
        /// </summary>
        public static void Reset()
        {
            Close();
        }

        /// <summary>
        /// Propagates an execution to the rest of the gizmo's group, as GizmoGridDrawer does.
        /// Runs BEFORE the selected gizmo's own ProcessInput.
        /// </summary>
        internal static void PropagateToGroupedGizmos(Gizmo selectedGizmo, Event fakeEvent)
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
        /// Calls ProcessGroupInput with the full group list, as GizmoGridDrawer does. Runs AFTER
        /// the selected gizmo's own ProcessInput.
        /// </summary>
        internal static void ProcessGroupInput(Gizmo selectedGizmo, Event fakeEvent)
        {
            if (gizmoGroups.TryGetValue(selectedGizmo, out var group))
            {
                selectedGizmo.ProcessGroupInput(fakeEvent, group);
            }
        }

        public static void SelectNext()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            int before = selectedGizmoIndex;
            selectedGizmoIndex = MenuHelper.SelectNext(selectedGizmoIndex, availableGizmos.Count, out bool wrapped);
            if (wrapped)
                MenuHelper.PlayWrapTone();
            if (selectedGizmoIndex == before)
            {
                MenuHelper.PlayEdgeTone();
                return;
            }
            AnnounceCurrentGizmo();
        }

        public static void SelectPrevious()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            int before = selectedGizmoIndex;
            selectedGizmoIndex = MenuHelper.SelectPrevious(selectedGizmoIndex, availableGizmos.Count, out bool wrapped);
            if (wrapped)
                MenuHelper.PlayWrapTone();
            if (selectedGizmoIndex == before)
            {
                MenuHelper.PlayEdgeTone();
                return;
            }
            AnnounceCurrentGizmo();
        }

        /// <summary>Executes the gizmo under the cursor, then closes the menu.</summary>
        public static void ExecuteSelected()
        {
            ExecuteSelected(0);
        }

        /// <param name="mouseButton">1 replays vanilla's right-click on a gizmo with no float menu options.</param>
        private static void ExecuteSelected(int mouseButton)
        {
            menuInterceptedDuringExecution = false;
            if (!isActive || availableGizmos.Count == 0)
                return;

            if (selectedGizmoIndex < 0 || selectedGizmoIndex >= availableGizmos.Count)
                return;

            Gizmo selectedGizmo = availableGizmos[selectedGizmoIndex];

            // A world "View layer" gizmo's action only sets PlanetLayer.Selected, so the switch is
            // detected afterwards and followed like the Tab cycle; otherwise it appears inert.
            PlanetLayer layerBeforeExecute = PlanetLayer.Selected;

            // Resolve lazy properties (Label, Disabled, disabledReason) with the owner
            // selected so they don't read stale Find.Selector state.
            string gizmoLabel = WithGizmoOwnerSelected(selectedGizmo, null, () => GetGizmoLabel(selectedGizmo));
            bool gizmoDisabled = WithGizmoOwnerSelected(selectedGizmo, null, () => selectedGizmo.Disabled);

            if (gizmoDisabled)
            {
                string reason = WithGizmoOwnerSelected(selectedGizmo, null, () => selectedGizmo.disabledReason);
                if (string.IsNullOrEmpty(reason))
                    reason = "RimWorldAccess.Inspection.Gizmo.DisabledExecuteFallback".Translate();

                string announcement = "RimWorldAccess.Inspection.Gizmo.DisabledSuffix".Translate(reason);

                ISelectable gizmoOwner = null;
                if (gizmoOwners.Count > 0)
                    gizmoOwners.TryGetValue(selectedGizmo, out gizmoOwner);
                string context = GetDisabledGizmoContext(selectedGizmo, gizmoOwner);
                if (!string.IsNullOrEmpty(context))
                    announcement += $". {context}";

                TolkHelper.SpeakData(announcement);
                return;
            }

            Event fakeEvent = new Event();
            fakeEvent.type = EventType.Used;
            fakeEvent.button = mouseButton;

            // Lets DialogInterceptionPatch intercept any FloatMenu the gizmo opens.
            isExecutingGizmo = true;

            try
            {
                // First statement inside the try: Begin() runs a reflected getter on an
                // arbitrary modded Designator, and a throw before the try would leak
                // isExecutingGizmo past the finally.
                ReverseDesignationOutcome designationOutcome = ReverseDesignationOutcome.Begin(selectedGizmo, gizmoLabel);

                // Unconditional, independent of PawnJustSelected/multi-select: the owner-sync
                // variable below is conditional and must not be reused for this.
                gizmoOwners.TryGetValue(selectedGizmo, out ISelectable resolvedOwner);
                var handlerContext = new GizmoHandlerContext(resolvedOwner, gizmoLabel, fakeEvent, PawnJustSelected);

                // Designators enter placement mode and are handled here rather than through
                // GizmoHandlerRegistry: this branch must run BEFORE the owner-selection preamble
                // below, whose multi-select gate its own owner-sync deliberately omits.
                if (selectedGizmo is Designator designator)
                {
                    DesignatorGizmoHandler.Execute(designator, handlerContext);
                    return;
                }

                // Non-Designator gizmos need their owner selected, because some actions read
                // Find.Selector/Find.WorldSelector. Skipped under multi-select, where the selection
                // is already correct and must not be cleared.
                if (!PawnJustSelected && !MultiSelectState.IsMultiSelectMode && gizmoOwners.ContainsKey(selectedGizmo))
                {
                    ISelectable owner = gizmoOwners[selectedGizmo];
                    if (owner is WorldObject worldObj && Find.WorldSelector != null)
                    {
                        Find.WorldSelector.ClearSelection();

                        var multiSelected = WorldNavigationState.GetMultiSelectedCaravans();
                        if (multiSelected.Count > 0 && owner is Caravan ownerCaravan)
                        {
                            // The owner caravan MUST be selected first: the merge command checks
                            // FirstSelectedObject against the caravan that generated the gizmo, and
                            // FirstSelectedObject is whichever was added to the selection first.
                            Find.WorldSelector.Select(ownerCaravan, playSound: false);

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

                // Every other gizmo kind resolves through GizmoHandlerRegistry's ordered tiers.
                bool runEpilogue = GizmoHandlerRegistry.Execute(selectedGizmo, handlerContext);

                if (runEpilogue)
                {
                    // Merge/split rewrite the world object list, so the multi-selection is stale.
                    WorldNavigationState.ClearMultiSelection();

                    bool layerChanged = PlanetLayer.Selected != layerBeforeExecute;

                    Close();

                    // After Close(), so world navigation is active and this lands as an announcement.
                    if (layerChanged)
                        WorldNavigationState.OnSelectedLayerChanged();

                    // Last utterance of the action: what a reverse-designation gizmo actually did.
                    designationOutcome.Announce();
                }
            }
            finally
            {
                isExecutingGizmo = false;
                if (menuInterceptedDuringExecution)
                    WindowlessFloatMenuState.AnnounceOpened();
            }
        }
    }
}
