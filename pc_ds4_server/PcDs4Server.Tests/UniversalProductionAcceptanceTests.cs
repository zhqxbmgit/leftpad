using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace PcDs4Server.Tests;

[Collection(UniversalRadialPhase3Collection.Name)]
[Trait("Category", "Universal Production Acceptance")]
public sealed class UniversalProductionAcceptanceTests
{
    private const string RadialV5 = "radial-v5";
    private const string Radial8Minimal = "radial-8-minimal-v1";
    private const string DarkFantasy = "dark-fantasy-radial8-v1";
    private const string PublishRootVariable = "PCDS4_ACCEPTANCE_PUBLISH_ROOT";
    private const string ArtifactRootVariable = "PCDS4_ACCEPTANCE_ARTIFACT_ROOT";
    private const string RealRadialConfigVariable = "PCDS4_ACCEPTANCE_REAL_RADIAL_CONFIG";
    private const string ExpectedRadialConfigSha =
        "F013A4442353253B265AAEDFE0EB9DC54D6D0D81ED787F913C937C3C5C1489AE";

    private static readonly string[] Themes = { RadialV5, Radial8Minimal, DarkFantasy };
    private static readonly int[] Scales = { 60, 100, 140 };
    private static readonly int[] Fonts = { 6, 15, 48 };
    private static readonly MappingAuthority[] MappingAuthorities =
        Enum.GetValues<MappingAuthority>();
    private static readonly int[] Dpis = { 96, 192 };
    private static readonly string ArtifactRoot = ResolveArtifactRoot();
    private static readonly string EmptyCompanionRoot = CreateEmptyCompanionRoot();

    [Fact]
    public void ProductionCandidateMatrixCoversAll162CellsAnd54DeterministicRepresentatives()
    {
        Assert.Equal(RadialRenderPolicy.Legacy, RadialRenderPolicyAuthority.ProductionDefault);
        var catalog = new RadialVisualPackCatalog();
        RadialVisualPackCatalogSnapshot discovered = catalog.Discover();
        Assert.Empty(discovered.Issues);
        RuntimeRenderBundleTargetBuilder builder =
            RadialRenderPolicyAuthority.CreateBundleBuilder(
                RadialRenderPolicy.UniversalInitial,
                EmptyCompanionRoot);
        var actionKinds = new HashSet<RadialActionKind>();
        int cells = 0;
        int deterministicCells = 0;
        int dpi96CanonicalCells = 0;
        int dpi192NoGoldenCells = 0;
        Stopwatch elapsed = Stopwatch.StartNew();

        foreach (string themeId in Themes)
        foreach (int scale in Scales)
        foreach (int font in Fonts)
        foreach (MappingAuthority mappingAuthority in MappingAuthorities)
        foreach (int dpi in Dpis)
        {
            RadialVisualPackCatalogEntry entry = Entry(discovered, themeId);
            RadialMenuSettings settings = MatrixSettings(
                themeId,
                scale,
                font,
                mappingAuthority,
                entry.LayoutDefinition.SlotCount);
            using CandidateHarness candidate = BuildCandidate(catalog, settings, dpi, builder);
            RadialVisualPackSession session = candidate.Session;
            RuntimeRenderBundle bundle = session.Bundle;
            VerifyCell(candidate, entry, settings, dpi, actionKinds);
            string[] hashes = StateHashes(bundle, session.LayoutDefinition.SlotCount);

            if (scale == 100 && font == 15)
            {
                if (dpi == 96)
                {
                    Assert.Equal(hashes, LegacyCanonicalStateHashes(entry, settings));
                    dpi96CanonicalCells++;
                }
                else
                {
                    // This is a second canonical build, not a newly declared golden.
                    using RuntimeRenderBundle canonical = builder(
                        entry.Plan,
                        settings,
                        settings.CreateRenderMetrics().CanvasSize,
                        dpi);
                    Assert.Equal(hashes, StateHashes(canonical, entry.LayoutDefinition.SlotCount));
                    dpi192NoGoldenCells++;
                }
            }

            if (mappingAuthority == MappingAuthority.Mixed)
            {
                using CandidateHarness repeated = BuildCandidate(catalog, settings, dpi, builder);
                VerifyCell(repeated, entry, settings, dpi, actionKinds);
                Assert.Equal(
                    hashes,
                    StateHashes(repeated.Session.Bundle, entry.LayoutDefinition.SlotCount));
                deterministicCells++;
            }

            cells++;
        }

        elapsed.Stop();
        Assert.Equal(162, cells);
        Assert.Equal(54, deterministicCells);
        Assert.Equal(9, dpi96CanonicalCells);
        Assert.Equal(9, dpi192NoGoldenCells);
        Assert.Contains(RadialActionKind.KeyboardKey, actionKinds);
        Assert.Contains(RadialActionKind.KeyboardShortcut, actionKinds);
        Assert.Contains(RadialActionKind.Ds4Button, actionKinds);
        Assert.Contains(RadialActionKind.None, actionKinds);
        WriteArtifact(
            "matrix-result.txt",
            $"Total={cells}{Environment.NewLine}" +
            $"Passed={cells}{Environment.NewLine}" +
            "Failed=0" + Environment.NewLine +
            $"DeterminismCells={deterministicCells}{Environment.NewLine}" +
            "HashMismatches=0" + Environment.NewLine +
            $"Dpi96CanonicalRawExact={dpi96CanonicalCells}{Environment.NewLine}" +
            $"Dpi192NoPreexistingGolden={dpi192NoGoldenCells}{Environment.NewLine}" +
            $"Duration={elapsed.Elapsed:c}{Environment.NewLine}");
    }

    [Fact]
    public void ProfilesCurrentConfigAndHiddenHistoricalPoisonRemainCoherent()
    {
        Assert.Equal(RadialRenderPolicy.Legacy, RadialRenderPolicyAuthority.ProductionDefault);
        var catalog = new RadialVisualPackCatalog();
        RadialVisualPackCatalogSnapshot discovered = catalog.Discover();
        Assert.Empty(discovered.Issues);
        RuntimeRenderBundleTargetBuilder builder =
            RadialRenderPolicyAuthority.CreateBundleBuilder(
                RadialRenderPolicy.UniversalInitial,
                EmptyCompanionRoot);

        RadialSlotMappings radial6 = Mappings(MappingAuthority.KeyboardHeavy, 6);
        RadialSlotMappings radial8 = Mappings(MappingAuthority.Ds4Heavy, 8);
        RadialMenuSettings profiles = RadialMenuSettings.Default
            .SetProfileMappings(LayoutProfileRegistry.Radial6ProfileId, radial6)
            .SetProfileMappings(LayoutProfileRegistry.Radial8ProfileId, radial8);
        var worker = new ManualWorkQueue();
        var dispatcher = new ManualDispatcher();
        var scheduler = new ManualScheduler();
        using (var runtime = new RadialVisualPackRuntime(
                   catalog,
                   RadialRenderPolicy.UniversalInitial,
                   builder))
        {
            runtime.EnableUniversalAsync(dispatcher, _ => { }, null, worker, scheduler);
            foreach ((string themeId, string profileId, RadialSlotMappings expected) in new[]
                     {
                         (RadialV5, LayoutProfileRegistry.Radial6ProfileId, radial6),
                         (DarkFantasy, LayoutProfileRegistry.Radial8ProfileId, radial8),
                         (Radial8Minimal, LayoutProfileRegistry.Radial8ProfileId, radial8),
                         (RadialV5, LayoutProfileRegistry.Radial6ProfileId, radial6)
                     })
            {
                RadialMenuSettings request = profiles with
                {
                    VisualPackId = themeId,
                    MappingProfileId = profileId
                };
                runtime.RequestUniversalAsync(
                    request,
                    request.CreateRenderMetrics().CanvasSize,
                    96,
                    UniversalRenderRequestIntent.Committed);
                worker.RunAll();
                dispatcher.RunAll();
                Assert.Equal(themeId, runtime.ActivePackId);
                Assert.Equal(profileId, runtime.Active!.LayoutDefinition.ProfileId);
                Assert.Equal(expected, runtime.Active.Mappings);
                Assert.Equal(expected, runtime.ActiveAsyncSnapshot!.Settings.GetProfileMappings(profileId));
            }
        }

        RadialMenuSettings real = LoadRealConfigReadOnly(out string? realPath, out string? beforeSha);
        Assert.Equal(DarkFantasy, real.VisualPackId);
        Assert.Null(real.SettingsSemanticRevision);
        Assert.Equal(140, real.ScalePercent);
        Assert.Equal(48f, real.FontSize);
        Assert.Equal(LayoutProfileRegistry.Radial8ProfileId, real.MappingProfileId);
        Assert.Equal(KeyboardKey.D, real.GetProfileMappings(
            LayoutProfileRegistry.Radial8ProfileId)[0].Key);
        RadialVisualPackCatalogEntry dark = Entry(discovered, DarkFantasy);
        UniversalRadialParameters current =
            ProductionUniversalRadialParametersAdapter.Adapt(real, dark.Plan);
        UniversalRadialRenderPlan currentPlan = UniversalRadialRenderPlan.Create(dark.Plan, current);
        Assert.Equal(1.4d, current.SurfaceScale, 12);
        Assert.Equal(3.2d, current.FontScale, 12);
        Assert.Equal(1.4d, current.DynamicContentScale, 12);
        Assert.Equal(byte.MaxValue, current.TextStrength);
        Assert.Equal(byte.MaxValue, current.HighlightStrength);
        AssertNeutralRemovedFields(current);
        Assert.Equal(new UniversalLogicalSize(588d, 588d), currentPlan.EffectiveLogicalSurface);
        Assert.Equal(new Size(1176, 1176), currentPlan.PhysicalSurfaceSize(192));
        Assert.Equal(new NormalizedPoint(294d, 294d), currentPlan.EffectiveActivationAnchor);
        Assert.Equal(new Point(588, 588), currentPlan.PhysicalActivationAnchor(192));
        using (CandidateHarness currentCandidate = BuildCandidate(catalog, real, 192, builder))
        {
            Assert.Equal(new Size(1176, 1176), currentCandidate.Session.Bundle.PhysicalSurfaceSize);
        }
        if (realPath != null)
            Assert.Equal(beforeSha, FileSha(realPath));

        RadialMenuSettings poison = (RadialMenuSettings.Default with
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
        });
        UniversalRadialParameters legacyV2 = ProductionUniversalRadialParametersAdapter.Adapt(
            poison with
            {
                VisualPackId = DarkFantasy,
                MappingProfileId = LayoutProfileRegistry.Radial8ProfileId
            },
            dark.Plan);
        Assert.Equal(1.4d, legacyV2.SurfaceScale, 12);
        Assert.Equal(3.2d, legacyV2.FontScale, 12);
        Assert.Equal(byte.MaxValue, legacyV2.TextStrength);
        AssertNeutralRemovedFields(legacyV2);

        UniversalRadialParameters revision2 = ProductionUniversalRadialParametersAdapter.Adapt(
            (poison with
            {
                SettingsSemanticRevision = RadialSettingsSemanticRevision.Universal,
                VisualPackId = DarkFantasy,
                MappingProfileId = LayoutProfileRegistry.Radial8ProfileId
            }),
            dark.Plan);
        Assert.Equal(1.4d, revision2.SurfaceScale, 12);
        Assert.Equal(3.2d, revision2.FontScale, 12);
        Assert.Equal(byte.MaxValue, revision2.TextStrength);
        AssertNeutralRemovedFields(revision2);

        foreach (string themeId in Themes)
        {
            RadialVisualPackCatalogEntry entry = Entry(discovered, themeId);
            RadialMenuSettings revision2Poison = poison with
            {
                SettingsSemanticRevision = RadialSettingsSemanticRevision.Universal,
                VisualPackId = themeId,
                MappingProfileId = entry.LayoutDefinition.ProfileId
            };
            UniversalRadialParameters neutralRevision2 =
                ProductionUniversalRadialParametersAdapter.Adapt(
                    RadialMenuSettings.Default with
                    {
                        SettingsSemanticRevision = RadialSettingsSemanticRevision.Universal,
                        VisualPackId = themeId,
                        MappingProfileId = entry.LayoutDefinition.ProfileId,
                        ScalePercent = 140,
                        FontSize = 48
                    },
                    entry.Plan);
            Assert.Equal(neutralRevision2,
                ProductionUniversalRadialParametersAdapter.Adapt(revision2Poison, entry.Plan));
        }

        RadialVisualPackCatalogEntry v5 = Entry(discovered, RadialV5);
        UniversalRadialParameters legacyV1 = ProductionUniversalRadialParametersAdapter.Adapt(
            poison with
            {
                VisualPackId = RadialV5,
                MappingProfileId = LayoutProfileRegistry.Radial6ProfileId
            },
            v5.Plan);
        Assert.Equal(1.6d, legacyV1.SurfaceScale, 12);
        Assert.Equal(2.8d, legacyV1.FontScale, 12);
        Assert.Equal(
            UniversalRadialSettingsNormalizer.NormalizeLegacyTextStrength(117),
            legacyV1.TextStrength);
        AssertNeutralRemovedFields(legacyV1);
        WriteArtifact(
            "config-and-compatibility-result.txt",
            "ProfileSwitch=PASS" + Environment.NewLine +
            $"RealConfig={(realPath == null ? "SYNTHETIC-FALLBACK" : realPath)}{Environment.NewLine}" +
            $"RealConfigSha={beforeSha ?? "N/A"}{Environment.NewLine}" +
            "RealConfigWriteback=NONE" + Environment.NewLine +
            "LegacyV1Poison=PASS" + Environment.NewLine +
            "LegacyV2Poison=PASS" + Environment.NewLine +
            "Revision2Poison=PASS" + Environment.NewLine);
    }

    [Fact]
    public void RealBuilderAsyncStormsAtomicSwapAndFailureContractsPass()
    {
        var catalog = new RadialVisualPackCatalog();
        RuntimeRenderBundleTargetBuilder universal =
            RadialRenderPolicyAuthority.CreateBundleBuilder(
                RadialRenderPolicy.UniversalInitial,
                EmptyCompanionRoot);

        var worker = new ManualWorkQueue();
        var dispatcher = new ManualDispatcher();
        var scheduler = new ManualScheduler();
        var publishedThemes = new List<string>();
        using (var runtime = new RadialVisualPackRuntime(
                   catalog,
                   RadialRenderPolicy.UniversalInitial,
                   universal))
        {
            runtime.EnableUniversalAsync(
                dispatcher,
                snapshot => publishedThemes.Add(snapshot.Settings.VisualPackId),
                null,
                worker,
                scheduler);
            foreach (string themeId in new[] { RadialV5, DarkFantasy, Radial8Minimal })
            {
                RadialMenuSettings request = MatrixSettings(
                    themeId,
                    100,
                    15,
                    MappingAuthority.Mixed,
                    themeId == RadialV5 ? 6 : 8);
                runtime.RequestUniversalAsync(
                    request,
                    request.CreateRenderMetrics().CanvasSize,
                    96,
                    UniversalRenderRequestIntent.Committed);
                Assert.True(worker.Count > 0);
                worker.RunAll();
            }
            Assert.Null(runtime.Active);
            dispatcher.RunAll();
            Assert.Equal(Radial8Minimal, runtime.ActivePackId);
            Assert.Equal(new[] { Radial8Minimal }, publishedThemes);
            Assert.Equal(3, runtime.AsyncDiagnostics.BuildStartCount);
            Assert.Equal(2, runtime.AsyncDiagnostics.StaleCandidateCount);
            Assert.Equal(2, runtime.AsyncDiagnostics.StaleCandidateDisposeCount);
            Assert.Equal(1, runtime.AsyncDiagnostics.MaxConcurrentBuildCount);
        }

        worker = new ManualWorkQueue();
        dispatcher = new ManualDispatcher();
        scheduler = new ManualScheduler();
        var publishedScales = new List<int>();
        using (var runtime = new RadialVisualPackRuntime(
                   catalog,
                   RadialRenderPolicy.UniversalInitial,
                   universal))
        {
            runtime.EnableUniversalAsync(
                dispatcher,
                snapshot => publishedScales.Add(snapshot.Settings.ScalePercent),
                null,
                worker,
                scheduler);
            foreach (int scale in new[] { 100, 110, 120, 130, 140 })
            {
                RadialMenuSettings request = MatrixSettings(
                    DarkFantasy,
                    scale,
                    15,
                    MappingAuthority.Mixed,
                    8);
                runtime.RequestUniversalAsync(
                    request,
                    request.CreateRenderMetrics().CanvasSize,
                    96,
                    UniversalRenderRequestIntent.Preview);
            }
            Assert.True(scheduler.FireLatest());
            Assert.Equal(1, worker.Count);
            Assert.Null(runtime.Active);
            worker.RunAll();
            Assert.Null(runtime.Active);
            dispatcher.RunAll();
            Assert.Equal(new[] { 140 }, publishedScales);
            Assert.Equal(140, runtime.ActiveAsyncSnapshot!.Settings.ScalePercent);
            Assert.Equal(1, runtime.AsyncDiagnostics.BuildStartCount);
            Assert.Equal(1, runtime.AsyncDiagnostics.MaxConcurrentBuildCount);
            Assert.Equal(4, runtime.AsyncDiagnostics.CoalescedRequestCount);
        }

        worker = new ManualWorkQueue();
        dispatcher = new ManualDispatcher();
        scheduler = new ManualScheduler();
        using (var runtime = new RadialVisualPackRuntime(
                   catalog,
                   RadialRenderPolicy.UniversalInitial,
                   universal))
        {
            runtime.EnableUniversalAsync(dispatcher, _ => { }, null, worker, scheduler);
            RadialMenuSettings v5 = MatrixSettings(
                RadialV5, 100, 15, MappingAuthority.Mixed, 6);
            runtime.RequestUniversalAsync(
                v5, v5.CreateRenderMetrics().CanvasSize, 96,
                UniversalRenderRequestIntent.Committed);
            worker.RunAll();
            dispatcher.RunAll();
            RadialVisualPackSession old = runtime.Active!;
            RadialMenuSettings r8 = MatrixSettings(
                Radial8Minimal, 100, 15, MappingAuthority.Mixed, 8);
            runtime.RequestUniversalAsync(
                r8, r8.CreateRenderMetrics().CanvasSize, 96,
                UniversalRenderRequestIntent.Committed);
            worker.RunAll();
            Assert.Same(old, runtime.Active);
            Assert.False(old.IsDisposed);
            dispatcher.RunAll();
            Assert.Equal(Radial8Minimal, runtime.ActivePackId);
            Assert.True(old.IsDisposed);
        }

        var failures = new HashSet<string>(StringComparer.Ordinal) { DarkFantasy };
        RuntimeRenderBundleTargetBuilder selectiveFailure = (plan, settings, targetSize, dpi) =>
            failures.Contains(plan.ThemeId)
                ? throw new InvalidDataException($"Synthetic failure for {plan.ThemeId}.")
                : universal(plan, settings, targetSize, dpi);
        worker = new ManualWorkQueue();
        dispatcher = new ManualDispatcher();
        scheduler = new ManualScheduler();
        using (var runtime = new RadialVisualPackRuntime(
                   catalog,
                   RadialRenderPolicy.UniversalInitial,
                   selectiveFailure))
        {
            runtime.EnableUniversalAsync(dispatcher, _ => { }, null, worker, scheduler);
            RadialMenuSettings dark = MatrixSettings(
                DarkFantasy, 100, 15, MappingAuthority.Mixed, 8);
            runtime.RequestUniversalAsync(
                dark, dark.CreateRenderMetrics().CanvasSize, 96,
                UniversalRenderRequestIntent.Committed);
            worker.RunAll();
            dispatcher.RunAll();
            Assert.Equal(RadialV5, runtime.ActivePackId);
            Assert.Equal(RadialRenderPolicy.UniversalInitial, runtime.Active!.RenderPolicy);
            Assert.Equal(1, runtime.AsyncDiagnostics.RequestedGeneration);
            Assert.Equal(1, runtime.AsyncDiagnostics.PublishedGeneration);
        }

        failures.Add(RadialV5);
        worker = new ManualWorkQueue();
        dispatcher = new ManualDispatcher();
        scheduler = new ManualScheduler();
        using (var runtime = new RadialVisualPackRuntime(
                   catalog,
                   RadialRenderPolicy.UniversalInitial,
                   selectiveFailure))
        {
            runtime.EnableUniversalAsync(dispatcher, _ => { }, null, worker, scheduler);
            RadialMenuSettings initial = MatrixSettings(
                Radial8Minimal, 100, 15, MappingAuthority.Mixed, 8);
            runtime.RequestUniversalAsync(
                initial, initial.CreateRenderMetrics().CanvasSize, 96,
                UniversalRenderRequestIntent.Committed);
            worker.RunAll();
            dispatcher.RunAll();
            RadialVisualPackSession old = runtime.Active!;
            string[] oldHashes = StateHashes(old.Bundle, old.LayoutDefinition.SlotCount);
            RadialMenuSettings dark = MatrixSettings(
                DarkFantasy, 100, 15, MappingAuthority.Mixed, 8);
            runtime.RequestUniversalAsync(
                dark, dark.CreateRenderMetrics().CanvasSize, 96,
                UniversalRenderRequestIntent.Committed);
            worker.RunAll();
            dispatcher.RunAll();
            Assert.Same(old, runtime.Active);
            Assert.False(old.IsDisposed);
            Assert.Equal(oldHashes, StateHashes(old.Bundle, old.LayoutDefinition.SlotCount));
            Assert.Equal(1, runtime.AsyncDiagnostics.CandidateFailureCount);
            Assert.Equal(1, runtime.AsyncDiagnostics.PublishCount);
        }

        worker = new ManualWorkQueue();
        dispatcher = new ManualDispatcher();
        scheduler = new ManualScheduler();
        var terminalFailures = new List<string>();
        using (var runtime = new RadialVisualPackRuntime(
                   catalog,
                   RadialRenderPolicy.UniversalInitial,
                   selectiveFailure))
        {
            runtime.EnableUniversalAsync(
                dispatcher, _ => { }, terminalFailures.Add, worker, scheduler);
            RadialMenuSettings dark = MatrixSettings(
                DarkFantasy, 100, 15, MappingAuthority.Mixed, 8);
            runtime.RequestUniversalAsync(
                dark, dark.CreateRenderMetrics().CanvasSize, 96,
                UniversalRenderRequestIntent.Committed);
            worker.RunAll();
            dispatcher.RunAll();
            Assert.Null(runtime.Active);
            Assert.Single(terminalFailures);
            Assert.Equal(1, runtime.AsyncDiagnostics.CandidateFailureCount);
            Assert.Equal(0, runtime.AsyncDiagnostics.PublishCount);
            Assert.Equal(0, runtime.AsyncDiagnostics.PendingRequestCount);
            Assert.Equal(0, runtime.AsyncDiagnostics.InFlightBuildCount);
        }

        WriteArtifact(
            "async-and-failure-result.txt",
            "RealBuilderThemes=3/3" + Environment.NewLine +
            "ThemeStormFinal=radial-8-minimal-v1" + Environment.NewLine +
            "ThemeStormStaleDisposed=2" + Environment.NewLine +
            "ScaleStormFinal=140" + Environment.NewLine +
            "MaxConcurrent=1" + Environment.NewLine +
            "AtomicSwap=PASS" + Environment.NewLine +
            "DarkFantasyToV5Fallback=PASS" + Environment.NewLine +
            "OldActiveRetained=PASS" + Environment.NewLine +
            "DoubleFailureNoActive=PASS" + Environment.NewLine);
    }

    [Fact]
    public void PublishedRootCatalogAssetsNeutralSmokesAndFallbackPass()
    {
        string? configured = Environment.GetEnvironmentVariable(PublishRootVariable);
        string contentRoot = string.IsNullOrWhiteSpace(configured)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(configured);
        Assert.True(Directory.Exists(contentRoot), $"Content root not found: {contentRoot}");
        string visualPacks = Path.Combine(contentRoot, "Assets", "UIVisualPacks");
        string themes = Path.Combine(contentRoot, "Assets", "UIThemes");
        Assert.True(Directory.Exists(visualPacks));
        Assert.True(Directory.Exists(themes));
        var catalog = new RadialVisualPackCatalog(visualPacks, themes);
        RadialVisualPackCatalogSnapshot discovered = catalog.Discover();
        Assert.Empty(discovered.Issues);
        Assert.Equal(LayoutProfileRegistry.Radial6ProfileId, Entry(discovered, RadialV5).LayoutDefinition.ProfileId);
        Assert.Equal(LayoutProfileRegistry.Radial8ProfileId, Entry(discovered, Radial8Minimal).LayoutDefinition.ProfileId);
        Assert.Equal(LayoutProfileRegistry.Radial8ProfileId, Entry(discovered, DarkFantasy).LayoutDefinition.ProfileId);

        string absentCompanion = string.IsNullOrWhiteSpace(configured)
            ? EmptyCompanionRoot
            : Path.Combine(contentRoot, "Assets", "UniversalRadialV3", "phase3-selected-emphasis");
        Assert.Empty(Directory.Exists(absentCompanion)
            ? Directory.GetFiles(absentCompanion, "*", SearchOption.AllDirectories)
            : Array.Empty<string>());
        RuntimeRenderBundleTargetBuilder universal =
            RadialRenderPolicyAuthority.CreateBundleBuilder(
                RadialRenderPolicy.UniversalInitial,
                absentCompanion);
        foreach ((string themeId, int scale, int font, int dpi) in new[]
                 {
                     (RadialV5, 100, 15, 96),
                     (Radial8Minimal, 100, 15, 96),
                     (DarkFantasy, 100, 15, 96),
                     (DarkFantasy, 140, 48, 192)
                 })
        {
            RadialVisualPackCatalogEntry entry = Entry(discovered, themeId);
            RadialMenuSettings settings = MatrixSettings(
                themeId,
                scale,
                font,
                MappingAuthority.Mixed,
                entry.LayoutDefinition.SlotCount);
            using CandidateHarness candidate = BuildCandidate(catalog, settings, dpi, universal);
            RuntimeRenderBundle bundle = candidate.Session.Bundle;
            Assert.Equal(themeId, candidate.Session.PackId);
            Assert.Equal(RadialRenderPolicy.UniversalInitial, candidate.Session.RenderPolicy);
            Assert.Equal(0, bundle.SelectedEmphasisManifestReadCount);
            Assert.Equal(0, bundle.SelectedEmphasisDecodedAssetCount);
            Assert.False(bundle.HasSelectedEmphasisCache);
            if (themeId == DarkFantasy && scale == 140)
                Assert.Equal(new Size(1176, 1176), bundle.PhysicalSurfaceSize);
        }

        RuntimeRenderBundleTargetBuilder failDark = (plan, settings, targetSize, dpi) =>
            plan.ThemeId == DarkFantasy
                ? throw new InvalidDataException("Synthetic packaged Dark Fantasy failure.")
                : universal(plan, settings, targetSize, dpi);
        using (CandidateHarness fallback = BuildCandidate(
                   catalog,
                   MatrixSettings(DarkFantasy, 100, 15, MappingAuthority.Mixed, 8),
                   96,
                   failDark))
        {
            Assert.Equal(RadialV5, fallback.Session.PackId);
            Assert.Equal(RadialRenderPolicy.UniversalInitial, fallback.Session.RenderPolicy);
            Assert.Equal(0, fallback.Session.Bundle.SelectedEmphasisManifestReadCount);
        }

        if (!string.IsNullOrWhiteSpace(configured))
        {
            string[] relativeFiles = Directory.GetFiles(contentRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(contentRoot, path).Replace('\\', '/'))
                .ToArray();
            Assert.DoesNotContain(relativeFiles, path =>
                path.Contains("UniversalRadialV3", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("phase3-selected-emphasis", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("visual_prototypes", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("spike", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("prototype", StringComparison.OrdinalIgnoreCase));
            string[] assetDirectories = Directory.GetDirectories(Path.Combine(contentRoot, "Assets"))
                .Select(Path.GetFileName)
                .Where(name => name != null)
                .Cast<string>()
                .Order(StringComparer.Ordinal)
                .ToArray();
            WriteArtifact(
                "published-root-result.txt",
                $"PublishRoot={contentRoot}{Environment.NewLine}" +
                $"FileCount={relativeFiles.Length}{Environment.NewLine}" +
                $"Bytes={relativeFiles.Sum(path => new FileInfo(Path.Combine(contentRoot, path.Replace('/', Path.DirectorySeparatorChar))).Length)}{Environment.NewLine}" +
                $"Assets={string.Join(",", assetDirectories)}{Environment.NewLine}" +
                "PrototypeMatches=0" + Environment.NewLine +
                "CatalogIssues=0" + Environment.NewLine +
                "NeutralSmokes=4/4" + Environment.NewLine +
                "PackagedFallback=PASS" + Environment.NewLine);
        }
    }

    [Fact]
    public void SixReviewSheetsUseProductionCandidateFinalStates()
    {
        var catalog = new RadialVisualPackCatalog();
        RadialVisualPackCatalogSnapshot discovered = catalog.Discover();
        Assert.Empty(discovered.Issues);
        RuntimeRenderBundleTargetBuilder builder =
            RadialRenderPolicyAuthority.CreateBundleBuilder(
                RadialRenderPolicy.UniversalInitial,
                EmptyCompanionRoot);
        var artifacts = new List<ReviewArtifact>();
        foreach (string themeId in Themes)
        {
            string mainPath = Path.Combine(ArtifactRoot, $"review-{themeId}-matrix-dpi96.png");
            CreateMainReviewSheet(mainPath, catalog, discovered, builder, themeId);
            ReviewArtifact main = Review(mainPath);
            Assert.Equal(1920, main.Width);
            Assert.Equal(1080, main.Height);
            artifacts.Add(main);
            string highDpiPath = Path.Combine(ArtifactRoot, $"review-{themeId}-scale140-font48-dpi192.png");
            CreateHighDpiReview(highDpiPath, catalog, discovered, builder, themeId);
            ReviewArtifact highDpi = Review(highDpiPath);
            Assert.Equal(1600, highDpi.Width);
            Assert.Equal(820, highDpi.Height);
            artifacts.Add(highDpi);
        }
        Assert.Equal(6, artifacts.Count);
        Assert.All(artifacts, artifact =>
        {
            Assert.True(File.Exists(artifact.Path));
            Assert.Equal(64, artifact.Sha256.Length);
            Assert.True(artifact.Width > 0);
            Assert.True(artifact.Height > 0);
        });
        WriteArtifact(
            "review-artifacts.txt",
            string.Join(
                Environment.NewLine,
                artifacts.Select(artifact =>
                    $"{artifact.Path}|{artifact.Sha256}|{artifact.Width}x{artifact.Height}")) +
            Environment.NewLine +
            "ManualVisualStatus=MANUAL VISUAL REVIEW PENDING" + Environment.NewLine);
    }

    private static void VerifyCell(
        CandidateHarness candidate,
        RadialVisualPackCatalogEntry entry,
        RadialMenuSettings settings,
        int dpi,
        ISet<RadialActionKind> actionKinds)
    {
        RadialVisualPackSession session = candidate.Session;
        RuntimeRenderBundle bundle = session.Bundle;
        UniversalRenderRequestSnapshot snapshot = candidate.Runtime.ActiveAsyncSnapshot!;
        UniversalRadialParameters parameters =
            ProductionUniversalRadialParametersAdapter.Adapt(settings, entry.Plan);
        UniversalRadialRenderPlan plan = bundle.UniversalPlan;
        Assert.Equal(RadialRenderPolicy.UniversalInitial, candidate.Runtime.RenderPolicy);
        Assert.Equal(RadialRenderPolicy.UniversalInitial, session.RenderPolicy);
        Assert.True(bundle.IsUniversal);
        Assert.Equal(settings.VisualPackId, session.PackId);
        Assert.Equal(settings.VisualPackId, snapshot.Authority.RequestedThemeId);
        Assert.Equal(entry.LayoutDefinition.ProfileId, snapshot.Authority.MappingProfileId);
        Assert.Equal(entry.LayoutDefinition.ProfileId, session.LayoutDefinition.ProfileId);
        Assert.Equal(settings.GetProfileMappings(entry.LayoutDefinition.ProfileId), session.Mappings);
        Assert.Equal(parameters, snapshot.Authority.EffectiveParameters);
        Assert.Equal(settings.NormalizeMappings(), snapshot.Settings);
        Assert.Equal(entry.Plan.ReferenceScale.LogicalWidth * parameters.SurfaceScale,
            plan.EffectiveLogicalSurface.Width, 10);
        Assert.Equal(entry.Plan.ReferenceScale.LogicalHeight * parameters.SurfaceScale,
            plan.EffectiveLogicalSurface.Height, 10);
        Assert.Equal(plan.PhysicalSurfaceSize(dpi), bundle.PhysicalSurfaceSize);
        Assert.Equal(plan.EffectiveActivationAnchor, bundle.UniversalPlan.EffectiveActivationAnchor);
        Point activation = new(1500, 900);
        Point topLeft = plan.ComputeOverlayTopLeft(activation, dpi);
        Point physicalAnchor = plan.PhysicalActivationAnchor(dpi);
        Assert.Equal(activation, new Point(
            topLeft.X + physicalAnchor.X,
            topLeft.Y + physicalAnchor.Y));
        if (entry.Id == DarkFantasy)
        {
            int expectedLogical = settings.ScalePercent switch
            {
                60 => 252,
                100 => 420,
                140 => 588,
                _ => throw new InvalidOperationException()
            };
            int expectedAnchor = expectedLogical / 2;
            int dpiScale = dpi / 96;
            Assert.Equal(new UniversalLogicalSize(expectedLogical, expectedLogical),
                plan.EffectiveLogicalSurface);
            Assert.Equal(new NormalizedPoint(expectedAnchor, expectedAnchor),
                plan.EffectiveActivationAnchor);
            Assert.Equal(new Size(expectedLogical * dpiScale, expectedLogical * dpiScale),
                bundle.PhysicalSurfaceSize);
            Assert.Equal(new Point(expectedAnchor * dpiScale, expectedAnchor * dpiScale),
                physicalAnchor);
        }

        int stateCount = entry.LayoutDefinition.SlotCount + 1;
        for (int slot = 0; slot < stateCount; slot++)
        {
            Bitmap state = bundle.GetFinalState(slot);
            Assert.NotNull(state);
            Assert.Equal(bundle.PhysicalSurfaceSize, state.Size);
            Assert.Equal(PixelFormat.Format32bppPArgb, state.PixelFormat);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => bundle.GetFinalState(stateCount));
        Assert.True(bundle.HasPrebuiltFinalStates);
        Assert.Equal(0, bundle.SelectionHotPathWorkCount);
        Assert.Equal(0, bundle.SelectedEmphasisManifestReadCount);
        Assert.Equal(0, bundle.SelectedEmphasisDecodedAssetCount);
        Assert.False(bundle.HasSelectedEmphasisCache);
        Assert.Equal(byte.MaxValue, plan.Parameters.HighlightStrength);
        Assert.Equal(
            ThemeMappingSnapshot.Capture(settings, entry.LayoutDefinition.ProfileId),
            bundle.UniversalCacheKey!.MappingSnapshot);
        VerifyResolvedMappingContent(settings, entry.LayoutDefinition.ProfileId, actionKinds);
        if (entry.IsV2)
        {
            FullStateFrameCache cache = bundle.FullStateCache;
            DynamicCacheBuildDiagnostics diagnostics = cache.DynamicDiagnostics;
            Assert.Equal(stateCount, cache.StateCount);
            Assert.Equal(stateCount, diagnostics.FinalStateBuildCount);
            Assert.Equal(0, diagnostics.FullSurfaceDynamicBitmapCount);
            Assert.Equal(0, diagnostics.FullSurfaceDynamicBytes);
            Assert.Equal(0, cache.DynamicBitmapLiveCount);
            Assert.True(diagnostics.LocalSpriteBitmapCount > 0);
            Assert.Equal(diagnostics.LocalSpriteBitmapCount, cache.LocalSpriteBitmapLiveCount);
            Assert.InRange(diagnostics.SharedDynamicLayerRasterCount,
                1, entry.LayoutDefinition.SlotCount * 2);
            Assert.InRange(diagnostics.SelectedDynamicLayerRasterCount,
                1, entry.LayoutDefinition.SlotCount * 2);
            Assert.Equal(
                diagnostics.SharedDynamicLayerRasterCount + diagnostics.SelectedDynamicLayerRasterCount,
                diagnostics.DynamicLayerRasterCount);
            Assert.Equal(0, cache.HotPathDynamicWorkCount);
        }
    }

    private static void VerifyResolvedMappingContent(
        RadialMenuSettings settings,
        string profileId,
        ISet<RadialActionKind> actionKinds)
    {
        ThemeMappingSnapshot snapshot = ThemeMappingSnapshot.Capture(settings, profileId);
        for (int slot = 1; slot <= snapshot.Slots.Count; slot++)
        {
            RadialSlotMapping requested = settings.GetProfileMappings(profileId)[slot - 1];
            RadialSlotMapping captured = snapshot.GetSlot(slot);
            Assert.Equal(requested, captured);
            actionKinds.Add(captured.Kind);
            ResolvedDynamicContent outer = ThemeDynamicContentResolver.Resolve(
                $"slot{slot}ActionLabel", slot, snapshot);
            ResolvedDynamicContent center = ThemeDynamicContentResolver.Resolve(
                "selectedActionLabel", slot, snapshot);
            Assert.Equal(slot, outer.SourceSlot);
            Assert.Equal(slot, center.SourceSlot);
            Assert.Equal(outer, center);
            if (captured.Kind == RadialActionKind.None)
            {
                Assert.True(outer.IsEmpty);
                Assert.Empty(outer.LabelText);
                continue;
            }
            Assert.False(outer.IsEmpty);
            Assert.Equal(captured.Kind, outer.ActionKind);
            Assert.Equal(RadialActionDisplayText.FromMapping(captured).Primary, outer.LabelText);
            Assert.False(string.IsNullOrWhiteSpace(outer.GlyphId));
            if (captured.Kind == RadialActionKind.KeyboardShortcut)
                Assert.Equal("keyboardShortcut", outer.GlyphFamily);
            if (captured.Kind == RadialActionKind.Ds4Button)
            {
                Assert.Equal("ds4", outer.GlyphFamily);
                Assert.True(RadialDs4ActionCatalog.TryGet(captured.Ds4Button, out _));
                var symbols = new LeftPadRuntimeSymbolProvider();
                Assert.True(symbols.TryCreatePath("leftpad-ds4", outer.GlyphId, 100f, out GraphicsPath path));
                using (path)
                    Assert.True(path.PointCount > 0);
            }
        }
    }

    private static CandidateHarness BuildCandidate(
        RadialVisualPackCatalog catalog,
        RadialMenuSettings settings,
        int dpi,
        RuntimeRenderBundleTargetBuilder builder)
    {
        var worker = new ManualWorkQueue();
        var dispatcher = new ManualDispatcher();
        var scheduler = new ManualScheduler();
        var failures = new List<string>();
        var runtime = new RadialVisualPackRuntime(
            catalog,
            RadialRenderPolicy.UniversalInitial,
            builder);
        try
        {
            runtime.EnableUniversalAsync(
                dispatcher,
                _ => { },
                failures.Add,
                worker,
                scheduler);
            long generation = runtime.RequestUniversalAsync(
                settings,
                settings.CreateRenderMetrics().CanvasSize,
                dpi,
                UniversalRenderRequestIntent.Committed);
            Assert.True(generation > 0);
            Assert.Null(runtime.Active);
            Assert.Equal(1, worker.Count);
            worker.RunAll();
            Assert.Null(runtime.Active);
            Assert.Equal(1, dispatcher.Count);
            dispatcher.RunAll();
            Assert.Empty(failures);
            Assert.NotNull(runtime.Active);
            Assert.Equal(generation, runtime.AsyncDiagnostics.PublishedGeneration);
            Assert.Equal(1, runtime.AsyncDiagnostics.MaxConcurrentBuildCount);
            return new(runtime);
        }
        catch
        {
            runtime.Dispose();
            throw;
        }
    }

    private static RadialMenuSettings MatrixSettings(
        string themeId,
        int scale,
        int font,
        MappingAuthority authority,
        int slotCount)
    {
        string profileId = themeId == RadialV5
            ? LayoutProfileRegistry.Radial6ProfileId
            : LayoutProfileRegistry.Radial8ProfileId;
        Assert.Equal(slotCount, LayoutProfileRegistry.GetRequired(profileId).SlotCount);
        return (RadialMenuSettings.Default with
        {
            VisualPackId = themeId,
            MappingProfileId = profileId,
            ScalePercent = scale,
            FontSize = font
        }).SetProfileMappings(profileId, Mappings(authority, slotCount));
    }

    private static RadialSlotMappings Mappings(MappingAuthority authority, int slotCount)
    {
        RadialSlotMapping[] values = authority switch
        {
            MappingAuthority.KeyboardHeavy => new[]
            {
                Keyboard(KeyboardKey.E),
                Keyboard(KeyboardKey.Tab),
                Keyboard(KeyboardKey.F1),
                Keyboard(KeyboardKey.Space),
                Keyboard(KeyboardKey.Enter),
                Keyboard(KeyboardKey.F12),
                Keyboard(KeyboardKey.D0),
                Keyboard(KeyboardKey.Z)
            },
            MappingAuthority.Mixed => new[]
            {
                Keyboard(KeyboardKey.E),
                Shortcut(KeyboardKey.K, ctrl: true, shift: true),
                Ds4("cross"),
                RadialSlotMapping.None,
                Keyboard(KeyboardKey.Tab),
                Ds4("dpad_down"),
                Keyboard(KeyboardKey.F1),
                Ds4("l3")
            },
            MappingAuthority.Ds4Heavy => new[]
            {
                Ds4("cross"),
                Ds4("circle"),
                Ds4("l1"),
                Ds4("l3"),
                Ds4("dpad_down"),
                RadialSlotMapping.None,
                Ds4("triangle"),
                Ds4("r3")
            },
            _ => throw new ArgumentOutOfRangeException(nameof(authority))
        };
        return RadialSlotMappings.Create(slotCount, values);
    }

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

    private static RadialSlotMapping Ds4(string id) => new()
    {
        Kind = RadialActionKind.Ds4Button,
        Ds4Button = id
    };

    private static string[] StateHashes(RuntimeRenderBundle bundle, int slotCount) =>
        Enumerable.Range(0, slotCount + 1)
            .Select(slot => RawPixelSha(bundle.GetFinalState(slot)))
            .ToArray();

    private static string[] LegacyCanonicalStateHashes(
        RadialVisualPackCatalogEntry entry,
        RadialMenuSettings settings)
    {
        using RuntimeRenderBundle production = RuntimeRenderBundle.Build(
            entry.Plan,
            settings,
            settings.CreateRenderMetrics().CanvasSize,
            96);
        var hashes = new List<string>();
        for (int slot = 0; slot <= entry.LayoutDefinition.SlotCount; slot++)
        {
            if (production.IsFullStateFrame)
            {
                hashes.Add(RawPixelSha(production.GetFinalState(slot)));
                continue;
            }
            using var composed = new Bitmap(
                production.TargetSize,
                production.TargetSize,
                PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(composed))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImageUnscaled(production.AssetCache.ScaledBase, 0, 0);
                graphics.CompositingMode = CompositingMode.SourceOver;
                if (slot > 0)
                    graphics.DrawImageUnscaled(production.AssetCache.GetSelectedSlot(slot), 0, 0);
                graphics.DrawImageUnscaled(production.DynamicContent.Content, 0, 0);
            }
            hashes.Add(RawPixelSha(composed));
        }
        return hashes.ToArray();
    }

    private static string RawPixelSha(Bitmap bitmap)
    {
        Rectangle bounds = new(Point.Empty, bitmap.Size);
        BitmapData data = bitmap.LockBits(
            bounds,
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppPArgb);
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

    private static RadialVisualPackCatalogEntry Entry(
        RadialVisualPackCatalogSnapshot snapshot,
        string themeId) =>
        Assert.IsType<RadialVisualPackCatalogEntry>(snapshot.Find(themeId));

    private static RadialMenuSettings LoadRealConfigReadOnly(
        out string? path,
        out string? sha)
    {
        path = Environment.GetEnvironmentVariable(RealRadialConfigVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            path = null;
            sha = null;
            return CurrentLikeSettings();
        }
        path = Path.GetFullPath(path);
        sha = FileSha(path);
        Assert.Equal(ExpectedRadialConfigSha, sha);
        var store = new RadialMenuSettingsStore(path);
        RadialMenuSettingsLoadResult result = store.Load();
        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, result.Status);
        return result.Settings;
    }

    private static RadialMenuSettings CurrentLikeSettings() =>
        (RadialMenuSettings.Default with
        {
            VisualPackId = DarkFantasy,
            MappingProfileId = LayoutProfileRegistry.Radial8ProfileId,
            ScalePercent = 140,
            FontSize = 48,
            FillAlpha = 218,
            BorderAlpha = 100,
            TextAlpha = 240,
            HighlightAlpha = 255
        }).SetProfileMappings(
            LayoutProfileRegistry.Radial8ProfileId,
            RadialSlotMappings.Create(8, new[]
            {
                Keyboard(KeyboardKey.D),
                RadialSlotMapping.None,
                RadialSlotMapping.None,
                RadialSlotMapping.None,
                RadialSlotMapping.None,
                RadialSlotMapping.None,
                RadialSlotMapping.None,
                RadialSlotMapping.None
            }));

    private static string FileSha(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void CreateMainReviewSheet(
        string path,
        RadialVisualPackCatalog catalog,
        RadialVisualPackCatalogSnapshot discovered,
        RuntimeRenderBundleTargetBuilder builder,
        string themeId)
    {
        const int cellWidth = 640;
        const int cellHeight = 360;
        using var sheet = new Bitmap(
            cellWidth * Fonts.Length,
            cellHeight * Scales.Length,
            PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(sheet);
        PrepareSheetGraphics(graphics);
        graphics.Clear(Color.FromArgb(255, 24, 24, 28));
        using var captionFont = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var labelFont = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var captionBrush = new SolidBrush(Color.White);
        using var linePen = new Pen(Color.FromArgb(255, 80, 80, 88), 1f);
        RadialVisualPackCatalogEntry entry = Entry(discovered, themeId);
        for (int row = 0; row < Scales.Length; row++)
        for (int column = 0; column < Fonts.Length; column++)
        {
            int scale = Scales[row];
            int font = Fonts[column];
            RadialMenuSettings settings = MatrixSettings(
                themeId,
                scale,
                font,
                MappingAuthority.Mixed,
                entry.LayoutDefinition.SlotCount);
            using CandidateHarness candidate = BuildCandidate(catalog, settings, 96, builder);
            Rectangle cell = new(column * cellWidth, row * cellHeight, cellWidth, cellHeight);
            graphics.DrawRectangle(linePen, cell.X, cell.Y, cell.Width - 1, cell.Height - 1);
            ReviewSheetCaption.Draw(graphics,
                $"{themeId} | Scale {scale} | Font {font} | DPI 96 | Mapping B",
                captionFont,
                captionBrush,
                cell.X + 12,
                cell.Y + 9);
            DrawCandidatePair(
                graphics,
                candidate.Session.Bundle.GetFinalState(0),
                candidate.Session.Bundle.GetFinalState(3),
                new Rectangle(cell.X + 12, cell.Y + 40, cell.Width - 24, cell.Height - 52),
                labelFont,
                captionBrush);
        }
        sheet.Save(path, ImageFormat.Png);
    }

    private static void CreateHighDpiReview(
        string path,
        RadialVisualPackCatalog catalog,
        RadialVisualPackCatalogSnapshot discovered,
        RuntimeRenderBundleTargetBuilder builder,
        string themeId)
    {
        const int width = 1600;
        const int height = 820;
        using var sheet = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(sheet);
        PrepareSheetGraphics(graphics);
        graphics.Clear(Color.FromArgb(255, 24, 24, 28));
        using var captionFont = new Font("Segoe UI", 20f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var labelFont = new Font("Segoe UI", 16f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.White);
        RadialVisualPackCatalogEntry entry = Entry(discovered, themeId);
        RadialMenuSettings settings = MatrixSettings(
            themeId,
            140,
            48,
            MappingAuthority.Mixed,
            entry.LayoutDefinition.SlotCount);
        using CandidateHarness candidate = BuildCandidate(catalog, settings, 192, builder);
        ReviewSheetCaption.Draw(graphics,
            $"{themeId} | Scale 140 | Font 48 | DPI 192 | Mapping B",
            captionFont,
            brush,
            24,
            18);
        DrawCandidatePair(
            graphics,
            candidate.Session.Bundle.GetFinalState(0),
            candidate.Session.Bundle.GetFinalState(3),
            new Rectangle(24, 64, width - 48, height - 88),
            labelFont,
            brush);
        sheet.Save(path, ImageFormat.Png);
    }

    private static void PrepareSheetGraphics(Graphics graphics)
    {
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
    }

    private static void DrawCandidatePair(
        Graphics graphics,
        Bitmap idle,
        Bitmap selected,
        Rectangle bounds,
        Font labelFont,
        Brush brush)
    {
        const int labelHeight = 22;
        int gap = 16;
        int availableWidth = (bounds.Width - gap) / 2;
        int availableHeight = bounds.Height - labelHeight;
        DrawOne(idle, "Idle", bounds.X, availableWidth);
        DrawOne(selected, "Selected slot 3", bounds.X + availableWidth + gap, availableWidth);

        void DrawOne(Bitmap source, string label, int x, int width)
        {
            double scale = Math.Min(
                width / (double)source.Width,
                availableHeight / (double)source.Height);
            int drawWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
            int drawHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
            int drawX = x + (width - drawWidth) / 2;
            int drawY = bounds.Y + labelHeight + (availableHeight - drawHeight) / 2;
            ReviewSheetCaption.Draw(graphics, label, labelFont, brush, x + 4, bounds.Y + 1);
            graphics.DrawImage(source, new Rectangle(drawX, drawY, drawWidth, drawHeight));
        }
    }

    private static ReviewArtifact Review(string path)
    {
        using var image = new Bitmap(path);
        return new(
            path,
            FileSha(path),
            image.Width,
            image.Height);
    }

    private static string ResolveArtifactRoot()
    {
        string? configured = Environment.GetEnvironmentVariable(ArtifactRootVariable);
        string path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Path.GetTempPath(),
                "LeftPad-UniversalProductionAcceptance-" + Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(configured);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateEmptyCompanionRoot()
    {
        string path = Path.Combine(ArtifactRoot, "empty-companion-root");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteArtifact(string name, string content) =>
        File.WriteAllText(Path.Combine(ArtifactRoot, name), content, new UTF8Encoding(false));

    private enum MappingAuthority
    {
        KeyboardHeavy,
        Mixed,
        Ds4Heavy
    }

    private sealed record ReviewArtifact(string Path, string Sha256, int Width, int Height);

    private sealed class CandidateHarness : IDisposable
    {
        private bool _disposed;

        public CandidateHarness(RadialVisualPackRuntime runtime) => Runtime = runtime;

        public RadialVisualPackRuntime Runtime { get; }
        public RadialVisualPackSession Session => Runtime.Active ??
            throw new InvalidOperationException("The candidate has not been published.");

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RadialVisualPackSession session = Session;
            RuntimeRenderBundle bundle = session.Bundle;
            FullStateFrameCache? cache = bundle.IsFullStateFrame ? bundle.FullStateCache : null;
            Runtime.Dispose();
            Assert.True(session.IsDisposed);
            Assert.True(bundle.IsDisposed);
            Assert.Equal(1, bundle.DisposeCount);
            if (cache != null)
            {
                Assert.Equal(0, cache.FullSurfaceBitmapLiveCount);
                Assert.Equal(cache.FullSurfaceBitmapCreatedCount, cache.FullSurfaceBitmapDisposedCount);
                Assert.Equal(0, cache.LocalSpriteBitmapLiveCount);
                Assert.Equal(0, cache.LocalSpriteBitmapLiveBytes);
                Assert.Equal(cache.LocalSpriteBitmapCreatedCount, cache.LocalSpriteBitmapDisposedCount);
            }
        }
    }

    private sealed class ManualWorkQueue : IUniversalRenderWorkQueue
    {
        private readonly Queue<Action> _work = new();

        public int Count => _work.Count;
        public void Queue(Action work) => _work.Enqueue(work);

        public void RunAll()
        {
            while (_work.Count > 0)
                _work.Dequeue()();
        }
    }

    private sealed class ManualDispatcher : IUniversalRenderDispatcher
    {
        private readonly Queue<Action> _callbacks = new();

        public int Count => _callbacks.Count;
        public void Post(Action callback) => _callbacks.Enqueue(callback);

        public void RunAll()
        {
            while (_callbacks.Count > 0)
                _callbacks.Dequeue()();
        }
    }

    private sealed class ManualScheduler : IUniversalRenderDebounceScheduler
    {
        private readonly List<ScheduledCallback> _callbacks = new();

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var scheduled = new ScheduledCallback(delay, callback);
            _callbacks.Add(scheduled);
            return scheduled;
        }

        public bool FireLatest()
        {
            ScheduledCallback? scheduled = _callbacks.LastOrDefault(value => value.IsActive);
            if (scheduled == null) return false;
            scheduled.Fire();
            return true;
        }

        private sealed class ScheduledCallback : IDisposable
        {
            private Action? _callback;

            public ScheduledCallback(TimeSpan delay, Action callback)
            {
                Delay = delay;
                _callback = callback;
            }

            public TimeSpan Delay { get; }
            public bool IsActive => _callback != null;

            public void Fire()
            {
                Action? callback = Interlocked.Exchange(ref _callback, null);
                callback?.Invoke();
            }

            public void Dispose() => Interlocked.Exchange(ref _callback, null);
        }
    }
}
