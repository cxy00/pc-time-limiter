using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CmjLimit
{
    /// <summary>
    /// 常驻置顶的倒计时悬浮窗。
    /// - 尺寸随文字收缩，字号保持不变
    /// - 鼠标穿透，不进 Alt+Tab / 任务栏，用户无法关闭
    /// - 通过 Opacity 整体半透明，默认约 0.82
    /// </summary>
    internal sealed class HudForm : Form
    {
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;

        private readonly string _position;
        private Font _font;

        private string _text = "";
        private Color _back = Color.FromArgb(20, 20, 28);

        public HudForm(string position, double opacity, double fontSize)
        {
            _position = string.IsNullOrWhiteSpace(position) ? "TopRight" : position;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Text = "";
            BackColor = _back;
            Opacity = Math.Min(1.0, Math.Max(0.2, opacity));

            _font = new Font("Microsoft YaHei", (float)Math.Min(28, Math.Max(8, fontSize)), FontStyle.Bold);

            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(10, 6, 10, 6);

            var label = new Label
            {
                AutoSize = true,
                ForeColor = Color.White,
                Font = _font,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "cmjLimit"
            };
            Controls.Add(label);

            Load += (s, e) => Place();
            FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_TRANSPARENT;
                return cp;
            }
        }

        public void SetText(string text, Color backColor)
        {
            if (IsDisposed)
                return;

            _text = text ?? "";
            _back = backColor;
            BackColor = _back;

            var label = Controls.Count > 0 ? Controls[0] as Label : null;
            if (label != null)
                label.Text = _text;

            if (IsHandleCreated)
                Place();
        }

        public void ApplyStyle(double opacity, double fontSize)
        {
            Opacity = Math.Min(1.0, Math.Max(0.2, opacity));

            var old = _font;
            _font = new Font("Microsoft YaHei", (float)Math.Min(28, Math.Max(8, fontSize)), FontStyle.Bold);
            if (old != null)
            {
                try { old.Dispose(); } catch { }
            }

            var label = Controls.Count > 0 ? Controls[0] as Label : null;
            if (label != null)
                label.Font = _font;

            if (IsHandleCreated && !IsDisposed)
            {
                Place();
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _font != null)
            {
                try { _font.Dispose(); } catch { }
                _font = null;
            }
            base.Dispose(disposing);
        }

        private void Place()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            const int margin = 10;

            switch (_position.ToLowerInvariant())
            {
                case "topleft":
                    Location = new Point(area.Left + margin, area.Top + margin);
                    break;
                case "bottomleft":
                    Location = new Point(area.Left + margin, area.Bottom - Height - margin);
                    break;
                case "bottomright":
                    Location = new Point(area.Right - Width - margin, area.Bottom - Height - margin);
                    break;
                default:
                    Location = new Point(area.Right - Width - margin, area.Top + margin);
                    break;
            }

            // 每帧重新钉到“永远置顶”层，避免被其它置顶窗口（如全屏游戏、播放器）盖住
            NativeMethods.SetTopMost(Handle);
        }
    }
}
