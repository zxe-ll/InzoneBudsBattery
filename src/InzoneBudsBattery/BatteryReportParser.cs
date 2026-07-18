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

        var leftRaw = report[14];
        var rightRaw = report[16];
        var caseRaw = report[18];
        if (IsInvalidPercentage(rightRaw) || IsInvalidPercentage(leftRaw) || IsInvalidPercentage(caseRaw))
        {
            validationError = $"Invalid battery values: L={leftRaw}, R={rightRaw}, Case={caseRaw}.";
            return false;
        }

        var right = ToPercentage(rightRaw);
        var left = ToPercentage(leftRaw);
        var batteryCase = ToPercentage(caseRaw);

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

    private static bool IsInvalidPercentage(byte value) => value > 100 && value != byte.MaxValue;

    private static int? ToPercentage(byte value) => value == byte.MaxValue ? null : value;
}
