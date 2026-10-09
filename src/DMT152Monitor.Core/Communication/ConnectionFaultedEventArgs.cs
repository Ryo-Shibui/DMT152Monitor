namespace DMT152Monitor.Core.Communication;

public sealed class ConnectionFaultedEventArgs : EventArgs
{
    public ConnectionFaultedEventArgs(Exception exception)
    {
        Exception = exception;
    }

    public Exception Exception { get; }
}
