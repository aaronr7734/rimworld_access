# Building Module

Construction, zones, areas, and shape-based multi-cell placement.

## File Groups

**Architect Menu** - `ArchitectState.cs`, `ArchitectTreeState.cs`, `ArchitectHelper.cs`, `ArchitectMenuPatch.cs`, `ArchitectPlacementPatch.cs`
Category/tool selection with treeview navigation. ArchitectState tracks mode (Category/Tool/Material/Placement).

**Shape Placement** - `ShapePlacementState.cs`, `ShapeHelper.cs`, `ShapePreviewHelper.cs`, `ShapeSelectionMenuState.cs`
Two-point placement workflow (Line, Rectangle, Oval). ShapeHelper wraps RimWorld's DrawStyle classes.

**Zones** - `ZoneCreationState.cs`, `ZoneCreationPatch.cs`, `ZoneUndoTracker.cs`, `ZoneRenameState.cs`
Zone create/expand/shrink with undo. Uses ShapePreviewHelper for shape selection.

**Areas** - `AreaPatch.cs`, `AreaPaintingState.cs`, `WindowlessAreaState.cs`
Allowed areas and home zone management.

**Analysis** - `ObstacleDetector.cs`, `EnclosureDetector.cs`
Find obstacles blocking placement; detect rooms formed by blueprints.

**Post-Placement** - `ViewingModeState.cs`, `SelectionPreviewPatch.cs`
Review results, undo segments, navigate obstacles via ScannerState.

**Building Controls** - `BuildingComponentsHelper.cs`, `*ComponentState.cs`, `*ControlState.cs`
Toggle flickable/refuel/door/forbid settings on buildings.

**Storage Linking** - `ShelfLinkingState.cs`, `ShelfLinkingPatch.cs`, `ShelfLinkingHelper.cs`, `ShelfLinkingConfirmDialog.cs`
Link storage buildings (shelves, bookcases) together without mouse-based multi-select. Two gizmos: "Link all in room" and "Link storage manually".

## Key Entry Point

`DesignatorManagerPatch.Postfix` intercepts ALL designator selections and routes to ShapePlacementState.

## Designator Type Checking

Use ShapeHelper methods (not manual type checks):
- `IsBuildDesignator()` - Designator_Build
- `IsZoneDesignator()` - Zone hierarchy
- `IsCellsDesignator()` - Mine, etc.
- `IsDeleteDesignator()` - Zone shrink
- `IsOrderDesignator()` - Hunt, Haul, Tame

## Two-Point Selection Pattern

```csharp
var previewHelper = new ShapePreviewHelper();
previewHelper.SetCurrentShape(ShapeType.FilledRectangle);
previewHelper.SetFirstCorner(cell, "[Context]");
previewHelper.UpdatePreview(cursor);  // plays sound on count change
previewHelper.SetSecondCorner(cell, "[Context]");
var cells = previewHelper.PreviewCells;
```

### Select-All Shortcut
`Ctrl+A` in shape placement fills both corners automatically:
- Inside a finished enclosed room → room bounds via `Room.ExtentsClose`.
- Inside an area ringed by wall BLUEPRINTS/frames (no finished room yet) →
  `EnclosureDetector.TryFloodFillFromCell(cursor, map)` picks it up and the
  bounding box of its interior cells is used.
- Otherwise (outdoors / open map) → entire map bounds.
- Line and AngledLine shapes refuse — only rect/oval variants apply.
- Uses `ShapePlacementState.SetBothPoints` which jumps straight to `Previewing`.

## Large-Shape Placement Performance

Selecting many cells at once (e.g. whole-map rectangle) must avoid O(n²) patterns:
- `ViewingModeState` keeps `obstacleCells` as a `List<IntVec3>` but pairs it with a
  `HashSet<IntVec3> obstacleCellsSet` so dedup is O(1) — a prior List.Contains loop
  froze the game for multiple seconds on full-map chop-wood selections.
- When adding new obstacle/protected cells, do `if (obstacleCellsSet.Add(cell)) obstacleCells.Add(cell)`.
- Always clear both containers together (Enter-fresh and Reset paths).
- Removals go through the HashSet first: `if (obstacleCellsSet.Remove(cell)) obstacleCells.Remove(cell);`.

## Zone Undo Pattern

```csharp
ZoneUndoTracker.CaptureBeforeState(zone, map, isShrink);
designator.DesignateMultiCell(cells);
ZoneUndoTracker.CaptureAfterState(map);
ZoneUndoTracker.AddSegment();
```

## Storage Linking

Accessible gizmos for linking storage buildings without mouse-based multi-select.

### Keyboard Shortcuts (Manual Selection Mode)
- **Arrow Keys** - Navigate map cursor
- **Space** - Toggle storage selection at cursor
- **Enter** - Confirm and link all selected storage
- **Escape** - Cancel selection mode

### Gizmos Added to Building_Storage
1. **Link all in room (X shelves, Y bookcases)** - Only on storage in enclosed rooms. Links all compatible storage in the room.
2. **Link storage manually** - Always available. Enter selection mode to manually choose storage to link.

### Confirmation Dialog
When linking storage that's already in a different group, a confirmation dialog appears:
- **Enter** - Confirm and move items to this group
- **Escape** - Cancel

### Priority in UnifiedKeyboardPatch
- 0.26: ShelfLinkingConfirmDialog
- 0.27: ShelfLinkingState
