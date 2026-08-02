using System.Windows.Interop;

namespace ClipGuard.Services;

public sealed class ClipboardChangedEventArgs : EventArgs
{
    public required string? Text { get; init; }
    public required IntPtr OwnerWindow { get; init; }
    public required IntPtr ForegroundWindow { get; init; }
    public required uint SequenceNumber { get; init; }
}

/// <summary>
/// Raw clipboard change notification. Owns a message-only window subscribed to
/// WM_CLIPBOARDUPDATE, so there is no polling loop and no busy CPU.
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    /// <summary>
    /// Windows raises WM_CLIPBOARDUPDATE once per format an app offers, so a
    /// single copy typically arrives two or three times a few ms apart.
    /// </summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(400);

    /// <summary>How long our own write keeps echoing back before we stop ignoring it.</summary>
    private static readonly TimeSpan SelfWriteWindow = TimeSpan.FromSeconds(2);

    private HwndSource? _source;
    private bool _listening;

    /// <summary>Set while ClipGuard itself is writing, so we don't react to our own restore.</summary>
    private string? _selfWriteContent;
    private DateTime _selfWriteAt;

    // Last notification we accepted, for collapsing the duplicate burst.
    private string? _lastText;
    private IntPtr _lastOwner;
    private DateTime _lastAt;

    public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;

    /// <summary>
    /// False when the clipboard listener never attached. Security software that
    /// silently does nothing while claiming to stand guard is worse than absent,
    /// so this is surfaced all the way to the status chip.
    /// </summary>
    public bool IsListening => _listening;

    public void Start()
    {
        if (_source is not null) return;

        // A message-only window (HWND_MESSAGE) receives broadcasts without ever
        // being shown or appearing in the taskbar.
        var parameters = new HwndSourceParameters("ClipGuard.ClipboardListener")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            Width = 0,
            Height = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        _listening = Win32.AddClipboardFormatListener(_source.Handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Win32.WM_CLIPBOARDUPDATE) return IntPtr.Zero;

        handled = true;

        // Capture these before any await/retry - the foreground window can move.
        var owner = Win32.GetClipboardOwner();
        var foreground = Win32.GetForegroundWindow();
        var sequence = Win32.GetClipboardSequenceNumber();

        var text = ReadClipboardText();
        var now = DateTime.UtcNow;

        // Collapse the multi-format notification burst into one logical change.
        if (owner == _lastOwner
            && string.Equals(text, _lastText, StringComparison.Ordinal)
            && now - _lastAt < DuplicateWindow)
        {
            _lastAt = now;
            return IntPtr.Zero;
        }

        _lastText = text;
        _lastOwner = owner;
        _lastAt = now;

        // Our own restore write echoes back - ignore it for as long as the echo
        // can plausibly last, not just once.
        if (_selfWriteContent is not null
            && string.Equals(text, _selfWriteContent, StringComparison.Ordinal)
            && now - _selfWriteAt < SelfWriteWindow)
        {
            return IntPtr.Zero;
        }

        ClipboardChanged?.Invoke(this, new ClipboardChangedEventArgs
        {
            Text = text,
            OwnerWindow = owner,
            ForegroundWindow = foreground,
            SequenceNumber = sequence
        });

        return IntPtr.Zero;
    }

    /// <summary>
    /// The clipboard is a shared, lock-guarded resource - another process may
    /// hold it open for a few milliseconds. Retry rather than drop the event.
    /// </summary>
    public static string? ReadClipboardText()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.ExternalException)
            {
                Thread.Sleep(15);
            }
        }
        return null;
    }

    /// <summary>Writes text back to the clipboard without re-triggering detection.</summary>
    public void WriteClipboardText(string? text)
    {
        _selfWriteContent = text;
        _selfWriteAt = DateTime.UtcNow;

        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                    Clipboard.Clear();
                else
                    // copy:true so the content survives ClipGuard exiting.
                    Clipboard.SetDataObject(text, true);
                return;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.ExternalException)
            {
                Thread.Sleep(15);
            }
        }

        // Never landed - clear the guard so the next real change isn't swallowed.
        _selfWriteContent = null;
    }

    public void Dispose()
    {
        if (_source is null) return;

        if (_listening)
        {
            Win32.RemoveClipboardFormatListener(_source.Handle);
            _listening = false;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }
}
