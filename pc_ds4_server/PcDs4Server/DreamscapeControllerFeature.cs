namespace PcDs4Server;

internal static class DreamscapeControllerFeature
{
    public const string EnvironmentVariable = "LEFTPAD_WEBVIEW2_CONTROLLER";

    public static bool IsEnabled =>
        ReceiverFrontendPolicy.ShouldUseDreamscape(EnvironmentVariable);

    internal static bool IsEnabledValue(string? value) =>
        ReceiverFrontendPolicy.ShouldUseDreamscape(null, value);

    public static string AssetDirectory => Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "DreamscapeController");
}
