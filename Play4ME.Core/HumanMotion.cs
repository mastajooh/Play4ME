using System.Numerics;

namespace Play4ME;

/// <summary> Random source, injectable for tests. </summary>
public interface IRandom
{
    /// <summary> Uniform value in [0, 1). </summary>
    double NextDouble();
}

public sealed class SharedRandom : IRandom
{
    public static readonly SharedRandom Instance = new();

    public double NextDouble()
        => Random.Shared.NextDouble();
}

/// <summary>
/// Makes dodging look and feel like a player rather than a script: a short reaction time, clearance from AOE edges,
/// a little variety in where you stand, and optionally moving late (keeping enough time to walk out). Pure logic.
/// </summary>
public static class HumanMotion
{
    /// <summary> Normal run speed in yalms per second, used when the game's speed isn't known. </summary>
    public const float DefaultRunSpeed = 6f;

    private const int ClearanceSamples = 8;
    private const int VariationTries = 6;

    /// <summary> Random pause before reacting, between the configured minimum and maximum. </summary>
    public static TimeSpan ReactionDelay(Play4MESettings s, IRandom random)
    {
        var min = Math.Max(0, s.ReactionMinMs);
        var max = Math.Max(min, s.ReactionMaxMs);
        return TimeSpan.FromMilliseconds(min + random.NextDouble() * (max - min));
    }

    /// <summary> Safe at <paramref name="point"/> and at <paramref name="margin"/> in every direction around it. </summary>
    public static bool IsClear(Vector2 point, Func<Vector2, bool> isSafe, float margin)
    {
        if (!isSafe(point))
            return false;
        if (margin <= 0f)
            return true;

        for (var i = 0; i < ClearanceSamples; ++i)
        {
            var angle = i * MathF.Tau / ClearanceSamples;
            if (!isSafe(point + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * margin))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The nearest spot with <paramref name="margin"/> of clearance; if no spot within reach has that much room,
    /// the nearest merely-safe spot.
    /// </summary>
    public static Vector2? FindClearSpot(Vector2 origin, Func<Vector2, bool> isSafe, DodgeSearch search, RangePreference? preference, float margin)
    {
        if (margin > 0f && DodgePlanner.FindSafeSpot(origin, p => IsClear(p, isSafe, margin), search, preference) is { } roomy)
            return roomy;

        return DodgePlanner.FindSafeSpot(origin, isSafe, search, preference);
    }

    /// <summary> Nudge <paramref name="spot"/> by up to <paramref name="variation"/>, staying clear; otherwise keep it. </summary>
    public static Vector2 Vary(Vector2 spot, Func<Vector2, bool> isSafe, float margin, float variation, IRandom random)
    {
        if (variation <= 0f)
            return spot;

        for (var i = 0; i < VariationTries; ++i)
        {
            var angle  = (float)(random.NextDouble() * Math.Tau);
            var radius = variation * MathF.Sqrt((float)random.NextDouble()); // uniform over the disc
            var candidate = spot + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            if (IsClear(candidate, isSafe, margin))
                return candidate;
        }

        return spot;
    }

    /// <summary>
    /// For late dodging: true once the hit is close enough that you must start walking now
    /// (walking time at <paramref name="runSpeed"/> plus <paramref name="buffer"/> seconds).
    /// </summary>
    public static bool MustMoveNow(float secondsToHit, float distance, float buffer, float runSpeed = DefaultRunSpeed)
    {
        if (!float.IsFinite(secondsToHit))
            return true; // timing unknown: don't gamble, move now
        var walk = distance / Math.Max(1f, runSpeed);
        return secondsToHit <= walk + Math.Max(0f, buffer);
    }
}
