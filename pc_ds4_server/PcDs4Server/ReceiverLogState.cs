using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcDs4Server;

internal sealed record ReceiverLogEntry(
    [property: JsonPropertyName("sequenceId")] long SequenceId,
    [property: JsonPropertyName("rawText")] string RawText,
    [property: JsonPropertyName("category")] string Category);

internal sealed record ReceiverLogSnapshot(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("entries")] IReadOnlyList<ReceiverLogEntry> Entries,
    [property: JsonPropertyName("connectionState")] string ConnectionState)
{
    public const string MessageType = "receiverLogSnapshot";

    public string ToJson() => JsonSerializer.Serialize(this);
}

internal sealed record ReceiverLogAppend(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("entry")] ReceiverLogEntry Entry)
{
    public const string MessageType = "receiverLogAppend";

    public string ToJson() => JsonSerializer.Serialize(this);
}

internal sealed record ReceiverLogConnectionState(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("connectionState")] string ConnectionState)
{
    public const string MessageType = "receiverLogConnectionState";

    public string ToJson() => JsonSerializer.Serialize(this);
}

internal sealed class ReceiverLogBuffer
{
    public const int MaximumEntries = 300;

    private readonly object _sync = new();
    private readonly List<ReceiverLogEntry> _entries = [];
    private long _nextSequenceId = 1;

    public ReceiverLogEntry Append(string rawText)
    {
        ArgumentNullException.ThrowIfNull(rawText);

        lock (_sync)
        {
            var entry = new ReceiverLogEntry(
                _nextSequenceId++,
                rawText,
                ReceiverLogCategory.Classify(rawText));
            if (_entries.Count == MaximumEntries)
                _entries.RemoveAt(0);
            _entries.Add(entry);
            return entry;
        }
    }

    public ReceiverLogSnapshot CreateSnapshot(string connectionState)
    {
        lock (_sync)
        {
            return new ReceiverLogSnapshot(
                ReceiverLogSnapshot.MessageType,
                _entries.ToArray(),
                string.IsNullOrWhiteSpace(connectionState) ? "等待连接" : connectionState);
        }
    }
}

internal static class ReceiverLogCategory
{
    public static string Classify(string rawText)
    {
        ArgumentNullException.ThrowIfNull(rawText);

        if (rawText.Contains("[Warning]", StringComparison.OrdinalIgnoreCase))
            return "warning";
        if (rawText.Contains("[Controller]", StringComparison.OrdinalIgnoreCase))
            return "controller";
        if (rawText.Contains("[Network]", StringComparison.OrdinalIgnoreCase))
            return "network";
        if (rawText.Contains("[Info]", StringComparison.OrdinalIgnoreCase))
            return "info";
        if (rawText.Contains("[视觉主题]", StringComparison.Ordinal) ||
            rawText.Contains("[环形菜单]", StringComparison.Ordinal))
        {
            return "dreamscape";
        }

        return "default";
    }
}

internal enum ReceiverLogsCommand
{
    RequestSnapshot,
    ReturnToBottom,
    BeginDrag,
    Minimize,
    CloseWindow,
    ShowOverview,
    ShowController,
    ShowSettings
}

internal static class ReceiverLogsCommandAllowList
{
    private static readonly IReadOnlyDictionary<string, ReceiverLogsCommand> Commands =
        new Dictionary<string, ReceiverLogsCommand>(StringComparer.Ordinal)
        {
            ["logsRequestSnapshot"] = ReceiverLogsCommand.RequestSnapshot,
            ["logsReturnToBottom"] = ReceiverLogsCommand.ReturnToBottom,
            ["beginDrag"] = ReceiverLogsCommand.BeginDrag,
            ["minimize"] = ReceiverLogsCommand.Minimize,
            ["closeWindow"] = ReceiverLogsCommand.CloseWindow,
            ["showOverview"] = ReceiverLogsCommand.ShowOverview,
            ["showController"] = ReceiverLogsCommand.ShowController,
            ["showSettings"] = ReceiverLogsCommand.ShowSettings
        };

    public static IReadOnlyCollection<string> AllowedNames => Commands.Keys.ToArray();

    public static bool TryParse(
        string json,
        out ReceiverLogsCommand command,
        out string rejectionReason)
    {
        command = default;
        rejectionReason = string.Empty;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("command", out JsonElement commandElement) ||
                commandElement.ValueKind != JsonValueKind.String)
            {
                rejectionReason = "Bridge message must contain a string command.";
                return false;
            }

            string? commandName = commandElement.GetString();
            if (commandName == null || !Commands.TryGetValue(commandName, out command))
            {
                rejectionReason = $"Unknown bridge command '{commandName ?? "<null>"}'.";
                return false;
            }

            return true;
        }
        catch (JsonException exception)
        {
            rejectionReason = $"Invalid bridge JSON: {exception.Message}";
            return false;
        }
    }
}
