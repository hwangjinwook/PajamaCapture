// 화면 복사(캡쳐·녹화 공용)와 열린 창 목록.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PajamaCapture
{
    /// <summary>화면의 한 사각 범위를 메모리 그림으로 복사한다. 녹화 때는 한 번 만들어 계속 다시 쓴다.</summary>
    sealed class ScreenCapturer : IDisposable
    {
        public readonly Rectangle Area;
        readonly IntPtr screenDc, memDc, dib, oldObject, bits;
        IntPtr lastCursor = IntPtr.Zero;
        int hotX, hotY;

        public ScreenCapturer(Rectangle area)
        {
            if (area.Width <= 0 || area.Height <= 0) throw new ArgumentException("캡쳐 범위가 비어 있어요.");
            Area = area;
            screenDc = Native.GetDC(IntPtr.Zero);
            memDc = Native.CreateCompatibleDC(screenDc);
            var info = new Native.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)),
                biWidth = area.Width,
                biHeight = -area.Height, // 위에서 아래로 저장
                biPlanes = 1,
                biBitCount = 32,
            };
            IntPtr b;
            dib = Native.CreateDIBSection(memDc, ref info, 0, out b, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero)
            {
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
                throw new OutOfMemoryException("화면을 담을 메모리를 만들지 못했어요(범위가 너무 클 수 있어요).");
            }
            bits = b;
            oldObject = Native.SelectObject(memDc, dib);
        }

        public int ByteCount { get { return Area.Width * Area.Height * 4; } }

        /// <summary>지금 화면을 복사한다. layered=true 면 반투명 창(풍선 도움말 등)까지 담는다(커서가 잠깐 깜빡일 수 있어 녹화에서는 끈다).</summary>
        public void Grab(bool layered, bool drawCursor)
        {
            Native.BitBlt(memDc, 0, 0, Area.Width, Area.Height, screenDc, Area.X, Area.Y, Native.SRCCOPY | (layered ? Native.CAPTUREBLT : 0));
            if (drawCursor) DrawCursor();
            Native.GdiFlush();
        }

        void DrawCursor()
        {
            var ci = new Native.CURSORINFO { cbSize = Marshal.SizeOf(typeof(Native.CURSORINFO)) };
            if (!Native.GetCursorInfo(ref ci) || (ci.flags & Native.CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero) return;
            if (ci.hCursor != lastCursor)
            {
                Native.ICONINFO ii;
                if (!Native.GetIconInfo(ci.hCursor, out ii)) return;
                hotX = ii.xHotspot;
                hotY = ii.yHotspot;
                if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);
                if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);
                lastCursor = ci.hCursor;
            }
            int x = ci.ptScreenPos.X - hotX - Area.X, y = ci.ptScreenPos.Y - hotY - Area.Y;
            if (x > Area.Width || y > Area.Height || x < -64 || y < -64) return;
            Native.DrawIconEx(memDc, x, y, ci.hCursor, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL);
        }

        /// <summary>BGRA 순서(한 점 4바이트, 위에서 아래)로 복사한다. 알파 값은 의미 없다.</summary>
        public void CopyTo(byte[] buffer)
        {
            Marshal.Copy(bits, buffer, 0, ByteCount);
        }

        /// <summary>불투명한 32비트 그림으로 만든다(화면에 빠르게 그리기 좋은 형식).</summary>
        public Bitmap ToBitmap()
        {
            int w = Area.Width, h = Area.Height;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var row = new byte[w * 4];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(new IntPtr(bits.ToInt64() + (long)y * w * 4), row, 0, row.Length);
                    for (int i = 3; i < row.Length; i += 4) row[i] = 255;
                    Marshal.Copy(row, 0, new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), row.Length);
                }
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }

        public void Dispose()
        {
            Native.SelectObject(memDc, oldObject);
            Native.DeleteObject(dib);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }

        /// <summary>범위를 한 번 찍어 그림으로 돌려준다.</summary>
        public static Bitmap Snap(Rectangle area)
        {
            using (var c = new ScreenCapturer(area))
            {
                c.Grab(true, false);
                return c.ToBitmap();
            }
        }
    }

    sealed class WindowInfo
    {
        public IntPtr Handle;
        public Rectangle Bounds;
        public string Title;
    }

    static class WindowFinder
    {
        /// <summary>보이는 최상위 창을 앞에서 뒤 순서로 모은다(이 프로그램 창·숨은 창·투명 창 제외).</summary>
        public static List<WindowInfo> List(Rectangle virtualScreen)
        {
            var result = new List<WindowInfo>();
            int myPid = Process.GetCurrentProcess().Id;
            var text = new StringBuilder(512);
            var cls = new StringBuilder(256);
            Native.EnumWindows((hwnd, l) =>
            {
                if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;
                int pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                if (pid == myPid) return true;
                int cloaked;
                if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return true; // 다른 가상 데스크톱·숨은 앱
                int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
                if ((ex & Native.WS_EX_TRANSPARENT) != 0) return true; // 클릭이 통과하는 겹침 창(게임 오버레이 등)
                if ((ex & Native.WS_EX_LAYERED) != 0)
                {
                    int key, flags; byte alpha;
                    if (Native.GetLayeredWindowAttributes(hwnd, out key, out alpha, out flags) && (flags & 2) != 0 && alpha == 0) return true;
                }
                cls.Length = 0;
                Native.GetClassName(hwnd, cls, cls.Capacity);
                string className = cls.ToString();
                if (className == "Progman" || className == "WorkerW") return true; // 바탕 화면은 모니터 전체로 대신한다
                text.Length = 0;
                Native.GetWindowText(hwnd, text, text.Capacity);
                string title = text.ToString();
                bool taskbar = className == "Shell_TrayWnd" || className == "Shell_SecondaryTrayWnd";
                if (title.Length == 0 && !taskbar) return true;
                if (taskbar) title = "작업 표시줄";

                Rectangle r = Bounds(hwnd);
                r.Intersect(virtualScreen);
                if (r.Width < 8 || r.Height < 8) return true;
                result.Add(new WindowInfo { Handle = hwnd, Bounds = r, Title = title });
                return true;
            }, IntPtr.Zero);
            return result;
        }

        /// <summary>그림자를 뺀 실제 창 테두리 범위.</summary>
        public static Rectangle Bounds(IntPtr hwnd)
        {
            Native.RECT r;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(Native.RECT))) == 0)
                return r.ToRectangle();
            Native.GetWindowRect(hwnd, out r);
            return r.ToRectangle();
        }

        /// <summary>점 아래의 가장 앞 창. 없으면 그 점이 있는 모니터 전체.</summary>
        public static WindowInfo At(List<WindowInfo> windows, Point screenPoint)
        {
            foreach (var w in windows)
                if (w.Bounds.Contains(screenPoint)) return w;
            var screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
                if (screens[i].Bounds.Contains(screenPoint))
                    return new WindowInfo { Bounds = screens[i].Bounds, Title = "화면 전체 (모니터 " + (i + 1) + ")" };
            return null;
        }
    }

    static class ImageTools
    {
        /// <summary>큰 그림에서 범위를 잘라 24비트 그림으로 만든다(저장·복사용).</summary>
        public static Bitmap Crop(Bitmap source, Rectangle area)
        {
            area.Intersect(new Rectangle(0, 0, source.Width, source.Height));
            if (area.Width <= 0 || area.Height <= 0) throw new ArgumentException("캡쳐 범위가 화면 밖이에요.");
            var result = new Bitmap(area.Width, area.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(result))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.DrawImage(source, new Rectangle(0, 0, area.Width, area.Height), area, GraphicsUnit.Pixel);
            }
            return result;
        }
    }
}
