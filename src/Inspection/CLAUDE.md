# Inspection Module

## Purpose
Building and object inspection UI, bills management, storage settings, and gizmo navigation.

## Files
**Patches:** BuildingInspectPatch.cs, StorageSettingsMenuPatch.cs, GizmoNavigationPatch.cs
**States:** BuildingInspectState.cs, WindowlessInspectionState.cs, BillsMenuState.cs, BillConfigState.cs, StorageSettingsMenuState.cs, ThingFilterMenuState.cs, ThingFilterNavigationState.cs, RangeEditMenuState.cs, GizmoNavigationState.cs, WindowlessInventoryState.cs
**Helpers:** InspectionInfoHelper.cs, InspectionTreeBuilder.cs, InspectionTreeItem.cs, InventoryHelper.cs, PowerInfoHelper.cs

## Key Shortcuts
- **Enter** - Open inspection at cursor
- **G** - Gizmo navigation
- **I** - Colony inventory
- **Arrow Keys** - Navigate inspection tree
- **+/-** - Adjust values

## Architecture
BuildingInspectPatch runs at VeryHigh priority. ThingFilterMenuState handles recursive tree navigation.

## Dependencies
**Requires:** ScreenReader/, Input/, Map/ (cursor position)

## Testing
- [ ] Building inspection opens correctly
- [ ] Bills management navigable
- [ ] Storage filters work
- [ ] Gizmo navigation functional
