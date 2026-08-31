namespace PcDs4Server;

internal static class ReceiverFrontendPolicy
{
    public const string NativeUiEnvironmentVariable = "LEFTPAD_NATIVE_UI";

    // Native WinForms is the only production frontend. Legacy environment overrides
    // cannot activate the dormant Dreamscape hosts or their WebView2 runtime.
    public static bool ShouldUseDreamscape(string pageEnvironmentVariable) => false;

    internal static bool ShouldUseDreamscape(
        string? nativeUiValue,
        string? pageOverrideValue) => false;

    internal static bool? ParseBoolean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => null
        };
    }
}
