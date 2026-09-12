using System.Text;

namespace PcDs4Server;

internal sealed class LineFrameDecoder
{
    public const int DefaultMaxFrameLength = 4 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly byte[] _frameBuffer;
    private int _frameLength;

    public LineFrameDecoder(int maxFrameLength = DefaultMaxFrameLength)
    {
        if (maxFrameLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFrameLength));

        _frameBuffer = new byte[maxFrameLength];
    }

    internal int BufferedByteCount => _frameLength;

    public void Append(ReadOnlySpan<byte> chunk, Action<string> onFrame)
    {
        ArgumentNullException.ThrowIfNull(onFrame);

        foreach (byte value in chunk)
        {
            if (value == (byte)'\n')
            {
                EmitFrame(onFrame);
                continue;
            }

            if (_frameLength == _frameBuffer.Length)
            {
                throw new LineFrameException(
                    $"Frame exceeded the {_frameBuffer.Length}-byte limit before LF delimiter.");
            }

            _frameBuffer[_frameLength++] = value;
        }
    }

    private void EmitFrame(Action<string> onFrame)
    {
        int payloadLength = _frameLength;
        _frameLength = 0;

        if (payloadLength > 0 && _frameBuffer[payloadLength - 1] == (byte)'\r')
            payloadLength--;
        if (payloadLength == 0) return;

        try
        {
            onFrame(StrictUtf8.GetString(_frameBuffer, 0, payloadLength));
        }
        catch (DecoderFallbackException ex)
        {
            throw new LineFrameException("Frame contains invalid UTF-8.", ex);
        }
    }
}

internal sealed class LineFrameException : IOException
{
    public LineFrameException(string message) : base(message) { }
    public LineFrameException(string message, Exception innerException) : base(message, innerException) { }
}

internal static class LineFrameReader
{
    private const int ReadBufferLength = 1024;

    public static async Task ReadAsync(
        Stream stream,
        Action<string> onFrame,
        CancellationToken cancellationToken,
        int maxFrameLength = LineFrameDecoder.DefaultMaxFrameLength)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(onFrame);

        var decoder = new LineFrameDecoder(maxFrameLength);
        byte[] readBuffer = new byte[ReadBufferLength];
        while (!cancellationToken.IsCancellationRequested)
        {
            int bytesRead = await stream.ReadAsync(readBuffer, cancellationToken);
            if (bytesRead == 0) return;
            decoder.Append(readBuffer.AsSpan(0, bytesRead), onFrame);
        }
    }
}
