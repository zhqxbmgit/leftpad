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
public sealed class RadialSlotMappings : IReadOnlyList<RadialSlotMapping>, IEquatable<RadialSlotMappings>
{
    private readonly RadialSlotMapping[] _mappings;

    public RadialSlotMappings(params RadialSlotMapping?[] mappings)
    {
        _mappings = (mappings ?? Array.Empty<RadialSlotMapping?>())
            .Select(mapping => mapping?.Sanitize() ?? RadialSlotMapping.None)
            .ToArray();
    }

    public static RadialSlotMappings Empty { get; } = new();
    public static RadialSlotMappings Default => Create(LayoutProfileRegistry.Radial6SlotCount);

    public int Count => _mappings.Length;

    public RadialSlotMapping this[int index] => _mappings[index];

    // Compatibility helper for callers constructing the legacy radial-6 mapping list.
    public static RadialSlotMappings Create(IEnumerable<RadialSlotMapping?>? mappings)
        => Create(LayoutProfileRegistry.Radial6SlotCount, mappings);

    public static RadialSlotMappings Create(
        int slotCount,
        IEnumerable<RadialSlotMapping?>? mappings = null)
    {
        if (slotCount < 0) throw new ArgumentOutOfRangeException(nameof(slotCount));

        RadialSlotMapping[] normalized = Enumerable.Repeat(RadialSlotMapping.None, slotCount).ToArray();
        if (mappings != null)
        {
            int index = 0;
            foreach (RadialSlotMapping? mapping in mappings)
            {
                if (index >= slotCount) break;
                normalized[index++] = mapping?.Sanitize() ?? RadialSlotMapping.None;
            }
        }

        return new RadialSlotMappings(normalized);
    }

    public static RadialSlotMappings Sanitize(RadialSlotMappings? mappings) =>
        mappings == null ? Default : Create(mappings.Count, mappings);

    public RadialSlotMappings Normalize(int slotCount) => Create(slotCount, this);

    public RadialSlotMappings WithSlot(int slot, RadialSlotMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (slot < 1 || slot > Count) throw new ArgumentOutOfRangeException(nameof(slot));
        RadialSlotMapping[] values = this.ToArray();
        values[slot - 1] = mapping.Sanitize();
        return new RadialSlotMappings(values);
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
        => ((IEnumerable<RadialSlotMapping>)_mappings).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(RadialSlotMappings? other) =>
        other != null && _mappings.SequenceEqual(other._mappings);

    public override bool Equals(object? obj) => Equals(obj as RadialSlotMappings);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (RadialSlotMapping mapping in _mappings) hash.Add(mapping);
        return hash.ToHashCode();
    }
}

internal sealed class RadialSlotMappingsJsonConverter : JsonConverter<RadialSlotMappings>
{
    public override RadialSlotMappings Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return RadialSlotMappings.Empty;

        var mappings = new List<RadialSlotMapping?>();
        foreach (JsonElement element in document.RootElement.EnumerateArray())
            mappings.Add(ReadMapping(element));

        return new RadialSlotMappings(mappings.ToArray());
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

[JsonConverter(typeof(RadialMappingsByProfileJsonConverter))]
public sealed class RadialMappingsByProfile :
    IReadOnlyDictionary<string, RadialSlotMappings>,
    IEquatable<RadialMappingsByProfile>
{
    private readonly IReadOnlyDictionary<string, RadialSlotMappings> _profiles;

    private RadialMappingsByProfile(IEnumerable<KeyValuePair<string, RadialSlotMappings>> profiles)
    {
        var values = new Dictionary<string, RadialSlotMappings>(StringComparer.Ordinal);
        foreach ((string profileId, RadialSlotMappings mappings) in profiles)
        {
            if (string.IsNullOrWhiteSpace(profileId) || mappings == null) continue;
            values[profileId] = RadialSlotMappings.Sanitize(mappings);
        }

        _profiles = values;
    }

    public static RadialMappingsByProfile Empty { get; } = new(
        Array.Empty<KeyValuePair<string, RadialSlotMappings>>());

    public int Count => _profiles.Count;
    public IEnumerable<string> Keys => _profiles.Keys;
    public IEnumerable<RadialSlotMappings> Values => _profiles.Values;
    public RadialSlotMappings this[string profileId] => _profiles[profileId];

    public bool ContainsKey(string profileId) => _profiles.ContainsKey(profileId);

    public bool TryGetValue(string profileId, out RadialSlotMappings value) =>
        _profiles.TryGetValue(profileId, out value!);

    public RadialMappingsByProfile Set(string profileId, RadialSlotMappings mappings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(mappings);

        var values = new Dictionary<string, RadialSlotMappings>(_profiles, StringComparer.Ordinal)
        {
            [profileId] = RadialSlotMappings.Sanitize(mappings)
        };
        return new RadialMappingsByProfile(values);
    }

    public IEnumerator<KeyValuePair<string, RadialSlotMappings>> GetEnumerator() =>
        _profiles.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(RadialMappingsByProfile? other)
    {
        if (other == null || Count != other.Count) return false;
        return _profiles.All(pair =>
            other.TryGetValue(pair.Key, out RadialSlotMappings mappings) &&
            pair.Value.Equals(mappings));
    }

    public override bool Equals(object? obj) => Equals(obj as RadialMappingsByProfile);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach ((string profileId, RadialSlotMappings mappings) in
                 _profiles.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(profileId, StringComparer.Ordinal);
            hash.Add(mappings);
        }
        return hash.ToHashCode();
    }

    internal static RadialMappingsByProfile Create(
        IEnumerable<KeyValuePair<string, RadialSlotMappings>> profiles) => new(profiles);
}

internal sealed class RadialMappingsByProfileJsonConverter : JsonConverter<RadialMappingsByProfile>
{
    public override RadialMappingsByProfile Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            using JsonDocument _ = JsonDocument.ParseValue(ref reader);
            return RadialMappingsByProfile.Empty;
        }

        var profiles = new Dictionary<string, RadialSlotMappings>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected a layout profile id.");

            string profileId = reader.GetString() ?? string.Empty;
            if (!reader.Read()) throw new JsonException("Expected a profile mapping array.");
            RadialSlotMappings mappings =
                JsonSerializer.Deserialize<RadialSlotMappings>(ref reader, options) ??
                RadialSlotMappings.Empty;
            if (!string.IsNullOrWhiteSpace(profileId)) profiles[profileId] = mappings;
        }

        return RadialMappingsByProfile.Create(profiles);
    }

    public override void Write(
        Utf8JsonWriter writer,
        RadialMappingsByProfile value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach ((string profileId, RadialSlotMappings mappings) in
                 value.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(profileId);
            JsonSerializer.Serialize(writer, mappings, options);
        }
        writer.WriteEndObject();
    }
}
