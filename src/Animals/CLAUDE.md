# Animals Module

## Purpose
Tame animal and wildlife management with tabular navigation.

## Files
**Patches:** AnimalsMenuPatch.cs, AutoSlaughterPatch.cs, WildlifeMenuPatch.cs
**States:** AnimalsMenuState.cs, AutoSlaughterState.cs, WildlifeMenuState.cs
**Helpers:** AnimalsMenuHelper.cs, WildlifeMenuHelper.cs

## Key Shortcuts
- **Arrow Keys Up/Down** - Navigate animal list (rows)
- **Arrow Keys Left/Right** - Navigate columns
- **Enter** - Interact with current cell
- **Home/End** - Jump to first/last animal
- **Alt+S** - Sort by current column
- **Type letters** - Typeahead search

## Architecture

### AutoSlaughterState (Auto-Slaughter Settings)
Opened from the Animals tab's "Manage Auto-Slaughter" button. Custom row/column navigation (not TabularMenuHelper) for the 7-column limit-setting grid.

**Columns:** Max Total, Max Males, Max Males Young, Max Females, Max Females Young, Allow Pregnant, Allow Bonded

**Key Shortcuts:**
| Key | Action |
|-----|--------|
| Up/Down | Navigate animal rows |
| Left/Right | Navigate columns |
| +/= | +1 limit |
| - | -1 limit |
| Shift+Up/Down | ±10 limit |
| Ctrl+Up/Down | ±100 limit |
| Enter | Type a specific number (numeric input mode) |
| Shift+Home | Set to unlimited |
| Shift+End | Set limit to 0 |
| Space | Toggle checkbox (Allow Pregnant/Bonded) |
| Home/End | Jump to first/last animal |
| Letters | Typeahead animal name search |

**Counting Logic:** Matches vanilla's `CountPlayerAnimals` exactly — bonded animals excluded from category counts when bonded slaughter is disabled; pregnant females excluded from female count/total when pregnant slaughter is disabled.

**Slaughter Summary:** On dialog close, summarizes all animals marked for slaughter across all species (e.g., "Marked for slaughter: 5 cow, 3 chicken").

### Colony Animals & Wildlife Menus
Both menus use `TabularMenuHelper<Pawn>` (from UI module) for shared navigation:
- Row/column navigation with wrap-around
- Sorting with item preservation
- Typeahead search integration
- Cell announcement building

### AnimalsMenuState (Colony Animals)
- 14+ columns (Name, Bond, Master, Slaughter, training columns, etc.)
- Dynamic training columns based on TrainableDef
- Submenu system for Master, AllowedArea, MedicalCare, FoodRestriction
- Default sort: Name ascending

### WildlifeMenuState (Wild Animals)
- 9 fixed columns (Name, Gender, LifeStage, Age, BodySize, Health, Pregnant, Hunt, Tame)
- Simple toggle interactions (Hunt, Tame)
- Default sort: BodySize descending

### TabularMenuHelper Integration
Both state classes create a `TabularMenuHelper<Pawn>` in `Open()`:
```csharp
tableHelper = new TabularMenuHelper<Pawn>(
    getColumnCount: MenuHelper.GetTotalColumnCount,
    getItemLabel: MenuHelper.GetAnimalName,
    getColumnName: MenuHelper.GetColumnName,
    getColumnValue: MenuHelper.GetColumnValue,
    sortByColumn: (items, col, desc) => MenuHelper.SortByColumn(items.ToList(), col, desc),
    defaultSortColumn: 0,
    defaultSortDescending: false
);
```

## Dependencies
**Requires:** ScreenReader/, Input/, UI/TabularMenuHelper

## Testing
- [ ] Animals menu navigation (rows, columns)
- [ ] Wildlife menu navigation (rows, columns)
- [ ] Typeahead search in both menus
- [ ] Sorting by column in both menus
- [ ] Cell interactions (toggles, submenus)
- [ ] Submenu navigation in Animals menu
- [ ] Auto-slaughter: row/column navigation with announcements
- [ ] Auto-slaughter: +/- adjustments and Shift/Ctrl shortcuts
- [ ] Auto-slaughter: Enter numeric input mode, type number, confirm/cancel
- [ ] Auto-slaughter: Checkbox toggles update counts in other columns
- [ ] Auto-slaughter: Pressing + at unlimited sets to current count + 1 (not 0)
- [ ] Auto-slaughter: Pressing - at unlimited sets to current count - 1
- [ ] Auto-slaughter: Shift+Home sets to unlimited
- [ ] Auto-slaughter: Slaughter summary on close lists all animals marked for slaughter
- [ ] Auto-slaughter: Typeahead search for animal names
