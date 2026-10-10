using System.Runtime.InteropServices;
using D = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;

namespace KeepyUppy;

internal static class BalloonIcons
{
    public static D.Icon Create(bool awake, int size = 0)
    {
        if (size <= 0)
        {
            size = System.Windows.Forms.SystemInformation.SmallIconSize.Width;
            if (size < 16)
                size = 16;
        }

        using var bitmap = Draw(size, awake);
        return ToIcon(bitmap);
    }

    private static D.Bitmap Draw(int size, bool awake)
    {
        var bitmap = new D.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = D.Graphics.FromImage(bitmap);
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;
        g.Clear(D.Color.Transparent);

        var s = (float)size;
        var body = awake ? D.Color.FromArgb(255, 78, 180, 224) : D.Color.FromArgb(255, 168, 182, 194);
        var ink = awake ? D.Color.FromArgb(255, 28, 104, 148) : D.Color.FromArgb(255, 88, 102, 116);
        var stroke = Math.Max(1.6f, s * 0.09f);
        var balloon = new D.RectangleF(
            s * 0.08f + (stroke / 2f),
            s * 0.04f + (stroke / 2f),
            s * 0.84f - stroke,
            s * 0.60f - stroke);

        using var bodyBrush = new D.SolidBrush(body);
        using var inkBrush = new D.SolidBrush(ink);
        using var outline = new D.Pen(ink, stroke);
        g.FillEllipse(bodyBrush, balloon);
        g.DrawEllipse(outline, balloon);

        var cx = s * 0.50f;
        var knotTop = balloon.Bottom - (stroke * 0.35f);
        var knotHalf = Math.Max(stroke * 0.95f, s * 0.09f);
        var knotBottom = Math.Min(s - (stroke * 1.6f), knotTop + Math.Max(stroke, s * 0.12f));
        g.FillPolygon(inkBrush, new[]
        {
            new D.PointF(cx - knotHalf, knotTop),
            new D.PointF(cx + knotHalf, knotTop),
            new D.PointF(cx, knotBottom)
        });

        using var tail = new D.Pen(ink, Math.Max(1.8f, s * 0.10f));
        tail.StartCap = Drawing2D.LineCap.Round;
        tail.EndCap = Drawing2D.LineCap.Round;
        var tailEnd = Math.Min(s - 1f, knotBottom + (s * 0.16f));
        g.DrawBezier(
            tail,
            cx, knotBottom - 0.5f,
            cx + (s * 0.14f), knotBottom + (s * 0.04f),
            cx - (s * 0.12f), knotBottom + (s * 0.10f),
            cx + (s * 0.02f), tailEnd);

        using var shine = new D.SolidBrush(D.Color.FromArgb(230, 255, 255, 255));
        g.FillEllipse(
            shine,
            balloon.X + (balloon.Width * 0.22f),
            balloon.Y + (balloon.Height * 0.16f),
            Math.Max(2.4f, balloon.Width * 0.28f),
            Math.Max(1.8f, balloon.Height * 0.22f));

        return bitmap;
    }

    private static D.Icon ToIcon(D.Bitmap bitmap)
    {
        var created = CreateAlphaIcon(bitmap);
        if (created != IntPtr.Zero)
        {
            using var temp = D.Icon.FromHandle(created);
            var clone = (D.Icon)temp.Clone();
            DestroyIcon(created);
            return clone;
        }

        var handle = bitmap.GetHicon();
        using var fallback = D.Icon.FromHandle(handle);
        var fallbackClone = (D.Icon)fallback.Clone();
        DestroyIcon(handle);
        return fallbackClone;
    }

    private static IntPtr CreateAlphaIcon(D.Bitmap bitmap)
    {
        var rect = new D.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var bottomUp = stride < 0;
            stride = Math.Abs(stride);
            var raw = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);

            var premultiplied = new byte[bitmap.Width * bitmap.Height * 4];
            for (var y = 0; y < bitmap.Height; y++)
            {
                var sourceY = bottomUp ? bitmap.Height - 1 - y : y;
                var source = sourceY * stride;
                var destination = y * bitmap.Width * 4;
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var i = source + (x * 4);
                    var b = raw[i];
                    var g = raw[i + 1];
                    var r = raw[i + 2];
                    var a = raw[i + 3];
                    premultiplied[destination] = (byte)(b * a / 255);
                    premultiplied[destination + 1] = (byte)(g * a / 255);
                    premultiplied[destination + 2] = (byte)(r * a / 255);
                    premultiplied[destination + 3] = a;
                    destination += 4;
                }
            }

            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = bitmap.Width,
                biHeight = -bitmap.Height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
                biSizeImage = (uint)premultiplied.Length
            };

            var hColor = CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
            if (hColor == IntPtr.Zero || bits == IntPtr.Zero)
                return IntPtr.Zero;

            Marshal.Copy(premultiplied, 0, bits, premultiplied.Length);

            var maskStride = ((bitmap.Width + 31) / 32) * 4;
            var maskBits = new byte[maskStride * bitmap.Height];
            var pin = GCHandle.Alloc(maskBits, GCHandleType.Pinned);
            IntPtr hMask;
            try
            {
                hMask = CreateBitmap(bitmap.Width, bitmap.Height, 1, 1, pin.AddrOfPinnedObject());
            }
            finally
            {
                pin.Free();
            }

            if (hMask == IntPtr.Zero)
            {
                DeleteObject(hColor);
                return IntPtr.Zero;
            }

            var info = new ICONINFO
            {
                fIcon = true,
                hbmColor = hColor,
                hbmMask = hMask
            };

            var icon = CreateIconIndirect(ref info);
            DeleteObject(hColor);
            DeleteObject(hMask);
            return icon;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BITMAPINFOHEADER pbmi,
        uint usage,
        out IntPtr ppvBits,
        IntPtr hSection,
        uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, IntPtr bits);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO iconInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr handle);
}
