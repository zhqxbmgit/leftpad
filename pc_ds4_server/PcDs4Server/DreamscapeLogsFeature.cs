namespace PcDs4Server;

internal static class DreamscapeLogsFeature
{
    public const string EnvironmentVariable = "LEFTPAD_WEBVIEW2_LOGS";

    public static bool IsEnabled => IsEnabledValue(
        Environment.GetEnvironmentVariable(EnvironmentVariable));

    internal static bool IsEnabledValue(string? value) =>
        value is not null &&
        (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    public static string AssetDirectory => Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "DreamscapeLogs");
}
