using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PcDs4Server
{
    public class RoundedPanel : Panel
    {
        public int Radius { get; set; } = 15;
        public Color BorderColor { get; set; } = ThemeColors.BorderPurple;
        public float BorderWidth { get; set; } = 1f;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            GraphicsPath path = new GraphicsPath();
            path.AddArc(0, 0, Radius, Radius, 180, 90);
            path.AddArc(Width - Radius - 1, 0, Radius, Radius, 270, 90);
            path.AddArc(Width - Radius - 1, Height - Radius - 1, Radius, Radius, 0, 90);
            path.AddArc(0, Height - Radius - 1, Radius, Radius, 90, 90);
            path.CloseAllFigures();

            this.Region = new Region(path);

            using (Pen pen = new Pen(BorderColor, BorderWidth))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    public class SidebarButton : Button
    {
        public bool IsSelected { get; set; }

        public SidebarButton()
        {
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.BackColor = Color.Transparent;
            this.ForeColor = ThemeColors.TextSecondary;
            this.Font = new Font("Microsoft YaHei UI", 10, FontStyle.Regular);
            this.TextAlign = ContentAlignment.MiddleLeft;
            this.Padding = new Padding(20, 0, 0, 0);
            this.Height = 45;
            this.Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (IsSelected)
            {
                float receiverScale = Height / 45f;
                using (SolidBrush brush = new SolidBrush(ThemeColors.AccentPurple))
                {
                    int inset = (int)MathF.Round(10 * receiverScale);
                    int markerWidth = Math.Max(1, (int)MathF.Round(4 * receiverScale));
                    e.Graphics.FillRectangle(brush, 0, inset, markerWidth, Height - (2 * inset));
                }
                this.ForeColor = ThemeColors.TextMain;
                this.Font = new Font(this.Font, FontStyle.Bold);
            }
            else
            {
                this.ForeColor = ThemeColors.TextSecondary;
                this.Font = new Font(this.Font, FontStyle.Regular);
            }
        }
    }

    public class ModernStatusCard : Panel
    {
        private Label _title, _value;
        private Panel _indicator;

        public string Value { get => _value.Text; set => _value.Text = value; }

        public ModernStatusCard(string title, string initialValue)
        {
            this.Size = new Size(160, 80);
            this.BackColor = Color.FromArgb(40, 42, 85); // 稍微亮一点的背景
            this.Padding = new Padding(12);

            _title = new Label {
                Text = title.ToUpper(),
                Font = new Font("Microsoft YaHei UI", 7, FontStyle.Bold),
                ForeColor = ThemeColors.TextSecondary,
                AutoSize = true,
                Location = new Point(12, 12)
            };

            _value = new Label {
                Text = initialValue,
                Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold),
                ForeColor = ThemeColors.TextMain,
                AutoSize = true,
                Location = new Point(12, 35)
            };

            _indicator = new Panel {
                Size = new Size(8, 8),
                Location = new Point(135, 15),
                BackColor = Color.Gray
            };

            this.Controls.Add(_title);
            this.Controls.Add(_value);
            this.Controls.Add(_indicator);
        }

        public void SetStatusColor(Color color)
        {
            _indicator.BackColor = color;
            UpdateIndicatorRegion();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            UpdateIndicatorRegion();
        }

        private void UpdateIndicatorRegion()
        {
            // Setting the card's Size in the constructor can synchronously call
            // OnLayout before the child indicator has been created.
            if (_indicator == null || _indicator.IsDisposed ||
                _indicator.Width <= 0 || _indicator.Height <= 0)
            {
                return;
            }

            using var path = new GraphicsPath();
            path.AddEllipse(0, 0, _indicator.Width, _indicator.Height);
            Region? previous = _indicator.Region;
            _indicator.Region = new Region(path);
            previous?.Dispose();
        }
    }
}
