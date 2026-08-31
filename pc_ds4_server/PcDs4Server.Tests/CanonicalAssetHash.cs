using System.Security.Cryptography;

namespace PcDs4Server.Tests;

internal static class CanonicalAssetHash
{
    // Opt in only for frozen Production JSON source metadata (UIVisualPacks/UIThemes).
    // Preserve every UTF-8 byte, including BOM, spacing and final newline, except EOL.
    // Binary assets and rendered pixels must keep their existing raw-byte authority.
    public static string JsonSha256(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Canonical text hashing requires a JSON asset.", nameof(path));

        byte[] bytes = File.ReadAllBytes(path);
        byte[] canonical = new byte[bytes.Length];
        int length = 0;
        for (int index = 0; index < bytes.Length; index++)
        {
            byte value = bytes[index];
            if (value == (byte)'\r')
            {
                value = (byte)'\n';
                if (index + 1 < bytes.Length && bytes[index + 1] == (byte)'\n')
                    index++;
            }
            canonical[length++] = value;
        }
        return Convert.ToHexString(SHA256.HashData(canonical.AsSpan(0, length)));
    }
}
