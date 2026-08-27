using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PcDs4Server;

internal static class SingleInstanceIdentity
{
    public const string MutexName = "Global\\LeftPadDs4Receiver_Mutex";
    public const string ActivationPipeName = "LeftPadDs4Receiver.Activation.v1";
}

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private bool _ownsMutex;

    private SingleInstanceCoordinator(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    public bool IsPrimaryInstance => _ownsMutex;

    public static SingleInstanceCoordinator Acquire(string mutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        var mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
        return new SingleInstanceCoordinator(mutex, createdNew);
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _ownsMutex = false;
            _mutex.ReleaseMutex();
        }
        _mutex.Dispose();
    }
}

internal enum SingleInstanceActivationResult
{
    Activated,
    Rejected,
    EndpointUnavailable
}

internal sealed class SingleInstanceActivationClient
{
    internal const int MaximumPayloadBytes = 32;
    internal static readonly TimeSpan DefaultOverallTimeout = TimeSpan.FromMilliseconds(2500);
    internal static readonly TimeSpan DefaultAttemptTimeout = TimeSpan.FromMilliseconds(350);
    private const string ActivationCommand = "activate";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _pipeName;
    private readonly Action<NamedPipeClientStream> _grantForegroundPermission;

    public SingleInstanceActivationClient(
        string pipeName,
        Action<NamedPipeClientStream>? grantForegroundPermission = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
        _grantForegroundPermission = grantForegroundPermission ??
            WindowsForegroundActivation.GrantPipeServerForegroundPermission;
    }

    public Task<SingleInstanceActivationResult> TryActivateAsync(
        CancellationToken cancellationToken = default) =>
        SendCommandAsync(ActivationCommand, DefaultOverallTimeout, cancellationToken);

    internal async Task<SingleInstanceActivationResult> SendCommandAsync(
        string command,
        TimeSpan overallTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (overallTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(overallTimeout));

        byte[] payload = StrictUtf8.GetBytes(command);
        var elapsed = Stopwatch.StartNew();
        using var overallCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overallCancellation.CancelAfter(overallTimeout);

        while (!overallCancellation.IsCancellationRequested)
        {
            TimeSpan remaining = overallTimeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            TimeSpan attemptTimeout = remaining < DefaultAttemptTimeout
                ? remaining
                : DefaultAttemptTimeout;

            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                int connectTimeoutMs = Math.Max(1, (int)Math.Ceiling(attemptTimeout.TotalMilliseconds));
                await pipe.ConnectAsync(connectTimeoutMs, overallCancellation.Token).ConfigureAwait(false);
                _grantForegroundPermission(pipe);

                using var responseCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    overallCancellation.Token);
                responseCancellation.CancelAfter(attemptTimeout);
                await SingleInstanceActivationProtocol.WriteFrameAsync(
                    pipe,
                    payload,
                    responseCancellation.Token).ConfigureAwait(false);
                int response = await SingleInstanceActivationProtocol.ReadResponseAsync(
                    pipe,
                    responseCancellation.Token).ConfigureAwait(false);
                return response == SingleInstanceActivationProtocol.AcceptedResponse
                    ? SingleInstanceActivationResult.Activated
                    : SingleInstanceActivationResult.Rejected;
            }
            catch (Exception exception) when (
                exception is IOException or TimeoutException or OperationCanceledException)
            {
                if (overallCancellation.IsCancellationRequested) break;
                TimeSpan retryDelay = TimeSpan.FromMilliseconds(100);
                remaining = overallTimeout - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero) break;
                if (retryDelay > remaining) retryDelay = remaining;
                try
                {
                    await Task.Delay(retryDelay, overallCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        return SingleInstanceActivationResult.EndpointUnavailable;
    }
}

internal sealed class SingleInstanceActivationServer : IDisposable
{
    private readonly string _pipeName;
    private readonly Func<CancellationToken, Task<bool>> _activate;
    private readonly TimeSpan _requestTimeout;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _sync = new();
    private NamedPipeServerStream? _activePipe;
    private Task? _listenerTask;
    private bool _disposed;

    public SingleInstanceActivationServer(
        string pipeName,
        Func<CancellationToken, Task<bool>> activate,
        TimeSpan? requestTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
        _activate = activate ?? throw new ArgumentNullException(nameof(activate));
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(1);
        if (_requestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_listenerTask != null) return;
        _listenerTask = Task.Run(ListenAsync);
    }

    private async Task ListenAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                    inBufferSize: 64,
                    outBufferSize: 64);
                lock (_sync)
                {
                    if (_disposed)
                    {
                        pipe.Dispose();
                        return;
                    }
                    _activePipe = pipe;
                }

                await pipe.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
                await HandleRequestAsync(pipe, _shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // A single request exceeded its bounded processing timeout. Keep
                // the primary listener alive for later activation attempts.
            }
            catch (IOException)
            {
                if (!_shutdown.IsCancellationRequested)
                {
                    try { await Task.Delay(50, _shutdown.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
            }
            catch (Exception)
            {
                // An endpoint failure must not terminate the Receiver or disable
                // all future activation attempts. Retry at a bounded cadence.
                if (_shutdown.IsCancellationRequested) return;
                try { await Task.Delay(50, _shutdown.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_activePipe, pipe)) _activePipe = null;
                }
                pipe?.Dispose();
            }
        }
    }

    private async Task HandleRequestAsync(Stream pipe, CancellationToken shutdownToken)
    {
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        requestCancellation.CancelAfter(_requestTimeout);
        CancellationToken token = requestCancellation.Token;

        byte[]? payload = await SingleInstanceActivationProtocol.ReadFrameAsync(pipe, token)
            .ConfigureAwait(false);
        bool accepted = false;
        if (payload != null)
        {
            try
            {
                string command = SingleInstanceActivationProtocol.StrictUtf8.GetString(payload);
                accepted = string.Equals(command, "activate", StringComparison.Ordinal) &&
                    await _activate(token).ConfigureAwait(false);
            }
            catch (DecoderFallbackException)
            {
                accepted = false;
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                accepted = false;
            }
        }

        await SingleInstanceActivationProtocol.WriteResponseAsync(pipe, accepted, token)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        lock (_sync) _activePipe?.Dispose();
        try { _listenerTask?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        _shutdown.Dispose();
    }
}

internal static class SingleInstanceActivationProtocol
{
    public const byte RejectedResponse = 0;
    public const byte AcceptedResponse = 1;
    public static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task WriteFrameAsync(
        Stream stream,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        byte[] header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (!payload.IsEmpty)
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<byte[]?> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] header = new byte[sizeof(int)];
        if (!await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false))
            return null;
        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (payloadLength <= 0 || payloadLength > SingleInstanceActivationClient.MaximumPayloadBytes)
            return null;

        byte[] payload = new byte[payloadLength];
        return await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false)
            ? payload
            : null;
    }

    public static async Task WriteResponseAsync(
        Stream stream,
        bool accepted,
        CancellationToken cancellationToken)
    {
        byte[] response = [accepted ? AcceptedResponse : RejectedResponse];
        await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<int> ReadResponseAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] response = new byte[1];
        return await ReadExactlyAsync(stream, response, cancellationToken).ConfigureAwait(false)
            ? response[0]
            : RejectedResponse;
    }

    private static async Task<bool> ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }
}

internal static class WindowsForegroundActivation
{
    private const uint SendMessageTimeoutMilliseconds = 750;

    public static void GrantPipeServerForegroundPermission(NamedPipeClientStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        if (GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint processId))
            _ = AllowSetForegroundWindow(processId);
    }

    public static void TrySetForegroundWindow(IntPtr windowHandle)
    {
        if (windowHandle != IntPtr.Zero) _ = SetForegroundWindow(windowHandle);
    }

    public static bool SendActivationMessage(IntPtr windowHandle, int message)
    {
        if (windowHandle == IntPtr.Zero) return false;
        IntPtr sent = SendMessageTimeout(
            windowHandle,
            unchecked((uint)message),
            UIntPtr.Zero,
            IntPtr.Zero,
            SendMessageTimeoutFlags.Block | SendMessageTimeoutFlags.AbortIfHung,
            SendMessageTimeoutMilliseconds,
            out UIntPtr result);
        return sent != IntPtr.Zero && result != UIntPtr.Zero;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(
        SafePipeHandle pipe,
        out uint serverProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        SendMessageTimeoutFlags flags,
        uint timeoutMilliseconds,
        out UIntPtr result);

    [Flags]
    private enum SendMessageTimeoutFlags : uint
    {
        Block = 0x0001,
        AbortIfHung = 0x0002
    }
}
