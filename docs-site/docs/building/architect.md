# The Architect menu

The Architect menu is where you issue every build, mine, chop, haul, and zone order on the map. RimWorld calls each orderable action a "designator." Walls, doors, stockpile zones, "chop this tree," and "expand the home area" are all designators, and they all live here.

## Opening the Architect menu

Press **Tab** to open the Architect menu.

It is a [tree view](../concepts/navigating-menus.md) organized by category: Structure, Zone, Production, Temperature, and so on. **Right arrow** expands a category, **Left arrow** collapses it, and **Up** and **Down** move between items.

You almost never need to crawl the whole tree. Type-ahead works here, so start typing what you want and the menu jumps to it. Type "wall" for walls, "stock" for stockpile zones, "chop" for the chop-wood order, "expand" for expand-home-area. Find the designator you want, then press **Enter** to start placing it.

## Designators are shape-based

Once you pick a designator, you place it using a shape. Most designators support more than one shape and start on a sensible default. For something physical like a wall or door, you choose the material first (see "Choosing a material" below), and then you pick the shape. Press **Tab** to move to the shape menu, then **Up** and **Down** to change shape, or type the shape name (for example "empty" for an empty rectangle) and press **Enter**.

Three shapes cover most work:

- **Filled rectangle.** You set two corners and it fills the whole interior. Use this for zones, like a stockpile covering a 12-by-12 area.
- **Empty rectangle.** Same two corners, but only the perimeter is placed. Use this for [walls](walls-doors-rooms.md), since you want the outline of a room, not a solid block.
- **Manual.** Place one object at a time. Use this for single items like a door.

## Placing a shape

The placement flow is the same every time: pick a starting point, pick an ending point, confirm.

1. Move your cursor to where the shape should begin and press **Space** to set the first point.
2. Move the cursor to the opposite corner. Use the arrow keys for one tile at a time, or jump modes (below) for precise distances.
3. Press **Space** again to set the second point.
4. Press **Enter** to confirm and place. The mod announces what was created, for example "12 by 12 stockpile zone created."

For a manual designator like a door, the flow is shorter: position the cursor, **Space** to place, **Enter** to confirm.

**Shift+Space** removes the last point you placed, or removes a blueprint when the cursor is over one.

### Jump modes for precise distances

Two jump modes are available during building:

**Preset distance** (the default) moves the cursor by a fixed number of tiles per **Ctrl+arrow** press.

- **Shift+Left** and **Shift+Right** decrease or increase the preset distance by 1.
- **Shift+Ctrl+Left** and **Shift+Ctrl+Right** decrease or increase it by 10.

So to lay out a 12-tile wall: set the preset distance to 12 with **Shift+Right** (pressing it until the mod announces 12), press **Space** to set the first point, then **Ctrl+Right** to jump 12 tiles east, **Space** again, and **Enter** to place.

The jump distance counts the starting tile, so a distance of 13 moves the cursor 14 tiles including the start. To enclose a 12-by-12 interior, set the jump distance to 13: the walls form a 14-by-14 exterior, and the two wall tiles on each side leave 12 by 12 of open floor inside.

**Adjacent to wall** jumps the cursor to the next wall-adjacent tile in that direction, which is handy when you want to pick up exactly where an existing structure ends.

**Shift+Up** and **Shift+Down** cycle through all available jump modes. These keys change the mode; they do not move the cursor.

For jump modes in other contexts (navigating the map outside of building), see [the map](../concepts/the-map.md).

### Building more than one shape at once

- Press **=** (equals) to add another shape to the current order. Already-placed shapes stay put, and you start positioning a new one. Repeat as needed, then **Enter** to confirm them all.
- Press **-** (minus) to remove the last shape you placed. If a rock or other obstacle blocks part of your placement, minus removes a shape, but you then press **=** (equals) to add one back. With zero shapes there is nothing left to confirm.

### Select all

Some designators, like Chop Wood or Mine, can apply to many targets at once. Press **Ctrl+A** to select all: the first press grabs everything in the current enclosure, a second press extends to the whole map. **Ctrl+Shift+A** steps back out one level if you went too far. Press **Enter** to confirm.

Inside a room, **Ctrl+A** selects its interior, which is useful when you want to zone or order everything inside a building without painting tiles one by one.

## Choosing a material

When you build something physical like a wall or door, the game presents a material list. You choose the material first, then the shape. Type to filter the list or arrow through it, then **Enter** to choose. Stone blocks and wood are good choices for walls and doors. Avoid steel for walls and doors: it is wasteful, and steel is better saved for machines and other components.

If the list says **None available**, you have no stored stock of that material. Selecting it is still valid: place the blueprint, and pawns will deliver the material once they gather or haul it. This lets you stage large projects: lay out the whole structure now, and construction proceeds as materials arrive. You can also pick a material you have on hand to start immediately.

## Related pages

- [Gizmos](../concepts/gizmos.md): the contextual commands on something you have already built.
- [The context menu](../concepts/context-menu.md): bulk actions on a designation type, like canceling every chop order at once.
- [Walls, doors, and rooms](walls-doors-rooms.md): how buildings become enclosed spaces.
- [Zones and stockpiles](zones-stockpiles.md): laying out and configuring storage.
