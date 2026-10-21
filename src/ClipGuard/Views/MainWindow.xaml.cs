using System.ComponentModel;
using System.Windows;
using ClipGuard.ViewModels;

namespace ClipGuard.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    /// <summary>
    /// The banner's button. Opens the release page in a browser; ClipGuard
    /// never fetches or runs an update itself.
    /// </summary>
    private void OnOpenReleases(object sender, RoutedEventArgs e)
    {
        var url = _vm.Updates.Available?.PageUrl ?? Services.UpdateService.ReleasesPage;

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // No default browser, or the shell refused.
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>Set by AppHost so a real shutdown can actually close the window.</summary>
    public bool AllowClose { get; set; }

    /// <summary>
    /// Closing the window hides it - ClipGuard keeps standing guard from the
    /// tray. Actual exit goes through the tray menu.
    /// </summary>
    private void OnClose(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (AllowClose) return;

        e.Cancel = true;
        Hide();
    }

    public void ShowTab(int index)
    {
        _vm.SelectedTabIndex = index;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
}
