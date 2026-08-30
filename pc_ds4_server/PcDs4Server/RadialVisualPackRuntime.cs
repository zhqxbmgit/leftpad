using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PcDs4Server;

internal sealed record UniversalRenderAuthorityKey(
    string RequestedThemeId,
    string SourcePlanIdentity,
    string MappingProfileId,
    RadialMappingsByProfile MappingsByProfile,
    UniversalRadialParameters? EffectiveParameters,
    RadialMenuSettings? UnresolvedSettings,
    int TargetSize,
    int Dpi,
    RadialRenderPolicy Policy);

internal sealed class UniversalRenderRequestSnapshot :
    IEquatable<UniversalRenderRequestSnapshot>
{
    public UniversalRenderRequestSnapshot(
        RadialMenuSettings settings,
        RadialVisualPackCatalogSnapshot catalog,
        RadialVisualPackCatalogEntry? target,
        UniversalRenderAuthorityKey authority,
        int targetSize,
        int dpi,
        bool allowFallback,
        UniversalRenderRequestIntent intent)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Target = target;
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
        TargetSize = targetSize;
        Dpi = dpi;
        AllowFallback = allowFallback;
        Intent = intent;
    }

    public RadialMenuSettings Settings { get; }
    public RadialVisualPackCatalogSnapshot Catalog { get; }
    public RadialVisualPackCatalogEntry? Target { get; }
    public UniversalRenderAuthorityKey Authority { get; }
    public int TargetSize { get; }
    public int Dpi { get; }
    public bool AllowFallback { get; }
    public UniversalRenderRequestIntent Intent { get; }

    public bool Equals(UniversalRenderRequestSnapshot? other) =>
        other != null && Authority == other.Authority;

    public override bool Equals(object? obj) =>
        Equals(obj as UniversalRenderRequestSnapshot);

    public override int GetHashCode() => Authority.GetHashCode();
}

internal sealed class UniversalDetachedRenderCandidate : IDisposable
{
    private RadialVisualPackSession? _session;

    public UniversalDetachedRenderCandidate(
        RadialVisualPackSession session,
        string requestedThemeId,
        IReadOnlyList<string> attemptedThemeIds,
        string? fallbackReason = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        RequestedThemeId = requestedThemeId;
        AttemptedThemeIds = attemptedThemeIds ?? throw new ArgumentNullException(nameof(attemptedThemeIds));
        FallbackReason = fallbackReason;
    }

    public string RequestedThemeId { get; }
    public IReadOnlyList<string> AttemptedThemeIds { get; }
    public string? FallbackReason { get; }
    public bool IsDisposed { get; private set; }

    public RadialVisualPackSession TransferSession() =>
        Interlocked.Exchange(ref _session, null) ??
        throw new InvalidOperationException("Universal candidate session ownership was already transferred.");

    public void Dispose()
    {
        RadialVisualPackSession? session = Interlocked.Exchange(ref _session, null);
        session?.Dispose();
        IsDisposed = true;
    }
}

internal sealed class RadialVisualPackSession : IDisposable
{
    private readonly RuntimeRenderBundleTargetBuilder _bundleBuilder;
    private readonly RadialVisualPackDefinition? _definition;
    private readonly RadialRenderPolicy _renderPolicy;
    private RuntimeRenderBundle _bundle;
    private bool _disposed;

    public RadialVisualPackSession(RadialVisualPackDefinition definition,
        RadialMenuSettings settings, int targetSize)
        : this(definition, V1VisualPackCompatibilityAdapter.BuildPlan(definition), settings,
            targetSize, RadialDpiScaling.DefaultDpi, RadialRenderPolicy.Legacy,
            RuntimeRenderBundle.Build) { }

    internal RadialVisualPackSession(RadialVisualPackDefinition definition,
        NormalizedRenderPlan plan, RadialMenuSettings settings, int targetSize,
        RuntimeRenderBundleBuilder builder)
        : this(definition, plan, settings, targetSize, RadialDpiScaling.DefaultDpi,
            RadialRenderPolicy.Legacy,
            (candidate, candidateSettings, size, _) => builder(candidate, candidateSettings, size)) { }

    internal RadialVisualPackSession(RadialVisualPackCatalogEntry entry,
        RadialMenuSettings settings, int targetSize, int dpi,
        RuntimeRenderBundleTargetBuilder? builder = null)
        : this(entry.V1Definition, entry.Plan, settings, targetSize, dpi,
            RadialRenderPolicy.Legacy,
            builder ?? RuntimeRenderBundle.Build) { }

    internal RadialVisualPackSession(RadialVisualPackCatalogEntry entry,
        RadialMenuSettings settings, int targetSize, int dpi,
        RadialRenderPolicy renderPolicy,
        RuntimeRenderBundleTargetBuilder? builder = null)
        : this(entry.V1Definition, entry.Plan, settings, targetSize, dpi,
            renderPolicy,
            builder ?? RadialRenderPolicyAuthority.CreateBundleBuilder(renderPolicy)) { }

    private RadialVisualPackSession(RadialVisualPackDefinition? definition,
        NormalizedRenderPlan plan, RadialMenuSettings settings, int targetSize, int dpi,
        RadialRenderPolicy renderPolicy,
        RuntimeRenderBundleTargetBuilder builder)
    {
        _definition = definition;
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(renderPolicy)) throw new ArgumentOutOfRangeException(nameof(renderPolicy));
        _renderPolicy = renderPolicy;
        _bundleBuilder = builder ?? throw new ArgumentNullException(nameof(builder));
        if (definition != null && !string.Equals(definition.Manifest.Id, plan.ThemeId, StringComparison.Ordinal))
            throw new InvalidDataException("V1 definition and normalized plan IDs do not match.");
        Mappings = settings.GetProfileMappings(LayoutDefinition.ProfileId);
        _bundle = _bundleBuilder(Plan, settings, targetSize, dpi) ??
            throw new InvalidDataException("Runtime render bundle builder returned no bundle.");
        if (_renderPolicy == RadialRenderPolicy.UniversalInitial && !_bundle.IsUniversal)
        {
            _bundle.Dispose();
            throw new InvalidDataException("UniversalInitial policy returned a legacy render bundle.");
        }
    }

    public RadialVisualPackDefinition Definition => _definition ??
        throw new InvalidOperationException("V2 sessions do not have a V1 definition.");
    public NormalizedRenderPlan Plan { get; }
    public LayoutDefinition LayoutDefinition => Plan.LayoutDefinition;
    public RuntimeRenderBundle Bundle => _bundle;
    public RadialVisualPackCache AssetCache => _bundle.AssetCache;
    public RadialDynamicContentCache DynamicContent => _bundle.DynamicContent;
    public RadialSlotMappings Mappings { get; private set; }
    public string PackId => Plan.ThemeId;
    internal RadialRenderPolicy RenderPolicy => _renderPolicy;
    internal bool IsDisposed => _disposed;

    public void EnsureContent(RadialMenuSettings settings, int targetSize) =>
        EnsureContent(settings, targetSize, RadialDpiScaling.DefaultDpi);

    public void EnsureContent(RadialMenuSettings settings, int targetSize, int dpi)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        bool rebuild;
        UniversalRadialParameters? effectiveParameters = null;
        if (_renderPolicy == RadialRenderPolicy.UniversalInitial)
        {
            effectiveParameters = ProductionUniversalRadialParametersAdapter.Adapt(settings, Plan);
            Size expectedSurface = UniversalRadialRenderPlan.Create(Plan, effectiveParameters)
                .PhysicalSurfaceSize(dpi);
            rebuild = _bundle.Dpi != dpi ||
                _bundle.UniversalPlan.Parameters.SurfaceScale != effectiveParameters.SurfaceScale ||
                _bundle.PhysicalSurfaceSize != expectedSurface;
        }
        else
        {
            rebuild = Plan.IsFullStateFrame ? _bundle.Dpi != dpi : _bundle.TargetSize != targetSize;
        }
        if (rebuild)
        {
            RadialSlotMappings mappings = settings.GetProfileMappings(LayoutDefinition.ProfileId);
            RuntimeRenderBundle replacement = _bundleBuilder(Plan, settings, targetSize, dpi) ??
                throw new InvalidDataException("Runtime render bundle builder returned no bundle.");
            if (_renderPolicy == RadialRenderPolicy.UniversalInitial && !replacement.IsUniversal)
            {
                replacement.Dispose();
                throw new InvalidDataException("UniversalInitial policy returned a legacy render bundle.");
            }
            RuntimeRenderBundle previous = _bundle;
            _bundle = replacement;
            Mappings = mappings;
            previous.Dispose();
            return;
        }
        if (effectiveParameters == null)
            _bundle.EnsureDynamicContent(settings);
        else
            _ = _bundle.EnsureUniversalContent(settings, effectiveParameters);
        Mappings = settings.GetProfileMappings(LayoutDefinition.ProfileId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bundle.Dispose();
    }
}

internal sealed class RadialVisualPackRuntime : IDisposable
{
    private readonly RadialVisualPackCatalog _catalog;
    private readonly Action<string> _log;
    private readonly RadialRenderPolicy _renderPolicy;
    private readonly RuntimeRenderBundleTargetBuilder _bundleBuilder;
    private RadialVisualPackSession? _active;
    private UniversalRenderRequestSnapshot? _activeAsyncSnapshot;
    private bool _activeAsyncWasPreview;
    private RadialVisualPackCatalogSnapshot? _asyncCatalogSnapshot;
    private bool _asyncCatalogIssuesLogged;
    private UniversalAsyncRenderCoordinator<
        UniversalRenderRequestSnapshot,
        UniversalDetachedRenderCandidate>? _asyncCoordinator;
    private string? _lastRequestedId;
    private bool _disposed;

    public RadialVisualPackRuntime(RadialVisualPackCatalog catalog, Action<string>? log = null)
        : this(catalog, RadialRenderPolicyAuthority.ProductionDefault, null, log) { }

    internal RadialVisualPackRuntime(RadialVisualPackCatalog catalog,
        RadialRenderPolicy renderPolicy,
        Action<string>? log = null)
        : this(catalog, renderPolicy, null, log) { }

    internal RadialVisualPackRuntime(RadialVisualPackCatalog catalog,
        RadialRenderPolicy renderPolicy,
        RuntimeRenderBundleTargetBuilder? bundleBuilder,
        Action<string>? log = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        if (!Enum.IsDefined(renderPolicy)) throw new ArgumentOutOfRangeException(nameof(renderPolicy));
        _renderPolicy = renderPolicy;
        _bundleBuilder = bundleBuilder ?? RadialRenderPolicyAuthority.CreateBundleBuilder(renderPolicy);
        _log = log ?? (message => Trace.TraceInformation(message));
    }
    public RadialVisualPackSession? Active => _active;
    public string? ActivePackId => _active?.PackId;
    public string? LastError { get; private set; }
    internal int InstallCount { get; private set; }
    internal RadialRenderPolicy RenderPolicy => _renderPolicy;
    internal bool IsUniversalAsyncEnabled => _asyncCoordinator != null;
    internal UniversalRenderRequestSnapshot? ActiveAsyncSnapshot => _activeAsyncSnapshot;
    internal UniversalAsyncRenderDiagnostics AsyncDiagnostics =>
        _asyncCoordinator?.Diagnostics ?? new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    internal void EnableUniversalAsync(
        IUniversalRenderDispatcher dispatcher,
        Action<UniversalRenderRequestSnapshot> published,
        Action<string>? failed = null,
        IUniversalRenderWorkQueue? worker = null,
        IUniversalRenderDebounceScheduler? scheduler = null,
        TimeSpan? previewDebounce = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(published);
        if (_renderPolicy != RadialRenderPolicy.UniversalInitial)
            throw new InvalidOperationException(
                "The async render coordinator is only valid for UniversalInitial policy.");
        if (_asyncCoordinator != null)
            throw new InvalidOperationException("The async render coordinator is already enabled.");

        _asyncCoordinator = new(
            BuildDetachedUniversalCandidate,
            PublishDetachedUniversalCandidate,
            snapshot =>
            {
                RadialVisualPackSession? active = _active;
                if (active != null)
                {
                    _log($"[视觉主题] Loaded radial visual pack asynchronously: {active.PackId}");
                }
                published(snapshot);
            },
            (snapshot, exception) =>
            {
                LastError = exception.Message;
                _lastRequestedId = snapshot.Settings.VisualPackId;
                _log($"[视觉主题] Async Universal candidate '{snapshot.Settings.VisualPackId}' failed: {exception.Message}");
                if (_active != null) LogRetained(snapshot.Settings.VisualPackId);
                failed?.Invoke(exception.Message);
            },
            worker,
            dispatcher,
            scheduler,
            previewDebounce);
    }

    internal long RequestUniversalAsync(
        RadialMenuSettings settings,
        int targetSize,
        int dpi,
        UniversalRenderRequestIntent intent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        if (targetSize <= 0) throw new ArgumentOutOfRangeException(nameof(targetSize));
        UniversalAsyncRenderCoordinator<
            UniversalRenderRequestSnapshot,
            UniversalDetachedRenderCandidate> coordinator = _asyncCoordinator ??
            throw new InvalidOperationException("The async render coordinator is not enabled.");
        UniversalRenderRequestSnapshot snapshot = CreateUniversalSnapshot(
            settings,
            targetSize,
            dpi,
            intent);

        if (coordinator.TryGetLatest(
                out UniversalRenderRequestSnapshot? latest,
                out UniversalRenderRequestIntent? latestIntent,
                out long latestGeneration) &&
            latest!.Equals(snapshot) &&
            (intent != UniversalRenderRequestIntent.Committed ||
             latestIntent == UniversalRenderRequestIntent.Committed))
        {
            return latestGeneration;
        }
        if (_activeAsyncSnapshot?.Equals(snapshot) == true)
        {
            long generation = coordinator.ReassertPublished(snapshot, intent);
            if (intent == UniversalRenderRequestIntent.Committed)
                _activeAsyncWasPreview = false;
            return generation;
        }
        return coordinator.Request(snapshot, intent);
    }

    internal void CancelUniversalPreview()
    {
        _asyncCoordinator?.CancelPreview();
        if (_activeAsyncWasPreview)
        {
            _activeAsyncSnapshot = null;
            _activeAsyncWasPreview = false;
        }
    }

    public RadialVisualPackSession? Ensure(RadialMenuSettings settings, int targetSize) =>
        Ensure(settings, targetSize, RadialDpiScaling.DefaultDpi);

    public RadialVisualPackSession? Ensure(RadialMenuSettings settings, int targetSize, int dpi)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        if (targetSize <= 0) throw new ArgumentOutOfRangeException(nameof(targetSize));
        string requestedId = settings.VisualPackId;
        if (requestedId == _lastRequestedId)
            return _active == null ? null : EnsureExisting(_active, settings, targetSize, dpi, requestedId);

        RadialVisualPackCatalogSnapshot snapshot = _catalog.Discover();
        LogCatalogIssues(snapshot);
        RadialVisualPackCatalogEntry? target = snapshot.Find(requestedId);
        if (target == null)
        {
            LogFallback(
                requestedId,
                $"requested pack '{requestedId}' is unavailable or incompatible",
                RadialVisualPackContract.FallbackVisualPackId);
            _lastRequestedId = requestedId;
            if (_active != null) { LogRetained(requestedId); return _active; }
            return TryInstallFallback(
                snapshot,
                settings,
                targetSize,
                dpi,
                failedThemeId: requestedId,
                preferredFallbackId: null);
        }
        _lastRequestedId = requestedId;
        if (_active?.PackId == target.Id)
            return EnsureExisting(_active, settings, targetSize, dpi, requestedId);
        if (TryCreateSession(target, settings, targetSize, dpi, out RadialVisualPackSession? replacement))
            return Install(replacement!);

        string failure = LastError ?? "unknown load failure";
        string preferredFallbackId = target.Plan.Fallback.StartupFallbackThemeId;
        LogFallback(
            requestedId,
            failure,
            string.Equals(preferredFallbackId, target.Id, StringComparison.Ordinal)
                ? RadialVisualPackContract.FallbackVisualPackId
                : preferredFallbackId);
        if (_active != null) { LogRetained(requestedId); return _active; }
        return TryInstallFallback(
            snapshot,
            settings,
            targetSize,
            dpi,
            target.Id,
            preferredFallbackId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _asyncCoordinator?.Dispose();
        _asyncCoordinator = null;
        _active?.Dispose();
        _active = null;
        _activeAsyncSnapshot = null;
        _activeAsyncWasPreview = false;
    }

    private RadialVisualPackSession EnsureExisting(RadialVisualPackSession session,
        RadialMenuSettings settings, int targetSize, int dpi, string requestedId)
    {
        try { session.EnsureContent(settings, targetSize, dpi); LastError = null; }
        catch (Exception exception) when (IsRuntimePackException(exception))
        {
            LastError = exception.Message;
            _log($"[视觉主题] Active pack '{session.PackId}' refresh failed for requested ID '{requestedId}': {exception.Message}");
        }
        return session;
    }

    private bool TryCreateSession(RadialVisualPackCatalogEntry entry, RadialMenuSettings settings,
        int targetSize, int dpi, out RadialVisualPackSession? session)
    {
        try
        {
            session = new(entry, settings, targetSize, dpi, _renderPolicy, _bundleBuilder);
            LastError = null;
            return true;
        }
        catch (Exception exception) when (IsRuntimePackException(exception))
        { LastError = exception.Message; session = null; return false; }
    }

    private RadialVisualPackSession Install(RadialVisualPackSession replacement)
    {
        RadialVisualPackSession? previous = _active;
        _active = replacement;
        _activeAsyncSnapshot = null;
        _activeAsyncWasPreview = false;
        InstallCount++;
        LastError = null;
        _log($"[视觉主题] Loaded radial visual pack: {replacement.PackId}");
        previous?.Dispose();
        return replacement;
    }

    private UniversalRenderRequestSnapshot CreateUniversalSnapshot(
        RadialMenuSettings settings,
        int targetSize,
        int dpi,
        UniversalRenderRequestIntent intent)
    {
        RadialMenuSettings immutableSettings = settings.NormalizeMappings();
        RadialVisualPackCatalogSnapshot catalog = _asyncCatalogSnapshot ??= _catalog.Discover();
        LogCatalogIssuesOnce(catalog);
        RadialVisualPackCatalogEntry? target = catalog.Find(immutableSettings.VisualPackId);
        UniversalRadialParameters? effectiveParameters = target == null
            ? null
            : ProductionUniversalRadialParametersAdapter.Adapt(immutableSettings, target.Plan);
        string sourceIdentity = target == null
            ? $"missing:{immutableSettings.VisualPackId}"
            : $"{target.Id}\0{target.Version}\0{target.DirectoryPath}";
        string mappingProfileId = target?.LayoutDefinition.ProfileId ??
            immutableSettings.MappingProfileId;
        var authority = new UniversalRenderAuthorityKey(
            immutableSettings.VisualPackId,
            sourceIdentity,
            mappingProfileId,
            immutableSettings.MappingsByProfile,
            effectiveParameters,
            target == null ? immutableSettings : null,
            targetSize,
            dpi,
            _renderPolicy);
        return new(
            immutableSettings,
            catalog,
            target,
            authority,
            targetSize,
            dpi,
            allowFallback: _active == null || _activeAsyncSnapshot == null,
            intent: intent);
    }

    private void LogCatalogIssuesOnce(RadialVisualPackCatalogSnapshot snapshot)
    {
        if (_asyncCatalogIssuesLogged || !ReferenceEquals(snapshot, _asyncCatalogSnapshot)) return;
        _asyncCatalogIssuesLogged = true;
        foreach (var issue in snapshot.Issues)
            _log($"[视觉主题] Ignored pack '{issue.PackId ?? issue.DirectoryPath}': {issue.Message}");
    }

    private UniversalDetachedRenderCandidate BuildDetachedUniversalCandidate(
        UniversalRenderRequestSnapshot snapshot)
    {
        var attempts = new List<string>();
        string requestedId = snapshot.Settings.VisualPackId;
        string? firstFailure = null;
        if (snapshot.Target != null)
        {
            attempts.Add(snapshot.Target.Id);
            if (TryCreateDetachedSession(snapshot.Target, snapshot, out RadialVisualPackSession? session,
                    out string failure))
            {
                return new(session!, requestedId, attempts.ToArray());
            }
            firstFailure = failure;
            if (!snapshot.AllowFallback)
                throw new InvalidDataException(
                    $"Universal candidate '{requestedId}' failed: {failure}");
        }
        else
        {
            firstFailure = $"requested pack '{requestedId}' is unavailable or incompatible";
            if (!snapshot.AllowFallback)
                throw new InvalidDataException(firstFailure);
        }

        var visited = new HashSet<string>(attempts, StringComparer.Ordinal)
        {
            requestedId
        };
        string? preferredFallbackId = snapshot.Target?.Plan.Fallback.StartupFallbackThemeId;
        foreach (string fallbackId in new[]
                 {
                     preferredFallbackId,
                     RadialVisualPackContract.FallbackVisualPackId
                 }
                 .Where(id => !string.IsNullOrWhiteSpace(id))
                 .Select(id => id!)
                 .Where(visited.Add))
        {
            RadialVisualPackCatalogEntry? fallback = snapshot.Catalog.Find(fallbackId);
            if (fallback == null) continue;
            attempts.Add(fallback.Id);
            if (TryCreateDetachedSession(fallback, snapshot, out RadialVisualPackSession? session,
                    out string failure))
            {
                return new(session!, requestedId, attempts.ToArray(), firstFailure);
            }
            firstFailure += $"; fallback '{fallback.Id}' failed: {failure}";
        }

        throw new InvalidDataException(
            firstFailure ?? $"No compatible Universal fallback was available for '{requestedId}'.");
    }

    private bool TryCreateDetachedSession(
        RadialVisualPackCatalogEntry entry,
        UniversalRenderRequestSnapshot snapshot,
        out RadialVisualPackSession? session,
        out string failure)
    {
        try
        {
            session = new(
                entry,
                snapshot.Settings,
                snapshot.TargetSize,
                snapshot.Dpi,
                _renderPolicy,
                _bundleBuilder);
            failure = string.Empty;
            return true;
        }
        catch (Exception exception) when (IsRuntimePackException(exception))
        {
            session = null;
            failure = exception.Message;
            return false;
        }
    }

    private IDisposable? PublishDetachedUniversalCandidate(
        UniversalRenderRequestSnapshot snapshot,
        UniversalDetachedRenderCandidate candidate)
    {
        RadialVisualPackSession replacement = candidate.TransferSession();
        if (candidate.FallbackReason != null)
            LogFallback(
                candidate.RequestedThemeId,
                candidate.FallbackReason,
                replacement.PackId);
        RadialVisualPackSession? previous = _active;
        _active = replacement;
        _activeAsyncSnapshot = snapshot;
        _activeAsyncWasPreview = snapshot.Intent == UniversalRenderRequestIntent.Preview;
        _lastRequestedId = snapshot.Settings.VisualPackId;
        LastError = null;
        InstallCount++;
        return previous;
    }

    private RadialVisualPackSession? TryInstallFallback(
        RadialVisualPackCatalogSnapshot snapshot,
        RadialMenuSettings settings,
        int targetSize,
        int dpi,
        string failedThemeId,
        string? preferredFallbackId)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { failedThemeId };
        foreach (string fallbackId in new[]
                 {
                     preferredFallbackId,
                     RadialVisualPackContract.FallbackVisualPackId
                 }
                 .Where(id => !string.IsNullOrWhiteSpace(id))
                 .Select(id => id!)
                 .Where(visited.Add))
        {
            RadialVisualPackCatalogEntry? fallback = snapshot.Find(fallbackId);
            if (fallback == null) continue;
            if (TryCreateSession(
                    fallback,
                    settings,
                    targetSize,
                    dpi,
                    out RadialVisualPackSession? replacement))
            {
                return Install(replacement!);
            }
        }

        LastError ??=
            $"No compatible fallback pack '{RadialVisualPackContract.FallbackVisualPackId}' was found.";
        _log($"[视觉主题] {LastError}");
        return _active;
    }

    private void LogCatalogIssues(RadialVisualPackCatalogSnapshot snapshot)
    { foreach (var issue in snapshot.Issues) _log($"[视觉主题] Ignored pack '{issue.PackId ?? issue.DirectoryPath}': {issue.Message}"); }
    private void LogFallback(string id, string reason, string fallbackId) => _log(
        $"[视觉主题] Requested pack '{id}' failed: {reason}; fallback '{fallbackId}'.");
    private void LogRetained(string id) => _log(
        $"[视觉主题] Retaining active pack '{_active!.PackId}' after failed switch to '{id}'.");
    private static bool IsRuntimePackException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or
        ExternalException or OutOfMemoryException or InvalidOperationException or NotSupportedException;
}
