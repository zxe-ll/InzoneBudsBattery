using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace InzoneBudsBattery;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/inzone";
    private const string DtrEntryName = "InzoneBudsBattery";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IPluginLog _log;
    private readonly ICondition _condition;
    private readonly INotificationManager _notificationManager;
    private readonly IDtrBar _dtrBar;
    private readonly IDtrBarEntry _dtrEntry;
    private readonly WindowSystem _windowSystem = new("InzoneBudsBattery");
    private readonly Configuration _configuration;
    private readonly InzoneBatteryService _batteryService;
    private readonly OverlayWindow _overlayWindow;
    private readonly ConfigWindow _configWindow;
    private bool _disposed;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IPluginLog log,
        ICondition condition,
        INotificationManager notificationManager,
        IDtrBar dtrBar)
    {
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _log = log;
        _condition = condition;
        _notificationManager = notificationManager;
        _dtrBar = dtrBar;

        _configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _configuration.Initialize(pluginInterface);
        _batteryService = new InzoneBatteryService(log, _configuration);

        _overlayWindow = new OverlayWindow(_configuration, _batteryService);
        _configWindow = new ConfigWindow(_configuration, _batteryService, _overlayWindow);
        _windowSystem.AddWindow(_overlayWindow);
        _windowSystem.AddWindow(_configWindow);

        _dtrEntry = dtrBar.Get(DtrEntryName);
        _dtrEntry.Shown = false;

        commandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                HelpMessage = "設定画面を開きます。status / reconnect / debug も使用できます。",
                ShowInHelp = true,
            });

        pluginInterface.UiBuilder.Draw += DrawUi;
        pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        pluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;
        _log.Info("INZONE Buds Battery plugin loaded.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pluginInterface.UiBuilder.Draw -= DrawUi;
        _pluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        _pluginInterface.UiBuilder.OpenMainUi -= OpenConfigUi;
        _pluginInterface.UiBuilder.DisableUserUiHide = false;
        _commandManager.RemoveHandler(CommandName);
        _dtrEntry.Shown = false;
        _dtrBar.Remove(DtrEntryName);
        _windowSystem.RemoveAllWindows();
        _configuration.Save();
        _batteryService.Dispose();
        _log.Info("INZONE Buds Battery plugin unloaded.");
    }

    private void DrawUi()
    {
        var state = _batteryService.Current;
        UpdateDtr(state);

        _pluginInterface.UiBuilder.DisableUserUiHide = _configuration.ShowWhenGameUiHidden;

        var shouldShowOverlay = _configuration.OverlayEnabled
                                && (_configuration.ShowInCombat || !_condition[ConditionFlag.InCombat])
                                && (_configuration.ShowWhenGameUiHidden || _pluginInterface.UiBuilder.ShouldModifyUi)
                                && (!_configuration.HideWhenDisconnected || state.IsTransmitterConnected);
        _overlayWindow.IsOpen = shouldShowOverlay;
        _windowSystem.Draw();
    }

    private void UpdateDtr(BatteryState state)
    {
        _dtrEntry.Shown = _configuration.DtrEnabled;
        if (!_configuration.DtrEnabled)
        {
            return;
        }

        _dtrEntry.Text = _configuration.DtrShowDetails
            ? $"L{FormatCompact(state.LeftPercent)} R{FormatCompact(state.RightPercent)} C{FormatCompact(state.CasePercent)}"
            : state.MinimumEarbudPercent is { } minimum
                ? $"INZONE {minimum}%"
                : "INZONE --";

        _dtrEntry.Tooltip = state.IsTransmitterConnected
            ? state.HasReceivedBatteryReport
                ? $"INZONE Buds\nL {Format(state.LeftPercent)}  R {Format(state.RightPercent)}\nCase {Format(state.CasePercent)}"
                : "INZONE Buds\n残量取得待ち..."
            : "INZONE Buds\n未接続";
    }

    private void OnCommand(string command, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "":
                _configWindow.Toggle();
                break;
            case "status":
                ShowStatusNotification();
                break;
            case "reconnect":
                _batteryService.RequestReconnect();
                _notificationManager.AddNotification(
                    new Notification
                    {
                        Title = "INZONE Buds Battery",
                        Content = "HID再接続を開始しました。",
                        Type = NotificationType.Info,
                    });
                break;
            case "debug":
                _configuration.DebugLogging = !_configuration.DebugLogging;
                _configuration.Save();
                _notificationManager.AddNotification(
                    new Notification
                    {
                        Title = "INZONE Buds Battery",
                        Content = $"デバッグログ: {(_configuration.DebugLogging ? "ON" : "OFF")}",
                        Type = NotificationType.Info,
                    });
                break;
            default:
                _notificationManager.AddNotification(
                    new Notification
                    {
                        Title = "INZONE Buds Battery",
                        Content = "使用法: /inzone [status|reconnect|debug]",
                        Type = NotificationType.Warning,
                    });
                break;
        }
    }

    private void OpenConfigUi()
    {
        _configWindow.IsOpen = true;
        _configWindow.BringToFront();
    }

    private void ShowStatusNotification()
    {
        var state = _batteryService.Current;
        var content = !state.IsTransmitterConnected
            ? "トランシッター未接続"
            : !state.HasReceivedBatteryReport
                ? "接続済み・残量取得待ち"
                : $"L {Format(state.LeftPercent)} / R {Format(state.RightPercent)} / Case {Format(state.CasePercent)}";

        _notificationManager.AddNotification(
            new Notification
            {
                Title = "INZONE Buds Battery",
                Content = content,
                Type = NotificationType.Info,
            });
    }

    private static string Format(int? value) => value is { } percentage ? $"{percentage}%" : "--";

    private static string FormatCompact(int? value) => value is { } percentage ? percentage.ToString() : "--";
}
