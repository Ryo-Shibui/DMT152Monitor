namespace DMT152Monitor.Core.Measurement;

public sealed class MeasurementCompletedEventArgs : EventArgs
{
    public MeasurementCompletedEventArgs(
        MeasurementStopReason reason,
        string outputPath,
        long sampleCount,
        double elapsedSeconds,
        Exception? exception = null)
    {
        Reason = reason;
        OutputPath = outputPath;
        SampleCount = sampleCount;
        ElapsedSeconds = elapsedSeconds;
        Exception = exception;
    }

    public MeasurementStopReason Reason { get; }
    public string OutputPath { get; }
    public long SampleCount { get; }
    public double ElapsedSeconds { get; }
    public Exception? Exception { get; }
}
