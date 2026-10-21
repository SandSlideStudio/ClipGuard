using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipGuard.Services;

/// <summary>A release newer than the one running.</summary>
public sealed record AvailableUpdate(ReleaseVersion Version, string PageUrl);

public enum UpdateCheckResult
{
    /// <summary>Nothing newer is published.</summary>
    UpToDate,
    /// <summary>Something newer is published. See <see cref="UpdateService.Available"/>.</summary>
    UpdateAvailable,
    /// <summary>No network, GitHub unreachable, or an unreadable answer.</summary>
    Failed,
    /// <summary>Turned off in Settings, or checked recently enough already.</summary>
    Skipped
}

/// <summary>
/// Asks GitHub whether a newer release exists, and does nothing else.
///
/// This is the only outbound network call in ClipGuard, and it deliberately
/// stays the only one. An unsigned security tool with no way to tell you it is
/// out of date is a liability: the first real detection fix shipped after
/// release reaches nobody who already downloaded the app. That is the problem
/// this solves, and the whole of it.
///
/// What it will not do:
///
/// <list type="bullet">
/// <item>
/// It downloads and installs nothing. Fetching an executable over the network
/// and running it is a far larger attack surface than the stale-build problem
/// it would fix, and it is a particularly poor trade for an unsigned binary. The
/// user is shown the release page and stays in charge of the download, where
/// the checksum and the provenance attestation are there to be checked.
/// </item>
/// <item>
/// It sends nothing about the user or the machine. The request carries no
/// identifier, no version and no clipboard data: it is a plain GET for a public
/// list of releases, and GitHub learns only that some copy of ClipGuard asked.
/// The <c>User-Agent</c> is the bare product name because GitHub requires one,
/// and it is deliberately version-free so the request cannot be used to count
/// installs per build.
/// </item>
/// </list>
/// </summary>
public sealed class UpdateService : INotifyPropertyChanged
{
    private const string ReleasesApi =
        "https://api.github.com/repos/SandSlideStudio/ClipGuard/releases?per_page=10";

    public const string ReleasesPage = "https://github.com/SandSlideStudio/ClipGuard/releases";

    private const string StateFile = "updates.json";

    /// <summary>
    /// How long an automatic check waits after the last one. Long enough that
    /// restarting the app repeatedly cannot turn into a stream of requests,
    /// short enough that a fix published today is noticed tomorrow. A check the
    /// user asks for ignores this entirely.
    /// </summary>
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(20);

    /// <summary>
    /// A hostile or broken answer should not be read into memory without limit.
    /// The real response for ten releases is a few tens of kilobytes.
    /// </summary>
    private const int MaxResponseBytes = 512 * 1024;

    private static readonly HttpClient Http = CreateClient();

    private readonly Func<CancellationToken, Task<string>> _fetch;
    private readonly Func<DateTimeOffset> _now;
    private UpdateState _state;

    private AvailableUpdate? _available;
    private bool _isChecking;
    private string? _statusText;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised the first time a particular new version is seen, for the tray balloon.</summary>
    public event EventHandler<AvailableUpdate>? UpdateFirstSeen;

    public UpdateService(
        Func<CancellationToken, Task<string>>? fetch = null,
        Func<DateTimeOffset>? now = null,
        bool loadState = true)
    {
        _fetch = fetch ?? FetchFromGitHubAsync;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _state = loadState ? Storage.Load(StateFile, () => new UpdateState()) : new UpdateState();
    }

    /// <summary>
    /// The running build, read from the informational version so a pre-release
    /// suffix survives. The assembly version cannot carry one.
    /// </summary>
    public static ReleaseVersion Current { get; } = ReadCurrentVersion();

    public string CurrentVersionText => Current.ToString();

    /// <summary>The newer release, or null when there isn't one (yet).</summary>
    public AvailableUpdate? Available
    {
        get => _available;
        private set
        {
            if (_available == value) return;
            _available = value;
            Raise();
            Raise(nameof(HasUpdate));
            Raise(nameof(BannerText));
        }
    }

    public bool HasUpdate => Available is not null;

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (_isChecking == value) return;
            _isChecking = value;
            Raise();
        }
    }

    /// <summary>Human-readable outcome of the last check, for the About tab.</summary>
    public string? StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value) return;
            _statusText = value;
            Raise();
        }
    }

    public string BannerText => Available is null
        ? string.Empty
        : $"ClipGuard {Available.Version} is available. You're on {Current}.";

    /// <summary>
    /// Checks for a newer release.
    /// </summary>
    /// <param name="enabled">
    /// The user's preference. Passed in rather than read here so this service
    /// stays independent of <see cref="ProtectionService"/>, and so a check the
    /// user clicks can be honoured while automatic ones are off.
    /// </param>
    /// <param name="userInitiated">
    /// True for the About tab's button: ignores both the preference and the
    /// interval, because someone asking is entitled to an answer.
    /// </param>
    public async Task<UpdateCheckResult> CheckAsync(
        bool enabled, bool userInitiated = false, CancellationToken cancellationToken = default)
    {
        if (!userInitiated)
        {
            if (!enabled) return UpdateCheckResult.Skipped;
            if (_now() - _state.LastCheckedUtc < MinimumInterval) return UpdateCheckResult.Skipped;
        }

        IsChecking = true;
        StatusText = "Checking…";

        try
        {
            var json = await _fetch(cancellationToken).ConfigureAwait(true);
            var update = ChooseUpdate(json, Current);

            // Record the attempt whether or not it found anything, so a
            // reachable GitHub with no new release still rate-limits.
            _state = _state with { LastCheckedUtc = _now() };
            SaveState();

            Available = update;

            if (update is null)
            {
                StatusText = $"You're up to date on {Current}.";
                return UpdateCheckResult.UpToDate;
            }

            StatusText = $"{update.Version} is available.";
            AnnounceOnce(update);

            return UpdateCheckResult.UpdateAvailable;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                      or JsonException or OperationCanceledException
                                      or InvalidOperationException or IOException)
        {
            // Being offline is the normal case here, not an error worth
            // interrupting anyone over. Say so and move on.
            StatusText = "Couldn't reach GitHub. We'll try again later.";
            return UpdateCheckResult.Failed;
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// Fires <see cref="UpdateFirstSeen"/> only for a version not announced
    /// before, so the tray balloon appears once per release rather than at every
    /// launch until the user updates.
    /// </summary>
    private void AnnounceOnce(AvailableUpdate update)
    {
        var version = update.Version.ToString();
        if (_state.LastAnnouncedVersion == version) return;

        _state = _state with { LastAnnouncedVersion = version };
        SaveState();

        UpdateFirstSeen?.Invoke(this, update);
    }

    /// <summary>
    /// Picks the newest release worth offering, as a pure function of the
    /// response so it can be tested without a network.
    ///
    /// A build running a pre-release is offered pre-releases, because someone on
    /// a release candidate wants the next candidate. A final build is only ever
    /// offered final releases, so a stable install is never nudged onto an
    /// untried one.
    /// </summary>
    internal static AvailableUpdate? ChooseUpdate(string json, ReleaseVersion current)
    {
        var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json) ?? [];

        AvailableUpdate? best = null;

        foreach (var release in releases)
        {
            if (release.Draft) continue;
            if (release.PreRelease && !current.IsPreRelease) continue;

            var version = ReleaseVersion.Parse(release.TagName);
            if (version is null || version <= current) continue;

            // Don't trust the response's ordering; compare them.
            if (best is not null && version <= best.Version) continue;

            var url = string.IsNullOrWhiteSpace(release.HtmlUrl) ? ReleasesPage : release.HtmlUrl;
            best = new AvailableUpdate(version, url);
        }

        return best;
    }

    private static async Task<string> FetchFromGitHubAsync(CancellationToken cancellationToken)
    {
        using var response = await Http
            .GetAsync(ReleasesApi, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var buffer = new byte[MaxResponseBytes];
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0) break;
            total += read;
        }

        return System.Text.Encoding.UTF8.GetString(buffer, 0, total);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // GitHub rejects requests without a User-Agent. The bare product name
        // is all it gets: no version, so this can't be used to count installs
        // per build, and no machine or user detail of any kind.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClipGuard");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        return client;
    }

    private static ReleaseVersion ReadCurrentVersion()
    {
        var informational = typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // The SDK appends "+<commit sha>" to the informational version; Parse
        // drops build metadata, so it is handed over as it comes.
        return ReleaseVersion.Parse(informational)
               ?? ReleaseVersion.Parse(typeof(UpdateService).Assembly.GetName().Version?.ToString())
               ?? new ReleaseVersion { Major = 0, Minor = 0, Patch = 0 };
    }

    private void SaveState() => Storage.Save(StateFile, _state);

    private void Raise([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property!));

    /// <summary>
    /// When we last asked, and what we last told the user about. Not settings:
    /// nothing here is a preference, and it holds no clipboard content, so it is
    /// stored unencrypted beside them.
    /// </summary>
    private sealed record UpdateState
    {
        public DateTimeOffset LastCheckedUtc { get; init; } = DateTimeOffset.MinValue;
        public string? LastAnnouncedVersion { get; init; }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("prerelease")]
        public bool PreRelease { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }
    }
}
