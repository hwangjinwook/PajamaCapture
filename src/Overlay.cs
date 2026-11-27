// 고르기 화면 — 찍어 둔 화면을 어둡게 깔고, 끌어서 영역을 정하거나 창을 골라 범위를 정한다.
// 좌표: 이 창의 안쪽 좌표 = 화면 좌표 - 전체 화면(모든 모니터) 왼쪽 위.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using PinkUi;

namespace PajamaCapture
{
    enum OverlayMode { Area, Window }
    enum OverlayAction { Cancel, Copy, Save, Record }

    sealed class Overlay : Form
    {
        // 강조선·손잡이·도구 막대 색(핑크 테마)
        static readonly Color Edge = PinkTheme.Accent;
        static readonly Color EdgeSoft = PinkTheme.TitleBar;

        readonly OverlayMode mode;
        readonly Bitmap shot, dim;
        readonly Rectangle virtualScreen;
        readonly List<WindowInfo> windows;
        readonly Rectangle[] monitors; // 안쪽 좌표

        public OverlayAction Action { get; private set; }
        /// <summary>고른 범위(화면 좌표).</summary>
        public Rectangle Selection { get; private set; }
        public Bitmap Shot { get { return shot; } }
        public Rectangle VirtualScreen { get { return virtualScreen; } }

        Rectangle sel;           // 고른 범위(안쪽 좌표)
        bool hasSel;
        WindowInfo hover;        // 마우스 아래 창
        Rectangle hoverRect;     // 안쪽 좌표

        enum DragKind { None, New, Move, Resize }
        DragKind drag = DragKind.None;
        int handle = -1;         // 0~7: 왼위·위·오른위·오른·오른아래·아래·왼아래·왼
        Point dragStart;
        Rectangle selStart;
        bool dragged;
        Point mouse;

        sealed class ToolButton { public string Text; public OverlayAction Action; public Rectangle Bounds; public bool Primary; }
        readonly List<ToolButton> buttons = new List<ToolButton>();
        int hotButton = -1, pressedButton = -1;
        Rectangle lastDrawn = Rectangle.Empty;
        float scale = 1f;
        Font font, bold;
        float fontScale;

        public Overlay(OverlayMode mode, Bitmap shot, Rectangle virtualScreen, List<WindowInfo> windows)
        {
            this.mode = mode;
            this.shot = shot;
            this.virtualScreen = virtualScreen;
            this.windows = windows;
            Action = OverlayAction.Cancel;

            var screens = Screen.AllScreens;
            monitors = new Rectangle[screens.Length];
            for (int i = 0; i < screens.Length; i++) monitors[i] = ToClient(screens[i].Bounds);

            dim = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(dim))
            {
                g.DrawImageUnscaled(shot, 0, 0);
                using (var b = new SolidBrush(Color.FromArgb(150, 25, 10, 18))) g.FillRectangle(b, 0, 0, shot.Width, shot.Height);
            }

            Text = "파자마 캡쳐 - 범위 고르기";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            Bounds = virtualScreen;
            Cursor = Cursors.Cross;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);

            mouse = ToClient(Cursor.Position);
            UpdateScale(mouse);
            UpdateHover(mouse);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 모니터마다 배율이 달라도 정확히 전체를 덮도록 실제 좌표로 다시 맞춘다
            Native.SetWindowPos(Handle, new IntPtr(-1), virtualScreen.X, virtualScreen.Y, virtualScreen.Width, virtualScreen.Height, 0x0040);
            Activate();
            Native.SetForegroundWindow(Handle);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                dim.Dispose();
                if (font != null) { font.Dispose(); bold.Dispose(); }
            }
            base.Dispose(disposing);
        }

        Point ToClient(Point screen) { return new Point(screen.X - virtualScreen.X, screen.Y - virtualScreen.Y); }
        Rectangle ToClient(Rectangle r) { r.Offset(-virtualScreen.X, -virtualScreen.Y); return r; }
        Rectangle ToScreen(Rectangle r) { r.Offset(virtualScreen.X, virtualScreen.Y); return r; }
        int S(float px) { return (int)Math.Round(px * scale); }

        Rectangle MonitorOf(Point p)
        {
            foreach (var m in monitors) if (m.Contains(p)) return m;
            Rectangle best = monitors.Length > 0 ? monitors[0] : new Rectangle(0, 0, shot.Width, shot.Height);
            double bestDist = double.MaxValue;
            foreach (var m in monitors)
            {
                double dx = Math.Max(0, Math.Max(m.Left - p.X, p.X - m.Right)), dy = Math.Max(0, Math.Max(m.Top - p.Y, p.Y - m.Bottom));
                double d = dx * dx + dy * dy;
                if (d < bestDist) { bestDist = d; best = m; }
            }
            return best;
        }

        void UpdateScale(Point clientPoint)
        {
            var p = clientPoint;
            p.Offset(virtualScreen.X, virtualScreen.Y);
            scale = Math.Max(1f, Native.ScaleAt(p));
            if (font == null || fontScale != scale)
            {
                if (font != null) { font.Dispose(); bold.Dispose(); }
                font = new Font("Malgun Gothic", 13f * scale, GraphicsUnit.Pixel);
                bold = new Font("Malgun Gothic", 13f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
                fontScale = scale;
            }
        }

        void UpdateHover(Point p)
        {
            var screen = p;
            screen.Offset(virtualScreen.X, virtualScreen.Y);
            hover = WindowFinder.At(windows, screen);
            hoverRect = hover == null ? Rectangle.Empty : ToClient(hover.Bounds);
            hoverRect.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
        }

        // ── 다시 그리기: 바뀐 부분만 ──

        void Redraw()
        {
            UpdateScale(hasSel ? Center(sel) : mouse);
            LayoutButtons();
            Rectangle now = DrawnArea();
            Rectangle area = lastDrawn.IsEmpty ? now : now.IsEmpty ? lastDrawn : Rectangle.Union(lastDrawn, now);
            if (!area.IsEmpty) Invalidate(area);
            lastDrawn = now;
        }

        static Point Center(Rectangle r) { return new Point(r.X + r.Width / 2, r.Y + r.Height / 2); }

        Rectangle Bright { get { return hasSel ? sel : (drag == DragKind.New && dragged) ? sel : hoverRect; } }

        Rectangle DrawnArea()
        {
            var parts = new List<Rectangle>();
            Rectangle b = Bright;
            if (!b.IsEmpty)
            {
                parts.Add(Rectangle.Inflate(b, S(8), S(8)));
                parts.Add(LabelRect(b, LabelText(b)));
            }
            foreach (var t in buttons) parts.Add(Rectangle.Inflate(t.Bounds, S(4), S(4)));
            if (!hasSel) parts.Add(Rectangle.Inflate(HintRect(), S(2), S(2)));
            Rectangle all = Rectangle.Empty;
            foreach (var r in parts)
            {
                if (r.IsEmpty) continue;
                all = all.IsEmpty ? r : Rectangle.Union(all, r);
            }
            return all;
        }

        string HintText()
        {
            if (mode == OverlayMode.Window) return "캡쳐할 창을 클릭하세요  ·  Esc 취소";
            return "끌어서 영역을 정하세요  ·  창 위에서 클릭하면 창 범위  ·  Esc 취소";
        }

        Rectangle HintRect()
        {
            Rectangle m = MonitorOf(mouse);
            Size t = TextRenderer.MeasureText(HintText(), bold);
            int w = t.Width + S(32), h = t.Height + S(16);
            return new Rectangle(m.X + (m.Width - w) / 2, m.Y + S(24), w, h);
        }

        string LabelText(Rectangle b)
        {
            string size = b.Width + " × " + b.Height;
            if (!hasSel && !(drag == DragKind.New && dragged) && hover != null)
            {
                string title = hover.Title;
                if (title.Length > 40) title = title.Substring(0, 39) + "…";
                return title + "   " + size;
            }
            return size;
        }

        Rectangle LabelRect(Rectangle b, string text)
        {
            Size t = TextRenderer.MeasureText(text, font);
            int w = t.Width + S(14), h = t.Height + S(6);
            Rectangle m = MonitorOf(b.Location);
            int x = Math.Max(m.Left, Math.Min(b.Left, m.Right - w));
            int y = b.Top - h - S(6);
            if (y < m.Top) y = b.Top + S(6); // 위에 자리가 없으면 범위 안쪽 위
            return new Rectangle(x, y, w, h);
        }

        void LayoutButtons()
        {
            buttons.Clear();
            if (!hasSel || drag != DragKind.None) return;
            Add("복사  (Enter)", OverlayAction.Copy, true);
            Add("저장…", OverlayAction.Save, false);
            Add("● 녹화", OverlayAction.Record, false); // 고른 범위를 그대로 녹화(끌어서 범위 정해 녹화하는 유일한 길)
            Add("취소", OverlayAction.Cancel, false);

            int gap = S(6), h = S(34), total = -gap;
            foreach (var t in buttons)
            {
                t.Bounds.Width = TextRenderer.MeasureText(t.Text, bold).Width + S(24);
                t.Bounds.Height = h;
                total += t.Bounds.Width + gap;
            }
            int pad = S(6);
            Rectangle m = MonitorOf(Center(sel));
            int x = sel.Right - total;
            x = Math.Max(m.Left + pad, Math.Min(x, m.Right - total - pad));
            int y = sel.Bottom + S(10);
            if (y + h + pad > m.Bottom) y = sel.Top - S(10) - h;          // 아래가 모자라면 위
            if (y < m.Top + pad) y = sel.Bottom - S(10) - h;              // 위도 모자라면 범위 안쪽 아래
            foreach (var t in buttons)
            {
                t.Bounds.X = x;
                t.Bounds.Y = y;
                x += t.Bounds.Width + gap;
            }
        }

        void Add(string text, OverlayAction action, bool primary)
        {
            buttons.Add(new ToolButton { Text = text, Action = action, Primary = primary });
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            PaintScene(e.Graphics, e.ClipRectangle);
        }

        /// <summary>장면 전체를 그린다(시험에서 그림으로 뽑을 때도 쓴다).</summary>
        public void PaintScene(Graphics g, Rectangle clip)
        {
            clip.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (!clip.IsEmpty) g.DrawImage(dim, clip, clip, GraphicsUnit.Pixel);

            Rectangle b = Bright;
            Rectangle lit = Rectangle.Intersect(b, clip);
            if (!lit.IsEmpty) g.DrawImage(shot, lit, lit, GraphicsUnit.Pixel);
            g.CompositingMode = CompositingMode.SourceOver;
            g.PixelOffsetMode = PixelOffsetMode.Default;

            if (!b.IsEmpty)
            {
                bool chosen = hasSel || (drag == DragKind.New && dragged);
                using (var pen = new Pen(chosen ? Edge : EdgeSoft, Math.Max(2, S(2))))
                {
                    if (!chosen) pen.DashPattern = new float[] { 3, 2 };
                    int o = (int)Math.Ceiling(pen.Width / 2);
                    g.DrawRectangle(pen, b.X - o, b.Y - o, b.Width + o * 2 - 1, b.Height + o * 2 - 1);
                }
                if (hasSel) DrawHandles(g);
                string label = LabelText(b);
                DrawPill(g, LabelRect(b, label), label, font, PinkTheme.Accent, PinkTheme.OnPink);
            }

            if (!hasSel)
            {
                var hint = HintRect();
                DrawPill(g, hint, HintText(), bold, Color.FromArgb(235, 255, 240, 243), PinkTheme.Text);
            }

            for (int i = 0; i < buttons.Count; i++)
            {
                var t = buttons[i];
                bool hot = i == hotButton;
                Color back = t.Primary ? (hot ? PinkTheme.TitleBar : PinkTheme.Accent) : (hot ? PinkTheme.SoftPink : PinkTheme.Surface);
                DrawPill(g, t.Bounds, t.Text, bold, back, t.Primary ? PinkTheme.OnPink : PinkTheme.Text, t.Primary ? PinkTheme.Accent : PinkTheme.TitleBar);
            }
        }

        void DrawHandles(Graphics g)
        {
            int size = Math.Max(7, S(8));
            using (var fill = new SolidBrush(Color.White))
            using (var pen = new Pen(Edge, Math.Max(1, S(1.5f))))
            {
                foreach (var p in HandlePoints(sel))
                {
                    var r = new Rectangle(p.X - size / 2, p.Y - size / 2, size, size);
                    g.FillRectangle(fill, r);
                    g.DrawRectangle(pen, r);
                }
            }
        }

        void DrawPill(Graphics g, Rectangle r, string text, Font f, Color back, Color fore, Color? border = null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(r, S(7)))
            {
                using (var b = new SolidBrush(back)) g.FillPath(b, path);
                if (border.HasValue) using (var pen = new Pen(border.Value, Math.Max(1, S(1)))) g.DrawPath(pen, path);
            }
            g.SmoothingMode = SmoothingMode.None;
            TextRenderer.DrawText(g, text, f, r, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
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

        static Point[] HandlePoints(Rectangle r)
        {
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            return new[] {
                new Point(r.Left, r.Top), new Point(cx, r.Top), new Point(r.Right, r.Top), new Point(r.Right, cy),
                new Point(r.Right, r.Bottom), new Point(cx, r.Bottom), new Point(r.Left, r.Bottom), new Point(r.Left, cy) };
        }

        int HandleAt(Point p)
        {
            if (!hasSel) return -1;
            var pts = HandlePoints(sel);
            int reach = Math.Max(6, S(8));
            for (int i = 0; i < pts.Length; i++)
                if (Math.Abs(p.X - pts[i].X) <= reach && Math.Abs(p.Y - pts[i].Y) <= reach) return i;
            return -1;
        }

        int ButtonAt(Point p)
        {
            for (int i = 0; i < buttons.Count; i++) if (buttons[i].Bounds.Contains(p)) return i;
            return -1;
        }

        static readonly Cursor[] HandleCursors = {
            Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
            Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE };

        // ── 마우스 ──

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            mouse = e.Location;
            if (e.Button == MouseButtons.Right)
            {
                if (hasSel && mode != OverlayMode.Window) { hasSel = false; drag = DragKind.None; UpdateHover(mouse); Redraw(); }
                else Finish(OverlayAction.Cancel);
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            pressedButton = ButtonAt(e.Location);
            if (pressedButton >= 0) return;
            if (mode == OverlayMode.Window) return; // 놓을 때 고른다

            handle = HandleAt(e.Location);
            dragStart = e.Location;
            selStart = sel;
            dragged = false;
            if (handle >= 0) drag = DragKind.Resize;
            else if (hasSel && sel.Contains(e.Location)) drag = DragKind.Move;
            else drag = DragKind.New;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            mouse = ClampPoint(e.Location);
            var full = new Rectangle(0, 0, shot.Width, shot.Height);
            switch (drag)
            {
                case DragKind.New:
                    if (!dragged && (Math.Abs(mouse.X - dragStart.X) > 3 || Math.Abs(mouse.Y - dragStart.Y) > 3))
                    {
                        dragged = true;
                        hasSel = false;
                    }
                    if (dragged)
                    {
                        sel = Rectangle.FromLTRB(Math.Min(dragStart.X, mouse.X), Math.Min(dragStart.Y, mouse.Y),
                                                 Math.Max(dragStart.X, mouse.X) + 1, Math.Max(dragStart.Y, mouse.Y) + 1);
                        sel.Intersect(full);
                    }
                    break;
                case DragKind.Move:
                    {
                        var r = selStart;
                        r.X = Math.Max(0, Math.Min(selStart.X + mouse.X - dragStart.X, full.Width - r.Width));
                        r.Y = Math.Max(0, Math.Min(selStart.Y + mouse.Y - dragStart.Y, full.Height - r.Height));
                        sel = r;
                        dragged = true;
                    }
                    break;
                case DragKind.Resize:
                    {
                        int l = selStart.Left, t = selStart.Top, r = selStart.Right, b = selStart.Bottom;
                        int dx = mouse.X - dragStart.X, dy = mouse.Y - dragStart.Y;
                        if (handle == 0 || handle == 6 || handle == 7) l += dx;
                        if (handle == 2 || handle == 3 || handle == 4) r += dx;
                        if (handle == 0 || handle == 1 || handle == 2) t += dy;
                        if (handle == 4 || handle == 5 || handle == 6) b += dy;
                        var nr = Rectangle.FromLTRB(Math.Min(l, r), Math.Min(t, b), Math.Max(l, r), Math.Max(t, b));
                        nr.Intersect(full);
                        if (nr.Width < 1) nr.Width = 1;
                        if (nr.Height < 1) nr.Height = 1;
                        sel = nr;
                        dragged = true;
                    }
                    break;
                default:
                    if (!hasSel) UpdateHover(mouse);
                    break;
            }
            UpdateCursor();
            Redraw();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            Capture = false;
            mouse = ClampPoint(e.Location);

            if (pressedButton >= 0)
            {
                int hit = ButtonAt(mouse);
                int pressed = pressedButton;
                pressedButton = -1;
                if (hit == pressed) Finish(buttons[hit].Action);
                return;
            }

            if (mode == OverlayMode.Window)
            {
                UpdateHover(mouse);
                if (!hoverRect.IsEmpty) { sel = hoverRect; hasSel = true; Finish(OverlayAction.Copy); }
                return;
            }

            if (drag == DragKind.New)
            {
                if (dragged && sel.Width >= 2 && sel.Height >= 2) hasSel = true;
                else if (!dragged && !hoverRect.IsEmpty)
                {
                    UpdateHover(mouse);
                    sel = hoverRect;   // 클릭만 하면 그 창(또는 모니터) 범위를 고른다
                    hasSel = true;
                }
                else hasSel = false;
            }
            drag = DragKind.None;
            if (!hasSel) UpdateHover(mouse);
            UpdateCursor();
            Redraw();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && hasSel && sel.Contains(e.Location) && mode != OverlayMode.Window)
                Finish(OverlayAction.Copy);
        }

        Point ClampPoint(Point p)
        {
            return new Point(Math.Max(0, Math.Min(p.X, shot.Width - 1)), Math.Max(0, Math.Min(p.Y, shot.Height - 1)));
        }

        void UpdateCursor()
        {
            int b = ButtonAt(mouse);
            if (b != hotButton) hotButton = b;
            if (drag == DragKind.Move) { Cursor = Cursors.SizeAll; return; }
            if (drag == DragKind.Resize) { Cursor = HandleCursors[handle]; return; }
            if (drag == DragKind.New) { Cursor = Cursors.Cross; return; }
            if (b >= 0) { Cursor = Cursors.Hand; return; }
            if (mode == OverlayMode.Window) { Cursor = Cursors.Hand; return; }
            int h = HandleAt(mouse);
            if (h >= 0) Cursor = HandleCursors[h];
            else if (hasSel && sel.Contains(mouse)) Cursor = Cursors.SizeAll;
            else Cursor = Cursors.Cross;
        }

        // ── 키보드: Enter 복사, Esc 취소, 화살표 1점 이동(Shift 는 크기) ──

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool shift = (keyData & Keys.Shift) != 0;
            if (key == Keys.Escape) { Finish(OverlayAction.Cancel); return true; }
            if (key == Keys.Enter && hasSel) { Finish(OverlayAction.Copy); return true; }
            if (hasSel && (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down))
            {
                int dx = key == Keys.Left ? -1 : key == Keys.Right ? 1 : 0;
                int dy = key == Keys.Up ? -1 : key == Keys.Down ? 1 : 0;
                var r = sel;
                if (shift) { r.Width = Math.Max(1, r.Width + dx); r.Height = Math.Max(1, r.Height + dy); }
                else { r.X += dx; r.Y += dy; }
                var full = new Rectangle(0, 0, shot.Width, shot.Height);
                if (full.Contains(r)) { sel = r; Redraw(); }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void Finish(OverlayAction action)
        {
            if (action != OverlayAction.Cancel && (!hasSel || sel.Width < 1 || sel.Height < 1)) return;
            Action = action;
            Selection = action == OverlayAction.Cancel ? Rectangle.Empty : ToScreen(sel);
            Close();
        }

        // ── 시험용: 범위를 정한 상태를 만든다 ──
        internal void TestSelect(Rectangle screenRect)
        {
            sel = ToClient(screenRect);
            hasSel = true;
            Redraw();
        }

        internal void TestHover(Point screenPoint)
        {
            hasSel = false;
            mouse = ToClient(screenPoint);
            UpdateHover(mouse);
            Redraw();
        }
    }
}
