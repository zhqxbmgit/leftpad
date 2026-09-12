using System.Net;
using System.Net.Sockets;

namespace PcDs4Server;

internal sealed class DiscoveryResponder : IDisposable
{
    private readonly byte[] _receiverId;
    private readonly Func<bool> _isReady;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _stop = new();
    private UdpClient? _socket;
    private Task? _worker;
    internal long InvalidPackets { get; private set; }

    internal DiscoveryResponder(byte[] receiverId, Func<bool> isReady, Action<string> log)
    {
        if (receiverId.Length != 16) throw new ArgumentException("receiverId must be 16 bytes.");
        _receiverId = receiverId.ToArray();
        _isReady = isReady;
        _log = log;
    }

    internal void Start(int port = DiscoveryProtocol.DiscoveryPort)
    {
        if (!_isReady()) throw new InvalidOperationException("TCP listener is not ready.");
        if (_socket != null) throw new InvalidOperationException("Discovery already started.");
        var socket = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            socket.ExclusiveAddressUse = true;
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            _socket = socket;
            _worker = Task.Run(() => ReceiveAsync(socket, _stop.Token));
        }
        catch { socket.Dispose(); throw; }
    }

    internal int BoundPort => ((IPEndPoint)_socket!.Client.LocalEndPoint!).Port;

    private async Task ReceiveAsync(UdpClient socket, CancellationToken token)
    {
        long lastDiagnostic = Environment.TickCount64 - 10_000;
        while (!token.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult packet = await socket.ReceiveAsync(token).ConfigureAwait(false);
                if (!DiscoveryProtocol.TryDecodeDiscover(packet.Buffer, out ulong nonce))
                {
                    InvalidPackets++;
                    if (Environment.TickCount64 - lastDiagnostic >= 10_000)
                    {
                        lastDiagnostic = Environment.TickCount64;
                        _log($"[Discovery] Ignored invalid packets: {InvalidPackets}");
                    }
                    continue;
                }
                if (!_isReady() || token.IsCancellationRequested) continue;
                // The payload has no address. Reply only to the probe's actual source endpoint.
                await socket.SendAsync(DiscoveryProtocol.EncodeOffer(nonce, _receiverId),
                    packet.RemoteEndPoint, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (token.IsCancellationRequested) { break; }
            catch (SocketException) when (token.IsCancellationRequested) { break; }
            catch (SocketException ex)
            {
                if (Environment.TickCount64 - lastDiagnostic >= 10_000)
                {
                    lastDiagnostic = Environment.TickCount64;
                    _log($"[Discovery] UDP error: {ex.SocketErrorCode}");
                }
                await Task.Delay(250, token).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _socket?.Dispose();
        try { _worker?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        _stop.Dispose();
    }
}
