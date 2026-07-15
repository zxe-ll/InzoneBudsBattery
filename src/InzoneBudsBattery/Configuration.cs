using Dalamud.Configuration;
using Dalamud.Plugin;

namespace InzoneBudsBattery;

public enum EarbudDisplayMode
{
    Individual,
    Minimum,
}

public sealed class Configuration : IPluginConfiguration
{
    [NonSerialized]
    private IDalamudPluginInterface? _pluginInterface;

    public int Version { get; set; } = 1;

    public bool OverlayEnabled { get; set; } = true;

    public EarbudDisplayMode DisplayMode { get; set; } = EarbudDisplayMode.Individual;

    public bool ShowCase { get; set; } = true;

    public bool ShowBackground { get; set; } = true;

    public float PositionX { get; set; } = 100f;

    public float PositionY { get; set; } = 100f;

    public float FontScale { get; set; } = 1.15f;

    public float Opacity { get; set; } = 0.75f;

    public bool LockPosition { get; set; }

    public int LowBatteryThreshold { get; set; } = 20;

    public int CriticalBatteryThreshold { get; set; } = 10;

    public bool ShowInCombat { get; set; } = true;

    public bool ShowWhenGameUiHidden { get; set; }

    public bool ShowLastUpdated { get; set; }

    public int StaleAfterMinutes { get; set; } = 10;

    public int RefreshIntervalMinutes { get; set; } = 3;

    public bool HideWhenDisconnected { get; set; }

    public bool EnableCriticalNotification { get; set; } = true;

    public bool DtrEnabled { get; set; }

    public bool DtrShowDetails { get; set; }

    public bool DebugLogging { get; set; }

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        Normalize();
    }

    public void Save()
    {
        Normalize();
        _pluginInterface?.SavePluginConfig(this);
    }

    private void Normalize()
    {
        FontScale = Math.Clamp(FontScale, 0.7f, 3f);
        Opacity = Math.Clamp(Opacity, 0f, 1f);
        LowBatteryThreshold = Math.Clamp(LowBatteryThreshold, 1, 100);
        CriticalBatteryThreshold = Math.Clamp(CriticalBatteryThreshold, 1, LowBatteryThreshold);
        StaleAfterMinutes = Math.Clamp(StaleAfterMinutes, 1, 1440);
        RefreshIntervalMinutes = Math.Clamp(RefreshIntervalMinutes, 1, 60);
    }
}
