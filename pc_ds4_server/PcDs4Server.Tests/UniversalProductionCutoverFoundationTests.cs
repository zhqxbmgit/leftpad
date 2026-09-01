using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace PcDs4Server.Tests;

[Collection(UniversalRadialPhase3Collection.Name)]
public sealed class UniversalProductionCutoverFoundationTests
{
    private const string RadialV5 = "radial-v5";
    private const string Radial8Minimal = "radial-8-minimal-v1";
    private const string DarkFantasy = HistoricalV2ThemeCatalog.ReferenceThemeId;

    public static TheoryData<string> FormalThemes => new()
    {
        RadialV5,
        Radial8Minimal,
        DarkFantasy
    };

    public static TheoryData<string, string> HiddenFieldCases
    {
        get
        {
            var values = new TheoryData<string, string>();
            foreach (string themeId in new[] { RadialV5, DarkFantasy })
            foreach (string scenario in new[]
                     {
                         "default",
                         "text-alpha",
                         "text-radius",
                         "highlight",
                         "fill-border",
                         "geometry",
                         "base",
                         "combined"
                     })
                values.Add(themeId, scenario);
            return values;
        }
    }

    [Fact]
    public void ProductionDefaultIsUniversalAndLegacyRollbackRemainsExplicit()
    {
        Assert.Equal(RadialRenderPolicy.UniversalInitial, RadialRenderPolicyAuthority.ProductionDefault);
        using var production = new RadialVisualPackRuntime(new RadialVisualPackCatalog());
        using var rollback = new RadialVisualPackRuntime(
            new RadialVisualPackCatalog(),
            RadialRenderPolicy.Legacy);

        Assert.Equal(RadialRenderPolicy.UniversalInitial, production.RenderPolicy);
        Assert.Equal(RadialRenderPolicy.Legacy, rollback.RenderPolicy);
    }

    [Theory]
    [InlineData(280, 120, 336, 1.2)]
    [InlineData(336, 100, 336, 1.0)]
    [InlineData(320, 125, 400, 1.25)]
    public void LegacyV1AdapterPreservesSurfaceFontAndTypographyFloor(
        int baseCanvas,
        int scalePercent,
        int expectedSurface,
        double expectedProductScale)
    {
        RadialVisualPackCatalogEntry entry = Entry(RadialV5);
        RadialMenuSettings settings = ThemeSettings(RadialV5) with
        {
            BaseCanvasSize = baseCanvas,
            ScalePercent = scalePercent,
            FontSize = 15f
        };

        UniversalRadialParameters parameters =
            ProductionUniversalRadialParametersAdapter.Adapt(settings, entry.Plan);
        UniversalRadialRenderPlan plan = UniversalRadialRenderPlan.Create(entry.Plan, parameters);

        Assert.Equal(baseCanvas / 280d * expectedProductScale, parameters.SurfaceScale, 12);
        Assert.Equal(280d / baseCanvas, parameters.FontScale, 12);
        Assert.Equal(expectedProductScale, parameters.DynamicContentScale, 12);
        Assert.Equal(expectedProductScale, parameters.SurfaceScale * parameters.FontScale, 12);
        Assert.Equal(new Size(expectedSurface, expectedSurface), plan.PhysicalSurfaceSize(96));
    }

    [Theory]
    [InlineData(280, 60, 6)]
    [InlineData(336, 60, 6)]
    [InlineData(320, 100, 15)]
    [InlineData(336, 100, 48)]
    [InlineData(280, 140, 6)]
    [InlineData(320, 140, 48)]
    public void LegacyV1FontAndFloorRemainIndependentOfBaseCanvas(
        int baseCanvas,
        int scalePercent,
        float fontSize)
    {
        RadialVisualPackCatalogEntry entry = Entry(RadialV5);
        RadialMenuSettings settings = ThemeSettings(RadialV5) with
        {
            BaseCanvasSize = baseCanvas,
            ScalePercent = scalePercent,
            FontSize = fontSize
        };

        UniversalRadialParameters parameters =
            ProductionUniversalRadialParametersAdapter.Adapt(settings, entry.Plan);
        double productScale = scalePercent / 100d;
        double currentPreferredFontFactor = fontSize / 15d * productScale;

        Assert.Equal(
            currentPreferredFontFactor,
            parameters.SurfaceScale * parameters.FontScale,
            12);
        Assert.Equal(productScale, parameters.DynamicContentScale, 12);
    }

    [Fact]
    public void AdapterPreservesOnlyHistoricallyEffectiveLegacyV1HiddenValues()
    {
        RadialVisualPackCatalogEntry entry = Entry(RadialV5);
        RadialMenuSettings settings = ThemeSettings(RadialV5) with
        {
            BaseCanvasSize = 320,
            ScalePercent = 125,
            TextAlpha = 117,
            TextRadius = 90,
            HighlightAlpha = 40,
            FillAlpha = 109,
            BorderAlpha = 50,
            HubRadius = 30,
            PetalInnerRadius = 40,
            PetalOuterRadius = 120,
            PetalGapDegrees = 8f
        };
        string before = JsonSerializer.Serialize(settings);

        UniversalRadialParameters parameters =
            ProductionUniversalRadialParametersAdapter.Adapt(settings, entry.Plan);

        Assert.Equal(10d / 7d, parameters.SurfaceScale, 12);
        Assert.Equal((byte)127, parameters.TextStrength);
        AssertNeutralRemovedFields(parameters);
        Assert.Equal(before, JsonSerializer.Serialize(settings));
    }

    [Fact]
    public void AdapterNeutralizesAllRemovedLegacyV2VisualFields()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = ThemeSettings(DarkFantasy) with
        {
            BaseCanvasSize = 320,
            ScalePercent = 140,
            FontSize = 48,
            TextAlpha = 117,
            TextRadius = 90,
            HighlightAlpha = 40,
            FillAlpha = 109,
            BorderAlpha = 50,
            HubRadius = 30,
            PetalInnerRadius = 40,
            PetalOuterRadius = 120,
            PetalGapDegrees = 8f
        };

        UniversalRadialParameters parameters =
            ProductionUniversalRadialParametersAdapter.Adapt(settings, entry.Plan);

        Assert.Equal(1.4d, parameters.SurfaceScale, 12);
        Assert.Equal(3.2d, parameters.FontScale, 12);
        Assert.Equal(1.4d, parameters.DynamicContentScale, 12);
        Assert.Equal(byte.MaxValue, parameters.TextStrength);
        AssertNeutralRemovedFields(parameters);
    }

    [Theory]
    [MemberData(nameof(HiddenFieldCases))]
    public void SyntheticHiddenFieldMatrixGrantsOnlyHistoricalV1Authority(
        string themeId,
        string scenario)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings baselineSettings = ThemeSettings(themeId);
        RadialMenuSettings candidateSettings = scenario switch
        {
            "default" => baselineSettings,
            "text-alpha" => baselineSettings with { TextAlpha = 117 },
            "text-radius" => baselineSettings with { TextRadius = 90 },
            "highlight" => baselineSettings with { HighlightAlpha = 40 },
            "fill-border" => baselineSettings with { FillAlpha = 109, BorderAlpha = 50 },
            "geometry" => baselineSettings with
            {
                HubRadius = 30,
                PetalInnerRadius = 40,
                PetalOuterRadius = 120,
                PetalGapDegrees = 8f
            },
            "base" => baselineSettings with { BaseCanvasSize = 320 },
            "combined" => baselineSettings with
            {
                BaseCanvasSize = 320,
                TextAlpha = 117,
                TextRadius = 90,
                HighlightAlpha = 40,
                FillAlpha = 109,
                BorderAlpha = 50,
                HubRadius = 30,
                PetalInnerRadius = 40,
                PetalOuterRadius = 120,
                PetalGapDegrees = 8f
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        UniversalRadialParameters baseline =
            ProductionUniversalRadialParametersAdapter.Adapt(baselineSettings, entry.Plan);
        UniversalRadialParameters candidate =
            ProductionUniversalRadialParametersAdapter.Adapt(candidateSettings, entry.Plan);

        if (themeId == DarkFantasy || scenario is "default" or "text-radius" or
            "highlight" or "fill-border" or "geometry")
        {
            Assert.Equal(baseline, candidate);
            return;
        }

        if (scenario is "text-alpha" or "combined")
            Assert.Equal((byte)127, candidate.TextStrength);
        if (scenario is "base" or "combined")
        {
            Assert.Equal(8d / 7d, candidate.SurfaceScale, 12);
            Assert.Equal(7d / 8d, candidate.FontScale, 12);
            Assert.Equal(
                baseline.SurfaceScale * baseline.FontScale,
                candidate.SurfaceScale * candidate.FontScale,
                12);
        }
        AssertNeutralRemovedFields(candidate);
    }

    [Fact]
    public void LegacyV1AdapterPreservesEveryCurrentTextAlphaValue()
    {
        RadialVisualPackCatalogEntry entry = Entry(RadialV5);
        for (int alpha = byte.MinValue; alpha <= byte.MaxValue; alpha++)
        {
            RadialMenuSettings settings = ThemeSettings(RadialV5) with { TextAlpha = alpha };
            UniversalRadialParameters candidate =
                ProductionUniversalRadialParametersAdapter.Adapt(settings, entry.Plan);
            Assert.Equal(
                UniversalRadialSettingsNormalizer.NormalizeLegacyTextStrength(alpha),
                candidate.TextStrength);
        }
    }

    [Theory]
    [InlineData(RadialV5)]
    [InlineData(DarkFantasy)]
    public void RevisionTwoInitialPolicyKeepsOnlyProductScaleAndFont(string themeId)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId) with
        {
            SettingsSemanticRevision = RadialSettingsSemanticRevision.Universal,
            BaseCanvasSize = 336,
            ScalePercent = 120,
            FontSize = 30,
            TextAlpha = 128,
            TextRadius = 90,
            HighlightAlpha = 40,
            FillAlpha = 109,
            BorderAlpha = 50
        };

        UniversalRadialParameters parameters =
            ProductionUniversalRadialParametersAdapter.Adapt(settings, entry.Plan);

        Assert.Equal(1.2d, parameters.SurfaceScale, 12);
        Assert.Equal(2d, parameters.FontScale, 12);
        Assert.Equal(1.2d, parameters.DynamicContentScale, 12);
        Assert.Equal(byte.MaxValue, parameters.TextStrength);
        AssertNeutralRemovedFields(parameters);
    }

    [Theory]
    [MemberData(nameof(FormalThemes))]
    public void NeutralUniversalInitialBuildsWithoutCompanionAndMatchesProduction(string themeId)
    {
        RadialVisualPackCatalogEntry entry = Entry(themeId);
        RadialMenuSettings settings = ThemeSettings(themeId);
        using var empty = new TemporaryDirectory();
        int targetSize = settings.CreateRenderMetrics().CanvasSize;

        using RuntimeRenderBundle candidate = RadialRenderPolicyAuthority.Build(
            RadialRenderPolicy.UniversalInitial,
            entry.Plan,
            settings,
            targetSize,
            96,
            empty.Path);
        using RuntimeRenderBundle production = RuntimeRenderBundle.Build(
            entry.Plan,
            settings,
            targetSize,
            96);

        Assert.True(candidate.IsUniversal);
        Assert.True(candidate.HasPrebuiltFinalStates);
        Assert.Equal(0, candidate.SelectedEmphasisManifestReadCount);
        Assert.Equal(0, candidate.SelectedEmphasisDecodedAssetCount);
        Assert.False(candidate.HasSelectedEmphasisCache);
        for (int slot = 0; slot <= entry.LayoutDefinition.SlotCount; slot++)
        {
            using Bitmap expected = production.IsFullStateFrame
                ? Clone(production.GetFinalState(slot))
                : ComposeLegacyState(production, slot);
            Assert.Equal(RawPixelSha(expected), RawPixelSha(candidate.GetFinalState(slot)));
        }
    }

    [Fact]
    public void NonneutralHighlightWithoutCompanionFailsExplicitly()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = ThemeSettings(DarkFantasy);
        UniversalRadialParameters neutral =
            ProductionUniversalRadialParametersAdapter.Adapt(settings, entry.Plan);
        UniversalRadialParameters nonneutral = WithHighlight(neutral, 128);
        using var empty = new TemporaryDirectory();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            RuntimeRenderBundle.BuildUniversal(
                UniversalRadialRenderPlan.Create(entry.Plan, nonneutral),
                settings,
                96,
                empty.Path));

        Assert.Contains("semantic companion unavailable", exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UniversalSessionRefreshesParametersAndUsesFullSurfaceRebuilds()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = ThemeSettings(DarkFantasy);
        using var empty = new TemporaryDirectory();
        RuntimeRenderBundleTargetBuilder builder =
            RadialRenderPolicyAuthority.CreateBundleBuilder(
                RadialRenderPolicy.UniversalInitial,
                empty.Path);
        using var session = new RadialVisualPackSession(
            entry,
            settings,
            settings.CreateRenderMetrics().CanvasSize,
            96,
            RadialRenderPolicy.UniversalInitial,
            builder);

        RuntimeRenderBundle initial = session.Bundle;
        int initialDynamicBuilds = initial.FullStateCache.DynamicBuildCount;
        RadialMenuSettings hiddenOnly = settings with { TextRadius = 90, HighlightAlpha = 40 };
        session.EnsureContent(hiddenOnly, hiddenOnly.CreateRenderMetrics().CanvasSize, 96);
        Assert.Same(initial, session.Bundle);
        Assert.Equal(initialDynamicBuilds, session.Bundle.FullStateCache.DynamicBuildCount);
        Assert.Equal(73d, session.Bundle.UniversalPlan.Parameters.SlotContentRadiusCru);
        Assert.Equal(byte.MaxValue, session.Bundle.UniversalPlan.Parameters.HighlightStrength);

        RadialMenuSettings font = hiddenOnly with { FontSize = 48 };
        session.EnsureContent(font, font.CreateRenderMetrics().CanvasSize, 96);
        Assert.Same(initial, session.Bundle);
        Assert.Equal(3.2d, session.Bundle.UniversalPlan.Parameters.FontScale, 12);

        RadialMenuSettings scaled = font with { ScalePercent = 140 };
        session.EnsureContent(scaled, scaled.CreateRenderMetrics().CanvasSize, 96);
        RuntimeRenderBundle scaledBundle = session.Bundle;
        Assert.NotSame(initial, scaledBundle);
        Assert.True(initial.IsDisposed);
        Assert.Equal(new Size(588, 588), scaledBundle.PhysicalSurfaceSize);

        session.EnsureContent(scaled, scaled.CreateRenderMetrics().CanvasSize, 192);
        Assert.NotSame(scaledBundle, session.Bundle);
        Assert.True(scaledBundle.IsDisposed);
        Assert.Equal(new Size(1176, 1176), session.Bundle.PhysicalSurfaceSize);
    }

    [Fact]
    public void UniversalMappingRefreshIsTargetedAndSurfaceFailureRetainsOldBundle()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = ThemeSettings(DarkFantasy) with { ScalePercent = 60 };
        using var empty = new TemporaryDirectory();
        RuntimeRenderBundleTargetBuilder universal =
            RadialRenderPolicyAuthority.CreateBundleBuilder(
                RadialRenderPolicy.UniversalInitial,
                empty.Path);
        int buildCount = 0;
        RuntimeRenderBundleTargetBuilder failSecondSurface = (plan, value, targetSize, dpi) =>
        {
            buildCount++;
            if (buildCount > 1)
                throw new InvalidDataException("Synthetic replacement failure.");
            return universal(plan, value, targetSize, dpi);
        };
        using var session = new RadialVisualPackSession(
            entry,
            settings,
            settings.CreateRenderMetrics().CanvasSize,
            96,
            RadialRenderPolicy.UniversalInitial,
            failSecondSurface);
        RuntimeRenderBundle active = session.Bundle;
        int mappingRebuilds = active.FullStateCache.MappingRebuildCount;
        RadialSlotMappings mappings = settings.GetProfileMappings(
            LayoutProfileRegistry.Radial8ProfileId).WithSlot(
                1,
                new RadialSlotMapping
                {
                    Kind = RadialActionKind.KeyboardKey,
                    Key = KeyboardKey.D
                });
        RadialMenuSettings mapped = settings.SetProfileMappings(
            LayoutProfileRegistry.Radial8ProfileId,
            mappings);

        session.EnsureContent(mapped, mapped.CreateRenderMetrics().CanvasSize, 96);
        Assert.Same(active, session.Bundle);
        Assert.Equal(mappingRebuilds + 1, active.FullStateCache.MappingRebuildCount);
        Assert.Equal(KeyboardKey.D, session.Mappings[0].Key);

        RadialMenuSettings scaled = mapped with { ScalePercent = 100 };
        Assert.Throws<InvalidDataException>(() => session.EnsureContent(
            scaled,
            scaled.CreateRenderMetrics().CanvasSize,
            96));
        Assert.Same(active, session.Bundle);
        Assert.False(active.IsDisposed);
    }

    [Theory]
    [InlineData(60, 96)]
    [InlineData(60, 192)]
    [InlineData(100, 96)]
    [InlineData(100, 192)]
    [InlineData(140, 96)]
    [InlineData(140, 192)]
    public void EffectiveActivationAnchorKeepsRequestedScreenPointStable(
        int scalePercent,
        int dpi)
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialMenuSettings settings = ThemeSettings(DarkFantasy) with
        {
            ScalePercent = scalePercent
        };
        UniversalRadialParameters parameters =
            ProductionUniversalRadialParametersAdapter.Adapt(settings, entry.Plan);
        UniversalRadialRenderPlan plan = UniversalRadialRenderPlan.Create(entry.Plan, parameters);
        var requested = new Point(1400, 900);

        Point topLeft = plan.ComputeOverlayTopLeft(requested, dpi);
        Point anchor = plan.PhysicalActivationAnchor(dpi);

        Assert.Equal(requested, new Point(topLeft.X + anchor.X, topLeft.Y + anchor.Y));
        Assert.Equal(210d * scalePercent / 100d, plan.EffectiveActivationAnchor.X, 12);
        Assert.Equal(210d * scalePercent / 100d, plan.EffectiveActivationAnchor.Y, 12);
    }

    [Fact]
    public void UniversalV1OverlayUsesOnlyPrebuiltFinalStatesWhileLegacyV1CompositionRemains()
    {
        RadialVisualPackCatalogEntry entry = Entry(RadialV5);
        RadialMenuSettings settings = ThemeSettings(RadialV5);
        using var empty = new TemporaryDirectory();
        int targetSize = settings.CreateRenderMetrics().CanvasSize;
        using RuntimeRenderBundle universal = RadialRenderPolicyAuthority.Build(
            RadialRenderPolicy.UniversalInitial,
            entry.Plan,
            settings,
            targetSize,
            96,
            empty.Path);
        using RuntimeRenderBundle legacy = RuntimeRenderBundle.Build(
            entry.Plan,
            settings,
            targetSize,
            96);

        Assert.True(universal.HasPrebuiltFinalStates);
        Assert.False(legacy.HasPrebuiltFinalStates);
        using var universalDraw = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(universalDraw))
            RadialMenuOverlay.DrawRuntimeComposition(graphics, universal, 1);
        Assert.Equal(RawPixelSha(universal.GetFinalState(1)), RawPixelSha(universalDraw));

        using var legacyDraw = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(legacyDraw))
            RadialMenuOverlay.DrawRuntimeComposition(graphics, legacy, 1);
        using Bitmap expectedLegacy = ComposeLegacyState(legacy, 1);
        Assert.Equal(RawPixelSha(expectedLegacy), RawPixelSha(legacyDraw));

        int manifestReads = universal.SelectedEmphasisManifestReadCount;
        int decoded = universal.SelectedEmphasisDecodedAssetCount;
        for (int index = 0; index < 64; index++)
            _ = universal.GetFinalState(index % (entry.LayoutDefinition.SlotCount + 1));
        Assert.Equal(manifestReads, universal.SelectedEmphasisManifestReadCount);
        Assert.Equal(decoded, universal.SelectedEmphasisDecodedAssetCount);
        Assert.Equal(0, universal.SelectionHotPathWorkCount);
    }

    [Fact]
    public void UniversalFallbackKeepsPolicyAndSucceedsWithoutCompanion()
    {
        using var empty = new TemporaryDirectory();
        RuntimeRenderBundleTargetBuilder universal =
            RadialRenderPolicyAuthority.CreateBundleBuilder(
                RadialRenderPolicy.UniversalInitial,
                empty.Path);
        var attempts = new List<string>();
        RuntimeRenderBundleTargetBuilder failDarkFantasy = (plan, settings, targetSize, dpi) =>
        {
            attempts.Add(plan.ThemeId);
            if (plan.RenderModel is FullStateFrameRenderPlan)
                throw new InvalidDataException("Synthetic Dark Fantasy failure.");
            return universal(plan, settings, targetSize, dpi);
        };
        using var runtime = new RadialVisualPackRuntime(
            HistoricalV2ThemeCatalog.Create(),
            RadialRenderPolicy.UniversalInitial,
            failDarkFantasy);
        RadialMenuSettings settings = ThemeSettings(DarkFantasy);

        RadialVisualPackSession? session = runtime.Ensure(
            settings,
            settings.CreateRenderMetrics().CanvasSize,
            96);

        Assert.NotNull(session);
        Assert.Equal(RadialV5, session!.PackId);
        Assert.Equal(RadialRenderPolicy.UniversalInitial, session.RenderPolicy);
        Assert.True(session.Bundle.IsUniversal);
        Assert.Equal(0, session.Bundle.SelectedEmphasisManifestReadCount);
        Assert.Equal(new[] { DarkFantasy, RadialV5 }, attempts);
    }

    [Fact]
    public void UniversalFallbackDoubleFailureTerminatesAndNeverUsesLegacyBuilder()
    {
        var attempts = new List<string>();
        RuntimeRenderBundleTargetBuilder fail = (plan, _, _, _) =>
        {
            attempts.Add(plan.ThemeId);
            throw new InvalidDataException("Synthetic Universal failure.");
        };
        using var runtime = new RadialVisualPackRuntime(
            HistoricalV2ThemeCatalog.Create(),
            RadialRenderPolicy.UniversalInitial,
            fail);
        RadialMenuSettings settings = ThemeSettings(DarkFantasy);

        RadialVisualPackSession? session = runtime.Ensure(
            settings,
            settings.CreateRenderMetrics().CanvasSize,
            96);

        Assert.Null(session);
        Assert.Equal(0, runtime.InstallCount);
        Assert.Equal(new[] { DarkFantasy, RadialV5 }, attempts);
        Assert.NotNull(runtime.LastError);
    }

    [Fact]
    public void FailedUniversalSwitchRetainsExistingActiveSession()
    {
        using var empty = new TemporaryDirectory();
        RuntimeRenderBundleTargetBuilder universal =
            RadialRenderPolicyAuthority.CreateBundleBuilder(
                RadialRenderPolicy.UniversalInitial,
                empty.Path);
        RuntimeRenderBundleTargetBuilder failDarkFantasy = (plan, settings, targetSize, dpi) =>
            plan.RenderModel is FullStateFrameRenderPlan
                ? throw new InvalidDataException("Synthetic Dark Fantasy failure.")
                : universal(plan, settings, targetSize, dpi);
        using var runtime = new RadialVisualPackRuntime(
            HistoricalV2ThemeCatalog.Create(),
            RadialRenderPolicy.UniversalInitial,
            failDarkFantasy);
        RadialMenuSettings v1Settings = ThemeSettings(RadialV5);
        RadialVisualPackSession active = Assert.IsType<RadialVisualPackSession>(runtime.Ensure(
            v1Settings,
            v1Settings.CreateRenderMetrics().CanvasSize,
            96));

        RadialMenuSettings failed = ThemeSettings(DarkFantasy);
        RadialVisualPackSession retained = Assert.IsType<RadialVisualPackSession>(runtime.Ensure(
            failed,
            failed.CreateRenderMetrics().CanvasSize,
            96));

        Assert.Same(active, retained);
        Assert.False(active.IsDisposed);
        Assert.Equal(RadialV5, runtime.ActivePackId);
    }

    [Fact]
    public void CurrentConfigSnapshotProducesExpectedFutureCandidateParametersWithoutMutation()
    {
        RadialVisualPackCatalogEntry entry = Entry(DarkFantasy);
        RadialSlotMappings mappings = RadialSlotMappings.Create(8).WithSlot(
            1,
            new RadialSlotMapping
            {
                Kind = RadialActionKind.KeyboardKey,
                Key = KeyboardKey.D
            });
        RadialMenuSettings settings = (ThemeSettings(DarkFantasy) with
        {
            ScalePercent = 140,
            FontSize = 48,
            HighlightAlpha = 255
        }).SetProfileMappings(LayoutProfileRegistry.Radial8ProfileId, mappings);
        string before = JsonSerializer.Serialize(settings);

        UniversalRadialParameters parameters =
            ProductionUniversalRadialParametersAdapter.Adapt(settings, entry.Plan);
        UniversalRadialRenderPlan plan = UniversalRadialRenderPlan.Create(entry.Plan, parameters);

        Assert.Equal(1.4d, parameters.SurfaceScale, 12);
        Assert.Equal(3.2d, parameters.FontScale, 12);
        Assert.Equal(byte.MaxValue, parameters.TextStrength);
        Assert.Equal(byte.MaxValue, parameters.HighlightStrength);
        Assert.Equal(73d, parameters.SlotContentRadiusCru);
        Assert.Equal(new Size(1176, 1176), plan.PhysicalSurfaceSize(192));
        Assert.Equal(294d, plan.EffectiveActivationAnchor.X, 12);
        Assert.Equal(KeyboardKey.D, settings.GetProfileMappings(
            LayoutProfileRegistry.Radial8ProfileId)[0].Key);
        Assert.Equal(before, JsonSerializer.Serialize(settings));
    }

    private static void AssertNeutralRemovedFields(UniversalRadialParameters parameters)
    {
        Assert.Equal(35d, parameters.HubRadiusCru);
        Assert.Equal(42d, parameters.InnerRadiusCru);
        Assert.Equal(103d, parameters.OuterRadiusCru);
        Assert.Equal(4d, parameters.GapDegrees);
        Assert.Equal(73d, parameters.SlotContentRadiusCru);
        Assert.Equal(byte.MaxValue, parameters.FillStrength);
        Assert.Equal(byte.MaxValue, parameters.BorderStrength);
        Assert.Equal(byte.MaxValue, parameters.HighlightStrength);
    }

    private static UniversalRadialParameters WithHighlight(
        UniversalRadialParameters value,
        byte highlightStrength) => new(
            value.SemanticRevision,
            value.SurfaceScale,
            value.HubRadiusCru,
            value.InnerRadiusCru,
            value.OuterRadiusCru,
            value.GapDegrees,
            value.SlotContentRadiusCru,
            value.FontScale,
            value.FillStrength,
            value.BorderStrength,
            value.TextStrength,
            highlightStrength,
            value.DynamicContentScale);

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
        return RadialMenuSettings.Default with
        {
            VisualPackId = themeId,
            MappingProfileId = profile
        };
    }

    private static Bitmap ComposeLegacyState(RuntimeRenderBundle bundle, int selectedSlot)
    {
        var target = new Bitmap(
            bundle.TargetSize,
            bundle.TargetSize,
            PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImageUnscaled(bundle.AssetCache.ScaledBase, 0, 0);
        graphics.CompositingMode = CompositingMode.SourceOver;
        if (selectedSlot > 0)
            graphics.DrawImageUnscaled(bundle.AssetCache.GetSelectedSlot(selectedSlot), 0, 0);
        graphics.DrawImageUnscaled(bundle.DynamicContent.Content, 0, 0);
        return target;
    }

    private static Bitmap Clone(Bitmap source)
    {
        var target = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImageUnscaled(source, 0, 0);
        return target;
    }

    private static string RawPixelSha(Bitmap bitmap)
    {
        Rectangle bounds = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            int rowBytes = checked(bitmap.Width * 4);
            byte[] raw = new byte[checked(rowBytes * bitmap.Height)];
            for (int row = 0; row < bitmap.Height; row++)
            {
                IntPtr source = IntPtr.Add(data.Scan0, row * data.Stride);
                Marshal.Copy(source, raw, row * rowBytes, rowBytes);
            }
            return Convert.ToHexString(SHA256.HashData(raw));
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "LeftPad-UniversalCutoverFoundation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
