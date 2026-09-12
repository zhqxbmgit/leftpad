using System.Buffers.Binary;

namespace PcDs4Server;

internal static class DiscoveryProtocol
{
    internal const int DiscoveryPort = 8889;
    internal const ushort ControllerPort = 8888;
    internal const int DiscoverLength = 16;
    internal const int OfferLength = 40;

    internal static bool TryDecodeDiscover(ReadOnlySpan<byte> packet, out ulong nonce)
    {
        nonce = 0;
        if (packet.Length != DiscoverLength || !packet[..4].SequenceEqual("LPAD"u8) ||
            packet[4] != 1 || packet[5] != 1 || packet[6] != 0 || packet[7] != 0)
            return false;
        nonce = BinaryPrimitives.ReadUInt64LittleEndian(packet[8..]);
        return true;
    }

    internal static byte[] EncodeOffer(ulong nonce, ReadOnlySpan<byte> receiverId)
    {
        if (receiverId.Length != 16) throw new ArgumentException("receiverId must be 16 opaque bytes.");
        var packet = new byte[OfferLength];
        "LPAD"u8.CopyTo(packet);
        packet[4] = 1;
        packet[5] = 2;
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(8), nonce);
        receiverId.CopyTo(packet.AsSpan(16));
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(32), ControllerPort);
        packet[34] = 1;
        return packet;
    }
}
