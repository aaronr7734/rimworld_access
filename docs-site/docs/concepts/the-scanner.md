# The scanner

The scanner finds and jumps to things on the current map or world map without hunting tile by tile. Looking for the nearest geyser, a dropped rifle, every patch of stony soil, or all the temperate forest tiles on the world? The scanner organizes what is out there into categories and lets you jump directly to any of it.

It works by drilling through levels: broad **categories**, then **subcategories**, then **item types** (ordered by distance), and finally **individual instances** of a type. **Home** jumps the cursor to whatever the scanner is currently pointing at.

## Key scheme

- **Ctrl+Page Up / Ctrl+Page Down**: move between **categories** (the broadest grouping).
- **Shift+Page Up / Shift+Page Down**: move between **subcategories** within the current category.
- **Page Up / Page Down**: move between **item types** within the current group, ordered by distance (nearest first).
- **Alt+Page Up / Alt+Page Down**: step through the **individual instances** of the current type, one at a time (for example, "grass, 1 of 39").
- **Home**: jump the cursor to the scanner's current target.
- **End**: announce the distance and direction from the cursor to the current target.
- **Alt+Home**: toggle scanner auto-jump mode (the cursor follows the scanner automatically as you step through items).

As you move through levels, the mod announces what you have landed on and how many there are. You might hear "12,977 grass tiles" at the type level, or "grass, 1 of 39" as you step through instances.

Not every category has subcategories. When one does not, work with categories, item types, and instances only.

## Clumps and the Home double-press

Many terrain types and items appear as contiguous patches rather than isolated tiles. The scanner groups these into a single **clump** entry and reports the tile count (for example, "fertile soil, 200 tiles").

Pressing **Home** once jumps the cursor to the tile in the clump nearest your current position. If you are now standing on the clump and it is large enough to have a distinct center, pressing **Home** a second time jumps to the clump's center. The mod offers the "press Home for center" hint only when a center jump is available.

## A typical flow

1. Use **Ctrl+Page Up/Down** to find the right category.
2. If the category has subcategories, narrow down with **Shift+Page Up/Down**.
3. Use **Page Up/Down** to pick the item type you want (nearest first).
4. Press **Home** to jump the cursor to it. If there are many instances of the type, use **Alt+Page Up/Down** to step to the specific one you want, then **Home**.

## Examples

**On the world map, finding biomes.** Move to the biomes category with Ctrl+Page Up/Down, then use Page Up/Down to reach temperate forest. The mod reports how many there are, and Alt+Page Up/Down steps through each patch. Home jumps the cursor to the current one so you can check its tile info. Roads and settlements are available as their own categories.

**On the colony map, finding terrain.** Looking for stony soil to build a foundation? Drill into the terrain category, pick the terrain type (sorted by distance, so the closest patch appears first), and press Home to land on it. Press Home again from within the patch to jump to its center.

**On the colony map, finding an item.** You dropped a weapon somewhere in the chaos. The scanner groups items into categories and types; find weapons, narrow to the rifle, and Home jumps the cursor straight to it.

## Pairing the scanner with search

Sometimes it is faster to name the thing you want. Press **Z** to open the map search, type what you are after, and press **Enter** to dismiss the search field. Matches become available as you type, so the scanner is then pointed at your results: step through them with the scanner keys and press **Home** to jump the cursor to one. Get in the habit of pressing Enter to close the field first, otherwise a later Enter just closes it when you meant to do something else.

## Where to go next

- [The map](the-map.md) covers cursor movement, terrain audio, the number-key tile info, and jump modes.
- [The world map and choosing a site](../starting/world-map-site.md) uses the scanner heavily when picking a landing spot.
