namespace DMT152Monitor.Core.Communication;

public sealed class Dmt152Reading : EventArgs
{
    public Dmt152Reading(
        double dewPointC,
        double h2oPpb,
        string statusCode,
        string errorBits,
        DateTimeOffset receivedAtLocal,
        long monotonicTimestamp)
    {
        DewPointC = dewPointC;
        H2OPpb = h2oPpb;
        StatusCode = statusCode;
        ErrorBits = errorBits;
        ReceivedAtLocal = receivedAtLocal;
        MonotonicTimestamp = monotonicTimestamp;
    }

    public double DewPointC { get; }
    public double H2OPpb { get; }
    public string StatusCode { get; }
    public string ErrorBits { get; }
    public DateTimeOffset ReceivedAtLocal { get; }
    public long MonotonicTimestamp { get; }
    public string StatusDescription => Dmt152Protocol.DescribeStatus(StatusCode, ErrorBits);
}
