using System.Diagnostics;
using System.Windows;
using ClipGuard.Services;
using ClipGuard.ViewModels;

namespace ClipGuard.Views;

public partial class AboutView : UserControl
{
    public AboutView() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>
    /// A check the user asked for, so it ignores both the daily interval and
    /// the Settings toggle: someone pressing the button is entitled to an
    /// answer. The status text and the mark's animation are bound to the
    /// service, so there is nothing to update here.
    /// </summary>
    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        UpdateButton.IsEnabled = false;

        try
        {
            await Vm.Updates.CheckAsync(
                enabled: Vm.Protection.Settings.CheckForUpdates, userInitiated: true);
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }

    private void OnOpenReleases(object sender, RoutedEventArgs e) =>
        Open(Vm?.Updates.Available?.PageUrl ?? UpdateService.ReleasesPage);

    private void OnOpenSite(object sender, RoutedEventArgs e) => Open("https://sandslide.studio");

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // No default browser, or the shell refused - nothing worth interrupting for.
        }
    }
}
