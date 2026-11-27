// 실제 앱 화면 시험 — 파자마캡쳐.exe 를 띄우고 진짜 마우스·키보드 입력으로 영역 캡쳐·창 캡쳐·녹화를 해 본다.
// 사용: UiDriver.exe <파자마캡쳐.exe> <임시 폴더>
// 설정(PAJAMA_CAPTURE_HOME)과 저장 폴더를 임시 폴더로 돌려서 사장님 사진 폴더에는 아무것도 남기지 않는다.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

static class UiDriver
{
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(int flags, int dx, int dy, int data, IntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, int flags, IntPtr extra);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point p);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowW(string cls, string title);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "  통과: " : "  실패: ") + what); if (!ok) failures++; }

    [STAThread]
    static int Main(string[] args)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string exe = args[0], temp = args[1];
        string appData = Path.Combine(temp, "appdata"), outDir = Path.Combine(temp, "out");
        Directory.CreateDirectory(Path.Combine(appData, "PajamaCapture"));
        Directory.CreateDirectory(outDir);
        File.WriteAllLines(Path.Combine(appData, "PajamaCapture", "settings.ini"),
            new[] { "autosave=1", "folder=" + outDir, "format=gif", "fps=15", "cursor=1", "ffmpeg=",
                    "frame=" + (Screen.PrimaryScreen.Bounds.X + 300) + "," + (Screen.PrimaryScreen.Bounds.Y + 250) + ",400,240" });

        Point original;
        GetCursorPos(out original);
        Rectangle prim = Screen.PrimaryScreen.Bounds;
        Rectangle frameStart = new Rectangle(prim.X + 300, prim.Y + 250, 400, 240);
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        psi.EnvironmentVariables["PAJAMA_CAPTURE_HOME"] = Path.Combine(appData, "PajamaCapture");
        var app = Process.Start(psi);
        Form target = null;
        try
        {
            AutomationElement main = WaitFor(() => FindWindow(app.Id, "파자마 캡쳐"), 8000);
            Check(main != null, "메인 창이 뜸(제목 「파자마 캡쳐」)");
            if (main == null) return 1;

            // 0) 화면의 버전 표시가 exe 파일 속성의 버전과 같은지(둘 다 Program.Version 에서 온다)
            {
                string fileVersion = FileVersionInfo.GetVersionInfo(exe).ProductVersion;
                AutomationElement ver = null;
                foreach (AutomationElement el in main.FindAll(TreeScope.Descendants, Condition.TrueCondition))
                    if (el.Current.Name.StartsWith("버전 ")) { ver = el; break; }
                string shown = ver == null ? "없음" : ver.Current.Name.Substring(3);
                Check(ver != null && shown == fileVersion, "버전 표시 " + shown + " = exe 파일 속성 " + fileVersion);
            }

            // 1) 영역 캡쳐: 끌어서 500×300 범위 → 손잡이로 오른쪽 아래를 40,20 늘림 → Enter
            Console.WriteLine("[영역 캡쳐]");
            Invoke(main, "영역 캡쳐");
            Thread.Sleep(900);
            {
                IntPtr ov = FindWindowW(null, "파자마 캡쳐 - 범위 고르기");
                RECT o;
                GetWindowRect(ov, out o);
                var virt = new Rectangle(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
                var got = Rectangle.FromLTRB(o.L, o.T, o.R, o.B);
                Check(ov != IntPtr.Zero && got == virt, "고르기 화면이 모든 모니터를 덮음 " + virt + " → 실제 " + got);
            }
            int x0 = prim.X + 300, y0 = prim.Y + 250;
            Drag(x0, y0, x0 + 500, y0 + 300);
            Thread.Sleep(250);
            Drag(x0 + 501, y0 + 301, x0 + 541, y0 + 321); // 오른쪽 아래 손잡이
            Thread.Sleep(250);
            Key(0x0D);
            string png = WaitNewFile(outDir, "*.png", 5000);
            Check(png != null, "PNG 자동 저장");
            if (png != null) using (var img = Image.FromFile(png)) Check(img.Width == 541 && img.Height == 321, "저장 크기 541×321 (끌기 501×301 + 손잡이 40×20) → 실제 " + img.Width + "×" + img.Height);
            var clip = Clipboard.GetImage();
            Check(clip != null && clip.Width == 541 && clip.Height == 321, "클립보드에 같은 그림 " + (clip == null ? "없음" : clip.Width + "×" + clip.Height));
            Thread.Sleep(600);

            // 2) 창 캡쳐: 시험용 창을 띄우고 그 위를 클릭
            Console.WriteLine("[창 캡쳐]");
            target = new Form { Text = "시험용 창", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(prim.X + 200, prim.Y + 150, 700, 450), BackColor = Color.LightSkyBlue, TopMost = true };
            target.Show();
            Application.DoEvents();
            Thread.Sleep(400);
            RECT r;
            DwmGetWindowAttribute(target.Handle, 9, out r, 16);
            var expect = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
            target.TopMost = false;
            Application.DoEvents();
            main = FindWindow(app.Id, "파자마 캡쳐");
            Invoke(main, "창 캡쳐");
            Thread.Sleep(900);
            Click(expect.X + expect.Width / 2, expect.Y + expect.Height / 2);
            string png2 = WaitNewFile(outDir, "*.png", 5000, png);
            Check(png2 != null, "창 캡쳐 PNG 저장");
            if (png2 != null) using (var img = new Bitmap(png2))
            {
                Check(img.Width == expect.Width && img.Height == expect.Height, "창 테두리 크기 그대로 " + expect.Width + "×" + expect.Height + " → 실제 " + img.Width + "×" + img.Height);
                Color c = img.GetPixel(img.Width / 2, img.Height / 2);
                Check(Math.Abs(c.R - 135) < 12 && Math.Abs(c.G - 206) < 12 && Math.Abs(c.B - 250) < 12, "가운데가 시험용 창 색(하늘색) " + c);
            }
            target.Close();
            target = null;
            Thread.Sleep(600);

            // 3) 녹화(GIF): 범위를 끌어 정하고 Enter → 2초 뒤 막대의 「정지」
            Console.WriteLine("[녹화]");
            main = FindWindow(app.Id, "파자마 캡쳐");
            Invoke(main, "● 녹화");
            Thread.Sleep(900);
            Drag(prim.X + 400, prim.Y + 300, prim.X + 800, prim.Y + 540);
            Thread.Sleep(250);
            Key(0x0D);
            Thread.Sleep(2200);
            AutomationElement bar = WaitFor(() => FindWindow(app.Id, "파자마 캡쳐 - 녹화 중"), 3000);
            Check(bar != null, "녹화 막대가 뜸");
            if (bar != null) Invoke(bar, "■ 정지");
            string gif = WaitNewFile(outDir, "*.gif", 15000);
            Check(gif != null, "GIF 저장");
            if (gif != null)
            {
                Thread.Sleep(500);
                using (var img = Image.FromFile(gif))
                {
                    int frames = img.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time);
                    Check(img.Width == 401 && img.Height == 241, "GIF 크기 401×241 → 실제 " + img.Width + "×" + img.Height);
                    Check(frames >= 1, "GIF 장면 수 " + frames);
                }
            }
            Thread.Sleep(800);
            main = FindWindow(app.Id, "파자마 캡쳐");
            Check(main != null, "녹화가 끝나면 메인 창이 다시 보임");

            // 4) 녹화 틀(꿀캠 방식): 틀을 띄워 끌어서 크기·자리를 맞춘 뒤 녹화 → 결과 테두리 1점까지 맞는지
            FrameTest(app, main, frameStart, outDir, appData, gif);
        }
        finally
        {
            if (target != null) target.Close();
            SetCursorPos(original.X, original.Y);
            try { app.CloseMainWindow(); if (!app.WaitForExit(3000)) app.Kill(); } catch (Exception) { }
        }
        Console.WriteLine(failures == 0 ? "모두 통과" : "실패 " + failures + "건");
        return failures == 0 ? 0 : 1;
    }

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, int flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point pt, int flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);

    static Rectangle RectOf(IntPtr hwnd) { RECT r; GetWindowRect(hwnd, out r); return Rectangle.FromLTRB(r.L, r.T, r.R, r.B); }

    static uint DpiAt(Point p) { uint x, y; GetDpiForMonitor(MonitorFromPoint(p, 2), 0, out x, out y); return x; }

    /// <summary>바깥 1점은 빨강, 안쪽은 하늘색인 시험 무늬 창. 녹화 결과의 가장자리가 모두 빨강이면 틀 안쪽과 1점도 어긋나지 않은 것.</summary>
    sealed class Pattern : Form
    {
        public Pattern(Rectangle r)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
            Bounds = r; BackColor = Color.LightSkyBlue; Text = "시험 무늬";
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.LightSkyBlue);
            using (var p = new Pen(Color.Red, 1)) e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    static void FrameTest(Process app, AutomationElement main, Rectangle inner, string outDir, string appData, string lastGif)
    {
        Console.WriteLine("[녹화 틀]");
        Invoke(main, "▣ 녹화 틀");
        IntPtr fw = IntPtr.Zero;
        var sw = Stopwatch.StartNew();
        while (fw == IntPtr.Zero && sw.ElapsedMilliseconds < 4000) { fw = FindWindowW(null, "파자마 캡쳐 - 녹화 틀"); Thread.Sleep(100); }
        Check(fw != IntPtr.Zero, "녹화 틀이 뜸");
        if (fw == IntPtr.Zero) return;
        Thread.Sleep(400);
        Rectangle w = RectOf(fw);
        int t = inner.X - w.Left;
        Check(t >= 5 && w.Bottom == inner.Bottom + t && w.Top < inner.Top - t, "지난번 자리·크기로 뜸(안쪽 " + inner + ", 테두리 " + t + ", 창 " + w + ")");

        // 크기: 오른쪽 아래 모서리 +41,+21 / 왼쪽 가장자리 -20 — 끌기
        Drag(inner.Right + t / 2, inner.Bottom + t / 2, inner.Right + t / 2 + 41, inner.Bottom + t / 2 + 21);
        inner.Width += 41; inner.Height += 21;
        Thread.Sleep(200);
        Drag(inner.X - t / 2 - 1, inner.Y + inner.Height / 2, inner.X - t / 2 - 21, inner.Y + inner.Height / 2);
        inner.X -= 20; inner.Width += 20;
        Thread.Sleep(200);
        // 자리: 막대 왼쪽 손잡이를 끌어 -50,+30
        w = RectOf(fw);
        Point grip = new Point(w.Left + 8, (w.Top + inner.Top - t) / 2);
        Drag(grip.X, grip.Y, grip.X - 50, grip.Y + 30);
        inner.Offset(-50, 30);
        Thread.Sleep(300);
        w = RectOf(fw);
        Check(w.Left == inner.Left - t && w.Bottom == inner.Bottom + t, "끌어서 맞춘 틀 " + inner.Width + "×" + inner.Height + " (400×240 +41+20, +21) → 창 " + w);

        string gif = FrameRecord(fw, inner, t, outDir, lastGif, "100%");

        // 배율이 다른 모니터가 있으면 틀을 끌어 그리로 옮긴 뒤 한 번 더
        uint baseDpi = DpiAt(inner.Location);
        foreach (var s in Screen.AllScreens)
        {
            uint dpi = DpiAt(new Point(s.Bounds.X + s.Bounds.Width / 2, s.Bounds.Y + s.Bounds.Height / 2));
            if (dpi == baseDpi) continue;
            Console.WriteLine("  (배율 " + dpi * 100 / 96 + "% 모니터로 옮김)");
            var target = new Point(s.Bounds.X + 300, s.Bounds.Y + 400);
            w = RectOf(fw);
            grip = new Point(w.Left + 8, (w.Top + inner.Top - t) / 2);
            Drag(grip.X, grip.Y, grip.X + target.X - inner.X, grip.Y + target.Y - inner.Y);
            inner.Location = target;
            Thread.Sleep(600);
            w = RectOf(fw);
            int t2 = inner.X - w.Left;
            Check(w.Bottom == inner.Bottom + t2 && t2 > t, "옮긴 뒤 안쪽 크기 그대로, 테두리 " + t + "→" + t2 + " (창 " + w + ")");
            gif = FrameRecord(fw, inner, t2, outDir, gif, dpi * 100 / 96 + "%");
            break;
        }

        // 닫기: 손잡이를 눌러 틀을 앞으로 → Esc. 자리가 설정에 남는다
        w = RectOf(fw);
        Click(w.Left + 8, (w.Top + inner.Top - (inner.X - w.Left)) / 2);
        Key(0x1B);
        Thread.Sleep(600);
        Check(!IsWindowVisible(fw) || FindWindowW(null, "파자마 캡쳐 - 녹화 틀") == IntPtr.Zero, "Esc 로 틀 닫힘");
        string ini = File.ReadAllText(Path.Combine(appData, "PajamaCapture", "settings.ini"));
        string want = "frame=" + inner.X + "," + inner.Y + "," + inner.Width + "," + inner.Height;
        Check(ini.Contains(want), "다음에 쓸 틀 자리 저장 " + want);
    }

    static string FrameRecord(IntPtr fw, Rectangle inner, int t, string outDir, string lastGif, string label)
    {
        var existing = new System.Collections.Generic.HashSet<string>(Directory.GetFiles(outDir, "*.gif"));
        var pattern = new Pattern(inner);
        pattern.Show();
        SetWindowPos(pattern.Handle, new IntPtr(-1), inner.X, inner.Y, inner.Width, inner.Height, 0x0010);
        Application.DoEvents();
        Thread.Sleep(300);
        Rectangle before = RectOf(fw);
        Click(before.Left + 8, (before.Top + inner.Top - t) / 2); // 틀을 앞으로(키 입력용)
        Thread.Sleep(200);
        Key(0x0D);                                                   // Enter = ● 녹화
        Thread.Sleep(1500);
        Drag(inner.Right + t / 2, inner.Bottom + t / 2, inner.Right + t / 2 + 30, inner.Bottom + t / 2 + 30); // 녹화 중 크기 바꾸기 시도
        Thread.Sleep(300);
        Rectangle during = RectOf(fw); // 막대 글자가 바뀌어 오른쪽 끝은 달라질 수 있어 왼쪽·위·아래로 본다
        Check(during.Left == before.Left && during.Top == before.Top && during.Bottom == before.Bottom, "[" + label + "] 녹화 중에는 끌어도 크기가 안 바뀜 " + before + " → " + during);
        Thread.Sleep(500);
        Key(0x0D);                                                   // Enter = ■ 정지(틀이 앞에 있을 때)
        string gif = WaitFor(() => Directory.GetFiles(outDir, "*.gif").FirstOrDefault(f => !existing.Contains(f) && new FileInfo(f).Length > 0), 15000);
        Check(gif != null, "[" + label + "] 틀로 녹화한 GIF 저장");
        if (gif != null)
        {
            Thread.Sleep(600);
            using (var img = new Bitmap(gif))
            {
                Check(img.Width == inner.Width && img.Height == inner.Height, "[" + label + "] GIF 크기 = 틀 안쪽 " + inner.Width + "×" + inner.Height + " → 실제 " + img.Width + "×" + img.Height);
                if (img.Width == inner.Width && img.Height == inner.Height)
                {
                    int bad = 0, n = 0;
                    Func<Color, bool> red = c => c.R > 200 && c.G < 80 && c.B < 80;
                    Func<Color, bool> blue = c => c.B > 200 && c.R < 180 && c.G > 170;
                    for (int x = 0; x < img.Width; x++) { n += 2; if (!red(img.GetPixel(x, 0))) bad++; if (!red(img.GetPixel(x, img.Height - 1))) bad++; }
                    for (int y = 0; y < img.Height; y++) { n += 2; if (!red(img.GetPixel(0, y))) bad++; if (!red(img.GetPixel(img.Width - 1, y))) bad++; }
                    Check(bad == 0, "[" + label + "] 결과 가장자리 " + n + "점이 모두 시험 무늬의 빨간 테두리(틀 테두리·막대가 안 찍힘, 1점도 안 어긋남) — 어긋난 점 " + bad);
                    int inBad = 0;
                    for (int x = 1; x < img.Width - 1; x++) { if (!blue(img.GetPixel(x, 1))) inBad++; if (!blue(img.GetPixel(x, img.Height - 2))) inBad++; }
                    for (int y = 1; y < img.Height - 1; y++) { if (!blue(img.GetPixel(1, y))) inBad++; if (!blue(img.GetPixel(img.Width - 2, y))) inBad++; }
                    Check(inBad == 0, "[" + label + "] 그 바로 안쪽 한 줄은 하늘색 — 어긋난 점 " + inBad + ", 가운데 " + img.GetPixel(img.Width / 2, img.Height / 2));
                }
            }
        }
        pattern.Close();
        Application.DoEvents();
        Thread.Sleep(600);
        return gif ?? lastGif;
    }

    static AutomationElement FindWindow(int pid, string title)
    {
        var cond = new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, pid), new PropertyCondition(AutomationElement.NameProperty, title));
        return AutomationElement.RootElement.FindFirst(TreeScope.Children, cond);
    }

    static void Invoke(AutomationElement window, string startsWith)
    {
        var buttons = window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        foreach (AutomationElement b in buttons)
            if (b.Current.Name.StartsWith(startsWith))
            {
                ((InvokePattern)b.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                return;
            }
        throw new Exception("버튼을 못 찾음: " + startsWith);
    }

    static T WaitFor<T>(Func<T> f, int ms) where T : class
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { var v = f(); if (v != null) return v; Thread.Sleep(100); }
        return null;
    }

    static string WaitNewFile(string dir, string pattern, int ms, string except = null)
    {
        return WaitFor(() => Directory.GetFiles(dir, pattern).FirstOrDefault(f => f != except && new FileInfo(f).Length > 0), ms);
    }

    const int DOWN = 0x2, UP = 0x4;
    static void Drag(int x1, int y1, int x2, int y2)
    {
        SetCursorPos(x1, y1); Thread.Sleep(80);
        mouse_event(DOWN, 0, 0, 0, IntPtr.Zero); Thread.Sleep(80);
        for (int i = 1; i <= 10; i++) { SetCursorPos(x1 + (x2 - x1) * i / 10, y1 + (y2 - y1) * i / 10); Thread.Sleep(25); }
        Thread.Sleep(80);
        mouse_event(UP, 0, 0, 0, IntPtr.Zero);
    }

    static void Click(int x, int y)
    {
        SetCursorPos(x, y); Thread.Sleep(150);
        SetCursorPos(x + 1, y); Thread.Sleep(80); // 마우스 움직임 알림으로 창 강조가 갱신되게
        mouse_event(DOWN, 0, 0, 0, IntPtr.Zero); Thread.Sleep(60);
        mouse_event(UP, 0, 0, 0, IntPtr.Zero);
    }

    static void Key(byte vk)
    {
        keybd_event(vk, 0, 0, IntPtr.Zero); Thread.Sleep(40);
        keybd_event(vk, 0, 2, IntPtr.Zero);
    }
}
