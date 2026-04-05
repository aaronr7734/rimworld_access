# Mechs Module

## Purpose
Mechanoid management tab accessibility for the Biotech DLC. Provides keyboard navigation for the Mechs PawnTable tab.

## Files
**Patches:** MechsMenuPatch.cs
**States:** MechsMenuState.cs
**Helpers:** MechsMenuHelper.cs

## Key Shortcuts
| Key | Action |
|-----|--------|
| Up/Down | Navigate mech list (rows) |
| Left/Right | Navigate columns |
| Enter | Interact with current cell |
| Home/End | Jump to first/last mech |
| Shift+Down/Up | Paint current cell's value to next/previous row |
| Shift+Home/End | Bulk paint from current row to first/last |
| Ctrl+Shift+Home/End | Paint entire column |
| Alt+S | Sort by current column |
| Alt+I | Open info card |
| Type letters | Typeahead search |
| Backspace | Clear search |
| Escape | Clear search or close menu |

## Architecture

### Columns (8 total, matching Biotech PawnTableDef)
| # | Column | defName | Interactive | Paintable | Sortable |
|---|--------|---------|-------------|-----------|----------|
| 0 | Name | LabelWithIcon | Jump to mech | No | Yes |
| 1 | Energy | Energy | No (display) | No | No |
| 2 | Draft | DraftMech | Toggle | Yes | No |
| 3 | Auto Repair | AutoRepair | Toggle | Yes | No |
| 4 | Overseer | Overseer | No (display) | No | No |
| 5 | Control Group | ControlGroup | Submenu | Yes | No |
| 6 | Work Mode | WorkMode | Submenu | No | No |
| 7 | Allowed Area | AllowedAreaMech | Submenu | Yes | No |

### Submenus
- **ControlGroup**: Lists overseer's control groups; apply assigns mech to selected group
- **WorkMode**: Lists MechWorkModeDefs ordered by uiOrder; changes affect entire control group
- **AllowedArea**: Same pattern as Animals (Manage Areas, Unrestricted, areas list)

### Painting
- **Draft/AutoRepair**: Boolean painting (checkbox toggle)
- **ControlGroup**: Copies group assignment (same overseer only — cross-overseer blocked)
- **AllowedArea**: Uses lastAppliedArea mechanism (same as Animals)
- **WorkMode**: NOT paintable (affects entire control group, not individual mechs)

### Special States
- **Gestating mechs**: Energy shows "Gestating", ControlGroup shows "Gestating", Draft disabled
- **No CompMechRepairable**: AutoRepair shows "N/A"

### Default Sort
Matches PawnTable_Mechs.LabelSortFunction: Overseer → ControlGroupIndex → KindLabel → Label

## Dependencies
**Requires:** ScreenReader/, Input/, UI/TabularMenuHelper, UI/PawnColumnSortHelper
**DLC Required:** Biotech

## Priority in UnifiedKeyboardPatch
4.741 (after Animals 4.74, before Scanner Search 4.745)

## Testing Checklist
- [ ] Mechs menu opens from game tab (with Biotech active and mechs present)
- [ ] Opening announcement includes mech count
- [ ] Row navigation (Up/Down) with mech names and kind labels
- [ ] Column navigation (Left/Right) with column names and values
- [ ] Alt+S on Name column cycles sort (descending/ascending/clear)
- [ ] Alt+S on other columns announces "cannot be sorted"
- [ ] Enter on Name jumps to mech on map
- [ ] Enter on Draft toggles draft (with CanDraftMech guard)
- [ ] Enter on AutoRepair toggles auto-repair (null comp guard)
- [ ] Enter on ControlGroup opens submenu, navigate, apply
- [ ] Enter on WorkMode opens submenu, navigate, apply (group-wide change announced)
- [ ] Enter on AllowedArea opens submenu with Manage Areas support
- [ ] Shift+Down/Up paints Draft column
- [ ] Shift+Down/Up paints AutoRepair column
- [ ] Shift+Down/Up paints ControlGroup (same overseer only)
- [ ] Shift+Down/Up paints AllowedArea with lastAppliedArea
- [ ] Shift+Home/End and Ctrl+Shift+Home/End bulk paint
- [ ] WorkMode column shows "Cannot paint this column"
- [ ] Gestating mechs show "Gestating" for Energy and ControlGroup
- [ ] Typeahead search finds mechs by name
- [ ] Alt+I opens info card
- [ ] Escape closes menu (or clears search first)
- [ ] Menu appears in IsAnyAccessibilityMenuActive
