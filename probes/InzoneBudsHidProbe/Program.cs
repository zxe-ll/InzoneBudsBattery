using InzoneBudsHidProbe;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var options = ProbeOptions.Parse(args);
if (options.ShowHelp)
{
    ProbeOptions.PrintHelp();
    return 0;
}

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

Console.WriteLine("INZONE Buds HID Probe");
Console.WriteLine("Press Ctrl+C to stop safely.");
Console.WriteLine();

try
{
    var probe = new HidProbe(options);
    await probe.RunAsync(shutdown.Token);
    return 0;
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Fatal error: {exception}");
    return 1;
}
finally
{
    Console.WriteLine("Probe stopped.");
}
