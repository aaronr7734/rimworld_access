# Selecting pawns

Colonists (the game calls them pawns) are the heart of a RimWorld colony. This page covers how to select them, navigate the colonist bar, jump the cursor to a specific pawn, and build combat groups. For reading a pawn's health, mood, and other stats at a glance, see [checking on pawns](checking-on-pawns.md).

## The colonist bar

The colonist bar is a persistent strip along the top of the screen that shows all your colonists and mechs. You can navigate it, select individual pawns, reorder it, and page through larger groups without touching the cursor.

## Cycling through colonists (comma / period)

**Period** selects the next colonist; **comma** selects the previous one. This is the game's own vanilla cycling behavior: it steps through your colonists in bar order and announces the selected pawn. When you reach the mech section of the bar, the same keys cycle through mechs instead.

## Selecting by number (Alt+1–0)

**Alt+1** through **Alt+9** select the colonist at positions 1–9 on the current bar page. **Alt+0** selects position 10.

Pressing the same Alt+number twice within about half a second also moves the cursor to that pawn's tile and snaps the camera to them. The single press selects and announces; the double press is the cursor jump.

## Paging through more than ten colonists (Alt+Down / Alt+Up)

When you have more than ten colonists or mechs, they are organized into pages. **Alt+Down** pages to the next group; **Alt+Up** pages back. After paging, use **Alt+1–0** to select within the new page.

## Navigating the bar one pawn at a time (Alt+Left / Alt+Right)

**Alt+Left** and **Alt+Right** move through the colonist bar one pawn at a time, crossing page boundaries. This is a linear walk across the entire bar rather than a page-at-a-time jump.

## Reordering the bar (Ctrl+Alt+Left / Ctrl+Alt+Right)

**Ctrl+Alt+Left** and **Ctrl+Alt+Right** move the currently selected pawn earlier or later on the bar. This lets you arrange colonists in an order that makes sense to you, so the numbers fall where you expect.

**Ctrl+Alt+Down** and **Ctrl+Alt+Up** move the pawn between pages on the bar.

## Focusing the cursor pawn on the bar (/)

Press **`/`** to focus the pawn under the cursor on the colonist bar. If the cursor is on a colonist or mech that appears on the bar, the bar jumps to that pawn's position. This is how you move quickly from "cursor on pawn" to "pawn selected on bar."

## Jumping the cursor to the selected pawn (Alt+C)

Press **Alt+C** to move the cursor and camera to the currently selected pawn. This is the reverse of **`/`**: it takes you from "pawn selected on bar" to "cursor on pawn." When several pawns are multi-selected, **Alt+C** opens a picker so you can choose which one to jump to.

## Multi-select (Alt+Space)

**Alt+Space** toggles the pawn under the cursor (or the currently focused bar pawn) into or out of a multi-select group. Once multiple pawns are selected, orders issued via **`]`** or gizmos apply to all of them.

Multi-select is mainly useful for combat: sending a group to attack a position, drafting several colonists at once, or setting up combat groups. Many gizmos expect a single pawn and will produce unexpected results (or not work at all) when multiple pawns are selected. If a gizmo behaves strangely, check whether multi-select is active.

**Ctrl+Alt+Space** selects all colonists at once, or clears the multi-select if already active.

**Alt+Shift+Right** and **Alt+Shift+Left** extend the current selection contiguously to the next or previous pawn on the bar.

## Combat groups (Ctrl+Shift+F1–F4 / Ctrl+F1–F4)

You can save and recall named groups of pawns for fast combat deployment.

- **Ctrl+Shift+F1** through **Ctrl+Shift+F4** save the current multi-select group to slot 1–4.
- **Ctrl+F1** through **Ctrl+F4** recall the saved group from slot 1–4, re-selecting those pawns.

Note: **F1** by itself opens the Work menu; **F2** opens the Schedule menu; **F3** opens the Assign menu; **F4** opens the Animals/Mechs menu. The combat group keys require Ctrl to avoid conflicting with those.

## Going further

- [Checking on pawns](checking-on-pawns.md): health, mood, needs, gear, and skills at a glance.
- [The context menu](context-menu.md): issuing orders to a selected pawn at the cursor tile.
- [Gizmos](gizmos.md): the command actions available for a selected pawn or object.
