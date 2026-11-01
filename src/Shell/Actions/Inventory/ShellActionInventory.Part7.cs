using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Action inventory, part 7 — in-game side doors: Work, the Building Inspect components,
    /// Architect, world/map navigation, Targeting, the Anomaly entity tab, and the generic
    /// window/table screens.
    /// Charter boundary: an action belongs here when its owning screen is reached from THE MAP OR
    /// WORLD VIEW during ordinary play; pre-game and ideology-building flows live in Part8.
    /// Each header names the Harmony patch that owns those keys.
    /// </summary>
    internal static class ShellActionInventoryPart7
    {
        internal static void Register(ActionCatalog c)
        {
            // ---- Side door: WorkMenuPatch, focused view (src/Work/WorkMenuPatch.cs:16, UIRootOnGUI) ----
            c.Register(InputAction.ForScreen("work", "work.toggleMode",
                new List<KeyChord> { KeyChord.Of(KeyCode.M, alt: true) }));
            c.Register(InputAction.ForScreen("work", "work.swapToTableView",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab, ctrl: true), KeyChord.Of(KeyCode.Tab, ctrl: true, shift: true) }));
            // Each colonist is itself the tab in the focused view's 2D grid, so Tab cycles colonists:
            // the scope turns region cycling off and Up/Down step priority levels instead.
            c.Register(InputAction.ForScreen("work", "work.nextPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) }));
            c.Register(InputAction.ForScreen("work", "work.previousPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab, shift: true) }));
            c.Register(InputAction.ForScreen("work", "work.cyclePriorityDown",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftBracket) }));
            c.Register(InputAction.ForScreen("work", "work.cyclePriorityUp",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            c.Register(InputAction.ForScreen("work", "work.cyclePriorityDownAll",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftBracket, shift: true) }));
            c.Register(InputAction.ForScreen("work", "work.cyclePriorityUpAll",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket, shift: true) }));
            c.Register(InputAction.ForScreen("work", "work.setPriority0",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha0), KeyChord.Of(KeyCode.Keypad0) }));
            c.Register(InputAction.ForScreen("work", "work.setPriority1",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha1), KeyChord.Of(KeyCode.Keypad1) }));
            c.Register(InputAction.ForScreen("work", "work.setPriority2",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha2), KeyChord.Of(KeyCode.Keypad2) }));
            c.Register(InputAction.ForScreen("work", "work.setPriority3",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha3), KeyChord.Of(KeyCode.Keypad3) }));
            c.Register(InputAction.ForScreen("work", "work.setPriority4",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha4), KeyChord.Of(KeyCode.Keypad4) }));
            c.Register(InputAction.ForScreen("work", "work.setPriorityAll0",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha0, shift: true), KeyChord.Of(KeyCode.Keypad0, shift: true) }));
            c.Register(InputAction.ForScreen("work", "work.setPriorityAll1",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha1, shift: true), KeyChord.Of(KeyCode.Keypad1, shift: true) }));
            c.Register(InputAction.ForScreen("work", "work.setPriorityAll2",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha2, shift: true), KeyChord.Of(KeyCode.Keypad2, shift: true) }));
            c.Register(InputAction.ForScreen("work", "work.setPriorityAll3",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha3, shift: true), KeyChord.Of(KeyCode.Keypad3, shift: true) }));
            c.Register(InputAction.ForScreen("work", "work.setPriorityAll4",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha4, shift: true), KeyChord.Of(KeyCode.Keypad4, shift: true) }));
            c.Register(InputAction.ForScreen("work", "work.copyPriorities",
                new List<KeyChord> { KeyChord.Of(KeyCode.C, ctrl: true) }));
            c.Register(InputAction.ForScreen("work", "work.pastePriorities",
                new List<KeyChord> { KeyChord.Of(KeyCode.V, ctrl: true) }));
            c.Register(InputAction.ForScreen("work", "work.toggleSelected",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) })); // DORMANT — Space reaches the row through ScreenScope's activation alias, which is this toggle

            // ---- Side door: WorkTableMenuInputPatch, table view (src/Work/WorkMenuPatch.cs:482, UIRootOnGUI) ----
            c.Register(InputAction.ForScreen("workTable", "workTable.swapToFocusedView",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab, ctrl: true), KeyChord.Of(KeyCode.Tab, ctrl: true, shift: true) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.toggleMode",
                new List<KeyChord> { KeyChord.Of(KeyCode.M, alt: true) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.paintColumn",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true, shift: true), KeyChord.Of(KeyCode.End, ctrl: true, shift: true) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.paintToStart",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, shift: true) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.paintToEnd",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, shift: true) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.paintDown",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.paintUp",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.cyclePriorityDown",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftBracket) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.cyclePriorityUp",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.cyclePriorityDownAll",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftBracket, shift: true) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.cyclePriorityUpAll",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket, shift: true) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.setPriority0",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha0), KeyChord.Of(KeyCode.Keypad0) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.setPriority1",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha1), KeyChord.Of(KeyCode.Keypad1) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.setPriority2",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha2), KeyChord.Of(KeyCode.Keypad2) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.setPriority3",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha3), KeyChord.Of(KeyCode.Keypad3) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.setPriority4",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha4), KeyChord.Of(KeyCode.Keypad4) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.setPriorityAll0",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha0, shift: true), KeyChord.Of(KeyCode.Keypad0, shift: true) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.setPriorityAll1",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha1, shift: true), KeyChord.Of(KeyCode.Keypad1, shift: true) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.setPriorityAll2",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha2, shift: true), KeyChord.Of(KeyCode.Keypad2, shift: true) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.setPriorityAll3",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha3, shift: true), KeyChord.Of(KeyCode.Keypad3, shift: true) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.setPriorityAll4",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha4, shift: true), KeyChord.Of(KeyCode.Keypad4, shift: true) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.toggleCell",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) })); // basic mode only
            c.Register(InputAction.ForScreen("workTable", "workTable.copyPriorities",
                new List<KeyChord> { KeyChord.Of(KeyCode.C, ctrl: true) }));
            c.Register(InputAction.ForScreen("workTable", "workTable.pastePriorities",
                new List<KeyChord> { KeyChord.Of(KeyCode.V, ctrl: true) }));

            // ---- Side door: ArchitectMenuPatch (src/Building/ArchitectMenuPatch.cs:17, UIRootOnGUI) ----
            c.Register(new InputAction(
                "map.architect.toggle",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) })); // open menu / cancel placement / close, context-dependent
            c.Register(InputAction.ForScreen("architectTree", "architectTree.designatorOptions",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            c.Register(InputAction.ForScreen("architectTree", "architectTree.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            // ---- CANONICAL HOME of the shared tree.*/filterTree.* ids ----
            // Action ids are globally unique in ActionCatalog (Register throws on a duplicate), so
            // this set is registered exactly ONCE, under the "tree"/"filterTree" pseudo-screen keys:
            // one rebindable set for every TreeRegionScope/FilterTreeScopeBase subclass. Dispatch
            // resolves claims by bare id, so a consumer scope's Claim finds these whichever screen is
            // live. Do NOT re-register these ids per screen.
            c.Register(InputAction.ForScreen("tree", "tree.jumpToFirstAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true) }));
            c.Register(InputAction.ForScreen("tree", "tree.jumpToLastAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, ctrl: true) }));
            c.Register(InputAction.ForScreen("tree", "tree.expandAllSiblings",
                new List<KeyChord> { KeyChord.Of(KeyCode.KeypadMultiply), KeyChord.Of(KeyCode.Alpha8, shift: true) })); // '*' WCAG tree pattern
            c.Register(InputAction.ForScreen("tree", "tree.jumpToPreviousSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) }));
            c.Register(InputAction.ForScreen("tree", "tree.jumpToNextSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));
            c.Register(InputAction.ForScreen("filterTree", "filterTree.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            // The range sub-editor (hit points / quality / mental-break chance) is ONE shared
            // component reused by StorageSettings, BillConfig, ThingFilterMenu and the policy window,
            // registered once under its own scope; its rows navigate and adjust on the base
            // ScreenScope grammar, so it needs no bespoke ids.
            c.Register(InputAction.ForScreen("plantSelection", "plantSelection.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            // PlaySettingsScope needs no ids: Up/Down/Enter/Escape are all generic Menus grammar.

            // ---- Side door: WorldNavigationPatch (src/World/WorldNavigationPatch.cs:16, WorldInterface.HandleLowPriorityInput) ----
            c.Register(new InputAction("world.cursor.north",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow) }));
            c.Register(new InputAction("world.cursor.south",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow) }));
            c.Register(new InputAction("world.cursor.west",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow) }));
            c.Register(new InputAction("world.cursor.east",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow) }));
            // Alt+Home / Alt+End / I are the Part 2 world.scanner.*/world.caravan.inspect functions
            // reached through a second entry point, not second functions.
            c.Register(new InputAction("world.formCaravan",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.C) }));
            c.Register(new InputAction("world.caravanOrders",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            // UNRESOLVED: this side door's scanner Page Up/Down/Home/End/Alt+J and tile-info 1-5
            // appear to duplicate the ambient world-scanner and tile-info actions registered
            // elsewhere.

            // ---- Side door: TargetingPatch (src/Combat/TargetingPatch.cs:18, Targeter.ProcessInputEvents) ----
            c.Register(InputAction.ForScreen("targeting", "targeting.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));

            // ---- BuildingInspectPatch, HandleTempControlInput -> TempControlScope,
            //      src/Shell/Screens/InspectComponentScopes.Game.cs; the whole
            //      patch file is DELETED ----
            // The small step is the stepper row's generic Left/Right adjust, so it needs no id here.
            // increaseLarge/decreaseLarge stay bespoke: the codebase has no shared "large step"
            // modifier convention.
            c.Register(InputAction.ForScreen("tempControl", "tempControl.increaseLarge",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow, shift: true) }));
            c.Register(InputAction.ForScreen("tempControl", "tempControl.decreaseLarge",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow, shift: true) }));
            c.Register(InputAction.ForScreen("tempControl", "tempControl.reset",
                new List<KeyChord> { KeyChord.Of(KeyCode.R) }));

            // ---- Side door: BuildingInspectPatch, HandleBillsMenuInput (src/Inspection/BuildingInspectPatch.cs:153) ----
            c.Register(InputAction.ForScreen("bills", "bills.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            c.Register(InputAction.ForScreen("bills", "bills.delete",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            c.Register(InputAction.ForScreen("bills", "bills.copy",
                new List<KeyChord> { KeyChord.Of(KeyCode.C, ctrl: true) }));

            // ---- Side door: BuildingInspectPatch, HandleBillConfigInput (src/Inspection/BuildingInspectPatch.cs:302) ----
            c.Register(InputAction.ForScreen("billConfig", "billConfig.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            c.Register(InputAction.ForScreen("billConfig", "billConfig.increaseBy10",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            c.Register(InputAction.ForScreen("billConfig", "billConfig.decreaseBy10",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("billConfig", "billConfig.increaseBy100",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, ctrl: true) }));
            c.Register(InputAction.ForScreen("billConfig", "billConfig.decreaseBy100",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, ctrl: true) }));
            c.Register(InputAction.ForScreen("billConfig", "billConfig.increaseBy1000",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, ctrl: true, shift: true) }));
            c.Register(InputAction.ForScreen("billConfig", "billConfig.decreaseBy1000",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, ctrl: true, shift: true) }));
            c.Register(InputAction.ForScreen("billConfig", "billConfig.jumpToMin",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, shift: true) }));
            c.Register(InputAction.ForScreen("billConfig", "billConfig.jumpToMax",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, shift: true) }));
            // No decrease/increase ids: Left/Right on a value row ride the base ScreenScope's
            // menus.previousHorizontal/nextHorizontal, and Enter on a countable field opens a real
            // TextFieldEditSession whose keys are the shared text-session vocabulary.

            // ---- ThingFilterMenuScope (src/Shell/Screens/ThingFilterMenuScope.Game.cs) ----
            // Registers nothing: it rides the shared tree.*/filterTree.* ids above.
            // ThingFilterMenuState is a SEPARATE implementation from the policy window's filter
            // panels, whose thingFilter.* ids live under their own screen id in Part4 — do not
            // conflate the two screen ids.

            // ---- DoorControlScope (src/Shell/Screens/InspectComponentScopes.Game.cs) ----
            // Hold Open carries no id of its own: a Space/Enter claim would collide with the
            // universal menus.activate claim on the screen's other row, so Enter toggles it only
            // while that row has focus, the checkbox-row convention every migrated screen uses.
            c.Register(InputAction.ForScreen("doorControl", "doorControl.detailedStatus",
                new List<KeyChord> { KeyChord.Of(KeyCode.D) }));

            // ---- ForbidControlScope (src/Shell/Screens/InspectComponentScopes.Game.cs) ----
            // Forbidden carries no toggle id, for the same collision reason as Hold Open above.
            c.Register(InputAction.ForScreen("forbidControl", "forbidControl.detailedStatus",
                new List<KeyChord> { KeyChord.Of(KeyCode.D) }));

            // ---- Side door: BuildingInspectPatch, HandleFishingZoneMenuInput (src/Inspection/BuildingInspectPatch.cs:909, Odyssey DLC) ----
            c.Register(InputAction.ForScreen("fishingZone", "fishingZone.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            c.Register(InputAction.ForScreen("fishingZone", "fishingZone.increaseBy10",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            c.Register(InputAction.ForScreen("fishingZone", "fishingZone.decreaseBy10",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("fishingZone", "fishingZone.increaseBy100",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, ctrl: true) }));
            c.Register(InputAction.ForScreen("fishingZone", "fishingZone.decreaseBy100",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, ctrl: true) }));
            c.Register(InputAction.ForScreen("fishingZone", "fishingZone.increaseBy1000",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, ctrl: true, shift: true) }));
            c.Register(InputAction.ForScreen("fishingZone", "fishingZone.decreaseBy1000",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, ctrl: true, shift: true) }));
            c.Register(InputAction.ForScreen("fishingZone", "fishingZone.jumpToMin",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, shift: true) }));
            c.Register(InputAction.ForScreen("fishingZone", "fishingZone.jumpToMax",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, shift: true) }));

            // ---- Side door: MapNavigationPatch (src/Map/MapNavigationPatch.cs:17, CameraDriver.Update) ----
            // Arrow-key map cursor movement is NOT here: it lives in MapArrowKeyHandler. This
            // patch's keys use Input.GetKey/GetKeyDown, since Update() is not an OnGUI callback and
            // Event.current is invalid there.
            c.Register(new InputAction("map.switchToNextMap",
                ActionCategory.Map, new List<KeyChord> { KeyChord.Of(KeyCode.Period, shift: true) }));
            c.Register(new InputAction("map.switchToPreviousMap",
                ActionCategory.Map, new List<KeyChord> { KeyChord.Of(KeyCode.Comma, shift: true) }));

            // ---- Side door: ThingSelectionUtilityPatch (src/Map/MapNavigationPatch.cs:288, vanilla method intercept) ----
            // Bare Comma/Period are vanilla's own NextColonist/PreviousColonist KeyBindingDefs; this
            // patch redirects the callback to filter by current map, multi-select focus and
            // mech-section cycling, so both register as vanilla mirrors.
            c.Register(new InputAction("map.selectNextColonist",
                ActionCategory.Map, new List<KeyChord> { KeyChord.Of(KeyCode.Period) }, vanillaKeyBindingDefName: "NextColonist"));
            c.Register(new InputAction("map.selectPreviousColonist",
                ActionCategory.Map, new List<KeyChord> { KeyChord.Of(KeyCode.Comma) }, vanillaKeyBindingDefName: "PreviousColonist"));
            // CameraMapConfigPatch and CameraDriverOnGUIPatch are pure blockers (zeroed
            // dolly/velocity so the vanilla camera does not fight cursor mode) — no actions.

            // ---- Delegation target: EntityTabScope (src/Shell/Screens/EntityTabScope.Game.cs),
            //      backed by the EntityTabState facade (Anomaly DLC) ----
            c.Register(InputAction.ForScreen("entityTab", "entityTab.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            c.Register(InputAction.ForScreen("entityTab", "entityTab.healthInfo",
                new List<KeyChord> { KeyChord.Of(KeyCode.H, alt: true) }));
            c.Register(InputAction.ForScreen("entityTab", "entityTab.moodInfo",
                new List<KeyChord> { KeyChord.Of(KeyCode.M, alt: true) }));
            c.Register(InputAction.ForScreen("entityTab", "entityTab.needsInfo",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));
            c.Register(InputAction.ForScreen("entityTab", "entityTab.gearInfo",
                new List<KeyChord> { KeyChord.Of(KeyCode.G, alt: true) }));

            // ---- Side door: ArchitectPlacementInputPatch (src/Building/ArchitectPlacementPatch.cs:19, UIRootOnGUI) ----
            c.Register(InputAction.ForScreen("architectPlacement", "architectPlacement.openShapeMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) }));
            c.Register(InputAction.ForScreen("architectPlacement", "architectPlacement.switchToManualMode",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab, shift: true) }));
            c.Register(InputAction.ForScreen("architectPlacement", "architectPlacement.expandSelectionScope",
                new List<KeyChord> { KeyChord.Of(KeyCode.A, ctrl: true) }));
            c.Register(InputAction.ForScreen("architectPlacement", "architectPlacement.previousSelectionScope",
                new List<KeyChord> { KeyChord.Of(KeyCode.A, ctrl: true, shift: true) }));
            c.Register(InputAction.ForScreen("architectPlacement", "architectPlacement.removePointOrCancelBlueprint",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space, shift: true) }));
            c.Register(InputAction.ForScreen("architectPlacement", "architectPlacement.rotateCounterclockwise",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, shift: true) }));
            c.Register(InputAction.ForScreen("architectPlacement", "architectPlacement.rotateClockwise",
                new List<KeyChord> { KeyChord.Of(KeyCode.R) }));
            c.Register(InputAction.ForScreen("architectPlacement", "architectPlacement.placeOrToggleCell",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("architectPlacement", "architectPlacement.confirmPlacement",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));
            // Some mods read the physical modifier keys at designation time and change what the
            // tool does (Allow Tool: Alt lifts Select Similar's selection cap, Shift/Ctrl narrow
            // Harvest Fully Grown to crops or trees). A sighted player holds the modifier while
            // clicking; this is the keyboard's equivalent. Ctrl+Alt is excluded — the ambient map
            // scope owns it for colonist-bar inspection — and on Windows Unity may swallow bare
            // Alt+Enter for its fullscreen toggle, which is why Shift+Alt+Enter is in the set too.
            var confirmModified = new List<KeyChord>();
            confirmModified.AddRange(KeyChord.ModifierHeldVariants(KeyCode.Return));
            confirmModified.AddRange(KeyChord.ModifierHeldVariants(KeyCode.KeypadEnter));
            c.Register(InputAction.ForScreen("architectPlacement", "architectPlacement.confirmPlacementModified",
                confirmModified));
            c.Register(InputAction.ForScreen("architectPlacement", "architectPlacement.designatorOptions",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            // Escape here is layered generic cancel (clear points / return to viewing mode / exit).

            // Transport-pod landing (Find.Targeter.IsTargeting with the pod-drop cursor) also accepts
            // Space. OVERLAPS with targeting.confirm, which independently handles Enter/KeypadEnter
            // for the same targeting session; registered separately because Space is unique here.
            c.Register(InputAction.ForScreen("targetingPodLanding", "targetingPodLanding.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space), KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));
            // ArchitectPlacementTimeControlsPatch is a pure blocker (suppresses vanilla
            // Space/TogglePause during placement) and ArchitectPlacementVisualizationPatch is pure
            // rendering — no actions for either.

            // ---- PawnSkillsTableScope ----
            c.Register(InputAction.ForScreen("pawnSkillsTable", "pawnSkillsTable.close",
                new List<KeyChord> { KeyChord.Of(KeyCode.P, alt: true) })); // mirrors the Alt+P opener as a toggle

            // ---- Alt+C jump-to-selected-pawn (the claim lives in MapScope.QuickInfo.Game.cs
            //      as map.jumpToSelectedPawn) ----
            c.Register(new InputAction("map.jumpToSelectedPawn",
                ActionCategory.Map, new List<KeyChord> { KeyChord.Of(KeyCode.C, alt: true) }));

            // ---- Side door: DetailInfoPatch (src/Map/DetailInfoPatch.cs:13, CameraDriver.Update postfix) ----
            // Reads Input.GetKeyDown directly (Update() is not an OnGUI callback), so grepping only
            // for Event.current.keyCode misses these. Local-map tile detail categories 1-7, distinct
            // from World's tile-info 1-5; bare 1/2/3 are free for them because vanilla's default
            // time-speed bindings are blocked in favor of Shift+1/2/3.
            c.Register(new InputAction("map.tileInfo.itemsAndPawns",
                ActionCategory.Map, new List<KeyChord> { KeyChord.Of(KeyCode.Alpha1), KeyChord.Of(KeyCode.Keypad1) }));
            c.Register(new InputAction("map.tileInfo.flooring",
                ActionCategory.Map, new List<KeyChord> { KeyChord.Of(KeyCode.Alpha2), KeyChord.Of(KeyCode.Keypad2) }));
            c.Register(new InputAction("map.tileInfo.resources",
                ActionCategory.Map, new List<KeyChord> { KeyChord.Of(KeyCode.Alpha3), KeyChord.Of(KeyCode.Keypad3) })); // plants/fish/minerals
            c.Register(new InputAction("map.tileInfo.brightnessAndTemp",
                ActionCategory.Map, new List<KeyChord> { KeyChord.Of(KeyCode.Alpha4), KeyChord.Of(KeyCode.Keypad4) }));
            c.Register(new InputAction("map.tileInfo.roomStats",
                ActionCategory.Map, new List<KeyChord> { KeyChord.Of(KeyCode.Alpha5), KeyChord.Of(KeyCode.Keypad5) }));
            c.Register(new InputAction("map.tileInfo.power",
                ActionCategory.Map, new List<KeyChord> { KeyChord.Of(KeyCode.Alpha6), KeyChord.Of(KeyCode.Keypad6) }));
            c.Register(new InputAction("map.tileInfo.areas",
                ActionCategory.Map, new List<KeyChord> { KeyChord.Of(KeyCode.Alpha7), KeyChord.Of(KeyCode.Keypad7) }));

            // ---- GenericWindowScope (src/Shell/Screens/GenericWindowScope.Game.cs) ----
            // Left/Right step the focused row's value when it is a Slider; SharedMenuGrammar
            // already covers Up/Down/Home/End/Enter/Escape/search-backspace for every scope.
            c.Register(InputAction.ForScreen("genericWindow", "genericWindow.setting.decrease",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow) }));
            c.Register(InputAction.ForScreen("genericWindow", "genericWindow.setting.increase",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow) }));
            // Left/Right on a fused stepper row (IntEntry/IntAdjuster) click the small -/+ button.
            // The modified chords are REQUIRED, not multipliers of our own: vanilla's
            // GenUI.CurrentAdjustmentMultiplier reads the PHYSICAL modifier state during the pass
            // that consumes the injected click, so a held modifier scales the button's own
            // arithmetic. Gated on stepper focus, disjoint from the slider pair above.
            c.Register(InputAction.ForScreen("genericWindow", "genericWindow.stepper.decrease",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.LeftArrow), KeyChord.Of(KeyCode.LeftArrow, shift: true),
                    KeyChord.Of(KeyCode.LeftArrow, ctrl: true), KeyChord.Of(KeyCode.LeftArrow, ctrl: true, shift: true),
                }));
            c.Register(InputAction.ForScreen("genericWindow", "genericWindow.stepper.increase",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.RightArrow), KeyChord.Of(KeyCode.RightArrow, shift: true),
                    KeyChord.Of(KeyCode.RightArrow, ctrl: true), KeyChord.Of(KeyCode.RightArrow, ctrl: true, shift: true),
                }));
            // PageUp/PageDown jump to the adjacent settings section's first row, claimed only when a
            // window has two or more distinct sections.
            c.Register(InputAction.ForScreen("genericWindow", "genericWindow.jumpToPreviousSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) }));
            c.Register(InputAction.ForScreen("genericWindow", "genericWindow.jumpToNextSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));
            // Space selects the focused row of a Widgets.CheckboxLabeledSelectable list. Vanilla
            // gives that control two disjoint click targets (the label selects, the right 24 pixels
            // toggle the check), so parity needs a second key beside Enter rather than a second
            // meaning for it. Space is free here: this scope's typeahead takes only letters/digits.
            c.Register(InputAction.ForScreen("genericWindow", "genericWindow.row.select",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));

            // ---- CharacterEditorScope (src/Compat/CharacterEditor/CharacterEditorScope.Game.cs) ----
            // Reached from the map during play, so it belongs to this file's charter. The screen
            // rides the shared tree.* ids above; every mod action is a toolbar entry, and the ten
            // worth a keystroke carry a first-letter Alt chord, all unique within the charEditor
            // ScopeKey. Alt+S is the mod's "save to slot": it shadows menus.sortColumn only in the
            // sense CanCoexist already exempts, and this scope has no table region to sort.
            c.Register(InputAction.ForScreen("charEditor", "charEditor.previousPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.P, alt: true) }));
            c.Register(InputAction.ForScreen("charEditor", "charEditor.nextPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));
            // The five creation-strip entries. Their toolbar rows stay PRESENT but disabled while
            // creation mode is off, and the claims refuse in the same words rather than mutating
            // (CharacterEditorScope.RunCreationAction).
            c.Register(InputAction.ForScreen("charEditor", "charEditor.addPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.A, alt: true) }));
            c.Register(InputAction.ForScreen("charEditor", "charEditor.deletePawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.D, alt: true) }));
            c.Register(InputAction.ForScreen("charEditor", "charEditor.clonePawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.C, alt: true) }));
            c.Register(InputAction.ForScreen("charEditor", "charEditor.randomizePawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));
            c.Register(InputAction.ForScreen("charEditor", "charEditor.findPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.F, alt: true) }));
            // The two preset slots and the in-game-only jump; jumpToPawn is when-guarded on
            // !InStartingScreen, exactly where its toolbar entry exists.
            c.Register(InputAction.ForScreen("charEditor", "charEditor.saveToSlot",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("charEditor", "charEditor.loadFromSlot",
                new List<KeyChord> { KeyChord.Of(KeyCode.L, alt: true) }));
            c.Register(InputAction.ForScreen("charEditor", "charEditor.jumpToPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.J, alt: true) }));

            // ---- CharacterEditorScope's Character section ----
            // Space and Delete are claimed CONTEXTUALLY here, via a when-guard on a skill/trait row.
            c.Register(InputAction.ForScreen("charEditor", "charEditor.cyclePassion",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("charEditor", "charEditor.removeTrait",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));

            // ---- CharacterEditorScope's Abilities/Psycasts/Identity/Training rows ----
            // charEditor.removeTrait above is widened, not re-registered, to also remove an ability
            // row. Only Training's "train one step" needs its own id: Space, when-guarded on a
            // trainable row and disjoint from cyclePassion, since no row is both.
            c.Register(InputAction.ForScreen("charEditor", "charEditor.trainStep",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));

            // ---- Colony Manager Redux + the generic pawn-table tier ----
            // Left/Right bracket cycles a focused work-priority cell exactly as the vanilla work
            // tab's table view does. Shift+Down/Up paint a checkbox row's state onto its neighbor,
            // offered only on the two mod lists whose sighted checkbox is drag-paintable
            // (Widgets.CheckboxMulti with paintable: true).
            c.Register(InputAction.ForScreen("pawnTable", "pawnTable.cyclePriorityDown",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftBracket) }));
            c.Register(InputAction.ForScreen("pawnTable", "pawnTable.cyclePriorityUp",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            c.Register(InputAction.ForScreen("pawnTable", "pawnTable.removeColumn",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            c.Register(InputAction.ForScreen("cmrManager", "cmrManager.cyclePriorityDown",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftBracket) }));
            c.Register(InputAction.ForScreen("cmrManager", "cmrManager.cyclePriorityUp",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            c.Register(InputAction.ForScreen("cmrManager", "cmrManager.paintDown",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("cmrManager", "cmrManager.paintUp",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            c.Register(InputAction.ForScreen("cmrImportJobs", "cmrImportJobs.paintDown",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("cmrImportJobs", "cmrImportJobs.paintUp",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));

            // ---- First-letter Alt chords on five Compat dialog Confirm/OK buttons ----
            // Each is its own screen family, and CanCoexist treats different Screen ScopeKeys as
            // never simultaneously visible, so none of these chords can collide.
            c.Register(InputAction.ForScreen("vefContracts", "vefContracts.accept",
                new List<KeyChord> { KeyChord.Of(KeyCode.A, alt: true) })); // "AcceptQuest".Translate() ("Accept quest")
            c.Register(InputAction.ForScreen("vefHire", "vefHire.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.C, alt: true) }));
            c.Register(InputAction.ForScreen("vefGraphicCustomization", "vefGraphicCustomization.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.C, alt: true) }));
            c.Register(InputAction.ForScreen("charEdBrowser", "charEdBrowser.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.O, alt: true) })); // "OK".Translate()
            c.Register(InputAction.ForScreen("charEdBirthday", "charEdBirthday.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.O, alt: true) })); // "OK".Translate()

            // ---- First-letter mnemonics on three more Compat dialog commit buttons ----
            c.Register(InputAction.ForScreen("charEdConfig", "charEdConfig.saveAndClose",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) })); // "Save and close"
            c.Register(InputAction.ForScreen("vfAssignSeats", "vfAssignSeats.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.C, alt: true) })); // "Confirm".Translate()
            c.Register(InputAction.ForScreen("charEdDefEditor", "charEdDefEditor.saveModifications",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
        }
    }
}
