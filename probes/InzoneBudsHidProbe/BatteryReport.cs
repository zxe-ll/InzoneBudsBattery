namespace InzoneBudsHidProbe;

internal sealed record BatteryReport(
    int LeftPercent,
    int RightPercent,
    int CasePercent,
    bool ChecksumValid,
    DateTimeOffset ReceivedAt);
