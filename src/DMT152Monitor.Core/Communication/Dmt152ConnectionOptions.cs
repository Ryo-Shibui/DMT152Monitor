namespace DMT152Monitor.Core.Communication;

public sealed record Dmt152ConnectionOptions
{
    public required string PortName { get; init; }
    public double ProcessPressureBarA { get; init; } = 1.01325;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PortName))
            throw new ArgumentException("Select a COM port.");

        if (!double.IsFinite(ProcessPressureBarA) || ProcessPressureBarA < 0.1 || ProcessPressureBarA > 50.0)
            throw new ArgumentException("Process pressure must be an absolute pressure from 0.1 to 50 bara.");
    }
}
