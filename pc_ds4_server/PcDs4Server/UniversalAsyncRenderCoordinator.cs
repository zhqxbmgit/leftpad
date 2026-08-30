namespace PcDs4Server;

internal enum UniversalRenderRequestIntent
{
    Preview,
    Committed
}

internal sealed record UniversalAsyncRenderDiagnostics(
    long RequestedGeneration,
    long LatestGeneration,
    long PublishedGeneration,
    int BuildStartCount,
    int BuildCompleteCount,
    int CoalescedRequestCount,
    int StaleCandidateCount,
    int StaleCandidateDisposeCount,
    int CandidateFailureCount,
    int MaxConcurrentBuildCount,
    int PublishCount,
    int PendingRequestCount,
    int InFlightBuildCount);

internal interface IUniversalRenderWorkQueue
{
    void Queue(Action work);
}

internal interface IUniversalRenderDispatcher
{
    void Post(Action callback);
}

internal interface IUniversalRenderDebounceScheduler
{
    IDisposable Schedule(TimeSpan delay, Action callback);
}

internal sealed class ThreadPoolUniversalRenderWorkQueue : IUniversalRenderWorkQueue
{
    public static ThreadPoolUniversalRenderWorkQueue Instance { get; } = new();

    private ThreadPoolUniversalRenderWorkQueue() { }

    public void Queue(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        ThreadPool.QueueUserWorkItem(
            static state => ((Action)state!).Invoke(),
            work,
            preferLocal: false);
    }
}

internal sealed class DelegateUniversalRenderDispatcher : IUniversalRenderDispatcher
{
    private readonly Action<Action> _post;

    public DelegateUniversalRenderDispatcher(Action<Action> post) =>
        _post = post ?? throw new ArgumentNullException(nameof(post));

    public void Post(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _post(callback);
    }
}

internal sealed class TimerUniversalRenderDebounceScheduler : IUniversalRenderDebounceScheduler
{
    public static TimerUniversalRenderDebounceScheduler Instance { get; } = new();

    private TimerUniversalRenderDebounceScheduler() { }

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        return new SingleShotTimer(delay, callback);
    }

    private sealed class SingleShotTimer : IDisposable
    {
        private System.Threading.Timer? _timer;
        private Action? _callback;

        public SingleShotTimer(TimeSpan delay, Action callback)
        {
            _callback = callback;
            _timer = new System.Threading.Timer(static state => ((SingleShotTimer)state!).Fire(), this,
                delay, Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _callback, null);
            Interlocked.Exchange(ref _timer, null)?.Dispose();
        }

        private void Fire()
        {
            Action? callback = Interlocked.Exchange(ref _callback, null);
            Interlocked.Exchange(ref _timer, null)?.Dispose();
            callback?.Invoke();
        }
    }
}

/// <summary>
/// Serializes expensive detached render builds. Only reference/generation state is
/// protected by <see cref="_sync"/>; rendering, disposal, and dispatcher callbacks
/// always execute outside the state lock.
/// </summary>
internal sealed class UniversalAsyncRenderCoordinator<TSnapshot, TCandidate> : IDisposable
    where TSnapshot : class
    where TCandidate : class, IDisposable
{
    internal static readonly TimeSpan DefaultPreviewDebounce = TimeSpan.FromMilliseconds(150);

    private readonly object _sync = new();
    private readonly Func<TSnapshot, TCandidate> _builder;
    private readonly Func<TSnapshot, TCandidate, IDisposable?> _publish;
    private readonly Action<TSnapshot> _published;
    private readonly Action<TSnapshot, Exception> _failed;
    private readonly IUniversalRenderWorkQueue _worker;
    private readonly IUniversalRenderDispatcher _dispatcher;
    private readonly IUniversalRenderDebounceScheduler _scheduler;
    private readonly TimeSpan _previewDebounce;
    private readonly HashSet<CandidateEnvelope> _awaitingPublish = new();
    private BuildRequest? _pending;
    private bool _pendingReady;
    private bool _buildInFlight;
    private int _concurrentBuildCount;
    private IDisposable? _debounceLease;
    private long _debounceVersion;
    private long _requestedGeneration;
    private long _latestGeneration;
    private long _publishedGeneration;
    private TSnapshot? _latestSnapshot;
    private UniversalRenderRequestIntent? _latestIntent;
    private int _buildStartCount;
    private int _buildCompleteCount;
    private int _coalescedRequestCount;
    private int _staleCandidateCount;
    private int _staleCandidateDisposeCount;
    private int _candidateFailureCount;
    private int _maxConcurrentBuildCount;
    private int _publishCount;
    private bool _disposed;

    public UniversalAsyncRenderCoordinator(
        Func<TSnapshot, TCandidate> builder,
        Func<TSnapshot, TCandidate, IDisposable?> publish,
        Action<TSnapshot> published,
        Action<TSnapshot, Exception> failed,
        IUniversalRenderWorkQueue? worker = null,
        IUniversalRenderDispatcher? dispatcher = null,
        IUniversalRenderDebounceScheduler? scheduler = null,
        TimeSpan? previewDebounce = null)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _published = published ?? throw new ArgumentNullException(nameof(published));
        _failed = failed ?? throw new ArgumentNullException(nameof(failed));
        _worker = worker ?? ThreadPoolUniversalRenderWorkQueue.Instance;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _scheduler = scheduler ?? TimerUniversalRenderDebounceScheduler.Instance;
        _previewDebounce = previewDebounce ?? DefaultPreviewDebounce;
        if (_previewDebounce < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(previewDebounce));
    }

    public UniversalAsyncRenderDiagnostics Diagnostics
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _requestedGeneration,
                    _latestGeneration,
                    _publishedGeneration,
                    _buildStartCount,
                    _buildCompleteCount,
                    _coalescedRequestCount,
                    _staleCandidateCount,
                    _staleCandidateDisposeCount,
                    _candidateFailureCount,
                    _maxConcurrentBuildCount,
                    _publishCount,
                    _pending == null ? 0 : 1,
                    _buildInFlight ? 1 : 0);
            }
        }
    }

    public bool TryGetLatest(
        out TSnapshot? snapshot,
        out UniversalRenderRequestIntent? intent,
        out long generation)
    {
        lock (_sync)
        {
            snapshot = _latestSnapshot;
            intent = _latestIntent;
            generation = _latestGeneration;
            return !_disposed && snapshot != null;
        }
    }

    public long Request(TSnapshot snapshot, UniversalRenderRequestIntent intent)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        List<TCandidate> stale = new();
        BuildRequest? start = null;
        IDisposable? debounceLease;
        long? scheduleVersion = null;
        long generation;
        lock (_sync)
        {
            if (_disposed) return 0;
            generation = ++_requestedGeneration;
            _latestGeneration = generation;
            _latestSnapshot = snapshot;
            _latestIntent = intent;
            CollectAwaitingAsStaleLocked(generation, stale);

            if (_pending != null)
                _coalescedRequestCount++;
            _pending = new(generation, snapshot, intent);

            debounceLease = _debounceLease;
            _debounceLease = null;
            long debounceVersion = ++_debounceVersion;
            if (intent == UniversalRenderRequestIntent.Preview)
            {
                _pendingReady = false;
                scheduleVersion = debounceVersion;
            }
            else
            {
                _pendingReady = true;
                start = TakeNextBuildLocked();
            }
        }

        debounceLease?.Dispose();
        DisposeStaleCandidates(stale);
        if (scheduleVersion.HasValue)
            SchedulePreview(scheduleVersion.Value, generation, snapshot);
        QueueBuild(start);
        return generation;
    }

    /// <summary>
    /// Makes an already-active snapshot the newest authority without rebuilding it.
    /// This is used when Apply reasserts the active value while an older Preview
    /// request is pending or in flight.
    /// </summary>
    public long ReassertPublished(TSnapshot snapshot, UniversalRenderRequestIntent intent)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        List<TCandidate> stale = new();
        IDisposable? debounceLease;
        long generation;
        lock (_sync)
        {
            if (_disposed) return 0;
            generation = ++_requestedGeneration;
            _latestGeneration = generation;
            _publishedGeneration = generation;
            _latestSnapshot = snapshot;
            _latestIntent = intent;
            if (_pending != null)
                _coalescedRequestCount++;
            _pending = null;
            _pendingReady = false;
            debounceLease = _debounceLease;
            _debounceLease = null;
            _debounceVersion++;
            CollectAwaitingAsStaleLocked(generation, stale);
        }

        debounceLease?.Dispose();
        DisposeStaleCandidates(stale);
        return generation;
    }

    public void CancelPreview()
    {
        List<TCandidate> stale = new();
        IDisposable? debounceLease;
        lock (_sync)
        {
            if (_disposed || _latestIntent != UniversalRenderRequestIntent.Preview) return;
            _latestGeneration = ++_requestedGeneration;
            _latestSnapshot = null;
            _latestIntent = null;
            if (_pending?.Intent == UniversalRenderRequestIntent.Preview)
                _pending = null;
            _pendingReady = false;
            debounceLease = _debounceLease;
            _debounceLease = null;
            _debounceVersion++;
            CollectAwaitingAsStaleLocked(_latestGeneration, stale);
        }
        debounceLease?.Dispose();
        DisposeStaleCandidates(stale);
    }

    public void Dispose()
    {
        List<TCandidate> stale = new();
        IDisposable? debounceLease;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _latestGeneration = ++_requestedGeneration;
            _latestSnapshot = null;
            _latestIntent = null;
            _pending = null;
            _pendingReady = false;
            debounceLease = _debounceLease;
            _debounceLease = null;
            _debounceVersion++;
            CollectAwaitingAsStaleLocked(_latestGeneration, stale);
        }
        debounceLease?.Dispose();
        DisposeStaleCandidates(stale);
    }

    private void SchedulePreview(long version, long generation, TSnapshot snapshot)
    {
        IDisposable lease;
        try
        {
            lease = _scheduler.Schedule(
                _previewDebounce,
                () => DebounceElapsed(version));
        }
        catch (Exception exception)
        {
            FailureEnvelope? failure = null;
            lock (_sync)
            {
                if (!_disposed &&
                    version == _debounceVersion &&
                    _pending?.Generation == generation)
                {
                    BuildRequest request = _pending;
                    _pending = null;
                    _pendingReady = false;
                    _candidateFailureCount++;
                    failure = new(request, exception);
                }
            }
            if (failure != null) PostFailure(failure);
            return;
        }

        bool retained;
        lock (_sync)
        {
            retained = !_disposed &&
                version == _debounceVersion &&
                _pending?.Generation == generation &&
                !_pendingReady;
            if (retained)
                _debounceLease = lease;
        }
        if (!retained)
            lease.Dispose();
    }

    private void DebounceElapsed(long version)
    {
        BuildRequest? start;
        lock (_sync)
        {
            if (_disposed || version != _debounceVersion) return;
            _debounceLease = null;
            _pendingReady = true;
            start = TakeNextBuildLocked();
        }
        QueueBuild(start);
    }

    private BuildRequest? TakeNextBuildLocked()
    {
        if (_disposed || _buildInFlight || !_pendingReady || _pending == null)
            return null;
        BuildRequest request = _pending;
        _pending = null;
        _pendingReady = false;
        _buildInFlight = true;
        _concurrentBuildCount++;
        _maxConcurrentBuildCount = Math.Max(_maxConcurrentBuildCount, _concurrentBuildCount);
        _buildStartCount++;
        return request;
    }

    private void QueueBuild(BuildRequest? request)
    {
        if (request == null) return;
        try
        {
            _worker.Queue(() => RunBuild(request));
        }
        catch (Exception exception)
        {
            CompleteBuild(request, null, exception);
        }
    }

    private void RunBuild(BuildRequest request)
    {
        TCandidate? candidate = null;
        Exception? failure = null;
        try
        {
            candidate = _builder(request.Snapshot) ??
                throw new InvalidDataException("Universal render builder returned no candidate.");
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        CompleteBuild(request, candidate, failure);
    }

    private void CompleteBuild(
        BuildRequest request,
        TCandidate? candidate,
        Exception? failure)
    {
        CandidateEnvelope? envelope = null;
        FailureEnvelope? failureEnvelope = null;
        BuildRequest? next;
        bool disposeCandidate = false;
        lock (_sync)
        {
            _buildCompleteCount++;
            _buildInFlight = false;
            _concurrentBuildCount--;
            if (candidate != null)
            {
                if (_disposed || request.Generation != _latestGeneration)
                {
                    MarkStaleLocked();
                    disposeCandidate = true;
                }
                else
                {
                    envelope = new(request, candidate);
                    _awaitingPublish.Add(envelope);
                }
            }
            else
            {
                failure ??= new InvalidDataException(
                    "Universal render build completed without a candidate or failure.");
                _candidateFailureCount++;
                if (!_disposed && request.Generation == _latestGeneration)
                    failureEnvelope = new(request, failure);
            }
            next = TakeNextBuildLocked();
        }

        if (disposeCandidate)
        {
            candidate!.Dispose();
            lock (_sync) _staleCandidateDisposeCount++;
        }
        if (envelope != null) PostCandidate(envelope);
        if (failureEnvelope != null) PostFailure(failureEnvelope);
        QueueBuild(next);
    }

    private void PostCandidate(CandidateEnvelope envelope)
    {
        try
        {
            _dispatcher.Post(() => PublishCandidate(envelope));
        }
        catch
        {
            TCandidate? candidate;
            bool stale;
            lock (_sync)
            {
                _awaitingPublish.Remove(envelope);
                candidate = envelope.Take();
                stale = _disposed || envelope.Request.Generation != _latestGeneration;
                if (stale)
                    MarkStaleLocked();
                else
                    _candidateFailureCount++;
            }
            candidate?.Dispose();
            if (candidate != null && stale)
            {
                lock (_sync) _staleCandidateDisposeCount++;
            }
            // A dispatcher that rejected the post cannot safely receive the
            // corresponding UI-affine failure notification.
        }
    }

    private void PublishCandidate(CandidateEnvelope envelope)
    {
        TCandidate? candidate;
        IDisposable? previous = null;
        Exception? failure = null;
        bool published = false;
        bool staleCandidate = false;
        lock (_sync)
        {
            if (!_awaitingPublish.Remove(envelope)) return;
            candidate = envelope.Take();
            if (candidate == null) return;
            if (_disposed || envelope.Request.Generation != _latestGeneration)
            {
                MarkStaleLocked();
                staleCandidate = true;
            }
            else
            {
                try
                {
                    // The publish delegate is intentionally limited to the active
                    // reference swap. It must not render, wait, or dispose here.
                    previous = _publish(envelope.Request.Snapshot, candidate);
                    candidate = null;
                    _publishedGeneration = envelope.Request.Generation;
                    _publishCount++;
                    published = true;
                }
                catch (Exception exception)
                {
                    failure = exception;
                    _candidateFailureCount++;
                }
            }
        }

        if (candidate != null)
        {
            candidate.Dispose();
            if (staleCandidate)
            {
                lock (_sync) _staleCandidateDisposeCount++;
            }
        }
        previous?.Dispose();
        if (failure != null)
            _failed(envelope.Request.Snapshot, failure);
        else if (published)
            _published(envelope.Request.Snapshot);
    }

    private void PostFailure(FailureEnvelope envelope)
    {
        try
        {
            _dispatcher.Post(() =>
            {
                lock (_sync)
                {
                    if (_disposed || envelope.Request.Generation != _latestGeneration)
                        return;
                }
                _failed(envelope.Request.Snapshot, envelope.Exception);
            });
        }
        catch
        {
            // The active candidate remains authoritative. A dispatcher that is
            // shutting down cannot safely receive an error callback.
        }
    }

    private void CollectAwaitingAsStaleLocked(
        long latestGeneration,
        ICollection<TCandidate> stale)
    {
        foreach (CandidateEnvelope envelope in _awaitingPublish
                     .Where(value => value.Request.Generation != latestGeneration)
                     .ToArray())
        {
            _awaitingPublish.Remove(envelope);
            TCandidate? candidate = envelope.Take();
            if (candidate == null) continue;
            MarkStaleLocked();
            stale.Add(candidate);
        }
    }

    private void DisposeStaleCandidates(IEnumerable<TCandidate> candidates)
    {
        int disposed = 0;
        foreach (TCandidate candidate in candidates)
        {
            candidate.Dispose();
            disposed++;
        }
        if (disposed == 0) return;
        lock (_sync) _staleCandidateDisposeCount += disposed;
    }

    private void MarkStaleLocked() => _staleCandidateCount++;

    private sealed record BuildRequest(
        long Generation,
        TSnapshot Snapshot,
        UniversalRenderRequestIntent Intent);

    private sealed class CandidateEnvelope
    {
        private TCandidate? _candidate;

        public CandidateEnvelope(BuildRequest request, TCandidate candidate)
        {
            Request = request;
            _candidate = candidate;
        }

        public BuildRequest Request { get; }

        public TCandidate? Take() => Interlocked.Exchange(ref _candidate, null);
    }

    private sealed record FailureEnvelope(BuildRequest Request, Exception Exception);
}
