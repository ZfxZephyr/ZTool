using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ZTool.Services;

/// <summary>
/// What an app looks like, as the shell draws it. A Start Menu shortcut and a
/// Store AUMID both resolve to a shell item, and the shell is the only place
/// that knows what either of them looks like to a person.
/// </summary>
public static class ShellIcon
{
    // 28 logical px on the mark tile, taken at 2x so the row stays crisp on a
    // scaled display.
    private const int Edge = 56;

    private static readonly Guid FactoryId = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>
    /// Icon for a shell parsing name — a file path or a
    /// <c>shell:AppsFolder\…</c> moniker. Null when the shell has nothing to
    /// give; the row then falls back to its kind letter.
    /// </summary>
    public static Bitmap? For(string parsingName)
    {
        if (string.IsNullOrWhiteSpace(parsingName))
        {
            return null;
        }

        IShellItemImageFactory? factory = null;
        IntPtr bits = IntPtr.Zero;

        try
        {
            var riid = FactoryId;
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref riid, out factory);
            if (factory is null)
            {
                return null;
            }

            var hr = factory.GetImage(
                new SIZE { cx = Edge, cy = Edge },
                SIIGBF.IconOnly,
                out bits);

            return hr == 0 && bits != IntPtr.Zero ? ToBitmap(bits) : null;
        }
        catch (Exception)
        {
            // Shell and bitmap failures alike. A missing icon costs the row its
            // letter; it never costs the index a row.
            return null;
        }
        finally
        {
            if (bits != IntPtr.Zero)
            {
                DeleteObject(bits);
            }

            if (factory is not null)
            {
                Marshal.FinalReleaseComObject(factory);
            }
        }
    }

    /// <summary>
    /// Copies a shell HBITMAP into an Avalonia bitmap. Returns null rather than
    /// throwing: a missing icon is a missing letter, not a broken row.
    /// </summary>
    private static Bitmap? ToBitmap(IntPtr hbitmap)
    {
        IntPtr dc = IntPtr.Zero;

        try
        {
            var shape = new BITMAP();
            if (GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), ref shape) == 0 || shape.bmWidth <= 0 || shape.bmHeight <= 0)
            {
                return null;
            }

            var width = shape.bmWidth;
            var height = shape.bmHeight;

            dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero)
            {
                return null;
            }

            var info = new BITMAPINFO
            {
                Header = new BITMAPINFOHEADER
                {
                    Size = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    Width = width,
                    Height = -height, // top-down, the order a bitmap wants
                    Planes = 1,
                    BitCount = 32,
                    Compression = BI_RGB,
                },
            };

            var pixels = new byte[width * height * 4];
            if (GetDIBits(dc, hbitmap, 0, (uint)height, pixels, ref info, DIB_RGB_COLORS) == 0)
            {
                return null;
            }

            var bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

            using (var buffer = bitmap.Lock())
            {
                Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
            }

            return bitmap;
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            if (dc != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, dc);
            }
        }
    }

    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER Header;
        public uint Colors;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr bindContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(IntPtr handle, int count, ref BITMAP buffer);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr dc,
        IntPtr bitmap,
        uint startScan,
        uint scanLines,
        byte[] bits,
        ref BITMAPINFO info,
        uint usage);

    [DllImport("gdi32.dll")]
    private static extern int DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
}
