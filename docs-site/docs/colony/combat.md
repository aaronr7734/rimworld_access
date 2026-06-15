# Combat

When a raid shows up, a manhunter pack wanders in, or you want a colonist to put down a wounded animal, you take direct control of your pawns and tell them where to go and what to shoot. That direct control is called drafting, and this page covers how to do it from the keyboard: draft a pawn, move them, and order an attack.

RimWorld's combat runs in real time, so pausing with **Space** is essential. Pause, line up your orders, let time run, and watch what happens.

For the combat mechanics themselves (how shooting accuracy works, why cover matters, when a pawn goes down instead of dying), see the [Combat page on the RimWorld wiki](https://rimworldwiki.com/wiki/Combat).

## Drafting a pawn

Drafting takes a colonist off their normal work schedule and puts them under your direct command. While drafted, they stand where you tell them and attack what you point at instead of wandering off to haul rocks.

First [select the pawn](../concepts/checking-on-pawns.md) you want to control (comma and period cycle colonists; Alt plus a number jumps to a specific one). Then press **R** to draft. You will hear the pawn's name and the draft sound. Press **R** again to undraft, and the pawn returns to their schedule.

You can draft several pawns at once. Select a group (see [checking on pawns](../concepts/checking-on-pawns.md) for multi-select), then press **R**. The mod follows the game's own rule: it toggles every selected pawn that shares the first pawn's draft state, so one press drafts the stragglers. You will hear a summary like "Everyone drafted" or "Everyone except Dan drafted."

A few things to know:

- Only colonists who can be drafted respond to **R**. A pawn who is downed, in a mental break, or otherwise unable to fight will not draft.
- **R** only drafts when you are on the map with a pawn selected and no menu open. If a dialog is open, **R** does whatever that dialog uses it for.
- Drafting does not equip a weapon. A pawn fights with whatever they are already holding. To hand someone a weapon first, stand on the weapon's tile, select the pawn, and use the [context menu](../concepts/context-menu.md) to equip it.

See the [Drafting page on the RimWorld wiki](https://rimworldwiki.com/wiki/Drafting) for more on the game's drafting rules.

## Moving a drafted pawn

With a pawn drafted, move them by pointing the [map cursor](../concepts/the-map.md) at a destination tile and giving a "go here" order.

The thorough way is the [context menu](../concepts/context-menu.md). Move the cursor to the destination tile and press **`]`**. A menu opens listing what the pawn can do at that spot, with "Go here" usually at the top. Arrow to it and press **Enter**.

The fast way is **`[`** (left bracket). It fires the top option in that same menu immediately. Over an empty tile "Go here" is normally first, so **`[`** is a quick "walk there." You will hear the pawn's name and the order, for example "Dan: Go here." Be careful with **`[`** when an enemy is on the tile: the first option is then likely an attack order (fire at or melee), not "Go here," so the fast key may issue an attack you did not intend. With an enemy present, prefer the **`]`** menu so you can see which order is at the top.

Hold **Shift** when confirming an order to queue it instead of replacing the current one. You can tell a pawn to move to one spot, then Shift+confirm a move to a second spot, and they will do them in sequence. The mod adds "Queued" to the announcement. Queuing works from both the **`]`** menu (Shift+Enter on the option) and the **`[`** shortcut (Shift+**`[`**).

## Cover

Standing in the open during a firefight is how colonists die. Cover cuts the chance an incoming bullet connects, so where you park a drafted pawn is most of the fight.

Move the cursor onto a pawn and the mod reports how much cover whatever they are standing behind provides: either "no cover" or something like "behind sandbags (blocks 65%)." Scout a tile before confirming a move to see what cover it offers.

Cover comes from how solid the nearby object is (its fill percentage), not from one specific kind of thing:

- Sandbags and rock chunks are reliable cover.
- Walls fill the tile completely, so they give the most cover. Funneling enemies past a wall at a chokepoint is effective for this reason.
- Plants and other partial obstacles give partial cover: a saguaro cactus around 35%, trees around 25%, bushes around 20%.

The percentage the mod reads is a best case. Actual cover in a fight depends on where the enemy is standing relative to the object, so the same wall or sandbag protects against fire from one direction and not another.

The full cover rules (which objects count, how shooting angles interact with cover) live on the [Cover page on the RimWorld wiki](https://rimworldwiki.com/wiki/Cover).

## Ordering an attack

There are two ways to order an attack, depending on how much control you want.

### Quick attack from the context menu

Put the cursor on the enemy and press **`]`**. With a drafted pawn selected, the menu includes attack options such as "Attack [target]" for a ranged shot or a melee swing, depending on what the pawn is holding and how close they are. Arrow to the option you want and press **Enter**. Hold **Shift** to queue it after another order.

This is the order-and-forget approach: the pawn attacks that target and keeps attacking until it is down or out of reach.

Fire at and melee behave differently, and the difference decides where you have to stand your pawns:

- **Melee** makes the pawn run to the target and fight it in close. The pawn moves on its own to reach the enemy.
- **Fire at** makes the pawn stand still and shoot from where they are. The pawn will **not** move to get the enemy into range or into line of sight. If they cannot see the target or it is out of range, they do nothing. You position the pawn for cover and a clear line of fire yourself, then order the shot.

### Aimed targeting with gizmos

For more control, especially with a specific weapon or a psycast, use the [gizmo menu](../concepts/gizmos.md). Select a drafted pawn, press **G**, and arrow to the weapon's attack command. Press **Enter** to start targeting mode.

The mod hands the cursor back to you with a prompt such as "Bolt-action rifle attack. Use map navigation to select target, then press Enter." Move the [map cursor](../concepts/the-map.md) with the arrow keys to the tile you want, then press **Enter** to issue the order. Press **Escape** to cancel targeting without attacking.

While in targeting mode, press **R** to announce the distance from the pawn to the cursor and whether the target is in range, for example "Distance: 12 tiles, IN RANGE" or "Distance: 35 tiles, OUT OF RANGE (max 28)."

Targeting mode can aim at things the **`]`** order menu will not, including friendlies, walls, doors, and any other object. If you need to shoot a door open or fire on a friendly, this is the way to do it. Be deliberate about where you point it.

This targeting flow applies to ranged weapons, melee, mortars, and psycasts (Royalty). Whatever the source, you move the cursor and press Enter.

Some gizmos also carry their own shortcut key. When one does, the mod announces it right after the gizmo's name as you arrow through the **G** menu. You fire it with **Shift** plus that letter (for example, Shift plus the attack command's key starts targeting without opening the menu), because RimWorld Access keeps the plain letters for its own navigation. See [gizmos](../concepts/gizmos.md) for how this delegation works.

### Targeting stays open when a shot fails

In vanilla RimWorld, clicking an invalid target exits targeting mode immediately, and in vanilla you would simply click again. Getting dropped out of targeting on every failed attempt would be tedious.

When you press **Enter** on a target that does not work, the mod explains why and keeps targeting open. You will hear something like "Out of range. Distance: 35, max range: 28" or "No line of sight to target," and the cursor stays put so you can adjust and try again. When you land a valid target, the pawn takes the order and targeting closes.

## Reading the fight

A firefight is fast, and you cannot see the bullets. A few tools help:

- **Cover**: move the cursor onto a pawn and the mod reports how much cover whatever they are behind provides (described above).
- **Alt+H** reads a pawn's health: injuries, blood loss, whether they are down. Works on colonists, enemies, and animals, prioritizing whoever is under the cursor. Use it to tell whether a raider you have been shooting is actually taking damage.
- **Alt+B** reads the selected pawn's combat log: recent shots, hits, and misses involving them, grouped by battle.
- The [scanner](../concepts/the-scanner.md) finds hostiles on the map so you can locate the threat before it reaches you.

## Tips

- Ranged pawns do not automatically move into range or line of sight. If a pawn is not shooting, check that they can actually see the target from where they are standing.
- Guns can be used as melee weapons when the enemy is adjacent. The pawn will automatically switch to a melee attack at point-blank range.
- Melee pawns absorb fire and protect ranged colonists behind them. Placing melee fighters in front of shooters at a chokepoint concentrates fire while keeping your shooters out of reach.
- Letting enemies come to you through a narrow entrance is generally more effective than charging into an open field. Spread out pawns give enemies multiple targets to choose from; concentrated fire on a single enemy brings it down faster.
- Kiting (moving a colonist away while the enemy follows, buying time for other pawns to shoot) can turn a dangerous fight. A fast pawn can lead melee enemies around a corner while others fire. Movement speed decides who can kite whom: check a pawn's moving capacity with **Alt+H**, or open its [info card](../concepts/info-card.md) with **Alt+I** for the full move-speed stat.
- The difficulty setting affects how raids scale. Starting on a lower difficulty while learning combat is reasonable.

For the mechanics, the wiki is thorough:

- [Combat](https://rimworldwiki.com/wiki/Combat) for the overall system.
- [Cover](https://rimworldwiki.com/wiki/Cover) for positioning.
- [Weapons](https://rimworldwiki.com/wiki/Weapons) for what each gun and blade does.
- [Shooting](https://rimworldwiki.com/wiki/Shooting) and [Melee](https://rimworldwiki.com/wiki/Melee) skills determine how well a given pawn fights.

## Related pages

- [Checking on pawns](../concepts/checking-on-pawns.md) for selecting pawns and reading their health, mood, and skills.
- [The context menu](../concepts/context-menu.md) for the **`]`** order menu used to move and attack.
- [Gizmos](../concepts/gizmos.md) for the **G** menu where weapon and ability targeting lives.
- [The map](../concepts/the-map.md) for cursor movement, which is how every order is aimed.
- [Learning RimWorld](../reference/learn-rimworld.md) for tactics and the wider game.
