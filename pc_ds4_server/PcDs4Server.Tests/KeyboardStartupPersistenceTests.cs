using System.Drawing;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Xunit;
using Xunit.Abstractions;

namespace PcDs4Server.Tests;

[Collection("Radial settings WinForms geometry")]
public sealed class KeyboardStartupPersistenceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(KeyboardBindingLoadStatus.Loaded)]
    [InlineData(KeyboardBindingLoadStatus.Missing)]
    [InlineData(KeyboardBindingLoadStatus.InvalidJson)]
    [InlineData(KeyboardBindingLoadStatus.IoError)]
    [InlineData(KeyboardBindingLoadStatus.PermissionError)]
    public void Startup_PreservesLoadResultAndBindingsWithoutWriting(KeyboardBindingLoadStatus status)
    {
        NativeReceiverTestThread.Run(() =>
        {
            using var fixture = new Fixture(status);
            byte[]? original = fixture.Files.Bytes(fixture.KeyboardPath);
            KeyboardBindings expected = status == KeyboardBindingLoadStatus.Loaded ? CustomBindings() : new();
            Assert.Equal(status, fixture.Service.KeyboardBindingLoadResult.Status);
            Assert.Equal(1, fixture.Keyboard.LoadCount);
            Assert.Equal(0, fixture.Keyboard.SaveCount);
            AssertBindings(expected, fixture.Service.KeyboardBindings);

            fixture.CreateNativeHandles();
            // Real OnShown and page navigation, with isolated stores and a factory that cannot create DS4.
            fixture.Form.Show();
            foreach (string page in new[] { "Gamepad", "Settings", "Log", "Overview" })
                typeof(MainForm).GetMethod("NavigateTo", PrivateInstance)!.Invoke(fixture.Form,
                    [Enum.Parse(typeof(MainForm).GetNestedType("ReceiverPage", BindingFlags.NonPublic)!, page)]);

            Assert.Equal(1, fixture.Keyboard.LoadCount);
            Assert.Equal(0, fixture.Keyboard.SaveCount);
            Assert.Null(fixture.Keyboard.LastSaved);
            Assert.Equal(0, fixture.Files.WriteCount);
            Assert.Equal(original, fixture.Files.Bytes(fixture.KeyboardPath));
            AssertBindings(expected, fixture.Service.KeyboardBindings);
            foreach (string action in KeyboardBindings.ProtocolActions)
                Assert.Equal(expected.Get(action), fixture.Editor(action).SelectedItem);
            if (status is KeyboardBindingLoadStatus.InvalidJson or KeyboardBindingLoadStatus.IoError or KeyboardBindingLoadStatus.PermissionError)
            {
                Assert.NotNull(fixture.Service.KeyboardBindingLoadResult.ErrorType);
                string log = fixture.LogText;
                Assert.Contains("operation=load", log);
                Assert.Contains(fixture.Service.KeyboardBindingLoadResult.ErrorType!, log);
                Assert.DoesNotContain("operation=save", log);
            }
            Assert.False(File.Exists(fixture.RadialPath));
            output.WriteLine($"{status}: Load={fixture.Keyboard.LoadCount}, StartupSave={fixture.Keyboard.SaveCount}, diskWrites={fixture.Files.WriteCount}, sourcePreserved=true");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UserSelection_SavesExactlyOnceAndPreservesOtherMappings(bool missing)
    {
        NativeReceiverTestThread.Run(() =>
        {
            using var fixture = new Fixture(missing ? KeyboardBindingLoadStatus.Missing : KeyboardBindingLoadStatus.Loaded);
            fixture.CreateNativeHandles();
            fixture.SelectKeyboardMode();
            Assert.True(fixture.Editor("cross").Enabled);
            Assert.Equal(0, fixture.Keyboard.SaveCount);
            KeyboardBindings expected = missing ? new() : CustomBindings();
            expected.Cross = KeyboardKey.Z;

            fixture.Editor("cross").SelectedItem = KeyboardKey.Z;

            Assert.Equal(1, fixture.Keyboard.SaveCount);
            Assert.Equal(1, fixture.Keyboard.LoadCount);
            Assert.Equal(1, fixture.Files.WriteCount);
            AssertBindings(expected, fixture.Keyboard.LastSaved!);
            AssertBindings(expected, fixture.Service.KeyboardBindings);
            AssertBindings(expected, fixture.JsonStore.Load());
            output.WriteLine($"missing={missing}: StartupSave=0, UserSave=1, Cross=Z, other mappings preserved; file materialized only by explicit change");
        });
    }

    [Fact]
    public void RunningService_StillDisablesEditorsAndRejectsChanges()
    {
        NativeReceiverTestThread.Run(() =>
        {
            using var fixture = new Fixture(KeyboardBindingLoadStatus.Loaded);
            fixture.CreateNativeHandles();
            fixture.SelectKeyboardMode();
            // Set the existing lifecycle state without opening a listener or starting cursor sampling.
            PropertyInfo running = typeof(Ds4Service).GetProperty(nameof(Ds4Service.IsRunning))!;
            running.SetValue(fixture.Service, true);
            try
            {
                typeof(MainForm).GetMethod("UpdateOutputControls", PrivateInstance)!.Invoke(fixture.Form, null);
                Assert.All(KeyboardBindings.ProtocolActions, action => Assert.False(fixture.Editor(action).Enabled));
                fixture.Editor("cross").SelectedItem = KeyboardKey.Z;
                Assert.Equal(0, fixture.Keyboard.SaveCount);
                AssertBindings(CustomBindings(), fixture.Service.KeyboardBindings);
                Assert.False(fixture.Service.TryUpdateKeyboardBindings(new KeyboardBindings(), out _));
                Assert.Equal(0, fixture.Keyboard.SaveCount);
            }
            finally { running.SetValue(fixture.Service, false); }
        });
    }

    [Fact]
    public void UserSaveFailure_RemainsVisibleAndDoesNotChangeRuntimeOrSource()
    {
        NativeReceiverTestThread.Run(() =>
        {
            using var fixture = new Fixture(KeyboardBindingLoadStatus.Loaded);
            fixture.CreateNativeHandles();
            fixture.SelectKeyboardMode();
            byte[]? original = fixture.Files.Bytes(fixture.KeyboardPath);
            fixture.Files.WriteException = new UnauthorizedAccessException("isolated write denied");

            fixture.Editor("cross").SelectedItem = KeyboardKey.Z;

            Assert.Equal(1, fixture.Keyboard.SaveCount);
            AssertBindings(CustomBindings(), fixture.Service.KeyboardBindings);
            Assert.Equal(original, fixture.Files.Bytes(fixture.KeyboardPath));
            Assert.Contains("operation=save", fixture.LogText);
            Assert.Contains(nameof(UnauthorizedAccessException), fixture.LogText);
            fixture.Files.WriteException = null;
            fixture.Editor("cross").SelectedItem = KeyboardKey.A;
            Assert.Equal(2, fixture.Keyboard.SaveCount);
            Assert.Equal(KeyboardKey.A, fixture.Service.KeyboardBindings.Cross);
            Assert.Equal(KeyboardKey.A, fixture.JsonStore.Load().Cross);
        });
    }

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static KeyboardBindings CustomBindings() => new()
    {
        Cross = KeyboardKey.Enter, Circle = KeyboardKey.Space, L1 = KeyboardKey.F1, R1 = KeyboardKey.F2
    };
    private static void AssertBindings(KeyboardBindings expected, KeyboardBindings actual) =>
        Assert.All(KeyboardBindings.ProtocolActions, action => Assert.Equal(expected.Get(action), actual.Get(action)));

    private sealed class Fixture : IDisposable
    {
        public Fixture(KeyboardBindingLoadStatus status)
        {
            string directory = Path.Combine(Path.GetTempPath(), "LeftPad.KeyboardStartupTests", Guid.NewGuid().ToString("N"));
            KeyboardPath = Path.Combine(directory, "keyboard.json");
            RadialPath = Path.Combine(directory, "radial.json");
            if (status != KeyboardBindingLoadStatus.Missing)
                Files.SetBytes(KeyboardPath, status == KeyboardBindingLoadStatus.InvalidJson
                    ? Encoding.UTF8.GetBytes("{ invalid-json")
                    : JsonSerializer.SerializeToUtf8Bytes(KeyboardBindings.ProtocolActions.ToDictionary(
                        action => action, action => CustomBindings().Get(action).ToString())));
            Files.ReadException = status switch
            {
                KeyboardBindingLoadStatus.IoError => new IOException("isolated read failure"),
                KeyboardBindingLoadStatus.PermissionError => new UnauthorizedAccessException("isolated read denied"),
                _ => null
            };
            JsonStore = new JsonKeyboardBindingStore(KeyboardPath, Files);
            Keyboard = new RecordingKeyboardBindingStore(JsonStore);
            Service = new Ds4Service(new UnusedDs4Factory(), new NoOpKeyboardOutput(), Keyboard);
            Form = new MainForm(Service, new RadialMenuSettingsStore(RadialPath))
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ShowInTaskbar = false
            };
        }
        public MemoryAtomicFileOperations Files { get; } = new();
        public string KeyboardPath { get; }
        public string RadialPath { get; }
        public JsonKeyboardBindingStore JsonStore { get; }
        public RecordingKeyboardBindingStore Keyboard { get; }
        public Ds4Service Service { get; }
        public MainForm Form { get; }
        public string LogText => ((RichTextBox)typeof(MainForm).GetField("_logBox", PrivateInstance)!.GetValue(Form)!).Text;
        public ComboBox Editor(string action) =>
            Assert.IsType<ComboBox>(Assert.Single(Form.Controls.Find("bindingEditor_" + action, true)));
        public void CreateNativeHandles()
        {
            _ = Form.Handle;
            foreach (string action in KeyboardBindings.ProtocolActions) _ = Editor(action).Handle;
        }
        public void SelectKeyboardMode() =>
            ((ComboBox)typeof(MainForm).GetField("_outputMode", PrivateInstance)!.GetValue(Form)!).SelectedItem = OutputMode.Keyboard;
        public void Dispose()
        {
            typeof(MainForm).GetField("_isReallyClosing", PrivateInstance)!.SetValue(Form, true);
            Form.Close();
            ((NotifyIcon)typeof(MainForm).GetField("_notifyIcon", PrivateInstance)!.GetValue(Form)!).Dispose();
            Form.Dispose();
            Service.Dispose();
        }
    }

    private sealed class RecordingKeyboardBindingStore(IKeyboardBindingStore inner) : IKeyboardBindingStore
    {
        public int LoadCount { get; private set; }
        public int SaveCount { get; private set; }
        public KeyboardBindings? LastSaved { get; private set; }
        public KeyboardBindings Load() => LoadWithStatus().Bindings;
        public KeyboardBindingLoadResult LoadWithStatus()
        {
            LoadCount++;
            return inner.LoadWithStatus();
        }
        public void Save(KeyboardBindings bindings)
        {
            SaveCount++;
            LastSaved = bindings.Clone();
            inner.Save(bindings);
        }
        public KeyboardBindingSaveResult TrySave(KeyboardBindings bindings)
        {
            SaveCount++;
            LastSaved = bindings.Clone();
            return inner.TrySave(bindings);
        }
    }

    private sealed class MemoryAtomicFileOperations : IAtomicFileOperations
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
        public Exception? ReadException { get; set; }
        public Exception? WriteException { get; set; }
        public int WriteCount { get; private set; }
        public bool FileExists(string path) => _files.ContainsKey(path);
        public string ReadAllText(string path) => ReadException is { } error
            ? throw error : Encoding.UTF8.GetString(_files[path]);
        public void CreateDirectory(string path) { }
        public void WriteDurable(string path, byte[] content)
        {
            WriteCount++;
            if (WriteException is { } error) throw error;
            _files.Add(path, content.ToArray());
        }
        public void Replace(string sourcePath, string destinationPath, string? backupPath)
        {
            if (backupPath != null) _files[backupPath] = _files[destinationPath].ToArray();
            _files[destinationPath] = _files[sourcePath];
            _files.Remove(sourcePath);
        }
        public void Move(string sourcePath, string destinationPath)
        {
            _files.Add(destinationPath, _files[sourcePath]);
            _files.Remove(sourcePath);
        }
        public void Delete(string path) => _files.Remove(path);
        public void SetBytes(string path, byte[] value) => _files[path] = value.ToArray();
        public byte[]? Bytes(string path) => _files.TryGetValue(path, out byte[]? value) ? value.ToArray() : null;
    }

    private sealed class UnusedDs4Factory : IDirectDs4Factory
    {
        public IDirectDs4Session Create() => throw new InvalidOperationException("No real DS4 in startup tests.");
    }
    private sealed class NoOpKeyboardOutput : IKeyboardOutput
    {
        public void SetKeyState(KeyboardKey key, bool isPressed) { }
    }
}
