# The Work tab

The Work tab is where you decide who does what. Hauling, cooking, mining, doctoring, art: every job in the colony gets assigned here. Press **F1** to open it.

## Two views

There are two ways to look at the same information. Switch between them with **Ctrl+Tab**.

- **Focused view** shows one pawn at a time. You move through that pawn's jobs and priority levels, then Tab to the next pawn. It is a straightforward place to start, and most of the instructions below assume it.
- **Table view** is the multi-column layout: every pawn as a row, every job as a column. It covers the whole colony at a glance and adds a few bulk tools, but the concepts are the same. It suits players who are comfortable with large tables.

Whichever view you pick becomes the default for next time.

## Basic mode and manual priority mode

Press **Alt+M** to toggle between the two assignment modes:

- **Basic mode** uses on/off checkboxes. A pawn either does a job or doesn't. The game then decides the order on its own, working left to right across the jobs (the leftmost enabled task is attempted first). Basic mode is perfectly fine for a new colony: you can leave it here, get comfortable with the rest of the game, and come back to manual priorities later.
- **Manual priority mode** lets you set a number from 1 to 4 on each job, giving you direct control over the order. It is more work up front, but it is the only way to say "doctor first, haul only when there is nothing better to do." The docs assume manual mode once you are ready for it.

## Setting priorities in manual mode (focused view)

In manual mode, in the focused view, jobs are organized by priority column:

- **Up/Down** move between priority levels (columns 1 through 4, plus Disabled).
- **Left/Right** move between jobs within the current priority level.
- **Tab/Shift+Tab** move to the next or previous pawn.

To set a priority, press a number key:

- **1** is the highest priority.
- **2**, **3**, and **4** step down from there.
- **0** turns the job off entirely.

As you arrow left and right through a priority level, the mod announces each job's name, skill level, passion, a description, and the specific sub-tasks it includes, in the order the pawn performs them. Some of those sub-tasks are spoken with an "E" in front. The "E" is a spoken marker, not a key you press: it flags a task the game treats as an emergency (such as firefighting or rescuing a downed colonist), which a pawn will drop other work to attend to.

## Bulk operations and bracket cycling

These keys work the same in both the focused view and the table view.

**`[`** cycles the current job's priority up (lower number, more important). **`]`** cycles it down (higher number, less important).

Add **Shift** to apply the same cycle to every eligible colonist at once: **Shift+`[`** raises the priority for every pawn, **Shift+`]`** lowers it for every pawn.

**Shift+number** (0 through 4) sets that job to the chosen priority for every eligible colonist at once.

## How execution order works

The game runs all priority-1 jobs first, reading them left to right across the row, then all priority-2 jobs left to right, and so on down to 4. Two jobs at the same priority number resolve by their left-to-right position. This is why the order within a priority level matters alongside the number.

## Table view

The table view gives a colonist-per-row, job-per-column layout. The same concepts as the focused view apply, plus a set of painting tools that exist only here. All the standard table controls apply:

- **Up/Down** move between colonists.
- **Left/Right** move between jobs.
- **Alt+S** sorts by the current column.
- **Shift+Up/Down** paints the current cell's value onto rows above or below as you move.
- **Shift+Home/End** paints from the current row to the top or bottom of the column.
- **Ctrl+Shift+Home/End** paints the value across every row in the column at once.

Painting (the **Shift+Up/Down** and **Shift+Home/End** keys above) is available only in the table view. The bracket keys (**`[`** / **`]`** and their Shift variants) and **Shift+number** behave the same in both views.

See [navigating menus](../concepts/navigating-menus.md) for the full table grammar.

## The unskilled-job warning

When you enable a job the pawn has no skill in, you will hear a warning sound. It is not blocking you, just a notice. Sometimes assigning unskilled work is exactly what you want (someone has to haul rocks). Other times it signals that you are about to let your best researcher attempt surgery.

## Learn more

Deciding which priorities to give which pawns is a strategy topic in its own right. The [RimWorld wiki's Work page](https://rimworldwiki.com/wiki/Work) covers the trade-offs in depth.

## Related pages

- [The Schedule tab](schedule.md) decides when pawns work; the Work tab decides what they do.
- [Checking on pawns](../concepts/checking-on-pawns.md) shows a pawn's skills and passions, which is useful to have open while assigning work.
