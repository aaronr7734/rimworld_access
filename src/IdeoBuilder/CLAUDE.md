# IdeoBuilder Module

## Purpose
Keyboard accessibility for the custom ideoligion **builder** flow — creating, editing, reforming, and loading custom ideoligions. Mirrors vanilla's edit-mode UI (`Page_ConfigureIdeo` / `Page_ConfigureFluidIdeo`, `Dialog_ChooseMemes`, the precept add menus, `Dialog_ChooseIdeoSymbols`, `Dialog_EditDeity`, `Dialog_EditIdeoDescription`, `Dialog_ReformIdeo`, `Dialog_IdeoList_Load`).

Separate from `Ideology/`, which is the read-only in-game **viewer**.

## Entry points
- `MainMenu/IdeologySelectionPatch` (`Page_ChooseIdeoPreset`) launches Classic / Custom Fixed / Custom Fluid / Load / Presets. All are accessible — there is no longer an "Accessibility coming soon" gate.
- In-game reform: the viewer's Fluid-ideology section exposes a "Reform" node (when `development.CanReformNow`) that closes the viewer and opens `Dialog_ReformIdeo` (see `Ideology/IdeologyHelper.BuildFluidSection` + `IdeologyTreeNavigation.HandleActivate`).

## Architecture: light hub + dedicated editor states

`Page_ConfigureIdeo` (and its Fluid subclass, via inheritance) is presented as a **two-tab screen** mirroring vanilla's left-list / right-details layout (`IdeoBuilderHubState` owns both tabs):
- **Tab 1 — ideoligion list:** every ideoligion in `Find.IdeoManager.IdeosInViewOrder` (NPC factions' ideoligions exist here because world gen precedes the ideo page). Up/Down/Home/End + typeahead; the one being built is marked "yours".
- **Tab 2 — detail:** the **flat section editor** when the listed selection is our own ideo (each row shows a live value, Enter dispatches to a dedicated editor via `IdeoBuilderSectionActions.Activate(ideo, kind)`); the **read-only viewer** (the same `IdeologyTreeNavigation` tree as the in-game F12 → Ideology tab) for any other ideoligion.

`Tab`/`Shift+Tab` flip between the two tabs; arrow keys navigate within a tab. You never *leave* the editor by tabbing — you only flip the page. `Escape` is **not** tab navigation: in every tab it does the same thing (clear an active typeahead search, otherwise back out of the builder via the discard confirmation).

Editors are standalone states, reused by both the hub and the reform dialog:
- **Meme picker** — a real game `Dialog_ChooseMemes` window with its own patch (`IdeoMemeSelectionPatch`).
- **Windowless overlays** (no game window; live on top of the host screen): `IdeoPreceptSelectionState`, `IdeoTypedPreceptState`, `IdeoDeityListState`, `IdeoAppearanceEditState`. Routed through `IdeoBuilderOverlays`.
- **Text / picker editors** (`IdeoSymbolEditState`): name/adjective/member/worship/description via the modal `TextInputController`; styles/icon/color via `WindowlessFloatMenuState`.

The reform dialog (`IdeoReformState` / `IdeoReformPatch`) reuses all of the above on the reform's working-copy ideo.

## Files

| File | Role |
|------|------|
| `IdeoBuilderHelper.cs` | `HubSection` model + `SectionKind` enum, section builders, validation summary (mirrors `CanDoNext`), `ImpactOf` |
| `IdeoBuilderHubState.cs` | Hub state: section list, navigation, typeahead, activation, announcements. Also the **two-tab shell** — tab 1 is the list of all ideoligions (`IdeologyHelper.BuildIdeologyList`), tab 2 is the detail: the section editor for our own ideo, the read-only viewer (reused `IdeologyTreeNavigation`) for any other. Tab/Shift+Tab flip tabs; Escape is uniform (leave the builder) in every tab, never navigation |
| `IdeoBuilderHubPatch.cs` | `Page_ConfigureIdeo` patches: DoWindowContents (input), PostOpen (creates ideo for Custom Fixed), Close |
| `IdeoBuilderSectionActions.cs` | Shared "section → editor" dispatch (used by hub + reform stage 2) |
| `IdeoBuilderOverlays.cs` | Shared router for the windowless overlay editors + float-menu return refresh |
| `IdeoSectionEditorState.cs` | Host-independent section editor over a single `Ideo` (sections from `IdeoBuilderHelper.BuildSections`, Enter → `IdeoBuilderSectionActions.Activate`). Decoupled from any host (no list/viewer/staging), so any screen can make one ideoligion fully editable. Used by the Archonexus reform screen's detail tab for the player's created/loaded ideo. **Behaviorally identical to the hub's tab-2 editor**: Up/Down/Home/End + typeahead (with "X of Y matches" announce), Enter opens a section, Space re-announces, Alt+R randomizes all, `]` opens the context menu, opening announces overall impact. `IdeoSymbolEditState.AfterEdit` refreshes it when active |
| `IdeoEditorCommands.cs` | Editor-level commands shared by both editors so they can't drift: `RandomizeAll(ideo)`, `OpenContextMenu(ideo, onRandomizeAll)` (Save to file / Randomize all / Preview ritual sound), `SaveIdeoligion`, ritual-preview `ToggleRitualPreview`/`MaintainRitualPreview`. The hub (`IdeoBuilderHubState`) and the Archonexus editor (`IdeoSectionEditorState`) both call these; each supplies its own post-command refresh |
| `IdeoMemeSelectionHelper.cs` | Meme picker: reflection accessors, tree building (impact→group→meme / group→meme), labels, status line |
| `IdeoMemeSelectionState.cs` | Meme picker state: tree nav, selection toggle (mirrors vanilla DrawMeme rules), randomize/accept/back |
| `IdeoMemeSelectionPatch.cs` | `Dialog_ChooseMemes` patches |
| `IdeoPreceptSelectionHelper.cs` | Base precepts: issue enumeration, tree, value-picker options (with disable reasons), set/toggle |
| `IdeoPreceptSelectionState.cs` | Base-precept overlay: issue tree, Enter → value picker float menu |
| `IdeoTypedPreceptState.cs` | Roles/rituals/buildings/relics/weapons/animals/xenotypes/apparel: list + Add (reuses vanilla `AddPrecept` via reflection) + Delete + `]` edit menu. `]` mirrors `Dialog_EditPrecept` per type (rename/lock; leader title; role apparel requirements; relic stuff; building style; ritual anytime-or-date + reward) plus inline `EditFloatMenuOptions` (weapon swap, apparel gender/type, ritual-seat replace), all via `WindowlessFloatMenuState`. Page Up/Down jumps detail-section headers |
| `IdeoSymbolEditState.cs` | Name/adjective/member/worship/description (TextInputController) + styles/icon/color (float menus) |
| `IdeoDeityListState.cs` | Deity list: add/remove/regenerate + per-deity edit (name/title/gender) |
| `IdeoAppearanceEditState.cs` | Appearance editor (vanilla `Dialog_EditIdeoStyleItems`): tree of hair/beard/face-tattoo/body-tattoo → style categories → styles; Enter sets a style's frequency, `]` sets gender (hair/tattoo) or bulk frequency (category). Edits `ideo.style` live |
| `IdeoReformState.cs` | In-game reform: stage 1 (one-change memes/styles), stage 2 (section menu on working copy, incl. Appearance; announces impact + proactively flags incompatible precepts). `ReformActionMarker` is the viewer's entry hook |
| `IdeoReformPatch.cs` | `Dialog_ReformIdeo` patches |
| `IdeoLoadState.cs` / `IdeoLoadPatch.cs` | Saved-ideoligion picker (`Dialog_IdeoList_Load`): list, load, delete |

## Patching pattern

Builder screens during `ProgramState.Entry` (pre-game) have unreliable IMGUI focus, so each Page/Dialog's `DoWindowContents` is patched directly (the established world-gen pattern) rather than routing through `UnifiedKeyboardPatch`. Each Prefix:
1. Yields if `TextInputManager.Active`, `WindowlessFloatMenuState.IsActive`, `WindowlessDialogState.IsActive`, or `WindowlessConfirmationState.IsActive` (those own the keyboard).
2. Routes to an active overlay editor via `IdeoBuilderOverlays`, or to the host state.
3. Returns `true` so the original renders for sighted players.

`OnAcceptKeyPressed` / `OnCancelKeyPressed` are blocked (via prefix returning false) for our dialogs so Enter/Escape don't trigger vanilla's accept/cancel before our handlers run. For Pages, Enter/Escape are consumed in the DoWindowContents prefix to prevent `DoBottomButtons` from firing DoNext/DoBack; Alt+S/Escape invoke DoNext/DoBack via reflection.

## Reuse of vanilla logic
- Meme selection toggles re-implement `Dialog_ChooseMemes.DrawMeme`'s rules but call its private `CanUseMeme`/`CanRemoveMeme`/`TryAccept` via reflection.
- Adding typed precepts calls the private `IdeoUIUtility.AddPrecept`; its FloatMenu (and nested grouping menus) are redirected to `WindowlessFloatMenuState` by a case in `UI/DialogInterceptionPatch` gated on `IdeoTypedPreceptState.IsActive`.
- Precept availability uses `Ideo.CanAddPreceptAllFactions` / `IdeoUIUtility.CanListPrecept`; impact uses `IdeoImpactUtility`; reform apply uses `IdeoDevelopmentUtility.ConfirmChangesToIdeo`/`ApplyChangesToIdeo`.

## Localization
All labels/descriptions come from the game's translation keys or def labels. The few words the game has no key for (selection-state markers like "Selected"/"current"/"removed") are short English strings, consistent with existing mod usage.

## Dependencies
**Requires:** `ScreenReader/` (TolkHelper), `Input/` (KeyboardHelper, TextInput), `UI/` (MenuHelper, TypeaheadSearchHelper, TreeNavigationHelper, WindowlessFloatMenuState, WindowlessDialogState, DialogInterceptionPatch), `Inspection/` (InspectionTreeItem), `MainMenu/` (IdeologySelectionPatch), `Ideology/` (viewer reform entry hook).
