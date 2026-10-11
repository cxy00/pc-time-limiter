using System;
using System.Drawing;
using System.Windows.Forms;

namespace CmjLimit
{
    /// <summary>居中置顶的提醒窗口（不抢焦点，可选自动关闭）。</summary>
    internal sealed class NoticeForm : Form
    {
        private readonly Timer _closeTimer;
        private readonly Label _label;

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW：不出现在 Alt+Tab
                return cp;
            }
        }

        public NoticeForm(string message, Color backColor, int autoCloseSeconds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = backColor;
            Size = new Size(440, 160);
            Opacity = 0.95;

            _label = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei", 12F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = message
            };
            Controls.Add(_label);

            Load += (s, e) => CenterOnScreen();
            FormClosing += (s, e) =>
            {
                // 不允许用户用 Alt+F4 关掉常驻提醒
                if (!ClosableByUser && e.CloseReason == CloseReason.UserClosing)
                    e.Cancel = true;
            };

            if (autoCloseSeconds > 0)
            {
                _closeTimer = new Timer { Interval = autoCloseSeconds * 1000 };
                _closeTimer.Tick += (s, e) =>
                {
                    _closeTimer.Stop();
                    Close();
                };
                _closeTimer.Start();
            }
        }

        /// <summary>是否允许用户关闭（false 时 Alt+F4 无效）。</summary>
        public bool ClosableByUser { get; set; } = true;

        /// <summary>更新提示文字（用于倒计时）。</summary>
        public void SetMessage(string message)
        {
            if (_label != null && _label.IsHandleCreated && !_label.IsDisposed)
                _label.Text = message;
        }

        private void CenterOnScreen()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(
                area.Left + (area.Width - Width) / 2,
                area.Top + (area.Height - Height) / 2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _closeTimer != null)
                _closeTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
