using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace PcDs4Server.Tests;

[Collection(UniversalRadialPhase3Collection.Name)]
public sealed class UniversalProductionCutoverTests
{
    private const string V5 = "radial-v5";
    private const string R8 = "radial-8-minimal-v1";
    private const string Dark = HistoricalV2ThemeCatalog.ReferenceThemeId;

    [Fact]
    public void PublicOverlayUsesDefaultAsyncForStartupPreviewUpdateAndCommittedSettings()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var messages = new ConcurrentQueue<string>();
                using var overlay = new RadialMenuOverlay(HistoricalV2ThemeCatalog.Create(), messages.Enqueue);
                Assert.Equal(RadialRenderPolicy.UniversalInitial, RadialRenderPolicyAuthority.ProductionDefault);
                Assert.Equal(RadialRenderPolicyAuthority.ProductionDefault, overlay.RenderPolicy);
                RadialMenuSettings initial = Settings(V5);
                using var controller = new RadialMenuController(overlay, initial);
                int Published() => messages.Count(message => message.Contains("Loaded radial visual pack asynchronously:"));

                overlay.PrepareVisualPack(initial);
                overlay.ShowAt(new Point(700, 500), initial, 0);
                Assert.Equal(0, Published());
                Assert.False(overlay.Visible); // No candidate is published before the UI dispatcher runs.
                PumpUntil(() => Published() == 1 && overlay.Visible);

                controller.PreviewAt(new Point(700, 500), Settings(R8));
                PumpUntil(() => Published() == 2);
                controller.UpdatePreview(Settings(R8) with { ScalePercent = 140, FontSize = 48 });
                PumpUntil(() => Published() == 3);
                Assert.True(controller.IsPreviewActive);

                // In-memory controller call only: no Settings UI or Store is involved.
                controller.ApplySettings(initial);
                PumpUntil(() => Published() == 4);
                Assert.Equal(RadialRenderPolicy.UniversalInitial, overlay.RenderPolicy);
                Assert.NotNull(overlay.ActiveLayoutDefinition);
                Assert.Equal("radial-6", overlay.ActiveLayoutDefinition.ProfileId);
                controller.ClosePreview();
                Assert.False(overlay.IsVisible);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Production default HWND lifecycle timed out.");
        Assert.Null(failure);
    }

    [Theory]
    [InlineData(V5)]
    [InlineData(R8)]
    [InlineData(Dark)]
    public void ProductionDefaultBuilderUsesUniversalWithoutCompanions(string theme)
    {
        using var empty = new RadialVisualPackTestDirectory();
        RuntimeRenderBundleTargetBuilder builder = RadialRenderPolicyAuthority.CreateBundleBuilder(
            RadialRenderPolicyAuthority.ProductionDefault, empty.Root);
        RadialMenuSettings settings = Settings(theme);
        using RuntimeRenderBundle bundle = builder(Entry(theme).Plan, settings,
            settings.CreateRenderMetrics().CanvasSize, 96);
        AssertUniversal(bundle);
        Assert.Equal(byte.MaxValue, bundle.UniversalPlan.Parameters.HighlightStrength);
        int stateCount = Entry(theme).LayoutDefinition.SlotCount + 1;
        for (int slot = 0; slot < stateCount; slot++) Assert.NotNull(bundle.GetFinalState(slot));
        Assert.Throws<ArgumentOutOfRangeException>(() => bundle.GetFinalState(stateCount));
    }

    [Fact]
    public void CurrentLikeDefaultPreservesAcceptedParametersSurfaceAndCacheArchitecture()
    {
        RadialMenuSettings settings = (Settings(Dark) with
        {
            SettingsSemanticRevision = null,
            ScalePercent = 140,
            FontSize = 48,
            FillAlpha = 218,
            BorderAlpha = 100,
            TextAlpha = 240,
            HighlightAlpha = 255
        }).SetProfileMappings("radial-8", RadialSlotMappings.Create(8).WithSlot(1,
            new RadialSlotMapping { Kind = RadialActionKind.KeyboardKey, Key = KeyboardKey.D }));
        RadialMenuSettings before = settings with { };
        var builder = RadialRenderPolicyAuthority.CreateBundleBuilder(RadialRenderPolicyAuthority.ProductionDefault);
        using RuntimeRenderBundle bundle = builder(Entry(Dark).Plan, settings, 588, 192);
        AssertUniversal(bundle);
        UniversalRadialParameters p = bundle.UniversalPlan.Parameters;
        Assert.Equal(1.4, p.SurfaceScale, 12);
        Assert.Equal(3.2, p.FontScale, 12);
        Assert.Equal(1.4, p.DynamicContentScale, 12);
        Assert.Equal(byte.MaxValue, p.TextStrength);
        Assert.Equal(byte.MaxValue, p.HighlightStrength);
        Assert.Equal(byte.MaxValue, p.FillStrength);
        Assert.Equal(byte.MaxValue, p.BorderStrength);
        Assert.Equal(73, p.SlotContentRadiusCru);
        Assert.Equal((35d, 42d, 103d, 4d), (p.HubRadiusCru, p.InnerRadiusCru, p.OuterRadiusCru, p.GapDegrees));
        Assert.Equal(new UniversalLogicalSize(588, 588), bundle.UniversalPlan.EffectiveLogicalSurface);
        Assert.Equal(new NormalizedPoint(294, 294), bundle.UniversalPlan.EffectiveActivationAnchor);
        Assert.Equal(new Size(1176, 1176), bundle.PhysicalSurfaceSize);
        Assert.Equal(new Point(588, 588), bundle.UniversalPlan.PhysicalActivationAnchor(192));
        Assert.Equal(9, bundle.FullStateCache.StateCount);
        Assert.Equal(0, bundle.FullStateCache.DynamicDiagnostics.FullSurfaceDynamicBitmapCount);
        Assert.Equal(0, bundle.FullStateCache.DynamicDiagnostics.FullSurfaceDynamicBytes);
        Assert.True(bundle.FullStateCache.DynamicDiagnostics.LocalSpriteBitmapCount > 0);
        for (int i = 0; i < 100; i++) _ = bundle.GetFinalState(i % 9);
        Assert.Equal(0, bundle.SelectionHotPathWorkCount);
        Assert.Equal(before, settings);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DefaultFailureRetainsUniversalPolicyAndNeverRendersLegacy(bool oldActive, bool failFallback)
    {
        var attempts = new List<string>();
        var builder = RadialRenderPolicyAuthority.CreateBundleBuilder(RadialRenderPolicyAuthority.ProductionDefault);
        RuntimeRenderBundle Build(NormalizedRenderPlan plan, RadialMenuSettings settings, int size, int dpi)
        {
            attempts.Add(plan.ThemeId);
            if (plan.ThemeId == Dark || (failFallback && plan.ThemeId == V5))
                throw new InvalidDataException("Synthetic default-policy candidate failure.");
            RuntimeRenderBundle bundle = builder(plan, settings, size, dpi);
            AssertUniversal(bundle);
            return bundle;
        }
        using var harness = new DefaultRuntimeHarness(Build);
        RadialVisualPackSession? old = null;
        if (oldActive)
        {
            harness.Request(Settings(R8));
            harness.Drain();
            old = harness.Runtime.Active!;
            attempts.Clear();
        }
        long generation = harness.Request(Settings(Dark));
        Assert.Same(old, harness.Runtime.Active);
        harness.Worker.RunAll();
        Assert.Same(old, harness.Runtime.Active);
        harness.Dispatcher.RunAll();
        Assert.Equal(RadialRenderPolicy.UniversalInitial, harness.Runtime.RenderPolicy);
        Assert.Equal(oldActive ? new[] { Dark } : new[] { Dark, V5 }, attempts);
        if (!failFallback)
        {
            Assert.Empty(harness.Failures);
            Assert.Equal(V5, harness.Runtime.ActivePackId);
            AssertUniversal(harness.Runtime.Active!.Bundle);
            Assert.Equal(generation, harness.Runtime.AsyncDiagnostics.PublishedGeneration);
        }
        else
        {
            Assert.Single(harness.Failures);
            Assert.Same(old, harness.Runtime.Active);
            if (old != null)
            {
                Assert.False(old.IsDisposed);
                AssertUniversal(old.Bundle);
                for (int i = 0; i < 100; i++) _ = old.Bundle.GetFinalState(i % 9);
                Assert.Equal(0, old.Bundle.SelectionHotPathWorkCount);
            }
            else Assert.Equal(0, harness.Runtime.AsyncDiagnostics.PublishCount);
        }
        Assert.Equal(0, harness.Runtime.AsyncDiagnostics.PendingRequestCount);
        Assert.Equal(0, harness.Runtime.AsyncDiagnostics.InFlightBuildCount);
    }

    [Fact]
    public void DefaultHundredRequestsUseOneWorkerAndOnlyPublishLatest()
    {
        int actualBuilds = 0;
        var builder = RadialRenderPolicyAuthority.CreateBundleBuilder(RadialRenderPolicyAuthority.ProductionDefault);
        using var harness = new DefaultRuntimeHarness((plan, settings, size, dpi) =>
        {
            actualBuilds++;
            return builder(plan, settings, size, dpi);
        });
        for (int i = 1; i <= 100; i++)
            harness.Request(Settings(V5) with { ScalePercent = 100 + i % 41, FontSize = i == 100 ? 48 : 15 + i % 34 });
        Assert.Null(harness.Runtime.Active);
        Assert.Equal(0, actualBuilds);
        Assert.Equal(1, harness.Worker.Count);
        harness.Worker.RunAll();
        Assert.Equal(2, actualBuilds);
        Assert.Null(harness.Runtime.Active);
        harness.Dispatcher.RunAll();
        AssertUniversal(harness.Runtime.Active!.Bundle);
        Assert.Equal(100, harness.Runtime.AsyncDiagnostics.RequestedGeneration);
        Assert.Equal(100, harness.Runtime.AsyncDiagnostics.PublishedGeneration);
        Assert.Equal(1, harness.Runtime.AsyncDiagnostics.MaxConcurrentBuildCount);
        Assert.Equal(1, harness.Runtime.AsyncDiagnostics.PublishCount);
        Assert.Equal(48, harness.Runtime.ActiveAsyncSnapshot!.Settings.FontSize);
        Assert.True(harness.Runtime.AsyncDiagnostics.StaleCandidateDisposeCount > 0);
    }

    [Fact]
    public void DefaultPreviewDebounceAndDpiRefreshPublishOneCoherentSnapshot()
    {
        using var harness = new DefaultRuntimeHarness();
        foreach (int font in new[] { 15, 18, 24, 30, 36, 48 })
            harness.Request(Settings(R8) with { ScalePercent = 140, FontSize = font },
                UniversalRenderRequestIntent.Preview, 192);
        Assert.Equal(TimeSpan.FromMilliseconds(150), harness.Scheduler.Delay);
        Assert.Equal(0, harness.Worker.Count);
        harness.Scheduler.Fire();
        harness.Drain();
        Assert.Equal(48, harness.Runtime.ActiveAsyncSnapshot!.Settings.FontSize);
        Assert.Equal(192, harness.Runtime.ActiveAsyncSnapshot.Dpi);
        Assert.Equal(new Size(784, 784), harness.Runtime.Active!.Bundle.PhysicalSurfaceSize);
        Assert.Equal(1, harness.Runtime.AsyncDiagnostics.BuildStartCount);
        AssertUniversal(harness.Runtime.Active.Bundle);
        long generation = harness.Request(Settings(R8) with { ScalePercent = 140, FontSize = 48 }, dpi: 96);
        harness.Drain();
        Assert.Equal(generation, harness.Runtime.AsyncDiagnostics.PublishedGeneration);
        Assert.Equal(new Size(392, 392), harness.Runtime.Active!.Bundle.PhysicalSurfaceSize);
    }

    private static RadialVisualPackCatalogEntry Entry(string theme) =>
        Assert.IsType<RadialVisualPackCatalogEntry>(HistoricalV2ThemeCatalog.Create().Discover().Find(theme));

    private static RadialMenuSettings Settings(string theme) => RadialMenuSettings.Default with
    {
        VisualPackId = theme, MappingProfileId = theme == V5 ? "radial-6" : "radial-8",
        ScalePercent = 100, FontSize = 15
    };

    private static void AssertUniversal(RuntimeRenderBundle bundle)
    {
        Assert.True(bundle.IsUniversal);
        Assert.True(bundle.HasPrebuiltFinalStates);
        Assert.Equal(0, bundle.SelectedEmphasisManifestReadCount);
        Assert.Equal(0, bundle.SelectedEmphasisDecodedAssetCount);
        Assert.False(bundle.HasSelectedEmphasisCache);
    }

    private static void PumpUntil(Func<bool> complete)
    {
        var watch = Stopwatch.StartNew();
        while (!complete() && watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            Application.DoEvents();
            Thread.Yield();
        }
        Assert.True(complete(), "Default Universal UI publication did not complete.");
    }

    private sealed class DefaultRuntimeHarness : IDisposable
    {
        public ManualQueue Worker { get; } = new();
        public ManualQueue Dispatcher { get; } = new();
        public ManualDebounce Scheduler { get; } = new();
        public List<string> Failures { get; } = new();
        public RadialVisualPackRuntime Runtime { get; }

        public DefaultRuntimeHarness(RuntimeRenderBundleTargetBuilder? builder = null)
        {
            Runtime = builder == null
                ? new RadialVisualPackRuntime(HistoricalV2ThemeCatalog.Create())
                : new RadialVisualPackRuntime(HistoricalV2ThemeCatalog.Create(), RadialRenderPolicyAuthority.ProductionDefault, builder);
            Runtime.EnableUniversalAsync(Dispatcher, _ => { }, Failures.Add, Worker, Scheduler);
            Assert.True(Runtime.IsUniversalAsyncEnabled);
            Assert.Equal(RadialRenderPolicy.UniversalInitial, Runtime.RenderPolicy);
        }

        public long Request(RadialMenuSettings settings,
            UniversalRenderRequestIntent intent = UniversalRenderRequestIntent.Committed, int dpi = 96) =>
            Runtime.RequestUniversalAsync(settings, settings.CreateRenderMetrics().CanvasSize, dpi, intent);
        public void Drain() { Worker.RunAll(); Dispatcher.RunAll(); }
        public void Dispose() => Runtime.Dispose();
    }

    private sealed class ManualQueue : IUniversalRenderWorkQueue, IUniversalRenderDispatcher
    {
        private readonly Queue<Action> _pending = new();
        public int Count => _pending.Count;
        public void Queue(Action work) => _pending.Enqueue(work);
        public void Post(Action callback) => _pending.Enqueue(callback);
        public void RunAll() { while (_pending.TryDequeue(out Action? next)) next(); }
    }

    private sealed class ManualDebounce : IUniversalRenderDebounceScheduler
    {
        private Pending? _pending;
        public TimeSpan Delay { get; private set; }
        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            Delay = delay;
            return _pending = new Pending(callback);
        }
        public void Fire() { Assert.NotNull(_pending); _pending.Fire(); }
        private sealed class Pending(Action callback) : IDisposable
        {
            private Action? _callback = callback;
            public void Fire() => Interlocked.Exchange(ref _callback, null)?.Invoke();
            public void Dispose() => Interlocked.Exchange(ref _callback, null);
        }
    }
}
