using System.Text.RegularExpressions;

namespace ClipGuard.Services;

public enum RiskKind
{
    None,
    /// <summary>A wallet address - the classic swap target.</summary>
    CryptoAddress,
    /// <summary>A shell command, especially a download-and-execute pipeline.</summary>
    ShellCommand
}

/// <summary>
/// Shape analysis for clipboard content. Used two ways: to decide whether a
/// substitution is high-risk (sticky red notification), and to spot the case
/// where a copied wallet address is swapped for a *different* wallet address.
/// </summary>
public static partial class RiskAnalyzer
{
    // ── Wallet addresses ──────────────────────────────────────────────────
    [GeneratedRegex(@"^bc1[a-z0-9]{25,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex BitcoinBech32();

    [GeneratedRegex(@"^[13][a-km-zA-HJ-NP-Z1-9]{25,34}$", RegexOptions.CultureInvariant)]
    private static partial Regex BitcoinLegacy();

    [GeneratedRegex(@"^0x[a-fA-F0-9]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex Ethereum();

    [GeneratedRegex(@"^4[0-9AB][1-9A-HJ-NP-Za-km-z]{93}$", RegexOptions.CultureInvariant)]
    private static partial Regex Monero();

    [GeneratedRegex(@"^[LM][a-km-zA-HJ-NP-Z1-9]{26,33}$", RegexOptions.CultureInvariant)]
    private static partial Regex Litecoin();

    [GeneratedRegex(@"^T[1-9A-HJ-NP-Za-km-z]{33}$", RegexOptions.CultureInvariant)]
    private static partial Regex Tron();

    // ── Download-and-execute pipelines ────────────────────────────────────
    [GeneratedRegex(@"\b(curl|wget|iwr|Invoke-WebRequest)\b[^|]*\|\s*(sudo\s+)?(ba|z|k|fi)?sh\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurlPipeShell();

    [GeneratedRegex(@"\b(iex|Invoke-Expression)\b|powershell(\.exe)?\s+.*-e(nc|ncodedcommand)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PowerShellExec();

    [GeneratedRegex(@"\bbase64\s+(-d|--decode)\b[^|]*\|\s*(ba)?sh\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Base64PipeShell();

    [GeneratedRegex(@"\brm\s+-rf\s+[~/]|\bmkfs\b|\bdd\s+if=.*of=/dev/",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DestructiveShell();

    // ── Windows living-off-the-land runners ───────────────────────────────
    // The ClickFix / fake-CAPTCHA playbook: the page never swaps anything, it
    // just parks one of these on the clipboard and tells you to press Win+R.

    [GeneratedRegex(@"\b(mshta|regsvr32|rundll32|certutil|bitsadmin|msiexec|wscript|cscript|forfiles|conhost)\b[^\r\n]*\b(https?://|/i\b|-urlcache|/transfer|javascript:|--headless)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LivingOffTheLand();

    [GeneratedRegex(@"\b(irm|iwr|Invoke-RestMethod|Invoke-WebRequest|curl|wget)\b[^\r\n]*\|\s*(iex|Invoke-Expression)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PowerShellDownloadRun();

    [GeneratedRegex(@"powershell(\.exe)?\s+[^\r\n]*-(w|windowstyle)\s+hidden|powershell(\.exe)?\s+[^\r\n]*-nop\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HiddenPowerShell();

    /// <summary>
    /// The tell that turns a suspicious command into a certain one: attackers
    /// append a fake verification note so the payload looks like a CAPTCHA step.
    /// </summary>
    [GeneratedRegex(@"(I am not a robot|not a robot|reCAPTCHA|CAPTCHA|Cloudflare|Ray ID|human verification|verify (you|that you)( are|'re) human|Verification ID)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VerificationLure();

    /// <summary>Leading whitespace/newlines are a known trick to hide a pasted command.</summary>
    [GeneratedRegex(@"^[\s\r\n]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex PaddedStart();

    // ── Defusing ──────────────────────────────────────────────────────────
    //
    // A replacement that obviously reads "your clipboard was tampered with"
    // tells someone mid-attack that the paste failed, and the natural next step
    // is to copy the command again or turn the guard off. So the replacement is
    // shaped like an ordinary command and exits silently: the "verification
    // step" appears to complete, nothing happens, and the attack dies quietly.
    // The explanation lives in the notification and History instead.
    //
    // HARD RULE: templates are fixed constants, selected by the *shape* of the
    // original. Never build a replacement by transforming attacker-supplied
    // text - splicing fragments risks putting live payload back on the
    // clipboard, which is the exact failure this feature exists to prevent.

    /// <summary>
    /// Windows shape - the ClickFix mainstream (Run dialog, cmd, PowerShell).
    /// Write-Verbose prints nothing unless verbose output is switched on, so
    /// this exits 0 in silence. In the Run dialog a console flashes and closes,
    /// which reads as "it ran" rather than as an error.
    /// </summary>
    /// <remarks>
    /// Contains no <c>$</c> and no backtick, and that is load-bearing. Pasting
    /// into a PowerShell prompt makes the *outer* shell expand the double-quoted
    /// argument before the child process sees it, so a <c>$var</c> here would
    /// vanish and leave a syntax error on screen - precisely the "your paste
    /// failed" tell the disguise exists to avoid.
    /// </remarks>
    private const string DefusedPowerShell =
        "powershell -NoProfile -Command \"Write-Verbose 'Clipboard entry expired. Nothing to run.'\"";

    /// <summary>
    /// Unix shape, for a <c>curl … | sh</c> style original. No redirection:
    /// <c>&gt; /dev/null</c> would create a stray <c>dev\null</c> file if this
    /// landed in PowerShell instead of a real shell.
    /// </summary>
    private const string DefusedShell =
        "echo 'Clipboard entry expired. Nothing to run.'";

    static RiskAnalyzer()
    {
        // A '$' or backtick in a stand-in gets expanded by the outer shell when
        // it's pasted into a PowerShell prompt, mangling the command into a
        // visible error. Fail loudly in development rather than shipping a
        // disguise that announces itself.
        foreach (var template in new[] { DefusedPowerShell, DefusedShell })
        {
            System.Diagnostics.Debug.Assert(
                !template.Contains('$') && !template.Contains('`'),
                $"Defused stand-in must not contain '$' or a backtick: {template}");
        }
    }

    /// <summary>
    /// Picks the inert stand-in that best resembles the original's shape.
    /// Takes the payload only to classify it - never to copy from it.
    /// </summary>
    public static string DefusedReplacementFor(string? original)
    {
        if (string.IsNullOrWhiteSpace(original)) return DefusedPowerShell;

        // A pipe into a unix shell is the one common non-Windows shape.
        return CurlPipeShell().IsMatch(original) || Base64PipeShell().IsMatch(original)
            ? DefusedShell
            : DefusedPowerShell;
    }

    /// <summary>Whether text is one of our own stand-ins, so we never re-flag it.</summary>
    public static bool IsDefusedReplacement(string? text) =>
        text is DefusedPowerShell or DefusedShell;

    // ── Telling a real installer from a payload ───────────────────────────
    //
    // `irm https://ollama.com/install.ps1 | iex` and a ClickFix payload are the
    // same shape. No pattern can separate them, because as text they aren't
    // different - the difference is where the code comes from. So the one
    // durable signal is the download host.

    [GeneratedRegex(@"https?://([A-Za-z0-9][A-Za-z0-9\-.]*[A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlHost();

    /// <summary>
    /// Vendor-controlled install endpoints. Deliberately narrow: only hosts
    /// whose owner decides what is served there.
    ///
    /// Redirectors and user-content hosts are excluded on purpose - aka.ms,
    /// raw.githubusercontent.com and gist.github.com would each let an attacker
    /// serve a payload from an allowlisted name.
    /// </summary>
    private static readonly HashSet<string> KnownInstallHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "ollama.com", "get.docker.com", "download.docker.com",
        "sh.rustup.rs", "win.rustup.rs", "astral.sh",
        "bun.sh", "deno.land", "scoop.sh", "get.scoop.sh",
        "community.chocolatey.org", "chocolatey.org",
        "install.python-poetry.org", "get.pnpm.io",
        "deb.nodesource.com", "nodejs.org", "python.org",
        "starship.rs", "get.helm.sh", "packages.microsoft.com",
        "apt.releases.hashicorp.com"
    };

    /// <summary>Every http(s) host referenced by the text.</summary>
    public static IReadOnlyList<string> ExtractHosts(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        return UrlHost().Matches(text)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// True when every host the command fetches from is one we trust, so the
    /// command is an install step rather than a payload.
    ///
    /// <paramref name="alsoTrusted"/> carries hosts the user has personally
    /// vouched for. A verification lure overrides all of this: no legitimate
    /// installer arrives wearing a fake CAPTCHA note, whoever hosts it.
    /// </summary>
    public static bool IsTrustedInstallSource(string? text, IEnumerable<string>? alsoTrusted = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (IsVerificationLure(text)) return false;

        var hosts = ExtractHosts(text);
        if (hosts.Count == 0) return false;

        return hosts.All(h => KnownInstallHosts.Contains(h)
                              || (alsoTrusted?.Contains(h, StringComparer.OrdinalIgnoreCase) ?? false));
    }


    // ── Command, or document that quotes one ──────────────────────────────
    //
    // Every pattern above answers "does this text contain a dangerous
    // command". That is the wrong question on its own. A README, a chat log or
    // a page of install instructions all contain command lines, and treating
    // one as a payload is this app's most damaging false positive: it accuses
    // the user of being attacked while they copy their own writing, and
    // defusing replaces pages of their text with a one-line stand-in.
    //
    // The shape is the discriminator. A ClickFix payload has to be something a
    // victim can paste and run in one go, and the Run dialog takes a single
    // line, so the attack is essentially single-line by construction. Prose
    // wrapped around a payload is not a workable attack: Win+R would take only
    // the first line, and in a terminal every prose line would error on screen,
    // which is the "your paste failed" tell the whole design avoids.

    /// <summary>
    /// Above this many non-blank lines the content is a document, whatever it
    /// quotes. Generous enough for a payload that carries a couple of lines of
    /// fake verification text.
    /// </summary>
    private const int MaxCommandLines = 6;

    /// <summary>
    /// How much of the content the command lines must account for. A payload is
    /// mostly command; a document mentioning one is mostly not.
    /// </summary>
    private const double MinCommandShare = 0.5;

    /// <summary>
    /// True when the text <em>is</em> a command rather than a document that
    /// happens to contain one.
    ///
    /// Deliberately separate from <see cref="IsShellCommand"/>: the patterns
    /// answer what the text contains, this answers whether the text is a
    /// payload. Both are needed, and conflating them is what caused ClipGuard
    /// to flag a project README as a fake-CAPTCHA attack.
    /// </summary>
    public static bool IsCommandShaped(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var lines = text.ReplaceLineEndings("\n")
                        .Split('\n')
                        .Select(l => l.Trim())
                        .Where(l => l.Length > 0)
                        .ToList();

        if (lines.Count is 0 or > MaxCommandLines) return false;

        var total = lines.Sum(l => l.Length);
        if (total == 0) return false;

        var commandChars = lines.Where(IsShellCommand).Sum(l => l.Length);

        // A command split across lines matches as a whole but on no single
        // line. Within the line cap, accept it rather than measuring a share
        // of zero.
        if (commandChars == 0) return IsShellCommand(text);

        return commandChars >= total * MinCommandShare;
    }

    public static RiskKind Classify(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return RiskKind.None;

        var trimmed = content.Trim();

        if (IsCryptoAddress(trimmed)) return RiskKind.CryptoAddress;
        if (IsShellCommand(content)) return RiskKind.ShellCommand;

        return RiskKind.None;
    }

    public static bool IsCryptoAddress(string value)
    {
        var v = value.Trim();
        if (v.Length is < 26 or > 110) return false;
        if (v.Contains(' ') || v.Contains('\n')) return false;

        return BitcoinBech32().IsMatch(v)
            || BitcoinLegacy().IsMatch(v)
            || Ethereum().IsMatch(v)
            || Monero().IsMatch(v)
            || Litecoin().IsMatch(v)
            || Tron().IsMatch(v);
    }

    public static bool IsShellCommand(string value) =>
        CurlPipeShell().IsMatch(value)
        || PowerShellExec().IsMatch(value)
        || Base64PipeShell().IsMatch(value)
        || DestructiveShell().IsMatch(value)
        || LivingOffTheLand().IsMatch(value)
        || PowerShellDownloadRun().IsMatch(value)
        || HiddenPowerShell().IsMatch(value)
        // A command that arrives padded with blank lines is trying to scroll
        // itself out of view in a terminal paste.
        || (PaddedStart().IsMatch(value) && value.TrimStart().StartsWith("curl", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A shell command carrying fake verification text. This is the ClickFix
    /// signature and has no legitimate explanation - nothing you genuinely need
    /// to run comes with a reCAPTCHA note attached.
    /// </summary>
    public static bool IsVerificationLure(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && IsShellCommand(value)
        && VerificationLure().IsMatch(value);

    /// <summary>
    /// True when replacing <paramref name="original"/> with <paramref name="replacement"/>
    /// warrants the sticky, high-risk notification rather than the soft one.
    /// </summary>
    public static bool IsHighRiskSubstitution(string? original, string? replacement)
    {
        var risk = Classify(replacement);
        if (risk == RiskKind.ShellCommand) return true;

        // A wallet address swapped for a *different* wallet address is the
        // attack this app exists for.
        if (risk == RiskKind.CryptoAddress)
        {
            return original is null
                || !IsCryptoAddress(original)
                || !string.Equals(original.Trim(), replacement?.Trim(), StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>Category to record for a blocked substitution.</summary>
    public static Models.EventCategory CategoryFor(string? replacement) =>
        Classify(replacement) == RiskKind.ShellCommand
            ? Models.EventCategory.CommandInjection
            : Models.EventCategory.ContentSubstitution;

    /// <summary>Single-line, length-capped preview for the UI.</summary>
    public static string Preview(string? content, int max = 120)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;

        var flat = content.ReplaceLineEndings(" ").Trim();
        flat = MultiSpace().Replace(flat, " ");

        return flat.Length <= max ? flat : flat[..max] + "…";
    }

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultiSpace();
}
