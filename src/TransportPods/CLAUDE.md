# TransportPods Module

## Purpose
Keyboard accessibility for transport pod loading, launch targeting, and world map destination selection. Also supports shuttles (Royalty DLC) which use the same systems.

## Shared loading state
`TransportPodLoadingState` is **shared** between two near-identical dialogs via the
`ITransferLoadDialog` abstraction (`ITransferLoadDialog.cs`):
- `Dialog_LoadTransporters` (pods/shuttles) - `LoadTransportersAdapter.cs` (this module)
- `Dialog_EnterPortal` (map portals) - `EnterPortalAdapter` in the **Portals** module

The state owns all navigation, announcement, typeahead, summary, and quantity logic; the adapters
isolate the dialog-specific reflection, mass capacity, change notification, and summary stats.
When changing the state, remember it now drives both dialogs - keep dialog-specific behavior in
the adapters, not the state.

## Files
**Patches:** TransportPodPatch.cs
**States:** TransportPodSelectionState.cs, TransportPodLoadingState.cs, TransportPodLaunchState.cs
**Helpers:** TransportPodHelper.cs

## Key Shortcuts

### Pod Selection Mode (Custom Gizmo)
- **G** - Open gizmo menu at cursor
- Select "Select pods to group" gizmo, then **Enter**
- **Up/Down** - Navigate between pods at cursor
- **Space** - Toggle current pod selection
- **Enter** - Group selected pods and open loading dialog
- **Escape** - Cancel selection

### Loading Dialog (Dialog_LoadTransporters)
- **Left/Right** - Switch tabs (Pawns/Items)
- **Up/Down** - Navigate items
- **Enter/Space** - Adjust quantity (opens QuantityMenuState)
- **Tab** - Read mass summary
- **Alt+I** - Inspect current item
- **Alt+S** - Accept (start loading)
- **Alt+R** - Reset
- **Escape** - Cancel

### Launch Targeting (World Map)
- **G** on loaded pod, navigate to "Launch" gizmo, then **Enter**
- **Arrow Keys** - Navigate world tiles (WorldNavigationState)
- **Scanner** (PageUp/Down) - Browse destinations with fuel costs
- **Enter** - Select destination (shows float menu if multiple options)
- **Escape** - Cancel targeting

### Local Map Landing (When Landing at Existing Map)
- **Arrow Keys** - Move landing cursor
- **Space/Enter** - Confirm landing cell
- **Escape** - Cancel

## Architecture

### State Machine
1. **Pod Selection Mode** - User selects which pods to group
2. **Loading Dialog** - User assigns cargo to pods
3. **Launch Targeting** - User selects world map destination
4. **Local Map Landing** - User selects specific landing cell (only for existing maps)

### Key Patterns

**Pod Selection:**
- Custom gizmo "Select pods to group" appears on CompTransporter buildings
- Enters selection mode where user toggles individual pods with Space
- All selected pods assigned same groupID when Enter pressed
- Opens loading dialog automatically after grouping

**Loading Dialog:**
- Mirrors CaravanFormationState but simpler (no travel supplies, no route planning)
- Two tabs: Pawns, Items
- Tab key reads mass summary: "Mass: 150 of 1050 kg"
- Alt+S accepts and starts hauling jobs

**Launch Targeting:**
- Extends WorldScannerState to announce fuel costs per destination
- Format: "[Location], [distance] tiles, [fuel cost] chemfuel"
- If insufficient fuel: "[Location], NOT ENOUGH FUEL"
- Enter selects tile, float menu appears if multiple options

**Local Map Landing:**
- Reuses existing Find.Targeter targeting mode
- Extends existing placement mode handler in ArchitectPlacementPatch
- Only announces when cell is invalid: "Wall, INVALID LANDING SITE"

### Gizmo Integration

**CompTransporter Gizmos (Loading State):**
- "Load transporter(s)" - Opens Dialog_LoadTransporters
- "Cancel load" / "Unload" - Cancels loading

**CompLaunchable Gizmos (Ready State):**
- "Launch" - Begins world map targeting

**Custom Gizmo (Our Addition):**
- "Select pods to group" - Enters TransportPodSelectionState
- Only appears on transport pods

### Reflection Required

**Dialog_LoadTransporters:**
- `Tab tab` (private enum) - Current selected tab
- `List<TransferableOneWay> transferables` (private) - All items
- `TransferableOneWayWidget pawnsTransfer` (private) - Pawns widget
- `TransferableOneWayWidget itemsTransfer` (private) - Items widget
- `List<CompTransporter> transporters` (private) - Pod group

**CompTransporter:**
- `int groupID` (private) - Links pods together

**CompLaunchable:**
- `CompProperties_Launchable Props` - Contains fuelPerTile

### Shuttle Support

Shuttles (Royalty DLC) use the same systems:
- Same `Dialog_LoadTransporters`
- Same `CompTransporter` and `CompLaunchable`
- All transport pod accessibility features automatically work with shuttles

## Dependencies
**Requires:** ScreenReader/, Input/, Map/ (cursor), World/ (navigation, scanner), UI/ (QuantityMenuState, WindowlessInspectionState)
**Used by:** None (self-contained)

## Integration Points

### UnifiedKeyboardPatch Priorities
- 0.28: TransportPodSelectionState (pod selection mode)
- 0.32: TransportPodLoadingState (loading dialog navigation)
- 0.36: TransportPodLaunchState (world targeting with fuel)

### Modified Existing Files
- **WorldScannerState.cs** - Add fuel cost to announcements when LaunchTargetingState.IsActive
- **ArchitectPlacementPatch.cs** - Handle Find.Targeter.IsTargeting for local map landing

### Shared States
- **QuantityMenuState** - Quantity adjustment in loading dialog
- **WindowlessInspectionState** - Alt+I item inspection
- **WorldNavigationState** - World map navigation during targeting
- **WorldScannerState** - Enhanced with fuel cost announcements
- **TypeaheadSearchHelper** - Search in loading dialog

## Testing Checklist
- [ ] Pod selection mode activates from custom gizmo
- [ ] Up/Down navigates between pods
- [ ] Space toggles pod selection
- [ ] Enter groups pods and opens loading dialog
- [ ] Loading dialog tabs switch correctly
- [ ] Quantity adjustment works for pawns and items
- [ ] Tab announces mass summary
- [ ] Alt+S accepts and starts loading
- [ ] Launch gizmo appears after loading completes
- [ ] Launch targeting announces fuel costs per destination
- [ ] "NOT ENOUGH FUEL" announced for out-of-range tiles
- [ ] Enter on tile opens float menu (if multiple options)
- [ ] Local map landing mode announces only invalid cells
- [ ] Shuttle loading works identically to transport pods

## Known Issues / Notes

**Pod Placement:**
- Transport pods must be placed adjacent to fueling port cell (90 degrees clockwise from launcher facing)
- Use `FuelingPortUtility.GetFuelingPortCell(launcher)` to get valid cell

**Fuel Calculation:**
- Fuel cost = distance * fuelPerTile (from CompProperties_Launchable)
- Use `MaxLaunchDistanceAtFuelLevel(float)` to check range
- Pods are one-way (destroyed on launch, pawns walk/caravan back)

**Message Syncing:**
- Game sends "MessageFinishedLoadingTransporters" when complete
- Existing message handlers catch this automatically
