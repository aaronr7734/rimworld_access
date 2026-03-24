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

**Required Features:**
- Up/Down navigation through visible (flattened) items
- Left to collapse or go to parent
- Right to expand or go to first child
- Home/End for sibling navigation
- Ctrl+Home/Ctrl+End for absolute first/last
- Typeahead search with match navigation
- Level change announcements
- Expand/collapse state announcements

**Data Structure:**
```csharp
private class TreeItem
{
    public string Label { get; set; }
    public int IndentLevel { get; set; }
    public bool IsExpandable { get; set; }
    public bool IsExpanded { get; set; }
    public List<TreeItem> Children { get; set; } = new List<TreeItem>();
    public TreeItem Parent { get; set; }
}

private static List<TreeItem> hierarchy = new List<TreeItem>();      // Root items
private static List<TreeItem> flattenedItems = new List<TreeItem>(); // Visible items
private static int selectedIndex = 0;
private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
private const string LevelTrackingKey = "MyTreeView"; // Unique key for level tracking
```

**Flattening (rebuild when expand/collapse changes):**
```csharp
private static void FlattenItems()
{
    flattenedItems.Clear();
    foreach (var item in hierarchy)
    {
        flattenedItems.Add(item);
        if (item.IsExpandable && item.IsExpanded)
        {
            foreach (var child in item.Children)
                flattenedItems.Add(child);
        }
    }
}
```

**Navigation Methods:**
```csharp
public static void NavigateUp()
{
    typeahead.ClearSearch();
    selectedIndex = MenuHelper.SelectPrevious(selectedIndex, flattenedItems.Count);
    AnnounceCurrentItem();
}

public static void NavigateDown()
{
    typeahead.ClearSearch();
    selectedIndex = MenuHelper.SelectNext(selectedIndex, flattenedItems.Count);
    AnnounceCurrentItem();
}

public static void ExpandOrDrillDown()
{
    typeahead.ClearSearch();
    var item = flattenedItems[selectedIndex];
    if (item.IsExpandable)
    {
        if (!item.IsExpanded)
        {
            item.IsExpanded = true;
            FlattenItems();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }
        else if (item.Children.Count > 0)
        {
            // Move to first child
            selectedIndex = flattenedItems.IndexOf(item.Children[0]);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }
    }
    else
    {
        SoundDefOf.ClickReject.PlayOneShotOnCamera();
    }
}

public static void CollapseOrDrillUp()
{
    typeahead.ClearSearch();
    var item = flattenedItems[selectedIndex];
    if (item.IsExpandable && item.IsExpanded)
    {
        item.IsExpanded = false;
        FlattenItems();
        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        AnnounceCurrentItem();
    }
    else if (item.Parent != null)
    {
        selectedIndex = flattenedItems.IndexOf(item.Parent);
        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        AnnounceCurrentItem();
    }
    else
    {
        SoundDefOf.ClickReject.PlayOneShotOnCamera();
    }
}

// Home/End use MenuHelper's tree navigation
public static void HandleHomeKey(bool ctrlPressed)
{
    MenuHelper.HandleTreeHomeKey(
        flattenedItems, ref selectedIndex,
        item => item.IndentLevel,
        ctrlPressed,
        () => { typeahead.ClearSearch(); AnnounceCurrentItem(); });
}

public static void HandleEndKey(bool ctrlPressed)
{
    MenuHelper.HandleTreeEndKey(
        flattenedItems, ref selectedIndex,
        item => item.IndentLevel,
        item => item.IsExpanded,
        item => item.IsExpandable && item.Children.Count > 0,
        ctrlPressed,
        () => { typeahead.ClearSearch(); AnnounceCurrentItem(); });
}
```

**TreeView Announcements:**
```csharp
private static void AnnounceCurrentItem()
{
    var item = flattenedItems[selectedIndex];
    var (position, total) = GetSiblingPosition(item);
    string positionPart = MenuHelper.FormatPosition(position - 1, total);
    string announcement;

    if (item.IsExpandable)
    {
        string state = item.IsExpanded ? "expanded" : "collapsed";
        string itemCount = item.Children.Count == 1 ? "1 item" : $"{item.Children.Count} items";
        announcement = $"{item.Label}, {state}, {itemCount}";
    }
    else
    {
        announcement = item.Label;
    }

    if (!string.IsNullOrEmpty(positionPart))
        announcement += $" ({positionPart})";

    // Add level change suffix (only announces when level changes)
    announcement += MenuHelper.GetLevelSuffix(LevelTrackingKey, item.IndentLevel);

    TolkHelper.Speak(announcement);
}

private static (int position, int total) GetSiblingPosition(TreeItem item)
{
    var siblings = item.Parent?.Children ?? hierarchy;
    int position = siblings.IndexOf(item) + 1;
    return (position, siblings.Count);
}
```

**TreeView Keyboard Handling:**
```csharp
if (keyCode == KeyCode.UpArrow)
{
    if (HasActiveSearch) SelectPreviousMatch();
    else NavigateUp();
    Event.current.Use();
}
else if (keyCode == KeyCode.DownArrow)
{
    if (HasActiveSearch) SelectNextMatch();
    else NavigateDown();
    Event.current.Use();
}
else if (keyCode == KeyCode.RightArrow)
{
    ExpandOrDrillDown();
    Event.current.Use();
}
else if (keyCode == KeyCode.LeftArrow)
{
    CollapseOrDrillUp();
    Event.current.Use();
}
else if (keyCode == KeyCode.Home)
{
    HandleHomeKey(Event.current.control);
    Event.current.Use();
}
else if (keyCode == KeyCode.End)
{
    HandleEndKey(Event.current.control);
    Event.current.Use();
}
// ... Escape, Backspace, character input same as flat menu
```

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
