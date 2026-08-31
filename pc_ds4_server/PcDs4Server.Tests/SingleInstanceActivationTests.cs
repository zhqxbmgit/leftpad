using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Xunit;

namespace PcDs4Server.Tests;

[Collection("Radial settings WinForms geometry")]
public sealed class SingleInstanceActivationTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void FirstCoordinatorObtainsOwnership()
    {
        using SingleInstanceCoordinator coordinator = SingleInstanceCoordinator.Acquire(
            UniqueMutexName());

        Assert.True(coordinator.IsPrimaryInstance);
    }

    [Fact]
    public void SecondCoordinatorDetectsExistingInstance()
    {
        string mutexName = UniqueMutexName();
        using SingleInstanceCoordinator primary = SingleInstanceCoordinator.Acquire(mutexName);
        using SingleInstanceCoordinator secondary = SingleInstanceCoordinator.Acquire(mutexName);

        Assert.True(primary.IsPrimaryInstance);
        Assert.False(secondary.IsPrimaryInstance);
    }

    [Fact]
    public async Task SecondarySendsOneActivateCommand_AndPrimaryReceivesIt()
    {
        string pipeName = UniquePipeName();
        int activationCount = 0;
        using var server = Server(pipeName, _ =>
        {
            Interlocked.Increment(ref activationCount);
            return Task.FromResult(true);
        });

        SingleInstanceActivationResult result = await Client(pipeName).TryActivateAsync();

        Assert.Equal(SingleInstanceActivationResult.Activated, result);
        Assert.Equal(1, Volatile.Read(ref activationCount));
    }

    [Fact]
    public async Task UnknownCommand_IsRejectedWithoutActivation()
    {
        string pipeName = UniquePipeName();
        int activationCount = 0;
        using var server = Server(pipeName, _ =>
        {
            Interlocked.Increment(ref activationCount);
            return Task.FromResult(true);
        });

        SingleInstanceActivationResult result = await Client(pipeName).SendCommandAsync(
            "not-act",
            TimeSpan.FromSeconds(2));

        Assert.Equal(SingleInstanceActivationResult.Rejected, result);
        Assert.Equal(0, Volatile.Read(ref activationCount));
    }

    [Fact]
    public async Task ActivationCallbackException_DoesNotStopPrimaryListener()
    {
        string pipeName = UniquePipeName();
        int callbackCount = 0;
        using var server = Server(pipeName, _ =>
        {
            if (Interlocked.Increment(ref callbackCount) == 1)
                throw new InvalidOperationException("injected activation failure");
            return Task.FromResult(true);
        });

        Assert.Equal(
            SingleInstanceActivationResult.Rejected,
            await Client(pipeName).TryActivateAsync());
        Assert.Equal(
            SingleInstanceActivationResult.Activated,
            await Client(pipeName).TryActivateAsync());
        Assert.Equal(2, Volatile.Read(ref callbackCount));
    }

    [Fact]
    public async Task OversizedPayload_IsRejectedWithoutAllocationOrActivation()
    {
        string pipeName = UniquePipeName();
        int activationCount = 0;
        using var server = Server(pipeName, _ =>
        {
            Interlocked.Increment(ref activationCount);
            return Task.FromResult(true);
        });

        string oversized = new('x', SingleInstanceActivationClient.MaximumPayloadBytes + 1);
        SingleInstanceActivationResult result = await Client(pipeName).SendCommandAsync(
            oversized,
            TimeSpan.FromSeconds(2));

        Assert.Equal(SingleInstanceActivationResult.Rejected, result);
        Assert.Equal(0, Volatile.Read(ref activationCount));
    }

    [Fact]
    public async Task EndpointUnavailable_HasBoundedTimeoutAndDoesNotHang()
    {
        var stopwatch = Stopwatch.StartNew();

        SingleInstanceActivationResult result = await Client(UniquePipeName()).SendCommandAsync(
            "activate",
            TimeSpan.FromMilliseconds(500));

        stopwatch.Stop();
        Assert.Equal(SingleInstanceActivationResult.EndpointUnavailable, result);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StartupRace_RetriesUntilPrimaryEndpointAppears()
    {
        string pipeName = UniquePipeName();
        Task<SingleInstanceActivationResult> activation = Client(pipeName).SendCommandAsync(
            "activate",
            TimeSpan.FromSeconds(2));
        await Task.Delay(250);

        using var server = Server(pipeName, _ => Task.FromResult(true));

        Assert.Equal(SingleInstanceActivationResult.Activated, await activation);
    }

    [Fact]
    public async Task ServerDisposal_IsClean_AndLateRequestIsHarmless()
    {
        string pipeName = UniquePipeName();
        var server = Server(pipeName, _ => Task.FromResult(true));

        server.Dispose();
        var stopwatch = Stopwatch.StartNew();
        SingleInstanceActivationResult result = await Client(pipeName).SendCommandAsync(
            "activate",
            TimeSpan.FromMilliseconds(350));

        Assert.Equal(SingleInstanceActivationResult.EndpointUnavailable, result);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ExitRace_CancelsInFlightRequestWithinBoundedTime()
    {
        string pipeName = UniquePipeName();
        using var callbackEntered = new ManualResetEventSlim();
        var server = Server(pipeName, async token =>
        {
            callbackEntered.Set();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return true;
        });
        Task<SingleInstanceActivationResult> request = Client(pipeName).SendCommandAsync(
            "activate",
            TimeSpan.FromSeconds(2));
        Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(2)));

        var stopwatch = Stopwatch.StartNew();
        server.Dispose();
        SingleInstanceActivationResult result = await request;

        Assert.NotEqual(SingleInstanceActivationResult.Activated, result);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TenSequentialSecondaryRequests_AllActivateExactlyOnce()
    {
        string pipeName = UniquePipeName();
        int activationCount = 0;
        using var server = Server(pipeName, _ =>
        {
            Interlocked.Increment(ref activationCount);
            return Task.FromResult(true);
        });

        for (int index = 0; index < 10; index++)
            Assert.Equal(SingleInstanceActivationResult.Activated, await Client(pipeName).TryActivateAsync());

        Assert.Equal(10, Volatile.Read(ref activationCount));
    }

    [Fact]
    public async Task FiveConcurrentSecondaryRequests_AllCompleteAndLeaveOneServer()
    {
        string pipeName = UniquePipeName();
        int activationCount = 0;
        using var server = Server(pipeName, async token =>
        {
            Interlocked.Increment(ref activationCount);
            await Task.Delay(10, token);
            return true;
        });
        Task<SingleInstanceActivationResult>[] requests = Enumerable.Range(0, 5)
            .Select(_ => Client(pipeName).TryActivateAsync())
            .ToArray();

        SingleInstanceActivationResult[] results = await Task.WhenAll(requests);

        Assert.All(results, result => Assert.Equal(SingleInstanceActivationResult.Activated, result));
        Assert.Equal(5, Volatile.Read(ref activationCount));
    }

    [Fact]
    public void ActivationMessage_MarshalsToUiThreadAndRestoresHiddenMainForm()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(nativeUi: true);
            fixture.Form.Show();
            fixture.Form.Hide();

            Task<bool> request = Task.Run(() =>
                fixture.Form.RequestExistingInstanceActivationAsync(CancellationToken.None));
            PumpUntil(request);

            Assert.True(request.GetAwaiter().GetResult());
            Assert.True(fixture.Form.Visible);
            Assert.NotEqual(FormWindowState.Minimized, fixture.Form.WindowState);
        });
    }

    [Fact]
    public void VisibleNormalMainForm_RemainsVisibleAndUsesSameInstance()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(nativeUi: true);
            fixture.Form.Show();
            MainForm original = fixture.Form;

            Assert.True(fixture.Form.ActivateExistingInstance());

            Assert.Same(original, fixture.Form);
            Assert.True(fixture.Form.Visible);
            Assert.NotEqual(FormWindowState.Minimized, fixture.Form.WindowState);
            Assert.Equal(1, Application.OpenForms.Cast<Form>().Count(form => form == fixture.Form));
        });
    }

    [Fact]
    public void MinimizedMainForm_IsRestoredToNormal()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(nativeUi: true);
            fixture.Form.Show();
            fixture.Form.WindowState = FormWindowState.Minimized;

            Assert.True(fixture.Form.ActivateExistingInstance());

            Assert.True(fixture.Form.Visible);
            Assert.Equal(FormWindowState.Normal, fixture.Form.WindowState);
        });
    }

    [Fact]
    public void MaximizedMainForm_IsNotForcedBackToNormal()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(nativeUi: true);
            fixture.Form.Show();
            fixture.Form.WindowState = FormWindowState.Maximized;

            Assert.True(fixture.Form.ActivateExistingInstance());

            Assert.Equal(FormWindowState.Maximized, fixture.Form.WindowState);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TrayHiddenMainForm_IsRestoredAsNativeRegardlessOfLegacyOverride(bool nativeUi)
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(nativeUi);
            fixture.Form.Show();
            fixture.Form.Hide();
            Assert.False(fixture.Form.Visible);

            Assert.True(fixture.Form.ActivateExistingInstance());

            Assert.True(fixture.Form.Visible);
            Assert.NotEqual(FormWindowState.Minimized, fixture.Form.WindowState);
            Assert.Null(fixture.Form.DreamscapeOverviewHost);
        });
    }

    [Theory]
    [InlineData("Settings", true)]
    [InlineData("Log", true)]
    [InlineData("Gamepad", true)]
    [InlineData("Settings", false)]
    [InlineData("Log", false)]
    [InlineData("Gamepad", false)]
    public void TrayRestore_PreservesCurrentPage(string pageName, bool nativeUi)
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(nativeUi);
            FieldInfo pageField = Assert.IsAssignableFrom<FieldInfo>(
                typeof(MainForm).GetField("_currentPage", PrivateInstance));
            object requestedPage = Enum.Parse(pageField.FieldType, pageName);
            MethodInfo navigate = Assert.IsAssignableFrom<MethodInfo>(
                typeof(MainForm).GetMethod("NavigateTo", PrivateInstance));
            navigate.Invoke(fixture.Form, [requestedPage]);
            fixture.Form.Show();
            fixture.Form.Hide();

            Assert.True(fixture.Form.ActivateExistingInstance());

            Assert.Equal(requestedPage, pageField.GetValue(fixture.Form));
        });
    }

    [Fact]
    public void Activation_DoesNotRestartServiceOrCreateAnotherTrayOrMainForm()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(nativeUi: true);
            fixture.Form.Show();
            fixture.Form.Hide();
            NotifyIcon trayBefore = Field<NotifyIcon>(fixture.Form, "_notifyIcon");

            Assert.True(fixture.Form.ActivateExistingInstance());

            Assert.Equal(0, fixture.Factory.CreateCalls);
            Assert.Same(trayBefore, Field<NotifyIcon>(fixture.Form, "_notifyIcon"));
            Assert.Equal(1, Application.OpenForms.Cast<Form>().Count(form => form == fixture.Form));
        });
    }

    [Fact]
    public void DisposedUiTarget_RejectsLateActivationWithoutThrowing()
    {
        RunInSta(() =>
        {
            using var fixture = new MainFormFixture(nativeUi: true);
            _ = fixture.Form.Handle;
            fixture.Form.Dispose();

            Assert.False(fixture.Form.RequestExistingInstanceActivationAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult());
        });
    }

    private static SingleInstanceActivationServer Server(
        string pipeName,
        Func<CancellationToken, Task<bool>> callback)
    {
        var server = new SingleInstanceActivationServer(pipeName, callback);
        server.Start();
        return server;
    }

    private static SingleInstanceActivationClient Client(string pipeName) =>
        new(pipeName, _ => { });

    private static string UniqueMutexName() =>
        $"Local\\LeftPad.SingleInstance.Tests.{Guid.NewGuid():N}";

    private static string UniquePipeName() =>
        $"LeftPad.SingleInstance.Tests.{Guid.NewGuid():N}";

    private static T Field<T>(MainForm form, string name) where T : class =>
        Assert.IsAssignableFrom<T>(typeof(MainForm).GetField(name, PrivateInstance)?.GetValue(form));

    private static void PumpUntil(Task task)
    {
        var timeout = Stopwatch.StartNew();
        while (!task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(5))
            Application.DoEvents();
        Assert.True(task.IsCompleted, "UI dispatch did not complete within five seconds.");
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class MainFormFixture : IDisposable
    {
        private readonly string? _originalNativeUi;

        public MainFormFixture(bool nativeUi)
        {
            _originalNativeUi = Environment.GetEnvironmentVariable(
                ReceiverFrontendPolicy.NativeUiEnvironmentVariable);
            Environment.SetEnvironmentVariable(
                ReceiverFrontendPolicy.NativeUiEnvironmentVariable,
                nativeUi ? "1" : null);

            Factory = new FakeDirectDs4Factory();
            var service = new Ds4Service(
                Factory,
                new NoOpKeyboardOutput(),
                new MemoryKeyboardBindingStore());
            string settingsPath = Path.Combine(
                Path.GetTempPath(),
                "leftpad-single-instance-tests",
                Guid.NewGuid().ToString("N"),
                "radial-menu-settings.json");
            Form = new MainForm(service, new RadialMenuSettingsStore(settingsPath));
            Service = service;
        }

        public FakeDirectDs4Factory Factory { get; }
        public Ds4Service Service { get; }
        public MainForm Form { get; }

        public void Dispose()
        {
            Form.Dispose();
            Service.Dispose();
            Environment.SetEnvironmentVariable(
                ReceiverFrontendPolicy.NativeUiEnvironmentVariable,
                _originalNativeUi);
        }
    }

    private sealed class FakeDirectDs4Factory : IDirectDs4Factory
    {
        public int CreateCalls { get; private set; }

        public IDirectDs4Session Create()
        {
            CreateCalls++;
            return new NoOpDirectDs4Session();
        }
    }

    private sealed class NoOpDirectDs4Session : IDirectDs4Session
    {
        public void SetButton(DualShock4Button button, bool pressed) { }
        public void SetDPadDirection(DualShock4DPadDirection direction) { }
        public void SetTrigger(DualShock4Slider trigger, byte value) { }
        public void SetLeftStick(byte x, byte y) { }
        public void SubmitReport() { }
        public void Dispose() { }
    }

    private sealed class NoOpKeyboardOutput : IKeyboardOutput
    {
        public void SetKeyState(KeyboardKey key, bool isPressed) { }
    }

    private sealed class MemoryKeyboardBindingStore : IKeyboardBindingStore
    {
        private KeyboardBindings _bindings = new();

        public KeyboardBindings Load() => _bindings.Clone();
        public void Save(KeyboardBindings bindings) => _bindings = bindings.Clone();
    }
}
