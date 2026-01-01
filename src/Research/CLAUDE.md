# Research Module

## Purpose
Research tree browsing and project selection.

## Files
**States:** WindowlessResearchMenuState.cs, WindowlessResearchDetailState.cs

## Key Shortcuts
- **F6** - Open research menu
- **Arrow Keys** - Navigate research tree
- **Right Arrow** - Expand category
- **Left Arrow** - Collapse category
- **Enter** - View details or start research

## Architecture
Hierarchical tree (categories → projects). Right arrow expands, Left collapses. Detail view shows requirements and benefits.

## Dependencies
**Requires:** ScreenReader/, Input/

## Testing
- [ ] Research menu opens
- [ ] Tree navigation works
- [ ] Research can be started via keyboard
