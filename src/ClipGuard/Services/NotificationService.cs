using System.Windows;
using System.Windows.Media;
using ClipGuard.Views;

namespace ClipGuard.Services;

/// <summary>
/// Places toasts bottom-right and stacks them upward, newest nearest the tray.
/// Caps how many show at once so a burst of events can't wallpaper the screen.
/// </summary>
public sealed class NotificationService
{
    private const int MaxVisible = 4;
    private const double Gap = 4;

    private readonly ProtectionService _protection;
    private readonly List<NotificationWindow> _open = [];

    public NotificationService(ProtectionService protection) => _protection = protection;

    public void Show(NotificationRequest request)
    {
        // Oldest goes first when we're at capacity.
        while (_open.Count >= MaxVisible)
        {
            var oldest = _open[0];
            _open.RemoveAt(0);
            oldest.Close();
        }

        var window = new NotificationWindow(_protection, request);
        window.Dismissed += (_, _) =>
        {
            _open.Remove(window);
            Reflow();
        };

        _open.Add(window);
        window.Show();

        // Show() finishes layout, so the height is real by the time we reflow.
        Reflow();
    }

    private void Reflow()
    {
        var work = Screen.PrimaryScreen?.WorkingArea
                   ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

        double offset = 0;

        // Newest sits closest to the tray, older ones pushed up.
        for (var i = _open.Count - 1; i >= 0; i--)
        {
            var window = _open[i];
            if (!window.IsLoaded) continue;

            var dpi = VisualTreeHelper.GetDpi(window);
            var height = window.ActualHeight > 0 ? window.ActualHeight : window.DesiredSize.Height;

            window.Left = (work.Right / dpi.DpiScaleX) - window.Width;
            window.Top = (work.Bottom / dpi.DpiScaleY) - height - offset;

            offset += height + Gap;
        }
    }

    public void CloseAll()
    {
        foreach (var window in _open.ToList()) window.Close();
        _open.Clear();
    }
}
