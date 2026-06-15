# The Schedule tab

The Schedule tab sets each pawn's daily rhythm: when they work, when they rest, and when they sleep. The day is divided into 24 hours, and you decide what each hour is for. Press **F2** to open it.

The default schedule is usually fine for a new colony. Most colonies run for a long time without needing changes here, so if this screen feels like extra work, leave it alone until you have a concrete reason to tinker.

## Brushes

You assign hours by picking a "brush" and painting it onto the schedule. A brush is a time assignment type, and you choose one by pressing a number key: **1** through **9** and **0**.

The list typically looks like this:

- **1** Anything
- **2** Work
- **3** Recreation
- **4** Sleep

The list comes directly from the game, so the exact order can vary, and DLCs can add more entries. Trust what the mod announces when you press a number over what is written here.

## Applying a brush

Move to the hour you want, then:

- **Space** or **Enter** applies the current brush to that single hour.
- **Shift+Left** and **Shift+Right** paint the brush across the hours toward that side as you move.
- **Shift+Home** paints the brush from the current hour back to hour 0.
- **Shift+End** paints the brush from the current hour to hour 23, the last hour of the day.

### Filling a night

A night is not a single stretch on this screen, because the row runs from hour 0 to hour 23 and a night spans the wrap between them: the late-evening and early-night hours sit at the right end of the row, while the late-night and early-morning hours sit at the left end. Filling a full sleep period therefore takes two passes, not one. Pick the Sleep brush with **4**, then paint each edge of the night separately: from your pawn's bedtime use **Shift+End** to paint to hour 23, and from hour 0 use **Shift+Right** (or **Shift+Home** from the wake-up hour) to paint the early-morning hours. Painting from a single hour cannot wrap across the end of the day in one move.

## Copying a schedule

Once you have built a schedule you like, you can apply it to the whole colony without rebuilding it for each pawn.

- **Ctrl+C** copies the current pawn's schedule.
- **Ctrl+V** pastes it onto another pawn.

## Related pages

- [The Work tab](work.md) decides what each pawn does during their work hours. The Schedule tab decides when those hours are.
