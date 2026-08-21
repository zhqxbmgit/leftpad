using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PcDs4Server;

internal sealed class RadialVisualPackSession : IDisposable
{
    private bool _disposed;

    public RadialVisualPackSession(
        RadialVisualPackDefinition definition,
        RadialMenuSettings settings,
        int targetSize)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        AssetCache = new RadialVisualPackCache(definition, targetSize);
        RadialDynamicContentCache? dynamicContent = null;
        try
        {
            dynamicContent = new RadialDynamicContentCache(
                definition,
                WindowsUiFontResolver.ResolveUiFontFamily());
            dynamicContent.Ensure(settings, targetSize);
            DynamicContent = dynamicContent;
        }
        catch
        {
            dynamicContent?.Dispose();
            AssetCache.Dispose();
            throw;
        }
    }

    public RadialVisualPackDefinition Definition { get; }
    public RadialVisualPackCache AssetCache { get; }
    public RadialDynamicContentCache DynamicContent { get; }
    public string PackId => Definition.Manifest.Id;
    internal bool IsDisposed => _disposed;

    public void EnsureContent(RadialMenuSettings settings, int targetSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AssetCache.Rebuild(targetSize);
        DynamicContent.Ensure(settings, targetSize);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DynamicContent.Dispose();
        AssetCache.Dispose();
    }
}

internal sealed class RadialVisualPackRuntime : IDisposable
{
    private readonly RadialVisualPackCatalog _catalog;
    private readonly Action<string> _log;
    private RadialVisualPackSession? _active;
    private string? _lastRequestedId;
    private bool _disposed;

    public RadialVisualPackRuntime(
        RadialVisualPackCatalog catalog,
        Action<string>? log = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _log = log ?? (message => Trace.TraceInformation(message));
    }

    public RadialVisualPackSession? Active => _active;
    public string? ActivePackId => _active?.PackId;
    public string? LastError { get; private set; }
    internal int InstallCount { get; private set; }

    public RadialVisualPackSession? Ensure(
        RadialMenuSettings settings,
        int targetSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        if (targetSize <= 0) throw new ArgumentOutOfRangeException(nameof(targetSize));

        string requestedId = settings.VisualPackId;
        if (string.Equals(requestedId, _lastRequestedId, StringComparison.Ordinal))
        {
            if (_active == null) return null;
            return EnsureExisting(_active, settings, targetSize, requestedId);
        }

        RadialVisualPackCatalogSnapshot snapshot = _catalog.Discover();
        LogCatalogIssues(snapshot);
        RadialVisualPackCatalogEntry? requested = snapshot.Find(requestedId);
        RadialVisualPackCatalogEntry? target = requested;
        if (target == null)
        {
            string reason = $"requested pack '{requestedId}' is unavailable or incompatible";
            LogFallback(requestedId, reason);
            target = snapshot.Find(RadialVisualPackContract.DefaultVisualPackId);
        }

        _lastRequestedId = requestedId;
        if (target == null)
        {
            LastError =
                $"No compatible fallback pack '{RadialVisualPackContract.DefaultVisualPackId}' was found.";
            _log($"[视觉主题] {LastError}");
            return _active;
        }

        if (_active != null &&
            string.Equals(_active.PackId, target.Id, StringComparison.Ordinal))
        {
            return EnsureExisting(_active, settings, targetSize, requestedId);
        }

        if (TryCreateSession(target, settings, targetSize, out RadialVisualPackSession? replacement))
            return Install(replacement!);

        string targetFailure = LastError ?? "unknown load failure";
        LogFallback(requestedId, targetFailure);
        if (string.Equals(
                target.Id,
                RadialVisualPackContract.DefaultVisualPackId,
                StringComparison.Ordinal))
        {
            _log(
                $"[视觉主题] Fallback pack '{target.Id}' failed: " +
                targetFailure);
            return _active;
        }

        RadialVisualPackCatalogEntry? fallback = snapshot.Find(
            RadialVisualPackContract.DefaultVisualPackId);
        if (fallback == null)
            return _active;

        if (_active != null &&
            string.Equals(_active.PackId, fallback.Id, StringComparison.Ordinal))
        {
            return EnsureExisting(_active, settings, targetSize, requestedId);
        }

        if (!TryCreateSession(fallback, settings, targetSize, out replacement))
        {
            _log(
                $"[视觉主题] Fallback pack '{fallback.Id}' failed: " +
                $"{LastError ?? "unknown load failure"}");
            return _active;
        }

        return Install(replacement!);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _active?.Dispose();
        _active = null;
    }

    private RadialVisualPackSession? EnsureExisting(
        RadialVisualPackSession session,
        RadialMenuSettings settings,
        int targetSize,
        string requestedId)
    {
        try
        {
            session.EnsureContent(settings, targetSize);
            LastError = null;
            return session;
        }
        catch (Exception exception) when (IsRuntimePackException(exception))
        {
            LastError = exception.Message;
            _log(
                $"[视觉主题] Active pack '{session.PackId}' refresh failed for " +
                $"requested ID '{requestedId}': {exception.Message}");
            return null;
        }
    }

    private bool TryCreateSession(
        RadialVisualPackCatalogEntry entry,
        RadialMenuSettings settings,
        int targetSize,
        out RadialVisualPackSession? session)
    {
        try
        {
            session = new RadialVisualPackSession(entry.Definition, settings, targetSize);
            LastError = null;
            return true;
        }
        catch (Exception exception) when (IsRuntimePackException(exception))
        {
            LastError = exception.Message;
            session = null;
            return false;
        }
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

    private void LogCatalogIssues(RadialVisualPackCatalogSnapshot snapshot)
    {
        foreach (RadialVisualPackCatalogIssue issue in snapshot.Issues)
        {
            _log(
                $"[视觉主题] Ignored pack '{issue.PackId ?? issue.DirectoryPath}': " +
                issue.Message);
        }
    }

    private void LogFallback(string requestedId, string reason)
    {
        _log(
            $"[视觉主题] Requested pack '{requestedId}' failed: {reason}; " +
            $"fallback '{RadialVisualPackContract.DefaultVisualPackId}'.");
    }

    private static bool IsRuntimePackException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException or
        ArgumentException or ExternalException or OutOfMemoryException or
        InvalidOperationException or NotSupportedException;
}
