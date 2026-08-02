using ClipGuard.Models;

namespace ClipGuard.Services;

/// <summary>Everything a detection decision depends on.</summary>
public sealed record DetectionInputs
{
    /// <summary>What just landed on the clipboard. Null for images and files.</summary>
    public string? Text { get; init; }

    /// <summary>What we believed the user actually meant to have.</summary>
    public string? Previous { get; init; }

    public string WriterName { get; init; } = "unknown";

    /// <summary>Landed inside the swap window of the previous change.</summary>
    public bool ArrivedRapidly { get; init; }

    /// <summary>The writing process isn't the one the user is looking at.</summary>
    public bool FromBackgroundApp { get; init; }

    public RuleKind? Rule { get; init; }
    public bool IsPaused { get; init; }
    public bool WarnRiskyContentEnabled { get; init; } = true;
    public bool DefuseEnabled { get; init; } = true;

    /// <summary>Download hosts the user has vouched for.</summary>
    public IReadOnlyCollection<string> TrustedSources { get; init; } = [];
}

/// <summary>What to do about it.</summary>
public sealed record DetectionOutcome
{
    public bool Block { get; init; }
    public bool Warn { get; init; }
    public bool Defuse { get; init; }

    /// <summary>
    /// A blocked swap severe enough for the sticky card with the before/after
    /// diff. Distinct from <see cref="HighRisk"/>: a ClickFix lure is high risk
    /// but nothing was swapped, so showing "You copied / They wrote" would be
    /// inventing a comparison that never happened.
    /// </summary>
    public bool HighRiskSwap { get; init; }

    /// <summary>Severity recorded on the event — a severe swap, or a lure.</summary>
    public bool HighRisk { get; init; }

    public EventCategory Category { get; init; }
}

/// <summary>
/// The swap / warn / defuse decision, as a pure function.
///
/// Extracted from ProtectionService so it can actually be tested: in place it
/// was tangled with clipboard access, dispatcher timers and JSON persistence,
/// which meant the most consequential logic in the product had no coverage at
/// all. Every bug found during development lived within a few lines of here.
/// </summary>
public static class DetectionPolicy
{
    private static readonly string[] Browsers =
    [
        "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe",
        "vivaldi.exe", "arc.exe", "iexplore.exe", "chromium.exe", "librewolf.exe"
    ];

    public static bool IsBrowser(string processName) =>
        Browsers.Contains(processName, StringComparer.OrdinalIgnoreCase);

    public static DetectionOutcome Evaluate(DetectionInputs input)
    {
        var text = input.Text;
        var previous = input.Previous;

        var changed = !string.Equals(previous, text, StringComparison.Ordinal);
        var riskShaped = RiskAnalyzer.Classify(text) != RiskKind.None;

        // A swap that keeps the *shape* of what you copied — an address for a
        // different address, a command for a different command — is the
        // signature of the attack, and the only signal strong enough to act on
        // when the writer is the focused app. That's the case that matters: a
        // malicious page rewriting the clipboard inside your own browser.
        var sameKindSwap = changed
                           && RiskAnalyzer.Classify(previous) != RiskKind.None
                           && RiskAnalyzer.Classify(previous) == RiskAnalyzer.Classify(text);

        // Substitution needs prior content plus two corroborating signals — any
        // one alone produces far too many false positives in normal use.
        // Non-text content is logged but never judged: we can't read it, so
        // there's nothing to reason about.
        var substitution = changed
                           && !string.IsNullOrEmpty(previous)
                           && !string.IsNullOrEmpty(text)
                           && ((input.FromBackgroundApp && (riskShaped || input.ArrivedRapidly))
                               || (input.ArrivedRapidly && sameKindSwap));

        var blockedByRule = input.Rule == RuleKind.Blocked;
        var allowedByRule = input.Rule == RuleKind.AlwaysAllow;

        // A command that only fetches from a vendor's own install endpoint is an
        // install step, not a payload. Exempt from blocking as well as warning:
        // silently reverting someone's install command is worse than asking.
        var trustedSource = RiskAnalyzer.IsTrustedInstallSource(text, input.TrustedSources);

        var block = !input.IsPaused
                    && (blockedByRule
                        || (substitution && !allowedByRule && !trustedSource));

        // Nothing was replaced, but something dangerous just landed — the
        // ClickFix shape, where a page hands you a command to paste. Warn
        // without blocking: you may have copied it deliberately.
        var warn = !block
                   && !input.IsPaused
                   && !allowedByRule
                   && !trustedSource
                   && input.WarnRiskyContentEnabled
                   && WarrantsContentWarning(text, input.WriterName);

        // Only commands are defused. A wallet address is data, not a payload,
        // and swapping what someone is about to paste into a payment field
        // would be its own hazard.
        var defuse = warn
                     && input.DefuseEnabled
                     && RiskAnalyzer.Classify(text) == RiskKind.ShellCommand;

        var highRiskSwap = block && RiskAnalyzer.IsHighRiskSubstitution(previous, text);

        return new DetectionOutcome
        {
            Block = block,
            Warn = warn,
            Defuse = defuse,
            HighRiskSwap = highRiskSwap,
            HighRisk = highRiskSwap || RiskAnalyzer.IsVerificationLure(text),
            Category = block ? RiskAnalyzer.CategoryFor(text)
                     : warn ? EventCategory.SuspiciousContent
                     : EventCategory.ClipboardWrite
        };
    }

    /// <summary>
    /// Whether content is worth warning about on its own merits. Shell commands
    /// always are. Wallet addresses only when a browser put them there — copying
    /// an address from your own wallet is routine, a web page placing one on
    /// your clipboard unprompted is not.
    /// </summary>
    private static bool WarrantsContentWarning(string? text, string writerName)
    {
        // Belt and braces: our own stand-in classifies as harmless anyway, but
        // never let defusing feed itself.
        if (RiskAnalyzer.IsDefusedReplacement(text)) return false;

        return RiskAnalyzer.Classify(text) switch
        {
            RiskKind.ShellCommand => true,
            RiskKind.CryptoAddress => IsBrowser(writerName),
            _ => false
        };
    }
}
