# Archonexus Module

## Purpose
Keyboard accessibility for the Archonexus relocation endgame — the multi-screen
"sell your colony and start fresh" finale fired by `QuestPart_NewColony`. Without
this module the player gets stuck on the very first screen and, even if they
could get past it, lands in an unescapable world-tile targeting mode at screen 3.

## The relocation chain

`QuestPart_NewColony.Notify_QuestSignalReceived` runs five screens in order:

1. **`Dialog_ChooseThingsForNewColony`** — pick what comes with you (5 colonists,
   5 animals, 1 relic, 7 items by default). Handled by `ArchonexusColonyState` /
   `ArchonexusColonyPatch`.
2. **`Screen_ArchonexusSettlementCinematics`** — auto-advancing transition
   cinematic. Handled by `ArchonexusCinematicPatch` (announces
   `SoldColonyDescription` once on open).
3. **`PickNewColonyTile`** → `Find.TilePicker` with `allowEscape: false`.
   Handled by `NewColonyTilePickState` / `NewColonyTilePickPatch`, routed in
   `UnifiedKeyboardPatch` at priority 0.367.
4. **`Dialog_ConfigureIdeo(forArchonexusRestart: true)`** — reconfigure the
   ideoligion for the new colony. Handled by `ArchonexusReformIdeoState` /
   `ArchonexusReformIdeoPatch`.
5. **`MoveColonyAndReset`** — long event with no UI; map navigation works after
   the new map loads. No mod work needed.

## Cycles and the victory finale

The Archonexus victory (`Script_EndGame_ArchonexusVictory.xml`) has **three cycles**, but
only the **first two relocate** — both inherit `QuestNode_Root_ArchonexusVictory_Cycle.PickNewColony`,
which adds the SAME `QuestPart_NewColony`, so the chain above covers both automatically (handlers
are keyed on dialog type, not phase). Only difference: **maxRelics is 1 (first cycle) vs 2 (second)**
— `ArchonexusColonyState` reads it via reflection and the relic labels switch singular/plural, so it
adapts with no special-casing.

The **third cycle does NOT relocate**. `QuestNode_Root_ArchonexusVictory_ThirdCycle` spawns a
`GrandArchotechStructure` (`Building_ArchonexusCore`) **site** you travel to and activate to win.
Activation is a right-click float-menu order, not a gizmo: select a colonist, put the map cursor on
the core, press `]` → the `FloatMenuOptionProvider_InvokeArchotech` "Invoke power" option appears in
the windowless float menu (our `]` handler uses `FloatMenuMakerMap.GetOptions`, which aggregates that
provider) → the colonist runs `JobDefOf.ActivateArchonexusCore` → `ArchonexusCountdown` →
`GameVictoryUtility.ShowCredits` → `Screen_Credits`. The credits screen is announced by
`MainMenu/CreditsAccessibilityPatch` (speaks the screen's own victory text records + its
"SkipCredits" button label — game strings only). Escape dismisses it (Screen_Credits keeps
`preventCameraMotion` true, suppressing the priority-8 pause-menu Escape, so Escape reaches the
screen's own `OnCancelKeyPressed` → skip → exit to main menu). So the finale is fully reachable.

## Files

| File | Role |
|------|------|
| `ArchonexusColonyState.cs` | Screen 1: navigable 4-section checklist (People / Animals / Relics / Items), reflection-driven against the dialog's own `selected` set + `AcceptanceReport` |
| `ArchonexusColonyPatch.cs` | Screen 1: DoWindowContents prefix + OnAccept/OnCancel/PostOpen/PostClose patches |
| `ArchonexusCinematicPatch.cs` | Screen 2: PostOpen postfix that speaks `SoldColonyDescription` |
| `NewColonyTilePickState.cs` | Screen 3: invokes TilePicker's validator + tileChosen via reflection; refuses Escape (vanilla `allowEscape:false`) |
| `NewColonyTilePickPatch.cs` | Screen 3: defined alongside the state — Postfix on `MoveColonyUtility.PickNewColonyTile` activates the state |
| `ArchonexusReformIdeoState.cs` | Screen 4: two-tab ideoligion screen — list (Create new / Create fluid / Load saved / all ideoligions / Assign colonists / Next) + a detail tab (Tab). The detail tab shows the **full section editor** (`IdeoSectionEditorState`) for the editable custom ideoligion (`customOrLoadedIdeo`) and the read-only `IdeologyTreeNavigation` viewer for every other ideoligion — exactly the one-ideo-editable rule vanilla's `DoIdeoListAndDetails(editMode: customOrLoadedIdeo != null, onlyEditIdeo: customOrLoadedIdeo)` enforces. Enter activates a row (make primary / create / load / assign / confirm); Alt+S confirms. Patch classes (`ArchonexusReformIdeoPatch*`) live here: DoWindowContents (also routes the `IdeoBuilderOverlays` editors + reclaims focus via `HostFocusReturn`) + OnAccept/OnCancel/PostOpen/PostClose |
| `ArchonexusConvertColonistsState.cs` | "Assign colonists" sub-dialog (`Dialog_ChooseColonistsForIdeo`): per-colonist list, Enter toggles convert-to-primary / revert, Escape closes. Writes the dialog's own `pawnConvertToIdeo` map so the reform's "Next" applies it |

## Key shortcuts

### Screen 1 — choose things for new colony
Tabbed UI: People / Animals / Relics / Items are four tabs that match
vanilla's draw order. Empty sections are dropped (vanilla `count > 0` gate).

| Key | Action |
|-----|--------|
| Left / Right (or Tab / Shift+Tab) | Switch tab (cycles, always wraps) |
| Up / Down / Home / End | Navigate within the current tab |
| Space / Enter | Toggle the current row's checkbox (both do the same, matching caravan formation) |
| Letters / digits | Typeahead by name **within the current tab** |
| Backspace | Trim typeahead |
| Alt+S | Send the colony off — attempt accept (gated by the game's `AcceptanceReport`) |
| Alt+H / M / N / G / K | Read the focused pawn's health / mood / needs / gear / skills (People & Animals tabs; reuses `CaravanInputHelper.HandlePawnInfoShortcuts`) |
| Alt+I | Open info card on the focused thing |
| T | Read full status (totals per category + acceptance reason) |
| Escape | Clear typeahead if active; otherwise cancel via vanilla (fires the questline's cancel callback) |

### Screen 3 — choose new colony site
| Key | Action |
|-----|--------|
| Arrows / PgUp / PgDn | Standard world navigation + scanner |
| Enter | Confirm via `TilePicker.validator` + `tileChosen` |
| Escape | Refused (the vanilla picker is `allowEscape:false`) |

### Screen 4 — reform ideoligion
Standard **two-tab** ideoligion screen (parity with `IdeoBuilderHubState`), NOT a flat list. The list
holds "Create new" / "Create new fluid" / "Load saved" action rows, every ideoligion, an "Assign
colonists" row (when colonists need converting), and a "Next" row.

| Key | Action |
|-----|--------|
| Up/Down/Home/End | Navigate the list |
| Letters/digits | Typeahead by name |
| Tab | Open the detail tab for the focused ideoligion. For the editable custom ideo (the one you created/loaded) this is the **full section editor** (`IdeoSectionEditorState`: name, memes, precepts, deities, appearance, etc. — Enter on a row opens its editor); for any other ideoligion it's the read-only `IdeologyTreeNavigation` viewer (memes/precepts/deities/description + **Alt+I** info cards). Tab/Escape returns to the list |
| Enter | Activate the focused row: make an ideoligion primary, or run Create / Load / Assign colonists / Next |
| Alt+S | Confirm — commit primary, apply conversions, proceed (the dialog's "Next") |
| Escape | Clears typeahead if active; otherwise refused (the dialog cannot be cancelled) |

Create/Load delegate to the game's own already-accessible `Dialog_ChooseMemes` / `Dialog_IdeoList_Load`.
"Assign colonists" opens `Dialog_ChooseColonistsForIdeo`, made accessible by `ArchonexusConvertColonistsState`
(Up/Down to browse colonists, Enter to toggle convert/revert, Escape to close).

**Detail-tab editor (`IdeoSectionEditorState`).** A host-independent section editor over a single
`Ideo`, reusing `IdeoBuilderHelper.BuildSections` + `IdeoBuilderSectionActions.Activate` (the same
machinery as the builder hub / in-game reform, but decoupled from any one host). The reform screen
opens it on the editable custom ideoligion so its name/memes/precepts/deities/appearance are fully
editable here, matching what a sighted player gets from vanilla's edit-mode details panel. Section
editors are the existing ones: `Dialog_ChooseMemes` (real window, tracked by `HostFocusReturn`), the
windowless overlays (precepts/typed-precepts/deities/appearance, routed via `IdeoBuilderOverlays`),
and `IdeoSymbolEditState` (text/float-menu edits — `AfterEdit` refreshes the active host, now
including `IdeoSectionEditorState`).

**Parity with the worldgen editor is the requirement, not a nice-to-have.** The editing/viewing
experience here must be byte-for-byte the same as the worldgen builder (`IdeoBuilderHubState`):
viewing reuses the same `IdeologyTreeNavigation`; editing reuses the same `IdeoSectionEditorState`
keys/announcements (Space re-announces, Enter activates, Alt+R randomizes, `]` context menu, typeahead
"X of Y matches", opening impact line). Randomize / save-to-file / ritual-sound preview live in the
shared `IdeoEditorCommands` so the two editors can't diverge. The reform patch calls
`IdeoEditorCommands.MaintainRitualPreview()` every frame (as the hub patch does) and steps
`ArchonexusOverlayOpen()` out for `WindowlessFloatMenuState`/`TextInputManager` so the `]` menu and the
save-name field route correctly. The only intentional differences are the outer shell (the quest adds
Create/Load/Assign-colonists/Next/make-primary because it is *choosing* the colony's ideoligion) and
that Escape leaves to the list rather than out of the builder.

## Architecture

### Input ownership and overlays
While an Archonexus screen is active, `UnifiedKeyboardPatch` returns early (an
EXCLUSIVE block) so leftover map/menu states from before the quest can't grab
keys — each screen drives input from its own `DoWindowContents` prefix instead.
That early return must *step aside* for overlays that own their own keyboard
handling: a windowless confirmation prompt (`WindowlessDialogState`/
`WindowlessConfirmationState` — the intercepted accept-confirmation) and the Alt+I
`Dialog_InfoCard`. The step-out is `ArchonexusOverlayOpen()`; the matching screen
prefix yields (without consuming) for the same overlays so it never double-processes
a key the overlay already handled. See the "Overlays own the keyboard" note below for
why the confirmation has no detectable window.

### Lingering states across the chain
The chain is entered by *accepting the quest from the (windowless) quest menu*, which —
like the vanilla Quests tab — does NOT close on accept, so `QuestMenuState.IsActive`
stays true through every screen. Screens 1 and 4 are shielded by the EXCLUSIVE block, but
the world-tile pick (Screen 3) is not: its arrow navigation comes from `WorldNavigationState`,
whose handler is gated `!QuestMenuState.IsActive`, so a lingering quest menu sends arrows to
the quest list instead of the map (Escape still hits the tile picker → "must choose a valid
tile" — the tell). Fix: `ArchonexusColonyState.EnsureOpen` (head of the chain) calls
`QuestMenuState.Close(announce: false)` once. Symmetrically, `NewColonyTilePickState` closes
itself the instant a tile is committed (in `ConfirmCurrentTile`) rather than lingering until
the next keystroke on Screen 4 / the new map. General lesson: a state reached *through* the
relocation chain must not assume the screen it was launched from has been torn down.

### Limits and validation (Screen 1)
The state never does its own limit math. Toggling is always allowed (mirrors
vanilla), and Accept is the only gate — it reads the dialog's private
`AcceptanceReport` property via reflection and speaks the red reason on
rejection. Item stack counts are read from the dialog's
`itemArchonexusAllowedStackCount` dictionary; distinct items (quality
weapons/apparel/guns/utility) use the full `Thing.stackCount`. This makes it
structurally impossible to relocate with more than the allowed number of any
category.

### Escape discipline
- Screen 1: vanilla `OnCancelKeyPressed` is only intercepted when a typeahead
  search is active (to clear it). Otherwise vanilla closes the dialog and fires
  the questline's `outSignalCancelled`. This matches the lesson from the ideo
  loader: never disable the game's own escape hatch without keeping a fallback.
- Screen 3: cannot cancel — vanilla sets `allowEscape:false`. Escape is gracefully
  refused with an announcement.
- Screen 4: cannot cancel — `closeOnCancel:false`, `openMenuOnCancel:true` would
  pop the in-game menu. Escape is refused; Enter is the only proceed.

### Focus
Each Window patch's `PostOpen` postfix calls `Notify_ManuallySetFocus`, matching
the pattern from `IdeoBuilder` and `IdeoLoad` — keyboard events only reach the
GUI-focused window, and dialogs that open mid-keystroke can fail to grab focus
on their own.

**Important:** `Dialog_ChooseThingsForNewColony` overrides `PostOpen` and does
NOT call `base.PostOpen()`, so a patch on `Window.PostOpen` never fires for it.
We patch the dialog's own `PostOpen` override directly. Without this fix the
dialog re-opened cleanly the first time (Unity's IMGUI auto-focused the new
window) but went unresponsive after Escape + re-accept-quest because no path
re-grabbed focus on the second open.

**Info card return:** opening the Alt+I info card (a real `Dialog_InfoCard`, which
`DialogInterceptionPatch` explicitly does NOT intercept) moves GUI focus off Screen 1;
when it closes, RimWorld's deferred `GUI.FocusWindow` does not reliably restore focus
to a directly-patched host, leaving the screen dead to the keyboard. The patch holds a
`HostFocusReturn` (shared helper with the IdeoBuilder screens) and calls `Track` every
frame from `DoWindowContents`, reclaiming focus the frame the card closes — the reclaim
only takes effect from inside the host's own GUI pass.

### Overlays own the keyboard (the confirmation prompt)
The accept-confirmation is a `Dialog_MessageBox`, but `DialogInterceptionPatch` intercepts
**every** `Dialog_MessageBox` on `WindowStack.Add` and converts it into a windowless
`WindowlessDialogState` — the real message-box window is never added. So there is no
message-box *window* to detect; the only signal is `WindowlessDialogState.IsActive`.

Two gates must honor that, or the prompt opens but arrow keys keep cycling Screen 1's
tabs underneath (the box appears unreachable):
- `UnifiedKeyboardPatch`'s EXCLUSIVE early-return for Archonexus screens steps out when
  `ArchonexusOverlayOpen()` is true, which includes `WindowlessDialogState`/
  `WindowlessConfirmationState`, so the windowless dialog's own handler (priority -0.x)
  gets the keys.
- The screen's `DoWindowContents` prefix yields (without consuming) for the same states.

This mirrors the IdeoBuilder pattern (yield for `WindowlessDialogState`/
`WindowlessConfirmationState`/`WindowlessFloatMenuState`/`TextInputManager.Active`).

**`ArchonexusOverlayOpen()` must step out for everything UnifiedKeyboardPatch routes, not
just confirmations.** Screen 4's section editor (`IdeoSectionEditorState`) opens editors
that are handled by UnifiedKeyboardPatch handlers living *after* the EXCLUSIVE early-return:
a **windowless float menu** (precept value picker, style/icon/color, deity actions — priority
-5 handler) and the **modal text controller** (`TextInputManager` — name/adjective/description,
priority -1.6 dispatch). If `ArchonexusOverlayOpen()` doesn't include `WindowlessFloatMenuState`
and `TextInputManager.IsActive`, the early-return swallows their keys and they lock up (this
was the reform-precept lockup). Note the asymmetry: the section editor's *own* overlays
(`IdeoBuilderOverlays` — the issue tree, typed-precept list, deity list, appearance tree) are
routed by the reform `DoWindowContents` prefix itself, which runs independently of the
early-return, so they must NOT be in `ArchonexusOverlayOpen()` (stepping out for them would let
UnifiedKeyboardPatch's regular chain double-process). Only the float menu / text controller they
spawn — which UnifiedKeyboardPatch owns — need the step-out.

## Localization
All player-facing text routes through the game's translation keys —
`ChooseThingsForNewColonyTitle`, `ChooseThingsForNewColonyDesc`,
`ChoosePeopleDesc`, `ChooseThingsDesc`, `MessageNewColonyMax`,
`MessageNewColoyRequiresOneColonist` (vanilla typo "Coloy"),
`ChooseOnlySlavesInfo`, `ItemQuantityTooltip`, `ChooseNextColonySite`,
`SoldColonyDescription`, `ConfigureIdeoligion`, plus the standard
`People/AnimalsLower/RelicLower/RelicsLower/ItemsLower` particles. Short English
hint strings ("You must choose a valid tile to continue.", etc.) follow the same
convention the rest of the codebase uses where the game has no equivalent key.

## Screen 4 parity note

Screen 4 is **full parity**, not an MVP — it exposes everything the vanilla
`DoIdeoListAndDetails` offers: browse/pick any ideoligion, Create new / Create
fluid (→ `Dialog_ChooseMemes`), Load saved (→ `Dialog_IdeoList_Load`), per-pawn
conversion (→ `Dialog_ChooseColonistsForIdeo`), typeahead, and confirm. The detail
tab honors vanilla's one-ideo-editable rule: the created/loaded custom ideoligion
gets the **full section editor** (`IdeoSectionEditorState` — name, memes, precepts,
deities, appearance, the lot), every other ideoligion gets the read-only viewer with
info cards. This was originally a read-only-only miss (the editor wasn't reachable
here); now editing the custom ideo in this screen matches the builder.

### Focus return is the recurring lockup
The reform screen opens several windows over itself — meme picker, load picker,
per-pawn conversion, info card — plus windowless overlay editors. Each real window
steals IMGUI keyboard focus; when it closes, focus is NOT automatically returned to
a directly-patched host, so the screen goes dead to the keyboard (the meme-selection
lockup). The fix is the shared `HostFocusReturn` (`onChildClosed` overload): it
reclaims focus the frame the last tracked child closes — from the host's own
`DoWindowContents` pass, the only GUI context where `GUI.FocusWindow` takes — and
re-announces where the cursor landed (context-aware: list row, viewer item, or
section-editor row). Windowless overlays carry no real window, so they're tracked
separately (`overlayWasOpen`) and refresh the section editor on return. This is the
same pattern `IdeoBuilderHubPatch` / `IdeoReformPatch` use; when a future screen opens
child windows over a directly-patched host, reach for `HostFocusReturn` first.

## Dependencies
**Requires:** ScreenReader/, Input/, IdeoBuilder/ (uses `Ideo` types), World/
(WorldNavigationState for Screen 3), UI/ (MenuHelper, TypeaheadSearchHelper),
Ideology/ (the reused `IdeologyTreeNavigation` read-only viewer)
**Used by:** None (top-level relocation flow)
