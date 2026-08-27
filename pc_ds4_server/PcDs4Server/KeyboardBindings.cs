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

    KeyboardBindingLoadResult LoadWithStatus()
    {
        try
        {
            return new KeyboardBindingLoadResult(
                Load(),
                KeyboardBindingLoadStatus.Loaded,
                KeyboardBindingPathCategories.InjectedStore);
        }
        catch (Exception ex)
        {
            return KeyboardBindingLoadResult.FromException(
                ex,
                KeyboardBindingPathCategories.InjectedStore);
        }
    }

    KeyboardBindingSaveResult TrySave(KeyboardBindings bindings)
    {
        try
        {
            Save(bindings);
            return KeyboardBindingSaveResult.Succeeded(
                KeyboardBindingPathCategories.InjectedStore);
        }
        catch (Exception ex)
        {
            return KeyboardBindingSaveResult.Failed(
                ex,
                KeyboardBindingPathCategories.InjectedStore);
        }
    }
}

public enum KeyboardBindingLoadStatus
{
    Missing,
    Loaded,
    InvalidJson,
    IoError,
    PermissionError,
    UnknownError
}

public static class KeyboardBindingPathCategories
{
    public const string UserConfiguration = "user-config/keyboard-bindings";
    public const string InjectedStore = "injected/keyboard-bindings";
}

public sealed record KeyboardBindingLoadResult(
    KeyboardBindings Bindings,
    KeyboardBindingLoadStatus Status,
    string PathCategory,
    string? ErrorType = null,
    string? Error = null)
{
    internal static KeyboardBindingLoadResult FromException(
        Exception exception,
        string pathCategory)
    {
        KeyboardBindingLoadStatus status = exception switch
        {
            UnauthorizedAccessException => KeyboardBindingLoadStatus.PermissionError,
            IOException => KeyboardBindingLoadStatus.IoError,
            JsonException => KeyboardBindingLoadStatus.InvalidJson,
            _ => KeyboardBindingLoadStatus.UnknownError
        };
        return new KeyboardBindingLoadResult(
            new KeyboardBindings(),
            status,
            pathCategory,
            exception.GetType().Name,
            exception.Message);
    }
}

public sealed record KeyboardBindingSaveResult(
    bool Success,
    string PathCategory,
    string? ErrorType = null,
    string? Error = null)
{
    public static KeyboardBindingSaveResult Succeeded(string pathCategory) =>
        new(true, pathCategory);

    public static KeyboardBindingSaveResult Failed(Exception exception, string pathCategory) =>
        new(false, pathCategory, exception.GetType().Name, exception.Message);

    internal static KeyboardBindingSaveResult Failed(
        string error,
        string? errorType,
        string pathCategory) =>
        new(false, pathCategory, errorType ?? nameof(IOException), error);
}

public sealed class KeyboardBindingPersistenceException : IOException
{
    public KeyboardBindingPersistenceException(KeyboardBindingSaveResult result)
        : base(result.Error ?? "Keyboard bindings could not be saved.")
    {
        Result = result;
    }

    public KeyboardBindingSaveResult Result { get; }
}

public sealed class JsonKeyboardBindingStore : IKeyboardBindingStore
{
    private readonly string _path;
    private readonly IAtomicFileOperations _files;

    public JsonKeyboardBindingStore(string? path = null)
        : this(
            path ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LeftPad",
                "keyboard-bindings.json"),
            SystemAtomicFileOperations.Instance)
    {
    }

    internal JsonKeyboardBindingStore(string path, IAtomicFileOperations files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _files = files ?? throw new ArgumentNullException(nameof(files));
    }

    public KeyboardBindings Load() => LoadWithStatus().Bindings;

    public KeyboardBindingLoadResult LoadWithStatus()
    {
        var result = new KeyboardBindings();
        try
        {
            if (!_files.FileExists(_path))
            {
                return new KeyboardBindingLoadResult(
                    result,
                    KeyboardBindingLoadStatus.Missing,
                    KeyboardBindingPathCategories.UserConfiguration);
            }
            Dictionary<string, string>? values = JsonSerializer.Deserialize<Dictionary<string, string>>(
                _files.ReadAllText(_path));
            if (values == null)
            {
                return new KeyboardBindingLoadResult(
                    result,
                    KeyboardBindingLoadStatus.InvalidJson,
                    KeyboardBindingPathCategories.UserConfiguration,
                    nameof(JsonException),
                    "Keyboard binding JSON did not contain an object.");
            }

            foreach (string action in KeyboardBindings.ProtocolActions)
            {
                if (values.TryGetValue(action, out string? value) &&
                    Enum.TryParse(value, ignoreCase: true, out KeyboardKey key) &&
                    Enum.IsDefined(key))
                {
                    result.Set(action, key);
                }
            }

            return new KeyboardBindingLoadResult(
                result,
                KeyboardBindingLoadStatus.Loaded,
                KeyboardBindingPathCategories.UserConfiguration);
        }
        catch (Exception ex)
        {
            return KeyboardBindingLoadResult.FromException(
                ex,
                KeyboardBindingPathCategories.UserConfiguration);
        }
    }

    public void Save(KeyboardBindings bindings)
    {
        KeyboardBindingSaveResult result = TrySave(bindings);
        if (!result.Success) throw new KeyboardBindingPersistenceException(result);
    }

    public KeyboardBindingSaveResult TrySave(KeyboardBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        try
        {
            var values = KeyboardBindings.ProtocolActions.ToDictionary(
                action => action,
                action => bindings.Get(action).ToString(),
                StringComparer.OrdinalIgnoreCase);
            byte[] content = JsonSerializer.SerializeToUtf8Bytes(
                values,
                new JsonSerializerOptions { WriteIndented = true });
            if (!AtomicFilePersistence.TryWrite(
                    _path,
                    content,
                    _files,
                    retainOriginalForRollback: false,
                    out AtomicFileWriteTransaction? transaction,
                    out string error,
                    out string? errorType))
            {
                return KeyboardBindingSaveResult.Failed(
                    error,
                    errorType,
                    KeyboardBindingPathCategories.UserConfiguration);
            }

            transaction!.Commit();
            return KeyboardBindingSaveResult.Succeeded(
                KeyboardBindingPathCategories.UserConfiguration);
        }
        catch (Exception ex)
        {
            return KeyboardBindingSaveResult.Failed(
                ex,
                KeyboardBindingPathCategories.UserConfiguration);
        }
    }
}
