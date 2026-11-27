// 핑크 테마 — 사장님 프로그램들이 함께 쓰는 색과 WinForms 컨트롤 꾸미기(PajamaNamer(파자마네이머) 의 src/PinkTheme.cs 와 같은 파일).
// 다른 WinForms 프로그램에는 이 파일을 복사한 뒤 PinkTheme.Apply(폼) 한 줄을 부르면 같은 톤이 된다.
// 색을 바꿀 때는 아래 「색」 부분만 고친다. 다른 파일에 색 값을 직접 쓰지 않는다.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PinkUi
{
    public static class PinkTheme
    {
        // ── 색 ─────────────────────────────────────────────

        // 파자마캘린더(PajamaCalendar\src\Ui.cs 의 Palette)와 같은 값
        public static readonly Color Background = Hex("#fdfaf5");  // BG: 창 바탕(크림색)
        public static readonly Color Surface = Hex("#ffffff");     // CARD: 입력 칸·목록·버튼
        public static readonly Color Line = Hex("#e9e2d6");        // LINE: 테두리·구분선
        public static readonly Color TitleBar = Hex("#ff8fa3");    // TITLE_BG: 제목줄
        public static readonly Color Accent = Hex("#ff6f8a");      // ACCENT: 주요 버튼·체크·입력 중 테두리
        public static readonly Color Selected = Hex("#ff8fa3");    // SEL_BG: 고른 줄
        public static readonly Color SoftPink = Hex("#ffe3e9");    // TODAY_BG: 메뉴 호버·누름·목록 머리글
        public static readonly Color PalePink = Hex("#fff0f3");    // 날짜 칩 배경: 버튼 호버
        public static readonly Color Text = Hex("#33302c");        // TEXT: 기본 글자
        public static readonly Color TextDisabled = Hex("#b9b4ab"); // DONE: 꺼진 항목 글자
        public static readonly Color DisabledBack = Hex("#f3f1ec"); // 되돌리기 칩 배경: 꺼진 버튼

        // 할일 달력의 값은 대비가 부족해 읽기 쉽게 정한 값(기준 4.5:1 이상 — 시험에서 확인한다)
        public static readonly Color OnPink = Text;                // 분홍 위 글자. 할일 달력의 흰 글자는 대비가 2:1대다.
        public static readonly Color TextMuted = Hex("#736d63");   // 보조 글자. 할일 달력 SUB(#9a948a)는 흰 바탕에서 3:1 정도다.

        // 뜻이 있는 색 — 분홍에 묻히지 않아야 해서 따로 둔다
        public static readonly Color Success = Color.FromArgb(20, 110, 60);
        public static readonly Color ErrorText = Color.FromArgb(170, 30, 25);
        public static readonly Color ErrorBack = Color.FromArgb(255, 214, 208);

        static Color Hex(string value) { return ColorTranslator.FromHtml(value); }

        /// <summary>두 색의 명암 대비(WCAG 기준, 1~21).</summary>
        public static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        static double Luminance(Color c)
        {
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }

        static double Linear(byte channel)
        {
            double v = channel / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        // ── 적용 ───────────────────────────────────────────

        /// <summary>폼과 그 안의 컨트롤을 모두 핑크 테마로 꾸민다. 컨트롤을 다 넣은 뒤에 부르고, 주요 버튼은 먼저 Tag = PrimaryTag 로 표시해 둔다.</summary>
        public static void Apply(Form form)
        {
            form.BackColor = Background;
            form.ForeColor = Text;
            if (form.IsHandleCreated) PaintTitleBar(form);
            form.HandleCreated += (s, e) => PaintTitleBar(form);
            ApplyTree(form);
        }

        public static readonly object PrimaryTag = new object();

        static void ApplyTree(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is Button) StyleButton((Button)c);
                else if (c is TextBox) StyleField((TextBox)c);
                else if (c is NumericUpDown) StyleNumber((NumericUpDown)c);
                else if (c is ComboBox) StyleCombo((ComboBox)c);
                else if (c is ListView) StyleList((ListView)c);
                ApplyTree(c);
            }
        }

        // ── 제목줄(윈도우 11 — 그 이전 윈도우는 값을 무시하고 기본 제목줄을 쓴다) ──

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        const int DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;

        static void PaintTitleBar(Form form)
        {
            SetWindowColor(form, DWMWA_CAPTION_COLOR, TitleBar);
            SetWindowColor(form, DWMWA_BORDER_COLOR, TitleBar);
            SetWindowColor(form, DWMWA_TEXT_COLOR, OnPink);
        }

        static void SetWindowColor(Form form, int attribute, Color color)
        {
            int colorRef = ColorTranslator.ToWin32(color);
            DwmSetWindowAttribute(form.Handle, attribute, ref colorRef, sizeof(int));
        }

        // ── 버튼: 보통·호버·누름·포커스·꺼짐 ────────────────

        static void StyleButton(Button b)
        {
            bool primary = b.Tag == PrimaryTag;
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.ForeColor = primary ? OnPink : Text;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = primary ? TitleBar : PalePink;
            b.FlatAppearance.MouseDownBackColor = SoftPink;

            EventHandler refresh = (s, e) =>
            {
                b.BackColor = !b.Enabled ? DisabledBack : primary ? Accent : Surface;
                b.FlatAppearance.BorderColor = !b.Enabled ? Line : b.Focused || primary ? Accent : Line;
            };
            b.EnabledChanged += refresh;
            b.GotFocus += refresh;
            b.LostFocus += refresh;
            refresh(b, EventArgs.Empty);
        }

        // ── 입력 칸: 보통·입력 중·꺼짐 ──────────────────────

        static void StyleField(TextBox box)
        {
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = Surface;
            box.ForeColor = Text;
            FrameBorder.Attach(box);
        }

        static void StyleNumber(NumericUpDown n)
        {
            n.BorderStyle = BorderStyle.FixedSingle;
            n.BackColor = Surface;
            n.ForeColor = Text;
            FrameBorder.Attach(n);
        }

        static void StyleCombo(ComboBox c)
        {
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Surface;
            c.ForeColor = Text;
            c.DrawMode = DrawMode.OwnerDrawFixed;
            FrameBorder.Attach(c);
            c.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                bool picked = (e.State & DrawItemState.Selected) != 0;
                Color back = !c.Enabled ? DisabledBack : picked ? SoftPink : Surface;
                Color fore = c.Enabled ? Text : TextDisabled;
                using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
                TextRenderer.DrawText(e.Graphics, c.GetItemText(c.Items[e.Index]), c.Font, e.Bounds, fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            };
        }

        // ── 목록: 머리글·고른 줄(입력 중/다른 곳 볼 때)·구분선 ──

        static void StyleList(ListView list)
        {
            list.OwnerDraw = true;
            list.GridLines = false; // 기본 격자는 색을 못 바꿔 줄마다 직접 그린다
            list.BackColor = Surface;
            list.ForeColor = Text;
            list.BorderStyle = BorderStyle.FixedSingle;
            FrameBorder.Attach(list);
            HeaderFiller.Attach(list);
            var headerFont = new Font(list.Font, FontStyle.Bold);
            list.Disposed += (s, e) => headerFont.Dispose();
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix; // 파일 이름의 & 를 단축키로 해석하지 않게

            list.DrawColumnHeader += (s, e) =>
            {
                using (var brush = new SolidBrush(SoftPink)) e.Graphics.FillRectangle(brush, e.Bounds);
                using (var pen = new Pen(TitleBar))
                {
                    e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                    e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
                }
                var area = Rectangle.Inflate(e.Bounds, -Scale(list, 6), 0);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, headerFont, area, Text, flags);
            };
            list.DrawItem += (s, e) => { }; // 줄 전체는 DrawSubItem 이 칸마다 그린다
            list.DrawSubItem += (s, e) =>
            {
                bool picked = e.Item.Selected;
                Color back = picked ? (list.Focused ? Selected : SoftPink) : e.SubItem.BackColor;
                Color fore = picked ? OnPink : e.SubItem.ForeColor;
                using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
                using (var pen = new Pen(Line))
                {
                    e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                    e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
                }
                if (e.Item.Focused && list.Focused)
                    using (var pen = new Pen(Accent))
                    {
                        e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Top, e.Bounds.Right, e.Bounds.Top);
                        e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                    }
                var area = Rectangle.Inflate(e.Bounds, -Scale(list, 6), 0);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, area, fore, flags);
            };
            list.GotFocus += (s, e) => list.Invalidate();
            list.LostFocus += (s, e) => list.Invalidate();
            list.ColumnWidthChanged += (s, e) => list.Invalidate();
        }

        internal static int Scale(Control c, int px)
        {
            using (var g = c.CreateGraphics()) return (int)Math.Round(px * g.DpiX / 96f);
        }

        // ── 윈도우가 직접 그리는 부분의 색을 덧칠 ───────────────

        const int WM_PAINT = 0x000F, WM_NCPAINT = 0x0085;
        const int RDW_INVALIDATE = 0x1, RDW_FRAME = 0x400;
        [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, int flags);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, ref RECT rect);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>한 줄 테두리를 팔레트 색으로 다시 그린다 — 평소 Line, 포커스가 있으면 Accent.</summary>
        sealed class FrameBorder : NativeWindow
        {
            readonly Control control;

            FrameBorder(Control control) { this.control = control; }

            public static void Attach(Control c)
            {
                var frame = new FrameBorder(c);
                if (c.IsHandleCreated) frame.AssignHandle(c.Handle);
                c.HandleCreated += (s, e) => frame.AssignHandle(c.Handle);
                c.HandleDestroyed += (s, e) => frame.ReleaseHandle();
                EventHandler redraw = (s, e) => { if (c.IsHandleCreated) RedrawWindow(c.Handle, IntPtr.Zero, IntPtr.Zero, RDW_FRAME | RDW_INVALIDATE); };
                c.Enter += redraw;
                c.Leave += redraw;
                c.EnabledChanged += redraw;
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg != WM_NCPAINT && m.Msg != WM_PAINT) return;
                RECT r;
                if (!GetWindowRect(Handle, out r)) return;
                IntPtr dc = GetWindowDC(Handle);
                try
                {
                    using (var g = Graphics.FromHdc(dc))
                    using (var pen = new Pen(control.ContainsFocus ? Accent : Line))
                        g.DrawRectangle(pen, 0, 0, r.Right - r.Left - 1, r.Bottom - r.Top - 1);
                }
                finally { ReleaseDC(Handle, dc); }
            }
        }

        /// <summary>마지막 칸 오른쪽의 빈 머리글은 DrawColumnHeader 가 불리지 않아 여기서 칠한다.</summary>
        sealed class HeaderFiller : NativeWindow
        {
            const int LVM_GETHEADER = 0x101F, HDM_GETITEMRECT = 0x1207;
            readonly ListView list;

            HeaderFiller(ListView list) { this.list = list; }

            public static void Attach(ListView list)
            {
                var filler = new HeaderFiller(list);
                EventHandler hook = (s, e) =>
                {
                    IntPtr header = SendMessage(list.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                    if (header != IntPtr.Zero) filler.AssignHandle(header);
                };
                if (list.IsHandleCreated) hook(list, EventArgs.Empty);
                list.HandleCreated += hook;
                list.HandleDestroyed += (s, e) => filler.ReleaseHandle();
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg != WM_PAINT || list.Columns.Count == 0) return;
                RECT last, client;
                last = new RECT();
                if (SendMessage(Handle, HDM_GETITEMRECT, (IntPtr)(list.Columns.Count - 1), ref last) == IntPtr.Zero) return;
                if (!GetClientRect(Handle, out client) || last.Right >= client.Right) return;
                using (var g = Graphics.FromHwnd(Handle))
                using (var brush = new SolidBrush(SoftPink))
                using (var pen = new Pen(TitleBar))
                {
                    g.FillRectangle(brush, last.Right, 0, client.Right - last.Right, client.Bottom);
                    g.DrawLine(pen, last.Right, client.Bottom - 1, client.Right, client.Bottom - 1);
                }
            }
        }
    }

    /// <summary>체크 표시가 시스템 파랑이 되지 않도록 직접 그리는 체크 상자. 키보드·클릭 동작은 기본 그대로다.</summary>
    public class PinkCheckBox : CheckBox
    {
        bool hovered;

        public PinkCheckBox() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : PinkTheme.Background);
            int size = PinkTheme.Scale(this, 13);
            var box = new Rectangle(0, (Height - size) / 2, size - 1, size - 1);

            Color fill = !Enabled ? PinkTheme.DisabledBack : Checked ? (hovered ? PinkTheme.TitleBar : PinkTheme.Accent) : (hovered ? PinkTheme.PalePink : PinkTheme.Surface);
            Color border = !Enabled ? PinkTheme.Line : Checked || hovered || Focused ? PinkTheme.Accent : PinkTheme.TextMuted;
            using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, box);
            using (var pen = new Pen(border)) g.DrawRectangle(pen, box);
            if (Checked)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float s = box.Width;
                using (var pen = new Pen(Enabled ? PinkTheme.OnPink : PinkTheme.TextDisabled, Math.Max(1.5f, s / 7f)))
                    g.DrawLines(pen, new[] {
                        new PointF(box.X + s * 0.22f, box.Y + s * 0.52f),
                        new PointF(box.X + s * 0.42f, box.Y + s * 0.72f),
                        new PointF(box.X + s * 0.78f, box.Y + s * 0.30f) });
                g.SmoothingMode = SmoothingMode.None;
            }

            int textX = size + PinkTheme.Scale(this, 3);
            var area = new Rectangle(textX, 0, Width - textX, Height);
            TextRenderer.DrawText(g, Text, Font, area, Enabled ? PinkTheme.Text : PinkTheme.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, area, PinkTheme.Text, Parent != null ? Parent.BackColor : PinkTheme.Background);
        }
    }

    /// <summary>테두리와 제목을 팔레트 색으로 그리는 묶음 상자.</summary>
    public class PinkGroupBox : GroupBox
    {
        public PinkGroupBox() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            using (var title = new Font(Font, FontStyle.Bold))
            {
                Size text = TextRenderer.MeasureText(g, Text, title, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                int top = text.Height / 2;
                int left = PinkTheme.Scale(this, 8), pad = PinkTheme.Scale(this, 3);
                using (var pen = new Pen(PinkTheme.Line)) g.DrawRectangle(pen, 0, top, Width - 1, Height - top - 1);
                using (var brush = new SolidBrush(BackColor)) g.FillRectangle(brush, left - pad, 0, text.Width + pad * 2, text.Height);
                TextRenderer.DrawText(g, Text, title, new Point(left, 0), PinkTheme.Text, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }
        }
    }
}
