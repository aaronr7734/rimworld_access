using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

/// <summary>Fixed-word vocabulary standing in for the translated one.</summary>
internal sealed class FakeVocabulary : IShellVocabulary
{
    public string RoleWord(ElementRole role)
    {
        switch (role)
        {
            case ElementRole.Button: return "button";
            case ElementRole.Checkbox: return "checkbox";
            case ElementRole.RadioButton: return "radio button";
            case ElementRole.ComboBox: return "combo box";
            case ElementRole.Slider: return "slider";
            case ElementRole.Stepper: return "spin box";
            case ElementRole.TextField: return "edit box";
            case ElementRole.Tab: return "tab";
            default: return "";
        }
    }

    public string Word(ElementStateWord word)
    {
        switch (word)
        {
            case ElementStateWord.Checked: return "checked";
            case ElementStateWord.Unchecked: return "not checked";
            case ElementStateWord.PartiallyChecked: return "partially checked";
            case ElementStateWord.Selected: return "selected";
            case ElementStateWord.NotSelected: return "not selected";
            case ElementStateWord.Expanded: return "expanded";
            case ElementStateWord.Collapsed: return "collapsed";
            case ElementStateWord.Blank: return "blank";
            case ElementStateWord.Disabled: return "disabled";
            case ElementStateWord.ReadOnly: return "read only";
            case ElementStateWord.AtMinimum: return "at minimum";
            case ElementStateWord.AtMaximum: return "at maximum";
            case ElementStateWord.ColumnHeader: return "column header";
            case ElementStateWord.Sortable: return "sortable";
            case ElementStateWord.SortedAscending: return "sorted ascending";
            case ElementStateWord.SortedDescending: return "sorted descending";
            default: return "";
        }
    }

    public string Position(int index, int count) => index + " of " + count;

    public string TabPosition(int index, int count) => "tab " + index + " of " + count;

    public string Level(int level) => "level " + level;

    public string RowPosition(int index, int count) => "row " + index + " of " + count;

    public string ColumnPosition(int index, int count) => "column " + index + " of " + count;

    public string TableDimensions(int columns, int rows) => "table, " + columns + " columns, " + rows + " rows";

    public string TabCount(int count) => count + " tabs";
}

public class AnnouncementComposerTests
{
    private static readonly FakeVocabulary Vocab = new();

    private static string Focus(ElementDescription d) =>
        AnnouncementComposer.ComposeFocus(d, Vocab, ComposeOptions.AllOn);

    [Fact]
    public void DefaultOrder_LevelLabelHotkeyRoleThenStateExtrasHintPosition()
    {
        var d = new ElementDescription
        {
            Label = "Allow fresh corpses",
            Hotkey = "Alt+C",
            Role = ElementRole.Checkbox,
            Check = CheckState.Checked,
            PositionIndex = 3,
            PositionCount = 7,
            Extras = "Includes rotten ones",
        };

        Assert.Equal(
            "Allow fresh corpses. Alt+C. checkbox. checked. Includes rotten ones. 3 of 7",
            Focus(d));
    }

    [Fact]
    public void Role_ButtonHasNoStateWord()
    {
        Assert.Equal("Accept. button",
            Focus(new ElementDescription { Label = "Accept", Role = ElementRole.Button }));
    }

    [Fact]
    public void SilentRoles_SpeakNoRoleWord()
    {
        Assert.Equal("Chop wood",
            Focus(new ElementDescription { Label = "Chop wood", Role = ElementRole.MenuItem }));

        Assert.Equal("level 2. Weapons. expanded",
            Focus(new ElementDescription
            {
                Label = "Weapons",
                Role = ElementRole.TreeItem,
                Expanded = true,
                Level = 2,
            }));
    }

    [Fact]
    public void ComboBox_SpeaksCurrentValueAsState()
    {
        Assert.Equal("Diet. combo box. Lavish meals",
            Focus(new ElementDescription
            {
                Label = "Diet",
                Role = ElementRole.ComboBox,
                Value = "Lavish meals",
            }));
    }

    [Fact]
    public void TextField_BlankSpeaksBlankInsteadOfValue()
    {
        Assert.Equal("Name. edit box. blank",
            Focus(new ElementDescription
            {
                Label = "Name",
                Role = ElementRole.TextField,
                ValueBlank = true,
            }));
    }

    [Fact]
    public void Stepper_SpeaksValueAndBound()
    {
        Assert.Equal("Target fuel. spin box. 30, at maximum",
            Focus(new ElementDescription
            {
                Label = "Target fuel",
                Role = ElementRole.Stepper,
                Value = "30",
                AtMaximum = true,
            }));
    }

    [Fact]
    public void Disabled_AppendsToStatePart()
    {
        Assert.Equal("Launch. button. disabled",
            Focus(new ElementDescription { Label = "Launch", Role = ElementRole.Button, Disabled = true }));

        // Disabled speaks even on silent roles.
        Assert.Equal("Chop wood. disabled",
            Focus(new ElementDescription { Label = "Chop wood", Role = ElementRole.MenuItem, Disabled = true }));
    }

    [Fact]
    public void ReadOnly_SilentForRolelessLabel()
    {
        // "Read only" is a control state; a plain label / heading (no role) was
        // never adjustable, so the word would mislead a screen-reader user into
        // expecting a control. It must not be spoken here.
        Assert.Equal("New faction: Cannibal tribe",
            Focus(new ElementDescription { Label = "New faction: Cannibal tribe", ReadOnly = true }));
    }

    [Fact]
    public void ReadOnly_SpokenForNonAdjustableControl()
    {
        // A real control that happens to be fixed keeps the cue.
        Assert.Equal("Launch. button. read only",
            Focus(new ElementDescription { Label = "Launch", Role = ElementRole.Button, ReadOnly = true }));
    }

    [Fact]
    public void Cell_ReadOnly_SilentForRolelessLabel()
    {
        // Same rule in the table/cell grammar: a roleless label row carries no
        // "read only" annotation.
        Assert.Equal("New faction: Cannibal tribe",
            Cell(new ElementDescription
            {
                Label = "New faction: Cannibal tribe",
                Axis = CellAxis.Entry,
                ReadOnly = true,
            }));
    }

    /// <summary>
    /// A tab's ordinal carries the word "tab": bare, it lands beside the focused item's own
    /// ordinal and the two cannot be told apart ("Server Browser. 2 of 11. 1 of 2").
    /// </summary>
    [Fact]
    public void Tab_SelectedWithPosition()
    {
        Assert.Equal("Hair. tab. selected. tab 1 of 4",
            Focus(new ElementDescription
            {
                Label = "Hair",
                Role = ElementRole.Tab,
                Selected = true,
                PositionIndex = 1,
                PositionCount = 4,
            }));
    }

    [Fact]
    public void Options_GatePositionAndHints()
    {
        var d = new ElementDescription
        {
            Label = "Colonist",
            Role = ElementRole.RadioButton,
            Selected = false,
            PositionIndex = 2,
            PositionCount = 5,
            Hint = "Press Enter to select",
        };

        Assert.Equal("Colonist. radio button. not selected. Press Enter to select. 2 of 5",
            Focus(d));

        // Position/hints gate independently of label/role/state — start from AllOn
        // (not a bare default, now that label/hotkey/role/state/extras have their own
        // suppress gates) and flip only the two under test.
        ComposeOptions terse = ComposeOptions.AllOn;
        terse.IncludePosition = false;
        terse.IncludeHints = false;
        Assert.Equal("Colonist. radio button. not selected",
            AnnouncementComposer.ComposeFocus(d, Vocab, terse));
    }

    [Fact]
    public void Join_SkipsDoublePunctuation()
    {
        Assert.Equal("Chop wood. Fells the tree.",
            Focus(new ElementDescription { Label = "Chop wood.", Extras = "Fells the tree." }));
    }

    [Fact]
    public void StateChange_SpeaksOnlyTheNewState()
    {
        Assert.Equal("checked",
            AnnouncementComposer.ComposeStateChange(
                new ElementDescription { Label = "Allow", Role = ElementRole.Checkbox, Check = CheckState.Checked },
                Vocab));

        Assert.Equal("40 percent",
            AnnouncementComposer.ComposeStateChange(
                new ElementDescription { Label = "Volume", Role = ElementRole.Slider, Value = "40 percent" },
                Vocab));

        Assert.Equal("3, at minimum",
            AnnouncementComposer.ComposeStateChange(
                new ElementDescription { Role = ElementRole.Stepper, Value = "3", AtMinimum = true },
                Vocab));

        Assert.Equal("",
            AnnouncementComposer.ComposeStateChange(
                new ElementDescription { Label = "Plain", Role = ElementRole.Button },
                Vocab));
    }

    /// <summary>
    /// The shape a gizmo row fills: a Command_Toggle carries its hotkey, its
    /// live check state, its position in the command list, and its description
    /// as the verbose tail — and, when vanilla has gated it, the disabled state
    /// word with the game's own reason leading the tail.
    /// </summary>
    [Fact]
    public void ToggleGizmoShape_HotkeyCheckStatePositionAndTail()
    {
        Assert.Equal("Hold open. Shift+H. checkbox. checked. Keeps the door open. 2 of 6",
            Focus(new ElementDescription
            {
                Label = "Hold open",
                Hotkey = "Shift+H",
                Role = ElementRole.Checkbox,
                Check = CheckState.Checked,
                PositionIndex = 2,
                PositionCount = 6,
                Extras = "Keeps the door open.",
            }));

        Assert.Equal("Hold open. Shift+H. checkbox. not checked, disabled. Needs power. Keeps the door open. 2 of 6",
            Focus(new ElementDescription
            {
                Label = "Hold open",
                Hotkey = "Shift+H",
                Role = ElementRole.Checkbox,
                Check = CheckState.Unchecked,
                Disabled = true,
                PositionIndex = 2,
                PositionCount = 6,
                Extras = "Needs power. Keeps the door open.",
            }));
    }

    [Fact]
    public void EmptyFragments_AreSkippedEntirely()
    {
        Assert.Equal("Just a label", Focus(new ElementDescription { Label = "Just a label" }));
        Assert.Equal("", Focus(new ElementDescription()));
    }

    // ------------------------------------------------------------------
    // Configure Spoken Announcements: PartOrder + Suppress* gates.
    // ------------------------------------------------------------------

    [Fact]
    public void NullPartOrder_UsesDefaultOrder()
    {
        var d = new ElementDescription
        {
            Label = "Allow fresh corpses",
            Hotkey = "Alt+C",
            Role = ElementRole.Checkbox,
            Check = CheckState.Checked,
            PositionIndex = 3,
            PositionCount = 7,
            Extras = "Includes rotten ones",
        };
        var options = ComposeOptions.AllOn;
        options.PartOrder = null;

        Assert.Equal(
            "Allow fresh corpses. Alt+C. checkbox. checked. Includes rotten ones. 3 of 7",
            AnnouncementComposer.ComposeFocus(d, Vocab, options));
    }

    [Fact]
    public void CustomPartOrder_IsRespected()
    {
        // Position first, then label, then extras — a reordering nobody would pick, but it
        // proves ComposeFocus actually walks PartOrder rather than the hardcoded sequence.
        var d = new ElementDescription
        {
            Label = "Allow fresh corpses",
            PositionIndex = 3,
            PositionCount = 7,
            Extras = "Includes rotten ones",
        };
        var options = ComposeOptions.AllOn;
        options.PartOrder = new[] { AnnouncementPart.Position, AnnouncementPart.Label, AnnouncementPart.Extras };

        Assert.Equal("3 of 7. Allow fresh corpses. Includes rotten ones",
            AnnouncementComposer.ComposeFocus(d, Vocab, options));
    }

    [Fact]
    public void SuppressedParts_AreSkipped()
    {
        var d = new ElementDescription
        {
            Label = "Allow fresh corpses",
            Hotkey = "Alt+C",
            Role = ElementRole.Checkbox,
            Check = CheckState.Checked,
            Extras = "Includes rotten ones",
        };
        var options = ComposeOptions.AllOn;
        options.SuppressHotkey = true;
        options.SuppressExtras = true;

        Assert.Equal("Allow fresh corpses. checkbox. checked",
            AnnouncementComposer.ComposeFocus(d, Vocab, options));
    }

    [Fact]
    public void EveryPartSuppressed_FallsBackToLabelInsteadOfSilence()
    {
        var d = new ElementDescription
        {
            Label = "Allow fresh corpses",
            Hotkey = "Alt+C",
            Role = ElementRole.Checkbox,
            Check = CheckState.Checked,
            PositionIndex = 3,
            PositionCount = 7,
            Extras = "Includes rotten ones",
            Hint = "Press Space to toggle",
        };
        var options = ComposeOptions.AllOn;
        options.SuppressLabel = true;
        options.SuppressHotkey = true;
        options.SuppressRole = true;
        options.SuppressState = true;
        options.SuppressExtras = true;
        options.IncludeLevels = false;
        options.IncludePosition = false;
        options.IncludeHints = false;

        // Every gate is off, but the item still has a label — a blind user must
        // never lose the item entirely to their own verbosity settings.
        Assert.Equal("Allow fresh corpses",
            AnnouncementComposer.ComposeFocus(d, Vocab, options));

        // A genuinely label-less item still legitimately composes to "".
        Assert.Equal("", AnnouncementComposer.ComposeFocus(
            new ElementDescription { Hotkey = "Alt+C" }, Vocab, options));
    }

    [Fact]
    public void DefaultComposeOptions_NeverSuppressesAnything()
    {
        // The dozen-plus call sites across the codebase that build a bare
        // default(ComposeOptions) and only set IncludePosition/IncludeHints/etc. must keep
        // speaking labels, hotkeys, role, state, and extras exactly as before — the new
        // Suppress* gates default false (opt-OUT), never opt-in, precisely so this holds.
        ComposeOptions options = default;
        options.IncludePosition = true;
        options.IncludeHints = true;
        options.IncludeLevels = true;

        var d = new ElementDescription
        {
            Label = "Allow fresh corpses",
            Hotkey = "Alt+C",
            Role = ElementRole.Checkbox,
            Check = CheckState.Checked,
            PositionIndex = 3,
            PositionCount = 7,
            Extras = "Includes rotten ones",
        };
        Assert.Equal(
            "Allow fresh corpses. Alt+C. checkbox. checked. Includes rotten ones. 3 of 7",
            AnnouncementComposer.ComposeFocus(d, Vocab, options));
    }

    // ------------------------------------------------------------------
    // ComposeCell — the table delta grammar (the table-model doctrine).
    // ------------------------------------------------------------------

    private static string Cell(ElementDescription d) =>
        AnnouncementComposer.ComposeCell(d, Vocab, ComposeOptions.AllOn);

    private static ElementDescription DataCell(CellAxis axis) => new()
    {
        Role = ElementRole.TableCell,
        Axis = axis,
        Label = "Muffalo",
        ColumnName = "Mass",
        Value = "60 kg",
        ColumnTooltip = "Total mass carried",
        RowIndex = 3,
        RowCount = 8,
        ColumnIndex = 2,
        ColumnCount = 5,
    };

    [Fact]
    public void Cell_Entry_SpeaksBothAxes()
    {
        Assert.Equal(
            "Muffalo. Mass. 60 kg. Total mass carried. row 3 of 8. column 2 of 5",
            Cell(DataCell(CellAxis.Entry)));
    }

    [Fact]
    public void Cell_RowMove_SkipsColumnNameAndTooltip()
    {
        Assert.Equal("Muffalo. 60 kg. row 3 of 8", Cell(DataCell(CellAxis.Row)));
    }

    [Fact]
    public void Cell_CellTip_SpeaksAfterColumnTooltip_BeforePositions()
    {
        var d = DataCell(CellAxis.Entry);
        d.Extras = "Includes rotten ones";
        Assert.Equal(
            "Muffalo. Mass. 60 kg. Total mass carried. Includes rotten ones. row 3 of 8. column 2 of 5",
            Cell(d));
    }

    [Fact]
    public void Cell_CellTipContainingColumnTooltip_SpeaksItOnce()
    {
        var d = DataCell(CellAxis.Entry);
        d.Extras = "Total mass carried. Includes rotten ones";
        Assert.Equal(
            "Muffalo. Mass. 60 kg. Total mass carried. Includes rotten ones. row 3 of 8. column 2 of 5",
            Cell(d));
    }

    [Fact]
    public void Cell_ColumnTooltipRepeatingColumnName_IsDropped()
    {
        var d = DataCell(CellAxis.Entry);
        d.ColumnTooltip = "Mass";
        Assert.Equal("Muffalo. Mass. 60 kg. row 3 of 8. column 2 of 5", Cell(d));
    }

    [Fact]
    public void Cell_ColumnMove_SkipsRowIdentity_SpeaksTooltipBeforePosition()
    {
        Assert.Equal(
            "Mass. 60 kg. Total mass carried. column 2 of 5",
            Cell(DataCell(CellAxis.Column)));
    }

    [Fact]
    public void Cell_ButtonColumn_SpeaksTheRoleWord()
    {
        var d = new ElementDescription
        {
            Role = ElementRole.Button,
            Axis = CellAxis.Column,
            Label = "Area 1",
            ColumnName = "Expand",
            ColumnIndex = 2,
            ColumnCount = 7,
        };
        Assert.Equal("Expand. button. column 2 of 7", Cell(d));
    }

    [Fact]
    public void Cell_IdentityColumn_DoesNotEchoTheLabelAsValue()
    {
        var d = DataCell(CellAxis.Row);
        d.ColumnName = "Name";
        d.Value = "Muffalo";
        Assert.Equal("Muffalo. row 3 of 8", Cell(d));
    }

    [Fact]
    public void Cell_PositionGate_SilencesRowColumnFragments()
    {
        var options = ComposeOptions.AllOn;
        options.IncludeRowColumnPosition = false;
        Assert.Equal(
            "Muffalo. Mass. 60 kg. Total mass carried",
            AnnouncementComposer.ComposeCell(DataCell(CellAxis.Entry), Vocab, options));
    }

    [Fact]
    public void HeaderCell_Sortable_SpeaksHeaderWordAndSortability()
    {
        var d = new ElementDescription
        {
            Role = ElementRole.TableCell,
            Axis = CellAxis.Column,
            ColumnName = "Mass",
            IsHeaderCell = true,
            Sortable = true,
            ColumnTooltip = "Total mass carried",
            RowIndex = 1,
            RowCount = 8,
            ColumnIndex = 2,
            ColumnCount = 5,
            Hint = "Enter sorts",
        };
        Assert.Equal(
            "Mass. column header, sortable. Total mass carried. column 2 of 5. Enter sorts",
            Cell(d));
    }

    [Fact]
    public void HeaderCell_Sorted_SpeaksDirectionInsteadOfSortable()
    {
        var d = new ElementDescription
        {
            Role = ElementRole.TableCell,
            Axis = CellAxis.Entry,
            ColumnName = "Mass",
            IsHeaderCell = true,
            Sortable = true,
            SortDescending = true,
            RowIndex = 1,
            RowCount = 8,
            ColumnIndex = 2,
            ColumnCount = 5,
        };
        Assert.Equal(
            "Mass. column header, sorted descending. row 1 of 8. column 2 of 5",
            Cell(d));
    }
}
