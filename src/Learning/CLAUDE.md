# Learning Module

## Purpose
Provides screen reader accessibility for RimWorld's Learning Helper (contextual tutorial system). Announces new lessons via screen reader and provides a keyboard-navigable menu to browse, read, and complete tutorial concepts.

## Files
**Patches:** LearningHelperPatch.cs
**States:** LearningHelperState.cs

## Architecture

### LearningHelperState - Two-Mode Navigation
Operates in two modes toggled with Tab:

1. **Active Lessons** (default) - Shows only currently active/unlearned concepts from `Find.Tutor.learningReadout`'s private `activeConcepts` field (via reflection)
2. **All Lessons** - Shows all non-triggered concepts from `DefDatabase<ConceptDef>`, with typeahead search

Detail view uses TwoLevelMenuHelper: header (label + knowledge %) → help text lines → Mark as Learned button.

### LearningHelperPatch - New Lesson Announcements
Harmony postfix on `LearningReadout.TryActivateConcept` announces newly activated concepts via TolkHelper. Supplements the game's existing `SoundDefOf.Lesson_Activated` audio cue.

### Knowledge Tracking
- Opening detail view: `KnowledgeDemonstrated(conc, KnowledgeAmount.SpecificInteraction)` (+0.4 each time)
- "Mark as learned" button: `SetKnowledge(conc, 1f)` (immediate completion)
- Knowledge percentage shown in list and detail header, matching game's progress bar

## Key Shortcuts

### Learning Helper Menu (?)
**List Mode:**
- **Up/Down** - Navigate lessons (with typeahead in All mode)
- **Home/End** - Jump to first/last
- **Enter** - Open detail view
- **Tab** - Toggle Active ↔ All Lessons
- **Escape** - Clear search / Close menu
- **Backspace** - Delete search character (All mode)
- **Type letters** - Typeahead search (All mode)

**Detail Mode:**
- **Up/Down** - Navigate header → help text lines → button
- **Left/Right** - Navigate buttons
- **Enter** - Activate button (Mark as Learned)
- **Escape** - Back to list
- **Home/End** - Jump to start/end of detail

## Dependencies
**Requires:** ScreenReader/, Input/, UI/TwoLevelMenuHelper, UI/MenuHelper, UI/TypeaheadSearchHelper

## Settings Respect
- `TutorSystem.AdaptiveTrainingEnabled` - Must be true (checks both Prefs and storyteller)
- `TutorSystem.TutorialMode` - Must be false (game handles its own tutorial UI)
- Feature is fully disabled when adaptive training is off

## Localization
All text comes from the game's own localized data:
- `ConceptDef.LabelCap` - Concept name
- `ConceptDef.HelpTextAdjusted` - Help text (keyboard/controller aware)
- `"LearningHelper".Translate()` - Feature title
- `"MarkLearned".Translate()` - Button text
- `"AlreadyLearned".Translate()` - Disabled button text

## Testing
- [ ] ? key opens menu in-game
- [ ] ? key does nothing when adaptive training is disabled
- [ ] Active mode shows only unlearned concepts
- [ ] Tab toggles to All mode with full list
- [ ] Typeahead search works in All mode
- [ ] Enter reads lesson via detail view
- [ ] Knowledge percentage announced correctly
- [ ] "Mark as learned" completes lesson
- [ ] New lesson triggers screen reader announcement
- [ ] Escape flow: detail → list → close
- [ ] No key leakage while menu is open
