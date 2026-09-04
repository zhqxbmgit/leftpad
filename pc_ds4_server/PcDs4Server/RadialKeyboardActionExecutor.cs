namespace PcDs4Server;

public sealed class RadialKeyboardActionExecutor
{
    private readonly KeyboardKeyState _state;
    private long _executionId;

    public RadialKeyboardActionExecutor(KeyboardKeyState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public bool TryExecute(RadialSlotMapping mapping, out string error)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        if (mapping.Kind is not (RadialActionKind.KeyboardKey or RadialActionKind.KeyboardShortcut))
        {
            error = "该动作不是键盘动作。";
            return false;
        }
        if (!mapping.TryValidate(out error)) return false;

        string executionPrefix = $"radial:{Interlocked.Increment(ref _executionId)}:";
        (string Source, KeyboardKey Key)[] keys = BuildExecutionKeys(mapping, executionPrefix);
        Exception? failure = null;

        try
        {
            foreach ((string source, KeyboardKey key) in keys)
                _state.Press(source, key);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            for (int index = keys.Length - 1; index >= 0; index--)
            {
                try
                {
                    _state.Release(keys[index].Source);
                }
                catch (Exception ex)
                {
                    failure ??= ex;
                }
            }
        }

        if (failure == null)
        {
            error = string.Empty;
            return true;
        }

        Exception reportedFailure = failure is KeyboardOutputException outputFailure &&
            outputFailure.InnerException != null
                ? outputFailure.InnerException
                : failure;
        error = reportedFailure.Message;
        return false;
    }

    internal string[] BeginPress(RadialSlotMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (mapping.Kind is not (RadialActionKind.KeyboardKey or RadialActionKind.KeyboardShortcut) ||
            !mapping.TryValidate(out _))
            throw new ArgumentException("该动作不是有效的键盘动作。", nameof(mapping));

        string prefix = $"radial:{Interlocked.Increment(ref _executionId)}:";
        (string Source, KeyboardKey Key)[] keys = BuildExecutionKeys(mapping, prefix);
        string[] sources = keys.Select(key => key.Source).ToArray();
        try
        {
            foreach ((string source, KeyboardKey key) in keys)
                _state.Press(source, key);
            return sources;
        }
        catch
        {
            TryEndPress(sources, out _);
            throw;
        }
    }

    internal bool TryEndPress(IReadOnlyList<string> sources, out string error)
    {
        Exception? failure = null;
        for (int index = sources.Count - 1; index >= 0; index--)
        {
            try { _state.Release(sources[index]); }
            catch (Exception ex) { failure ??= ex; }
        }
        error = failure?.GetBaseException().Message ?? string.Empty;
        return failure == null;
    }

    private static (string Source, KeyboardKey Key)[] BuildExecutionKeys(
        RadialSlotMapping mapping,
        string executionPrefix)
    {
        var keys = new List<(string Source, KeyboardKey Key)>(5);
        if (mapping.Kind == RadialActionKind.KeyboardShortcut)
        {
            if (mapping.Ctrl) keys.Add((executionPrefix + "ctrl", KeyboardKey.LeftControl));
            if (mapping.Alt) keys.Add((executionPrefix + "alt", KeyboardKey.LeftAlt));
            if (mapping.Shift) keys.Add((executionPrefix + "shift", KeyboardKey.LeftShift));
            if (mapping.Win) keys.Add((executionPrefix + "win", KeyboardKey.LeftWin));
        }

        keys.Add((executionPrefix + "key", mapping.Key!.Value));
        return keys.ToArray();
    }
}
