using System.Windows;
using System.Windows.Media;
using ClipGuard.ViewModels;

namespace ClipGuard.Views;

public partial class TrayFlyoutWindow : Window
{
    private readonly MainViewModel _vm;

    public event EventHandler? OpenAppRequested;

    public TrayFlyoutWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    /// <summary>Anchors the flyout above the tray, inside the working area.</summary>
    public void ShowNearTray()
    {
        var work = Screen.PrimaryScreen?.WorkingArea
                   ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

        // Measure before positioning - SizeToContent means Height isn't known yet.
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;

        Left = (work.Right / dpi.DpiScaleX) - width;
        Top = (work.Bottom / dpi.DpiScaleY) - height;

        Show();
        Activate();
    }

    private void OnDeactivated(object? sender, EventArgs e) => Hide();

    private void OnOpenApp(object sender, RoutedEventArgs e)
    {
        Hide();
        OpenAppRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnTogglePause(object sender, RoutedEventArgs e) => _vm.Protection.TogglePause();

    private void OnClearClipboard(object sender, RoutedEventArgs e)
    {
        _vm.Protection.ClearClipboardNow();
        Hide();
    }
}
