using System.Text.Json.Serialization;

namespace ClipGuard.Models;

public enum ClipboardAction
{
    Read,
    Write
}

/// <summary>
/// Why an event was interesting. Mirrors the categories shown in the mocks'
/// event log, mapped onto what a desktop-side monitor can actually determine.
/// </summary>
public enum EventCategory
{
    ClipboardRead,
    ClipboardWrite,
    /// <summary>Content replaced shortly after a user copy — the classic swap.</summary>
    ContentSubstitution,
    /// <summary>Substituted content looked like a shell command.</summary>
    CommandInjection,
    /// <summary>
    /// Dangerous content arrived without replacing anything — the ClickFix /
    /// fake-CAPTCHA pattern, where the page just hands you a command to paste.
    /// </summary>
    SuspiciousContent
}

public sealed class ClipboardEvent
{
    public string Id { get; init; } = string.Empty;

    /// <summary>Executable name, e.g. "chrome.exe". The mono-styled identifier in the UI.</summary>
    public string ProcessName { get; init; } = "unknown";

    /// <summary>Full image path, when it could be resolved.</summary>
    public string? ProcessPath { get; init; }

    /// <summary>FileDescription from version info, e.g. "Google Chrome". Used in tooltips.</summary>
    public string? FriendlyName { get; init; }

    public ClipboardAction Action { get; init; }
    public EventCategory Category { get; init; }

    /// <summary>Truncated, single-line preview of the clipboard content.</summary>
    public string ContentPreview { get; init; } = string.Empty;

    /// <summary>What the content was before it got swapped — only set on substitutions.</summary>
    public string? ReplacedPreview { get; init; }

    public bool Blocked { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>High-risk swaps get the sticky, red-header notification.</summary>
    public bool HighRisk { get; init; }

    /// <summary>
    /// A copy with the clipboard content stripped, for persisting when the user
    /// has turned off content history. The verdict is still worth keeping.
    /// </summary>
    public ClipboardEvent WithoutContent() => new()
    {
        Id = Id,
        ProcessName = ProcessName,
        ProcessPath = ProcessPath,
        FriendlyName = FriendlyName,
        Action = Action,
        Category = Category,
        ContentPreview = "(not stored)",
        ReplacedPreview = ReplacedPreview is null ? null : "(not stored)",
        PasteTarget = PasteTarget,
        Blocked = Blocked,
        Warned = Warned,
        Defused = Defused,
        UserOverrode = UserOverrode,
        HighRisk = HighRisk,
        Timestamp = Timestamp
    };

    // ── Display helpers used by the views — computed, never persisted ──────

    /// <summary>The grey action text next to the process name, per the mocks.</summary>
    [JsonIgnore]
    public string ActionText => (Action, Blocked) switch
    {
        (ClipboardAction.Write, true) => "wrote to clipboard — blocked",
        (ClipboardAction.Write, false) => "wrote to clipboard",
        _ => "read clipboard"
    };

    [JsonIgnore]
    public string CategoryText => Category switch
    {
        EventCategory.ContentSubstitution => "Content substitution",
        EventCategory.CommandInjection => "Command injection",
        EventCategory.SuspiciousContent => "Suspicious content",
        EventCategory.ClipboardRead => "Clipboard read",
        _ => "Clipboard write"
    };

    /// <summary>Flagged as dangerous but deliberately not blocked.</summary>
    public bool Warned { get; init; }

    /// <summary>Swapped for an inert placeholder; the original is recoverable.</summary>
    public bool Defused { get; init; }

    /// <summary>
    /// The user told us this was fine. Settable because it's recorded after the
    /// fact, and it's the only ground truth we have about false positives —
    /// every other signal is inference.
    /// </summary>
    public bool UserOverrode { get; set; }

    /// <summary>Set on paste warnings: where the command was about to go.</summary>
    public string? PasteTarget { get; init; }

    /// <summary>Per-row CTA in the History tab.</summary>
    [JsonIgnore]
    public string Cta => Blocked ? "Block app" : "Always allow";

    [JsonIgnore]
    public string RelativeTime
    {
        get
        {
            var d = DateTimeOffset.Now - Timestamp;
            if (d.TotalSeconds < 60) return "now";
            if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m";
            if (d.TotalHours < 24) return $"{(int)d.TotalHours}h";
            return $"{(int)d.TotalDays}d";
        }
    }
}
