namespace InzoneBudsBattery;

internal static class BatteryReportParser
{
    private const int MinimumReportLength = 20;

    public static bool TryParse(
        ReadOnlySpan<byte> report,
        DateTimeOffset receivedAt,
        out BatteryState? state,
        out string? validationError)
    {
        state = null;
        validationError = null;

        if (report.Length < MinimumReportLength
            || report[0] != 0x02
            || report[1] != 0x12
            || report[2] != 0x04)
        {
            return false;
        }

        var right = (int)report[14];
        var left = (int)report[16];
        var batteryCase = (int)report[18];
        if (left > 100 || right > 100 || batteryCase > 100)
        {
            validationError = $"Invalid battery values: L={left}, R={right}, Case={batteryCase}.";
            return false;
        }

        var checksumSum = 0;
        for (var index = 5; index < 19; index++)
        {
            checksumSum += report[index];
        }

        state = new BatteryState
        {
            LeftPercent = left,
            RightPercent = right,
            CasePercent = batteryCase,
            LastUpdatedAt = receivedAt,
            IsTransmitterConnected = true,
            HasReceivedBatteryReport = true,
            ChecksumValid = report[19] == (byte)(checksumSum & 0xFF),
            RawReport = report.ToArray(),
        };
        return true;
    }
}
