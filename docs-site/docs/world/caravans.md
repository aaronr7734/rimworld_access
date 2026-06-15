# Caravans

Caravans are groups of your colonists (and any pack animals and goods they carry) traveling across the [world map](world-map.md). Forming a caravan is one way to leave your colony: you also have [transport pods](transport-pods.md) and other options. Caravans let you explore, trade with other settlements, raid, and found new colonies.

## Forming a caravan

There are two ways to begin. From the world map, navigate to your settlement's tile, press **`]`** to open its orders, and choose to form a caravan. You can also use the **caravan hitching spot** from the Architect menu and form a caravan from there. Either way you land in the route-planning step.

Move with the **arrow keys** if you like, but most people **switch to the [scanner](../concepts/the-scanner.md), jump to the destination, and press Space to set a waypoint** there. Waypoint 1 is placed automatically at your settlement.

- Press **Space** to add a waypoint. The mod announces each one with the path and an estimated travel time, for example "waypoint 2 added, estimated travel time 0.2 days at average caravan speed."
- Press **Shift+Space** to remove the waypoint at the cursor.
- Press **E** to hear the estimated time of arrival.
- Press **Enter** to confirm the route and open the formation screen.
- Press **Escape** to cancel.

If you are not using auto-provision, set the single destination waypoint and press **Enter**. If you are using auto-provision, after the destination waypoint, return to your settlement and press **Space** to add another waypoint there. This does not make the caravan travel home; it tells the auto-provision system how long the round trip is so it can pack the right amount of supplies.

## The formation screen

The formation screen is where you choose who and what travels. **Left** and **Right** move between the tabs (pawns, items, supplies). Press **Tab** to reach the summary and info view, and Tab again to return.

### Choosing pawns and goods

On the pawns tab, **Space** or **Enter** selects a colonist or animal to bring. Animals with identical name, age, gender, and medical status are grouped into one entry; press **Enter** on a group to pick how many to take. Other animals each get their own slot.

On the items and supplies tabs, **Space** or **Enter** on an entry opens a quantity chooser. In the chooser, **Up** and **Down** change the amount by one, **Home** sets the minimum and **End** the maximum, or type a number directly; **Enter** confirms. Back on the list, **Shift+Enter** takes as many as you can carry within the remaining capacity. Pack animals such as alpacas and buffalo raise that capacity.

Load real food, medicine, and meals on the supplies tab. Colonists can forage in forests and shrublands, but rarely enough to feed the whole group, so pack food rather than relying on it.

### The summary view

Press **Tab** to reach the summary view. It shows the numbers that decide whether the trip succeeds: total mass, carry capacity, speed, food on hand, foraging rate, visibility, and more. Press **Alt+I** on most values to open the [info card](../concepts/info-card.md) for a breakdown.

## Auto-provision

Press **Alt+A** to toggle auto-provision. When it is on, the game fills the Supplies tab based on the planned route and travel time; the tab is locked while auto-provision is active. Press **Alt+A** again to unlock and adjust supplies yourself.

## Sending the caravan

Press **Alt+S** to send. A food-warning dialog may appear; confirm if the food you packed is sufficient for the trip. Your pawns then gather their gear and walk off the map.

## Caravans on the world map

### Selecting and inspecting

- **Comma / Period**: cycle between your caravans.
- **Enter**: on the tile where a caravan sits, inspects that caravan. From there you can inspect inventory, swap gear between pawns, or enter one of your own settlements on that tile.
- **Delete** on an item or pawn in the inspect view removes it. Removed items are destroyed. Removing a pawn from the colony is permanent, but it is not deletion: that pawn is not destroyed and may appear later in events, including raids.

### Following a traveling caravan

A caravan in transit has its path drawn on the world map, and the mod describes it for the selected caravan. On the caravan's own tile, you hear the direction it is heading. Move the cursor that way and the mod keeps describing the path ahead toward the destination, so you can trace the whole route step by step.

### Ordering a caravan

Select a caravan first with **Comma** or **Period**. Then press **`]`** on a destination tile to open the orders that caravan can carry out there: Travel to, Trade with, Attack, Settle, and so on. **`[`** fires the top option immediately. This is the same idea as **`]`** on your home map: the selected caravan is the actor, and the cursor tile is the target. See [the context menu](../concepts/context-menu.md) for the full model.

The caravan's own orders come from its gizmos. Press **G** on a selected caravan for Settle, Split, Rest, Merge, and others.

- **Settle**: founds a new colony or camp on the current tile.
- **Split**: opens a formation-style dialog to divide people and items into a second caravan.
- **Rest**: pauses travel to let the caravan recover. Caravans rest automatically at night, so this is mainly for emergency stops mid-day.

### Merging two caravans

1. Move both caravans to the same tile.
2. Select both: press **Ctrl+Space** on the first, press **Period** to move to the second, then press **Ctrl+Space** again.
3. Press **G** and choose **Merge**.

## Camping

When a caravan enters a tile without an existing map, you can stop and settle temporarily. The tile becomes a small colony map where your pawns can hunt, mine, and gather before moving on. This is mainly a way to acquire resources from a tile and carry them back, so it works best with a caravan that has high carrying capacity. It is also there for rest or shelter when a trip runs long.

## Getting home

Navigate to your colony's tile and press **Enter** to re-enter it, or press **F8** to leave the world map and return where you were.

## Moving between open maps

When you have more than one map open (your colony plus a camp or a second settlement), **Shift+Comma** and **Shift+Period** move between them. One exception: from a pocket map, such as one opened by a quest or event, you do not leave with Shift+Comma. The entry portal that brought you in has a gizmo to view the outer map instead. Press **G** on it.

## Related pages

- [World map](world-map.md) for world navigation and tile reading
- [The context menu](../concepts/context-menu.md) for tile orders: Travel, Trade, Attack, Settle
- [The info card](../concepts/info-card.md) for caravan stat breakdowns with Alt+I
- [Transport pods](transport-pods.md) for faster travel than walking
- [Trading](trading.md) for what happens when you reach a settlement
