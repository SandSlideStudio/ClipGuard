using ClipGuard.Services;

namespace ClipGuard.Tests;

/// <summary>
/// The update check, without a network. The fetch is injected, so these assert
/// the decisions rather than GitHub's availability.
/// </summary>
public class UpdateServiceTests
{
    private static string Releases(params (string Tag, bool PreRelease, bool Draft)[] releases) =>
        "[" + string.Join(",", releases.Select(r =>
            $$"""
            {"tag_name":"{{r.Tag}}","html_url":"https://example.test/{{r.Tag}}",
             "prerelease":{{(r.PreRelease ? "true" : "false")}},"draft":{{(r.Draft ? "true" : "false")}}}
            """)) + "]";

    private static ReleaseVersion V(string text) => ReleaseVersion.Parse(text)!;

    // ── Choosing what to offer ────────────────────────────────────────────

    [Fact]
    public void Offers_a_newer_stable_release()
    {
        var update = UpdateService.ChooseUpdate(Releases(("v1.0.1", false, false)), V("1.0.0"));

        Assert.NotNull(update);
        Assert.Equal("1.0.1", update.Version.ToString());
        Assert.Equal("https://example.test/v1.0.1", update.PageUrl);
    }

    [Fact]
    public void Offers_nothing_when_the_newest_release_is_the_one_running() =>
        Assert.Null(UpdateService.ChooseUpdate(Releases(("v1.0.0", false, false)), V("1.0.0")));

    [Fact]
    public void Offers_nothing_when_the_published_release_is_older() =>
        Assert.Null(UpdateService.ChooseUpdate(Releases(("v0.9.0", false, false)), V("1.0.0")));

    [Fact]
    public void Never_nudges_a_stable_build_onto_a_pre_release() =>
        Assert.Null(UpdateService.ChooseUpdate(
            Releases(("v1.1.0-rc.1", true, false)), V("1.0.0")));

    [Fact]
    public void Offers_a_pre_release_to_a_build_that_is_itself_one()
    {
        // Someone running a release candidate wants the next candidate. Anyone
        // on a final build does not, which is the case above.
        var update = UpdateService.ChooseUpdate(
            Releases(("v1.0.0-rc.2", true, false)), V("1.0.0-rc.1"));

        Assert.Equal("1.0.0-rc.2", update?.Version.ToString());
    }

    [Fact]
    public void Offers_the_final_release_to_a_release_candidate()
    {
        // The upgrade path that matters for the build currently published.
        var update = UpdateService.ChooseUpdate(
            Releases(("v1.0.0", false, false), ("v1.0.0-rc.1", true, false)), V("1.0.0-rc.1"));

        Assert.Equal("1.0.0", update?.Version.ToString());
    }

    [Fact]
    public void Ignores_drafts() =>
        Assert.Null(UpdateService.ChooseUpdate(Releases(("v2.0.0", false, true)), V("1.0.0")));

    [Fact]
    public void Picks_the_highest_rather_than_the_first()
    {
        // GitHub orders by creation date. A release published out of order, or
        // a patch backported to an older line, must not win on position alone.
        var update = UpdateService.ChooseUpdate(
            Releases(("v1.0.1", false, false), ("v1.2.0", false, false), ("v1.1.0", false, false)),
            V("1.0.0"));

        Assert.Equal("1.2.0", update?.Version.ToString());
    }

    [Fact]
    public void Skips_tags_it_cannot_parse_without_discarding_the_rest()
    {
        var update = UpdateService.ChooseUpdate(
            Releases(("nightly", false, false), ("v1.0.1", false, false)), V("1.0.0"));

        Assert.Equal("1.0.1", update?.Version.ToString());
    }

    [Fact]
    public void Survives_an_empty_release_list() =>
        Assert.Null(UpdateService.ChooseUpdate("[]", V("1.0.0")));

    [Fact]
    public void Falls_back_to_the_releases_page_when_a_release_has_no_url()
    {
        var update = UpdateService.ChooseUpdate(
            """[{"tag_name":"v1.0.1","prerelease":false,"draft":false}]""", V("1.0.0"));

        Assert.Equal(UpdateService.ReleasesPage, update?.PageUrl);
    }

    // ── Behaviour around the check ────────────────────────────────────────

    private static UpdateService Service(
        Func<CancellationToken, Task<string>> fetch, Func<DateTimeOffset>? now = null) =>
        new(fetch, now, loadState: false);

    [Fact]
    public async Task Does_not_reach_the_network_when_the_setting_is_off()
    {
        var called = false;

        var service = Service(_ =>
        {
            called = true;
            return Task.FromResult("[]");
        });

        var result = await service.CheckAsync(enabled: false);

        Assert.Equal(UpdateCheckResult.Skipped, result);
        Assert.False(called, "a disabled check must make no request at all");
    }

    [Fact]
    public async Task A_check_the_user_asked_for_runs_even_with_the_setting_off()
    {
        var called = false;

        var service = Service(_ =>
        {
            called = true;
            return Task.FromResult("[]");
        });

        var result = await service.CheckAsync(enabled: false, userInitiated: true);

        Assert.True(called, "pressing the button is entitled to an answer");
        Assert.Equal(UpdateCheckResult.UpToDate, result);
    }

    [Fact]
    public async Task Automatic_checks_are_rate_limited_but_the_button_is_not()
    {
        var calls = 0;
        var clock = DateTimeOffset.Parse("2026-10-21T09:00:00Z");

        var service = Service(_ =>
        {
            calls++;
            return Task.FromResult("[]");
        }, () => clock);

        Assert.Equal(UpdateCheckResult.UpToDate, await service.CheckAsync(enabled: true));
        Assert.Equal(1, calls);

        // Restarting the app repeatedly must not turn into a stream of requests.
        clock = clock.AddHours(2);
        Assert.Equal(UpdateCheckResult.Skipped, await service.CheckAsync(enabled: true));
        Assert.Equal(1, calls);

        // The user can still force one.
        Assert.Equal(UpdateCheckResult.UpToDate,
            await service.CheckAsync(enabled: true, userInitiated: true));
        Assert.Equal(2, calls);

        // And the next day's automatic check goes ahead.
        clock = clock.AddHours(20);
        Assert.Equal(UpdateCheckResult.UpToDate, await service.CheckAsync(enabled: true));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Being_offline_is_reported_rather_than_thrown()
    {
        var service = Service(_ => throw new System.Net.Http.HttpRequestException("no network"));

        var result = await service.CheckAsync(enabled: true, userInitiated: true);

        Assert.Equal(UpdateCheckResult.Failed, result);
        Assert.False(service.HasUpdate);
        Assert.NotNull(service.StatusText);
    }

    [Fact]
    public async Task A_broken_response_is_reported_rather_than_thrown()
    {
        var service = Service(_ => Task.FromResult("this is not json"));

        Assert.Equal(UpdateCheckResult.Failed,
            await service.CheckAsync(enabled: true, userInitiated: true));
    }

    [Fact]
    public async Task Announces_a_new_version_once_rather_than_at_every_check()
    {
        var json = Releases(("v99.0.0", false, false));
        var service = Service(_ => Task.FromResult(json));

        var announced = new List<string>();
        service.UpdateFirstSeen += (_, update) => announced.Add(update.Version.ToString());

        await service.CheckAsync(enabled: true, userInitiated: true);
        await service.CheckAsync(enabled: true, userInitiated: true);
        await service.CheckAsync(enabled: true, userInitiated: true);

        // Three checks, one balloon: nagging at every launch is how people turn
        // the whole thing off.
        Assert.Equal(["99.0.0"], announced);
        Assert.True(service.HasUpdate);
    }

    [Fact]
    public async Task Announces_again_when_a_further_release_appears()
    {
        var json = Releases(("v99.0.0", false, false));
        var service = Service(_ => Task.FromResult(json));

        var announced = new List<string>();
        service.UpdateFirstSeen += (_, update) => announced.Add(update.Version.ToString());

        await service.CheckAsync(enabled: true, userInitiated: true);

        json = Releases(("v99.1.0", false, false), ("v99.0.0", false, false));
        await service.CheckAsync(enabled: true, userInitiated: true);

        Assert.Equal(["99.0.0", "99.1.0"], announced);
    }

    [Fact]
    public async Task Clears_the_banner_if_a_release_is_withdrawn()
    {
        var json = Releases(("v99.0.0", false, false));
        var service = Service(_ => Task.FromResult(json));

        await service.CheckAsync(enabled: true, userInitiated: true);
        Assert.True(service.HasUpdate);

        json = "[]";
        await service.CheckAsync(enabled: true, userInitiated: true);

        Assert.False(service.HasUpdate);
        Assert.Empty(service.BannerText);
    }

    [Fact]
    public void Knows_which_version_it_is_running()
    {
        // Guards the csproj: InformationalVersion is what carries a pre-release
        // suffix, and without it a candidate build believes it is the final
        // release and never offers the upgrade.
        Assert.NotEqual("0.0.0", UpdateService.Current.ToString());
        Assert.Equal(UpdateService.Current.ToString(), new UpdateService(
            _ => Task.FromResult("[]"), loadState: false).CurrentVersionText);
    }
}
