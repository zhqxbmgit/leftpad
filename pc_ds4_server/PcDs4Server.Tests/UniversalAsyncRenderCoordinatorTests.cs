using System.Diagnostics;
using System.Drawing;
using Xunit;
using Xunit.Abstractions;

namespace PcDs4Server.Tests;

[Collection(UniversalRadialPhase3Collection.Name)]
public sealed class UniversalAsyncRenderCoordinatorTests
{
    private const string RadialV5 = "radial-v5";
    private const string Radial8Minimal = "radial-8-minimal-v1";
    private const string DarkFantasy = "dark-fantasy-radial8-v1";
    private readonly ITestOutputHelper _output;

    public UniversalAsyncRenderCoordinatorTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void LatestValueWinsBuildsFirstAndThirdAndPublishesOnlyThird()
    {
        using var harness = new Harness();

        Assert.Equal(1, harness.Request(Value(20)));
        Assert.Equal(2, harness.Request(Value(24)));
        Assert.Equal(3, harness.Request(Value(30)));

        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();

        Assert.Equal(new[] { 20, 30 }, harness.Built.Select(value => value.Font));
        Assert.Equal(new[] { 30 }, harness.Published.Select(value => value.Font));
        Assert.Equal(30, harness.Active!.Snapshot.Font);
        Assert.Equal(1, harness.Created.Single(value => value.Snapshot.Font == 20).DisposeCount);
        UniversalAsyncRenderDiagnostics diagnostics = harness.Coordinator.Diagnostics;
        Assert.Equal(3, diagnostics.RequestedGeneration);
        Assert.Equal(3, diagnostics.PublishedGeneration);
        Assert.Equal(2, diagnostics.BuildStartCount);
        Assert.Equal(1, diagnostics.CoalescedRequestCount);
        Assert.Equal(1, diagnostics.StaleCandidateCount);
        Assert.Equal(1, diagnostics.StaleCandidateDisposeCount);
        Assert.Equal(1, diagnostics.MaxConcurrentBuildCount);
        Assert.Equal(1, diagnostics.PublishCount);
    }

    [Fact]
    public void ScalePreviewStormCoalescesSevenInputsIntoOneBuild()
    {
        using var harness = new Harness();

        foreach (int scale in new[] { 100, 102, 105, 110, 120, 130, 140 })
            harness.Request(Value(scale: scale), UniversalRenderRequestIntent.Preview);

        Assert.Equal(0, harness.Worker.Count);
        Assert.Equal(1, harness.Scheduler.ActiveCount);
        Assert.Equal(UniversalAsyncRenderCoordinator<ValueSnapshot, Candidate>.DefaultPreviewDebounce,
            harness.Scheduler.LatestDelay);
        Assert.True(harness.Scheduler.FireLatest());
        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();

        Assert.Equal(new[] { 140 }, harness.Built.Select(value => value.Scale));
        Assert.Equal(new[] { 140 }, harness.Published.Select(value => value.Scale));
        Assert.Equal(6, harness.Coordinator.Diagnostics.CoalescedRequestCount);
        Assert.Equal(7, harness.Coordinator.Diagnostics.RequestedGeneration);
    }

    [Fact]
    public void FontPreviewStormCoalescesSixInputsAndPreservesSurfaceAndAnchor()
    {
        using var harness = new Harness();
        NormalizedPoint anchor = new(0.25, 0.75);

        foreach (int font in new[] { 15, 18, 24, 30, 36, 48 })
            harness.Request(Value(font, scale: 125, anchor: anchor),
                UniversalRenderRequestIntent.Preview);

        Assert.True(harness.Scheduler.FireLatest());
        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();

        ValueSnapshot published = Assert.Single(harness.Published);
        Assert.Equal(48, published.Font);
        Assert.Equal(125, published.Scale);
        Assert.Equal(anchor, published.Anchor);
        Assert.Equal(1, harness.Coordinator.Diagnostics.BuildStartCount);
        Assert.Equal(5, harness.Coordinator.Diagnostics.CoalescedRequestCount);
    }

    [Fact]
    public void ThemeAtoBtoCPublishesOnlyC()
    {
        using var harness = new Harness();

        harness.Request(Value(theme: "A"));
        harness.Request(Value(theme: "B"));
        harness.Request(Value(theme: "C"));
        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();

        Assert.Equal(new[] { "A", "C" }, harness.Built.Select(value => value.Theme));
        Assert.Equal(new[] { "C" }, harness.Published.Select(value => value.Theme));
        Assert.Equal("C", harness.Active!.Snapshot.Theme);
    }

    [Fact]
    public void CommittedMappingSupersedesPreviewDebounceAndStartsImmediately()
    {
        using var harness = new Harness();
        harness.Request(Value(mapping: "A"), UniversalRenderRequestIntent.Preview);

        long generation = harness.Request(
            Value(mapping: "B"),
            UniversalRenderRequestIntent.Committed);

        Assert.Equal(2, generation);
        Assert.Equal(1, harness.Worker.Count);
        Assert.Equal(0, harness.Scheduler.ActiveCount);
        Assert.False(harness.Scheduler.FireLatest());
        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();
        Assert.Equal("B", harness.Active!.Snapshot.Mapping);
        Assert.Equal(new[] { "B" }, harness.Built.Select(value => value.Mapping));
    }

    [Fact]
    public void LatestSnapshotKeepsDpiScaleAndMappingFromOneGeneration()
    {
        using var harness = new Harness();
        harness.Request(Value(scale: 100, mapping: "A", dpi: 96),
            UniversalRenderRequestIntent.Preview);
        harness.Request(Value(scale: 140, mapping: "B", dpi: 144),
            UniversalRenderRequestIntent.Preview);

        Assert.True(harness.Scheduler.FireLatest());
        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();

        ValueSnapshot snapshot = harness.Active!.Snapshot;
        Assert.Equal(140, snapshot.Scale);
        Assert.Equal("B", snapshot.Mapping);
        Assert.Equal(144, snapshot.Dpi);
        Assert.Equal(2, harness.Coordinator.Diagnostics.PublishedGeneration);
    }

    [Fact]
    public void LatestCandidateFailureRetainsOldActiveCandidate()
    {
        using var harness = new Harness();
        harness.Request(Value(theme: "A"));
        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();
        Candidate active = harness.Active!;
        harness.BuildFailure = value => value.Theme == "B"
            ? new InvalidDataException("synthetic failure")
            : null;

        harness.Request(Value(theme: "B"));
        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();

        Assert.Same(active, harness.Active);
        Assert.Equal(0, active.DisposeCount);
        Assert.Equal(new[] { "A" }, harness.Published.Select(value => value.Theme));
        Assert.Equal(new[] { "B" }, harness.Failures.Select(value => value.Snapshot.Theme));
        Assert.Equal(1, harness.Coordinator.Diagnostics.CandidateFailureCount);
    }

    [Fact]
    public void ClosingPreviewDropsPendingAndDisposesAwaitingCandidate()
    {
        using var harness = new Harness();
        harness.Request(Value(theme: "A"));
        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();
        Candidate active = harness.Active!;

        harness.Request(Value(theme: "B"), UniversalRenderRequestIntent.Preview);
        harness.Coordinator.CancelPreview();
        Assert.False(harness.Scheduler.FireLatest());
        Assert.Equal(0, harness.Worker.Count);

        harness.Request(Value(theme: "C"), UniversalRenderRequestIntent.Preview);
        Assert.True(harness.Scheduler.FireLatest());
        harness.Worker.RunAll();
        Candidate stale = harness.Created.Single(value => value.Snapshot.Theme == "C");
        Assert.Equal(1, harness.Dispatcher.Count);

        harness.Coordinator.CancelPreview();
        harness.Dispatcher.RunAll();

        Assert.Same(active, harness.Active);
        Assert.Equal(1, stale.DisposeCount);
        Assert.Equal(0, harness.Coordinator.Diagnostics.PendingRequestCount);
        Assert.Equal(0, harness.Coordinator.Diagnostics.InFlightBuildCount);
        Assert.Equal(1, harness.Coordinator.Diagnostics.StaleCandidateCount);
        Assert.Equal(1, harness.Coordinator.Diagnostics.StaleCandidateDisposeCount);
    }

    [Fact]
    public void DisposeWhileBuildQueuedPreventsPublishAndDisposesCompletion()
    {
        using var harness = new Harness();
        harness.Request(Value(theme: "A"));

        harness.Coordinator.Dispose();
        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();

        Candidate candidate = Assert.Single(harness.Created);
        Assert.Equal(1, candidate.DisposeCount);
        Assert.Null(harness.Active);
        Assert.Empty(harness.Published);
        Assert.Equal(0, harness.Coordinator.Diagnostics.PendingRequestCount);
        Assert.Equal(0, harness.Coordinator.Diagnostics.InFlightBuildCount);
        Assert.Equal(1, harness.Coordinator.Diagnostics.StaleCandidateDisposeCount);
    }

    [Fact]
    public void LateDispatcherCallbackCannotPublishSupersededCandidate()
    {
        using var harness = new Harness();
        harness.Request(Value(theme: "A"));
        harness.Worker.RunNext();
        Candidate stale = Assert.Single(harness.Created);
        Assert.Equal(1, harness.Dispatcher.Count);

        harness.Request(Value(theme: "B"));
        Assert.Equal(1, stale.DisposeCount);
        harness.Dispatcher.RunNext();
        Assert.Null(harness.Active);
        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();

        Assert.Equal("B", harness.Active!.Snapshot.Theme);
        Assert.Equal(new[] { "B" }, harness.Published.Select(value => value.Theme));
        Assert.Equal(1, harness.Coordinator.Diagnostics.StaleCandidateDisposeCount);
    }

    [Fact]
    public void ApplyCanReassertActiveValueOverInFlightPreviewWithoutRebuild()
    {
        using var harness = new Harness();
        ValueSnapshot committed = Value(theme: "A", scale: 100);
        harness.Request(committed);
        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();
        Candidate active = harness.Active!;

        harness.Request(Value(theme: "B", scale: 140), UniversalRenderRequestIntent.Preview);
        Assert.True(harness.Scheduler.FireLatest());
        Assert.Equal(1, harness.Worker.Count);
        long committedGeneration = harness.Coordinator.ReassertPublished(
            committed,
            UniversalRenderRequestIntent.Committed);
        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();

        Assert.Equal(3, committedGeneration);
        Assert.Same(active, harness.Active);
        Assert.Equal(new[] { "A", "B" }, harness.Built.Select(value => value.Theme));
        Assert.Equal(new[] { "A" }, harness.Published.Select(value => value.Theme));
        Assert.Equal(3, harness.Coordinator.Diagnostics.PublishedGeneration);
        Assert.Equal(1, harness.Created.Single(value => value.Snapshot.Theme == "B").DisposeCount);
    }

    [Fact]
    public void HundredRequestBurstUsesOneWorkerAndLeavesNoPendingWork()
    {
        using var harness = new Harness();
        for (int value = 1; value <= 100; value++)
            harness.Request(Value(value));

        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();

        UniversalAsyncRenderDiagnostics diagnostics = harness.Coordinator.Diagnostics;
        Assert.Equal(new[] { 1, 100 }, harness.Built.Select(value => value.Font));
        Assert.Equal(new[] { 100 }, harness.Published.Select(value => value.Font));
        Assert.Equal(100, diagnostics.RequestedGeneration);
        Assert.Equal(100, diagnostics.PublishedGeneration);
        Assert.Equal(2, diagnostics.BuildStartCount);
        Assert.Equal(2, diagnostics.BuildCompleteCount);
        Assert.Equal(98, diagnostics.CoalescedRequestCount);
        Assert.Equal(1, diagnostics.MaxConcurrentBuildCount);
        Assert.Equal(0, diagnostics.PendingRequestCount);
        Assert.Equal(0, diagnostics.InFlightBuildCount);
        Assert.Equal(1, diagnostics.StaleCandidateCount);
        Assert.Equal(1, diagnostics.StaleCandidateDisposeCount);
        Assert.Equal(2, harness.Created.Count);
        Assert.Equal(1, harness.Created.Sum(value => value.DisposeCount));
    }

    [Fact]
    public void ThirtyInputPreviewStormBuildsAndPublishesOnlyFinalSnapshot()
    {
        using var harness = new Harness();
        for (int value = 1; value <= 30; value++)
            harness.Request(Value(value), UniversalRenderRequestIntent.Preview);

        Assert.True(harness.Scheduler.FireLatest());
        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();

        Assert.Equal(new[] { 30 }, harness.Built.Select(value => value.Font));
        Assert.Equal(new[] { 30 }, harness.Published.Select(value => value.Font));
        Assert.Equal(30, harness.Coordinator.Diagnostics.RequestedGeneration);
        Assert.Equal(29, harness.Coordinator.Diagnostics.CoalescedRequestCount);
        Assert.Equal(1, harness.Coordinator.Diagnostics.BuildStartCount);
        Assert.Equal(1, harness.Coordinator.Diagnostics.PublishCount);
        Assert.Equal(1, harness.Coordinator.Diagnostics.MaxConcurrentBuildCount);
    }

    [Fact]
    public void AtomicSwapKeepsOldActiveAliveUntilNewCandidateIsPublished()
    {
        using var harness = new Harness();
        harness.Request(Value(theme: "A"));
        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();
        Candidate oldActive = harness.Active!;

        harness.Request(Value(theme: "B"));
        harness.Worker.RunAll();
        Candidate newCandidate = harness.Created.Single(value => value.Snapshot.Theme == "B");

        Assert.Same(oldActive, harness.Active);
        Assert.Equal(0, oldActive.DisposeCount);
        Assert.Equal(0, newCandidate.DisposeCount);
        harness.Dispatcher.RunAll();
        Assert.Same(newCandidate, harness.Active);
        Assert.Equal(1, oldActive.DisposeCount);
        Assert.Equal(0, newCandidate.DisposeCount);
    }

    [Fact]
    public void RejectedUiDispatchDisposesCandidateWithoutPublishingOnWorker()
    {
        var worker = new ManualWorkQueue();
        Candidate? created = null;
        var coordinator = new UniversalAsyncRenderCoordinator<ValueSnapshot, Candidate>(
            snapshot => created = new Candidate(snapshot),
            (_, _) => throw new InvalidOperationException("must not publish"),
            _ => throw new InvalidOperationException("must not notify"),
            (_, _) => throw new InvalidOperationException("must not notify"),
            worker,
            new RejectingDispatcher(),
            new ManualScheduler());

        coordinator.Request(Value(), UniversalRenderRequestIntent.Committed);
        worker.RunAll();

        Assert.NotNull(created);
        Assert.Equal(1, created!.DisposeCount);
        Assert.Equal(0, coordinator.Diagnostics.PublishCount);
        Assert.Equal(1, coordinator.Diagnostics.CandidateFailureCount);
        Assert.Equal(0, coordinator.Diagnostics.StaleCandidateCount);
        Assert.Equal(0, coordinator.Diagnostics.StaleCandidateDisposeCount);
        coordinator.Dispose();
    }

    [Fact]
    public async Task BlockingBuilderNeverRunsOnRequestCallerAndRequestReturnsWithin50Ms()
    {
        using var harness = new Harness();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int requestThread = Environment.CurrentManagedThreadId;
        int buildThread = 0;
        harness.BeforeBuild = _ =>
        {
            buildThread = Environment.CurrentManagedThreadId;
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        };

        Stopwatch stopwatch = Stopwatch.StartNew();
        harness.Request(Value());
        stopwatch.Stop();

        Assert.False(entered.IsSet);
        Assert.InRange(stopwatch.Elapsed.TotalMilliseconds, 0, 50);
        Task build = Task.Run(harness.Worker.RunNext);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotEqual(requestThread, buildThread);
        Assert.False(build.IsCompleted);
        release.Set();
        await build.WaitAsync(TimeSpan.FromSeconds(10));
        harness.Dispatcher.RunAll();
        Assert.NotNull(harness.Active);
    }

    [Fact]
    public void BuilderCanRequestNewGenerationDuringFallbackWithoutPublishingOldResult()
    {
        using var harness = new Harness();
        bool requestedLatest = false;
        harness.BeforeBuild = snapshot =>
        {
            if (snapshot.Theme != "A" || requestedLatest) return;
            requestedLatest = true;
            harness.Request(Value(theme: "C"));
        };

        harness.Request(Value(theme: "A"));
        harness.Worker.RunAll();
        harness.Dispatcher.RunAll();

        Assert.Equal(new[] { "A", "C" }, harness.Built.Select(value => value.Theme));
        Assert.Equal(new[] { "C" }, harness.Published.Select(value => value.Theme));
        Assert.Equal(1, harness.Created.Single(value => value.Snapshot.Theme == "A").DisposeCount);
        Assert.Equal(2, harness.Coordinator.Diagnostics.PublishedGeneration);
    }

    [Fact]
    public void HiddenNeutralUniversalFieldsDoNotCreateAnotherGeneration()
    {
        var worker = new ManualWorkQueue();
        var dispatcher = new ManualDispatcher();
        var scheduler = new ManualScheduler();
        using var runtime = new RadialVisualPackRuntime(
            new RadialVisualPackCatalog(),
            RadialRenderPolicy.UniversalInitial);
        runtime.EnableUniversalAsync(dispatcher, _ => { }, null, worker, scheduler);
        RadialMenuSettings settings = ThemeSettings(DarkFantasy);

        long baseline = runtime.RequestUniversalAsync(
            settings,
            settings.CreateRenderMetrics().CanvasSize,
            96,
            UniversalRenderRequestIntent.Preview);
        long neutralHiddenChange = runtime.RequestUniversalAsync(
            settings with
            {
                FillAlpha = 109,
                BorderAlpha = 50,
                TextRadius = 90,
                HighlightAlpha = 40
            },
            settings.CreateRenderMetrics().CanvasSize,
            96,
            UniversalRenderRequestIntent.Preview);

        Assert.Equal(1, baseline);
        Assert.Equal(baseline, neutralHiddenChange);
        Assert.Equal(1, runtime.AsyncDiagnostics.RequestedGeneration);
        Assert.Equal(0, runtime.AsyncDiagnostics.BuildStartCount);
        Assert.Equal(1, scheduler.ActiveCount);
        runtime.CancelUniversalPreview();
    }

    [Fact]
    public void InitialUniversalFallbackTraversalStaysInsideOneGeneration()
    {
        var worker = new ManualWorkQueue();
        var dispatcher = new ManualDispatcher();
        var scheduler = new ManualScheduler();
        RuntimeRenderBundleTargetBuilder universal =
            RadialRenderPolicyAuthority.CreateBundleBuilder(RadialRenderPolicy.UniversalInitial);
        var attempts = new List<string>();
        RuntimeRenderBundleTargetBuilder failDarkFantasy = (plan, settings, targetSize, dpi) =>
        {
            attempts.Add(plan.ThemeId);
            if (plan.ThemeId == DarkFantasy)
                throw new InvalidDataException("Synthetic Dark Fantasy failure.");
            return universal(plan, settings, targetSize, dpi);
        };
        using var runtime = new RadialVisualPackRuntime(
            new RadialVisualPackCatalog(),
            RadialRenderPolicy.UniversalInitial,
            failDarkFantasy);
        runtime.EnableUniversalAsync(dispatcher, _ => { }, null, worker, scheduler);
        RadialMenuSettings requested = ThemeSettings(DarkFantasy);

        long generation = runtime.RequestUniversalAsync(
            requested,
            requested.CreateRenderMetrics().CanvasSize,
            96,
            UniversalRenderRequestIntent.Preview);
        Assert.True(scheduler.FireLatest());
        worker.RunAll();
        dispatcher.RunAll();

        Assert.Equal(1, generation);
        Assert.Equal(new[] { DarkFantasy, RadialV5 }, attempts);
        Assert.Equal(RadialV5, runtime.ActivePackId);
        Assert.Equal(RadialRenderPolicy.UniversalInitial, runtime.Active!.RenderPolicy);
        Assert.True(runtime.Active.Bundle.IsUniversal);
        Assert.Equal(1, runtime.AsyncDiagnostics.BuildStartCount);
        Assert.Equal(1, runtime.AsyncDiagnostics.BuildCompleteCount);
        Assert.Equal(1, runtime.AsyncDiagnostics.PublishedGeneration);
        Assert.Equal(1, runtime.AsyncDiagnostics.PublishCount);
        RadialVisualPackSession previewSession =
            Assert.IsType<RadialVisualPackSession>(runtime.Active);
        runtime.CancelUniversalPreview();
        Assert.Null(runtime.ActiveAsyncSnapshot);
        Assert.Same(previewSession, runtime.Active);
        Assert.False(previewSession.IsDisposed);
    }

    [Fact]
    public void NewGenerationDuringFallbackDisposesFallbackAndPublishesLatestTheme()
    {
        var worker = new ManualWorkQueue();
        var dispatcher = new ManualDispatcher();
        var scheduler = new ManualScheduler();
        RuntimeRenderBundleTargetBuilder universal =
            RadialRenderPolicyAuthority.CreateBundleBuilder(RadialRenderPolicy.UniversalInitial);
        RadialVisualPackRuntime? runtime = null;
        var attempts = new List<string>();
        bool superseded = false;
        RadialMenuSettings latest = ThemeSettings(Radial8Minimal);
        RuntimeRenderBundleTargetBuilder supersedingBuilder = (plan, settings, targetSize, dpi) =>
        {
            attempts.Add(plan.ThemeId);
            if (plan.ThemeId == DarkFantasy)
            {
                if (!superseded)
                {
                    superseded = true;
                    runtime!.RequestUniversalAsync(
                        latest,
                        latest.CreateRenderMetrics().CanvasSize,
                        dpi,
                        UniversalRenderRequestIntent.Committed);
                }
                throw new InvalidDataException("Synthetic Dark Fantasy failure.");
            }
            return universal(plan, settings, targetSize, dpi);
        };
        using (runtime = new RadialVisualPackRuntime(
                   new RadialVisualPackCatalog(),
                   RadialRenderPolicy.UniversalInitial,
                   supersedingBuilder))
        {
            runtime.EnableUniversalAsync(dispatcher, _ => { }, null, worker, scheduler);
            RadialMenuSettings initial = ThemeSettings(DarkFantasy);

            runtime.RequestUniversalAsync(
                initial,
                initial.CreateRenderMetrics().CanvasSize,
                96,
                UniversalRenderRequestIntent.Committed);
            worker.RunAll();
            dispatcher.RunAll();

            Assert.Equal(new[] { DarkFantasy, RadialV5, Radial8Minimal }, attempts);
            Assert.Equal(Radial8Minimal, runtime.ActivePackId);
            Assert.Equal(2, runtime.AsyncDiagnostics.RequestedGeneration);
            Assert.Equal(2, runtime.AsyncDiagnostics.PublishedGeneration);
            Assert.Equal(2, runtime.AsyncDiagnostics.BuildStartCount);
            Assert.Equal(1, runtime.AsyncDiagnostics.StaleCandidateCount);
            Assert.Equal(1, runtime.AsyncDiagnostics.StaleCandidateDisposeCount);
            Assert.Equal(1, runtime.AsyncDiagnostics.PublishCount);
            Assert.Equal(1, runtime.AsyncDiagnostics.MaxConcurrentBuildCount);
        }
    }

    [Fact]
    public void FailedSwitchRetainsActiveAndSelectionHotPathNeverWaitsForQueuedBuild()
    {
        var worker = new ManualWorkQueue();
        var dispatcher = new ManualDispatcher();
        var scheduler = new ManualScheduler();
        RuntimeRenderBundleTargetBuilder universal =
            RadialRenderPolicyAuthority.CreateBundleBuilder(RadialRenderPolicy.UniversalInitial);
        RuntimeRenderBundleTargetBuilder failDarkFantasy = (plan, settings, targetSize, dpi) =>
            plan.ThemeId == DarkFantasy
                ? throw new InvalidDataException("Synthetic Dark Fantasy failure.")
                : universal(plan, settings, targetSize, dpi);
        var failures = new List<string>();
        using var runtime = new RadialVisualPackRuntime(
            new RadialVisualPackCatalog(),
            RadialRenderPolicy.UniversalInitial,
            failDarkFantasy);
        runtime.EnableUniversalAsync(dispatcher, _ => { }, failures.Add, worker, scheduler);
        RadialMenuSettings initial = ThemeSettings(RadialV5);
        runtime.RequestUniversalAsync(
            initial,
            initial.CreateRenderMetrics().CanvasSize,
            96,
            UniversalRenderRequestIntent.Committed);
        worker.RunAll();
        dispatcher.RunAll();
        RadialVisualPackSession active = runtime.Active!;

        RadialMenuSettings failed = ThemeSettings(DarkFantasy);
        runtime.RequestUniversalAsync(
            failed,
            failed.CreateRenderMetrics().CanvasSize,
            96,
            UniversalRenderRequestIntent.Committed);
        Assert.Equal(1, worker.Count);
        for (int index = 0; index < 64; index++)
            _ = active.Bundle.GetFinalState(index % (active.LayoutDefinition.SlotCount + 1));

        Assert.Same(active, runtime.Active);
        Assert.False(active.IsDisposed);
        Assert.Equal(0, active.Bundle.SelectionHotPathWorkCount);
        worker.RunAll();
        dispatcher.RunAll();
        Assert.Same(active, runtime.Active);
        Assert.False(active.IsDisposed);
        Assert.Single(failures);
        Assert.Equal(1, runtime.AsyncDiagnostics.CandidateFailureCount);
        Assert.Equal(1, runtime.AsyncDiagnostics.MaxConcurrentBuildCount);
    }

    [Fact]
    public void ControllerUsesPreviewIntentButApplyUsesCommittedPathAndCloseCancelsPreview()
    {
        var overlay = new RecordingPreviewOverlay();
        using var controller = new RadialMenuController(overlay, RadialMenuSettings.Default);
        Point anchor = new(320, 240);

        controller.PreviewAt(anchor, RadialMenuSettings.Default with { ScalePercent = 110 });
        controller.UpdatePreview(RadialMenuSettings.Default with { FontSize = 24 });
        controller.ApplySettings(RadialMenuSettings.Default with { ScalePercent = 120 });
        controller.ClosePreview();

        Assert.Equal(2, overlay.PreviewCalls.Count);
        Assert.Equal(new[] { 110, 100 }, overlay.PreviewCalls.Select(value => value.ScalePercent));
        Assert.Single(overlay.CommittedCalls);
        Assert.Equal(120, overlay.CommittedCalls[0].ScalePercent);
        Assert.Equal(1, overlay.CancelCount);
        Assert.False(controller.IsPreviewActive);
    }

    [Fact]
    public void ReleaseDarkFantasyRecordsDetachedInitialScaleFontAndMappingDurations()
    {
        var worker = new ManualWorkQueue();
        var dispatcher = new ManualDispatcher();
        var scheduler = new ManualScheduler();
        using var runtime = new RadialVisualPackRuntime(
            new RadialVisualPackCatalog(),
            RadialRenderPolicy.UniversalInitial);
        runtime.EnableUniversalAsync(dispatcher, _ => { }, null, worker, scheduler);
        RadialMenuSettings initial = ThemeSettings(DarkFantasy);
        RadialMenuSettings scale = initial with { ScalePercent = 140 };
        RadialMenuSettings font = scale with { FontSize = 48 };
        RadialSlotMappings changedMappings = font
            .GetProfileMappings(LayoutProfileRegistry.Radial8ProfileId)
            .WithSlot(1, new RadialSlotMapping
            {
                Kind = RadialActionKind.KeyboardKey,
                Key = KeyboardKey.D
            });
        RadialMenuSettings mapping = font.SetProfileMappings(
            LayoutProfileRegistry.Radial8ProfileId,
            changedMappings);

        (TimeSpan InitialRequest, TimeSpan InitialBuild) = Measure(initial);
        (TimeSpan ScaleRequest, TimeSpan ScaleBuild) = Measure(scale);
        (TimeSpan FontRequest, TimeSpan FontBuild) = Measure(font);
        (TimeSpan MappingRequest, TimeSpan MappingBuild) = Measure(mapping);

        _output.WriteLine(
            "Initial request={0:0.000}ms build={1:0.000}ms; " +
            "Scale request={2:0.000}ms build={3:0.000}ms; " +
            "Font request={4:0.000}ms build={5:0.000}ms; " +
            "Mapping request={6:0.000}ms build={7:0.000}ms",
            InitialRequest.TotalMilliseconds,
            InitialBuild.TotalMilliseconds,
            ScaleRequest.TotalMilliseconds,
            ScaleBuild.TotalMilliseconds,
            FontRequest.TotalMilliseconds,
            FontBuild.TotalMilliseconds,
            MappingRequest.TotalMilliseconds,
            MappingBuild.TotalMilliseconds);
        Assert.True(InitialRequest < InitialBuild);
        Assert.True(ScaleRequest < ScaleBuild);
        Assert.True(FontRequest < FontBuild);
        Assert.True(MappingRequest < MappingBuild);
        Assert.Equal(4, runtime.AsyncDiagnostics.BuildStartCount);
        Assert.Equal(4, runtime.AsyncDiagnostics.BuildCompleteCount);
        Assert.Equal(4, runtime.AsyncDiagnostics.PublishCount);
        Assert.Equal(1, runtime.AsyncDiagnostics.MaxConcurrentBuildCount);
        Assert.Equal(0, runtime.AsyncDiagnostics.PendingRequestCount);
        Assert.Equal(0, runtime.AsyncDiagnostics.InFlightBuildCount);
        Assert.Equal(140, runtime.ActiveAsyncSnapshot!.Settings.ScalePercent);
        Assert.Equal(48, runtime.ActiveAsyncSnapshot.Settings.FontSize);
        Assert.Equal(changedMappings, runtime.Active!.Mappings);

        (TimeSpan Request, TimeSpan Build) Measure(RadialMenuSettings settings)
        {
            Stopwatch request = Stopwatch.StartNew();
            runtime.RequestUniversalAsync(
                settings,
                settings.CreateRenderMetrics().CanvasSize,
                96,
                UniversalRenderRequestIntent.Committed);
            request.Stop();
            Assert.Equal(1, worker.Count);
            Stopwatch build = Stopwatch.StartNew();
            worker.RunAll();
            build.Stop();
            Assert.Equal(1, dispatcher.Count);
            dispatcher.RunAll();
            return (request.Elapsed, build.Elapsed);
        }
    }

    private static ValueSnapshot Value(
        int font = 15,
        int scale = 100,
        string theme = "theme",
        string mapping = "mapping",
        int dpi = 96,
        NormalizedPoint? anchor = null) => new(
            theme,
            scale,
            font,
            mapping,
            dpi,
            anchor ?? new NormalizedPoint(0.5, 0.5));

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

    private sealed record ValueSnapshot(
        string Theme,
        int Scale,
        int Font,
        string Mapping,
        int Dpi,
        NormalizedPoint Anchor);

    private sealed class Candidate : IDisposable
    {
        public Candidate(ValueSnapshot snapshot) => Snapshot = snapshot;

        public ValueSnapshot Snapshot { get; }
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            Coordinator = new(
                Build,
                Publish,
                snapshot => Published.Add(snapshot),
                (snapshot, exception) => Failures.Add((snapshot, exception)),
                Worker,
                Dispatcher,
                Scheduler);
        }

        public ManualWorkQueue Worker { get; } = new();
        public ManualDispatcher Dispatcher { get; } = new();
        public ManualScheduler Scheduler { get; } = new();
        public UniversalAsyncRenderCoordinator<ValueSnapshot, Candidate> Coordinator { get; }
        public List<ValueSnapshot> Built { get; } = new();
        public List<ValueSnapshot> Published { get; } = new();
        public List<(ValueSnapshot Snapshot, Exception Exception)> Failures { get; } = new();
        public List<Candidate> Created { get; } = new();
        public Candidate? Active { get; private set; }
        public Action<ValueSnapshot>? BeforeBuild { get; set; }
        public Func<ValueSnapshot, Exception?>? BuildFailure { get; set; }

        public long Request(
            ValueSnapshot snapshot,
            UniversalRenderRequestIntent intent = UniversalRenderRequestIntent.Committed) =>
            Coordinator.Request(snapshot, intent);

        public void Dispose()
        {
            Coordinator.Dispose();
            Active?.Dispose();
            Active = null;
        }

        private Candidate Build(ValueSnapshot snapshot)
        {
            Built.Add(snapshot);
            BeforeBuild?.Invoke(snapshot);
            Exception? failure = BuildFailure?.Invoke(snapshot);
            if (failure != null) throw failure;
            var candidate = new Candidate(snapshot);
            Created.Add(candidate);
            return candidate;
        }

        private IDisposable? Publish(ValueSnapshot _, Candidate candidate)
        {
            Candidate? previous = Active;
            Active = candidate;
            return previous;
        }
    }

    private sealed class ManualWorkQueue : IUniversalRenderWorkQueue
    {
        private readonly Queue<Action> _work = new();

        public int Count
        {
            get
            {
                lock (_work) return _work.Count;
            }
        }

        public void Queue(Action work)
        {
            lock (_work) _work.Enqueue(work);
        }

        public void RunNext()
        {
            Action work;
            lock (_work) work = _work.Dequeue();
            work();
        }

        public void RunAll()
        {
            while (Count > 0) RunNext();
        }
    }

    private sealed class ManualDispatcher : IUniversalRenderDispatcher
    {
        private readonly Queue<Action> _callbacks = new();

        public int Count => _callbacks.Count;

        public void Post(Action callback) => _callbacks.Enqueue(callback);

        public void RunNext() => _callbacks.Dequeue()();

        public void RunAll()
        {
            while (_callbacks.Count > 0) RunNext();
        }
    }

    private sealed class ManualScheduler : IUniversalRenderDebounceScheduler
    {
        private readonly List<ScheduledCallback> _callbacks = new();

        public int ActiveCount => _callbacks.Count(value => value.IsActive);
        public TimeSpan LatestDelay => _callbacks[^1].Delay;

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

    private sealed class RejectingDispatcher : IUniversalRenderDispatcher
    {
        public void Post(Action callback) =>
            throw new ObjectDisposedException("Synthetic UI dispatcher");
    }

    private sealed class RecordingPreviewOverlay :
        IRadialMenuOverlay,
        IRadialPreviewRenderOverlay
    {
        public List<RadialMenuSettings> PreviewCalls { get; } = new();
        public List<RadialMenuSettings> CommittedCalls { get; } = new();
        public int CancelCount { get; private set; }
        public bool IsVisible { get; private set; }

        public void ShowPreviewAt(Point _, RadialMenuSettings settings, int selectedSlot)
        {
            Assert.Equal(0, selectedSlot);
            PreviewCalls.Add(settings);
            IsVisible = true;
        }

        public void CancelPreviewRequests() => CancelCount++;

        public void ShowAt(Point _, RadialMenuSettings settings, int selectedSlot)
        {
            Assert.Equal(0, selectedSlot);
            CommittedCalls.Add(settings);
            IsVisible = true;
        }

        public void Hide() => IsVisible = false;
        public void Dispose() => IsVisible = false;
    }
}
