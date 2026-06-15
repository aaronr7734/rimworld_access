# The Animals tab

The Animals tab is where you manage every tamed creature in the colony: who trains them, what they are allowed to do, where they can go, and how many of each species you want around. Press **F4** to open it.

With the **Biotech** DLC and mechanoids in your colony, **F4** may open the **Mechs** tab instead. If you have both tamed animals and mechs, **F4** first shows a chooser. The two tabs are nearly identical tables with slightly different columns, so everything below applies to both.

## The table

Each animal is a row, and the columns are its settings. Sorting, painting a value down a column, and moving around all work the way they do in any table. See [navigating menus](../concepts/navigating-menus.md) for the full set. The short version:

- **Alt+S** sorts by the column you are on. Press again to toggle descending, then clear.
- **Shift+Up/Down** paints the current cell's value onto the rows above or below.
- **Shift+Home/End** paints from your row to the top or bottom of the column.
- **Ctrl+Shift+Home/End** paints the value across every row at once.
- **Enter** or **Space** toggles a checkbox cell.

## Training

Many columns represent trainable skills you turn on with a checkbox. Checking one tells your handlers to begin training that animal. Training takes time and depends on your handler's Animals skill.

The standard training categories are:

- **Tameness** (keeping the animal from reverting to wild)
- **Guard** (the animal follows a master and stays near them; this is the prerequisite for the other training types)
- **Attack** (the animal can be released to attack distant targets instead of only guarding)
- **Rescue** (the animal retrieves downed colonists)
- **Haul** (the animal carries items)

### How attack training actually behaves

By default, an animal trained to guard and assigned a master will follow that master and **guard** them: it attacks foes that come close, but it does not go looking for enemies. Guard mode is usually the safer choice, because an animal that charges into a firefight can catch your own gunfire.

Once an animal has learned **Attack** as well, has a master assigned, and is set to follow, a gizmo appears on the **master** (not on the animal) when that master is drafted. The gizmo toggles between guarding and being released to go attack, so you can decide whether the animals hold their position or charge the enemy. To reach it, select the master, draft them, and press **G** to open their gizmos. See [gizmos](../concepts/gizmos.md) for how to navigate gizmo commands.

With the **Odyssey** DLC, an additional **attack target** training type lets you point attack-trained animals at one specific target. When trained, a separate gizmo appears on the drafted master; activate it and press **Enter** to pick the target the animals should attack.

## Hunting and the Animals tab

Hunting is designated in [the Wildlife tab](wildlife.md), not here. When a hunter kills an animal, they collect meat and may collect other resources such as hides, wool, or leather depending on the species. Not every animal yields leather.

## The auto-slaughter screen

Auto-slaughter lets you set population caps per species. Whenever your count exceeds a cap, the game automatically slaughters the surplus until you are back under the limit. You set it once and the colony manages its own herd sizes without ongoing micromanagement.

The screen lists every animal species in the game, including species you do not yet own. You can pre-set caps before you have a single animal of that type.

### Setting a cap

Find the species you want to limit. For each numeric cell, press **Enter** to type a value directly, or nudge it one at a time with **=** to raise and **-** to lower. The columns are:

- **Maximum population**: the total of that species you will keep.
- **Maximum males**
- **Maximum young males**
- **Maximum females**
- **Maximum young females**

The per-sex columns let you keep plenty of breeding females while capping surplus males. Leave a cap blank and that count is not limited.

### Pregnant and bonded animals

Two checkboxes determine whether certain animals are spared from auto-slaughter:

- **Allow pregnant slaughter**: off by default. Pregnant females are protected until you turn this on. The screen shows how many are currently pregnant.
- **Allow bonded slaughter**: same idea for animals that have bonded with a colonist. Enabling this when a bonded animal is over the cap will cause a mood hit when that colonist loses their companion.

### Who gets slaughtered

When you are over a cap, the game removes the **oldest animals first** and works down until you are back within the limit. This means auto-slaughter trims the elderly before the young, which generally makes sense for a herd you are managing for meat or breeding.

<!-- source: decompiled/Verse/AutoSlaughterManager.cs:94 - SortByDescending(a => a.ageTracker.AgeBiologicalTicks), then PopFront() at :109,118,127,136,147 -->

## Learn more

Bonding, training mechanics, and which animals are worth keeping are covered on the [RimWorld wiki's Animals page](https://rimworldwiki.com/wiki/Animals).

## Related pages

- [The Wildlife tab](wildlife.md) is for untamed animals: marking creatures to hunt or tame.
- [Navigating menus](../concepts/navigating-menus.md) covers the full table mechanics including sorting, painting, and type-ahead.
- [Gizmos](../concepts/gizmos.md) explains how to navigate and use the command buttons on a selected animal.
