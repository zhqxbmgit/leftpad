using Microsoft.Win32;

namespace PcDs4Server;

internal interface IStartupValueStore
{
    string? Read(string name);
    void Write(string name, string command);
    void Delete(string name);
}

internal sealed class WindowsStartupValueStore : IStartupValueStore
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Read(string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(
            name,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void Write(string name, string command)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the Windows startup key.");
        key.SetValue(name, command, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

internal sealed record StartupState(bool Enabled, string Notice = "");

internal sealed class StartupRegistration
{
    internal const string ValueName = "LeftPad Receiver";
    internal const string ReadFailureNotice = "无法读取 Windows 开机启动状态。";
    internal const string UpdateFailureNotice = "无法更新 Windows 开机启动。";
    internal const string ExecutableUnavailableNotice = "无法确定 Receiver 程序路径。";

    private readonly IStartupValueStore _store;
    private readonly string? _executablePath;

    public StartupRegistration(IStartupValueStore store, string? executablePath)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _executablePath = string.IsNullOrWhiteSpace(executablePath)
            ? null
            : executablePath;
    }

    internal string? Command => _executablePath is null
        ? null
        : QuoteExecutable(_executablePath);

    public StartupState Read()
    {
        if (_executablePath is null)
            return new(false, ExecutableUnavailableNotice);

        try
        {
            return new(IsCurrentExecutable(_store.Read(ValueName)));
        }
        catch
        {
            return new(false, ReadFailureNotice);
        }
    }

    public StartupState SetEnabled(bool enabled)
    {
        if (_executablePath is null)
            return new(false, ExecutableUnavailableNotice);

        string notice = string.Empty;
        try
        {
            if (enabled)
                _store.Write(ValueName, Command!);
            else
                _store.Delete(ValueName);
        }
        catch
        {
            notice = UpdateFailureNotice;
        }

        StartupState actual = Read();
        return actual with
        {
            Notice = notice.Length == 0 ? actual.Notice : notice
        };
    }

    private bool IsCurrentExecutable(string? command)
    {
        if (_executablePath is null || string.IsNullOrWhiteSpace(command))
            return false;

        string trimmed = command.Trim();
        string? parsed = ParseExecutable(trimmed);
        if (parsed is null && !trimmed.Contains('"'))
            parsed = trimmed;

        try
        {
            return parsed is not null && string.Equals(
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(parsed)),
                Path.GetFullPath(_executablePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal static string QuoteExecutable(string path) => $"\"{path}\"";

    internal static string? ParseExecutable(string command)
    {
        command = command.Trim();
        if (command.Length == 0) return null;
        if (command[0] == '"')
        {
            int closing = command.IndexOf('"', 1);
            return closing > 1 ? command[1..closing] : null;
        }

        int separator = command.IndexOfAny([' ', '\t']);
        return separator < 0 ? command : command[..separator];
    }
}
