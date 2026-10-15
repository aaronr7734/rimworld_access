using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Action inventory, part 2: caravan split, map/world targeting overlays, the world scanner
    /// and world navigation keys, the WorldGen starting-site screen, the route planner, the
    /// Scenario Builder family, the pawn-filter family, the starting-pawn screen, and the area
    /// manager. Retired ids stay registered as DORMANT so saved rebindings survive.
    /// </summary>
    internal static class ShellActionInventoryPart2
    {
        internal static void Register(ActionCatalog c)
        {
            // ---- split caravan dialog ----
            // DORMANT: the live screen is SplitCaravanScope, which claims a splitCaravan.* family
            // and the shared menus.nextRegion/previousRegion grammar instead.
            c.Register(InputAction.ForScreen("caravanSplit", "caravanSplit.toggleSummary",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) }));
            c.Register(InputAction.ForScreen("caravanSplit", "caravanSplit.previousTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow) }));
            c.Register(InputAction.ForScreen("caravanSplit", "caravanSplit.nextTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow) }));
            c.Register(InputAction.ForScreen("caravanSplit", "caravanSplit.addMaxOrSelectAll",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return, shift: true), KeyChord.Of(KeyCode.KeypadEnter, shift: true) }));
            c.Register(InputAction.ForScreen("caravanSplit", "caravanSplit.removeItem",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            c.Register(InputAction.ForScreen("caravanSplit", "caravanSplit.inspect",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            c.Register(InputAction.ForScreen("caravanSplit", "caravanSplit.apply",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("caravanSplit", "caravanSplit.reset",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));

            // ---- transport pod launch targeting -> TransportPodLaunchState ----
            // Enter/Escape are targeting confirm/cancel here, not list grammar.
            c.Register(InputAction.ForScreen("podLaunch", "podLaunch.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));
            c.Register(InputAction.ForScreen("podLaunch", "podLaunch.cancel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Escape) }));
            c.Register(InputAction.ForScreen("podLaunch", "podLaunch.fuelStatus",
                new List<KeyChord> { KeyChord.Of(KeyCode.F) }));

            // ---- gravship destination targeting -> GravshipDestinationState ----
            c.Register(InputAction.ForScreen("gravshipDest", "gravshipDest.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));
            c.Register(InputAction.ForScreen("gravshipDest", "gravshipDest.cancel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Escape) }));
            c.Register(InputAction.ForScreen("gravshipDest", "gravshipDest.fuelStatus",
                new List<KeyChord> { KeyChord.Of(KeyCode.F) }));

            // ---- Archonexus new-colony tile pick -> NewColonyTilePickState ----
            c.Register(InputAction.ForScreen("newColonyTile", "newColonyTile.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));
            c.Register(InputAction.ForScreen("newColonyTile", "newColonyTile.cancel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Escape) })); // announces the picker cannot be cancelled

            // ---- jump-pack / locust-armor targeting -> JumpTargetingState ----
            c.Register(InputAction.ForScreen("jumpTargeting", "jumpTargeting.range",
                new List<KeyChord> { KeyChord.Of(KeyCode.R) }));

            // ---- psycast / ability map targeting -> AbilityTargetingState ----
            c.Register(InputAction.ForScreen("abilityTargeting", "abilityTargeting.range",
                new List<KeyChord> { KeyChord.Of(KeyCode.R) }));
            c.Register(InputAction.ForScreen("abilityTargeting", "abilityTargeting.affectedTargets",
                new List<KeyChord> { KeyChord.Of(KeyCode.T) }));

            // ---- generic targeting fallback -> GenericTargetingState ----
            c.Register(InputAction.ForScreen("genericTargeting", "genericTargeting.range",
                new List<KeyChord> { KeyChord.Of(KeyCode.R) }));

            // ---- item targeting (force-wear / force-equip) -> ItemTargetingState ----
            c.Register(InputAction.ForScreen("itemTargeting", "itemTargeting.range",
                new List<KeyChord> { KeyChord.Of(KeyCode.R) }));
            // T does nothing user-visible; it exists so TargetingScope has an id to bind the
            // silent consume to (blocks the game's time shortcut).
            c.Register(InputAction.ForScreen("itemTargeting", "itemTargeting.affectedTargets",
                new List<KeyChord> { KeyChord.Of(KeyCode.T) }));

            // ---- Command_Target range check -> TargetingPatch.HandleRangeCheck ----
            // Handled inline, gated on Find.Targeter.IsTargeting && TargetingPatch.HasTargetingContext.
            c.Register(InputAction.ForScreen("commandTargeting", "commandTargeting.range",
                new List<KeyChord> { KeyChord.Of(KeyCode.R) }));

            // ---- world ability targeting (e.g. Farskip) -> WorldAbilityTargetingState ----
            c.Register(InputAction.ForScreen("worldAbilityTargeting", "worldAbilityTargeting.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));
            c.Register(InputAction.ForScreen("worldAbilityTargeting", "worldAbilityTargeting.cancel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Escape) }));

            // ---- external-provider world targeting (e.g. Vehicle Framework aerial launch) ----
            c.Register(InputAction.ForScreen("externalWorldTargeting", "externalWorldTargeting.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));
            c.Register(InputAction.ForScreen("externalWorldTargeting", "externalWorldTargeting.cancel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Escape) }));
            c.Register(InputAction.ForScreen("externalWorldTargeting", "externalWorldTargeting.popWaypoint",
                new List<KeyChord> { KeyChord.Of(KeyCode.Backspace) }));
            c.Register(InputAction.ForScreen("externalWorldTargeting", "externalWorldTargeting.status",
                new List<KeyChord> { KeyChord.Of(KeyCode.F) }));

            // ---- world scanner keys -> WorldScannerState ----
            // PageUp/PageDown here are structured scanner navigation (item / Shift=subcategory /
            // Ctrl=category / Alt=instance), not generic paging. WorldScope (Context==InGame) and
            // StartingSiteScreenScope (WorldGen) claim the same ids, so one rebind covers both worlds.
            c.Register(new InputAction("world.scanner.nextItem",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));
            c.Register(new InputAction("world.scanner.nextSubcategory",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.PageDown, shift: true) }));
            c.Register(new InputAction("world.scanner.nextCategory",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.PageDown, ctrl: true) }));
            c.Register(new InputAction("world.scanner.nextInstance",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.PageDown, alt: true) }));
            c.Register(new InputAction("world.scanner.previousItem",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) }));
            c.Register(new InputAction("world.scanner.previousSubcategory",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.PageUp, shift: true) }));
            c.Register(new InputAction("world.scanner.previousCategory",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.PageUp, ctrl: true) }));
            c.Register(new InputAction("world.scanner.previousInstance",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.PageUp, alt: true) }));
            c.Register(new InputAction("world.scanner.jumpToCurrent",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.Home) }));
            c.Register(new InputAction("world.scanner.jumpToHome",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.Home, alt: true) }));
            c.Register(new InputAction("world.scanner.readDistance",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.End) }));
            c.Register(new InputAction("world.scanner.jumpToNearestCaravan",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.End, alt: true) })); // in-game world only
            c.Register(new InputAction("world.scanner.toggleAutoJump",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.J, alt: true) }));

            // ---- world navigation keys -> WorldNavigationState ----
            // Ambient in-game world view (Context == InGame); not menu grammar.
            c.Register(new InputAction("world.caravan.cycleNext",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.Period) }));
            c.Register(new InputAction("world.caravan.cyclePrevious",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.Comma) }));
            c.Register(new InputAction("world.caravan.toggleSelection",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.Space, ctrl: true) }));
            c.Register(new InputAction("world.caravan.jumpToSelected",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.C, alt: true) }));
            c.Register(new InputAction("world.caravan.inspect",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.I) })); // caravan inspect when one is selected, else detailed tile info; yields to gizmo-menu typeahead
            c.Register(new InputAction("world.object.select",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) })); // opens world-object selection at the current tile
            // ---- WorldGen starting-site screen -> StartingSiteScreenScope ----
            // Name mismatch, deliberate: the four biomeJump ids and startingSite.announceTile belong
            // to the shared WorldMapElement, not to this screen, but keep their startingSite.* names
            // because renaming an action id throws away a player's saved rebinding.
            c.Register(InputAction.ForScreen("startingSite", "startingSite.biomeJumpNorth",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, ctrl: true) }));
            c.Register(InputAction.ForScreen("startingSite", "startingSite.biomeJumpSouth",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, ctrl: true) }));
            c.Register(InputAction.ForScreen("startingSite", "startingSite.biomeJumpWest",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow, ctrl: true) }));
            c.Register(InputAction.ForScreen("startingSite", "startingSite.biomeJumpEast",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow, ctrl: true) }));
            c.Register(InputAction.ForScreen("startingSite", "startingSite.randomTile",
                new List<KeyChord> { KeyChord.Of(KeyCode.R) }));
            c.Register(InputAction.ForScreen("startingSite", "startingSite.announceTile",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            // startingSite.additionalInfo is RETIRED: its categories are now the read-only
            // "Tile info" content region on StartingSiteScreenScope. No id replaces it.
            c.Register(InputAction.ForScreen("startingSite", "startingSite.factionRelations",
                new List<KeyChord> { KeyChord.Of(KeyCode.F) }));
            // Alt+N, the mod-wide "Next" toolbar chord.
            c.Register(InputAction.ForScreen("startingSite", "startingSite.next",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));

            // ---- route planner -> RoutePlannerState ----
            // Overlay over the world map; Space is consumed to prevent vanilla TogglePause.
            c.Register(InputAction.ForScreen("route", "route.addWaypoint",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("route", "route.removeWaypoint",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space, shift: true) }));
            c.Register(InputAction.ForScreen("route", "route.eta",
                new List<KeyChord> { KeyChord.Of(KeyCode.E) }));
            c.Register(InputAction.ForScreen("route", "route.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) })); // confirm route (caravan mode) or announce summary (standalone)

            // ---- R opens the route planner in world view ----
            c.Register(new InputAction("world.routePlanner.open",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.R) }));

            // ---- Shift+R opens Vehicle Framework's standalone vehicle route planner selector ----
            // Mutually exclusive with world.routePlanner.open; Shift+R is otherwise unclaimed in
            // the World ambient category.
            c.Register(new InputAction("world.vfRoutePlanner.open",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.R, shift: true) }));

            // ---- F8 dismisses the in-game world map ----
            c.Register(new InputAction("world.dismiss",
                ActionCategory.World, new List<KeyChord> { KeyChord.Of(KeyCode.F8) }));

            // ---- world-view key suppression -> WorldScope ----
            // CyclePlanetLayer has no "previous" direction, so one action carries both chords.
            c.Register(new InputAction("world.layer.cycle",
                ActionCategory.World,
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab), KeyChord.Of(KeyCode.Tab, shift: true) }));
            // Silent consume-only claim, load-bearing against map rungs whose own gates pass on
            // world view because Find.CurrentMap stays non-null while the planet renders.
            // Return/KeypadEnter are deliberately excluded — see WorldScope.Nav.Game.cs's remarks.
            // Shift twins are required for the bare letters: vanilla KeyBindingDef matching ignores
            // modifier state, so a shifted press would otherwise reach vanilla's own binding.
            c.Register(new InputAction("world.blackout.blockKey",
                ActionCategory.World,
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.A), KeyChord.Of(KeyCode.Q), KeyChord.Of(KeyCode.P),
                    KeyChord.Of(KeyCode.S), KeyChord.Of(KeyCode.L),
                    KeyChord.Of(KeyCode.A, shift: true), KeyChord.Of(KeyCode.Q, shift: true),
                    KeyChord.Of(KeyCode.P, shift: true), KeyChord.Of(KeyCode.S, shift: true),
                    KeyChord.Of(KeyCode.L, shift: true),
                    KeyChord.Of(KeyCode.M, alt: true), KeyChord.Of(KeyCode.H, alt: true),
                    KeyChord.Of(KeyCode.N, alt: true), KeyChord.Of(KeyCode.B, alt: true),
                    KeyChord.Of(KeyCode.K, alt: true), KeyChord.Of(KeyCode.A, alt: true),
                    KeyChord.Of(KeyCode.F, alt: true), KeyChord.Of(KeyCode.R, alt: true),
                }));

            // ---- Scenario Builder overlays ----
            // ScenarioAddPartScreenScope is windowless-but-MODAL (a deliberate deviation from a
            // non-modal cascade — see ScenarioOverlayScreenScopes.Game.cs's class remarks), so
            // ShellGuards' native modal swallow does the anti-leak work. Every partEdit.* id below
            // is DORMANT: field editing rides ScreenScope's generic
            // ComboBox/Stepper/TextFieldEditSession rows.
            c.Register(InputAction.ForScreen("addPart", "addPart.blockNav",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.LeftArrow), KeyChord.Of(KeyCode.RightArrow),
                    KeyChord.Of(KeyCode.Tab), KeyChord.Of(KeyCode.Delete),
                }));
            // Same-chord partEdit actions across the three sub-modes are real mode-dependent pairs;
            // the inventory test's KnownModeDependentPairs allowlist records them.
            c.Register(InputAction.ForScreen("partEdit", "partEdit.quantityIncrease",
                new List<KeyChord>
                {
                    // Arrow chords scale by modifiers (Shift=+10, Ctrl=+100, Ctrl wins); +/- always
                    // step by one. Shift+Equals is the physical '+' on US layouts.
                    KeyChord.Of(KeyCode.UpArrow), KeyChord.Of(KeyCode.UpArrow, shift: true),
                    KeyChord.Of(KeyCode.UpArrow, ctrl: true), KeyChord.Of(KeyCode.UpArrow, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.Plus), KeyChord.Of(KeyCode.KeypadPlus),
                    KeyChord.Of(KeyCode.Equals), KeyChord.Of(KeyCode.Equals, shift: true),
                }));
            c.Register(InputAction.ForScreen("partEdit", "partEdit.quantityDecrease",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.DownArrow), KeyChord.Of(KeyCode.DownArrow, shift: true),
                    KeyChord.Of(KeyCode.DownArrow, ctrl: true), KeyChord.Of(KeyCode.DownArrow, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.Minus), KeyChord.Of(KeyCode.KeypadMinus),
                }));
            c.Register(InputAction.ForScreen("partEdit", "partEdit.quantityMin",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home) }));
            c.Register(InputAction.ForScreen("partEdit", "partEdit.quantityMax",
                new List<KeyChord> { KeyChord.Of(KeyCode.End) }));
            // Text mode routes cursor keys through the embedded TextInputController's own event
            // reader, hence the full modifier matrix.
            c.Register(InputAction.ForScreen("partEdit", "partEdit.textCursor",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.LeftArrow), KeyChord.Of(KeyCode.LeftArrow, shift: true),
                    KeyChord.Of(KeyCode.LeftArrow, ctrl: true), KeyChord.Of(KeyCode.LeftArrow, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.RightArrow), KeyChord.Of(KeyCode.RightArrow, shift: true),
                    KeyChord.Of(KeyCode.RightArrow, ctrl: true), KeyChord.Of(KeyCode.RightArrow, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.Home), KeyChord.Of(KeyCode.Home, shift: true),
                    KeyChord.Of(KeyCode.Home, ctrl: true), KeyChord.Of(KeyCode.Home, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.End), KeyChord.Of(KeyCode.End, shift: true),
                    KeyChord.Of(KeyCode.End, ctrl: true), KeyChord.Of(KeyCode.End, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.Delete), KeyChord.Of(KeyCode.Delete, shift: true),
                    KeyChord.Of(KeyCode.Delete, ctrl: true), KeyChord.Of(KeyCode.Delete, ctrl: true, shift: true),
                }));
            c.Register(InputAction.ForScreen("partEdit", "partEdit.textNewline",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return, shift: true), KeyChord.Of(KeyCode.KeypadEnter, shift: true) }));
            c.Register(InputAction.ForScreen("partEdit", "partEdit.textLineUp",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow), KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            c.Register(InputAction.ForScreen("partEdit", "partEdit.textLineDown",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow), KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("partEdit", "partEdit.textReadCurrent",
                new List<KeyChord> { KeyChord.Of(KeyCode.Insert) }));
            c.Register(InputAction.ForScreen("partEdit", "partEdit.textCopy",
                new List<KeyChord> { KeyChord.Of(KeyCode.C, ctrl: true) }));
            c.Register(InputAction.ForScreen("partEdit", "partEdit.textPaste",
                new List<KeyChord> { KeyChord.Of(KeyCode.V, ctrl: true) }));
            // Dropdown/Quantity anti-leak consume; in Text mode textCursor wins Left/Right/Delete
            // by registration order and only Tab lands here.
            c.Register(InputAction.ForScreen("partEdit", "partEdit.blockNav",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.LeftArrow), KeyChord.Of(KeyCode.RightArrow),
                    KeyChord.Of(KeyCode.Tab), KeyChord.Of(KeyCode.Delete),
                }));

            // ---- Scenario editor -> ScenarioEditorScreenScope ----
            // Arrows/Home/End/Enter/Tab/Escape are the shared ScreenScope grammar, so nextSection
            // and previousSection below are DORMANT. MovePartUp/MovePartDown are retired outright
            // onto SharedMenuGrammar.ReorderUp/Down; Ctrl+Home/End and '*' expand-all ride the
            // shared "tree" set now that the parts region is a TreeRegionScope panel.
            c.Register(InputAction.ForScreen("scenarioBuilder", "scenarioBuilder.next",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));
            c.Register(InputAction.ForScreen("scenarioBuilder", "scenarioBuilder.load",
                new List<KeyChord> { KeyChord.Of(KeyCode.L, alt: true) }));
            c.Register(InputAction.ForScreen("scenarioBuilder", "scenarioBuilder.save",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("scenarioBuilder", "scenarioBuilder.randomizeSeed",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));
            c.Register(InputAction.ForScreen("scenarioBuilder", "scenarioBuilder.addPart",
                new List<KeyChord> { KeyChord.Of(KeyCode.A, alt: true) }));
            c.Register(InputAction.ForScreen("scenarioBuilder", "scenarioBuilder.nextSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) }));
            c.Register(InputAction.ForScreen("scenarioBuilder", "scenarioBuilder.previousSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab, shift: true) }));
            c.Register(InputAction.ForScreen("scenarioBuilder", "scenarioBuilder.deletePart",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            // Coarse Quantity steps; plain Left/Right (step 1) ride menus.previousHorizontal/
            // nextHorizontal, Shift/Ctrl scale the step to 10/100.
            c.Register(InputAction.ForScreen("scenarioBuilder", "scenarioBuilder.quantityStepLarge",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.LeftArrow, shift: true), KeyChord.Of(KeyCode.RightArrow, shift: true),
                    KeyChord.Of(KeyCode.LeftArrow, ctrl: true), KeyChord.Of(KeyCode.RightArrow, ctrl: true),
                }));
            c.Register(InputAction.ForScreen("scenarioBuilder", "scenarioBuilder.readFullField",
                new List<KeyChord> { KeyChord.Of(KeyCode.Insert) }));

            // ---- pawn-filter preset overlays -> FilterPreset*Scope ----
            // Only Delete is screen-specific; the rest is shared list grammar, and name editing is
            // a modal TextFieldEditSession that owns its own caret and clipboard chords.
            c.Register(InputAction.ForScreen("filterPresetLoad", "filterPresetLoad.delete",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));

            // ---- pawn-filter editor -> PawnFilterScope ----
            c.Register(InputAction.ForScreen("pawnFilter", "pawnFilter.saveAndClose",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("pawnFilter", "pawnFilter.jumpToPreviousSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) })); // jump between section headers
            c.Register(InputAction.ForScreen("pawnFilter", "pawnFilter.jumpToNextSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));
            // Both chord flavors are registered so exact-match dispatch covers the shifted press;
            // the handler forwards the snapshot's Shift to scale the step.
            c.Register(InputAction.ForScreen("pawnFilter", "pawnFilter.decreaseValue",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow), KeyChord.Of(KeyCode.LeftArrow, shift: true) }));
            c.Register(InputAction.ForScreen("pawnFilter", "pawnFilter.increaseValue",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow), KeyChord.Of(KeyCode.RightArrow, shift: true) }));
            c.Register(InputAction.ForScreen("pawnFilter", "pawnFilter.activateSecondary",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("pawnFilter", "pawnFilter.deleteTrait",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            c.Register(InputAction.ForScreen("pawnFilter", "pawnFilter.valueMin",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, shift: true) })); // slider to minimum
            c.Register(InputAction.ForScreen("pawnFilter", "pawnFilter.valueMax",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, shift: true) })); // slider to maximum

            // ---- starting-pawn selection screen -> StartingPawnScreenScope ----
            // The base ScreenScope grammar owns region cycling, Up/Down/Home/End, Left/Right and
            // Enter, so toggleTab and infoCard below are DORMANT. expandAllSiblings/firstAbsolute/
            // lastAbsolute are RETIRED onto the shared tree.* ids (Part7), which carry the identical
            // chords for every tree screen; tree.jumpToPreviousSection/NextSection stay unclaimed
            // here because previousPawn/nextPawn own Page Up/Down.
            c.Register(InputAction.ForScreen("startingPawns", "startingPawns.toggleTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab), KeyChord.Of(KeyCode.Tab, shift: true) })); // DORMANT — base region cycling
            c.Register(InputAction.ForScreen("startingPawns", "startingPawns.openFilter",
                new List<KeyChord> { KeyChord.Of(KeyCode.F, alt: true) }));
            c.Register(InputAction.ForScreen("startingPawns", "startingPawns.randomize",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));
            c.Register(InputAction.ForScreen("startingPawns", "startingPawns.rename",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));
            c.Register(InputAction.ForScreen("startingPawns", "startingPawns.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) })); // DORMANT — menus.info is the unified Alt+I drill-in
            c.Register(InputAction.ForScreen("startingPawns", "startingPawns.addPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.A, alt: true) })); // wanderer context only
            c.Register(InputAction.ForScreen("startingPawns", "startingPawns.removePawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) })); // wanderer context only
            c.Register(InputAction.ForScreen("startingPawns", "startingPawns.previousPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) })); // switch pawn preserving tree position
            c.Register(InputAction.ForScreen("startingPawns", "startingPawns.nextPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));
            c.Register(InputAction.ForScreen("startingPawns", "startingPawns.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            // Start takes Alt+S because Alt+N is startingPawns.rename here and the only shared Alt+S
            // action (menus.sortColumn) needs a table region this screen never builds. The Enter
            // double-press proceed seam reaches this id via DefaultAcceptActionId, not a chord.
            c.Register(InputAction.ForScreen("startingPawns", "startingPawns.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            // Delete on the GameStart tree is deliberately unregistered: no node wires OnDelete, so
            // the generic tree grammar's DeleteCurrent() is a no-op and the modal backstop's silent
            // consume is identical.

            // ---- dev-mode debug actions menu -> DevDebugScope ----
            // Space is the one per-screen secondary: on a node row it pins or unpins the action to
            // Prefs.DebugActionsPalette; the when-gate lets Space pass on Tabs and Buttons rows.
            c.Register(InputAction.ForScreen("devDebug", "devDebug.togglePin",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));

            // ---- dev-mode tweak-values editor -> DevTweakValuesScope ----
            // Space resets the focused tweak field to its initial value; the when-gate lets Space pass on the Buttons row.
            c.Register(InputAction.ForScreen("devTweak", "devTweak.reset",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));

            // ---- RimWorld 1.7 frame profiler -> FrameProfilerScope (the window's own keys; see its header) ----
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.pause",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.olderFrame",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.newerFrame",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.newestFrame",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown, ctrl: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.extendOlder",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.extendNewer",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.clearSelection",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.copyRow",
                new List<KeyChord> { KeyChord.Of(KeyCode.C, ctrl: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.copyView",
                new List<KeyChord> { KeyChord.Of(KeyCode.C, ctrl: true, shift: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.nextMatch",
                new List<KeyChord> { KeyChord.Of(KeyCode.F3) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.previousMatch",
                new List<KeyChord> { KeyChord.Of(KeyCode.F3, shift: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.settings",
                new List<KeyChord> { KeyChord.Of(KeyCode.O, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.toggleSource",
                new List<KeyChord> { KeyChord.Of(KeyCode.K, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.toggleHistogramMode",
                new List<KeyChord> { KeyChord.Of(KeyCode.M, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.clearFrames",
                new List<KeyChord> { KeyChord.Of(KeyCode.C, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.cpuTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha1, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.allocTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha2, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.vramTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha3, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.toggleDisplay",
                new List<KeyChord> { KeyChord.Of(KeyCode.G, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.threads",
                new List<KeyChord> { KeyChord.Of(KeyCode.T, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.search",
                new List<KeyChord> { KeyChord.Of(KeyCode.F, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.invert",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.expandAll",
                new List<KeyChord> { KeyChord.Of(KeyCode.Equals, alt: true), KeyChord.Of(KeyCode.KeypadPlus, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.collapseAll",
                new List<KeyChord> { KeyChord.Of(KeyCode.Minus, alt: true), KeyChord.Of(KeyCode.KeypadMinus, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.vramBaseline",
                new List<KeyChord> { KeyChord.Of(KeyCode.B, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.vramClearBaseline",
                new List<KeyChord> { KeyChord.Of(KeyCode.B, shift: true, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.vramDeltaSort",
                new List<KeyChord> { KeyChord.Of(KeyCode.D, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.vramExport",
                new List<KeyChord> { KeyChord.Of(KeyCode.X, alt: true) }));
            c.Register(InputAction.ForScreen("frameProfiler", "frameProfiler.selectTimeRange",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return, shift: true), KeyChord.Of(KeyCode.KeypadEnter, shift: true) }));

            // ---- dev-mode def editor -> DevDefEditorScope ----
            // A TreeRegionScope, so only the Space secondary (structural New/Add/Delete context
            // menu) carries its own id. Page Up/Down stay unregistered and unclaimed: claiming them
            // would turn a fall-through into a reject.
            c.Register(InputAction.ForScreen("devDefEditor", "devDefEditor.actions",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
        }
    }
}
