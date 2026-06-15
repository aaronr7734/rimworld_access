# The Assign tab

The Assign tab is where you set the standing rules your pawns follow on their own: what medicine they receive, which clothes they are allowed to wear, what food they will eat, and which drugs they take. You set policies once and pawns follow them automatically. Press **F3** to open it.

## The layout

Assign is a table. Rows are your pawns; columns are the policy categories:

- Medical care
- Apparel
- Food
- Drugs
- Reading
- Medicine carry amount
- Allowed medicine quality
- Allowed meal quality

Each column holds a policy name, and several pawns can share the same one. If you want most pawns on an "anything goes" food policy but two pawns on a strict diet, you create two food policies and assign each pawn the one they need.

The **Reading** column appears with the **Anomaly** DLC. A reading policy controls which books and tomes a pawn is allowed to read. This matters because some reading material in Anomaly is dangerous, so you can keep risky tomes away from pawns you would rather not expose to them.

The standard table controls apply here: **Alt+S** sorts by the column you are on, and painting a value down a column works the same way it does everywhere. See [navigating menus](../concepts/navigating-menus.md) for the full table grammar.

## Managing policies with the context menu

To create, rename, copy, or delete a policy, press **`]`** on a policy category. The available options depend on the category and are announced as you arrow through them:

- On **Medical care**, **`]`** offers "Change defaults."
- On **Apparel**, **`]`** offers New (**Alt+N**), Rename, Copy, Delete, and Edit.

Food and drug policies offer the same kind of options. This is also how you create a new policy before assigning it to anyone. See [the context menu](../concepts/context-menu.md) for how the **`]`** menu works in general.

## Editing a policy

Choosing Edit (or opening any policy for editing) opens a large tree view where you specify what is allowed. Type-ahead works throughout, so you can jump to an item by typing its name.

### The hit-point requirement filter

Apparel and weapons lose hit points as they take damage. This filter sets the condition range a pawn is willing to wear or carry.

Press **Enter** on the hit-points entry (it reads something like "65% to 100%"), then:

- **Up/Down** move between the minimum and maximum bound.
- **Left/Right** adjust whichever bound you are on.
- **Enter** confirms.

One reason to adjust this: a piece of clothing's resale value drops noticeably once it falls below roughly 60% hit points. Setting the lower bound above that point means pawns will swap out of their clothes while the items are still worth selling.

### The quality filter

Items have quality levels ranging from awful up to legendary. The quality filter works exactly like the hit-point filter: **Enter** to open, **Up/Down** between min and max, **Left/Right** to adjust, **Enter** to confirm.

### Allow and forbid checkboxes

The tree includes checkboxes for broad categories, such as burnable apparel or bio-coded items. Toggle them to allow or forbid whole groups at once.

One thing to watch: forbidding burnable apparel sounds tidy, but most cloth and leather clothing is burnable, so that single checkbox can leave pawns with almost nothing they are allowed to wear. Read what you are forbidding before you commit.

### Expandable items for precise control

When a broad category is not precise enough, expand it. Armor categories expand into specific pieces, so you can allow flak vests but forbid flak pants, or any combination you like. **Right** expands, **Left** collapses.

## Food and drug policies

Food and drug policies follow the same tree-view pattern. A food policy lets you specify which meals a pawn is allowed to eat, useful for keeping fine meals away from the hauler who would just eat them standing over the pots. A drug policy sets which drugs a pawn takes and how often.

## Learn more

Policies go deep, especially drug schedules and the finer points of apparel management. The [RimWorld wiki's Assign page](https://rimworldwiki.com/wiki/Assign) covers the strategy and the edge cases. For specific policy types, see the wiki's [Food](https://rimworldwiki.com/wiki/Food) and [Drug policy](https://rimworldwiki.com/wiki/Drug_policy) articles.
