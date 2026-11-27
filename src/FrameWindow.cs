// 녹화 틀(꿀캠 방식) — 녹화하기 전에 화면에 띄워 두고, 끌어서 옮기고 가장자리·모서리로 크기를 맞춘 뒤 녹화한다.
// 틀 안쪽은 창 모양(Region)에서 빠져 있어서 뒤 화면이 그대로 보이고 마우스 클릭도 뒤 창으로 통과한다.
// 좌표는 모두 실제 화면 점(픽셀). 안쪽 범위(Inner)가 기준이고, 테두리와 막대는 그 바깥에 붙인다.
// 창 전체가 화면 캡쳐·녹화에서 빠지므로(ExcludeFromCapture) 막대가 범위와 겹치더라도 결과물에는 찍히지 않는다.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using PinkUi;

namespace PajamaCapture
{
    sealed class FrameWindow : Form
    {
        public const string WindowTitle = "파자마 캡쳐 - 녹화 틀";
        /// <summary>안쪽 범위의 가장 작은 가로·세로(짝수).</summary>
        public const int MinSide = 20;

        enum State { Idle, Recording, Saving }
        enum Part { None, Grip, Edge, Size, Record, Stop, Cancel, Close }
        const int L = 1, T = 2, R = 4, B = 8; // 끄는 가장자리

        // Kind: 0 글자, 1 고르기 칩, 2 주요 버튼, 3 버튼, 4 경과 시간
        sealed class Item { public Part Part; public string Text; public int Kind; public bool Right; public Rectangle Rect; }

        static readonly Color RecRed = Color.FromArgb(232, 48, 78);
        static readonly Color CornerColor = Color.FromArgb(196, 40, 72);

        Rectangle inner;            // 녹화할 안쪽 범위(화면 좌표)
        State state = State.Idle;
        bool evenOnly;
        string format;
        Func<long> elapsed;
        bool allowClose;

        // 배치 결과
        float scale = 1f;
        int edge;                   // 테두리 두께
        Rectangle windowRect;       // 창 범위(화면 좌표)
        Rectangle outerLocal, innerLocal, barLocal; // 창 안쪽 좌표
        readonly List<Item> items = new List<Item>();
        Font font, bold;
        float fontScale;
        bool laying, layoutAgain;

        // 마우스
        Part dragPart = Part.None;
        int dragEdges;
        Point dragFrom;
        Rectangle dragStart;
        Part pressed = Part.None, hot = Part.None;

        readonly Timer timer = new Timer { Interval = 500 };
        bool blink;
        readonly ContextMenuStrip sizeMenu = new ContextMenuStrip();
        Font menuFont;

        public event Action RecordClicked, StopClicked, CancelClicked;

        public FrameWindow(Rectangle innerRect, bool evenOnly, string format)
        {
            Text = WindowTitle;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None; // 배율은 직접 계산한다(모니터마다 다를 수 있어서)
            BackColor = PinkTheme.SoftPink;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.evenOnly = evenOnly;
            this.format = format;
            inner = Fit(innerRect);
            timer.Tick += (s, e) => { blink = !blink; Invalidate(); };
            Relayout();
        }

        // ── 바깥에서 쓰는 것 ──

        /// <summary>녹화할 안쪽 범위(화면 좌표, 실제 화면 점).</summary>
        public Rectangle Inner { get { return inner; } }
        /// <summary>창 전체 범위(화면 좌표) — 테두리와 막대를 합친 것.</summary>
        public Rectangle WindowRect { get { return windowRect; } }
        public int EdgeThickness { get { return edge; } }
        public float UiScale { get { return scale; } }
        public bool Busy { get { return state != State.Idle; } }

        /// <summary>MP4 는 H.264 가 짝수 크기만 받아서, 켜면 크기를 짝수로만 맞춘다.</summary>
        public bool EvenOnly
        {
            get { return evenOnly; }
            set
            {
                if (evenOnly == value) return;
                evenOnly = value;
                if (state == State.Idle) inner = Fit(inner);
                Relayout();
            }
        }

        public string Format { get { return format; } set { format = value; Relayout(); } }

        public void SetInner(Rectangle r)
        {
            if (state != State.Idle) return;
            inner = Fit(r);
            Relayout();
        }

        /// <summary>녹화 중으로 바꾼다 — 크기·위치가 잠기고 막대에 경과 시간과 정지·취소가 보인다.</summary>
        public void ShowRecording(Func<long> elapsedMs, string recordFormat)
        {
            EndDrag();
            pressed = Part.None;
            state = State.Recording;
            elapsed = elapsedMs;
            format = recordFormat;
            blink = false;
            timer.Start();
            Relayout();
        }

        public void ShowSaving()
        {
            state = State.Saving;
            timer.Stop();
            blink = false;
            Relayout();
        }

        public void ShowIdle()
        {
            state = State.Idle;
            timer.Stop();
            elapsed = null;
            blink = false;
            Relayout();
        }

        /// <summary>녹화 중이어도 닫는다(프로그램을 끌 때).</summary>
        public void ForceClose()
        {
            allowClose = true;
            Close();
        }

        // ── 배치 ──

        int S(float px) { return (int)Math.Round(px * scale); }

        Rectangle Fit(Rectangle r)
        {
            Rectangle v = Native.VirtualScreen;
            int w = Math.Max(MinSide, Math.Min(r.Width, v.Width));
            int h = Math.Max(MinSide, Math.Min(r.Height, v.Height));
            if (evenOnly) { w -= w % 2; h -= h % 2; }
            int x = Math.Max(v.Left, Math.Min(r.X, v.Right - w));
            int y = Math.Max(v.Top, Math.Min(r.Y, v.Bottom - h));
            return new Rectangle(x, y, w, h);
        }

        static Point Center(Rectangle r) { return new Point(r.X + r.Width / 2, r.Y + r.Height / 2); }

        void Relayout()
        {
            if (laying) { layoutAgain = true; return; } // 창을 옮기다 배율이 바뀌면(WM_DPICHANGED) 끝난 뒤 한 번 더
            laying = true;
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    layoutAgain = false;
                    LayoutOnce();
                    if (!layoutAgain) break;
                }
            }
            finally { laying = false; }
        }

        void LayoutOnce()
        {
            scale = Math.Max(1f, Native.ScaleAt(Center(inner)));
            if (font == null || fontScale != scale)
            {
                if (font != null) { font.Dispose(); bold.Dispose(); }
                font = new Font("Malgun Gothic", 13f * scale, GraphicsUnit.Pixel);
                bold = new Font("Malgun Gothic", 13f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
                fontScale = scale;
            }
            edge = Math.Max(5, S(6));
            BuildItems();

            int h = S(36), pad = S(5), gap = S(5), grip = S(24);
            int leftW = 0, rightW = 0;
            foreach (var it in items)
            {
                string measure = it.Kind == 4 ? "● 00:00" : it.Text;
                int w = TextRenderer.MeasureText(measure, it.Kind >= 2 ? bold : font).Width + (it.Kind == 0 || it.Kind == 4 ? S(4) : S(20));
                it.Rect = new Rectangle(0, 0, w, h - pad * 2);
                if (it.Right) rightW += w + gap; else leftW += w + gap;
            }

            Rectangle outer = Rectangle.Inflate(inner, edge, edge);
            int barW = Math.Max(outer.Width, grip + leftW + S(10) + rightW + pad);
            Rectangle mon = Screen.FromPoint(Center(inner)).Bounds;
            int by;
            if (outer.Top - h >= mon.Top) by = outer.Top - h;              // 틀 위
            else if (outer.Bottom + h <= mon.Bottom) by = outer.Bottom;    // 위에 자리가 없으면 아래
            else by = inner.Top;                                           // 둘 다 없으면 안쪽 위(이 창은 녹화에 찍히지 않는다)
            int bx = outer.Left;
            if (bx + barW > mon.Right) bx = Math.Max(Math.Min(outer.Left, mon.Left), mon.Right - barW);
            var barScreen = new Rectangle(bx, by, barW, h);
            Rectangle win = Rectangle.Union(outer, barScreen);

            outerLocal = outer; outerLocal.Offset(-win.X, -win.Y);
            innerLocal = inner; innerLocal.Offset(-win.X, -win.Y);
            barLocal = barScreen; barLocal.Offset(-win.X, -win.Y);

            int x = barLocal.X + grip, rx = barLocal.Right - pad, y = barLocal.Y + pad;
            foreach (var it in items)
            {
                if (it.Right) continue;
                it.Rect.Location = new Point(x, y);
                x += it.Rect.Width + gap;
            }
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var it = items[i];
                if (!it.Right) continue;
                rx -= it.Rect.Width;
                it.Rect.Location = new Point(rx, y);
                rx -= gap;
            }

            windowRect = win;
            ApplyWindow();
        }

        void BuildItems()
        {
            items.Clear();
            string size = inner.Width + " × " + inner.Height;
            switch (state)
            {
                case State.Idle:
                    Add(Part.Size, size + "  ▾", 1, false);
                    Add(Part.None, format, 0, false);
                    Add(Part.Record, "● 녹화", 2, true);
                    Add(Part.Close, "닫기", 3, true);
                    break;
                case State.Recording:
                    Add(Part.None, "", 4, false);
                    Add(Part.None, size + "  " + format, 0, false);
                    Add(Part.Stop, "■ 정지", 2, true);
                    Add(Part.Cancel, "취소", 3, true);
                    break;
                default:
                    Add(Part.None, "저장 중…", 0, false);
                    Add(Part.None, size + "  " + format, 0, false);
                    break;
            }
        }

        void Add(Part part, string text, int kind, bool right)
        {
            items.Add(new Item { Part = part, Text = text, Kind = kind, Right = right });
        }

        void ApplyWindow()
        {
            var region = new Region(outerLocal);
            region.Exclude(innerLocal);   // 안쪽은 창이 아니다 → 뒤가 보이고 클릭이 통과한다
            region.Union(barLocal);
            Region old = Region;
            if (IsHandleCreated)
            {
                // 화면 점 그대로 놓는다(WinForms 의 크기 제한·배율 계산을 거치지 않게)
                Native.SetWindowPos(Handle, new IntPtr(-1), windowRect.X, windowRect.Y, windowRect.Width, windowRect.Height, 0x0010);
                Region = region;
            }
            else
            {
                Bounds = windowRect;
                Region = region;
            }
            if (old != null) old.Dispose();
            Invalidate();
        }

        // ── 창 ──

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.ExcludeFromCapture(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Relayout(); // 실제 화면 점으로 다시 놓는다
            Activate();
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_DPICHANGED = 0x02E0, WM_DISPLAYCHANGE = 0x007E;
            if (m.Msg == WM_DPICHANGED)
            {
                // 윈도우가 권하는 크기로 바꾸지 않는다 — 녹화 범위(화면 점)는 그대로 두고 막대 글자·테두리만 새 배율로
                m.Result = IntPtr.Zero;
                Relayout();
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == WM_DISPLAYCHANGE && state == State.Idle) SetInner(inner);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (state != State.Idle && !allowClose) { e.Cancel = true; return; } // 녹화·저장 중에는 닫지 않는다(정지 먼저)
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                sizeMenu.Dispose();
                if (menuFont != null) menuFont.Dispose();
                if (font != null) { font.Dispose(); bold.Dispose(); }
            }
            base.Dispose(disposing);
        }

        // ── 그리기 ──

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e) { PaintScene(e.Graphics); }

        /// <summary>틀과 막대를 그린다(시험에서 그림으로 뽑을 때도 쓴다).</summary>
        public void PaintScene(Graphics g)
        {
            Color ring = state == State.Recording ? (blink ? PinkTheme.TitleBar : RecRed) : PinkTheme.Accent;
            Rectangle o = outerLocal, n = innerLocal;
            using (var b = new SolidBrush(ring))
            {
                g.FillRectangle(b, o.X, o.Y, o.Width, n.Top - o.Top);                // 위
                g.FillRectangle(b, o.X, n.Bottom, o.Width, o.Bottom - n.Bottom);      // 아래
                g.FillRectangle(b, o.X, n.Top, n.Left - o.X, n.Height);               // 왼쪽
                g.FillRectangle(b, n.Right, n.Top, o.Right - n.Right, n.Height);      // 오른쪽
            }
            if (state == State.Idle)
            {
                // 모서리를 진하게 — 잡아서 크기를 바꿀 수 있다는 표시(녹화 중에는 잠겨서 안 그린다)
                int c = Math.Min(S(22), Math.Min(n.Width, n.Height) / 2) + edge;
                using (var b = new SolidBrush(CornerColor))
                {
                    g.FillRectangle(b, o.X, o.Y, c, edge); g.FillRectangle(b, o.X, o.Y, edge, c);
                    g.FillRectangle(b, o.Right - c, o.Y, c, edge); g.FillRectangle(b, o.Right - edge, o.Y, edge, c);
                    g.FillRectangle(b, o.X, o.Bottom - edge, c, edge); g.FillRectangle(b, o.X, o.Bottom - c, edge, c);
                    g.FillRectangle(b, o.Right - c, o.Bottom - edge, c, edge); g.FillRectangle(b, o.Right - edge, o.Bottom - c, edge, c);
                }
            }

            // 막대
            using (var b = new SolidBrush(PinkTheme.SoftPink)) g.FillRectangle(b, barLocal);
            using (var pen = new Pen(state == State.Recording ? RecRed : PinkTheme.Accent, Math.Max(1, S(1))))
                g.DrawRectangle(pen, barLocal.X, barLocal.Y, barLocal.Width - 1, barLocal.Height - 1);
            using (var dot = new SolidBrush(state == State.Idle ? PinkTheme.TextMuted : PinkTheme.TextDisabled)) // 손잡이 점(옮기기)
            {
                int d = Math.Max(2, S(3)), sx = barLocal.X + S(8), sy = barLocal.Y + barLocal.Height / 2 - d * 5 / 2;
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 2; col++)
                        g.FillRectangle(dot, sx + col * d * 2, sy + row * d * 2, d, d);
            }

            foreach (var it in items)
            {
                bool isHot = it.Part != Part.None && it.Part == hot;
                switch (it.Kind)
                {
                    case 0:
                        TextRenderer.DrawText(g, it.Text, font, it.Rect, PinkTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                        break;
                    case 4:
                        long ms = elapsed != null ? elapsed() : 0;
                        string t = (blink ? "○ " : "● ") + string.Format("{0:00}:{1:00}", ms / 60000, ms / 1000 % 60);
                        TextRenderer.DrawText(g, t, bold, it.Rect, PinkTheme.ErrorText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                        break;
                    case 1:
                        DrawPill(g, it.Rect, it.Text, font, isHot ? PinkTheme.PalePink : PinkTheme.Surface, PinkTheme.Text, PinkTheme.TitleBar);
                        break;
                    case 2:
                        DrawPill(g, it.Rect, it.Text, bold, isHot ? PinkTheme.TitleBar : PinkTheme.Accent, PinkTheme.OnPink, PinkTheme.Accent);
                        break;
                    default:
                        DrawPill(g, it.Rect, it.Text, bold, isHot ? PinkTheme.PalePink : PinkTheme.Surface, PinkTheme.Text, PinkTheme.TitleBar);
                        break;
                }
            }
        }

        void DrawPill(Graphics g, Rectangle r, string text, Font f, Color back, Color fore, Color border)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(r, S(6)))
            {
                using (var b = new SolidBrush(back)) g.FillPath(b, path);
                using (var pen = new Pen(border, Math.Max(1, S(1)))) g.DrawPath(pen, path);
            }
            g.SmoothingMode = SmoothingMode.None;
            TextRenderer.DrawText(g, text, f, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 1) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>시험용: 창 모양 그대로 그림을 만든다(안쪽은 어두운 회색으로).</summary>
        public Bitmap Snapshot()
        {
            var bmp = new Bitmap(windowRect.Width, windowRect.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                using (var b = new SolidBrush(Color.FromArgb(60, 60, 70))) g.FillRectangle(b, innerLocal);
                PaintScene(g);
            }
            return bmp;
        }

        // ── 마우스 ──

        Part HitTest(Point p, out int edges)
        {
            edges = 0;
            if (barLocal.Contains(p))
            {
                foreach (var it in items)
                    if (it.Kind >= 1 && it.Kind <= 3 && it.Rect.Contains(p)) return it.Part;
                return Part.Grip;
            }
            if (!outerLocal.Contains(p) || innerLocal.Contains(p)) return Part.None;
            int grab = Math.Max(edge * 2, S(18)); // 모서리 근처는 두 방향으로
            bool left = p.X < innerLocal.Left, right = p.X >= innerLocal.Right, top = p.Y < innerLocal.Top, bottom = p.Y >= innerLocal.Bottom;
            if (left || right)
            {
                if (p.Y < innerLocal.Top + grab) top = true;
                else if (p.Y >= innerLocal.Bottom - grab) bottom = true;
            }
            if (top || bottom)
            {
                if (p.X < innerLocal.Left + grab) left = true;
                else if (p.X >= innerLocal.Right - grab) right = true;
            }
            edges = (left ? L : 0) | (top ? T : 0) | (right ? R : 0) | (bottom ? B : 0);
            return Part.Edge;
        }

        Cursor CursorFor(Part part, int edges)
        {
            switch (part)
            {
                case Part.Grip: return state == State.Idle ? Cursors.SizeAll : Cursors.Default;
                case Part.Size: case Part.Record: case Part.Stop: case Part.Cancel: case Part.Close: return Cursors.Hand;
                case Part.Edge:
                    if (state != State.Idle) return Cursors.No; // 녹화 중에는 크기가 잠겨 있다
                    if (edges == (L | T) || edges == (R | B)) return Cursors.SizeNWSE;
                    if (edges == (R | T) || edges == (L | B)) return Cursors.SizeNESW;
                    if ((edges & (L | R)) != 0) return Cursors.SizeWE;
                    return Cursors.SizeNS;
            }
            return Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int edges;
            Part p = HitTest(e.Location, out edges);
            if (p >= Part.Size) { pressed = p; Invalidate(); return; }
            if (state != State.Idle || (p != Part.Grip && p != Part.Edge)) return;
            dragPart = p;
            dragEdges = edges;
            dragFrom = Cursor.Position;   // 화면 좌표로 계산한다(끄는 동안 창 자체가 움직이므로)
            dragStart = inner;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragPart != Part.None) { DoDrag(Cursor.Position); return; }
            int edges;
            Part p = HitTest(e.Location, out edges);
            Part h = p >= Part.Size ? p : Part.None;
            if (h != hot) { hot = h; Invalidate(); }
            Cursor = CursorFor(p, edges);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (pressed != Part.None)
            {
                int edges;
                Part p = HitTest(e.Location, out edges);
                Part was = pressed;
                pressed = Part.None;
                Invalidate();
                if (p == was) PressPart(was);
                return;
            }
            EndDrag();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hot != Part.None) { hot = Part.None; Invalidate(); }
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) dragPart = Part.None;
        }

        void EndDrag()
        {
            if (dragPart == Part.None) return;
            dragPart = Part.None;
            Capture = false;
        }

        void DoDrag(Point now)
        {
            if (state != State.Idle) { EndDrag(); return; }
            int dx = now.X - dragFrom.X, dy = now.Y - dragFrom.Y;
            Rectangle v = Native.VirtualScreen, s = dragStart, r;
            if (dragPart == Part.Grip)
            {
                r = s;
                r.X = Math.Max(v.Left, Math.Min(s.X + dx, v.Right - s.Width));
                r.Y = Math.Max(v.Top, Math.Min(s.Y + dy, v.Bottom - s.Height));
            }
            else
            {
                int l = s.Left, t = s.Top, rr = s.Right, b = s.Bottom;
                if ((dragEdges & L) != 0) l = Math.Max(v.Left, Math.Min(s.Left + dx, rr - MinSide));
                if ((dragEdges & R) != 0) rr = Math.Min(v.Right, Math.Max(s.Right + dx, l + MinSide));
                if ((dragEdges & T) != 0) t = Math.Max(v.Top, Math.Min(s.Top + dy, b - MinSide));
                if ((dragEdges & B) != 0) b = Math.Min(v.Bottom, Math.Max(s.Bottom + dy, t + MinSide));
                if (evenOnly)
                {
                    // 끄는 쪽 가장자리를 1점 당겨 짝수로(반대쪽은 그대로)
                    if ((rr - l) % 2 != 0) { if ((dragEdges & L) != 0) l++; else rr--; }
                    if ((b - t) % 2 != 0) { if ((dragEdges & T) != 0) t++; else b--; }
                }
                r = Rectangle.FromLTRB(l, t, rr, b);
            }
            if (r == inner) return;
            inner = r;
            Relayout();
            Update(); // 끄는 동안 바로 다시 그린다
        }

        void PressPart(Part p)
        {
            Action handler = null;
            switch (p)
            {
                case Part.Size: if (state == State.Idle) ShowSizeMenu(); return;
                case Part.Close: Close(); return;
                case Part.Record: handler = RecordClicked; break;
                case Part.Stop: handler = StopClicked; break;
                case Part.Cancel: handler = CancelClicked; break;
            }
            if (handler != null) handler();
        }

        // ── 키보드: 화살표 1점 이동(Shift 는 크기), Enter 녹화·정지, Esc 닫기 ──

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool shift = (keyData & Keys.Shift) != 0;
            if (state == State.Idle)
            {
                if (key == Keys.Escape) { Close(); return true; }
                if (key == Keys.Enter) { PressPart(Part.Record); return true; }
                if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down)
                {
                    int dx = key == Keys.Left ? -1 : key == Keys.Right ? 1 : 0;
                    int dy = key == Keys.Up ? -1 : key == Keys.Down ? 1 : 0;
                    var r = inner;
                    if (shift)
                    {
                        int step = evenOnly ? 2 : 1;
                        r.Width = Math.Max(MinSide, r.Width + dx * step);
                        r.Height = Math.Max(MinSide, r.Height + dy * step);
                    }
                    else r.Offset(dx, dy);
                    SetInner(r);
                    return true;
                }
            }
            else if (state == State.Recording && key == Keys.Enter) { PressPart(Part.Stop); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ── 크기 고르기 ──

        static readonly int[,] Presets = { { 640, 360 }, { 854, 480 }, { 1280, 720 }, { 1920, 1080 }, { 800, 600 }, { 1080, 1080 }, { 720, 1280 } };
        static readonly string[] PresetNotes = { "", "", "HD", "풀HD", "4:3", "정사각", "세로" };

        void ShowSizeMenu()
        {
            sizeMenu.Items.Clear();
            Font old = menuFont;
            menuFont = new Font("Malgun Gothic", 9f * scale); // 모니터 배율에 맞춘 글자 크기
            sizeMenu.Font = menuFont;
            if (old != null) old.Dispose();
            for (int i = 0; i < Presets.GetLength(0); i++)
            {
                var size = new Size(Presets[i, 0], Presets[i, 1]);
                string label = size.Width + " × " + size.Height + (PresetNotes[i].Length > 0 ? "   (" + PresetNotes[i] + ")" : "");
                var item = new ToolStripMenuItem(label) { Checked = inner.Size == size };
                item.Click += (s, e) => ResizeCentered(size);
                sizeMenu.Items.Add(item);
            }
            sizeMenu.Items.Add(new ToolStripSeparator());
            var custom = new ToolStripMenuItem("직접 입력…");
            custom.Click += (s, e) => BeginInvoke(new Action(AskSize));
            sizeMenu.Items.Add(custom);
            Item chip = items.Find(it => it.Part == Part.Size);
            Rectangle at = chip != null ? chip.Rect : barLocal;
            sizeMenu.Show(this, new Point(at.Left, at.Bottom));
        }

        void AskSize()
        {
            Rectangle v = Native.VirtualScreen;
            using (var dlg = new FrameSizeDialog(inner.Size, evenOnly, Math.Max(v.Width, v.Height)))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) ResizeCentered(dlg.Result);
            }
        }

        /// <summary>가운데를 그대로 두고 크기를 바꾼다. 들어가면 지금 모니터 안에 들어가게 옮긴다.</summary>
        void ResizeCentered(Size size)
        {
            if (state != State.Idle) return;
            Point c = Center(inner);
            Rectangle mon = Screen.FromPoint(c).Bounds;
            var r = new Rectangle(c.X - size.Width / 2, c.Y - size.Height / 2, size.Width, size.Height);
            if (r.Width <= mon.Width) r.X = Math.Max(mon.Left, Math.Min(r.X, mon.Right - r.Width));
            if (r.Height <= mon.Height) r.Y = Math.Max(mon.Top, Math.Min(r.Y, mon.Bottom - r.Height));
            SetInner(r);
        }
    }

    /// <summary>녹화 틀 크기를 숫자로 넣는 대화 상자.</summary>
    sealed class FrameSizeDialog : Form
    {
        readonly SizeInputs inputs;
        public Size Result { get { return inputs.Value; } }

        public FrameSizeDialog(Size current, bool even, int max)
        {
            inputs = new SizeInputs(even, max);
            SuspendLayout();
            Text = "녹화 틀 크기";
            EditUi.Prepare(this, new Size(300, 150));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            TopMost = true;

            var note = new Label { AutoSize = true, Location = new Point(16, 14), Text = even ? "MP4는 가로·세로를 짝수로 맞춰요." : "녹화할 안쪽 크기(화면 점)" };
            var lw = new Label { Text = "가로", AutoSize = true, Location = new Point(16, 50) };
            inputs.Width.Bounds = new Rectangle(52, 46, 80, 24);
            var lx = new Label { Text = "×  세로", AutoSize = true, Location = new Point(140, 50) };
            inputs.Height.Bounds = new Rectangle(190, 46, 80, 24);
            inputs.KeepRatio.Bounds = new Rectangle(16, 80, 120, 24);
            var ok = new Button { Text = "적용", Bounds = new Rectangle(116, 110, 82, 30), Tag = PinkTheme.PrimaryTag, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "취소", Bounds = new Rectangle(204, 110, 82, 30), DialogResult = DialogResult.Cancel };
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { note, lw, inputs.Width, lx, inputs.Height, inputs.KeepRatio, ok, cancel });
            inputs.Set(current);
            inputs.KeepRatio.Checked = false;
            ResumeLayout(false);
            PinkTheme.Apply(this);
            note.ForeColor = PinkTheme.TextMuted;
            foreach (var n in new[] { inputs.Width, inputs.Height }) n.Minimum = FrameWindow.MinSide;
        }
    }
}
