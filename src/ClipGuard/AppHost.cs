using System.Windows;
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
    private readonly MainViewModel _vm;

    private MainWindow? _main;
    private TrayFlyoutWindow? _flyout;

    public AppHost(bool testMode = false)
    {
        _protection = new ProtectionService { IsTestMode = testMode };
        _vm = new MainViewModel(_protection);
        _notifications = new NotificationService(_protection);
        _pasteGuard = new PasteGuardService(_protection);
        _tray = new TrayService(_protection);

        _protection.NotificationRequested += (_, request) => _notifications.Show(request);

        _tray.OpenRequested += (_, _) => ShowMain();
        _tray.FlyoutRequested += (_, _) => ToggleFlyout();
        _tray.ExitRequested += (_, _) => Shutdown();
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
