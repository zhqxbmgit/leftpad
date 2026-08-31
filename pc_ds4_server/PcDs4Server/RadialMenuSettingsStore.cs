using System.Text.Json;

namespace PcDs4Server;

public enum RadialMenuSettingsLoadStatus
{
    Loaded,
    Missing,
    Malformed,
    Invalid,
    Failed
}

public sealed record RadialMenuSettingsLoadResult(
    RadialMenuSettings Settings,
    RadialMenuSettingsLoadStatus Status,
    string? Error = null);

public sealed class RadialMenuSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly IAtomicFileOperations _files;

    public RadialMenuSettingsStore(string? path = null)
        : this(
            path ?? System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LeftPad",
                "radial-menu-settings.json"),
            SystemAtomicFileOperations.Instance)
    {
    }

    internal RadialMenuSettingsStore(string path, IAtomicFileOperations files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _files = files ?? throw new ArgumentNullException(nameof(files));
    }

    public string Path => _path;

    public RadialMenuSettingsLoadResult Load()
    {
        if (!_files.FileExists(_path))
            return new RadialMenuSettingsLoadResult(RadialMenuSettings.Default, RadialMenuSettingsLoadStatus.Missing);

        try
        {
            string content = _files.ReadAllText(_path);
            using JsonDocument document = JsonDocument.Parse(content);
            bool hasVisualPackId = document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.EnumerateObject().Any(property =>
                    string.Equals(
                        property.Name,
                        JsonOptions.PropertyNamingPolicy!.ConvertName(
                            nameof(RadialMenuSettings.VisualPackId)),
                        JsonOptions.PropertyNameCaseInsensitive
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal));
            RadialMenuSettings? settings = JsonSerializer.Deserialize<RadialMenuSettings>(
                content,
                JsonOptions);
            if (settings == null)
            {
                return new RadialMenuSettingsLoadResult(
                    RadialMenuSettings.SafeFallback,
                    RadialMenuSettingsLoadStatus.Invalid,
                    "Settings file was empty.");
            }
            if (!hasVisualPackId)
            {
                settings = settings with
                {
                    VisualPackId = RadialVisualPackContract.LegacyV1DefaultPackId,
                    MappingProfileId = RadialVisualPackContract.FallbackMappingProfileId
                };
            }
            settings = settings with
            {
                SlotMappings = RadialSlotMappings.Sanitize(settings.SlotMappings),
                ReceiverUiScalePercent = ReceiverUiScaling.Normalize(
                    settings.ReceiverUiScalePercent)
            };
            if (!settings.TryValidate(out string validationError))
            {
                return new RadialMenuSettingsLoadResult(
                    RadialMenuSettings.SafeFallback,
                    RadialMenuSettingsLoadStatus.Invalid,
                    validationError);
            }

            return new RadialMenuSettingsLoadResult(settings, RadialMenuSettingsLoadStatus.Loaded);
        }
        catch (JsonException ex)
        {
            return new RadialMenuSettingsLoadResult(
                RadialMenuSettings.SafeFallback,
                RadialMenuSettingsLoadStatus.Malformed,
                ex.Message);
        }
        catch (Exception ex)
        {
            return new RadialMenuSettingsLoadResult(
                RadialMenuSettings.SafeFallback,
                RadialMenuSettingsLoadStatus.Failed,
                ex.Message);
        }
    }

    public bool TrySave(RadialMenuSettings settings, out string error)
    {
        if (!TryBeginSave(
                settings,
                retainOriginalForRollback: false,
                out AtomicFileWriteTransaction? transaction,
                out error))
            return false;

        transaction!.Commit();
        return true;
    }

    internal bool TryBeginSave(
        RadialMenuSettings settings,
        bool retainOriginalForRollback,
        out AtomicFileWriteTransaction? transaction,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(settings);
        RadialMenuSettings candidate = settings.NormalizeMappings();
        if (!candidate.TryValidate(out error))
        {
            transaction = null;
            return false;
        }

        try
        {
            byte[] content = JsonSerializer.SerializeToUtf8Bytes(candidate, JsonOptions);
            return AtomicFilePersistence.TryWrite(
                _path,
                content,
                _files,
                retainOriginalForRollback,
                out transaction,
                out error,
                out _);
        }
        catch (Exception ex)
        {
            transaction = null;
            error = ex.Message;
            return false;
        }
    }
}

public static class RadialMenuSettingsPersistence
{
    public static bool TryApplyAndSave(
        RadialMenuController controller,
        RadialMenuSettingsStore store,
        RadialMenuSettings temporarySettings,
        Action<RadialMenuSettings> applySettings,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(temporarySettings);
        ArgumentNullException.ThrowIfNull(applySettings);

        RadialMenuSettings candidate = temporarySettings.NormalizeMappings();
        if (!candidate.TryValidate(out error)) return false;

        RadialMenuSettings runtimeSnapshot = controller.ConfiguredSettings.NormalizeMappings();
        if (!store.TryBeginSave(
                candidate,
                retainOriginalForRollback: true,
                out AtomicFileWriteTransaction? transaction,
                out error))
            return false;

        try
        {
            applySettings(candidate);
            if (controller.ConfiguredSettings.NormalizeMappings() != candidate)
            {
                throw new InvalidOperationException(
                    "Runtime settings did not match the persisted candidate after apply.");
            }

            transaction!.Commit();
            error = string.Empty;
            return true;
        }
        catch (Exception applyException)
        {
            string? runtimeRollbackError = null;
            try
            {
                applySettings(runtimeSnapshot);
            }
            catch (Exception rollbackException)
            {
                runtimeRollbackError = rollbackException.Message;
            }

            bool diskRestored = transaction!.TryRollback(out string diskRollbackError);
            error = $"运行时应用失败：{applyException.Message}";
            if (runtimeRollbackError != null)
                error += $"；运行时恢复失败：{runtimeRollbackError}";
            if (!diskRestored)
                error += $"；磁盘恢复失败：{diskRollbackError}";
            return false;
        }
    }
}
