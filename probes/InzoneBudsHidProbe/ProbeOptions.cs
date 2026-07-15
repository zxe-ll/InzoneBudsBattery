namespace InzoneBudsHidProbe;

internal sealed record ProbeOptions(bool LogRawReports, int ScanIntervalSeconds, bool ShowHelp)
{
    public static ProbeOptions Parse(string[] args)
    {
        var logRawReports = true;
        var scanIntervalSeconds = 2;
        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--no-raw":
                    logRawReports = false;
                    break;
                case "--scan-seconds" when index + 1 < args.Length:
                    if (!int.TryParse(args[++index], out scanIntervalSeconds) || scanIntervalSeconds is < 1 or > 60)
                    {
                        throw new ArgumentException("--scan-seconds must be an integer from 1 to 60.");
                    }

                    break;
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }

        return new ProbeOptions(logRawReports, scanIntervalSeconds, showHelp);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Usage: InzoneBudsHidProbe [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --no-raw             Do not print every HID report as hexadecimal.");
        Console.WriteLine("  --scan-seconds <n>   Device rescan interval, from 1 to 60 (default: 2).");
        Console.WriteLine("  -h, --help           Show this help.");
    }
}
