# The map

The map is your colony: a grid of tiles where most of the game takes place. A cursor moves over it with the arrow keys, terrain under the cursor plays a distinct sound, and number keys provide on-demand information about a tile. This page covers getting around the map and controlling game time. For finding and jumping to specific things, see [the scanner](the-scanner.md).

## Moving the cursor

The **arrow keys** move the cursor one tile at a time, with the camera following. Up, Down, Left, and Right work as you would expect.

As you move, the tile underfoot plays a terrain sound. Soil, stone, sand, water, and built flooring each have distinct audio cues, so with practice you can tell where you are by ear. Moving against a wall plays its own sound, which helps you trace room edges.

As you arrow around, the mod announces the name of what is under the cursor. Number keys are for deeper, on-demand detail rather than something you press after every step.

## What is on this tile? (number keys 1–7)

With the cursor on a tile, press a number key to hear a specific category of information about that tile. These are spot-checks; most of this information is also announced as you move.

- **1** = items and pawns at the cursor
- **2** = terrain (fertility, path cost, beauty, cleanliness)
- **3** = harvestable things (plants and their growth, and with the right DLC or equipment, fish and deep-scanner mineral targets)
- **4** = light (brightness) and temperature
- **5** = room stats
- **6** = power
- **7** = areas

On the world map, the same number keys report different categories. That mapping is covered on the [world map page](../starting/world-map-site.md).

## Jumping to coordinates (Ctrl+G)

Press **Ctrl+G** to open the coordinate-jump dialog. Type an X coordinate, press **Comma** or **Space** to advance to the Z field, type a Z coordinate, and press **Enter** to jump. Leaving a field blank keeps the current coordinate. Entering `+N` or `-N` moves that many tiles relative to the current position. **Escape** cancels.

## Jump modes: covering ground fast

Tile-by-tile movement is fine for close work, but crossing the map one step at a time is slow. Jump modes let a single keypress leap to the next thing in a given direction. Two jump modes are available:

- **Preset distance:** jumps a fixed number of tiles in the direction you choose.
- **Adjacent to wall:** jumps to the next tile adjacent to a wall in the chosen direction.

Controls:

- **Ctrl+arrow** jumps in that direction using the active mode.
- **Shift+Up / Shift+Down** cycle to the next or previous jump mode. The cursor does not move; the mod announces the new mode.
- **Shift+Left / Shift+Right** adjust the preset distance by 1.
- **Shift+Ctrl+Left / Shift+Ctrl+Right** adjust the preset distance by 10.

Jump modes are also used when sizing a wall or zone during building placement. See the [Architect menu page](../building/architect.md) for that flow.

## Controlling time

RimWorld runs in real time when unpaused. You will pause often to think and give orders.

- **Space** pauses and unpauses.
- **Shift+1** sets normal speed, **Shift+2** sets fast, **Shift+3** sets super-fast.
- **T** announces the current in-game time, date, weather, and season. Press it twice: if the time has not changed, the game is paused.
- **Alt+T** announces the current game speed and performance: speed name (Normal, Fast, etc.), actual and target ticks per second, and whether a threat is causing a forced slowdown.
- **?** (question mark) opens the **Learning Helper**, RimWorld's own built-in tips. Press **Enter** on a lesson to read it, **Down** to keep reading, choose "Mark as learned" and press **Enter** to dismiss a lesson. **Escape** closes the helper. These are the game's tutorial nudges, separate from this documentation.

## Where to go next

- [The scanner](the-scanner.md) finds and jumps to anything on the map: a stretch of stony soil, a dropped weapon, the nearest geyser.
- [Checking on pawns](checking-on-pawns.md) covers selecting colonists and reading their health, mood, needs, gear, and skills.
- [The Architect menu](../building/architect.md) is how you place buildings and zones.
