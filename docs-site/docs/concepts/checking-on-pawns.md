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
