using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Play4ME.Services;
using Play4ME.Windows;

namespace Play4ME;

public sealed class Play4MEPlugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface _pi;
    private readonly ICommandManager _commands;
    private readonly IFramework _framework;
    private readonly WindowSystem _windows = new("Play4ME");
    private readonly MainWindow _mainWindow;

    public Play4MEPlugin(IDalamudPluginInterface pi, ICommandManager commands, IFramework framework, IObjectTable objects,
        ITargetManager targets, ICondition condition, IClientState clientState, INotificationManager notifications, IPluginLog log)
    {
        _pi        = pi;
        _commands  = commands;
        _framework = framework;

        Configuration = pi.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Settings ??= new Play4MESettings();
        if (!Configuration.Settings.RememberEnabled)
            Configuration.Settings.Enabled = false; // always start off unless the user asked to remember
        SettingsSanitizer.Sanitize(Configuration.Settings);
        Save();

        BossMod    = new BossModIpc(pi, log);
        Navmesh    = new NavmeshIpc(pi, log);
        Wrath      = new WrathIpc(pi, log);
        Controller = new Play4MEController(Configuration, Save, BossMod, Navmesh, Wrath, objects, targets, condition, clientState, notifications, log);

        // If the user chose to remember "on", re-acquire Wrath on load.
        if (Configuration.Settings.Enabled)
        {
            Configuration.Settings.Enabled = false;
            Controller.SetEnabled(true);
        }

        _mainWindow = new MainWindow(this);
        _windows.AddWindow(_mainWindow);

        var info = new CommandInfo(OnCommand) { HelpMessage = "Open Play4ME. '/play4me on', '/play4me off' or '/play4me toggle' switches it." };
        _commands.AddHandler("/play4me", info);
        _commands.AddHandler("/p4m", new CommandInfo(OnCommand) { HelpMessage = "Short for /play4me.", ShowInHelp = false });

        _pi.UiBuilder.Draw         += _windows.Draw;
        _pi.UiBuilder.OpenMainUi   += ToggleWindow;
        _pi.UiBuilder.OpenConfigUi += ToggleWindow;
        _framework.Update          += OnUpdate;
    }

    public Configuration Configuration { get; }
    public BossModIpc BossMod { get; }
    public NavmeshIpc Navmesh { get; }
    public WrathIpc Wrath { get; }
    public Play4MEController Controller { get; }

    public void Save()
    {
        SettingsSanitizer.Sanitize(Configuration.Settings);
        _pi.SavePluginConfig(Configuration);
    }

    public void ToggleWindow()
        => _mainWindow.Toggle();

    public void Dispose()
    {
        _framework.Update          -= OnUpdate;
        _pi.UiBuilder.Draw         -= _windows.Draw;
        _pi.UiBuilder.OpenMainUi   -= ToggleWindow;
        _pi.UiBuilder.OpenConfigUi -= ToggleWindow;
        _commands.RemoveHandler("/play4me");
        _commands.RemoveHandler("/p4m");
        _windows.RemoveAllWindows();
        Controller.Dispose();
    }

    private void OnUpdate(IFramework framework)
        => Controller.Tick(DateTime.UtcNow);

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "on":     Controller.SetEnabled(true); break;
            case "off":    Controller.SetEnabled(false); break;
            case "toggle": Controller.Toggle(); break;
            default:       ToggleWindow(); break;
        }
    }
}
