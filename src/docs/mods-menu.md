# RimWorld Mods Menu - Complete Feature Analysis

## Overview

The mods menu (`Page_ModsConfig`) is a full-featured mod management interface with two-column layout, drag-and-drop reordering, search/filter, dependency management, and Steam Workshop integration.

**Key source files:**
- `decompiled/RimWorld/Page_ModsConfig.cs` (1303 lines) — Main UI
- `decompiled/Verse/ModMetaData.cs` — Per-mod metadata
- `decompiled/Verse/ModsConfig.cs` — Active mod list backend
- `decompiled/Verse/ModDependency.cs` / `ModIncompatibility.cs` / `ModRequirement.cs` — Dependency system

---

## Layout

```
┌─────────────────────────────────────────────────────────────────┐
│  [Search Widget (354px)]              [Auto-sort mods (150px)]  │
├──────────────┬──────────────┬───────────────────────────────────┤
│  "Disabled"  │  "Enabled"   │  Mod Details Panel                │
│  (250px)     │  (250px)     │                                   │
│              │              │  Preview Image                    │
│  - Mod A     │  1. Core     │  Mod Name (large)                │
│  - Mod B     │  2. Harmony  │  Author: ...                     │
│  - Mod C     │  3. Hugslib  │  ID: author.modname              │
│  ...         │  ...         │  Target Versions: 1.5 ✓          │
│              │              │  Mod Version: 2.1.0              │
│  [Downloading│              │                                   │
│   items...]  │              │  [More Actions]  [Enable/Disable] │
│              │              │                                   │
│              │              │  Requirements:                    │
│              │              │  ☐ Display fulfilled requirements │
│              │              │  - Depends on: HugsLib ✓          │
│              │              │  - Incompatible with: CE ✗        │
│              │              │                                   │
│              │              │  Description text...              │
├──────────────┴──────────────┴───────────────────────────────────┤
│  [Get Mods ▼]  [Unsubscribe ▼]  [Save/Load List ▼]    [Save]  │
└─────────────────────────────────────────────────────────────────┘
```

---

## All User Interactions

### Mouse Interactions
| Action | Behavior |
|--------|----------|
| Single click mod | Select mod, show details in right panel |
| Ctrl+Click | Toggle selection (multi-select) |
| Shift+Click | Range select from last selected to clicked |
| Double-click | Toggle mod active/inactive |
| Drag within active list | Reorder mod load order |
| Drag from inactive → active | Enable mod at drop position |
| Drag from active → inactive | Disable mod |
| Drag limit | Max 30 mods at once |

### Keyboard Interactions (Vanilla)
| Key | Behavior |
|-----|----------|
| Up/Down arrows | Move selection within current list |
| Shift+Up/Down | Extend selection |
| Type in search widget | Filter both lists in real-time |

### Buttons

**Top Bar:**
- **QuickSearchWidget** — Real-time search/filter (case-insensitive substring match on mod name). Filters both active and inactive lists independently.
- **"ResolveModOrder" / "Auto-sort mods"** — Calls `ModsConfig.TrySortMods()`. Builds a DAG from mod constraints, runs topological sort, detects cycles.

**Right Panel:**
- **"More Actions" button** — Opens a FloatMenu (context menu) with options depending on mod type:
  - **Workshop mods:** "Unsubscribe" (confirmation dialog), "WorkshopPage" (opens Steam), "ModFolder" (opens in file explorer)
  - **Local mods:** "ModWebsite" (opens URL if available), "ModFolder"
  - **Mods with settings:** "ModOptions" → opens `Dialog_ModSettings`
  - **Dev Mode + Workshop:** "UploadToSteamWorkshop" or "UpdateOnSteamWorkshop"
- **"Enable" / "Disable" toggle button** — Activates or deactivates the selected mod
- **"Display Fulfilled Requirements" checkbox** — Toggles showing satisfied requirements in the requirements section

**Requirements Section (per-mod, right panel):**
- Each requirement row is clickable:
  - **Uninstalled dependency:** Opens download URL / Workshop page
  - **Installed but inactive dependency:** Selects the mod in the list
  - **Active incompatibility:** Selects the conflicting mod
- Shows status icons: NotInstalled (gray), Installed (yellow), Resolved (green ✓), NotResolved (gray ✗)
- Shows ordering issue warning text if mod has load order conflicts

**Bottom Buttons:**
- **"GetMods"** — Submenu: links to Steam Workshop and Ludeon Forums
- **"UnsubscribeMultiple"** — Submenu:
  - All Selected Mods
  - All Incompatible Mods
  - All Disabled Mods
- **"SaveLoadList"** — Submenu:
  - Save Mod List → `Dialog_ModList_Save`
  - Load Mod List → `Dialog_ModList_Load`
- **"SaveModChanges" / "Save and apply changes"** — Bottom-right, 200x30px. Saves config, triggers restart if mods changed.

---

## Mod Details Panel Information

When a mod is selected, the right panel shows:
1. **Preview image** (if exists, from `About/Preview.png`, max 35% of panel height)
2. **Mod name** (GameFont.Medium)
3. **Author** (gray)
4. **Package ID** (gray, truncated with tooltip)
5. **Target versions** — Color-coded: green = compatible, red = incompatible
6. **Mod version** (if provided)
7. **Requirements section** (dependencies + incompatibilities with status)
8. **Ordering issue warning** (if applicable)
9. **Description** (scrollable, HTML-decoded)

---

## Warning & Error System

| Indicator | Condition | Display |
|-----------|-----------|---------|
| Yellow warning icon + yellow text | Version incompatibility (`VersionCompatible == false`) | Tooltip: "ModNotMadeForThisVersion" or "ModNotMadeForThisVersion_Newer" |
| Red error icon + red text | Conflicts/ordering issues (active mods only) | Tooltip: detailed error from `modWarningsCached` |

**Mod conflicts checked by `GetModWarnings()`:**
- Duplicate mod IDs active
- Incompatibilities with active mods
- Load order violations (loadBefore/loadAfter)
- Force load order violations
- Unsatisfied dependencies
- Cyclic dependencies (detected by TrySortMods)

---

## Load Order Rules

Enforced by `ReorderConflict()`:
1. **Core mod** (`ludeon.rimworld`) must load before all expansions
2. **Official expansions** must load after Core
3. **`forceLoadBefore`** — Hard constraint: mod must load before specified mods
4. **`forceLoadAfter`** — Hard constraint: mod must load after specified mods
5. **`loadBefore` / `loadAfter`** — Soft ordering hints (used by TrySortMods)

Error messages use translation keys: `ModReorderConflict_MustLoadBefore`, `ModReorderConflict_MustLoadAfter`

---

## Related Dialogs

### Dialog_ModSettings
- 900x700px window for per-mod configuration
- Calls the mod's own `DoSettingsWindowContents(Rect)` — content is entirely mod-defined
- Saves on close via `mod.WriteSettings()`

### Dialog_ModList_Save / Dialog_ModList_Load
- Save/load mod configurations (active mod list + names) to XML files
- Uses `GenFilePaths.AllModListFiles` for file listing
- Data structure: `ModList { ids: List<string>, names: List<string> }`

### Dialog_ModMismatch
- Shown when loading a save with different mods than currently active
- Three columns: Added (green), Missing (red), Shared
- Actions: Go Back, Save Mod List, Load Save's Mods, Load Anyway

### Dialog_ConfirmModUpload (Dev Mode)
- Confirmation before Steam Workshop upload
- "Tag as Translation" checkbox
- Validates mod metadata before allowing upload

---

## Steam Workshop Integration

- **Downloading items** appear in inactive list with "Downloading..." label and cancel button
- **Workshop notifications** refresh the mod list when items are subscribed/installed/unsubscribed
- **Unsubscribe** opens confirmation dialog, then calls `Workshop.Unsubscribe()`
- **Workshop page** opens Steam overlay to mod's Workshop page

---

## Exit Behavior

- If mods changed: shows "RestartFromChangedMods" confirmation dialog
- "Save and apply changes" saves config and may trigger game restart
- "Discard changes" reverts to previous mod configuration
- Unloads preview images on close

---

## Key Translation Strings

From `Core/Languages/English/Keyed/Menus_Main.xml`:
```
Mods, Mod, ResolveModOrder, SaveModChanges, DiscardModChanges,
ModReorderConflict_MustLoadBefore, ModReorderConflict_MustLoadAfter,
ModWebsite, ModFolder, ModOptions, ModTargetVersion, ModVersion,
ModPackageId, ModMustLoadBefore, ModMustLoadAfter, ModDependsOn,
ModIncompatibleWith, ModOrderingWarning, ModUnsatisfiedDependency,
GetMods, UnsubscribeMultiple, SaveLoadList,
AddedModsList, MissingModsList, SharedModsList,
ConfirmDisableCoreMod, ConfirmDeleteMod
```

From `Core/Languages/English/Keyed/Menu_Options.xml`:
```
ModSettings, SelectMod, NoConfigurableMods
```

---

## Current Accessibility Implementation

**Files:** `src/MainMenu/ModListState.cs` (~915 lines), `ModListPatch.cs`, `ModSettingsDialogPatch.cs`

### What's Currently Supported
- ✅ Two-column navigation (Up/Down, Left/Right to switch columns)
- ✅ Home/End navigation
- ✅ Typeahead search (type letters to jump to matching mod)
- ✅ Enter to toggle enable/disable
- ✅ Ctrl+Up/Down to reorder in active list
- ✅ Alt+M — Open mod settings
- ✅ Alt+I — Read full mod info (author, version, compatibility, warnings, description)
- ✅ Alt+S — Save mod changes
- ✅ Alt+R — Auto-sort mods
- ✅ Alt+O — Open mod folder
- ✅ Alt+W — Open Steam Workshop page
- ✅ Alt+U — Upload to Workshop (Dev Mode)
- ✅ Escape — Clear search / close dialog
- ✅ Screen reader announcements for selection, column switch, errors

### What's Missing
- ❌ **Real search/filter** — Only typeahead (jump to match). No persistent filter like vanilla's QuickSearchWidget that narrows both lists.
- ❌ **Mod requirements/dependencies browsable** — Alt+I reads them but can't navigate or interact with individual requirements
- ❌ **Multi-select** — No Ctrl/Shift selection support
- ❌ **Bottom action buttons** — Get Mods, Unsubscribe Multiple, Save/Load List menus not exposed
- ❌ **Downloading items** — Workshop download status not announced
- ❌ **Requirement interaction** — Can't click through to unmet dependencies
- ❌ **Visual highlight** — No visible selection indicator for sighted observers
- ❌ **Per-mod "More Actions" menu** — The context menu with Website/Folder/Options/Unsubscribe (partially covered by Alt+shortcuts but not as a unified menu)

### Architecture Note
ModListState handles input directly in `DoWindowContents_Prefix` — does **NOT** route through `UnifiedKeyboardPatch`. Uses 17 cached reflection fields/methods to access Page_ModsConfig internals.
