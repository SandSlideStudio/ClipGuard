using System.IO;
using System.Text;

namespace ClipGuard.Services;

/// <summary>
/// Local crash log. Deliberately local: the product's promise is that nothing
/// leaves the machine, and a crash report that phoned home would be the one
/// exception nobody agreed to. The setting is worded to match what this does.
///
/// Clipboard content is never written here — only the exception itself.
/// </summary>
public static class CrashLog
{
    private const int MaxLogFiles = 10;

    public static string Directory { get; } = Path.Combine(Storage.Root, "logs");

    public static void Write(Exception exception, string origin)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            var path = Path.Combine(Directory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");

            var text = new StringBuilder()
                .AppendLine($"ClipGuard {typeof(CrashLog).Assembly.GetName().Version}")
                .AppendLine($"When    : {DateTimeOffset.Now:u}")
                .AppendLine($"Origin  : {origin}")
                .AppendLine($"OS      : {Environment.OSVersion}")
                .AppendLine()
                .AppendLine(exception.ToString())
                .ToString();

            File.WriteAllText(path, text);
            Prune();
        }
        catch
        {
            // A failure to log a crash must never cause another one.
        }
    }

    private static void Prune()
    {
        try
        {
            var files = new DirectoryInfo(Directory)
                .GetFiles("crash-*.log")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(MaxLogFiles);

            foreach (var file in files) file.Delete();
        }
        catch
        {
        }
    }
}
