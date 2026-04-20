# Building Menus and TreeViews

All menus and treeviews must incorporate standard accessibility features for consistency. Use the helpers in `UI/` module.

## Standard Menu (Flat List)

**Required Features:**
- Up/Down navigation with configurable wrapping
- Home/End to jump to first/last
- Typeahead search with match navigation
- Position announcements (configurable)

**State Class Template:**
```csharp
public static class MyMenuState
{
    public static bool IsActive { get; private set; }
    private static List<MyItem> items = new List<MyItem>();
    private static int selectedIndex = 0;
    private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

    public static bool HasActiveSearch => typeahead.HasActiveSearch;

    public static void Open(List<MyItem> menuItems)
    {
        items = menuItems;
        selectedIndex = 0;
        typeahead.ClearSearch();
        IsActive = true;
        AnnounceCurrentItem();
    }

    public static void Close()
    {
        IsActive = false;
        items.Clear();
        typeahead.ClearSearch();
    }

    // Navigation - uses MenuHelper for settings compliance
    public static void NavigateUp()
    {
        if (items.Count == 0) return;
        typeahead.ClearSearch();
        selectedIndex = MenuHelper.SelectPrevious(selectedIndex, items.Count);
        AnnounceCurrentItem();
    }

    public static void NavigateDown()
    {
        if (items.Count == 0) return;
        typeahead.ClearSearch();
        selectedIndex = MenuHelper.SelectNext(selectedIndex, items.Count);
        AnnounceCurrentItem();
    }

    public static void NavigateHome()
    {
        if (items.Count == 0) return;
        typeahead.ClearSearch();
        selectedIndex = 0;
        AnnounceCurrentItem();
    }

    public static void NavigateEnd()
    {
        if (items.Count == 0) return;
        typeahead.ClearSearch();
        selectedIndex = items.Count - 1;
        AnnounceCurrentItem();
    }

    // Typeahead search
    public static bool HandleTypeahead(char character)
    {
        var labels = items.Select(i => i.Label).ToList();
        if (typeahead.ProcessCharacterInput(character, labels, out int newIndex))
        {
            if (newIndex >= 0)
            {
                selectedIndex = newIndex;
                AnnounceWithSearch();
            }
        }
        else
        {
            TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
        }
        return true;
    }

    public static bool HandleTypeaheadBackspace()
    {
        if (!typeahead.HasActiveSearch) return false;
        var labels = items.Select(i => i.Label).ToList();
        if (typeahead.ProcessBackspace(labels, out int newIndex))
        {
            if (newIndex >= 0) selectedIndex = newIndex;
            AnnounceWithSearch();
        }
        return true;
    }

    public static bool ClearTypeaheadSearch()
    {
        if (typeahead.ClearSearchAndAnnounce())
        {
            AnnounceCurrentItem();
            return true;
        }
        return false;
    }

    public static bool SelectNextMatch()
    {
        if (!typeahead.HasActiveSearch) return false;
        int next = typeahead.GetNextMatch(selectedIndex);
        if (next >= 0) { selectedIndex = next; AnnounceWithSearch(); }
        return true;
    }

    public static bool SelectPreviousMatch()
    {
        if (!typeahead.HasActiveSearch) return false;
        int prev = typeahead.GetPreviousMatch(selectedIndex);
        if (prev >= 0) { selectedIndex = prev; AnnounceWithSearch(); }
        return true;
    }

    // Announcements
    private static void AnnounceCurrentItem()
    {
        if (selectedIndex < 0 || selectedIndex >= items.Count) return;
        var item = items[selectedIndex];
        string position = MenuHelper.FormatPosition(selectedIndex, items.Count);
        string text = item.Label;
        if (!string.IsNullOrEmpty(position)) text += $" ({position})";
        TolkHelper.Speak(text);
    }

    private static void AnnounceWithSearch()
    {
        if (!typeahead.HasActiveSearch) { AnnounceCurrentItem(); return; }
        var item = items[selectedIndex];
        TolkHelper.Speak($"{item.Label}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
    }
}
```

**Keyboard Handling (in Patch or UnifiedKeyboardPatch):**
```csharp
if (MyMenuState.IsActive)
{
    if (keyCode == KeyCode.UpArrow)
    {
        if (MyMenuState.HasActiveSearch)
            MyMenuState.SelectPreviousMatch();
        else
            MyMenuState.NavigateUp();
        Event.current.Use();
    }
    else if (keyCode == KeyCode.DownArrow)
    {
        if (MyMenuState.HasActiveSearch)
            MyMenuState.SelectNextMatch();
        else
            MyMenuState.NavigateDown();
        Event.current.Use();
    }
    else if (keyCode == KeyCode.Home)
    {
        MyMenuState.NavigateHome();
        Event.current.Use();
    }
    else if (keyCode == KeyCode.End)
    {
        MyMenuState.NavigateEnd();
        Event.current.Use();
    }
    else if (keyCode == KeyCode.Escape)
    {
        if (MyMenuState.HasActiveSearch)
            MyMenuState.ClearTypeaheadSearch();
        else
            MyMenuState.Close();
        Event.current.Use();
    }
    else if (keyCode == KeyCode.Backspace)
    {
        if (MyMenuState.HandleTypeaheadBackspace())
            Event.current.Use();
    }
    else if (Event.current.character != '\0' &&
             !Event.current.control && !KeyboardHelper.IsAltHeld &&
             char.IsLetterOrDigit(Event.current.character))
    {
        MyMenuState.HandleTypeahead(Event.current.character);
        Event.current.Use();
    }
}
```

## TreeView (Hierarchical Menu)

**Use `TreeNavigationHelper`** for all treeview implementations. Do NOT implement treeview navigation manually.

TreeNavigationHelper handles all standard WCAG tree keyboard patterns:
- Up/Down navigation (search-aware when typeahead active)
- Left to collapse or go to parent, Right to expand or go to first child
- Home/End for sibling navigation, Ctrl+Home/Ctrl+End for absolute first/last
- Enter to toggle expand/collapse (or custom activate via delegate)
- Space to re-announce current item
- `*` to expand all siblings at current level
- Typeahead search with progressive backspace
- Level change announcements, position announcements, sound effects

**Node Type:** Always use `InspectionTreeItem` (from `Inspection/InspectionTreeItem.cs`).

### Basic Usage

```csharp
public static class MyFeatureState
{
    public static bool IsActive { get; private set; }
    private static TreeNavigationHelper treeNav = new TreeNavigationHelper("MyFeature");

    static MyFeatureState()
    {
        // Optional: customize announcements
        treeNav.FormatItemAnnouncement = item => $"{item.Label} custom format";
        // Optional: custom Enter behavior
        treeNav.OnActivate = item => { DoSomething(item); return true; };
        // Optional: lazy loading
        treeNav.OnBeforeExpand = item => {
            if (item.Children.Count == 0 && item.OnActivate != null)
                item.OnActivate();
        };
    }

    public static void Open(MyData data)
    {
        var root = BuildTree(data); // Your tree-building logic returns InspectionTreeItem
        treeNav.Initialize(root);
        IsActive = true;
        AnnounceOpening(); // Custom opening announcement
    }

    public static void Close()
    {
        treeNav.Reset();
        IsActive = false;
    }

    // Delegate all keyboard input to TreeNavigationHelper
    public static bool HandleInput(Event ev)
    {
        if (!IsActive) return false;

        // Handle feature-specific keys BEFORE standard tree nav
        if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.PageDown)
        {
            JumpToNextSection(); // Domain-specific
            return true;
        }

        // Standard tree navigation
        if (treeNav.HandleInput(ev))
            return true;

        // HandleInput returned false = Escape with no active search
        if (ev.keyCode == KeyCode.Escape)
        {
            Close();
            TolkHelper.Speak("Feature closed");
            return true;
        }

        return true; // Consume all other keys
    }

    private static InspectionTreeItem BuildTree(MyData data)
    {
        var root = new InspectionTreeItem
        {
            Label = "Root",
            IndentLevel = -1,
            IsExpanded = true
        };

        // Add children...
        var category = new InspectionTreeItem
        {
            Type = InspectionTreeItem.ItemType.Category,
            Label = "Category Name",
            IndentLevel = 0,
            IsExpandable = true,
            Parent = root
        };
        root.Children.Add(category);

        return root;
    }
}
```

### Customizing Announcements

Override `FormatItemAnnouncement` to change how items are announced:

```csharp
treeNav.FormatItemAnnouncement = item =>
{
    // Use the default format as a starting point
    string baseAnnouncement = treeNav.DefaultFormatItemAnnouncement(item);
    // Add custom suffix
    return baseAnnouncement + (HasInfoCard(item) ? " Inspectable." : "");
};
```

Override `FormatStateChangeAnnouncement` for a different format after expand/collapse:

```csharp
treeNav.FormatStateChangeAnnouncement = item =>
{
    // Short announcement: just name + state
    return $"{item.Label}, {(item.IsExpanded ? "expanded" : "collapsed")}";
};
```

### Keyboard Routing (in Patch or UnifiedKeyboardPatch)

```csharp
if (MyFeatureState.IsActive)
{
    if (MyFeatureState.HandleInput(Event.current))
    {
        Event.current.Use();
        return;
    }
}
```

### TreeNavigationHelper Configuration Reference

| Property | Default | Purpose |
|----------|---------|---------|
| `FormatItemAnnouncement` | `null` (uses default) | Custom announcement for navigation |
| `FormatStateChangeAnnouncement` | `null` (falls back to item announcement) | Custom announcement after expand/collapse |
| `FormatSearchAnnouncement` | `null` (uses default) | Custom announcement during search |
| `OnActivate` | `null` | Custom Enter key handler (return true = handled) |
| `OnDelete` | `null` | Custom Delete key handler |
| `OnInfo` | `null` | Custom Alt+I handler |
| `OnBeforeExpand` | `null` | Called before expanding (for lazy loading) |
| `AnnounceChildCounts` | `true` | Include "3 items" in expand/collapse announcements |
| `SkipRootInVisibleList` | `true` | Hide root node from navigation |
| `TrackLastChild` | `false` | Remember last visited child per parent |

### Examples in Codebase

| Pattern | Example | Notes |
|---------|---------|-------|
| Non-static wrapper | `FactionTreeNavigation.cs` | Thin wrapper, custom Alt+I |
| Non-static with custom formats | `IdeologyTreeNavigation.cs` | Smart label truncation, ritual sounds |
| Static class, custom node conversion | `ArchitectTreeState.cs` | MenuItem→InspectionTreeItem |
| HandleInput with pre-intercept | `GeneInspectionState.cs` | Page Up/Down, Left arrow label restore |
| Card stacking + float menu | `InfoCardState.cs` | Most complex: nested cards, lazy loading |
| Multiple instances (tabbed) | `XenogermState.cs` | 2 TreeNavigationHelper instances for 2 tree tabs |
| Dual-tab (flat + tree) | `IdeologyNavigationState.cs` | Options flat list + Presets treeview |
| Checkbox tree with sliders | `ThingFilterNavigationState.cs` | Toggle/slider editing modes |

## MenuHelper Reference

Key methods in `UI/MenuHelper.cs`:

| Method | Purpose |
|--------|---------|
| `SelectNext(index, count)` | Returns next index, respects WrapNavigation setting |
| `SelectPrevious(index, count)` | Returns previous index, respects WrapNavigation setting |
| `FormatPosition(index, total)` | Returns "X of Y" string if AnnouncePosition enabled, else empty |
| `GetLevelSuffix(key, level)` | Returns ", level N" only when level changes from last call |
| `ResetLevel(key)` | Resets level tracking (call on menu open/close) |
| `HandleTreeHomeKey(...)` | Home = first sibling, Ctrl+Home = absolute first |
| `HandleTreeEndKey(...)` | End = last sibling, Ctrl+End = absolute last |

## TypeaheadSearchHelper Reference

Key members in `UI/TypeaheadSearchHelper.cs`:

| Member | Purpose |
|--------|---------|
| `HasActiveSearch` | True if search buffer is not empty |
| `SearchBuffer` | Current search string |
| `LastFailedSearch` | Search string that had no matches (for announcement) |
| `MatchCount` | Number of current matches |
| `CurrentMatchPosition` | 1-based position in matches |
| `ProcessCharacterInput(char, labels, out newIndex)` | Add character, find matches, returns false if no matches |
| `ProcessBackspace(labels, out newIndex)` | Remove last char, update matches |
| `GetNextMatch(currentIndex)` | Get next match index (wraps) |
| `GetPreviousMatch(currentIndex)` | Get previous match index (wraps) |
| `ClearSearch()` | Clear buffer silently |
| `ClearSearchAndAnnounce()` | Clear buffer and speak "Search cleared" |

## Examples in Codebase

**Flat Menu:** `ScenarioNavigationState.cs` (scenario list), `WindowlessFloatMenuState.cs`
**TreeView:** `ArchitectTreeState.cs`, `ScenarioNavigationState.cs` (detail panel)
