# Animals Module

## Purpose
Tame animal and wildlife management with tabular navigation.

## Files
**Patches:** AnimalsMenuPatch.cs, AutoSlaughterPatch.cs, WildlifeMenuPatch.cs
**States:** AnimalsMenuState.cs, AutoSlaughterState.cs, WildlifeMenuState.cs
**Helpers:** AnimalsMenuHelper.cs, WildlifeMenuHelper.cs

## Key Shortcuts (Animals & Wildlife)
- **Arrow Keys Up/Down** - Navigate animal list (rows)
- **Arrow Keys Left/Right** - Navigate columns
- **Enter** - Interact with current cell
- **Home/End** - Jump to first/last animal
- **Shift+Down/Up** - Paint current cell's value to next/previous row
- **Shift+Home/End** - Bulk paint from current row to first/last
- **Ctrl+Shift+Home/End** - Paint entire column
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

### Painting Support
Both menus support column painting for game-paintable columns:
- **Checkbox columns** (detected via `PawnColumnDef.paintable`): Slaughter, Sterile, FollowDrafted, FollowFieldwork, ReleaseToWild, SpecialTrainable, AnimalDig, AnimalForage
- **Training columns** (workers pass `paintable: true` to `Widgets.Checkbox`): All TrainableDef columns (Obedience, Release, Rescue, Haul, etc.)
- **Dropdown columns** (workers pass `paintable: true` to `Widgets.Dropdown`): Master (Click sound), MedicalCare (Tick_High sound)
- **AllowedArea** (mod-specific `lastAppliedArea` mechanism)
- **WildlifeMenuHelper**: Checkbox columns (Hunt, Tame)
- Uses `BulkSoundQueue` for rapid-fire sounds during bulk paint
- Single-step paint announces "already {value}" when target matches brush
- Announcements follow Schedule tab pattern: "Painted {column} {value} for {name list}"

## Testing
- [ ] Animals menu navigation (rows, columns)
- [ ] Wildlife menu navigation (rows, columns)
- [ ] Typeahead search in both menus
- [ ] Sorting by column in both menus
- [ ] Cell interactions (toggles, submenus)
- [ ] Submenu navigation in Animals menu
- [ ] Animals: Shift+Down/Up paints checkbox columns (Slaughter, FollowDrafted, etc.)
- [ ] Animals: Shift+Down/Up paints training columns (Obedience, Release, etc.)
- [ ] Animals: Shift+Down/Up paints Master column (copies master assignment)
- [ ] Animals: Shift+Down/Up paints Medical care column (copies care level)
- [ ] Animals: Shift+Home/End bulk paints with name list announcement
- [ ] Animals: Ctrl+Shift+Home/End paints entire column
- [ ] Animals: AllowedArea painting uses current cell's area as brush
- [ ] Animals: "Already set" announced when target matches brush value
- [ ] Animals: Non-paintable columns (Name, Gender, Age, etc.) show "Cannot paint this column"
- [ ] Wildlife: Shift+Down/Up paints Hunt/Tame columns
- [ ] Wildlife: Shift+Home/End bulk paints with name list announcement
- [ ] Wildlife: Ctrl+Shift+Home/End paints entire column
- [ ] Wildlife: Non-paintable columns show "Cannot paint this column"
- [ ] Auto-slaughter: row/column navigation with announcements
- [ ] Auto-slaughter: +/- adjustments and Shift/Ctrl shortcuts
- [ ] Auto-slaughter: Enter numeric input mode, type number, confirm/cancel
- [ ] Auto-slaughter: Checkbox toggles update counts in other columns
- [ ] Auto-slaughter: Pressing + at unlimited sets to current count + 1 (not 0)
- [ ] Auto-slaughter: Pressing - at unlimited sets to current count - 1
- [ ] Auto-slaughter: Shift+Home sets to unlimited
- [ ] Auto-slaughter: Slaughter summary on close lists all animals marked for slaughter
- [ ] Auto-slaughter: Typeahead search for animal names
