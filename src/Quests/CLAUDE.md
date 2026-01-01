# Quests Module

## Purpose
Quest browsing and notification viewing.

## Files
**Patches:** NotificationAccessibilityPatch.cs
**States:** QuestMenuState.cs, NotificationMenuState.cs

## Key Shortcuts
- **L** - Open notification menu
- **Q/F7** - Open quest menu
- **Arrow Keys** - Navigate lists
- **Enter** - View details / Jump to target
- **A** - Accept quest
- **D** - Dismiss/Resume/Delete
- **Delete** - Delete letter

## Architecture
QuestMenuState has 3 tabs (Available, Active, Historical). NotificationMenuState has 2-level navigation (List → Detail).

## Dependencies
**Requires:** ScreenReader/, Input/, Map/ (jump targets sync with cursor)

## Testing
- [ ] Notification menu accessible
- [ ] Quest menu tabs work
- [ ] Jump-to-target functional
