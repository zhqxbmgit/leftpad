using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace PcDs4Server.Tests;

[Collection("Radial settings WinForms geometry")]
public sealed class ReceiverFrontendPolicyTests
{
    public static IEnumerable<object[]> PageEnvironmentVariables()
    {
        yield return [DreamscapeOverviewFeature.EnvironmentVariable];
        yield return [DreamscapeSettingsFeature.EnvironmentVariable];
        yield return [DreamscapeControllerFeature.EnvironmentVariable];
        yield return [DreamscapeLogsFeature.EnvironmentVariable];
    }

    [Theory]
    [MemberData(nameof(PageEnvironmentVariables))]
    public void ProductionReceiverFrontend_IsNativeOnly(string pageVariable)
    {
        using var environment = new FrontendEnvironmentScope();
        string?[] values = [null, "", " ", "0", "1", "false", "true", "no", "yes", "off", "on", "invalid", "2"];
        foreach (string? native in values)
        foreach (string? page in values)
        {
            Environment.SetEnvironmentVariable(ReceiverFrontendPolicy.NativeUiEnvironmentVariable, native);
            foreach (object[] variable in PageEnvironmentVariables())
                Environment.SetEnvironmentVariable((string)variable[0], "1");
            Environment.SetEnvironmentVariable(pageVariable, page);

            Assert.False(ReceiverFrontendPolicy.ShouldUseDreamscape(pageVariable));
            Assert.False(ReceiverFrontendPolicy.ShouldUseDreamscape(native, page));
            Assert.False(DreamscapeOverviewFeature.IsEnabled);
            Assert.False(DreamscapeControllerFeature.IsEnabled);
            Assert.False(DreamscapeSettingsFeature.IsEnabled);
            Assert.False(DreamscapeLogsFeature.IsEnabled);
        }
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("on")]
    [InlineData("TRUE")]
    public void TruthyNativeMaster_ForcesEveryPageToNative(string nativeValue)
    {
        Assert.All(
            PageEnvironmentVariables(),
            _ => Assert.False(ReceiverFrontendPolicy.ShouldUseDreamscape(nativeValue, null)));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("on")]
    public void NativeMaster_HasPriorityOverTruthyPageOverride(string nativeValue)
    {
        Assert.False(ReceiverFrontendPolicy.ShouldUseDreamscape(nativeValue, "1"));
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("OFF", false)]
    [InlineData("YeS", true)]
    public void DormantBooleanParser_PreservesParsingWithoutEnablingProduction(string pageValue, bool expected)
    {
        Assert.Equal(expected, ReceiverFrontendPolicy.ParseBoolean(pageValue));
        Assert.False(ReceiverFrontendPolicy.ShouldUseDreamscape(null, pageValue));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("2")]
    public void MissingWhitespaceOrInvalidPageValue_UsesNativeDefault(string? pageValue)
    {
        Assert.False(ReceiverFrontendPolicy.ShouldUseDreamscape(null, pageValue));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("off")]
    [InlineData("")]
    [InlineData("invalid")]
    public void FalseOrInvalidNativeMaster_CannotEnableDreamscape(string? nativeValue)
    {
        Assert.False(ReceiverFrontendPolicy.ShouldUseDreamscape(nativeValue, null));
        Assert.False(ReceiverFrontendPolicy.ShouldUseDreamscape(nativeValue, "0"));
        Assert.False(ReceiverFrontendPolicy.ShouldUseDreamscape(nativeValue, "1"));
    }

    [Fact]
    public void OneNativePageOverride_DoesNotChangeOtherDefaultPages()
    {
        Assert.False(ReceiverFrontendPolicy.ShouldUseDreamscape(null, "0"));
        Assert.False(ReceiverFrontendPolicy.ShouldUseDreamscape(null, null));
    }

    [Fact]
    public void FeatureClasses_KeepIndependentPageVariablesAndUseNewDefaults()
    {
        string[] variables =
        [
            DreamscapeOverviewFeature.EnvironmentVariable,
            DreamscapeSettingsFeature.EnvironmentVariable,
            DreamscapeControllerFeature.EnvironmentVariable,
            DreamscapeLogsFeature.EnvironmentVariable
        ];

        Assert.Equal(4, variables.Distinct(StringComparer.Ordinal).Count());
        Assert.All(variables, variable => Assert.StartsWith("LEFTPAD_WEBVIEW2_", variable));
        Assert.False(DreamscapeOverviewFeature.IsEnabledValue(null));
        Assert.False(DreamscapeSettingsFeature.IsEnabledValue(null));
        Assert.False(DreamscapeControllerFeature.IsEnabledValue(null));
        Assert.False(DreamscapeLogsFeature.IsEnabledValue(null));
    }

    [Fact]
    public void NativeMaster_DoesNotConstructAnyWebHosts_AndKeepsSettingsEmbedded()
    {
        RunInSta(() =>
        {
            using var environment = new FrontendEnvironmentScope(nativeUi: "1");
            using MainForm form = CreateMainForm();

            Assert.Null(form.DreamscapeOverviewHost);
            Assert.Null(form.DreamscapeSettingsHost);
            Assert.Null(form.DreamscapeControllerHost);
            Assert.Null(form.DreamscapeLogsHost);
            Assert.Same(form, form.SettingsControl.FindForm());
            Assert.Empty(form.OwnedForms);
        });
    }

    [Fact]
    public void DefaultMode_ConstructsNoDreamscapeHostsOrPopup()
    {
        RunInSta(() =>
        {
            using var environment = new FrontendEnvironmentScope();
            using MainForm form = CreateMainForm();

            Assert.Null(form.DreamscapeOverviewHost);
            Assert.Null(form.DreamscapeSettingsHost);
            Assert.Null(form.DreamscapeControllerHost);
            Assert.Null(form.DreamscapeLogsHost);
            Assert.Empty(form.OwnedForms);
        });
    }

    [Fact]
    public void MissingFrontendAssets_ReportInitializationFailureWithoutThrowing()
    {
        RunInSta(() =>
        {
            string missingDirectory = Path.Combine(
                Path.GetTempPath(),
                "leftpad-missing-dreamscape-assets",
                Guid.NewGuid().ToString("N"));
            using var host = new DreamscapeOverviewHost(
                () => throw new InvalidOperationException("state must not be requested"),
                _ => { },
                _ => { },
                missingDirectory);
            string? failure = null;
            host.InitializationFailed += message => failure = message;

            host.InitializeAsync();

            Assert.NotNull(failure);
            Assert.Contains("asset is missing", failure, StringComparison.OrdinalIgnoreCase);
            Assert.False(host.IsInitialized);
            Assert.NotNull(typeof(MainForm).GetMethod(
                "SetDreamscapeOverviewVisibility",
                BindingFlags.Instance | BindingFlags.NonPublic));
        });
    }

    [Fact]
    public void NativeCloseTrayLifecycleEntryPoints_RemainUnchanged()
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        Assert.NotNull(typeof(MainForm).GetMethod("MainForm_FormClosing", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("ExitProgram", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("ShowMainForm", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("WndProc", privateInstance));
    }

    private static MainForm CreateMainForm()
    {
        var service = new Ds4Service(
            new UnusedDirectDs4Factory(),
            new NoOpKeyboardOutput(),
            new MemoryKeyboardBindingStore());
        string settingsPath = Path.Combine(
            Path.GetTempPath(),
            "leftpad-ui-policy-tests",
            Guid.NewGuid().ToString("N"),
            "radial-menu-settings.json");
        return new MainForm(service, new RadialMenuSettingsStore(settingsPath));
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
            throw new TargetInvocationException(failure);
    }

    private sealed class FrontendEnvironmentScope : IDisposable
    {
        private static readonly string[] Variables =
        [
            ReceiverFrontendPolicy.NativeUiEnvironmentVariable,
            DreamscapeOverviewFeature.EnvironmentVariable,
            DreamscapeSettingsFeature.EnvironmentVariable,
            DreamscapeControllerFeature.EnvironmentVariable,
            DreamscapeLogsFeature.EnvironmentVariable
        ];

        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.Ordinal);

        public FrontendEnvironmentScope(string? nativeUi = null)
        {
            foreach (string variable in Variables)
            {
                _originalValues[variable] = Environment.GetEnvironmentVariable(variable);
                Environment.SetEnvironmentVariable(variable, null);
            }
            Environment.SetEnvironmentVariable(
                ReceiverFrontendPolicy.NativeUiEnvironmentVariable,
                nativeUi);
        }

        public void Dispose()
        {
            foreach ((string variable, string? value) in _originalValues)
                Environment.SetEnvironmentVariable(variable, value);
        }
    }

    private sealed class UnusedDirectDs4Factory : IDirectDs4Factory
    {
        public IDirectDs4Session Create() =>
            throw new InvalidOperationException("Direct DS4 must not start during UI policy tests.");
    }

    private sealed class NoOpKeyboardOutput : IKeyboardOutput
    {
        public void SetKeyState(KeyboardKey key, bool isPressed) { }
    }

    private sealed class MemoryKeyboardBindingStore : IKeyboardBindingStore
    {
        private readonly KeyboardBindings _bindings = new();

        public KeyboardBindings Load() => _bindings.Clone();

        public void Save(KeyboardBindings bindings) { }
    }
}
