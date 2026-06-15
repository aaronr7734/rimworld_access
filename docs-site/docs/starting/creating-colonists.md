# Creating your colonists

This is the last screen before the game starts: the character setup screen, where you decide who lands with you. You can read each candidate's background, reroll ones you dislike, set filters to control what the game generates, and rename anyone. The colonists you keep here are the ones you will be keeping alive for the rest of the game.

The screen is a tree view (see [Navigating menus](../concepts/navigating-menus.md)). Use **Up** and **Down** to move through the list of candidates, **Right** to expand one and read its details, and **Left** to collapse it again.

## The slots: Selected and Left Behind

The screen shows up to eight candidate slots even if your scenario only starts you with three colonists. The list is divided by two markers: **Selected** and **Left Behind**. Pawns in the Selected group come with you when the game starts. Pawns in the Left Behind group do not. As you arrow across the boundary, the mod announces which group you have entered.

The extra slots exist for rerolling. You can reroll a Left Behind slot as many times as you like looking for a good candidate, then move that pawn up into the Selected group.

### Moving a pawn between groups

Press **Ctrl+Up** and **Ctrl+Down** to move the current candidate up or down the list. When a pawn crosses the Selected/Left Behind boundary, the mod announces which group they landed in and who their new neighbors are.

## Reading a pawn's details

Press **Right** on a candidate to expand them. Inside you find categories you can expand in turn:

- **Bio:** their backstory and background.
- **Age:** RimWorld tracks two ages. Biological age is how old the body is; chronological age is how many years have passed since birth. The two can differ due to cryptosleep stasis.
- **Relations:** other pawns this candidate knows, with opinions in each relation's detail. Press **Enter** on a relation to jump to that pawn in the list.
- **Traits:** personality traits. Most pawns have at least one, up to three.
- **Incapable of:** work types this pawn refuses to do. Worth checking before you commit to keeping someone.
- **Skills:** their twelve skill levels (0 to 20) and passions. A passion makes a pawn learn a skill faster and gain a mood boost from doing it. Skills come with no passion, a single passion, or a double (burning) passion.
- **Health** and **possessions** round out the picture.

When a category is collapsed, the mod reads a short summary so you can decide whether to expand. The collapsed pawn entry itself carries age, gender, traits, and top skills inline.

### Comparing the same detail across pawns

Once you have expanded to a particular detail on one pawn, **Page Up** and **Page Down** jump to the same spot on the previous or next pawn. If you are on the Construction skill for candidate one, Page Down takes you to Construction on candidate two, and so on. This makes it easy to compare one attribute across everyone without re-navigating each pawn's tree.

## Rerolling and filtering

If you do not like a candidate, you can generate a new one. Filters let you tell the game what to look for before it generates.

Pressing **`]`** on a candidate opens a context menu gathering these actions in one place: randomize (the same as Alt+R), rename (the same as Alt+N), and edit the pawn filter (the same as Alt+F). With the Biotech DLC it also includes the developmental stage and xenotype options described below. Each has a direct key as well, so you can use whichever you prefer.

### Reroll (Alt+R)

Press **Alt+R** to reroll the candidate you are on. A new pawn replaces them. If you have filters active, the game rerolls behind the scenes until it finds someone who matches (or hits the roll limit), then tells you how many attempts it took.

### Filters (Alt+F)

Press **Alt+F** to open the pawn filter editor. Filters constrain what reroll will accept. You might ask for a colonist with Shooting 8 and no Pyromaniac trait, then reroll until you get one. This is not a native RimWorld feature: it is a rebuild of Random Plus, a mod popular with sighted players. New players can ignore it and just reroll by hand; it is here for when you want finer control. Filters persist across new games.

The editor is a flat list grouped into sections. Use **Up** and **Down** to move through it.

- **Minimum skill levels:** for each of the twelve skills, use **Left** and **Right** to set a minimum level, or **Shift+Left** and **Shift+Right** to jump in larger steps. Press **Enter** on a skill row to cycle the required passion (none, then single, then double). **Shift+Home** and **Shift+End** snap a value to its minimum or maximum.
- **Passion counts:** set a minimum and maximum number of total passions across all skills, adjusted with **Left** and **Right**.
- **Age:** minimum and maximum, adjusted with **Left** and **Right**.
- **Required traits** and **excluded traits:** navigate to the "add required trait" or "add excluded trait" row, then press **Enter** to open a trait picker. Type to search (typing "pyr" finds Pyromaniac, a trait you do not want), then press **Enter** to select. To remove a trait you have added, navigate to it and press **Delete**.
- **Roll limit:** the maximum number of reroll attempts before the game gives up and keeps the last result.

Press **Alt+S** to save your filters and close the editor. **Escape** also closes the editor.

### Presets

Near the bottom of the filter list are preset options. You can save the current filter set as a named preset and load a saved one later. Because presets persist across games, you can build a "balanced starter" filter once and reuse it for every new colony.

## The Team Skills summary (Tab)

Press **Tab** to switch to the Team Skills summary, which covers everyone currently in the Selected group. It lists the skills the game treats as important for a starting colony, which is most but not all of the twelve. For each listed skill it tells you the best pawn at that skill, their level, and their passion. Press **Up** and **Down** to move through the skills, or type to jump to one by name. Press **Tab** again to return to the candidate list.

## Renaming a pawn (Alt+N)

Press **Alt+N** to rename the candidate you are on. You can set their first name, nickname, and last name. The nickname is what the game usually uses, so a clear, distinct nickname makes everyone easier to track. This is your chance to set a pawn's first and last name: you cannot change those once the game starts. With the Biotech DLC there is a brief 24-hour window to name a newborn child, but otherwise names are fixed at the start.

## Starting the game

When you are happy with your roster, press **Enter** to confirm. The game asks you to confirm starting with the colonists you have chosen. Confirm, and the game begins.

## Picking a xenotype (Biotech DLC)

With the Biotech DLC, each candidate has a xenotype representing their genetic makeup, with most baseline humans set to **Baseliner**. To change it, press **`]`** on a pawn to open its context menu and choose the xenotype option. That opens a list of every xenotype in the game, plus an option to create a custom one in a gene editor. Picking a xenotype rerolls the pawn as that type. Developmental stage (baby, child, adult) is also on this menu. For how the context menu works, see [The context menu](../concepts/context-menu.md).
