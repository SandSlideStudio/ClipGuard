using System.Windows;
using System.Windows.Media;
using ClipGuard.Models;
using ClipGuard.Services;

namespace ClipGuard.Views;

/// <summary>
/// The four-step first-run wizard. Step 2 gates "I agree" on the
/// acceptance checkbox; step 4's body summarises whatever was chosen on step 3.
/// </summary>
public partial class SetupWindow : Window
{
    private static readonly string[] StepLabels = ["Welcome", "Terms", "Preferences", "Done"];

    private static readonly string[] Titles =
    [
        "Let's get ClipGuard standing guard.",
        "The legal bit. It's short, promise.",
        "A few choices before we start.",
        "All set. Copy things with confidence."
    ];

    private readonly AppSettings _settings;
    private int _step;

    /// <summary>True once the user finished the wizard rather than bailing out.</summary>
    public bool Completed { get; private set; }

    public SetupWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        PrefStartup.IsChecked = settings.StartWithWindows;
        PrefMinimized.IsChecked = settings.LaunchMinimized;
        PrefToasts.IsChecked = settings.ShowNotifications;
        PrefCrash.IsChecked = settings.CrashReports;

        Render();
    }

    private bool CanAdvance => _step != 1 || AcceptCheckBox.IsChecked == true;

    private string BodyFor(int step) => step switch
    {
        0 => "Apps and web pages can read and rewrite your clipboard: a wallet address, a command, "
             + "a link, sometimes swapping it for something nastier. ClipGuard sits in your system tray "
             + "and tells you the moment something touches it. Setup takes under a minute.",
        1 => "Scroll through, tick the box, and we're off. Summary: everything runs locally, we never "
             + "see your clipboard, and no security software is perfect.",
        2 => "You can change any of these later in Settings.",
        _ => BuildSummary()
    };

    /// <summary>Step 4 reflects the toggles the user actually set.</summary>
    private string BuildSummary()
    {
        var startsWith = PrefStartup.IsChecked == true ? "with Windows" : "when you launch it";
        var tucked = PrefMinimized.IsChecked == true ? ", tucked into the tray" : "";
        var notifications = PrefToasts.IsChecked == true ? "on" : "set to silent log";

        return $"ClipGuard starts {startsWith}{tucked}. Notifications are {notifications}.";
    }

    private void Render()
    {
        StepLabel.Text = $"STEP {_step + 1} OF 4 · {StepLabels[_step].ToUpperInvariant()}";
        StepTitle.Text = Titles[_step];
        StepBody.Text = BodyFor(_step);

        TermsPanel.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        PrefsPanel.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        DonePanel.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;

        BackButton.Content = _step == 0 ? "Exit setup" : "Back";
        NextButton.Content = _step switch
        {
            0 => "Let's go",
            1 => "I agree",
            2 => "Finish setup",
            _ => "Open ClipGuard"
        };

        // Disabled at 45% opacity until the terms are accepted.
        NextButton.IsEnabled = CanAdvance;

        var reached = (Brush)FindResource("Accent");
        var pending = (Brush)FindResource("Neutral300");
        Bar0.Background = reached;
        Bar1.Background = _step >= 1 ? reached : pending;
        Bar2.Background = _step >= 2 ? reached : pending;
        Bar3.Background = _step >= 3 ? reached : pending;
    }

    private void OnAcceptChanged(object sender, RoutedEventArgs e) =>
        NextButton.IsEnabled = CanAdvance;

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_step == 0)
        {
            Close();
            return;
        }

        _step--;
        Render();
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (!CanAdvance) return;

        if (_step == 3)
        {
            Completed = true;
            Close();
            return;
        }

        // Capture the preference toggles as we leave step 3.
        if (_step == 2) CommitPreferences();

        _step++;
        Render();
    }

    private void CommitPreferences()
    {
        _settings.StartWithWindows = PrefStartup.IsChecked == true;
        _settings.LaunchMinimized = PrefMinimized.IsChecked == true;
        _settings.ShowNotifications = PrefToasts.IsChecked == true;
        _settings.CrashReports = PrefCrash.IsChecked == true;

        StartupService.Apply(_settings.StartWithWindows, _settings.LaunchMinimized);
    }

    private void OnExitSetup(object sender, RoutedEventArgs e) => Close();
}
