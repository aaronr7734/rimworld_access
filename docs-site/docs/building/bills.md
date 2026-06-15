# Bills and workbenches

A bill is a standing order to produce a specific item at a workbench: cook meals, butcher corpses, tailor a parka, smith plate armor. Bills differ from designators, which are one-off map orders. A designator says "build a wall here." A bill says "keep making this until I say otherwise."

Every crafting workbench uses bills: the stove, butcher table, tailoring bench, smithy, fabrication bench, and so on.

## Adding a bill

1. Move your cursor onto a workbench and press **Enter** to open its inspect screen.
2. Navigate to **Bills**.
3. Choose **Add bill**.
4. A flat menu lists every product that bench can make. For a stove, that includes simple meals, fine meals, lavish meals, vegetarian and carnivore variants, and batch versions.
5. Press **Enter** on the item you want. It joins the bench's bill list.

For how flat menus work in general (arrow keys, type-ahead, Home/End), see [navigating menus](../concepts/navigating-menus.md).

## Bill order matters

Bills run top to bottom, most important first. A pawn does the first bill that still has work to do. You can reorder bills in the bill list.

## Editing a bill

Press **Enter** on a bill to open its options. The fields are presented in the order below. Most adjust with **Left** / **Right**; many also accept **Enter** to type a value directly.

### Recipe info

A read-only summary of the recipe: name, description, work amount, and skill requirements.

### Suspend / Resume

Press **Enter** to toggle the bill between active and suspended. A suspended bill is skipped by all workers until you resume it.

If the bill is currently auto-paused (it reached its target and paused itself), an **Unpause** item appears just below this. Press **Enter** on it to resume manually.

### Repeat mode

Controls how long the bill keeps running:

- **Do X times**: make the product a set number of times, then stop. When this mode is active, a **Repeat count** field appears below where you set the number.
- **Do until you have X**: keep production topped up to a target count. This opens a block of additional fields (see Target count through Pause when satisfied below).
- **Do forever**: never stop. Useful for chores with no real target, like butchering corpses.

### Target count

Applies in "do until you have X" mode. This is the X. Adjust with **Left** / **Right**, or press **Enter** to type a number.

### Currently have

A read-only readout showing how many you currently have versus the target, for example "currently have 0 of 51."

Within this block you may also see:

- **Include equipped**: count weapons or apparel that are currently equipped by pawns (appears for weapon and apparel bills).
- **Include tainted**: count tainted apparel (appears for applicable apparel bills).
- **Include source**: choose whether to count from all storage or a specific stockpile.
- **Hit points filter**: only count items above a percentage of their max condition.
- **Quality range**: restrict which quality levels count toward the target.
- **Limit to allowed stuff**: only count items made from your allowed materials.

### Pause when satisfied

A toggle. When the target count is reached, the bill pauses rather than sitting idle. It resumes once the count drops to the **Unpause at** threshold, which appears just below when this toggle is on. This prevents pawns from repeatedly starting and stopping at the boundary.

### Destination

This is the store-mode field. It controls where the finished product goes after it is made. Press **Enter** to choose:

- **Drop on floor**: the item lands at the bench. No hauling trip needed if your bench is near storage.
- **Take to best stockpile**: a pawn hauls the item to the appropriate stockpile.
- Your defined storage groups and stockpiles may also appear here as options.

This field is labeled based on the current store mode, not "product destination."

### Worker

Choose which pawn or pawn type is allowed to work this bill. Options include any worker, any slave, any mech, any non-mech, any human, or a specific pawn by name. This is the field literally labeled "Worker" in the game.

### Skill min / max

Two separate items that set a skill level range. Only pawns whose skill level falls within the range may work the bill. These appear when no specific pawn is set (the Worker field is on "any" rather than a named pawn) and the recipe has a relevant skill.

This is a distinct filter from Worker. Worker controls who by type; skill range controls who by level.

### Ingredient search radius

How far pawns will travel to gather ingredients for this bill. Reduce it to prevent pawns from hiking across the map for a single ingredient. "Unlimited" is the maximum.

### Ingredient filter

Choose which ingredients the bill may use. The standard example is excluding human meat from a cooking bill, so colonists do not accidentally prepare long pig.

### Rename

Give the bill a custom name so it is easier to identify in the list.

## Learn more

Bills are simple individually but combine in complex ways. For strategy on setting up reliable production chains and managing target counts, see the [Bills](https://rimworldwiki.com/wiki/Bills) article on the RimWorld wiki.
