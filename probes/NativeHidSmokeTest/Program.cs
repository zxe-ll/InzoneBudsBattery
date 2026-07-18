using InzoneBudsBattery;

const int vendorId = 0x054C;
const int productId = 0x0EC2;

Console.WriteLine("Native Windows HID smoke test");
var devices = WindowsHid.Enumerate(vendorId, productId);
Console.WriteLine($"Found {devices.Count} interface(s).");

foreach (var device in devices)
{
    Console.WriteLine(
        $"Input={device.InputReportLength}, Output={device.OutputReportLength}, "
        + $"Usage={device.UsagePage:X4}:{device.Usage:X4}: {device.DevicePath}");
}

var batteryInterface = devices.FirstOrDefault(static device => device.InputReportLength == 64);
if (batteryInterface is null)
{
    Console.Error.WriteLine("No 64-byte battery interface was found.");
    return 1;
}

if (BudsHciProtocol.BuildBatteryGetReport(batteryInterface.OutputReportLength, 1) is { } batteryRequest)
{
    try
    {
        using var stream = batteryInterface.OpenReadWrite();
        Console.WriteLine("Shared read/write handle opened successfully.");
        await stream.WriteAsync(batteryRequest);
        await stream.FlushAsync();
        Console.WriteLine($"Battery GET sent: {Convert.ToHexString(batteryRequest)}");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var buffer = new byte[Math.Max(64, batteryInterface.InputReportLength)];
        while (!timeout.IsCancellationRequested)
        {
            var bytesRead = await stream.ReadAsync(buffer, timeout.Token);
            var received = buffer.AsSpan(0, bytesRead);
            Console.WriteLine($"Interrupt report: {Convert.ToHexString(received)}");
            if (BatteryReportParser.TryParse(
                    received,
                    DateTimeOffset.Now,
                    out var battery,
                    out var validationError))
            {
                Console.WriteLine(
                    $"Battery GET response: L={FormatBattery(battery!.LeftPercent)} "
                    + $"R={FormatBattery(battery.RightPercent)} Case={FormatBattery(battery.CasePercent)}");
                return 0;
            }

            if (validationError is not null)
            {
                Console.WriteLine(validationError);
            }
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("No battery response was received within three seconds.");
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
    {
        Console.WriteLine($"Active battery query unavailable: {exception.Message}");
    }
}
else
{
    Console.WriteLine("The battery interface does not expose a 64-byte output report.");
}

using (batteryInterface.OpenRead())
{
    Console.WriteLine("Shared read handle opened successfully.");
}

if (!WindowsHid.TryGetInputReport(
        batteryInterface.DevicePath,
        batteryInterface.InputReportLength,
        0x02,
        out var report,
        out var errorCode))
{
    Console.WriteLine($"HidD_GetInputReport is not supported or failed (Win32 error {errorCode}).");
    return 0;
}

Console.WriteLine($"HidD_GetInputReport succeeded: {Convert.ToHexString(report)}");
if (report.Length >= 20 && report[0] == 0x02 && report[1] == 0x12 && report[2] == 0x04)
{
    Console.WriteLine($"Battery: L={report[14]}% R={report[16]}% Case={report[18]}%");
}
else
{
    Console.WriteLine("The control request returned a non-battery report; interrupt report waiting remains required.");
}

return 0;

static string FormatBattery(int? value) => value is null ? "--" : $"{value}%";
