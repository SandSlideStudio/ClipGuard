using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Media.Imaging;
using ClipGuard.Controls;
using WinForms = System.Windows.Forms;
using WpfMedia = System.Windows.Media;

// GlobalUsings.cs points Color/Brush at WPF for the rest of the app. Global
// aliases can't be shadowed per-file, so GDI+ gets its own names here.
using GdiColor = System.Drawing.Color;
using GdiBrush = System.Drawing.Brush;

namespace ClipGuard.Services;

/// <summary>
/// The tray presence. WPF has no tray primitive, so this is the one place
/// WinForms shows up. The icon is drawn from the same 8x9 pixel-shield grid as
/// the in-app mark, so the two stay in sync.
/// </summary>
public sealed class TrayService : IDisposable
{
    private static readonly GdiColor Accent = GdiColor.FromArgb(0xec, 0x30, 0x13);
    private static readonly GdiColor Muted = GdiColor.FromArgb(0x9b, 0x97, 0x97);

    private readonly WinForms.NotifyIcon _icon;
    private readonly ProtectionService _protection;

    private Icon? _activeIcon;
    private Icon? _pausedIcon;

    public event EventHandler? OpenRequested;
    public event EventHandler? FlyoutRequested;
    public event EventHandler? ExitRequested;

    public TrayService(ProtectionService protection)
    {
        _protection = protection;

        _activeIcon = BuildIcon(Accent);
        _pausedIcon = BuildIcon(Muted);

        _icon = new WinForms.NotifyIcon
        {
            Icon = _activeIcon,
            Text = "ClipGuard — standing guard",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        // Left click opens the flyout; double click jumps straight to the app.
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) FlyoutRequested?.Invoke(this, EventArgs.Empty);
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        _protection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProtectionService.IsPaused) or nameof(ProtectionService.StatusTitle))
                RefreshState();
        };
    }

    private WinForms.ContextMenuStrip BuildMenu()
    {
        var menu = new WinForms.ContextMenuStrip();

        var open = new WinForms.ToolStripMenuItem("Open ClipGuard");
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        var pause = new WinForms.ToolStripMenuItem(_protection.PauseCta);
        pause.Click += (_, _) => _protection.TogglePause();

        var clear = new WinForms.ToolStripMenuItem("Clear clipboard now");
        clear.Click += (_, _) => _protection.ClearClipboardNow();

        var exit = new WinForms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.AddRange([open, pause, clear, new WinForms.ToolStripSeparator(), exit]);

        // Refresh the pause label each time the menu opens.
        menu.Opening += (_, _) => pause.Text = _protection.PauseCta;

        return menu;
    }

    private void RefreshState()
    {
        _icon.Icon = _protection.IsPaused ? _pausedIcon : _activeIcon;
        _icon.Text = $"ClipGuard — {_protection.StatusTitle.ToLowerInvariant()}";
    }

    /// <summary>
    /// Renders the tray icon from <see cref="MarkGeometry"/> using WPF, then
    /// converts to a GDI+ icon.
    ///
    /// Drawing it a second time in GDI+ would mean two hand-maintained copies of
    /// the mark that silently diverge; going through WPF means the tray icon is
    /// the same geometry the app draws on screen, anti-aliased identically.
    ///
    /// The clip is knocked out to transparent rather than painted, so the icon
    /// reads correctly on both light and dark taskbars.
    /// </summary>
    private static Icon BuildIcon(GdiColor color)
    {
        const int size = 32;
        var scale = size / MarkGeometry.Extent;

        var brush = new WpfMedia.SolidColorBrush(
            WpfMedia.Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();

        var visual = new WpfMedia.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new WpfMedia.ScaleTransform(scale, scale));
            dc.DrawGeometry(brush, null, MarkWithClipRemoved);
            dc.Pop();
        }

        var rendered = new RenderTargetBitmap(size, size, 96, 96, WpfMedia.PixelFormats.Pbgra32);
        rendered.Render(visual);

        using var bitmap = ToBitmap(rendered, size);
        return FromBitmap(bitmap);
    }

    /// <summary>
    /// The simplified mark: a tray icon is drawn at 16-24px whatever we render
    /// it at, so the two-wire clip is the right one here regardless of the
    /// bitmap size we hand Windows.
    /// </summary>
    private static readonly WpfMedia.Geometry MarkWithClipRemoved =
        MarkGeometry.BuildSolidMark(simplified: true);

    /// <summary>WPF render target to a GDI+ bitmap, preserving the alpha channel.</summary>
    private static Bitmap ToBitmap(RenderTargetBitmap source, int size)
    {
        var stride = size * 4;
        var pixels = new byte[stride * size];
        source.CopyPixels(pixels, stride, 0);

        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, size, size),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private static Icon FromBitmap(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        using var temp = Icon.FromHandle(handle);

        // Clone so the icon survives destroying the native handle.
        var icon = (Icon)temp.Clone();
        DestroyIcon(handle);
        return icon;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();

        _activeIcon?.Dispose();
        _pausedIcon?.Dispose();
        _activeIcon = null;
        _pausedIcon = null;
    }
}
