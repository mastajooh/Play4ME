# Play4ME

A Dalamud plugin for Final Fantasy XIV that reads **BossmodReborn**'s AOE telegraphs, moves your character to a safe spot with **vnavmesh**, walks you back into range of your target, and runs **Wrath Combo**'s Auto-Rotation while it's on.

> Automating movement and combat is against Square Enix's Terms of Service. Use at your own risk.

## Requirements

| Plugin | Needed for |
|---|---|
| [BossmodReborn](https://github.com/FFXIV-CombatReborn/BossmodReborn) | AOE data (required). Play4ME can only dodge fights BossmodReborn has a module for. |
| [vnavmesh](https://github.com/awgil/ffxiv_navmesh) | Moving your character (required). |
| [Wrath Combo](https://github.com/PunishXIV/WrathCombo) | Rotation (optional). |

Turn **off** BossmodReborn's own AI movement (`/bmrai`); Play4ME pauses while it's steering.

## How it works

About ten times a second, while on and in combat:

1. Asks BossmodReborn (`BossMod.Hints.ForbiddenZonesCount`, `BossMod.Hints.IsDashSafe`) whether you're standing in an AOE.
2. If you are, searches outward in rings for the nearest safe spot, preferring one that keeps you at your role's distance from the target.
3. Moves there with vnavmesh (`vnavmesh.Path.MoveTo`, or pathfinding if enabled).
4. When nothing is going off, walks back into range of your target (melee 2.5y / ranged 15y from its edge, configurable).
5. While on, leases Wrath Combo's Auto-Rotation (`WrathCombo.RegisterForLease` / `SetAutoRotationState`) and releases it when turned off or unloaded.

### Human-like movement

- **Reaction time:** a random 150–400 ms pause before moving.
- **Clearance:** keeps 1y from the AOE edge instead of hugging it.
- **Variation:** lands up to 0.75y off the exact spot, so dodges don't repeat identically.
- **No jitter:** stays put while safe.
- **Late dodge (optional):** keeps attacking until it must move (walking time + slack).

All adjustable in `/play4me`.

## Commands

`/play4me` opens the window. `/play4me on`, `off`, `toggle`. Short: `/p4m`.

## Build

```powershell
dotnet build Play4ME/Play4ME.csproj -c Release
dotnet test Play4ME.Tests/Play4ME.Tests.csproj -c Release
```

## License

MIT. The icon is original artwork for this project.
