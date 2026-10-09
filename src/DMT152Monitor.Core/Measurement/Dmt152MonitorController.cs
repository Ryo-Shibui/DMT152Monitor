using System.Diagnostics;
using DMT152Monitor.Core.Communication;
using DMT152Monitor.Core.Storage;

namespace DMT152Monitor.Core.Measurement;

public sealed class Dmt152MonitorController : IDisposable
{
    private readonly object _sync = new();
    private readonly Func<IDmt152Session> _sessionFactory;

    private IDmt152Session? _session;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private bool _connecting;
    private bool _connected;
    private bool _disposed;

    private CsvMeasurementWriter? _writer;
    private CancellationTokenSource? _timerCts;
    private long _measurementStartTimestamp;
    private long _measurementSampleCount;
    private string? _measurementOutputPath;
    private bool _measurementRunning;

    public Dmt152MonitorController(Func<IDmt152Session>? sessionFactory = null)
    {
        _sessionFactory = sessionFactory ?? (() => new Dmt152SerialSession());
    }

    public event EventHandler<Dmt152Reading>? ReadingUpdated;
    public event EventHandler<MeasurementUpdate>? MeasurementUpdated;
    public event EventHandler<MeasurementCompletedEventArgs>? MeasurementCompleted;
    public event EventHandler<ConnectionChangedEventArgs>? ConnectionChanged;
    public event EventHandler<ConnectionFaultedEventArgs>? ConnectionFaulted;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
                return _connected;
        }
    }

    public bool IsConnecting
    {
        get
        {
            lock (_sync)
                return _connecting;
        }
    }

    public bool IsMeasurementRunning
    {
        get
        {
            lock (_sync)
                return _measurementRunning;
        }
    }

    public Dmt152ConnectionInfo? CurrentConnectionInfo
    {
        get
        {
            lock (_sync)
                return _session?.ConnectionInfo;
        }
    }

    public async Task<Dmt152ConnectionInfo> ConnectAsync(Dmt152ConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();
        options.Validate();

        lock (_sync)
        {
            if (_connected || _connecting)
                throw new InvalidOperationException("A DMT152 connection is already active or being established.");
            _connecting = true;
        }

        IDmt152Session? session = null;
        CancellationTokenSource? cts = null;

        try
        {
            session = _sessionFactory();
            await Task.Run(() => session.OpenAndStart(options)).ConfigureAwait(false);
            cts = new CancellationTokenSource();

            lock (_sync)
            {
                _session = session;
                _readCts = cts;
                _connected = true;
                _connecting = false;
                _readTask = Task.Run(() => ReadLoop(session, cts.Token));
            }

            Dmt152ConnectionInfo info = session.ConnectionInfo;
            ConnectionChanged?.Invoke(this, new ConnectionChangedEventArgs(true, info));
            return info;
        }
        catch
        {
            session?.Dispose();
            cts?.Dispose();
            lock (_sync)
                _connecting = false;
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        Task? readTask;
        CancellationTokenSource? cts;
        IDmt152Session? session;
        MeasurementCompletedEventArgs? measurementCompletion = null;

        lock (_sync)
        {
            if (!_connected && !_connecting)
                return;

            cts = _readCts;
            readTask = _readTask;
            session = _session;
            cts?.Cancel();
        }

        if (readTask is not null)
        {
            try
            {
                await readTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        lock (_sync)
        {
            if (_measurementRunning)
                measurementCompletion = FinalizeMeasurementLocked(MeasurementStopReason.Manual, null);

            _session = null;
            _readCts = null;
            _readTask = null;
            _connected = false;
            _connecting = false;
        }

        session?.Dispose();
        cts?.Dispose();

        if (measurementCompletion is not null)
            MeasurementCompleted?.Invoke(this, measurementCompletion);

        ConnectionChanged?.Invoke(this, new ConnectionChangedEventArgs(false, null));
    }

    public void StartMeasurement(MeasurementSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfDisposed();
        settings.Validate();

        string outputPath = settings.GetOutputPath();
        if (File.Exists(outputPath))
            throw new IOException($"The output file already exists:\n{outputPath}");

        CsvMeasurementWriter? writer = null;
        CancellationTokenSource? timerCts = null;

        try
        {
            writer = new CsvMeasurementWriter(outputPath);
            if (settings.StopMode == MeasurementStopMode.Timer)
                timerCts = new CancellationTokenSource();

            lock (_sync)
            {
                if (!_connected || _session is null)
                    throw new InvalidOperationException("Connect to the DMT152 before starting a measurement.");
                if (_measurementRunning)
                    throw new InvalidOperationException("A measurement is already running.");

                _writer = writer;
                _timerCts = timerCts;
                _measurementStartTimestamp = Stopwatch.GetTimestamp();
                _measurementSampleCount = 0;
                _measurementOutputPath = outputPath;
                _measurementRunning = true;
            }

            writer = null;
            timerCts = null;

            if (settings.StopMode == MeasurementStopMode.Timer)
            {
                CancellationToken token;
                lock (_sync)
                    token = _timerCts!.Token;

                _ = RunMeasurementTimerAsync(settings.TimerDurationSeconds, token);
            }
        }
        catch
        {
            writer?.Dispose();
            timerCts?.Dispose();
            try
            {
                if (File.Exists(outputPath) && new FileInfo(outputPath).Length <= 64)
                    File.Delete(outputPath);
            }
            catch
            {
                // Do not mask the setup error.
            }

            throw;
        }
    }

    public Task StopMeasurementAsync()
    {
        MeasurementCompletedEventArgs? completion;
        lock (_sync)
            completion = _measurementRunning ? FinalizeMeasurementLocked(MeasurementStopReason.Manual, null) : null;

        if (completion is not null)
            MeasurementCompleted?.Invoke(this, completion);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Dispose must release local resources even if the device disappeared.
        }

        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
            _timerCts?.Dispose();
            _timerCts = null;
            _readCts?.Dispose();
            _readCts = null;
            _session?.Dispose();
            _session = null;
            _connected = false;
            _connecting = false;
            _measurementRunning = false;
        }
    }

    private void ReadLoop(IDmt152Session session, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Dmt152Reading reading = session.ReadNext(cancellationToken);
                ReadingUpdated?.Invoke(this, reading);

                MeasurementUpdate? measurementUpdate = null;
                MeasurementCompletedEventArgs? writeFailureCompletion = null;

                lock (_sync)
                {
                    if (_measurementRunning && _writer is not null && IsNormalOrUnknownStatus(reading))
                    {
                        try
                        {
                            double elapsed = Stopwatch.GetElapsedTime(_measurementStartTimestamp, reading.MonotonicTimestamp).TotalSeconds;
                            _writer.WriteSample(elapsed, reading.DewPointC, reading.H2OPpb);
                            _measurementSampleCount++;
                            measurementUpdate = new MeasurementUpdate(
                                _measurementSampleCount,
                                elapsed,
                                reading.DewPointC,
                                reading.H2OPpb);
                        }
                        catch (Exception ex)
                        {
                            writeFailureCompletion = FinalizeMeasurementLocked(MeasurementStopReason.Faulted, ex);
                        }
                    }
                }

                if (measurementUpdate is not null)
                    MeasurementUpdated?.Invoke(this, measurementUpdate);
                if (writeFailureCompletion is not null)
                    MeasurementCompleted?.Invoke(this, writeFailureCompletion);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            HandleConnectionFault(session, ex);
        }
    }

    private static bool IsNormalOrUnknownStatus(Dmt152Reading reading)
    {
        // An empty status means STAT was unavailable on this firmware, so keep the
        // previous behavior. Explicit self-diagnostic states are not written to CSV
        // because the DMT152 manual states that its measurement output is frozen.
        return string.IsNullOrWhiteSpace(reading.StatusCode) ||
               string.Equals(reading.StatusCode, "0000", StringComparison.Ordinal);
    }

    private void HandleConnectionFault(IDmt152Session faultedSession, Exception ex)
    {
        MeasurementCompletedEventArgs? measurementCompletion = null;
        CancellationTokenSource? cts;
        bool ownsSession;

        lock (_sync)
        {
            ownsSession = ReferenceEquals(_session, faultedSession);
            if (!ownsSession)
                return;

            if (_measurementRunning)
                measurementCompletion = FinalizeMeasurementLocked(MeasurementStopReason.ConnectionLost, ex);

            cts = _readCts;
            _session = null;
            _readCts = null;
            _readTask = null;
            _connected = false;
            _connecting = false;
        }

        try { faultedSession.Dispose(); } catch { }
        cts?.Dispose();

        if (measurementCompletion is not null)
            MeasurementCompleted?.Invoke(this, measurementCompletion);

        ConnectionChanged?.Invoke(this, new ConnectionChangedEventArgs(false, null));
        ConnectionFaulted?.Invoke(this, new ConnectionFaultedEventArgs(ex));
    }

    private async Task RunMeasurementTimerAsync(double durationSeconds, CancellationToken cancellationToken)
    {
        try
        {
            TimeSpan duration = TimeSpan.FromSeconds(durationSeconds);
            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        MeasurementCompletedEventArgs? completion;
        lock (_sync)
            completion = _measurementRunning ? FinalizeMeasurementLocked(MeasurementStopReason.TimerCompleted, null) : null;

        if (completion is not null)
            MeasurementCompleted?.Invoke(this, completion);
    }

    private MeasurementCompletedEventArgs FinalizeMeasurementLocked(MeasurementStopReason reason, Exception? exception)
    {
        if (!_measurementRunning)
            throw new InvalidOperationException("No measurement is running.");

        string outputPath = _measurementOutputPath ?? string.Empty;
        long sampleCount = _measurementSampleCount;
        double elapsed = Stopwatch.GetElapsedTime(_measurementStartTimestamp).TotalSeconds;

        Exception? finalException = exception;
        try
        {
            _writer?.Dispose();
        }
        catch (Exception ex)
        {
            finalException ??= ex;
            reason = MeasurementStopReason.Faulted;
        }

        _writer = null;
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = null;
        _measurementRunning = false;
        _measurementOutputPath = null;
        _measurementSampleCount = 0;
        _measurementStartTimestamp = 0;

        return new MeasurementCompletedEventArgs(reason, outputPath, sampleCount, elapsed, finalException);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
