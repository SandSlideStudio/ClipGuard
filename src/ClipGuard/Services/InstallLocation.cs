using System.Diagnostics;
using System.IO;

namespace ClipGuard.Services;

/// <summary>Why a location is unsuitable, phrased for the user.</summary>
public enum LocationProblem
{
    None,
    Downloads,
    Temporary,
    RemovableDrive,
    NetworkDrive
}

/// <summary>
/// Where ClipGuard is running from, and whether that will hold.
///
/// "Start with Windows" records the executable's current path in the registry.
/// Run from Downloads, enable it, then tidy that folder months later and
/// protection stops with no window, no icon and no error. A clipboard guard
/// that silently isn't there is the worst failure this app has, so a volatile
/// location is treated as a blocking problem rather than a warning.
/// </summary>
public static class InstallLocation
{
    public static string CurrentExecutable { get; } =
        Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? string.Empty;

    /// <summary>Where we offer to move to. Per-user, so no admin rights needed.</summary>
    public static string SuggestedDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "ClipGuard");

    public static LocationProblem Diagnose() => Diagnose(CurrentExecutable);

    public static LocationProblem Diagnose(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return LocationProblem.None;

        var directory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(directory)) return LocationProblem.None;

        if (IsUnder(directory, DownloadsFolder())) return LocationProblem.Downloads;
        if (IsUnder(directory, Path.GetTempPath())) return LocationProblem.Temporary;

        // A removable drive can simply be unplugged; a network path can vanish
        // or change letter. Either way the startup entry rots.
        try
        {
            var root = Path.GetPathRoot(directory);
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                if (drive.DriveType == DriveType.Removable) return LocationProblem.RemovableDrive;
                if (drive.DriveType == DriveType.Network) return LocationProblem.NetworkDrive;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // An undiagnosable drive is not grounds for blocking startup.
        }

        return LocationProblem.None;
    }

    /// <summary>
    /// Downloads has no SpecialFolder entry, so it is resolved from the shell's
    /// known-folder path with a fallback for redirected profiles.
    /// </summary>
    private static string DownloadsFolder()
    {
        try
        {
            var known = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(known, "Downloads");
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsUnder(string candidate, string parent)
    {
        if (string.IsNullOrWhiteSpace(parent)) return false;

        try
        {
            var a = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar);
            var b = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);

            return a.Equals(b, StringComparison.OrdinalIgnoreCase)
                   || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            return false;
        }
    }

    public static string Describe(LocationProblem problem) => problem switch
    {
        LocationProblem.Downloads => "your Downloads folder",
        LocationProblem.Temporary => "a temporary folder",
        LocationProblem.RemovableDrive => "a removable drive",
        LocationProblem.NetworkDrive => "a network drive",
        _ => "this location"
    };

    public static string ExplainRisk(LocationProblem problem) => problem switch
    {
        LocationProblem.Downloads =>
            "Files here get cleaned up. If ClipGuard is deleted or moved, it stops "
            + "protecting you at the next sign-in and nothing tells you.",
        LocationProblem.Temporary =>
            "Windows clears this folder. ClipGuard will disappear from it, and stop "
            + "protecting you at the next sign-in, with no warning.",
        LocationProblem.RemovableDrive =>
            "Unplug this drive and ClipGuard stops protecting you at the next "
            + "sign-in, with no warning.",
        LocationProblem.NetworkDrive =>
            "If this share is unavailable or changes drive letter, ClipGuard stops "
            + "protecting you at the next sign-in, with no warning.",
        _ => string.Empty
    };

    /// <summary>
    /// Copies the running executable into <paramref name="directory"/> and starts
    /// it there. Returns the new path, or null if the copy failed.
    /// </summary>
    /// <remarks>
    /// The new process is told where it came from so it can delete the original
    /// once this one has exited; a running executable can't remove itself.
    /// </remarks>
    public static string? MoveTo(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);

            var destination = Path.Combine(directory, Path.GetFileName(CurrentExecutable));

            if (!string.Equals(destination, CurrentExecutable, StringComparison.OrdinalIgnoreCase))
                File.Copy(CurrentExecutable, destination, overwrite: true);

            Process.Start(new ProcessStartInfo(destination)
            {
                UseShellExecute = true,
                Arguments = $"--moved-from \"{CurrentExecutable}\""
            });

            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Deletes the copy we were started from, once it has released its lock.</summary>
    public static void CleanUpPrevious(string previousPath)
    {
        if (string.IsNullOrWhiteSpace(previousPath) || !File.Exists(previousPath)) return;

        Task.Run(async () =>
        {
            // The old process is exiting as we start; give it room to let go.
            for (var attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(500);
                try
                {
                    File.Delete(previousPath);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Still locked, or not ours to remove. Leaving a stray copy
                    // behind is untidy but harmless.
                }
            }
        });
    }
}
