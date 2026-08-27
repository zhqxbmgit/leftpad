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

internal sealed record SettingsAdvancedFieldState(
    [property: JsonPropertyName("value")] decimal Value,
    [property: JsonPropertyName("min")] decimal Min,
    [property: JsonPropertyName("max")] decimal Max,
    [property: JsonPropertyName("step")] decimal Step,
    [property: JsonPropertyName("default")] decimal Default,
    [property: JsonPropertyName("unit")] string Unit);

internal sealed record SettingsMappingOption(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);

internal sealed record SettingsMappingSlotState(
    [property: JsonPropertyName("slotId")] int SlotId,
    [property: JsonPropertyName("actionKind")] string ActionKind,
    [property: JsonPropertyName("key")] string? Key,
    [property: JsonPropertyName("ctrl")] bool Ctrl,
    [property: JsonPropertyName("alt")] bool Alt,
    [property: JsonPropertyName("shift")] bool Shift,
    [property: JsonPropertyName("win")] bool Win,
    [property: JsonPropertyName("ds4Action")] string? Ds4Action,
    [property: JsonPropertyName("summary")] string Summary);

internal sealed record ReceiverSettingsStatusMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("tone")] string Tone)
{
    public const string MessageType = "receiverSettingsStatus";

    public static ReceiverSettingsStatusMessage Saved() =>
        new(MessageType, "设置已保存", "success");

    public static ReceiverSettingsStatusMessage SaveFailed(string error) =>
        new(MessageType, $"保存失败：{error}", "error");

    public string ToJson() => JsonSerializer.Serialize(this);
}

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
    [property: JsonPropertyName("advancedFields")] IReadOnlyDictionary<string, SettingsAdvancedFieldState> AdvancedFields,
    [property: JsonPropertyName("mappingProfileId")] string MappingProfileId,
    [property: JsonPropertyName("mappingSlotCount")] int MappingSlotCount,
    [property: JsonPropertyName("mappingSplitIndex")] int MappingSplitIndex,
    [property: JsonPropertyName("selectedMappingSlot")] int? SelectedMappingSlot,
    [property: JsonPropertyName("mappings")] IReadOnlyList<SettingsMappingSlotState> Mappings,
    [property: JsonPropertyName("actionKindOptions")] IReadOnlyList<SettingsMappingOption> ActionKindOptions,
    [property: JsonPropertyName("keyboardKeyOptions")] IReadOnlyList<SettingsMappingOption> KeyboardKeyOptions,
    [property: JsonPropertyName("ds4ActionOptions")] IReadOnlyList<SettingsMappingOption> Ds4ActionOptions,
    [property: JsonPropertyName("activeSection")] string ActiveSection,
    [property: JsonPropertyName("previewActive")] bool PreviewActive,
    [property: JsonPropertyName("dirty")] bool Dirty,
    [property: JsonPropertyName("enabled")] bool Enabled)
{
    public const string MessageType = "receiverSettingsBasicState";

    public string ToJson() => JsonSerializer.Serialize(this);
}

internal enum ReceiverSettingsSection
{
    Basic,
    Advanced,
    Mapping
}

internal static class SettingsMappingCatalogs
{
    public static IReadOnlyList<SettingsMappingOption> ActionKinds { get; } =
        Enum.GetValues<RadialActionKind>()
            .Select(kind => new SettingsMappingOption(KindId(kind), KindName(kind)))
            .ToArray();

    public static IReadOnlyList<SettingsMappingOption> KeyboardKeys { get; } =
        KeyboardKeyCatalog.MainKeys
            .Select(key => new SettingsMappingOption(key.ToString(), key.ToString()))
            .ToArray();

    public static IReadOnlyList<SettingsMappingOption> Ds4Actions { get; } =
        RadialDs4ActionCatalog.Actions
            .Select(action => new SettingsMappingOption(action.Id, action.DisplayName))
            .ToArray();

    public static string KindId(RadialActionKind kind) => kind switch
    {
        RadialActionKind.None => "none",
        RadialActionKind.KeyboardKey => "keyboardKey",
        RadialActionKind.KeyboardShortcut => "keyboardShortcut",
        RadialActionKind.Ds4Button => "ds4Button",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static bool TryKind(string? id, out RadialActionKind kind)
    {
        kind = id switch
        {
            "none" => RadialActionKind.None,
            "keyboardKey" => RadialActionKind.KeyboardKey,
            "keyboardShortcut" => RadialActionKind.KeyboardShortcut,
            "ds4Button" => RadialActionKind.Ds4Button,
            _ => (RadialActionKind)(-1)
        };
        return Enum.IsDefined(kind);
    }

    public static string Summary(RadialSlotMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        return mapping.Kind switch
        {
            RadialActionKind.None => string.Empty,
            RadialActionKind.KeyboardKey => mapping.Key?.ToString() ?? string.Empty,
            RadialActionKind.KeyboardShortcut => string.Join(" + ", ShortcutParts(mapping)),
            RadialActionKind.Ds4Button => RadialDs4ActionCatalog.TryGet(
                mapping.Ds4Button,
                out RadialDs4ActionMapping action)
                    ? action.DisplayName
                    : string.Empty,
            _ => string.Empty
        };
    }

    private static IEnumerable<string> ShortcutParts(RadialSlotMapping mapping)
    {
        if (mapping.Ctrl) yield return "Ctrl";
        if (mapping.Alt) yield return "Alt";
        if (mapping.Shift) yield return "Shift";
        if (mapping.Win) yield return "Win";
        if (mapping.Key is KeyboardKey key) yield return key.ToString();
    }

    private static string KindName(RadialActionKind kind) => kind switch
    {
        RadialActionKind.None => "无",
        RadialActionKind.KeyboardKey => "键盘单键",
        RadialActionKind.KeyboardShortcut => "键盘组合键",
        RadialActionKind.Ds4Button => "DS4 按键",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

internal static class SettingsMappingLayout
{
    public static int SplitIndex(int slotCount)
    {
        if (slotCount <= 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
        return (slotCount + 1) / 2;
    }
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

internal static class SettingsAdvancedFields
{
    public const string CanvasSize = "canvasSize";
    public const string CenterRadius = "centerRadius";
    public const string PetalInnerRadius = "petalInnerRadius";
    public const string PetalOuterRadius = "petalOuterRadius";
    public const string TextRadius = "textRadius";
    public const string PetalGapDegrees = "petalGapDegrees";
    public const string FontSize = "fontSize";
    public const string SelectionPollIntervalMs = "selectionPollIntervalMs";

    public static IReadOnlyList<string> Left { get; } =
    [
        CanvasSize,
        CenterRadius,
        PetalInnerRadius,
        PetalOuterRadius
    ];

    public static IReadOnlyList<string> Right { get; } =
    [
        TextRadius,
        PetalGapDegrees,
        FontSize,
        SelectionPollIntervalMs
    ];

    public static IReadOnlyList<string> All { get; } = [.. Left, .. Right];

    public static IReadOnlyDictionary<string, SettingsAdvancedFieldState> CreateStates(
        RadialMenuSettings settings)
    {
        RadialMenuSettings defaults = RadialMenuSettings.Default;
        return new Dictionary<string, SettingsAdvancedFieldState>(StringComparer.Ordinal)
        {
            [CanvasSize] = new(settings.BaseCanvasSize, 160, 800, 1, defaults.BaseCanvasSize, "px"),
            [CenterRadius] = new(settings.HubRadius, 1, 399, 1, defaults.HubRadius, "px"),
            [PetalInnerRadius] = new(settings.PetalInnerRadius, 1, 399, 1, defaults.PetalInnerRadius, "px"),
            [PetalOuterRadius] = new(settings.PetalOuterRadius, 2, 399, 1, defaults.PetalOuterRadius, "px"),
            [TextRadius] = new(settings.TextRadius, 1, 399, 1, defaults.TextRadius, "px"),
            [PetalGapDegrees] = new(
                (decimal)settings.PetalGapDegrees,
                (decimal)RadialMenuSettings.MinimumGapDegrees,
                (decimal)RadialMenuSettings.MaximumGapDegrees,
                0.5m,
                (decimal)defaults.PetalGapDegrees,
                "°"),
            [FontSize] = new((decimal)settings.FontSize, 6, 48, 0.5m, (decimal)defaults.FontSize, "px"),
            [SelectionPollIntervalMs] = new(
                settings.SelectionPollIntervalMs,
                RadialMenuSettings.MinimumSelectionPollIntervalMs,
                RadialMenuSettings.MaximumSelectionPollIntervalMs,
                1,
                defaults.SelectionPollIntervalMs,
                "ms")
        };
    }

    public static SettingsAdvancedFieldState Definition(string field) =>
        CreateStates(RadialMenuSettings.Default)[field];
}

internal enum ReceiverSettingsCommand
{
    BasicChange,
    AdvancedChange,
    MappingSelectSlot,
    MappingChange,
    Preview,
    HidePreview,
    ApplySave,
    RestoreDefault,
    RequestState,
    ShowOverview,
    ShowGamepad,
    ShowLogs,
    ShowBasic,
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
    int? IntegerValue = null,
    decimal? DecimalValue = null,
    string? ProfileId = null,
    int? SlotId = null,
    RadialActionKind? ActionKind = null,
    KeyboardKey? Key = null,
    bool Ctrl = false,
    bool Alt = false,
    bool Shift = false,
    bool Win = false,
    string? Ds4Action = null);

internal static class ReceiverSettingsCommandAllowList
{
    private static readonly IReadOnlyDictionary<string, ReceiverSettingsCommand> Commands =
        new Dictionary<string, ReceiverSettingsCommand>(StringComparer.Ordinal)
        {
            ["settingsBasicChange"] = ReceiverSettingsCommand.BasicChange,
            ["settingsAdvancedChange"] = ReceiverSettingsCommand.AdvancedChange,
            ["settingsMappingSelectSlot"] = ReceiverSettingsCommand.MappingSelectSlot,
            ["settingsMappingChange"] = ReceiverSettingsCommand.MappingChange,
            ["settingsPreview"] = ReceiverSettingsCommand.Preview,
            ["settingsHidePreview"] = ReceiverSettingsCommand.HidePreview,
            ["settingsApplySave"] = ReceiverSettingsCommand.ApplySave,
            ["settingsRestoreDefault"] = ReceiverSettingsCommand.RestoreDefault,
            ["settingsRequestState"] = ReceiverSettingsCommand.RequestState,
            ["showOverview"] = ReceiverSettingsCommand.ShowOverview,
            ["showGamepad"] = ReceiverSettingsCommand.ShowGamepad,
            ["showLogs"] = ReceiverSettingsCommand.ShowLogs,
            ["showSettingsBasic"] = ReceiverSettingsCommand.ShowBasic,
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

            if (command is ReceiverSettingsCommand.MappingSelectSlot or
                ReceiverSettingsCommand.MappingChange)
            {
                return TryParseMapping(root, commandName, command, out message, out rejectionReason);
            }

            if (command != ReceiverSettingsCommand.BasicChange &&
                command != ReceiverSettingsCommand.AdvancedChange)
            {
                message = new ReceiverSettingsMessage(command);
                return true;
            }

            if (!root.TryGetProperty("field", out JsonElement fieldElement) ||
                fieldElement.ValueKind != JsonValueKind.String)
            {
                return Reject($"{commandName} requires a string field.", out rejectionReason);
            }
            string? field = fieldElement.GetString();
            IReadOnlyList<string> allowedFields = command == ReceiverSettingsCommand.BasicChange
                ? SettingsBasicFields.All
                : SettingsAdvancedFields.All;
            if (field == null || !allowedFields.Contains(field, StringComparer.Ordinal))
                return Reject($"Unknown Settings {command} field '{field ?? "<null>"}'.", out rejectionReason);
            if (!root.TryGetProperty("value", out JsonElement valueElement))
                return Reject($"{commandName} requires a value.", out rejectionReason);

            if (command == ReceiverSettingsCommand.BasicChange &&
                field == SettingsBasicFields.VisualPackId)
            {
                if (valueElement.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(valueElement.GetString()))
                {
                    return Reject("visualPackId must be a non-empty string.", out rejectionReason);
                }
                message = new ReceiverSettingsMessage(command, field, valueElement.GetString());
                return true;
            }

            if (valueElement.ValueKind != JsonValueKind.Number)
                return Reject($"{field} must be a number.", out rejectionReason);

            if (command == ReceiverSettingsCommand.AdvancedChange)
            {
                if (!valueElement.TryGetDecimal(out decimal decimalValue))
                    return Reject($"{field} must be a finite decimal number.", out rejectionReason);
                SettingsAdvancedFieldState definition = SettingsAdvancedFields.Definition(field);
                if (decimalValue < definition.Min || decimalValue > definition.Max ||
                    (decimalValue - definition.Min) % definition.Step != 0)
                {
                    return Reject(
                        $"{field} must be between {definition.Min} and {definition.Max} in steps of {definition.Step}.",
                        out rejectionReason);
                }
                message = new ReceiverSettingsMessage(command, field, DecimalValue: decimalValue);
                return true;
            }

            if (!valueElement.TryGetInt32(out int integerValue))
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

    private static bool TryParseMapping(
        JsonElement root,
        string commandName,
        ReceiverSettingsCommand command,
        out ReceiverSettingsMessage message,
        out string rejectionReason)
    {
        message = new ReceiverSettingsMessage(default);
        rejectionReason = string.Empty;
        if (!TryRequiredString(root, "profileId", out string? profileId))
            return Reject($"{commandName} requires a non-empty profileId.", out rejectionReason);

        LayoutProfileRegistration profile;
        try
        {
            profile = LayoutProfileRegistry.GetRequired(profileId);
        }
        catch (InvalidDataException)
        {
            return Reject($"Unknown mapping profile '{profileId}'.", out rejectionReason);
        }

        if (!root.TryGetProperty("slotId", out JsonElement slotElement) ||
            !slotElement.TryGetInt32(out int slotId) ||
            slotId < 1 || slotId > profile.SlotCount)
        {
            return Reject(
                $"slotId must be between 1 and {profile.SlotCount} for profile {profile.ProfileId}.",
                out rejectionReason);
        }

        if (command == ReceiverSettingsCommand.MappingSelectSlot)
        {
            message = new ReceiverSettingsMessage(command, ProfileId: profile.ProfileId, SlotId: slotId);
            return true;
        }

        if (!TryRequiredString(root, "actionKind", out string? kindId) ||
            !SettingsMappingCatalogs.TryKind(kindId, out RadialActionKind kind))
        {
            return Reject($"Unknown mapping action kind '{kindId ?? "<null>"}'.", out rejectionReason);
        }

        KeyboardKey? key = null;
        if (root.TryGetProperty("key", out JsonElement keyElement) &&
            keyElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (keyElement.ValueKind != JsonValueKind.String ||
                !Enum.TryParse(keyElement.GetString(), ignoreCase: false, out KeyboardKey parsedKey) ||
                !KeyboardKeyCatalog.IsMainKey(parsedKey))
            {
                return Reject($"Unknown keyboard key '{keyElement.ToString()}'.", out rejectionReason);
            }
            key = parsedKey;
        }

        bool ctrl = OptionalBoolean(root, "ctrl", out bool ctrlValid);
        bool alt = OptionalBoolean(root, "alt", out bool altValid);
        bool shift = OptionalBoolean(root, "shift", out bool shiftValid);
        bool win = OptionalBoolean(root, "win", out bool winValid);
        if (!ctrlValid || !altValid || !shiftValid || !winValid)
            return Reject("Mapping modifiers must be booleans.", out rejectionReason);

        string? ds4Action = null;
        if (root.TryGetProperty("ds4Action", out JsonElement ds4Element) &&
            ds4Element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (ds4Element.ValueKind != JsonValueKind.String ||
                !RadialDs4ActionCatalog.TryGet(ds4Element.GetString(), out RadialDs4ActionMapping action))
            {
                return Reject($"Unknown DS4 action '{ds4Element.ToString()}'.", out rejectionReason);
            }
            ds4Action = action.Id;
        }

        var mapping = new RadialSlotMapping
        {
            Kind = kind,
            Key = key,
            Ctrl = ctrl,
            Alt = alt,
            Shift = shift,
            Win = win,
            Ds4Button = ds4Action
        };
        if (!mapping.TryValidate(out string mappingError))
            return Reject(mappingError, out rejectionReason);

        message = new ReceiverSettingsMessage(
            command,
            ProfileId: profile.ProfileId,
            SlotId: slotId,
            ActionKind: kind,
            Key: key,
            Ctrl: ctrl,
            Alt: alt,
            Shift: shift,
            Win: win,
            Ds4Action: ds4Action);
        return true;
    }

    private static bool TryRequiredString(JsonElement root, string name, out string? value)
    {
        if (root.TryGetProperty(name, out JsonElement element) &&
            element.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(element.GetString()))
        {
            value = element.GetString();
            return true;
        }
        value = null;
        return false;
    }

    private static bool OptionalBoolean(JsonElement root, string name, out bool valid)
    {
        if (!root.TryGetProperty(name, out JsonElement element))
        {
            valid = true;
            return false;
        }
        valid = element.ValueKind is JsonValueKind.True or JsonValueKind.False;
        return valid && element.GetBoolean();
    }
}
