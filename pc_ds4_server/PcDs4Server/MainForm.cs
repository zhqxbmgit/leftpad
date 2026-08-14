using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace PcDs4Server
{
    public class MainForm : Form
    {
        private readonly Ds4Service _service;
        private readonly ServerLifecycleController _lifecycle;
        private NotifyIcon? _notifyIcon;
        private ContextMenuStrip? _trayMenu;

        // 核心布局控件
        private Panel _sidebar = null!, _topBar = null!, _mainContent = null!;
        private RoundedPanel _contentPanel = null!;
        private RichTextBox _logBox = null!;
        private Label _lblStatusBadge = null!;
        private Label _joystickDebug = null!;
        private ComboBox _outputMode = null!;
        private Button _startStop = null!;
        private FlowLayoutPanel _keyboardMapping = null!;
        private readonly Dictionary<string, ComboBox> _bindingEditors = new(StringComparer.OrdinalIgnoreCase);
        private bool _initializingBindingEditors = true;
        private bool _isReallyClosing = false;
        private readonly VirtualJoystickOverlay _joystickOverlay;
        private readonly RadialMenuController _radialMenu;
        private readonly RadialMenuSettingsStore _radialSettingsStore;

        // 状态卡片
        private ModernStatusCard _cardVigem = null!, _cardDs4 = null!, _cardPhone = null!, _cardPort = null!;

        // 手柄监视器按钮状态
        private readonly Dictionary<string, bool> _btnStates = new() {
            { "triangle", false }, { "square", false }, { "cross", false }, { "circle", false }
        };

        // 用于拖动无边框窗口
        [DllImport("user32.DLL", EntryPoint = "ReleaseCapture")]
        private extern static void ReleaseCapture();
        [DllImport("user32.DLL", EntryPoint = "SendMessage")]
        private extern static void SendMessage(System.IntPtr hWnd, int wMsg, int wParam, int lParam);

        public MainForm(Ds4Service service, RadialMenuSettingsStore? radialSettingsStore = null)
        {
            _service = service;
            _lifecycle = new ServerLifecycleController(service);
            InitializeComponent();
            _radialSettingsStore = radialSettingsStore ?? new RadialMenuSettingsStore();
            RadialMenuSettingsLoadResult radialSettings = _radialSettingsStore.Load();
            _joystickOverlay = new VirtualJoystickOverlay();
            _ = _joystickOverlay.Handle;
            var radialOverlay = new RadialMenuOverlay();
            _ = radialOverlay.Handle;
            _radialMenu = new RadialMenuController(radialOverlay, radialSettings.Settings);
            _service.ConfigureVirtualJoystick(_joystickOverlay, new WindowsCursorPositionProvider());
            SetupServiceEvents();
            LogRadialSettingsLoad(radialSettings);
        }

        private void InitializeComponent()
        {
            // 基础属性：无边框现代化设计
            this.Text = "LeftPad DS4 Receiver";
            this.Size = new Size(960, 620);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ThemeColors.Background;
            this.Icon = SystemIcons.Application;

            // 1. 顶部栏 (用于拖动和关闭按钮)
            _topBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.Transparent };
            _topBar.MouseDown += (s, e) => { ReleaseCapture(); SendMessage(Handle, 0x112, 0xf012, 0); };

            var btnExit = new Button {
                Text = "✕", Size = new Size(40, 40), Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat, ForeColor = Color.Gray
            };
            btnExit.FlatAppearance.BorderSize = 0;
            btnExit.Click += (s, e) => { this.Close(); };

            var btnMin = new Button {
                Text = "—", Size = new Size(40, 40), Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat, ForeColor = Color.Gray
            };
            btnMin.FlatAppearance.BorderSize = 0;
            btnMin.Click += (s, e) => { this.WindowState = FormWindowState.Minimized; };

            _topBar.Controls.Add(btnMin);
            _topBar.Controls.Add(btnExit);

            // 2. 左侧导航栏
            _sidebar = new Panel { Dock = DockStyle.Left, Width = 200, BackColor = ThemeColors.Sidebar };

            var lblLogo = new Label {
                Text = "LeftPad\nDS4 RECEIVER",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = ThemeColors.AccentPurple,
                Location = new Point(20, 20),
                AutoSize = true
            };
            _sidebar.Controls.Add(lblLogo);

            var btnOverview = new SidebarButton { Text = "🎮 Overview", Location = new Point(0, 100), Width = 200, IsSelected = true };
            var btnGamepad = new SidebarButton { Text = "🕹 Gamepad", Location = new Point(0, 145), Width = 200 };
            var btnSettings = new SidebarButton { Text = "⚙ Settings", Location = new Point(0, 190), Width = 200 };
            var btnLog = new SidebarButton { Text = "📋 Logs", Location = new Point(0, 235), Width = 200 };

            _sidebar.Controls.AddRange(new Control[] { btnOverview, btnGamepad, btnSettings, btnLog });
            btnSettings.Click += (_, _) => OpenRadialMenuSettings();

            // 3. 右侧主内容区
            _mainContent = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 0, 20, 20) };

            _contentPanel = new RoundedPanel {
                Dock = DockStyle.Fill,
                BackColor = ThemeColors.ContentPanel,
                Radius = 20,
                Padding = new Padding(25)
            };

            // 4. 内容区头部
            var header = new Panel { Dock = DockStyle.Top, Height = 60 };
            var lblPageTitle = new Label {
                Text = "Control Center", Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = ThemeColors.TextMain, Location = new Point(0, 5), AutoSize = true
            };
            _lblStatusBadge = new Label {
                Text = "WAITING", TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(110, 28), Location = new Point(580, 8),
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                BackColor = Color.FromArgb(40, 40, 0), ForeColor = ThemeColors.Warning
            };
            header.Controls.Add(lblPageTitle);
            header.Controls.Add(_lblStatusBadge);
            _contentPanel.Controls.Add(header);

            // 5. 状态卡片布局
            var cardFlow = new FlowLayoutPanel {
                Dock = DockStyle.Top, Height = 100,
                FlowDirection = FlowDirection.LeftToRight
            };
            _cardVigem = new ModernStatusCard("ViGEmBus", "Checking...");
            _cardDs4 = new ModernStatusCard("Virtual DS4", "Created");
            _cardPhone = new ModernStatusCard("Phone", "Waiting");
            _cardPort = new ModernStatusCard("Port", "8888");
            _cardPort.SetStatusColor(ThemeColors.Success);
            cardFlow.Controls.AddRange(new Control[] { _cardVigem, _cardDs4, _cardPhone, _cardPort });
            _contentPanel.Controls.Add(cardFlow);

            var outputSection = new FlowLayoutPanel {
                Dock = DockStyle.Top, Height = 82, FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false, Padding = new Padding(0, 8, 0, 4)
            };
            outputSection.Controls.Add(new Label {
                Text = "Output Mode", AutoSize = false, Width = 90, Height = 28,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = ThemeColors.TextSecondary
            });
            _outputMode = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDownList, Width = 125,
                DataSource = new[] { OutputMode.DirectDs4, OutputMode.Keyboard }
            };
            _outputMode.Format += (_, e) => e.Value = (OutputMode)e.ListItem! == OutputMode.DirectDs4
                ? "Direct DS4" : "Keyboard";
            _outputMode.SelectedValueChanged += (_, _) => ApplySelectedOutputMode();
            outputSection.Controls.Add(_outputMode);
            _startStop = new Button {
                Text = "Start", Width = 80, Height = 28,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                BackColor = ThemeColors.ControlDark,
                ForeColor = ThemeColors.TextMain
            };
            _startStop.FlatAppearance.BorderColor = ThemeColors.BorderPurple;
            _startStop.Click += (_, _) => ToggleServer();
            outputSection.Controls.Add(_startStop);
            _keyboardMapping = new FlowLayoutPanel {
                Width = 385, Height = 66, AutoScroll = true, WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight
            };
            foreach (string action in KeyboardBindings.ProtocolActions)
            {
                var label = new Label {
                    Text = action.ToUpperInvariant(), Width = 58, Height = 25,
                    TextAlign = ContentAlignment.MiddleRight,
                    ForeColor = ThemeColors.TextSecondary
                };
                var editor = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 };
                editor.DataSource = Enum.GetValues<KeyboardKey>();
                editor.SelectedItem = _service.KeyboardBindings.Get(action);
                editor.SelectedValueChanged += (_, _) => SaveKeyboardMappings();
                _bindingEditors[action] = editor;
                _keyboardMapping.Controls.Add(label);
                _keyboardMapping.Controls.Add(editor);
            }
            _initializingBindingEditors = false;
            outputSection.Controls.Add(_keyboardMapping);
            _contentPanel.Controls.Add(outputSection);

            // 6. 手柄监控区 (绘制在 Panel 上)
            var monitorSection = new Panel { Dock = DockStyle.Top, Height = 130, Padding = new Padding(0, 10, 0, 0) };
            var lblMonitorTitle = new Label {
                Text = "Gamepad Input Monitor", Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = ThemeColors.TextSecondary, Dock = DockStyle.Top
            };
            var monitorCanvas = new Panel { Dock = DockStyle.Fill };
            monitorCanvas.Paint += (s, e) => DrawGamepadMonitor(e.Graphics, monitorCanvas.Width, monitorCanvas.Height);
            monitorSection.Controls.Add(monitorCanvas);
            monitorSection.Controls.Add(lblMonitorTitle);
            _contentPanel.Controls.Add(monitorSection);

            var joystickDebugSection = new Panel { Dock = DockStyle.Top, Height = 148, Padding = new Padding(0, 8, 0, 4) };
            var lblJoystickTitle = new Label {
                Text = "Visible Virtual Joystick", Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = ThemeColors.TextSecondary, Dock = DockStyle.Top, Height = 20
            };
            _joystickDebug = new Label {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 8),
                ForeColor = ThemeColors.TextSecondary,
                Text = "MOVE: Released    Mode: Stopped    Cursor sampling: Inactive\nDirection Captured This Hold: No    Movement Locked: No\nCurrent Direction: 0.000 / 0.000    Locked Direction: 0.000 / 0.000\nStick: 0.000 / 0.000    DS4: 128 / 128\nCenter: - / -    Current Cursor: - / -\nCursor Delta: 0.0 / 0.0    Cursor Distance: 0.0"
            };
            joystickDebugSection.Controls.Add(_joystickDebug);
            joystickDebugSection.Controls.Add(lblJoystickTitle);
            _contentPanel.Controls.Add(joystickDebugSection);

            // 7. 日志区
            var logSection = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 15, 0, 0) };
            var lblLogTitle = new Label {
                Text = "Live Feed", Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = ThemeColors.TextSecondary, Dock = DockStyle.Top
            };
            _logBox = new RichTextBox {
                Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 50),
                ForeColor = ThemeColors.TextSecondary, BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 8), ReadOnly = true
            };
            logSection.Controls.Add(_logBox);
            logSection.Controls.Add(lblLogTitle);
            _contentPanel.Controls.Add(logSection);

            _mainContent.Controls.Add(_contentPanel);
            this.Controls.Add(_mainContent);
            this.Controls.Add(_sidebar);
            this.Controls.Add(_topBar);

            // 托盘菜单
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Show Dashboard", null, (s, e) => ShowMainForm());
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Exit Receiver", null, (s, e) => ExitProgram());

            _notifyIcon = new NotifyIcon {
                Icon = this.Icon, ContextMenuStrip = _trayMenu,
                Text = "LeftPad DS4 Receiver", Visible = true
            };
            _notifyIcon.DoubleClick += (s, e) => ShowMainForm();
            this.FormClosing += MainForm_FormClosing;
        }

        private void DrawGamepadMonitor(Graphics g, int w, int h)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int radius = 42, gap = 70;
            int startX = (w - (4 * radius + 3 * gap)) / 2;
            int y = 25;

            string[] keys = { "triangle", "square", "cross", "circle" };
            string[] labels = { "△", "▢", "✖", "○" };
            Color[] colors = { ThemeColors.Success, Color.HotPink, Color.DeepSkyBlue, Color.OrangeRed };

            for (int i = 0; i < 4; i++)
            {
                bool active = _btnStates[keys[i]];
                var rect = new Rectangle(startX + i * (radius + gap), y, radius, radius);

                // 绘制按钮背景 (按下时带发光感)
                using (var brush = new SolidBrush(active ? colors[i] : Color.FromArgb(35, 38, 80)))
                {
                    g.FillEllipse(brush, rect);
                }

                // 绘制描边
                using (var pen = new Pen(active ? colors[i] : Color.FromArgb(60, 65, 120), 2))
                {
                    g.DrawEllipse(pen, rect);
                }

                // 绘制符号
                using (var font = new Font("Segoe UI", 12, FontStyle.Bold))
                {
                    var size = g.MeasureString(labels[i], font);
                    g.DrawString(labels[i], font, active ? Brushes.White : Brushes.DimGray,
                        rect.X + (radius - size.Width) / 2, rect.Y + (radius - size.Height) / 2);
                }
            }
        }

        private void SetupServiceEvents()
        {
            _service.OnLog += msg => AppendLog(msg);
            _service.OnStatusChanged += status => PostToUi(() => {
                bool ready = status.Contains("Connected", StringComparison.OrdinalIgnoreCase) ||
                    status.Contains("Ready", StringComparison.OrdinalIgnoreCase);
                _cardVigem.Value = ready ? "Ready" : "Error";
                _cardVigem.SetStatusColor(ready ? ThemeColors.Success : ThemeColors.Error);
            });
            _service.OnConnectionChanged += conn => PostToUi(() => {
                bool connected = conn.StartsWith("Connected:", StringComparison.OrdinalIgnoreCase);
                _cardPhone.Value = connected ? "Active" : "Waiting";
                _cardPhone.SetStatusColor(connected ? ThemeColors.Success : ThemeColors.Warning);

                _lblStatusBadge.Text = connected ? "CONNECTED" : "WAITING";
                _lblStatusBadge.ForeColor = connected ? ThemeColors.Success : ThemeColors.Warning;
                _lblStatusBadge.BackColor = connected ? Color.FromArgb(0, 50, 20) : Color.FromArgb(40, 35, 0);
            });
            _service.OnButtonEvent += btn => PostToUi(() => {
                foreach(var key in _btnStates.Keys) {
                    if (btn.StartsWith(key)) {
                        _btnStates[key] = btn.EndsWith("down");
                        break;
                    }
                }
                _contentPanel.Refresh(); // 强制重绘手柄状态
            });
            _service.OnJoystickStateChanged += state => PostToUi(() => {
                string center = state.Center is ScreenPoint centerPoint
                    ? $"{centerPoint.X} / {centerPoint.Y}"
                    : "- / -";
                string currentCursor = state.CurrentCursor is ScreenPoint cursorPoint
                    ? $"{cursorPoint.X} / {cursorPoint.Y}"
                    : "- / -";
                double stickMagnitude = Math.Sqrt(
                    (state.StickX * state.StickX) +
                    (state.StickY * state.StickY));
                string mode = state.JoystickActive
                    ? "Live"
                    : state.MovementLocked ? "Locked" : "Stopped";
                string sampling = state.MoveButtonPressed && state.JoystickActive
                    ? "Active"
                    : "Inactive";
                _joystickDebug.Text =
                    $"MOVE: {(state.MoveButtonPressed ? "Held" : "Released")}    Mode: {mode}    Cursor sampling: {sampling}\n" +
                    $"Direction Captured This Hold: {(state.DirectionCapturedDuringHold ? "Yes" : "No")}    Movement Locked: {(state.MovementLocked ? "Yes" : "No")}\n" +
                    $"Current Direction: {state.CurrentDirectionX:F3} / {state.CurrentDirectionY:F3}    Locked Direction: {state.LockedDirectionX:F3} / {state.LockedDirectionY:F3}\n" +
                    $"Stick: {state.StickX:F3} / {state.StickY:F3}    Magnitude: {stickMagnitude:F3}    DS4: {state.Ds4X} / {state.Ds4Y}\n" +
                    $"Center: {center}    Current Cursor: {currentCursor}\n" +
                    $"Cursor Delta: {state.CursorDeltaX:F1} / {state.CursorDeltaY:F1}    Cursor Distance: {state.CursorDistance:F1}";
            });
            _service.RadialMenuTriggered += () =>
                PostToUi(() => _radialMenu.ToggleAt(Cursor.Position));
            _service.OnStopped += () => PostToUi(_radialMenu.Close);
        }

        private void AppendLog(string message)
        {
            if (this.IsDisposed || _logBox == null) return;
            PostToUi(() => {
                if (_logBox.Lines.Length > 300) _logBox.Clear();
                _logBox.AppendText(message + Environment.NewLine);
                _logBox.ScrollToCaret();
            });
        }

        private void OpenRadialMenuSettings()
        {
            using var settingsForm = new RadialMenuSettingsForm(
                _radialMenu,
                _radialSettingsStore,
                LogRadialMessage);
            settingsForm.ShowDialog(this);
        }

        private void LogRadialSettingsLoad(RadialMenuSettingsLoadResult result)
        {
            string message = result.Status switch
            {
                RadialMenuSettingsLoadStatus.Loaded => "[RADIAL] Settings loaded",
                RadialMenuSettingsLoadStatus.Missing => "[RADIAL] Using default settings",
                _ => "[RADIAL] Settings load failed; using defaults"
            };
            LogRadialMessage(message);
        }

        private void LogRadialMessage(string message) =>
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {message}");

        private void ApplySelectedOutputMode()
        {
            if (_outputMode.SelectedItem is not OutputMode selected) return;
            if (!_service.TrySetOutputMode(selected))
            {
                _outputMode.SelectedItem = _service.OutputMode;
                return;
            }
            UpdateOutputControls();
        }

        private void SaveKeyboardMappings()
        {
            if (_initializingBindingEditors || _service.IsRunning ||
                _bindingEditors.Count != KeyboardBindings.ProtocolActions.Count) return;
            var bindings = _service.KeyboardBindings;
            foreach ((string action, ComboBox editor) in _bindingEditors)
            {
                if (editor.SelectedItem is KeyboardKey key) bindings.Set(action, key);
            }
            _service.TryUpdateKeyboardBindings(bindings);
        }

        private void ToggleServer()
        {
            if (_service.IsRunning)
            {
                _service.Stop();
                UpdateOutputControls();
                return;
            }

            StartServer(showErrorDialog: true);
        }

        private bool StartServer(bool showErrorDialog)
        {
            ServerStartResult result = _lifecycle.TryStart();
            UpdateOutputControls();
            if (result.Succeeded) return true;

            ShowStartFailure(result, showErrorDialog);
            return false;
        }

        private void AutoStartDirectDs4Once()
        {
            ServerStartResult result = _lifecycle.TryAutoStartDirectDs4();
            _outputMode.SelectedItem = _service.OutputMode;
            UpdateOutputControls();
            if (!result.Succeeded && result.Status != ServerStartStatus.AutoStartAlreadyAttempted)
            {
                ShowStartFailure(result, showErrorDialog: false);
            }
        }

        private void ShowStartFailure(ServerStartResult result, bool showErrorDialog)
        {
            string detail = result.Exception?.Message ?? result.Status.ToString();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] Output start failed: {detail}");
            _cardVigem.Value = "Error";
            _cardDs4.Value = "Not created";
            _cardVigem.SetStatusColor(ThemeColors.Error);
            _cardDs4.SetStatusColor(ThemeColors.Error);
            if (showErrorDialog)
            {
                MessageBox.Show($"The selected output could not be started.\n\n{detail}",
                    "Output Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateOutputControls()
        {
            bool stopped = !_service.IsRunning;
            _outputMode.Enabled = stopped;
            bool mappingsEditable = ShouldEnableKeyboardMappings(_service.OutputMode, isRunning: !stopped);
            foreach (ComboBox editor in _bindingEditors.Values)
            {
                editor.Enabled = mappingsEditable;
            }
            _startStop.Text = stopped ? "Start" : "Stop";
            if (_service.OutputMode == OutputMode.Keyboard)
            {
                _cardVigem.Value = "Disabled";
                _cardDs4.Value = "Disabled";
                _cardVigem.SetStatusColor(ThemeColors.TextSecondary);
                _cardDs4.SetStatusColor(ThemeColors.TextSecondary);
            }
            else if (stopped)
            {
                _cardVigem.Value = "Not started";
                _cardDs4.Value = "Not created";
                _cardVigem.SetStatusColor(ThemeColors.Warning);
                _cardDs4.SetStatusColor(ThemeColors.Warning);
            }
            else
            {
                _cardVigem.Value = "Ready";
                _cardDs4.Value = "Connected";
                _cardVigem.SetStatusColor(ThemeColors.Success);
                _cardDs4.SetStatusColor(ThemeColors.Success);
            }
        }

        public static bool ShouldEnableKeyboardMappings(OutputMode outputMode, bool isRunning)
        {
            return outputMode == OutputMode.Keyboard && !isRunning;
        }

        private void PostToUi(Action action)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }

        private void ShowMainForm() { this.Show(); this.WindowState = FormWindowState.Normal; this.BringToFront(); }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (!_isReallyClosing && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                _notifyIcon?.ShowBalloonTip(2000, "LeftPad Receiver", "Still running in tray", ToolTipIcon.Info);
            }
        }

        private void ExitProgram()
        {
            var result = MessageBox.Show("确定要退出接收端吗？\n退出后手机手柄将立即断开连接。", "Confirm Exit", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes) {
                _isReallyClosing = true;
                _service.Stop();
                if (_notifyIcon != null) { _notifyIcon.Visible = false; _notifyIcon.Dispose(); }
                Application.Exit();
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _outputMode.SelectedItem = _service.OutputMode;
            UpdateOutputControls();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            BeginInvoke(AutoStartDirectDs4Once);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _service.ResetVirtualJoystick(JoystickResetReason.NormalExit);
            _radialMenu.Dispose();
            _joystickOverlay.Dispose();
            base.OnFormClosed(e);
        }
    }
}
