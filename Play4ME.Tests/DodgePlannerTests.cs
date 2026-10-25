using System.Numerics;

namespace Play4ME.Tests;

public class DodgePlannerTests
{
    private static readonly DodgeSearch Search = new(MaxDistance: 20f, Step: 1f, Directions: 24);

    /// <summary> A circle AOE: anything within <paramref name="radius"/> of <paramref name="center"/> is unsafe. </summary>
    private static Func<Vector2, bool> OutsideCircle(Vector2 center, float radius)
        => p => Vector2.Distance(p, center) > radius;

    [Fact]
    public void FindsTheNearestSafeSpot_OutsideACircleAoe()
    {
        var spot = DodgePlanner.FindSafeSpot(Vector2.Zero, OutsideCircle(Vector2.Zero, 5f), Search, null);
        Assert.NotNull(spot);
        var distance = spot!.Value.Length();
        Assert.True(distance > 5f, $"spot at {distance} is still inside the AOE");
        Assert.True(distance <= 6.01f, $"spot at {distance} is not the nearest ring");
    }

    [Fact]
    public void MovesAwayFromAnOffCenterAoe()
    {
        // Standing 1y east of a 6y circle's center: the shortest way out is further east.
        var spot = DodgePlanner.FindSafeSpot(new Vector2(1, 0), OutsideCircle(Vector2.Zero, 6f), Search, null)!.Value;
        Assert.True(spot.X > 5f, $"expected to exit east, got {spot}");
    }

    [Fact]
    public void AmongEquallyNearSpots_PrefersTheOneAtTheRightDistanceFromTheTarget()
    {
        // Line AOE along the Z axis, 4y wide: safe spots exist both east and west at the same distance.
        Func<Vector2, bool> outsideLine = p => MathF.Abs(p.X) > 2f;
        var boss = new Vector2(10f, 0f);
        var spot = DodgePlanner.FindSafeSpot(Vector2.Zero, outsideLine, Search, new RangePreference(boss, 3f))!.Value;
        Assert.True(spot.X > 0f, $"expected to dodge toward the boss (east), got {spot}");
    }

    [Fact]
    public void NeverPicksAFartherRing_WhenANearerSafeSpotExists()
    {
        Func<Vector2, bool> outsideLine = p => MathF.Abs(p.X) > 2f;
        var spot = DodgePlanner.FindSafeSpot(Vector2.Zero, outsideLine, Search, new RangePreference(new Vector2(0, 50), 3f))!.Value;
        Assert.True(spot.Length() <= 3.01f, $"took a far spot {spot} to chase the preferred range");
    }

    [Fact]
    public void ReturnsNull_WhenNothingIsSafe()
    {
        Assert.Null(DodgePlanner.FindSafeSpot(Vector2.Zero, _ => false, Search, null));
    }

    [Fact]
    public void RespectsTheMaximumDistance()
    {
        var calls = new List<Vector2>();
        DodgePlanner.FindSafeSpot(Vector2.Zero, p =>
        {
            calls.Add(p);
            return false;
        }, Search, null);

        Assert.All(calls, p => Assert.True(p.Length() <= Search.MaxDistance + 0.01f));
        Assert.Equal(20 * 24, calls.Count);
    }

    [Fact]
    public void Candidates_StartWithTheNearestRing()
    {
        var first = DodgePlanner.Candidates(new Vector2(3, 4), Search).Take(24).ToList();
        Assert.All(first, c =>
        {
            Assert.Equal(1, c.Ring);
            Assert.Equal(1f, Vector2.Distance(c.Point, new Vector2(3, 4)), 3);
        });
    }

    [Fact]
    public void ApproachPoint_IsOnTheLineToTheTarget_AtThePreferredDistance()
    {
        var target = new Vector2(0, 0);
        var point  = DodgePlanner.ApproachPoint(new Vector2(20, 0), new RangePreference(target, 5f))!.Value;
        Assert.Equal(5f, point.X, 3);
        Assert.Equal(0f, point.Y, 3);
    }

    [Fact]
    public void ApproachPoint_IsNull_WhenAlreadyInRange()
    {
        Assert.Null(DodgePlanner.ApproachPoint(new Vector2(5.5f, 0), new RangePreference(Vector2.Zero, 5f)));
        Assert.Null(DodgePlanner.ApproachPoint(new Vector2(2f, 0), new RangePreference(Vector2.Zero, 5f)));
    }

    [Fact]
    public void PreferredDistance_AddsHitboxAndRoleRange()
    {
        var s = new Play4MESettings { MeleeRange = 2.5f, RangedRange = 15f };
        Assert.Equal(7.5f, DodgePlanner.PreferredDistance(true, 5f, s));
        Assert.Equal(20f, DodgePlanner.PreferredDistance(false, 5f, s));
    }

    [Fact]
    public void FlatAndLift_UseTheGroundPlane()
    {
        var v = new Vector3(1, 2, 3);
        Assert.Equal(new Vector2(1, 3), DodgePlanner.Flat(v));
        Assert.Equal(new Vector3(1, 7, 3), DodgePlanner.Lift(new Vector2(1, 3), 7));
    }
}

public class SettingsTests
{
    [Fact]
    public void Defaults_StartOff_AndAreInRange()
    {
        var s = new Play4MESettings();
        Assert.False(s.Enabled);
        Assert.False(s.RememberEnabled);
        Assert.False(s.LetWrathConfigureJob); // never changes Wrath job settings unless asked
        Assert.True(s.DodgeEnabled);
        Assert.False(SettingsSanitizer.Sanitize(s));
    }

    [Fact]
    public void Sanitize_ClampsOutOfRangeValues()
    {
        var s = new Play4MESettings { MaxDodgeDistance = 999, SearchStep = 0, SearchDirections = 1, MeleeRange = float.NaN, SettingsVersion = 0 };
        Assert.True(SettingsSanitizer.Sanitize(s));
        Assert.Equal(SettingsSanitizer.MaxDodge, s.MaxDodgeDistance);
        Assert.Equal(SettingsSanitizer.MinStep, s.SearchStep);
        Assert.Equal(SettingsSanitizer.MinDirections, s.SearchDirections);
        Assert.Equal(0.5f, s.MeleeRange);
        Assert.Equal(Play4MESettings.CurrentVersion, s.SettingsVersion);
        Assert.False(SettingsSanitizer.Sanitize(s));
    }
}
