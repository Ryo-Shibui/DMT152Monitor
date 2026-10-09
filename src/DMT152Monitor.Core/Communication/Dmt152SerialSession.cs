using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DMT152Monitor.Core.Communication;

public sealed partial class Dmt152SerialSession : IDmt152Session
{
    private static readonly SerialCandidate[] Candidates =
    {
        new(4800, 7, 'N', 1),
        new(19200, 8, 'N', 1)
    };

    private readonly Queue<string> _lineQueue = new();
    private readonly StringBuilder _partialLine = new();
    private Win32SerialPort? _port;
    private bool _disposed;
    private bool _openedPollLine;
    private long _lastSendTimestamp;
    private bool _hasReceivedMeasurement;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PerRequestResponseTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FirstMeasurementGracePeriod = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan EstablishedMeasurementGracePeriod = TimeSpan.FromSeconds(30);

    public Dmt152ConnectionInfo ConnectionInfo { get; private set; } =
        new(string.Empty, 0, 0, "N", 0, 0, string.Empty);

    public void OpenAndStart(Dmt152ConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);
        options.Validate();

        if (_port is not null)
            throw new InvalidOperationException("DMT152 session is already open.");

        Exception? lastError = null;
        var attemptDiagnostics = new List<string>();

        foreach (SerialCandidate candidate in Candidates)
        {
            Win32SerialPort? port = null;
            bool deviceDetected = false;
            try
            {
                port = new Win32SerialPort();
                port.Open(options.PortName, candidate.BaudRate, candidate.DataBits, candidate.Parity, candidate.StopBits);
                _port = port;
                ResetParserState();

                ProbeResult probe = ProbeDevice();
                if (!probe.IsDmt152)
                    throw new IOException($"No DMT152 response was detected at {candidate.BaudRate} baud / {candidate.DataBits}N{candidate.StopBits}.");

                deviceDetected = true;
                ConfigureForMonitoring(options.ProcessPressureBarA);

                ConnectionInfo = new Dmt152ConnectionInfo(
                    options.PortName.Trim().ToUpperInvariant(),
                    candidate.BaudRate,
                    candidate.DataBits,
                    candidate.Parity.ToString().ToUpperInvariant(),
                    candidate.StopBits,
                    options.ProcessPressureBarA,
                    probe.DeviceInfo);

                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                string stage = deviceDetected
                    ? "DMT152 detected; configuration failed"
                    : "connection/probe failed";
                attemptDiagnostics.Add(
                    $"{candidate.BaudRate}/{candidate.DataBits}{candidate.Parity}{candidate.StopBits}: {stage}: {GetDeepestMessage(ex)}");

                _openedPollLine = false;
                _lastSendTimestamp = 0;
                _hasReceivedMeasurement = false;
                _port = null;
                port?.Dispose();
                ResetParserState();
            }
        }

        string details = attemptDiagnostics.Count == 0
            ? "No diagnostic details were captured."
            : string.Join(Environment.NewLine, attemptDiagnostics.Select(x => "- " + x));

        throw new IOException(
            "Could not complete communication setup with the DMT152." + Environment.NewLine +
            "The program tried 4800/7N1 and 19200/8N1." + Environment.NewLine +
            "Attempt details:" + Environment.NewLine + details,
            lastError);
    }

    public Dmt152Reading ReadNext(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_port is null)
            throw new InvalidOperationException("DMT152 session is not open.");

        TimeSpan grace = _hasReceivedMeasurement
            ? EstablishedMeasurementGracePeriod
            : FirstMeasurementGracePeriod;
        DateTime overallDeadline = DateTime.UtcNow + grace;
        var recentRaw = new Queue<string>();

        while (DateTime.UtcNow < overallDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaitForNextPoll(cancellationToken);

            if (TryRequestSingleMeasurement(cancellationToken, out Dmt152Reading? reading, out string rawSummary) && reading is not null)
            {
                _hasReceivedMeasurement = true;
                return reading;
            }

            if (!string.Equals(rawSummary, "(none)", StringComparison.Ordinal))
            {
                recentRaw.Enqueue(rawSummary);
                while (recentRaw.Count > 4)
                    recentRaw.Dequeue();
            }
        }

        string raw = recentRaw.Count == 0 ? "(none)" : string.Join(" || ", recentRaw);
        string phase = _hasReceivedMeasurement ? "live monitoring" : "initial measurement startup";
        throw new TimeoutException(
            $"No valid DMT152 measurement was received during {phase} for {grace.TotalSeconds:0} seconds. " +
            "The DMT152 may need time after power-up before a measurement is available. " +
            $"Last raw serial lines received: {raw}");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (_port is not null && _openedPollLine)
            {
                try
                {
                    WriteCommand("CLOSE");
                    Thread.Sleep(40);
                }
                catch
                {
                    // The port may already be disconnected. Dispose must still release it.
                }
            }
        }
        finally
        {
            _port?.Dispose();
            _port = null;
            _disposed = true;
        }
    }

    private ProbeResult ProbeDevice()
    {
        Win32SerialPort port = GetPort();

        // Clear any previous RUN output and stale bytes before probing.
        WriteRaw("\rS\r");
        Thread.Sleep(120);
        port.Purge();
        ResetParserState();

        WriteCommand("?");
        string normalInfo = ReadTextWindow(TimeSpan.FromMilliseconds(900));
        if (ContainsDmt152(normalInfo))
            return new ProbeResult(true, NormalizeDeviceInfo(normalInfo));

        port.Purge();
        ResetParserState();
        WriteCommand("??");
        string overrideInfo = ReadTextWindow(TimeSpan.FromMilliseconds(1100));
        if (!ContainsDmt152(overrideInfo))
            return new ProbeResult(false, overrideInfo);

        // ?? responds even when the transmitter is in POLL mode. In that case normal
        // configuration commands require OPEN <address>. Try to discover the address.
        if (TryOpenPollLine(overrideInfo))
            _openedPollLine = true;

        return new ProbeResult(true, NormalizeDeviceInfo(overrideInfo));
    }

    private bool TryOpenPollLine(string deviceInfo)
    {
        Match match = AddressRegex().Match(deviceInfo);
        int? parsedAddress = null;

        if (match.Success && int.TryParse(match.Groups[1].Value, out int detectedAddress))
        {
            parsedAddress = detectedAddress;
            if (TryOpenAddress(detectedAddress))
                return true;
        }

        // The manual allows addresses 0..99. This fallback is only used when ?? identifies
        // a DMT152 but the normal ? command did not, which strongly suggests POLL mode.
        for (int candidateAddress = 0; candidateAddress <= 99; candidateAddress++)
        {
            if (parsedAddress.HasValue && candidateAddress == parsedAddress.Value)
                continue;

            if (TryOpenAddress(candidateAddress))
                return true;
        }

        return false;
    }

    private bool TryOpenAddress(int address)
    {
        Win32SerialPort port = GetPort();
        port.Purge();
        ResetParserState();
        WriteCommand($"OPEN {address}");
        string response = ReadTextWindow(TimeSpan.FromMilliseconds(140));
        return response.Contains("line opened", StringComparison.OrdinalIgnoreCase);
    }

    private void ConfigureForMonitoring(double processPressureBarA)
    {
        Win32SerialPort port = GetPort();

        // Stop any pre-existing continuous output, configure metric units and the absolute
        // pressure used by the transmitter's water-content calculation, then restore the
        // documented default response format. Monitoring itself uses SEND polling rather
        // than firmware-dependent custom FORM + R continuous streaming.
        SendConfigurationCommand("S", TimeSpan.FromMilliseconds(150), responseRequired: false);
        SendConfigurationCommand("UNIT M", TimeSpan.FromMilliseconds(350), responseRequired: true);
        SendConfigurationCommand(
            $"XPRES {processPressureBarA.ToString("0.#####", CultureInfo.InvariantCulture)}",
            TimeSpan.FromMilliseconds(350),
            responseRequired: true);
        SendConfigurationCommand(
            Dmt152Protocol.RestoreDefaultFormatCommand,
            TimeSpan.FromMilliseconds(400),
            responseRequired: false);

        port.Purge();
        ResetParserState();
        _lastSendTimestamp = 0;
        _hasReceivedMeasurement = false;
    }

    private bool TryRequestSingleMeasurement(
        CancellationToken cancellationToken,
        out Dmt152Reading? reading,
        out string rawSummary)
    {
        reading = null;

        // DMT152 intentionally freezes the measurement output while sensor purge or
        // auto-calibration is active. Query STAT for every poll so the GUI can
        // distinguish a fresh serial reply containing a frozen instrument value from
        // accidental software-side reuse of an old sample.
        string statusCode = QueryStatus(cancellationToken);

        Win32SerialPort port = GetPort();
        port.Purge();
        ResetParserState();
        WriteCommand("SEND");
        _lastSendTimestamp = Stopwatch.GetTimestamp();

        DateTime deadline = DateTime.UtcNow + PerRequestResponseTimeout;
        var rawLines = new List<string>();

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            string? line = TryReadLine(
                remaining < TimeSpan.FromMilliseconds(400) ? remaining : TimeSpan.FromMilliseconds(400),
                cancellationToken);
            if (line is null)
                continue;

            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed == ">")
                continue;

            rawLines.Add(trimmed);
            if (Dmt152Protocol.TryParseMeasurementLine(trimmed, out Dmt152Reading? parsed) && parsed is not null)
            {
                reading = new Dmt152Reading(
                    parsed.DewPointC,
                    parsed.H2OPpb,
                    statusCode,
                    parsed.ErrorBits,
                    parsed.ReceivedAtLocal,
                    parsed.MonotonicTimestamp);

                rawSummary = string.Join(" | ", rawLines.Take(8));
                return true;
            }
        }

        if (_partialLine.Length > 0)
            rawLines.Add("[partial] " + _partialLine);

        rawSummary = rawLines.Count == 0
            ? "(none)"
            : string.Join(" | ", rawLines.Take(8));
        return false;
    }

    private string QueryStatus(CancellationToken cancellationToken)
    {
        Win32SerialPort port = GetPort();
        port.Purge();
        ResetParserState();
        WriteCommand("STAT");

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(700);
        var rawLines = new List<string>();

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            string? line = TryReadLine(
                remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250),
                cancellationToken);
            if (line is null)
                continue;

            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed == ">")
                continue;

            rawLines.Add(trimmed);
            if (Dmt152Protocol.TryParseStatusResponse(trimmed, out string statusCode))
                return statusCode;
        }

        // Status is diagnostic metadata. A firmware variant that does not answer STAT
        // should not prevent acquisition of the actual SEND measurement.
        string combined = string.Join(" ", rawLines);
        return Dmt152Protocol.TryParseStatusResponse(combined, out string fallbackStatus)
            ? fallbackStatus
            : string.Empty;
    }

    private void WaitForNextPoll(CancellationToken cancellationToken)
    {
        if (_lastSendTimestamp == 0)
            return;

        TimeSpan elapsed = Stopwatch.GetElapsedTime(_lastSendTimestamp);
        TimeSpan remaining = PollInterval - elapsed;
        if (remaining <= TimeSpan.Zero)
            return;

        if (cancellationToken.WaitHandle.WaitOne(remaining))
            cancellationToken.ThrowIfCancellationRequested();
    }

    private void SendConfigurationCommand(string command, TimeSpan wait, bool responseRequired)
    {
        Win32SerialPort port = GetPort();
        port.Purge();
        ResetParserState();
        WriteCommand(command);
        string response = ReadTextWindow(wait);

        if (responseRequired && string.IsNullOrWhiteSpace(response))
            throw new IOException($"The DMT152 did not acknowledge the command: {command}");

        if (response.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("unknown", StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"The DMT152 rejected the command '{command}'. Response: {response.Trim()}");
        }
    }

    private string ReadTextWindow(TimeSpan duration)
    {
        DateTime deadline = DateTime.UtcNow + duration;
        var lines = new List<string>();

        while (DateTime.UtcNow < deadline)
        {
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            string? line;
            try
            {
                line = TryReadLine(remaining < TimeSpan.FromMilliseconds(220) ? remaining : TimeSpan.FromMilliseconds(220), CancellationToken.None);
            }
            catch (TimeoutException)
            {
                break;
            }

            if (line is null)
                continue;

            string trimmed = line.Trim();
            if (trimmed.Length > 0 && trimmed != ">")
                lines.Add(trimmed);
        }

        return string.Join("\n", lines);
    }

    private string? TryReadLine(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_lineQueue.Count > 0)
            return _lineQueue.Dequeue();

        Win32SerialPort port = GetPort();
        DateTime deadline = DateTime.UtcNow + timeout;
        var buffer = new byte[256];

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = port.Read(buffer, 0, buffer.Length);
            if (read > 0)
            {
                ProcessIncomingBytes(buffer, read);
                if (_lineQueue.Count > 0)
                    return _lineQueue.Dequeue();
            }
        }

        return null;
    }

    private void ProcessIncomingBytes(byte[] buffer, int count)
    {
        for (int i = 0; i < count; i++)
        {
            char c = (char)buffer[i];
            if (c is '\r' or '\n')
            {
                if (_partialLine.Length > 0)
                {
                    _lineQueue.Enqueue(_partialLine.ToString());
                    _partialLine.Clear();
                }
                continue;
            }

            if (c >= 0x20 || c == '\t')
                _partialLine.Append(c);
        }
    }

    private void WriteCommand(string command) => WriteRaw(command + "\r");

    private void WriteRaw(string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        GetPort().Write(bytes);
    }

    private Win32SerialPort GetPort() =>
        _port ?? throw new InvalidOperationException("Serial port is not open.");

    private void ResetParserState()
    {
        _lineQueue.Clear();
        _partialLine.Clear();
    }

    private static bool ContainsDmt152(string text) =>
        text.Contains("DMT152", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDeviceInfo(string text)
    {
        string normalized = string.Join(
            " | ",
            text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && line != ">"));

        return normalized.Length > 300 ? normalized[..300] + "..." : normalized;
    }


    private static string GetDeepestMessage(Exception exception)
    {
        Exception current = exception;
        while (current.InnerException is not null)
            current = current.InnerException;
        return current.Message;
    }

    [GeneratedRegex(@"Address\s*[:=]\s*(\d{1,2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AddressRegex();

    private readonly record struct SerialCandidate(int BaudRate, int DataBits, char Parity, int StopBits);
    private readonly record struct ProbeResult(bool IsDmt152, string DeviceInfo);
}
