// 작은 창들 — 녹화 범위 테두리, 녹화 막대(정지 버튼), 끝났을 때 알림.
// 모두 화면 캡쳐·녹화에 찍히지 않게 한다.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using PinkUi;

namespace PajamaCapture
{
    /// <summary>녹화 범위 바깥에 두르는 분홍 테두리. 마우스가 통과한다.</summary>
    sealed class RecordFrame : Form
    {
        readonly int thickness;
        bool blink;
        readonly Timer timer = new Timer { Interval = 600 };

        public RecordFrame(Rectangle area)
        {
            thickness = Math.Max(3, (int)Math.Round(3 * Native.ScaleAt(area.Location)));
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = PinkTheme.Accent;
            var outer = Rectangle.Inflate(area, thickness, thickness);
            Bounds = outer;
            var region = new Region(new Rectangle(0, 0, outer.Width, outer.Height));
            region.Exclude(new Rectangle(thickness, thickness, area.Width, area.Height));
            Region = region;
            timer.Tick += (s, e) => { blink = !blink; BackColor = blink ? PinkTheme.TitleBar : PinkTheme.Accent; };
            timer.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetLayeredOpaque(Handle);
            Native.ExcludeFromCapture(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            var b = Bounds;
            Native.SetWindowPos(Handle, new IntPtr(-1), b.X, b.Y, b.Width, b.Height, 0x0010 | 0x0040);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, int key, byte alpha, int flags);
        static void SetLayeredOpaque(IntPtr hwnd) { SetLayeredWindowAttributes(hwnd, 0, 235, 2); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>녹화 중 막대: 경과 시간 + 정지·취소. 끌어서 옮길 수 있다.</summary>
    sealed class RecordBar : Form
    {
        readonly Label time = new Label();
        readonly Button stop = new Button(), cancel = new Button();
        readonly Timer timer = new Timer { Interval = 250 };
        readonly Func<long> elapsed;
        readonly string format;
        bool dot = true;

        public event EventHandler StopClicked, CancelClicked;

        public RecordBar(Rectangle area, string format, Func<long> elapsed)
        {
            SuspendLayout();
            this.elapsed = elapsed;
            this.format = format;
            Text = "파자마 캡쳐 - 녹화 중";
            Font = new Font("Malgun Gothic", 9.5f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ClientSize = new Size(300, 44);
            Padding = new Padding(1);

            time.AutoSize = true; // 고정 크기 라벨은 화면 배율이 두 번 곱해져 글자 길이에 맞춘다
            time.Location = new Point(12, 12);
            time.Font = new Font("Malgun Gothic", 10.5f, FontStyle.Bold);
            stop.Text = "■ 정지";
            stop.Tag = PinkTheme.PrimaryTag;
            stop.SetBounds(134, 7, 90, 30);
            cancel.Text = "취소";
            cancel.SetBounds(230, 7, 60, 30);
            Controls.AddRange(new Control[] { time, stop, cancel });
            ResumeLayout(false);
            PinkTheme.Apply(this);
            BackColor = PinkTheme.SoftPink;
            time.ForeColor = PinkTheme.ErrorText;
            stop.Click += (s, e) => { var h = StopClicked; if (h != null) h(this, EventArgs.Empty); };
            cancel.Click += (s, e) => { var h = CancelClicked; if (h != null) h(this, EventArgs.Empty); };
            timer.Tick += (s, e) => UpdateTime();
            UpdateTime();

            MouseDown += Drag;
            time.MouseDown += Drag;
            this.area = area;
        }

        readonly Rectangle area;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e); // 화면 배율에 맞춰 크기가 정해진 뒤에 자리를 잡는다
            Place(area);
        }

        void Place(Rectangle area)
        {
            Rectangle work = Screen.FromRectangle(area).WorkingArea;
            int w = Width, h = Height, gap = 8;
            int x = Math.Max(work.Left, Math.Min(area.Right - w, work.Right - w));
            int y = area.Bottom + gap;
            if (y + h > work.Bottom) y = area.Top - gap - h;
            if (y < work.Top) y = area.Bottom - gap - h; // 범위가 화면을 꽉 채우면 안쪽 아래(녹화에는 찍히지 않는다)
            Location = new Point(x, Math.Max(work.Top, y));
        }

        void Drag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, new IntPtr(Native.HTCAPTION), IntPtr.Zero);
        }

        void UpdateTime()
        {
            long ms = elapsed();
            dot = !dot;
            time.Text = (dot ? "● " : "○ ") + string.Format("{0:00}:{1:00}", ms / 60000, ms / 1000 % 60) + "  " + format;
        }

        public void ShowSaving()
        {
            timer.Stop();
            time.Text = "저장 중…";
            time.ForeColor = PinkTheme.Text;
            stop.Enabled = false;
            cancel.Enabled = false;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.ExcludeFromCapture(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(PinkTheme.Accent, 2)) e.Graphics.DrawRectangle(pen, 1, 1, ClientSize.Width - 2, ClientSize.Height - 2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>오른쪽 아래에 잠깐 뜨는 알림. 누르면 파일을 열고, 「폴더」를 누르면 탐색기에서 보여 준다.</summary>
    sealed class Toast : Form
    {
        readonly Timer timer = new Timer { Interval = 5000 };
        readonly string path;
        readonly Image thumb;
        readonly string title, message;
        readonly bool isError;
        Rectangle folderLink, editLink;

        /// <summary>정해 두면 「편집하기」 링크가 생기고, 누르면 파일 경로를 넘겨 부른다(녹화 알림용).</summary>
        public Action<string> EditRequested;

        public Toast(string title, string message, string path, Image thumbnail, bool isError)
        {
            SuspendLayout();
            this.title = title;
            this.message = message;
            this.path = path;
            this.isError = isError;
            thumb = thumbnail;
            Text = "파자마 캡쳐 알림";
            Font = new Font("Malgun Gothic", 9f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = PinkTheme.Surface;
            ClientSize = new Size(360, 96);
            DoubleBuffered = true;
            Cursor = string.IsNullOrEmpty(path) ? Cursors.Default : Cursors.Hand;
            timer.Tick += (s, e) => { if (!Bounds.Contains(Cursor.Position)) Close(); };
            if (isError) timer.Interval = 12000;
            ResumeLayout(false);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.ExcludeFromCapture(Handle);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(work.Right - Width - 16, work.Bottom - Height - 16);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); timer.Start(); }

        float DeviceScale { get { using (var g = CreateGraphics()) return g.DpiX / 96f; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float s = DeviceScale;
            Func<int, int> P = v => (int)Math.Round(v * s);
            g.Clear(PinkTheme.Surface);
            using (var bar = new SolidBrush(isError ? PinkTheme.ErrorText : PinkTheme.Accent)) g.FillRectangle(bar, 0, 0, P(6), ClientSize.Height);
            using (var pen = new Pen(PinkTheme.TitleBar)) g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

            int x = P(16);
            if (thumb != null)
            {
                var box = new Rectangle(P(16), P(12), P(96), ClientSize.Height - P(24));
                float k = Math.Min((float)box.Width / thumb.Width, (float)box.Height / thumb.Height);
                k = Math.Min(k, 1f);
                int tw = Math.Max(1, (int)(thumb.Width * k)), th = Math.Max(1, (int)(thumb.Height * k));
                var dest = new Rectangle(box.X + (box.Width - tw) / 2, box.Y + (box.Height - th) / 2, tw, th);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(thumb, dest);
                using (var pen = new Pen(PinkTheme.Line)) g.DrawRectangle(pen, dest.X - 1, dest.Y - 1, dest.Width + 1, dest.Height + 1);
                x = box.Right + P(12);
            }
            using (var b = new Font(Font, FontStyle.Bold))
                TextRenderer.DrawText(g, title, b, new Rectangle(x, P(10), ClientSize.Width - x - P(8), P(22)), isError ? PinkTheme.ErrorText : PinkTheme.Text,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, message, Font, new Rectangle(x, P(32), ClientSize.Width - x - P(8), ClientSize.Height - P(56)), PinkTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (!string.IsNullOrEmpty(path))
            {
                string link = "폴더에서 보기";
                Size t = TextRenderer.MeasureText(link, Font);
                folderLink = new Rectangle(ClientSize.Width - t.Width - P(12), ClientSize.Height - t.Height - P(8), t.Width, t.Height);
                int hintRight = folderLink.X - P(8);
                using (var u = new Font(Font, FontStyle.Underline))
                {
                    TextRenderer.DrawText(g, link, u, folderLink.Location, PinkTheme.Accent, TextFormatFlags.NoPrefix);
                    if (EditRequested != null)
                    {
                        string edit = "편집하기";
                        Size et = TextRenderer.MeasureText(edit, Font);
                        editLink = new Rectangle(folderLink.X - et.Width - P(14), folderLink.Y, et.Width, et.Height);
                        TextRenderer.DrawText(g, edit, u, editLink.Location, PinkTheme.Accent, TextFormatFlags.NoPrefix);
                        hintRight = editLink.X - P(8);
                    }
                }
                string hint = "누르면 열기";
                if (x + TextRenderer.MeasureText(hint, Font).Width <= hintRight) // 링크와 겹치면 안내 글은 뺀다
                    TextRenderer.DrawText(g, hint, Font, new Point(x, ClientSize.Height - t.Height - P(8)), PinkTheme.TextMuted, TextFormatFlags.NoPrefix);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            try
            {
                if (e.Button == MouseButtons.Left && !string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    if (EditRequested != null && editLink.Contains(e.Location)) EditRequested(path);
                    else if (folderLink.Contains(e.Location)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                    else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
            }
            catch (Exception ex) { MessageBox.Show("열지 못했어요: " + ex.Message, "파자마 캡쳐"); }
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); if (thumb != null) thumb.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
