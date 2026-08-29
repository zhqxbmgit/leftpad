using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PcDs4Server;

internal sealed class RadialVisualPackSession : IDisposable
{
    private readonly RuntimeRenderBundleTargetBuilder _bundleBuilder;
    private readonly RadialVisualPackDefinition? _definition;
    private RuntimeRenderBundle _bundle;
    private bool _disposed;

    public RadialVisualPackSession(RadialVisualPackDefinition definition,
        RadialMenuSettings settings, int targetSize)
        : this(definition, V1VisualPackCompatibilityAdapter.BuildPlan(definition), settings,
            targetSize, RadialDpiScaling.DefaultDpi, RuntimeRenderBundle.Build) { }

    internal RadialVisualPackSession(RadialVisualPackDefinition definition,
        NormalizedRenderPlan plan, RadialMenuSettings settings, int targetSize,
        RuntimeRenderBundleBuilder builder)
        : this(definition, plan, settings, targetSize, RadialDpiScaling.DefaultDpi,
            (candidate, candidateSettings, size, _) => builder(candidate, candidateSettings, size)) { }

    internal RadialVisualPackSession(RadialVisualPackCatalogEntry entry,
        RadialMenuSettings settings, int targetSize, int dpi,
        RuntimeRenderBundleTargetBuilder? builder = null)
        : this(entry.V1Definition, entry.Plan, settings, targetSize, dpi,
            builder ?? RuntimeRenderBundle.Build) { }

    private RadialVisualPackSession(RadialVisualPackDefinition? definition,
        NormalizedRenderPlan plan, RadialMenuSettings settings, int targetSize, int dpi,
        RuntimeRenderBundleTargetBuilder builder)
    {
        _definition = definition;
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        ArgumentNullException.ThrowIfNull(settings);
        _bundleBuilder = builder ?? throw new ArgumentNullException(nameof(builder));
        if (definition != null && !string.Equals(definition.Manifest.Id, plan.ThemeId, StringComparison.Ordinal))
            throw new InvalidDataException("V1 definition and normalized plan IDs do not match.");
        Mappings = settings.GetProfileMappings(LayoutDefinition.ProfileId);
        _bundle = _bundleBuilder(Plan, settings, targetSize, dpi) ??
            throw new InvalidDataException("Runtime render bundle builder returned no bundle.");
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
    internal bool IsDisposed => _disposed;

    public void EnsureContent(RadialMenuSettings settings, int targetSize) =>
        EnsureContent(settings, targetSize, RadialDpiScaling.DefaultDpi);

    public void EnsureContent(RadialMenuSettings settings, int targetSize, int dpi)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        bool rebuild = Plan.IsFullStateFrame ? _bundle.Dpi != dpi : _bundle.TargetSize != targetSize;
        if (rebuild)
        {
            RadialSlotMappings mappings = settings.GetProfileMappings(LayoutDefinition.ProfileId);
            RuntimeRenderBundle replacement = _bundleBuilder(Plan, settings, targetSize, dpi) ??
                throw new InvalidDataException("Runtime render bundle builder returned no bundle.");
            RuntimeRenderBundle previous = _bundle;
            _bundle = replacement;
            Mappings = mappings;
            previous.Dispose();
            return;
        }
        _bundle.EnsureDynamicContent(settings);
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
    private RadialVisualPackSession? _active;
    private string? _lastRequestedId;
    private bool _disposed;

    public RadialVisualPackRuntime(RadialVisualPackCatalog catalog, Action<string>? log = null)
    { _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); _log = log ?? (message => Trace.TraceInformation(message)); }
    public RadialVisualPackSession? Active => _active;
    public string? ActivePackId => _active?.PackId;
    public string? LastError { get; private set; }
    internal int InstallCount { get; private set; }

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
    { if (_disposed) return; _disposed = true; _active?.Dispose(); _active = null; }

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
        try { session = new(entry, settings, targetSize, dpi); LastError = null; return true; }
        catch (Exception exception) when (IsRuntimePackException(exception))
        { LastError = exception.Message; session = null; return false; }
    }

    private RadialVisualPackSession Install(RadialVisualPackSession replacement)
    {
        RadialVisualPackSession? previous = _active;
        _active = replacement;
        InstallCount++;
        LastError = null;
        _log($"[视觉主题] Loaded radial visual pack: {replacement.PackId}");
        previous?.Dispose();
        return replacement;
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
