# Health and medical care

<!-- source: src/Pawns/PawnInfoHelper.cs, src/Pawns/HealthState.cs, src/Inspection/InspectionTreeBuilder.cs, src/Pawns/HealthTabState.cs, src/Input/UnifiedKeyboardPatch.cs -->

Sooner or later someone gets shot, bitten by a wild boar, or wakes up with the flu. This page covers how to check a pawn's health, how treatment works, and how to schedule surgery. For the medical mechanics themselves (what each injury does, how infections progress, which prosthetics are worth installing), see the [Health](https://rimworldwiki.com/wiki/Health) and [Medicine](https://rimworldwiki.com/wiki/Medicine) pages on the RimWorld wiki.

## The quick health readout: Alt+H

Press **Alt+H** on the map to hear a fast health summary for a pawn. The mod checks the tile under the cursor first; if no pawn is there, it falls back to whoever you have selected via the colonist bar. It works on colonists, enemies, and animals.

The summary is built for combat triage, not paperwork. It reads the name, then: pain level, bleeding rate with a time-to-death estimate when they are losing blood, any of consciousness, movement, or manipulation that has dropped below full, injured body parts with their remaining hit points, and whole-body conditions such as diseases. If nothing is wrong, it says the pawn is healthy.

When multiple pawns are selected and the cursor is not on anyone, **Alt+H** opens a small picker to choose whose health to read.

For a deeper look, open the pawn's inspection tree and go to the Health category. Type "inf" to jump to infections, "leg," "arm," or "head" to jump to everything wrong with that body part. Arrow up and down through the matching results. For how the inspection tree works in general, see [navigating menus](../concepts/navigating-menus.md).

## Reading the full health tab

To open the inspection tree for a pawn, put the cursor on them and press **Enter**, or select the pawn via the colonist bar and press **Ctrl+Alt+Enter**. Then arrow to the **Health** category and expand it.

Inside Health you will find, in order:

- **Operations** and **Health Settings**: the two action items described below.
- **Pain** and **bleeding** lines, when they apply.
- A node for each injured **body part**. Expand a part to hear its condition and remaining hit points, plus the specific conditions on it. Identical wounds are grouped, so you hear "gunshot wound x3" rather than three separate entries. If the pawn has no conditions, it says so.
- A **Capacities** section: sight, manipulation, moving, consciousness, and the rest, each with a percentage and a plain-language rating. Expand a capacity to hear what is dragging it down.

Learn more about what specific conditions mean on the wiki: [Injury](https://rimworldwiki.com/wiki/Injury) and [Disease](https://rimworldwiki.com/wiki/Disease).

## How treatment works

Treatment is a job your colonists perform on their own, controlled through the work system. A pawn who is hurt or sick needs to be tended by a doctor. The doctor is whoever has the **Doctor** work type enabled and a free moment. The patient needs the **Patient** work type enabled so they will go rest in a bed and accept care. Both are columns in the [Work tab](work.md) (F1). If nobody is tending the wounded, the usual causes are that no colonist has Doctor turned on, or the patient will not lie down because Patient is off.

A few details:

- Tend quality depends on the doctor's Medicine skill and the medicine used. Better medicine and a more skilled doctor mean wounds heal cleaner and infections are less likely.
- Medical beds and a clean, well-lit room improve outcomes. Mark a bed as medical from its inspection menu.

See [Doctoring](https://rimworldwiki.com/wiki/Doctoring) on the wiki for tend quality and surgery, and [Medicine](https://rimworldwiki.com/wiki/Medicine) for the medicine types and when to use each.

## Scheduling surgery

Surgery in RimWorld is a bill placed on a pawn, the same way a crafting bill sits on a workbench. Queue the operation, and a doctor performs it when they get to it. This covers everything from amputating an infected limb to installing a peg leg, a bionic eye, or harvesting an organ.

To schedule one:

1. Open the pawn's inspection tree (**Enter** on them, or **Ctrl+Alt+Enter** when selected), expand **Health**, and activate **Operations** with **Enter**.
2. A list shows operations already queued for this pawn, with an **Add operation** entry at the bottom. Arrow with Up/Down, and type to search.
3. Move to **Add operation** and press **Enter** to see every operation available for this pawn right now. Each entry announces the operation's name, description, the skill it requires, its ingredients, and a warning if you are missing a required medicine or part.
4. Press **Enter** on the operation you want. If it applies to a specific body part and more than one valid spot exists, you then pick the part from a list that reads each part's name, condition, and hit points. If there is only one sensible part, or the operation is not tied to a part, it is added immediately.

Back on the operations list, press **Enter** on a queued operation to reach **View details**, **Remove**, and **Go back**. Reorder the queue with **Ctrl+Up** and **Ctrl+Down** to control which operation a doctor performs first.

Two things to know before queuing surgery:

- The operation only happens if you have the right ingredients on hand (medicine, and the prosthetic or organ being installed) and a colonist with enough Medicine skill. The list flags missing ingredients, but it will not conjure a bionic arm you do not own.
- Surgery can fail, and a failed operation can injure or kill the patient. Higher Medicine skill and better medicine reduce that risk.

See [Doctoring](https://rimworldwiki.com/wiki/Doctoring) for success chances and [Prosthesis](https://rimworldwiki.com/wiki/Prosthesis) for what can be installed.

<!-- TODO(Aaron): confirm there isn't a faster map shortcut to jump straight to a pawn's Operations list (this draft routes through the inspection tree via Enter / Ctrl+Alt+Enter, which is what the code shows). -->

## Health Settings: per-pawn medical rules

The other action under Health is **Health Settings**. Open it to set three things for this pawn:

- **Allowed food**: which food policy the pawn follows.
- **Medical care**: the highest quality of medicine a doctor is allowed to spend on this pawn, from no medicine up through herbal, industrial, and glitterworld. The setting reads its current value and the available levels.
- **Self tend**: whether the pawn is allowed to treat their own wounds. If you enable this for a pawn who cannot perform Doctor work, the game reverts it and tells you why.

Arrow Up/Down through the three settings, press **Enter** to change one, pick a value, and **Enter** again to confirm. **Medical care** is a radio button: press **Enter** to select the care level you want. Interaction mode, where it applies, is a separate radio button.

## Medical care across the colony

Health Settings changes one pawn at a time. To set medical care for the whole colony at once, use the **Medical care** column in the [Assign tab](assign.md) (F3). It is the same setting, in a table where you can sort by column and paint a value down the column, so setting "industrial medicine for everyone, glitterworld for the prisoner you're trying to keep alive" takes a few keystrokes instead of a tour of every pawn.

Choosing the right medicine level matters: glitterworld medicine works wonders but runs out fast, and spending it on minor injuries is a waste. The [Medicine](https://rimworldwiki.com/wiki/Medicine) wiki page lays out the trade-offs.

## Learn more

- [Health](https://rimworldwiki.com/wiki/Health): overview of injuries, conditions, and capacities.
- [Doctoring](https://rimworldwiki.com/wiki/Doctoring): tending, tend quality, and surgery.
- [Medicine](https://rimworldwiki.com/wiki/Medicine): medicine types and when to use each.
- [Injury](https://rimworldwiki.com/wiki/Injury) and [Disease](https://rimworldwiki.com/wiki/Disease): what specific conditions do.
- [Prosthesis](https://rimworldwiki.com/wiki/Prosthesis): prosthetics and bionics.
