using System.Diagnostics;
using System.Windows;

namespace ClipGuard.Views;

public partial class AboutView : UserControl
{
    public AboutView() => InitializeComponent();

    /// <summary>
    /// No update service exists yet, so this reports honestly rather than
    /// pretending to reach a server. The mark animates while it works - when a
    /// real check lands here, the waiting state is already built.
    /// </summary>
    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Checking…";
        Mark.IsAnimating = true;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1.4));
            UpdateStatus.Text = "You're on v1.0.0. No update service yet.";
        }
        finally
        {
            Mark.IsAnimating = false;
            UpdateButton.IsEnabled = true;
        }
    }

    private void OnOpenSite(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://sandslide.studio") { UseShellExecute = true });
        }
        catch
        {
            // No default browser, or the shell refused - nothing worth interrupting for.
        }
    }
}
