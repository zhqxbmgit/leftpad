using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using PcDs4Server;

string auditRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
string visualPackPath = Path.Combine(
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../PcDs4Server/bin/Release/net8.0-windows/win-x64")),
    "Assets", "UIVisualPacks", "radial-v5");
LayoutDefinition layout = RadialVisualPackDefinition.Load(visualPackPath).LayoutDefinition;

string saveTargetDirectory = Path.Combine(auditRoot, "save-target-is-directory");
Directory.CreateDirectory(saveTargetDirectory);
var initial = RadialMenuSettings.Default;
var changed = initial with { ScalePercent = initial.ScalePercent + 1 };
using var controller = new RadialMenuController(new FakeOverlay(), initial, layout);
var store = new RadialMenuSettingsStore(saveTargetDirectory);
bool radialSaved = RadialMenuSettingsPersistence.TryApplyAndSave(
    controller,
    store,
    changed,
    controller.ApplySettings,
    out string radialError);

var bindingStore = new ThrowingBindingStore();
using var service = new Ds4Service(
    new UnusedDirectDs4Factory(),
    new NoOpKeyboardOutput(),
    bindingStore);
KeyboardBindings bindings = service.KeyboardBindings;
bindings.Cross = KeyboardKey.Enter;
string? bindingException = null;
try
{
    service.TryUpdateKeyboardBindings(bindings);
}
catch (Exception exception)
{
    bindingException = exception.GetType().Name + ": " + exception.Message;
}

object uiRepros = RunUiRepros(auditRoot);

string resultJson = JsonSerializer.Serialize(new
{
    radial = new
    {
        saveReturned = radialSaved,
        error = radialError,
        requestedScale = changed.ScalePercent,
        runtimeScaleAfterFailure = controller.ActiveSettings.ScalePercent,
        runtimeChangedDespiteFailure = controller.ActiveSettings.ScalePercent == changed.ScalePercent
    },
    keyboard = new
    {
        exception = bindingException,
        requestedCross = bindings.Cross.ToString(),
        runtimeCrossAfterFailure = service.KeyboardBindings.Cross.ToString(),
        runtimeChangedDespiteException = service.KeyboardBindings.Cross == bindings.Cross
    },
    ui = uiRepros
}, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(auditRoot, "persistence-failure.json"), resultJson);
Console.WriteLine(resultJson);

static object RunUiRepros(string auditRoot)
{
    object? result = null;
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        string? previousNative = Environment.GetEnvironmentVariable("LEFTPAD_NATIVE_UI");
        try
        {
            Environment.SetEnvironmentVariable("LEFTPAD_NATIVE_UI", "1");
            var bindingStore = new MemoryBindingStore();
            using var uiService = new Ds4Service(
                new UnusedDirectDs4Factory(),
                new NoOpKeyboardOutput(),
                bindingStore);
            string settingsPath = Path.Combine(auditRoot, "ui-repro-settings.json");
            using var form = new MainForm(uiService, new RadialMenuSettingsStore(settingsPath));

            const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
            var states = (Dictionary<string, bool>)typeof(MainForm)
                .GetField("_btnStates", privateInstance)!.GetValue(form)!;
            uiService.ProcessProtocolAction("triangle", "down");
            bool pressedBeforeStop = states["triangle"];
            uiService.Stop();
            bool pressedAfterStop = states["triangle"];

            MethodInfo appendLog = typeof(MainForm).GetMethod("AppendLog", privateInstance)!;
            for (int index = 1; index <= 1000; index++)
                appendLog.Invoke(form, new object[] { $"native line {index}" });
            var logBox = (RichTextBox)typeof(MainForm)
                .GetField("_logBox", privateInstance)!.GetValue(form)!;
            string[] nonEmptyLines = logBox.Lines.Where(line => line.Length > 0).ToArray();

            result = new
            {
                controllerPressedBeforeStop = pressedBeforeStop,
                controllerPressedAfterStop = pressedAfterStop,
                controllerDisplayStuckAfterStop = pressedAfterStop,
                nativeLogsAppended = 1000,
                nativeLogsFinalNonEmptyLineCount = nonEmptyLines.Length,
                nativeLogsFirstText = nonEmptyLines.FirstOrDefault(),
                nativeLogsLastText = nonEmptyLines.LastOrDefault()
            };
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            Environment.SetEnvironmentVariable("LEFTPAD_NATIVE_UI", previousNative);
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure != null) throw new TargetInvocationException(failure);
    return result!;
}

sealed class FakeOverlay : IRadialMenuOverlay
{
    public bool IsVisible { get; private set; }
    public void ShowAt(Point screenPoint, RadialMenuSettings settings, int selectedSlot) => IsVisible = true;
    public void Hide() => IsVisible = false;
    public void Dispose() => IsVisible = false;
}

sealed class ThrowingBindingStore : IKeyboardBindingStore
{
    public KeyboardBindings Load() => new();
    public void Save(KeyboardBindings bindings) => throw new IOException("simulated binding store failure");
}

sealed class MemoryBindingStore : IKeyboardBindingStore
{
    private KeyboardBindings _bindings = new();
    public KeyboardBindings Load() => _bindings.Clone();
    public void Save(KeyboardBindings bindings) => _bindings = bindings.Clone();
}

sealed class NoOpKeyboardOutput : IKeyboardOutput
{
    public void SetKeyState(KeyboardKey key, bool isPressed) { }
}

sealed class UnusedDirectDs4Factory : IDirectDs4Factory
{
    public IDirectDs4Session Create() => throw new InvalidOperationException("not used");
}
