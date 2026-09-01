using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Xunit;
using Xunit.Abstractions;

namespace PcDs4Server.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UniversalRadialPhase3Collection
{
    public const string Name = "Universal Radial Phase 3";
}

[Collection(UniversalRadialPhase3Collection.Name)]
public sealed class UniversalRadialPhase3SelectedEmphasisTests
{
    private const string RadialV5 = "radial-v5";
    private const string Radial8Minimal = "radial-8-minimal-v1";
    private const string DarkFantasy = HistoricalV2ThemeCatalog.ReferenceThemeId;
    private readonly ITestOutputHelper _output;

    public UniversalRadialPhase3SelectedEmphasisTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string> FormalThemes => new()
    {
        RadialV5,
        Radial8Minimal,
        DarkFantasy
    };

    public static TheoryData<string, int[]> ReviewSlots => new()
    {
        { RadialV5, new[] { 1, 3, 4, 6 } },
        { Radial8Minimal, new[] { 1, 3, 5, 8 } },
        { DarkFantasy, new[] { 1, 2, 3, 5, 6, 8 } }
    };

    [Theory]
    [MemberData(nameof(FormalThemes))]
    public void DescriptorOwnsExplicitHashedBaseSelectedAndBinaryMasks(string themeId)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        UniversalSelectedEmphasisDescriptor descriptor =
            UniversalSelectedEmphasisCatalog.LoadForPlan(entry.Plan);

        Assert.Equal(themeId, descriptor.ThemeId);
        Assert.Equal(entry.Plan.SourceProtocolVersion, descriptor.SourceProtocolVersion);
        Assert.Equal(entry.Plan.SourcePackageRevision, descriptor.SourcePackageRevision);
        Assert.Equal(entry.LayoutDefinition.SlotCount, descriptor.SlotCount);
        Assert.Equal(entry.Plan.ReferenceCanvas, descriptor.ReferenceCanvas);
        Assert.Equal(64, descriptor.ManifestSha256.Length);
        Assert.Equal("identity/base-static.png", descriptor.BaseStaticSource.PackagePath);
        Assert.Equal(64, descriptor.BaseStaticSource.Sha256.Length);
        Assert.Equal("identity/base-static.pargbz", descriptor.BaseStaticSource.PArgbPackagePath);
        Assert.Equal(64, descriptor.BaseStaticSource.PArgbSha256!.Length);
        Assert.True(descriptor.BaseStaticSource.PArgbContent.HasValue);
        Assert.Equal(
            Enumerable.Range(1, descriptor.SlotCount),
            descriptor.Slots.Select(x => x.SlotId));
        foreach (UniversalSelectedEmphasisSlotDescriptor slot in descriptor.Slots)
        {
            Assert.Equal($"selected/selected-{slot.SlotId}-source.png", slot.FullSelectedSource.PackagePath);
            Assert.Equal($"selected/selected-{slot.SlotId}-mask.png", slot.ExplicitMask.PackagePath);
            Assert.Equal(64, slot.FullSelectedSource.Sha256.Length);
            Assert.Equal(64, slot.ExplicitMask.Sha256.Length);
            Assert.Equal(
                $"selected/selected-{slot.SlotId}-source.pargbz",
                slot.FullSelectedSource.PArgbPackagePath);
            Assert.Equal(64, slot.FullSelectedSource.PArgbSha256!.Length);
            Assert.True(slot.FullSelectedSource.PArgbContent.HasValue);
            Assert.False(slot.ExplicitMask.PArgbContent.HasValue);
            using Stream stream = slot.ExplicitMask.OpenRead();
            using var mask = new Bitmap(stream);
            Assert.Equal(new Size(1254, 1254), mask.Size);
            AssertBinaryOpaqueMask(mask);
        }
    }

    [Theory]
    [MemberData(nameof(FormalThemes))]
    public void SemanticSourceEndpointsAreRawPixelExactWithFrozenStaticAuthority(string themeId)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId, SetAMappings(entry.LayoutDefinition.SlotCount));
        using var semantic = new UniversalSelectedEmphasisCache(
            UniversalSelectedEmphasisCatalog.LoadForPlan(entry.Plan),
            UniversalRadialRenderPlan.Create(entry.Plan, Parameters(settings, 128)),
            96);
        using Bitmap expectedBase = FrozenStatic(entry, 0);

        for (int slot = 1; slot <= entry.LayoutDefinition.SlotCount; slot++)
        {
            using Bitmap zero = semantic.BuildSourceArtwork(slot, 0);
            using Bitmap full = semantic.BuildSourceArtwork(slot, 255);
            using Bitmap expectedSelected = FrozenStatic(entry, slot);
            Assert.Equal(RawPixelSha(expectedBase), RawPixelSha(zero));
            Assert.Equal(RawPixelSha(expectedSelected), RawPixelSha(full));
        }
    }

    [Theory]
    [MemberData(nameof(ReviewSlots))]
    public void Strength255128And0PreserveIdleAndProduceDistinctSelectedStates(
        string themeId,
        int[] slots)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId, SetAMappings(entry.LayoutDefinition.SlotCount));
        using RuntimeRenderBundle bundle = Build(entry, settings, Parameters(settings, 255));
        string idle255 = RawPixelSha(bundle.GetFinalState(0));
        Dictionary<int, string> full = slots.ToDictionary(slot => slot, slot => RawPixelSha(bundle.GetFinalState(slot)));

        Assert.True(bundle.EnsureUniversalContent(settings, Parameters(settings, 128)));
        string idle128 = RawPixelSha(bundle.GetFinalState(0));
        Dictionary<int, string> half = slots.ToDictionary(slot => slot, slot => RawPixelSha(bundle.GetFinalState(slot)));

        Assert.True(bundle.EnsureUniversalContent(settings, Parameters(settings, 0)));
        string idle0 = RawPixelSha(bundle.GetFinalState(0));
        Dictionary<int, string> zero = slots.ToDictionary(slot => slot, slot => RawPixelSha(bundle.GetFinalState(slot)));

        Assert.Equal(idle255, idle128);
        Assert.Equal(idle255, idle0);
        foreach (int slot in slots)
        {
            Assert.NotEqual(full[slot], half[slot]);
            Assert.NotEqual(half[slot], zero[slot]);
            Assert.NotEqual(full[slot], zero[slot]);
        }
        if (themeId == DarkFantasy)
            Assert.NotEqual(idle0, zero[slots[0]]); // selected-center dynamic content remains present.
    }

    [Theory]
    [MemberData(nameof(FormalThemes))]
    public void MaskOwnedPixelsUseFrozenChannelWisePArgbInterpolation(string themeId)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId, SetAMappings(entry.LayoutDefinition.SlotCount));
        using var semantic = new UniversalSelectedEmphasisCache(
            UniversalSelectedEmphasisCatalog.LoadForPlan(entry.Plan),
            UniversalRadialRenderPlan.Create(entry.Plan, Parameters(settings, 128)),
            96);
        UniversalSelectedEmphasisSlotDescriptor descriptor = semantic.Descriptor.GetSlot(1);
        Point support = FirstMaskPixel(descriptor.ExplicitMask);
        using Bitmap baseArtwork = semantic.BuildSourceArtwork(1, 0);
        using Bitmap selectedArtwork = semantic.BuildSourceArtwork(1, 255);
        byte[] basePixel = PArgbPixel(baseArtwork, support);
        byte[] selectedPixel = PArgbPixel(selectedArtwork, support);
        Assert.NotEqual(basePixel, selectedPixel);

        foreach (byte strength in new byte[] { 0, 64, 128, 192, 255 })
        {
            using Bitmap actualArtwork = semantic.BuildSourceArtwork(1, strength);
            byte[] actual = PArgbPixel(actualArtwork, support);
            for (int channel = 0; channel < 4; channel++)
            {
                int expected = (basePixel[channel] * (255 - strength) +
                    selectedPixel[channel] * strength + 127) / 255;
                Assert.Equal((byte)expected, actual[channel]);
            }
        }
    }

    [Theory]
    [MemberData(nameof(ReviewSlots))]
    public void IntermediateStaticArtworkIsPArgbAndDistinctFromBothEndpoints(
        string themeId,
        int[] slots)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId, SetAMappings(entry.LayoutDefinition.SlotCount));
        using RuntimeRenderBundle bundle = Build(entry, settings, Parameters(settings, 128));
        UniversalSelectedEmphasisCache semantic = SemanticCache(bundle);
        Assert.True(semantic.HasIntermediateArtwork);

        foreach (int slot in slots)
        {
            Bitmap half = semantic.GetIntermediateSelected(slot);
            Assert.Equal(PixelFormat.Format32bppPArgb, half.PixelFormat);
            AssertPArgbInvariant(half);
            using Bitmap zero = semantic.BuildSourceArtwork(slot, 0);
            using Bitmap full = semantic.BuildSourceArtwork(slot, 255);
            using Bitmap midpoint = semantic.BuildSourceArtwork(slot, 128);
            Assert.NotEqual(RawPixelSha(zero), RawPixelSha(midpoint));
            Assert.NotEqual(RawPixelSha(full), RawPixelSha(midpoint));
        }
    }

    [Fact]
    public void DarkFantasySelectedDynamicGlyphAndLabelAreIndependentOfHighlightStrength()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = ThemeSettings(DarkFantasy, SetAMappings(8));
        foreach (string contentKey in new[] { "selectedActionGlyph", "selectedActionLabel" })
        {
            using Bitmap full = RenderDynamic(entry.Plan, settings, contentKey, Parameters(settings, 255));
            using Bitmap half = RenderDynamic(entry.Plan, settings, contentKey, Parameters(settings, 128));
            using Bitmap zero = RenderDynamic(entry.Plan, settings, contentKey, Parameters(settings, 0));
            Assert.Equal(RawPixelSha(full), RawPixelSha(half));
            Assert.Equal(RawPixelSha(full), RawPixelSha(zero));
        }
    }

    [Theory]
    [InlineData(RadialV5)]
    [InlineData(Radial8Minimal)]
    public void V1HighlightOnlyRebuildDoesNotRebuildOrAttenuateDynamicContent(string themeId)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId, SetAMappings(entry.LayoutDefinition.SlotCount));
        using RuntimeRenderBundle bundle = Build(entry, settings, Parameters(settings, 255));
        int dynamicBuilds = bundle.DynamicContent.BuildCount;
        string dynamic = RawPixelSha(bundle.DynamicContent.Content);
        Assert.Equal(0, bundle.SelectedEmphasisManifestReadCount);
        Assert.Equal(0, bundle.SelectedEmphasisDecodedAssetCount);
        Assert.False(bundle.HasSelectedEmphasisCache);

        Assert.True(bundle.EnsureUniversalContent(settings, Parameters(settings, 128)));

        Assert.Equal(dynamicBuilds, bundle.DynamicContent.BuildCount);
        Assert.Equal(dynamic, RawPixelSha(bundle.DynamicContent.Content));
        Assert.Equal(1, bundle.SelectedEmphasisManifestReadCount);
        Assert.Equal(
            1 + entry.LayoutDefinition.SlotCount * 2,
            bundle.SelectedEmphasisDecodedAssetCount);
        Assert.Equal(1, bundle.SelectedEmphasisCache.ArtworkBuildCount);
    }

    [Fact]
    public void DarkFantasyHighlightOnlyRebuildReusesDynamicAuthoredAndIdleCaches()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = ThemeSettings(DarkFantasy, SetAMappings(8));
        using RuntimeRenderBundle bundle = Build(entry, settings, Parameters(settings, 255));
        Bitmap idle = bundle.GetFinalState(0);
        int authoredDecoded = bundle.FullStateCache.DecodedAssetCount;
        int semanticDecoded = bundle.FullStateCache.SelectedEmphasisDecodedAssetCount;
        int dynamicBuilds = bundle.FullStateCache.DynamicBuildCount;
        int authoredLayers = bundle.FullStateCache.AuthoredLayerCount;

        Assert.True(bundle.EnsureUniversalContent(settings, Parameters(settings, 128)));

        Assert.Same(idle, bundle.GetFinalState(0));
        Assert.Equal(authoredDecoded, bundle.FullStateCache.DecodedAssetCount);
        Assert.Equal(0, semanticDecoded);
        Assert.Equal(17, bundle.FullStateCache.SelectedEmphasisDecodedAssetCount);
        Assert.Equal(1, bundle.SelectedEmphasisManifestReadCount);
        Assert.Equal(dynamicBuilds, bundle.FullStateCache.DynamicBuildCount);
        Assert.Equal(authoredLayers, bundle.FullStateCache.AuthoredLayerCount);
        Assert.Equal(1, bundle.FullStateCache.HighlightRebuildCount);
        Assert.Equal(1, bundle.FullStateCache.SelectedEmphasisArtworkBuildCount);

        int artworkBuilds = bundle.FullStateCache.SelectedEmphasisArtworkBuildCount;
        for (int index = 0; index < 64; index++) _ = bundle.GetFinalState(index % 9);
        Assert.Equal(artworkBuilds, bundle.FullStateCache.SelectedEmphasisArtworkBuildCount);
        Assert.Equal(0, bundle.SelectionHotPathWorkCount);
    }

    [Fact]
    public void MappingRebuildAtHighlight128ReusesSelectedSemanticArtworkAndDecodes()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings setA = ThemeSettings(DarkFantasy, SetAMappings(8));
        UniversalRadialParameters highlight128 = Parameters(setA, 128);
        using RuntimeRenderBundle bundle = Build(entry, setA, highlight128);
        int authoredDecoded = bundle.FullStateCache.DecodedAssetCount;
        int semanticDecoded = bundle.FullStateCache.SelectedEmphasisDecodedAssetCount;
        int artworkBuilds = bundle.FullStateCache.SelectedEmphasisArtworkBuildCount;
        int dynamicBuilds = bundle.FullStateCache.DynamicBuildCount;
        string before = RawPixelSha(bundle.GetFinalState(3));
        RadialMenuSettings setB = ThemeSettings(DarkFantasy, SetBMappings());

        Assert.True(bundle.EnsureUniversalContent(setB, Parameters(setB, 128)));

        Assert.NotEqual(before, RawPixelSha(bundle.GetFinalState(3)));
        Assert.Equal(authoredDecoded, bundle.FullStateCache.DecodedAssetCount);
        Assert.Equal(semanticDecoded, bundle.FullStateCache.SelectedEmphasisDecodedAssetCount);
        Assert.Equal(1, bundle.SelectedEmphasisManifestReadCount);
        Assert.Equal(artworkBuilds, bundle.FullStateCache.SelectedEmphasisArtworkBuildCount);
        Assert.Equal(dynamicBuilds + 1, bundle.FullStateCache.DynamicBuildCount);
        Assert.Equal(1, bundle.FullStateCache.MappingRebuildCount);
    }

    [Theory]
    [MemberData(nameof(FormalThemes))]
    public void HighlightComposesIndependentlyWithTextFontAndContentRadius(string themeId)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId, SetAMappings(entry.LayoutDefinition.SlotCount));
        UniversalRadialParameters initial = Parameters(settings, 128);
        using RuntimeRenderBundle bundle = Build(entry, settings, initial);
        UniversalSelectedEmphasisCache semantic = SemanticCache(bundle);
        string staticBefore = RawPixelSha(semantic.GetIntermediateSelected(1));
        int artworkBuilds = semantic.ArtworkBuildCount;
        string finalBefore = RawPixelSha(bundle.GetFinalState(1));
        UniversalRadialParameters combined = Parameters(
            settings,
            128,
            fontScale: 2d,
            textStrength: 128,
            contentRadius: 83d);

        Assert.True(bundle.EnsureUniversalContent(settings, combined));

        Assert.Equal(staticBefore, RawPixelSha(semantic.GetIntermediateSelected(1)));
        Assert.Equal(artworkBuilds, semantic.ArtworkBuildCount);
        Assert.NotEqual(finalBefore, RawPixelSha(bundle.GetFinalState(1)));
        Assert.Equal(128, bundle.UniversalPlan.Parameters.HighlightStrength);
        Assert.Equal(2d, bundle.UniversalPlan.Parameters.FontScale);
        Assert.Equal(128, bundle.UniversalPlan.Parameters.TextStrength);
        Assert.Equal(83d, bundle.UniversalPlan.Parameters.SlotContentRadiusCru);
    }

    [Theory]
    [MemberData(nameof(FormalThemes))]
    public void Highlight128BuildsAtSurfaceScale12BeforePrebuiltSelectionHotPath(string themeId)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId, SetAMappings(entry.LayoutDefinition.SlotCount));
        UniversalRadialParameters parameters = Parameters(settings, 128, surfaceScale: 1.2d);
        UniversalRadialRenderPlan plan = UniversalRadialRenderPlan.Create(entry.Plan, parameters);
        using RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(plan, settings, 96);
        UniversalSelectedEmphasisCache semantic = SemanticCache(bundle);

        Assert.Equal(plan.PhysicalSurfaceSize(96), bundle.PhysicalSurfaceSize);
        Assert.Equal(bundle.PhysicalSurfaceSize, semantic.GetIntermediateSelected(1).Size);
        int artworkBuilds = semantic.ArtworkBuildCount;
        for (int index = 0; index < 32; index++) _ = bundle.GetFinalState(index % (entry.LayoutDefinition.SlotCount + 1));
        Assert.Equal(artworkBuilds, semantic.ArtworkBuildCount);
        Assert.Equal(0, bundle.SelectionHotPathWorkCount);
    }

    [Theory]
    [MemberData(nameof(FormalThemes))]
    public void ProductionBuildRemainsOnLegacyPathAndDoesNotExposeUniversalSemanticCache(string themeId)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId, SetAMappings(entry.LayoutDefinition.SlotCount));
        using RuntimeRenderBundle production = RuntimeRenderBundle.Build(entry.Plan, settings, 280, 96);

        Assert.False(production.IsUniversal);
        Assert.Throws<InvalidOperationException>(() => _ = production.UniversalPlan);
        Assert.Throws<InvalidOperationException>(() => _ = production.SelectedEmphasisCache);
        if (production.IsFullStateFrame)
            Assert.Equal(0, production.FullStateCache.SelectedEmphasisDecodedAssetCount);
    }

    [Fact]
    public void UniversalCacheKeyIncludesHighlightStrength()
    {
        RadialVisualPackCatalogEntry entry = Entry(RadialV5);
        RadialMenuSettings settings = ThemeSettings(RadialV5, SetAMappings(6));
        using RuntimeRenderBundle bundle = Build(entry, settings, Parameters(settings, 255));
        Assert.Equal(255, bundle.UniversalCacheKey!.HighlightStrength);
        Assert.True(bundle.EnsureUniversalContent(settings, Parameters(settings, 128)));
        Assert.Equal(128, bundle.UniversalCacheKey!.HighlightStrength);
    }

    [Fact]
    public void PerformanceAuditRecordsZeroDecodeDeltaAndZeroHotPathWork()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings setA = ThemeSettings(DarkFantasy, SetAMappings(8));
        using RuntimeRenderBundle bundle = Build(entry, setA, Parameters(setA, 255));
        int authoredDecoded = bundle.FullStateCache.DecodedAssetCount;
        int semanticDecoded = bundle.FullStateCache.SelectedEmphasisDecodedAssetCount;

        Assert.True(bundle.EnsureUniversalContent(setA, Parameters(setA, 128)));
        TimeSpan highlight = bundle.LastUniversalDynamicRebuild;
        RadialMenuSettings setB = ThemeSettings(DarkFantasy, SetBMappings());
        Assert.True(bundle.EnsureUniversalContent(setB, Parameters(setB, 128)));
        TimeSpan mapping = bundle.LastUniversalDynamicRebuild;
        int artworkBuilds = bundle.FullStateCache.SelectedEmphasisArtworkBuildCount;
        int dynamicBuilds = bundle.FullStateCache.DynamicBuildCount;
        for (int index = 0; index < 128; index++) _ = bundle.GetFinalState(index % 9);

        Assert.Equal(authoredDecoded, bundle.FullStateCache.DecodedAssetCount);
        Assert.Equal(0, semanticDecoded);
        Assert.Equal(17, bundle.FullStateCache.SelectedEmphasisDecodedAssetCount);
        Assert.Equal(1, bundle.SelectedEmphasisManifestReadCount);
        Assert.Equal(artworkBuilds, bundle.FullStateCache.SelectedEmphasisArtworkBuildCount);
        Assert.Equal(dynamicBuilds, bundle.FullStateCache.DynamicBuildCount);
        Assert.Equal(0, bundle.SelectionHotPathWorkCount);
        _output.WriteLine($"highlight-only-rebuild-ms={highlight.TotalMilliseconds:F3}");
        _output.WriteLine($"mapping-at-highlight128-rebuild-ms={mapping.TotalMilliseconds:F3}");
        _output.WriteLine("authored-decode-delta=0");
        _output.WriteLine("selected-source-mask-decode-delta=0");
        _output.WriteLine("selection-hot-path-work=0");
    }

    private static RuntimeRenderBundle Build(
        RadialVisualPackCatalogEntry entry,
        RadialMenuSettings settings,
        UniversalRadialParameters parameters) => RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, parameters), settings, 96);

    private static UniversalSelectedEmphasisCache SemanticCache(RuntimeRenderBundle bundle) =>
        bundle.IsFullStateFrame
            ? bundle.FullStateCache.SelectedEmphasisCache
            : bundle.SelectedEmphasisCache;

    private static UniversalRadialParameters Parameters(
        RadialMenuSettings settings,
        byte highlightStrength,
        double surfaceScale = 1d,
        double fontScale = 1d,
        byte textStrength = 255,
        double contentRadius = 73d) => new(
            RadialSettingsSemanticRevision.Universal,
            surfaceScale,
            settings.HubRadius,
            settings.PetalInnerRadius,
            settings.PetalOuterRadius,
            settings.PetalGapDegrees,
            contentRadius,
            fontScale,
            byte.MaxValue,
            byte.MaxValue,
            textStrength,
            highlightStrength);

    private static RadialVisualPackCatalogEntry Entry(string themeId)
    {
        RadialVisualPackCatalogSnapshot snapshot = HistoricalV2ThemeCatalog.Create().Discover();
        Assert.Empty(snapshot.Issues);
        return Assert.IsType<RadialVisualPackCatalogEntry>(snapshot.Find(themeId));
    }

    private static RadialMenuSettings ThemeSettings(string themeId, RadialSlotMappings mappings)
    {
        string profile = themeId == RadialV5
            ? LayoutProfileRegistry.Radial6ProfileId
            : LayoutProfileRegistry.Radial8ProfileId;
        return (RadialMenuSettings.Default with
        {
            VisualPackId = themeId,
            MappingProfileId = profile
        }).SetProfileMappings(profile, mappings);
    }

    private static RadialSlotMappings SetAMappings(int slots)
    {
        RadialSlotMapping[] source =
        {
            Keyboard(KeyboardKey.E),
            Shortcut(KeyboardKey.K),
            Ds4("cross"),
            Keyboard(KeyboardKey.Tab),
            RadialSlotMapping.None,
            Ds4("triangle"),
            Keyboard(KeyboardKey.F1),
            Ds4("dpad_down")
        };
        return RadialSlotMappings.Create(slots, source.Take(slots));
    }

    private static RadialSlotMappings SetBMappings() => RadialSlotMappings.Create(8, new[]
    {
        Keyboard(KeyboardKey.F2),
        Shortcut(KeyboardKey.K),
        Ds4("square"),
        Keyboard(KeyboardKey.Tab),
        RadialSlotMapping.None,
        Ds4("circle"),
        Keyboard(KeyboardKey.F1),
        Ds4("dpad_down")
    });

    private static RadialSlotMapping Keyboard(KeyboardKey key) => new()
    {
        Kind = RadialActionKind.KeyboardKey,
        Key = key
    };

    private static RadialSlotMapping Shortcut(KeyboardKey key) => new()
    {
        Kind = RadialActionKind.KeyboardShortcut,
        Key = key,
        Ctrl = true,
        Shift = true
    };

    private static RadialSlotMapping Ds4(string button) => new()
    {
        Kind = RadialActionKind.Ds4Button,
        Ds4Button = button
    };

    private static Bitmap FrozenStatic(RadialVisualPackCatalogEntry entry, int selectedSlot)
    {
        if (entry.Plan.RenderModel is LegacyCanonicalSelectedPlan)
        {
            using var assets = new RadialVisualPackCache(entry.Plan, 1254);
            var result = new Bitmap(1254, 1254, PixelFormat.Format32bppPArgb);
            using Graphics graphics = Graphics.FromImage(result);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(assets.ScaledBase, 0, 0);
            if (selectedSlot > 0)
            {
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.DrawImageUnscaled(assets.GetSelectedSlot(selectedSlot), 0, 0);
            }
            return result;
        }

        FullStateFrameRenderPlan model = Assert.IsType<FullStateFrameRenderPlan>(entry.Plan.RenderModel);
        FullStateFrameState state = model.ResolveState(selectedSlot);
        VerifiedThemeAsset asset = Assert.Single(state.Assets.Values);
        return new GdiThemeAssetDecoder().Decode(asset);
    }

    private static Bitmap RenderDynamic(
        NormalizedRenderPlan plan,
        RadialMenuSettings settings,
        string contentKey,
        UniversalRadialParameters parameters)
    {
        NormalizedDynamicThemeModel dynamic = Assert.IsType<NormalizedDynamicThemeModel>(plan.DynamicTheme);
        FullStateFrameRenderPlan model = Assert.IsType<FullStateFrameRenderPlan>(plan.RenderModel);
        FullStateFrameLayer layer = Assert.Single(model.OrderedLayers, candidate =>
            dynamic.OwnershipByLayer.TryGetValue(candidate.Id, out NormalizedDynamicOwnership? ownership) &&
            ownership.ContentKey == contentKey);
        FullStateFrameState state = model.ResolveState(3);
        ThemeMappingSnapshot mappings = ThemeMappingSnapshot.Capture(settings, plan.LayoutProfileId);
        using ThemeFontSession fonts = ThemeFontResolver.Resolve(dynamic.FontRoles);
        var rasterizer = new ThemeDynamicRasterizer(plan, fonts, universalParameters: parameters);
        Bitmap bitmap = rasterizer.RenderLayer(layer, state, mappings, 96, out ThemeDynamicLayoutResult semantic);
        semantic.Dispose();
        return bitmap;
    }

    private static Point FirstMaskPixel(UniversalSelectedEmphasisAsset asset)
    {
        using Stream stream = asset.OpenRead();
        using var source = new Bitmap(stream);
        using var mask = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(mask))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(source, 0, 0);
        }
        Rectangle rectangle = new(0, 0, mask.Width, mask.Height);
        BitmapData data = mask.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            byte[] row = new byte[mask.Width * 4];
            for (int y = 0; y < mask.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < mask.Width; x++)
                    if (row[x * 4] == byte.MaxValue) return new Point(x, y);
            }
        }
        finally
        {
            mask.UnlockBits(data);
        }
        throw new InvalidDataException("Selected-emphasis mask has no owned pixels.");
    }

    private static byte[] PArgbPixel(Bitmap bitmap, Point point)
    {
        Rectangle rectangle = new(point.X, point.Y, 1, 1);
        BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var pixel = new byte[4];
            Marshal.Copy(data.Scan0, pixel, 0, pixel.Length);
            return pixel;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void AssertBinaryOpaqueMask(Bitmap source)
    {
        using var mask = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(mask))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(source, 0, 0);
        }
        Rectangle rectangle = new(0, 0, mask.Width, mask.Height);
        BitmapData data = mask.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            byte[] row = new byte[mask.Width * 4];
            bool owned = false;
            for (int y = 0; y < mask.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int offset = 0; offset < row.Length; offset += 4)
                {
                    byte value = row[offset];
                    Assert.Equal(byte.MaxValue, row[offset + 3]);
                    Assert.Equal(value, row[offset + 1]);
                    Assert.Equal(value, row[offset + 2]);
                    Assert.True(value is 0 or byte.MaxValue);
                    owned |= value == byte.MaxValue;
                }
            }
            Assert.True(owned);
        }
        finally
        {
            mask.UnlockBits(data);
        }
    }

    private static void AssertPArgbInvariant(Bitmap bitmap)
    {
        Rectangle rectangle = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            byte[] row = new byte[bitmap.Width * 4];
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int offset = 0; offset < row.Length; offset += 4)
                {
                    byte alpha = row[offset + 3];
                    Assert.True(row[offset] <= alpha);
                    Assert.True(row[offset + 1] <= alpha);
                    Assert.True(row[offset + 2] <= alpha);
                    if (alpha == 0)
                    {
                        Assert.Equal(0, row[offset]);
                        Assert.Equal(0, row[offset + 1]);
                        Assert.Equal(0, row[offset + 2]);
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static string RawPixelSha(Bitmap bitmap)
    {
        Rectangle rectangle = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            byte[] bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
