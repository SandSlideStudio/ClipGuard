using System.Diagnostics;
using System.Windows;

namespace ClipGuard.Views;

public partial class AboutView : UserControl
{
    public AboutView() => InitializeComponent();

    private void OnCheckUpdates(object sender, RoutedEventArgs e) =>
        MessageBox.Show("You're on v1.4.2 — the latest.", "ClipGuard",
            MessageBoxButton.OK, MessageBoxImage.Information);

    private void OnOpenSite(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://sandslide.studio") { UseShellExecute = true });
        }
        catch
        {
            // No default browser, or the shell refused — nothing worth interrupting for.
        }
    }
}
