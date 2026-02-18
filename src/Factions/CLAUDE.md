# Factions Module

## Purpose
Provides keyboard accessibility for the in-game Factions tab.

## Files
- **FactionHelper.cs** - Shared utilities for building faction lists and announcements (used by both this module and MainMenu/FactionLandingState)
- **FactionTabPatch.cs** - Harmony prefix on MainTabWindow_Factions.DoWindowContents (windowless pattern)
- **FactionTabState.cs** - Windowless keyboard navigation state

## Key Shortcuts
- **Up/Down** - Navigate factions
- **Home/End** - Jump to first/last faction
- **Space** - Re-announce current faction
- **Alt+I** - Open info card for selected faction
- **Escape** - Clear search, then close
- **A-Z, 0-9** - Typeahead search by faction name
- **Backspace** - Delete last search character

## Architecture
Follows the windowless ResearchMenuPatch pattern: Prefix on DoWindowContents opens our state, removes the RimWorld window, returns false. No RimWorld window remains in the stack.

## Announcement Content
Each faction announces: name, defeated status, type, leader, relation kind, goodwill + natural goodwill, ongoing goodwill events (situations capping max goodwill), recent goodwill events (history changes), ideology name(s), enemy-of list.

## How to Open
F12 > Factions (via ExtraMenusState), which calls MainButtonDefOf.Factions.Worker.Activate().

## Dependencies
**Requires:** ScreenReader/, Input/, UI/ (MenuHelper, TypeaheadSearchHelper)
**Shared by:** MainMenu/FactionLandingState (via FactionHelper)

## Testing
- [ ] F12 > Factions opens accessible faction list
- [ ] All factions listed with correct info
- [ ] Ongoing/recent goodwill events announced
- [ ] Up/Down navigation with tick sound
- [ ] Home/End jump to first/last
- [ ] Typeahead search filters correctly
- [ ] Alt+I opens info card
- [ ] Escape closes and announces closure
- [ ] Works when opened from world map
