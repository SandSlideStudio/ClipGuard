namespace ClipGuard.Services;

/// <summary>
/// A release version, ordered the way semver orders them.
///
/// <see cref="System.Version"/> cannot do this job: it has no concept of a
/// pre-release suffix, so it would read <c>1.0.0-rc.1</c> and <c>1.0.0</c> as
/// the same release and never offer the upgrade from the candidate to the final
/// build. That is the first comparison ClipGuard actually has to get right.
/// </summary>
public sealed record ReleaseVersion : IComparable<ReleaseVersion>
{
    public required int Major { get; init; }
    public required int Minor { get; init; }
    public required int Patch { get; init; }

    /// <summary>The bit after the hyphen, or null for a final release.</summary>
    public string? PreRelease { get; init; }

    public bool IsPreRelease => PreRelease is not null;

    /// <summary>
    /// Accepts the shapes ClipGuard tags releases with, and tolerates the rest:
    /// a leading <c>v</c>, one to three numeric parts, an optional pre-release
    /// suffix. Build metadata after a <c>+</c> is dropped, since semver says it
    /// carries no weight in comparisons.
    /// </summary>
    public static ReleaseVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var value = text.Trim();

        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];

        var plus = value.IndexOf('+');
        if (plus >= 0) value = value[..plus];

        string? preRelease = null;
        var hyphen = value.IndexOf('-');
        if (hyphen >= 0)
        {
            preRelease = value[(hyphen + 1)..];
            value = value[..hyphen];

            // "1.0.0-" is malformed rather than a pre-release of nothing.
            if (preRelease.Length == 0) return null;
        }

        var parts = value.Split('.');
        if (parts.Length is 0 or > 3) return null;

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var n) || n < 0) return null;
            numbers[i] = n;
        }

        return new ReleaseVersion
        {
            Major = numbers[0],
            Minor = numbers[1],
            Patch = numbers[2],
            PreRelease = preRelease
        };
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;

        var numeric = Major.CompareTo(other.Major);
        if (numeric != 0) return numeric;

        numeric = Minor.CompareTo(other.Minor);
        if (numeric != 0) return numeric;

        numeric = Patch.CompareTo(other.Patch);
        if (numeric != 0) return numeric;

        // Same numbers: a final release outranks any candidate for it.
        if (PreRelease is null) return other.PreRelease is null ? 0 : 1;
        if (other.PreRelease is null) return -1;

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    /// <summary>
    /// Semver pre-release precedence: dot-separated identifiers, compared left
    /// to right. Numeric identifiers compare as numbers and rank below
    /// alphanumeric ones, and a shorter run of equal identifiers ranks lower, so
    /// <c>rc.1</c> precedes <c>rc.2</c> and <c>rc</c> precedes <c>rc.1</c>.
    /// </summary>
    private static int ComparePreRelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNumeric = int.TryParse(a[i], out var an);
            var bNumeric = int.TryParse(b[i], out var bn);

            var result = (aNumeric, bNumeric) switch
            {
                (true, true) => an.CompareTo(bn),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a[i], b[i])
            };

            if (result != 0) return result;
        }

        return a.Length.CompareTo(b.Length);
    }

    public override string ToString() =>
        PreRelease is null
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{PreRelease}";

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;
    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;
}
