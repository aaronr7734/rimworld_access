# Quests Module

## Purpose
Quest browsing with detail navigation, reward viewing, reward preferences, and notification viewing.

## Files
**Patches:** NotificationAccessibilityPatch.cs, QuestMenuPatch.cs
**States:** QuestMenuState.cs, NotificationMenuState.cs
**Helpers:** QuestRewardHelper.cs

## Architecture

### QuestMenuState - Three-Mode Navigation
QuestMenuState operates in three modes:

1. **QuestList** - Flat list with tabs (Available/Active/Historical), typeahead search
2. **QuestDetail** - TwoLevelMenuHelper-based detail view (header → content lines → buttons)
3. **RewardPreferences** - Per-faction reward toggle settings

Detail view content lines include difficulty, time info, description, and reward items with info card targets. Each reward item gets its own navigable line so users can press Alt+I to inspect items.

Buttons are context-sensitive: Accept (with reward choice variants for multi-choice quests), Dismiss/Resume, Delete, Jump to location.

### NotificationMenuState - Two-Level Navigation
List view (messages/letters/alerts) → Detail view (content + buttons). Uses TwoLevelMenuHelper.

### QuestRewardHelper - Reward Data Extraction
Static helper that extracts reward data from Quest objects into screen-reader text. Handles Reward_Items, Reward_Goodwill, Reward_RoyalFavor, Reward_Pawn, and fallback for other types. Also manages reward preference items from Dialog_RewardPrefsConfig data.

## Key Shortcuts

### Quest Menu (Q/F7)
**List Mode:**
- **Up/Down** - Navigate quests (with typeahead)
- **Left/Right** - Switch tabs
- **Home/End** - Jump to first/last
- **Enter** - Open detail view
- **Tab** - Switch to Reward Preferences
- **Alt+A** - Accept quest
- **Alt+D** - Dismiss/Resume/Delete
- **Escape** - Clear search / Close menu
- **Type letters** - Typeahead search

**Detail Mode:**
- **Up/Down** - Navigate header → info → description → rewards → buttons
- **Left/Right** - Navigate between buttons (in buttons section)
- **Enter** - Activate current button
- **Alt+A** - Accept quest (shortcut)
- **Alt+D** - Dismiss/Resume/Delete (shortcut)
- **Alt+I** - Open info card for current reward item/faction
- **Escape** - Back to list
- **Home/End** - Jump to start/end of detail

**Reward Preferences Mode:**
- **Up/Down** - Navigate settings
- **Enter** - Toggle current setting
- **Tab/Escape** - Back to quest list
- **Home/End** - Jump to first/last

### Notification Menu (L)
- **Up/Down** - Navigate list / detail lines
- **Enter** - Open detail / Activate button
- **Left/Right** - Navigate buttons
- **Escape** - Back to list / Close
- **Delete** - Delete letter

## Dependencies
**Requires:** ScreenReader/, Input/, Map/ (jump targets), UI/TwoLevelMenuHelper, UI/MenuHelper, UI/TypeaheadSearchHelper, UI/WindowlessFloatMenuState (pawn selection)

## Key Design Notes
- **Pawn selection priority safety:** AcceptQuestWithPawnSelection() CLOSES QuestMenuState before opening WindowlessFloatMenuState to prevent priority routing conflicts (QuestMenuState at 4.73 would steal keys from WindowlessFloatMenuState at 5).
- **Info card via DetailLine:** Each content line carries optional Thing/Faction references for Alt+I info card support.
- **Multi-choice quests:** QuestPart_Choice with 2+ choices generates separate accept buttons per choice. Alt+A from list mode warns user to use detail view.

## Testing
- [ ] Quest menu tabs work (Available/Active/Historical)
- [ ] Enter opens navigable detail view
- [ ] Detail view shows difficulty, time, description, rewards
- [ ] Reward items show on individual lines
- [ ] Alt+I opens info card for reward items/factions
- [ ] Multi-choice quests show separate accept buttons
- [ ] RequiresAccepter quests open pawn selection (arrow keys work in float menu)
- [ ] Tab switches to Reward Preferences, Enter toggles settings
- [ ] Alt+A and Alt+D work from both list and detail views
- [ ] Escape navigates back correctly (detail → list → close)
- [ ] Notification menu accessible
- [ ] Jump-to-target functional
