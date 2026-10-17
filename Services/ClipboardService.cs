using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace ZTool.Services;

/// <summary>
/// Turns copies into history rows and rows back into copies. Everything a
/// clipboard feature is judged on lives here: what counts, what is refused, how
/// repeats collapse, and how a row goes back to the app it came from.
/// </summary>
public sealed class ClipboardService : IDisposable
{
    /// <summary>Row count the table is held at. Pinned rows are never the ones dropped.</summary>
    public const int Capacity = 1000;

    /// <summary>Over this and the copy is not history — truncating would not be the text.</summary>
    private const int MaxTextBytes = 256 * 1024;

    private const int MaxImageBytes = 2 * 1024 * 1024;

    private const string PauseKey = "clipboard.paused";

    private readonly ClipboardStore _store;
    private readonly object _gate = new();
    private ClipboardWatcher? _watcher;
    private IntPtr _pasteTarget;
    private bool _suppressEcho;

    public ClipboardService(ClipboardStore store)
    {
        _store = store;
    }

    public event Action? Changed;

    public bool IsPaused
    {
        get => SettingsStore.ReadBool(PauseKey, false);
        set
        {
            SettingsStore.WriteBool(PauseKey, value);
            Changed?.Invoke();
        }
    }

    /// <summary>Remembers the window a paste belongs to. Called when the palette comes up.</summary>
    public void NotePasteTarget()
    {
        lock (_gate)
        {
            _pasteTarget = GetForegroundWindow();
        }
    }

    public void Start()
    {
        _watcher = ClipboardWatcher.Start(OnClipboardChanged);
        CaptureCurrent();
    }

    /// <summary>
    /// Whatever is on the clipboard right now becomes the newest row. A palette
    /// that starts at login would otherwise be blind to the last thing copied
    /// the night before.
    /// </summary>
    public void CaptureCurrent()
    {
        Task.Run(() => Capture());
    }

    /// <summary>
    /// Takes a row back to where it came from: clipboard first, keystroke
    /// second. If the keystroke never lands — the target took focus away, or it
    /// is elevated and will not accept input — the content is still on the
    /// clipboard and one Ctrl+V finishes the job.
    /// </summary>
    public void Paste(ClipboardEntry entry)
    {
        lock (_gate)
        {
            _suppressEcho = true;
        }

        try
        {
            var written = entry.Kind == ClipboardKind.Text
                ? ClipboardWatcher.WriteText(entry.Content)
                : ClipboardWatcher.WriteImage(ReadImage(entry.Content));

            if (!written)
            {
                return;
            }
        }
        finally
        {
            lock (_gate)
            {
                _suppressEcho = false;
            }
        }

        _store.Touch(entry.Id);

        IntPtr target;
        lock (_gate)
        {
            target = _pasteTarget;
        }

        SendPaste(target);
        Changed?.Invoke();
    }

    public void TogglePinned(ClipboardEntry entry)
    {
        _store.SetPinned(entry.Id, !entry.Pinned);
        Changed?.Invoke();
    }

    public void Delete(ClipboardEntry entry)
    {
        var owned = _store.Delete(entry.Id);
        if (entry.Kind == ClipboardKind.Image && !string.IsNullOrEmpty(owned))
        {
            ImageStore.Delete(owned);
        }

        Changed?.Invoke();
    }

    private void OnClipboardChanged()
    {
        if (_suppressEcho)
        {
            return;
        }

        Task.Run(() => Capture());
    }

    private void Capture()
    {
        try
        {
            if (IsPaused)
            {
                return;
            }

            var payload = ClipboardWatcher.Read();
            if (payload is null)
            {
                return;
            }

            // Blame is read before anything else: writing back later would make
            // ZTool look like the source of everything it ever saw.
            var source = ClipboardWatcher.SourceProcess() ?? string.Empty;
            if (ProcessExclusions.IsExcluded(source))
            {
                return;
            }

            if (payload.Kind == ClipboardKind.Text)
            {
                var text = Normalize(payload.Text);
                if (text.Length == 0 || Encoding.UTF8.GetByteCount(text) > MaxTextBytes)
                {
                    return;
                }

                _store.Upsert(ClipboardKind.Text, text, PreviewOf(text), Hash(Encoding.UTF8.GetBytes(text)), source, Capacity);
            }
            else
            {
                var png = payload.PngBytes;
                if (png is null || png.Length == 0 || png.Length > MaxImageBytes)
                {
                    return;
                }

                var name = ImageStore.Save(LoadBitmap(png), HashOfImage(png));
                _store.Upsert(ClipboardKind.Image, name, PreviewOf(LoadBitmap(png)), HashOfImage(png), source, Capacity);
            }

            ImageStore.Sweep(_store.ReferencedImages());
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // A copy we could not read is a copy we do not have. The next one
            // still has to work.
        }
    }

    /// <summary>
    /// Drops the CRLF Windows appends to text on the way in. The body is left
    /// exactly as copied — this is the tail the platform added, not content.
    /// </summary>
    public static string Normalize(string text) => text.TrimEnd('\r', '\n', ' ', '\t');

    private static string PreviewOf(string text)
    {
        var firstLine = text;
        var newline = text.IndexOfAny(new[] { '\r', '\n' });
        if (newline >= 0)
        {
            firstLine = text[..newline];
        }

        return firstLine.Length <= 60 ? firstLine : firstLine[..60] + "…";
    }

    private static string PreviewOf(Bitmap bitmap) => $"{bitmap.PixelSize.Width}×{bitmap.PixelSize.Height}";

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>
    /// Images are deduplicated by what they look like, not by how they happen to
    /// be encoded — the same screenshot copied twice is one row.
    /// </summary>
    private static string HashOfImage(byte[] png)
    {
        try
        {
            using var bitmap = LoadBitmap(png);
            var pixels = DibFactory.FromBitmap(bitmap);
            if (pixels is not null)
            {
                return Hash(pixels);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ExternalException)
        {
            // Fall through to the encoded bytes — a hash of something beats none.
        }

        return Hash(png);
    }

    private static Bitmap LoadBitmap(byte[] png)
    {
        using var stream = new MemoryStream(png);
        return new Bitmap(stream);
    }

    private static byte[] ReadImage(string fileName)
    {
        var path = ImageStore.PathFor(fileName);
        return File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
    }

    private void SendPaste(IntPtr target)
    {
        if (target != IntPtr.Zero)
        {
            SetForegroundWindow(target);
            // Focus follows the call, not the call's return — give it a beat
            // before the keystroke or the key goes to whoever had it before.
            System.Threading.Thread.Sleep(50);
        }

        var down = new INPUT[4];
        down[0] = Key(VK_CONTROL, 0);
        down[1] = Key('V', 0);
        down[2] = Key('V', KEYEVENTF_KEYUP);
        down[3] = Key(VK_CONTROL, KEYEVENTF_KEYUP);

        SendInput((uint)down.Length, down, Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(ushort code, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { Keyboard = new KEYBDINPUT { wVk = code, dwFlags = flags } },
    };

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_CONTROL = 0x11;

    /// <summary>
    /// 40 bytes on x64 — the union is sized by MOUSEINPUT, the widest of the
    /// three. Shrink it to KEYBDINPUT's 24 and SendInput rejects the whole
    /// batch with ERROR_INVALID_PARAMETER, and the paste silently does nothing.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT Mouse;

        [FieldOffset(0)]
        public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
        _store.Dispose();
    }
}
