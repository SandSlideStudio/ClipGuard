namespace ClipGuard.Services;

/// <summary>
/// Guards the moment that actually causes harm: a command going into a terminal.
///
/// The ClickFix / fake-CAPTCHA attack never swaps anything — a page parks a
/// PowerShell one-liner on your clipboard and tells you to press Win+R, Ctrl+V,
/// Enter. Swap detection is blind to it, so this watches for the *destination*
/// instead: when a shell or the Run dialog takes focus while a command is on the
/// clipboard, say something.
///
/// Deliberately a foreground-window hook rather than a keyboard hook. It fires
/// at the same moment — Win+R opening is what raises it — while observing no
/// keystrokes at all. A clipboard security tool has no business keylogging.
/// </summary>
public sealed class PasteGuardService : IDisposable
{
    /// <summary>Processes where pasting a command executes it.</summary>
    private static readonly string[] ShellProcesses =
    [
        "cmd.exe", "powershell.exe", "pwsh.exe", "windowsterminal.exe",
        "wt.exe", "conhost.exe", "openconsole.exe", "mshta.exe",
        "wscript.exe", "cscript.exe"
    ];

    /// <summary>Don't nag about the same clipboard content over and over.</summary>
    private static readonly TimeSpan RewarnInterval = TimeSpan.FromSeconds(30);

    private readonly ProtectionService _protection;
    private readonly ProcessResolver _resolver = new();

    // The delegate must be held: the hook keeps an unmanaged pointer to it, and
    // letting it be collected crashes the callback.
    private readonly Win32.WinEventProc _callback;
    private IntPtr _hook;

    private string? _lastWarnedContent;
    private DateTime _lastWarnedAt;

    public PasteGuardService(ProtectionService protection)
    {
        _protection = protection;
        _callback = OnForegroundChanged;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;

        _hook = Win32.SetWinEventHook(
            Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _callback, 0, 0,
            Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd,
                                     int idObject, int idChild, uint threadId, uint timestamp)
    {
        if (!_protection.Settings.WarnBeforeTerminalPaste) return;
        if (hwnd == IntPtr.Zero) return;

        var target = _resolver.Resolve(hwnd);
        if (!IsPasteTarget(target.Name, hwnd)) return;

        // Read the clipboard live — the guard has to reflect what would actually
        // be pasted, not what monitoring last recorded.
        var text = ClipboardMonitor.ReadClipboardText();
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!RiskAnalyzer.IsShellCommand(text)) return;

        var now = DateTime.UtcNow;
        if (string.Equals(text, _lastWarnedContent, StringComparison.Ordinal)
            && now - _lastWarnedAt < RewarnInterval)
        {
            return;
        }

        _lastWarnedContent = text;
        _lastWarnedAt = now;

        _protection.RaisePasteWarning(FriendlyTarget(target, hwnd), text);
    }

    private static bool IsPasteTarget(string processName, IntPtr hwnd)
    {
        if (ShellProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase)) return true;

        // Win+R. The Run box is a standard dialog (class #32770) owned by the shell.
        return processName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
               && Win32.GetWindowClass(hwnd) == "#32770";
    }

    /// <summary>
    /// Names the user recognises. Version-info descriptions are unhelpful here —
    /// conhost.exe calls itself "Windows Terminal Host", which means nothing to
    /// someone being told not to paste something.
    /// </summary>
    private static string FriendlyTarget(ProcessIdentity target, IntPtr hwnd)
    {
        if (target.Name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
            && Win32.GetWindowClass(hwnd) == "#32770")
        {
            return "the Run dialog";
        }

        return target.Name.ToLowerInvariant() switch
        {
            "cmd.exe" => "Command Prompt",
            "powershell.exe" or "pwsh.exe" => "PowerShell",
            "windowsterminal.exe" or "wt.exe" => "Windows Terminal",
            "conhost.exe" or "openconsole.exe" => "a console window",
            "mshta.exe" => "the Windows script host",
            "wscript.exe" or "cscript.exe" => "the Windows script host",
            _ => target.FriendlyName ?? target.Name
        };
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;

        Win32.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }
}
