using System.Globalization;
using System.Text;

namespace DMT152Monitor.Core.Storage;

public sealed class CsvMeasurementWriter : IDisposable
{
    private readonly StreamWriter _writer;
    private bool _disposed;

    public CsvMeasurementWriter(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        OutputPath = outputPath;

        var stream = new FileStream(
            outputPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 16 * 1024,
            options: FileOptions.SequentialScan);

        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 16 * 1024);
        _writer.WriteLine("time,Dew point,H2O ppb");
        _writer.Flush();
    }

    public string OutputPath { get; }

    public void WriteSample(double elapsedSeconds, double dewPointC, double h2oPpb)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (!double.IsFinite(dewPointC))
            throw new ArgumentOutOfRangeException(nameof(dewPointC));
        if (!double.IsFinite(h2oPpb))
            throw new ArgumentOutOfRangeException(nameof(h2oPpb));

        _writer.Write(elapsedSeconds.ToString("0.######", CultureInfo.InvariantCulture));
        _writer.Write(',');
        _writer.Write(dewPointC.ToString("0.##", CultureInfo.InvariantCulture));
        _writer.Write(',');
        _writer.Write(h2oPpb.ToString("0.###", CultureInfo.InvariantCulture));
        _writer.WriteLine();

        // DMT152 output is approximately 1 sample/s at the fastest setting, so flushing
        // every row is inexpensive and preserves nearly all data if a cable is unplugged.
        _writer.Flush();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _writer.Flush();
        _writer.Dispose();
        _disposed = true;
    }
}
