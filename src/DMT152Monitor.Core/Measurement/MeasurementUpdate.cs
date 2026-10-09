namespace DMT152Monitor.Core.Measurement;

public sealed class MeasurementUpdate : EventArgs
{
    public MeasurementUpdate(long sampleCount, double elapsedSeconds, double dewPointC, double h2oPpb)
    {
        SampleCount = sampleCount;
        ElapsedSeconds = elapsedSeconds;
        DewPointC = dewPointC;
        H2OPpb = h2oPpb;
    }

    public long SampleCount { get; }
    public double ElapsedSeconds { get; }
    public double DewPointC { get; }
    public double H2OPpb { get; }
}
