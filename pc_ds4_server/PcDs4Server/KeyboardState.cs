namespace PcDs4Server;

public sealed class KeyboardKeyState
{
    private const string MovePrefix = "move:";
    private readonly object _sync = new();
    private readonly IKeyboardOutput _output;
    private readonly Dictionary<string, KeyboardKey> _sourceKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<KeyboardKey, HashSet<string>> _keyOwners = new();
    private readonly HashSet<KeyboardKey> _currentPressedKeys = new();
    private readonly List<KeyboardKey> _pressedKeyOrder = new();

    public KeyboardKeyState(IKeyboardOutput output) => _output = output;

    public IReadOnlyCollection<KeyboardKey> CurrentPressedKeys
    {
        get { lock (_sync) return _currentPressedKeys.ToArray(); }
    }

    public void Press(string source, KeyboardKey key)
    {
        if (key == KeyboardKey.None) return;

        lock (_sync)
        {
            if (_sourceKeys.ContainsKey(source)) return;
            _sourceKeys[source] = key;
            if (!_keyOwners.TryGetValue(key, out HashSet<string>? owners))
            {
                owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _keyOwners[key] = owners;
            }
            owners.Add(source);
            if (_currentPressedKeys.Add(key))
            {
                _pressedKeyOrder.Add(key);
                SendSafely(key, true);
            }
        }
    }

    public void Release(string source)
    {
        lock (_sync)
        {
            if (!_sourceKeys.Remove(source, out KeyboardKey key)) return;
            if (!_keyOwners.TryGetValue(key, out HashSet<string>? owners)) return;
            owners.Remove(source);
            if (owners.Count != 0) return;
            _keyOwners.Remove(key);
            if (_currentPressedKeys.Contains(key))
            {
                SendSafely(key, false);
                _currentPressedKeys.Remove(key);
                _pressedKeyOrder.Remove(key);
            }
        }
    }

    public void SetMovementKeys(IReadOnlySet<KeyboardKey> desiredKeys)
    {
        lock (_sync)
        {
            var currentMovement = _sourceKeys
                .Where(pair => pair.Key.StartsWith(MovePrefix, StringComparison.Ordinal))
                .ToDictionary(pair => pair.Value, pair => pair.Key);

            foreach ((KeyboardKey key, string source) in currentMovement)
            {
                if (!desiredKeys.Contains(key)) Release(source);
            }
            foreach (KeyboardKey key in desiredKeys)
            {
                if (!currentMovement.ContainsKey(key)) Press(MovePrefix + key, key);
            }
        }
    }

    public void ReleaseMovement() => SetMovementKeys(new HashSet<KeyboardKey>());

    public void ReleaseAll()
    {
        lock (_sync) ReleaseAllCore();
    }

    private void SendSafely(KeyboardKey key, bool pressed)
    {
        try
        {
            _output.SetKeyState(key, pressed);
        }
        catch (Exception ex)
        {
            ReleaseAllCore();
            throw new KeyboardOutputException("Keyboard output failed; all tracked keys were released.", ex);
        }
    }

    private void ReleaseAllCore()
    {
        // Includes an attempted DOWN whose output threw; unwind in reverse acquisition order.
        KeyboardKey[] keys = _pressedKeyOrder.AsEnumerable().Reverse().ToArray();
        _sourceKeys.Clear();
        _keyOwners.Clear();
        _currentPressedKeys.Clear();
        _pressedKeyOrder.Clear();
        foreach (KeyboardKey key in keys)
        {
            try { _output.SetKeyState(key, false); }
            catch { /* Best-effort cleanup continues for every tracked key. */ }
        }
    }
}

public sealed class KeyboardMoveOutput : ILeftStickOutput
{
    private static readonly IReadOnlySet<KeyboardKey> NoKeys = new HashSet<KeyboardKey>();
    private readonly KeyboardKeyState _state;

    public KeyboardMoveOutput(KeyboardKeyState state) => _state = state;

    public void SetLeftStick(byte x, byte y)
    {
        double dx = x >= VirtualJoystickController.NeutralAxis
            ? (x - VirtualJoystickController.NeutralAxis) / 127.0
            : (x - VirtualJoystickController.NeutralAxis) / 128.0;
        double dy = y >= VirtualJoystickController.NeutralAxis
            ? (y - VirtualJoystickController.NeutralAxis) / 127.0
            : (y - VirtualJoystickController.NeutralAxis) / 128.0;
        _state.SetMovementKeys(Quantize(dx, dy));
    }

    public static IReadOnlySet<KeyboardKey> Quantize(double x, double y)
    {
        if (Math.Abs(x) < 0.0001 && Math.Abs(y) < 0.0001) return NoKeys;

        double degrees = Math.Atan2(-y, x) * 180.0 / Math.PI;
        if (degrees < 0) degrees += 360.0;
        int sector = (int)Math.Floor((degrees + 22.5) / 45.0) % 8;
        return sector switch
        {
            0 => Keys(KeyboardKey.D),
            1 => Keys(KeyboardKey.W, KeyboardKey.D),
            2 => Keys(KeyboardKey.W),
            3 => Keys(KeyboardKey.W, KeyboardKey.A),
            4 => Keys(KeyboardKey.A),
            5 => Keys(KeyboardKey.S, KeyboardKey.A),
            6 => Keys(KeyboardKey.S),
            _ => Keys(KeyboardKey.S, KeyboardKey.D)
        };
    }

    private static IReadOnlySet<KeyboardKey> Keys(params KeyboardKey[] keys) => new HashSet<KeyboardKey>(keys);
}
