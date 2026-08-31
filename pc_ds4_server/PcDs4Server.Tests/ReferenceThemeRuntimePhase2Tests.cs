using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Xunit;
using Xunit.Abstractions;

namespace PcDs4Server.Tests;

public sealed class ReferenceThemeRuntimePhase2Tests
{
    private static readonly int[] Dpis = { 96, 120, 144, 168, 192 };
    private readonly ITestOutputHelper _output;

    public ReferenceThemeRuntimePhase2Tests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Loader_NormalizesOfficialNonSquareCoordinateFixture()
    {
        using var fixture = new V2ThemeFixture(6, 800, 500, 400, 400, 0, 75, 120, 200);
        UiThemeV2Package package = fixture.Load();

        Assert.Equal(2, package.Plan.SourceProtocolVersion);
        Assert.Equal(new NormalizedReferenceCanvas(800, 500, "sRGB"), package.Plan.ReferenceCanvas);
        Assert.Equal(400, package.Plan.ReferenceScale.LogicalWidth);
        Assert.Equal(400, package.Plan.ReferenceScale.LogicalHeight);
        Assert.Equal(new NormalizedPoint(0, 75), package.Plan.ReferenceScale.ContentOrigin);
        Assert.Equal(new NormalizedPoint(120, 200), package.Plan.Placement.ActivationAnchor);
        FullStateFrameRenderPlan model = Assert.IsType<FullStateFrameRenderPlan>(package.Plan.RenderModel);
        Assert.Equal(7, model.States.Count);
        Assert.Equal("selected-6", model.ResolveState(6).Name);
        Assert.Empty(package.Plan.GeometryTransforms);
        Assert.Equal(NormalizedDynamicContentDescriptor.None, package.Plan.DynamicContent.Mode);
    }

    [Fact]
    public void LegacyAdapterDerivesOldLogicalCenterWithoutEmbeddingPhysicalPlacement()
    {
        RadialVisualPackCatalogEntry entry = new RadialVisualPackCatalog().Discover().Find("radial-v5")!;
        Assert.Equal(280, entry.Plan.ReferenceScale.LogicalWidth);
        Assert.Equal(280, entry.Plan.ReferenceScale.LogicalHeight);
        Assert.Equal(140d, entry.Plan.Placement.ActivationAnchor.X, precision: 10);
        Assert.Equal(140d, entry.Plan.Placement.ActivationAnchor.Y, precision: 10);
    }

    [Fact]
    public void LogicalRectangleRoundsEdgesInsteadOfRoundedOriginPlusRoundedWidth()
    {
        Rectangle rectangle = RadialDpiScaling.LogicalRectToPhysical(
            left: 0.4, top: 0.4, right: 1.0, bottom: 1.0, dpi: 120);
        Assert.Equal(Rectangle.FromLTRB(1, 1, 1, 1), rectangle);
        Assert.Equal(1, RadialDpiScaling.LogicalEdgeToPhysical(0.4, 120));
        Assert.Equal(1, RadialDpiScaling.LogicalEdgeToPhysical(1.0, 120));
    }

    [Theory]
    [InlineData("example")]
    [InlineData("version")]
    [InlineData("layered")]
    [InlineData("slot-coherence")]
    public void LoaderRejectsNonProductionProtocolShapes(string mutation)
    {
        using var fixture = new V2ThemeFixture(6, 800, 500, 400, 400, 0, 75, 120, 200);
        fixture.EditManifest(root =>
        {
            if (mutation == "example") root["exampleOnly"] = true;
            if (mutation == "version") root["protocolVersion"] = 3;
            if (mutation == "layered") root["renderStrategy"] = "layered-state";
            if (mutation == "slot-coherence") root["states"]!["selected-1"]!["slotId"] = 2;
        });
        Assert.Throws<InvalidDataException>(fixture.Load);
    }

    [Theory]
    [InlineData(96, 400, 400, 0, 75, 400, 325, 120, 200)]
    [InlineData(120, 500, 500, 0, 94, 500, 406, 150, 250)]
    [InlineData(144, 600, 600, 0, 113, 600, 488, 180, 300)]
    [InlineData(168, 700, 700, 0, 131, 700, 569, 210, 350)]
    [InlineData(192, 800, 800, 0, 150, 800, 650, 240, 400)]
    public void OfficialFixture_UsesEdgeRoundedContentLetterboxAndActivationAnchor(int dpi,
        int surfaceWidth, int surfaceHeight, int contentLeft, int contentTop,
        int contentRight, int contentBottom, int anchorX, int anchorY)
    {
        using var fixture = new V2ThemeFixture(6, 800, 500, 400, 400, 0, 75, 120, 200);
        NormalizedRenderPlan plan = fixture.Load().Plan;
        using var cache = new FullStateFrameCache(plan, dpi);

        Assert.Equal(new Size(surfaceWidth, surfaceHeight), cache.PhysicalSurfaceSize);
        Assert.Equal(new Rectangle(contentLeft, contentTop,
            contentRight - contentLeft, contentBottom - contentTop), fixture.PhysicalContentRect(plan, dpi));
        Assert.Equal(Color.FromArgb(0), cache.GetState(0).GetPixel(surfaceWidth / 2, Math.Max(0, contentTop - 1)));
        Point screen = new(1000, 800);
        Assert.Equal(new Point(1000 - anchorX, 800 - anchorY),
            RadialMenuOverlay.ComputeOverlayTopLeft(screen, plan, dpi, cache.PhysicalSurfaceSize));
    }

    [Theory]
    [MemberData(nameof(DpiAndSlotCounts))]
    public void EveryFullState_IsPrebuiltAtFinalPhysicalSizeAndPArgb(int dpi, int slotCount)
    {
        using var fixture = new V2ThemeFixture(slotCount, 800, 500, 400, 400, 0, 75, 120, 200);
        using var cache = new FullStateFrameCache(fixture.Load().Plan, dpi);

        Assert.Equal(slotCount + 1, cache.StateCount);
        Assert.Equal(slotCount + 2, cache.DecodedAssetCount);
        for (int slot = 0; slot <= slotCount; slot++)
        {
            Bitmap frame = cache.GetState(slot);
            Assert.Equal(cache.PhysicalSurfaceSize, frame.Size);
            Assert.Equal(PixelFormat.Format32bppPArgb, frame.PixelFormat);
            AssertPArgb(frame);
        }
    }

    [Theory]
    [InlineData(96, 6, "57D4CF5831305242636919148A1EF0AB0E8496E000CF6F06A353644F1BA1E0A9")]
    [InlineData(120, 6, "E9CD719F149CF9A1A9E4E4D616A8B03DFDB493D92B975C3229BAB11991149C12")]
    [InlineData(144, 6, "AFCE33D02CA2BD36C034974AEC74D967855AEC6C2C2295C20A2A340AF77B2566")]
    [InlineData(168, 6, "EF9A50CC0020C1AA1250FDA02C98EE5CD71E637E6F3B98C87ED96AB188560BB5")]
    [InlineData(192, 6, "901B85D0E0706D96E69BE786285F42F2427EC2E0CFA15F103792B33031631B26")]
    [InlineData(96, 8, "9ED88DB40586EB321014EA16BDB4784CA0E5CBB0928DB3AD4312FFE013DBEA7F")]
    [InlineData(120, 8, "E4C2E83BF76A8231693F03E359276436B9136BAC92C8024AA9E3B50459CB4164")]
    [InlineData(144, 8, "F505B2D086CDC5F3CCF59656053405A35A0E37E8849DFF47548983C1652CC59D")]
    [InlineData(168, 8, "C902436A535FE2494AB76EBD2222FA13A689B801E06868261F3B7FECDEE8D49F")]
    [InlineData(192, 8, "8085957C925CA9B3D05466AE788E03CD5E061EA7D2EB7173DC13949DFBDE2771")]
    public void RawPixelGolden_RecordsEveryState(int dpi, int slotCount, string expectedMatrixSha)
    {
        using var fixture = new V2ThemeFixture(slotCount, 800, 500, 400, 400, 0, 75, 120, 200);
        using var cache = new FullStateFrameCache(fixture.Load().Plan, dpi);
        string[] records = Enumerable.Range(0, slotCount + 1).Select(slot =>
        {
            Bitmap bitmap = cache.GetState(slot);
            return $"{slot}|{bitmap.Width}x{bitmap.Height}|{bitmap.PixelFormat}|{RawPixelSha(bitmap)}";
        }).ToArray();
        string matrixSha = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            string.Join("\n", records))));
        Assert.True(string.Equals(expectedMatrixSha, matrixSha, StringComparison.Ordinal),
            $"dpi={dpi}; slots={slotCount}; matrix={matrixSha}\n{string.Join("\n", records)}");
    }

    [Fact]
    public void SelectedFrame_CanChangeTheWholeScene()
    {
        using var fixture = new V2ThemeFixture(6, 800, 500, 400, 400, 0, 75, 120, 200);
        using var cache = new FullStateFrameCache(fixture.Load().Plan, 96);
        Bitmap idle = cache.GetState(0);
        Bitmap selected = cache.GetState(1);
        foreach (Point point in new[] { new Point(1, 76), new Point(200, 200), new Point(398, 323) })
            Assert.NotEqual(idle.GetPixel(point.X, point.Y), selected.GetPixel(point.X, point.Y));
    }

    [Fact]
    public void NonCenteredFixture_KeepsArtworkSurfaceAndActivationIndependent()
    {
        using var fixture = new V2ThemeFixture(6, 800, 500, 500, 300, 20, 0, 100, 150);
        NormalizedRenderPlan plan = fixture.Load().Plan;
        using var cache = new FullStateFrameCache(plan, 144);

        Assert.Equal(new Size(750, 450), cache.PhysicalSurfaceSize);
        Assert.Equal(new Rectangle(30, 0, 720, 450), fixture.PhysicalContentRect(plan, 144));
        Assert.Equal(new Point(850, 575), RadialMenuOverlay.ComputeOverlayTopLeft(
            new Point(1000, 800), plan, 144, cache.PhysicalSurfaceSize));
        Assert.Equal(0, cache.GetState(0).GetPixel(0, 200).A);
        Assert.NotEqual(0, cache.GetState(0).GetPixel(31, 200).A);
    }

    [Fact]
    public void SameZIndex_UsesDeclarationOrder()
    {
        using var fixture = new V2ThemeFixture(6, 800, 500, 400, 400, 0, 75, 120, 200,
            addTieBreakLayer: true);
        using var cache = new FullStateFrameCache(fixture.Load().Plan, 96);
        Color pixel = cache.GetState(0).GetPixel(200, 200);

        Assert.Equal(new[] { "background", "tie", "stateFrame" },
            Assert.IsType<FullStateFrameRenderPlan>(fixture.Load().Plan.RenderModel)
                .OrderedLayers.Select(x => x.Id));
        Assert.True(pixel.B > pixel.R, $"Expected later blue layer over red; actual={pixel}");
    }

    [Fact]
    public void BadSha_IsRejectedBeforeAnyPngDecode()
    {
        using var fixture = new V2ThemeFixture(6, 800, 500, 400, 400, 0, 75, 120, 200);
        fixture.EditManifest(root => root["assetHashes"]!["static.png"] = new string('0', 64));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
        {
            UiThemeV2Package package = fixture.Load();
            using var cache = new FullStateFrameCache(package.Plan, 96, fixture.Decoder);
        });

        Assert.Contains("SHA-256 mismatch", error.Message);
        Assert.Equal(0, fixture.Decoder.DecodeCount);
    }

    [Theory]
    [InlineData("unsupported-capability")]
    [InlineData("missing-state")]
    [InlineData("future-state")]
    [InlineData("dynamic-layer")]
    public void UnsupportedOrIncompleteCandidatesAreRejected(string mutation)
    {
        using var fixture = new V2ThemeFixture(6, 800, 500, 400, 400, 0, 75, 120, 200);
        fixture.EditManifest(root =>
        {
            switch (mutation)
            {
                case "unsupported-capability":
                    root["capabilities"]!["required"]!.AsArray().Add("dynamicAnchors");
                    break;
                case "missing-state":
                    root["states"]!.AsObject().Remove("selected-6");
                    break;
                case "future-state":
                    root["layers"]![0]!["visibleStates"] = new JsonArray("pressed-1");
                    break;
                case "dynamic-layer":
                    root["layers"]![0]!["kind"] = "dynamicText";
                    root["layers"]![0]!.AsObject().Remove("asset");
                    break;
            }
        });
        Assert.Throws<InvalidDataException>(fixture.Load);
    }

    [Fact]
    public void CorrectHashButInvalidPng_RejectsCandidateAndRetainsActiveV1()
    {
        using var fixture = new V2ThemeFixture(6, 800, 500, 400, 400, 0, 75, 120, 200);
        fixture.ReplaceAsset("selected-1.png", "not a png"u8.ToArray());
        using var runtime = new RadialVisualPackRuntime(fixture.Catalog, RadialRenderPolicy.Legacy);
        RadialVisualPackSession v1 = runtime.Ensure(RadialMenuSettings.SafeFallback, 280, 96)!;

        RadialVisualPackSession retained = runtime.Ensure(fixture.Settings, 280, 96)!;

        Assert.Same(v1, retained);
        Assert.Equal("radial-v5", runtime.ActivePackId);
        Assert.False(v1.Bundle.IsDisposed);
        Assert.NotEmpty(runtime.LastError!);
    }

    [Fact]
    public void AtomicV1V2V1Switch_PublishesCompleteBundleBeforeDisposingPrevious()
    {
        using var fixture = new V2ThemeFixture(6, 800, 500, 400, 400, 0, 75, 120, 200);
        using var runtime = new RadialVisualPackRuntime(fixture.Catalog, RadialRenderPolicy.Legacy);
        RadialVisualPackSession v1 = runtime.Ensure(RadialMenuSettings.SafeFallback, 280, 96)!;

        RadialVisualPackSession v2 = runtime.Ensure(fixture.Settings, 280, 96)!;
        Assert.True(v2.Bundle.IsFullStateFrame);
        Assert.Equal(7, v2.Bundle.FullStateCache.StateCount);
        Assert.True(v1.Bundle.IsDisposed);

        RadialVisualPackSession v1Again = runtime.Ensure(RadialMenuSettings.SafeFallback, 280, 96)!;
        Assert.False(v1Again.Bundle.IsFullStateFrame);
        Assert.True(v2.Bundle.IsDisposed);
    }

    [Theory]
    [InlineData("bad-sha")]
    [InlineData("missing-state")]
    [InlineData("invalid-png")]
    [InlineData("unsupported-capability")]
    public void BadV2CandidateRetainsActiveV2(string mutation)
    {
        using var fixture = new V2ThemeFixture(6, 800, 500, 400, 400, 0, 75, 120, 200);
        using var runtime = new RadialVisualPackRuntime(fixture.Catalog, RadialRenderPolicy.Legacy);
        RadialVisualPackSession active = runtime.Ensure(fixture.Settings, 280, 96)!;
        RuntimeRenderBundle bundle = active.Bundle;
        NormalizedRenderPlan plan = active.Plan;
        RadialSlotMappings mappings = active.Mappings;
        RadialMenuSettings bad = fixture.AddBadCandidate(mutation);

        RadialVisualPackSession retained = runtime.Ensure(bad, 280, 96)!;

        Assert.Same(active, retained);
        Assert.Same(bundle, retained.Bundle);
        Assert.Same(plan, retained.Plan);
        Assert.Same(mappings, retained.Mappings);
        Assert.False(bundle.IsDisposed);
    }

    [Fact]
    public void V2DpiRebuild_IsAtomicAndRetainsPlan()
    {
        using var fixture = new V2ThemeFixture(6, 800, 500, 400, 400, 0, 75, 120, 200);
        RadialVisualPackCatalogEntry entry = fixture.Catalog.Discover().Find(fixture.Id)!;
        using var session = new RadialVisualPackSession(entry, fixture.Settings, 280, 96);
        NormalizedRenderPlan plan = session.Plan;
        RuntimeRenderBundle original = session.Bundle;

        session.EnsureContent(fixture.Settings, 420, 144);

        Assert.Same(plan, session.Plan);
        Assert.Equal(new Size(600, 600), session.Bundle.PhysicalSurfaceSize);
        Assert.True(original.IsDisposed);
    }

    [Fact]
    public void FailedV2DpiRebuild_RetainsOldCompleteBundle()
    {
        using var fixture = new V2ThemeFixture(6, 800, 500, 400, 400, 0, 75, 120, 200);
        RadialVisualPackCatalogEntry entry = fixture.Catalog.Discover().Find(fixture.Id)!;
        RuntimeRenderBundle Builder(NormalizedRenderPlan plan, RadialMenuSettings settings,
            int targetSize, int dpi)
        {
            if (dpi == 144) throw new InvalidDataException("Synthetic V2 DPI failure.");
            return RuntimeRenderBundle.Build(plan, settings, targetSize, dpi);
        }
        using var session = new RadialVisualPackSession(entry, fixture.Settings, 280, 96, Builder);
        RuntimeRenderBundle original = session.Bundle;

        Assert.Throws<InvalidDataException>(() => session.EnsureContent(fixture.Settings, 420, 144));

        Assert.Same(original, session.Bundle);
        Assert.False(original.IsDisposed);
        Assert.Equal(96, original.Dpi);
        Assert.Equal(7, original.FullStateCache.StateCount);
    }

    [Fact]
    public void V2ResourcesDoubleDisposeSafelyAndHotPathDoesNotDecode()
    {
        using var fixture = new V2ThemeFixture(6, 800, 500, 400, 400, 0, 75, 120, 200);
        NormalizedRenderPlan plan = fixture.Load().Plan;
        var cache = new FullStateFrameCache(plan, 96, fixture.Decoder);
        int decoded = fixture.Decoder.DecodeCount;
        for (int pass = 0; pass < 100; pass++)
            for (int slot = 0; slot <= 6; slot++) _ = cache.GetState(slot);
        Assert.Equal(decoded, fixture.Decoder.DecodeCount);
        cache.Dispose();
        cache.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cache.GetState(0));
    }

    [Fact]
    public void PerformanceProbe_RecordsBundleBuildSwitchCopyAndRetainedMemory()
    {
        foreach (int slots in new[] { 6, 8 })
        {
            using var fixture = new V2ThemeFixture(slots, 800, 500, 400, 400, 0, 75, 120, 200);
            NormalizedRenderPlan plan = fixture.Load().Plan;
            long before = GC.GetTotalMemory(true);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using var cache = new FullStateFrameCache(plan, 144);
            watch.Stop();
            long retained = Math.Max(0, GC.GetTotalMemory(false) - before);
            using var target = new Bitmap(cache.PhysicalSurfaceSize.Width, cache.PhysicalSurfaceSize.Height,
                PixelFormat.Format32bppPArgb);
            using Graphics graphics = Graphics.FromImage(target);
            var copies = System.Diagnostics.Stopwatch.StartNew();
            for (int iteration = 0; iteration < 100; iteration++)
                graphics.DrawImageUnscaled(cache.GetState(iteration % (slots + 1)), 0, 0);
            copies.Stop();
            _output.WriteLine($"V2 radial-{slots}: bundle={watch.Elapsed.TotalMilliseconds:F3}ms; " +
                $"stateCopy100={copies.Elapsed.TotalMilliseconds:F3}ms; retained={retained} bytes");
            Assert.True(watch.Elapsed > TimeSpan.Zero);
            Assert.True(copies.Elapsed > TimeSpan.Zero);
        }
    }

    [Theory]
    [InlineData(6, 0)]
    [InlineData(6, 1)]
    [InlineData(8, 0)]
    [InlineData(8, 8)]
    public void RealHwndGate_PresentsV2AtActivationAnchor(int slotCount, int selectedSlot)
    {
        using var fixture = new V2ThemeFixture(slotCount, 800, 500, 500, 300, 20, 0, 100, 150);
        Exception? failure = null;
        int observedDpi = 0;
        Point expectedLocation = Point.Empty;
        Point actualLocation = Point.Empty;
        var thread = new Thread(() =>
        {
            try
            {
                using var overlay = new RadialMenuOverlay(RadialRenderPolicy.Legacy, fixture.Catalog);
                Point activation = new(700, 500);
                overlay.ShowAt(activation, fixture.Settings, selectedSlot);
                int dpi = overlay.ActiveDpi;
                observedDpi = dpi;
                Assert.True(overlay.IsHandleCreated);
                Assert.True(overlay.IsVisible);
                Assert.Equal(RadialDpiScaling.LogicalSizeToPhysical(500, 300, dpi), overlay.ClientSize);
                expectedLocation = new Point(
                    activation.X - RadialDpiScaling.LogicalEdgeToPhysical(100, dpi),
                    activation.Y - RadialDpiScaling.LogicalEdgeToPhysical(150, dpi));
                actualLocation = overlay.Location;
                Assert.Equal(expectedLocation, actualLocation);
                overlay.Hide();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Real HWND gate timed out.");
        Assert.Null(failure);
        _output.WriteLine($"radial-{slotCount} slot={selectedSlot}; requested=700,500; dpi={observedDpi}; " +
            $"expected={expectedLocation.X},{expectedLocation.Y}; actual={actualLocation.X},{actualLocation.Y}");
    }

    [Fact]
    public void CatalogRejectsV1CollisionWithoutShadowingV1()
    {
        using var fixture = new V2ThemeFixture(6, 800, 500, 400, 400, 0, 75, 120, 200);
        fixture.EditManifest(root => root["id"] = "radial-v5");

        RadialVisualPackCatalogSnapshot snapshot = fixture.Catalog.Discover();

        Assert.Equal(2, snapshot.Packs.Count);
        Assert.Equal(1, snapshot.Find("radial-v5")!.Version);
        Assert.Contains(snapshot.Issues, x => x.Message.Contains("collides", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../escape.png")]
    [InlineData("C:/escape.png")]
    [InlineData("file://escape.png")]
    [InlineData("folder\\escape.png")]
    [InlineData("~secret.png")]
    public void UnsafeAssetPathsAreRejected(string path)
    {
        using var fixture = new V2ThemeFixture(6, 800, 500, 400, 400, 0, 75, 120, 200);
        fixture.EditManifest(root => root["layers"]![0]!["asset"] = path);
        Assert.Throws<InvalidDataException>(fixture.Load);
    }

    public static IEnumerable<object[]> DpiAndSlotCounts() =>
        from dpi in Dpis from slots in new[] { 6, 8 } select new object[] { dpi, slots };

    private static void AssertPArgb(Bitmap bitmap)
    {
        Rectangle bounds = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            byte[] row = new byte[bitmap.Width * 4];
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                for (int x = 0; x < row.Length; x += 4)
                {
                    Assert.True(row[x] <= row[x + 3]);
                    Assert.True(row[x + 1] <= row[x + 3]);
                    Assert.True(row[x + 2] <= row[x + 3]);
                    if (row[x + 3] == 0) Assert.Equal(new byte[] { 0, 0, 0 }, row[x..(x + 3)]);
                }
            }
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static string RawPixelSha(Bitmap bitmap)
    {
        Rectangle bounds = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            byte[] bytes = new byte[checked(bitmap.Width * bitmap.Height * 4)];
            int rowLength = bitmap.Width * 4;
            for (int y = 0; y < bitmap.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bytes, y * rowLength, rowLength);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally { bitmap.UnlockBits(data); }
    }

    private sealed class CountingDecoder : IThemeAssetDecoder
    {
        public int DecodeCount { get; private set; }
        public Bitmap Decode(VerifiedThemeAsset asset)
        { DecodeCount++; return new GdiThemeAssetDecoder().Decode(asset); }
    }

    private sealed class V2ThemeFixture : IDisposable
    {
        private readonly RadialVisualPackTestDirectory _v1 = new();
        private readonly string _manifestPath;
        private readonly int _referenceWidth;
        private readonly int _referenceHeight;

        public V2ThemeFixture(int slots, int referenceWidth, int referenceHeight,
            double logicalWidth, double logicalHeight, double originX, double originY,
            double anchorX, double anchorY, bool addTieBreakLayer = false)
        {
            _referenceWidth = referenceWidth;
            _referenceHeight = referenceHeight;
            _v1.AddPack("default", "radial-v5", "Tactical HUD V5");
            _v1.AddRadial8Pack();
            ThemeRoot = Path.Combine(Path.GetTempPath(), "LeftPad.Tests", Guid.NewGuid().ToString("N"));
            PackageRoot = Path.Combine(ThemeRoot, "synthetic-v2");
            Directory.CreateDirectory(PackageRoot);
            Id = $"synthetic-radial-{slots}";
            string profile = $"radial-{slots}";
            WritePng("static.png", Color.FromArgb(180, 200, 20, 10));
            if (addTieBreakLayer) WritePng("tie.png", Color.FromArgb(180, 10, 20, 220));
            WritePng("idle.png", Color.FromArgb(120, 10, 30, 60));
            for (int slot = 1; slot <= slots; slot++)
                WritePng($"selected-{slot}.png", Color.FromArgb(120,
                    20 + slot * 12, 180 - slot * 8, 30 + slot * 9));

            var layers = new JsonArray(Layer("background", "staticAsset", 0, "STATIC", "static.png"));
            if (addTieBreakLayer) layers.Add(Layer("tie", "staticAsset", 0, "STATIC", "tie.png"));
            layers.Add(Layer("stateFrame", "stateAsset", 10, "STATE_ASSET", null));
            var states = new JsonObject { ["idle"] = State(null, "idle.png") };
            for (int slot = 1; slot <= slots; slot++) states[$"selected-{slot}"] = State(slot, $"selected-{slot}.png");
            var ownership = new JsonArray(Ownership("background", "STATIC"));
            if (addTieBreakLayer) ownership.Add(Ownership("tie", "STATIC"));
            ownership.Add(Ownership("stateFrame", "STATE_ASSET"));
            var root = new JsonObject
            {
                ["protocolVersion"] = 2, ["packageRevision"] = 1, ["id"] = Id,
                ["name"] = "Synthetic Full State", ["surface"] = "radial-overlay",
                ["renderStrategy"] = "full-state-frame", ["layoutProfile"] = profile,
                ["compatibleLayouts"] = new JsonArray(profile),
                ["referenceCanvas"] = new JsonObject { ["width"] = referenceWidth, ["height"] = referenceHeight,
                    ["colorSpace"] = "sRGB", ["alphaMode"] = "straight" },
                ["referenceScale"] = new JsonObject { ["logicalWidth"] = logicalWidth, ["logicalHeight"] = logicalHeight,
                    ["fit"] = "contain", ["contentOrigin"] = new JsonObject { ["x"] = originX, ["y"] = originY } },
                ["placement"] = new JsonObject { ["activationAnchor"] = new JsonObject { ["x"] = anchorX, ["y"] = anchorY } },
                ["states"] = states, ["layers"] = layers, ["dynamicAnchors"] = new JsonArray(),
                ["styles"] = ValidStyles(), ["glyphs"] = ValidGlyphs(), ["elementOwnership"] = ownership,
                ["masks"] = new JsonArray(), ["visualRegions"] = new JsonArray(),
                ["fallback"] = new JsonObject { ["onInvalidCandidate"] = "retain-active",
                    ["onUnsupportedVersion"] = "retain-active", ["startupThemeId"] = "radial-v5" },
                ["capabilities"] = new JsonObject { ["required"] = new JsonArray("fullStateFrame", "instantTransitions"),
                    ["optional"] = new JsonArray() },
                ["transitions"] = new JsonObject { ["mode"] = "instant" }, ["assetHashes"] = new JsonObject()
            };
            _manifestPath = Path.Combine(PackageRoot, "manifest.json");
            WriteManifest(root);
            Catalog = new(_v1.Root, ThemeRoot);
            Settings = RadialMenuSettings.Default with { VisualPackId = Id, MappingProfileId = profile };
        }

        public string Id { get; }
        public string ThemeRoot { get; }
        public string PackageRoot { get; }
        public RadialVisualPackCatalog Catalog { get; }
        public RadialMenuSettings Settings { get; }
        public CountingDecoder Decoder { get; } = new();
        public UiThemeV2Package Load()
        {
            LayoutDefinition layout = Catalog.Discover().Packs.First(x =>
                !x.IsV2 && x.LayoutDefinition.ProfileId == Settings.MappingProfileId).LayoutDefinition;
            return UiThemeV2Loader.Load(PackageRoot, layout);
        }

        public Rectangle PhysicalContentRect(NormalizedRenderPlan plan, int dpi)
        {
            double factor = Math.Min(plan.ReferenceScale.LogicalWidth / _referenceWidth,
                plan.ReferenceScale.LogicalHeight / _referenceHeight);
            return RadialDpiScaling.LogicalRectToPhysical(plan.ReferenceScale.ContentOrigin.X,
                plan.ReferenceScale.ContentOrigin.Y,
                plan.ReferenceScale.ContentOrigin.X + _referenceWidth * factor,
                plan.ReferenceScale.ContentOrigin.Y + _referenceHeight * factor, dpi);
        }

        public void EditManifest(Action<JsonObject> edit)
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(_manifestPath))!.AsObject();
            edit(root);
            WriteManifest(root, refreshHashes: false);
        }

        public void ReplaceAsset(string name, byte[] bytes)
        {
            File.WriteAllBytes(Path.Combine(PackageRoot, name), bytes);
            JsonObject root = JsonNode.Parse(File.ReadAllText(_manifestPath))!.AsObject();
            WriteManifest(root);
        }

        public RadialMenuSettings AddBadCandidate(string mutation)
        {
            string badId = $"bad-{mutation}";
            string destination = Path.Combine(ThemeRoot, badId);
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(PackageRoot))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            string manifestPath = Path.Combine(destination, "manifest.json");
            JsonObject root = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            root["id"] = badId;
            if (mutation == "bad-sha") root["assetHashes"]!["static.png"] = new string('0', 64);
            if (mutation == "missing-state") root["states"]!.AsObject().Remove("selected-6");
            if (mutation == "unsupported-capability")
                root["capabilities"]!["required"]!.AsArray().Add("dynamicAnchors");
            if (mutation == "invalid-png")
            {
                byte[] invalid = "not a png"u8.ToArray();
                File.WriteAllBytes(Path.Combine(destination, "selected-1.png"), invalid);
                root["assetHashes"]!["selected-1.png"] = Convert.ToHexString(SHA256.HashData(invalid));
            }
            File.WriteAllText(manifestPath, root.ToJsonString(new JsonSerializerOptions
            { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() }));
            return Settings with { VisualPackId = badId };
        }

        public void Dispose()
        {
            _v1.Dispose();
            if (Directory.Exists(ThemeRoot)) Directory.Delete(ThemeRoot, true);
        }

        private JsonObject Layer(string id, string kind, int z, string owner, string? asset)
        {
            var value = new JsonObject { ["id"] = id, ["kind"] = kind, ["zIndex"] = z,
                ["bounds"] = new JsonObject { ["x"] = 0, ["y"] = 0,
                    ["width"] = _referenceWidth, ["height"] = _referenceHeight },
                ["visibleStates"] = new JsonArray("all"), ["ownership"] = owner, ["required"] = true };
            if (asset != null) value["asset"] = asset;
            return value;
        }
        private static JsonObject State(int? slot, string asset) => new()
        { ["slotId"] = slot == null ? null : JsonValue.Create(slot),
            ["assets"] = new JsonObject { ["stateFrame"] = asset } };
        private static JsonObject Ownership(string layer, string owner) => new()
        { ["element"] = $"{layer}Element", ["owner"] = owner, ["layerId"] = layer };
        private static JsonObject ValidStyles() => new()
        { ["fontRoles"] = new JsonObject { ["interface"] = "ui" },
            ["colorRoles"] = new JsonObject { ["white"] = "#FFFFFFFF" },
            ["outlineRoles"] = new JsonObject(), ["shadowRoles"] = new JsonObject(),
            ["dynamicRoles"] = new JsonObject { ["unused"] = new JsonObject
                { ["fontRole"] = "interface", ["colorRole"] = "white", ["size"] = 12 } } };
        private static JsonObject ValidGlyphs()
        {
            JsonObject family() => new() { ["sources"] = new JsonArray(new JsonObject
                { ["type"] = "text", ["styleRole"] = "unused" }) };
            return new() { ["roles"] = new JsonObject { ["primary"] = new JsonObject
                { ["keyboard"] = family(), ["keyboardShortcut"] = family(), ["ds4"] = family(),
                    ["genericAction"] = family() } } };
        }
        private void WritePng(string name, Color color)
        {
            using var bitmap = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(color);
            bitmap.Save(Path.Combine(PackageRoot, name), ImageFormat.Png);
        }
        private void WriteManifest(JsonObject root, bool refreshHashes = true)
        {
            if (refreshHashes)
            {
                var hashes = new JsonObject();
                foreach (string file in Directory.GetFiles(PackageRoot, "*.png"))
                    hashes[Path.GetFileName(file)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
                root["assetHashes"] = hashes;
            }
            File.WriteAllText(_manifestPath, root.ToJsonString(new JsonSerializerOptions
            { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() }));
        }
    }
}
