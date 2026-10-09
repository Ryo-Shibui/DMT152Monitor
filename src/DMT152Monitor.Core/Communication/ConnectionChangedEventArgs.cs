namespace DMT152Monitor.Core.Communication;

public sealed class ConnectionChangedEventArgs : EventArgs
{
    public ConnectionChangedEventArgs(bool isConnected, Dmt152ConnectionInfo? connectionInfo)
    {
        IsConnected = isConnected;
        ConnectionInfo = connectionInfo;
    }

    public bool IsConnected { get; }
    public Dmt152ConnectionInfo? ConnectionInfo { get; }
}
