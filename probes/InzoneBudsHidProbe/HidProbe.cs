using System.Collections.Concurrent;
using HidSharp;

namespace InzoneBudsHidProbe;

internal sealed class HidProbe
{
    private const int VendorId = 0x054C;
    private const int ProductId = 0x0EC2;
    private const int MinimumBufferLength = 64;

    private readonly ProbeOptions _options;
    private readonly ConcurrentDictionary<string, Task> _readers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _nonReadablePaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _wasDevicePresent;

    public HidProbe(ProbeOptions options)
    {
        _options = options;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ConsoleLog.Info($"Scanning for HID devices VID={VendorId:X4}, PID={ProductId:X4}.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                RemoveCompletedReaders();
                DiscoverAndStartReaders(cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(_options.ScanIntervalSeconds), cancellationToken);
            }
        }
        finally
        {
            ConsoleLog.Info("Stopping HID readers...");
            await WaitForReadersToStopAsync();
        }
    }

    private void DiscoverAndStartReaders(CancellationToken cancellationToken)
    {
        HidDevice[] devices;
        try
        {
            devices = DeviceList.Local.GetHidDevices(VendorId, ProductId).ToArray();
        }
        catch (Exception exception)
        {
            ConsoleLog.Error($"Device enumeration failed: {exception.Message}");
            return;
        }

        if (devices.Length == 0)
        {
            if (_wasDevicePresent)
            {
                ConsoleLog.Warn("INZONE Buds transmitter disconnected.");
            }

            _wasDevicePresent = false;
            return;
        }

        if (!_wasDevicePresent)
        {
            ConsoleLog.Info($"INZONE Buds transmitter found ({devices.Length} HID interface(s)).");
            ConsoleLog.Info("Waiting for battery report...");
        }

        _wasDevicePresent = true;

        foreach (var device in devices)
        {
            var path = device.DevicePath;
            if (device.GetMaxInputReportLength() <= 0)
            {
                if (_nonReadablePaths.Add(path))
                {
                    ConsoleLog.Info($"Skipping interface with no input reports: {path}");
                }

                continue;
            }

            if (_readers.ContainsKey(path))
            {
                continue;
            }

            var reader = ReadDeviceAsync(device, cancellationToken);
            if (!_readers.TryAdd(path, reader))
            {
                _ = reader.ContinueWith(
                    static completed => _ = completed.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
        }
    }

    private async Task ReadDeviceAsync(HidDevice device, CancellationToken cancellationToken)
    {
        var path = device.DevicePath;
        HidStream? stream = null;

        try
        {
            ConsoleLog.Info($"Opening HID interface: {path}");
            if (!device.TryOpen(out stream))
            {
                ConsoleLog.Warn($"Could not open HID interface (possibly in use): {path}");
                return;
            }

            ConsoleLog.Info($"HID interface opened; input report length={device.GetMaxInputReportLength()}: {path}");
            var buffer = new byte[Math.Max(MinimumBufferLength, device.GetMaxInputReportLength())];

            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead;
                try
                {
                    bytesRead = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
                }
                catch (TimeoutException)
                {
                    // HidSharp uses a short read timeout by default. Battery reports may be
                    // minutes apart, so a timeout only means that no report has arrived yet.
                    continue;
                }

                if (bytesRead <= 0)
                {
                    throw new EndOfStreamException("The HID interface returned end-of-stream.");
                }

                var receivedAt = DateTimeOffset.Now;

                if (_options.LogRawReports)
                {
                    ConsoleLog.Raw(path, buffer.AsSpan(0, bytesRead));
                }

                if (BatteryReportParser.TryParse(
                        buffer.AsSpan(0, bytesRead),
                        receivedAt,
                        out var battery,
                        out var validationError))
                {
                    ConsoleLog.Battery(path, battery!);
                    if (!battery!.ChecksumValid)
                    {
                        ConsoleLog.Warn("Battery checksum did not match. The report was shown but not rejected pending real-device validation.");
                    }
                }
                else if (validationError is not null)
                {
                    ConsoleLog.Warn(validationError);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ConsoleLog.Warn($"HID interface closed or read failed: {path} ({exception.Message})");
        }
        catch (Exception exception)
        {
            ConsoleLog.Error($"Unexpected HID reader error: {path} ({exception})");
        }
        finally
        {
            stream?.Dispose();
            ConsoleLog.Info($"HID interface reader stopped: {path}");
        }
    }

    private void RemoveCompletedReaders()
    {
        foreach (var entry in _readers)
        {
            if (!entry.Value.IsCompleted || !_readers.TryRemove(entry.Key, out var completed))
            {
                continue;
            }

            if (completed.IsFaulted)
            {
                ConsoleLog.Error($"Reader task faulted: {entry.Key} ({completed.Exception?.GetBaseException().Message})");
            }
        }
    }

    private async Task WaitForReadersToStopAsync()
    {
        var readers = _readers.Values.ToArray();
        if (readers.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(readers);
        }
        catch (Exception exception)
        {
            ConsoleLog.Error($"A reader failed while shutting down: {exception.GetBaseException().Message}");
        }
    }
}
