namespace PcDs4Server;

using Nefarius.ViGEm.Client.Targets.DualShock4;

internal static class KeyboardSmokeTest
{
    public static int Run()
    {
        using var form = new CaptureForm();
        Application.Run(form);
        foreach (string result in form.Results) Console.WriteLine(result);
        return form.Passed ? 0 : 1;
    }

    private sealed class CaptureForm : Form
    {
        private readonly KeyboardKeyState _state = new(new SendInputKeyboardOutput());
        private readonly HashSet<Keys> _held = new();
        private readonly HashSet<Keys> _seenDown = new();
        private readonly HashSet<Keys> _seenUp = new();

        public CaptureForm()
        {
            Text = "LeftPad SendInput Safety Test";
            Width = 520;
            Height = 180;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Safe SendInput verification in progress...\nNo game is being controlled."
            });
            KeyDown += (_, e) => { _held.Add(e.KeyCode); _seenDown.Add(e.KeyCode); e.SuppressKeyPress = true; };
            KeyUp += (_, e) => { _held.Remove(e.KeyCode); _seenUp.Add(e.KeyCode); e.SuppressKeyPress = true; };
            Shown += RunAsync;
        }

        public bool Passed { get; private set; }
        public List<string> Results { get; } = new();

        private async void RunAsync(object? sender, EventArgs e)
        {
            var factory = new CountingFactory();
            using var service = new Ds4Service(factory, new SendInputKeyboardOutput(), new MemoryStore());
            try
            {
                service.TrySetOutputMode(OutputMode.Keyboard);
                bool keyboardStarted = service.Initialize();
                service.Start();
                Activate();
                Focus();
                await Task.Delay(250);

                _state.SetMovementKeys(new HashSet<KeyboardKey> { KeyboardKey.D });
                await Task.Delay(100);
                bool rightHeld = _held.Contains(Keys.D);
                _state.ReleaseMovement();
                await Task.Delay(100);

                _state.SetMovementKeys(new HashSet<KeyboardKey> { KeyboardKey.W, KeyboardKey.D });
                await Task.Delay(100);
                bool diagonalHeld = _held.Contains(Keys.W) && _held.Contains(Keys.D);
                _state.ReleaseMovement();
                await Task.Delay(100);

                _state.Press("action:cross", KeyboardKey.Space);
                await Task.Delay(100);
                bool crossHeld = _held.Contains(Keys.Space);
                _state.Release("action:cross");
                await Task.Delay(100);

                _state.ReleaseAll();
                await Task.Delay(100);
                bool released = _held.Count == 0 && _state.CurrentPressedKeys.Count == 0;
                bool messagesComplete =
                    new[] { Keys.W, Keys.D, Keys.Space }.All(key => _seenDown.Contains(key) && _seenUp.Contains(key));

                Results.Add($"MOVE right -> D: {(rightHeld ? "PASS" : "FAIL")}");
                Results.Add($"MOVE diagonal -> W+D held together: {(diagonalHeld ? "PASS" : "FAIL")}");
                Results.Add($"Cross -> Space: {(crossHeld ? "PASS" : "FAIL")}");
                Results.Add($"Stop/release -> no held keys: {(released ? "PASS" : "FAIL")}");
                Results.Add($"Windows received KeyDown/KeyUp messages: {(messagesComplete ? "PASS" : "FAIL")}");
                Results.Add($"Keyboard mode virtual-controller factory calls: {factory.CreateCalls}");
                Passed = keyboardStarted && factory.CreateCalls == 0 && rightHeld && diagonalHeld &&
                    crossHeld && released && messagesComplete;
            }
            catch (Exception ex)
            {
                Results.Add($"Smoke test exception: {ex}");
                Passed = false;
            }
            finally
            {
                _state.ReleaseAll();
                service.Stop();
                Close();
            }
        }

        private sealed class CountingFactory : IDirectDs4Factory
        {
            public int CreateCalls { get; private set; }
            public IDirectDs4Session Create()
            {
                CreateCalls++;
                throw new InvalidOperationException("ViGEm must not be created by the keyboard smoke test.");
            }
        }

        private sealed class MemoryStore : IKeyboardBindingStore
        {
            public KeyboardBindings Load() => new();
            public void Save(KeyboardBindings bindings) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _state.ReleaseAll();
            base.Dispose(disposing);
        }
    }
}
