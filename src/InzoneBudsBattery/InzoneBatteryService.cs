using Dalamud.Plugin.Services;

namespace InzoneBudsBattery;

internal sealed class InzoneBatteryService : IDisposable
{
    private const int VendorId = 0x054C;
    private const int ProductId = 0x0EC2;
    private const int ExpectedReportLength = 64;
    private const byte ReportId = 0x02;

    private readonly IPluginLog _log;
    private readonly Configuration _configuration;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _streamLock = new();
    private readonly Task _worker;
    private BatteryState _state = new();
    private FileStream? _activeStream;
    private bool _disposed;

    public InzoneBatteryService(IPluginLog log, Configuration configuration)
    {
        _log = log;
        _configuration = configuration;
        _worker = Task.Run(() => RunAsync(_shutdown.Token));
    }

    public BatteryState Current => Volatile.Read(ref _state);

    public void RequestReconnect()
    {
        lock (_streamLock)
        {
            _activeStream?.Dispose();
            _activeStream = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        RequestReconnect();

        try
        {
            if (!_worker.Wait(TimeSpan.FromSeconds(5)))
            {
                _log.Warning("INZONE HID reader did not stop within five seconds.");
            }
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(static inner => inner is OperationCanceledException))
        {
            // Normal cancellation.
        }
        finally
        {
            _shutdown.Dispose();
            _log.Info("INZONE battery service disposed.");
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        _log.Info("INZONE battery service started using native Windows HID APIs.");

        while (!cancellationToken.IsCancellationRequested)
        {
            WindowsHidDevice? device;
            try
            {
                device = FindBatteryInterface();
            }
            catch (Exception exception)
            {
                SetConnectionState(false, $"HID enumeration failed: {exception.Message}");
                _log.Warning(exception, "Failed to enumerate INZONE HID interfaces.");
                await DelayBeforeReconnectAsync(cancellationToken);
                continue;
            }

            if (device is null)
            {
                SetConnectionState(false, null);
                await DelayBeforeReconnectAsync(cancellationToken);
                continue;
            }

            await ReadDeviceUntilDisconnectedAsync(device, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                await DelayBeforeReconnectAsync(cancellationToken);
            }
        }
    }

    private WindowsHidDevice? FindBatteryInterface()
    {
        var devices = WindowsHid.Enumerate(VendorId, ProductId);
        if (_configuration.DebugLogging)
        {
            _log.Debug("Found {Count} INZONE HID interfaces.", devices.Count);
            foreach (var device in devices)
            {
                _log.Debug("INZONE HID interface: length={Length}, path={Path}", device.InputReportLength, device.DevicePath);
            }
        }

        return devices.FirstOrDefault(static device => device.InputReportLength == ExpectedReportLength)
               ?? devices
                   .Where(static device => device.InputReportLength >= 20)
                   .OrderByDescending(static device => device.InputReportLength)
                   .FirstOrDefault();
    }

    private async Task ReadDeviceUntilDisconnectedAsync(
        WindowsHidDevice device,
        CancellationToken cancellationToken)
    {
        FileStream? stream = null;
        var path = device.DevicePath;

        try
        {
            stream = device.OpenRead();
            lock (_streamLock)
            {
                _activeStream = stream;
            }

            SetConnectionState(true, null, path);
            _log.Info("Opened INZONE HID interface: {Path}", path);

            var buffer = new byte[Math.Max(ExpectedReportLength, device.InputReportLength)];
            Task<int>? pendingRead = null;
            var nextRefreshAt = DateTimeOffset.Now + GetRefreshInterval();

            while (!cancellationToken.IsCancellationRequested)
            {
                pendingRead ??= stream.ReadAsync(buffer.AsMemory(), cancellationToken).AsTask();
                var refreshDelay = Task.Delay(GetRefreshDelay(nextRefreshAt), cancellationToken);
                var completed = await Task.WhenAny(pendingRead, refreshDelay).ConfigureAwait(false);

                if (completed == refreshDelay)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TryPeriodicRefresh(device);
                    nextRefreshAt = DateTimeOffset.Now + GetRefreshInterval();
                    continue;
                }

                var bytesRead = await pendingRead.ConfigureAwait(false);
                pendingRead = null;
                if (bytesRead <= 0)
                {
                    throw new EndOfStreamException("The HID interface returned end-of-stream.");
                }

                ProcessReport(buffer.AsSpan(0, bytesRead), path, "interrupt");
            }
        }
        catch (OperationCanceledException)
        {
            // Disposing an overlapped HID stream can surface as OperationCanceledException
            // even when the service token itself was not cancelled (for example, reconnect).
            if (!cancellationToken.IsCancellationRequested)
            {
                SetConnectionState(false, null);
                _log.Info("INZONE HID read cancelled for reconnect: {Path}", path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException)
        {
            SetConnectionState(false, exception.Message);
            _log.Warning(exception, "INZONE HID interface disconnected: {Path}", path);
        }
        catch (Exception exception)
        {
            SetConnectionState(false, exception.Message);
            _log.Error(exception, "Unexpected INZONE HID reader error: {Path}", path);
        }
        finally
        {
            lock (_streamLock)
            {
                if (ReferenceEquals(_activeStream, stream))
                {
                    _activeStream = null;
                }
            }

            stream?.Dispose();
        }
    }

    private void TryPeriodicRefresh(WindowsHidDevice device)
    {
        var attemptedAt = DateTimeOffset.Now;
        if (!WindowsHid.TryGetInputReport(
                device.DevicePath,
                Math.Max(ExpectedReportLength, device.InputReportLength),
                ReportId,
                out var report,
                out var errorCode))
        {
            Volatile.Write(
                ref _state,
                Current with
                {
                    LastRefreshAttemptAt = attemptedAt,
                    PeriodicRefreshSupported = false,
                });

            if (_configuration.DebugLogging)
            {
                _log.Debug("Periodic HidD_GetInputReport was not supported (Win32 error {ErrorCode}).", errorCode);
            }

            return;
        }

        var receivedBatteryReport = ProcessReport(report, device.DevicePath, "periodic control request");
        Volatile.Write(
            ref _state,
            Current with
            {
                LastRefreshAttemptAt = attemptedAt,
                PeriodicRefreshSupported = receivedBatteryReport,
            });

        if (!receivedBatteryReport && _configuration.DebugLogging)
        {
            _log.Debug("Periodic HidD_GetInputReport returned a non-battery report; continuing the interrupt listener.");
        }
    }

    private bool ProcessReport(ReadOnlySpan<byte> report, string path, string source)
    {
        if (_configuration.DebugLogging)
        {
            _log.Debug("INZONE HID report via {Source} ({Length} bytes): {Report}", source, report.Length, Convert.ToHexString(report));
        }

        if (BatteryReportParser.TryParse(
                report,
                DateTimeOffset.Now,
                out var parsed,
                out var validationError))
        {
            var previous = Current;
            var next = parsed! with
            {
                DevicePath = path,
                LastRefreshAttemptAt = previous.LastRefreshAttemptAt,
                PeriodicRefreshSupported = previous.PeriodicRefreshSupported,
            };
            Volatile.Write(ref _state, next);
            _log.Info(
                "INZONE battery report via {Source}: L={Left}% R={Right}% Case={Case}% Checksum={Checksum}",
                source,
                next.LeftPercent ?? -1,
                next.RightPercent ?? -1,
                next.CasePercent ?? -1,
                next.ChecksumValid?.ToString() ?? "unknown");

            if (next.ChecksumValid == false)
            {
                _log.Warning("INZONE battery report checksum mismatch; values retained for diagnostics.");
            }

            return true;
        }

        if (validationError is not null)
        {
            _log.Warning("{ValidationError}", validationError);
        }

        return false;
    }

    private void SetConnectionState(bool connected, string? error, string? path = null)
    {
        var previous = Current;
        if (previous.IsTransmitterConnected == connected
            && previous.ErrorMessage == error
            && (path is null || previous.DevicePath == path))
        {
            return;
        }

        Volatile.Write(
            ref _state,
            previous with
            {
                IsTransmitterConnected = connected,
                ErrorMessage = error,
                DevicePath = path ?? previous.DevicePath,
            });
    }

    private TimeSpan GetRefreshInterval() => TimeSpan.FromMinutes(Math.Clamp(_configuration.RefreshIntervalMinutes, 1, 60));

    private static TimeSpan GetRefreshDelay(DateTimeOffset nextRefreshAt)
    {
        var delay = nextRefreshAt - DateTimeOffset.Now;
        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }

    private static async Task DelayBeforeReconnectAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
    }
}
