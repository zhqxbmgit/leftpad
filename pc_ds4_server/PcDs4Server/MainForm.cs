using System;
using System.Drawing;
using System.Windows.Forms;

namespace PcDs4Server
{
    public class MainForm : Form
    {
        private readonly Ds4Service _service;
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _trayMenu;
        private RichTextBox _logBox;
        private Label _lblIp, _lblPort, _lblVigem, _lblConnection, _lblLastKey;
        private bool _isReallyClosing = false;

        public MainForm(Ds4Service service)
        {
            _service = service;
            InitializeComponent();
            SetupServiceEvents();
        }

        private void InitializeComponent()
        {
            this.Text = "LeftPad DS4 Receiver";
            this.Size = new Size(500, 450);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.Icon = SystemIcons.Application;

            // 布局控件
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 120, ColumnCount = 2, Padding = new Padding(10) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));

            panel.Controls.Add(new Label { Text = "本机 IP:", Font = new Font(DefaultFont, FontStyle.Bold) }, 0, 0);
            _lblIp = new Label { Text = _service.LocalIp, AutoSize = true };
            panel.Controls.Add(_lblIp, 1, 0);

            panel.Controls.Add(new Label { Text = "监听端口:", Font = new Font(DefaultFont, FontStyle.Bold) }, 0, 1);
            _lblPort = new Label { Text = _service.Port.ToString(), AutoSize = true };
            panel.Controls.Add(_lblPort, 1, 1);

            panel.Controls.Add(new Label { Text = "手柄状态:", Font = new Font(DefaultFont, FontStyle.Bold) }, 0, 2);
            _lblVigem = new Label { Text = "初始化中...", AutoSize = true };
            panel.Controls.Add(_lblVigem, 1, 2);

            panel.Controls.Add(new Label { Text = "手机连接:", Font = new Font(DefaultFont, FontStyle.Bold) }, 0, 3);
            _lblConnection = new Label { Text = "等待连接", AutoSize = true };
            panel.Controls.Add(_lblConnection, 1, 3);

            panel.Controls.Add(new Label { Text = "最近按键:", Font = new Font(DefaultFont, FontStyle.Bold) }, 0, 4);
            _lblLastKey = new Label { Text = "-", AutoSize = true, ForeColor = Color.Blue };
            panel.Controls.Add(_lblLastKey, 1, 4);

            _logBox = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.Black, ForeColor = Color.LightGray };

            this.Controls.Add(_logBox);
            this.Controls.Add(panel);

            // 托盘菜单
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("显示窗口", null, (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; });
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("退出", null, (s, e) => ExitProgram());

            _notifyIcon = new NotifyIcon
            {
                Icon = this.Icon,
                ContextMenuStrip = _trayMenu,
                Text = "LeftPad DS4 Receiver",
                Visible = true
            };
            _notifyIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; };

            this.FormClosing += MainForm_FormClosing;
        }

        private void SetupServiceEvents()
        {
            _service.OnLog += msg => AppendLog(msg);
            _service.OnStatusChanged += status => this.Invoke((MethodInvoker)(() => _lblVigem.Text = status));
            _service.OnConnectionChanged += conn => this.Invoke((MethodInvoker)(() => _lblConnection.Text = conn));
            _service.OnButtonEvent += btn => this.Invoke((MethodInvoker)(() => _lblLastKey.Text = btn));
        }

        private void AppendLog(string message)
        {
            if (this.IsDisposed) return;
            this.Invoke((MethodInvoker)(() =>
            {
                if (_logBox.Lines.Length > 500) _logBox.Text = ""; // 简易清理
                _logBox.AppendText(message + Environment.NewLine);
                _logBox.SelectionStart = _logBox.Text.Length;
                _logBox.ScrollToCaret();
            }));
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_isReallyClosing && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                _notifyIcon.ShowBalloonTip(2000, "LeftPad", "程序已最小化到托盘，仍在后台运行", ToolTipIcon.Info);
            }
        }

        private void ExitProgram()
        {
            var result = MessageBox.Show("确定要退出接收端吗？\n退出后手机手柄将立即断开连接。", "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                _isReallyClosing = true;
                _service.Stop();
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                Application.Exit();
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!_service.Initialize())
            {
                MessageBox.Show("初始化虚拟手柄失败！请检查是否安装了 ViGEmBus 驱动。", "驱动错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            _service.Start();
        }
    }
}
