using System.Text.Json.Serialization;

namespace ClipGuard.Models;

public enum AutoClearMode
{
    Never,
    Seconds60,
    Minutes5
}

public enum RetentionMode
{
    Hours24,
    Days7,
    Days30
}

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; } = true;
    public bool LaunchMinimized { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool CrashReports { get; set; }

    /// <summary>Warn when dangerous content lands on the clipboard, swap or not.</summary>
    public bool WarnRiskyContent { get; set; } = true;

    /// <summary>Warn when a terminal or the Run dialog takes focus with a command on the clipboard.</summary>
    public bool WarnBeforeTerminalPaste { get; set; } = true;

    /// <summary>
    /// Replace a download-and-run command with an inert one, so pasting it after
    /// missing the warning does nothing. Always recoverable from the notification.
    /// </summary>
    public bool DefuseRiskyCommands { get; set; } = true;

    /// <summary>
    /// Whether previews of copied content are written to disk at all. On, they
    /// are stored DPAPI-encrypted; off, history keeps the app and verdict but
    /// never the content — the strongest option for anyone who copies secrets.
    /// </summary>
    public bool RememberCopiedContent { get; set; } = true;

    public AutoClearMode AutoClear { get; set; } = AutoClearMode.Seconds60;
    public RetentionMode Retention { get; set; } = RetentionMode.Days7;

    /// <summary>False until the first-run wizard completes.</summary>
    public bool SetupCompleted { get; set; }

    // Derived from the enums above — computed, never persisted.

    [JsonIgnore]
    public TimeSpan? AutoClearDelay => AutoClear switch
    {
        AutoClearMode.Seconds60 => TimeSpan.FromSeconds(60),
        AutoClearMode.Minutes5 => TimeSpan.FromMinutes(5),
        _ => null
    };

    [JsonIgnore]
    public TimeSpan RetentionWindow => Retention switch
    {
        RetentionMode.Hours24 => TimeSpan.FromHours(24),
        RetentionMode.Days30 => TimeSpan.FromDays(30),
        _ => TimeSpan.FromDays(7)
    };

    [JsonIgnore]
    public string RetentionText => Retention switch
    {
        RetentionMode.Hours24 => "24 hours",
        RetentionMode.Days30 => "30 days",
        _ => "7 days"
    };
}
