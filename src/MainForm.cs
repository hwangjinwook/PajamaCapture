// 파자마 캡쳐 메인 창 — 영역 캡쳐·창 캡쳐·녹화 틀 버튼과 설정, 그리고 찍기→고르기→저장(또는 녹화) 흐름.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using PinkUi;

[assembly: System.Reflection.AssemblyTitle("파자마 캡쳐")]
[assembly: System.Reflection.AssemblyProduct("파자마 캡쳐")]
[assembly: System.Reflection.AssemblyDescription("화면 캡쳐·녹화 프로그램")]
// 버전은 Program.Version 한곳에서 바꾼다 — 화면 오른쪽 위 표시와 exe 파일 속성에 함께 쓰인다.
[assembly: System.Reflection.AssemblyVersion(PajamaCapture.Program.Version)]
[assembly: System.Reflection.AssemblyFileVersion(PajamaCapture.Program.Version)]
[assembly: System.Reflection.AssemblyInformationalVersion(PajamaCapture.Program.Version)]

namespace PajamaCapture
{
    static class Program
    {
        /// <summary>프로그램 버전. 바꾼 뒤 build.cmd 로 exe 를 다시 만든다.</summary>
        public const string Version = "1.2.1";

        [STAThread]
        static int Main(string[] args)
        {
            Native.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0 && args[0] == "--selftest") return SelfTest.Run(args);

            bool first;
            // 시험처럼 설정 위치를 따로 준 실행은 켜져 있는 평소 앱과 따로 돈다(한 번만 실행 표식을 나눈다)
            string home = Environment.GetEnvironmentVariable("PAJAMA_CAPTURE_HOME");
            string mutexName = "PajamaCapture.SingleInstance" + (string.IsNullOrEmpty(home) ? "" : "." + (home.ToLowerInvariant().GetHashCode() & 0x7fffffff));
            using (var mutex = new Mutex(true, mutexName, out first))
            {
                if (!first)
                {
                    IntPtr other = Native.FindWindow(null, MainForm.AppTitle);
                    if (other != IntPtr.Zero) { Native.ShowWindow(other, 9); Native.SetForegroundWindow(other); }
                    return 0;
                }
                Application.Run(new MainForm());
            }
            return 0;
        }
    }

    /// <summary>설정 — %APPDATA%\PajamaCapture\settings.ini 에 「이름=값」 줄로 저장한다.</summary>
    sealed class Settings
    {
        public bool AutoSave = true;
        public string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "파자마 캡쳐");
        public bool Mp4 = false;
        public int Fps = 15;
        public bool Cursor = true;
        public string FfmpegPath = "";
        /// <summary>마지막에 쓴 녹화 틀 안쪽 범위(화면 점). 비어 있으면 처음 자리(640×360)로 띄운다.</summary>
        public Rectangle Frame = Rectangle.Empty;

        static string FilePath
        {
            get
            {
                string home = Environment.GetEnvironmentVariable("PAJAMA_CAPTURE_HOME"); // 시험할 때 설정 위치를 임시 폴더로 돌린다
                if (string.IsNullOrEmpty(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PajamaCapture");
                return Path.Combine(home, "settings.ini");
            }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    int n;
                    switch (k)
                    {
                        case "autosave": s.AutoSave = v == "1"; break;
                        case "folder": if (v.Length > 0) s.Folder = v; break;
                        case "format": s.Mp4 = v == "mp4"; break;
                        case "fps": if (int.TryParse(v, out n) && n >= 5 && n <= 30) s.Fps = n; break;
                        case "cursor": s.Cursor = v == "1"; break;
                        case "ffmpeg": s.FfmpegPath = v; break;
                        case "frame":
                            {
                                string[] p = v.Split(',');
                                int x, y, w, h;
                                if (p.Length == 4 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y) && int.TryParse(p[2], out w) && int.TryParse(p[3], out h) && w > 0 && h > 0)
                                    s.Frame = new Rectangle(x, y, w, h);
                            }
                            break;
                    }
                }
            }
            catch (Exception) { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new[] {
                    "autosave=" + (AutoSave ? 1 : 0), "folder=" + Folder,
                    "format=" + (Mp4 ? "mp4" : "gif"), "fps=" + Fps, "cursor=" + (Cursor ? 1 : 0), "ffmpeg=" + FfmpegPath,
                    "frame=" + (Frame.IsEmpty ? "" : Frame.X + "," + Frame.Y + "," + Frame.Width + "," + Frame.Height) }, Encoding.UTF8);
            }
            catch (Exception) { }
        }
    }

    public class MainForm : Form
    {
        public const string AppTitle = "파자마 캡쳐";

        readonly Settings settings = Settings.Load();
        readonly Button btnArea = new Button(), btnWindow = new Button(), btnFrame = new Button();
        readonly ComboBox cbFormat = new ComboBox(), cbFps = new ComboBox();
        readonly CheckBox chkAutoSave = new PinkCheckBox(), chkCursor = new PinkCheckBox();
        readonly TextBox tbFolder = new TextBox();
        readonly Button btnFolder = new Button(), btnOpenFolder = new Button();
        readonly Label lblFfmpeg = new Label();
        readonly Button btnFfmpegInstall = new Button(), btnFfmpegPick = new Button();
        readonly Button btnEditRecent = new Button(), btnEditOpen = new Button();
        readonly Label status = new Label();
        string ffmpeg;
        string lastRecording; // 이번에 켠 뒤 마지막으로 저장한 녹화(「최근 녹화 편집」이 먼저 연다)
        bool busy;
        Recorder recorder;
        RecordFrame recordFrame;
        RecordBar recordBar;
        FrameWindow frameWindow; // 녹화 틀(꿀캠 방식) — 열려 있는 동안만

        const string FrameText = "▣ 녹화 틀\n크기 먼저 맞추기", FrameCloseText = "▣ 녹화 틀\n닫기";

        public MainForm()
        {
            SuspendLayout(); // 다 넣은 뒤 ResumeLayout 에서 화면 배율만큼 한꺼번에 키운다
            Text = AppTitle;
            Font = new Font("Malgun Gothic", 9f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(476, 548);
            AllowDrop = true; // GIF·MP4 를 끌어다 놓으면 간단 편집으로 연다
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            var title = new Label { Text = "파자마 캡쳐", Font = new Font("Malgun Gothic", 15f, FontStyle.Bold), AutoSize = true, Location = new Point(16, 12) };
            var sub = new Label { Text = "화면을 찍고, 창을 찍고, 움직임을 녹화해요.", AutoSize = true, Location = new Point(18, 46) };

            SetupBig(btnArea, "영역 캡쳐", "끌어서 범위 지정", 16);
            SetupBig(btnWindow, "창 캡쳐", "클릭해서 창 찍기", 168);
            // 녹화는 녹화 틀(꿀캠 방식)로 한다. 끌어서 범위를 정해 녹화하려면 영역 캡쳐 도구 막대의 「● 녹화」를 쓴다.
            SetupBig(btnFrame, "▣ 녹화 틀", "크기 먼저 맞추기", 320);

            // 캡쳐 설정
            var gCapture = new PinkGroupBox { Text = "캡쳐 설정", Bounds = new Rectangle(16, 170, 444, 62) };
            chkAutoSave.Text = "찍으면 폴더에 PNG로 자동 저장";
            chkAutoSave.Bounds = new Rectangle(12, 24, 260, 24);
            gCapture.Controls.Add(chkAutoSave);

            // 녹화 설정
            var gRecord = new PinkGroupBox { Text = "녹화 설정", Bounds = new Rectangle(16, 242, 444, 98) };
            var lFormat = new Label { Text = "형식", AutoSize = true, Location = new Point(12, 28) };
            cbFormat.DropDownStyle = ComboBoxStyle.DropDownList;
            cbFormat.Items.AddRange(new object[] { "GIF (움짤)", "MP4 (영상)" });
            cbFormat.Bounds = new Rectangle(48, 24, 104, 24);
            var lFps = new Label { Text = "초당 장면", AutoSize = true, Location = new Point(166, 28) };
            cbFps.DropDownStyle = ComboBoxStyle.DropDownList;
            cbFps.Items.AddRange(new object[] { "10", "15", "20", "30" });
            cbFps.Bounds = new Rectangle(226, 24, 56, 24);
            chkCursor.Text = "마우스 커서 포함";
            chkCursor.Bounds = new Rectangle(300, 24, 136, 24);
            lblFfmpeg.AutoSize = true; // 고정 크기 라벨은 화면 배율이 두 번 곱해져 버튼을 덮어서 글자 길이에 맞춘다
            lblFfmpeg.Location = new Point(12, 66);
            btnFfmpegInstall.Text = "ffmpeg 설치";
            btnFfmpegInstall.Bounds = new Rectangle(252, 60, 92, 28);
            btnFfmpegPick.Text = "직접 지정…";
            btnFfmpegPick.Bounds = new Rectangle(350, 60, 84, 28);
            gRecord.Controls.AddRange(new Control[] { lFormat, cbFormat, lFps, cbFps, chkCursor, lblFfmpeg, btnFfmpegInstall, btnFfmpegPick });

            // 간단 편집
            var gEdit = new PinkGroupBox { Text = "간단 편집", Bounds = new Rectangle(16, 350, 444, 62) };
            var lEdit = new Label { Text = "녹화한 GIF·MP4를 열어 가볍게 고쳐요", AutoSize = true, Location = new Point(12, 30) };
            btnEditRecent.Text = "최근 녹화";
            btnEditRecent.Bounds = new Rectangle(254, 24, 86, 28);
            btnEditOpen.Text = "파일 열기…";
            btnEditOpen.Bounds = new Rectangle(346, 24, 88, 28);
            gEdit.Controls.AddRange(new Control[] { lEdit, btnEditRecent, btnEditOpen });

            // 저장 폴더
            var gFolder = new PinkGroupBox { Text = "저장 폴더", Bounds = new Rectangle(16, 422, 444, 64) };
            tbFolder.ReadOnly = true;
            tbFolder.Bounds = new Rectangle(12, 26, 260, 24);
            btnFolder.Text = "바꾸기…";
            btnFolder.Bounds = new Rectangle(280, 24, 74, 28);
            btnOpenFolder.Text = "열기";
            btnOpenFolder.Bounds = new Rectangle(360, 24, 74, 28);
            gFolder.Controls.AddRange(new Control[] { tbFolder, btnFolder, btnOpenFolder });

            status.Dock = DockStyle.Bottom; // 창 폭에 맞춰 아래에 붙인다(고정 크기 라벨은 배율이 두 번 곱해진다)
            status.Height = 36;
            status.Padding = new Padding(16, 0, 16, 0);
            status.AutoEllipsis = true;
            status.TextAlign = ContentAlignment.MiddleLeft;

            // 버전 — 제목 줄 오른쪽 끝에 흐린 글씨로(위치는 배율이 적용된 뒤 아래에서 맞춘다)
            var version = new Label { Name = "version", Text = "v" + Program.Version, AccessibleName = "버전 " + Program.Version, AutoSize = true };

            Controls.AddRange(new Control[] { title, sub, version, btnArea, btnWindow, btnFrame, gCapture, gRecord, gEdit, gFolder, status });
            ResumeLayout(false);
            PinkTheme.Apply(this);
            title.ForeColor = PinkTheme.Accent;
            sub.ForeColor = PinkTheme.TextMuted;
            version.ForeColor = PinkTheme.TextMuted;
            version.Location = new Point(ClientSize.Width - version.Width - title.Left, title.Top + (title.Height - version.Height) / 2);
            status.ForeColor = PinkTheme.TextMuted;
            lblFfmpeg.ForeColor = PinkTheme.TextMuted;
            lEdit.ForeColor = PinkTheme.TextMuted;

            // 값 채우기
            chkAutoSave.Checked = settings.AutoSave;
            cbFormat.SelectedIndex = settings.Mp4 ? 1 : 0;
            int fpsIndex = Array.IndexOf(new[] { 10, 15, 20, 30 }, settings.Fps);
            cbFps.SelectedIndex = fpsIndex < 0 ? 1 : fpsIndex;
            chkCursor.Checked = settings.Cursor;
            tbFolder.Text = settings.Folder;
            RefreshFfmpeg();
            SetStatus("준비됐어요. 버튼을 눌러 시작하세요.");

            // 이벤트
            btnArea.Click += (s, e) => StartPick(OverlayMode.Area);
            btnWindow.Click += (s, e) => StartPick(OverlayMode.Window);
            btnFrame.Click += (s, e) => ToggleFrame();
            chkAutoSave.CheckedChanged += (s, e) => { settings.AutoSave = chkAutoSave.Checked; settings.Save(); };
            cbFormat.SelectedIndexChanged += (s, e) => { settings.Mp4 = cbFormat.SelectedIndex == 1; settings.Save(); RefreshFfmpeg(); SyncFrameFormat(); };
            cbFps.SelectedIndexChanged += (s, e) => { settings.Fps = new[] { 10, 15, 20, 30 }[cbFps.SelectedIndex]; settings.Save(); };
            chkCursor.CheckedChanged += (s, e) => { settings.Cursor = chkCursor.Checked; settings.Save(); };
            btnFolder.Click += (s, e) => PickFolder();
            btnOpenFolder.Click += (s, e) => OpenFolder();
            btnFfmpegInstall.Click += (s, e) => InstallFfmpeg();
            btnFfmpegPick.Click += (s, e) => PickFfmpeg();
            btnEditRecent.Click += (s, e) => EditRecent();
            btnEditOpen.Click += (s, e) => EditPick();
            DragEnter += (s, e) => e.Effect = DroppedFile(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None;
            DragDrop += (s, e) => { string f = DroppedFile(e.Data); if (f != null) BeginInvoke(new Action(() => OpenEditor(f))); };
            Activated += (s, e) => { if (ffmpeg == null) RefreshFfmpeg(); }; // 설치하고 돌아오면 다시 찾는다
        }

        void SetupBig(Button b, string head, string tail, int x)
        {
            b.Text = head + "\n" + tail;
            b.Font = new Font("Malgun Gothic", 10.5f, FontStyle.Bold);
            b.Bounds = new Rectangle(x, 76, 140, 82);
            b.Tag = PinkTheme.PrimaryTag;
        }

        void SetStatus(string text) { status.Text = text; }

        // ── ffmpeg ──

        void RefreshFfmpeg()
        {
            ffmpeg = FfmpegLocator.Find(settings.FfmpegPath);
            if (ffmpeg != null)
            {
                lblFfmpeg.Text = "MP4 준비됨 (ffmpeg 찾음)";
                lblFfmpeg.ForeColor = PinkTheme.Success;
                btnFfmpegInstall.Enabled = false;
            }
            else
            {
                lblFfmpeg.Text = "MP4에는 ffmpeg가 필요해요";
                lblFfmpeg.ForeColor = settings.Mp4 ? PinkTheme.ErrorText : PinkTheme.TextMuted;
                btnFfmpegInstall.Enabled = true;
            }
        }

        void InstallFfmpeg()
        {
            var answer = MessageBox.Show(this,
                "MP4 녹화에 쓰는 무료 프로그램 ffmpeg를 설치할게요.\n\n" +
                "검은 명령 창이 열리고 윈도우 기본 설치 도구(winget)가 아래 명령을 실행해요:\n" +
                FfmpegLocator.WingetCommand + "\n\n" +
                "설치가 끝나면 그 창을 닫고 이 창으로 돌아오세요. 자동으로 다시 찾아요.\n" +
                "(winget이 없으면 README의 「ffmpeg 직접 받기」를 보세요.)",
                AppTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (answer != DialogResult.OK) return;
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe",
                    "/k " + FfmpegLocator.WingetCommand + " --accept-source-agreements --accept-package-agreements && echo. && echo 설치가 끝났어요. 이 창을 닫으세요.")
                { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(this, "설치 창을 열지 못했어요: " + ex.Message, AppTitle); }
        }

        void PickFfmpeg()
        {
            using (var dlg = new OpenFileDialog { Title = "ffmpeg.exe 고르기", Filter = "ffmpeg.exe|ffmpeg.exe|실행 파일 (*.exe)|*.exe" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                settings.FfmpegPath = dlg.FileName;
                settings.Save();
                RefreshFfmpeg();
            }
        }

        // ── 폴더 ──

        void PickFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "캡쳐와 녹화를 저장할 폴더", SelectedPath = settings.Folder })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                settings.Folder = dlg.SelectedPath;
                tbFolder.Text = settings.Folder;
                settings.Save();
            }
        }

        void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(settings.Folder);
                Process.Start("explorer.exe", "\"" + settings.Folder + "\"");
            }
            catch (Exception ex) { MessageBox.Show(this, "폴더를 열지 못했어요: " + ex.Message, AppTitle); }
        }

        string NewPath(string ext)
        {
            Directory.CreateDirectory(settings.Folder);
            string stem = "파자마캡쳐_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(settings.Folder, stem + ext);
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(settings.Folder, stem + "_" + i + ext);
            return path;
        }

        // ── 찍기 → 고르기 ──

        void StartPick(OverlayMode mode)
        {
            if (busy) return;
            busy = true;
            SetButtons(false);
            Hide(); // 이 창이 찍히지 않게 숨기고, 사라질 시간을 준 뒤 찍는다
            After(250, () => OpenOverlay(mode));
        }

        bool EnsureFfmpegOrGif()
        {
            RefreshFfmpeg();
            if (ffmpeg != null) return true;
            var answer = MessageBox.Show(this,
                "MP4로 녹화하려면 ffmpeg가 필요한데 아직 없어요.\n\n" +
                "[예] ffmpeg 설치 안내 열기\n[아니요] 이번에는 GIF로 녹화하기\n[취소] 그만두기",
                AppTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes) { InstallFfmpeg(); return false; }
            if (answer == DialogResult.No) { cbFormat.SelectedIndex = 0; return true; }
            return false;
        }

        static void After(int ms, Action action)
        {
            var t = new System.Windows.Forms.Timer { Interval = ms };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); action(); };
            t.Start();
        }

        void OpenOverlay(OverlayMode mode)
        {
            Overlay overlay = null;
            try
            {
                Rectangle virt = Native.VirtualScreen;
                List<WindowInfo> windows = WindowFinder.List(virt);
                Bitmap shot = ScreenCapturer.Snap(virt);
                overlay = new Overlay(mode, shot, virt, windows);
                var ov = overlay;
                ov.FormClosed += (s, e) => OnOverlayClosed(ov);
                ov.Show();
            }
            catch (Exception ex)
            {
                if (overlay != null) overlay.Dispose();
                Done();
                ShowError("화면을 찍지 못했어요", ex.Message);
            }
        }

        void OnOverlayClosed(Overlay ov)
        {
            OverlayAction action = ov.Action;
            Rectangle rect = ov.Selection;
            Bitmap shot = ov.Shot;
            Rectangle virt = ov.VirtualScreen;
            ov.Dispose();
            try
            {
                if (action == OverlayAction.Cancel) { shot.Dispose(); Done(); SetStatus("취소했어요."); return; }
                if (action == OverlayAction.Record) // 영역 캡쳐 도구 막대의 「● 녹화」 — 고른 범위 그대로 녹화
                {
                    shot.Dispose();
                    if (settings.Mp4 && !EnsureFfmpegOrGif()) { Done(); SetStatus("녹화를 그만뒀어요."); return; }
                    StartRecording(rect);
                    return;
                }

                var local = rect;
                local.Offset(-virt.X, -virt.Y);
                Bitmap image = ImageTools.Crop(shot, local);
                shot.Dispose();
                Done();
                using (image) Deliver(image, action == OverlayAction.Save);
            }
            catch (Exception ex)
            {
                Done();
                ShowError("캡쳐를 마무리하지 못했어요", ex.Message);
            }
        }

        /// <summary>복사(+자동 저장) 또는 다른 이름으로 저장.</summary>
        void Deliver(Bitmap image, bool askPath)
        {
            bool copied = CopyToClipboard(image);
            string saved = null;
            if (askPath)
            {
                using (var dlg = new SaveFileDialog
                {
                    Title = "캡쳐 저장",
                    Filter = "PNG 그림 (*.png)|*.png|JPG 그림 (*.jpg)|*.jpg|BMP 그림 (*.bmp)|*.bmp",
                    FileName = Path.GetFileName(NewPath(".png")),
                    InitialDirectory = settings.Folder,
                })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                        ImageFormat fmt = ext == ".jpg" || ext == ".jpeg" ? ImageFormat.Jpeg : ext == ".bmp" ? ImageFormat.Bmp : ImageFormat.Png;
                        image.Save(dlg.FileName, fmt);
                        saved = dlg.FileName;
                    }
                }
            }
            else if (settings.AutoSave)
            {
                saved = NewPath(".png");
                image.Save(saved, ImageFormat.Png);
            }

            string size = image.Width + " × " + image.Height;
            string head = copied ? "클립보드에 복사했어요" : "캡쳐했어요";
            string body = saved != null ? size + " · " + Path.GetFileName(saved) : size + (copied ? " · 붙여넣기(Ctrl+V) 하세요" : "");
            if (!copied) body += "\n(클립보드 복사는 실패했어요)";
            SetStatus(head + " — " + body.Replace("\n", " "));
            new Toast(head, body, saved, new Bitmap(image), false).Show();
        }

        static bool CopyToClipboard(Bitmap image)
        {
            for (int i = 0; i < 5; i++)
            {
                try { Clipboard.SetImage(image); return true; }
                catch (ExternalException) { Thread.Sleep(60); } // 다른 프로그램이 클립보드를 잠깐 잡고 있을 때
            }
            return false;
        }

        void Done()
        {
            busy = false;
            SetButtons(true);
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void SetButtons(bool on)
        {
            bool frameOpen = frameWindow != null; // 녹화 틀이 떠 있는 동안에는 다른 찍기를 막고, 틀 버튼은 「닫기」가 된다
            btnArea.Enabled = on && !frameOpen;
            btnWindow.Enabled = on && !frameOpen;
            btnFrame.Enabled = on && recorder == null;
            btnFrame.Text = frameOpen ? FrameCloseText : FrameText;
        }

        // ── 녹화 틀(꿀캠 방식): 틀을 먼저 띄워 옮기고 크기를 맞춘 뒤 녹화 ──

        void ToggleFrame()
        {
            if (frameWindow != null) { frameWindow.Close(); return; }
            if (busy || recorder != null) return;
            if (settings.Mp4 && !EnsureFfmpegOrGif()) return;
            bool mp4 = settings.Mp4 && ffmpeg != null;
            var fw = new FrameWindow(InitialFrame(), mp4, mp4 ? "MP4" : "GIF");
            fw.RecordClicked += () => StartFrameRecording();
            fw.StopClicked += () => StopRecording(false);
            fw.CancelClicked += () => StopRecording(true);
            fw.FormClosed += (s, e) =>
            {
                settings.Frame = fw.Inner; // 다음에 같은 자리·크기로 띄운다
                settings.Save();
                frameWindow = null;
                if (IsDisposed || Disposing) return;
                SetButtons(!busy);
                SetStatus("녹화 틀을 닫았어요.");
            };
            frameWindow = fw;
            fw.Show();
            SetButtons(true);
            SetStatus("녹화 틀을 띄웠어요. 막대를 끌어 옮기고 테두리로 크기를 맞춘 뒤 「● 녹화」를 누르세요.");
        }

        /// <summary>지난번 틀 자리(화면 안에 있으면), 아니면 이 창이 있는 모니터 가운데 640×360.</summary>
        Rectangle InitialFrame()
        {
            Rectangle last = settings.Frame;
            if (last.Width >= FrameWindow.MinSide && last.Height >= FrameWindow.MinSide)
                foreach (var s in Screen.AllScreens)
                    if (s.Bounds.Contains(new Point(last.X + last.Width / 2, last.Y + last.Height / 2))) return last;
            Rectangle work = Screen.FromControl(this).WorkingArea;
            int w = Math.Min(640, work.Width - 40), h = Math.Min(360, work.Height - 80);
            return new Rectangle(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h);
        }

        void SyncFrameFormat()
        {
            if (frameWindow == null || frameWindow.Busy) return;
            bool mp4 = settings.Mp4 && ffmpeg != null;
            frameWindow.EvenOnly = mp4;
            frameWindow.Format = mp4 ? "MP4" : "GIF";
        }

        void StartFrameRecording()
        {
            if (frameWindow == null || frameWindow.Busy || recorder != null || busy) return;
            if (settings.Mp4 && !EnsureFfmpegOrGif()) return;
            SyncFrameFormat(); // ffmpeg 를 잃어 GIF 로 바뀌었으면 틀에도 알린다
            var fw = frameWindow;
            bool mp4 = settings.Mp4 && ffmpeg != null;
            busy = true;
            SetButtons(false);
            fw.ShowRecording(() => recorder != null ? recorder.ElapsedMs : 0, mp4 ? "MP4" : "GIF"); // 바로 크기를 잠근다
            bool wasVisible = Visible;
            Hide(); // 이 창이 범위와 겹쳐 찍히지 않게 숨기고, 사라질 시간을 준다
            After(wasVisible ? 250 : 1, () =>
            {
                if (frameWindow != fw) { busy = false; Done(); return; }
                Rectangle rect = fw.Inner;
                string path = NewPath(mp4 ? ".mp4" : ".gif");
                var rec = new Recorder(rect, mp4, settings.Fps, settings.Cursor, path, ffmpeg);
                rec.Finished += r => BeginInvoke(new Action(() => OnRecordingFinished(r)));
                try { rec.Start(); }
                catch (Exception ex) { fw.ShowIdle(); Done(); ShowError("녹화를 시작하지 못했어요", ex.Message); return; }
                recorder = rec;
                SetStatus("녹화 틀로 녹화 중이에요(" + rect.Width + " × " + rect.Height + "). 틀 막대의 「■ 정지」를 누르면 저장해요.");
            });
        }

        // ── 녹화 ──

        void StartRecording(Rectangle rect)
        {
            bool mp4 = settings.Mp4 && ffmpeg != null;
            if (mp4 && (rect.Width < 2 || rect.Height < 2)) { Done(); ShowError("녹화를 시작하지 못했어요", "범위가 너무 작아요."); return; }
            string path = NewPath(mp4 ? ".mp4" : ".gif");
            var rec = new Recorder(rect, mp4, settings.Fps, settings.Cursor, path, ffmpeg);
            rec.Finished += r => BeginInvoke(new Action(() => OnRecordingFinished(r)));
            try { rec.Start(); }
            catch (Exception ex) { Done(); ShowError("녹화를 시작하지 못했어요", ex.Message); return; }
            recorder = rec;

            recordFrame = new RecordFrame(rect);
            recordFrame.Show();
            recordBar = new RecordBar(rect, mp4 ? "MP4" : "GIF", () => rec.ElapsedMs);
            recordBar.StopClicked += (s, e) => StopRecording(false);
            recordBar.CancelClicked += (s, e) => StopRecording(true);
            recordBar.Show();
            SetButtons(false);
            SetStatus("녹화 중이에요. 막대의 「정지」를 누르면 저장해요.");
        }

        void StopRecording(bool cancel)
        {
            if (recorder == null) return;
            IWin32Window owner = recordBar != null ? (IWin32Window)recordBar : frameWindow;
            if (cancel && MessageBox.Show(owner, "지금까지 녹화한 것을 버릴까요?", AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (recorder == null) return; // 묻는 사이에 끝났을 때
            recorder.Stop(cancel);
            if (recordFrame != null) { recordFrame.Close(); recordFrame = null; }
            if (recordBar != null) recordBar.ShowSaving();
            if (frameWindow != null) frameWindow.ShowSaving();
            SetStatus("저장 중이에요…");
        }

        void OnRecordingFinished(Recorder r)
        {
            if (recordFrame != null) { recordFrame.Close(); recordFrame = null; }
            if (recordBar != null) { recordBar.Close(); recordBar = null; }
            recorder = null;
            if (frameWindow != null) frameWindow.ShowIdle(); // 틀은 그대로 두어 같은 자리에서 또 녹화할 수 있다
            Done();

            if (r.Canceled) { SetStatus("녹화를 버렸어요."); return; }
            if (r.Error != null) { ShowError("녹화를 저장하지 못했어요", r.Error); return; }

            long ms = r.ElapsedMs;
            string length = string.Format("{0}분 {1:0.0}초", ms / 60000, (ms % 60000) / 1000.0);
            if (ms < 60000) length = string.Format("{0:0.0}초", ms / 1000.0);
            string size = FormatBytes(new FileInfo(r.OutputPath).Length);
            string body = length + " · " + size + " · " + Path.GetFileName(r.OutputPath);
            SetStatus("녹화를 저장했어요 — " + body);
            lastRecording = r.OutputPath;
            Image thumb = null;
            try { if (!r.IsMp4) using (var img = Image.FromFile(r.OutputPath)) thumb = new Bitmap(img); } catch (Exception) { }
            var toast = new Toast("녹화를 저장했어요", body, r.OutputPath, thumb, false);
            toast.EditRequested = p => OpenEditor(p);
            toast.Show();
        }

        // ── 간단 편집 ──

        /// <summary>GIF 는 편집 창, MP4 는 크기 바꾸기 창으로 연다.</summary>
        public void OpenEditor(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { ShowError("편집할 파일을 열지 못했어요", "파일이 없어요: " + path); return; }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".gif")
            {
                new GifEditorForm(path).Show();
                SetStatus("간단 편집을 열었어요 — " + Path.GetFileName(path));
                return;
            }
            if (ext == ".mp4")
            {
                RefreshFfmpeg();
                if (ffmpeg == null)
                {
                    if (MessageBox.Show(this, "MP4 크기를 바꾸려면 ffmpeg가 필요한데 아직 없어요.\n설치 안내를 열까요?", AppTitle,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) InstallFfmpeg();
                    return;
                }
                new Mp4ResizeForm(path, ffmpeg).Show();
                SetStatus("MP4 크기 바꾸기를 열었어요 — " + Path.GetFileName(path));
                return;
            }
            MessageBox.Show(this, "GIF나 MP4 파일만 편집할 수 있어요.", AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void EditRecent()
        {
            string path = lastRecording != null && File.Exists(lastRecording) ? lastRecording : NewestRecording();
            if (path == null)
            {
                MessageBox.Show(this, "저장 폴더에 녹화한 GIF·MP4가 아직 없어요.\n「파일 열기…」로 직접 고를 수 있어요.", AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            OpenEditor(path);
        }

        string NewestRecording()
        {
            try
            {
                if (!Directory.Exists(settings.Folder)) return null;
                FileInfo best = null;
                foreach (var f in new DirectoryInfo(settings.Folder).GetFiles())
                {
                    string ext = f.Extension.ToLowerInvariant();
                    if (ext != ".gif" && ext != ".mp4") continue;
                    if (best == null || f.LastWriteTime > best.LastWriteTime) best = f;
                }
                return best == null ? null : best.FullName;
            }
            catch (Exception) { return null; }
        }

        void EditPick()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "편집할 GIF·MP4 고르기",
                Filter = "GIF·MP4 (*.gif;*.mp4)|*.gif;*.mp4|GIF 움짤 (*.gif)|*.gif|MP4 영상 (*.mp4)|*.mp4",
                InitialDirectory = Directory.Exists(settings.Folder) ? settings.Folder : "",
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                OpenEditor(dlg.FileName);
            }
        }

        static string DroppedFile(IDataObject data)
        {
            if (!data.GetDataPresent(DataFormats.FileDrop)) return null;
            var files = data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length != 1) return null;
            string ext = Path.GetExtension(files[0]).ToLowerInvariant();
            return ext == ".gif" || ext == ".mp4" ? files[0] : null;
        }

        static string FormatBytes(long n)
        {
            if (n >= 1024 * 1024) return string.Format("{0:0.0}MB", n / 1048576.0);
            return string.Format("{0:0}KB", Math.Max(1, n / 1024.0));
        }

        void ShowError(string head, string detail)
        {
            SetStatus(head + ": " + detail.Split('\n')[0]);
            MessageBox.Show(this, head + ".\n\n" + detail, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // 배율이 다른 모니터로 옮기면 창과 글자를 그 배율에 맞춰 다시 키우거나 줄인다
        protected override void WndProc(ref Message m)
        {
            const int WM_DPICHANGED = 0x02E0;
            if (m.Msg == WM_DPICHANGED)
            {
                int dpi = (int)(m.WParam.ToInt64() & 0xFFFF);
                if (currentDpi > 0 && dpi > 0 && dpi != currentDpi)
                {
                    float f = dpi / (float)currentDpi;
                    currentDpi = dpi;
                    SuspendLayout();
                    Scale(new SizeF(f, f));
                    ScaleFonts(this, f);
                    ResumeLayout(true);
                    var r = (Native.RECT)Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
                    Native.SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, 0x0004 | 0x0010);
                }
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        int currentDpi;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            using (var g = CreateGraphics()) currentDpi = (int)Math.Round(g.DpiX); // 처음 배치에 쓴 배율(시스템 배율)
        }

        static void ScaleFonts(Control c, float f)
        {
            foreach (Control child in c.Controls)
            {
                if (child.Font != c.Font) child.Font = new Font(child.Font.FontFamily, child.Font.Size * f, child.Font.Style);
                ScaleFonts(child, f);
            }
            if (c is Form) c.Font = new Font(c.Font.FontFamily, c.Font.Size * f, c.Font.Style);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (recorder != null)
            {
                var answer = MessageBox.Show(this, "녹화 중이에요. 녹화를 저장하지 않고 끌까요?", AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) { e.Cancel = true; return; }
                recorder.Stop(true);
            }
            if (frameWindow != null) frameWindow.ForceClose(); // 틀 자리도 이때 저장된다
            base.OnFormClosing(e);
        }
    }
}
