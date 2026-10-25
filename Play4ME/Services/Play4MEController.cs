using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;

namespace Play4ME.Services;

public enum Activity
{
    Off,
    Waiting,
    Idle,
    Dodging,
    Returning,
}

/// <summary>
/// The Play4ME loop. Runs on the framework thread, throttled to ~10 checks per second.
/// Reads AOE safety from BossmodReborn, moves through vnavmesh, and leases Wrath Combo's Auto-Rotation while enabled.
/// </summary>
public sealed class Play4MEController : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ReturnInterval = TimeSpan.FromSeconds(1);

    private readonly Configuration _config;
    private readonly Action _save;
    private readonly BossModIpc _bossMod;
    private readonly NavmeshIpc _nav;
    private readonly WrathIpc _wrath;
    private readonly IObjectTable _objects;
    private readonly ITargetManager _targets;
    private readonly ICondition _condition;
    private readonly IClientState _clientState;
    private readonly INotificationManager _notifications;
    private readonly IPluginLog _log;

    private DateTime _nextTick = DateTime.MinValue;
    private DateTime _nextReturn = DateTime.MinValue;
    private Vector3? _destination;
    private bool _ownMove;
    private DateTime? _reactAt;
    private readonly IRandom _random = SharedRandom.Instance;

    public Play4MEController(Configuration config, Action save, BossModIpc bossMod, NavmeshIpc nav, WrathIpc wrath, IObjectTable objects,
        ITargetManager targets, ICondition condition, IClientState clientState, INotificationManager notifications, IPluginLog log)
    {
        _config        = config;
        _save          = save;
        _bossMod       = bossMod;
        _nav           = nav;
        _wrath         = wrath;
        _objects       = objects;
        _targets       = targets;
        _condition     = condition;
        _clientState   = clientState;
        _notifications = notifications;
        _log           = log;

        _clientState.ClassJobChanged += OnClassJobChanged;
    }

    private Play4MESettings Settings
        => _config.Settings;

    public bool Enabled
        => Settings.Enabled;

    public Activity Activity { get; private set; } = Activity.Off;

    public string Status { get; private set; } = "Off.";

    public int DodgeCount { get; private set; }

    public int ForbiddenZones { get; private set; }

    public void SetEnabled(bool enabled)
    {
        if (Settings.Enabled == enabled)
            return;

        Settings.Enabled = enabled;
        _save();
        if (enabled)
        {
            if (Settings.UseWrathAutoRotation)
            {
                if (_wrath.IsAvailable)
                    _wrath.Start(Settings.LetWrathConfigureJob);
                else
                    Notify(NotificationType.Warning, "Wrath Combo is not loaded, so Play4ME will only dodge.");
            }

            Notify(NotificationType.Success, "Play4ME on." + (Settings.UseWrathAutoRotation && _wrath.IsAvailable ? $" {_wrath.LastMessage}" : string.Empty));
        }
        else
        {
            StopOwnMovement();
            _wrath.Stop();
            Activity = Activity.Off;
            Status   = "Off.";
            Notify(NotificationType.Info, "Play4ME off. Wrath Combo is back under your control.");
        }
    }

    public void Toggle()
        => SetEnabled(!Settings.Enabled);

    /// <summary> Called every framework update. </summary>
    public void Tick(DateTime now)
    {
        if (!Settings.Enabled)
            return;
        if (now < _nextTick)
            return;

        _nextTick = now + TickInterval;
        try
        {
            Step(now);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Play4ME step failed.");
            Status = $"Error: {ex.Message}";
        }
    }

    public void Dispose()
    {
        _clientState.ClassJobChanged -= OnClassJobChanged;
        StopOwnMovement();
        _wrath.Stop(); // never leave Wrath leased after unload
    }

    // ------------------------------------------------------------------------------------------------

    private void Step(DateTime now)
    {
        var player = _objects.LocalPlayer;
        if (player is null || player.IsDead)
        {
            Wait("Waiting for your character.");
            return;
        }

        if (Settings.OnlyInCombat && !_condition[ConditionFlag.InCombat])
        {
            Wait("Waiting for combat.");
            return;
        }

        if (!_bossMod.IsAvailable)
        {
            Wait("BossmodReborn is not loaded: nothing to dodge with.");
            return;
        }

        if (!_nav.IsAvailable || !_nav.IsReady())
        {
            Wait(_nav.IsAvailable ? "vnavmesh is still building the navmesh for this zone." : "vnavmesh is not loaded: can't move.");
            return;
        }

        if (_bossMod.OwnAiIsNavigating())
        {
            Wait("BossmodReborn's own AI is moving you. Turn off its AI movement (/bmrai) so Play4ME can steer.");
            return;
        }

        var position = player.Position;
        ForbiddenZones = Settings.DodgeEnabled ? _bossMod.ForbiddenZones() : 0;
        var preference = RangeToTarget(player);

        // 1. Dodging comes first.
        if (ForbiddenZones > 0)
        {
            var safeHere = _bossMod.IsSpotSafe(position);
            if (Activity is Activity.Dodging && _destination is { } dest)
            {
                if (_nav.IsMoving() && _bossMod.IsSpotSafe(dest))
                {
                    Status = $"Dodging to a safe spot ({Vector3.Distance(position, dest):0.0}y away).";
                    return;
                }

                if (!_nav.IsMoving() && safeHere)
                {
                    Arrived("Safe.");
                }
                else if (!_bossMod.IsSpotSafe(dest))
                {
                    // A new AOE covers where we were heading: stop, then re-plan below if needed.
                    StopOwnMovement();
                    Arrived(safeHere ? "Destination became unsafe; staying here." : "Destination became unsafe; re-planning.");
                }
            }

            if (Activity is Activity.Returning && _destination is { } back && !_bossMod.IsSpotSafe(back))
                StopOwnMovement(); // walking back into a new AOE: cancel and dodge instead

            if (!safeHere)
            {
                // Human reaction time: notice, then move.
                _reactAt ??= now + HumanMotion.ReactionDelay(Settings, _random);
                if (now < _reactAt)
                {
                    Status = "AOE spotted, reacting...";
                    return;
                }

                Dodge(position, preference);
                return;
            }

            _reactAt = null;
        }
        else
        {
            _reactAt = null;
            if (Activity is Activity.Dodging && !_nav.IsMoving())
                Arrived("No AOEs active.");
        }

        // 2. Otherwise get back into range of the target.
        if (Settings.ReturnToTarget && preference is { } pref && Activity is not Activity.Dodging && !_nav.IsMoving() && now >= _nextReturn)
        {
            _nextReturn = now + ReturnInterval;
            if (DodgePlanner.ApproachPoint(DodgePlanner.Flat(position), pref) is { } approach)
            {
                var target = Snap(DodgePlanner.Lift(approach, position.Y));
                if (ForbiddenZones == 0 || _bossMod.IsSpotSafe(target))
                {
                    if (_nav.Pathfind(target))
                    {
                        _ownMove     = true;
                        _destination = target;
                        Activity     = Activity.Returning;
                        Status       = "Moving back into range of your target.";
                        return;
                    }
                }
            }
        }

        if (Activity is Activity.Returning && !_nav.IsMoving())
            Arrived("In range.");

        if (Activity is not (Activity.Dodging or Activity.Returning))
        {
            Activity = Activity.Idle;
            Status   = ForbiddenZones > 0 ? $"Standing safe ({ForbiddenZones} AOE(s) active)." : "Ready. No AOEs.";
        }
    }

    private void Dodge(Vector3 position, RangePreference? preference)
    {
        var y = position.Y;
        Func<Vector2, bool> isSafe = p => _bossMod.IsSpotSafe(DodgePlanner.Lift(p, y));
        var spot = HumanMotion.FindClearSpot(DodgePlanner.Flat(position), isSafe, DodgeSearch.FromSettings(Settings), preference,
            Settings.SafetyMargin);
        if (spot is null)
        {
            Status = $"In an AOE, but no safe spot within {Settings.MaxDodgeDistance:0}y.";
            return;
        }

        var chosen   = HumanMotion.Vary(spot.Value, isSafe, Settings.SafetyMargin, Settings.SpotVariation, _random);
        var distance = Vector2.Distance(DodgePlanner.Flat(position), chosen);
        if (Settings.LateDodge && !HumanMotion.MustMoveNow(_bossMod.SecondsToNextHit(), distance, Settings.LateDodgeBuffer))
        {
            Status = $"Holding position: AOE hits in {_bossMod.SecondsToNextHit():0.0}s, safe spot {distance:0.0}y away.";
            return;
        }

        var destination = DodgePlanner.Lift(chosen, y);
        var snapped     = Snap(destination);
        if (_bossMod.IsSpotSafe(snapped))
            destination = snapped;

        var moved = Settings.PathfindDodges ? _nav.Pathfind(destination) : _nav.MoveDirect(destination);
        if (!moved)
        {
            Status = "vnavmesh refused the move.";
            return;
        }

        _ownMove     = true;
        _destination = destination;
        Activity     = Activity.Dodging;
        DodgeCount++;
        Status = $"Dodging to a safe spot ({Vector3.Distance(position, destination):0.0}y away).";
    }

    private Vector3 Snap(Vector3 point)
        => _nav.NearestWalkable(point) ?? point;

    private RangePreference? RangeToTarget(IGameObject player)
    {
        if (_targets.Target is not IBattleNpc target || target.IsDead || !target.IsTargetable)
            return null;

        var role  = (player as ICharacter)?.ClassJob.ValueNullable?.Role ?? 0;
        var melee = role is 1 or 2; // ClassJob.Role: 1 tank, 2 melee DPS, 3 ranged DPS, 4 healer
        return new RangePreference(DodgePlanner.Flat(target.Position), DodgePlanner.PreferredDistance(melee, target.HitboxRadius, Settings));
    }

    private void Arrived(string status)
    {
        _ownMove     = false;
        _destination = null;
        Activity     = Activity.Idle;
        Status       = status;
    }

    private void Wait(string status)
    {
        StopOwnMovement();
        Activity = Activity.Waiting;
        Status   = status;
    }

    /// <summary> Stop movement only if Play4ME started it, so other plugins' paths aren't cancelled. </summary>
    private void StopOwnMovement()
    {
        if (_ownMove)
            _nav.Stop();
        _ownMove     = false;
        _destination = null;
    }

    private void OnClassJobChanged(uint classJobId)
    {
        if (Settings.Enabled && Settings.UseWrathAutoRotation && Settings.LetWrathConfigureJob && _wrath.Lease is not null)
            _wrath.Start(true);
    }

    private void Notify(NotificationType type, string message)
    {
        if (!Settings.ShowNotifications)
            return;

        try
        {
            _notifications.AddNotification(new Notification { Title = "Play4ME", Content = message, Type = type });
        }
        catch (Exception ex)
        {
            _log.Debug($"Notification failed: {ex.Message}");
        }
    }
}
