using System;
using System.Runtime.InteropServices;

namespace ZTool.Services;

/// <summary>The chord that ended up being claimed, rendered as it was taken.</summary>
public sealed record HotkeyBinding(string Modifier, string Key);

/// <summary>
/// System-wide summon. Avalonia ships no global hotkey, so this owns a
/// message-only Win32 window created on the UI thread — the thread's message
/// pump delivers WM_HOTKEY straight back into our window procedure.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModCtrl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const int HotkeyId = 0x5A01;

    private const uint VkSpace = 0x20;
    private const uint VkZ = 0x5A;

    private static readonly IntPtr HwndMessage = new(-3);

    // Alt+Space is the advertised chord and the one the footer shows when we
    // get it. It is also PowerToys Run's default, so the rest are fallbacks
    // rather than alternatives — the tool has to stay summonable by keyboard.
    private static readonly (uint Modifiers, uint Key, HotkeyBinding Label)[] Candidates =
    {
        (ModAlt, VkSpace, new HotkeyBinding("Alt", "Space")),
        (ModAlt, VkZ, new HotkeyBinding("Alt", "Z")),
        (ModAlt | ModCtrl, VkZ, new HotkeyBinding("Ctrl+Alt", "Z")),
    };

    // Held for the lifetime of the window; a collected delegate faults the
    // message loop the moment the next hotkey arrives.
    private Native.WndProc? _windowProc;
    private IntPtr _hwnd;
    private HotkeyBinding? _bound;

    public event Action? Pressed;

    public HotkeyBinding? Bound => _bound;

    /// <summary>Non-null when we could not even create the message window.</summary>
    public string? ShellFailure { get; private set; }

    /// <summary>True when the advertised chord had to be given up to something else.</summary>
    public bool FellBack { get; private set; }

    public bool Register()
    {
        if (_bound is not null)
        {
            return true;
        }

        _windowProc = HandleMessage;

        var instance = Native.GetModuleHandle(null);
        var className = "ZTool.GlobalHotkey." + Environment.ProcessId;

        var windowClass = new Native.WNDCLASS
        {
            lpfnWndProc = _windowProc,
            hInstance = instance,
            lpszClassName = className,
        };

        if (Native.RegisterClass(ref windowClass) == 0)
        {
            ShellFailure = "RegisterClass 失败";
            return false;
        }

        _hwnd = Native.CreateWindowEx(
            0, className, string.Empty, 0,
            0, 0, 0, 0,
            HwndMessage, IntPtr.Zero, instance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            ShellFailure = "CreateWindowEx 失败";
            return false;
        }

        for (var i = 0; i < Candidates.Length; i++)
        {
            var candidate = Candidates[i];
            if (Native.RegisterHotKey(_hwnd, HotkeyId, candidate.Modifiers | ModNoRepeat, candidate.Key))
            {
                _bound = candidate.Label;
                FellBack = i > 0;
                return true;
            }
        }

        return false;
    }

    private IntPtr HandleMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmHotkey)
        {
            Pressed?.Invoke();
            return IntPtr.Zero;
        }

        return Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_bound is not null)
        {
            Native.UnregisterHotKey(_hwnd, HotkeyId);
            _bound = null;
        }

        if (_hwnd != IntPtr.Zero)
        {
            Native.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        _windowProc = null;
    }

    private static class Native
    {
        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASS
        {
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowEx(
            int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
