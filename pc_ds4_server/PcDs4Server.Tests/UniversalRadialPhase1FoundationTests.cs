using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace PcDs4Server.Tests;

public sealed class UniversalRadialPhase1FoundationTests
{
    private const string RadialV5 = "radial-v5";
    private const string Radial8Minimal = "radial-8-minimal-v1";
    private const string DarkFantasy = HistoricalV2ThemeCatalog.ReferenceThemeId;
    private readonly ITestOutputHelper _output;

    private static readonly IReadOnlyDictionary<int, string> DarkSetAHashes =
        new Dictionary<int, string>
        {
            [0] = "33608F5E69F671F57E915453937AD056BDD51140A9FAF04DB54228D164C0ED04",
            [1] = "39342554BF97147DEDCA8ECBB663885FE92719A26128B9877C79AEA16054E93E",
            [2] = "E1D80B47AF03A405B1974F02C80908CC582E0FE76C22CCCFECBFA50C78186A79",
            [3] = "06B0C3305E6A2C7C9346B671E6D4971E9BF40347F72D73FC3BDBECC2A3B022C8",
            [5] = "C14AE5188422E89BA526B48D25EE14707CC61DF0B14EDB9170FF6CC5113F1D7F",
            [8] = "16E1307DB5DE3824D1AAFC3CBDD063A4EE58AA29E74BCC966D826C6FBA51D0FC"
        };

    private static readonly IReadOnlyDictionary<int, string> DarkSetBHashes =
        new Dictionary<int, string>
        {
            [0] = "EE5EE43D7FE0C41C2C3646DD9434A5A6898880D36806D139D9DA7C45DBCDA7C6",
            [1] = "EED441A05F6F0817E699C36A9368520F0E16CA847BB7A23074D0E018C0584FF9",
            [3] = "36B7F4E77AE20882631DA2490096D95200C980662575284EB99A937D4718FBA7",
            [6] = "48EFD848B39B84515205D110A90779E8E476D3F49A79CD9BE70EA75A2C652CA9"
        };

    public UniversalRadialPhase1FoundationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void NeutralVector_IsFrozenAndContainsOnlyVisualParameters()
    {
        UniversalRadialParameters value = UniversalRadialParameters.Neutral;
        Assert.Equal(RadialSettingsSemanticRevision.Universal, value.SemanticRevision);
        Assert.Equal(1d, value.SurfaceScale);
        Assert.Equal(35d, value.HubRadiusCru);
        Assert.Equal(42d, value.InnerRadiusCru);
        Assert.Equal(103d, value.OuterRadiusCru);
        Assert.Equal(4d, value.GapDegrees);
        Assert.Equal(73d, value.SlotContentRadiusCru);
        Assert.Equal(1d, value.FontScale);
        Assert.Equal(byte.MaxValue, value.FillStrength);
        Assert.Equal(byte.MaxValue, value.BorderStrength);
        Assert.Equal(byte.MaxValue, value.TextStrength);
        Assert.Equal(byte.MaxValue, value.HighlightStrength);
        string[] properties = typeof(UniversalRadialParameters).GetProperties().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(nameof(RadialMenuSettings.ReceiverUiScalePercent), properties);
        Assert.DoesNotContain(nameof(RadialMenuSettings.DoubleTapWindowMs), properties);
        Assert.DoesNotContain(nameof(RadialMenuSettings.SelectionDeadZone), properties);
        Assert.DoesNotContain(nameof(RadialMenuSettings.SelectionPollIntervalMs), properties);
    }

    [Fact]
    public void MissingAndRevisionOneAreLegacyWhileRevisionTwoIsUniversal()
    {
        RadialMenuSettings missing = ThemeSettings(RadialV5);
        UniversalRadialParameters implicitLegacy = UniversalRadialSettingsNormalizer.Normalize(missing);
        UniversalRadialParameters explicitLegacy = UniversalRadialSettingsNormalizer.Normalize(
            missing with { SettingsSemanticRevision = RadialSettingsSemanticRevision.Legacy });
        UniversalRadialParameters universal = UniversalRadialSettingsNormalizer.Normalize(
            missing with
            {
                SettingsSemanticRevision = RadialSettingsSemanticRevision.Universal,
                TextAlpha = 128
            });

        Assert.Equal(RadialSettingsSemanticRevision.Legacy, implicitLegacy.SemanticRevision);
        Assert.Equal(implicitLegacy, explicitLegacy);
        Assert.Equal(RadialSettingsSemanticRevision.Universal, universal.SemanticRevision);
        Assert.Equal((byte)128, universal.TextStrength);
    }

    [Fact]
    public void LegacyNormalization_IsPureDeterministicAndDoesNotMutateInput()
    {
        RadialMenuSettings input = ThemeSettings(RadialV5) with
        {
            BaseCanvasSize = 320,
            ScalePercent = 125,
            TextAlpha = 240
        };
        string before = JsonSerializer.Serialize(input);

        UniversalRadialParameters first = UniversalRadialSettingsNormalizer.Normalize(input);
        UniversalRadialParameters second = UniversalRadialSettingsNormalizer.Normalize(input);

        Assert.Equal(first, second);
        Assert.Equal(10d / 7d, first.SurfaceScale, precision: 12);
        Assert.Equal(byte.MaxValue, first.TextStrength);
        Assert.Equal(before, JsonSerializer.Serialize(input));
    }

    [Fact]
    public void LegacyAlphaDefaultsAndTextClampNormalizeToFullStrength()
    {
        UniversalRadialParameters defaults = UniversalRadialSettingsNormalizer.Normalize(
            ThemeSettings(RadialV5));
        Assert.Equal(byte.MaxValue, defaults.FillStrength);
        Assert.Equal(byte.MaxValue, defaults.BorderStrength);
        Assert.Equal(byte.MaxValue, defaults.HighlightStrength);
        Assert.Equal(byte.MaxValue, defaults.TextStrength);

        Assert.Equal(byte.MaxValue, UniversalRadialSettingsNormalizer.Normalize(
            ThemeSettings(RadialV5) with { TextAlpha = 255 }).TextStrength);
        Assert.Equal((byte)128, UniversalRadialSettingsNormalizer.Normalize(
            ThemeSettings(RadialV5) with
            {
                SettingsSemanticRevision = RadialSettingsSemanticRevision.Universal,
                TextAlpha = 128
            }).TextStrength);
    }

    [Fact]
    public void LegacyAlphaNormalizationUsesFrozenDefaultsAndAwayFromZeroRounding()
    {
        RadialMenuSettings settings = ThemeSettings(RadialV5) with
        {
            FillAlpha = 109,
            BorderAlpha = 50,
            HighlightAlpha = 40,
            TextAlpha = 117
        };

        UniversalRadialParameters parameters = UniversalRadialSettingsNormalizer.Normalize(settings);

        Assert.Equal((byte)128, parameters.FillStrength);
        Assert.Equal((byte)128, parameters.BorderStrength);
        Assert.Equal((byte)128, parameters.HighlightStrength);
        Assert.Equal((byte)127, parameters.TextStrength);
    }

    [Fact]
    public void RevisionTwoNormalizationIsIdempotentAndDoesNotApplyLegacyAlphaRules()
    {
        RadialMenuSettings settings = ThemeSettings(RadialV5) with
        {
            SettingsSemanticRevision = RadialSettingsSemanticRevision.Universal,
            FillAlpha = 109,
            BorderAlpha = 50,
            TextAlpha = 128,
            HighlightAlpha = 40
        };

        UniversalRadialParameters first = UniversalRadialSettingsNormalizer.Normalize(settings);
        UniversalRadialParameters second = UniversalRadialSettingsNormalizer.Normalize(settings);

        Assert.Equal(first, second);
        Assert.Equal((byte)109, first.FillStrength);
        Assert.Equal((byte)50, first.BorderStrength);
        Assert.Equal((byte)128, first.TextStrength);
        Assert.Equal((byte)40, first.HighlightStrength);
    }

    [Fact]
    public void FontSizeNormalizesAgainstTheFrozenAuthoredBaseline()
    {
        Assert.Equal(1d, UniversalRadialSettingsNormalizer.Normalize(
            ThemeSettings(RadialV5) with { FontSize = 15f }).FontScale);
        Assert.Equal(2d, UniversalRadialSettingsNormalizer.Normalize(
            ThemeSettings(RadialV5) with { FontSize = 30f }).FontScale);
        Assert.Equal(.5d, UniversalRadialSettingsNormalizer.Normalize(
            ThemeSettings(RadialV5) with { FontSize = 7.5f }).FontScale);
    }

    [Fact]
    public void RevisionTwoIgnoresLegacyBaseCanvasAndSupportsTheCanonicalScaleRange()
    {
        RadialMenuSettings first = ThemeSettings(RadialV5) with
        {
            SettingsSemanticRevision = RadialSettingsSemanticRevision.Universal,
            BaseCanvasSize = 160,
            ScalePercent = 200
        };
        RadialMenuSettings second = first with { BaseCanvasSize = 800 };

        UniversalRadialParameters firstParameters = UniversalRadialSettingsNormalizer.Normalize(first);
        UniversalRadialParameters secondParameters = UniversalRadialSettingsNormalizer.Normalize(second);

        Assert.Equal(2d, firstParameters.SurfaceScale);
        Assert.Equal(firstParameters, secondParameters);
    }

    [Fact]
    public void LegacyStoreRoundTripDoesNotAddOrUpgradeSemanticRevision()
    {
        using var temporary = new TemporarySettingsPath();
        var store = new RadialMenuSettingsStore(temporary.FilePath);
        RadialMenuSettings legacy = ThemeSettings(RadialV5);

        Assert.True(store.TrySave(legacy, out string error), error);
        string json = File.ReadAllText(temporary.FilePath);
        RadialMenuSettingsLoadResult loaded = store.Load();

        Assert.DoesNotContain("settingsSemanticRevision", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(loaded.Settings.SettingsSemanticRevision);
        Assert.Equal(RadialSettingsSemanticRevision.Legacy,
            UniversalRadialSettingsNormalizer.Normalize(loaded.Settings).SemanticRevision);
    }

    [Fact]
    public void ExplicitUniversalRevisionRoundTripsWithoutStoreSideEffects()
    {
        using var temporary = new TemporarySettingsPath();
        var store = new RadialMenuSettingsStore(temporary.FilePath);
        RadialMenuSettings value = ThemeSettings(RadialV5) with
        {
            SettingsSemanticRevision = RadialSettingsSemanticRevision.Universal,
            TextAlpha = 128
        };

        Assert.True(store.TrySave(value, out string error), error);
        RadialMenuSettingsLoadResult loaded = store.Load();

        Assert.Equal(RadialSettingsSemanticRevision.Universal,
            loaded.Settings.SettingsSemanticRevision);
        Assert.Equal((byte)128,
            UniversalRadialSettingsNormalizer.Normalize(loaded.Settings).TextStrength);
    }

    [Theory]
    [InlineData(RadialV5, 280d, 140d, 6)]
    [InlineData(Radial8Minimal, 280d, 140d, 8)]
    [InlineData(DarkFantasy, 420d, 210d, 8)]
    public void UniversalPlanWrapsV1AndV2WithoutThemeIdBranching(
        string themeId,
        double nominal,
        double anchor,
        int slotCount)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        UniversalRadialRenderPlan plan = UniversalPlan(entry, ThemeSettings(themeId));

        Assert.Same(entry.Plan, plan.SourcePlan);
        Assert.Same(entry.LayoutDefinition, plan.LayoutDefinition);
        Assert.Equal(nominal, plan.NominalLogicalSurface.Width);
        Assert.Equal(nominal, plan.EffectiveLogicalSurface.Width);
        Assert.Equal(anchor, plan.AuthoredActivationAnchor.X);
        Assert.Equal(anchor, plan.EffectiveActivationAnchor.X);
        Assert.Equal(slotCount, plan.LayoutDefinition.SlotCount);
        Assert.Equal(entry.Version, plan.SourceProtocolVersion);
        Assert.Equal(themeId == DarkFantasy
            ? UniversalRadialRenderModel.FullStateFrame
            : UniversalRadialRenderModel.LegacyCanonicalSelected, plan.RenderModel);
    }

    [Theory]
    [InlineData(RadialV5, 280, 336)]
    [InlineData(Radial8Minimal, 280, 336)]
    [InlineData(DarkFantasy, 420, 504)]
    public void ScaleMatrixChangesSurfaceAnchorAndPixelsButNotTopology(
        string themeId,
        int neutralSize,
        int scaledSize)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId);
        UniversalRadialParameters neutral = Parameters(settings, surfaceScale: 1d);
        UniversalRadialParameters scaled = Parameters(settings, surfaceScale: 1.2d);
        using RuntimeRenderBundle one = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, neutral), settings, 96);
        using RuntimeRenderBundle twelve = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, scaled), settings, 96);

        Assert.Equal(new Size(neutralSize, neutralSize), one.PhysicalSurfaceSize);
        Assert.Equal(new Size(scaledSize, scaledSize), twelve.PhysicalSurfaceSize);
        Assert.Equal(one.UniversalPlan.EffectiveActivationAnchor.X * 1.2d,
            twelve.UniversalPlan.EffectiveActivationAnchor.X, precision: 10);
        Assert.NotEqual(RawPixelSha(one.GetFinalState(1)), RawPixelSha(twelve.GetFinalState(1)));
        Assert.Same(one.UniversalPlan.LayoutDefinition, twelve.UniversalPlan.LayoutDefinition);
        foreach ((double x, double y) in new[] { (20d, 5d), (40d, 5d), (70d, 20d), (-55d, 40d) })
        {
            Assert.Equal(
                RadialSelectionEngine.GetSelectedSlot(one.UniversalPlan.LayoutDefinition, x, y, 28),
                RadialSelectionEngine.GetSelectedSlot(twelve.UniversalPlan.LayoutDefinition, x, y, 28));
        }
    }

    [Fact]
    public void DarkFantasyScaleTwelveIs504At96And1008At192()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = ThemeSettings(DarkFantasy);
        UniversalRadialParameters parameters = Parameters(settings, surfaceScale: 1.2d);
        UniversalRadialRenderPlan plan = UniversalRadialRenderPlan.Create(entry.Plan, parameters);

        Assert.Equal(new Size(504, 504), plan.PhysicalSurfaceSize(96));
        Assert.Equal(new Size(1008, 1008), plan.PhysicalSurfaceSize(192));
        Assert.Equal(new Point(252, 252), plan.PhysicalActivationAnchor(96));
        Assert.Equal(new Point(448, 248), plan.ComputeOverlayTopLeft(new Point(700, 500), 96));
    }

    [Fact]
    public void SelectionDeadZoneIsIndependentOfUniversalSurfaceScale()
    {
        LayoutDefinition layout = Entry(DarkFantasy).LayoutDefinition;
        int at100 = RadialSelectionEngine.GetSelectedSlot(layout, 40, 5, 28);
        int at120 = RadialSelectionEngine.GetSelectedSlot(layout, 40, 5, 28);
        Assert.Equal(at100, at120);
        Assert.Equal(0, RadialSelectionEngine.GetSelectedSlot(layout, 20, 5, 28));
        Assert.Equal(28d * 192d / 96d, 56d);
    }

    [Theory]
    [InlineData(RadialV5)]
    [InlineData(Radial8Minimal)]
    [InlineData(DarkFantasy)]
    public void LegacyBaseScaleEquivalentInputsProduceIdenticalUniversalRenders(string themeId)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings first = ThemeSettings(themeId) with { BaseCanvasSize = 280, ScalePercent = 120 };
        RadialMenuSettings second = ThemeSettings(themeId) with { BaseCanvasSize = 336, ScalePercent = 100 };
        UniversalRadialParameters firstParameters = UniversalRadialSettingsNormalizer.Normalize(first);
        UniversalRadialParameters secondParameters = UniversalRadialSettingsNormalizer.Normalize(second);
        Assert.Equal(firstParameters, secondParameters);

        using RuntimeRenderBundle firstBundle = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, firstParameters), first, 96);
        using RuntimeRenderBundle secondBundle = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, secondParameters), second, 96);
        Assert.Equal(firstBundle.PhysicalSurfaceSize, secondBundle.PhysicalSurfaceSize);
        Assert.Equal(firstBundle.UniversalPlan.EffectiveActivationAnchor,
            secondBundle.UniversalPlan.EffectiveActivationAnchor);
        Assert.Equal(RawPixelSha(firstBundle.GetFinalState(1)),
            RawPixelSha(secondBundle.GetFinalState(1)));
    }

    [Theory]
    [InlineData(RadialV5)]
    [InlineData(Radial8Minimal)]
    public void V1NeutralUniversalOutputIsPixelExactWithProductionPath(string themeId)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId);
        using RuntimeRenderBundle production = RuntimeRenderBundle.Build(entry.Plan, settings, 280, 96);
        using RuntimeRenderBundle universal = RuntimeRenderBundle.BuildUniversal(
            UniversalPlan(entry, settings), settings, 96);

        Assert.False(production.IsUniversal);
        Assert.True(universal.IsUniversal);
        for (int slot = 0; slot <= entry.LayoutDefinition.SlotCount; slot++)
        {
            using Bitmap expected = ComposeProductionV1(production, slot);
            Assert.Equal(RawPixelSha(expected), RawPixelSha(universal.GetFinalState(slot)));
        }
    }

    [Fact]
    public void DarkFantasyNeutralUniversalOutputMatchesAllFrozenIdentityHashes()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings setA = DarkSettings(SetAMappings());
        using RuntimeRenderBundle production = RuntimeRenderBundle.Build(entry.Plan, setA, 280, 192);
        using RuntimeRenderBundle universal = RuntimeRenderBundle.BuildUniversal(
            UniversalPlan(entry, setA), setA, 192);
        foreach ((int slot, string expected) in DarkSetAHashes)
        {
            Assert.Equal(expected, RawPixelSha(universal.GetFinalState(slot)));
            Assert.Equal(RawPixelSha(production.GetFinalState(slot)),
                RawPixelSha(universal.GetFinalState(slot)));
        }

        RadialMenuSettings setB = DarkSettings(SetBMappings());
        Assert.True(universal.EnsureUniversalContent(
            setB,
            UniversalRadialSettingsNormalizer.Normalize(setB)));
        foreach ((int slot, string expected) in DarkSetBHashes)
            Assert.Equal(expected, RawPixelSha(universal.GetFinalState(slot)));
    }

    [Theory]
    [InlineData(RadialV5)]
    [InlineData(Radial8Minimal)]
    [InlineData(DarkFantasy)]
    public void FontMatrixChangesDynamicPixelsWithoutChangingArtOrSurface(string themeId)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId);
        UniversalRadialParameters normal = Parameters(settings, fontScale: 1d);
        UniversalRadialParameters doubleFont = Parameters(settings, fontScale: 2d);
        using RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, normal), settings, 96);
        Size surface = bundle.PhysicalSurfaceSize;
        string before = RawPixelSha(bundle.GetFinalState(1));
        string? artBefore = bundle.IsFullStateFrame ? null : RawPixelSha(bundle.AssetCache.ScaledBase);
        Rectangle? dynamicBoundsBefore = bundle.IsFullStateFrame
            ? null
            : AlphaBounds(bundle.DynamicContent.Content);

        Assert.True(bundle.EnsureUniversalContent(settings, doubleFont));

        Assert.Equal(surface, bundle.PhysicalSurfaceSize);
        Assert.NotEqual(before, RawPixelSha(bundle.GetFinalState(1)));
        if (artBefore != null)
        {
            Assert.Equal(artBefore, RawPixelSha(bundle.AssetCache.ScaledBase));
            Assert.NotEqual(dynamicBoundsBefore, AlphaBounds(bundle.DynamicContent.Content));
        }
    }

    [Fact]
    public void V2RuntimeSymbolGeometryIgnoresFontScaleWhileTextUsesIt()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = DarkSettings(SetAMappings());
        using DynamicRenderPair glyph = RenderDynamicPair(
            entry.Plan, settings, "selectedActionGlyph",
            Parameters(settings, fontScale: 1d), Parameters(settings, fontScale: 2d));
        using DynamicRenderPair label = RenderDynamicPair(
            entry.Plan, settings, "selectedActionLabel",
            Parameters(settings, fontScale: 1d), Parameters(settings, fontScale: 2d));

        Assert.Equal(glyph.FirstBounds, glyph.SecondBounds);
        Assert.Equal(RawPixelSha(glyph.First), RawPixelSha(glyph.Second));
        Assert.NotEqual(label.FirstBounds, label.SecondBounds);
        Assert.NotEqual(RawPixelSha(label.First), RawPixelSha(label.Second));
    }

    [Theory]
    [InlineData(RadialV5)]
    [InlineData(Radial8Minimal)]
    [InlineData(DarkFantasy)]
    public void TextStrengthMatrixChangesDynamicPixelsWithoutChangingGeometry(string themeId)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId);
        UniversalRadialParameters full = Parameters(settings, textStrength: 255);
        UniversalRadialParameters half = Parameters(settings, textStrength: 128);
        using RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, full), settings, 96);
        string before = RawPixelSha(bundle.GetFinalState(1));
        Size surface = bundle.PhysicalSurfaceSize;
        NormalizedPoint anchor = bundle.UniversalPlan.EffectiveActivationAnchor;
        string? artBefore = bundle.IsFullStateFrame ? null : RawPixelSha(bundle.AssetCache.ScaledBase);
        int? dynamicAlphaBefore = bundle.IsFullStateFrame ? null : MaxAlpha(bundle.DynamicContent.Content);

        Assert.True(bundle.EnsureUniversalContent(settings, half));

        Assert.NotEqual(before, RawPixelSha(bundle.GetFinalState(1)));
        Assert.Equal(surface, bundle.PhysicalSurfaceSize);
        Assert.Equal(anchor, bundle.UniversalPlan.EffectiveActivationAnchor);
        if (artBefore != null)
        {
            Assert.Equal(artBefore, RawPixelSha(bundle.AssetCache.ScaledBase));
            Assert.True(MaxAlpha(bundle.DynamicContent.Content) < dynamicAlphaBefore!.Value);
        }
    }

    [Fact]
    public void V2TextStrengthScalesRuntimeSymbolsAndPreservesTheirBounds()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = DarkSettings(SetAMappings());
        using DynamicRenderPair glyph = RenderDynamicPair(
            entry.Plan, settings, "selectedActionGlyph",
            Parameters(settings, textStrength: 255), Parameters(settings, textStrength: 128));

        Assert.Equal(glyph.FirstBounds, glyph.SecondBounds);
        Assert.True(MaxAlpha(glyph.Second) < MaxAlpha(glyph.First));
        Assert.NotEqual(RawPixelSha(glyph.First), RawPixelSha(glyph.Second));
    }

    [Fact]
    public void V2TextStrengthDoesNotModifyStateArtwork()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings empty = DarkSettings(RadialSlotMappings.Create(8));
        using RuntimeRenderBundle full = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, Parameters(empty, textStrength: 255)), empty, 96);
        using RuntimeRenderBundle half = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, Parameters(empty, textStrength: 128)), empty, 96);
        for (int slot = 0; slot <= 8; slot++)
            Assert.Equal(RawPixelSha(full.GetFinalState(slot)), RawPixelSha(half.GetFinalState(slot)));
    }

    [Fact]
    public void UniversalCacheKeyCoversScaleFontAlphaDpiMappingsAndFontEnvironment()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = DarkSettings(SetAMappings());
        using RuntimeRenderBundle baseline = RuntimeRenderBundle.BuildUniversal(
            UniversalPlan(entry, settings), settings, 96);
        using RuntimeRenderBundle scale = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, Parameters(settings, surfaceScale: 1.2d)), settings, 96);
        using RuntimeRenderBundle dpi = RuntimeRenderBundle.BuildUniversal(
            UniversalPlan(entry, settings), settings, 192);
        UniversalRadialCacheKey key = Assert.IsType<UniversalRadialCacheKey>(baseline.UniversalCacheKey);

        Assert.Equal(entry.Id, key.ThemeId);
        Assert.Equal(entry.Version, key.SourceProtocolVersion);
        Assert.Equal(entry.Plan.SourcePackageRevision, key.SourcePackageRevision);
        Assert.False(string.IsNullOrWhiteSpace(key.FontEnvironment));
        Assert.NotEqual(key, scale.UniversalCacheKey);
        Assert.NotEqual(key, dpi.UniversalCacheKey);

        Assert.True(baseline.EnsureUniversalContent(settings, Parameters(settings, fontScale: 2d)));
        UniversalRadialCacheKey font = baseline.UniversalCacheKey!;
        Assert.NotEqual(key, font);
        Assert.True(baseline.EnsureUniversalContent(settings, Parameters(settings, fontScale: 2d, textStrength: 128)));
        UniversalRadialCacheKey alpha = baseline.UniversalCacheKey!;
        Assert.NotEqual(font, alpha);
        RadialMenuSettings remapped = DarkSettings(SetBMappings());
        Assert.True(baseline.EnsureUniversalContent(remapped,
            Parameters(remapped, fontScale: 2d, textStrength: 128)));
        Assert.NotEqual(alpha, baseline.UniversalCacheKey);
    }

    [Fact]
    public void DynamicAndMappingRebuildsDoNotDecodeAuthoredAssets()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = DarkSettings(SetAMappings());
        using RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(
            UniversalPlan(entry, settings), settings, 96);
        int decoded = bundle.FullStateCache.DecodedAssetCount;
        int authored = bundle.FullStateCache.AuthoredLayerCount;

        Assert.True(bundle.EnsureUniversalContent(settings, Parameters(settings, fontScale: 2d)));
        Assert.Equal(decoded, bundle.FullStateCache.DecodedAssetCount);
        Assert.Equal(authored, bundle.FullStateCache.AuthoredLayerCount);
        RadialMenuSettings remapped = DarkSettings(SetBMappings());
        Assert.True(bundle.EnsureUniversalContent(remapped, Parameters(remapped, fontScale: 2d)));
        Assert.Equal(decoded, bundle.FullStateCache.DecodedAssetCount);
        Assert.Equal(authored, bundle.FullStateCache.AuthoredLayerCount);
        Assert.Equal(1, bundle.FullStateCache.MappingRebuildCount);
    }

    [Theory]
    [InlineData(RadialV5)]
    [InlineData(Radial8Minimal)]
    [InlineData(DarkFantasy)]
    public void SelectionHotPathReturnsPrebuiltStatesWithoutDynamicWork(string themeId)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId);
        using RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(
            UniversalPlan(entry, settings), settings, 96);
        int dynamicBuilds = bundle.IsFullStateFrame
            ? bundle.FullStateCache.DynamicBuildCount
            : bundle.DynamicContent.BuildCount;
        for (int iteration = 0; iteration < 20; iteration++)
            _ = bundle.GetFinalState(iteration % (entry.LayoutDefinition.SlotCount + 1));
        Assert.Equal(0, bundle.SelectionHotPathWorkCount);
        Assert.Equal(dynamicBuilds, bundle.IsFullStateFrame
            ? bundle.FullStateCache.DynamicBuildCount
            : bundle.DynamicContent.BuildCount);
        Assert.True(bundle.UniversalDiagnostics!.SelectionHotPathPrebuilt);
    }

    [Fact]
    public void ExplicitLegacyRuntimeRemainsAvailableForEmergencyRollback()
    {
        using var runtime = new RadialVisualPackRuntime(new RadialVisualPackCatalog(), RadialRenderPolicy.Legacy);
        RadialVisualPackSession session = Assert.IsType<RadialVisualPackSession>(
            runtime.Ensure(ThemeSettings(DarkFantasy), 280, 96));
        Assert.False(session.Bundle.IsUniversal);
        Assert.Null(session.Bundle.UniversalCacheKey);
    }

    [Fact]
    public void PerformanceAuditRecordsBuildAndTargetedRebuildWork()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = DarkSettings(SetAMappings());
        var neutralWatch = Stopwatch.StartNew();
        using RuntimeRenderBundle neutral = RuntimeRenderBundle.BuildUniversal(
            UniversalPlan(entry, settings), settings, 96);
        neutralWatch.Stop();
        var scaleWatch = Stopwatch.StartNew();
        using RuntimeRenderBundle scaled = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, Parameters(settings, surfaceScale: 1.2d)), settings, 96);
        scaleWatch.Stop();
        Assert.True(neutral.EnsureUniversalContent(settings, Parameters(settings, fontScale: 2d)));
        TimeSpan font = neutral.LastUniversalDynamicRebuild;
        Assert.True(neutral.EnsureUniversalContent(settings,
            Parameters(settings, fontScale: 2d, textStrength: 128)));
        TimeSpan alpha = neutral.LastUniversalDynamicRebuild;
        RadialMenuSettings remapped = DarkSettings(SetBMappings());
        int decoded = neutral.FullStateCache.DecodedAssetCount;
        Assert.True(neutral.EnsureUniversalContent(remapped,
            Parameters(remapped, fontScale: 2d, textStrength: 128)));
        TimeSpan mapping = neutral.LastUniversalDynamicRebuild;
        Assert.Equal(decoded, neutral.FullStateCache.DecodedAssetCount);

        _output.WriteLine($"neutral-build-ms={neutralWatch.Elapsed.TotalMilliseconds:F3}");
        _output.WriteLine($"scale-1.2-build-ms={scaleWatch.Elapsed.TotalMilliseconds:F3}");
        _output.WriteLine($"font-rebuild-ms={font.TotalMilliseconds:F3}");
        _output.WriteLine($"text-alpha-rebuild-ms={alpha.TotalMilliseconds:F3}");
        _output.WriteLine($"mapping-rebuild-ms={mapping.TotalMilliseconds:F3}");
        _output.WriteLine($"mapping-authored-decode-delta={neutral.FullStateCache.DecodedAssetCount - decoded}");
    }

    private static UniversalRadialRenderPlan UniversalPlan(
        RadialVisualPackCatalogEntry entry,
        RadialMenuSettings settings) => UniversalRadialRenderPlan.Create(
            entry.Plan,
            UniversalRadialSettingsNormalizer.Normalize(settings));

    private static UniversalRadialParameters Parameters(
        RadialMenuSettings settings,
        double surfaceScale = 1d,
        double fontScale = 1d,
        byte textStrength = 255) => new(
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
            byte.MaxValue);

    private static RadialVisualPackCatalogEntry Entry(string themeId)
    {
        RadialVisualPackCatalogSnapshot snapshot = HistoricalV2ThemeCatalog.Create().Discover();
        Assert.Empty(snapshot.Issues);
        return Assert.IsType<RadialVisualPackCatalogEntry>(snapshot.Find(themeId));
    }

    private static RadialMenuSettings ThemeSettings(string themeId)
    {
        string profile = themeId == RadialV5
            ? LayoutProfileRegistry.Radial6ProfileId
            : LayoutProfileRegistry.Radial8ProfileId;
        int slots = profile == LayoutProfileRegistry.Radial6ProfileId ? 6 : 8;
        RadialSlotMappings mappings = RadialSlotMappings.Create(slots,
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

    private static Bitmap ComposeProductionV1(RuntimeRenderBundle bundle, int selectedSlot)
    {
        var target = new Bitmap(bundle.TargetSize, bundle.TargetSize, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.DrawImageUnscaled(bundle.AssetCache.ScaledBase, 0, 0);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
        if (selectedSlot > 0)
            graphics.DrawImageUnscaled(bundle.AssetCache.GetSelectedSlot(selectedSlot), 0, 0);
        graphics.DrawImageUnscaled(bundle.DynamicContent.Content, 0, 0);
        return target;
    }

    private static DynamicRenderPair RenderDynamicPair(
        NormalizedRenderPlan plan,
        RadialMenuSettings settings,
        string contentKey,
        UniversalRadialParameters firstParameters,
        UniversalRadialParameters secondParameters)
    {
        NormalizedDynamicThemeModel dynamic = Assert.IsType<NormalizedDynamicThemeModel>(plan.DynamicTheme);
        FullStateFrameLayer layer = Assert.Single(
            Assert.IsType<FullStateFrameRenderPlan>(plan.RenderModel).OrderedLayers,
            candidate => dynamic.OwnershipByLayer.TryGetValue(candidate.Id, out NormalizedDynamicOwnership? ownership) &&
                ownership.ContentKey == contentKey);
        FullStateFrameState state = Assert.IsType<FullStateFrameRenderPlan>(plan.RenderModel).States["selected-3"];
        ThemeMappingSnapshot mappings = ThemeMappingSnapshot.Capture(settings, plan.LayoutProfileId);
        using ThemeFontSession fonts = ThemeFontResolver.Resolve(dynamic.FontRoles);
        var firstRasterizer = new ThemeDynamicRasterizer(plan, fonts, universalParameters: firstParameters);
        Bitmap first = firstRasterizer.RenderLayer(layer, state, mappings, 96,
            out ThemeDynamicLayoutResult firstSemantic);
        ThemeSemanticRect? firstBounds = firstSemantic.StyledBounds;
        firstSemantic.Dispose();
        var secondRasterizer = new ThemeDynamicRasterizer(plan, fonts, universalParameters: secondParameters);
        Bitmap second = secondRasterizer.RenderLayer(layer, state, mappings, 96,
            out ThemeDynamicLayoutResult secondSemantic);
        ThemeSemanticRect? secondBounds = secondSemantic.StyledBounds;
        secondSemantic.Dispose();
        return new(first, second, firstBounds, secondBounds);
    }

    private static byte MaxAlpha(Bitmap bitmap)
    {
        Rectangle rectangle = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            byte[] bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            byte maximum = 0;
            for (int offset = 3; offset < bytes.Length; offset += 4)
                maximum = Math.Max(maximum, bytes[offset]);
            return maximum;
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static Rectangle AlphaBounds(Bitmap bitmap)
    {
        Rectangle rectangle = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            byte[] bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            int left = bitmap.Width;
            int top = bitmap.Height;
            int right = -1;
            int bottom = -1;
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bytes[y * data.Stride + x * 4 + 3] == 0) continue;
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
            return right < left
                ? Rectangle.Empty
                : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }
        finally { bitmap.UnlockBits(data); }
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
        finally { bitmap.UnlockBits(data); }
    }

    private sealed record DynamicRenderPair(
        Bitmap First,
        Bitmap Second,
        ThemeSemanticRect? FirstBounds,
        ThemeSemanticRect? SecondBounds) : IDisposable
    {
        public void Dispose()
        {
            First.Dispose();
            Second.Dispose();
        }
    }

    private sealed class TemporarySettingsPath : IDisposable
    {
        public TemporarySettingsPath()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "LeftPad.UniversalRadialPhase1.Tests",
                Guid.NewGuid().ToString("N"));
            FilePath = Path.Combine(DirectoryPath, "radial-menu-settings.json");
        }

        public string DirectoryPath { get; }
        public string FilePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
