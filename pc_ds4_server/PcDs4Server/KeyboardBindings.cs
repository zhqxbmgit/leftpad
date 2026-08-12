using System.Text.Json;

namespace PcDs4Server;

public sealed class KeyboardBindings
{
    public KeyboardKey Cross { get; set; } = KeyboardKey.None;
    public KeyboardKey Circle { get; set; } = KeyboardKey.None;
    public KeyboardKey Square { get; set; } = KeyboardKey.None;
    public KeyboardKey Triangle { get; set; } = KeyboardKey.None;
    public KeyboardKey L1 { get; set; } = KeyboardKey.None;
    public KeyboardKey R1 { get; set; } = KeyboardKey.None;
    public KeyboardKey L2 { get; set; } = KeyboardKey.None;
    public KeyboardKey R2 { get; set; } = KeyboardKey.None;
    public KeyboardKey L3 { get; set; } = KeyboardKey.None;
    public KeyboardKey R3 { get; set; } = KeyboardKey.None;

    public KeyboardKey Get(string protocolAction) => protocolAction.ToLowerInvariant() switch
    {
        "cross" => Cross,
        "circle" => Circle,
        "square" => Square,
        "triangle" => Triangle,
        "l1" => L1,
        "r1" => R1,
        "l2" => L2,
        "r2" => R2,
        "l3" => L3,
        "r3" => R3,
        _ => throw new ArgumentOutOfRangeException(nameof(protocolAction))
    };

    public void Set(string protocolAction, KeyboardKey key)
    {
        switch (protocolAction.ToLowerInvariant())
        {
            case "cross": Cross = key; break;
            case "circle": Circle = key; break;
            case "square": Square = key; break;
            case "triangle": Triangle = key; break;
            case "l1": L1 = key; break;
            case "r1": R1 = key; break;
            case "l2": L2 = key; break;
            case "r2": R2 = key; break;
            case "l3": L3 = key; break;
            case "r3": R3 = key; break;
            default: throw new ArgumentOutOfRangeException(nameof(protocolAction));
        }
    }

    public KeyboardBindings Clone() => new()
    {
        Cross = Cross, Circle = Circle, Square = Square, Triangle = Triangle,
        L1 = L1, R1 = R1, L2 = L2, R2 = R2, L3 = L3, R3 = R3
    };

    public static IReadOnlyList<string> ProtocolActions { get; } =
        new[] { "cross", "circle", "square", "triangle", "l1", "r1", "l2", "r2", "l3", "r3" };
}

public interface IKeyboardBindingStore
{
    KeyboardBindings Load();
    void Save(KeyboardBindings bindings);
}

public sealed class JsonKeyboardBindingStore : IKeyboardBindingStore
{
    private readonly string _path;

    public JsonKeyboardBindingStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LeftPad", "keyboard-bindings.json");
    }

    public KeyboardBindings Load()
    {
        var result = new KeyboardBindings();
        try
        {
            if (!File.Exists(_path)) return result;
            Dictionary<string, string>? values = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(_path));
            if (values == null) return result;

            foreach (string action in KeyboardBindings.ProtocolActions)
            {
                if (values.TryGetValue(action, out string? value) &&
                    Enum.TryParse(value, ignoreCase: true, out KeyboardKey key) &&
                    Enum.IsDefined(key))
                {
                    result.Set(action, key);
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }

        return result;
    }

    public void Save(KeyboardBindings bindings)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var values = KeyboardBindings.ProtocolActions.ToDictionary(
            action => action,
            action => bindings.Get(action).ToString(),
            StringComparer.OrdinalIgnoreCase);
        File.WriteAllText(_path, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
    }
}
