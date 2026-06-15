# Walls, doors, and rooms

In RimWorld you do not pick a "bedroom" or a "kitchen" from a menu. You build walls and doors yourself, and the game figures out what the room is from what you put inside it. Drop a bed in a walled space and it becomes a bedroom. Put a stove in another and it becomes a kitchen. (A butcher table also belongs to food prep, but avoid putting stoves and butcher tables in the same room: the mess from butchering hurts the cleanliness that cooking depends on. Keep them separate.)

All of this is placed from the [Architect menu](architect.md).

## Building an enclosure

To make a room, build a connected enclosure of walls with at least one door. The game continuously checks enclosed spaces and labels them based on their contents.

The placement flow for walls:

1. Open the [Architect menu](architect.md) with **Tab** and type "wall" to jump to the wall designator. Press **Enter** to select it.
2. Choose your material from the list that appears. **Material is chosen first**, before you draw the shape.
3. If the list says **None available** for a material, you can still select it. Pawns will deliver the material when they have it. Choose a material you already have if you want construction to start immediately.
4. The shape selector defaults to **empty rectangle**, which is what you want for a room outline. Press **Tab** to change shape if needed.
5. Move your cursor to one corner of the intended room and press **Space**.
6. Move to the opposite corner. Use **Ctrl+arrow** to jump by the preset distance (set the distance with **Shift+Left** / **Shift+Right**) for precise placement.
7. Press **Space** to set the second corner, then **Enter** to confirm.

The mod announces the result, for example "14 by 14 wall blueprint placed."

**Shift+Space** removes the last placed point. If you have already confirmed a blueprint and your cursor is on it, **Shift+Space** removes that blueprint.

Do not choose steel or a single material by default if you have stone blocks available. Stone walls are stronger and do not burn; they are worth the extra cost for permanent structures. The choice depends on what materials are at hand.

## Filled vs. empty rectangles

The shape matters:

- **Empty rectangle** places only the perimeter. Use this for walls.
- **Filled rectangle** places the entire interior. Use this for zones and floors, not for walls (a filled rectangle of walls would solid-block the interior).

Press **Tab** while a designator is active to cycle through available shapes.

## Doors

A door replaces one wall cell. Build your walls first, then add doors where pawns need to pass. To place a door:

1. In the Architect menu, type "door" and press **Enter**.
2. Choose a material first, the same as with walls. Stone blocks or wood work well; avoid steel here too.
3. The shape defaults to **Manual** (one tile at a time), which is correct for a door. In manual mode, **Space** places one door per press, and placing over an existing wall replaces it.
4. Move the cursor to the wall cell where the door goes, press **Space**, then **Enter**.

There is a faster way. Move the cursor onto an existing wall, press **G** for its [gizmos](../concepts/gizmos.md), and turn that wall straight into a door. This skips the Architect menu entirely.

Colonists can open and close doors automatically as they pass. You need at least one door in any enclosed space, or colonists cannot enter or exit.

## Building over existing walls

You can run a build order straight over walls that are already placed. The game skips cells that already have the structure you asked for. If you specify a different material than what is already there, the game treats it as a replacement order and rebuilds in the new material.

## Roofs

Once you enclose a space with walls, the game roofs it automatically. A wall supports roof for up to 6 tiles out from itself. The practical limit for an unsupported room is roughly 12 tiles on the short side. A 14-by-14 outer footprint gives a 12-by-12 interior, which is close to the maximum for a single open room.

Only the short side is limited. A 12-by-50 hall roofs without issue, because no interior point is more than 6 tiles from a wall. For larger open spaces, add interior columns or short walls to give the roof additional support.

## Electricity and conduits

A generator powers machines near it. Power-consuming buildings do not need to be placed directly on a conduit: the game auto-connects any building to the nearest power transmitter within 6 tiles. To carry power further, run conduits from your generator outward. In practice, lay conduits as an unbroken run of adjacent tiles leading away from the generator, so the grid reaches wherever you need it.

**Hidden conduits** (in the Architect menu under Power) are built underground so they do not show as objects on the map. They cost 2 steel each, compared to 1 steel for a regular conduit. For permanent installations inside buildings, hidden conduits are the cleaner choice. For water crossings, waterproof conduits are available at a higher material cost.

## Selecting a room's interior

Press **Ctrl+A** while your cursor is inside a room to select the entire interior. This is useful for placing a zone or issuing an order across the whole inside of a building without painting each tile.

## Learn more

Room mechanics go deeper: roof support math, room roles, beauty, wealth, temperature, and cleanliness. See the [Walls](https://rimworldwiki.com/wiki/Walls) and [Room](https://rimworldwiki.com/wiki/Room) articles on the RimWorld wiki for the full picture.
