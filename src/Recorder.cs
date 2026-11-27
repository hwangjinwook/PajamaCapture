// 녹화 — 찍는 일꾼(정해진 간격으로 화면 복사)과 저장 일꾼(GIF·MP4 로 쓰기)을 따로 돌린다.
// 저장이 밀리면 장면을 버리되 시간 정보는 지켜서, 결과 영상의 길이는 실제 녹화 시간과 같다.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace PajamaCapture
{
    interface IFrameSink : IDisposable
    {
        void Write(byte[] bgra, long ms);
        void Finish(long endMs);
    }

    sealed class GifSink : IFrameSink
    {
        readonly GifEncoder encoder;
        public GifSink(string path, int w, int h) { encoder = new GifEncoder(path, w, h); }
        public void Write(byte[] bgra, long ms) { encoder.AddFrame(bgra, ms); }
        public void Finish(long endMs) { encoder.Finish(endMs); }
        public void Dispose() { encoder.Dispose(); }
    }

    /// <summary>ffmpeg 에 화면 원본을 흘려 넣어 MP4(H.264)로 만든다. 장면 수를 시간에 맞춰 고정 속도로 채운다.</summary>
    sealed class FfmpegSink : IFrameSink
    {
        readonly Process process;
        readonly Stream input;
        readonly StringBuilder log = new StringBuilder();
        readonly int fps;
        readonly byte[] last;
        long written;
        bool closed;

        public FfmpegSink(string ffmpeg, string path, int w, int h, int fps)
        {
            this.fps = fps;
            last = new byte[w * h * 4];
            var info = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = string.Format(
                    "-hide_banner -loglevel error -y -f rawvideo -pix_fmt bgra -video_size {0}x{1} -framerate {2} -i - " +
                    "-vf \"pad=ceil(iw/2)*2:ceil(ih/2)*2\" -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart \"{3}\"",
                    w, h, fps, path),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
            };
            process = new Process { StartInfo = info };
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                lock (log) { if (log.Length < 8000) log.AppendLine(e.Data); }
            };
            try { process.Start(); }
            catch (Exception ex) { throw new Exception("ffmpeg 를 실행하지 못했어요: " + ex.Message); }
            process.BeginErrorReadLine();
            input = process.StandardInput.BaseStream;
        }

        public void Write(byte[] bgra, long ms)
        {
            long slot = (long)Math.Round(ms * fps / 1000.0);
            long copies = slot + 1 - written;
            if (written == 0) copies = 1;
            if (copies <= 0) return;
            Buffer.BlockCopy(bgra, 0, last, 0, last.Length);
            for (long i = 0; i < copies; i++) Push(bgra);
        }

        public void Finish(long endMs)
        {
            long total = Math.Max(1, (long)Math.Round(endMs * fps / 1000.0));
            while (written > 0 && written < total) Push(last);
            closed = true;
            try { input.Close(); } catch (IOException) { }
            if (!process.WaitForExit(180000)) { Kill(); throw new Exception("ffmpeg 가 3분 안에 끝나지 않았어요."); }
            process.WaitForExit(); // 오류 출력 읽기까지 마치게
            if (process.ExitCode != 0) throw new Exception("ffmpeg 가 영상을 만들지 못했어요(코드 " + process.ExitCode + ").\n" + Log());
        }

        void Push(byte[] frame)
        {
            try { input.Write(frame, 0, frame.Length); written++; }
            catch (IOException)
            {
                process.WaitForExit(3000);
                throw new Exception("ffmpeg 가 도중에 멈췄어요.\n" + Log());
            }
        }

        string Log() { lock (log) return log.ToString().Trim(); }

        void Kill() { try { if (!process.HasExited) process.Kill(); } catch (Exception) { } }

        public void Dispose()
        {
            if (!closed) { try { input.Close(); } catch (Exception) { } }
            if (!process.WaitForExit(2000)) Kill();
            process.Dispose();
        }
    }

    sealed class Recorder
    {
        struct Frame
        {
            public byte[] Data;
            public long Ms;
        }

        public readonly System.Drawing.Rectangle Area;
        public readonly string OutputPath;
        public readonly bool IsMp4;
        readonly int fps;
        readonly bool drawCursor;
        readonly string ffmpeg;
        readonly ConcurrentBag<byte[]> pool = new ConcurrentBag<byte[]>();
        BlockingCollection<Frame> queue;
        Thread captureThread, saveThread;
        IFrameSink sink;
        readonly Stopwatch clock = new Stopwatch();
        volatile bool stopping;
        volatile bool canceled;
        long endMs;
        int captured, dropped;

        public string Error { get; private set; }
        public bool Canceled { get { return canceled; } }
        public int CapturedFrames { get { return captured; } }
        public int DroppedFrames { get { return dropped; } }
        public long ElapsedMs { get { return stopping ? endMs : clock.ElapsedMilliseconds; } }

        /// <summary>저장까지 끝나면(성공·실패·취소 모두) 저장 일꾼에서 부른다.</summary>
        public event Action<Recorder> Finished;

        public Recorder(System.Drawing.Rectangle area, bool mp4, int fps, bool drawCursor, string outputPath, string ffmpegPath)
        {
            Area = area;
            IsMp4 = mp4;
            this.fps = Math.Max(1, Math.Min(60, fps));
            this.drawCursor = drawCursor;
            OutputPath = outputPath;
            ffmpeg = ffmpegPath;
        }

        public void Start()
        {
            sink = IsMp4 ? (IFrameSink)new FfmpegSink(ffmpeg, OutputPath, Area.Width, Area.Height, fps)
                         : new GifSink(OutputPath, Area.Width, Area.Height);
            long frameBytes = (long)Area.Width * Area.Height * 4;
            int capacity = (int)Math.Max(2, Math.Min(90, 300L * 1024 * 1024 / frameBytes)); // 기다리는 장면은 300MB 까지만
            queue = new BlockingCollection<Frame>(capacity);
            captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "녹화-찍기", Priority = ThreadPriority.AboveNormal };
            saveThread = new Thread(SaveLoop) { IsBackground = true, Name = "녹화-저장" };
            clock.Start();
            captureThread.Start();
            saveThread.Start();
        }

        public void Stop(bool cancel)
        {
            if (cancel) canceled = true;
            if (!stopping) endMs = clock.ElapsedMilliseconds;
            stopping = true;
        }

        void CaptureLoop()
        {
            Native.timeBeginPeriod(1);
            try
            {
                using (var capturer = new ScreenCapturer(Area))
                {
                    long n = 0;
                    double interval = 1000.0 / fps;
                    while (!stopping)
                    {
                        long now = clock.ElapsedMilliseconds;
                        long due = (long)(n * interval);
                        if (now < due) { Thread.Sleep((int)Math.Max(1, Math.Min(due - now, 20))); continue; }
                        if (now - due > interval) n = (long)(now / interval); // 많이 밀렸으면 지금 칸으로 건너뛴다

                        capturer.Grab(false, drawCursor);
                        byte[] buffer;
                        if (!pool.TryTake(out buffer)) buffer = new byte[capturer.ByteCount];
                        capturer.CopyTo(buffer);
                        if (queue.TryAdd(new Frame { Data = buffer, Ms = now })) captured++;
                        else { pool.Add(buffer); dropped++; }
                        n++;
                    }
                }
            }
            catch (Exception ex)
            {
                if (Error == null) Error = "화면을 찍는 중 문제가 생겼어요: " + ex.Message;
                if (!stopping) endMs = clock.ElapsedMilliseconds;
                stopping = true;
            }
            finally
            {
                Native.timeEndPeriod(1);
                queue.CompleteAdding();
            }
        }

        void SaveLoop()
        {
            try
            {
                foreach (var frame in queue.GetConsumingEnumerable())
                {
                    if (!canceled && Error == null)
                    {
                        try { sink.Write(frame.Data, frame.Ms); }
                        catch (Exception ex) { Error = ex.Message; Stop(false); }
                    }
                    pool.Add(frame.Data);
                }
                captureThread.Join();
                if (!canceled && Error == null)
                {
                    if (captured == 0) Error = "찍힌 장면이 없어요.";
                    else sink.Finish(endMs);
                }
            }
            catch (Exception ex)
            {
                if (Error == null) Error = ex.Message;
            }
            finally
            {
                try { sink.Dispose(); } catch (Exception) { }
                if (canceled || Error != null)
                {
                    try { if (File.Exists(OutputPath)) File.Delete(OutputPath); } catch (Exception) { }
                }
                var handler = Finished;
                if (handler != null) handler(this);
            }
        }
    }

    /// <summary>MP4 녹화에 쓸 ffmpeg.exe 를 찾는다.</summary>
    static class FfmpegLocator
    {
        public const string WingetCommand = "winget install -e --id Gyan.FFmpeg";

        public static string Find(string custom)
        {
            foreach (string path in Candidates(custom))
            {
                try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path; }
                catch (Exception) { }
            }
            return null;
        }

        static IEnumerable<string> Candidates(string custom)
        {
            yield return custom;
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            yield return Path.Combine(exeDir, "ffmpeg.exe");
            yield return Path.Combine(exeDir, "ffmpeg", "ffmpeg.exe");
            yield return Path.Combine(exeDir, "ffmpeg", "bin", "ffmpeg.exe");

            string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathVar.Split(';'))
            {
                string d = dir.Trim().Trim('"');
                if (d.Length > 0) yield return SafeCombine(d, "ffmpeg.exe");
            }

            // winget 으로 설치한 직후에는 이 프로그램의 PATH 가 아직 옛것이라 직접 찾아본다
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(local, "Microsoft", "WinGet", "Links", "ffmpeg.exe");
            string packages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(packages))
            {
                string[] found = new string[0];
                try
                {
                    var list = new List<string>();
                    foreach (string pkg in Directory.GetDirectories(packages, "*FFmpeg*"))
                        list.AddRange(Directory.GetFiles(pkg, "ffmpeg.exe", SearchOption.AllDirectories));
                    found = list.ToArray();
                }
                catch (Exception) { }
                foreach (string f in found) yield return f;
            }
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(user, "scoop", "shims", "ffmpeg.exe");
            yield return @"C:\ProgramData\chocolatey\bin\ffmpeg.exe";
            yield return @"C:\ffmpeg\bin\ffmpeg.exe";
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe");
        }

        static string SafeCombine(string a, string b)
        {
            try { return Path.Combine(a, b); } catch (Exception) { return null; }
        }
    }
}
