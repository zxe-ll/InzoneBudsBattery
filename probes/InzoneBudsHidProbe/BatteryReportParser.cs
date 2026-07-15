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

        var right = (int)report[14];
        var left = (int)report[16];
        var batteryCase = (int)report[18];

        if (left > 100 || right > 100 || batteryCase > 100)
        {
            validationError = $"Battery report has an out-of-range value: L={left}, R={right}, Case={batteryCase}.";
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
            left,
            right,
            batteryCase,
            report[19] == expectedChecksum,
            receivedAt);
        return true;
    }
}
