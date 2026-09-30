using Overburden.Core.Structure;

namespace Overburden.Core.Tests;

/// <summary>COLLAPSE-PLAN part e: when the phonk plays, when it restarts, how it fades, and how fast.</summary>
public class CollapseMusicCueTests
{
    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(2.0, 1.0625)]
    [InlineData(4.0, 1.125)]
    [InlineData(8.0, 1.1875)]
    [InlineData(16.0, 1.25)]
    [InlineData(64.0, 1.25)]
    [InlineData(0.5, 1.0)]
    public void ThePitchIsLinearInDoublingsUpToTheCap(double multiplier, double pitch)
    {
        Assert.Equal(pitch, new CollapseMusicCue().PitchFor(multiplier), 12);
    }

    [Fact]
    public void TheCeilingAndTheCapMoveThePitch()
    {
        var cue = new CollapseMusicCue { MaxPitch = 1.5, CapMultiplier = 4 };
        Assert.Equal(1.25, cue.PitchFor(2), 12);
        Assert.Equal(1.5, cue.PitchFor(4), 12);
        Assert.Equal(1.0, new CollapseMusicCue { CapMultiplier = 1 }.PitchFor(16));
        Assert.Equal(1.0, new CollapseMusicCue { MaxPitch = 0.5 }.PitchFor(16));
    }

    [Fact]
    public void ItStartsFromTheTopOnceAndNotAgainWhileACollapseIsLive()
    {
        var cue = new CollapseMusicCue();
        Assert.False(cue.Step(null, 0.016));
        Assert.False(cue.Playing);

        Assert.True(cue.Step(1.0, 0.016));
        Assert.True(cue.Playing);
        Assert.Equal(1.0, cue.Gain);
        for (int i = 0; i < 100; i++)
        {
            Assert.False(cue.Step(8.0, 0.016));
        }

        Assert.Equal(1.1875, cue.Pitch, 12);
        Assert.Equal(1, cue.Starts);
    }

    [Fact]
    public void ItFadesOverTwoSecondsThenStopsHoldingItsPitch()
    {
        var cue = new CollapseMusicCue();
        cue.Step(16.0, 0.016);
        cue.Step(null, 0.5);
        Assert.Equal(0.75, cue.Gain, 12);
        Assert.True(cue.Playing);
        Assert.Equal(1.25, cue.Pitch, 12);

        cue.Step(null, 1.0);
        Assert.Equal(0.25, cue.Gain, 12);
        Assert.True(cue.Playing);
        Assert.Equal(0, cue.Stops);

        cue.Step(null, 0.5);
        Assert.Equal(0.0, cue.Gain);
        Assert.False(cue.Playing);
        Assert.Equal(1, cue.Stops);
    }

    [Fact]
    public void ACollapseDuringTheFadeBringsItBackWithoutARestart()
    {
        var cue = new CollapseMusicCue();
        cue.Step(4.0, 0.016);
        cue.Step(null, 1.0);
        Assert.Equal(0.5, cue.Gain, 12);

        Assert.False(cue.Step(1.0, 0.016));
        Assert.Equal(1.0, cue.Gain);
        Assert.Equal(1.0, cue.Pitch);
        Assert.Equal(1, cue.Starts);
        Assert.Equal(0, cue.Stops);
    }

    [Fact]
    public void ACollapseAfterTheFadeStartsItFromTheTopAgain()
    {
        var cue = new CollapseMusicCue();
        cue.Step(1.0, 0.016);
        cue.Step(null, 3.0);
        Assert.False(cue.Playing);

        Assert.True(cue.Step(1.0, 0.016));
        Assert.Equal(2, cue.Starts);
        Assert.Equal(1, cue.Stops);
    }
}
