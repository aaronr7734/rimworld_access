# Anomaly Module

Screen reader accessibility for RimWorld's Anomaly DLC.

## Files

| File | Purpose |
|------|---------|
| `EntityCodexState.cs` | Navigation state for `Dialog_EntityCodex` — flat menu with typeahead search |
| `EntityCodexPatch.cs` | Harmony patches for `Dialog_EntityCodex` lifecycle (PostOpen, PostClose, OnCancelKeyPressed) |

## EntityCodexState

Handles keyboard navigation inside `Dialog_EntityCodex`, the in-game browsable codex of discovered entities.

### Architecture

Follows the State + Patch pattern from `src/Rituals/DryadCasteState.cs`. The state builds a flat list of all visible `EntityCodexEntryDef` entries ordered by category (`listOrder`) then by `orderInCategory`. TypeaheadSearchHelper enables incremental search across entry names.

### Keyboard map

| Key | Action |
|-----|--------|
| Up / Down | Navigate entries |
| Home / End | Jump to first / last entry |
| Enter | Re-announce current entry |
| Letters / Digits | Typeahead search |
| Backspace | Remove last typeahead character |
| Escape (search active) | Clear search, return to position announcement |
| Escape (no search) | Close the dialog |

### Announcement format

**Discovered entry:** `"{LabelCap}, {category LabelCap}. {description}. {linked things}. {position}"`

**Undiscovered entry:** `"UndiscoveredEntity (translated), {category LabelCap}. UndiscoveredEntityDesc (translated). {position}"`

### Translatable strings used

- `"EntityCodex".Translate()` — dialog title in open announcement
- `"UndiscoveredEntity".Translate()` — label for entries not yet discovered
- `"UndiscoveredEntityDesc".Translate()` — description placeholder for undiscovered entries
- `entry.LabelCap` / `entry.category.LabelCap` — def-sourced names (translated via XML)
- `entry.Description` — def-sourced description (translated via XML)

### Hardcoded English strings

- `"entries."` — entry count suffix in open announcement
- `"Entity Codex closed."` — close notification
- `"No matches for '...'"` — typeahead failure message

## Coverage notes

### Already covered by existing systems

| Feature | Handler |
|---------|---------|
| Void monolith investigate dialog (`Dialog_NodeTree`) | `WindowlessDialogState` |
| Void monolith activated dialog (`Dialog_NodeTree`) | `WindowlessDialogState` |
| Study Notes tab (`ITab_StudyNotesVoidMonolith`) | `TabRegistry` → `BasicInspectString` |
| Entity tab (`ITab_Entity`) | `TabRegistry` → `BasicInspectString` |
| Monolith gizmo navigation | `GizmoNavigationState` (generic) |

### Priority

`EntityCodexState` is registered in `UnifiedKeyboardPatch` at **priority 4.64**, between the options menu (4.6) and the research menu (4.7).
