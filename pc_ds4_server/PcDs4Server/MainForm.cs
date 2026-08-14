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
            _service.SetRadialDoubleTapWindow(radialSettings.Settings.DoubleTapWindowMs);
            _service.ConfigureVirtualJoystick(_joystickOverlay, new WindowsCursorPositionProvider());
            SetupServiceEvents();
            LogRadialSettingsLoad(radialSettings);
        }

        private void InitializeComponent()
        {
            // 基础属性：无边框现代化设计
            this.Text = "LeftPad DS4 接收器";
            this.Size = new Size(960, 620);
            this.Font = new Font("Microsoft YaHei UI", 9f);
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
                Text = "LeftPad\nDS4 接收器",
                Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold),
                ForeColor = ThemeColors.AccentPurple,
                Location = new Point(20, 20),
                AutoSize = true
            };
            _sidebar.Controls.Add(lblLogo);

            var btnOverview = new SidebarButton { Text = "🎮 总览", Location = new Point(0, 100), Width = 200, IsSelected = true };
            var btnGamepad = new SidebarButton { Text = "🕹 手柄状态", Location = new Point(0, 145), Width = 200 };
            var btnSettings = new SidebarButton { Text = "⚙ 设置", Location = new Point(0, 190), Width = 200 };
            var btnLog = new SidebarButton { Text = "📋 日志", Location = new Point(0, 235), Width = 200 };

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
                Text = "控制中心", Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold),
                ForeColor = ThemeColors.TextMain, Location = new Point(0, 5), AutoSize = true
            };
            _lblStatusBadge = new Label {
                Text = "等待连接", TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(110, 28), Location = new Point(580, 8),
                Font = new Font("Microsoft YaHei UI", 8, FontStyle.Bold),
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
            _cardVigem = new ModernStatusCard("ViGEmBus", "检查中...");
            _cardDs4 = new ModernStatusCard("虚拟 DS4", "未启动");
            _cardPhone = new ModernStatusCard("手机", "等待连接");
            _cardPort = new ModernStatusCard("端口", "8888");
            _cardPort.SetStatusColor(ThemeColors.Success);
            cardFlow.Controls.AddRange(new Control[] { _cardVigem, _cardDs4, _cardPhone, _cardPort });
            _contentPanel.Controls.Add(cardFlow);

            var outputSection = new FlowLayoutPanel {
                Dock = DockStyle.Top, Height = 82, FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false, Padding = new Padding(0, 8, 0, 4)
            };
            outputSection.Controls.Add(new Label {
                Text = "输出模式", AutoSize = false, Width = 90, Height = 28,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = ThemeColors.TextSecondary
            });
            _outputMode = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDownList, Width = 125,
                DataSource = new[] { OutputMode.DirectDs4, OutputMode.Keyboard }
            };
            _outputMode.Format += (_, e) => e.Value = (OutputMode)e.ListItem! == OutputMode.DirectDs4
                ? "Direct DS4" : "键盘";
            _outputMode.SelectedValueChanged += (_, _) => ApplySelectedOutputMode();
            outputSection.Controls.Add(_outputMode);
            _startStop = new Button {
                Text = "启动", Width = 80, Height = 28,
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
                Text = "手柄输入监视", Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
                ForeColor = ThemeColors.TextSecondary, Dock = DockStyle.Top
            };
            var monitorCanvas = new Panel { Dock = DockStyle.Fill };
            monitorCanvas.Paint += (s, e) => DrawGamepadMonitor(e.Graphics, monitorCanvas.Width, monitorCanvas.Height);
            monitorSection.Controls.Add(monitorCanvas);
            monitorSection.Controls.Add(lblMonitorTitle);
            _contentPanel.Controls.Add(monitorSection);

            var joystickDebugSection = new Panel { Dock = DockStyle.Top, Height = 148, Padding = new Padding(0, 8, 0, 4) };
            var lblJoystickTitle = new Label {
                Text = "可视虚拟摇杆", Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
                ForeColor = ThemeColors.TextSecondary, Dock = DockStyle.Top, Height = 20
            };
            _joystickDebug = new Label {
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 8),
                ForeColor = ThemeColors.TextSecondary,
                Text = "MOVE：已释放    模式：已停止    光标采样：未启用\n本次按住已捕获方向：否    移动锁定：否\n当前方向：0.000 / 0.000    锁定方向：0.000 / 0.000\n摇杆：0.000 / 0.000    DS4：128 / 128\n中心：- / -    当前光标：- / -\n光标偏移：0.0 / 0.0    光标距离：0.0"
            };
            joystickDebugSection.Controls.Add(_joystickDebug);
            joystickDebugSection.Controls.Add(lblJoystickTitle);
            _contentPanel.Controls.Add(joystickDebugSection);

            // 7. 日志区
            var logSection = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 15, 0, 0) };
            var lblLogTitle = new Label {
                Text = "实时日志", Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
                ForeColor = ThemeColors.TextSecondary, Dock = DockStyle.Top
            };
            _logBox = new RichTextBox {
                Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 50),
                ForeColor = ThemeColors.TextSecondary, BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 8), ReadOnly = true
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
            _trayMenu.Items.Add("显示窗口", null, (s, e) => ShowMainForm());
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("退出", null, (s, e) => ExitProgram());

            _notifyIcon = new NotifyIcon {
                Icon = this.Icon, ContextMenuStrip = _trayMenu,
                Text = "LeftPad DS4 接收器", Visible = true
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
                bool ready = status.Contains("已连接", StringComparison.Ordinal) ||
                    status.Contains("就绪", StringComparison.Ordinal);
                _cardVigem.Value = ready ? "就绪" : "错误";
                _cardVigem.SetStatusColor(ready ? ThemeColors.Success : ThemeColors.Error);
            });
            _service.OnConnectionChanged += conn => PostToUi(() => {
                bool connected = conn.StartsWith("已连接：", StringComparison.Ordinal);
                _cardPhone.Value = connected ? "已连接" : "等待连接";
                _cardPhone.SetStatusColor(connected ? ThemeColors.Success : ThemeColors.Warning);

                _lblStatusBadge.Text = connected ? "已连接" : "等待连接";
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
                    ? "实时"
                    : state.MovementLocked ? "已锁定" : "已停止";
                string sampling = state.MoveButtonPressed && state.JoystickActive
                    ? "已启用"
                    : "未启用";
                _joystickDebug.Text =
                    $"MOVE：{(state.MoveButtonPressed ? "按住" : "已释放")}    模式：{mode}    光标采样：{sampling}\n" +
                    $"本次按住已捕获方向：{(state.DirectionCapturedDuringHold ? "是" : "否")}    移动锁定：{(state.MovementLocked ? "是" : "否")}\n" +
                    $"当前方向：{state.CurrentDirectionX:F3} / {state.CurrentDirectionY:F3}    锁定方向：{state.LockedDirectionX:F3} / {state.LockedDirectionY:F3}\n" +
                    $"摇杆：{state.StickX:F3} / {state.StickY:F3}    幅度：{stickMagnitude:F3}    DS4：{state.Ds4X} / {state.Ds4Y}\n" +
                    $"中心：{center}    当前光标：{currentCursor}\n" +
                    $"光标偏移：{state.CursorDeltaX:F1} / {state.CursorDeltaY:F1}    光标距离：{state.CursorDistance:F1}";
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
                ApplyRadialMenuSettings,
                LogRadialMessage);
            settingsForm.ShowDialog(this);
        }

        private void ApplyRadialMenuSettings(RadialMenuSettings settings)
        {
            _radialMenu.ApplySettings(settings);
            _service.SetRadialDoubleTapWindow(settings.DoubleTapWindowMs);
        }

        private void LogRadialSettingsLoad(RadialMenuSettingsLoadResult result)
        {
            string message = result.Status switch
            {
                RadialMenuSettingsLoadStatus.Loaded => "[环形菜单] 设置已加载",
                RadialMenuSettingsLoadStatus.Missing => "[环形菜单] 正在使用默认设置",
                _ => "[环形菜单] 设置加载失败，已恢复默认设置"
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
            string detail = result.Exception?.Message ?? DescribeStartStatus(result.Status);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 输出启动失败：{detail}");
            _cardVigem.Value = "错误";
            _cardDs4.Value = "未创建";
            _cardVigem.SetStatusColor(ThemeColors.Error);
            _cardDs4.SetStatusColor(ThemeColors.Error);
            if (showErrorDialog)
            {
                MessageBox.Show($"无法启动所选输出模式。\n\n{detail}",
                    "输出错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string DescribeStartStatus(ServerStartStatus status) => status switch
        {
            ServerStartStatus.InitializationFailed => "输出初始化失败",
            ServerStartStatus.StartFailed => "服务启动失败",
            ServerStartStatus.DirectDs4SelectionFailed => "无法选择 Direct DS4 输出",
            ServerStartStatus.AutoStartAlreadyAttempted => "已尝试自动启动",
            ServerStartStatus.AlreadyRunning => "服务已在运行",
            _ => "未知启动错误"
        };

        private void UpdateOutputControls()
        {
            bool stopped = !_service.IsRunning;
            _outputMode.Enabled = stopped;
            bool mappingsEditable = ShouldEnableKeyboardMappings(_service.OutputMode, isRunning: !stopped);
            foreach (ComboBox editor in _bindingEditors.Values)
            {
                editor.Enabled = mappingsEditable;
            }
            _startStop.Text = stopped ? "启动" : "停止";
            if (_service.OutputMode == OutputMode.Keyboard)
            {
                _cardVigem.Value = "已禁用";
                _cardDs4.Value = "已禁用";
                _cardVigem.SetStatusColor(ThemeColors.TextSecondary);
                _cardDs4.SetStatusColor(ThemeColors.TextSecondary);
            }
            else if (stopped)
            {
                _cardVigem.Value = "未启动";
                _cardDs4.Value = "未创建";
                _cardVigem.SetStatusColor(ThemeColors.Warning);
                _cardDs4.SetStatusColor(ThemeColors.Warning);
            }
            else
            {
                _cardVigem.Value = "就绪";
                _cardDs4.Value = "已连接";
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
                _notifyIcon?.ShowBalloonTip(2000, "LeftPad DS4 接收器", "程序已最小化到系统托盘，并仍在后台运行。", ToolTipIcon.Info);
            }
        }

        private void ExitProgram()
        {
            var result = MessageBox.Show("确定要退出接收器吗？\n退出后手机手柄将断开连接。", "确认退出", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
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
