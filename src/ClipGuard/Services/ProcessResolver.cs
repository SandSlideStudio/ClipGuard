using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace ClipGuard.Services;

public readonly record struct ProcessIdentity(string Name, string? Path, string? FriendlyName)
{
    public static ProcessIdentity Unknown => new("unknown", null, null);
}

/// <summary>
/// Turns a window handle into something we can name in the UI. Results are
/// cached per image path - version-info lookups hit disk.
/// </summary>
public sealed class ProcessResolver
{
    private readonly ConcurrentDictionary<string, string?> _friendlyNames = new(StringComparer.OrdinalIgnoreCase);

    public ProcessIdentity Resolve(IntPtr hwnd)
    {
        var pid = Win32.GetProcessIdForWindow(hwnd);
        if (pid == 0) return ProcessIdentity.Unknown;

        var path = Win32.GetProcessImagePath(pid);
        if (path is null)
        {
            // Fall back to the managed API - works for same-bitness, same-user processes.
            try
            {
                using var p = Process.GetProcessById((int)pid);
                return new ProcessIdentity($"{p.ProcessName}.exe", null, null);
            }
            catch
            {
                return ProcessIdentity.Unknown;
            }
        }

        return new ProcessIdentity(Path.GetFileName(path), path, GetFriendlyName(path));
    }

    private string? GetFriendlyName(string path) =>
        _friendlyNames.GetOrAdd(path, static p =>
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(p);
                return string.IsNullOrWhiteSpace(info.FileDescription) ? null : info.FileDescription;
            }
            catch
            {
                return null;
            }
        });
}
