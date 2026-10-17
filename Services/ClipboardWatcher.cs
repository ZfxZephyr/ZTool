using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ZTool.Services;

/// <summary>What was on the clipboard, before any of it is stored.</summary>
public sealed record ClipboardPayload(ClipboardKind Kind, string Text, byte[]? PngBytes);

/// <summary>
/// Raw clipboard access. The palette is hidden most of the time, so listening
/// hangs off a message-only window rather than the visible one — a hidden
/// window still gets <c>WM_CLIPBOARDUPDATE</c>, and it keeps clipboard plumbing
/// out of Avalonia's window entirely.
/// </summary>
public sealed class ClipboardWatcher : IDisposable
{
    private const uint WM_CLIPBOARDUPDATE = 0x031D;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly WndProc _proc;
    private readonly IntPtr _hwnd;
    private bool _disposed;

    private ClipboardWatcher(WndProc proc, IntPtr hwnd, Action onChanged)
    {
        _proc = proc;
        _hwnd = hwnd;
        _onChanged = onChanged;
    }

    private readonly Action _onChanged;

    public static ClipboardWatcher? Start(Action onChanged)
    {
        WndProc proc = (hwnd, msg, wParam, lParam) =>
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                onChanged();
                return IntPtr.Zero;
            }

            return DefWindowProc(hwnd, msg, wParam, lParam);
        };

        var className = "ZTool.ClipboardListener." + Guid.NewGuid().ToString("N");
        var module = GetModuleHandle(null);

        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = proc,
            hInstance = module,
            lpszClassName = className,
        };

        if (RegisterClassEx(ref windowClass) == 0)
        {
            return null;
        }

        var hwnd = CreateWindowEx(
            0, className, string.Empty, 0, 0, 0, 0, 0,
            HWND_MESSAGE, IntPtr.Zero, module, IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        if (!AddClipboardFormatListener(hwnd))
        {
            DestroyWindow(hwnd);
            return null;
        }

        // The delegate is rooted on the instance; the window's class still
        // points at it and the GC must not collect it mid-pump.
        return new ClipboardWatcher(proc, hwnd, onChanged);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        RemoveClipboardFormatListener(_hwnd);
        DestroyWindow(_hwnd);
        GC.KeepAlive(_proc);
    }

    /// <summary>
    /// Takes whatever is on the clipboard. Prefers the "PNG" format when an app
    /// put one there — it is the pixels exactly as copied — and falls back to
    /// the device-independent bitmap every screenshot also leaves behind.
    /// </summary>
    public static ClipboardPayload? Read()
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return null;
        }

        try
        {
            if (IsAvailable(CF_HDROP))
            {
                // Files are a later phase. Recording their paths as "text"
                // would be worse than recording nothing: it looks like a copy
                // of words and is not.
                return null;
            }

            if (IsAvailable(CF_UNICODETEXT))
            {
                var text = ReadUnicodeText();
                return string.IsNullOrEmpty(text) ? null : new ClipboardPayload(ClipboardKind.Text, text, null);
            }

            var png = ReadPng();
            if (png is not null)
            {
                return new ClipboardPayload(ClipboardKind.Image, string.Empty, png);
            }

            // PrintScreen puts up three formats and PNG is not one of them:
            // CF_BITMAP, CF_DIB and CF_DIBV5. V5 is the fullest of the three.
            var dib = ReadBytes(CF_DIBV5) ?? ReadBytes(CF_DIB);
            return dib is null ? null : new ClipboardPayload(ClipboardKind.Image, string.Empty, DibToPng(dib));
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Puts a piece of history back where the apps can see it. Text goes as
    /// Unicode; an image goes as both PNG and DIB so whatever the target reads
    /// is the one it gets.
    /// </summary>
    public static bool WriteText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            EmptyClipboard();

            var bytes = Encoding.Unicode.GetBytes(text + "\0");
            var handle = Alloc(bytes);
            return handle != IntPtr.Zero && SetClipboardData(CF_UNICODETEXT, handle) != IntPtr.Zero;
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static bool WriteImage(byte[] png)
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            EmptyClipboard();

            var pngFormat = RegisterClipboardFormat("PNG");
            var ok = false;

            var pngHandle = Alloc(png);
            if (pngHandle != IntPtr.Zero)
            {
                ok = SetClipboardData(pngFormat, pngHandle) != IntPtr.Zero;
            }

            var dib = PngToDib(png);
            if (dib is not null)
            {
                var dibHandle = Alloc(dib);
                ok |= dibHandle != IntPtr.Zero && SetClipboardData(CF_DIB, dibHandle) != IntPtr.Zero;
            }

            return ok;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Who to blame for this copy. The clipboard's owner window is usually gone
    /// by the time we look, so the fallback is whatever window had focus — which
    /// in practice is the app that did the copying.
    /// </summary>
    public static string? SourceProcess()
    {
        var process = ProcessNameFor(GetClipboardOwner());
        return process ?? ProcessNameFor(GetForegroundWindow());
    }

    private static string? ProcessNameFor(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            GetWindowThreadProcessId(window, out var processId);
            if (processId == 0)
            {
                return null;
            }

            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static bool IsAvailable(uint format) => IsClipboardFormatAvailable(format);

    private static string? ReadUnicodeText()
    {
        var handle = GetClipboardData(CF_UNICODETEXT);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static byte[]? ReadBytes(uint format)
    {
        var handle = GetClipboardData(format);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var size = GlobalSize(handle);
        if (size <= 0)
        {
            return null;
        }

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var length = checked((int)size);
            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return bytes;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static byte[]? ReadPng()
    {
        var format = RegisterClipboardFormat("PNG");
        return format == 0 ? null : ReadBytes(format);
    }

    /// <summary>
    /// Turns a DIB into a PNG by handing the bytes to a BMP decoder. A DIB is a
    /// BMP with the file header missing, so adding that header is the whole
    /// conversion — every colour depth the clipboard can hold comes free.
    /// </summary>
    private static byte[]? DibToPng(byte[] dib)
    {
        try
        {
            using var stream = new System.IO.MemoryStream();
            WriteBmpHeader(stream, dib);
            stream.Write(dib, 0, dib.Length);
            stream.Position = 0;

            using var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            using var png = new System.IO.MemoryStream();
            bitmap.Save(png, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            return png.ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ExternalException)
        {
            return null;
        }
    }

    /// <summary>
    /// A DIB is a BMP with the file header missing. Where the pixels start
    /// depends on the DIB itself: a BITMAPINFOHEADER claiming BI_BITFIELDS
    /// trails three colour masks behind it, and a BITMAPV5HEADER already
    /// carries its masks inside. Getting this wrong is why real screenshots
    /// would not decode.
    /// </summary>
    private static void WriteBmpHeader(System.IO.Stream stream, byte[] dib)
    {
        const int FileHeaderSize = 14;
        const uint BI_BITFIELDS = 3;

        if (dib.Length < 16)
        {
            throw new ArgumentException("not a DIB", nameof(dib));
        }

        var headerSize = BitConverter.ToUInt32(dib, 0);
        var bitCount = BitConverter.ToUInt16(dib, 14);
        var compression = BitConverter.ToUInt32(dib, 16);

        ulong pixelsAt = FileHeaderSize + headerSize;

        if (headerSize == 40 && compression == BI_BITFIELDS)
        {
            pixelsAt += 12; // three DWORD masks
        }
        else if (headerSize == 40 && bitCount <= 8)
        {
            var colours = BitConverter.ToUInt32(dib, 32); // biClrUsed
            pixelsAt += (colours == 0 ? 1u << bitCount : colours) * 4u;
        }

        Span<byte> header = stackalloc byte[FileHeaderSize];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BitConverter.TryWriteBytes(header[2..], checked((int)(FileHeaderSize + (ulong)dib.Length)));
        BitConverter.TryWriteBytes(header[10..], checked((int)pixelsAt));

        stream.Write(header);
    }

    private static byte[]? PngToDib(byte[] png)
    {
        try
        {
            using var stream = new System.IO.MemoryStream(png);
            using var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            return DibFactory.FromBitmap(bitmap);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ExternalException)
        {
            return null;
        }
    }

    private static IntPtr Alloc(byte[] bytes)
    {
        var handle = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            GlobalFree(handle);
            return IntPtr.Zero;
        }

        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        return handle;
    }

    private const uint CF_BITMAP = 2;
    private const uint CF_DIB = 8;
    private const uint CF_DIBV5 = 17;
    private const uint CF_HDROP = 15;
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern int DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint format, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string name);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr handle);
}
