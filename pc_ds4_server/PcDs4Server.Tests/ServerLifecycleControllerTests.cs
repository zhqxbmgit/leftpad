using Xunit;

namespace PcDs4Server.Tests;

public sealed class ServerLifecycleControllerTests
{
    [Fact]
    public void StartupDefaultMode_IsDirectDs4()
    {
        var service = new FakeLifecycle();
        Assert.Equal(OutputMode.DirectDs4, service.OutputMode);
    }

    [Fact]
    public void AutoStart_ForcesDirectDs4EvenWhenPreviousModeWasKeyboard()
    {
        var service = new FakeLifecycle { OutputMode = OutputMode.Keyboard };
        var lifecycle = new ServerLifecycleController(service);

        ServerStartResult result = lifecycle.TryAutoStartDirectDs4();

        Assert.True(result.Succeeded);
        Assert.Equal(OutputMode.DirectDs4, service.OutputMode);
        Assert.Equal(1, service.InitializeCalls);
        Assert.Equal(1, service.StartCalls);
    }

    [Fact]
    public void AutoStart_IsAttemptedOnlyOnce()
    {
        var service = new FakeLifecycle();
        var lifecycle = new ServerLifecycleController(service);

        Assert.Equal(ServerStartStatus.Started, lifecycle.TryAutoStartDirectDs4().Status);
        Assert.Equal(ServerStartStatus.AutoStartAlreadyAttempted, lifecycle.TryAutoStartDirectDs4().Status);
        Assert.Equal(1, service.InitializeCalls);
        Assert.Equal(1, service.StartCalls);
    }

    [Fact]
    public void AlreadyRunning_DoesNotInitializeOrStartAgain()
    {
        var service = new FakeLifecycle { IsRunning = true };
        var lifecycle = new ServerLifecycleController(service);

        Assert.Equal(ServerStartStatus.AlreadyRunning, lifecycle.TryStart().Status);
        Assert.Equal(0, service.InitializeCalls);
        Assert.Equal(0, service.StartCalls);
    }

    [Fact]
    public void AutoAndManualStart_UseSameCoreStartSequence()
    {
        var autoService = new FakeLifecycle();
        var manualService = new FakeLifecycle();

        new ServerLifecycleController(autoService).TryAutoStartDirectDs4();
        new ServerLifecycleController(manualService).TryStart();

        Assert.Equal(manualService.InitializeCalls, autoService.InitializeCalls);
        Assert.Equal(manualService.StartCalls, autoService.StartCalls);
        Assert.Equal(manualService.Events.Where(item => item != "mode:DirectDs4"),
            autoService.Events.Where(item => item != "mode:DirectDs4"));
    }

    [Fact]
    public void AutoStart_CreatesDirectDs4OnlyOnce()
    {
        var service = new FakeLifecycle();
        var lifecycle = new ServerLifecycleController(service);

        lifecycle.TryAutoStartDirectDs4();
        lifecycle.TryAutoStartDirectDs4();

        Assert.Equal(1, service.InitializeCalls);
    }

    [Fact]
    public void StopAfterAutoStart_AllowsKeyboardSelectionAndManualStart()
    {
        var service = new FakeLifecycle();
        var lifecycle = new ServerLifecycleController(service);
        lifecycle.TryAutoStartDirectDs4();

        service.Stop();
        Assert.True(service.TrySetOutputMode(OutputMode.Keyboard));
        Assert.True(lifecycle.TryStart().Succeeded);

        Assert.Equal(OutputMode.Keyboard, service.OutputMode);
        Assert.Equal(2, service.InitializeCalls);
        Assert.Equal(2, service.StartCalls);
    }

    [Fact]
    public void InitializationFailure_CleansUpAndRemainsStoppedForRetry()
    {
        var service = new FakeLifecycle { InitializeResult = false };
        var lifecycle = new ServerLifecycleController(service);

        ServerStartResult failed = lifecycle.TryAutoStartDirectDs4();

        Assert.Equal(ServerStartStatus.InitializationFailed, failed.Status);
        Assert.False(service.IsRunning);
        Assert.Equal(1, service.StopCalls);
        service.InitializeResult = true;
        Assert.True(lifecycle.TryStart().Succeeded);
    }

    [Fact]
    public void StartException_CleansUpAndRemainsStoppedForRetry()
    {
        var service = new FakeLifecycle { StartException = new IOException("port in use") };
        var lifecycle = new ServerLifecycleController(service);

        ServerStartResult failed = lifecycle.TryAutoStartDirectDs4();

        Assert.Equal(ServerStartStatus.StartFailed, failed.Status);
        Assert.IsType<IOException>(failed.Exception);
        Assert.False(service.IsRunning);
        Assert.Equal(1, service.StopCalls);
        service.StartException = null;
        Assert.True(lifecycle.TryStart().Succeeded);
    }

    [Fact]
    public void DisposeEquivalentStop_AfterAutoStartReleasesLifecycleResources()
    {
        var service = new FakeLifecycle();
        var lifecycle = new ServerLifecycleController(service);
        lifecycle.TryAutoStartDirectDs4();

        service.Stop();

        Assert.False(service.IsRunning);
        Assert.Equal(1, service.StopCalls);
    }

    private sealed class FakeLifecycle : IServerLifecycle
    {
        public bool IsRunning { get; set; }
        public OutputMode OutputMode { get; set; } = OutputMode.DirectDs4;
        public bool InitializeResult { get; set; } = true;
        public Exception? StartException { get; set; }
        public int InitializeCalls { get; private set; }
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public List<string> Events { get; } = new();

        public bool TrySetOutputMode(OutputMode mode)
        {
            Events.Add($"mode:{mode}");
            if (IsRunning) return false;
            OutputMode = mode;
            return true;
        }

        public bool Initialize()
        {
            InitializeCalls++;
            Events.Add("initialize");
            return InitializeResult;
        }

        public void Start()
        {
            StartCalls++;
            Events.Add("start");
            if (StartException != null) throw StartException;
            IsRunning = true;
        }

        public void Stop()
        {
            StopCalls++;
            Events.Add("stop");
            IsRunning = false;
        }
    }
}
