using System.Windows;
using ClipGuard.Services;
using ClipGuard.Views;

namespace ClipGuard;

/// <summary>
/// Composition root. Wiring lives in AppHost so this stays a thin shell.
/// </summary>
public partial class App : Application
{
    // Session-scoped: one ClipGuard per logged-in user, not per machine.
    private const string InstanceMutexName = @"Local\ClipGuard.SingleInstance";
    private const string ShowWindowSignalName = @"Local\ClipGuard.ShowWindow";

    private AppHost? _host;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showWindowSignal;
    private bool _testMode;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Tidy away the copy we were relaunched from, if any.
        var movedFrom = ValueAfter(e.Args, "--moved-from");
        if (movedFrom is not null) Services.InstallLocation.CleanUpPrevious(movedFrom);

        // Deliberately before the single-instance mutex: relocating starts a new
        // copy of ClipGuard, and that copy must be able to take the mutex while
        // this process is still shutting down.
        if (!ResolveInstallLocation()) return;

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);

        if (!isFirstInstance)
        {
            // ClipGuard lives in the tray, so launching it again reads as "show me
            // the window" rather than "start a second copy" - two monitors would
            // fight over the clipboard.
            if (EventWaitHandle.TryOpenExisting(ShowWindowSignalName, out var running))
            {
                running.Set();
                running.Dispose();
            }

            Shutdown();
            return;
        }

        StartShowWindowListener();
        InstallCrashHandlers();

        _host = new AppHost(_testMode);
        _host.Start(e.Args);
    }

    /// <summary>
    /// True to carry on starting. False when this process has handed over to a
    /// copy somewhere permanent and should exit.
    /// </summary>
    private bool ResolveInstallLocation()
    {
        var problem = Services.InstallLocation.Diagnose();
        if (problem == LocationProblem.None) return true;

        var window = new RelocateWindow(problem);
        window.ShowDialog();

        if (window.Choice == RelocateChoice.Moved)
        {
            Shutdown();
            return false;
        }

        // Declined. Protection still runs for this session, but nothing may
        // write a startup entry pointing at a path that won't be there.
        _testMode = true;
        return true;
    }

    private static string? ValueAfter(string[] args, string flag)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>
    /// Without these, one unhandled exception silently kills the tray process
    /// and protection just stops - no window, no icon, no clue. For security
    /// software that failure mode is unacceptable: survive if we can, and say so
    /// plainly if we can't.
    /// </summary>
    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            if (_host?.Settings.CrashReports == true)
                CrashLog.Write(args.Exception, "UI thread");

            // A fault in one toast or view shouldn't take monitoring down with
            // it, so keep running - but stop claiming everything is fine.
            _host?.ReportFault();
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (_host?.Settings.CrashReports == true && args.ExceptionObject is Exception ex)
                CrashLog.Write(ex, "background thread");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            if (_host?.Settings.CrashReports == true)
                CrashLog.Write(args.Exception, "task");

            args.SetObserved();
        };
    }

    /// <summary>Waits for a later launch to ask us to surface the window.</summary>
    private void StartShowWindowListener()
    {
        _showWindowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowSignalName);

        var listener = new Thread(() =>
        {
            while (_showWindowSignal.WaitOne())
            {
                Dispatcher.Invoke(() => _host?.ShowMain());
            }
        })
        {
            IsBackground = true,
            Name = "ClipGuard.ShowWindowListener"
        };

        listener.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();

        _showWindowSignal?.Dispose();
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}
