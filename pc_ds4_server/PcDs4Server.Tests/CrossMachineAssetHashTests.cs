using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class CrossMachineAssetHashTests
{
    private const string Json = "{\n  \"name\": \"菜单\",\n  \"scale\": 100\n}\n";

    [Fact]
    public void LineEndingsShareOneCanonicalLfAuthority()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json)));
        foreach (string eol in new[] { "\n", "\r\n", "\r" })
        {
            string path = Path.Combine(temporary.Root, "asset.json");
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(Json.Replace("\n", eol)));
            Assert.Equal(expected, CanonicalAssetHash.JsonSha256(path));
        }
    }

    [Theory]
    [InlineData("character")]
    [InlineData("indentation")]
    [InlineData("final-newline")]
    [InlineData("bom")]
    public void NonEolByteChangesRemainVisible(string mutation)
    {
        using var temporary = new RadialVisualPackTestDirectory();
        string original = Path.Combine(temporary.Root, "original.json");
        string changed = Path.Combine(temporary.Root, "changed.json");
        string modified = mutation switch
        {
            "character" => Json.Replace("100", "101"),
            "indentation" => Json.Replace("  ", " "),
            "final-newline" => Json[..^1],
            "bom" => "\uFEFF" + Json,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        File.WriteAllBytes(original, Encoding.UTF8.GetBytes(Json));
        File.WriteAllBytes(changed, Encoding.UTF8.GetBytes(modified));
        Assert.NotEqual(CanonicalAssetHash.JsonSha256(original), CanonicalAssetHash.JsonSha256(changed));
    }

    [Fact]
    public void ProductionJsonLfAndCrlfCopiesMatchWithoutChangingSourceBytes()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        string[] files = new[]
            {
                RadialVisualPackContract.DiscoveryRoot,
                UiThemeV2Contract.DiscoveryRoot
            }
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
            .ToArray();
        Assert.NotEmpty(files);
        foreach (string source in files)
        {
            byte[] original = File.ReadAllBytes(source);
            string lf = Encoding.UTF8.GetString(original).Replace("\r\n", "\n").Replace('\r', '\n');
            string lfPath = Path.Combine(temporary.Root, "lf.json");
            string crlfPath = Path.Combine(temporary.Root, "crlf.json");
            File.WriteAllBytes(lfPath, Encoding.UTF8.GetBytes(lf));
            File.WriteAllBytes(crlfPath, Encoding.UTF8.GetBytes(lf.Replace("\n", "\r\n")));
            string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lf)));
            Assert.Equal(expected, CanonicalAssetHash.JsonSha256(source));
            Assert.Equal(expected, CanonicalAssetHash.JsonSha256(lfPath));
            Assert.Equal(expected, CanonicalAssetHash.JsonSha256(crlfPath));
            Assert.Equal(original, File.ReadAllBytes(source));
        }
    }

    [Fact]
    public void BinaryAssetsCannotOptIntoTextNormalization()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        string path = Path.Combine(temporary.Root, "asset.png");
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4e, 0x47, 13, 10, 0, 255 });
        Assert.Throws<ArgumentException>(() => CanonicalAssetHash.JsonSha256(path));
    }
}
