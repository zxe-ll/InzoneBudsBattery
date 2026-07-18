namespace InzoneBudsHidProbe;

internal static class BatteryReportParser
{
    private const int MinimumReportLength = 20;

    public static bool HasBatteryHeader(ReadOnlySpan<byte> report) =>
        report.Length >= MinimumReportLength
        && report[0] == 0x02
        && report[1] == 0x12
        && report[2] == 0x04;

    public static bool TryParse(
        ReadOnlySpan<byte> report,
        DateTimeOffset receivedAt,
        out BatteryReport? batteryReport,
        out string? validationError)
    {
        batteryReport = null;
        validationError = null;

        if (!HasBatteryHeader(report))
        {
            return false;
        }

        var leftRaw = report[14];
        var rightRaw = report[16];
        var caseRaw = report[18];

        if (IsInvalidPercentage(leftRaw) || IsInvalidPercentage(rightRaw) || IsInvalidPercentage(caseRaw))
        {
            validationError = $"Battery report has an out-of-range value: L={leftRaw}, R={rightRaw}, Case={caseRaw}.";
            return false;
        }

        // Real reports captured both with and without INZONE Hub show that byte[19]
        // is the modulo-256 sum of bytes 5 through 18. This includes session fields,
        // so a checksum derived from battery values alone only matches some reports.
        var checksumSum = 0;
        for (var index = 5; index < 19; index++)
        {
            checksumSum += report[index];
        }

        var expectedChecksum = (byte)(checksumSum & 0xFF);
        batteryReport = new BatteryReport(
            ToPercentage(leftRaw),
            ToPercentage(rightRaw),
            ToPercentage(caseRaw),
            report[19] == expectedChecksum,
            receivedAt);
        return true;
    }

    private static bool IsInvalidPercentage(byte value) => value > 100 && value != byte.MaxValue;

    private static int? ToPercentage(byte value) => value == byte.MaxValue ? null : value;
}
