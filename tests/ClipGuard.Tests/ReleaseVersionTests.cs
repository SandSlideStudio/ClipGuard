using ClipGuard.Services;

namespace ClipGuard.Tests;

/// <summary>
/// Ordering releases. The pre-release cases are the point: getting them wrong
/// either strands everyone on a release candidate forever or pushes a stable
/// install onto an untried build.
/// </summary>
public class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("V1.0.0", 1, 0, 0, null)]
    [InlineData("1.0.0-rc.1", 1, 0, 0, "rc.1")]
    [InlineData("v2.0.0-beta", 2, 0, 0, "beta")]
    [InlineData("1.2", 1, 2, 0, null)]
    [InlineData("3", 3, 0, 0, null)]
    // The .NET SDK appends build metadata to the informational version; semver
    // says it carries no weight, so it is dropped rather than rejected.
    [InlineData("1.0.0+abc123", 1, 0, 0, null)]
    [InlineData("1.0.0-rc.1+abc123", 1, 0, 0, "rc.1")]
    public void Parses_the_shapes_we_tag(string text, int major, int minor, int patch, string? pre)
    {
        var version = ReleaseVersion.Parse(text);

        Assert.NotNull(version);
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(pre, version.PreRelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("1.0.0.0")]
    [InlineData("1.0.x")]
    [InlineData("-1.0.0")]
    [InlineData("1.0.0-")]
    public void Refuses_what_it_cannot_order(string? text) =>
        Assert.Null(ReleaseVersion.Parse(text));

    [Theory]
    [InlineData("1.0.1", "1.0.0")]
    [InlineData("1.1.0", "1.0.9")]
    [InlineData("2.0.0", "1.9.9")]
    // The comparison the whole feature turns on: the final release outranks
    // every candidate for it, so someone on rc.1 is offered 1.0.0.
    [InlineData("1.0.0", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.10", "1.0.0-rc.9")]
    [InlineData("1.0.0-rc.1", "1.0.0-beta.9")]
    // Numeric identifiers rank below alphanumeric ones, and a longer run of
    // otherwise equal identifiers ranks higher.
    [InlineData("1.0.0-rc.alpha", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0-rc")]
    public void Orders_releases(string higher, string lower)
    {
        var a = ReleaseVersion.Parse(higher)!;
        var b = ReleaseVersion.Parse(lower)!;

        Assert.True(a > b, $"{higher} should outrank {lower}");
        Assert.True(b < a, $"{lower} should rank below {higher}");
    }

    [Fact]
    public void Rc_10_beats_rc_9_which_string_comparison_would_get_wrong()
    {
        // Spelled out separately because this is the trap: ordinal comparison
        // puts "rc.10" before "rc.9", so the first double-digit candidate would
        // silently stop offering updates.
        Assert.True(string.CompareOrdinal("rc.10", "rc.9") < 0);
        Assert.True(ReleaseVersion.Parse("1.0.0-rc.10")! > ReleaseVersion.Parse("1.0.0-rc.9")!);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("v1.0.0", "1.0.0")]
    [InlineData("1.0.0-rc.1", "v1.0.0-rc.1")]
    public void Treats_equal_versions_as_equal(string left, string right) =>
        Assert.Equal(0, ReleaseVersion.Parse(left)!.CompareTo(ReleaseVersion.Parse(right)));

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("v1.2.3-rc.4", "1.2.3-rc.4")]
    [InlineData("1.0.0+meta", "1.0.0")]
    public void Round_trips_to_the_text_we_show_users(string input, string expected) =>
        Assert.Equal(expected, ReleaseVersion.Parse(input)!.ToString());

    [Fact]
    public void Is_pre_release_only_when_it_carries_a_suffix()
    {
        Assert.True(ReleaseVersion.Parse("1.0.0-rc.1")!.IsPreRelease);
        Assert.False(ReleaseVersion.Parse("1.0.0")!.IsPreRelease);
    }
}
