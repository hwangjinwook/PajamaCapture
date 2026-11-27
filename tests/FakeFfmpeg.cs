// MP4 경로 시험용 가짜 ffmpeg — 받은 인자와 장면 수를 결과 파일에 적는다. 진짜 영상은 만들지 않는다.
// 환경 변수 FAKE_FFMPEG_FAIL=1 이면 조금 읽다가 오류로 끝난다(오류 안내 시험).
using System;
using System.IO;

static class FakeFfmpeg
{
    static int Main(string[] args)
    {
        int w = 0, h = 0, fps = 0;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-video_size") { var p = args[i + 1].Split('x'); w = int.Parse(p[0]); h = int.Parse(p[1]); }
            if (args[i] == "-framerate") fps = int.Parse(args[i + 1]);
        }
        string output = args[args.Length - 1];
        bool fail = Environment.GetEnvironmentVariable("FAKE_FFMPEG_FAIL") == "1";
        var input = Console.OpenStandardInput();
        var buf = new byte[1 << 20];
        long total = 0;
        int n;
        while ((n = input.Read(buf, 0, buf.Length)) > 0)
        {
            total += n;
            if (fail && total > (long)w * h * 4 * 3) { Console.Error.WriteLine("Unknown encoder 'libx264'"); return 1; }
        }
        long frameBytes = (long)w * h * 4;
        File.WriteAllText(output, string.Format("args={0}\nsize={1}x{2}\nfps={3}\nbytes={4}\nframes={5}\nremainder={6}\n",
            string.Join(" | ", args), w, h, fps, total, frameBytes > 0 ? total / frameBytes : -1, frameBytes > 0 ? total % frameBytes : -1));
        return 0;
    }
}
