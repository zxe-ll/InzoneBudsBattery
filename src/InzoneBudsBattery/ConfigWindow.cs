using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace InzoneBudsBattery;

internal sealed class ConfigWindow : Window
{
    private readonly Configuration _configuration;
    private readonly InzoneBatteryService _batteryService;
    private readonly OverlayWindow _overlayWindow;

    public ConfigWindow(
        Configuration configuration,
        InzoneBatteryService batteryService,
        OverlayWindow overlayWindow)
        : base("INZONE Buds Battery 設定###InzoneBudsBatteryConfig")
    {
        _configuration = configuration;
        _batteryService = batteryService;
        _overlayWindow = overlayWindow;
        Size = new System.Numerics.Vector2(480f, 600f);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var changed = false;

        DrawSection("表示");
        changed |= Checkbox("オーバーレイを表示", nameof(Configuration.OverlayEnabled));

        if (ImGui.RadioButton("左右を個別表示", _configuration.DisplayMode == EarbudDisplayMode.Individual))
        {
            _configuration.DisplayMode = EarbudDisplayMode.Individual;
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("低い方のみ", _configuration.DisplayMode == EarbudDisplayMode.Minimum))
        {
            _configuration.DisplayMode = EarbudDisplayMode.Minimum;
            changed = true;
        }

        changed |= Checkbox("ケース残量を表示", nameof(Configuration.ShowCase));
        changed |= Checkbox("背景を表示", nameof(Configuration.ShowBackground));
        changed |= Checkbox("位置を固定", nameof(Configuration.LockPosition));
        changed |= Checkbox("未接続時は非表示", nameof(Configuration.HideWhenDisconnected));
        changed |= Checkbox("最終更新時刻を表示", nameof(Configuration.ShowLastUpdated));

        var fontScale = _configuration.FontScale;
        if (ImGui.SliderFloat("文字サイズ", ref fontScale, 0.7f, 3f, "%.2fx"))
        {
            _configuration.FontScale = fontScale;
            changed = true;
        }

        var opacity = _configuration.Opacity;
        if (ImGui.SliderFloat("背景の透明度", ref opacity, 0f, 1f, "%.2f"))
        {
            _configuration.Opacity = opacity;
            changed = true;
        }

        var positionX = _configuration.PositionX;
        var positionY = _configuration.PositionY;
        if (ImGui.InputFloat("位置 X", ref positionX))
        {
            _configuration.PositionX = positionX;
            changed = true;
        }

        if (ImGui.InputFloat("位置 Y", ref positionY))
        {
            _configuration.PositionY = positionY;
            changed = true;
        }

        if (ImGui.Button("設定した位置へ移動"))
        {
            _overlayWindow.MoveToConfiguredPosition();
        }

        DrawSection("表示条件");
        changed |= Checkbox("戦闘中も表示", nameof(Configuration.ShowInCombat));
        changed |= Checkbox("FF14のUI非表示中も表示", nameof(Configuration.ShowWhenGameUiHidden));

        var staleMinutes = _configuration.StaleAfterMinutes;
        if (ImGui.SliderInt("古い値とみなす時間（分）", ref staleMinutes, 1, 120))
        {
            _configuration.StaleAfterMinutes = staleMinutes;
            changed = true;
        }

        var refreshMinutes = _configuration.RefreshIntervalMinutes;
        if (ImGui.SliderInt("定期更新の試行間隔（分）", ref refreshMinutes, 1, 30))
        {
            _configuration.RefreshIntervalMinutes = refreshMinutes;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Sony HCIの電池GET要求を送信します。応答しない場合も自発レポートの待受を継続します。");
        }

        DrawSection("残量警告");
        var lowThreshold = _configuration.LowBatteryThreshold;
        if (ImGui.SliderInt("警告しきい値", ref lowThreshold, 1, 100, "%d%%"))
        {
            _configuration.LowBatteryThreshold = lowThreshold;
            changed = true;
        }

        var criticalThreshold = _configuration.CriticalBatteryThreshold;
        if (ImGui.SliderInt("危険しきい値", ref criticalThreshold, 1, 100, "%d%%"))
        {
            _configuration.CriticalBatteryThreshold = criticalThreshold;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("警告しきい値よりも低い値を設定して下さい。");
        }

        DrawSection("DTRバー");
        changed |= Checkbox("DTRバーへ表示", nameof(Configuration.DtrEnabled));
        changed |= Checkbox("DTRに左右・ケースを表示", nameof(Configuration.DtrShowDetails));

        DrawSection("診断");
        changed |= Checkbox("デバッグログを有効化", nameof(Configuration.DebugLogging));

        var state = _batteryService.Current;
        ImGui.TextUnformatted($"接続: {(state.IsTransmitterConnected ? "接続済み" : "未接続")}");
        ImGui.TextUnformatted($"残量: L {Format(state.LeftPercent)} / R {Format(state.RightPercent)} / Case {Format(state.CasePercent)}");
        ImGui.TextUnformatted($"チェックサム: {FormatChecksum(state.ChecksumValid)}");
        ImGui.TextUnformatted($"定期更新: {FormatRefreshSupport(state.PeriodicRefreshSupported)}");
        if (state.LastUpdatedAt is { } updatedAt)
        {
            ImGui.TextUnformatted($"最終受信: {updatedAt:yyyy-MM-dd HH:mm:ss zzz}");
        }

        if (state.LastRefreshAttemptAt is { } refreshAttemptAt)
        {
            ImGui.TextUnformatted($"最終強制更新試行: {refreshAttemptAt:yyyy-MM-dd HH:mm:ss zzz}");
        }

        if (!string.IsNullOrWhiteSpace(state.ErrorMessage))
        {
            ImGui.TextWrapped($"エラー: {state.ErrorMessage}");
        }

        if (ImGui.Button("HIDを再接続"))
        {
            _batteryService.RequestReconnect();
        }

        if (changed)
        {
            _configuration.Save();
        }
    }

    private bool Checkbox(string label, string propertyName)
    {
        var property = typeof(Configuration).GetProperty(propertyName)
                       ?? throw new InvalidOperationException($"Unknown configuration property: {propertyName}");
        var value = (bool)(property.GetValue(_configuration) ?? false);
        if (!ImGui.Checkbox(label, ref value))
        {
            return false;
        }

        property.SetValue(_configuration, value);
        return true;
    }

    private static void DrawSection(string label)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(label);
    }

    private static string Format(int? value) => value is { } percentage ? $"{percentage}%" : "--";

    private static string FormatChecksum(bool? value) => value switch
    {
        true => "一致",
        false => "不一致",
        null => "未取得",
    };

    private static string FormatRefreshSupport(bool? value) => value switch
    {
        true => "電池GET応答あり",
        false => "電池GET応答なし（自発レポート待受中）",
        null => "電池GET応答待ち",
    };
}
