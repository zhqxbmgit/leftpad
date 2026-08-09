namespace PcDs4Server;

public sealed class CursorJoystickSampler : IDisposable
{
    public const int IntervalMilliseconds = 10;

    private readonly VirtualJoystickController _controller;
    private readonly System.Threading.Timer _timer;
    private int _callbackRunning;
    private int _disposed;

    public CursorJoystickSampler(VirtualJoystickController controller)
    {
        _controller = controller;
        _timer = new System.Threading.Timer(
            static state => ((CursorJoystickSampler)state!).Tick(),
            this,
            IntervalMilliseconds,
            IntervalMilliseconds);
    }

    public event Action<Exception>? SamplingFailed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _timer.Dispose();
    }

    private void Tick()
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            Interlocked.Exchange(ref _callbackRunning, 1) != 0)
        {
            return;
        }

        try
        {
            _controller.SampleCursor();
        }
        catch (Exception ex)
        {
            try
            {
                _controller.Reset(JoystickResetReason.CursorSamplingFailure);
            }
            finally
            {
                SamplingFailed?.Invoke(ex);
            }
        }
        finally
        {
            Volatile.Write(ref _callbackRunning, 0);
        }
    }
}
