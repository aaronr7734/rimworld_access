# Anomaly Module

Screen reader accessibility for RimWorld's Anomaly DLC.

## Files

| File | Purpose |
|------|---------|
| `EntityCodexState.cs` | Navigation state for `Dialog_EntityCodex` — flat menu with typeahead search, category transitions, and Alt+I drill-in picker |
| `EntityCodexPatch.cs` | Harmony patches for `Dialog_EntityCodex` lifecycle (PostOpen, PostClose, OnCancelKeyPressed) |

## EntityCodexState

Handles keyboard navigation inside `Dialog_EntityCodex`, the in-game browsable codex of discovered entities.

### Architecture

Follows the State + Patch pattern. The state builds a flat list of all visible `EntityCodexEntryDef`
entries ordered by category (`listOrder`) then by `orderInCategory`. TypeaheadSearchHelper enables
incremental search across entry names.

### Keyboard map

| Key | Action |
|-----|--------|
| Up / Down | Navigate entries |
| Home / End | Jump to first / last entry |
| Enter | Re-announce current entry |
| Alt+I | Drill-in picker — opens linked things' info cards or jumps to research projects |
| Letters / Digits | Typeahead search |
| Backspace | Remove last typeahead character |
| Escape (search active) | Clear search, return to position announcement |
| Escape (no search) | Close the dialog |

### Announcement format

**On category transition:** `"{Category LabelCap}. {entry announcement}"` — when the user navigates
into an entry whose `category` differs from the previously announced one, the category label is
spoken first so blind users hear when they've crossed a section boundary (vanilla shows category
headers visually only).

**Discovered entry:** `"{LabelCap}, {category LabelCap}. {description}. {linked things}. {ResearchUnlocks: …}. {position}"`

**Undiscovered entry:** `"UndiscoveredEntity (translated), {category LabelCap}. UndiscoveredEntityDesc (translated). {position}"`

### Alt+I drill-in picker

Opens a `WindowlessFloatMenuState` containing:
- One entry per **discovered** linked thing → opens `Dialog_InfoCard` for the `ThingDef`
- One entry per linked research project (prefixed with `ResearchUnlocks` translation) → calls
  `WindowlessResearchMenuState.OpenAndSelectProject` to jump straight to the project in the menu

Undiscovered linked things are masked from the picker (matches vanilla, which redacts them in the UI).
If neither linked-thing nor research drill-ins are available, speaks `"None"`.

### Translatable strings used

- `"EntityCodex".Translate()` — dialog title in open announcement
- `"UndiscoveredEntity".Translate()` / `"UndiscoveredEntityDesc".Translate()`
- `"ResearchUnlocks".Translate()` — header for research project list
- `"None".Translate()` — when no drill-in targets exist
- `entry.LabelCap` / `entry.category.LabelCap` / `entry.Description` — def-sourced (translated via XML)

## Coverage notes

### Already covered by existing systems

| Feature | Handler |
|---------|---------|
| Void monolith investigate dialog (`Dialog_NodeTree`) | `WindowlessDialogState` |
| Void monolith activated dialog (`Dialog_NodeTree`) | `WindowlessDialogState` |
| Study Notes tab (`ITab_StudyNotesVoidMonolith`) | `TabRegistry` → `BasicInspectString` |
| Entity tab (`ITab_Entity`) | `EntityTabState` (Inspection module, registered in TabRegistry as Action) |
| Anomaly settings popup (`Dialog_AnomalySettings`) | `AnomalySettingsDialogState` (MainMenu module) |
| Custom-difficulty Anomaly section sliders | `DifficultySettingsHelper` (MainMenu module) |
| Monolith gizmo navigation | `GizmoNavigationState` (generic) |
| Psychic ritual dialog (`Dialog_BeginPsychicRitual`) | `LordJobDialogState` + `PsychicRitualAdapter` (Rituals module) |

### Priority

`EntityCodexState` is registered in `UnifiedKeyboardPatch` at **priority 4.64**, between the
`AnomalySettingsDialogState` (4.63) and the research menu (4.7).
