using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcDs4Server;

internal sealed record ReceiverOverviewState(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("vigemStatus")] string VigemStatus,
    [property: JsonPropertyName("virtualDs4Status")] string VirtualDs4Status,
    [property: JsonPropertyName("phoneStatus")] string PhoneStatus,
    [property: JsonPropertyName("port")] int Port,
    [property: JsonPropertyName("outputMode")] string OutputMode,
    [property: JsonPropertyName("startStopLabel")] string StartStopLabel,
    [property: JsonPropertyName("mappings")] IReadOnlyDictionary<string, string> Mappings)
{
    public const string MessageType = "receiverOverviewState";

    public string ToJson() => JsonSerializer.Serialize(this);
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
    ShowLogs
}

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
            ["showLogs"] = ReceiverOverviewCommand.ShowLogs
        };

    public static IReadOnlyCollection<string> AllowedNames => Commands.Keys.ToArray();

    public static bool TryParse(
        string json,
        out ReceiverOverviewCommand command,
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
