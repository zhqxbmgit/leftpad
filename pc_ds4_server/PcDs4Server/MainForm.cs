using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace PcDs4Server
{
    public partial class MainForm : Form
    {
        internal static string GetOutputModeDisplayText(OutputMode mode) => mode switch
        {
            OutputMode.DirectDs4 => "Direct DS4",
            OutputMode.Keyboard => "键盘",
            _ => mode.ToString()
        };

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
        private TableLayoutPanel _keyboardMapping = null!;
        private readonly Dictionary<string, ComboBox> _bindingEditors = new(StringComparer.OrdinalIgnoreCase);
        private bool _initializingBindingEditors = true;
        private bool _isReallyClosing = false;
        private readonly VirtualJoystickOverlay _joystickOverlay;
        private readonly RadialMenuController _radialMenu;
        private readonly RadialMenuSettingsStore _radialSettingsStore;
        private readonly RadialVisualPackCatalog _radialVisualPackCatalog;
        private readonly RadialMenuOverlay _radialOverlay;
        private readonly System.Windows.Forms.Timer _radialSelectionTimer;
        private readonly ReceiverUiScaling _receiverUiScaling;
        private readonly Dictionary<ReceiverPage, SidebarButton> _pageNavigation = new();
        private Label _pageTitle = null!;
        private TableLayoutPanel _overviewCards = null!, _overviewOutput = null!;
        private Panel _gamepadMonitor = null!, _joystickDebugSection = null!, _logSection = null!;
        private RadialMenuSettingsControl _settingsPage = null!;
        private ReceiverPage _currentPage = ReceiverPage.Overview;

        private enum ReceiverPage
        {
            Overview,
            Gamepad,
            Settings,
            Log
        }

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

        private const int WmNcHitTest = 0x0084;
        private const int HtClient = 1;
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;
        internal const int ActivateExistingInstanceMessage = 0x8000 + 0x51;

        public MainForm(Ds4Service service, RadialMenuSettingsStore? radialSettingsStore = null)
        {
            _service = service;
            _lifecycle = new ServerLifecycleController(service);
            _radialSettingsStore = radialSettingsStore ?? new RadialMenuSettingsStore();
            RadialMenuSettingsLoadResult radialSettings = _radialSettingsStore.Load();
            InitializeComponent();
            _receiverUiScaling = new ReceiverUiScaling(this);
            _receiverUiScaling.Apply(radialSettings.Settings.ReceiverUiScalePercent);
            InitializeDreamscapeOverviewFeature();
            _joystickOverlay = new VirtualJoystickOverlay();
            _ = _joystickOverlay.Handle;
            _radialVisualPackCatalog = new RadialVisualPackCatalog();
            _radialOverlay = new RadialMenuOverlay(
                _radialVisualPackCatalog,
                LogRadialMessage);
            _radialOverlay.PrepareVisualPack(radialSettings.Settings);
            _ = _radialOverlay.Handle;
            _radialMenu = new RadialMenuController(_radialOverlay, radialSettings.Settings);
            _settingsPage.AttachReceiverUiScaling(_receiverUiScaling);
            _settingsPage.Initialize(
                _radialMenu,
                _radialSettingsStore,
                ApplyRadialMenuSettings,
                LogRadialMessage,
                _radialVisualPackCatalog);
            InitializeDreamscapeSettingsFeature();
            InitializeDreamscapeControllerFeature();
            InitializeDreamscapeLogsFeature();
            _radialSelectionTimer = new System.Windows.Forms.Timer
            {
                Interval = radialSettings.Settings.SelectionPollIntervalMs
            };
            _radialSelectionTimer.Tick += RadialSelectionTimer_Tick;
            _radialMenu.NormalMenuStateChanged += HandleRadialMenuStateChanged;
            _service.SetRadialDoubleTapWindow(radialSettings.Settings.DoubleTapWindowMs);
            _service.ConfigureVirtualJoystick(_joystickOverlay, new WindowsCursorPositionProvider());
            SetupServiceEvents();
            LogKeyboardBindingLoad(_service.KeyboardBindingLoadResult);
            LogRadialSettingsLoad(radialSettings);
        }

        private void InitializeComponent()
        {
            // 基础属性：无边框现代化设计
            this.Text = "LeftPad DS4 接收器";
            this.Size = ReceiverUiLayoutMetrics.MainClientBaseline;
            this.Font = new Font("Microsoft YaHei UI", 9f);
            // PMV2 owns monitor-DPI handling. ReceiverUiScaling adds only the user
            // preference, so WinForms must not apply a second font/DPI multiplier.
            this.AutoScaleMode = AutoScaleMode.None;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ThemeColors.Background;
            this.Icon = SystemIcons.Application;

            // 1. 顶部栏 (用于拖动和关闭按钮)
            _topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = ReceiverUiLayoutMetrics.MainTopBarHeight,
                BackColor = Color.Transparent
            };
            _topBar.MouseDown += (s, e) => { ReleaseCapture(); SendMessage(Handle, 0x112, 0xf012, 0); };

            var btnExit = new Button {
                Text = "✕", Size = new Size(44, 44), Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat, ForeColor = Color.Gray
            };
            btnExit.FlatAppearance.BorderSize = 0;
            btnExit.Click += (s, e) => { this.Close(); };

            var btnMin = new Button {
                Text = "—", Size = new Size(44, 44), Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat, ForeColor = Color.Gray
            };
            btnMin.FlatAppearance.BorderSize = 0;
            btnMin.Click += (s, e) => { this.WindowState = FormWindowState.Minimized; };

            _topBar.Controls.Add(btnMin);
            _topBar.Controls.Add(btnExit);

            // 2. 左侧导航栏
            _sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = ReceiverUiLayoutMetrics.MainSidebarWidth,
                BackColor = ThemeColors.Sidebar
            };

            var lblLogo = new Label {
                Text = "LeftPad\nDS4 接收器",
                Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold),
                ForeColor = ThemeColors.AccentPurple,
                Location = new Point(24, 24),
                AutoSize = true
            };
            _sidebar.Controls.Add(lblLogo);

            var btnOverview = new SidebarButton { Name = "overviewNavigation", Text = "🎮 总览", Location = new Point(0, 112), Width = 210, IsSelected = true };
            var btnGamepad = new SidebarButton { Name = "gamepadNavigation", Text = "🕹 手柄状态", Location = new Point(0, 164), Width = 210 };
            var btnSettings = new SidebarButton { Name = "settingsNavigation", Text = "⚙ 设置", Location = new Point(0, 216), Width = 210 };
            var btnLog = new SidebarButton { Name = "logNavigation", Text = "📋 日志", Location = new Point(0, 268), Width = 210 };

            _sidebar.Controls.AddRange(new Control[] { btnOverview, btnGamepad, btnSettings, btnLog });
            _pageNavigation.Add(ReceiverPage.Overview, btnOverview);
            _pageNavigation.Add(ReceiverPage.Gamepad, btnGamepad);
            _pageNavigation.Add(ReceiverPage.Settings, btnSettings);
            _pageNavigation.Add(ReceiverPage.Log, btnLog);

            // 3. 右侧主内容区
            _mainContent = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(
                    ReceiverUiLayoutMetrics.MainHorizontalPadding,
                    0,
                    ReceiverUiLayoutMetrics.MainHorizontalPadding,
                    ReceiverUiLayoutMetrics.MainBottomPadding)
            };

            _contentPanel = new RoundedPanel {
                Dock = DockStyle.Fill,
                BackColor = ThemeColors.ContentPanel,
                Radius = 20,
                Padding = new Padding(ReceiverUiLayoutMetrics.MainContentPadding)
            };

            // 4. 内容区头部
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = ReceiverUiLayoutMetrics.MainHeaderHeight,
                ColumnCount = 2,
                Padding = new Padding(0, 8, 0, 12)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            _pageTitle = new Label {
                Text = "控制中心", Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold),
                ForeColor = ThemeColors.TextMain, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _lblStatusBadge = new Label {
                Text = "等待连接", TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill, Margin = new Padding(12, 8, 0, 8),
                Font = new Font("Microsoft YaHei UI", 8, FontStyle.Bold),
                BackColor = Color.FromArgb(40, 40, 0), ForeColor = ThemeColors.Warning
            };
            header.Controls.Add(_pageTitle, 0, 0);
            header.Controls.Add(_lblStatusBadge, 1, 0);
            _contentPanel.Controls.Add(header);

            // 5. 状态卡片布局
            _overviewCards = new TableLayoutPanel {
                Name = "overviewCardsLayout",
                Dock = DockStyle.Top,
                Height = ReceiverUiLayoutMetrics.MainStatusCardsHeight,
                ColumnCount = ReceiverUiLayoutMetrics.StatusCardCount,
                RowCount = 1,
                Padding = new Padding(0, 6, 0, 18)
            };
            for (int index = 0; index < ReceiverUiLayoutMetrics.StatusCardCount; index++)
                _overviewCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            _cardVigem = new ModernStatusCard("ViGEmBus", "检查中...");
            _cardDs4 = new ModernStatusCard("虚拟 DS4", "未启动");
            _cardPhone = new ModernStatusCard("手机", "等待连接");
            _cardPort = new ModernStatusCard("端口", "8888");
            ModernStatusCard[] statusCards = { _cardVigem, _cardDs4, _cardPhone, _cardPort };
            for (int index = 0; index < statusCards.Length; index++)
            {
                statusCards[index].Name = $"statusCard{index + 1}";
                statusCards[index].Dock = DockStyle.Fill;
                statusCards[index].Margin = new Padding(
                    0,
                    0,
                    index == statusCards.Length - 1 ? 0 : ReceiverUiLayoutMetrics.StatusCardGap,
                    0);
                _overviewCards.Controls.Add(statusCards[index], index, 0);
            }
            _cardPort.SetStatusColor(ThemeColors.Success);
            _contentPanel.Controls.Add(_overviewCards);

            _overviewOutput = new TableLayoutPanel {
                Name = "overviewOutputLayout",
                Dock = DockStyle.Top,
                Height = ReceiverUiLayoutMetrics.MainOutputAreaHeight,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(0, 12, 0, 18)
            };
            _overviewOutput.ColumnStyles.Add(new ColumnStyle(
                SizeType.Absolute,
                ReceiverUiLayoutMetrics.MainOutputControlsWidth));
            _overviewOutput.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var outputControls = new FlowLayoutPanel
            {
                Name = "outputControlsGroup",
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 12, 12, 0),
                Margin = Padding.Empty
            };
            outputControls.Controls.Add(new Label {
                Name = "outputModeLabel",
                Text = "输出模式", AutoSize = false, Width = 100, Height = 32,
                Margin = new Padding(0, 0, 8, 0),
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = ThemeColors.TextSecondary
            });
            _outputMode = new ComboBox {
                Name = "outputModeSelector",
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = ReceiverUiLayoutMetrics.MainOutputModeComboBoxWidth,
                Margin = new Padding(0, 3, 10, 0),
                DataSource = new[] { OutputMode.DirectDs4, OutputMode.Keyboard }
            };
            _outputMode.Format += (_, e) => e.Value =
                GetOutputModeDisplayText((OutputMode)e.ListItem!);
            _outputMode.SelectedValueChanged += (_, _) => ApplySelectedOutputMode();
            outputControls.Controls.Add(_outputMode);
            _startStop = new Button {
                Name = "startStopButton",
                Text = "启动", Width = 86, Height = 32,
                Margin = Padding.Empty,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                BackColor = ThemeColors.ControlDark,
                ForeColor = ThemeColors.TextMain
            };
            _startStop.FlatAppearance.BorderColor = ThemeColors.BorderPurple;
            _startStop.Click += (_, _) => ToggleServer();
            outputControls.Controls.Add(_startStop);

            _keyboardMapping = new TableLayoutPanel {
                Name = "keyboardMappingGrid",
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 5,
                Margin = new Padding(18, 0, 0, 0),
                Padding = new Padding(0, 2, 0, 0)
            };
            _keyboardMapping.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
            _keyboardMapping.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            _keyboardMapping.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
            _keyboardMapping.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            for (int row = 0; row < 5; row++)
                _keyboardMapping.RowStyles.Add(new RowStyle(SizeType.Percent, 20));

            for (int index = 0; index < KeyboardBindings.ProtocolActions.Count; index++)
            {
                string action = KeyboardBindings.ProtocolActions[index];
                int row = index / 2;
                int column = (index % 2) * 2;
                var label = new Label {
                    Name = $"bindingLabel_{action}",
                    Text = action.ToUpperInvariant(), Dock = DockStyle.Fill,
                    Margin = new Padding(4, 4, 6, 4),
                    TextAlign = ContentAlignment.MiddleRight,
                    ForeColor = ThemeColors.TextSecondary
                };
                var editor = new ComboBox
                {
                    Name = $"bindingEditor_{action}",
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 4, 12, 4)
                };
                editor.DataSource = Enum.GetValues<KeyboardKey>();
                editor.SelectedItem = _service.KeyboardBindings.Get(action);
                editor.SelectedValueChanged += (_, _) => SaveKeyboardMappings();
                _bindingEditors[action] = editor;
                _keyboardMapping.Controls.Add(label, column, row);
                _keyboardMapping.Controls.Add(editor, column + 1, row);
            }
            _initializingBindingEditors = false;
            _overviewOutput.Controls.Add(outputControls, 0, 0);
            _overviewOutput.Controls.Add(_keyboardMapping, 1, 0);
            _contentPanel.Controls.Add(_overviewOutput);

            // 6. 手柄监控区 (绘制在 Panel 上)
            _gamepadMonitor = new Panel { Dock = DockStyle.Top, Height = 154, Padding = new Padding(0, 14, 0, 8) };
            var lblMonitorTitle = new Label {
                Text = "手柄输入监视", Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
                ForeColor = ThemeColors.TextSecondary, Dock = DockStyle.Top
            };
            var monitorCanvas = new Panel { Dock = DockStyle.Fill };
            monitorCanvas.Paint += (s, e) => DrawGamepadMonitor(e.Graphics, monitorCanvas.Width, monitorCanvas.Height);
            _gamepadMonitor.Controls.Add(monitorCanvas);
            _gamepadMonitor.Controls.Add(lblMonitorTitle);
            _contentPanel.Controls.Add(_gamepadMonitor);

            _joystickDebugSection = new Panel { Dock = DockStyle.Top, Height = 176, Padding = new Padding(0, 16, 0, 8) };
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
            _joystickDebugSection.Controls.Add(_joystickDebug);
            _joystickDebugSection.Controls.Add(lblJoystickTitle);
            _contentPanel.Controls.Add(_joystickDebugSection);

            // 7. 日志区
            _logSection = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 20, 0, 0) };
            var lblLogTitle = new Label {
                Text = "实时日志", Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
                ForeColor = ThemeColors.TextSecondary, Dock = DockStyle.Top
            };
            _logBox = new RichTextBox {
                Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 50),
                ForeColor = ThemeColors.TextSecondary, BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 8), ReadOnly = true
            };
            _logSection.Controls.Add(_logBox);
            _logSection.Controls.Add(lblLogTitle);
            _contentPanel.Controls.Add(_logSection);

            _settingsPage = new RadialMenuSettingsControl
            {
                Dock = DockStyle.Fill,
                Visible = false
            };
            _contentPanel.Controls.Add(_settingsPage);

            // DockStyle.Top follows z-order, not construction order. Keep the page
            // header first and make every page's sections flow downward predictably.
            _contentPanel.Controls.SetChildIndex(_settingsPage, 0);
            _contentPanel.Controls.SetChildIndex(_logSection, 1);
            _contentPanel.Controls.SetChildIndex(_joystickDebugSection, 2);
            _contentPanel.Controls.SetChildIndex(_gamepadMonitor, 3);
            _contentPanel.Controls.SetChildIndex(_overviewOutput, 4);
            _contentPanel.Controls.SetChildIndex(_overviewCards, 5);
            _contentPanel.Controls.SetChildIndex(header, 6);

            btnOverview.Click += (_, _) => NavigateTo(ReceiverPage.Overview);
            btnGamepad.Click += (_, _) => NavigateTo(ReceiverPage.Gamepad);
            btnSettings.Click += (_, _) => NavigateTo(ReceiverPage.Settings);
            btnLog.Click += (_, _) => NavigateTo(ReceiverPage.Log);
            NavigateTo(ReceiverPage.Overview);

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

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == ActivateExistingInstanceMessage)
            {
                message.Result = ActivateExistingInstance() ? (IntPtr)1 : IntPtr.Zero;
                return;
            }

            base.WndProc(ref message);
            if (message.Msg != WmNcHitTest ||
                message.Result != (IntPtr)HtClient ||
                WindowState != FormWindowState.Normal)
            {
                return;
            }

            long packedPoint = message.LParam.ToInt64();
            var screenPoint = new Point(
                unchecked((short)(packedPoint & 0xffff)),
                unchecked((short)((packedPoint >> 16) & 0xffff)));
            Point clientPoint = PointToClient(screenPoint);
            const int resizeGrip = 10;
            bool left = clientPoint.X <= resizeGrip;
            bool right = clientPoint.X >= ClientSize.Width - resizeGrip;
            bool top = clientPoint.Y <= resizeGrip;
            bool bottom = clientPoint.Y >= ClientSize.Height - resizeGrip;

            message.Result = (IntPtr)((left, right, top, bottom) switch
            {
                (true, _, true, _) => HtTopLeft,
                (_, true, true, _) => HtTopRight,
                (true, _, _, true) => HtBottomLeft,
                (_, true, _, true) => HtBottomRight,
                (true, _, _, _) => HtLeft,
                (_, true, _, _) => HtRight,
                (_, _, true, _) => HtTop,
                (_, _, _, true) => HtBottom,
                _ => HtClient
            });
        }

        private void DrawGamepadMonitor(Graphics g, int w, int h)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int radius = _receiverUiScaling.ScaleLogical(42);
            int gap = _receiverUiScaling.ScaleLogical(70);
            int startX = (w - (4 * radius + 3 * gap)) / 2;
            int y = _receiverUiScaling.ScaleLogical(25);

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
                using (var pen = new Pen(
                    active ? colors[i] : Color.FromArgb(60, 65, 120),
                    _receiverUiScaling.ScaleLogical(2)))
                {
                    g.DrawEllipse(pen, rect);
                }

                // 绘制符号
                using (var font = new Font(
                    "Segoe UI",
                    ReceiverUiScaling.Scale(12f, _receiverUiScaling.CurrentEffectiveScalePercent),
                    FontStyle.Bold))
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
                PublishDreamscapeControllerDisplayState();
                PublishDreamscapeLogsConnectionState();
            });
            _service.OnButtonEvent += btn => PostToUi(() => {
                foreach(var key in _btnStates.Keys) {
                    if (btn.StartsWith(key)) {
                        _btnStates[key] = btn.EndsWith("down");
                        break;
                    }
                }
                _contentPanel.Refresh(); // 强制重绘手柄状态
                PublishDreamscapeControllerDisplayState();
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
                PublishDreamscapeControllerJoystickState(state);
            });
            _service.OnInputStateReset += _ => PostToUi(ResetControllerPresentationState);
            _service.RadialMenuTriggered += source => PostToUi(() =>
            {
                _radialMenu.OpenAt(Cursor.Position, source);
                if (!_radialMenu.IsNormalMenuOpen) _service.NotifyRadialMenuClosed();
            });
            _service.RadialMenuConfirmationRequested += source =>
                PostToUi(() => CompleteRadialMenuSelection(source));
            _service.OnStopped += () => PostToUi(_radialMenu.Close);
        }

        private void AppendLog(string message)
        {
            if (this.IsDisposed || _logBox == null) return;
            PostToUi(() => {
                ReceiverLogMutation mutation = BufferDreamscapeLog(message);
                bool renderedFromSnapshot = false;
                if (mutation.EvictedEntry is ReceiverLogEntry evictedEntry &&
                    !_logBox.IsHandleCreated)
                {
                    _logBox.Text = NativeLogProjection.CreateText(CreateDreamscapeLogsSnapshot());
                    renderedFromSnapshot = true;
                }
                else if (mutation.EvictedEntry is ReceiverLogEntry visibleEvictedEntry)
                {
                    int removeLength = Math.Min(
                        _logBox.TextLength,
                        NativeLogProjection.GetRichTextCharacterCount(visibleEvictedEntry));
                    bool wasReadOnly = _logBox.ReadOnly;
                    try
                    {
                        _logBox.ReadOnly = false;
                        _logBox.Select(0, removeLength);
                        _logBox.SelectedText = string.Empty;
                    }
                    finally
                    {
                        _logBox.ReadOnly = wasReadOnly;
                    }
                }
                _logBox.SelectionStart = _logBox.TextLength;
                if (!renderedFromSnapshot)
                    _logBox.AppendText(NativeLogProjection.CreateEntryText(mutation.Entry));
                _logBox.ScrollToCaret();
                PublishDreamscapeLogAppend(mutation.Entry);
            });
        }

        private void ResetControllerPresentationState()
        {
            foreach (string key in _btnStates.Keys.ToArray())
                _btnStates[key] = false;
            _contentPanel.Refresh();
            PublishDreamscapeControllerDisplayState();
        }

        private void ApplyRadialMenuSettings(RadialMenuSettings settings)
        {
            _receiverUiScaling.Apply(settings.ReceiverUiScalePercent, preserveCenter: true);
            _radialOverlay.PrepareVisualPack(settings);
            _radialMenu.ApplySettings(settings);
            _service.SetRadialDoubleTapWindow(settings.DoubleTapWindowMs);
            SyncRadialSelectionTimer();
        }

        private void RadialSelectionTimer_Tick(object? sender, EventArgs e)
        {
            _radialMenu.UpdateSelectionForCursor(Cursor.Position);
        }

        private void SyncRadialSelectionTimer()
        {
            if (_radialMenu.IsNormalMenuOpen)
            {
                _radialSelectionTimer.Interval = _radialMenu.ActiveSettings.SelectionPollIntervalMs;
                _radialSelectionTimer.Start();
            }
            else
            {
                _radialSelectionTimer.Stop();
            }
        }

        private void NavigateTo(ReceiverPage page)
        {
            if (_currentPage == ReceiverPage.Settings && page != ReceiverPage.Settings)
                _settingsPage.Deactivate();
            _currentPage = page;
            _pageTitle.Text = page switch
            {
                ReceiverPage.Overview => "控制中心",
                ReceiverPage.Gamepad => "手柄状态",
                ReceiverPage.Settings => "设置",
                ReceiverPage.Log => "日志",
                _ => throw new ArgumentOutOfRangeException(nameof(page))
            };

            bool showOverview = page == ReceiverPage.Overview;
            bool showGamepad = page == ReceiverPage.Gamepad;
            _overviewCards.Visible = showOverview;
            _overviewOutput.Visible = showOverview;
            bool showWebController = SetDreamscapeControllerVisibility(showGamepad);
            _gamepadMonitor.Visible = showGamepad && !showWebController;
            _joystickDebugSection.Visible = showGamepad && !showWebController;
            bool showWebSettings = SetDreamscapeSettingsVisibility(page == ReceiverPage.Settings);
            _settingsPage.Visible = page == ReceiverPage.Settings && !showWebSettings;
            bool showWebLogs = SetDreamscapeLogsVisibility(page == ReceiverPage.Log);
            _logSection.Visible = page == ReceiverPage.Log && !showWebLogs;
            if (page == ReceiverPage.Settings && !showWebSettings && _settingsPage.IsInitialized)
                _settingsPage.RefreshFromRuntime();
            SetSelectedNavigation(_pageNavigation[page]);
            SetDreamscapeOverviewVisibility(showOverview);
        }

        internal RadialMenuSettingsControl SettingsControl => _settingsPage;

        private void SetSelectedNavigation(SidebarButton selected)
        {
            foreach (SidebarButton button in _sidebar.Controls.OfType<SidebarButton>())
            {
                button.IsSelected = ReferenceEquals(button, selected);
                button.Invalidate();
            }
        }

        private void HandleRadialMenuStateChanged()
        {
            SyncRadialSelectionTimer();
            if (!_radialMenu.IsNormalMenuOpen) _service.NotifyRadialMenuClosed();
        }

        private void CompleteRadialMenuSelection(RadialTriggerSource source)
        {
            if (!_radialMenu.TryCompleteFrom(source, out RadialMenuCompletion completion)) return;

            LogRadialMessage(RadialActionCompletionHandler.Handle(
                _service,
                _radialMenu.ActiveSettings,
                completion));
        }

        private void LogRadialSettingsLoad(RadialMenuSettingsLoadResult result)
        {
            RadialSlotMappings mappings = result.Settings.GetProfileMappings(
                _radialMenu.ActiveSettings.MappingProfileId);
            string message = result.Status switch
            {
                RadialMenuSettingsLoadStatus.Loaded =>
                    $"[环形菜单] 设置已加载（动作映射：{mappings.Count(MappingIsConfigured)}/{mappings.Count}）",
                RadialMenuSettingsLoadStatus.Missing => "[环形菜单] 正在使用默认设置",
                _ => "[环形菜单] 设置加载失败，已恢复默认设置"
            };
            LogRadialMessage(message);
        }

        private void LogKeyboardBindingLoad(KeyboardBindingLoadResult result)
        {
            if (result.Status is KeyboardBindingLoadStatus.Loaded or KeyboardBindingLoadStatus.Missing)
                return;

            AppendLog(
                $"[{DateTime.Now:HH:mm:ss}] [配置持久化] operation=load " +
                $"pathCategory={result.PathCategory} " +
                $"errorType={result.ErrorType ?? result.Status.ToString()}；" +
                $"键盘映射加载失败，已安全使用默认值：{result.Error}");
        }

        private static bool MappingIsConfigured(RadialSlotMapping mapping) =>
            mapping.Kind != RadialActionKind.None;

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
            if (!_service.TryUpdateKeyboardBindings(bindings, out string error))
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [键盘映射] 保存失败：{error}");
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

        private void ShowMainForm()
        {
            if (IsDisposed || Disposing) return;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            if (IsHandleCreated)
                WindowsForegroundActivation.TrySetForegroundWindow(Handle);
        }

        internal bool ActivateExistingInstance()
        {
            if (IsDisposed || Disposing) return false;
            ShowMainForm();
            return Visible && WindowState != FormWindowState.Minimized;
        }

        internal Task<bool> RequestExistingInstanceActivationAsync(
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested ||
                IsDisposed || Disposing || !IsHandleCreated)
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(WindowsForegroundActivation.SendActivationMessage(
                Handle,
                ActivateExistingInstanceMessage));
        }

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
            InitializeDreamscapeOverviewRuntime();
            InitializeDreamscapeSettingsRuntime();
            InitializeDreamscapeControllerRuntime();
            InitializeDreamscapeLogsRuntime();
            BeginInvoke(AutoStartDirectDs4Once);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _service.ResetVirtualJoystick(JoystickResetReason.NormalExit);
            _radialMenu.NormalMenuStateChanged -= HandleRadialMenuStateChanged;
            _radialSelectionTimer.Stop();
            _radialSelectionTimer.Dispose();
            _radialMenu.Dispose();
            _joystickOverlay.Dispose();
            _receiverUiScaling.Dispose();
            base.OnFormClosed(e);
        }
    }
}
