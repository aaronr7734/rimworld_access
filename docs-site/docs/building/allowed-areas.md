# Allowed areas

An allowed area is a part of the map you can tie a pawn or animal to, restricting where they will go. An allowed area is one kind of zone (the game groups areas, stockpiles, and growing zones together as zones), which is why you manage it from the Zone category of the [Architect menu](architect.md). You assign areas to pawns and pets from the [Animals menu](../colony/animals.md) (F4). To learn more, see [Allowed areas on the RimWorld wiki](https://rimworldwiki.com/wiki/Area).

The classic use: keep a pet out of the freezer while letting it roam everywhere else.

## Creating and expanding an area

Open the [Architect menu](architect.md) with **Tab**, then navigate to the Zone category. There you will find "Expand allowed area." Press **Enter** and choose which area you want to expand. From there, paint tiles onto the area the same way you place any designator: **Space** to set points, **Enter** to confirm. See [the Architect menu](architect.md) for the full placement flow.

If you have not made any areas yet, you will need to create one first (see Managing areas below).

## Managing areas

In the Zone menu, navigate to **Manage areas**. This is a flat menu of your areas. Press **`]`** (right bracket) to open the actions for one:

- **New**: create a new area and type a name for it.
- **Rename**: give an area a new name.
- **Expand**: add tiles to an area.
- **Shrink**: remove tiles from an area.
- **Invert**: flip the selection so every tile that was not in the area now is, and vice versa.
- **Copy**: duplicate an area.
- **Delete**: remove an area entirely.

The **`]`** menu announces each option as you arrow through it, the same as any [context menu](../concepts/context-menu.md).

## The invert-then-shrink pattern

To restrict a pet to the whole colony except one room, building the area tile by tile across the entire base would take a long time. The faster approach works backwards:

1. Make a new area (Manage areas, **`]`**, New, type a name).
2. **Invert** it. An empty area becomes the entire map in one step.
3. **Shrink** it, painting over the room you want to exclude.
4. Open the Animals menu with **F4** and assign the area to the animal.

The invert step is easy to forget on first use. If the concept is not clicking, see [Allowed areas on the RimWorld wiki](https://rimworldwiki.com/wiki/Area) for a fuller explanation.
