# World Module

## Purpose
Keyboard navigation for world map (F8 view and world gen starting site), settlement browsing, caravan formation/management, and biome descriptions.

## Files
**Patches:** WorldNavigationPatch.cs, CaravanFormationPatch.cs, MessageBoxAccessibilityPatch.cs
**States:** WorldNavigationState.cs, WorldScannerState.cs, SettlementBrowserState.cs, QuestLocationsBrowserState.cs, CaravanFormationState.cs, CaravanInspectState.cs
**Helpers:** WorldInfoHelper.cs, BiomeDescriptionTracker.cs

## Context System
WorldNavigationState supports two contexts via `WorldNavContext` enum:
- **InGame** - F8 world map during gameplay. Full feature set: caravans, route planner, settlement browser, quest locations.
- **WorldGen** - Starting site selection during game setup. Shared navigation + scanner + Z search + number keys. No caravans/quests. World-gen-specific features handled by `StartingSiteContext` (in MainMenu module).

Methods guarded with `if (context != WorldNavContext.InGame) return;`: CycleToNextCaravan, CycleToPreviousCaravan, FormCaravanAtSelectedSettlement, ShowCaravanInspect, GiveCaravanOrders, ToggleCaravanSelection, JumpToSelectedCaravans, JumpToNearestCaravan, OpenSettlementBrowser, OpenQuestLocationsBrowser.

## Biome Descriptions
`BiomeDescriptionTracker` tracks last announced biome. On tile change, returns `BiomeDef.description` if biome differs from last. Both contexts get biome descriptions. WorldGen context also gets faction proximity warnings and biome settle warnings before the description.

## Key Shortcuts
- **Arrow Keys** - Navigate world tiles (3D geographic compass)
- **Home** - Jump to scanner item (Alt+Home = home settlement)
- **End** - Read scanner distance (Alt+End = nearest caravan, in-game only)
- **Page Up/Down** - Scanner navigation (Ctrl=category, Shift=subcategory, Alt=instance)
- **Z** - Scanner search
- **1-5** - Categorized tile info
- **S** - Settlement browser (in-game only)
- **I** - Caravan inspect (in-game) / Info menu (world gen, via StartingSiteContext)
- **C** - Form caravan (in-game only)
- **]** - Caravan orders (in-game only)

## Architecture
WorldNavigationState tracks current world tile with `PlanetTile`. 3D geographic compass navigation (Vector3 math). `SyncSelectionWithGame()` syncs WorldSelector + WorldInterface + GameInitData. Settlement browser provides faction filtering.

## Dependencies
**Requires:** ScreenReader/, Input/, Map/ (cursor sync)
**Used by:** Quests/ (quest jump targets), MainMenu/ (StartingSitePatch + StartingSiteContext use WorldNavigationState)

## Localization
All speak calls in the State, Patch, and Helper layers route through `Languages/English/Keyed/RimWorldAccess_World.xml`. Tab labels reuse vanilla keys (`PawnsTab`, `ItemsTab`, `TravelSupplies`). Compass nouns reuse `RimWorldAccess.Map.Direction.Lower.*`. `WorldInfoHelper` keeps internal English compass tokens (north/east/...) for opposite-direction and priority dictionary lookups and only translates them at announcement time via a private `LocalizeCompass` helper. Two narrow English-game-data parsers remain: `ConvertToInfinitive` (turning the game's "Entering X" inspect string into an infinitive "enter X") and the "Traveling to " prefix extraction inside `GetDestinationSuffix`/`GetCaravanDestinationName`. Both fall through to localized fallback paths when the game data is non-English.

## Testing
- [ ] Arrow keys navigate world tiles (both contexts)
- [ ] Scanner works (both contexts)
- [ ] Z search works (both contexts)
- [ ] Number keys 1-5 work (both contexts)
- [ ] Biome descriptions announced on biome change (both contexts)
- [ ] Faction warnings before biome descriptions (world gen only)
- [ ] Settlement browser filters work (in-game only)
- [ ] Caravan formation keyboard nav works (in-game only)
- [ ] Caravan keys don't fire during world gen
