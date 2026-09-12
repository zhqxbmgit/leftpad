using System.Runtime.ExceptionServices;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class StartupRegistrationTests
{
    private const string Current = @"C:\Program Files\LeftPad 测试\PcDs4Server.exe";

    [Fact]
    public void AbsentValue_IsDisabled()
    {
        Assert.False(Registration(new MemoryStore()).Read().Enabled);
    }

    [Fact]
    public void CurrentExecutable_UnquotedWithoutSpaces_IsEnabled()
    {
        const string current = @"C:\LeftPad\PcDs4Server.exe";
        var store = new MemoryStore
        {
            Values = { [StartupRegistration.ValueName] = current }
        };

        Assert.True(new StartupRegistration(store, current).Read().Enabled);
    }

    [Fact]
    public void QuotedCurrentExecutable_WithOptionalArguments_IsEnabled()
    {
        var store = CurrentStore($"\"{Current}\" --ignored-argument");

        Assert.True(Registration(store).Read().Enabled);
    }

    [Fact]
    public void PathWithSpacesAndUnicode_IsQuotedParsedAndEnabled()
    {
        string command = StartupRegistration.QuoteExecutable(Current);

        Assert.Equal($"\"{Current}\"", command);
        Assert.Equal(Current, StartupRegistration.ParseExecutable(command));
        Assert.Equal(Current, StartupRegistration.ParseExecutable(command + " --argument"));
        Assert.True(Registration(CurrentStore(command)).Read().Enabled);
    }

    [Fact]
    public void EnvironmentVariablePath_IsExpandedWhenRead()
    {
        const string variable = "LEFTPAD_STARTUP_TEST_ROOT";
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, @"C:\Program Files\LeftPad 测试");
            var store = CurrentStore($"\"%{variable}%\\PcDs4Server.exe\"");
            Assert.True(Registration(store).Read().Enabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Theory]
    [InlineData("\"C:\\OldLeftPad\\PcDs4Server.exe\"")]
    [InlineData("\"C:\\Other\\Other.exe\"")]
    [InlineData("\"C:\\Program Files\\LeftPad 测试\\PcDs4Server.exe")]
    public void StaleWrongOrMalformedCommand_IsDisabled(string command)
    {
        Assert.False(Registration(CurrentStore(command)).Read().Enabled);
    }

    [Fact]
    public void Enable_WritesQuotedCurrentExecutable()
    {
        var store = new MemoryStore();

        StartupState state = Registration(store).SetEnabled(true);

        Assert.True(state.Enabled);
        Assert.Equal($"\"{Current}\"", store.Values[StartupRegistration.ValueName]);
    }

    [Fact]
    public void Enable_ReplacesStalePath()
    {
        var store = CurrentStore("\"C:\\OldLeftPad\\PcDs4Server.exe\"");

        Assert.True(Registration(store).SetEnabled(true).Enabled);
        Assert.Equal($"\"{Current}\"", store.Values[StartupRegistration.ValueName]);
    }

    [Fact]
    public void RepeatedEnable_IsIdempotentAndKeepsOneCorrectValue()
    {
        var store = new MemoryStore();
        var registration = Registration(store);

        registration.SetEnabled(true);
        registration.SetEnabled(true);

        Assert.Equal(2, store.Writes);
        Assert.Single(store.Values);
        Assert.Equal($"\"{Current}\"", store.Values[StartupRegistration.ValueName]);
    }

    [Fact]
    public void Disable_DeletesOnlyLeftPadValue()
    {
        var store = CurrentStore($"\"{Current}\"");
        store.Values["rightpad Receiver"] = @"""C:\Rightpad\Rightpad.Receiver.exe""";
        store.Values["Other App"] = @"C:\Other.exe";

        StartupState state = Registration(store).SetEnabled(false);

        Assert.False(state.Enabled);
        Assert.DoesNotContain(StartupRegistration.ValueName, store.Values.Keys);
        Assert.Equal(@"""C:\Rightpad\Rightpad.Receiver.exe""", store.Values["rightpad Receiver"]);
        Assert.Equal(@"C:\Other.exe", store.Values["Other App"]);
    }

    [Fact]
    public void Disable_WhenAbsent_IsSafe()
    {
        Assert.False(Registration(new MemoryStore()).SetEnabled(false).Enabled);
    }

    [Fact]
    public void ReadFailure_IsDisabledWithNoticeAndDoesNotThrow()
    {
        var store = new MemoryStore { FailRead = true };

        StartupState state = Registration(store).Read();

        Assert.False(state.Enabled);
        Assert.Equal(StartupRegistration.ReadFailureNotice, state.Notice);
    }

    [Fact]
    public void WriteFailure_ReassertsRegistryTruthAndDoesNotTouchRuntime()
    {
        var store = new MemoryStore { FailWrite = true };
        var runtime = new UnrelatedRuntimeProbe();

        StartupState state = Registration(store).SetEnabled(true);

        Assert.False(state.Enabled);
        Assert.Equal(StartupRegistration.UpdateFailureNotice, state.Notice);
        Assert.True(runtime.Running);
    }

    [Fact]
    public void DeleteFailure_ReassertsRegistryTruthAndDoesNotTouchRuntime()
    {
        var store = CurrentStore($"\"{Current}\"");
        store.FailDelete = true;
        var runtime = new UnrelatedRuntimeProbe();

        StartupState state = Registration(store).SetEnabled(false);

        Assert.True(state.Enabled);
        Assert.Equal(StartupRegistration.UpdateFailureNotice, state.Notice);
        Assert.True(runtime.Running);
    }

    [Fact]
    public void MissingProcessPath_FailsClearlyWithoutRegistryAccess()
    {
        var store = new MemoryStore();
        var registration = new StartupRegistration(store, null);

        Assert.Equal(StartupRegistration.ExecutableUnavailableNotice, registration.Read().Notice);
        Assert.Equal(StartupRegistration.ExecutableUnavailableNotice, registration.SetEnabled(true).Notice);
        Assert.Equal(0, store.Reads + store.Writes + store.Deletes);
    }

    [Fact]
    public void Toggle_WriteFailure_ReturnsToActualRegistryStateAndShowsNotice()
    {
        RunInSta(() =>
        {
            var store = new MemoryStore { FailWrite = true };
            using var control = new StartupToggleControl();
            control.Initialize(Registration(store));

            control.SetEnabledForTesting(true);

            Assert.False(control.EnabledFromRegistry);
            Assert.Equal(StartupRegistration.UpdateFailureNotice, control.Notice);
        });
    }

    [Fact]
    public void Toggle_DeleteFailure_ReturnsToActualRegistryStateAndShowsNotice()
    {
        RunInSta(() =>
        {
            var store = CurrentStore($"\"{Current}\"");
            store.FailDelete = true;
            using var control = new StartupToggleControl();
            control.Initialize(Registration(store));

            control.SetEnabledForTesting(false);

            Assert.True(control.EnabledFromRegistry);
            Assert.Equal(StartupRegistration.UpdateFailureNotice, control.Notice);
        });
    }

    private static MemoryStore CurrentStore(string command) => new()
    {
        Values = { [StartupRegistration.ValueName] = command }
    };

    private static StartupRegistration Registration(MemoryStore store) => new(store, Current);

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
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class UnrelatedRuntimeProbe
    {
        public bool Running { get; } = true;
    }

    private sealed class MemoryStore : IStartupValueStore
    {
        internal readonly Dictionary<string, string> Values = new(StringComparer.Ordinal);
        internal int Reads;
        internal int Writes;
        internal int Deletes;
        internal bool FailRead;
        internal bool FailWrite;
        internal bool FailDelete;

        public string? Read(string name)
        {
            Reads++;
            if (FailRead) throw new UnauthorizedAccessException("test read failure");
            return Values.GetValueOrDefault(name);
        }

        public void Write(string name, string command)
        {
            Writes++;
            if (FailWrite) throw new UnauthorizedAccessException("test write failure");
            Values[name] = command;
        }

        public void Delete(string name)
        {
            Deletes++;
            if (FailDelete) throw new UnauthorizedAccessException("test delete failure");
            Values.Remove(name);
        }
    }
}
