namespace PcDs4Server;

public interface IServerLifecycle
{
    bool IsRunning { get; }
    OutputMode OutputMode { get; }
    bool TrySetOutputMode(OutputMode mode);
    bool Initialize();
    void Start();
    void Stop();
}

public enum ServerStartStatus
{
    Started,
    AlreadyRunning,
    InitializationFailed,
    StartFailed,
    AutoStartAlreadyAttempted,
    DirectDs4SelectionFailed
}

public sealed record ServerStartResult(ServerStartStatus Status, Exception? Exception = null)
{
    public bool Succeeded => Status is ServerStartStatus.Started or ServerStartStatus.AlreadyRunning;
}

public sealed class ServerLifecycleController
{
    private readonly IServerLifecycle _service;
    private bool _autoStartAttempted;

    public ServerLifecycleController(IServerLifecycle service) => _service = service;

    public bool AutoStartAttempted => _autoStartAttempted;

    public ServerStartResult TryAutoStartDirectDs4()
    {
        if (_autoStartAttempted)
        {
            return new ServerStartResult(ServerStartStatus.AutoStartAlreadyAttempted);
        }

        _autoStartAttempted = true;
        if (_service.IsRunning)
        {
            return new ServerStartResult(ServerStartStatus.AlreadyRunning);
        }

        if (!_service.TrySetOutputMode(OutputMode.DirectDs4))
        {
            SafeStop();
            return new ServerStartResult(ServerStartStatus.DirectDs4SelectionFailed);
        }

        return TryStart();
    }

    public ServerStartResult TryStart()
    {
        if (_service.IsRunning)
        {
            return new ServerStartResult(ServerStartStatus.AlreadyRunning);
        }

        try
        {
            if (!_service.Initialize())
            {
                SafeStop();
                return new ServerStartResult(ServerStartStatus.InitializationFailed);
            }

            _service.Start();
            return new ServerStartResult(ServerStartStatus.Started);
        }
        catch (Exception ex)
        {
            SafeStop();
            return new ServerStartResult(ServerStartStatus.StartFailed, ex);
        }
    }

    private void SafeStop()
    {
        try { _service.Stop(); }
        catch { /* Preserve the original startup failure while cleanup remains best-effort. */ }
    }
}
