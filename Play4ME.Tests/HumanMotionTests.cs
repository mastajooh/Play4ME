using System.Numerics;

namespace Play4ME.Tests;

public class HumanMotionTests
{
    private sealed class FixedRandom(params double[] values) : IRandom
    {
        private int _i;

        public double NextDouble()
            => values[_i++ % values.Length];
    }

    private static readonly DodgeSearch Search = new(20f, 1f, 24);

    private static Func<Vector2, bool> OutsideCircle(float radius)
        => p => p.Length() > radius;

    [Fact]
    public void ReactionDelay_StaysBetweenMinAndMax()
    {
        var s = new Play4MESettings { ReactionMinMs = 150, ReactionMaxMs = 400 };
        Assert.Equal(150, HumanMotion.ReactionDelay(s, new FixedRandom(0)).TotalMilliseconds, 3);
        Assert.Equal(275, HumanMotion.ReactionDelay(s, new FixedRandom(0.5)).TotalMilliseconds, 3);
        Assert.True(HumanMotion.ReactionDelay(s, new FixedRandom(0.999)).TotalMilliseconds < 400);
    }

    [Fact]
    public void ClearSpot_KeepsDistanceFromTheEdge()
    {
        var hug  = DodgePlanner.FindSafeSpot(Vector2.Zero, OutsideCircle(5f), Search, null)!.Value;
        var roomy = HumanMotion.FindClearSpot(Vector2.Zero, OutsideCircle(5f), Search, null, margin: 1.5f)!.Value;
        Assert.True(roomy.Length() - 5f >= 1.5f - 0.01f, $"only {roomy.Length() - 5f:0.00}y of clearance");
        Assert.True(roomy.Length() > hug.Length());
    }

    [Fact]
    public void ClearSpot_FallsBackToMerelySafe_WhenThereIsNoRoom()
    {
        // Safe band only 1y wide (5..6y): a 2y margin can never fit, so take the plain safe spot.
        Func<Vector2, bool> band = p => p.Length() is > 5f and < 6f;
        var spot = HumanMotion.FindClearSpot(Vector2.Zero, band, new DodgeSearch(20f, 0.5f, 24), null, margin: 2f);
        Assert.NotNull(spot);
        Assert.True(band(spot!.Value));
    }

    [Fact]
    public void Vary_StaysSafe_AndWithinTheVariationRadius()
    {
        var spot = new Vector2(8, 0);
        for (var i = 0; i < 50; ++i)
        {
            var random = new FixedRandom(i / 50.0, (i * 7 % 50) / 50.0);
            var varied = HumanMotion.Vary(spot, OutsideCircle(5f), 1f, 0.75f, random);
            Assert.True(Vector2.Distance(spot, varied) <= 0.75f + 0.001f);
            Assert.True(HumanMotion.IsClear(varied, OutsideCircle(5f), 1f));
        }
    }

    [Fact]
    public void Vary_KeepsTheSpot_WhenEveryNudgeWouldBeUnsafe()
    {
        var spot = new Vector2(3, 0);
        Func<Vector2, bool> onlyThatSpot = p => Vector2.Distance(p, spot) < 0.01f;
        Assert.Equal(spot, HumanMotion.Vary(spot, onlyThatSpot, 0f, 1f, new FixedRandom(0.3, 0.9)));
    }

    [Fact]
    public void Vary_WithZeroVariation_ReturnsTheSameSpot()
    {
        Assert.Equal(new Vector2(8, 0), HumanMotion.Vary(new Vector2(8, 0), OutsideCircle(5f), 1f, 0f, new FixedRandom(0.5)));
    }

    [Fact]
    public void IsClear_ChecksAllAroundThePoint()
    {
        Assert.True(HumanMotion.IsClear(new Vector2(7, 0), OutsideCircle(5f), 1.5f));
        Assert.False(HumanMotion.IsClear(new Vector2(5.5f, 0), OutsideCircle(5f), 1f));
        Assert.True(HumanMotion.IsClear(new Vector2(5.5f, 0), OutsideCircle(5f), 0f));
    }

    [Fact]
    public void MustMoveNow_WaitsUntilWalkingTimePlusBuffer()
    {
        // 6y away at 6y/s = 1s walk, plus 1.2s buffer = move at 2.2s before the hit.
        Assert.False(HumanMotion.MustMoveNow(5f, 6f, 1.2f));
        Assert.True(HumanMotion.MustMoveNow(2.1f, 6f, 1.2f));
        Assert.True(HumanMotion.MustMoveNow(-1f, 6f, 1.2f)); // already going off
        Assert.True(HumanMotion.MustMoveNow(float.NaN, 6f, 1.2f)); // unknown timing: move
    }

    [Fact]
    public void Sanitize_KeepsReactionMaxAtLeastMin()
    {
        var s = new Play4MESettings { ReactionMinMs = 500, ReactionMaxMs = 100 };
        Assert.True(SettingsSanitizer.Sanitize(s));
        Assert.Equal(500, s.ReactionMaxMs);
    }
}
