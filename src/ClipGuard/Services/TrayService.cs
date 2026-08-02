using System.Drawing;
using System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

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
    /// Renders the pixel shield into a 32x32 icon. Same grid as PixelShield.xaml:
    /// an 8x9 shield with a 2x2 notch, drawn with hard edges.
    /// </summary>
    private static Icon BuildIcon(GdiColor color)
    {
        const int cell = 3;
        const int gridW = 8, gridH = 9;
        const int size = 32;

        const int offsetX = (size - gridW * cell) / 2;
        const int offsetY = (size - gridH * cell) / 2;

        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.Clear(GdiColor.Transparent);

            using var brush = new SolidBrush(color);

            // Shield body, matching the SVG rects.
            Fill(g, brush, offsetX, offsetY, 1, 0, 6, 1, cell);
            Fill(g, brush, offsetX, offsetY, 0, 1, 8, 4, cell);
            Fill(g, brush, offsetX, offsetY, 1, 5, 6, 2, cell);
            Fill(g, brush, offsetX, offsetY, 2, 7, 4, 1, cell);
            Fill(g, brush, offsetX, offsetY, 3, 8, 2, 1, cell);

            // Punch the notch back out to transparent.
            g.CompositingMode = CompositingMode.SourceCopy;
            using var clear = new SolidBrush(GdiColor.Transparent);
            g.FillRectangle(clear, new Rectangle(
                offsetX + 3 * cell, offsetY + 2 * cell, 2 * cell, 2 * cell));
        }

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

    private static void Fill(Graphics g, GdiBrush brush, int ox, int oy,
                             int x, int y, int w, int h, int cell) =>
        g.FillRectangle(brush, ox + x * cell, oy + y * cell, w * cell, h * cell);

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
