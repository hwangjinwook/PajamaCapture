// 간단 편집 화면 — GIF 편집 창(도구 막대·프레임 목록·미리보기·재생·저장), MP4 크기 바꾸기 창, 둘이 함께 쓰는 대화 상자.
// 꿀캠 편집 화면처럼 왼쪽에 프레임 목록, 가운데에 미리보기, 아래에 재생·저장을 둔다. 자세한 편집은 「파자마컷」에서 한다.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using PinkUi;

namespace PajamaCapture
{
    static class EditUi
    {
        public const string AppTitle = "파자마 캡쳐";

        public static void Prepare(Form f, Size client)
        {
            f.Font = new Font("Malgun Gothic", 9f);
            f.AutoScaleDimensions = new SizeF(96f, 96f);
            f.AutoScaleMode = AutoScaleMode.Dpi;
            f.ClientSize = client;
            try { f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
        }

        public static Button ToolButton(string text, string tip, ToolTip tips)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 30), Padding = new Padding(4, 0, 4, 0), Margin = new Padding(0, 0, 6, 0) };
            if (tip != null) tips.SetToolTip(b, tip);
            return b;
        }

        public static Control Separator()
        {
            return new Label { AutoSize = false, Width = 1, Height = 26, Margin = new Padding(4, 2, 10, 0), BackColor = PinkTheme.Line };
        }

        public static string Seconds(long ms) { return (ms / 1000.0).ToString("0.00") + "초"; }

        public static string Bytes(long n)
        {
            if (n >= 1024 * 1024) return string.Format("{0:0.0}MB", n / 1048576.0);
            return string.Format("{0:0}KB", Math.Max(1, n / 1024.0));
        }

        /// <summary>「원래이름_편집.확장자」, 있으면 「_편집_2」…</summary>
        public static string SuggestName(string source)
        {
            string dir = Path.GetDirectoryName(source), stem = Path.GetFileNameWithoutExtension(source), ext = Path.GetExtension(source);
            string path = Path.Combine(dir, stem + "_편집" + ext);
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, stem + "_편집_" + i + ext);
            return path;
        }
    }

    /// <summary>가로·세로 입력 칸과 「비율 유지」 — GIF 캔버스 크기 대화 상자와 MP4 창이 함께 쓴다.</summary>
    sealed class SizeInputs
    {
        public readonly NumericUpDown Width = new NumericUpDown(), Height = new NumericUpDown();
        public readonly CheckBox KeepRatio = new PinkCheckBox();
        readonly bool even;
        Size ratio;
        bool syncing;

        public SizeInputs(bool even, int max)
        {
            this.even = even;
            foreach (var n in new[] { Width, Height })
            {
                n.Minimum = even ? 2 : 1;
                n.Maximum = max;
                n.Increment = even ? 2 : 1;
                n.TextAlign = HorizontalAlignment.Right;
            }
            KeepRatio.Text = "비율 유지";
            KeepRatio.Checked = true;
            Width.ValueChanged += (s, e) => Follow(true);
            Height.ValueChanged += (s, e) => Follow(false);
            KeepRatio.CheckedChanged += (s, e) => { if (KeepRatio.Checked) ratio = Value; };
        }

        /// <summary>값을 넣고 이 값을 비율 기준으로 삼는다.</summary>
        public void Set(Size size)
        {
            syncing = true;
            Width.Value = Clamp(Width, size.Width);
            Height.Value = Clamp(Height, size.Height);
            syncing = false;
            ratio = new Size((int)Width.Value, (int)Height.Value);
        }

        /// <summary>비율 기준은 그대로 두고 크기만 바꾼다(빠른 비율 버튼용).</summary>
        public void SetScaled(Size basis, double factor)
        {
            syncing = true;
            Width.Value = Clamp(Width, (int)Math.Round(basis.Width * factor));
            Height.Value = Clamp(Height, (int)Math.Round(basis.Height * factor));
            syncing = false;
        }

        public Size Value
        {
            get
            {
                int w = (int)Width.Value, h = (int)Height.Value;
                if (even) { w = Mp4Tool.Even(w); h = Mp4Tool.Even(h); }
                return new Size(w, h);
            }
        }

        void Follow(bool fromWidth)
        {
            if (syncing || !KeepRatio.Checked || ratio.Width < 1 || ratio.Height < 1) return;
            syncing = true;
            if (fromWidth) Height.Value = Clamp(Height, (int)Math.Round((double)Width.Value * ratio.Height / ratio.Width));
            else Width.Value = Clamp(Width, (int)Math.Round((double)Height.Value * ratio.Width / ratio.Height));
            syncing = false;
        }

        decimal Clamp(NumericUpDown n, int v)
        {
            if (even) v = Mp4Tool.Even(v);
            return Math.Max(n.Minimum, Math.Min(n.Maximum, v));
        }
    }

    /// <summary>GIF 캔버스 크기 대화 상자.</summary>
    sealed class CanvasSizeDialog : Form
    {
        readonly SizeInputs inputs = new SizeInputs(false, 8000);
        public Size Result { get { return inputs.Value; } }

        public CanvasSizeDialog(Size current, Size basis)
        {
            SuspendLayout();
            Text = "캔버스 크기";
            EditUi.Prepare(this, new Size(360, 196));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            var note = new Label { AutoSize = true, Location = new Point(16, 14), Text = "지금 크기 " + current.Width + " × " + current.Height + "  ·  기준(잘라낸 크기) " + basis.Width + " × " + basis.Height };
            var lw = new Label { Text = "가로", AutoSize = true, Location = new Point(16, 50) };
            inputs.Width.Bounds = new Rectangle(52, 46, 80, 24);
            var lx = new Label { Text = "×  세로", AutoSize = true, Location = new Point(140, 50) };
            inputs.Height.Bounds = new Rectangle(190, 46, 80, 24);
            inputs.KeepRatio.Bounds = new Rectangle(16, 82, 120, 24);
            var quick = new FlowLayoutPanel { Location = new Point(16, 114), AutoSize = true, WrapContents = false };
            foreach (int pct in new[] { 25, 50, 75, 100 })
            {
                int p = pct;
                var b = new Button { Text = p == 100 ? "기준 크기" : p + "%", AutoSize = true, MinimumSize = new Size(56, 28), Margin = new Padding(0, 0, 6, 0) };
                b.Click += (s, e) => inputs.SetScaled(basis, p / 100.0);
                quick.Controls.Add(b);
            }
            var ok = new Button { Text = "적용", Bounds = new Rectangle(176, 156, 82, 30), Tag = PinkTheme.PrimaryTag, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "취소", Bounds = new Rectangle(264, 156, 82, 30), DialogResult = DialogResult.Cancel };
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { note, lw, inputs.Width, lx, inputs.Height, inputs.KeepRatio, quick, ok, cancel });
            inputs.Set(current);
            ResumeLayout(false);
            PinkTheme.Apply(this);
            note.ForeColor = PinkTheme.TextMuted;
        }
    }

    /// <summary>프레임 시간 대화 상자 — 고른 프레임/전체, 같은 시간으로 맞추기/빠르기 바꾸기.</summary>
    sealed class DelayDialog : Form
    {
        readonly RadioButton rbSelected = new RadioButton(), rbAll = new RadioButton(), rbFixed = new RadioButton(), rbSpeed = new RadioButton();
        readonly NumericUpDown seconds = new NumericUpDown(), percent = new NumericUpDown();

        public bool AllFrames { get { return rbAll.Checked; } }
        public bool Fixed { get { return rbFixed.Checked; } }
        public int DelayCs { get { return (int)Math.Round(seconds.Value * 100); } }
        public int SpeedPercent { get { return (int)percent.Value; } }

        public DelayDialog(int selectedCount, int totalCount, int currentCs)
        {
            SuspendLayout();
            Text = "프레임 시간";
            EditUi.Prepare(this, new Size(400, 262));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            var gTarget = new PinkGroupBox { Text = "바꿀 프레임", Bounds = new Rectangle(16, 10, 368, 84) };
            rbSelected.Text = "고른 프레임 (" + selectedCount + "장)";
            rbSelected.Bounds = new Rectangle(12, 24, 340, 24);
            rbAll.Text = "전체 프레임 (" + totalCount + "장)";
            rbAll.Bounds = new Rectangle(12, 50, 340, 24);
            gTarget.Controls.AddRange(new Control[] { rbSelected, rbAll });

            var gHow = new PinkGroupBox { Text = "바꾸는 방법", Bounds = new Rectangle(16, 102, 368, 108) };
            rbFixed.Text = "모두 이 시간으로";
            rbFixed.Bounds = new Rectangle(12, 26, 140, 24);
            seconds.DecimalPlaces = 2;
            seconds.Increment = 0.01m;
            seconds.Minimum = GifProject.MinDelayCs / 100m;
            seconds.Maximum = 600m;
            seconds.Value = Math.Max(seconds.Minimum, currentCs / 100m);
            seconds.TextAlign = HorizontalAlignment.Right;
            seconds.Bounds = new Rectangle(156, 25, 70, 24);
            var lSec = new Label { Text = "초", AutoSize = true, Location = new Point(232, 29) };
            rbSpeed.Text = "빠르기를";
            rbSpeed.Bounds = new Rectangle(12, 60, 140, 24);
            percent.Minimum = 10;
            percent.Maximum = 1000;
            percent.Increment = 10;
            percent.Value = 200;
            percent.TextAlign = HorizontalAlignment.Right;
            percent.Bounds = new Rectangle(156, 59, 70, 24);
            var lPct = new Label { Text = "% 로", AutoSize = true, Location = new Point(232, 63) };
            var hint = new Label { Text = "200%는 두 배 빠르게, 50%는 두 배 느리게", AutoSize = true, Location = new Point(30, 86) };
            gHow.Controls.AddRange(new Control[] { rbFixed, seconds, lSec, rbSpeed, percent, lPct, hint });

            var ok = new Button { Text = "적용", Bounds = new Rectangle(216, 222, 82, 30), Tag = PinkTheme.PrimaryTag, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "취소", Bounds = new Rectangle(304, 222, 82, 30), DialogResult = DialogResult.Cancel };
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { gTarget, gHow, ok, cancel });

            if (selectedCount > 0) rbSelected.Checked = true; else rbAll.Checked = true;
            rbSelected.Enabled = selectedCount > 0;
            rbFixed.Checked = true;
            seconds.Enter += (s, e) => rbFixed.Checked = true;
            percent.Enter += (s, e) => rbSpeed.Checked = true;
            ResumeLayout(false);
            PinkTheme.Apply(this);
            hint.ForeColor = PinkTheme.TextMuted;
        }
    }

    /// <summary>미리보기 — 평소에는 편집 결과(잘라낸 범위를 저장 크기로)를, 잘라내기 중에는 원본 전체와 고른 범위를 보여 준다.</summary>
    sealed class PreviewBox : Control
    {
        public Bitmap Image;       // 원본 크기 프레임(편집 창이 가지고 있다)
        public Rectangle Crop;     // 원본 좌표
        public Size Output;
        public string Message;
        bool cropMode;
        Rectangle selection;       // 원본 좌표
        Rectangle shown;           // 그림이 그려진 화면 범위
        float scale = 1f;
        enum Drag { None, New, Move }
        Drag drag;
        Point dragStart;
        Rectangle dragOrigin;

        public event EventHandler SelectionChanged;

        public PreviewBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = PinkTheme.DisabledBack;
            TabStop = true;
        }

        public bool CropMode
        {
            get { return cropMode; }
            set { cropMode = value; drag = Drag.None; Cursor = value ? Cursors.Cross : Cursors.Default; Invalidate(); }
        }

        public Rectangle Selection
        {
            get { return selection; }
            set { selection = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (Image == null)
            {
                TextRenderer.DrawText(g, Message ?? "", Font, ClientRectangle, PinkTheme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }
            Rectangle src = cropMode ? new Rectangle(0, 0, Image.Width, Image.Height) : Crop;
            Size size = cropMode ? Image.Size : Output;
            int margin = 12;
            float k = Math.Min((ClientSize.Width - margin * 2f) / size.Width, (ClientSize.Height - margin * 2f) / size.Height);
            k = Math.Max(0.01f, Math.Min(1f, k));
            int w = Math.Max(1, (int)(size.Width * k)), h = Math.Max(1, (int)(size.Height * k));
            shown = new Rectangle((ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
            scale = (float)w / size.Width;
            g.InterpolationMode = k < 1f || size != src.Size ? InterpolationMode.HighQualityBilinear : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(Image, shown, src, GraphicsUnit.Pixel);
            using (var pen = new Pen(PinkTheme.Line)) g.DrawRectangle(pen, shown.X - 1, shown.Y - 1, shown.Width + 1, shown.Height + 1);

            if (!cropMode) return;
            if (selection.Width > 0 && selection.Height > 0)
            {
                Rectangle s = ToScreen(selection);
                using (var region = new Region(shown))
                using (var dim = new SolidBrush(Color.FromArgb(130, 0, 0, 0)))
                {
                    region.Exclude(s);
                    g.FillRegion(dim, region);
                }
                using (var pen = new Pen(PinkTheme.Accent, 2f)) g.DrawRectangle(pen, s.X, s.Y, s.Width - 1, s.Height - 1);
                string label = selection.Width + " × " + selection.Height;
                Size t = TextRenderer.MeasureText(label, Font);
                var tag = new Rectangle(s.X, Math.Max(0, s.Y - t.Height - 4), t.Width + 8, t.Height + 2);
                using (var b = new SolidBrush(PinkTheme.Accent)) g.FillRectangle(b, tag);
                TextRenderer.DrawText(g, label, Font, tag, PinkTheme.OnPink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            else
            {
                using (var dim = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillRectangle(dim, shown);
            }
        }

        Rectangle ToScreen(Rectangle r)
        {
            int x1 = shown.X + (int)Math.Round(r.Left * scale), y1 = shown.Y + (int)Math.Round(r.Top * scale);
            int x2 = shown.X + (int)Math.Round(r.Right * scale), y2 = shown.Y + (int)Math.Round(r.Bottom * scale);
            return Rectangle.FromLTRB(x1, y1, Math.Max(x1 + 1, x2), Math.Max(y1 + 1, y2));
        }

        Point ToSource(Point p)
        {
            int x = (int)Math.Round((p.X - shown.X) / scale), y = (int)Math.Round((p.Y - shown.Y) / scale);
            return new Point(Math.Max(0, Math.Min(Image.Width, x)), Math.Max(0, Math.Min(Image.Height, y)));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (!cropMode || Image == null || e.Button != MouseButtons.Left) return;
            Point p = ToSource(e.Location);
            bool full = selection == new Rectangle(0, 0, Image.Width, Image.Height);
            drag = selection.Width > 0 && !full && selection.Contains(p) ? Drag.Move : Drag.New;
            dragStart = p;
            dragOrigin = selection;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!cropMode || Image == null) return;
            Point p = ToSource(e.Location);
            if (drag == Drag.None)
            {
                bool full = selection == new Rectangle(0, 0, Image.Width, Image.Height);
                Cursor = selection.Width > 0 && !full && selection.Contains(p) ? Cursors.SizeAll : Cursors.Cross;
                return;
            }
            if (drag == Drag.New)
                selection = Rectangle.FromLTRB(Math.Min(p.X, dragStart.X), Math.Min(p.Y, dragStart.Y), Math.Max(p.X, dragStart.X), Math.Max(p.Y, dragStart.Y));
            else
            {
                int x = Math.Max(0, Math.Min(Image.Width - dragOrigin.Width, dragOrigin.X + p.X - dragStart.X));
                int y = Math.Max(0, Math.Min(Image.Height - dragOrigin.Height, dragOrigin.Y + p.Y - dragStart.Y));
                selection = new Rectangle(x, y, dragOrigin.Width, dragOrigin.Height);
            }
            Invalidate();
            var h = SelectionChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (drag == Drag.New && (selection.Width < 2 || selection.Height < 2)) // 클릭만 했으면 고르기를 비운다
            {
                selection = Rectangle.Empty;
                Invalidate();
                var h = SelectionChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
            drag = Drag.None;
        }
    }

    /// <summary>GIF 간단 편집 창.</summary>
    sealed class GifEditorForm : Form
    {
        public const string TitlePrefix = "파자마 캡쳐 - 간단 편집";

        readonly string path;
        GifProject project;
        readonly ToolTip tips = new ToolTip();
        readonly FlowLayoutPanel tools = new FlowLayoutPanel(), cropTools = new FlowLayoutPanel();
        readonly Button btnEven, btnOdd, btnDelete, btnDelay, btnSize, btnCrop, btnUndo;
        readonly Button btnCropApply, btnCropCancel;
        readonly NumericUpDown nX = new NumericUpDown(), nY = new NumericUpDown(), nW = new NumericUpDown(), nH = new NumericUpDown();
        readonly ListView list = new ListView();
        readonly PreviewBox preview = new PreviewBox();
        readonly Label info = new Label(), status = new Label();
        readonly Button btnPlay = new Button(), btnSave = new Button(), btnClose = new Button();
        readonly System.Windows.Forms.Timer playTimer = new System.Windows.Forms.Timer();
        int current = -1, shownSource = -1;
        bool busy, cropMode, syncing, dirty, pendingShow;
        volatile bool closing;

        public GifEditorForm(string path)
        {
            this.path = path;
            SuspendLayout();
            Text = TitlePrefix + " - " + Path.GetFileName(path);
            EditUi.Prepare(this, new Size(1040, 700));
            MinimumSize = new Size(780, 520);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            // 도구 막대
            btnEven = EditUi.ToolButton("짝수 프레임만", "2·4·6…번째만 남겨요. 지운 프레임 시간은 앞 프레임에 더해 재생 시간은 그대로예요.", tips);
            btnOdd = EditUi.ToolButton("홀수 프레임만", "1·3·5…번째만 남겨요. 지운 프레임 시간은 앞 프레임에 더해 재생 시간은 그대로예요.", tips);
            btnDelete = EditUi.ToolButton("고른 프레임 삭제", "목록에서 고른 프레임을 지워요 (Delete)", tips);
            btnDelay = EditUi.ToolButton("프레임 시간…", "고른 프레임이나 전체 프레임의 표시 시간을 바꿔요", tips);
            btnSize = EditUi.ToolButton("캔버스 크기…", "가로·세로 크기를 바꿔요(비율 유지 가능)", tips);
            btnCrop = EditUi.ToolButton("영역 잘라내기", "미리보기에서 끌어서 남길 영역을 골라요", tips);
            btnUndo = EditUi.ToolButton("되돌리기", "바로 전 편집을 되돌려요 (Ctrl+Z)", tips);
            ConfigureBar(tools);
            tools.Controls.AddRange(new Control[] { btnEven, btnOdd, btnDelete, EditUi.Separator(), btnDelay, btnSize, btnCrop, EditUi.Separator(), btnUndo });

            ConfigureBar(cropTools);
            cropTools.Visible = false;
            var cropHint = new Label { Text = "미리보기에서 끌어서 남길 영역을 고르세요.", AutoSize = true, Margin = new Padding(0, 7, 12, 0) };
            cropTools.Controls.Add(cropHint);
            foreach (var pair in new[] { Tuple.Create("X", nX), Tuple.Create("Y", nY), Tuple.Create("너비", nW), Tuple.Create("높이", nH) })
            {
                cropTools.Controls.Add(new Label { Text = pair.Item1, AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
                pair.Item2.Width = 64;
                pair.Item2.Margin = new Padding(0, 3, 10, 0);
                pair.Item2.TextAlign = HorizontalAlignment.Right;
                pair.Item2.ValueChanged += (s, e) => CropFieldsChanged();
                cropTools.Controls.Add(pair.Item2);
            }
            btnCropApply = EditUi.ToolButton("잘라내기 적용 (Enter)", null, tips);
            btnCropApply.Tag = PinkTheme.PrimaryTag;
            btnCropCancel = EditUi.ToolButton("취소 (Esc)", null, tips);
            cropTools.Controls.AddRange(new Control[] { btnCropApply, btnCropCancel });

            // 왼쪽 프레임 목록
            var left = new Panel { Dock = DockStyle.Left, Width = 246, Padding = new Padding(10, 8, 0, 0) };
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.MultiSelect = true;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.Columns.Add("번호", 140);
            list.Columns.Add("프레임 시간", 70, HorizontalAlignment.Right);
            var listHint = new Label { Dock = DockStyle.Bottom, Height = 22, Text = "Ctrl·Shift 로 여러 장 고르기", TextAlign = ContentAlignment.MiddleLeft };
            left.Controls.Add(list);
            left.Controls.Add(listHint);

            // 가운데 미리보기
            var center = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 10, 0) };
            info.Dock = DockStyle.Top;
            info.Height = 24;
            info.TextAlign = ContentAlignment.MiddleRight;
            preview.Dock = DockStyle.Fill;
            preview.Message = "불러오는 중…";
            center.Controls.Add(preview);
            center.Controls.Add(info);

            // 아래 재생·저장
            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 52, ColumnCount = 4, RowCount = 1, Padding = new Padding(10, 9, 10, 9) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnPlay.Text = "▶ 재생";
            btnSave.Text = "다른 이름으로 저장…";
            btnSave.Tag = PinkTheme.PrimaryTag;
            btnClose.Text = "편집 닫기";
            foreach (var b in new[] { btnPlay, btnSave, btnClose })
            {
                b.AutoSize = true;
                b.MinimumSize = new Size(88, 32);
                b.Dock = DockStyle.Fill;
                b.Margin = new Padding(0, 0, 8, 0);
            }
            tips.SetToolTip(btnPlay, "미리보기를 재생하거나 멈춰요 (Space)");
            tips.SetToolTip(btnSave, "편집 결과를 새 GIF 파일로 저장해요 (Ctrl+S)");
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.AutoEllipsis = true;
            status.Margin = new Padding(4, 0, 8, 0);
            bottom.Controls.Add(btnPlay, 0, 0);
            bottom.Controls.Add(status, 1, 0);
            bottom.Controls.Add(btnSave, 2, 0);
            bottom.Controls.Add(btnClose, 3, 0);

            // 붙이는 순서: 채우기 → 왼쪽 → 아래 → 위(나중에 넣은 것이 먼저 자리를 잡는다)
            Controls.Add(center);
            Controls.Add(left);
            Controls.Add(bottom);
            Controls.Add(cropTools);
            Controls.Add(tools);
            ResumeLayout(false);
            PinkTheme.Apply(this);
            info.ForeColor = PinkTheme.TextMuted;
            status.ForeColor = PinkTheme.TextMuted;
            listHint.ForeColor = PinkTheme.TextMuted;
            cropHint.ForeColor = PinkTheme.Text;
            list.Columns[0].Width = PinkTheme.Scale(list, 116);
            list.Columns[1].Width = PinkTheme.Scale(list, 96);
            list.DrawSubItem += DrawNumberCell; // 테마가 바탕을 칠한 다음 번호와 작은 그림을 그린다

            // 이벤트
            btnEven.Click += (s, e) => Thin(true);
            btnOdd.Click += (s, e) => Thin(false);
            btnDelete.Click += (s, e) => DeleteSelected();
            btnDelay.Click += (s, e) => ChangeDelay();
            btnSize.Click += (s, e) => ChangeSize();
            btnCrop.Click += (s, e) => EnterCrop();
            btnUndo.Click += (s, e) => DoUndo();
            btnCropApply.Click += (s, e) => ApplyCrop();
            btnCropCancel.Click += (s, e) => LeaveCrop();
            btnPlay.Click += (s, e) => TogglePlay();
            btnSave.Click += (s, e) => SaveAs();
            btnClose.Click += (s, e) => Close();
            preview.SelectionChanged += (s, e) => SyncCropFields();
            list.SelectedIndexChanged += (s, e) => QueueShow();
            list.MouseDown += (s, e) => StopPlay();
            playTimer.Tick += (s, e) => PlayStep();
            SetEnabled(false);
        }

        static void ConfigureBar(FlowLayoutPanel bar)
        {
            bar.Dock = DockStyle.Top;
            bar.Height = 46;
            bar.Padding = new Padding(10, 8, 10, 6);
            bar.WrapContents = false;
        }

        public GifProject Project { get { return project; } } // 시험용

        // ── 불러오기 ──

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            int thumbH = PinkTheme.Scale(list, 34);
            list.SmallImageList = new ImageList { ImageSize = new Size(1, thumbH + PinkTheme.Scale(list, 6)) }; // 줄 높이를 작은 그림에 맞춘다
            busy = true;
            SetStatus("불러오는 중이에요…");
            var t = new Thread(() =>
            {
                GifProject p = null;
                Exception error = null;
                try
                {
                    p = GifProject.Load(path, thumbH, (i, n) =>
                    {
                        if (i % 5 == 0 || i == n) Post(() => { preview.Message = "불러오는 중… " + i + " / " + n; preview.Invalidate(); });
                    }, () => closing);
                }
                catch (Exception ex) { error = ex; }
                if (!Post(() => OnLoaded(p, error)) && p != null) p.Dispose(); // 그새 창이 닫혔으면 임시 파일을 바로 정리한다
            }) { IsBackground = true, Name = "편집-불러오기" };
            t.Start();
        }

        bool Post(Action a)
        {
            try { if (!IsDisposed && IsHandleCreated) { BeginInvoke(a); return true; } } catch (Exception) { }
            return false;
        }

        void OnLoaded(GifProject p, Exception error)
        {
            busy = false;
            if (IsDisposed || closing) { if (p != null) p.Dispose(); return; }
            if (error != null)
            {
                if (error is OperationCanceledException) return;
                MessageBox.Show(this, "GIF 를 열지 못했어요.\n\n" + error.Message, EditUi.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
                return;
            }
            project = p;
            FillList(0);
            SetEnabled(true);
            SetStatus("열었어요. 목록에서 프레임을 고르고 위 도구로 편집하세요.");
        }

        // ── 목록·미리보기 ──

        void FillList(int select)
        {
            list.BeginUpdate();
            list.Items.Clear();
            var items = new ListViewItem[project.Count];
            for (int i = 0; i < items.Length; i++)
                items[i] = new ListViewItem(new[] { "", FormatDelay(project.Frames[i].DelayCs) });
            list.Items.AddRange(items);
            list.EndUpdate();
            SelectOnly(Math.Max(0, Math.Min(project.Count - 1, select)));
            UpdateInfo();
        }

        static string FormatDelay(int cs) { return (cs / 100.0).ToString("0.00") + "초"; }

        void SelectOnly(int index)
        {
            list.SelectedIndices.Clear();
            if (index < 0 || index >= list.Items.Count) return;
            list.Items[index].Selected = true;
            list.Items[index].Focused = true;
            list.EnsureVisible(index);
            current = index;
            ShowFrame(index);
        }

        void QueueShow()
        {
            if (pendingShow) return;
            pendingShow = true;
            BeginInvoke(new Action(() =>
            {
                pendingShow = false;
                if (project == null || IsDisposed) return;
                int i = list.FocusedItem != null && list.FocusedItem.Selected ? list.FocusedItem.Index
                      : list.SelectedIndices.Count > 0 ? list.SelectedIndices[0] : current;
                if (i >= 0 && i < project.Count) { current = i; ShowFrame(i); }
            }));
        }

        void ShowFrame(int index)
        {
            if (project == null || index < 0 || index >= project.Count) return;
            int source = project.Frames[index].Source;
            if (source != shownSource)
            {
                Bitmap old = preview.Image;
                preview.Image = project.Store.ReadBitmap(source);
                shownSource = source;
                if (old != null) old.Dispose();
            }
            preview.Crop = project.Crop;
            preview.Output = project.Output;
            preview.Invalidate();
        }

        void UpdateInfo()
        {
            if (project == null) { info.Text = ""; return; }
            string size = project.Output.Width + "×" + project.Output.Height;
            if (project.IsCropped || project.Output != project.SourceSize) size += " (원본 " + project.SourceSize.Width + "×" + project.SourceSize.Height + ")";
            info.Text = "영상 크기: " + size + "   프레임 수: " + project.Count + "   재생 시간: " + EditUi.Seconds(project.TotalMs);
            btnUndo.Enabled = !busy && project.CanUndo;
        }

        void DrawNumberCell(object sender, DrawListViewSubItemEventArgs e)
        {
            if (e.ColumnIndex != 0 || project == null || e.ItemIndex >= project.Count) return;
            int pad = PinkTheme.Scale(list, 4);
            bool picked = e.Item.Selected;
            var numberArea = new Rectangle(e.Bounds.X + pad, e.Bounds.Y, PinkTheme.Scale(list, 34), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, (e.ItemIndex + 1).ToString(), list.Font, numberArea, picked ? PinkTheme.OnPink : PinkTheme.Text,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            Bitmap thumb = project.Thumbs[project.Frames[e.ItemIndex].Source];
            var box = new Rectangle(numberArea.Right + pad * 2, e.Bounds.Y + (e.Bounds.Height - thumb.Height) / 2, thumb.Width, thumb.Height);
            e.Graphics.DrawImage(thumb, box);
            using (var pen = new Pen(PinkTheme.Line)) e.Graphics.DrawRectangle(pen, box.X - 1, box.Y - 1, box.Width + 1, box.Height + 1);
        }

        List<int> SelectedIndices()
        {
            var r = new List<int>();
            foreach (int i in list.SelectedIndices) r.Add(i);
            return r;
        }

        // ── 재생 ──

        void TogglePlay()
        {
            if (project == null) return;
            if (playTimer.Enabled) { StopPlay(); return; }
            if (current < 0 || current >= project.Count - 1) current = -1; // 끝에 있으면 처음부터
            btnPlay.Text = "■ 멈춤";
            PlayStep();
        }

        void PlayStep()
        {
            playTimer.Stop();
            if (project == null || project.Count == 0) return;
            current = (current + 1) % project.Count;
            SelectOnly(current);
            playTimer.Interval = Math.Max(10, project.Frames[current].DelayCs * 10);
            playTimer.Start();
        }

        void StopPlay()
        {
            if (!playTimer.Enabled) return;
            playTimer.Stop();
            btnPlay.Text = "▶ 재생";
        }

        // ── 편집 ──

        void Changed(int select, string message)
        {
            dirty = true;
            shownSource = -1;
            FillList(select);
            SetStatus(message);
        }

        void Thin(bool even)
        {
            if (project == null) return;
            StopPlay();
            if (project.Count < 2) { SetStatus("프레임이 한 장뿐이라 솎아낼 수 없어요."); return; }
            int removed = project.KeepAlternate(even);
            Changed(0, (even ? "짝수" : "홀수") + " 프레임만 남겼어요 — " + removed + "장 지움, 재생 시간은 그대로예요.");
        }

        void DeleteSelected()
        {
            if (project == null) return;
            StopPlay();
            var picked = SelectedIndices();
            if (picked.Count == 0) { SetStatus("지울 프레임을 목록에서 먼저 고르세요."); return; }
            try
            {
                int first = picked[0];
                foreach (int i in picked) first = Math.Min(first, i);
                int n = project.Delete(picked);
                Changed(first, n + "장을 지웠어요.");
            }
            catch (InvalidOperationException ex) { SetStatus(ex.Message); }
        }

        void ChangeDelay()
        {
            if (project == null) return;
            StopPlay();
            var picked = SelectedIndices();
            int cur = current >= 0 && current < project.Count ? project.Frames[current].DelayCs : 10;
            using (var dlg = new DelayDialog(picked.Count, project.Count, cur))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var targets = picked;
                if (dlg.AllFrames || targets.Count == 0)
                {
                    targets = new List<int>();
                    for (int i = 0; i < project.Count; i++) targets.Add(i);
                }
                if (dlg.Fixed) project.SetDelay(targets, dlg.DelayCs);
                else project.ScaleSpeed(targets, dlg.SpeedPercent);
                int keep = current;
                Changed(keep, targets.Count + "장의 프레임 시간을 바꿨어요.");
                if (picked.Count > 1 && !dlg.AllFrames) // 고른 것을 그대로 고른 채로 둔다
                {
                    list.SelectedIndices.Clear();
                    foreach (int i in picked) list.Items[i].Selected = true;
                }
            }
        }

        void ChangeSize()
        {
            if (project == null) return;
            StopPlay();
            using (var dlg = new CanvasSizeDialog(project.Output, project.Crop.Size))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (project.SetOutput(dlg.Result)) Changed(current, "캔버스 크기를 " + project.Output.Width + "×" + project.Output.Height + "(으)로 바꿨어요.");
            }
        }

        void EnterCrop()
        {
            if (project == null) return;
            StopPlay();
            cropMode = true;
            tools.Visible = false;
            cropTools.Visible = true;
            list.Enabled = btnPlay.Enabled = btnSave.Enabled = false;
            Size src = project.SourceSize;
            syncing = true;
            nX.Maximum = src.Width - 1; nY.Maximum = src.Height - 1;
            nW.Minimum = nH.Minimum = 1;
            nW.Maximum = src.Width; nH.Maximum = src.Height;
            syncing = false;
            preview.Selection = project.IsCropped ? project.Crop : Rectangle.Empty;
            preview.CropMode = true;
            SyncCropFields();
            preview.Focus();
            SetStatus("미리보기에서 끌어서 영역을 고르고 「잘라내기 적용」을 누르세요. 고른 영역 안을 끌면 옮겨져요.");
        }

        void SyncCropFields()
        {
            Rectangle r = preview.Selection;
            bool has = r.Width > 0 && r.Height > 0;
            Rectangle v = has ? r : new Rectangle(Point.Empty, project.SourceSize);
            syncing = true;
            nX.Value = Math.Min(nX.Maximum, v.X);
            nY.Value = Math.Min(nY.Maximum, v.Y);
            nW.Value = Math.Max(nW.Minimum, Math.Min(nW.Maximum, v.Width));
            nH.Value = Math.Max(nH.Minimum, Math.Min(nH.Maximum, v.Height));
            syncing = false;
            btnCropApply.Enabled = has;
        }

        void CropFieldsChanged()
        {
            if (syncing || !cropMode || project == null) return;
            Size src = project.SourceSize;
            int x = (int)nX.Value, y = (int)nY.Value;
            int w = Math.Min((int)nW.Value, src.Width - x), h = Math.Min((int)nH.Value, src.Height - y);
            preview.Selection = new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
            btnCropApply.Enabled = true;
        }

        void ApplyCrop()
        {
            Rectangle r = preview.Selection;
            if (r.Width < 1 || r.Height < 1) { SetStatus("먼저 미리보기에서 영역을 끌어서 고르세요."); return; }
            bool resized = project.Output != project.Crop.Size;
            bool changed = project.SetCrop(r);
            LeaveCrop();
            if (changed) Changed(current, "영역을 잘라냈어요 — " + r.Width + "×" + r.Height + (resized ? " (캔버스 크기도 잘라낸 크기로 돌아갔어요)" : ""));
        }

        void LeaveCrop()
        {
            if (!cropMode) return;
            cropMode = false;
            preview.CropMode = false;
            preview.Selection = Rectangle.Empty;
            cropTools.Visible = false;
            tools.Visible = true;
            list.Enabled = btnPlay.Enabled = btnSave.Enabled = true;
            ShowFrame(current);
            SetStatus("잘라내기를 마쳤어요.");
        }

        void DoUndo()
        {
            if (project == null || cropMode) return;
            StopPlay();
            if (project.Undo()) Changed(current, "되돌렸어요.");
        }

        // ── 저장 ──

        void SaveAs()
        {
            if (project == null || busy) return;
            StopPlay();
            string target;
            using (var dlg = new SaveFileDialog
            {
                Title = "편집한 GIF 저장",
                Filter = "GIF 움짤 (*.gif)|*.gif",
                FileName = Path.GetFileName(EditUi.SuggestName(path)),
                InitialDirectory = Path.GetDirectoryName(path),
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                target = dlg.FileName;
            }
            if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) &&
                MessageBox.Show(this, "원본 GIF 를 덮어써요. 원본은 되살릴 수 없어요. 계속할까요?\n(새 이름으로 저장하는 것을 권해요)", EditUi.AppTitle,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            busy = true;
            SetEnabled(false);
            SetStatus("저장 중이에요…");
            var t = new Thread(() =>
            {
                Exception error = null;
                try { project.Save(target, (i, n) => { if (i % 5 == 0 || i == n) Post(() => SetStatus("저장 중이에요… " + i + " / " + n)); }); }
                catch (Exception ex) { error = ex; }
                Post(() => OnSaved(target, error));
            }) { IsBackground = true, Name = "편집-저장" };
            t.Start();
        }

        void OnSaved(string target, Exception error)
        {
            busy = false;
            SetEnabled(true);
            if (error != null)
            {
                SetStatus("저장하지 못했어요: " + error.Message);
                MessageBox.Show(this, "GIF 를 저장하지 못했어요.\n\n" + error.Message, EditUi.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            dirty = false;
            string body = project.Output.Width + "×" + project.Output.Height + " · " + project.Count + "장 · " + EditUi.Seconds(project.TotalMs)
                + " · " + EditUi.Bytes(new FileInfo(target).Length) + " · " + Path.GetFileName(target);
            SetStatus("저장했어요 — " + body);
            Image thumb = null;
            try { using (var img = Image.FromFile(target)) thumb = new Bitmap(img); } catch (Exception) { }
            new Toast("편집한 GIF 를 저장했어요", body, target, thumb, false).Show();
        }

        // ── 공통 ──

        void SetEnabled(bool on)
        {
            bool ready = on && project != null;
            foreach (Control c in tools.Controls) c.Enabled = ready;
            btnUndo.Enabled = ready && project.CanUndo;
            list.Enabled = btnPlay.Enabled = btnSave.Enabled = ready;
        }

        void SetStatus(string text) { status.Text = text; }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (cropMode)
            {
                if (keyData == Keys.Enter) { ApplyCrop(); return true; }
                if (keyData == Keys.Escape) { LeaveCrop(); return true; }
            }
            else if (project != null && !busy)
            {
                if (keyData == (Keys.Control | Keys.Z)) { DoUndo(); return true; }
                if (keyData == (Keys.Control | Keys.S)) { SaveAs(); return true; }
                if (keyData == Keys.Delete && list.Focused) { DeleteSelected(); return true; }
                if (keyData == Keys.Space && (list.Focused || preview.Focused)) { TogglePlay(); return true; }
                if (keyData == (Keys.Control | Keys.A) && list.Focused)
                {
                    list.BeginUpdate();
                    foreach (ListViewItem it in list.Items) it.Selected = true;
                    list.EndUpdate();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy && project != null)
            {
                MessageBox.Show(this, "저장하는 중이에요. 끝난 뒤에 닫아 주세요.", EditUi.AppTitle);
                e.Cancel = true;
                return;
            }
            if (dirty && e.CloseReason == CloseReason.UserClosing &&
                MessageBox.Show(this, "편집한 내용을 저장하지 않고 닫을까요?", EditUi.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            closing = true;
            playTimer.Stop();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                playTimer.Dispose();
                tips.Dispose();
                if (preview.Image != null) { preview.Image.Dispose(); preview.Image = null; }
                if (project != null) { project.Dispose(); project = null; }
                if (list.SmallImageList != null) list.SmallImageList.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>MP4 캔버스 크기(해상도) 바꾸기 창. 자세한 영상 편집은 「파자마컷」에서.</summary>
    sealed class Mp4ResizeForm : Form
    {
        public const string TitlePrefix = "파자마 캡쳐 - MP4 크기 바꾸기";

        readonly string path, ffmpeg;
        readonly PictureBox picture = new PictureBox();
        readonly Label info = new Label(), status = new Label(), note = new Label();
        readonly SizeInputs inputs = new SizeInputs(true, 7680);
        readonly List<Button> quick = new List<Button>();
        readonly Button btnSave = new Button(), btnClose = new Button();
        Mp4Tool.Info source;
        bool busy;

        public Mp4ResizeForm(string path, string ffmpeg)
        {
            this.path = path;
            this.ffmpeg = ffmpeg;
            SuspendLayout();
            Text = TitlePrefix + " - " + Path.GetFileName(path);
            EditUi.Prepare(this, new Size(560, 520));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            picture.Bounds = new Rectangle(16, 16, 528, 297);
            picture.SizeMode = PictureBoxSizeMode.Zoom;
            picture.BackColor = PinkTheme.DisabledBack;
            info.AutoSize = true;
            info.Location = new Point(16, 322);
            info.Text = "영상 정보를 읽는 중이에요…";

            var g = new PinkGroupBox { Text = "캔버스 크기", Bounds = new Rectangle(16, 348, 528, 92) };
            var lw = new Label { Text = "가로", AutoSize = true, Location = new Point(12, 30) };
            inputs.Width.Bounds = new Rectangle(48, 26, 80, 24);
            var lx = new Label { Text = "×  세로", AutoSize = true, Location = new Point(136, 30) };
            inputs.Height.Bounds = new Rectangle(186, 26, 80, 24);
            inputs.KeepRatio.Bounds = new Rectangle(284, 26, 110, 24);
            g.Controls.AddRange(new Control[] { lw, inputs.Width, lx, inputs.Height, inputs.KeepRatio });
            int x = 12;
            foreach (int pct in new[] { 25, 50, 75, 100 })
            {
                int p = pct;
                var b = new Button { Text = p == 100 ? "원래 크기" : p + "%", Bounds = new Rectangle(x, 56, p == 100 ? 76 : 56, 28) };
                x += b.Width + 6;
                b.Click += (s, e) => { if (source != null) inputs.SetScaled(new Size(source.Width, source.Height), p / 100.0); };
                quick.Add(b);
                g.Controls.Add(b);
            }

            note.AutoSize = true;
            note.Location = new Point(16, 446);
            note.Text = "MP4는 가로·세로를 짝수로 맞춰요. 자세한 영상 편집은 「파자마컷」에서 하세요.";
            note.MaximumSize = new Size(528, 0);

            status.AutoSize = false;
            status.AutoEllipsis = true;
            status.Bounds = new Rectangle(16, 482, 250, 28);
            status.TextAlign = ContentAlignment.MiddleLeft;
            btnSave.Text = "다른 이름으로 저장…";
            btnSave.Tag = PinkTheme.PrimaryTag;
            btnSave.Bounds = new Rectangle(276, 480, 170, 32);
            btnClose.Text = "닫기";
            btnClose.Bounds = new Rectangle(452, 480, 92, 32);

            Controls.AddRange(new Control[] { picture, info, g, note, status, btnSave, btnClose });
            ResumeLayout(false);
            PinkTheme.Apply(this);
            info.ForeColor = PinkTheme.TextMuted;
            note.ForeColor = PinkTheme.TextMuted;
            status.ForeColor = PinkTheme.TextMuted;

            btnSave.Click += (s, e) => SaveAs();
            btnClose.Click += (s, e) => Close();
            SetEnabled(false);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            busy = true;
            var t = new Thread(() =>
            {
                Mp4Tool.Info i = null;
                Bitmap first = null;
                Exception error = null;
                try { i = Mp4Tool.Probe(ffmpeg, path); first = Mp4Tool.FirstFrame(ffmpeg, path); }
                catch (Exception ex) { error = ex; }
                Post(() => OnProbed(i, first, error));
            }) { IsBackground = true, Name = "MP4-정보" };
            t.Start();
        }

        void Post(Action a)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch (Exception) { }
        }

        void OnProbed(Mp4Tool.Info i, Bitmap first, Exception error)
        {
            busy = false;
            if (IsDisposed) { if (first != null) first.Dispose(); return; }
            if (error != null)
            {
                MessageBox.Show(this, "MP4 를 열지 못했어요.\n\n" + error.Message, EditUi.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
                return;
            }
            source = i;
            picture.Image = first;
            info.Text = "원본 크기: " + i.Width + " × " + i.Height + "   길이: " + (i.Seconds > 0 ? i.Seconds.ToString("0.0") + "초" : "알 수 없음")
                + "   파일: " + EditUi.Bytes(new FileInfo(path).Length);
            inputs.Set(new Size(i.Width, i.Height));
            SetEnabled(true);
            status.Text = "크기를 정하고 저장하세요.";
        }

        public Mp4Tool.Info Source { get { return source; } } // 시험용
        public SizeInputs Inputs { get { return inputs; } }   // 시험용

        void SaveAs()
        {
            if (source == null || busy) return;
            Size size = inputs.Value;
            string target;
            using (var dlg = new SaveFileDialog
            {
                Title = "크기를 바꾼 MP4 저장",
                Filter = "MP4 영상 (*.mp4)|*.mp4",
                FileName = Path.GetFileName(EditUi.SuggestName(path)),
                InitialDirectory = Path.GetDirectoryName(path),
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                target = dlg.FileName;
            }
            if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) &&
                MessageBox.Show(this, "원본 MP4 를 덮어써요. 원본은 되살릴 수 없어요. 계속할까요?\n(새 이름으로 저장하는 것을 권해요)", EditUi.AppTitle,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            StartSave(target, size);
        }

        public void StartSave(string target, Size size)
        {
            busy = true;
            SetEnabled(false);
            status.Text = "변환 중이에요…";
            double seconds = source.Seconds;
            var t = new Thread(() =>
            {
                Exception error = null;
                int lastPct = -1;
                try
                {
                    Mp4Tool.Resize(ffmpeg, path, target, size, seconds, f =>
                    {
                        int pct = (int)(f * 100);
                        if (pct != lastPct) { lastPct = pct; Post(() => status.Text = "변환 중이에요… " + pct + "%"); }
                    });
                }
                catch (Exception ex) { error = ex; }
                Post(() => OnSaved(target, size, error));
            }) { IsBackground = true, Name = "MP4-크기" };
            t.Start();
        }

        public event Action<string, Exception> Saved; // 시험용

        void OnSaved(string target, Size size, Exception error)
        {
            busy = false;
            SetEnabled(true);
            var h = Saved;
            if (error != null)
            {
                status.Text = "저장하지 못했어요.";
                if (h != null) { h(target, error); return; }
                MessageBox.Show(this, "MP4 를 저장하지 못했어요.\n\n" + error.Message, EditUi.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string body = size.Width + "×" + size.Height + " · " + EditUi.Bytes(new FileInfo(target).Length) + " · " + Path.GetFileName(target);
            status.Text = "저장했어요 — " + Path.GetFileName(target);
            if (h != null) { h(target, null); return; }
            new Toast("크기를 바꾼 MP4 를 저장했어요", body, target, picture.Image != null ? new Bitmap(picture.Image) : null, false).Show();
        }

        void SetEnabled(bool on)
        {
            bool ready = on && source != null;
            inputs.Width.Enabled = inputs.Height.Enabled = inputs.KeepRatio.Enabled = ready;
            foreach (var b in quick) b.Enabled = ready;
            btnSave.Enabled = ready;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy && source != null && e.CloseReason == CloseReason.UserClosing)
            {
                MessageBox.Show(this, "변환하는 중이에요. 끝난 뒤에 닫아 주세요.", EditUi.AppTitle);
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && picture.Image != null) { picture.Image.Dispose(); picture.Image = null; }
            base.Dispose(disposing);
        }
    }
}
