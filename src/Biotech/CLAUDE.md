# Biotech Module

## Purpose
Accessibility features for RimWorld's Biotech DLC, including baby gene inspection, mechanitor control group management, and child growth moment choices.

## Files

**States:** GeneInspectionState.cs, MechControlGroupState.cs, GrowthMomentState.cs
**Patches:** GeneInspectionPatch.cs, GrowthMomentPatch.cs
**Helpers:** GeneTreeBuilder.cs

## Key Shortcuts (Gene Inspection)

| Key | Action |
|-----|--------|
| Up/Down | Navigate items (respects WrapNavigation) |
| Right/Enter | Expand collapsed node, or move to first child |
| Left | Collapse expanded node, or move to parent |
| Home | Jump to first sibling at same level |
| End | Jump to last sibling at same level |
| Ctrl+Home | Jump to absolute first in tree |
| Ctrl+End | Jump to absolute last in tree |
| Page Up/Down | Jump between gene headers |
| * | Expand all siblings at current level |
| Typeahead | Jump to gene/item starting with typed letters |
| Backspace | Clear search |
| Escape | Close gene inspection |

## Architecture

### Trigger Point

When a pregnant pawn is selected (not drafted), the "Inspect Baby Genes..." gizmo appears. Clicking it calls `InspectPaneUtility.OpenTab(typeof(ITab_GenesPregnancy))`. Our patch intercepts this to activate `GeneInspectionState`.

### Data Flow

```
User clicks gizmo
    → InspectPaneUtility.OpenTab()
        → GeneInspectionPatch.OpenTab_Patch (Postfix)
            → Gets selected pawn
            → Finds HediffWithParents (pregnancy hediff)
            → Extracts GeneSet from hediff
            → GeneTreeBuilder.BuildTree(geneSet)
            → GeneInspectionState.Open(pawn)
```

### Gene Tree Structure

```
Baby Genes: [Xenotype] (N genes)
├── Gene: Robust (expandable)
│   ├── Complexity: +1
│   ├── Metabolism: -1
│   ├── Description lines...
│   └── Effects (expandable, if present)
│       └── Effect descriptions...
├── Gene: Strong Back (expandable)
│   ├── Complexity: +1
│   └── Description lines...
└── Biostats Summary (expandable)
    ├── Total Complexity: X
    ├── Total Metabolism: +/-Y
    └── Archites Required: Z (if any)
```

### Key Classes

| Class | Location | Purpose |
|-------|----------|---------|
| `HediffWithParents` | Verse | Base class for pregnancy, holds `geneSet`, `Father`, `Mother` |
| `GeneSet` | RimWorld | Container for genes with `GenesListForReading`, biostats |
| `GeneDef` | Verse | Individual gene definition with label, description, stats |
| `ITab_GenesPregnancy` | RimWorld | The visual UI tab that displays baby genes |

### Accessing Pregnancy Data

```csharp
// Get pregnancy hediff from pawn
HediffWithParents pregnancy = pawn.health.hediffSet.hediffs
    .OfType<HediffWithParents>()
    .FirstOrDefault();

if (pregnancy != null)
{
    GeneSet babyGenes = pregnancy.geneSet;
    Pawn father = pregnancy.Father;
    Pawn mother = pregnancy.Mother;

    // Iterate genes
    foreach (GeneDef gene in babyGenes.GenesListForReading)
    {
        string name = gene.label;
        string description = gene.description;
        int complexity = gene.biostatCpx;
        int metabolism = gene.biostatMet;
    }

    // Biostats totals
    int totalComplexity = babyGenes.ComplexityTotal;
    int totalMetabolism = babyGenes.MetabolismTotal;
    int totalArchites = babyGenes.ArchitesTotal;
}
```

## Dependencies

**Requires:**
- ScreenReader/ (TolkHelper for announcements)
- Input/ (UnifiedKeyboardPatch for keyboard routing)
- UI/ (TypeaheadSearchHelper, MenuHelper)
- Inspection/ (InspectionTreeItem for tree structure)

**DLC Required:** Biotech

## Priority in UnifiedKeyboardPatch

GeneInspectionState is handled at Priority -0.23, after InfoCard (-0.25) and AutoSlaughter (-0.24), but before world map handlers (Priority 0+).

## State Lifecycle

1. User clicks "Inspect Baby Genes..." gizmo on pregnant pawn
2. `InspectPaneUtility.OpenTab(ITab_GenesPregnancy)` called
3. `GeneInspectionPatch.OpenTab_Patch` detects tab type
4. Finds pregnancy hediff and extracts GeneSet
5. `GeneTreeBuilder.BuildTree()` creates navigation tree
6. `GeneInspectionState.Open()` activates state, announces opening
7. User navigates with keyboard, `HandleInput()` processes keys
8. User presses Escape or tab closes
9. `GeneInspectionState.Close()` deactivates state

## Testing Checklist

- [ ] Gene inspection opens when gizmo clicked on pregnant pawn
- [ ] Opening announcement includes xenotype and gene count
- [ ] Up/Down navigates between items
- [ ] Right/Enter expands collapsed genes
- [ ] Left collapses expanded genes, or moves to parent
- [ ] Home/End jump to first/last sibling
- [ ] Ctrl+Home/End jump to absolute first/last
- [ ] Page Up/Down jump between gene headers
- [ ] * expands all siblings at current level
- [ ] Typeahead search finds genes by name
- [ ] Backspace clears search
- [ ] Escape closes gene inspection
- [ ] State closes when selection changes
- [ ] State closes when inspect tab closes

## Mech Control Group State

### Trigger Point

When a mechanitor pawn is selected, the `MechanitorControlGroupGizmo` gizmos appear in the gizmo bar (one per control group). Pressing Enter on one opens `MechControlGroupState`.

### Two-Page Layout

**Page 1: Settings** (default on open)
- Current work mode (Enter opens work mode selection float menu)
- Recharge range (Enter opens inline range editor)
- Select all mechs (Enter selects all mechs in group)

**Page 2: Members**
- Individual mechs with energy levels
- Enter jumps to and selects the mech
- `]` key opens reassignment menu to move mech to another group

Tab/Shift+Tab switches between pages.

### Key Shortcuts (Control Group)

| Key | Action |
|-----|--------|
| Up/Down | Navigate items (respects WrapNavigation) |
| Enter | Execute current item (varies by type) |
| Tab/Shift+Tab | Switch between Settings and Members pages |
| Home/End | Jump to first/last item |
| ] | Reassign mech to another group (members page only) |
| Typeahead | Jump to item starting with typed letters |
| Backspace | Clear search |
| Escape | Clear search, or close and return to gizmo navigation |

### Range Editor (Recharge Settings)

| Key | Action |
|-----|--------|
| Up/Down | Toggle between Minimum and Maximum |
| Left/Right | Adjust by 5% |
| Shift+Left/Right | Fine adjust by 1% |
| Enter | Save and close range editor |
| Escape | Cancel and close range editor |

### Key Classes

| Class | Location | Purpose |
|-------|----------|---------|
| `MechanitorControlGroup` | RimWorld | Control group data: mechs, work mode, index, recharge thresholds |
| `MechanitorControlGroupGizmo` | RimWorld | The visual gizmo (extends Gizmo directly, NOT Command) |
| `Pawn_MechanitorTracker` | RimWorld | Manages all control groups for a mechanitor pawn |
| `MechWorkModeDef` | Verse | Work mode definition (Work, Escort, Recharge, Dormant Self-Charge) |

### Accessing Control Group Data

The `controlGroup` field on `MechanitorControlGroupGizmo` is private. Accessed via `AccessTools.Field()` with cached `FieldInfo`. Key properties:
- `controlGroup.Index` (1-based int)
- `controlGroup.WorkMode.LabelCap` (translated work mode name)
- `controlGroup.MechsForReading` (list of non-gestating mechs)
- `controlGroup.mechRechargeThresholds` (FloatRange)
- `controlGroup.Tracker.controlGroups` (all groups for reassignment)

### Gizmo Label

The gizmo label in the gizmo bar is generated by `MechControlGroupState.GetGizmoLabel()`:
- Single group: "Control group 1, Work, 3 mechs"
- Merged empty groups: "Groups 1, 2, no mechs"

### Priority in UnifiedKeyboardPatch

MechControlGroupState is handled at Priority 4.7795, after plant selection (4.7793) but before gizmo navigation (4.78).

## Growth Moment State

### Trigger Point

When a child reaches age 7, 10, or 13, a `ChoiceLetter_GrowthMoment` letter arrives. Opening it (from notification menu or auto-open) creates `Dialog_GrowthMomentChoices`. Our patch intercepts `OpenLetter()` to activate `GrowthMomentState`.

### Tabbed Layout

**Tiers 0–3** (no passions): Info, Traits
**Tiers 4–8** (passions offered): Info, Passions, Traits

Tab/Shift+Tab switches tabs. Info tab reads automatically on open.

### Data Flow

```
Growth letter opens
    → ChoiceLetter_GrowthMoment.OpenLetter()
        → TrySetChoices() populates passionChoices/traitChoices
        → Dialog_GrowthMomentChoices added to WindowStack
        → GrowthMomentPatch.OpenLetter_Patch (Postfix)
            → Finds dialog on WindowStack
            → GrowthMomentState.Open(letter, dialog)
                → Builds info lines, passion items, trait items
                → Reads info aloud, focuses first selection tab
```

### Key Shortcuts (Growth Moment)

| Key | Action |
|-----|--------|
| Up/Down | Navigate items in current tab (respects WrapNavigation) |
| Enter/Space | Toggle/select passion or trait |
| Tab/Shift+Tab | Switch between tabs |
| Home/End | Jump to first/last item in current tab |
| Alt+S | Confirm choices (validates, then applies) |
| Escape | Clear search, or postpone (Later) |
| Typeahead | Jump to item by name (passion/trait tabs) |
| Backspace | Clear typeahead search |

### Key Classes

| Class | Location | Purpose |
|-------|----------|---------|
| `Dialog_GrowthMomentChoices` | RimWorld | The visual dialog (extends Window) |
| `ChoiceLetter_GrowthMoment` | RimWorld | Letter with growth data (extends LetterWithTimeout, NOT ChoiceLetter) |
| `GrowthUtility` | RimWorld | Growth tier definitions, moment ages, flavor text |
| `GrowthUtility.GrowthTier` | RimWorld | Tier data: passion/trait choices and gains counts |

### Passion Selection

- Each selected skill gets exactly ONE passion bump (None→Minor or Minor→Major)
- `passionGainsCount == 1`: Radio button behavior
- `passionGainsCount > 1`: Checkbox behavior with max count enforcement
- Skills that are permanently disabled or already at Major passion are excluded

### Trait Selection

- Always radio button (single selection)
- 35% chance of "No trait" option when 3+ trait choices
- `ChoiceLetter_GrowthMoment.NoTrait` sentinel used for no-trait selection

### Notification Menu Integration

`ChoiceLetter_GrowthMoment` extends `LetterWithTimeout`, NOT `ChoiceLetter`. The standard `ExtractLetterButtons()` in `NotificationMenuState` won't find DiaOption buttons. A specific check for `ChoiceLetter_GrowthMoment` adds an "Open growth moment" button that calls `OpenLetter()`.

### Priority in UnifiedKeyboardPatch

GrowthMomentState is handled at Priority -0.215, between GeneInspection (-0.23) and FactionLanding (-0.22).

### State Lifecycle

1. Growth letter arrives when child reaches age 7, 10, or 13
2. User opens letter from notification menu (L key) or it auto-opens
3. `ChoiceLetter_GrowthMoment.OpenLetter()` fires
4. `GrowthMomentPatch.OpenLetter_Patch` finds dialog, calls `GrowthMomentState.Open()`
5. Info tab reads aloud, auto-focuses first selection tab
6. User navigates with Tab (tabs) and Up/Down (items), selects with Enter
7. User presses Alt+S to confirm, or Escape to postpone
8. `GrowthMomentState.ConfirmChoices()` calls `letter.MakeChoices()`, closes dialog, removes letter
9. `Window.PostClose` patch calls `GrowthMomentState.Close()` as fallback cleanup

### Testing Checklist

- [ ] Growth letter appears in notification menu with "Open growth moment" button
- [ ] Opening reads info tab content automatically
- [ ] Tab/Shift+Tab switches between Info, Passions, Traits tabs
- [ ] Up/Down navigates items with inline details
- [ ] Enter toggles passion selection (radio for 1, checkbox for 2+)
- [ ] Enter selects traits (radio behavior, deselects previous)
- [ ] Alt+S validates and announces errors when incomplete
- [ ] Alt+S applies choices and closes when valid
- [ ] Escape postpones (or announces error if auto-open forced)
- [ ] Typeahead search works in passion and trait tabs
- [ ] Home/End jump to first/last in current tab
- [ ] Archive view shows read-only content
- [ ] State closes when dialog closes (timeout, pawn death)

## Future Work

- Xenogerm creation/editing accessibility
- Gene extractor accessibility
- Growth vat monitoring
