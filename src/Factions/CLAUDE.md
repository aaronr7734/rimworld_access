# Factions Module

## Purpose
Provides keyboard accessibility for the in-game Factions tab using a treeview.

## Files
- **FactionHelper.cs** - Shared utilities for building faction lists, announcements, and treeview nodes (used by both this module and MainMenu/FactionLandingState)
- **FactionTreeNavigation.cs** - Shared treeview navigation class holding tree state and keyboard handling (used by both FactionTabState and FactionLandingState)
- **FactionTabPatch.cs** - Harmony prefix on MainTabWindow_Factions.DoWindowContents (windowless pattern)
- **FactionTabState.cs** - Windowless keyboard navigation state, delegates to FactionTreeNavigation

## Tree Structure
Each faction is a collapsible node (level 0) whose label is the full announcement text. Children (level 1) are individual info sections. The description child is expandable into individual lines (level 2).

```
Root (hidden)
├── "Faction Name. Type. Leader..." [full announcement] (expandable)
│   ├── Outlander faction (type)
│   ├── Leader: Title: Name
│   ├── Ally, goodwill +35, natural goodwill +20
│   ├── Ongoing: ...
│   ├── Recent: ...
│   ├── Ideology: ...
│   ├── Enemy of: ...
│   └── "Full description text" (expandable if multi-line)
│       ├── Line 1
│       └── Line 2
```

## Key Shortcuts
- **Up/Down** - Navigate visible items (respects search matches)
- **Right** - Expand node, or move to first child if already expanded
- **Left** - Collapse node, or move to parent
- **Home/End** - First/last sibling at current level
- **Ctrl+Home/End** - Absolute first/last visible item
- **Enter** - Toggle expand/collapse on expandable nodes
- **Space** - Re-announce current item
- **\*** - Expand all siblings at current level
- **Alt+I** - Open info card for selected faction (walks up tree)
- **A-Z, 0-9** - Typeahead search on visible items
- **Backspace** - Delete last search character
- **Escape** - Clear search, then close

## Architecture
Follows the windowless ResearchMenuPatch pattern: Prefix on DoWindowContents opens our state, removes the RimWorld window, returns false. No RimWorld window remains in the stack.

FactionTreeNavigation is a non-static class that encapsulates tree state (root, visible items, selected index, typeahead) and all navigation logic. Both FactionTabState and FactionLandingState hold an instance and delegate keyboard handling to it.

## How to Open
F12 > Factions (via ExtraMenusState), which calls MainButtonDefOf.Factions.Worker.Activate().

## Dependencies
**Requires:** ScreenReader/, Input/, UI/ (MenuHelper, TypeaheadSearchHelper), Inspection/ (InspectionTreeItem)
**Shared by:** MainMenu/FactionLandingState (via FactionHelper and FactionTreeNavigation)

## Testing
- [ ] F12 > Factions opens accessible faction treeview
- [ ] Up/Down navigates collapsed factions with full announcements
- [ ] Right expands faction to show individual section children
- [ ] Left on child returns to parent faction node
- [ ] Left on faction collapses it
- [ ] Description child is expandable into individual lines
- [ ] Home/End navigate siblings, Ctrl+Home/End absolute
- [ ] Typeahead search works on visible items
- [ ] Alt+I opens info card from any tree depth
- [ ] Escape clears search, then closes
- [ ] Works when opened from world map
