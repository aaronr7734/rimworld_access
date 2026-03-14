# Ideology Module

## Purpose
Provides keyboard accessibility for the in-game Ideology tab using a two-panel tabbed interface with tree view navigation.

## Files
- **IdeologyHelper.cs** - Shared utilities for building ideology lists, announcements, and treeview nodes
- **IdeologyTreeNavigation.cs** - Tree navigation class holding tree state and keyboard handling for the details panel
- **IdeologyTabPatch.cs** - Harmony prefix on MainTabWindow_Ideos.DoWindowContents (windowless pattern)
- **IdeologyTabState.cs** - Two-panel keyboard navigation state: ideology list + details tree

## Architecture

### Two-Panel Interface
- **Panel 0 (List):** Flat menu of all visible ideologies from `Find.IdeoManager.IdeosInViewOrder`. Up/Down navigation with typeahead search.
- **Panel 1 (Details):** Tree view of the selected ideology's information. Follows the Faction screen pattern where parent nodes aggregate child text.

Tab/Shift+Tab switches panels. Escape navigates backward: details → list → close.

### Tree Structure (Details Panel)
Matches vanilla's `IdeoUIUtility.DoIdeoDetails` section order:

```
Root (hidden)
├── Overview (name, adjective, member name, structure, culture)
├── Factions (which factions use this ideology)
├── Fluid Ideology (development points, earn methods — only if fluid)
├── Memes (non-structure memes with descriptions and impact)
├── Narrative (ideology description text)
├── Deities (name, type, gender, related meme — only if deity foundation)
├── Precepts (base precepts grouped by issue)
├── Roles (Precept_Role)
├── Rituals (Precept_Ritual)
├── Buildings (Precept_Building, Precept_RitualSeat)
├── Relics (Precept_Relic)
├── Weapons (Precept_Weapon)
├── Venerated Animals (Precept_Animal)
├── Preferred Xenotypes (Precept_Xenotype — Biotech only)
├── Preferred Apparel (Precept_Apparel)
└── Appearance (hair/beard + tattoo style counts)
```

Each section node's label contains the full aggregated text of its children. Expanding shows individual items. Multi-line content (precept tips, meme descriptions) creates expandable sub-nodes with line-by-line children.

## Key Shortcuts

### List Panel
- **Up/Down** - Navigate ideologies
- **Home/End** - First/last ideology
- **Tab/Enter** - Switch to details panel
- **Space** - Re-announce current ideology
- **A-Z, 0-9** - Typeahead search
- **Backspace** - Delete last search char
- **Escape** - Clear search, then close tab

### Details Panel
- **Up/Down** - Navigate visible items (respects search matches)
- **Right** - Expand node, or move to first child if already expanded
- **Left** - Collapse node, or move to parent
- **Home/End** - First/last sibling at current level
- **Ctrl+Home/End** - Absolute first/last visible item
- **Enter** - Toggle expand/collapse on expandable nodes
- **Alt+I** - Open info card for inspectable items (memes, animals, buildings, relics, apparel, weapons, xenotypes, abilities)
- **Space** - Re-announce current item
- **\*** - Expand all siblings at current level
- **Tab/Shift+Tab** - Return to list panel
- **A-Z, 0-9** - Typeahead search on visible items
- **Backspace** - Delete last search character
- **Escape** - Clear search, then return to list panel

## How to Open
F12 > Ideology (via ExtraMenusState), which calls `MainButtonDefOf.Ideos.Worker.Activate()`.

## Dependencies
**Requires:** ScreenReader/, Input/, UI/ (MenuHelper, TypeaheadSearchHelper), Inspection/ (InspectionTreeItem)

## Testing
- [ ] F12 > Ideology opens accessible ideology tab
- [ ] List panel shows all visible ideologies with faction info
- [ ] Up/Down navigates ideologies in list panel
- [ ] Tab switches to details panel, shows tree for selected ideology
- [ ] All sections present matching vanilla's UI order
- [ ] Parent nodes contain full aggregated child text
- [ ] Right expands sections to show individual children
- [ ] Left on child returns to parent section node
- [ ] Multi-line precept tips are expandable into individual lines
- [ ] Memes show impact level, description, and required precepts
- [ ] Deities section shows for deity-based ideologies
- [ ] Fluid ideology section shows development points
- [ ] Home/End navigate siblings, Ctrl+Home/End absolute
- [ ] Typeahead search works in both panels
- [ ] Escape from details returns to list panel
- [ ] Escape from list closes tab
- [ ] Changing ideology in list rebuilds details tree
- [ ] Works when opened from world map
