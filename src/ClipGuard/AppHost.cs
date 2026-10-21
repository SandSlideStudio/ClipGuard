using System.Windows;
using System.Windows.Threading;
using ClipGuard.Services;
using ClipGuard.ViewModels;
using ClipGuard.Views;

namespace ClipGuard;

/// <summary>
/// Startup wiring: first-run wizard, protection service, tray, notifications.
/// The app has no main window in the usual sense - it lives in the tray, and
/// closing the window only hides it.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly ProtectionService _protection;
    private readonly NotificationService _notifications;
    private readonly PasteGuardService _pasteGuard;
    private readonly TrayService _tray;
    private readonly UpdateService _updates;
    private readonly MainViewModel _vm;

    /// <summary>Test mode means a volatile install location; it reaches no network.</summary>
    private readonly bool _testMode;

    private MainWindow? _main;
    private TrayFlyoutWindow? _flyout;

    public AppHost(bool testMode = false)
    {
        _testMode = testMode;
        _protection = new ProtectionService { IsTestMode = testMode };
        _updates = new UpdateService();
        _vm = new MainViewModel(_protection, _updates);
        _notifications = new NotificationService(_protection);
        _pasteGuard = new PasteGuardService(_protection);
        _tray = new TrayService(_protection);

        _protection.NotificationRequested += (_, request) => _notifications.Show(request);

        _tray.OpenRequested += (_, _) => ShowMain();
        _tray.FlyoutRequested += (_, _) => ToggleFlyout();
        _tray.ExitRequested += (_, _) => Shutdown();
        _tray.UpdateRequested += (_, _) => OpenReleasePage();

        // Once per release rather than once per launch: the service remembers
        // what it has already announced.
        _updates.UpdateFirstSeen += (_, update) => _tray.ShowUpdateAvailable(update);
    }

    public void Start(string[] args)
    {
        var justFinishedSetup = false;

        if (!_protection.Settings.SetupCompleted)
        {
            var setup = new SetupWindow(_protection.Settings);
            setup.ShowDialog();

            if (!setup.Completed)
            {
                // Bailed out of first-run setup - don't leave a tray icon behind.
                Shutdown();
                return;
            }

            _protection.Settings.SetupCompleted = true;
            _protection.SaveSettings();
            justFinishedSetup = true;
        }

        _protection.Start();
        _pasteGuard.Start();
        StartUpdateCheck();

        // The wizard's final button says "Open ClipGuard", so it opens ClipGuard -
        // the launch-minimized preference applies to later launches, not this one.
        if (justFinishedSetup)
        {
            ShowMain();
            return;
        }

        var startMinimized = args.Contains("--minimized", StringComparer.OrdinalIgnoreCase)
                             || _protection.Settings.LaunchMinimized;

        if (!startMinimized) ShowMain();
    }

    /// <summary>
    /// Asks GitHub about newer releases, a few seconds after startup so
    /// protection is running first and the setup and relocation flows are not
    /// racing a network call. Nothing happens here in test mode, and nothing
    /// happens at all if the user turned it off.
    /// </summary>
    private void StartUpdateCheck()
    {
        if (_testMode) return;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };

        timer.Tick += async (_, _) =>
        {
            timer.Stop();

            try
            {
                await _updates.CheckAsync(_protection.Settings.CheckForUpdates);
            }
            catch
            {
                // CheckAsync swallows the expected failures itself. Anything
                // left is still not worth taking the tray app down for.
            }
        };

        timer.Start();
    }

    /// <summary>Opens the release page. ClipGuard never downloads an update itself.</summary>
    private void OpenReleasePage()
    {
        var url = _updates.Available?.PageUrl ?? UpdateService.ReleasesPage;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // No default browser, or the shell refused.
        }
    }

    /// <summary>Settings, for the crash handler's opt-in check.</summary>
    public Models.AppSettings Settings => _protection.Settings;

    /// <summary>A fault happened; stop the UI claiming protection is fine.</summary>
    public void ReportFault() => _protection.MarkFaulted();

    /// <summary>Surfaces the main window. Also called when a second launch is folded in.</summary>
    public void ShowMain()
    {
        _main ??= new MainWindow(_vm);
        _main.ShowTab(_main.IsVisible ? _vm.SelectedTabIndex : 0);
    }

    private void ToggleFlyout()
    {
        _flyout ??= CreateFlyout();

        if (_flyout.IsVisible)
        {
            _flyout.Hide();
            return;
        }

        _flyout.ShowNearTray();
    }

    private TrayFlyoutWindow CreateFlyout()
    {
        var flyout = new TrayFlyoutWindow(_vm);
        flyout.OpenAppRequested += (_, _) => ShowMain();
        return flyout;
    }

    private void Shutdown()
    {
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _notifications.CloseAll();
        _pasteGuard.Dispose();
        _tray.Dispose();
        _protection.Dispose();

        _flyout?.Close();

        if (_main is not null)
        {
            // OnClosing cancels and hides; force it down on real shutdown.
            _main.AllowClose = true;
            _main.Close();
        }
    }
}
