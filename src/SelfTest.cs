// 시험용 실행 방식 — 사람이 마우스를 움직이지 않아도 화면 그리기·창 찾기·녹화를 확인한다.
//   파자마캡쳐.exe --selftest overlay <저장.png> <area|window|record> [hover]
//   파자마캡쳐.exe --selftest main <저장.png>
//   파자마캡쳐.exe --selftest windows
//   파자마캡쳐.exe --selftest record <gif|mp4> <초> <저장 파일> [ffmpeg.exe] [x y w h]
//   파자마캡쳐.exe --selftest gif <저장 폴더>      (합성 장면으로 GIF 만들기 + 원본 장면 PNG)
//   파자마캡쳐.exe --selftest popups <저장 폴더>   (녹화 막대·알림 창 그림 — 이 창들은 화면 캡쳐에 안 찍혀서 따로 그린다)
//   파자마캡쳐.exe --selftest edit <원본.gif> <저장 폴더>     (간단 편집: 불러오기·홀짝 솎기·지우기·시간·잘라내기·크기 → 저장, 기대값 json)
//   파자마캡쳐.exe --selftest editor <원본.gif> <저장 폴더>   (GIF 편집 창 그림: 평소·잘라내기 중)
//   파자마캡쳐.exe --selftest mp4edit <원본.mp4> <저장.mp4> <ffmpeg.exe> [창 그림.png]  (MP4 크기 절반으로)
//   파자마캡쳐.exe --selftest frame <저장 폴더> [ffmpeg.exe]  (녹화 틀: 창 자리·짝수 맞춤·모니터별 배율·그림, ffmpeg 가 있으면 MP4 녹화 크기)
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace PajamaCapture
{
    static class SelfTest
    {
        public static int Run(string[] args)
        {
            // 시험 결과 한글이 깨지지 않게 UTF-8 로 내보낸다(창 프로그램이라 콘솔 코드 페이지 설정이 안 먹는다)
            var utf8 = new System.Text.UTF8Encoding(false);
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
            try
            {
                switch (args.Length > 1 ? args[1] : "")
                {
                    case "overlay": return OverlayShot(args[2], args.Length > 3 ? args[3] : "area", args.Length > 4 && args[4] == "hover");
                    case "main": return MainShot(args[2]);
                    case "windows": return ListWindows();
                    case "record": return Record(args);
                    case "gif": return SyntheticGif(args[2]);
                    case "popups": return Popups(args[2]);
                    case "edit": return EditGif(args[2], args[3]);
                    case "editor": return EditorShot(args[2], args[3]);
                    case "mp4edit": return EditMp4(args[2], args[3], args[4], args.Length > 5 ? args[5] : null);
                    case "frame": return FrameCheck(args[2], args.Length > 3 ? args[3] : null);
                }
                Console.Error.WriteLine("알 수 없는 시험: " + string.Join(" ", args));
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("시험 실패: " + ex);
                return 1;
            }
        }

        static int OverlayShot(string output, string mode, bool hover)
        {
            Rectangle virt = Native.VirtualScreen;
            var windows = WindowFinder.List(virt);
            using (var shot = ScreenCapturer.Snap(virt))
            using (var ov = new Overlay(mode == "window" ? OverlayMode.Window : mode == "record" ? OverlayMode.Record : OverlayMode.Area, shot, virt, windows))
            {
                Rectangle primary = Screen.PrimaryScreen.Bounds;
                if (hover) ov.TestHover(new Point(primary.X + primary.Width / 2, primary.Y + primary.Height / 2));
                else ov.TestSelect(new Rectangle(primary.X + primary.Width / 4, primary.Y + primary.Height / 4, primary.Width / 2, primary.Height / 3));
                using (var canvas = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppPArgb))
                {
                    using (var g = Graphics.FromImage(canvas)) ov.PaintScene(g, new Rectangle(0, 0, shot.Width, shot.Height));
                    var local = primary;
                    local.Offset(-virt.X, -virt.Y);
                    using (var part = ImageTools.Crop(canvas, local)) part.Save(output, ImageFormat.Png);
                }
            }
            Console.WriteLine("저장: " + output);
            return 0;
        }

        static int MainShot(string output)
        {
            using (var f = new MainForm())
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-5000, -5000);
                f.Show();
                Application.DoEvents();
                using (var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    bmp.Save(output, ImageFormat.Png);
                }
                Console.WriteLine("창 안쪽 크기: " + f.ClientSize.Width + "x" + f.ClientSize.Height);
            }
            return 0;
        }

        static int Popups(string folder)
        {
            Directory.CreateDirectory(folder);
            var area = new Rectangle(Screen.PrimaryScreen.Bounds.X + 200, Screen.PrimaryScreen.Bounds.Y + 200, 400, 300);
            var thumb = new Bitmap(320, 200);
            using (var g = Graphics.FromImage(thumb)) g.Clear(Color.LightSkyBlue);
            var forms = new Form[] {
                new RecordBar(area, "GIF", () => 83000),
                new Toast("클립보드에 복사했어요", "541 × 321 · 파자마캡쳐_20261107_201708.png", @"C:\없음.png", thumb, false),
                new Toast("녹화를 저장했어요", "3.0초 · 1.2MB · 파자마캡쳐_20261108_101500.gif", @"C:\없음.gif", new Bitmap(thumb), false) { EditRequested = p => { } } };
            string[] names = { "bar", "toast", "toast_edit" };
            for (int i = 0; i < forms.Length; i++)
            {
                var f = forms[i];
                f.Show();
                f.Location = new Point(-5000, -5000);
                Application.DoEvents();
                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    bmp.Save(Path.Combine(folder, "popup_" + names[i] + ".png"), ImageFormat.Png);
                }
                Console.WriteLine(names[i] + " " + f.Width + "x" + f.Height);
                f.Close();
            }
            return 0;
        }

        static int ListWindows()
        {
            Console.WriteLine("전체 화면: " + Native.VirtualScreen);
            foreach (var s in Screen.AllScreens) Console.WriteLine("모니터: " + s.DeviceName + " " + s.Bounds + " 배율 " + Native.ScaleAt(s.Bounds.Location));
            foreach (var w in WindowFinder.List(Native.VirtualScreen)) Console.WriteLine(w.Bounds + "  " + w.Title);
            return 0;
        }

        static int Record(string[] args)
        {
            bool mp4 = args[2] == "mp4";
            double seconds = double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
            string output = args[4];
            string ffmpeg = args.Length > 5 && args[5] != "-" ? args[5] : FfmpegLocator.Find(null);
            Rectangle area = args.Length > 9
                ? new Rectangle(int.Parse(args[6]), int.Parse(args[7]), int.Parse(args[8]), int.Parse(args[9]))
                : new Rectangle(Screen.PrimaryScreen.Bounds.X + 100, Screen.PrimaryScreen.Bounds.Y + 100, 640, 360);

            // 실제처럼 테두리와 막대를 띄운 채 녹화한다(둘 다 결과에 찍히면 안 된다)
            var rec = new Recorder(area, mp4, 15, true, output, ffmpeg);
            var finished = new ManualResetEvent(false);
            rec.Finished += r => finished.Set();
            var frame = new RecordFrame(area);
            var bar = new RecordBar(new Rectangle(area.X, area.Y, area.Width, area.Height), mp4 ? "MP4" : "GIF", () => rec.ElapsedMs);
            frame.Show();
            bar.Show();
            bar.Location = new Point(area.X + 20, area.Y + 20); // 일부러 녹화 범위 안에 둔다
            rec.Start();
            var until = DateTime.Now.AddSeconds(seconds);
            while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(15); }
            rec.Stop(false);
            bar.ShowSaving();
            while (!finished.WaitOne(15)) Application.DoEvents();
            frame.Close();
            bar.Close();
            if (rec.Error != null) { Console.Error.WriteLine("녹화 오류: " + rec.Error); return 1; }
            Console.WriteLine(string.Format("녹화 완료: {0} 바이트, 찍은 장면 {1}, 버린 장면 {2}, 길이 {3}ms",
                new FileInfo(output).Length, rec.CapturedFrames, rec.DroppedFrames, rec.ElapsedMs));
            return 0;
        }

        /// <summary>간단 편집 속을 차례로 돌려 본다. 단계마다 기대 프레임 수·시간을 확인하고, 마지막 결과 GIF 와 비교용 PNG·json 을 남긴다.</summary>
        static int EditGif(string input, string folder)
        {
            Directory.CreateDirectory(folder);
            bool ok = true;
            Action<bool, string> check = (good, what) => { Console.WriteLine((good ? "통과: " : "실패: ") + what); ok &= good; };
            using (var p = GifProject.Load(input, 32, null, null))
            {
                // 불러온 원본 프레임(파이썬이 Pillow 로 푼 것과 비교)
                for (int i = 0; i < p.Count; i++)
                    using (var b = p.Store.ReadBitmap(p.Frames[i].Source)) b.Save(Path.Combine(folder, "src_" + i + ".png"), ImageFormat.Png);
                var srcDelays = new System.Collections.Generic.List<string>();
                foreach (var f in p.Frames) srcDelays.Add((f.DelayCs * 10).ToString());
                Console.WriteLine("불러옴: " + p.Count + "장, " + p.SourceSize.Width + "x" + p.SourceSize.Height + ", " + p.TotalMs + "ms");
                long total = p.TotalMs;
                int n = p.Count;

                int removed = p.KeepAlternate(true);
                check(p.Count == n / 2 && removed == n - n / 2 && p.TotalMs == total, "짝수 프레임만 — " + p.Count + "장, 재생 시간 " + p.TotalMs + "ms 그대로");
                check(p.Undo() && p.Count == n && p.TotalMs == total, "되돌리기");
                p.KeepAlternate(false);
                check(p.Count == (n + 1) / 2 && p.TotalMs == total, "홀수 프레임만 — " + p.Count + "장, 재생 시간 그대로");
                p.Undo();

                p.Delete(new[] { 1 });
                check(p.Count == n - 1, "1장 지우기");
                bool refused = false;
                try { var all = new System.Collections.Generic.List<int>(); for (int i = 0; i < p.Count; i++) all.Add(i); p.Delete(all); }
                catch (InvalidOperationException) { refused = true; }
                check(refused && p.Count == n - 1, "모두 지우기는 막기");

                p.SetDelay(new[] { 0 }, 7);
                check(p.Frames[0].DelayCs == 7, "첫 프레임 0.07초");
                int before = p.Frames[1].DelayCs;
                p.ScaleSpeed(new[] { 1 }, 200);
                check(p.Frames[1].DelayCs == Math.Max(2, (int)Math.Round(before / 2.0)), "둘째 프레임 두 배 빠르게");
                p.SetDelay(new[] { 2 }, 1);
                check(p.Frames[2].DelayCs == GifProject.MinDelayCs, "0.02초보다 짧게는 안 됨");

                var crop = new Rectangle(20, 30, Math.Min(200, p.SourceSize.Width - 20), Math.Min(120, p.SourceSize.Height - 30));
                p.SetCrop(crop);
                check(p.Crop == crop && p.Output == crop.Size, "잘라내기 " + crop);
                p.SetOutput(new Size(crop.Width / 2, crop.Height / 2));
                check(p.Output == new Size(crop.Width / 2, crop.Height / 2), "캔버스 크기 절반");

                string output = Path.Combine(folder, "edited.gif");
                p.Save(output, null);
                var delays = new System.Collections.Generic.List<string>();
                for (int i = 0; i < p.Count; i++)
                {
                    delays.Add((p.Frames[i].DelayCs * 10).ToString());
                    using (var src = p.Store.ReadBitmap(p.Frames[i].Source))
                    using (var o = p.RenderOutput(src)) o.Save(Path.Combine(folder, "want_" + i + ".png"), ImageFormat.Png);
                }
                File.WriteAllText(Path.Combine(folder, "expected.json"), "{\"frames\": " + p.Count + ", \"width\": " + p.Output.Width + ", \"height\": " + p.Output.Height +
                    ", \"durations\": [" + string.Join(", ", delays) + "], \"source_frames\": " + n + ", \"source_durations\": [" + string.Join(", ", srcDelays) + "]}");
                Console.WriteLine("저장: " + output + " (" + new FileInfo(output).Length + " 바이트)");
            }
            return ok ? 0 : 1;
        }

        /// <summary>GIF 편집 창을 화면 밖에 띄워 그림으로 남긴다(평소·잘라내기 중).</summary>
        static int EditorShot(string input, string folder)
        {
            Directory.CreateDirectory(folder);
            using (var f = new GifEditorForm(input))
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-5000, -5000);
                f.Show();
                var until = DateTime.Now.AddSeconds(20);
                while (f.Project == null && DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(15); }
                if (f.Project == null) { Console.Error.WriteLine("편집 창이 GIF 를 불러오지 못했어요."); return 1; }
                for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(10); }
                Snap(f, Path.Combine(folder, "editor.png"));
                // 잘라내기 중 화면
                var crop = f.GetType().GetMethod("EnterCrop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                crop.Invoke(f, null);
                var preview = FindControl<PreviewBox>(f);
                preview.Selection = new Rectangle(20, 30, Math.Min(200, f.Project.SourceSize.Width - 20), Math.Min(120, f.Project.SourceSize.Height - 30));
                f.GetType().GetMethod("SyncCropFields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(f, null);
                for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(10); }
                Snap(f, Path.Combine(folder, "editor_crop.png"));
                // 대화 상자들
                var dialogs = new Form[] { new DelayDialog(3, f.Project.Count, 10), new CanvasSizeDialog(f.Project.Output, f.Project.Crop.Size) };
                string[] names = { "dialog_delay.png", "dialog_size.png" };
                for (int i = 0; i < dialogs.Length; i++)
                {
                    dialogs[i].StartPosition = FormStartPosition.Manual;
                    dialogs[i].Location = new Point(-5000, -5000);
                    dialogs[i].Show();
                    for (int k = 0; k < 10; k++) { Application.DoEvents(); Thread.Sleep(10); }
                    Snap(dialogs[i], Path.Combine(folder, names[i]));
                    dialogs[i].Close();
                    dialogs[i].Dispose();
                }
                Console.WriteLine("편집 창 안쪽 크기: " + f.ClientSize.Width + "x" + f.ClientSize.Height + ", 프레임 " + f.Project.Count + "장");

                // 버튼이 부르는 처리를 차례로 불러 목록·정보 줄이 따라 바뀌는지 본다
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                Func<string, object[], object> call = (name, a) => f.GetType().GetMethod(name, flags).Invoke(f, a);
                var list = FindControl<ListView>(f);
                bool ok = true;
                Action<bool, string> check = (good, what) => { Console.WriteLine((good ? "통과: " : "실패: ") + what); ok &= good; };
                int n = f.Project.Count;
                Rectangle want = preview.Selection;
                call("ApplyCrop", null);
                check(f.Project.Crop == want && !preview.CropMode, "잘라내기 적용 → " + f.Project.Output.Width + "x" + f.Project.Output.Height);
                call("Thin", new object[] { false });
                check(list.Items.Count == f.Project.Count && f.Project.Count == (n + 1) / 2, "홀수 프레임만 → 목록 " + list.Items.Count + "줄");
                list.SelectedIndices.Clear();
                list.Items[0].Selected = true;
                call("DeleteSelected", null);
                check(list.Items.Count == f.Project.Count && f.Project.Count == (n + 1) / 2 - 1, "고른 프레임 삭제 → 목록 " + list.Items.Count + "줄");
                call("DoUndo", null);
                call("DoUndo", null);
                call("DoUndo", null);
                check(list.Items.Count == n && !f.Project.IsCropped, "되돌리기 세 번 → 처음 상태");
                for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(10); }
                f.GetType().GetField("dirty", flags).SetValue(f, false); // 닫을 때 묻지 않게
                if (!ok) return 1;            }
            return 0;
        }

        static T FindControl<T>(Control parent) where T : Control
        {
            foreach (Control c in parent.Controls)
            {
                if (c is T) return (T)c;
                var inner = FindControl<T>(c);
                if (inner != null) return inner;
            }
            return null;
        }

        static void Snap(Form f, string output) // 제목줄까지 창 전체
        {
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(output, ImageFormat.Png);
            }
        }

        /// <summary>MP4 크기 바꾸기 창을 띄워 크기를 절반으로 저장하고, 결과 크기를 ffmpeg 로 다시 읽어 확인한다.</summary>
        static int EditMp4(string input, string output, string ffmpeg, string shot)
        {
            using (var f = new Mp4ResizeForm(input, ffmpeg))
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-5000, -5000);
                f.Show();
                var until = DateTime.Now.AddSeconds(30);
                while (f.Source == null && DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(15); }
                if (f.Source == null) { Console.Error.WriteLine("MP4 정보를 읽지 못했어요."); return 1; }
                Console.WriteLine("원본: " + f.Source.Width + "x" + f.Source.Height + ", " + f.Source.Seconds + "초");
                f.Inputs.Width.Value = Mp4Tool.Even(f.Source.Width / 2); // 비율 유지가 세로를 따라 바꾼다
                Size want = f.Inputs.Value;
                for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(10); }
                if (shot != null) Snap(f, shot);
                Exception error = null;
                bool done = false;
                f.Saved += (p, ex) => { error = ex; done = true; };
                f.StartSave(output, want);
                until = DateTime.Now.AddMinutes(3);
                while (!done && DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(15); }
                if (!done) { Console.Error.WriteLine("변환이 끝나지 않았어요."); return 1; }
                if (error != null) { Console.Error.WriteLine("변환 실패: " + error.Message); return 1; }
                var r = Mp4Tool.Probe(ffmpeg, output);
                bool good = r.Width == want.Width && r.Height == want.Height && want.Width % 2 == 0 && want.Height % 2 == 0
                    && Math.Abs(r.Seconds - f.Source.Seconds) < 0.3;
                Console.WriteLine((good ? "통과: " : "실패: ") + "결과 " + r.Width + "x" + r.Height + " (바란 크기 " + want.Width + "x" + want.Height + "), " + r.Seconds + "초");
                return good ? 0 : 1;
            }
        }

        /// <summary>녹화 틀을 실제로 띄워 창이 안쪽 범위 바깥에 정확히 붙는지, MP4 짝수 맞춤, 모니터마다의 배율을 본다.</summary>
        static int FrameCheck(string folder, string ffmpeg)
        {
            Directory.CreateDirectory(folder);
            bool ok = true;
            Action<bool, string> check = (good, what) => { Console.WriteLine((good ? "통과: " : "실패: ") + what); ok &= good; };
            Action pump = () => { for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(10); } };
            Rectangle prim = Screen.PrimaryScreen.Bounds;
            using (var fw = new FrameWindow(new Rectangle(prim.X + 200, prim.Y + 300, 333, 201), false, "GIF"))
            {
                fw.Show();
                pump();
                Func<string> where = () => "안쪽 " + fw.Inner + ", 창 " + WindowRectOf(fw.Handle) + ", 테두리 " + fw.EdgeThickness + ", 배율 " + fw.UiScale;
                Func<bool> attached = () =>
                {
                    Rectangle win = WindowRectOf(fw.Handle), outer = Rectangle.Inflate(fw.Inner, fw.EdgeThickness, fw.EdgeThickness);
                    return win == fw.WindowRect && win.Contains(outer) && win.Left == outer.Left && win.Bottom == outer.Bottom;
                };
                check(fw.Inner == new Rectangle(prim.X + 200, prim.Y + 300, 333, 201), "GIF 는 홀수 크기 그대로 " + fw.Inner.Size);
                check(attached(), "창이 안쪽 범위 바깥에 붙음(막대는 위) — " + where());
                using (var b = fw.Snapshot()) b.Save(Path.Combine(folder, "frame_idle.png"), ImageFormat.Png);

                fw.EvenOnly = true;
                pump();
                check(fw.Inner.Width == 332 && fw.Inner.Height == 200, "MP4 로 바꾸면 짝수로 → " + fw.Inner.Size);
                fw.SetInner(new Rectangle(prim.X + 200, prim.Y + 300, 641, 359));
                check(fw.Inner.Width % 2 == 0 && fw.Inner.Height % 2 == 0, "MP4 일 때 홀수 크기를 넣어도 짝수 → " + fw.Inner.Size);
                fw.SetInner(new Rectangle(prim.X + 200, prim.Y + 300, 5, 5));
                check(fw.Inner.Width == FrameWindow.MinSide && fw.Inner.Height == FrameWindow.MinSide, "가장 작은 크기 " + FrameWindow.MinSide + " → " + fw.Inner.Size);
                fw.SetInner(new Rectangle(prim.X + 200, prim.Y + 300, 640, 360));

                fw.ShowRecording(() => 65000, "MP4");
                pump();
                fw.SetInner(new Rectangle(prim.X + 10, prim.Y + 300, 100, 100));
                check(fw.Inner == new Rectangle(prim.X + 200, prim.Y + 300, 640, 360), "녹화 중에는 크기·자리가 잠김");
                fw.Close();
                pump();
                check(fw.Visible, "녹화 중에는 닫기도 막힘");
                using (var b = fw.Snapshot()) b.Save(Path.Combine(folder, "frame_recording.png"), ImageFormat.Png);
                fw.ShowIdle();

                // 화면 위쪽에 붙이면 막대가 아래로
                fw.SetInner(new Rectangle(prim.X + 200, prim.Y + 2, 400, 200));
                pump();
                Rectangle w2 = WindowRectOf(fw.Handle);
                check(w2.Top == fw.Inner.Top - fw.EdgeThickness && w2.Bottom > fw.Inner.Bottom + fw.EdgeThickness, "위에 자리가 없으면 막대가 아래 — " + where());

                // 배율이 다른 모니터가 있으면 그리로 옮겨 본다
                foreach (var s in Screen.AllScreens)
                {
                    float sc = Native.ScaleAt(s.Bounds.Location);
                    if (sc == Native.ScaleAt(prim.Location)) continue;
                    var target = new Rectangle(s.Bounds.X + 300, s.Bounds.Y + 400, 640, 360);
                    fw.SetInner(target);
                    pump();
                    check(fw.Inner == target && Math.Abs(fw.UiScale - Math.Max(1f, sc)) < 0.01f && attached(),
                        "배율 " + (int)(sc * 100) + "% 모니터 " + s.DeviceName + " — 크기는 화면 점 그대로, 막대만 배율 — " + where());
                    using (var b = fw.Snapshot()) b.Save(Path.Combine(folder, "frame_scaled.png"), ImageFormat.Png);
                    break;
                }

                // ffmpeg 가 있으면 틀 안쪽을 실제 MP4 로 녹화해 크기를 확인
                if (ffmpeg != null && File.Exists(ffmpeg))
                {
                    fw.SetInner(new Rectangle(prim.X + 200, prim.Y + 300, 501, 301));
                    pump();
                    Rectangle area = fw.Inner;
                    string mp4 = Path.Combine(folder, "frame.mp4");
                    var rec = new Recorder(area, true, 15, false, mp4, ffmpeg);
                    var finished = new ManualResetEvent(false);
                    rec.Finished += r => finished.Set();
                    fw.ShowRecording(() => rec.ElapsedMs, "MP4");
                    rec.Start();
                    var until = DateTime.Now.AddSeconds(2);
                    while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(15); }
                    rec.Stop(false);
                    fw.ShowSaving();
                    while (!finished.WaitOne(15)) Application.DoEvents();
                    fw.ShowIdle();
                    if (rec.Error != null) check(false, "MP4 녹화 오류: " + rec.Error);
                    else
                    {
                        var info = Mp4Tool.Probe(ffmpeg, mp4);
                        check(info.Width == area.Width && info.Height == area.Height, "MP4 결과 " + info.Width + "x" + info.Height + " = 틀 안쪽 " + area.Width + "x" + area.Height + " (501×301 → 짝수 맞춤)");
                    }
                }
                fw.ForceClose();
            }
            return ok ? 0 : 1;
        }

        static Rectangle WindowRectOf(IntPtr hwnd)
        {
            Native.RECT r;
            Native.GetWindowRect(hwnd, out r);
            return r.ToRectangle();
        }

        /// <summary>움직이는 상자·그라데이션·멈춘 구간이 있는 합성 장면으로 GIF 를 만든다. 원본 장면도 PNG 로 남겨 비교한다.</summary>
        static int SyntheticGif(string folder)
        {
            Directory.CreateDirectory(folder);
            int w = 320, h = 200;
            var times = new long[] { 0, 100, 200, 300, 300, 305, 500, 900, 1000 };
            using (var enc = new GifEncoder(Path.Combine(folder, "test.gif"), w, h))
            {
                for (int f = 0; f < times.Length; f++)
                {
                    int step = f == 4 ? 3 : f == 5 ? 9 : f; // 4번은 3번과 같은 그림, 5번은 너무 촘촘해서 건너뛰어야 한다
                    if (f == 7) step = 6;                   // 7번은 6번과 같은 그림(멈춘 구간)
                    using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(255, 143, 163), Color.FromArgb(60, 40, 120), 30f))
                                g.FillRectangle(br, 0, 0, w, h);
                            g.FillRectangle(Brushes.White, 20 + step * 25, 60, 60, 60);
                            g.DrawString("파자마 " + step, new Font("Malgun Gothic", 14f), Brushes.Black, 10, 10);
                        }
                        if (f != 4 && f != 5 && f != 7) bmp.Save(Path.Combine(folder, "frame_" + f + ".png"), ImageFormat.Png);
                        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                        var bytes = new byte[w * h * 4];
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                        bmp.UnlockBits(data);
                        enc.AddFrame(bytes, times[f]);
                    }
                }
                enc.Finish(1400);
                Console.WriteLine("GIF 장면 수: " + enc.FrameCount);
            }
            return 0;
        }
    }
}
