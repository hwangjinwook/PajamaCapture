// 간단 편집의 속 — GIF 를 프레임으로 풀어 고친 뒤 다시 저장하는 일과, MP4 크기를 ffmpeg 로 바꾸는 일. 화면은 EditorForms.cs.
// 자세한 영상 편집은 「파자마컷」에서 하도록, 여기서는 프레임 솎기·지우기·시간·캔버스 크기·잘라내기만 한다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PajamaCapture
{
    /// <summary>GIF 프레임 원본(한 점 4바이트)을 임시 파일에 두고 필요할 때 읽는다. 큰 녹화도 메모리를 많이 쓰지 않게 한다.</summary>
    sealed class FrameStore : IDisposable
    {
        public readonly int Width, Height;
        readonly FileStream file;
        readonly object gate = new object();
        int count;

        public FrameStore(int width, int height)
        {
            Width = width;
            Height = height;
            string dir = Path.Combine(Path.GetTempPath(), "PajamaCaptureEdit");
            Directory.CreateDirectory(dir);
            CleanOld(dir);
            string path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".raw");
            file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);
        }

        public int FrameBytes { get { return Width * Height * 4; } }
        public int Count { get { return count; } }

        public void Add(byte[] bgra)
        {
            lock (gate)
            {
                file.Position = (long)count * FrameBytes;
                file.Write(bgra, 0, FrameBytes);
                count++;
            }
        }

        public void Read(int index, byte[] into)
        {
            lock (gate)
            {
                file.Position = (long)index * FrameBytes;
                int done = 0;
                while (done < FrameBytes)
                {
                    int n = file.Read(into, done, FrameBytes - done);
                    if (n <= 0) throw new IOException("편집용 임시 파일을 읽지 못했어요.");
                    done += n;
                }
            }
        }

        public Bitmap ReadBitmap(int index)
        {
            var bytes = new byte[FrameBytes];
            Read(index, bytes);
            return Pixels.ToBitmap(bytes, Width, Height);
        }

        /// <summary>프로그램이 갑자기 꺼져 남은 하루 지난 임시 파일을 지운다(쓰는 중인 파일은 잠겨 있어 못 지운다).</summary>
        static void CleanOld(string dir)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*.raw"))
                    try { if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-1)) File.Delete(f); } catch (Exception) { }
            }
            catch (Exception) { }
        }

        public void Dispose() { lock (gate) file.Dispose(); }
    }

    /// <summary>그림과 바이트 배열(파랑·초록·빨강·알파) 사이를 오간다.</summary>
    static class Pixels
    {
        public static Bitmap ToBitmap(byte[] bgra, int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int row = w * 4;
                for (int y = 0; y < h; y++) Marshal.Copy(bgra, y * row, IntPtr.Add(data.Scan0, y * data.Stride), row);
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }

        public static void ToBytes(Bitmap bmp, byte[] into)
        {
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int row = w * 4;
                for (int y = 0; y < h; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), into, y * row, row);
            }
            finally { bmp.UnlockBits(data); }
        }
    }

    struct GifFrame
    {
        public int Source;   // FrameStore 안의 원본 프레임 번호
        public int DelayCs;  // 표시 시간(1/100초)
    }

    /// <summary>편집 중인 GIF — 프레임 목록(원본 번호+시간), 잘라낼 범위(원본 좌표), 저장 크기. 모든 고치기는 되돌릴 수 있다.</summary>
    sealed class GifProject : IDisposable
    {
        public const int MinDelayCs = 2, MaxDelayCs = 65535; // 0.02초보다 짧으면 웹 브라우저가 0.1초로 바꿔 보여 준다

        public readonly string SourcePath;
        public readonly FrameStore Store;
        public readonly List<Bitmap> Thumbs; // 원본 프레임 번호별 작은 그림
        public List<GifFrame> Frames;
        public Rectangle Crop;
        public Size Output;
        readonly Stack<Snapshot> undo = new Stack<Snapshot>();

        sealed class Snapshot
        {
            public GifFrame[] Frames;
            public Rectangle Crop;
            public Size Output;
        }

        GifProject(string path, FrameStore store, List<Bitmap> thumbs, List<GifFrame> frames)
        {
            SourcePath = path;
            Store = store;
            Thumbs = thumbs;
            Frames = frames;
            Crop = new Rectangle(0, 0, store.Width, store.Height);
            Output = Crop.Size;
        }

        public Size SourceSize { get { return new Size(Store.Width, Store.Height); } }
        public int Count { get { return Frames.Count; } }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool IsCropped { get { return Crop != new Rectangle(Point.Empty, SourceSize); } }

        public long TotalMs
        {
            get
            {
                long cs = 0;
                foreach (var f in Frames) cs += f.DelayCs;
                return cs * 10;
            }
        }

        // ── 불러오기 ──

        /// <summary>GIF 를 프레임마다 완성된 그림으로 풀어 임시 파일에 담는다. progress(지금, 전체)는 일꾼 스레드에서 불린다.</summary>
        public static GifProject Load(string path, int thumbHeight, Action<int, int> progress, Func<bool> canceled)
        {
            byte[] bytes = File.ReadAllBytes(path); // 원본 파일을 잠그지 않아야 같은 이름으로 다시 저장할 수 있다
            Image img;
            var ms = new MemoryStream(bytes);
            try { img = Image.FromStream(ms); }
            catch (ArgumentException) { ms.Dispose(); throw new Exception("GIF 그림으로 읽을 수 없는 파일이에요."); }
            using (ms)
            using (img)
            {
                if (!img.RawFormat.Equals(ImageFormat.Gif)) throw new Exception("GIF 파일이 아니에요.");
                var dim = FrameDimension.Time;
                int n = img.GetFrameCount(dim);
                int[] delays = ReadDelays(img, n);
                int w = img.Width, h = img.Height;
                var store = new FrameStore(w, h);
                var thumbs = new List<Bitmap>();
                var frames = new List<GifFrame>();
                try
                {
                    var buffer = new byte[store.FrameBytes];
                    using (var canvas = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    using (var g = Graphics.FromImage(canvas))
                    {
                        for (int i = 0; i < n; i++)
                        {
                            if (canceled != null && canceled()) throw new OperationCanceledException();
                            img.SelectActiveFrame(dim, i); // GDI+ 가 앞 프레임에 덧그린 완성 그림을 준다
                            g.Clear(Color.White);        // 투명한 곳은 흰 바탕으로
                            g.DrawImage(img, new Rectangle(0, 0, w, h));
                            Pixels.ToBytes(canvas, buffer);
                            store.Add(buffer);
                            thumbs.Add(MakeThumb(canvas, thumbHeight));
                            frames.Add(new GifFrame { Source = i, DelayCs = delays[i] });
                            if (progress != null) progress(i + 1, n);
                        }
                    }
                }
                catch
                {
                    store.Dispose();
                    foreach (var t in thumbs) t.Dispose();
                    throw;
                }
                return new GifProject(path, store, thumbs, frames);
            }
        }

        static int[] ReadDelays(Image img, int n)
        {
            var d = new int[n];
            for (int i = 0; i < n; i++) d[i] = 10;
            try
            {
                PropertyItem p = img.GetPropertyItem(0x5100); // 프레임마다 4바이트, 1/100초 단위
                for (int i = 0; i < n && i * 4 + 3 < p.Value.Length; i++)
                {
                    int v = BitConverter.ToInt32(p.Value, i * 4);
                    d[i] = v < MinDelayCs ? 10 : Math.Min(v, MaxDelayCs);
                }
            }
            catch (Exception) { }
            return d;
        }

        static Bitmap MakeThumb(Bitmap source, int height)
        {
            height = Math.Max(8, height);
            int width = Math.Max(1, Math.Min(height * 2, (int)Math.Round(source.Width * (double)height / source.Height)));
            int th = Math.Max(1, Math.Min(height, (int)Math.Round(source.Height * (double)width / source.Width)));
            var t = new Bitmap(width, th, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(t))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, width, th));
            }
            return t;
        }

        // ── 고치기(모두 되돌리기 가능) ──

        void Remember()
        {
            undo.Push(new Snapshot { Frames = Frames.ToArray(), Crop = Crop, Output = Output });
        }

        public bool Undo()
        {
            if (undo.Count == 0) return false;
            var s = undo.Pop();
            Frames = new List<GifFrame>(s.Frames);
            Crop = s.Crop;
            Output = s.Output;
            return true;
        }

        static int Clamp(long cs) { return (int)Math.Max(MinDelayCs, Math.Min(MaxDelayCs, cs)); }

        /// <summary>짝수 번째(2·4·6…)나 홀수 번째(1·3·5…) 프레임만 남긴다. 지운 프레임의 시간은 바로 앞에 남은 프레임에 더해 전체 재생 시간을 지킨다. 지운 수를 돌려준다.</summary>
        public int KeepAlternate(bool even)
        {
            if (Frames.Count < 2) return 0;
            var kept = new List<GifFrame>();
            long pending = 0; // 맨 앞에서 지운 프레임 시간(남은 첫 프레임에 더한다)
            for (int i = 0; i < Frames.Count; i++)
            {
                GifFrame f = Frames[i];
                bool keep = even ? i % 2 == 1 : i % 2 == 0; // i 는 0부터, 화면 번호는 1부터
                if (keep)
                {
                    f.DelayCs = Clamp(f.DelayCs + pending);
                    pending = 0;
                    kept.Add(f);
                }
                else if (kept.Count > 0)
                {
                    GifFrame last = kept[kept.Count - 1];
                    last.DelayCs = Clamp((long)last.DelayCs + f.DelayCs);
                    kept[kept.Count - 1] = last;
                }
                else pending += f.DelayCs;
            }
            Remember();
            int removed = Frames.Count - kept.Count;
            Frames = kept;
            return removed;
        }

        /// <summary>고른 프레임을 지운다. 모두 지울 수는 없다.</summary>
        public int Delete(IEnumerable<int> indices)
        {
            var set = new SortedSet<int>();
            foreach (int i in indices) if (i >= 0 && i < Frames.Count) set.Add(i);
            if (set.Count == 0) return 0;
            if (set.Count >= Frames.Count) throw new InvalidOperationException("프레임을 모두 지울 수는 없어요. 한 장 이상 남겨 주세요.");
            Remember();
            var kept = new List<GifFrame>();
            for (int i = 0; i < Frames.Count; i++) if (!set.Contains(i)) kept.Add(Frames[i]);
            Frames = kept;
            return set.Count;
        }

        /// <summary>고른 프레임의 표시 시간을 같은 값으로 맞춘다.</summary>
        public void SetDelay(IEnumerable<int> indices, int cs)
        {
            Remember();
            foreach (int i in indices)
            {
                if (i < 0 || i >= Frames.Count) continue;
                GifFrame f = Frames[i];
                f.DelayCs = Clamp(cs);
                Frames[i] = f;
            }
        }

        /// <summary>고른 프레임의 빠르기를 바꾼다. 200 이면 두 배 빠르게(시간 절반), 50 이면 두 배 느리게.</summary>
        public void ScaleSpeed(IEnumerable<int> indices, int percent)
        {
            if (percent <= 0) return;
            Remember();
            foreach (int i in indices)
            {
                if (i < 0 || i >= Frames.Count) continue;
                GifFrame f = Frames[i];
                f.DelayCs = Clamp((long)Math.Round(f.DelayCs * 100.0 / percent));
                Frames[i] = f;
            }
        }

        /// <summary>원본에서 남길 범위를 정한다. 저장 크기도 그 범위 크기로 돌아간다.</summary>
        public bool SetCrop(Rectangle area)
        {
            area.Intersect(new Rectangle(Point.Empty, SourceSize));
            if (area.Width < 1 || area.Height < 1 || area == Crop) return false;
            Remember();
            Crop = area;
            Output = area.Size;
            return true;
        }

        /// <summary>저장할 캔버스 크기(가로·세로)를 정한다. 잘라낸 범위를 이 크기로 늘이거나 줄인다.</summary>
        public bool SetOutput(Size size)
        {
            size = new Size(Math.Max(1, Math.Min(65535, size.Width)), Math.Max(1, Math.Min(65535, size.Height)));
            if (size == Output) return false;
            Remember();
            Output = size;
            return true;
        }

        // ── 그리기·저장 ──

        /// <summary>잘라내기와 크기 바꾸기를 적용한 저장용 그림.</summary>
        public Bitmap RenderOutput(Bitmap source)
        {
            var result = new Bitmap(Output.Width, Output.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                var dest = new Rectangle(0, 0, Output.Width, Output.Height);
                if (Output == Crop.Size)
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(source, dest, Crop, GraphicsUnit.Pixel);
                }
                else
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (var attr = new ImageAttributes())
                    {
                        attr.SetWrapMode(WrapMode.TileFlipXY); // 가장자리가 흐려지거나 비치지 않게
                        g.DrawImage(source, dest, Crop.X, Crop.Y, Crop.Width, Crop.Height, GraphicsUnit.Pixel, attr);
                    }
                }
            }
            return result;
        }

        /// <summary>새 GIF 로 저장한다. 임시 이름으로 다 쓴 뒤 바꿔 달아, 도중에 실패해도 기존 파일이 망가지지 않는다.</summary>
        public void Save(string path, Action<int, int> progress)
        {
            string temp = path + ".saving.tmp";
            try
            {
                bool direct = !IsCropped && Output == Crop.Size;
                var source = new byte[Store.FrameBytes];
                var output = direct ? source : new byte[Output.Width * Output.Height * 4];
                using (var enc = new GifEncoder(temp, Output.Width, Output.Height))
                {
                    long ms = 0;
                    for (int i = 0; i < Frames.Count; i++)
                    {
                        Store.Read(Frames[i].Source, source);
                        if (!direct)
                        {
                            using (var src = Pixels.ToBitmap(source, Store.Width, Store.Height))
                            using (var o = RenderOutput(src))
                                Pixels.ToBytes(o, output);
                        }
                        enc.AddFrame(output, ms); // 앞 프레임과 똑같으면 저장기가 합쳐서 그만큼 길게 보여 준다
                        ms += Frames[i].DelayCs * 10L;
                        if (progress != null) progress(i + 1, Frames.Count);
                    }
                    enc.Finish(ms);
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { }
                throw;
            }
        }

        public void Dispose()
        {
            Store.Dispose();
            foreach (var t in Thumbs) t.Dispose();
        }
    }

    /// <summary>MP4 캔버스 크기(해상도) 바꾸기 — 녹화와 같은 ffmpeg·같은 H.264 설정을 쓴다.</summary>
    static class Mp4Tool
    {
        public sealed class Info
        {
            public int Width, Height;
            public double Seconds;
        }

        public static Info Probe(string ffmpeg, string path)
        {
            string log = Run(ffmpeg, "-hide_banner -i \"" + path + "\"", null, 20000);
            var info = new Info();
            Match v = Regex.Match(log, @"Video:.*?[ ,](\d{2,5})x(\d{2,5})[ ,\[]");
            if (!v.Success) throw new Exception("영상 크기를 읽지 못했어요. MP4 영상이 맞는지 확인해 주세요.\n" + Tail(log));
            info.Width = int.Parse(v.Groups[1].Value);
            info.Height = int.Parse(v.Groups[2].Value);
            Match d = Regex.Match(log, @"Duration: (\d+):(\d+):(\d+(?:\.\d+)?)");
            if (d.Success)
                info.Seconds = int.Parse(d.Groups[1].Value) * 3600 + int.Parse(d.Groups[2].Value) * 60
                    + double.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture);
            return info;
        }

        /// <summary>첫 장면을 그림으로 가져온다(미리보기용). 실패하면 null.</summary>
        public static Bitmap FirstFrame(string ffmpeg, string path)
        {
            try
            {
                var psi = NewInfo(ffmpeg, "-hide_banner -loglevel error -i \"" + path + "\" -frames:v 1 -f image2pipe -c:v png -");
                psi.RedirectStandardOutput = true;
                using (var p = Process.Start(psi))
                {
                    p.BeginErrorReadLine();
                    var ms = new MemoryStream();
                    p.StandardOutput.BaseStream.CopyTo(ms);
                    p.WaitForExit(20000);
                    if (ms.Length == 0) return null;
                    ms.Position = 0;
                    using (var img = Image.FromStream(ms)) return new Bitmap(img);
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>가로·세로를 바꿔 새 MP4 로 저장한다. progress 는 0~1(일꾼 스레드에서 불린다).</summary>
        public static void Resize(string ffmpeg, string input, string output, Size size, double seconds, Action<double> progress)
        {
            if (size.Width % 2 != 0 || size.Height % 2 != 0) throw new ArgumentException("MP4 는 가로·세로가 짝수여야 해요.");
            string temp = output + ".saving.mp4";
            string args = string.Format(
                "-hide_banner -loglevel error -nostats -progress pipe:1 -y -i \"{0}\" " +
                "-vf \"scale={1}:{2}:flags=lanczos,setsar=1\" -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p " +
                "-c:a copy -movflags +faststart \"{3}\"",
                input, size.Width, size.Height, temp);
            try
            {
                Action<string> onLine = line =>
                {
                    if (progress == null || seconds <= 0) return;
                    int eq = line.IndexOf('=');
                    if (eq < 0) return;
                    string key = line.Substring(0, eq);
                    long us;
                    if ((key == "out_time_us" || key == "out_time_ms") && long.TryParse(line.Substring(eq + 1), out us)) // 둘 다 백만분의 1초
                        progress(Math.Max(0, Math.Min(1, us / 1e6 / seconds)));
                };
                Run(ffmpeg, args, onLine, 30 * 60 * 1000);
                if (!File.Exists(temp) || new FileInfo(temp).Length == 0) throw new Exception("ffmpeg 가 영상을 만들지 못했어요.");
                if (File.Exists(output)) File.Delete(output);
                File.Move(temp, output);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { }
                throw;
            }
        }

        static ProcessStartInfo NewInfo(string ffmpeg, string args)
        {
            return new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
            };
        }

        /// <summary>ffmpeg 를 실행하고 오류 출력을 돌려준다. onLine 이 있으면 표준 출력을 한 줄씩 넘긴다. 결과 코드가 0이 아니고 onLine 이 있으면 실패로 본다.</summary>
        static string Run(string ffmpeg, string args, Action<string> onLine, int timeoutMs)
        {
            var psi = NewInfo(ffmpeg, args);
            if (onLine != null) { psi.RedirectStandardOutput = true; psi.StandardOutputEncoding = Encoding.UTF8; }
            var log = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (log) { if (log.Length < 20000) log.AppendLine(e.Data); } };
                if (onLine != null) p.OutputDataReceived += (s, e) => { if (e.Data != null) onLine(e.Data); };
                try { p.Start(); }
                catch (Exception ex) { throw new Exception("ffmpeg 를 실행하지 못했어요: " + ex.Message); }
                p.BeginErrorReadLine();
                if (onLine != null) p.BeginOutputReadLine();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch (Exception) { }
                    throw new Exception("ffmpeg 가 너무 오래 걸려서 멈췄어요.");
                }
                p.WaitForExit(); // 출력 읽기까지 마치게
                string text;
                lock (log) text = log.ToString();
                if (onLine != null && p.ExitCode != 0)
                    throw new Exception("ffmpeg 가 영상을 만들지 못했어요(코드 " + p.ExitCode + ").\n" + Tail(text));
                return text;
            }
        }

        static string Tail(string text)
        {
            text = (text ?? "").Trim();
            return text.Length > 600 ? text.Substring(text.Length - 600) : text;
        }

        /// <summary>비율을 지키며 짝수 크기로 맞춘다(H.264 yuv420p 는 짝수만 된다).</summary>
        public static int Even(int v) { return Math.Max(2, v - (v % 2)); }
    }
}
