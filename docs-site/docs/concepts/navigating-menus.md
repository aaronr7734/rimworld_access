# Navigating menus

RimWorld has many screens, but they share a small set of navigation shapes. This page covers all of them. When you open something new, the first question is: which shape is it?

- **Tabs:** separate panels, moved between with **Tab** and **Shift+Tab**.
- **Columns or categories:** sections moved between with **Left** and **Right** arrows.
- **Tree views:** lists that expand and collapse to reveal nested details.
- **Tables:** a grid of rows (usually pawns) and columns (usually attributes or policies).

A few things work regardless of shape: you can start typing to jump to an item (type-ahead), and press **Escape** to back out. The rest of this page walks through each shape and the rules that connect them.

## Tabs (Tab / Shift+Tab)

Many screens are split into panels. **Tab** moves to the next panel; **Shift+Tab** moves to the previous one. This is not the classic "tab through every field" behavior from web forms. It moves between whole sections of a screen.

For example, the Work tab uses Tab and Shift+Tab to move between pawns, and the caravan formation screen uses them to move between the summary and formation panels. Once you land on a panel, arrow keys navigate within it.

A single tab can itself contain any of the other shapes: a panel might hold a tree view, a table, or a set of columns. Some screens are several tables side by side. Tab and Shift+Tab always move between the panels; once inside one, you navigate it by its own shape.

## Columns and categories (Left / Right arrows)

Some screens lay content out as sections you step through sideways. **Left** and **Right** move between those categories; **Up** and **Down** move within the current one.

The Quest menu is a clear example: Left and Right move between Available, Active, and Historical. Tables also use this pattern, where Left and Right move between columns.

## Tree views

A tree view is a list where items can contain other items. RimWorld uses these frequently because much of its information is dense and hierarchical. Expanding an item reveals everything inside it.

- **Right** arrow expands the current item (reveals children).
- **Left** arrow collapses it (hides children).
- **Up** and **Down** move between items at the current level.
- **Shift+8** (asterisk / **`*`**) expands all expandable siblings at the current level, so you can arrow across them without expanding each one individually.
- **Ctrl+Home** / **Ctrl+End** jump to the absolute first or last item in the tree, regardless of nesting level.

When an item is collapsed, the mod speaks a short summary of what's inside, so you can decide whether to expand it. Some tree items hold controls (a checkbox, an adjustable value) as well as readable content, so expanding can reveal things you act on as well as things you read.

### Level announcements

As you move up and down the tree, the mod announces the depth level when you change levels. This tells you where you are in the hierarchy without requiring you to track it mentally.

### The "Rashad Hates Treeviews" mode

There is an option that changes how trees behave, named after a fellow accessibility-mod developer. The label is **"Rashad Hates Treeviews (Submenu-Style Navigation)"** and it lives under Main Menu > Options > RimWorld Access. It is off by default.

With the setting **off** (standard mode): pressing **Right** on a collapsed item expands it and keeps the parent in view, with children listed below. Pressing Right on an already-expanded item drills into the first child.

With the setting **on** (submenu mode): pressing **Right** expands an item, hides the parent, and drops you onto the first child, like stepping into a submenu. Pressing **Left** collapses back to the parent. The mod remembers your position inside each parent, so stepping back in returns you to where you were.

## Tables

A table is a grid. Rows are usually your pawns; columns are usually attributes or policies (medical care, apparel, a skill, a checkbox). **Up** and **Down** move between rows; **Left** and **Right** move between columns.

### Sorting (Alt+S)

Press **Alt+S** to sort the table by the column you are on. Press it again to cycle: descending, ascending, no sort. This is how you find, say, your best constructor or your highest shooting skill.

### Painting a column

To copy one cell's value down (or up) an entire column without setting each cell individually:

- **Shift+Up / Shift+Down:** paint the current cell's value onto the row above or below.
- **Shift+Home / Shift+End:** paint from the current row to the top or to the bottom.
- **Ctrl+Shift+Home / Ctrl+Shift+End:** paint the value across every row in the column, regardless of your current position.

### Editing cells

- **Enter** or **Space** toggles a checkbox cell.
- **=** and **-** nudge a numeric cell up or down by one; **Enter** lets you type a number directly.
- **`]`** on a column header or cell opens a context menu of options for that category. See the [context menu page](context-menu.md) for how that menu works.

## Type-ahead: start typing

In any menu or list, start typing and the mod jumps to the first matching entry. This is one of the fastest ways to navigate. In a long Architect menu, type "wall" or "stock" to land directly on what you want instead of arrowing through dozens of entries. Type-ahead also works inside filter screens and search boxes when adding items by name.

## The Enter-vs-Alt+S rule

The question is always: on this screen, does Enter already do something to the current item?

- **If Enter selects or deselects items** (checking a pawn into a caravan, toggling a choice), Enter is "busy." In that case, **Alt+S** confirms the screen and moves you to the next one. The starting-pawns and caravan-formation screens work this way.
- **If Enter does not do anything special** to the current item, **Enter** finishes the screen. World-map site selection works this way: Enter settles on the tile.

Before assuming Enter will advance you, notice whether it is already doing selection work. If it is, use **Alt+S** to confirm. In tables, Alt+S is also the sort key, which fits the same logic: you are toggling and editing cells, so Enter is busy and Alt+S takes the confirm role.

## Escape

**Escape** cancels or closes the current screen and steps you back one level. On the main map with nothing else open, Escape opens the pause menu.

## A quick checklist for any new screen

1. Press **Tab** once. If it moves you to a different panel, Tab and Shift+Tab navigate between them.
2. Try **Left** and **Right**. If they move between sections while Up and Down move within a section, it is a columns or categories layout. If Up and Down step through rows (usually pawns) while Left and Right change which column you are reading, it is a table.
3. Try **Right** on an item. If it expands, you are in a tree view (and Left collapses).
4. Start typing. Type-ahead jumps to a match.
5. To finish: check whether Enter is already doing selection work. If yes, confirm with **Alt+S**. If no, **Enter** finishes. **Escape** backs out.
