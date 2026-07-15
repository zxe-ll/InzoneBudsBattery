using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace InzoneBudsBattery;

internal sealed class OverlayWindow : Window
{
    private static readonly Vector4 NormalColor = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 LowColor = new(1f, 0.78f, 0.2f, 1f);
    private static readonly Vector4 CriticalColor = new(1f, 0.25f, 0.2f, 1f);
    private static readonly Vector4 StaleColor = new(0.72f, 0.72f, 0.72f, 1f);

    private readonly Configuration _configuration;
    private readonly InzoneBatteryService _batteryService;

    public OverlayWindow(Configuration configuration, InzoneBatteryService batteryService)
        : base("INZONE Buds Overlay###InzoneBudsBatteryOverlay")
    {
        _configuration = configuration;
        _batteryService = batteryService;
        DisableWindowSounds = true;
        DisableFadeInFadeOut = true;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        Position = new Vector2(configuration.PositionX, configuration.PositionY);
        PositionCondition = ImGuiCond.FirstUseEver;
        IsOpen = true;
    }

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.NoTitleBar
                | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoScrollWithMouse
                | ImGuiWindowFlags.AlwaysAutoResize
                | ImGuiWindowFlags.NoSavedSettings
                | ImGuiWindowFlags.NoFocusOnAppearing
                | ImGuiWindowFlags.NoNav;

        if (_configuration.LockPosition)
        {
            Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs;
        }

        BgAlpha = _configuration.ShowBackground ? _configuration.Opacity : 0f;
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(_configuration.FontScale);
        var state = _batteryService.Current;

        var compactDisplay = _configuration.DisplayMode == EarbudDisplayMode.Minimum
                             && state.IsTransmitterConnected
                             && state.HasReceivedBatteryReport;
        if (!compactDisplay)
        {
            ImGui.TextUnformatted("INZONE Buds");
        }

        if (!state.IsTransmitterConnected)
        {
            DrawColoredText("未接続", StaleColor);
            RememberPosition();
            return;
        }

        if (!state.HasReceivedBatteryReport)
        {
            DrawColoredText("残量取得待ち...", StaleColor);
            RememberPosition();
            return;
        }

        var isStale = state.LastUpdatedAt is { } updatedAt
                      && DateTimeOffset.Now - updatedAt >= TimeSpan.FromMinutes(_configuration.StaleAfterMinutes);
        var color = GetBatteryColor(state.MinimumEarbudPercent, isStale);

        if (_configuration.DisplayMode == EarbudDisplayMode.Minimum)
        {
            DrawColoredText(
                state.MinimumEarbudPercent is { } minimum ? $"INZONE {minimum}%" : "INZONE --%",
                color);
        }
        else
        {
            var left = state.LeftPercent is { } leftPercent ? $"{leftPercent}%" : "--%";
            var right = state.RightPercent is { } rightPercent ? $"{rightPercent}%" : "--%";
            DrawColoredText($"L {left}  R {right}", color);
        }

        if (_configuration.ShowCase)
        {
            var batteryCase = state.CasePercent is { } casePercent ? $"{casePercent}%" : "--%";
            DrawColoredText($"Case {batteryCase}", isStale ? StaleColor : NormalColor);
        }

        if (_configuration.ShowLastUpdated && state.LastUpdatedAt is { } lastUpdated)
        {
            var age = DateTimeOffset.Now - lastUpdated;
            var ageText = age.TotalMinutes < 1
                ? "たった今"
                : age.TotalHours < 1
                    ? $"{(int)age.TotalMinutes}分前"
                    : $"{(int)age.TotalHours}時間前";
            DrawColoredText($"更新: {ageText}{(isStale ? " (古い値)" : string.Empty)}", StaleColor);
        }

        RememberPosition();
    }

    public void MoveToConfiguredPosition()
    {
        Position = new Vector2(_configuration.PositionX, _configuration.PositionY);
        PositionCondition = ImGuiCond.Always;
    }

    private void RememberPosition()
    {
        if (_configuration.LockPosition)
        {
            return;
        }

        var position = ImGui.GetWindowPos();
        _configuration.PositionX = position.X;
        _configuration.PositionY = position.Y;
    }

    private Vector4 GetBatteryColor(int? percentage, bool isStale)
    {
        if (isStale || percentage is null)
        {
            return StaleColor;
        }

        if (percentage <= _configuration.CriticalBatteryThreshold)
        {
            return CriticalColor;
        }

        return percentage <= _configuration.LowBatteryThreshold ? LowColor : NormalColor;
    }

    private static void DrawColoredText(string text, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }
}
