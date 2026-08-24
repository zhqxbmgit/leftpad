using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcDs4Server;

internal sealed record SettingsBasicVisualPackOption(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("mappingProfileId")] string MappingProfileId);

internal sealed record SettingsBasicRange(
    [property: JsonPropertyName("min")] int Min,
    [property: JsonPropertyName("max")] int Max,
    [property: JsonPropertyName("step")] int Step);

internal sealed record ReceiverSettingsBasicState(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("connectionStatus")] string ConnectionStatus,
    [property: JsonPropertyName("visualPackId")] string VisualPackId,
    [property: JsonPropertyName("visualPackOptions")] IReadOnlyList<SettingsBasicVisualPackOption> VisualPackOptions,
    [property: JsonPropertyName("receiverUiScalePercent")] int ReceiverUiScalePercent,
    [property: JsonPropertyName("receiverUiScaleOptions")] IReadOnlyList<int> ReceiverUiScaleOptions,
    [property: JsonPropertyName("overallSizePercent")] int OverallSizePercent,
    [property: JsonPropertyName("doubleTapWindowMs")] int DoubleTapWindowMs,
    [property: JsonPropertyName("selectionDeadZone")] int SelectionDeadZone,
    [property: JsonPropertyName("selectedIntensity")] int SelectedIntensity,
    [property: JsonPropertyName("petalOpacity")] int PetalOpacity,
    [property: JsonPropertyName("borderOpacity")] int BorderOpacity,
    [property: JsonPropertyName("textOpacity")] int TextOpacity,
    [property: JsonPropertyName("ranges")] IReadOnlyDictionary<string, SettingsBasicRange> Ranges,
    [property: JsonPropertyName("previewActive")] bool PreviewActive,
    [property: JsonPropertyName("dirty")] bool Dirty,
    [property: JsonPropertyName("enabled")] bool Enabled)
{
    public const string MessageType = "receiverSettingsBasicState";

    public string ToJson() => JsonSerializer.Serialize(this);
}

internal static class SettingsBasicFields
{
    public const string VisualPackId = "visualPackId";
    public const string ReceiverUiScalePercent = "receiverUiScalePercent";
    public const string OverallSizePercent = "overallSizePercent";
    public const string DoubleTapWindowMs = "doubleTapWindowMs";
    public const string SelectionDeadZone = "selectionDeadZone";
    public const string SelectedIntensity = "selectedIntensity";
    public const string PetalOpacity = "petalOpacity";
    public const string BorderOpacity = "borderOpacity";
    public const string TextOpacity = "textOpacity";

    public static IReadOnlyList<string> All { get; } =
    [
        VisualPackId,
        ReceiverUiScalePercent,
        OverallSizePercent,
        DoubleTapWindowMs,
        SelectionDeadZone,
        SelectedIntensity,
        PetalOpacity,
        BorderOpacity,
        TextOpacity
    ];

    public static IReadOnlyDictionary<string, SettingsBasicRange> Ranges { get; } =
        new Dictionary<string, SettingsBasicRange>(StringComparer.Ordinal)
        {
            [OverallSizePercent] = new(
                RadialMenuSettings.MinimumScalePercent,
                RadialMenuSettings.MaximumScalePercent,
                1),
            [DoubleTapWindowMs] = new(
                RadialMenuSettings.MinimumDoubleTapWindowMs,
                RadialMenuSettings.MaximumDoubleTapWindowMs,
                1),
            [SelectionDeadZone] = new(
                RadialMenuSettings.MinimumSelectionDeadZone,
                RadialMenuSettings.MaximumSelectionDeadZone,
                1),
            [SelectedIntensity] = new(0, 255, 1),
            [PetalOpacity] = new(0, 255, 1),
            [BorderOpacity] = new(0, 255, 1),
            [TextOpacity] = new(0, 255, 1)
        };
}

internal enum ReceiverSettingsCommand
{
    BasicChange,
    Preview,
    HidePreview,
    ApplySave,
    RestoreDefault,
    RequestState,
    ShowOverview,
    ShowGamepad,
    ShowLogs,
    ShowAdvanced,
    ShowMappings,
    BeginDrag,
    Minimize,
    CloseWindow
}

internal sealed record ReceiverSettingsMessage(
    ReceiverSettingsCommand Command,
    string? Field = null,
    string? StringValue = null,
    int? IntegerValue = null);

internal static class ReceiverSettingsCommandAllowList
{
    private static readonly IReadOnlyDictionary<string, ReceiverSettingsCommand> Commands =
        new Dictionary<string, ReceiverSettingsCommand>(StringComparer.Ordinal)
        {
            ["settingsBasicChange"] = ReceiverSettingsCommand.BasicChange,
            ["settingsPreview"] = ReceiverSettingsCommand.Preview,
            ["settingsHidePreview"] = ReceiverSettingsCommand.HidePreview,
            ["settingsApplySave"] = ReceiverSettingsCommand.ApplySave,
            ["settingsRestoreDefault"] = ReceiverSettingsCommand.RestoreDefault,
            ["settingsRequestState"] = ReceiverSettingsCommand.RequestState,
            ["showOverview"] = ReceiverSettingsCommand.ShowOverview,
            ["showGamepad"] = ReceiverSettingsCommand.ShowGamepad,
            ["showLogs"] = ReceiverSettingsCommand.ShowLogs,
            ["showSettingsAdvanced"] = ReceiverSettingsCommand.ShowAdvanced,
            ["showSettingsMappings"] = ReceiverSettingsCommand.ShowMappings,
            ["beginDrag"] = ReceiverSettingsCommand.BeginDrag,
            ["minimize"] = ReceiverSettingsCommand.Minimize,
            ["closeWindow"] = ReceiverSettingsCommand.CloseWindow
        };

    public static IReadOnlyCollection<string> AllowedNames => Commands.Keys.ToArray();

    public static bool TryParse(
        string json,
        out ReceiverSettingsMessage message,
        out string rejectionReason)
    {
        message = new ReceiverSettingsMessage(default);
        rejectionReason = string.Empty;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("command", out JsonElement commandElement) ||
                commandElement.ValueKind != JsonValueKind.String)
            {
                return Reject("Bridge message must contain a string command.", out rejectionReason);
            }

            string? commandName = commandElement.GetString();
            if (commandName == null || !Commands.TryGetValue(commandName, out ReceiverSettingsCommand command))
            {
                return Reject(
                    $"Unknown settings bridge command '{commandName ?? "<null>"}'.",
                    out rejectionReason);
            }

            if (command != ReceiverSettingsCommand.BasicChange)
            {
                message = new ReceiverSettingsMessage(command);
                return true;
            }

            if (!root.TryGetProperty("field", out JsonElement fieldElement) ||
                fieldElement.ValueKind != JsonValueKind.String)
            {
                return Reject("settingsBasicChange requires a string field.", out rejectionReason);
            }
            string? field = fieldElement.GetString();
            if (field == null || !SettingsBasicFields.All.Contains(field, StringComparer.Ordinal))
                return Reject($"Unknown Settings Basic field '{field ?? "<null>"}'.", out rejectionReason);
            if (!root.TryGetProperty("value", out JsonElement valueElement))
                return Reject("settingsBasicChange requires a value.", out rejectionReason);

            if (field == SettingsBasicFields.VisualPackId)
            {
                if (valueElement.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(valueElement.GetString()))
                {
                    return Reject("visualPackId must be a non-empty string.", out rejectionReason);
                }
                message = new ReceiverSettingsMessage(command, field, valueElement.GetString());
                return true;
            }

            if (valueElement.ValueKind != JsonValueKind.Number ||
                !valueElement.TryGetInt32(out int integerValue))
            {
                return Reject($"{field} must be an integer.", out rejectionReason);
            }
            if (field == SettingsBasicFields.ReceiverUiScalePercent)
            {
                if (!ReceiverUiScaling.IsPreset(integerValue))
                    return Reject("receiverUiScalePercent is not an allowed preset.", out rejectionReason);
            }
            else
            {
                SettingsBasicRange range = SettingsBasicFields.Ranges[field];
                if (integerValue < range.Min || integerValue > range.Max ||
                    (integerValue - range.Min) % range.Step != 0)
                {
                    return Reject(
                        $"{field} must be between {range.Min} and {range.Max} in steps of {range.Step}.",
                        out rejectionReason);
                }
            }

            message = new ReceiverSettingsMessage(command, field, IntegerValue: integerValue);
            return true;
        }
        catch (JsonException exception)
        {
            return Reject($"Invalid settings bridge JSON: {exception.Message}", out rejectionReason);
        }
    }

    private static bool Reject(string reason, out string rejectionReason)
    {
        rejectionReason = reason;
        return false;
    }
}
