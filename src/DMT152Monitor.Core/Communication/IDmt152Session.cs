namespace DMT152Monitor.Core.Communication;

public interface IDmt152Session : IDisposable
{
    Dmt152ConnectionInfo ConnectionInfo { get; }
    void OpenAndStart(Dmt152ConnectionOptions options);
    Dmt152Reading ReadNext(CancellationToken cancellationToken);
}
