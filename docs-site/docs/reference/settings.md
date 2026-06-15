# Settings

RimWorld Access has its own options panel, separate from RimWorld's standard mod settings. Reach it from the main menu under **Options**, then **RimWorld Access**. The panel is available both at the main menu and in-game, so changes take effect immediately.

## Wrap navigation

**Default: off.**

When on, menus loop from the last item back to the first, and from the first back to the last. When off, navigation stops at the boundaries.

## Announce position

**Default: on.**

When on, announcements include a position indicator such as "3 of 7." Turn this off if the count feels like noise.

## Show pawn activity on map cursor movement

**Default: on.**

When on, arrowing over a non-colonist pawn (an animal, visitor, raider, or other non-colonist) announces what activity that pawn is performing. This information is inferred from the pawn's current state. If it feels like an unfair advantage, keep it off.

## Show cover info for drafted and hostile pawns

**Default: on.**

When on, cover information is included in the cursor announcement for drafted pawns and hostile pawns. For example: "Bob, behind sandbag (good cover), melee attacking." Applies during combat.

## Announce terrain on cursor movement

**Default: on.**

When on, the terrain name is spoken each time the cursor moves to a new tile. The terrain sound effect plays independently of this setting.

## Announce depth levels in treeviews

**Default: on.**

When on, moving deeper into a tree view announces the level number, for example "level 2." Turn this off if you navigate tree views fluently and find the level announcements redundant.

## Rashad Hates Treeviews (submenu-style navigation)

**Default: off.**

This is a toggle that changes how tree views behave across the mod. Named after another accessibility-mod developer, Rashad, who is not a fan of the traditional tree-view interaction pattern. (His own mods use tree views all over the place, just under other names, but the name stuck.)

**Off (standard tree view):**

- Right arrow on a collapsed item expands it, revealing its children alongside the parent.
- Right arrow on an already-expanded item moves into its first child.
- The parent stays visible in the list alongside its children.

**On (submenu mode):**

- Right arrow on an item expands it, hides the parent, and places you directly on the first child. It feels like stepping into a submenu.
- Left arrow collapses back to the parent.
- Your last position inside each parent is remembered when you return.

There is no wrong choice here, just preferences. Submenu mode keeps lists short and moves you forward quickly. Standard mode keeps parents and children visible together, which can help with orientation in a large tree. Try both.

For how tree views work in general, see [Navigating menus](../concepts/navigating-menus.md).

## Default Work Menu View (F1)

**Default: Focused.**

Determines which view opens when you press **F1**.

- **Focused**: a priority-grouped per-pawn view. Lower verbosity; well suited for checking on individual colonists.
- **Table**: pawn rows by work-type columns, mirroring vanilla. Supports sorting and painting.

You can switch between views at any time from within the Work menu using **Ctrl+Tab** (or **Option+Tab** on macOS). The setting updates automatically to remember your last choice.

## Announce forced slowdowns from threats

**Default: off.**

When on, the mod announces when the game forces Normal speed because of a nearby threat ("Game slowed down by presence of threat") and again when the slowdown lifts. This only fires if your chosen speed is faster than Normal.

To check the current game speed at any time, press **Alt+T**.

---

Setting tooltips are expanding as localization work continues. If a setting's in-game tooltip is not yet in your language, the English tooltip is still shown.
