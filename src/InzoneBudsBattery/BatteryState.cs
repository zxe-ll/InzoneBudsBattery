namespace InzoneBudsBattery;

public sealed record BatteryState
{
    public int? LeftPercent { get; init; }

    public int? RightPercent { get; init; }

    public int? CasePercent { get; init; }

    public DateTimeOffset? LastUpdatedAt { get; init; }

    public bool IsTransmitterConnected { get; init; }

    public bool HasReceivedBatteryReport { get; init; }

    public bool? ChecksumValid { get; init; }

    public string? ErrorMessage { get; init; }

    public string? DevicePath { get; init; }

    public byte[]? RawReport { get; init; }

    public DateTimeOffset? LastRefreshAttemptAt { get; init; }

    public bool? PeriodicRefreshSupported { get; init; }

    public int? MinimumEarbudPercent => LeftPercent is { } left && RightPercent is { } right
        ? Math.Min(left, right)
        : LeftPercent ?? RightPercent;
}
