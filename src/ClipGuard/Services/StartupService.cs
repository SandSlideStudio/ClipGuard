using System.Diagnostics;
using Microsoft.Win32;

namespace ClipGuard.Services;

/// <summary>
/// Backs the "Start ClipGuard with Windows" toggle via the per-user Run key.
/// Per-user only - no elevation, no machine-wide state.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipGuard";

    private static string? ExecutablePath =>
        Process.GetCurrentProcess().MainModule?.FileName;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(bool enabled, bool launchMinimized)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return;
            }

            var exe = ExecutablePath;
            if (exe is null) return;

            var command = launchMinimized ? $"\"{exe}\" --minimized" : $"\"{exe}\"";
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        catch
        {
            // A locked-down registry shouldn't break the settings screen.
        }
    }
}
