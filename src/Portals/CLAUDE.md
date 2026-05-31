# Portals Module

## Purpose
Keyboard accessibility for loading pawns and items into a `MapPortal` via the game's
`Dialog_EnterPortal`. Map portals include:

- **AncientHatch** - ancient complex entrances (Ideology/Odyssey opportunity-site quest)
- **PitGate** - Anomaly pit gates
- **InsectLairEntrance** - insect lairs
- **PocketMapExit** - the exit used to haul loot back out of a pocket map

All of these subclass `MapPortal`, whose `GetGizmos()` opens `Dialog_EnterPortal` from the
"Enter ... " / "Exit ... " command. Patching the single dialog covers every portal type and
both directions (enter and exit).

## Files
**Patches:** PortalPatch.cs
**Adapters:** EnterPortalAdapter.cs

## Architecture

`Dialog_EnterPortal` is structurally identical to `Dialog_LoadTransporters` (same Pawns/Items
tabs, same `transferables` / `pawnsTransfer` / `itemsTransfer` / `tab` private fields, same
`OnAcceptKeyPressed` flow, same `Window` base with no `PostClose` override). Rather than
duplicate the navigation logic, this module reuses **`TransportPodLoadingState`** (in the
TransportPods module) through the `ITransferLoadDialog` abstraction.

- `EnterPortalAdapter` implements `ITransferLoadDialog` for `Dialog_EnterPortal`.
- `PortalPatch` wires the dialog lifecycle into that shared state.

### Differences from the transport pod dialog (handled by the adapter)
- **No mass limit** - portals accept any mass; `MassCapacity` returns `float.MaxValue`.
- **No stats panel** - vanilla shows no caravan stats, so `HasSummary` is false and the Tab
  summary view is unavailable (matches what a sighted player sees).
- **No recache method** - `Dialog_EnterPortal` has no `CountToTransferChanged`; its widgets read
  live counts each frame, so `NotifyTransferablesChanged` is a no-op.

## Patches (PortalPatch.cs)
- `Dialog_EnterPortal.PostOpen` -> `TransportPodLoadingState.Open(dialog)`
- `Window.PostClose` (guarded by `is Dialog_EnterPortal`) -> `Close()` + cancel announcement
- `Window.OnCancelKeyPressed` (guarded) -> block Escape when an overlay/typeahead is active
- `Dialog_EnterPortal.OnAcceptKeyPressed` -> block the game's Enter handling while nav is active
- `Dialog_EnterPortal.DoWindowContents` -> draw the "Keyboard Mode Active" visual indicator

## Keyboard Shortcuts
Identical to the transport pod loading dialog (handled by `TransportPodLoadingState`):
- **Left/Right** - switch Pawns/Items tabs
- **Up/Down / Home / End** - navigate items
- **Enter/Space** - toggle a pawn or adjust an item quantity
- **Alt+I** - inspect the selected item/pawn
- **Alt+S** - confirm (accept)
- **Alt+R** - reset
- **Escape** - clear typeahead search, or close the dialog
- Letters/digits - typeahead search

## Dependencies
**Requires:** TransportPods/ (`TransportPodLoadingState`, `ITransferLoadDialog`), ScreenReader/,
Input/ (routing keys off `TransportPodLoadingState.IsActive`), UI/ (QuantityMenuState,
WindowlessInspectionState, StatBreakdownState)
**Used by:** None (self-contained)

## Integration Points
No new routing is required: because the portal dialog reuses `TransportPodLoadingState`, every
existing check that keys off `TransportPodLoadingState.IsActive` (UnifiedKeyboardPatch routing,
MapNavigationPatch key suppression, InfoCardTreeBuilder, TypeaheadConsumerRegistry) automatically
covers map portals too.

## Testing Checklist
- [ ] Entering an ancient complex (AncientHatch) opens an announced Pawns/Items screen
- [ ] Anomaly pit gate enter command opens the same accessible screen
- [ ] Exiting a pocket map (PocketMapExit) lets you load loot to bring out
- [ ] Pawns tab: Space toggles a colonist in/out
- [ ] Items tab: Enter adjusts quantity; items can be loaded
- [ ] Alt+S confirms and pawns begin hauling/loading
- [ ] Escape cancels with an announcement (no double-close of the underlying map)
- [ ] Tab announces "No summary available" (portals have no stats panel)
