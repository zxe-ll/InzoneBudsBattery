using Dalamud.Plugin.Services;

namespace InzoneBudsBattery;

internal sealed class InzoneBatteryService : IDisposable
{
    private const int VendorId = 0x054C;
    private const int ProductId = 0x0EC2;
    private const int ExpectedReportLength = 64;
    private static readonly TimeSpan QueryResponseTimeout = TimeSpan.FromSeconds(3);

    private readonly IPluginLog _log;
    private readonly Configuration _configuration;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _streamLock = new();
    private readonly Task _worker;
    private BatteryState _state = new();
    private FileStream? _activeStream;
    private ushort _nextTransactionId = 1;
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
                _log.Debug(
                    "INZONE HID interface: input={InputLength}, output={OutputLength}, usage={UsagePage:X4}:{Usage:X4}, path={Path}",
                    device.InputReportLength,
                    device.OutputReportLength,
                    device.UsagePage,
                    device.Usage,
                    device.DevicePath);
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
            var vendorQueryAvailable = device.OutputReportLength >= ExpectedReportLength;
            if (vendorQueryAvailable)
            {
                try
                {
                    stream = device.OpenReadWrite();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                {
                    vendorQueryAvailable = false;
                    _log.Warning(
                        exception,
                        "Could not open the INZONE HID interface for vendor queries; using passive input only: {Path}",
                        path);
                }
            }

            stream ??= device.OpenRead();
            lock (_streamLock)
            {
                _activeStream = stream;
            }

            SetConnectionState(true, null, path);
            if (!vendorQueryAvailable)
            {
                MarkPeriodicRefreshUnsupported();
            }

            _log.Info(
                "Opened INZONE HID interface ({Mode}): {Path}",
                vendorQueryAvailable ? "active battery query" : "passive input",
                path);

            var buffer = new byte[Math.Max(ExpectedReportLength, device.InputReportLength)];
            Task<int>? pendingRead = null;
            var nextRefreshAt = DateTimeOffset.Now;
            DateTimeOffset? queryResponseDeadline = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                pendingRead ??= stream.ReadAsync(buffer.AsMemory(), cancellationToken).AsTask();
                var wakeAt = queryResponseDeadline is { } deadline && deadline < nextRefreshAt
                    ? deadline
                    : nextRefreshAt;
                var refreshDelay = Task.Delay(GetRefreshDelay(wakeAt), cancellationToken);
                var completed = await Task.WhenAny(pendingRead, refreshDelay).ConfigureAwait(false);

                if (completed == refreshDelay)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var now = DateTimeOffset.Now;
                    if (queryResponseDeadline is { } responseDeadline && now >= responseDeadline)
                    {
                        MarkPeriodicRefreshUnsupported();
                        queryResponseDeadline = null;
                    }

                    if (now >= nextRefreshAt)
                    {
                        var querySent = vendorQueryAvailable
                                        && await TryPeriodicRefreshAsync(stream, device, cancellationToken).ConfigureAwait(false);
                        queryResponseDeadline = querySent ? DateTimeOffset.Now + QueryResponseTimeout : null;
                        nextRefreshAt = DateTimeOffset.Now + GetRefreshInterval();
                    }

                    continue;
                }

                var bytesRead = await pendingRead.ConfigureAwait(false);
                pendingRead = null;
                if (bytesRead <= 0)
                {
                    throw new EndOfStreamException("The HID interface returned end-of-stream.");
                }

                var receivedBatteryReport = ProcessReport(buffer.AsSpan(0, bytesRead), path, "interrupt");
                if (receivedBatteryReport && queryResponseDeadline is not null)
                {
                    MarkPeriodicRefreshSupported();
                    queryResponseDeadline = null;
                }
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

    private async Task<bool> TryPeriodicRefreshAsync(
        FileStream stream,
        WindowsHidDevice device,
        CancellationToken cancellationToken)
    {
        var attemptedAt = DateTimeOffset.Now;
        var transactionId = AllocateTransactionId();
        var report = BudsHciProtocol.BuildBatteryGetReport(device.OutputReportLength, transactionId);
        if (report is null)
        {
            Volatile.Write(
                ref _state,
                Current with
                {
                    LastRefreshAttemptAt = attemptedAt,
                    PeriodicRefreshSupported = false,
                });

            return false;
        }

        try
        {
            await stream.WriteAsync(report.AsMemory(), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(
                ref _state,
                Current with
                {
                    LastRefreshAttemptAt = attemptedAt,
                    PeriodicRefreshSupported = null,
                });

            if (_configuration.DebugLogging)
            {
                _log.Debug("Sent INZONE battery GET request with transaction ID {TransactionId}.", transactionId);
            }

            return true;
        }
        catch (ObjectDisposedException exception)
        {
            // RequestReconnect disposes the active stream to interrupt an outstanding read.
            // If that races with this write, leave the current device loop through the same
            // normal cancellation path instead of reporting a failed battery query.
            throw new OperationCanceledException(
                "The INZONE HID stream was disposed for reconnect.",
                exception,
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Volatile.Write(
                ref _state,
                Current with
                {
                    LastRefreshAttemptAt = attemptedAt,
                    PeriodicRefreshSupported = false,
                });
            _log.Warning(exception, "Could not send the INZONE battery GET request.");
            return false;
        }
    }

    private ushort AllocateTransactionId()
    {
        var transactionId = _nextTransactionId++;
        if (_nextTransactionId == 0)
        {
            _nextTransactionId = 1;
        }

        return transactionId;
    }

    private void MarkPeriodicRefreshSupported()
    {
        Volatile.Write(ref _state, Current with { PeriodicRefreshSupported = true });
    }

    private void MarkPeriodicRefreshUnsupported()
    {
        Volatile.Write(ref _state, Current with { PeriodicRefreshSupported = false });
        if (_configuration.DebugLogging)
        {
            _log.Debug("INZONE battery GET request did not receive a battery response within {TimeoutSeconds} seconds.", QueryResponseTimeout.TotalSeconds);
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
