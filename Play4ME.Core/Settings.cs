namespace Play4ME;

/// <summary> Persisted settings. Free of Dalamud types so defaults and sanitizing are unit-testable. </summary>
public sealed class Play4MESettings
{
    public const int CurrentVersion = 1;

    public int SettingsVersion { get; set; } = CurrentVersion;

    /// <summary> Master switch (also toggled with /play4me). Off on every plugin load unless remembered. </summary>
    public bool Enabled { get; set; }

    /// <summary> Keep the master switch state across game restarts. </summary>
    public bool RememberEnabled { get; set; }

    // ---- Dodging (BossmodReborn + vnavmesh) ---------------------------------------

    public bool DodgeEnabled { get; set; } = true;

    /// <summary> Only dodge while in combat. </summary>
    public bool OnlyInCombat { get; set; } = true;

    /// <summary> Furthest distance (yalms) to look for a safe spot. </summary>
    public float MaxDodgeDistance { get; set; } = 25f;

    /// <summary> Distance between search rings (yalms). </summary>
    public float SearchStep { get; set; } = 1.5f;

    /// <summary> Directions tested on each ring. </summary>
    public int SearchDirections { get; set; } = 24;

    // ---- Human-like movement -------------------------------------------------------------

    /// <summary> Shortest random pause before reacting to a new AOE (milliseconds). </summary>
    public int ReactionMinMs { get; set; } = 150;

    /// <summary> Longest random pause before reacting to a new AOE (milliseconds). </summary>
    public int ReactionMaxMs { get; set; } = 400;

    /// <summary> Extra clearance (yalms) to keep from the AOE edge, so you don't hug it. </summary>
    public float SafetyMargin { get; set; } = 1f;

    /// <summary> Random offset (yalms) inside the safe area, so you don't land on the identical spot every time. </summary>
    public float SpotVariation { get; set; } = 0.75f;

    /// <summary> Wait until the hit is close (keeping time to walk out) instead of moving as soon as the AOE appears. </summary>
    public bool LateDodge { get; set; }

    /// <summary> Seconds of slack kept when dodging late, on top of the walking time. </summary>
    public float LateDodgeBuffer { get; set; } = 1.2f;

    /// <summary> Use vnavmesh pathfinding for dodges instead of walking in a straight line. </summary>
    public bool PathfindDodges { get; set; }

    // ---- Staying in range of the target ------------------------------------------------

    /// <summary> After dodging, walk back into range of your target when that spot is safe. </summary>
    public bool ReturnToTarget { get; set; } = true;

    /// <summary> Distance from the target's edge for melee jobs (tanks, melee DPS). </summary>
    public float MeleeRange { get; set; } = 2.5f;

    /// <summary> Distance from the target's edge for ranged jobs, casters and healers. </summary>
    public float RangedRange { get; set; } = 15f;

    // ---- Wrath Combo ---------------------------------------------------------------------

    /// <summary> Turn on Wrath Combo's Auto-Rotation while Play4ME is enabled. </summary>
    public bool UseWrathAutoRotation { get; set; } = true;

    /// <summary>
    /// Ask Wrath to make the current job ready for Auto-Rotation. If the job has no Auto-Mode combos enabled,
    /// Wrath turns on the job's simple (or advanced) modes and locks your Wrath settings while leased.
    /// </summary>
    public bool LetWrathConfigureJob { get; set; }

    public bool ShowNotifications { get; set; } = true;
}

public static class SettingsSanitizer
{
    public const float MinDodge = 3f, MaxDodge = 40f;
    public const float MinStep = 0.5f, MaxStep = 5f;
    public const int MinDirections = 8, MaxDirections = 72;
    public const float MaxRange = 30f;

    /// <returns> True if anything changed. </returns>
    public static bool Sanitize(Play4MESettings s)
    {
        var changed = false;
        if (s.SettingsVersion < Play4MESettings.CurrentVersion)
        {
            s.SettingsVersion = Play4MESettings.CurrentVersion;
            changed           = true;
        }

        changed |= Clamp(s.MaxDodgeDistance, MinDodge, MaxDodge, v => s.MaxDodgeDistance = v);
        changed |= Clamp(s.SearchStep, MinStep, MaxStep, v => s.SearchStep = v);
        changed |= Clamp(s.MeleeRange, 0.5f, MaxRange, v => s.MeleeRange = v);
        changed |= Clamp(s.RangedRange, 0.5f, MaxRange, v => s.RangedRange = v);
        changed |= Clamp(s.SafetyMargin, 0f, 3f, v => s.SafetyMargin = v);
        changed |= Clamp(s.SpotVariation, 0f, 3f, v => s.SpotVariation = v);
        changed |= Clamp(s.LateDodgeBuffer, 0.3f, 5f, v => s.LateDodgeBuffer = v);
        var min = Math.Clamp(s.ReactionMinMs, 0, 2000);
        var max = Math.Clamp(s.ReactionMaxMs, min, 2000);
        if (min != s.ReactionMinMs || max != s.ReactionMaxMs)
        {
            s.ReactionMinMs = min;
            s.ReactionMaxMs = max;
            changed         = true;
        }
        var dirs = Math.Clamp(s.SearchDirections, MinDirections, MaxDirections);
        if (dirs != s.SearchDirections)
        {
            s.SearchDirections = dirs;
            changed            = true;
        }

        return changed;
    }

    private static bool Clamp(float value, float min, float max, Action<float> set)
    {
        var clamped = float.IsFinite(value) ? Math.Clamp(value, min, max) : min;
        if (clamped.Equals(value))
            return false;

        set(clamped);
        return true;
    }
}
