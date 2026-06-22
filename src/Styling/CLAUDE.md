# Styling Module

Keyboard accessibility for color/appearance customization that vanilla exposes only through
mouse-driven visual pickers.

## Files

| File | Purpose |
|------|---------|
| `StylingStationState.cs` | Multi-tab navigation state for vanilla `Dialog_StylingStation` (Ideology) |
| `StylingStationHelper.cs` | Reflection bridge into `Dialog_StylingStation` private state + apply/reset logic + style-item and color-source lists |
| `StylingStationPatch.cs` | Harmony lifecycle patches for `Dialog_StylingStation` (PostOpen / PostClose / OnCancelKeyPressed / OnAcceptKeyPressed) |
| `ColorNameHelper.cs` | Reverse-maps a raw `Color` back to the nearest named `ColorDef` for announcement |
| `StyleDescriptionHelper.cs` | Resolves a written visual description of a hair/beard/tattoo style for announcement |

## Style descriptions

Hair, beard, and tattoo styles are visual, so the mod ships a one-paragraph written description of
what each one looks like. `StyleDescriptionHelper.Describe(StyleItemDef)` returns it (or empty for a
style with no description, e.g. a modded one), and callers append it after the actionable label so a
fast navigator hears the name and moves on while a curious one pauses for the full picture.

Descriptions live in `Languages/<lang>/Keyed/RimWorldAccess_StyleDescriptions.xml`, keyed
`RimWorldAccess.StyleDesc.{Hair|Beard|Tattoo}_{defName}` (148 vanilla + DLC entries). They describe
**form only** (shape, length, parting, texture, coverage, what a tattoo depicts and where it sits),
never color, since the same art is worn in any dye color. The descriptions were authored by viewing
each style's in-game `Icon` texture, extracted from the running game via the dev bridge (the textures
are packed in Unity asset bundles, not loose on disk).

The same helper feeds four screens: the styling station (`StylingStationState`), a pawn's Appearance
inspect section (`Inspection/InspectionTreeBuilder.BuildAppearanceChildren`), and the ideoligion
viewer and editor where style frequency is shown/set (`Ideology/IdeologyHelper.BuildStyleItemLabel`
and `IdeoBuilder/IdeoAppearanceEditState`).

## Styling station

`Dialog_StylingStation` opens when a colonist takes the "Change style" job at a styling station.
It is a `forcePause` window the mod drives in place (the real window stays open and renders for
sighted onlookers; we mutate its private fields and call its private apply methods).

### Tabs and navigation

Presented as tabs over flat lists, modeled on `Anomaly/EntityCodexState`:

| Key | Action |
|-----|--------|
| Tab / Shift+Tab | Switch section (Hair, Beard*, Face tattoo, Body tattoo, Apparel color) |
| Up / Down / Home / End | Navigate the current section's list |
| Letters / digits | Typeahead search (routed via `TypeaheadDispatcher`, priority 4.66) |
| Enter | Apply the style and open its color dropdown in one step (hair/beard → hair color; apparel → that piece's color; tattoos just apply, no color) |
| Alt+S | Accept: apply all changes (schedules the styling job) and close |
| ] (right bracket) | Reset all pending changes |
| Escape | Clear typeahead search if active, else revert pending changes and close |

\* The Beard tab appears only when `pawn.style.CanWantBeard`.

Enter clears the typeahead buffer before opening the color dropdown (it would otherwise still be
live on return). The currently-applied style row announces its color and a "selected" marker
("Mohawk, blue, selected"); other rows are bare names since no color is applied to them. There is
deliberately no Alt+I — color selection is reached only through Enter.

### Faithful-to-vanilla details

- Style item lists mirror `Dialog_StylingStation.DrawStylingItemType`'s exact filter
  (`PawnStyleItemChooser.WantsToUseStyle(pawn, x) || x == initial`, sorted by
  `FrequencyFromGender`). An ideo-restricted pawn legitimately shows few styles — that is parity,
  not a bug.
- Style items are applied **live** as you Enter them (matching the dialog's preview behavior); the
  actual look change only commits when the scheduled `UseStylingStation` job runs after Accept.
- Color swatches come from the dialog's OWN deduped lists (`AllHairColors` ~70, `AllColors` ~36,
  via reflection) for exact parity with what a sighted player sees — the game ships duplicate-named
  ColorDefs (3× "Red", 3× "Purple") that vanilla collapses by color similarity. Names come from
  `ColorNameHelper`, which appends a luminance-ordered number to leftover same-name shades
  ("Purple 1/2/3", darkest first) so every swatch is reachable and distinct.
- The apparel color picker also offers the dialog's "Set ideoligion color" / "Set favorite color"
  buttons as menu entries, and refuses to recolor locked apparel (`ApparelLockedCannotRecolor`).
- Skin color / body type / head type tabs are vanilla **dev-only** (the "Show all" checkbox) and
  are intentionally not surfaced. Skin colors also come from genes all labelled "Skin color", with
  no distinct names.
- Accept replicates `DrawBottomButtons`' Accept branch via the dialog's own private `Reset(bool)`
  and `ApplyApparelColors()` plus `Pawn_StyleTracker.SetupNextLookChangeData` + a `UseStylingStation`
  job, so behavior matches the mouse path exactly.

### Reflection

All `Dialog_StylingStation` private access is centralized in `StylingStationHelper` (fields `pawn`,
`stylingStation`, `desiredHairColor`, `apparelColors`, `curTab`, `initial*`; methods `Reset`,
`ApplyApparelColors`). All targets are verified to resolve against the live game.

## Paint colors (cross-reference)

The companion feature — choosing colors for **Paint Floor / Paint Building** — lives in the Building
module (`Building/PaintColorHelper.cs`), because paint tools are architect designators. It reuses
the same `WindowlessFloatMenuState` + `ColorDef`-label approach. The picker auto-opens when a paint
designator is selected (mirroring vanilla's swatch grid) and reopens with `C` during placement.

## Dependencies

**Requires:** `ScreenReader/` (TolkHelper), `Input/` (KeyboardHelper, TypeaheadConsumerRegistry),
`UI/` (MenuHelper, TypeaheadSearchHelper, WindowlessFloatMenuState).
