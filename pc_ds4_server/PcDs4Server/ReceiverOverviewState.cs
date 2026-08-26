using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcDs4Server;

internal sealed record ReceiverOverviewOption(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("label")] string Label);

internal sealed record ReceiverOverviewState(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("vigemStatus")] string VigemStatus,
    [property: JsonPropertyName("virtualDs4Status")] string VirtualDs4Status,
    [property: JsonPropertyName("phoneStatus")] string PhoneStatus,
    [property: JsonPropertyName("port")] int Port,
    [property: JsonPropertyName("outputMode")] string OutputMode,
    [property: JsonPropertyName("outputModeValue")] string OutputModeValue,
    [property: JsonPropertyName("outputModeOptions")] IReadOnlyList<ReceiverOverviewOption> OutputModeOptions,
    [property: JsonPropertyName("keyboardKeyOptions")] IReadOnlyList<ReceiverOverviewOption> KeyboardKeyOptions,
    [property: JsonPropertyName("outputModeEditable")] bool OutputModeEditable,
    [property: JsonPropertyName("keyboardMappingsEditable")] bool KeyboardMappingsEditable,
    [property: JsonPropertyName("serviceRunning")] bool ServiceRunning,
    [property: JsonPropertyName("startStopLabel")] string StartStopLabel,
    [property: JsonPropertyName("mappings")] IReadOnlyDictionary<string, string> Mappings)
{
    public const string MessageType = "receiverOverviewState";

    public string ToJson() => JsonSerializer.Serialize(this);
}

internal static class ReceiverOverviewCatalog
{
    public static IReadOnlyList<ReceiverOverviewOption> OutputModeOptions { get; } =
        Enum.GetValues<OutputMode>()
            .Select(mode => new ReceiverOverviewOption(
                mode.ToString(),
                MainForm.GetOutputModeDisplayText(mode)))
            .ToArray();

    public static IReadOnlyList<ReceiverOverviewOption> KeyboardKeyOptions { get; } =
        Enum.GetValues<KeyboardKey>()
            .Select(key => new ReceiverOverviewOption(key.ToString(), key.ToString()))
            .ToArray();
}

internal enum ReceiverOverviewCommand
{
    StartStop,
    RequestState,
    BeginDrag,
    Minimize,
    CloseWindow,
    ShowGamepad,
    ShowSettings,
    ShowLogs,
    SetOutputMode,
    SetKeyboardMapping
}

internal sealed record ReceiverOverviewCommandRequest(
    ReceiverOverviewCommand Command,
    OutputMode? OutputMode = null,
    string? Action = null,
    KeyboardKey? Key = null);

internal static class ReceiverOverviewCommandAllowList
{
    private static readonly IReadOnlyDictionary<string, ReceiverOverviewCommand> Commands =
        new Dictionary<string, ReceiverOverviewCommand>(StringComparer.Ordinal)
        {
            ["startStop"] = ReceiverOverviewCommand.StartStop,
            ["requestState"] = ReceiverOverviewCommand.RequestState,
            ["beginDrag"] = ReceiverOverviewCommand.BeginDrag,
            ["minimize"] = ReceiverOverviewCommand.Minimize,
            ["closeWindow"] = ReceiverOverviewCommand.CloseWindow,
            ["showGamepad"] = ReceiverOverviewCommand.ShowGamepad,
            ["showSettings"] = ReceiverOverviewCommand.ShowSettings,
            ["showLogs"] = ReceiverOverviewCommand.ShowLogs,
            ["setOutputMode"] = ReceiverOverviewCommand.SetOutputMode,
            ["setKeyboardMapping"] = ReceiverOverviewCommand.SetKeyboardMapping
        };

    private static readonly HashSet<string> ProtocolActions =
        new(KeyboardBindings.ProtocolActions, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> AllowedNames => Commands.Keys.ToArray();

    public static bool TryParse(
        string json,
        out ReceiverOverviewCommandRequest request,
        out string rejectionReason)
    {
        request = new ReceiverOverviewCommandRequest(default);
        rejectionReason = string.Empty;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("command", out JsonElement commandElement) ||
                commandElement.ValueKind != JsonValueKind.String)
            {
                rejectionReason = "Bridge message must contain a string command.";
                return false;
            }

            string? commandName = commandElement.GetString();
            if (commandName == null || !Commands.TryGetValue(commandName, out ReceiverOverviewCommand command))
            {
                rejectionReason = $"Unknown bridge command '{commandName ?? "<null>"}'.";
                return false;
            }

            switch (command)
            {
                case ReceiverOverviewCommand.SetOutputMode:
                    return TryParseOutputMode(root, command, out request, out rejectionReason);
                case ReceiverOverviewCommand.SetKeyboardMapping:
                    return TryParseKeyboardMapping(root, command, out request, out rejectionReason);
                default:
                    if (!HasExactProperties(root, "command"))
                    {
                        rejectionReason = $"Bridge command '{commandName}' contains unexpected properties.";
                        return false;
                    }
                    request = new ReceiverOverviewCommandRequest(command);
                    return true;
            }
        }
        catch (JsonException exception)
        {
            rejectionReason = $"Invalid bridge JSON: {exception.Message}";
            return false;
        }
    }

    private static bool TryParseOutputMode(
        JsonElement root,
        ReceiverOverviewCommand command,
        out ReceiverOverviewCommandRequest request,
        out string rejectionReason)
    {
        request = new ReceiverOverviewCommandRequest(default);
        if (!HasExactProperties(root, "command", "value") ||
            !root.TryGetProperty("value", out JsonElement valueElement) ||
            valueElement.ValueKind != JsonValueKind.String)
        {
            rejectionReason = "setOutputMode requires only a string value payload.";
            return false;
        }

        string? value = valueElement.GetString();
        if (!TryParseExactEnum(value, out OutputMode mode))
        {
            rejectionReason = $"Invalid output mode '{value ?? "<null>"}'.";
            return false;
        }

        request = new ReceiverOverviewCommandRequest(command, OutputMode: mode);
        rejectionReason = string.Empty;
        return true;
    }

    private static bool TryParseKeyboardMapping(
        JsonElement root,
        ReceiverOverviewCommand command,
        out ReceiverOverviewCommandRequest request,
        out string rejectionReason)
    {
        request = new ReceiverOverviewCommandRequest(default);
        if (!HasExactProperties(root, "command", "action", "key") ||
            !root.TryGetProperty("action", out JsonElement actionElement) ||
            actionElement.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("key", out JsonElement keyElement) ||
            keyElement.ValueKind != JsonValueKind.String)
        {
            rejectionReason = "setKeyboardMapping requires only string action and key payloads.";
            return false;
        }

        string? action = actionElement.GetString();
        if (action == null || !ProtocolActions.Contains(action))
        {
            rejectionReason = $"Invalid keyboard mapping action '{action ?? "<null>"}'.";
            return false;
        }

        string? keyValue = keyElement.GetString();
        if (!TryParseExactEnum(keyValue, out KeyboardKey key))
        {
            rejectionReason = $"Invalid keyboard key '{keyValue ?? "<null>"}'.";
            return false;
        }

        request = new ReceiverOverviewCommandRequest(command, Action: action, Key: key);
        rejectionReason = string.Empty;
        return true;
    }

    private static bool TryParseExactEnum<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        result = default;
        return value != null &&
            Enum.TryParse(value, ignoreCase: false, out result) &&
            Enum.IsDefined(result) &&
            string.Equals(result.ToString(), value, StringComparison.Ordinal);
    }

    private static bool HasExactProperties(JsonElement root, params string[] expected)
    {
        var actual = new HashSet<string>(StringComparer.Ordinal);
        int count = 0;
        foreach (JsonProperty property in root.EnumerateObject())
        {
            count++;
            if (!actual.Add(property.Name))
                return false;
        }

        return count == expected.Length && expected.All(actual.Contains);
    }
}
