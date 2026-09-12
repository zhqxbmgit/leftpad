using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class LineFrameDecoderTests
{
    [Fact]
    public void CompleteFrameInOneChunk_IsEmitted()
    {
        var frames = new List<string>();
        var decoder = new LineFrameDecoder();

        Append(decoder, "A\n", frames);

        Assert.Equal(["A"], frames);
    }

    [Fact]
    public void MultipleFramesInOneChunk_AreEmittedInOrder()
    {
        var frames = new List<string>();
        var decoder = new LineFrameDecoder();

        Append(decoder, "A\nB\n", frames);

        Assert.Equal(["A", "B"], frames);
    }

    [Fact]
    public void SplitFrame_IsEmittedOnlyAfterTerminatingChunk()
    {
        var frames = new List<string>();
        var decoder = new LineFrameDecoder();

        Append(decoder, "A-part1", frames);
        Assert.Empty(frames);

        Append(decoder, "A-part2\n", frames);
        Assert.Equal(["A-part1A-part2"], frames);
    }

    [Fact]
    public void FrameFedOneByteAtATime_IsEmittedOnce()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("complete\n");
        var frames = new List<string>();
        var decoder = new LineFrameDecoder();

        foreach (byte value in bytes)
            decoder.Append([value], frames.Add);

        Assert.Equal(["complete"], frames);
    }

    [Fact]
    public void CompleteFrameAndPartialTail_PreservesTailForNextChunk()
    {
        var frames = new List<string>();
        var decoder = new LineFrameDecoder();

        Append(decoder, "A\nB-part", frames);
        Assert.Equal(["A"], frames);
        Assert.Equal(6, decoder.BufferedByteCount);

        Append(decoder, "-rest\n", frames);
        Assert.Equal(["A", "B-part-rest"], frames);
    }

    [Fact]
    public void DelimiterInFollowingChunk_CompletesFrame()
    {
        var frames = new List<string>();
        var decoder = new LineFrameDecoder();

        Append(decoder, "payload", frames);
        Append(decoder, "\n", frames);

        Assert.Equal(["payload"], frames);
    }

    [Fact]
    public void CrLf_StripsCarriageReturn()
    {
        var frames = new List<string>();
        var decoder = new LineFrameDecoder();

        Append(decoder, "A\r\n", frames);

        Assert.Equal(["A"], frames);
    }

    [Fact]
    public void EmptyLines_AreIgnored()
    {
        var frames = new List<string>();
        var decoder = new LineFrameDecoder();

        Append(decoder, "\n\r\nA\n", frames);

        Assert.Equal(["A"], frames);
    }

    [Fact]
    public void Utf8CharacterSplitInsideMultibyteSequence_IsPreserved()
    {
        const string payload = "{\"label\":\"中文\"}";
        byte[] bytes = Encoding.UTF8.GetBytes(payload + "\n");
        int firstMultibyteByte = Array.FindIndex(bytes, value => value >= 0x80);
        var frames = new List<string>();
        var decoder = new LineFrameDecoder();

        decoder.Append(bytes.AsSpan(0, firstMultibyteByte + 1), frames.Add);
        decoder.Append(bytes.AsSpan(firstMultibyteByte + 1), frames.Add);

        Assert.Equal([payload], frames);
    }

    [Fact]
    public void BadJsonFrameBoundary_DoesNotPolluteFollowingFrame()
    {
        const string valid = "{\"button\":\"cross\",\"action\":\"up\"}";
        var frames = new List<string>();
        var decoder = new LineFrameDecoder();

        Append(decoder, $"bad-json\n{valid}\n", frames);

        Assert.Equal(["bad-json", valid], frames);
    }

    [Fact]
    public void UnterminatedFrameOverLimit_ThrowsWithoutGrowingBuffer()
    {
        const int maximum = 8;
        var decoder = new LineFrameDecoder(maximum);

        LineFrameException error = Assert.Throws<LineFrameException>(
            () => decoder.Append(Encoding.UTF8.GetBytes("123456789"), _ => { }));

        Assert.Contains("8-byte limit", error.Message);
        Assert.Equal(maximum, decoder.BufferedByteCount);
    }

    [Fact]
    public async Task EofWithIncompleteTail_DoesNotEmitFrame()
    {
        byte[] incomplete = Encoding.UTF8.GetBytes("{\"button\":\"cross\",\"action\":\"down\"}");
        await using var stream = new MemoryStream(incomplete);
        var frames = new List<string>();

        await LineFrameReader.ReadAsync(stream, frames.Add, CancellationToken.None);

        Assert.Empty(frames);
    }

    private static void Append(LineFrameDecoder decoder, string chunk, List<string> frames) =>
        decoder.Append(Encoding.UTF8.GetBytes(chunk), frames.Add);
}

public sealed class LineFrameReaderIntegrationTests
{
    [Fact]
    public async Task LocalhostTcp_SplitJsonFrame_IsReassembledBeforeNextFrame()
    {
        string[] frames = await ExchangeAsync(async stream =>
        {
            await WriteAsync(stream, "{\"button\":\"cross\",\"act");
            await Task.Delay(25);
            await WriteAsync(stream, "ion\":\"down\"}\n");
            await WriteAsync(stream, "{\"button\":\"cross\",\"action\":\"up\"}\n");
        });

        Assert.Equal(
            [
                "{\"button\":\"cross\",\"action\":\"down\"}",
                "{\"button\":\"cross\",\"action\":\"up\"}"
            ],
            frames);
    }

    [Fact]
    public async Task LocalhostTcp_CoalescedJsonFrames_AreEmittedInOrder()
    {
        string[] frames = await ExchangeAsync(stream => WriteAsync(
            stream,
            "{\"button\":\"cross\",\"action\":\"down\"}\n" +
            "{\"button\":\"cross\",\"action\":\"up\"}\n"));

        Assert.Equal(
            [
                "{\"button\":\"cross\",\"action\":\"down\"}",
                "{\"button\":\"cross\",\"action\":\"up\"}"
            ],
            frames);
    }

    private static async Task<string[]> ExchangeAsync(Func<NetworkStream, Task> send)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var client = new TcpClient();
            Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using TcpClient serverClient = await acceptTask;
            var frames = new List<string>();
            Task readTask = LineFrameReader.ReadAsync(
                serverClient.GetStream(),
                frames.Add,
                CancellationToken.None);

            await send(client.GetStream());
            client.Client.Shutdown(SocketShutdown.Send);
            await readTask.WaitAsync(TimeSpan.FromSeconds(5));
            return [.. frames];
        }
        finally
        {
            listener.Stop();
        }
    }

    private static Task WriteAsync(NetworkStream stream, string text) =>
        stream.WriteAsync(Encoding.UTF8.GetBytes(text).AsMemory()).AsTask();
}

[CollectionDefinition("Ds4Service TCP framing", DisableParallelization = true)]
public sealed class Ds4ServiceTcpFramingCollection;

[Collection("Ds4Service TCP framing")]
public sealed class Ds4ServiceTcpFramingIntegrationTests
{
    [Fact]
    public async Task SplitFramesAndMalformedJson_ProcessValidDownAndUpInOrder()
    {
        using ServiceFixture fixture = StartService();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, fixture.Service.Port);
        NetworkStream stream = client.GetStream();

        await WriteAsync(stream, "bad-json\n{\"button\":\"cross\",\"act");
        await Task.Delay(25);
        await WriteAsync(stream, "ion\":\"down\"}\n");
        await WriteAsync(stream, "{\"button\":\"cross\",\"action\":\"up\"}\n");
        await fixture.TwoButtonEvents.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            [(KeyboardKey.F1, true), (KeyboardKey.F1, false)],
            fixture.Keyboard.Events.ToArray());
        Assert.Equal(["cross -> down", "cross -> up"], fixture.ButtonEvents.ToArray());
    }

    [Fact]
    public async Task CoalescedFrames_ProcessDownAndUpInOrder()
    {
        using ServiceFixture fixture = StartService();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, fixture.Service.Port);

        await WriteAsync(
            client.GetStream(),
            "{\"button\":\"cross\",\"action\":\"down\"}\n" +
            "{\"button\":\"cross\",\"action\":\"up\"}\n");
        await fixture.TwoButtonEvents.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            [(KeyboardKey.F1, true), (KeyboardKey.F1, false)],
            fixture.Keyboard.Events.ToArray());
        Assert.Equal(["cross -> down", "cross -> up"], fixture.ButtonEvents.ToArray());
    }

    [Fact]
    public async Task OversizedFrame_DisconnectsClientAndReleasesPressedKey()
    {
        using ServiceFixture fixture = StartService();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, fixture.Service.Port);
        NetworkStream stream = client.GetStream();

        await WriteAsync(stream, "{\"button\":\"cross\",\"action\":\"down\"}\n");
        await fixture.KeyPressed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stream.WriteAsync(
            new byte[LineFrameDecoder.DefaultMaxFrameLength + 1],
            CancellationToken.None);
        await fixture.KeyReleased.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            [(KeyboardKey.F1, true), (KeyboardKey.F1, false)],
            fixture.Keyboard.Events.ToArray());
        Assert.Contains(fixture.Logs, message => message.Contains("Frame exceeded", StringComparison.Ordinal));
    }

    private static ServiceFixture StartService()
    {
        var keyboard = new RecordingKeyboardOutput();
        var service = new Ds4Service(
            new UnexpectedDirectDs4Factory(),
            keyboard,
            new MemoryBindingStore());
        Assert.True(service.TrySetOutputMode(OutputMode.Keyboard));
        KeyboardBindings bindings = service.KeyboardBindings;
        bindings.Cross = KeyboardKey.F1;
        Assert.True(service.TryUpdateKeyboardBindings(bindings));
        Assert.True(service.Initialize());

        var fixture = new ServiceFixture(service, keyboard);
        service.OnButtonEvent += fixture.RecordButtonEvent;
        service.OnLog += fixture.Logs.Add;
        service.Start();
        return fixture;
    }

    private static Task WriteAsync(NetworkStream stream, string text) =>
        stream.WriteAsync(Encoding.UTF8.GetBytes(text).AsMemory()).AsTask();

    private sealed class ServiceFixture : IDisposable
    {
        public ServiceFixture(Ds4Service service, RecordingKeyboardOutput keyboard)
        {
            Service = service;
            Keyboard = keyboard;
            keyboard.KeyPressed = KeyPressed;
            keyboard.KeyReleased = KeyReleased;
        }

        public Ds4Service Service { get; }
        public RecordingKeyboardOutput Keyboard { get; }
        public ConcurrentQueue<string> ButtonEvents { get; } = new();
        public ConcurrentBag<string> Logs { get; } = new();
        public TaskCompletionSource KeyPressed { get; } = NewSignal();
        public TaskCompletionSource KeyReleased { get; } = NewSignal();
        public TaskCompletionSource TwoButtonEvents { get; } = NewSignal();

        public void RecordButtonEvent(string value)
        {
            ButtonEvents.Enqueue(value);
            if (ButtonEvents.Count == 2) TwoButtonEvents.TrySetResult();
        }

        public void Dispose() => Service.Dispose();

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public ConcurrentQueue<(KeyboardKey Key, bool Pressed)> Events { get; } = new();
        public TaskCompletionSource? KeyPressed { get; set; }
        public TaskCompletionSource? KeyReleased { get; set; }

        public void SetKeyState(KeyboardKey key, bool isPressed)
        {
            Events.Enqueue((key, isPressed));
            if (isPressed) KeyPressed?.TrySetResult();
            else KeyReleased?.TrySetResult();
        }
    }

    private sealed class UnexpectedDirectDs4Factory : IDirectDs4Factory
    {
        public IDirectDs4Session Create() =>
            throw new InvalidOperationException("Keyboard-mode framing tests must not create ViGEm output.");
    }

    private sealed class MemoryBindingStore : IKeyboardBindingStore
    {
        private KeyboardBindings _bindings = new();
        public KeyboardBindings Load() => _bindings.Clone();
        public void Save(KeyboardBindings bindings) => _bindings = bindings.Clone();
    }
}
