# The map

The map is your colony: a grid of tiles where most of the game takes place. A cursor moves over it with the arrow keys, terrain under the cursor plays a distinct sound, and number keys provide on-demand information about a tile. This page covers getting around the map and controlling game time. For finding and jumping to specific things, see [the scanner](the-scanner.md).

## Moving the cursor

The **arrow keys** move the cursor one tile at a time, with the camera following. Up, Down, Left, and Right work as you would expect.

As you move, the tile underfoot plays a terrain sound. Soil, sand, mud, marsh, rough and smooth rock, water, ice, lava, and wood, stone, metal, and carpeted floors each have their own sound, so with practice you can tell where you are by ear. Moving against a wall plays its own sound, which helps you trace room edges.

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

Tile-by-tile movement is fine for close work, but crossing the map one step at a time is slow. Jump modes let a single keypress leap to the next thing in a given direction. Four jump modes are available:

- **Preset distance:** jumps a fixed number of tiles in the direction you choose.
- **Impassable:** jumps to the last tile a colonist could walk to that way, stopping before a wall, a rock face, deep water, or anything else that would block a pawn. It also stops on a closed door, so you know you have reached one.
- **Terrain:** jumps to the next tile whose ground is different, so a stretch of sand or a paved floor crosses in one press.
- **Structure:** never lands on open ground. Standing on a building, it jumps to the last tile of that run, so a stretch of marble wall crosses in one press and stops on its far end. If a different building comes right after, it stops there instead. Press again from the end and it skips the open ground to the next building, which is also where it goes when you start on open ground.

Blueprints and frames count as the thing they will become, so a colony under construction navigates like the one being built.

Controls:

- **Ctrl+arrow** jumps in that direction using the active mode.
- **Shift+Up / Shift+Down** cycle to the next or previous jump mode. The cursor does not move; the mod announces the new mode. In the three scanning modes, where there is no distance to adjust, Shift+Left and Shift+Right repeat the mode name.
- **Shift+Left / Shift+Right** adjust the preset distance by 1.
- **Shift+Ctrl+Left / Shift+Ctrl+Right** adjust the preset distance by 10.

The scanning modes cover an unpredictable number of tiles, so they announce the distance before describing where the cursor landed, for example "7 tiles. Marble wall." When there is nothing to jump to, the mod says so and stays put: "blocked", "no terrain change that way", "no structure that way", or "map boundary".

No jump crosses into unexplored ground. A scan stops at the fog line, so a jump never tells you what is hiding in the dark.

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
