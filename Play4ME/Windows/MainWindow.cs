using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Play4ME.Services;

namespace Play4ME.Windows;

public sealed class MainWindow : Window
{
    private static readonly Vector4 Good = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 Bad = new(0.95f, 0.45f, 0.40f, 1f);
    private static readonly Vector4 Muted = new(0.65f, 0.65f, 0.65f, 1f);

    private readonly Play4MEPlugin _plugin;

    public MainWindow(Play4MEPlugin plugin)
        : base("Play4ME###Play4MEMain")
    {
        _plugin       = plugin;
        Size          = new Vector2(460, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(380, 300), MaximumSize = new Vector2(2000, 2000) };
    }

    private Play4MESettings Settings
        => _plugin.Configuration.Settings;

    public override void Draw()
    {
        var controller = _plugin.Controller;
        var enabled    = controller.Enabled;
        ImGui.PushStyleColor(ImGuiCol.Button, enabled ? new Vector4(0.15f, 0.5f, 0.2f, 1f) : new Vector4(0.35f, 0.35f, 0.35f, 1f));
        var clicked = ImGui.Button(enabled ? "Play4ME is ON  (click to stop)" : "Play4ME is OFF  (click to start)", new Vector2(-1, 36));
        ImGui.PopStyleColor();
        if (clicked)
            controller.Toggle();

        ImGui.TextWrapped($"Status: {controller.Status}");
        ImGui.TextColored(Muted, $"Activity: {controller.Activity}   AOEs: {controller.ForbiddenZones}   Dodges this session: {controller.DodgeCount}");

        ImGui.Spacing();
        ImGui.TextUnformatted("Plugins");
        ImGui.Separator();
        Dependency("BossmodReborn", _plugin.BossMod.IsAvailable,
            _plugin.BossMod.IsAvailable ? ModuleText() : "Not loaded. Required: it supplies the AOE telegraphs.");
        Dependency("vnavmesh", _plugin.Navmesh.IsAvailable,
            _plugin.Navmesh.IsAvailable ? (_plugin.Navmesh.IsReady() ? "Navmesh ready." : "Building navmesh for this zone...") : "Not loaded. Required: it moves your character.");
        Dependency("Wrath Combo", _plugin.Wrath.IsAvailable,
            _plugin.Wrath.IsAvailable ? $"{(_plugin.Wrath.Lease is null ? "Not leased." : "Leased by Play4ME.")} {_plugin.Wrath.LastMessage}" : "Not loaded. Optional: rotation.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Dodging");
        ImGui.Separator();
        Checkbox("Dodge AOEs", Settings.DodgeEnabled, v => Settings.DodgeEnabled = v);
        Checkbox("Only in combat", Settings.OnlyInCombat, v => Settings.OnlyInCombat = v);
        Float("Max dodge distance (y)", Settings.MaxDodgeDistance, SettingsSanitizer.MinDodge, SettingsSanitizer.MaxDodge, v => Settings.MaxDodgeDistance = v);
        Checkbox("Pathfind around obstacles when dodging (slower)", Settings.PathfindDodges, v => Settings.PathfindDodges = v);

        ImGui.Spacing();
        ImGui.TextUnformatted("Human-like movement");
        ImGui.Separator();
        Int("Reaction time min (ms)", Settings.ReactionMinMs, 0, 1000, v => Settings.ReactionMinMs = v);
        Int("Reaction time max (ms)", Settings.ReactionMaxMs, 0, 1500, v => Settings.ReactionMaxMs = v);
        Float("Clearance from AOE edge (y)", Settings.SafetyMargin, 0f, 3f, v => Settings.SafetyMargin = v);
        Float("Spot variation (y)", Settings.SpotVariation, 0f, 3f, v => Settings.SpotVariation = v);
        Checkbox("Dodge late (keep attacking until the hit is close)", Settings.LateDodge, v => Settings.LateDodge = v);
        if (Settings.LateDodge)
        {
            Float("Late dodge slack (s)", Settings.LateDodgeBuffer, 0.3f, 5f, v => Settings.LateDodgeBuffer = v);
            ImGui.TextColored(Muted, "Riskier: uses the timing of the first listed AOE. Raise the slack if you get clipped.");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Staying in range");
        ImGui.Separator();
        Checkbox("Walk back into range of my target when safe", Settings.ReturnToTarget, v => Settings.ReturnToTarget = v);
        Float("Melee distance from target edge (y)", Settings.MeleeRange, 0.5f, 10f, v => Settings.MeleeRange = v);
        Float("Ranged/healer distance from target edge (y)", Settings.RangedRange, 1f, SettingsSanitizer.MaxRange, v => Settings.RangedRange = v);

        ImGui.Spacing();
        ImGui.TextUnformatted("Wrath Combo");
        ImGui.Separator();
        Checkbox("Turn on Wrath Auto-Rotation while Play4ME is on", Settings.UseWrathAutoRotation, v => Settings.UseWrathAutoRotation = v);
        Checkbox("Let Wrath configure the job if it isn't set up", Settings.LetWrathConfigureJob, v => Settings.LetWrathConfigureJob = v);
        ImGui.TextColored(Muted, "Changes apply the next time you turn Play4ME on.");

        ImGui.Spacing();
        ImGui.TextUnformatted("General");
        ImGui.Separator();
        Checkbox("Remember on/off across restarts", Settings.RememberEnabled, v => Settings.RememberEnabled = v);
        Checkbox("Show notifications", Settings.ShowNotifications, v => Settings.ShowNotifications = v);
        ImGui.TextColored(Muted, "Commands: /play4me on | off | toggle   (short: /p4m)");
    }

    private string ModuleText()
        => _plugin.BossMod.HasActiveModule()
            ? $"Active fight: {_plugin.BossMod.ActiveModuleName() ?? "unknown"}."
            : "Loaded. No boss module active right now.";

    private void Checkbox(string label, bool value, Action<bool> set)
    {
        var v = value;
        if (ImGui.Checkbox(label, ref v))
        {
            set(v);
            _plugin.Save();
        }
    }

    private void Float(string label, float value, float min, float max, Action<float> set)
    {
        var v = value;
        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderFloat(label, ref v, min, max, "%.1f"))
        {
            set(v);
            _plugin.Save();
        }
    }

    private void Int(string label, int value, int min, int max, Action<int> set)
    {
        var v = value;
        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderInt(label, ref v, min, max))
        {
            set(v);
            _plugin.Save();
        }
    }

    private static void Dependency(string name, bool ok, string text)
    {
        ImGui.TextColored(ok ? Good : Bad, ok ? "OK " : "-- ");
        ImGui.SameLine();
        ImGui.TextUnformatted($"{name}:");
        ImGui.SameLine();
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }
}
