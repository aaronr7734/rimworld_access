# Checking on pawns

Colonists (the game calls them pawns) need attention constantly: who is hurt, who is in a bad mood, who should be on the guns. This page covers how to get a fast read on any pawn from the map without opening a full screen.

For selecting pawns, cycling through colonists, and building combat groups, see [selecting pawns](selecting-pawns.md).

## The at-a-glance info keys

These keys give a quick spoken summary of the pawn under the cursor, or the currently selected pawn if the cursor is not on a specific one.

- **Alt+H**: quick health readout, focused on what matters in combat or triage: consciousness, movement, manipulation, and whether the pawn is bleeding. Use this to decide who needs immediate attention.
- **Alt+M**: mood.
- **Alt+N**: needs.
- **Alt+G**: gear (what they are wielding and wearing).
- **Alt+K**: top 3 skills.
- **Alt+B**: the pawn's recent combat log (their last several fights).

These prioritize the thing under the cursor first, then fall back to the pawn you selected with comma/period or an Alt+number. Place the cursor on an enemy raider and press **Alt+H** to get the raider's health, not your selected colonist's. Move the cursor off anything specific and the keys report on the selected pawn instead.

## Finding who has an item

Press **Alt+Shift+G** to open the gear menu. It is one list of every weapon and piece of apparel worn or held by anyone you can see on the map, with weapons and belts first, then apparel in the gear tab's order. Things pawns carry in their inventory are left out; the inventory screens cover those. Each row names the item and who has it, like "Duster (normal), worn by Bob, Dylan, Harry". Identical items share a row, so a duster of a different quality, or one that is tainted, gets its own. When only one pawn has the item, the row also says its condition if it is damaged.

Press **Right arrow** on an item to step into the pawns who have it, then **Up** and **Down** to move between them. Each pawn is marked as colonist, prisoner, visitor, hostile and so on, along with the condition of their piece when it is damaged. Press **Left arrow** to go back to the item. Start typing an item's or pawn's name to find it.

Press **Enter** on a pawn to close the menu, jump to them and open their gear tab with the cursor on that item. If only one pawn has the item, Enter on the item does the same. You can also open this menu from the F12 extras menu.

Press **Tab** to reach the filters: Colonists, Colony animals, Colony mechanoids, Prisoners and slaves, Visitors, and Hostiles. They all start checked. Press **Enter** or **Space** on one to hide or show that group's gear, and the menu tells you how many holders are left. Pawns with no faction at all, like a wild man, count as visitors here. The menu remembers your filters, separately from the health overview.

The menu updates itself while it is open, paused or not. If the pawn or item you are on moves, the cursor goes with it. If it disappears, the cursor moves to the next row in the same list, and if that list is now empty, back up to its heading.

## The health overview (Alt+Shift+H)

After a raid, or whenever you want to know who needs a doctor, press **Alt+Shift+H** for the health overview. It lists every downed, bleeding, injured, or sick pawn you can see on the current map, so healthy pawns stay out of the way. It is also in the F12 extras menu.

The overview always has five headings: Downed and bleeding, Downed, Bleeding, Injured, and Sick. A hurt pawn shows up under exactly one of the first four, the first that fits, so someone who is downed and bleeding is only listed under Downed and bleeding. A Deathless pawn who is bleeding still counts as bleeding. Injured is everyone who is hurt but neither downed nor bleeding.

Sick is separate: anyone with a disease the game would give them the "sick" mood for, like the flu, plague, malaria, sleeping sickness, gut worms, muscle parasites, or an infection, is listed there too, even if they also appear under an injury heading. Sick normally comes last, but it moves to the top while one of its diseases is life-threatening.

Every heading sums up what is under it, like "Bleeding. 3 pawns, soonest bleeds out in 2 hours", "Injured. 4 pawns, lowest 38% health", "Sick. 2 pawns, most urgent Ana, Flu (major): 52%", or "Downed. None". Inside a heading the most urgent pawn comes first: whoever will bleed out soonest, otherwise whoever has the lowest health. Each pawn reads their name and who they are (colonist, colony animal, prisoner, visitor, hostile and so on), then whether they are downed, how long until they bleed out, and their overall health.

Under Sick, the most urgent pawn is whoever has a life-threatening disease, then whoever is closest to losing the race between their immunity and the disease. Each sick pawn reads their name and who they are, then every disease with its severity. Open a pawn with **Right** to get one row per disease, with its severity, a medical emergency warning when it is life-threatening, their immunity, and whether it is tended or needs tending, all read from the game's own tooltip.

Use **Right** and **Left** to open and close a heading, or just start typing a name to find a pawn anywhere in the tree. Press **Enter** on a pawn to close the overview, jump to them, and open their health tab. Press **Enter** on a disease to do the same with the cursor already on that disease. **Alt+I** opens the pawn's info card, or the disease's on a disease row.

Press **Tab** to reach the filters: Colonists, Colony animals, Colony mechanoids, Prisoners and slaves, Visitors, and Hostiles. They all start checked. Press **Enter** or **Space** on one to hide or show that group, and the overview tells you how many pawns are left. Wild animals are never listed, and other pawns with no faction, like a wild man, count as visitors. Your filters are remembered, separately from the gear menu.

The overview keeps itself up to date while it is open, paused or not, without saying anything. If the pawn you are on moves to a different heading, the cursor follows them as long as that heading is open. If they leave the list, say because they were patched up, the cursor moves to the next pawn in the same list, and if the list is now empty, back up to its heading. It never throws you back to the top.

<!-- source: GlobalHealthScope.Game.cs, MapOverviewScope.Game.cs, MapPawnGroup.Game.cs, PawnHealthSummary.Game.cs -->

## Finding conditions with type-ahead

For a deeper read on a pawn's health state, open the pawn's inspect view (press **Enter** on the pawn under the cursor, or **Ctrl+Alt+Enter** on a pawn selected via the colonist bar). From the top section of the inspect screen, start typing to jump to a condition:

- "inf" jumps to infections.
- "leg" or "arm" or "head" (or even "hea") jumps to everything wrong with that body part.

Arrow **Up** and **Down** through matching results. This is the fastest way to find a specific injury or condition without scrolling through the full list.

<!-- source: HealthTabState.cs, WindowlessInspectionState.cs -->

## The skills table (Alt+P)

To compare every colonist's skills side by side, press **Alt+P** to open the skills table. It lists all your colonists against all their skills.

Inside the table, **Alt+S** sorts by the column you are on, so you can jump to a skill column and find your strongest pawn for it. Press **Alt+S** again to flip the sort direction, and once more to clear it.

This pairs naturally with setting up [work priorities](../colony/work.md): find your best cook in the skills table, then assign cooking to them.

## Where to go from here

These keys assume you are on the colony map. For the basics of moving the cursor and camera, see [the map](the-map.md). For a deeper read on a single pawn (full health detail, operations, traits, relationships), open the relevant tab via the inspect view. The at-a-glance keys above are for the constant spot-checks that keep a colony running.
