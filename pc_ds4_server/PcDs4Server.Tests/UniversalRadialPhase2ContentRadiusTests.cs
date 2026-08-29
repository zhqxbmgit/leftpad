using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Xunit;
using Xunit.Abstractions;

namespace PcDs4Server.Tests;

public sealed class UniversalRadialPhase2ContentRadiusTests
{
    private const string RadialV5 = "radial-v5";
    private const string Radial8Minimal = "radial-8-minimal-v1";
    private const string DarkFantasy = "dark-fantasy-radial8-v1";
    private readonly ITestOutputHelper _output;

    public UniversalRadialPhase2ContentRadiusTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void GroupModelSeparatesOuterSlotAndSelectedCenterOwnership()
    {
        UniversalRadialRenderPlan radial6 = Plan(Entry(RadialV5), ThemeSettings(RadialV5));
        Assert.Equal(6, radial6.DynamicContentGroups.Count);
        Assert.All(radial6.DynamicContentGroups, group =>
        {
            Assert.Equal(UniversalDynamicContentGroupRole.OuterSlotContent, group.Role);
            Assert.NotNull(group.SlotId);
            Assert.NotNull(group.SlotCenterAngleDegrees);
            UniversalDynamicContentMember member = Assert.Single(group.Members);
            Assert.Equal("text", member.Role);
            Assert.Equal(0d, member.LocalOffsetX);
            Assert.Equal(0d, member.LocalOffsetY);
        });

        UniversalRadialRenderPlan dark = Plan(Entry(DarkFantasy), DarkSettings(SetAMappings()));
        UniversalDynamicContentGroup[] outer = dark.DynamicContentGroups
            .Where(group => group.Role == UniversalDynamicContentGroupRole.OuterSlotContent)
            .OrderBy(group => group.SlotId)
            .ToArray();
        Assert.Equal(8, outer.Length);
        Assert.All(outer, group =>
        {
            Assert.Equal(2, group.Members.Count);
            Assert.True(group.AuthoredRadialPosition > 0d);
            Assert.Contains(group.Members, member => member.Role == "glyph");
            Assert.Contains(group.Members, member => member.Role == "text");
            Assert.All(group.Members, member =>
            {
                Assert.False(string.IsNullOrWhiteSpace(member.HorizontalAlignment));
                Assert.False(string.IsNullOrWhiteSpace(member.VerticalAlignment));
                Assert.False(string.IsNullOrWhiteSpace(member.OverflowPolicy));
            });
        });
        UniversalDynamicContentGroup center = Assert.Single(dark.DynamicContentGroups,
            group => group.Role == UniversalDynamicContentGroupRole.SelectedCenterContent);
        Assert.Equal("selected-center", center.Identity);
        Assert.Null(center.SlotId);
        Assert.Null(center.SlotCenterAngleDegrees);
        Assert.Equal(2, center.Members.Count);
        Assert.Contains(36d, outer.SelectMany(group => group.Members).Select(member => member.Rotation));
        Assert.Contains(82d, outer.SelectMany(group => group.Members).Select(member => member.Rotation));
        Assert.Contains(-36d, outer.SelectMany(group => group.Members).Select(member => member.Rotation));
        Assert.Contains(-82d, outer.SelectMany(group => group.Members).Select(member => member.Rotation));
    }

    [Theory]
    [InlineData(0d, 0d, -1d)]
    [InlineData(45d, 0.7071067811865476d, -0.7071067811865476d)]
    [InlineData(90d, 1d, 0d)]
    [InlineData(180d, 0d, 1d)]
    [InlineData(270d, -1d, 0d)]
    public void RadialDirectionUsesClockwiseFromTopLayoutAuthority(
        double angle,
        double expectedX,
        double expectedY)
    {
        NormalizedPoint direction = UniversalDynamicContentTransform.RadialUnitVector(angle);
        Assert.Equal(expectedX, direction.X, precision: 12);
        Assert.Equal(expectedY, direction.Y, precision: 12);
        Assert.Equal(1d, Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y), precision: 12);
    }

    [Theory]
    [InlineData(1, 0d, 0d, -1d)]
    [InlineData(2, 60d, 0.8660254037844386d, -0.5d)]
    [InlineData(3, 120d, 0.8660254037844386d, 0.5d)]
    [InlineData(4, 180d, 0d, 1d)]
    [InlineData(5, 240d, -0.8660254037844386d, 0.5d)]
    [InlineData(6, 300d, -0.8660254037844386d, -0.5d)]
    public void RadialSixGroupsUseTheSameLayoutDirectionMath(
        int slot,
        double angle,
        double expectedX,
        double expectedY)
    {
        UniversalDynamicContentGroup group = Plan(Entry(RadialV5), ThemeSettings(RadialV5))
            .GetOuterDynamicContentGroup(slot);
        Assert.Equal(angle, group.SlotCenterAngleDegrees);
        Assert.Equal(expectedX, group.RadialUnitVector.X, precision: 12);
        Assert.Equal(expectedY, group.RadialUnitVector.Y, precision: 12);
    }

    [Theory]
    [InlineData(RadialV5, 83d, 10d)]
    [InlineData(RadialV5, 63d, -10d)]
    [InlineData(Radial8Minimal, 83d, 10d)]
    [InlineData(Radial8Minimal, 63d, -10d)]
    [InlineData(DarkFantasy, 83d, 15d)]
    [InlineData(DarkFantasy, 63d, -15d)]
    public void CruDeltaConvertsThroughNominalThemeLogicalWidth(
        string themeId,
        double radius,
        double expectedLogicalDelta)
    {
        NormalizedRenderPlan source = Entry(themeId).Plan;
        Assert.Equal(expectedLogicalDelta,
            UniversalDynamicContentTransform.NominalLogicalRadialDelta(source, radius),
            precision: 12);
    }

    [Theory]
    [InlineData(RadialV5, 24d)]
    [InlineData(Radial8Minimal, 24d)]
    [InlineData(DarkFantasy, 36d)]
    public void SurfaceScaleAndDpiApplyAfterCruToNominalConversion(
        string themeId,
        double expectedPhysicalMagnitude)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId);
        UniversalRadialRenderPlan plan = UniversalRadialRenderPlan.Create(
            entry.Plan,
            Parameters(settings, radius: 83d, surfaceScale: 1.2d));
        UniversalDynamicTranslation translation =
            UniversalDynamicContentTransform.EffectivePhysicalTranslation(
                plan,
                plan.GetOuterDynamicContentGroup(2),
                192);
        Assert.Equal(expectedPhysicalMagnitude, translation.Magnitude, precision: 10);
    }

    [Theory]
    [InlineData(RadialV5)]
    [InlineData(Radial8Minimal)]
    [InlineData(DarkFantasy)]
    public void NeutralRadiusIsPixelExactWithPhaseOneUniversalOutput(string themeId)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = themeId == DarkFantasy
            ? DarkSettings(SetAMappings())
            : ThemeSettings(themeId);
        using RuntimeRenderBundle phaseOne = RuntimeRenderBundle.BuildUniversal(
            Plan(entry, settings), settings, 96);
        using RuntimeRenderBundle phaseTwo = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, Parameters(settings, radius: 73d)),
            settings,
            96);
        for (int slot = 0; slot <= entry.LayoutDefinition.SlotCount; slot++)
            Assert.Equal(RawPixelSha(phaseOne.GetFinalState(slot)), RawPixelSha(phaseTwo.GetFinalState(slot)));
    }

    [Theory]
    [InlineData(RadialV5, 83d)]
    [InlineData(RadialV5, 63d)]
    [InlineData(Radial8Minimal, 83d)]
    [InlineData(Radial8Minimal, 63d)]
    public void V1RadiusMatrixMovesOnlyDynamicContent(string themeId, double radius)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = SingleVisibleSlotSettings(themeId, slot: 1, Keyboard(KeyboardKey.E));
        UniversalRadialParameters neutral = Parameters(settings, radius: 73d);
        UniversalRadialParameters changed = Parameters(settings, radius: radius);
        using RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, neutral), settings, 96);
        string beforeState = RawPixelSha(bundle.GetFinalState(1));
        string baseArt = RawPixelSha(bundle.AssetCache.ScaledBase);
        string selectedArt = RawPixelSha(bundle.AssetCache.GetSelectedSlot(1));
        Size surface = bundle.PhysicalSurfaceSize;
        NormalizedPoint anchor = bundle.UniversalPlan.EffectiveActivationAnchor;
        int decoded = bundle.AssetCache.DecodedAssetCount;

        Assert.True(bundle.EnsureUniversalContent(settings, changed));

        UniversalDynamicTranslation expected = UniversalDynamicContentTransform.AuthoredGeometryTranslation(
            bundle.UniversalPlan,
            bundle.UniversalPlan.GetOuterDynamicContentGroup(1));
        UniversalDynamicTranslation applied = bundle.DynamicContent.LastUniversalSlotTranslations[1];
        Assert.Equal(expected.X, applied.X, precision: 10);
        Assert.Equal(expected.Y, applied.Y, precision: 10);
        Assert.NotEqual(beforeState, RawPixelSha(bundle.GetFinalState(1)));
        Assert.Equal(baseArt, RawPixelSha(bundle.AssetCache.ScaledBase));
        Assert.Equal(selectedArt, RawPixelSha(bundle.AssetCache.GetSelectedSlot(1)));
        Assert.Equal(surface, bundle.PhysicalSurfaceSize);
        Assert.Equal(anchor, bundle.UniversalPlan.EffectiveActivationAnchor);
        Assert.Equal(decoded, bundle.AssetCache.DecodedAssetCount);
        Assert.Equal(
            RadialSelectionEngine.GetSelectedSlot(entry.LayoutDefinition, 60d, 10d, 28d),
            RadialSelectionEngine.GetSelectedSlot(bundle.UniversalPlan.LayoutDefinition, 60d, 10d, 28d));
    }

    [Theory]
    [InlineData(3, "slot3ActionGlyph", "slot3ActionLabel")]
    [InlineData(6, "slot6ActionGlyph", "slot6ActionLabel")]
    [InlineData(8, "slot8ActionGlyph", "slot8ActionLabel")]
    public void V2GlyphAndLabelShareOneContinuousGroupTranslation(
        int slot,
        string glyphKey,
        string labelKey)
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = DarkSettings(SetAMappings());
        UniversalRadialParameters neutral = Parameters(settings, radius: 73d);
        UniversalRadialParameters outward = Parameters(settings, radius: 83d);
        using DynamicRenderPair glyph = RenderDynamicPair(entry.Plan, settings, glyphKey, neutral, outward);
        using DynamicRenderPair label = RenderDynamicPair(entry.Plan, settings, labelKey, neutral, outward);
        Assert.True(glyph.FirstDraw);
        Assert.True(glyph.SecondDraw);
        Assert.True(label.FirstDraw);
        Assert.True(label.SecondDraw);

        UniversalRadialRenderPlan outwardPlan = UniversalRadialRenderPlan.Create(entry.Plan, outward);
        UniversalDynamicTranslation expected = UniversalDynamicContentTransform.AuthoredGeometryTranslation(
            outwardPlan,
            outwardPlan.GetOuterDynamicContentGroup(slot));
        NormalizedPoint glyphDelta = CenterDelta(glyph.FirstBounds!.Value, glyph.SecondBounds!.Value);
        NormalizedPoint labelDelta = CenterDelta(label.FirstBounds!.Value, label.SecondBounds!.Value);
        Assert.Equal(expected.X, glyphDelta.X, precision: 3);
        Assert.Equal(expected.Y, glyphDelta.Y, precision: 3);
        Assert.Equal(expected.X, labelDelta.X, precision: 3);
        Assert.Equal(expected.Y, labelDelta.Y, precision: 3);
        Assert.Equal(glyphDelta.X, labelDelta.X, precision: 3);
        Assert.Equal(glyphDelta.Y, labelDelta.Y, precision: 3);

        NormalizedPoint relativeBefore = CenterDelta(glyph.FirstBounds.Value, label.FirstBounds.Value);
        NormalizedPoint relativeAfter = CenterDelta(glyph.SecondBounds.Value, label.SecondBounds.Value);
        Assert.Equal(relativeBefore.X, relativeAfter.X, precision: 3);
        Assert.Equal(relativeBefore.Y, relativeAfter.Y, precision: 3);
        Assert.Equal(glyph.FirstAnchor.Rotation, glyph.SecondAnchor.Rotation);
        Assert.Equal(label.FirstAnchor.Rotation, label.SecondAnchor.Rotation);
        Assert.Equal(glyph.FirstAnchor.Bounds.Width, glyph.SecondAnchor.Bounds.Width);
        Assert.Equal(label.FirstAnchor.Bounds.Height, label.SecondAnchor.Bounds.Height);
    }

    [Theory]
    [InlineData(83d)]
    [InlineData(63d)]
    public void V2RadiusMatrixMovesOuterContentInBothDirections(double radius)
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = DarkSettings(SetAMappings());
        UniversalRadialParameters neutral = Parameters(settings, radius: 73d);
        UniversalRadialParameters changed = Parameters(settings, radius: radius);
        using DynamicRenderPair label = RenderDynamicPair(
            entry.Plan, settings, "slot3ActionLabel", neutral, changed);
        UniversalRadialRenderPlan changedPlan = UniversalRadialRenderPlan.Create(entry.Plan, changed);
        UniversalDynamicTranslation expected = UniversalDynamicContentTransform.AuthoredGeometryTranslation(
            changedPlan,
            changedPlan.GetOuterDynamicContentGroup(3));
        NormalizedPoint actual = CenterDelta(label.FirstBounds!.Value, label.SecondBounds!.Value);
        Assert.Equal(expected.X, actual.X, precision: 3);
        Assert.Equal(expected.Y, actual.Y, precision: 3);

        using RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, neutral), settings, 96);
        string before = RawPixelSha(bundle.GetFinalState(3));
        int decoded = bundle.FullStateCache.DecodedAssetCount;
        Size surface = bundle.PhysicalSurfaceSize;
        NormalizedPoint anchor = bundle.UniversalPlan.EffectiveActivationAnchor;
        Assert.True(bundle.EnsureUniversalContent(settings, changed));
        Assert.NotEqual(before, RawPixelSha(bundle.GetFinalState(3)));
        Assert.Equal(decoded, bundle.FullStateCache.DecodedAssetCount);
        Assert.Equal(surface, bundle.PhysicalSurfaceSize);
        Assert.Equal(anchor, bundle.UniversalPlan.EffectiveActivationAnchor);
        Assert.Equal(
            RadialSelectionEngine.GetSelectedSlot(entry.LayoutDefinition, 60d, 10d, 28d),
            RadialSelectionEngine.GetSelectedSlot(bundle.UniversalPlan.LayoutDefinition, 60d, 10d, 28d));
    }

    [Theory]
    [InlineData("selectedActionGlyph")]
    [InlineData("selectedActionLabel")]
    public void SelectedCenterContentIsUnaffectedBySlotContentRadius(string contentKey)
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = DarkSettings(SetAMappings());
        using DynamicRenderPair pair = RenderDynamicPair(
            entry.Plan,
            settings,
            contentKey,
            Parameters(settings, radius: 73d),
            Parameters(settings, radius: 93d));
        Assert.Equal(pair.FirstAnchor, pair.SecondAnchor);
        Assert.Equal(pair.FirstBounds, pair.SecondBounds);
        Assert.Equal(RawPixelSha(pair.First), RawPixelSha(pair.Second));
    }

    [Theory]
    [InlineData(1, KeyboardKey.E, false)]
    [InlineData(2, KeyboardKey.K, true)]
    [InlineData(4, KeyboardKey.Tab, false)]
    [InlineData(7, KeyboardKey.F1, false)]
    public void KeyboardGlyphRemainsNoDrawWhileItsLabelMoves(
        int slot,
        KeyboardKey key,
        bool shortcut)
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialSlotMapping mapping = shortcut
            ? Shortcut(key, ctrl: true, shift: true)
            : Keyboard(key);
        RadialMenuSettings settings = SingleVisibleSlotSettings(DarkFantasy, slot, mapping);
        UniversalRadialParameters neutral = Parameters(settings, radius: 73d);
        UniversalRadialParameters outward = Parameters(settings, radius: 83d);
        using DynamicRenderPair glyph = RenderDynamicPair(entry.Plan, settings,
            $"slot{slot}ActionGlyph", neutral, outward);
        using DynamicRenderPair label = RenderDynamicPair(entry.Plan, settings,
            $"slot{slot}ActionLabel", neutral, outward);
        Assert.False(glyph.FirstDraw);
        Assert.False(glyph.SecondDraw);
        Assert.Equal(RawPixelSha(glyph.First), RawPixelSha(glyph.Second));
        Assert.True(label.FirstDraw);
        Assert.True(label.SecondDraw);
        Assert.NotEqual(RawPixelSha(label.First), RawPixelSha(label.Second));
        UniversalDynamicTranslation expected = UniversalDynamicContentTransform.AuthoredGeometryTranslation(
            UniversalRadialRenderPlan.Create(entry.Plan, outward),
            UniversalRadialRenderPlan.Create(entry.Plan, outward).GetOuterDynamicContentGroup(slot));
        NormalizedPoint actual = CenterDelta(label.FirstBounds!.Value, label.SecondBounds!.Value);
        Assert.Equal(expected.X, actual.X, precision: 3);
        Assert.Equal(expected.Y, actual.Y, precision: 3);
    }

    [Fact]
    public void NoneSlotRemainsNoDrawAtEveryRadius()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = SingleVisibleSlotSettings(DarkFantasy, 5, RadialSlotMapping.None);
        foreach (string key in new[] { "slot5ActionGlyph", "slot5ActionLabel" })
        {
            using DynamicRenderPair pair = RenderDynamicPair(
                entry.Plan,
                settings,
                key,
                Parameters(settings, radius: 63d),
                Parameters(settings, radius: 83d));
            Assert.False(pair.FirstDraw);
            Assert.False(pair.SecondDraw);
            Assert.Equal(Rectangle.Empty, AlphaBounds(pair.First));
            Assert.Equal(Rectangle.Empty, AlphaBounds(pair.Second));
        }
    }

    [Fact]
    public void V2RadiusRebuildKeepsAuthoredAssetsSurfaceAnchorAndArtworkStable()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = DarkSettings(SetAMappings());
        using RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, Parameters(settings, radius: 73d)),
            settings,
            96);
        int decoded = bundle.FullStateCache.DecodedAssetCount;
        Assert.Equal(9, decoded);
        int authored = bundle.FullStateCache.AuthoredLayerCount;
        Size surface = bundle.PhysicalSurfaceSize;
        NormalizedPoint anchor = bundle.UniversalPlan.EffectiveActivationAnchor;
        string before = RawPixelSha(bundle.GetFinalState(3));

        Assert.True(bundle.EnsureUniversalContent(settings, Parameters(settings, radius: 83d)));

        Assert.NotEqual(before, RawPixelSha(bundle.GetFinalState(3)));
        Assert.Equal(decoded, bundle.FullStateCache.DecodedAssetCount);
        Assert.Equal(authored, bundle.FullStateCache.AuthoredLayerCount);
        Assert.Equal(surface, bundle.PhysicalSurfaceSize);
        Assert.Equal(anchor, bundle.UniversalPlan.EffectiveActivationAnchor);
        Assert.Equal(83d, bundle.UniversalCacheKey!.SlotContentRadiusCru);

        RadialMenuSettings empty = DarkSettings(RadialSlotMappings.Create(8));
        using RuntimeRenderBundle neutralArtwork = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, Parameters(empty, radius: 73d)), empty, 96);
        using RuntimeRenderBundle movedArtwork = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, Parameters(empty, radius: 83d)), empty, 96);
        for (int slot = 0; slot <= 8; slot++)
            Assert.Equal(RawPixelSha(neutralArtwork.GetFinalState(slot)), RawPixelSha(movedArtwork.GetFinalState(slot)));
    }

    [Fact]
    public void RadiusComposesIndependentlyWithFontAndTextStrength()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = DarkSettings(SetAMappings());
        using DynamicRenderPair fontPair = RenderDynamicPair(
            entry.Plan,
            settings,
            "slot3ActionLabel",
            Parameters(settings, radius: 73d, fontScale: 1d),
            Parameters(settings, radius: 83d, fontScale: 2d));
        Assert.True(fontPair.SecondBounds!.Value.Width > fontPair.FirstBounds!.Value.Width);
        Assert.True(Center(fontPair.SecondBounds.Value).X > Center(fontPair.FirstBounds.Value).X);

        using DynamicRenderPair alphaPair = RenderDynamicPair(
            entry.Plan,
            settings,
            "slot3ActionGlyph",
            Parameters(settings, radius: 73d, textStrength: 255),
            Parameters(settings, radius: 63d, textStrength: 128));
        Assert.True(Center(alphaPair.SecondBounds!.Value).X < Center(alphaPair.FirstBounds!.Value).X);
        Assert.True(MaxAlpha(alphaPair.Second) < MaxAlpha(alphaPair.First));
    }

    [Fact]
    public void MappingRebuildAtNonNeutralRadiusPreservesTranslationAndAuthoredDecodeCount()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings setA = DarkSettings(SetAMappings());
        UniversalRadialParameters radius = Parameters(setA, radius: 83d);
        using RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, radius), setA, 96);
        int decoded = bundle.FullStateCache.DecodedAssetCount;
        string before = RawPixelSha(bundle.GetFinalState(3));
        UniversalDynamicTranslation translationBefore = UniversalDynamicContentTransform.AuthoredGeometryTranslation(
            bundle.UniversalPlan,
            bundle.UniversalPlan.GetOuterDynamicContentGroup(3));
        RadialMenuSettings setB = DarkSettings(SetBMappings());

        Assert.True(bundle.EnsureUniversalContent(setB, Parameters(setB, radius: 83d)));

        UniversalDynamicTranslation translationAfter = UniversalDynamicContentTransform.AuthoredGeometryTranslation(
            bundle.UniversalPlan,
            bundle.UniversalPlan.GetOuterDynamicContentGroup(3));
        Assert.Equal(translationBefore, translationAfter);
        Assert.NotEqual(before, RawPixelSha(bundle.GetFinalState(3)));
        Assert.Equal(decoded, bundle.FullStateCache.DecodedAssetCount);
        Assert.Equal(1, bundle.FullStateCache.MappingRebuildCount);
        Assert.Equal(83d, bundle.UniversalPlan.Parameters.SlotContentRadiusCru);
    }

    [Fact]
    public void ContentRadiusPerformanceAuditRecordsTargetedRebuildsAndZeroHotPathWork()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings setA = DarkSettings(SetAMappings());
        using RuntimeRenderBundle bundle = RuntimeRenderBundle.BuildUniversal(
            UniversalRadialRenderPlan.Create(entry.Plan, Parameters(setA, radius: 73d)), setA, 96);
        int decoded = bundle.FullStateCache.DecodedAssetCount;

        Assert.True(bundle.EnsureUniversalContent(setA, Parameters(setA, radius: 83d)));
        TimeSpan radius = bundle.LastUniversalDynamicRebuild;
        RadialMenuSettings setB = DarkSettings(SetBMappings());
        Assert.True(bundle.EnsureUniversalContent(setB, Parameters(setB, radius: 83d)));
        TimeSpan mapping = bundle.LastUniversalDynamicRebuild;
        Assert.True(bundle.EnsureUniversalContent(setB,
            Parameters(setB, radius: 83d, fontScale: 2d)));
        TimeSpan fontAndRadius = bundle.LastUniversalDynamicRebuild;
        Assert.True(bundle.EnsureUniversalContent(setB,
            Parameters(setB, radius: 63d, fontScale: 2d, textStrength: 128)));
        TimeSpan alphaAndRadius = bundle.LastUniversalDynamicRebuild;

        int dynamicBuilds = bundle.FullStateCache.DynamicBuildCount;
        for (int iteration = 0; iteration < 32; iteration++)
            _ = bundle.GetFinalState(iteration % 9);
        Assert.Equal(dynamicBuilds, bundle.FullStateCache.DynamicBuildCount);
        Assert.Equal(0, bundle.SelectionHotPathWorkCount);
        Assert.Equal(decoded, bundle.FullStateCache.DecodedAssetCount);

        _output.WriteLine($"text-radius-rebuild-ms={radius.TotalMilliseconds:F3}");
        _output.WriteLine($"mapping-at-radius-rebuild-ms={mapping.TotalMilliseconds:F3}");
        _output.WriteLine($"font-plus-radius-rebuild-ms={fontAndRadius.TotalMilliseconds:F3}");
        _output.WriteLine($"alpha-plus-radius-rebuild-ms={alphaAndRadius.TotalMilliseconds:F3}");
        _output.WriteLine($"authored-decode-delta={bundle.FullStateCache.DecodedAssetCount - decoded}");
        _output.WriteLine("selection-hot-path-work=0");
    }

    private static UniversalRadialRenderPlan Plan(
        RadialVisualPackCatalogEntry entry,
        RadialMenuSettings settings) => UniversalRadialRenderPlan.Create(
            entry.Plan,
            UniversalRadialSettingsNormalizer.Normalize(settings));

    private static UniversalRadialParameters Parameters(
        RadialMenuSettings settings,
        double radius,
        double surfaceScale = 1d,
        double fontScale = 1d,
        byte textStrength = 255) => new(
            RadialSettingsSemanticRevision.Universal,
            surfaceScale,
            settings.HubRadius,
            settings.PetalInnerRadius,
            settings.PetalOuterRadius,
            settings.PetalGapDegrees,
            radius,
            fontScale,
            byte.MaxValue,
            byte.MaxValue,
            textStrength,
            byte.MaxValue);

    private static RadialVisualPackCatalogEntry Entry(string themeId)
    {
        RadialVisualPackCatalogSnapshot snapshot = new RadialVisualPackCatalog().Discover();
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

    private static RadialMenuSettings SingleVisibleSlotSettings(
        string themeId,
        int slot,
        RadialSlotMapping mapping)
    {
        RadialMenuSettings settings = ThemeSettings(themeId);
        int slots = settings.MappingProfileId == LayoutProfileRegistry.Radial6ProfileId ? 6 : 8;
        RadialSlotMapping[] values = Enumerable.Repeat(RadialSlotMapping.None, slots).ToArray();
        values[slot - 1] = mapping;
        return settings.SetProfileMappings(settings.MappingProfileId, RadialSlotMappings.Create(slots, values));
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

        NormalizedDynamicAnchor authored = dynamic.AnchorForLayer(layer.Id);
        UniversalRadialRenderPlan firstPlan = UniversalRadialRenderPlan.Create(plan, firstParameters);
        NormalizedDynamicAnchor firstAnchor = UniversalDynamicContentTransform.TranslateAnchor(firstPlan, layer.Id, authored);
        var firstRasterizer = new ThemeDynamicRasterizer(plan, fonts, universalParameters: firstParameters);
        Bitmap first = firstRasterizer.RenderLayer(layer, state, mappings, 96,
            out ThemeDynamicLayoutResult firstSemantic);
        bool firstDraw = firstSemantic.Draw;
        ThemeSemanticRect? firstBounds = firstSemantic.StyledBounds;
        firstSemantic.Dispose();

        UniversalRadialRenderPlan secondPlan = UniversalRadialRenderPlan.Create(plan, secondParameters);
        NormalizedDynamicAnchor secondAnchor = UniversalDynamicContentTransform.TranslateAnchor(secondPlan, layer.Id, authored);
        var secondRasterizer = new ThemeDynamicRasterizer(plan, fonts, universalParameters: secondParameters);
        Bitmap second = secondRasterizer.RenderLayer(layer, state, mappings, 96,
            out ThemeDynamicLayoutResult secondSemantic);
        bool secondDraw = secondSemantic.Draw;
        ThemeSemanticRect? secondBounds = secondSemantic.StyledBounds;
        secondSemantic.Dispose();
        return new(first, second, firstBounds, secondBounds, firstDraw, secondDraw, firstAnchor, secondAnchor);
    }

    private static NormalizedPoint Center(ThemeSemanticRect bounds) =>
        new((bounds.Left + bounds.Right) / 2d, (bounds.Top + bounds.Bottom) / 2d);

    private static NormalizedPoint CenterDelta(ThemeSemanticRect first, ThemeSemanticRect second)
    {
        NormalizedPoint a = Center(first);
        NormalizedPoint b = Center(second);
        return new(b.X - a.X, b.Y - a.Y);
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
            int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;
            for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bytes[y * data.Stride + x * 4 + 3] == 0) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
            return right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
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
        ThemeSemanticRect? SecondBounds,
        bool FirstDraw,
        bool SecondDraw,
        NormalizedDynamicAnchor FirstAnchor,
        NormalizedDynamicAnchor SecondAnchor) : IDisposable
    {
        public void Dispose()
        {
            First.Dispose();
            Second.Dispose();
        }
    }
}
