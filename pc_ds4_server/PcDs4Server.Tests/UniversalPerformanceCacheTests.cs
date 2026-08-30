using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class UniversalPerformanceCacheTests
{
    private const string RadialV5 = "radial-v5";
    private const string Radial8Minimal = "radial-8-minimal-v1";
    private const string DarkFantasy = "dark-fantasy-radial8-v1";

    [Fact]
    public void DarkFantasyDpi192RetainsOnlyTightSpritesAndPrebuiltFinalStates()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = DarkSettings(SetAMappings());
        using RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(
            UniversalPlan(entry, Parameters(settings)),
            settings,
            192);

        FullStateFrameCache cache = bundle.FullStateCache;
        DynamicCacheBuildDiagnostics diagnostics = cache.DynamicDiagnostics;
        long previousDynamicBytes = checked(162L * 840L * 840L * 4L);

        Assert.Equal(RadialRenderPolicy.Legacy, RadialRenderPolicyAuthority.ProductionDefault);
        Assert.Equal(new Size(840, 840), bundle.PhysicalSurfaceSize);
        Assert.Equal(9, cache.StateCount);
        Assert.Equal(9, diagnostics.FinalStateBuildCount);
        Assert.Equal(9, diagnostics.AuthoredDecodeCount);
        Assert.Equal(0, diagnostics.DynamicCompositeBuildCount);
        Assert.Equal(0, diagnostics.FullSurfaceDynamicBitmapCount);
        Assert.Equal(0, diagnostics.FullSurfaceDynamicBytes);
        Assert.Equal(0, cache.DynamicBitmapLiveCount);
        Assert.Equal(0, cache.PeakDynamicBitmapLiveCount);
        Assert.Equal(10, diagnostics.SharedDynamicLayerRasterCount);
        Assert.Equal(10, diagnostics.SelectedDynamicLayerRasterCount);
        Assert.Equal(
            diagnostics.SharedDynamicLayerRasterCount + diagnostics.SelectedDynamicLayerRasterCount,
            diagnostics.DynamicLayerRasterCount);
        Assert.Equal(diagnostics.DynamicLayerRasterCount, diagnostics.LocalSpriteBitmapCount);
        Assert.Equal(diagnostics.LocalSpriteBitmapCount, cache.LocalSpriteBitmapLiveCount);
        Assert.Equal(diagnostics.LocalSpriteBytes, cache.LocalSpriteBitmapLiveBytes);
        Assert.True(previousDynamicBytes > diagnostics.LocalSpriteBytes * 20L);
        Assert.Equal(18, cache.FullSurfaceBitmapLiveCount);
        Assert.Equal(18, cache.PeakFullSurfaceBitmapLiveCount);
        Assert.True(bundle.HasPrebuiltFinalStates);
    }

    [Fact]
    public void Highlight128MappingRebuildReusesSemanticArtworkAndTightSprites()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings setA = DarkSettings(SetAMappings());
        using RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(
            UniversalPlan(entry, Parameters(setA, highlightStrength: 128)),
            setA,
            96);
        FullStateFrameCache cache = bundle.FullStateCache;
        int authoredDecoded = cache.DecodedAssetCount;
        int semanticDecoded = bundle.SelectedEmphasisDecodedAssetCount;
        int manifestReads = bundle.SelectedEmphasisManifestReadCount;
        string selectedBefore = RawPixelSha(bundle.GetFinalState(3));

        RadialMenuSettings setB = DarkSettings(SetBMappings());
        Assert.True(bundle.EnsureUniversalContent(
            setB,
            Parameters(setB, highlightStrength: 128)));

        Assert.Equal(authoredDecoded, cache.DecodedAssetCount);
        Assert.Equal(semanticDecoded, bundle.SelectedEmphasisDecodedAssetCount);
        Assert.Equal(manifestReads, bundle.SelectedEmphasisManifestReadCount);
        Assert.Equal(17, semanticDecoded);
        Assert.Equal(1, manifestReads);
        Assert.Equal(0, cache.DynamicDiagnostics.FullSurfaceDynamicBitmapCount);
        Assert.Equal(cache.DynamicDiagnostics.LocalSpriteBitmapCount, cache.LocalSpriteBitmapLiveCount);
        Assert.NotEqual(selectedBefore, RawPixelSha(bundle.GetFinalState(3)));
    }

    [Fact]
    public void MappingAndFontRebuildsDecodeNoAuthoredOrSemanticArtwork()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings setA = DarkSettings(SetAMappings());
        using RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(
            UniversalPlan(entry, Parameters(setA)),
            setA,
            192);
        FullStateFrameCache cache = bundle.FullStateCache;
        int authoredDecoded = cache.DecodedAssetCount;
        int semanticDecoded = bundle.SelectedEmphasisDecodedAssetCount;
        int manifestReads = bundle.SelectedEmphasisManifestReadCount;
        Size surface = bundle.PhysicalSurfaceSize;
        NormalizedPoint anchor = bundle.UniversalPlan.EffectiveActivationAnchor;

        Assert.True(bundle.EnsureUniversalContent(setA, Parameters(setA, fontScale: 2d)));
        Assert.Equal(authoredDecoded, cache.DecodedAssetCount);
        Assert.Equal(semanticDecoded, bundle.SelectedEmphasisDecodedAssetCount);
        Assert.Equal(manifestReads, bundle.SelectedEmphasisManifestReadCount);
        Assert.Equal(surface, bundle.PhysicalSurfaceSize);
        Assert.Equal(anchor, bundle.UniversalPlan.EffectiveActivationAnchor);
        Assert.Equal(0, cache.DynamicDiagnostics.FullSurfaceDynamicBitmapCount);

        RadialMenuSettings setB = DarkSettings(SetBMappings());
        Assert.True(bundle.EnsureUniversalContent(setB, Parameters(setB, fontScale: 2d)));
        Assert.Equal(authoredDecoded, cache.DecodedAssetCount);
        Assert.Equal(semanticDecoded, bundle.SelectedEmphasisDecodedAssetCount);
        Assert.Equal(manifestReads, bundle.SelectedEmphasisManifestReadCount);
        Assert.Equal(1, cache.MappingRebuildCount);
        Assert.Equal(0, cache.DynamicDiagnostics.FullSurfaceDynamicBitmapCount);
        Assert.Equal(cache.DynamicDiagnostics.LocalSpriteBitmapCount, cache.LocalSpriteBitmapLiveCount);
        Assert.Equal(18, cache.FullSurfaceBitmapLiveCount);
        Assert.InRange(cache.PeakFullSurfaceBitmapLiveCount, 18, 27);
    }

    [Fact]
    public void Scale140AtDpi192KeepsDynamicFullSurfacesAtZero()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = DarkSettings(SetBMappings());
        using RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(
            UniversalPlan(entry, Parameters(settings, surfaceScale: 1.4d, fontScale: 2d)),
            settings,
            192);
        DynamicCacheBuildDiagnostics diagnostics = bundle.FullStateCache.DynamicDiagnostics;
        long previousDynamicBytes = checked(162L * 1176L * 1176L * 4L);

        Assert.Equal(new Size(1176, 1176), bundle.PhysicalSurfaceSize);
        Assert.Equal(0, diagnostics.FullSurfaceDynamicBitmapCount);
        Assert.Equal(0, diagnostics.FullSurfaceDynamicBytes);
        Assert.True(previousDynamicBytes > diagnostics.LocalSpriteBytes * 20L);
        Assert.Equal(9, diagnostics.FinalStateBuildCount);
    }

    [Fact]
    public void TwentyRebuildsReturnOwnedBitmapCountsToStableBaselines()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings setA = DarkSettings(SetAMappings());
        RadialMenuSettings setB = DarkSettings(SetBMappings());
        RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(
            UniversalPlan(entry, Parameters(setA)),
            setA,
            96);
        FullStateFrameCache cache = bundle.FullStateCache;
        int stableFullSurfaces = cache.FullSurfaceBitmapLiveCount;

        for (int iteration = 0; iteration < 20; iteration++)
        {
            RadialMenuSettings settings = iteration % 2 == 0 ? setB : setA;
            double fontScale = iteration % 4 < 2 ? 2d : 1d;
            Assert.True(bundle.EnsureUniversalContent(
                settings,
                Parameters(settings, fontScale: fontScale)));
            Assert.Equal(stableFullSurfaces, cache.FullSurfaceBitmapLiveCount);
            Assert.Equal(cache.DynamicDiagnostics.LocalSpriteBitmapCount, cache.LocalSpriteBitmapLiveCount);
            Assert.Equal(cache.DynamicDiagnostics.LocalSpriteBytes, cache.LocalSpriteBitmapLiveBytes);
            Assert.Equal(0, cache.DynamicDiagnostics.FullSurfaceDynamicBitmapCount);
        }

        Assert.Equal(18, stableFullSurfaces);
        Assert.InRange(cache.PeakFullSurfaceBitmapLiveCount, stableFullSurfaces, 27);
        Assert.True(cache.PeakLocalSpriteBitmapLiveCount <= 64);
        bundle.Dispose();
        Assert.Equal(0, cache.FullSurfaceBitmapLiveCount);
        Assert.Equal(cache.FullSurfaceBitmapCreatedCount, cache.FullSurfaceBitmapDisposedCount);
        Assert.Equal(0, cache.LocalSpriteBitmapLiveCount);
        Assert.Equal(0, cache.LocalSpriteBitmapLiveBytes);
        Assert.Equal(cache.LocalSpriteBitmapCreatedCount, cache.LocalSpriteBitmapDisposedCount);
    }

    [Fact]
    public void FailedMappingCandidateRetainsStatesSpritesAndLifecycleCounts()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings setA = DarkSettings(SetAMappings());
        int attempts = 0;
        using var cache = new FullStateFrameCache(
            entry.Plan,
            setA,
            96,
            mappingBuildProbe: _ =>
            {
                attempts++;
                if (attempts > 1)
                    throw new InvalidDataException("synthetic performance-cache candidate failure");
            });
        Bitmap active = cache.GetState(3);
        string activeHash = RawPixelSha(active);
        int fullCreated = cache.FullSurfaceBitmapCreatedCount;
        int fullDisposed = cache.FullSurfaceBitmapDisposedCount;
        int spriteCreated = cache.LocalSpriteBitmapCreatedCount;
        int spriteDisposed = cache.LocalSpriteBitmapDisposedCount;

        Assert.Throws<InvalidDataException>(() => cache.EnsureMappings(DarkSettings(SetBMappings())));
        Assert.Same(active, cache.GetState(3));
        Assert.Equal(activeHash, RawPixelSha(cache.GetState(3)));
        Assert.Equal(fullCreated, cache.FullSurfaceBitmapCreatedCount);
        Assert.Equal(fullDisposed, cache.FullSurfaceBitmapDisposedCount);
        Assert.Equal(spriteCreated, cache.LocalSpriteBitmapCreatedCount);
        Assert.Equal(spriteDisposed, cache.LocalSpriteBitmapDisposedCount);
        Assert.Equal(0, cache.MappingRebuildCount);
    }

    [Theory]
    [InlineData(RadialV5)]
    [InlineData(Radial8Minimal)]
    public void UniversalV1StillUsesOneDynamicCacheAndPrebuiltSelectionStates(string themeId)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId);
        using RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(
            UniversalPlan(entry, Parameters(settings)),
            settings,
            96);
        int builds = bundle.DynamicContent.BuildCount;

        Assert.False(bundle.IsFullStateFrame);
        Assert.True(bundle.HasPrebuiltFinalStates);
        for (int slot = 0; slot <= entry.LayoutDefinition.SlotCount; slot++)
            Assert.Equal(PixelFormat.Format32bppPArgb, bundle.GetFinalState(slot).PixelFormat);
        Assert.Equal(builds, bundle.DynamicContent.BuildCount);
        Assert.Equal(0, bundle.SelectionHotPathWorkCount);
    }

    [Fact]
    public void DarkFantasyGetFinalStatePerformsNoCacheOrRasterWork()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = DarkSettings(SetAMappings());
        using RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(
            UniversalPlan(entry, Parameters(settings)),
            settings,
            96);
        FullStateFrameCache cache = bundle.FullStateCache;
        DynamicCacheBuildDiagnostics before = cache.DynamicDiagnostics;
        int created = cache.FullSurfaceBitmapCreatedCount;
        int spriteCreated = cache.LocalSpriteBitmapCreatedCount;

        for (int iteration = 0; iteration < 128; iteration++)
            _ = bundle.GetFinalState(iteration % 9);

        Assert.Same(before, cache.DynamicDiagnostics);
        Assert.Equal(created, cache.FullSurfaceBitmapCreatedCount);
        Assert.Equal(spriteCreated, cache.LocalSpriteBitmapCreatedCount);
        Assert.Equal(0, cache.HotPathDynamicWorkCount);
        Assert.Equal(0, bundle.SelectionHotPathWorkCount);
    }

    private static RadialVisualPackCatalogEntry Entry(string themeId)
    {
        RadialVisualPackCatalogSnapshot snapshot = new RadialVisualPackCatalog().Discover();
        Assert.Empty(snapshot.Issues);
        return Assert.IsType<RadialVisualPackCatalogEntry>(snapshot.Find(themeId));
    }

    private static UniversalRadialRenderPlan UniversalPlan(
        RadialVisualPackCatalogEntry entry,
        UniversalRadialParameters parameters) =>
        UniversalRadialRenderPlan.Create(entry.Plan, parameters);

    private static UniversalRadialParameters Parameters(
        RadialMenuSettings settings,
        double surfaceScale = 1d,
        double fontScale = 1d,
        byte textStrength = byte.MaxValue,
        byte highlightStrength = byte.MaxValue) => new(
            RadialSettingsSemanticRevision.Universal,
            surfaceScale,
            settings.HubRadius,
            settings.PetalInnerRadius,
            settings.PetalOuterRadius,
            settings.PetalGapDegrees,
            settings.TextRadius,
            fontScale,
            byte.MaxValue,
            byte.MaxValue,
            textStrength,
            highlightStrength);

    private static RadialMenuSettings ThemeSettings(string themeId)
    {
        string profile = themeId == RadialV5
            ? LayoutProfileRegistry.Radial6ProfileId
            : LayoutProfileRegistry.Radial8ProfileId;
        int slots = profile == LayoutProfileRegistry.Radial6ProfileId ? 6 : 8;
        RadialSlotMappings mappings = RadialSlotMappings.Create(
            slots,
            Enumerable.Range(0, slots).Select(index => Keyboard(
                index % 2 == 0 ? KeyboardKey.E : KeyboardKey.F1)));
        return (RadialMenuSettings.Default with
        {
            VisualPackId = themeId,
            MappingProfileId = profile
        }).SetProfileMappings(profile, mappings);
    }

    private static RadialMenuSettings DarkSettings(RadialSlotMappings mappings) =>
        (RadialMenuSettings.Default with
        {
            VisualPackId = DarkFantasy,
            MappingProfileId = LayoutProfileRegistry.Radial8ProfileId
        }).SetProfileMappings(LayoutProfileRegistry.Radial8ProfileId, mappings);

    private static RadialSlotMappings SetAMappings() => RadialSlotMappings.Create(8, new[]
    {
        Keyboard(KeyboardKey.E),
        Shortcut(KeyboardKey.K, ctrl: true, shift: true),
        Ds4("cross"),
        Keyboard(KeyboardKey.Tab),
        RadialSlotMapping.None,
        Ds4("triangle"),
        Keyboard(KeyboardKey.F1),
        Ds4("dpad_down")
    });

    private static RadialSlotMappings SetBMappings() => RadialSlotMappings.Create(8, new[]
    {
        Keyboard(KeyboardKey.F2),
        Shortcut(KeyboardKey.K, ctrl: true, shift: true),
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

    private static RadialSlotMapping Shortcut(KeyboardKey key, bool ctrl, bool shift) => new()
    {
        Kind = RadialActionKind.KeyboardShortcut,
        Key = key,
        Ctrl = ctrl,
        Shift = shift
    };

    private static RadialSlotMapping Ds4(string button) => new()
    {
        Kind = RadialActionKind.Ds4Button,
        Ds4Button = button
    };

    private static string RawPixelSha(Bitmap bitmap)
    {
        Rectangle rectangle = new(Point.Empty, bitmap.Size);
        BitmapData data = bitmap.LockBits(
            rectangle,
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppPArgb);
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
