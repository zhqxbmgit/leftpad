using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class RadialVisualPackTests
{
    private const string ExpectedBaseSha =
        "FBB0B269DF351FC11357F154281E1F9D9D5ECFC637CF3068DAB3D059E1BD6A84";
    private const string ExpectedSelectedSha =
        "34A73FB156369E7A4F0A8AE6B95B9114ACF4114BB057083596FAECCBDF0CCB62";
    private const string ExpectedLayoutSha =
        "58C489F4C2463B94FB83E5D8EDFFACA7E178E0BFC6A4F481AEE615FCF06833A5";

    [Fact]
    public void ProductionPack_LoadsApprovedManifestAndLayout()
    {
        RadialVisualPackDefinition pack = LoadProductionPack();

        Assert.Equal("radial-v5", pack.Manifest.Id);
        Assert.Equal("Tactical HUD V5", pack.Manifest.Name);
        Assert.Equal(1, pack.Manifest.Version);
        Assert.Equal("radial-6", pack.Manifest.LayoutProfile);
        Assert.Equal(6, pack.Manifest.SlotCount);
        Assert.Equal("canonical-transform", pack.Manifest.SelectionAssetMode);
        Assert.Equal(1254, pack.Layout.Canvas.Width);
        Assert.Equal(1254, pack.Layout.Canvas.Height);
        Assert.Equal("RGBA", pack.Layout.Canvas.Mode);
        Assert.Equal(627d, pack.Layout.WheelCenter.X);
        Assert.Equal(627d, pack.Layout.WheelCenter.Y);
        Assert.Equal(6, pack.Layout.SlotCount);
        Assert.Equal(6, pack.Layout.Slots.Length);
    }

    [Theory]
    [InlineData(1, 0d)]
    [InlineData(2, 60d)]
    [InlineData(3, 120d)]
    [InlineData(4, 180d)]
    [InlineData(5, 240d)]
    [InlineData(6, 300d)]
    public void CanonicalSelection_RotatesClockwiseToSlot(int slot, double expectedDegrees)
    {
        Assert.Equal(expectedDegrees, LoadProductionPack().GetRotationDegrees(slot));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void CanonicalSelection_RejectsInvalidSlot(int slot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LoadProductionPack().GetRotationDegrees(slot));
    }

    [Fact]
    public void Loader_RejectsUnsupportedLayoutProfile()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "LeftPad.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "manifest.json"), """
                {
                  "id": "radial-v5",
                  "name": "Unsupported test pack",
                  "version": 1,
                  "layoutProfile": "grid-3x2",
                  "slotCount": 6,
                  "base": "base.png",
                  "selected": "selected.png",
                  "layout": "layout.json",
                  "selectionAssetMode": "canonical-transform"
                }
                """);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => RadialVisualPackDefinition.Load(directory));
            Assert.Contains("Unsupported layout profile", exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ProductionPngs_Are1254SquareAndAlphaCapable()
    {
        RadialVisualPackDefinition pack = LoadProductionPack();

        using Image baseImage = Image.FromFile(pack.BasePath);
        using Image selectedImage = Image.FromFile(pack.SelectedPath);
        Assert.Equal(new Size(1254, 1254), baseImage.Size);
        Assert.Equal(new Size(1254, 1254), selectedImage.Size);
        Assert.True(Image.IsAlphaPixelFormat(baseImage.PixelFormat));
        Assert.True(Image.IsAlphaPixelFormat(selectedImage.PixelFormat));
    }

    [Fact]
    public void ProductionAssets_MatchFrozenSha256()
    {
        RadialVisualPackDefinition pack = LoadProductionPack();

        Assert.Equal(ExpectedBaseSha, Sha256(pack.BasePath));
        Assert.Equal(ExpectedSelectedSha, Sha256(pack.SelectedPath));
        Assert.Equal(ExpectedLayoutSha, Sha256(pack.LayoutPath));
    }

    [Theory]
    [InlineData(1, 0d)]
    [InlineData(2, 60d)]
    [InlineData(3, 120d)]
    [InlineData(4, 180d)]
    [InlineData(5, 240d)]
    [InlineData(6, 300d)]
    public void ScaledSelectionCache_IsPArgbAndPointsToExpectedSlot(
        int slot,
        double expectedDegrees)
    {
        using var cache = new RadialVisualPackCache(LoadProductionPack(), targetSize: 280);
        Bitmap selected = cache.GetSelectedSlot(slot);

        Assert.Equal(PixelFormat.Format32bppPArgb, cache.ScaledBase.PixelFormat);
        Assert.Equal(PixelFormat.Format32bppPArgb, selected.PixelFormat);
        PointF centroid = AlphaCentroid(selected);
        double angle = Math.Atan2(centroid.X - 140d, 140d - centroid.Y) * (180d / Math.PI);
        if (angle < 0d) angle += 360d;
        double delta = Math.Abs(angle - expectedDegrees);
        delta = Math.Min(delta, 360d - delta);
        Assert.InRange(delta, 0d, 1.5d);
    }

    [Fact]
    public void ScaledCache_RebuildsOnlyWhenTargetSizeChanges()
    {
        using var cache = new RadialVisualPackCache(LoadProductionPack(), targetSize: 280);
        Bitmap originalBase = cache.ScaledBase;

        cache.Rebuild(280);
        Assert.Same(originalBase, cache.ScaledBase);

        cache.Rebuild(392);
        Assert.NotSame(originalBase, cache.ScaledBase);
        Assert.Equal(new Size(392, 392), cache.ScaledBase.Size);
        Assert.All(
            Enumerable.Range(1, 6),
            slot => Assert.Equal(new Size(392, 392), cache.GetSelectedSlot(slot).Size));
    }

    private static RadialVisualPackDefinition LoadProductionPack() =>
        RadialVisualPackDefinition.Load(RadialVisualPackDefinition.DefaultDirectory);

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static PointF AlphaCentroid(Bitmap bitmap)
    {
        double weightedX = 0d;
        double weightedY = 0d;
        long totalAlpha = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                int alpha = bitmap.GetPixel(x, y).A;
                weightedX += x * alpha;
                weightedY += y * alpha;
                totalAlpha += alpha;
            }
        }

        Assert.True(totalAlpha > 0);
        return new PointF((float)(weightedX / totalAlpha), (float)(weightedY / totalAlpha));
    }
}
