using InzoneBudsBattery;

const int vendorId = 0x054C;
const int productId = 0x0EC2;

Console.WriteLine("Native Windows HID smoke test");
var devices = WindowsHid.Enumerate(vendorId, productId);
Console.WriteLine($"Found {devices.Count} interface(s).");

foreach (var device in devices)
{
    Console.WriteLine($"Input={device.InputReportLength}: {device.DevicePath}");
}

var batteryInterface = devices.FirstOrDefault(static device => device.InputReportLength == 64);
if (batteryInterface is null)
{
    Console.Error.WriteLine("No 64-byte battery interface was found.");
    return 1;
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
    Console.WriteLine($"Battery: L={report[16]}% R={report[14]}% Case={report[18]}%");
}
else
{
    Console.WriteLine("The control request returned a non-battery report; interrupt report waiting remains required.");
}

return 0;
