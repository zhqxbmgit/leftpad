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
        WriteIndented = true
    };

    private readonly string _path;

    public RadialMenuSettingsStore(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LeftPad",
            "radial-menu-settings.json");
    }

    public string Path => _path;

    public RadialMenuSettingsLoadResult Load()
    {
        if (!File.Exists(_path))
            return new RadialMenuSettingsLoadResult(RadialMenuSettings.Default, RadialMenuSettingsLoadStatus.Missing);

        try
        {
            RadialMenuSettings? settings = JsonSerializer.Deserialize<RadialMenuSettings>(
                File.ReadAllText(_path),
                JsonOptions);
            if (settings == null)
            {
                return new RadialMenuSettingsLoadResult(
                    RadialMenuSettings.Default,
                    RadialMenuSettingsLoadStatus.Invalid,
                    "Settings file was empty.");
            }
            settings = settings with
            {
                SlotMappings = RadialSlotMappings.Sanitize(settings.SlotMappings)
            };
            if (!settings.TryValidate(out string validationError))
            {
                return new RadialMenuSettingsLoadResult(
                    RadialMenuSettings.Default,
                    RadialMenuSettingsLoadStatus.Invalid,
                    validationError);
            }

            return new RadialMenuSettingsLoadResult(settings, RadialMenuSettingsLoadStatus.Loaded);
        }
        catch (JsonException ex)
        {
            return new RadialMenuSettingsLoadResult(
                RadialMenuSettings.Default,
                RadialMenuSettingsLoadStatus.Malformed,
                ex.Message);
        }
        catch (Exception ex)
        {
            return new RadialMenuSettingsLoadResult(
                RadialMenuSettings.Default,
                RadialMenuSettingsLoadStatus.Failed,
                ex.Message);
        }
    }

    public bool TrySave(RadialMenuSettings settings, out string error)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryValidate(out error)) return false;

        string temporaryPath = _path + ".tmp";
        try
        {
            string? directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch { /* Best-effort cleanup must not mask the original save failure. */ }
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

        applySettings(temporarySettings);
        return store.TrySave(controller.ActiveSettings, out error);
    }
}
