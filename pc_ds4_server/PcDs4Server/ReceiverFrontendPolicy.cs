namespace PcDs4Server;

internal static class ReceiverFrontendPolicy
{
    public const string NativeUiEnvironmentVariable = "LEFTPAD_NATIVE_UI";

    public static bool ShouldUseDreamscape(string pageEnvironmentVariable) =>
        ShouldUseDreamscape(
            Environment.GetEnvironmentVariable(NativeUiEnvironmentVariable),
            Environment.GetEnvironmentVariable(pageEnvironmentVariable));

    internal static bool ShouldUseDreamscape(
        string? nativeUiValue,
        string? pageOverrideValue)
    {
        if (ParseBoolean(nativeUiValue) == true)
            return false;

        return ParseBoolean(pageOverrideValue) ?? true;
    }

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
