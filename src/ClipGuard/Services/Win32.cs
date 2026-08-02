using System.Runtime.InteropServices;
using System.Text;

namespace ClipGuard.Services;

/// <summary>
/// The Windows surface ClipGuard needs. Clipboard change notification is
/// push-based via AddClipboardFormatListener — no polling.
/// </summary>
/// <remarks>
/// Uses DllImport rather than LibraryImport: the source generator requires
/// AllowUnsafeBlocks, and this handful of calls isn't hot enough to justify
/// turning unsafe code on across the assembly.
/// </remarks>
internal static class Win32
{
    public const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    /// <summary>Window that last placed data on the clipboard.</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    // ── Foreground-change notification ────────────────────────────────────
    // Backs the paste guard. Deliberately a window-focus hook, not a keyboard
    // hook: it fires at the same moment (Win+R opening, or a terminal taking
    // focus) without observing a single keystroke.

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    public delegate void WinEventProc(
        IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint threadId, uint timestamp);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr module,
        WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder buffer, int maxCount);

    /// <summary>Win32 class name of a window, e.g. "#32770" for a dialog.</summary>
    public static string GetWindowClass(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return string.Empty;

        var buffer = new StringBuilder(256);
        var length = GetClassName(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString() : string.Empty;
    }

    // ── Process image path ────────────────────────────────────────────────
    // QueryFullProcessImageName works across bitness, unlike Process.MainModule.

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr process, uint flags, StringBuilder buffer, ref uint size);

    /// <summary>Full image path for a pid, or null if it can't be read.</summary>
    public static string? GetProcessImagePath(uint pid)
    {
        if (pid == 0) return null;

        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return null;

        try
        {
            uint size = 1024;
            var buffer = new StringBuilder((int)size);
            return QueryFullProcessImageName(handle, 0, buffer, ref size)
                ? buffer.ToString()
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Owning process id for a window handle.</summary>
    public static uint GetProcessIdForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }
}
