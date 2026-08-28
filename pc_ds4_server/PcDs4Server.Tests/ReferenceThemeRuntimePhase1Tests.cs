using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class ReferenceThemeRuntimePhase1Tests
{
    [Theory]
    [InlineData("radial-v5", "Tactical HUD V5", "radial-6", 6)]
    [InlineData("radial-8-minimal-v1", "Radial 8 Minimal V1", "radial-8", 8)]
    public void V1ProductionPack_AdaptsToCompleteNormalizedPlan(
        string packId,
        string displayName,
        string layoutProfileId,
        int slotCount)
    {
        RadialVisualPackDefinition definition = LoadProductionPack(packId);

        NormalizedRenderPlan plan = V1VisualPackCompatibilityAdapter.BuildPlan(definition);

        Assert.Equal(1, plan.SourceProtocolVersion);
        Assert.Equal(packId, plan.ThemeId);
        Assert.Equal(displayName, plan.DisplayName);
        Assert.Same(definition.LayoutDefinition, plan.LayoutDefinition);
        Assert.Equal(layoutProfileId, plan.LayoutProfileId);
        Assert.Equal(slotCount, plan.LayoutDefinition.SlotCount);
        Assert.Equal(new NormalizedReferenceCanvas(1254, 1254, "RGBA"), plan.ReferenceCanvas);
        Assert.Equal(NormalizedReferenceScale.DirectToFinalPhysical, plan.ReferenceScale.Mode);
        Assert.Equal(NormalizedDynamicContentDescriptor.LegacyGlyphMappings, plan.DynamicContent.Mode);
        Assert.Equal(RadialVisualPackContract.DefaultVisualPackId, plan.Fallback.StartupFallbackThemeId);
        Assert.True(plan.Fallback.RetainActiveOnCandidateFailure);
        Assert.Contains("pargb-layered-window", plan.RequiredRuntimeCapabilities);

        LegacyCanonicalSelectedPlan renderModel = Assert.IsType<LegacyCanonicalSelectedPlan>(
            plan.RenderModel);
        Assert.Same(Assert.Single(plan.StaticLayers), renderModel.BaseLayer);
        Assert.Equal(definition.BasePath, renderModel.BaseLayer.AssetPath);
        Assert.Equal(definition.SelectedPath, renderModel.CanonicalSelectedAssetPath);
        Assert.Equal(
            LegacyCanonicalSelectedPlan.LegacyRotationStateLookup,
            renderModel.StateLookupMode);
        Assert.Equal(
            NormalizedGeometryTransform.LayoutSlotRotation,
            Assert.Single(plan.GeometryTransforms).Mode);
    }

    [Fact]
    public void NormalizedPlan_IsImmutableAndDoesNotRetainSettings()
    {
        NormalizedRenderPlan plan = V1VisualPackCompatibilityAdapter.BuildPlan(
            LoadProductionPack("radial-v5"));

        Assert.All(
            typeof(NormalizedRenderPlan).GetProperties(),
            property => Assert.False(property.CanWrite, property.Name));
        Assert.DoesNotContain(
            typeof(NormalizedRenderPlan).GetProperties(),
            property => property.PropertyType == typeof(RadialMenuSettings));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<NormalizedStaticLayer>)plan.StaticLayers).Add(
                new NormalizedStaticLayer("mutation", "mutation.png")));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)plan.RequiredRuntimeCapabilities).Add("mutation"));
    }

    [Theory]
    [InlineData("radial-v5", 1, 0d)]
    [InlineData("radial-v5", 6, 300d)]
    [InlineData("radial-8-minimal-v1", 7, 270d)]
    [InlineData("radial-8-minimal-v1", 8, 315d)]
    public void LegacyCanonicalTransform_ContinuesToUseAuthoritativeLayoutAngles(
        string packId,
        int slot,
        double expectedAngle)
    {
        RadialVisualPackDefinition definition = LoadProductionPack(packId);
        NormalizedRenderPlan plan = V1VisualPackCompatibilityAdapter.BuildPlan(definition);

        Assert.Same(definition.LayoutDefinition, plan.LayoutDefinition);
        Assert.Equal(expectedAngle, plan.LayoutDefinition.Slots[slot - 1].AngleDegrees);
        Assert.DoesNotContain(
            typeof(NormalizedRenderPlan).GetProperties(),
            property => property.Name.Contains("Boundary", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("DeadZone", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(96, 280)]
    [InlineData(120, 350)]
    [InlineData(144, 420)]
    [InlineData(168, 490)]
    [InlineData(192, 560)]
    public void Bundle_UsesDirectFinalPhysicalPArgbCaches(int dpi, int expectedSize)
    {
        NormalizedRenderPlan plan = V1VisualPackCompatibilityAdapter.BuildPlan(
            LoadProductionPack("radial-v5"));
        int targetSize = RadialDpiScaling.ToPhysicalPixels(280, dpi);

        using RuntimeRenderBundle bundle = RuntimeRenderBundle.Build(
            plan,
            RadialMenuSettings.Default,
            targetSize);

        Assert.Equal(expectedSize, bundle.TargetSize);
        Assert.Same(plan, bundle.Plan);
        Assert.Equal(PixelFormat.Format32bppPArgb, bundle.AssetCache.ScaledBase.PixelFormat);
        Assert.All(
            Enumerable.Range(1, plan.LayoutDefinition.SlotCount),
            slot => Assert.Equal(
                PixelFormat.Format32bppPArgb,
                bundle.AssetCache.GetSelectedSlot(slot).PixelFormat));
        Assert.Equal(PixelFormat.Format32bppPArgb, bundle.DynamicContent.Content.PixelFormat);
    }

    [Fact]
    public void DpiRebuild_UsesSamePlanAndAtomicallySwapsCompleteBundle()
    {
        RadialVisualPackDefinition definition = LoadProductionPack("radial-v5");
        using var session = new RadialVisualPackSession(
            definition,
            RadialMenuSettings.Default,
            targetSize: 280);
        NormalizedRenderPlan plan = session.Plan;
        RuntimeRenderBundle original = session.Bundle;

        session.EnsureContent(RadialMenuSettings.Default, targetSize: 420);

        Assert.Same(plan, session.Plan);
        Assert.NotSame(original, session.Bundle);
        Assert.Equal(420, session.Bundle.TargetSize);
        Assert.True(original.IsDisposed);
        Assert.Equal(1, original.DisposeCount);
        Assert.False(session.Bundle.IsDisposed);
    }

    [Fact]
    public void FailedDpiRebuild_RetainsOldBundleAndMappings()
    {
        RadialVisualPackDefinition definition = LoadProductionPack("radial-v5");
        NormalizedRenderPlan plan = V1VisualPackCompatibilityAdapter.BuildPlan(definition);
        RuntimeRenderBundle Builder(
            NormalizedRenderPlan candidatePlan,
            RadialMenuSettings settings,
            int targetSize)
        {
            if (targetSize == 420)
                throw new InvalidDataException("Synthetic DPI bundle failure.");
            return RuntimeRenderBundle.Build(candidatePlan, settings, targetSize);
        }

        using var session = new RadialVisualPackSession(
            definition,
            plan,
            RadialMenuSettings.Default,
            targetSize: 280,
            Builder);
        RuntimeRenderBundle original = session.Bundle;
        RadialSlotMappings mappings = session.Mappings;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            session.EnsureContent(RadialMenuSettings.Default, targetSize: 420));

        Assert.Contains("Synthetic DPI bundle failure", exception.Message);
        Assert.Same(original, session.Bundle);
        Assert.Same(mappings, session.Mappings);
        Assert.Equal(280, session.Bundle.TargetSize);
        Assert.False(original.IsDisposed);
        Assert.Equal(0, original.DisposeCount);
    }

    [Fact]
    public void RuntimeRefreshFailure_ReturnsStillValidActiveSession()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        string directory = temporary.AddPack("default", "radial-v5", "Tactical HUD V5");
        using var runtime = new RadialVisualPackRuntime(new RadialVisualPackCatalog(temporary.Root));
        RadialVisualPackSession session = runtime.Ensure(
            RadialMenuSettings.Default,
            targetSize: 280)!;
        RuntimeRenderBundle original = session.Bundle;
        File.Delete(Path.Combine(directory, "radial-base-v5.png"));

        RadialVisualPackSession retained = runtime.Ensure(
            RadialMenuSettings.Default,
            targetSize: 420)!;

        Assert.Same(session, retained);
        Assert.Same(original, session.Bundle);
        Assert.False(original.IsDisposed);
        Assert.Equal(280, original.TargetSize);
        Assert.False(string.IsNullOrWhiteSpace(runtime.LastError));
    }

    [Fact]
    public void MappingChange_RebuildsOnlyDynamicContentInsideCurrentBundle()
    {
        RadialVisualPackDefinition definition = LoadProductionPack("radial-v5");
        using var session = new RadialVisualPackSession(
            definition,
            RadialMenuSettings.Default,
            targetSize: 280);
        RuntimeRenderBundle bundle = session.Bundle;
        Bitmap baseCache = session.AssetCache.ScaledBase;
        Bitmap selectedCache = session.AssetCache.GetSelectedSlot(1);
        Bitmap dynamicCache = session.DynamicContent.Content;
        RadialMenuSettings remapped = RadialMenuSettings.Default.SetProfileMappings(
            LayoutProfileRegistry.Radial6ProfileId,
            RadialSlotMappings.Create(6).WithSlot(1, new RadialSlotMapping
            {
                Kind = RadialActionKind.KeyboardKey,
                Key = KeyboardKey.F1
            }));

        session.EnsureContent(remapped, targetSize: 280);

        Assert.Same(bundle, session.Bundle);
        Assert.Same(baseCache, session.AssetCache.ScaledBase);
        Assert.Same(selectedCache, session.AssetCache.GetSelectedSlot(1));
        Assert.NotSame(dynamicCache, session.DynamicContent.Content);
        Assert.Equal(2, session.DynamicContent.BuildCount);
        Assert.Equal(KeyboardKey.F1, session.Mappings[0].Key);
    }

    [Fact]
    public void BundleAndSession_DisposeOwnedResourcesExactlyOnce()
    {
        var session = new RadialVisualPackSession(
            LoadProductionPack("radial-8-minimal-v1"),
            RadialMenuSettings.Default with
            {
                VisualPackId = "radial-8-minimal-v1",
                MappingProfileId = LayoutProfileRegistry.Radial8ProfileId
            },
            targetSize: 280);
        RuntimeRenderBundle bundle = session.Bundle;

        session.Dispose();
        session.Dispose();

        Assert.True(session.IsDisposed);
        Assert.True(bundle.IsDisposed);
        Assert.Equal(1, bundle.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() =>
            bundle.EnsureDynamicContent(RadialMenuSettings.Default));
    }

    [Theory]
    [InlineData("radial-v5", 29.999999, 1)]
    [InlineData("radial-v5", 30.0, 2)]
    [InlineData("radial-v5", 330.0, 1)]
    [InlineData("radial-8-minimal-v1", 22.499999, 1)]
    [InlineData("radial-8-minimal-v1", 22.5, 2)]
    [InlineData("radial-8-minimal-v1", 292.5, 8)]
    [InlineData("radial-8-minimal-v1", 337.5, 1)]
    public void Selection_RemainsOwnedByLayoutAndSelectionEngine(
        string packId,
        double angleDegrees,
        int expectedSlot)
    {
        NormalizedRenderPlan plan = V1VisualPackCompatibilityAdapter.BuildPlan(
            LoadProductionPack(packId));
        double radians = angleDegrees * (Math.PI / 180d);
        double deltaX = Math.Sin(radians) * 100d;
        double deltaY = -Math.Cos(radians) * 100d;

        int selected = RadialSelectionEngine.GetSelectedSlot(
            plan.LayoutDefinition,
            deltaX,
            deltaY,
            deadZoneRadius: 28d);

        Assert.Equal(expectedSlot, selected);
        Assert.Equal(
            0,
            RadialSelectionEngine.GetSelectedSlot(
                plan.LayoutDefinition,
                deltaX: 0,
                deltaY: 28,
                deadZoneRadius: 28d));
    }

    [Theory]
    [InlineData("radial-8-minimal-v1/manifest.json", "65512BACF70A0F3CAEE646309467DCB11B7E02118D2E012B700887340CD46986")]
    [InlineData("radial-8-minimal-v1/radial-base.png", "86236F861751A1A8944191E5F8BDC49B3096133DAF23CD588340163CCDF7DFF8")]
    [InlineData("radial-8-minimal-v1/radial-layout.json", "4028D56EC789E0CED3514DAFD357FD160223BCDEF7082F1F2B3B31D2219CE4E1")]
    [InlineData("radial-8-minimal-v1/radial-selected-card.png", "2516E30C60E8727AC6F173B8828708B454A9CC79A973BF6664180FB36D75A301")]
    [InlineData("radial-v5/manifest.json", "48630FF7FF3D1C51C8D1C62808CE8B7EB1E8D35109FC5220126025A5B393CFA7")]
    [InlineData("radial-v5/radial-base-v5.png", "FBB0B269DF351FC11357F154281E1F9D9D5ECFC637CF3068DAB3D059E1BD6A84")]
    [InlineData("radial-v5/radial-layout-v5.json", "58C489F4C2463B94FB83E5D8EDFFACA7E178E0BFC6A4F481AEE615FCF06833A5")]
    [InlineData("radial-v5/radial-selected-card-v5.png", "34A73FB156369E7A4F0A8AE6B95B9114ACF4114BB057083596FAECCBDF0CCB62")]
    public void ProductionPackFile_MatchesFrozenSha256(string relativePath, string expectedSha)
    {
        string path = Path.Combine(
            RadialVisualPackContract.DiscoveryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.Equal(expectedSha, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    [Fact]
    public void AlphaFixture_DecodePremultiplyScaleAndComposePreservePArgbContract()
    {
        using var temporary = new RadialVisualPackTestDirectory();
        string directory = temporary.AddPack("alpha", "alpha-fixture", "Alpha Fixture");
        string basePath = Path.Combine(directory, "radial-base-v5.png");
        string selectedPath = Path.Combine(directory, "radial-selected-card-v5.png");
        WriteStraightAlphaFixture(basePath);
        WriteTransparentPng(selectedPath);

        using (var source = new Bitmap(basePath))
        {
            Bgra residue = ReadBgra(source, 450, 450, PixelFormat.Format32bppArgb);
            Assert.Equal((byte)0, residue.A);
            Assert.True(residue.R > 0 || residue.G > 0 || residue.B > 0);
        }

        RadialVisualPackDefinition definition = RadialVisualPackDefinition.Load(directory);
        NormalizedRenderPlan plan = V1VisualPackCompatibilityAdapter.BuildPlan(definition);
        using var cache = new RadialVisualPackCache(plan, targetSize: 627);
        Bitmap master = GetPrivateBitmap(cache, "_baseMaster");

        Assert.Equal(PixelFormat.Format32bppPArgb, master.PixelFormat);
        Assert.Equal(new Bgra(0, 0, 128, 128), ReadBgra(master, 150, 150));
        Assert.Equal(new Bgra(96, 96, 96, 96), ReadBgra(master, 450, 150));
        Assert.Equal(new Bgra(10, 10, 10, 160), ReadBgra(master, 150, 450));
        Assert.Equal(new Bgra(0, 0, 0, 0), ReadBgra(master, 450, 450));

        using var composed = new Bitmap(627, 627, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(composed))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(cache.ScaledBase, 0, 0);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
            graphics.DrawImageUnscaled(cache.GetSelectedSlot(1), 0, 0);
        }

        AssertPArgbPixelsHaveNoBleedOrInversion(cache.ScaledBase);
        AssertPArgbPixelsHaveNoBleedOrInversion(composed);
        Assert.DoesNotContain(ReadAllPixels(composed), pixel => pixel.A == byte.MaxValue);
        Bgra red = ReadBgra(composed, 75, 75);
        Bgra glow = ReadBgra(composed, 225, 75);
        Bgra shadow = ReadBgra(composed, 75, 225);
        Bgra transparent = ReadBgra(composed, 225, 225);
        Assert.True(red.R > red.G && red.R > red.B && red.A > 0);
        Assert.True(glow.R > 0 && glow.R == glow.G && glow.G == glow.B && glow.A > 0);
        Assert.True(shadow.R > 0 && shadow.R == shadow.G && shadow.G == shadow.B && shadow.A > 0);
        Assert.Equal(new Bgra(0, 0, 0, 0), transparent);
    }

    private static RadialVisualPackDefinition LoadProductionPack(string packId) =>
        RadialVisualPackDefinition.Load(Path.Combine(
            RadialVisualPackContract.DiscoveryRoot,
            packId));

    private static Bitmap GetPrivateBitmap(object instance, string fieldName) =>
        Assert.IsType<Bitmap>(instance.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance));

    private static void WriteStraightAlphaFixture(string path)
    {
        using var bitmap = new Bitmap(1254, 1254, PixelFormat.Format32bppArgb);
        WriteBlock(bitmap, new Rectangle(100, 100, 100, 100), new Bgra(0, 0, 255, 128));
        WriteSoftWhiteGlow(bitmap, new Rectangle(400, 100, 101, 101));
        WriteBlock(bitmap, new Rectangle(100, 400, 100, 100), new Bgra(16, 16, 16, 160));
        WriteBlock(bitmap, new Rectangle(400, 400, 100, 100), new Bgra(255, 0, 255, 0));
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void WriteTransparentPng(string path)
    {
        using var bitmap = new Bitmap(1254, 1254, PixelFormat.Format32bppArgb);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void WriteBlock(Bitmap bitmap, Rectangle bounds, Bgra pixel)
    {
        BitmapData data = bitmap.LockBits(
            bounds,
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            byte[] row = new byte[checked(bounds.Width * 4)];
            for (int x = 0; x < bounds.Width; x++)
            {
                int offset = x * 4;
                row[offset] = pixel.B;
                row[offset + 1] = pixel.G;
                row[offset + 2] = pixel.R;
                row[offset + 3] = pixel.A;
            }
            for (int y = 0; y < bounds.Height; y++)
                Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), row.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void WriteSoftWhiteGlow(Bitmap bitmap, Rectangle bounds)
    {
        BitmapData data = bitmap.LockBits(
            bounds,
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            int rowLength = checked(bounds.Width * 4);
            byte[] row = new byte[rowLength];
            double centerX = (bounds.Width - 1) / 2d;
            double centerY = (bounds.Height - 1) / 2d;
            double radius = Math.Min(centerX, centerY);
            for (int y = 0; y < bounds.Height; y++)
            {
                for (int x = 0; x < bounds.Width; x++)
                {
                    double distance = Math.Sqrt(
                        Math.Pow(x - centerX, 2) + Math.Pow(y - centerY, 2));
                    byte alpha = (byte)Math.Round(
                        96d * Math.Max(0d, 1d - (distance / radius)),
                        MidpointRounding.AwayFromZero);
                    int offset = x * 4;
                    row[offset] = byte.MaxValue;
                    row[offset + 1] = byte.MaxValue;
                    row[offset + 2] = byte.MaxValue;
                    row[offset + 3] = alpha;
                }
                Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), rowLength);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static Bgra ReadBgra(
        Bitmap bitmap,
        int x,
        int y,
        PixelFormat pixelFormat = PixelFormat.Format32bppPArgb)
    {
        Rectangle bounds = new(x, y, 1, 1);
        BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, pixelFormat);
        try
        {
            byte[] pixel = new byte[4];
            Marshal.Copy(data.Scan0, pixel, 0, pixel.Length);
            return new Bgra(pixel[0], pixel[1], pixel[2], pixel[3]);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static IReadOnlyList<Bgra> ReadAllPixels(Bitmap bitmap)
    {
        Rectangle bounds = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(
            bounds,
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppPArgb);
        try
        {
            int rowLength = checked(bitmap.Width * 4);
            byte[] row = new byte[rowLength];
            var pixels = new List<Bgra>(checked(bitmap.Width * bitmap.Height));
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, rowLength);
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int offset = x * 4;
                    pixels.Add(new Bgra(
                        row[offset],
                        row[offset + 1],
                        row[offset + 2],
                        row[offset + 3]));
                }
            }
            return pixels;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void AssertPArgbPixelsHaveNoBleedOrInversion(Bitmap bitmap)
    {
        Assert.Equal(PixelFormat.Format32bppPArgb, bitmap.PixelFormat);
        foreach (Bgra pixel in ReadAllPixels(bitmap))
        {
            Assert.True(pixel.B <= pixel.A);
            Assert.True(pixel.G <= pixel.A);
            Assert.True(pixel.R <= pixel.A);
            if (pixel.A == 0)
                Assert.Equal(new Bgra(0, 0, 0, 0), pixel);
        }
    }

    private readonly record struct Bgra(byte B, byte G, byte R, byte A);
}
