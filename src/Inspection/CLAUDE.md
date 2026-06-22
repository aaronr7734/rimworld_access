# Inspection Module

## Purpose
Building and object inspection UI, bills management, storage settings, gizmo navigation, and Info Card accessibility.

## Files

**Patches:** BuildingInspectPatch.cs, StorageSettingsMenuPatch.cs, GizmoNavigationPatch.cs, InfoCardPatch.cs
**States:** WindowlessInspectionState.cs, BillsMenuState.cs, BillConfigState.cs, StorageSettingsMenuState.cs, ThingFilterMenuState.cs, ThingFilterNavigationState.cs, RangeEditMenuState.cs, GizmoNavigationState.cs, WindowlessInventoryState.cs, InfoCardState.cs, EntityTabState.cs
**Helpers:** InspectionInfoHelper.cs, InspectionTreeBuilder.cs, InspectionTreeItem.cs, InventoryHelper.cs, PowerInfoHelper.cs, TabRegistry.cs, InfoCardDataExtractor.cs, InfoCardTreeBuilder.cs

### EntityTabState

Replaces the old BasicInspectString fallback for `ITab_Entity` (Anomaly DLC). Previously, the
"Entity" tab dumped only `GetInspectString()`, which omitted every interactive control. The state
now surfaces a full menu:

| Row | Type | Behavior |
|-----|------|----------|
| Containment Strength | read-only | `StatDefOf.ContainmentStrength` value |
| Escape MTB Days | read-only | `ContainmentUtility.InitiateEscapeMtbDays` with reasons |
| Study Interval | read-only | `CompStudiable.Props.frequencyTicks` (when > 0) |
| Knowledge Gain | read-only | `CompStudiable.AdjustedAnomalyKnowledgePerStudy` |
| Allow Medicine | interactive | Left/Right cycles `MedicalCareCategory` enum |
| Containment Mode | interactive | Up/Down or Enter cycles MaintainOnly/Study/Release/Execute. Skips Execute when `!Props.canBeExecuted`. |
| Extract Bioferrite | interactive | Enter/Space toggles. Disabled if `BioferriteExtraction` not researched, or if HoldingPlatform has attached bioferrite harvester. |

`TabRegistry` registers `ITab_Entity` as `Action`. Dispatch in `InspectionTreeBuilder.ExecuteCategoryAction`
covers both held pawn (`pawn.IsOnHoldingPlatform`) and platform Thing targets — `EntityTabState.Open`
resolves the held pawn either way.

**Alt+I/H/M/N/G** open InfoCard / Health / Mood / Needs / Gear announcements for the held entity,
matching the universal pawn-picker keyboard convention.

Routed via `BuildingInspectPatch` (priority VeryHigh prefix) alongside BillsMenuState.

### Plan management

A `Verse.Plan` marker at the cursor is added to the inspection object list (alongside zones, which
are likewise not Things) and shows a read-only **Overview** (color, name, size). Its management
actions — Rename, Change color, Toggle visibility, Expand, Shrink, Delete — live on the **G gizmo
key**: `GizmoNavigationState.OpenAtCursor` collects the plan at the cursor and adds the synthetic
`Command_Action`s from `Building/PlanActionHelper.BuildGizmos`.

## Key Shortcuts
- **Enter** - Open inspection at cursor
- **G** - Gizmo navigation
- **I** - Colony inventory
- **Arrow Keys** - Navigate inspection tree
- **+/-** - Adjust values

## Architecture

BuildingInspectPatch runs at VeryHigh priority. ThingFilterMenuState handles recursive tree navigation.

### Dynamic Tab Discovery

The inspection system uses dynamic tab discovery to automatically detect and expose RimWorld's native inspect tabs (Health, Needs, Gear, Prisoner, etc.) for keyboard navigation.

**Key Components:**

1. **TabRegistry.cs** - Central registry mapping RimWorld tab types to accessibility handlers
   - `GetTabCategories(Thing)` - Discovers visible tabs for an object
   - `GetHandlerForTab()` - Returns handler type (RichNavigation, Action, BasicInspectString)
   - Maps tab type names (e.g., `ITab_Pawn_Prisoner`) to friendly category names

2. **InspectionInfoHelper.GetDynamicCategories()** - Builds category list by:
   - Calling `TabRegistry.GetTabCategories()` for real tabs
   - Adding synthetic categories (Mood, Skills, Work Priorities) that aren't tabs but provide useful info

3. **InspectionTreeBuilder.BuildCategoryItemFromInfo()** - Creates tree items from TabCategoryInfo

**Critical: Object Selection for Tab Visibility**

RimWorld tabs determine visibility via `SelPawn`/`SelThing` which come from `Find.Selector.SingleSelectedThing`. When inspecting via accessibility cursor, the object must be selected first:

```csharp
// WindowlessInspectionState.Open() - REQUIRED before building tree
if (objects.Count > 0 && objects[0] is Thing thingToSelect)
{
    Find.Selector.ClearSelection();
    Find.Selector.Select(thingToSelect, playSound: false, forceDesignatorDeselect: false);
}
```

Without this, tabs like Prisoner, Slave, Guest, Training will not appear because their `IsVisible` checks return false.

### Info Card System

Provides keyboard navigation for RimWorld's Dialog_InfoCard (opened via 'i' on items or from inspection tree).

**Components:**
- **InfoCardState.cs** - Main state class with tree navigation
- **InfoCardPatch.cs** - Uses PostOpen/PostClose lifecycle for proper window management
- **InfoCardDataExtractor.cs** - Extracts stats, descriptions, recipes from InfoCard
- **InfoCardTreeBuilder.cs** - Builds navigable tree from extracted data

**Input Handling:**
- Keyboard input is handled via UnifiedKeyboardPatch at priority -0.25
- InfoCardPatch.DoWindowContents handles only delayed initialization (waiting for stats to populate)
- PostOpen initializes state, PostClose cleans up state

### Handler Types

`TabHandlerType` enum determines how categories behave:

| Type | Behavior | Examples |
|------|----------|----------|
| `RichNavigation` | Expandable with sub-items | Health, Gear, Skills, Needs |
| `Action` | Opens separate menu | Bills, Storage, Prisoner, Temperature |
| `BasicInspectString` | Shows GetInspectString text | Guest, Power, unknown tabs |

## Dependencies
**Requires:** ScreenReader/, Input/, Map/ (cursor position)

## Testing
- [ ] Building inspection opens correctly
- [ ] Prisoner tab appears for prisoners (cursor-only, no click)
- [ ] Training tab appears for tamed animals
- [ ] Bills management navigable
- [ ] Storage filters work
- [ ] Gizmo navigation functional
- [ ] Info Card opens and navigates correctly
