using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class SliderStepTests
{
    [Fact]
    public void RepeatedSteps_NeverAccumulateDust()
    {
        // Live QA repro: thirteen 5% steps on a 0..1 slider landed on
        // 0.65000004, which the mod's own "value*100%" caption rendered as
        // "65.00001%". Stepping moves from the CURRENT value (never snaps it
        // to the step grid -- the CMR reversibility bug), but every result is
        // rounded onto the fine decimal grid, so binary dust never accumulates
        // across presses.
        float value = 0f;
        for (int i = 0; i < 13; i++)
        {
            value = SliderStep.Stepped(value, 1, 0f, 1f, 0f);
        }
        Assert.Equal(0.65f, value);
    }

    [Fact]
    public void SingleStep_FromDustyValue_LandsDecimalClean()
    {
        // Live QA repro: stepping DOWN from a dusty 0.15000001 spoke
        // "5.000001%". The fine decimal rounding cleans the dust in one press
        // without snapping the value onto the coarse step grid.
        float dusty = 0.15000001f;
        float stepped = SliderStep.Stepped(dusty, -1, 0f, 1f, 0f);
        Assert.Equal(0.1f, stepped);
    }

    [Fact]
    public void StepThenStepBack_RestoresTheExactOriginalValue()
    {
        // The live trace defect: Colony Manager Redux's 0..3000 threshold slider
        // starts at 500, which does not sit on this reader's own invented 150-unit
        // step grid for that range. Right then Left (or Left then Right) must
        // always restore 500 exactly -- the old algorithm landed on 450 instead.
        float original = 500f;
        Assert.Equal(original, SliderStep.Stepped(SliderStep.Stepped(original, 1, 0f, 3000f, 0f), -1, 0f, 3000f, 0f));
        Assert.Equal(original, SliderStep.Stepped(SliderStep.Stepped(original, -1, 0f, 3000f, 0f), 1, 0f, 3000f, 0f));
    }

    [Fact]
    public void OffGridStartingValue_StepsByExactlyOneStepUnit()
    {
        Assert.Equal(650f, SliderStep.Stepped(500f, 1, 0f, 3000f, 0f));
        Assert.Equal(350f, SliderStep.Stepped(500f, -1, 0f, 3000f, 0f));
    }

    [Fact]
    public void RoundToDeclared_StepsByRoundTo()
    {
        Assert.Equal(0.75f, SliderStep.Stepped(0.5f, 1, 0f, 1f, 0.25f));
    }

    [Fact]
    public void StepPastMaximum_ClampsAndHolds()
    {
        Assert.Equal(1f, SliderStep.Stepped(1f, 1, 0f, 1f, 0f));
    }

    [Fact]
    public void StepPastMinimum_ClampsAndHolds()
    {
        Assert.Equal(0f, SliderStep.Stepped(0f, -1, 0f, 1f, 0f));
    }

    [Fact]
    public void OffsetMinimum_GridStartsAtMin()
    {
        // Fractional bounds 1.5..301.5 with no roundTo: step = 15 from the
        // range/20 default. The grid is anchored at min, so one step from min is
        // min + step. (Non-integer bounds keep the twentieth-of-range default;
        // whole-number bounds now step by 1 -- see IntegerSlider tests.)
        Assert.Equal(16.5f, SliderStep.Stepped(1.5f, 1, 1.5f, 301.5f, 0f), 3);
    }

    [Fact]
    public void IntegerSlider_ReachesEveryWholeNumber()
    {
        // Live QA (Vanilla Expanded Framework new-faction settlements): 1..64
        // with no declared roundTo. The old (max-min)/20 = 3.15 step landed
        // between integers, so 16 was unreachable and the announced whole number
        // never matched a stop. Whole-number bounds now step by 1.
        Assert.Equal(16f, SliderStep.Stepped(17f, -1, 1f, 64f, -1f));
        Assert.Equal(18f, SliderStep.Stepped(17f, 1, 1f, 64f, -1f));
        Assert.Equal(15f, SliderStep.Stepped(16f, -1, 1f, 64f, -1f));
        Assert.Equal(17f, SliderStep.Stepped(16f, 1, 1f, 64f, -1f));
    }

    [Fact]
    public void IntegerSlider_ClampsAtBounds()
    {
        Assert.Equal(64f, SliderStep.Stepped(64f, 1, 1f, 64f, -1f));
        Assert.Equal(1f, SliderStep.Stepped(1f, -1, 1f, 64f, -1f));
    }

    [Fact]
    public void FractionalBounds_KeepTwentiethOfRangeStep()
    {
        // A 0..1 factor/percent has whole-number bounds but is NOT an integer
        // count; a range below 2 keeps it on the fine twentieth-of-range grid.
        Assert.Equal(0.55f, SliderStep.Stepped(0.5f, 1, 0f, 1f, 0f), 5);
        Assert.Equal(0.45f, SliderStep.Stepped(0.5f, -1, 0f, 1f, 0f), 5);
    }

    [Fact]
    public void LargeIntegerRange_CoarsensToWholeStep()
    {
        // Past a walkable range the step still lands on a WHOLE number, never
        // between integers: 0..2000 gives round(2000/20) = 100.
        Assert.Equal(100f, SliderStep.Stepped(0f, 1, 0f, 2000f, -1f));
        Assert.Equal(0f, SliderStep.Stepped(100f, -1, 0f, 2000f, -1f));
    }

    [Fact]
    public void DegenerateRange_ReturnsValueUnchanged()
    {
        Assert.Equal(5f, SliderStep.Stepped(5f, 1, 5f, 5f, 0f));
    }

    // Snapped is a genuinely different operation from Stepped (quantizing an
    // externally supplied value, e.g. a typed-in number, rather than moving the
    // slider's own live value by one step) and deliberately keeps the
    // round-onto-the-grid behavior Stepped no longer uses -- see the class remarks.

    [Fact]
    public void Snapped_QuantizesOntoTheStepGrid()
    {
        Assert.Equal(450f, SliderStep.Snapped(500f, 0f, 3000f, 0f));
    }

    [Fact]
    public void Snapped_AlreadyOnGrid_IsUnchanged()
    {
        Assert.Equal(600f, SliderStep.Snapped(600f, 0f, 3000f, 0f));
    }

    [Fact]
    public void Snapped_ClampsOutOfRangeValues()
    {
        Assert.Equal(3000f, SliderStep.Snapped(5000f, 0f, 3000f, 0f));
        Assert.Equal(0f, SliderStep.Snapped(-5000f, 0f, 3000f, 0f));
    }

    [Fact]
    public void Snapped_DegenerateRange_ClampsToTheSinglePoint()
    {
        Assert.Equal(5f, SliderStep.Snapped(9f, 5f, 5f, 0f));
    }
}

public class SliderStepFractionalHintTests
{
    [Fact]
    public void WholeNumberBounds_StepWholeUnits_ByDefault()
    {
        Assert.Equal(2f, SliderStep.Stepped(1f, 1, 0f, 5f, 0f));
    }

    [Fact]
    public void FractionalProof_KeepsTheFineStep()
    {
        // The same 0..5 range captioned as a percentage steps a twentieth of the range.
        Assert.Equal(1.25f, SliderStep.Stepped(1f, 1, 0f, 5f, 0f, fractional: true));
        Assert.Equal(0.1f, SliderStep.Stepped(0f, 1, 0f, 2f, 0f, fractional: true));
    }

    [Fact]
    public void DeclaredRoundTo_StillWins()
    {
        Assert.Equal(2f, SliderStep.Stepped(1f, 1, 0f, 5f, 1f, fractional: true));
    }

    [Fact]
    public void DeclaredGrid_StepsOneGridUnit_OverWholeNumberBounds()
    {
        Assert.Equal(2.6f, SliderStep.Stepped(2.5f, 1, 1f, 10f, -1f, grid: 0.1));
        Assert.Equal(0.49f, SliderStep.Stepped(0.5f, -1, 0f, 5f, -1f, fractional: true, grid: 0.01));
    }

    [Fact]
    public void DeclaredGrid_TooFineToWalk_CoarsensOnTheGrid()
    {
        Assert.Equal(0.05f, SliderStep.Stepped(0f, 1, 0f, 1f, -1f, grid: 0.000001));
    }

    [Fact]
    public void DeclaredRoundTo_OutranksTheGrid()
    {
        Assert.Equal(2f, SliderStep.Stepped(1f, 1, 0f, 5f, 1f, grid: 0.1));
    }
}
