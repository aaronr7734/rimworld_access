using System.Collections.Generic;
using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

public class CapturedExtrasRowsTests
{
    // ---- RoleFor ----

    [Theory]
    [InlineData(CapturedExtraKind.Checkbox, ElementRole.Checkbox)]
    [InlineData(CapturedExtraKind.RadioButton, ElementRole.RadioButton)]
    [InlineData(CapturedExtraKind.Button, ElementRole.Button)]
    [InlineData(CapturedExtraKind.InvisibleButton, ElementRole.Button)]
    [InlineData(CapturedExtraKind.Slider, ElementRole.Slider)]
    [InlineData(CapturedExtraKind.TextField, ElementRole.TextField)]
    [InlineData(CapturedExtraKind.Tab, ElementRole.Tab)]
    [InlineData(CapturedExtraKind.FillableBar, ElementRole.None)]
    [InlineData(CapturedExtraKind.Label, ElementRole.None)]
    public void RoleFor_MapsEveryKind(CapturedExtraKind kind, ElementRole expected)
    {
        Assert.Equal(expected, CapturedExtrasRows.RoleFor(kind));
    }

    // ---- Expand: no members -> one read-only row ----

    [Fact]
    public void Expand_NoMembers_ProducesOneReadOnlyRow()
    {
        List<CapturedExtraRow> rows = CapturedExtrasRows.Expand("Some label", "Some tip", null);

        CapturedExtraRow row = Assert.Single(rows);
        Assert.Equal("Some label", row.Label);
        Assert.Equal(ElementRole.None, row.Role);
        Assert.Equal("Some tip", row.Tip);
        Assert.True(row.ReadOnly);
    }

    [Fact]
    public void Expand_EmptyMemberList_ProducesOneReadOnlyRow()
    {
        List<CapturedExtraRow> rows = CapturedExtrasRows.Expand("Row text", null, new List<CapturedExtraMember>());
        Assert.Single(rows);
        Assert.True(rows[0].ReadOnly);
    }

    // ---- Expand: one row per member ----

    [Fact]
    public void Expand_OneMember_UsesMemberRawLabelAsLabel()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Button, RawLabel = "Reset" },
        };
        List<CapturedExtraRow> rows = CapturedExtrasRows.Expand("Row text", "Row tip", members);

        CapturedExtraRow row = Assert.Single(rows);
        Assert.Equal("Reset", row.Label);
        Assert.Equal(ElementRole.Button, row.Role);
        Assert.False(row.ReadOnly);
    }

    [Fact]
    public void Expand_MemberWithBlankRawLabel_FallsBackToRowText()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Checkbox, RawLabel = "", Fragment = "Enable feature. checkbox, On" },
        };
        List<CapturedExtraRow> rows = CapturedExtrasRows.Expand("Enable feature", "tip", members);
        Assert.Equal("Enable feature", rows[0].Label);
    }

    [Fact]
    public void Expand_MultipleMembers_OneRowEach_InOrder()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Slider, RawLabel = "Volume" },
            new CapturedExtraMember { Kind = CapturedExtraKind.Button, RawLabel = "Reset" },
        };
        List<CapturedExtraRow> rows = CapturedExtrasRows.Expand("Row text", "Row tip", members);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Volume", rows[0].Label);
        Assert.Equal(ElementRole.Slider, rows[0].Role);
        Assert.Equal("Reset", rows[1].Label);
        Assert.Equal(ElementRole.Button, rows[1].Role);
    }

    [Fact]
    public void Expand_MultipleMembers_OnlyFirstRowKeepsTheFoldedTip()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Slider, RawLabel = "Volume" },
            new CapturedExtraMember { Kind = CapturedExtraKind.Button, RawLabel = "Reset" },
        };
        List<CapturedExtraRow> rows = CapturedExtrasRows.Expand("Row text", "Row tip", members);

        Assert.Equal("Row tip", rows[0].Tip);
        Assert.Null(rows[1].Tip);
    }

    // ---- Kind-specific value shaping ----

    [Fact]
    public void Expand_Checkbox_CarriesCheckState()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Checkbox, Fragment = "Auto-save", Check = CheckState.Checked },
        };
        CapturedExtraRow row = CapturedExtrasRows.Expand("row", null, members)[0];
        Assert.Equal(CheckState.Checked, row.Check);
    }

    [Fact]
    public void Expand_RadioButton_CarriesSelected()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.RadioButton, Fragment = "Option A", Selected = true },
        };
        CapturedExtraRow row = CapturedExtrasRows.Expand("row", null, members)[0];
        Assert.True(row.Selected);
    }

    [Fact]
    public void Expand_Slider_PrefersSliderValueTextOverFormattedValue()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Slider, RawLabel = "Volume", SliderValueText = "+50%", SliderValue = 0.5f },
        };
        CapturedExtraRow row = CapturedExtrasRows.Expand("row", null, members)[0];
        Assert.Equal("+50%", row.Value);
    }

    [Fact]
    public void Expand_Slider_FormatsRawValueWhenNoValueText()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Slider, RawLabel = "Volume", SliderValueText = "", SliderValue = 3f },
        };
        CapturedExtraRow row = CapturedExtrasRows.Expand("row", null, members)[0];
        Assert.Equal("3", row.Value);
    }

    [Fact]
    public void Expand_Slider_FractionalValueFormatsToTwoDecimals()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Slider, RawLabel = "Volume", SliderValueText = "", SliderValue = 0.25f },
        };
        CapturedExtraRow row = CapturedExtrasRows.Expand("row", null, members)[0];
        Assert.Equal("0.25", row.Value);
    }

    [Theory]
    [InlineData(0f, 0f, 10f, true, false)]
    [InlineData(10f, 0f, 10f, false, true)]
    [InlineData(5f, 0f, 10f, false, false)]
    public void Expand_Slider_AtMinimumAtMaximumFlags(float value, float min, float max, bool atMin, bool atMax)
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Slider, Fragment = "V", SliderValue = value, SliderMin = min, SliderMax = max },
        };
        CapturedExtraRow row = CapturedExtrasRows.Expand("row", null, members)[0];
        Assert.Equal(atMin, row.AtMinimum);
        Assert.Equal(atMax, row.AtMaximum);
    }

    [Fact]
    public void Expand_TextField_UsesTextValue()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.TextField, Fragment = "", TextValue = "hello", ValueBlank = false },
        };
        CapturedExtraRow row = CapturedExtrasRows.Expand("row", null, members)[0];
        Assert.Equal("hello", row.Value);
        Assert.False(row.ValueBlank);
    }

    [Fact]
    public void Expand_TextField_BlankValue_NoValueText()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.TextField, Fragment = "", ValueBlank = true },
        };
        CapturedExtraRow row = CapturedExtrasRows.Expand("row", null, members)[0];
        Assert.Null(row.Value);
        Assert.True(row.ValueBlank);
    }

    [Fact]
    public void Expand_TextField_BlankRawLabel_DoesNotFallBackToRowText()
    {
        // A captionless TextField's own row text IS
        // already its role+state phrase ("edit box, blank") — falling back to it here
        // (the general blank-RawLabel rule every other kind uses) spoke that phrase as
        // the Label, then the Role+State grammar spoke it again right after
        // ("edit box, blank. edit box, blank. 4 of 15").
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.TextField, RawLabel = "", ValueBlank = true },
        };
        CapturedExtraRow row = CapturedExtrasRows.Expand("edit box, blank", "tip", members)[0];
        Assert.Equal("", row.Label);
    }

    [Fact]
    public void Expand_Checkbox_BlankRawLabel_StillFallsBackToRowText()
    {
        // The B4 fix is scoped to TextField only — every other kind's existing
        // fallback (Expand_MemberWithBlankRawLabel_FallsBackToRowText above) must
        // survive untouched.
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Checkbox, RawLabel = "", Check = CheckState.Checked },
        };
        CapturedExtraRow row = CapturedExtrasRows.Expand("Ideology", "tip", members)[0];
        Assert.Equal("Ideology", row.Label);
    }

    [Fact]
    public void Expand_Caption_NamesABareTextField()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.TextField, RawLabel = "", ValueBlank = true },
        };
        CapturedExtraRow row = CapturedExtrasRows.Expand(
            "Class name, edit box, blank", "tip", members, "Class name, ")[0];
        Assert.Equal("Class name", row.Label);
        Assert.True(row.ValueBlank);
    }

    [Fact]
    public void Expand_Caption_BecomesTheNameAndTheControlTextTheValue()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Button, RawLabel = "Classroom 1" },
        };
        CapturedExtraRow row = CapturedExtrasRows.Expand(
            "Classroom, Classroom 1", null, members, "Classroom, ")[0];
        Assert.Equal("Classroom", row.Label);
        Assert.Equal("Classroom 1", row.Value);
        Assert.Equal(ElementRole.Button, row.Role);
    }

    [Fact]
    public void Expand_Caption_NamesEveryControlSharingIt()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Button, RawLabel = "8" },
            new CapturedExtraMember { Kind = CapturedExtraKind.Button, RawLabel = "15" },
        };
        List<CapturedExtraRow> rows = CapturedExtrasRows.Expand(
            "Class hours, 8, 15", null, members, "Class hours, , ");
        Assert.Equal("Class hours", rows[0].Label);
        Assert.Equal("8", rows[0].Value);
        Assert.Equal("Class hours", rows[1].Label);
        Assert.Equal("15", rows[1].Value);
    }

    [Fact]
    public void Expand_Slider_BlankRawLabel_DoesNotFallBackToRowText()
    {
        // Same trap as the bare TextField: the row text IS the composed role phrase.
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Slider, RawLabel = "", SliderValue = 9000f, SliderMin = 1000f, SliderMax = 100000f },
        };
        CapturedExtraRow row = CapturedExtrasRows.Expand("slider, 9000", null, members)[0];
        Assert.Equal("", row.Label);
        Assert.Equal("9000", row.Value);
    }

    [Fact]
    public void Expand_FillableBar_ReadOnlyWithPercentValue()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.FillableBar, Fragment = "", FillPercent = 0.42f },
        };
        CapturedExtraRow row = CapturedExtrasRows.Expand("row", null, members)[0];
        Assert.True(row.ReadOnly);
        Assert.Equal(ElementRole.None, row.Role);
        Assert.Equal("42%", row.Value);
    }

    [Fact]
    public void Expand_Disabled_CarriesThrough()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Button, RawLabel = "Reset", Disabled = true },
        };
        CapturedExtraRow row = CapturedExtrasRows.Expand("row", null, members)[0];
        Assert.True(row.Disabled);
    }

    // ---- Describe: the one row -> ElementDescription mapping ----

    [Fact]
    public void Describe_MapsEveryField_TipBecomesExtras()
    {
        var row = new CapturedExtraRow
        {
            Label = "Growth speed",
            Role = ElementRole.Slider,
            Tip = "How fast plants grow.",
            Disabled = true,
            ReadOnly = true,
            Check = CheckState.PartiallyChecked,
            Selected = true,
            Value = "40 percent",
            ValueBlank = true,
            AtMinimum = true,
            AtMaximum = true,
        };

        ElementDescription d = CapturedExtrasRows.Describe(row);

        Assert.Equal("Growth speed", d.Label);
        Assert.Equal(ElementRole.Slider, d.Role);
        Assert.Equal("How fast plants grow.", d.Extras);
        Assert.True(d.Disabled);
        Assert.True(d.ReadOnly);
        Assert.Equal(CheckState.PartiallyChecked, d.Check);
        Assert.True(d.Selected);
        Assert.Equal("40 percent", d.Value);
        Assert.True(d.ValueBlank);
        Assert.True(d.AtMinimum);
        Assert.True(d.AtMaximum);
    }

    [Fact]
    public void Describe_NoTip_LeavesExtrasNull()
    {
        ElementDescription d = CapturedExtrasRows.Describe(new CapturedExtraRow { Label = "Close" });
        Assert.Null(d.Extras);
        Assert.Equal("Close", d.Label);
    }

    [Fact]
    public void Expand_Caption_RightOfLabeledButtonsNamesNoneOfThem()
    {
        const string caption = "Tip: Describe your colony status in natural language.";
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Button, RawLabel = "Save Overview", CaptionLeads = false },
            new CapturedExtraMember { Kind = CapturedExtraKind.Button, RawLabel = "AI Summary", CaptionLeads = false },
        };
        List<CapturedExtraRow> rows = CapturedExtrasRows.Expand("Save Overview, AI Summary, " + caption, null, members, caption);
        Assert.Equal("Save Overview", rows[0].Label);
        Assert.Null(rows[0].Value);
        Assert.Equal("AI Summary", rows[1].Label);
        Assert.True(CapturedExtrasRows.CaptionLeftOver(members, caption));
    }

    [Fact]
    public void Expand_CounterLine_NeverNamesALabeledButton()
    {
        const string caption = "ABM 0 · SCM 0 · ELS 0 · CLPA 0";
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Button, RawLabel = "Tools" },
        };
        CapturedExtraRow row = CapturedExtrasRows.Expand(caption + ", Tools", null, members, caption)[0];
        Assert.Equal("Tools", row.Label);
        Assert.True(CapturedExtrasRows.CaptionLeftOver(members, caption));
    }

    [Fact]
    public void CaptionLeftOver_FalseWhenAnyControlTakesIt()
    {
        var members = new List<CapturedExtraMember>
        {
            new CapturedExtraMember { Kind = CapturedExtraKind.Button, RawLabel = "Tools", CaptionLeads = false },
            new CapturedExtraMember { Kind = CapturedExtraKind.TextField, RawLabel = "", ValueBlank = true },
        };
        Assert.False(CapturedExtrasRows.CaptionLeftOver(members, "Search"));
        Assert.False(CapturedExtrasRows.CaptionLeftOver(members, null));
    }
}
