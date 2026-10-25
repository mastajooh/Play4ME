using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Play4ME.Services;

/// <summary>
/// Read-only access to BossmodReborn's AI hints.
/// Labels verified in FFXIV-CombatReborn/BossmodReborn BossMod/Framework/IPCProvider.cs ("BossMod." + name).
/// </summary>
public sealed class BossModIpc
{
    private readonly ICallGateSubscriber<int> _forbiddenZonesCount;
    private readonly ICallGateSubscriber<float> _nextActivation;
    private readonly ICallGateSubscriber<Vector3, Vector3, bool> _isDashSafe;
    private readonly ICallGateSubscriber<bool> _hasActiveModule;
    private readonly ICallGateSubscriber<bool> _aiIsNavigating;
    private readonly ICallGateSubscriber<string?> _activeModuleName;
    private readonly IPluginLog _log;

    public BossModIpc(IDalamudPluginInterface pi, IPluginLog log)
    {
        _log                 = log;
        _forbiddenZonesCount = pi.GetIpcSubscriber<int>("BossMod.Hints.ForbiddenZonesCount");
        _nextActivation      = pi.GetIpcSubscriber<float>("BossMod.Hints.ForbiddenZonesNextActivation");
        _isDashSafe          = pi.GetIpcSubscriber<Vector3, Vector3, bool>("BossMod.Hints.IsDashSafe");
        _hasActiveModule     = pi.GetIpcSubscriber<bool>("BossMod.HasActiveModule");
        _aiIsNavigating      = pi.GetIpcSubscriber<bool>("BossMod.AI.IsNavigating");
        _activeModuleName    = pi.GetIpcSubscriber<string?>("BossMod.ActiveModuleName");
    }

    public bool IsAvailable
        => _forbiddenZonesCount.HasFunction && _isDashSafe.HasFunction;

    /// <summary> Number of AOE areas BossmodReborn currently wants you out of. 0 when unavailable. </summary>
    public int ForbiddenZones()
        => Try(() => _forbiddenZonesCount.InvokeFunc(), 0);

    /// <summary>
    /// Whether standing at <paramref name="point"/> is safe: inside the arena and outside every forbidden zone.
    /// (Called with from == to, so BossmodReborn's arena-wall ray test is skipped and only the spot is judged.)
    /// Returns false when unavailable, so nothing is ever treated as safe by mistake.
    /// </summary>
    public bool IsSpotSafe(Vector3 point)
        => Try(() => _isDashSafe.InvokeFunc(point, point), false);

    /// <summary> Seconds until the first listed AOE goes off (negative when already active). 0 when unavailable, so late dodging never waits blind. </summary>
    public float SecondsToNextHit()
        => Try(() => _nextActivation.InvokeFunc(), 0f);

    public bool HasActiveModule()
        => Try(() => _hasActiveModule.InvokeFunc(), false);

    public string? ActiveModuleName()
        => Try(() => _activeModuleName.InvokeFunc(), null);

    /// <summary> True when BossmodReborn's own AI is steering the character (it would fight Play4ME for control). </summary>
    public bool OwnAiIsNavigating()
        => Try(() => _aiIsNavigating.InvokeFunc(), false);

    private T Try<T>(Func<T> call, T fallback)
    {
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            _log.Verbose($"[BossMod IPC] {ex.Message}");
            return fallback;
        }
    }
}

/// <summary>
/// Movement through vnavmesh. Labels verified in awgil/ffxiv_navmesh vnavmesh/IPCProvider.cs ("vnavmesh." + name).
/// </summary>
public sealed class NavmeshIpc
{
    private readonly ICallGateSubscriber<bool> _isReady;
    private readonly ICallGateSubscriber<List<Vector3>, bool, object> _moveTo;
    private readonly ICallGateSubscriber<object> _stop;
    private readonly ICallGateSubscriber<bool> _isRunning;
    private readonly ICallGateSubscriber<Vector3, bool, bool> _pathfindAndMoveTo;
    private readonly ICallGateSubscriber<bool> _pathfindInProgress;
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> _nearestPoint;
    private readonly IPluginLog _log;

    public NavmeshIpc(IDalamudPluginInterface pi, IPluginLog log)
    {
        _log                = log;
        _isReady            = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        _moveTo             = pi.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
        _stop               = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        _isRunning          = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        _pathfindAndMoveTo  = pi.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        _pathfindInProgress = pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        _nearestPoint       = pi.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPoint");
    }

    public bool IsAvailable
        => _isReady.HasFunction;

    /// <summary> The navmesh for this zone is built/loaded. </summary>
    public bool IsReady()
        => Try(() => _isReady.InvokeFunc(), false);

    public bool IsMoving()
        => Try(() => _isRunning.InvokeFunc() || _pathfindInProgress.InvokeFunc(), false);

    /// <summary> Walk straight to <paramref name="destination"/> (fast; right for short dodges in open arenas). </summary>
    public bool MoveDirect(Vector3 destination)
        => Try(() =>
        {
            _moveTo.InvokeAction([destination], false);
            return true;
        }, false);

    /// <summary> Pathfind around obstacles, then walk. </summary>
    public bool Pathfind(Vector3 destination)
        => Try(() => _pathfindAndMoveTo.InvokeFunc(destination, false), false);

    public void Stop()
        => Try(() =>
        {
            _stop.InvokeAction();
            return true;
        }, false);

    /// <summary> Snap a point onto the walkable mesh, or null when there is no mesh nearby. </summary>
    public Vector3? NearestWalkable(Vector3 point, float halfExtentXZ = 1.5f, float halfExtentY = 5f)
        => Try(() => _nearestPoint.InvokeFunc(point, halfExtentXZ, halfExtentY), null);

    private T Try<T>(Func<T> call, T fallback)
    {
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            _log.Verbose($"[vnavmesh IPC] {ex.Message}");
            return fallback;
        }
    }
}

/// <summary> Mirrors WrathCombo's SetResult (docs/IPCExample.cs). Values are converted across plugins by Dalamud. </summary>
public enum WrathSetResult
{
    Okay = 0,
    OkayWorking = 1,
    IPCDisabled = 10,
    InvalidLease = 11,
    BlacklistedLease = 12,
    Duplicate = 13,
    PlayerNotAvailable = 14,
    InvalidConfiguration = 15,
    InvalidValue = 16,
}

/// <summary>
/// Wrath Combo Auto-Rotation through its lease system, following PunishXIV/WrathCombo docs/IPCExample.cs
/// (EzIPC prefix "WrathCombo"). The lease is always released when Play4ME turns off or unloads.
/// </summary>
public sealed class WrathIpc
{
    public const string InternalName = "Play4ME";
    public const string DisplayName = "Play4ME";

    private readonly ICallGateSubscriber<string, string, Guid?> _registerForLease;
    private readonly ICallGateSubscriber<Guid, bool, WrathSetResult> _setAutoRotationState;
    private readonly ICallGateSubscriber<Guid, WrathSetResult> _setJobReady;
    private readonly ICallGateSubscriber<bool> _isJobReady;
    private readonly ICallGateSubscriber<bool> _getAutoRotationState;
    private readonly ICallGateSubscriber<Guid, object> _releaseControl;
    private readonly IPluginLog _log;

    public WrathIpc(IDalamudPluginInterface pi, IPluginLog log)
    {
        _log                  = log;
        _registerForLease     = pi.GetIpcSubscriber<string, string, Guid?>("WrathCombo.RegisterForLease");
        _setAutoRotationState = pi.GetIpcSubscriber<Guid, bool, WrathSetResult>("WrathCombo.SetAutoRotationState");
        _setJobReady          = pi.GetIpcSubscriber<Guid, WrathSetResult>("WrathCombo.SetCurrentJobAutoRotationReady");
        _isJobReady           = pi.GetIpcSubscriber<bool>("WrathCombo.IsCurrentJobAutoRotationReady");
        _getAutoRotationState = pi.GetIpcSubscriber<bool>("WrathCombo.GetAutoRotationState");
        _releaseControl       = pi.GetIpcSubscriber<Guid, object>("WrathCombo.ReleaseControl");
    }

    public Guid? Lease { get; private set; }

    public string LastMessage { get; private set; } = "Not used yet.";

    public bool IsAvailable
        => _registerForLease.HasFunction;

    public bool AutoRotationOn()
    {
        try
        {
            return _getAutoRotationState.InvokeFunc();
        }
        catch
        {
            return false;
        }
    }

    public bool JobReady()
    {
        try
        {
            return _isJobReady.InvokeFunc();
        }
        catch
        {
            return false;
        }
    }

    /// <summary> Lease Wrath and turn Auto-Rotation on. Optionally let Wrath make the current job ready. </summary>
    public bool Start(bool configureJob)
    {
        try
        {
            Lease ??= _registerForLease.InvokeFunc(InternalName, DisplayName);
            if (Lease is not { } lease)
            {
                LastMessage = "Wrath Combo refused the lease (its IPC may be disabled, or you revoked Play4ME in Wrath). See Wrath's log.";
                return false;
            }

            var state = _setAutoRotationState.InvokeFunc(lease, true);
            if (state is not (WrathSetResult.Okay or WrathSetResult.OkayWorking))
            {
                LastMessage = $"Wrath Combo did not turn on Auto-Rotation: {state}.";
                if (state is WrathSetResult.InvalidLease or WrathSetResult.BlacklistedLease)
                    Lease = null;
                return false;
            }

            if (configureJob)
            {
                var ready = _setJobReady.InvokeFunc(lease);
                LastMessage = ready is WrathSetResult.Okay or WrathSetResult.OkayWorking
                    ? "Auto-Rotation on; job made ready by Wrath."
                    : $"Auto-Rotation on, but Wrath could not make the job ready: {ready}.";
            }
            else
            {
                LastMessage = JobReady()
                    ? "Auto-Rotation on."
                    : "Auto-Rotation on, but this job has no Auto-Mode combos enabled in Wrath. Enable them in Wrath, or turn on 'Let Wrath configure the job'.";
            }

            return true;
        }
        catch (Exception ex)
        {
            LastMessage = $"Wrath Combo IPC error: {ex.Message}";
            _log.Warning(ex, "[Wrath IPC] Start failed.");
            return false;
        }
    }

    /// <summary> Give control back to the user. Wrath restores the user's own Auto-Rotation setting. </summary>
    public void Stop()
    {
        if (Lease is not { } lease)
            return;

        try
        {
            _releaseControl.InvokeAction(lease);
            LastMessage = "Released Wrath Combo.";
        }
        catch (Exception ex)
        {
            _log.Debug($"[Wrath IPC] ReleaseControl failed: {ex.Message}");
        }

        Lease = null;
    }
}
