// 움직이는 GIF 저장기 — 외부 프로그램 없이 동작한다.
// 장면마다 256색 팔레트를 새로 만들고(중앙값 분할), 앞 장면과 달라진 사각 범위만 저장해 파일을 작게 한다.
using System;
using System.Collections.Generic;
using System.IO;

namespace PajamaCapture
{
    sealed class GifEncoder : IDisposable
    {
        const int Transparent = 255; // 바뀌지 않은 점에 쓰는 투명 색 번호. 실제 색은 0~254 를 쓴다.
        const int MaxColors = 255;

        readonly FileStream file;
        readonly int width, height;
        readonly int[] previous;  // 앞 장면 원본(0xRRGGBB)
        readonly int[] shown;     // 지금 화면에 보이는 색(0xRRGGBB, 처음에는 -1)
        readonly int[] current;
        readonly int[] histogram = new int[32768];
        readonly int[] lookup = new int[32768];
        readonly bool[] changed;
        long delayPosition = -1;  // 마지막 장면의 표시 시간 칸 위치(다음 장면이 오면 채운다)
        long lastStartCs;
        int frameCount;
        bool finished;

        public GifEncoder(string path, int width, int height)
        {
            if (width < 1 || height < 1 || width > 65535 || height > 65535) throw new ArgumentException("GIF 크기가 올바르지 않아요.");
            this.width = width;
            this.height = height;
            previous = new int[width * height];
            shown = new int[width * height];
            current = new int[width * height];
            changed = new bool[width * height];
            for (int i = 0; i < shown.Length; i++) shown[i] = -1;
            file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1 << 16);

            WriteAscii("GIF89a");
            WriteShort(width);
            WriteShort(height);
            file.WriteByte(0x70); // 전체 팔레트 없음, 색 깊이 8비트
            file.WriteByte(0);
            file.WriteByte(0);
            // 끝없이 반복(NETSCAPE2.0)
            file.Write(new byte[] { 0x21, 0xFF, 0x0B }, 0, 3);
            WriteAscii("NETSCAPE2.0");
            file.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 }, 0, 5);
        }

        public int FrameCount { get { return frameCount; } }

        /// <summary>장면 하나를 더한다. bgra 는 한 점 4바이트(파랑·초록·빨강·무시), ms 는 녹화 시작부터 지난 시간.</summary>
        public void AddFrame(byte[] bgra, long ms)
        {
            if (finished) throw new InvalidOperationException("이미 끝난 GIF 예요.");
            long startCs = (ms + 5) / 10;
            if (frameCount > 0 && startCs - lastStartCs < 2) return; // 0.02초보다 촘촘한 장면은 건너뛴다(다음 장면이 이 변화까지 담는다)

            int n = width * height;
            for (int i = 0, j = 0; i < n; i++, j += 4)
                current[i] = (bgra[j + 2] << 16) | (bgra[j + 1] << 8) | bgra[j];

            // 앞 장면과 달라진 범위
            int left = width, top = height, right = -1, bottom = -1;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    bool diff = frameCount == 0 || current[row + x] != previous[row + x];
                    changed[row + x] = diff;
                    if (!diff) continue;
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                }
            }
            if (right < 0) return; // 바뀐 게 없으면 앞 장면을 더 오래 보여 준다

            if (frameCount > 0) PatchDelay(startCs - lastStartCs);
            int bw = right - left + 1, bh = bottom - top + 1;

            // 1) 바뀐 점들의 색 분포(빨강·초록·파랑 각 5비트)
            Array.Clear(histogram, 0, histogram.Length);
            for (int y = top; y <= bottom; y++)
                for (int x = left, i = y * width + left; x <= right; x++, i++)
                    if (changed[i]) histogram[Key(current[i])]++;

            // 2) 팔레트 만들기
            int[] palette = BuildPalette();

            // 3) 색 번호로 바꾸기 — 보이는 색과 같으면 투명으로 둔다
            var indices = new byte[bw * bh];
            int k = 0;
            for (int y = top; y <= bottom; y++)
            {
                for (int x = left, i = y * width + left; x <= right; x++, i++, k++)
                {
                    if (!changed[i]) { indices[k] = Transparent; continue; }
                    int index = lookup[Key(current[i])];
                    int color = palette[index];
                    if (color == shown[i]) { indices[k] = Transparent; continue; }
                    indices[k] = (byte)index;
                    shown[i] = color;
                }
            }
            Array.Copy(current, previous, n);

            // 4) 장면 쓰기
            file.Write(new byte[] { 0x21, 0xF9, 0x04, 0x05 }, 0, 4); // 그래픽 제어: 앞 장면 위에 덧그림 + 투명 색 사용
            delayPosition = file.Position;
            WriteShort(10); // 표시 시간은 다음 장면이 오면 고친다
            file.WriteByte(Transparent);
            file.WriteByte(0);

            file.WriteByte(0x2C);
            WriteShort(left);
            WriteShort(top);
            WriteShort(bw);
            WriteShort(bh);
            file.WriteByte(0x87); // 이 장면만의 팔레트 256색
            var table = new byte[256 * 3];
            for (int i = 0; i < palette.Length; i++)
            {
                table[i * 3] = (byte)(palette[i] >> 16);
                table[i * 3 + 1] = (byte)(palette[i] >> 8);
                table[i * 3 + 2] = (byte)palette[i];
            }
            file.Write(table, 0, table.Length);
            LzwWriter.Write(file, indices);

            lastStartCs = startCs;
            frameCount++;
        }

        /// <summary>마지막 장면의 표시 시간을 정하고 파일을 닫는다.</summary>
        public void Finish(long endMs)
        {
            if (finished) return;
            finished = true;
            if (frameCount > 0) PatchDelay((endMs + 5) / 10 - lastStartCs);
            file.WriteByte(0x3B);
            file.Flush();
            file.Dispose();
        }

        public void Dispose()
        {
            if (!finished) { finished = true; file.Dispose(); }
        }

        void PatchDelay(long cs)
        {
            if (delayPosition < 0) return;
            int value = (int)Math.Max(2, Math.Min(65535, cs));
            long here = file.Position;
            file.Position = delayPosition;
            WriteShort(value);
            file.Position = here;
        }

        static int Key(int rgb)
        {
            return ((rgb >> 9) & 0x7C00) | ((rgb >> 6) & 0x03E0) | ((rgb >> 3) & 0x001F);
        }

        // ── 중앙값 분할: 색이 넓게 퍼진 상자부터 반으로 나눠 255개 상자를 만들고, 상자 평균색을 팔레트로 쓴다 ──

        sealed class Box
        {
            public int Start, Count;     // colors 배열에서의 범위
            public long Pixels;
            public int Axis, Range;
        }

        int[] BuildPalette()
        {
            var colors = new List<int>();
            for (int key = 0; key < histogram.Length; key++)
                if (histogram[key] > 0) colors.Add(key);
            int[] keys = colors.ToArray();

            var boxes = new List<Box>();
            boxes.Add(Measure(keys, 0, keys.Length));
            while (boxes.Count < MaxColors)
            {
                int best = -1;
                long bestScore = 0;
                for (int i = 0; i < boxes.Count; i++)
                {
                    var b = boxes[i];
                    if (b.Count < 2 || b.Range == 0) continue;
                    long score = b.Range * (long)Math.Sqrt(b.Pixels) + 1;
                    if (score > bestScore) { bestScore = score; best = i; }
                }
                if (best < 0) break;
                var box = boxes[best];
                int shift = box.Axis == 0 ? 10 : box.Axis == 1 ? 5 : 0;
                Array.Sort(keys, box.Start, box.Count, new ChannelComparer(shift));
                long half = box.Pixels / 2, sum = 0;
                int split = box.Start;
                for (int i = box.Start; i < box.Start + box.Count - 1; i++)
                {
                    sum += histogram[keys[i]];
                    split = i + 1;
                    if (sum >= half) break;
                }
                boxes[best] = Measure(keys, box.Start, split - box.Start);
                boxes.Add(Measure(keys, split, box.Start + box.Count - split));
            }

            var palette = new int[boxes.Count];
            for (int b = 0; b < boxes.Count; b++)
            {
                var box = boxes[b];
                long r = 0, g = 0, bl = 0, total = 0;
                for (int i = box.Start; i < box.Start + box.Count; i++)
                {
                    int key = keys[i];
                    long c = histogram[key];
                    r += (((key >> 10) & 31) * 255 / 31) * c;
                    g += (((key >> 5) & 31) * 255 / 31) * c;
                    bl += ((key & 31) * 255 / 31) * c;
                    total += c;
                    lookup[key] = b;
                }
                if (total == 0) total = 1;
                palette[b] = (int)((r / total) << 16 | (g / total) << 8 | (bl / total));
            }
            return palette;
        }

        Box Measure(int[] keys, int start, int count)
        {
            int rMin = 31, rMax = 0, gMin = 31, gMax = 0, bMin = 31, bMax = 0;
            long pixels = 0;
            for (int i = start; i < start + count; i++)
            {
                int key = keys[i];
                int r = (key >> 10) & 31, g = (key >> 5) & 31, b = key & 31;
                if (r < rMin) rMin = r; if (r > rMax) rMax = r;
                if (g < gMin) gMin = g; if (g > gMax) gMax = g;
                if (b < bMin) bMin = b; if (b > bMax) bMax = b;
                pixels += histogram[key];
            }
            var box = new Box { Start = start, Count = count, Pixels = pixels };
            int rr = rMax - rMin, gr = gMax - gMin, br = bMax - bMin;
            if (count == 0) { rr = gr = br = 0; }
            if (gr >= rr && gr >= br) { box.Axis = 1; box.Range = gr; }
            else if (rr >= br) { box.Axis = 0; box.Range = rr; }
            else { box.Axis = 2; box.Range = br; }
            return box;
        }

        sealed class ChannelComparer : IComparer<int>
        {
            readonly int shift;
            public ChannelComparer(int shift) { this.shift = shift; }
            public int Compare(int a, int b) { return ((a >> shift) & 31) - ((b >> shift) & 31); }
        }

        void WriteShort(int v) { file.WriteByte((byte)(v & 0xFF)); file.WriteByte((byte)((v >> 8) & 0xFF)); }
        void WriteAscii(string s) { foreach (char c in s) file.WriteByte((byte)c); }
    }

    /// <summary>GIF 용 LZW 압축(8비트 색 번호, 최대 12비트 코드). 순서는 giflib 과 같다.</summary>
    static class LzwWriter
    {
        // 사전: (앞 코드 << 8 | 다음 색) → 코드. 사전을 비울 때 배열을 지우지 않도록 세대 번호를 쓴다.
        static readonly int[] stamp = new int[1 << 20];
        static readonly short[] codes = new short[1 << 20];
        static int generation;
        static readonly object gate = new object();

        public static void Write(Stream output, byte[] pixels)
        {
            lock (gate) WriteCore(output, pixels);
        }

        static void WriteCore(Stream output, byte[] pixels)
        {
            const int clear = 256, end = 257;
            output.WriteByte(8);
            var bits = new BitPacker(output);
            int codeSize = 9, next = 258;
            NewGeneration();

            bits.Write(clear, codeSize);
            int prefix = pixels[0];
            for (int i = 1; i < pixels.Length; i++)
            {
                int k = pixels[i];
                int key = (prefix << 8) | k;
                if (stamp[key] == generation) { prefix = codes[key]; continue; }

                bits.Write(prefix, codeSize);
                if (next >= (1 << codeSize) && codeSize < 12) codeSize++;
                prefix = k;
                if (next >= 4095)
                {
                    bits.Write(clear, codeSize);
                    codeSize = 9;
                    next = 258;
                    NewGeneration();
                }
                else
                {
                    stamp[key] = generation;
                    codes[key] = (short)next++;
                }
            }
            bits.Write(prefix, codeSize);
            if (next >= (1 << codeSize) && codeSize < 12) codeSize++;
            bits.Write(end, codeSize);
            bits.Flush();
            output.WriteByte(0);
        }

        static void NewGeneration()
        {
            generation++;
            if (generation == int.MaxValue) { Array.Clear(stamp, 0, stamp.Length); generation = 1; }
        }

        /// <summary>코드를 낮은 비트부터 채워 255바이트 묶음으로 내보낸다.</summary>
        sealed class BitPacker
        {
            readonly Stream output;
            readonly byte[] block = new byte[256];
            int blockLength;
            int buffer, count;

            public BitPacker(Stream output) { this.output = output; }

            public void Write(int code, int size)
            {
                buffer |= code << count;
                count += size;
                while (count >= 8)
                {
                    Put((byte)buffer);
                    buffer >>= 8;
                    count -= 8;
                }
            }

            void Put(byte b)
            {
                block[1 + blockLength++] = b;
                if (blockLength == 255) FlushBlock();
            }

            void FlushBlock()
            {
                if (blockLength == 0) return;
                block[0] = (byte)blockLength;
                output.Write(block, 0, blockLength + 1);
                blockLength = 0;
            }

            public void Flush()
            {
                if (count > 0) { Put((byte)buffer); buffer = 0; count = 0; }
                FlushBlock();
            }
        }
    }
}
