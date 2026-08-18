using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcDs4Server;

public enum RadialActionKind
{
    None,
    KeyboardKey,
    KeyboardShortcut,
    Ds4Button
}

public sealed record RadialSlotMapping
{
    public static RadialSlotMapping None { get; } = new();

    public RadialActionKind Kind { get; init; }
    public KeyboardKey? Key { get; init; }
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    public bool Shift { get; init; }
    public bool Win { get; init; }
    public string? Ds4Button { get; init; }

    public bool TryValidate(out string error)
    {
        bool hasModifier = Ctrl || Alt || Shift || Win;
        bool hasMainKey = Key is KeyboardKey key && KeyboardKeyCatalog.IsMainKey(key);
        bool hasDs4Button = !string.IsNullOrWhiteSpace(Ds4Button);

        switch (Kind)
        {
            case RadialActionKind.None:
                if (Key != null || hasModifier || hasDs4Button)
                    return Invalid("无动作不能包含按键配置。", out error);
                break;
            case RadialActionKind.KeyboardKey:
                if (!hasMainKey)
                    return Invalid("键盘单键需要一个有效主键。", out error);
                if (hasModifier || hasDs4Button)
                    return Invalid("键盘单键不能包含修饰键或 DS4 按键。", out error);
                break;
            case RadialActionKind.KeyboardShortcut:
                if (!hasMainKey)
                    return Invalid("键盘组合键需要一个有效主键。", out error);
                if (!hasModifier)
                    return Invalid("组合键至少需要一个 Ctrl、Alt、Shift 或 Win 修饰键。", out error);
                if (hasDs4Button)
                    return Invalid("键盘组合键不能包含 DS4 按键。", out error);
                break;
            case RadialActionKind.Ds4Button:
                if (!RadialDs4ActionCatalog.TryGet(Ds4Button, out _))
                    return Invalid("请选择一个受支持的 DS4 按键。", out error);
                if (Key != null || hasModifier)
                    return Invalid("DS4 按键不能包含键盘配置。", out error);
                break;
            default:
                return Invalid("未知的动作类型。", out error);
        }

        error = string.Empty;
        return true;
    }

    public RadialSlotMapping Sanitize()
    {
        if (!TryValidate(out _)) return None;
        if (Kind == RadialActionKind.Ds4Button &&
            RadialDs4ActionCatalog.TryGet(Ds4Button, out RadialDs4ActionMapping mapping))
        {
            return this with { Ds4Button = mapping.Id };
        }

        return this;
    }

    private static bool Invalid(string message, out string error)
    {
        error = message;
        return false;
    }
}

[JsonConverter(typeof(RadialSlotMappingsJsonConverter))]
public sealed record RadialSlotMappings(
    RadialSlotMapping Slot1,
    RadialSlotMapping Slot2,
    RadialSlotMapping Slot3,
    RadialSlotMapping Slot4,
    RadialSlotMapping Slot5,
    RadialSlotMapping Slot6) : IReadOnlyList<RadialSlotMapping>
{
    public const int SlotCount = 6;

    public static RadialSlotMappings Default => new(
        RadialSlotMapping.None,
        RadialSlotMapping.None,
        RadialSlotMapping.None,
        RadialSlotMapping.None,
        RadialSlotMapping.None,
        RadialSlotMapping.None);

    public int Count => SlotCount;

    public RadialSlotMapping this[int index] => index switch
    {
        0 => Slot1,
        1 => Slot2,
        2 => Slot3,
        3 => Slot4,
        4 => Slot5,
        5 => Slot6,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    public static RadialSlotMappings Create(IEnumerable<RadialSlotMapping?>? mappings)
    {
        RadialSlotMapping[] normalized = Enumerable.Repeat(RadialSlotMapping.None, SlotCount).ToArray();
        if (mappings != null)
        {
            int index = 0;
            foreach (RadialSlotMapping? mapping in mappings)
            {
                if (index >= SlotCount) break;
                normalized[index++] = mapping?.Sanitize() ?? RadialSlotMapping.None;
            }
        }

        return new RadialSlotMappings(
            normalized[0], normalized[1], normalized[2],
            normalized[3], normalized[4], normalized[5]);
    }

    public static RadialSlotMappings Sanitize(RadialSlotMappings? mappings) =>
        mappings == null ? Default : Create(mappings);

    public RadialSlotMappings WithSlot(int slot, RadialSlotMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (slot is < 1 or > SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
        RadialSlotMapping[] values = this.ToArray();
        values[slot - 1] = mapping;
        return Create(values);
    }

    public bool TryValidate(out string error)
    {
        for (int index = 0; index < Count; index++)
        {
            if (!this[index].TryValidate(out string mappingError))
            {
                error = $"Slot {index + 1}：{mappingError}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public IEnumerator<RadialSlotMapping> GetEnumerator()
    {
        for (int index = 0; index < Count; index++) yield return this[index];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class RadialSlotMappingsJsonConverter : JsonConverter<RadialSlotMappings>
{
    public override RadialSlotMappings Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return RadialSlotMappings.Default;

        var mappings = new List<RadialSlotMapping?>();
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            if (mappings.Count >= RadialSlotMappings.SlotCount) break;
            mappings.Add(ReadMapping(element));
        }

        return RadialSlotMappings.Create(mappings);
    }

    public override void Write(
        Utf8JsonWriter writer,
        RadialSlotMappings value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (RadialSlotMapping mapping in value)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", KindId(mapping.Kind));
            if (mapping.Kind is RadialActionKind.KeyboardKey or RadialActionKind.KeyboardShortcut)
                writer.WriteString("key", mapping.Key!.Value.ToString());
            if (mapping.Kind == RadialActionKind.KeyboardShortcut)
            {
                writer.WriteBoolean("ctrl", mapping.Ctrl);
                writer.WriteBoolean("alt", mapping.Alt);
                writer.WriteBoolean("shift", mapping.Shift);
                writer.WriteBoolean("win", mapping.Win);
            }
            if (mapping.Kind == RadialActionKind.Ds4Button)
                writer.WriteString("ds4Button", mapping.Ds4Button);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static RadialSlotMapping ReadMapping(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !TryString(element, "kind", out string? kindId) ||
            !TryKind(kindId, out RadialActionKind kind))
        {
            return RadialSlotMapping.None;
        }

        KeyboardKey? key = null;
        if (TryString(element, "key", out string? keyId) &&
            Enum.TryParse(keyId, ignoreCase: true, out KeyboardKey parsedKey) &&
            Enum.IsDefined(parsedKey))
        {
            key = parsedKey;
        }

        var mapping = new RadialSlotMapping
        {
            Kind = kind,
            Key = key,
            Ctrl = TryBoolean(element, "ctrl"),
            Alt = TryBoolean(element, "alt"),
            Shift = TryBoolean(element, "shift"),
            Win = TryBoolean(element, "win"),
            Ds4Button = TryString(element, "ds4Button", out string? button) ? button : null
        };
        return mapping.Sanitize();
    }

    private static bool TryKind(string? id, out RadialActionKind kind)
    {
        kind = id?.ToLowerInvariant() switch
        {
            "none" => RadialActionKind.None,
            "keyboardkey" => RadialActionKind.KeyboardKey,
            "keyboardshortcut" => RadialActionKind.KeyboardShortcut,
            "ds4button" => RadialActionKind.Ds4Button,
            _ => (RadialActionKind)(-1)
        };
        return Enum.IsDefined(kind);
    }

    private static string KindId(RadialActionKind kind) => kind switch
    {
        RadialActionKind.None => "none",
        RadialActionKind.KeyboardKey => "keyboardKey",
        RadialActionKind.KeyboardShortcut => "keyboardShortcut",
        RadialActionKind.Ds4Button => "ds4Button",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static bool TryString(JsonElement element, string name, out string? value)
    {
        if (element.TryGetProperty(name, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement property) &&
        property.ValueKind == JsonValueKind.True;
}
