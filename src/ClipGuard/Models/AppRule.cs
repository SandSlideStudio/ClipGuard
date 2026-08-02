using System.Text.Json.Serialization;

namespace ClipGuard.Models;

public enum RuleKind
{
    AlwaysAllow,
    Blocked
}

/// <summary>
/// A per-application rule. The mocks call this tab "Site rules"; with
/// process-level attribution the unit is an executable rather than a domain.
/// </summary>
public sealed class AppRule
{
    public string ProcessName { get; init; } = string.Empty;
    public RuleKind Rule { get; set; }
    public DateTimeOffset LastEvent { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public string RuleText => Rule == RuleKind.Blocked ? "Blocked" : "Always allow";

    [JsonIgnore]
    public string LastEventText
    {
        get
        {
            var d = DateTimeOffset.Now - LastEvent;
            if (d.TotalMinutes < 1) return "just now";
            if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} min ago";
            if (d.TotalHours < 24) return $"{(int)d.TotalHours} hr ago";
            return $"{(int)d.TotalDays} d ago";
        }
    }
}
