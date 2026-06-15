# Prisoners and wardens

Prisoners are people you have captured: raiders you knocked out, downed visitors, anyone you would rather hold than kill. You keep them to recruit into the colony, to ransom or release, or for other purposes. This page covers how to take a prisoner, where to put one, and how to manage them.

If you are new to the game's prisoner rules, the wiki's [Prisoner page](https://rimworldwiki.com/wiki/Prisoner) is the full reference.

## You need a prison first

Build a cell before the fight. A prison is a normal room with a bed set for prisoners. There is no special "prison" building.

Build it the same way you build any room: walls, a door, a roof, and a bed inside. See [walls, doors, and rooms](../building/walls-doors-rooms.md) for the room basics and [the Architect menu](../building/architect.md) for placing the bed.

To mark a bed as a prison bed:

1. Move the cursor onto the bed.
2. Press **G** to open the bed's [gizmos](../concepts/gizmos.md).
3. Find the bed ownership gizmo and open it with **Enter**.

The bed has a **dropdown** that lets you choose whether beds in this room are used by colonists, prisoners, or slaves. Set it to **Prisoners** to make it a prison bed. With the Ideology DLC active, the Slaves option also appears in the dropdown; without Ideology, the choice is a simpler colonists/prisoners toggle. Once a bed is set for prisoners, the room it sits in becomes a prison.

A prisoner will not share a cell with a non-prisoner, and the game refuses the setting if the bed has no roof above it. The wiki explains what makes a good cell on its [Prison section](https://rimworldwiki.com/wiki/Prisoner#Prisons).

## Capturing a downed pawn

Capturing goes through the [context menu](../concepts/context-menu.md).

1. Select a colonist who can do the carrying (comma/period to cycle colonists, or pick one another way).
2. Move the cursor onto the downed pawn you want to capture.
3. Press **`]`**.
4. Arrow to the **Capture** option and press **Enter**.

Your colonist picks the pawn up and hauls them to an open prison bed. If there is no free prison bed, the capture is refused and the game says so. Build a cell first.

The option itself tells you useful things as you arrow onto it: if the target can never be recruited, it is flagged "unrecruitable," and capturing a member of a non-hostile faction warns you it will damage relations with them. The wiki covers [capturing](https://rimworldwiki.com/wiki/Prisoner#Capturing_prisoners) in more detail.

## Opening the prisoner tab

Once someone is in a cell, open their inspection tree: move the cursor onto them and press **Enter**, or select them and press **Ctrl+Alt+Enter**. In the inspection tree, start typing "Prisoner" (or "Slave" for an enslaved pawn) to jump to that node, then press **Enter** to open the prisoner tab.

<!-- source: verified §4 in research/rewrite/04-verified-facts.md - pressing P on a selected pawn from the map does nothing; the correct flow is Enter to open the inspection tree then typeahead to the Prisoner node -->

The tab opens with the pawn's identity, their current interaction mode, and their medical care setting. Press **Escape** to close it.

## Moving around the prisoner tab

The tab is split into sections. Move between sections with **Left** and **Right** arrows, and move within a section with **Up** and **Down**.

- **Information**: read-only stats. Arrow down to hear recruitment resistance, prison-break chance, market value, what releasing them would do to faction relations, and more. With Ideology active you also hear their will level.
- **Medical care**: a radio button for the care level, from no care up through the best available medicine. Arrow to the level you want and press **Enter** to select it. This controls how much medicine your doctors spend treating this prisoner.
- **Interaction modes**: the main choices, a separate set of radio buttons. Arrow to the one you want and press **Enter** to select it. Only one is active at a time.
- **Non-exclusive modes**: extra options that layer on top of the interaction mode, handled as checkboxes. Press **Space** to toggle the current one.

As you arrow through interaction modes, each one reads its name, whether it is currently selected, and a description pulled from the game in your language.

## The interaction modes

These are the orders you give your wardens. The exact list adjusts to your situation and DLCs, but the common ones are:

- **Reduce resistance**: wardens talk the prisoner down until their resistance reaches zero, with no recruitment attempt.
- **Recruit**: wardens chip away at resistance and, once it is low enough, try to recruit the prisoner into the colony.
- **Release**: let them go. The Information section tells you ahead of time whether releasing improves relations with their faction.
- **Reduce will** and **Enslave** (Ideology): break a prisoner's will and turn them into a slave.
- **Execute**: have a warden kill the prisoner. The option description warns you if no warden on the map is both assigned to wardening and able to do violence, which is the usual reason an execution order sits there doing nothing.
- **Convert** (Ideology): wardens work to convert the prisoner to one of your ideoligions. If you run more than one ideoligion, choosing Convert opens a short list to pick which one; arrow to it and press **Enter**, or press **Escape** to back out. The tab warns you if no warden follows that ideoligion, since then nobody can do the converting. See [factions and ideology](factions-ideology.md) for more on ideoligions.

To learn more, see [Recruitment](https://rimworldwiki.com/wiki/Recruit) and [Resistance](https://rimworldwiki.com/wiki/Prisoner#Resistance) on the wiki.

## Who does the work: the Warden

Nothing in the prisoner tab happens on its own. A colonist with the **Warden** work type does it: feeding prisoners, chatting to reduce resistance, recruiting, converting, enslaving, and executing. No warden, no progress.

Warden is a Social-skill job, so a colonist with high Social makes a far better recruiter. Enable it for at least one colonist in the [Work tab](work.md), and raise its priority if you are trying to recruit someone quickly. Some jobs, like executions, also require a warden who is willing to do violence.

See the [Warden section of the Work page](https://rimworldwiki.com/wiki/Work#Warden) on the wiki.

## DLC notes

The prisoner tab adjusts to whatever DLCs you have:

- **Ideology**: adds will, the Reduce will and Enslave modes, and the Convert mode with its ideoligion picker. Slaves get their own version of the tab (suppression, terror, rebellion chance, and slave-specific modes).
- **Biotech**: bloodfeeder-related options appear when relevant, including a hemogen-farm mode.
- **Anomaly**: studiable captives show study interval and knowledge-gain information in the Information section.

If a mode exists for your prisoner, it will be in the list when you arrow through.

## Learn more

- [Prisoner](https://rimworldwiki.com/wiki/Prisoner): the full reference for prisoner rules, prison layout, resistance math, and prison breaks.
- [Recruitment](https://rimworldwiki.com/wiki/Recruit) and [Resistance](https://rimworldwiki.com/wiki/Prisoner#Resistance): the mechanics of turning a prisoner into a colonist.
- [Walls, doors, and rooms](../building/walls-doors-rooms.md): room-building basics.
