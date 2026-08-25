using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcDs4Server;

internal sealed record ReceiverControllerState(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("moveState")] string MoveState,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("cursorSampling")] string CursorSampling,
    [property: JsonPropertyName("directionCaptured")] string DirectionCaptured,
    [property: JsonPropertyName("moveLocked")] string MoveLocked,
    [property: JsonPropertyName("currentDirection")] string CurrentDirection,
    [property: JsonPropertyName("lockedDirection")] string LockedDirection,
    [property: JsonPropertyName("joystick")] string Joystick,
    [property: JsonPropertyName("ds4")] string Ds4,
    [property: JsonPropertyName("center")] string Center,
    [property: JsonPropertyName("cursor")] string Cursor,
    [property: JsonPropertyName("trianglePressed")] bool TrianglePressed,
    [property: JsonPropertyName("squarePressed")] bool SquarePressed,
    [property: JsonPropertyName("crossPressed")] bool CrossPressed,
    [property: JsonPropertyName("circlePressed")] bool CirclePressed,
    [property: JsonPropertyName("connectionState")] string ConnectionState)
{
    public const string MessageType = "receiverControllerState";

    public static ReceiverControllerState Create(
        VirtualJoystickSnapshot? snapshot,
        IReadOnlyDictionary<string, bool> buttonStates,
        string connectionState)
    {
        ArgumentNullException.ThrowIfNull(buttonStates);

        return new ReceiverControllerState(
            MessageType,
            snapshot?.MoveButtonPressed == true ? "按住" : "已释放",
            snapshot?.JoystickActive == true
                ? "实时"
                : snapshot?.MovementLocked == true ? "已锁定" : "已停止",
            snapshot is { MoveButtonPressed: true, JoystickActive: true }
                ? "已启用"
                : "未启用",
            snapshot?.DirectionCapturedDuringHold == true ? "是" : "否",
            snapshot?.MovementLocked == true ? "是" : "否",
            FormatPair(snapshot?.CurrentDirectionX ?? 0, snapshot?.CurrentDirectionY ?? 0),
            FormatPair(snapshot?.LockedDirectionX ?? 0, snapshot?.LockedDirectionY ?? 0),
            FormatPair(snapshot?.StickX ?? 0, snapshot?.StickY ?? 0),
            $"{snapshot?.Ds4X ?? VirtualJoystickController.NeutralAxis} / " +
                $"{snapshot?.Ds4Y ?? VirtualJoystickController.NeutralAxis}",
            FormatPoint(snapshot?.Center),
            FormatPoint(snapshot?.CurrentCursor),
            IsPressed(buttonStates, "triangle"),
            IsPressed(buttonStates, "square"),
            IsPressed(buttonStates, "cross"),
            IsPressed(buttonStates, "circle"),
            string.IsNullOrWhiteSpace(connectionState) ? "等待连接" : connectionState);
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    private static string FormatPair(double x, double y) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{x:F3} / {y:F3}");

    private static string FormatPoint(ScreenPoint? point) => point is ScreenPoint value
        ? $"{value.X} / {value.Y}"
        : "- / -";

    private static bool IsPressed(
        IReadOnlyDictionary<string, bool> states,
        string name) => states.TryGetValue(name, out bool pressed) && pressed;
}

internal enum ReceiverControllerCommand
{
    RequestState,
    BeginDrag,
    Minimize,
    CloseWindow,
    ShowOverview,
    ShowSettings,
    ShowLogs
}

internal static class ReceiverControllerCommandAllowList
{
    private static readonly IReadOnlyDictionary<string, ReceiverControllerCommand> Commands =
        new Dictionary<string, ReceiverControllerCommand>(StringComparer.Ordinal)
        {
            ["controllerRequestState"] = ReceiverControllerCommand.RequestState,
            ["beginDrag"] = ReceiverControllerCommand.BeginDrag,
            ["minimize"] = ReceiverControllerCommand.Minimize,
            ["closeWindow"] = ReceiverControllerCommand.CloseWindow,
            ["showOverview"] = ReceiverControllerCommand.ShowOverview,
            ["showSettings"] = ReceiverControllerCommand.ShowSettings,
            ["showLogs"] = ReceiverControllerCommand.ShowLogs
        };

    public static IReadOnlyCollection<string> AllowedNames => Commands.Keys.ToArray();

    public static bool TryParse(
        string json,
        out ReceiverControllerCommand command,
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
