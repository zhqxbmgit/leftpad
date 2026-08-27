using System.Text;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class AtomicSettingsPersistenceTests
{
    [Fact]
    public void RadialSuccessfulSave_CommitsDiskThenRuntime()
    {
        const string path = @"C:\isolated\radial.json";
        var files = new MemoryAtomicFileOperations();
        var store = new RadialMenuSettingsStore(path, files);
        using var controller = Controller(RadialMenuSettings.Default);
        RadialMenuSettings candidate = RadialMenuSettings.Default with { ScalePercent = 101 };
        int applyCalls = 0;

        bool saved = RadialMenuSettingsPersistence.TryApplyAndSave(
            controller,
            store,
            candidate,
            settings =>
            {
                Assert.True(files.FileExists(path));
                applyCalls++;
                controller.ApplySettings(settings);
            },
            out string error);

        Assert.True(saved, error);
        Assert.Equal(1, applyCalls);
        Assert.Equal(101, controller.ActiveSettings.ScalePercent);
        Assert.Equal(101, store.Load().Settings.ScalePercent);
        AssertNoTransactionFiles(files);
    }

    [Fact]
    public void RadialSaveFailure_LeavesRuntimeAndDiskUnchanged()
    {
        const string path = @"C:\isolated\radial.json";
        var files = new MemoryAtomicFileOperations();
        var store = new RadialMenuSettingsStore(path, files);
        RadialMenuSettings original = RadialMenuSettings.Default;
        Assert.True(store.TrySave(original, out string initialError), initialError);
        byte[] originalBytes = files.GetBytes(path);
        files.FailNextReplace = true;
        using var controller = Controller(original);
        int applyCalls = 0;

        bool saved = RadialMenuSettingsPersistence.TryApplyAndSave(
            controller,
            store,
            original with { ScalePercent = 101 },
            settings =>
            {
                applyCalls++;
                controller.ApplySettings(settings);
            },
            out string error);

        Assert.False(saved);
        Assert.Contains("injected replace failure", error);
        Assert.Equal(0, applyCalls);
        Assert.Equal(100, controller.ActiveSettings.ScalePercent);
        Assert.Equal(originalBytes, files.GetBytes(path));
        AssertNoTransactionFiles(files);
    }

    [Fact]
    public void RadialRuntimeFailure_RestoresRuntimeAndExactDiskBytes()
    {
        const string path = @"C:\isolated\radial.json";
        var files = new MemoryAtomicFileOperations();
        var store = new RadialMenuSettingsStore(path, files);
        RadialMenuSettings original = RadialMenuSettings.Default;
        Assert.True(store.TrySave(original, out string initialError), initialError);
        byte[] originalBytes = files.GetBytes(path);
        using var controller = Controller(original);

        bool saved = RadialMenuSettingsPersistence.TryApplyAndSave(
            controller,
            store,
            original with { ScalePercent = 101 },
            settings =>
            {
                controller.ApplySettings(settings);
                if (settings.ScalePercent == 101)
                    throw new InvalidOperationException("injected runtime failure");
            },
            out string error);

        Assert.False(saved);
        Assert.Contains("injected runtime failure", error);
        Assert.Equal(100, controller.ActiveSettings.ScalePercent);
        Assert.Equal(originalBytes, files.GetBytes(path));
        AssertNoTransactionFiles(files);
    }

    [Fact]
    public void RadialDreamscapeFailure_PreservesDraftAndDirtyState()
    {
        const string path = @"C:\isolated\radial.json";
        var files = new MemoryAtomicFileOperations();
        var store = new RadialMenuSettingsStore(path, files);
        Assert.True(store.TrySave(RadialMenuSettings.Default, out string initialError), initialError);
        files.FailNextReplace = true;
        using var controller = Controller(RadialMenuSettings.Default);
        var session = new DreamscapeSettingsBasicSession(
            () => controller.ActiveSettings,
            () => new RadialVisualPackCatalog().Discover(),
            _ => { },
            _ => { },
            () => { },
            draft =>
            {
                bool success = RadialMenuSettingsPersistence.TryApplyAndSave(
                    controller,
                    store,
                    draft,
                    controller.ApplySettings,
                    out string saveError);
                return (success, saveError);
            },
            _ => { });
        session.Activate();
        Assert.True(session.TryApplyChange(
            new ReceiverSettingsMessage(
                ReceiverSettingsCommand.BasicChange,
                SettingsBasicFields.OverallSizePercent,
                IntegerValue: 101),
            out string changeError), changeError);

        Assert.False(session.ApplyAndSave(out string error));
        Assert.Contains("injected replace failure", error);
        Assert.True(session.IsDirty);
        Assert.Equal(101, session.Draft.ScalePercent);
        Assert.Equal(100, controller.ActiveSettings.ScalePercent);
        Assert.Equal(100, store.Load().Settings.ScalePercent);
    }

    [Fact]
    public void RadialStore_FirstSaveUsesAtomicMoveAndCleansTemporaryFile()
    {
        const string path = @"C:\isolated\radial.json";
        var files = new MemoryAtomicFileOperations();
        var store = new RadialMenuSettingsStore(path, files);

        Assert.True(store.TrySave(
            RadialMenuSettings.Default with { ScalePercent = 101 },
            out string error), error);

        Assert.Equal(1, files.MoveCount);
        Assert.Equal(0, files.ReplaceCount);
        Assert.Equal(101, store.Load().Settings.ScalePercent);
        AssertNoTransactionFiles(files);
    }

    [Fact]
    public void RadialStore_ExistingFileUsesAtomicReplacement()
    {
        const string path = @"C:\isolated\radial.json";
        var files = new MemoryAtomicFileOperations();
        var store = new RadialMenuSettingsStore(path, files);
        Assert.True(store.TrySave(RadialMenuSettings.Default, out _));

        Assert.True(store.TrySave(
            RadialMenuSettings.Default with { ScalePercent = 101 },
            out string error), error);

        Assert.Equal(1, files.ReplaceCount);
        Assert.Equal(101, store.Load().Settings.ScalePercent);
        AssertNoTransactionFiles(files);
    }

    [Fact]
    public void RadialStore_FailedTemporaryWriteLeavesOriginalIntactAndCleansTemporaryFile()
    {
        const string path = @"C:\isolated\radial.json";
        var files = new MemoryAtomicFileOperations();
        var store = new RadialMenuSettingsStore(path, files);
        Assert.True(store.TrySave(RadialMenuSettings.Default, out _));
        byte[] originalBytes = files.GetBytes(path);
        files.FailNextWrite = true;

        Assert.False(store.TrySave(
            RadialMenuSettings.Default with { ScalePercent = 101 },
            out string error));

        Assert.Contains("injected write failure", error);
        Assert.Equal(originalBytes, files.GetBytes(path));
        AssertNoTransactionFiles(files);
    }

    [Fact]
    public void RadialStore_FailedReplaceLeavesOriginalIntactAndCleansTemporaryFile()
    {
        const string path = @"C:\isolated\radial.json";
        var files = new MemoryAtomicFileOperations();
        var store = new RadialMenuSettingsStore(path, files);
        Assert.True(store.TrySave(RadialMenuSettings.Default, out _));
        byte[] originalBytes = files.GetBytes(path);
        files.FailNextReplace = true;

        Assert.False(store.TrySave(
            RadialMenuSettings.Default with { ScalePercent = 101 },
            out string error));

        Assert.Contains("injected replace failure", error);
        Assert.Equal(originalBytes, files.GetBytes(path));
        AssertNoTransactionFiles(files);
    }

    [Fact]
    public void RadialLoad_MalformedExistingFileIsNotOverwritten()
    {
        const string path = @"C:\isolated\radial.json";
        var files = new MemoryAtomicFileOperations();
        byte[] malformed = Encoding.UTF8.GetBytes("{ definitely-not-json");
        files.SetBytes(path, malformed);
        var store = new RadialMenuSettingsStore(path, files);

        RadialMenuSettingsLoadResult result = store.Load();

        Assert.Equal(RadialMenuSettingsLoadStatus.Malformed, result.Status);
        Assert.Equal(malformed, files.GetBytes(path));
        AssertNoTransactionFiles(files);
    }

    [Fact]
    public void RadialRoundTrip_PreservesBothProfilesKeyboardShortcutAndDs4Button()
    {
        const string path = @"C:\isolated\radial.json";
        var files = new MemoryAtomicFileOperations();
        var radial6Shortcut = new RadialSlotMapping
        {
            Kind = RadialActionKind.KeyboardShortcut,
            Key = KeyboardKey.P,
            Ctrl = true,
            Alt = true,
            Shift = true,
            Win = true
        };
        var radial8Ds4 = new RadialSlotMapping
        {
            Kind = RadialActionKind.Ds4Button,
            Ds4Button = "cross"
        };
        RadialMenuSettings candidate = RadialMenuSettings.Default
            .SetProfileMappings(
                LayoutProfileRegistry.Radial6ProfileId,
                RadialSlotMappings.Create(LayoutProfileRegistry.Radial6SlotCount)
                    .WithSlot(1, radial6Shortcut))
            .SetProfileMappings(
                LayoutProfileRegistry.Radial8ProfileId,
                RadialSlotMappings.Create(LayoutProfileRegistry.Radial8SlotCount)
                    .WithSlot(8, radial8Ds4));
        var store = new RadialMenuSettingsStore(path, files);

        Assert.True(store.TrySave(candidate, out string error), error);
        RadialMenuSettings loaded = store.Load().Settings;

        Assert.Equal(radial6Shortcut, loaded.GetProfileMappings(LayoutProfileRegistry.Radial6ProfileId)[0]);
        Assert.Equal(radial8Ds4, loaded.GetProfileMappings(LayoutProfileRegistry.Radial8ProfileId)[7]);
    }

    [Fact]
    public void RadialNativeFailureFeedback_DoesNotClaimRuntimeWasApplied()
    {
        string message = RadialMenuSettingsControl.CreateSaveFailureMessage("disk denied");

        Assert.Contains("保存失败", message);
        Assert.Contains("设置未应用", message);
        Assert.DoesNotContain("本次运行中应用", message);
    }

    [Fact]
    public void KeyboardSuccessfulSave_ChangesRuntimeOnlyAfterPersistence()
    {
        const string path = @"C:\isolated\keyboard.json";
        var files = new MemoryAtomicFileOperations();
        var store = new JsonKeyboardBindingStore(path, files);
        Assert.True(store.TrySave(new KeyboardBindings()).Success);
        using Ds4Service service = Service(store);
        KeyboardBindings candidate = service.KeyboardBindings;
        candidate.Cross = KeyboardKey.Enter;

        Assert.True(service.TryUpdateKeyboardBindings(candidate, out string error), error);
        Assert.Equal(KeyboardKey.Enter, service.KeyboardBindings.Cross);
        Assert.Equal(KeyboardKey.Enter, store.LoadWithStatus().Bindings.Cross);
    }

    [Fact]
    public void KeyboardSaveIOException_LeavesRuntimeAndDiskUnchangedWithoutEscaping()
    {
        const string path = @"C:\isolated\keyboard.json";
        var files = new MemoryAtomicFileOperations();
        var store = new JsonKeyboardBindingStore(path, files);
        Assert.True(store.TrySave(new KeyboardBindings()).Success);
        byte[] originalBytes = files.GetBytes(path);
        using Ds4Service service = Service(store);
        KeyboardBindings candidate = service.KeyboardBindings;
        candidate.Cross = KeyboardKey.Enter;
        files.FailNextReplace = true;

        Exception? escaped = Record.Exception(() =>
            Assert.False(service.TryUpdateKeyboardBindings(candidate, out _)));

        Assert.Null(escaped);
        Assert.Equal(KeyboardKey.None, service.KeyboardBindings.Cross);
        Assert.Equal(originalBytes, files.GetBytes(path));
        AssertNoTransactionFiles(files);
    }

    [Fact]
    public void KeyboardThrowingInjectedStore_DoesNotEscapeUiFacingServicePath()
    {
        using Ds4Service service = Service(new ThrowingKeyboardBindingStore());
        var logs = new List<string>();
        service.OnLog += logs.Add;
        KeyboardBindings candidate = service.KeyboardBindings;
        candidate.Cross = KeyboardKey.Enter;

        Exception? escaped = Record.Exception(() =>
            Assert.False(service.TryUpdateKeyboardBindings(candidate, out string error)));

        Assert.Null(escaped);
        Assert.Equal(KeyboardKey.None, service.KeyboardBindings.Cross);
        Assert.Contains(logs, log =>
            log.Contains("operation=save", StringComparison.Ordinal) &&
            log.Contains("errorType=IOException", StringComparison.Ordinal));
    }

    [Fact]
    public void KeyboardStore_FirstSaveAndReplacementUseAtomicOperations()
    {
        const string path = @"C:\isolated\keyboard.json";
        var files = new MemoryAtomicFileOperations();
        var store = new JsonKeyboardBindingStore(path, files);

        Assert.True(store.TrySave(new KeyboardBindings { Cross = KeyboardKey.Enter }).Success);
        Assert.Equal(1, files.MoveCount);
        Assert.Equal(0, files.ReplaceCount);
        Assert.True(store.TrySave(new KeyboardBindings { Cross = KeyboardKey.Space }).Success);
        Assert.Equal(1, files.ReplaceCount);
        Assert.Equal(KeyboardKey.Space, store.LoadWithStatus().Bindings.Cross);
        AssertNoTransactionFiles(files);
    }

    [Fact]
    public void KeyboardStore_FailedReplaceLeavesOriginalIntactAndCleansTemporaryFile()
    {
        const string path = @"C:\isolated\keyboard.json";
        var files = new MemoryAtomicFileOperations();
        var store = new JsonKeyboardBindingStore(path, files);
        Assert.True(store.TrySave(new KeyboardBindings { Cross = KeyboardKey.Enter }).Success);
        byte[] originalBytes = files.GetBytes(path);
        files.FailNextReplace = true;

        KeyboardBindingSaveResult result = store.TrySave(
            new KeyboardBindings { Cross = KeyboardKey.Space });

        Assert.False(result.Success);
        Assert.Equal(nameof(IOException), result.ErrorType);
        Assert.Equal(originalBytes, files.GetBytes(path));
        AssertNoTransactionFiles(files);
    }

    [Fact]
    public void KeyboardInvalidJsonLoad_ReturnsStructuredErrorAndDoesNotOverwriteSource()
    {
        const string path = @"C:\isolated\keyboard.json";
        var files = new MemoryAtomicFileOperations();
        byte[] malformed = Encoding.UTF8.GetBytes("{ invalid-json");
        files.SetBytes(path, malformed);
        var store = new JsonKeyboardBindingStore(path, files);

        KeyboardBindingLoadResult result = store.LoadWithStatus();

        Assert.Equal(KeyboardBindingLoadStatus.InvalidJson, result.Status);
        Assert.Equal(nameof(System.Text.Json.JsonException), result.ErrorType);
        Assert.Equal(KeyboardKey.None, result.Bindings.Cross);
        Assert.Equal(malformed, files.GetBytes(path));
    }

    [Fact]
    public void KeyboardPermissionFailure_ReturnsStructuredErrorAndDefaults()
    {
        const string path = @"C:\isolated\keyboard.json";
        var files = new MemoryAtomicFileOperations();
        files.SetBytes(path, Encoding.UTF8.GetBytes("{}"));
        files.ReadException = new UnauthorizedAccessException("injected access denied");
        var store = new JsonKeyboardBindingStore(path, files);

        KeyboardBindingLoadResult result = store.LoadWithStatus();

        Assert.Equal(KeyboardBindingLoadStatus.PermissionError, result.Status);
        Assert.Equal(nameof(UnauthorizedAccessException), result.ErrorType);
        Assert.Equal(KeyboardKey.None, result.Bindings.Cross);
        Assert.Equal("injected access denied", result.Error);
    }

    [Fact]
    public void KeyboardLoadStatus_DistinguishesMissingLoadedIoAndUnknownFailures()
    {
        const string path = @"C:\isolated\keyboard.json";
        var missingFiles = new MemoryAtomicFileOperations();
        Assert.Equal(
            KeyboardBindingLoadStatus.Missing,
            new JsonKeyboardBindingStore(path, missingFiles).LoadWithStatus().Status);

        var loadedFiles = new MemoryAtomicFileOperations();
        var loadedStore = new JsonKeyboardBindingStore(path, loadedFiles);
        Assert.True(loadedStore.TrySave(new KeyboardBindings()).Success);
        Assert.Equal(KeyboardBindingLoadStatus.Loaded, loadedStore.LoadWithStatus().Status);

        loadedFiles.ReadException = new IOException("injected I/O failure");
        Assert.Equal(KeyboardBindingLoadStatus.IoError, loadedStore.LoadWithStatus().Status);

        loadedFiles.ReadException = new InvalidOperationException("injected unknown failure");
        Assert.Equal(KeyboardBindingLoadStatus.UnknownError, loadedStore.LoadWithStatus().Status);
    }

    [Fact]
    public void KeyboardAllActionsRoundTripAndCatalogRemains57Keys()
    {
        const string path = @"C:\isolated\keyboard.json";
        var files = new MemoryAtomicFileOperations();
        var store = new JsonKeyboardBindingStore(path, files);
        KeyboardKey[] keys =
        [
            KeyboardKey.A, KeyboardKey.B, KeyboardKey.C, KeyboardKey.D, KeyboardKey.E,
            KeyboardKey.F1, KeyboardKey.F2, KeyboardKey.F3, KeyboardKey.Enter, KeyboardKey.Space
        ];
        var candidate = new KeyboardBindings();
        for (int index = 0; index < KeyboardBindings.ProtocolActions.Count; index++)
            candidate.Set(KeyboardBindings.ProtocolActions[index], keys[index]);

        Assert.True(store.TrySave(candidate).Success);
        KeyboardBindingLoadResult result = store.LoadWithStatus();

        Assert.Equal(KeyboardBindingLoadStatus.Loaded, result.Status);
        for (int index = 0; index < KeyboardBindings.ProtocolActions.Count; index++)
            Assert.Equal(keys[index], result.Bindings.Get(KeyboardBindings.ProtocolActions[index]));
        Assert.Equal(10, KeyboardBindings.ProtocolActions.Count);
        Assert.Equal(57, Enum.GetValues<KeyboardKey>().Length);
        Assert.Equal(52, KeyboardKeyCatalog.MainKeys.Count);
    }

    [Fact]
    public void KeyboardModeSwitch_RetainsPersistedBindings()
    {
        var files = new MemoryAtomicFileOperations();
        var store = new JsonKeyboardBindingStore(@"C:\isolated\keyboard.json", files);
        using Ds4Service service = Service(store);
        Assert.True(service.TrySetOutputMode(OutputMode.Keyboard));
        KeyboardBindings candidate = service.KeyboardBindings;
        candidate.R3 = KeyboardKey.F12;
        Assert.True(service.TryUpdateKeyboardBindings(candidate, out string error), error);

        Assert.True(service.TrySetOutputMode(OutputMode.DirectDs4));
        Assert.True(service.TrySetOutputMode(OutputMode.Keyboard));

        Assert.Equal(KeyboardKey.F12, service.KeyboardBindings.R3);
        Assert.Equal(KeyboardKey.F12, store.LoadWithStatus().Bindings.R3);
    }

    [Fact]
    public void RealFileSystemSuccessGate_RadialAndKeyboardSurviveReloadIntoRuntime()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "LeftPad.AtomicSettingsPersistenceTests",
            Guid.NewGuid().ToString("N"));
        string radialPath = Path.Combine(directory, "radial-menu-settings.json");
        string keyboardPath = Path.Combine(directory, "keyboard-bindings.json");
        try
        {
            var radialStore = new RadialMenuSettingsStore(radialPath);
            using (RadialMenuController firstController = Controller(RadialMenuSettings.Default))
            {
                Assert.True(RadialMenuSettingsPersistence.TryApplyAndSave(
                    firstController,
                    radialStore,
                    RadialMenuSettings.Default with { ScalePercent = 101 },
                    firstController.ApplySettings,
                    out string radialError), radialError);
            }
            RadialMenuSettings reloadedRadial = new RadialMenuSettingsStore(radialPath).Load().Settings;
            using (RadialMenuController restartedController = Controller(reloadedRadial))
                Assert.Equal(101, restartedController.ActiveSettings.ScalePercent);

            var keyboardStore = new JsonKeyboardBindingStore(keyboardPath);
            using (Ds4Service firstService = Service(keyboardStore))
            {
                KeyboardBindings candidate = firstService.KeyboardBindings;
                candidate.Cross = KeyboardKey.Enter;
                Assert.True(firstService.TryUpdateKeyboardBindings(candidate, out string keyboardError), keyboardError);
            }
            using Ds4Service restartedService = Service(new JsonKeyboardBindingStore(keyboardPath));
            Assert.Equal(KeyboardBindingLoadStatus.Loaded, restartedService.KeyboardBindingLoadResult.Status);
            Assert.Equal(KeyboardKey.Enter, restartedService.KeyboardBindings.Cross);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static RadialMenuController Controller(RadialMenuSettings settings) =>
        new(new NoOpRadialOverlay(), settings);

    private static Ds4Service Service(IKeyboardBindingStore store) =>
        new(new UnusedDirectDs4Factory(), new NoOpKeyboardOutput(), store);

    private static void AssertNoTransactionFiles(MemoryAtomicFileOperations files) =>
        Assert.DoesNotContain(files.Paths, path =>
            path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase));

    private sealed class MemoryAtomicFileOperations : IAtomicFileOperations
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

        public bool FailNextWrite { get; set; }
        public bool FailNextReplace { get; set; }
        public Exception? ReadException { get; set; }
        public int MoveCount { get; private set; }
        public int ReplaceCount { get; private set; }
        public IReadOnlyCollection<string> Paths => _files.Keys;

        public bool FileExists(string path) => _files.ContainsKey(path);

        public string ReadAllText(string path)
        {
            if (ReadException != null) throw ReadException;
            return Encoding.UTF8.GetString(_files[path]);
        }

        public void CreateDirectory(string path)
        {
        }

        public void WriteDurable(string path, byte[] content)
        {
            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new IOException("injected write failure");
            }
            _files.Add(path, content.ToArray());
        }

        public void Replace(string sourcePath, string destinationPath, string? backupPath)
        {
            if (FailNextReplace)
            {
                FailNextReplace = false;
                throw new IOException("injected replace failure");
            }
            if (!_files.TryGetValue(sourcePath, out byte[]? source))
                throw new FileNotFoundException("Source does not exist.", sourcePath);
            if (!_files.TryGetValue(destinationPath, out byte[]? destination))
                throw new FileNotFoundException("Destination does not exist.", destinationPath);
            if (backupPath != null) _files[backupPath] = destination.ToArray();
            _files[destinationPath] = source.ToArray();
            _files.Remove(sourcePath);
            ReplaceCount++;
        }

        public void Move(string sourcePath, string destinationPath)
        {
            if (!_files.TryGetValue(sourcePath, out byte[]? source))
                throw new FileNotFoundException("Source does not exist.", sourcePath);
            if (_files.ContainsKey(destinationPath))
                throw new IOException("Destination already exists.");
            _files[destinationPath] = source.ToArray();
            _files.Remove(sourcePath);
            MoveCount++;
        }

        public void Delete(string path) => _files.Remove(path);

        public void SetBytes(string path, byte[] content) => _files[path] = content.ToArray();

        public byte[] GetBytes(string path) => _files[path].ToArray();
    }

    private sealed class NoOpRadialOverlay : IRadialMenuOverlay
    {
        public bool IsVisible => false;
        public void ShowAt(System.Drawing.Point screenPoint, RadialMenuSettings settings, int selectedSlot) { }
        public void Hide() { }
        public void Dispose() { }
    }

    private sealed class ThrowingKeyboardBindingStore : IKeyboardBindingStore
    {
        public KeyboardBindings Load() => new();
        public void Save(KeyboardBindings bindings) => throw new IOException("simulated binding store failure");
    }

    private sealed class NoOpKeyboardOutput : IKeyboardOutput
    {
        public void SetKeyState(KeyboardKey key, bool isPressed) { }
    }

    private sealed class UnusedDirectDs4Factory : IDirectDs4Factory
    {
        public IDirectDs4Session Create() => new UnusedDirectDs4Session();
    }

    private sealed class UnusedDirectDs4Session : IDirectDs4Session
    {
        public void SetButton(DualShock4Button button, bool pressed) { }
        public void SetDPadDirection(DualShock4DPadDirection direction) { }
        public void SetTrigger(DualShock4Slider trigger, byte value) { }
        public void SetLeftStick(byte x, byte y) { }
        public void SubmitReport() { }
        public void Dispose() { }
    }
}
