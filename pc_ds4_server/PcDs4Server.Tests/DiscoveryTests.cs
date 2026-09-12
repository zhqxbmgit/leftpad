using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace PcDs4Server.Tests;

// The production-port tests must not race with other service lifecycle tests.
[CollectionDefinition("Discovery ports", DisableParallelization = true)]
public sealed class DiscoveryPortCollection { }

[Collection("Discovery ports")]
public sealed class DiscoveryTests
{
    private static readonly byte[] Identity = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
    private const ulong Nonce = 0x0807060504030201UL;
    private static byte[] Discover(ulong nonce = Nonce)
    {
        byte[] bytes = { 76, 80, 65, 68, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), nonce);
        return bytes;
    }

    [Fact]
    public void DiscoverDecodesExactWireAndUnsignedNonce()
    {
        Assert.True(DiscoveryProtocol.TryDecodeDiscover(Discover(), out ulong decoded));
        Assert.Equal(Nonce, decoded);
        Assert.True(DiscoveryProtocol.TryDecodeDiscover(Discover(ulong.MaxValue), out decoded));
        Assert.Equal(ulong.MaxValue, decoded);
    }

    [Fact]
    public void DiscoverRejectsEveryTruncationOversizeAndInvalidHeader()
    {
        for (int length = 0; length < 16; length++)
            Assert.False(DiscoveryProtocol.TryDecodeDiscover(Discover()[..length], out _));
        Assert.False(DiscoveryProtocol.TryDecodeDiscover(new byte[17], out _));
        Assert.False(DiscoveryProtocol.TryDecodeDiscover(Discover().Concat(new byte[] { 0 }).ToArray(), out _));
        for (int offset = 0; offset < 8; offset++)
        {
            byte[] malformed = Discover();
            malformed[offset] ^= 0x40;
            Assert.False(DiscoveryProtocol.TryDecodeDiscover(malformed, out _));
        }
    }

    [Fact]
    public void OfferMatchesAndroidGoldenVectorWithNoIpField()
    {
        byte[] offer = DiscoveryProtocol.EncodeOffer(Nonce, Identity);
        Assert.Equal(40, offer.Length);
        Assert.Equal(Convert.FromHexString(
            "4C504144010200000102030405060708000102030405060708090A0B0C0D0E0FB822010000000000"), offer);
        Assert.Equal(Nonce, BinaryPrimitives.ReadUInt64LittleEndian(offer.AsSpan(8)));
        Assert.Equal(Identity, offer[16..32]);
        Assert.Equal(8888, BinaryPrimitives.ReadUInt16LittleEndian(offer.AsSpan(32)));
        Assert.Equal(1, offer[34]);
        Assert.All(offer[35..], value => Assert.Equal(0, value));
        Assert.Throws<ArgumentException>(() => DiscoveryProtocol.EncodeOffer(0, new byte[15]));
        Assert.Throws<ArgumentException>(() => DiscoveryProtocol.EncodeOffer(0, new byte[17]));
    }

    [Fact]
    public void IdentityFirstCreateReloadAndExactOpaqueWireRoundtrip()
    {
        using var directory = new IdentityDirectory();
        byte[] first = ReceiverIdentityStore.LoadOrCreate(directory.Path, _ => Assert.Fail("unexpected repair"));
        Assert.Equal(16, first.Length);
        Assert.Equal(32, File.ReadAllText(directory.Path).Length);
        byte[] second = ReceiverIdentityStore.LoadOrCreate(directory.Path, _ => Assert.Fail("unexpected repair"));
        Assert.Equal(first, second);
        Assert.Equal(first, DiscoveryProtocol.EncodeOffer(Nonce, second)[16..32]);
        Assert.Single(Directory.GetFiles(System.IO.Path.GetDirectoryName(directory.Path)!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("broken")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("000102030405060708090a0b0c0d0e0f\n")]
    public void CorruptIdentityIsDiagnosticallyAndAtomicallyReplaced(string corrupt)
    {
        using var directory = new IdentityDirectory();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(directory.Path)!);
        File.WriteAllText(directory.Path, corrupt);
        var logs = new List<string>();
        byte[] id = ReceiverIdentityStore.LoadOrCreate(directory.Path, logs.Add);
        Assert.Equal(16, id.Length);
        Assert.Single(logs);
        Assert.Contains("Invalid receiver identity", logs[0]);
        Assert.Equal(id, Convert.FromHexString(File.ReadAllText(directory.Path)));
        Assert.Equal(id, ReceiverIdentityStore.LoadOrCreate(directory.Path, logs.Add));
        Assert.Single(logs);
    }

    [Fact]
    public async Task ConcurrentIdentityCreatorsObserveOneCompleteIdentity()
    {
        using var directory = new IdentityDirectory();
        byte[][] identities = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            ReceiverIdentityStore.LoadOrCreate(directory.Path, _ => Assert.Fail("unexpected corruption")))));
        Assert.All(identities, id => Assert.Equal(identities[0], id));
    }

    [Fact]
    public async Task ResponderUnicastsRepeatedOffersEchoesNonceIgnoresInvalidAndStopsAdvertisingWhenUnready()
    {
        bool ready = true;
        using var responder = new DiscoveryResponder(Identity, () => Volatile.Read(ref ready), _ => { });
        responder.Start(0);
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var endpoint = new IPEndPoint(IPAddress.Loopback, responder.BoundPort);
        await client.SendAsync(new byte[] { 1, 2, 3 }, endpoint);
        await AssertNoOffer(client);
        for (ulong nonce = 0; nonce < 3; nonce++)
        {
            await client.SendAsync(Discover(nonce), endpoint);
            using var timeout = new CancellationTokenSource(2000);
            UdpReceiveResult response = await client.ReceiveAsync(timeout.Token);
            Assert.Equal(endpoint, response.RemoteEndPoint);
            Assert.Equal(DiscoveryProtocol.EncodeOffer(nonce, Identity), response.Buffer);
        }
        Assert.Equal(1, responder.InvalidPackets);
        Volatile.Write(ref ready, false);
        await client.SendAsync(Discover(), endpoint);
        await AssertNoOffer(client);
    }

    [Fact]
    public void ResponderCannotStartBeforeTcpReady()
    {
        using var responder = new DiscoveryResponder(Identity, () => false, _ => { });
        Assert.Throws<InvalidOperationException>(() => responder.Start());
        using var available = new UdpClient(new IPEndPoint(IPAddress.Any, 8889));
    }

    [Fact]
    public void Udp8889HasExclusiveOwnershipAndReleasesOnShutdown()
    {
        var responder = new DiscoveryResponder(Identity, () => true, _ => { });
        try
        {
            responder.Start();
            Assert.Equal(8889, responder.BoundPort);
            Assert.Throws<SocketException>(() =>
            {
                using var conflict = new UdpClient(new IPEndPoint(IPAddress.Any, 8889));
            });
        }
        finally { responder.Dispose(); }
        using var released = new UdpClient(new IPEndPoint(IPAddress.Any, 8889));
    }

    [Fact]
    public void FailedTcpStartNeverCreatesIdentityOrAdvertises()
    {
        using var occupied = new TcpListener(IPAddress.Any, 8888);
        occupied.Server.ExclusiveAddressUse = true;
        occupied.Start();
        using var service = new Ds4Service(receiverIdentityProvider: () => throw new Exception("must not be called"));
        Assert.True(service.TrySetOutputMode(OutputMode.Keyboard));
        Assert.Throws<SocketException>(service.Start);
        Assert.False(service.IsRunning);
        using var available = new UdpClient(new IPEndPoint(IPAddress.Any, 8889));
    }

    [Fact]
    public async Task ServiceStartAdvertisesOnlyAfterTcpBindAndStopReleasesBothPorts()
    {
        using var service = new Ds4Service(receiverIdentityProvider: () => Identity);
        Assert.True(service.TrySetOutputMode(OutputMode.Keyboard));
        service.Start();
        Assert.True(service.IsRunning);
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await client.SendAsync(Discover(), new IPEndPoint(IPAddress.Loopback, 8889));
        using var timeout = new CancellationTokenSource(2000);
        Assert.Equal(DiscoveryProtocol.EncodeOffer(Nonce, Identity), (await client.ReceiveAsync(timeout.Token)).Buffer);
        using (var tcp = new TcpClient()) await tcp.ConnectAsync(IPAddress.Loopback, 8888);
        service.Stop();
        Assert.False(service.IsRunning);
        using var udpReleased = new UdpClient(new IPEndPoint(IPAddress.Any, 8889));
        using var tcpReleased = new TcpListener(IPAddress.Any, 8888);
        tcpReleased.Start();
    }

    [Fact]
    public void FailedUdpBindRollsBackTcpAndDoesNotAdvertise()
    {
        using var occupied = new UdpClient(AddressFamily.InterNetwork);
        occupied.ExclusiveAddressUse = true;
        occupied.Client.Bind(new IPEndPoint(IPAddress.Any, 8889));
        using var service = new Ds4Service(receiverIdentityProvider: () => Identity);
        Assert.True(service.TrySetOutputMode(OutputMode.Keyboard));
        Assert.Throws<SocketException>(service.Start);
        Assert.False(service.IsRunning);
        using var released = new TcpListener(IPAddress.Any, 8888);
        released.Start();
    }

    private static async Task AssertNoOffer(UdpClient client)
    {
        using var timeout = new CancellationTokenSource(150);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.ReceiveAsync(timeout.Token));
    }

    private sealed class IdentityDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "leftpad-discovery-test-" + Guid.NewGuid().ToString("N"), "receiver-id");
        public void Dispose()
        {
            string directory = System.IO.Path.GetDirectoryName(Path)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
