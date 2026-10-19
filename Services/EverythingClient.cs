using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ZTool.Services;

/// <summary>One row out of Everything's index.</summary>
public sealed record EverythingHit(string FullPath, bool IsFolder, long Size, long ModifiedFileTime);

/// <summary>
/// Everything over its own IPC: a query on a hidden window, an answer back to
/// ours. No DLL to redistribute, no licence to carry — the wire format is the
/// SDK's public header, and the SDK itself is MIT.
///
/// Submitting returns in well under a millisecond; the answer arrives on the
/// message loop some tens of milliseconds later. Blocking for it would freeze
/// typing on a four-million-file index, so the caller is expected to keep
/// whatever it had and swap when <see cref="ResultsReady"/> says so.
/// </summary>
public sealed class EverythingClient : IDisposable
{
    private const uint WM_COPYDATA = 0x004A;

    /// <summary>EVERYTHING_IPC_COPYDATA_QUERY2W — the wide QUERY2 packet.</summary>
    private const uint Query2 = 18;

    private const uint ItemFolder = 0x01;

    // Request flags, answered in this order in the reply's data area.
    private const uint RequestFullPath = 0x04;
    private const uint RequestSize = 0x10;
    private const uint RequestDateCreated = 0x20;
    private const uint RequestDateModified = 0x40;
    private const uint Request = RequestFullPath | RequestSize | RequestDateModified;

    // Name ascending is the one sort Everything answers off its index for free,
    // and it is the only one that makes the same query return the same rows.
    private const uint SortNameAscending = 1;

    private static readonly IntPtr MessageOnly = new(-3);

    private const string EverythingWindowClass = "EVERYTHING_TASKBAR_NOTIFICATION";

    private readonly WndProc _proc;
    private readonly IntPtr _hwnd;
    private readonly IntPtr _copyData;
    private bool _disposed;

    public EverythingClient()
    {
        _proc = OnMessage;

        var windowClass = new WNDCLASSEX
        {
            Size = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            Proc = _proc,
            Instance = GetModuleHandle(null),
            Name = "ZTool.Everything." + Guid.NewGuid().ToString("N"),
        };

        if (RegisterClassEx(ref windowClass) == 0)
        {
            throw new InvalidOperationException("could not register the Everything reply window");
        }

        _hwnd = CreateWindowEx(
            0, windowClass.Name, string.Empty, 0, 0, 0, 0, 0,
            MessageOnly, IntPtr.Zero, windowClass.Instance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("could not create the Everything reply window");
        }

        // Everything may be running elevated and we may not be; let its reply in.
        ChangeWindowMessageFilterEx(_hwnd, WM_COPYDATA, 1, IntPtr.Zero);

        _copyData = Marshal.AllocHGlobal(Marshal.SizeOf<COPYDATASTRUCT>());
    }

    /// <summary>
    /// Raised on the UI thread with the answer to one query. A newer query
    /// cancels an older one inside Everything, so anything stale simply never
    /// arrives — and whatever does arrive carries its own query string.
    /// </summary>
    public event Action<string, IReadOnlyList<EverythingHit>>? ResultsReady;

    /// <summary>False when Everything is not running — the caller should keep its own catalogue.</summary>
    public static bool IsRunning => FindWindow(EverythingWindowClass, null) != IntPtr.Zero;

    /// <summary>
    /// Asks for up to <paramref name="max"/> rows matching <paramref name="query"/>.
    /// Everything's own search syntax is passed through untouched, so
    /// <c>ext:js</c>, <c>size:&gt;10mb</c> and wildcards all work; matching is
    /// on the file name only unless the query says <c>path:</c> itself.
    /// </summary>
    public bool Submit(string query, int max)
    {
        var target = FindWindow(EverythingWindowClass, null);
        if (target == IntPtr.Zero)
        {
            return false;
        }

        // One reply window, one outstanding query: a second submit supersedes.
        var tag = unchecked(++_tag);

        var searchBytes = (query.Length + 1) * sizeof(char);
        var total = QueryHeaderBytes + searchBytes;

        var packet = new byte[total];
        WriteUInt(packet, 0, (uint)_hwnd.ToInt64());
        WriteUInt(packet, 4, (uint)tag);
        WriteUInt(packet, 8, 0);                        // search flags: name match, case off
        WriteUInt(packet, 12, 0);                       // offset
        WriteUInt(packet, 16, (uint)max);
        WriteUInt(packet, 20, Request);
        WriteUInt(packet, 24, SortNameAscending);
        Encoding.Unicode.GetBytes(query, 0, query.Length, packet, QueryHeaderBytes);

        var payload = Marshal.AllocHGlobal(total);
        try
        {
            Marshal.Copy(packet, 0, payload, total);

            var data = new COPYDATASTRUCT { Data = (IntPtr)Query2, Size = total, Buffer = payload };
            Marshal.StructureToPtr(data, _copyData, false);

            _pendingTag = tag;
            _pendingQuery = query;

            // Blocks only for Everything to accept the packet — tens of microseconds.
            return SendMessage(target, WM_COPYDATA, _hwnd, _copyData) != IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(payload);
        }
    }

    private const int QueryHeaderBytes = 28;

    private int _tag;
    private int _pendingTag;
    private string _pendingQuery = string.Empty;

    private IntPtr OnMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_COPYDATA)
        {
            var data = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            if (data.Data.ToInt64() == _pendingTag)
            {
                // WM_COPYDATA's buffer dies with the call; take it all now.
                var bytes = new byte[data.Size];
                Marshal.Copy(data.Buffer, bytes, 0, data.Size);

                ResultsReady?.Invoke(_pendingQuery, Parse(bytes));
                return (IntPtr)1;
            }
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Reads an EVERYTHING_IPC_LIST2 reply. The item table comes first, then a
    /// data area whose entries the item's <c>data_offset</c> points at from the
    /// start of the reply — not from the start of the data area.
    /// </summary>
    private static IReadOnlyList<EverythingHit> Parse(byte[] reply)
    {
        var count = (int)UInt(reply, 4);
        if (count <= 0)
        {
            return Array.Empty<EverythingHit>();
        }

        var flags = UInt(reply, 12);
        var rows = new List<EverythingHit>(count);
        var at = 20; // five DWORDs of header

        for (var i = 0; i < count; i++)
        {
            var itemFlags = UInt(reply, at);
            var data = (int)UInt(reply, at + 4);
            at += 8;

            // Requested fields arrive in flag order. Skip anything we did not ask for.
            if ((flags & 0x01) != 0)
            {
                data = SkipString(reply, data);
            }

            if ((flags & 0x02) != 0)
            {
                data = SkipString(reply, data);
            }

            var path = ReadString(reply, data, out data);

            if ((flags & 0x08) != 0)
            {
                data = SkipString(reply, data);
            }

            var size = (flags & RequestSize) != 0 ? ReadInt64(reply, data, out data) : 0;

            if ((flags & RequestDateCreated) != 0)
            {
                data += 8;
            }

            var modified = (flags & RequestDateModified) != 0 ? ReadInt64(reply, data, out data) : 0;

            rows.Add(new EverythingHit(path, (itemFlags & ItemFolder) != 0, size, modified));
        }

        return rows;
    }

    private static uint UInt(byte[] buffer, int offset) => BitConverter.ToUInt32(buffer, offset);

    private static void WriteUInt(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    /// <summary>A counted UTF-16 string with its terminating NUL.</summary>
    private static string ReadString(byte[] buffer, int at, out int next)
    {
        var characters = (int)UInt(buffer, at);
        var value = Encoding.Unicode.GetString(buffer, at + 4, characters * 2);
        next = at + 4 + characters * 2 + 2;
        return value;
    }

    private static int SkipString(byte[] buffer, int at) => at + 4 + (int)UInt(buffer, at) * 2 + 2;

    private static long ReadInt64(byte[] buffer, int at, out int next)
    {
        next = at + 8;
        return BitConverter.ToInt64(buffer, at);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
        }

        if (_copyData != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_copyData);
        }

        GC.KeepAlive(_proc);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr Data;
        public int Size;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint Size;
        public uint Style;

        // CharSet.Unicode above is load-bearing: without it the class name is
        // marshalled as ANSI and RegisterClassEx quietly registers nothing.
        public WndProc Proc;

        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string Name;
        public IntPtr SmallIcon;
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint msg, uint action, IntPtr changeFilter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
