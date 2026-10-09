namespace DMT152Monitor.Core.Communication;

public sealed record Dmt152ConnectionInfo(
    string PortName,
    int BaudRate,
    int DataBits,
    string Parity,
    int StopBits,
    double ProcessPressureBarA,
    string DeviceInfo)
{
    public string SerialSummary => $"{BaudRate} baud / {DataBits}{Parity}{StopBits}";
}
