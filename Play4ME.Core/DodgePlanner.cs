using System.Numerics;

namespace Play4ME;

/// <summary> Where to stand relative to the current target, if anywhere. </summary>
public readonly record struct RangePreference(Vector2 TargetPosition, float Distance);

/// <summary> Search parameters for <see cref="DodgePlanner.FindSafeSpot"/>. </summary>
public readonly record struct DodgeSearch(float MaxDistance, float Step, int Directions)
{
    public static DodgeSearch FromSettings(Play4MESettings s)
        => new(s.MaxDodgeDistance, s.SearchStep, s.SearchDirections);
}

/// <summary>
/// Pure geometry for dodging. Positions are on the ground plane (X, Z) as <see cref="Vector2"/>.
/// The safety test is supplied by the caller (in game: BossmodReborn's <c>BossMod.Hints.IsDashSafe</c>),
/// so this class never knows about AOE shapes itself.
/// </summary>
public static class DodgePlanner
{
    /// <summary> Candidate spots on rings around <paramref name="origin"/>, nearest ring first. </summary>
    public static IEnumerable<(int Ring, Vector2 Point)> Candidates(Vector2 origin, DodgeSearch search)
    {
        var step       = Math.Max(0.1f, search.Step);
        var directions = Math.Max(4, search.Directions);
        var rings      = Math.Max(1, (int)MathF.Floor(search.MaxDistance / step));
        for (var ring = 1; ring <= rings; ++ring)
        {
            var radius = ring * step;
            // Offset every other ring by half a step so rings don't line up radially.
            var offset = ring % 2 == 0 ? MathF.PI / directions : 0f;
            for (var i = 0; i < directions; ++i)
            {
                var angle = offset + i * MathF.Tau / directions;
                yield return (ring, origin + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
            }
        }
    }

    /// <summary>
    /// The nearest safe spot to <paramref name="origin"/>. Among equally near safe spots (same ring), the one closest to
    /// the preferred distance from the target wins, so dodging keeps you in range when possible.
    /// Returns null when nothing within reach is safe (or the safety source rejects everything).
    /// </summary>
    public static Vector2? FindSafeSpot(Vector2 origin, Func<Vector2, bool> isSafe, DodgeSearch search, RangePreference? preference)
    {
        var bestRing  = -1;
        Vector2? best = null;
        var bestScore = float.MaxValue;
        foreach (var (ring, point) in Candidates(origin, search))
        {
            if (bestRing >= 0 && ring > bestRing)
                break; // nearest ring with a safe spot is done

            if (!isSafe(point))
                continue;

            var score = preference is { } p ? MathF.Abs(Vector2.Distance(point, p.TargetPosition) - p.Distance) : 0f;
            if (score < bestScore)
            {
                bestScore = score;
                best      = point;
                bestRing  = ring;
            }
        }

        return best;
    }

    /// <summary>
    /// The spot at the preferred distance from the target, on the line between target and player.
    /// Returns null when the player is already within <paramref name="tolerance"/> of that distance or closer.
    /// </summary>
    public static Vector2? ApproachPoint(Vector2 player, RangePreference preference, float tolerance = 1f)
    {
        var toPlayer = player - preference.TargetPosition;
        var distance = toPlayer.Length();
        if (distance <= preference.Distance + tolerance)
            return null;

        var direction = distance > 0.001f ? toPlayer / distance : Vector2.UnitX;
        return preference.TargetPosition + direction * preference.Distance;
    }

    /// <summary> Preferred distance from the target's center: its hitbox radius plus the role range. </summary>
    public static float PreferredDistance(bool meleeRole, float targetHitboxRadius, Play4MESettings settings)
        => Math.Max(0f, targetHitboxRadius) + (meleeRole ? settings.MeleeRange : settings.RangedRange);

    public static Vector2 Flat(Vector3 v)
        => new(v.X, v.Z);

    public static Vector3 Lift(Vector2 v, float y)
        => new(v.X, y, v.Y);
}
