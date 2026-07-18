namespace InzoneBudsHidProbe;

internal static class ConsoleLog
{
    private static readonly object Sync = new();

    public static void Info(string message) => Write(Console.Out, "INFO", message);

    public static void Warn(string message) => Write(Console.Out, "WARN", message);

    public static void Error(string message) => Write(Console.Error, "ERROR", message);

    public static void Raw(string path, ReadOnlySpan<byte> report)
    {
        lock (Sync)
        {
            Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] RAW  {path}");
            Console.WriteLine(Convert.ToHexString(report).ToLowerInvariant());
        }
    }

    public static void Battery(string path, BatteryReport report)
    {
        lock (Sync)
        {
            Console.WriteLine();
            Console.WriteLine("Battery report received:");
            Console.WriteLine($"Interface: {path}");
            Console.WriteLine($"Left:  {FormatBattery(report.LeftPercent)}");
            Console.WriteLine($"Right: {FormatBattery(report.RightPercent)}");
            Console.WriteLine($"Case:  {FormatBattery(report.CasePercent)}");
            Console.WriteLine($"Checksum: {(report.ChecksumValid ? "match" : "MISMATCH (not rejected)")}");
            Console.WriteLine($"Received: {report.ReceivedAt:yyyy-MM-dd HH:mm:ss zzz}");
            Console.WriteLine();
        }
    }

    private static string FormatBattery(int? value) => value is null ? "--" : $"{value}%";

    private static void Write(TextWriter writer, string level, string message)
    {
        lock (Sync)
        {
            writer.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {level,-5} {message}");
        }
    }
}
