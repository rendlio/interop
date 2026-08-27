using System.Net;
using System.Reflection;
using Rendlio.Interop.Sweep;
using Rendlio.Interop.Sweep.Sources;
using Xunit;

namespace Rendlio.Interop.Sweep.Tests;

/// <summary>
/// Pins each registry projection, and the two rules that hold for all of them: a run reads
/// and never writes, and a run goes where the collector decided rather than where the recipe
/// said. The payloads below are trimmed copies of what each API actually answers with.
/// </summary>
public sealed class RegistrySourceTests
{
    private const string CratesPayload =
        """
        {"crates":[
          {"id":"sample-crate","name":"sample-crate","description":"A sample crate",
           "max_version":"0.3.0","recent_downloads":74,"downloads":1024,
           "updated_at":"2026-08-26T09:15:00+00:00"},
          {"description":"nameless, and therefore not a candidate"}
        ]}
        """;

    private const string NuGetIndexPayload =
        """
        {"version":"3.0.0","resources":[
          {"@id":"https://api.nuget.org/v3/registration5-gz-semver2/","@type":"RegistrationsBaseUrl/3.6.0"},
          {"@id":"https://azuresearch-usnc.nuget.org/query","@type":"SearchQueryService/3.5.0"}
        ]}
        """;

    private const string NuGetSearchPayload =
        """
        {"totalHits":1,"data":[
          {"id":"Sample.Package","version":"2.1.0","description":"A sample package","totalDownloads":5000}
        ]}
        """;

    private const string NpmPayload =
        """
        {"objects":[
          {"package":{"name":"sample-package","version":"1.4.2","description":"A sample package",
                      "date":"2026-08-20T11:00:00.000Z"}},
          {"score":{"final":0.1}}
        ]}
        """;

    private const string PyPiPayload =
        """
        {"info":{"name":"sample-project","version":"0.9.1","summary":"A sample project",
                 "package_url":"https://pypi.org/project/sample-project/"},
         "urls":[{"upload_time_iso_8601":"2026-08-10T08:00:00Z"},
                 {"upload_time_iso_8601":"2026-08-12T08:00:00Z"}]}
        """;

    private const string GitHubPayload =
        """
        {"total_count":1,"items":[
          {"full_name":"someone/sample-repo","html_url":"https://github.com/someone/sample-repo",
           "description":"A sample repository","stargazers_count":9,"pushed_at":"2026-08-25T22:10:00Z"}
        ]}
        """;

    [Fact]
    public async Task Crates_io_projects_a_crate()
    {
        RecordingTransport transport = new(CratesPayload);

        Observation crate = Assert.Single(
            await new CratesIoSource(transport).CollectAsync(Query(SweepSource.CratesIo, "pdf"), default));

        Assert.Equal("crates.io:sample-crate", crate.Id);
        Assert.Equal("0.3.0", crate.Version);

        // The recent window, not the lifetime total: a threshold written against a window
        // would never be crossed by a total that only grows.
        Assert.Equal(74, crate.Downloads);
        Assert.Equal(new DateTimeOffset(2026, 8, 26, 9, 15, 0, TimeSpan.Zero), crate.Updated);
    }

    [Fact]
    public async Task Nuget_asks_the_service_index_where_search_is_and_asks_once()
    {
        RecordingTransport transport = new(NuGetIndexPayload, NuGetSearchPayload);
        NuGetSource source = new(transport);

        Observation package = Assert.Single(
            await source.CollectAsync(Query(SweepSource.NuGet, "render"), default));

        Assert.Equal("nuget:sample.package", package.Id);
        Assert.Equal(5000, package.Downloads);

        await source.CollectAsync(Query(SweepSource.NuGet, "convert"), default);

        Assert.Equal(NuGetSource.ServiceIndex, transport.Requested[0].ToString());
        Assert.Equal(
            ["api.nuget.org", "azuresearch-usnc.nuget.org", "azuresearch-usnc.nuget.org"],
            transport.Requested.Select(uri => uri.Host));
    }

    [Fact]
    public async Task Nuget_refuses_a_service_index_that_lists_no_search()
    {
        RecordingTransport transport = new("""{"version":"3.0.0","resources":[]}""");

        SweepException failure = await Assert.ThrowsAsync<SweepException>(
            () => new NuGetSource(transport).CollectAsync(Query(SweepSource.NuGet, "render"), default));

        Assert.Contains("SearchQueryService", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Npm_projects_a_package_and_skips_a_result_with_none()
    {
        RecordingTransport transport = new(NpmPayload);

        Observation package = Assert.Single(
            await new NpmSource(transport).CollectAsync(Query(SweepSource.Npm, "docx"), default));

        Assert.Equal("npm:sample-package", package.Id);
        Assert.Equal("1.4.2", package.Version);

        // npm publishes no download count on a search result, and fetching one per hit would
        // turn one query into a hundred requests.
        Assert.Null(package.Downloads);
    }

    [Fact]
    public async Task Pypi_projects_a_project_and_dates_it_from_its_newest_file()
    {
        RecordingTransport transport = new(PyPiPayload);

        Observation project = Assert.Single(
            await new PyPiSource(transport).CollectAsync(Query(SweepSource.PyPi, "sample-project"), default));

        Assert.Equal("pypi:sample-project", project.Id);
        Assert.Equal("0.9.1", project.Version);
        Assert.Equal(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero), project.Updated);
    }

    [Fact]
    public async Task Pypi_treats_a_name_that_is_not_there_as_an_ordinary_answer()
    {
        // The recipe names projects it expects to see. One that has not been published yet is
        // the answer, not a failure.
        RecordingTransport transport = new([null]);

        Assert.Empty(await new PyPiSource(transport).CollectAsync(Query(SweepSource.PyPi, "not-yet"), default));
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("name with spaces")]
    [InlineData("a/b")]
    public async Task Pypi_refuses_a_term_that_is_not_a_project_name(string term)
    {
        // This term lands in the path rather than a query string, so it is held to what PyPI
        // allows a project to be called instead of being escaped and hoped about.
        UnusedTransport transport = new();

        await Assert.ThrowsAsync<SweepException>(
            () => new PyPiSource(transport).CollectAsync(Query(SweepSource.PyPi, term), default));
    }

    [Fact]
    public async Task Github_projects_a_repository()
    {
        RecordingTransport transport = new(GitHubPayload);

        Observation repository = Assert.Single(
            await new GitHubSource(transport).CollectAsync(Query(SweepSource.GitHub, "ooxml"), default));

        Assert.Equal("github:someone/sample-repo", repository.Id);
        Assert.Equal(9, repository.Stars);
        Assert.Null(repository.Version);
    }

    [Theory]
    [InlineData(SweepSource.CratesIo, "crates.io")]
    [InlineData(SweepSource.Npm, "registry.npmjs.org")]
    [InlineData(SweepSource.PyPi, "pypi.org")]
    [InlineData(SweepSource.GitHub, "api.github.com")]
    public async Task A_term_cannot_send_a_run_to_another_host(SweepSource source, string host)
    {
        // The recipe says what to look for. Where a run connects to is the collector decision,
        // and a term that tried to move it lands escaped inside the query string instead.
        // nuget.org is absent because its search address is not a constant here — it is
        // whatever nuget.org own service index says, which the test above covers.
        const string Hostile = "x&q=y#https://elsewhere.invalid/";

        RecordingTransport transport = new(source is SweepSource.PyPi ? PyPiPayload : "{}");
        string term = source is SweepSource.PyPi ? "sample-project" : Hostile;

        await Collector(source, transport).CollectAsync(Query(source, term), default);

        Uri asked = Assert.Single(transport.Requested);

        Assert.Equal(host, asked.Host);
        Assert.Equal(Uri.UriSchemeHttps, asked.Scheme);
        Assert.Equal(string.Empty, asked.Fragment);
    }

    [Fact]
    public async Task A_term_is_escaped_into_the_query_string()
    {
        RecordingTransport transport = new("{}");

        await new CratesIoSource(transport)
            .CollectAsync(Query(SweepSource.CratesIo, "xlsx AND pdf&per_page=999"), default);

        Uri asked = Assert.Single(transport.Requested);

        Assert.Contains("q=xlsx%20AND%20pdf%26per_page%3D999", asked.Query, StringComparison.Ordinal);
        Assert.Contains("per_page=5", asked.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void A_collector_has_no_way_to_write_anything()
    {
        // The rule is that collectors observe. It holds because there is nothing else on the
        // interface every collector reaches the network through.
        MethodInfo[] methods = typeof(ISweepTransport).GetMethods();

        Assert.Equal("GetAsync", Assert.Single(methods).Name);
    }

    [Fact]
    public async Task The_transport_sends_a_get_and_says_who_it_is()
    {
        using StubHandler handler = new(body: "{}");
        using HttpClient client = new(handler);

        await new HttpSweepTransport(client).GetAsync(new Uri("https://crates.io/api/v1/crates"), default);

        Assert.NotNull(handler.Sent);
        Assert.Equal(HttpMethod.Get, handler.Sent.Method);
        Assert.Contains(HttpSweepTransport.UserAgent, handler.Sent.Headers.UserAgent);
    }

    [Theory]
    [InlineData("https://api.github.com/search/repositories", true)]
    [InlineData("https://crates.io/api/v1/crates", false)]
    public async Task The_github_credential_goes_to_github_and_nowhere_else(string address, bool sent)
    {
        // A token that travelled with every request would be handed to four registries that
        // never asked for one, and to whatever a mistyped endpoint resolved to.
        using StubHandler handler = new(body: "{}");
        using HttpClient client = new(handler);

        await new HttpSweepTransport(client, "a-token").GetAsync(new Uri(address), default);

        Assert.NotNull(handler.Sent);
        Assert.Equal(sent, handler.Sent.Headers.Authorization is not null);
    }

    [Fact]
    public async Task A_registry_that_fails_ends_the_run_saying_which_one()
    {
        using StubHandler handler = new(HttpStatusCode.TooManyRequests, "slow down");
        using HttpClient client = new(handler);

        SweepException failure = await Assert.ThrowsAsync<SweepException>(
            () => new HttpSweepTransport(client).GetAsync(new Uri("https://api.github.com/search/repositories"), default));

        Assert.Contains("api.github.com", failure.Message, StringComparison.Ordinal);
        Assert.Contains("429", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_registry_that_cannot_be_reached_ends_the_run_in_one_line()
    {
        // A weekly job runs with nobody watching, and the week a registry is unreachable is an
        // ordinary week. What should be waiting is a sentence naming it, not a stack trace
        // from inside the socket layer.
        using ThrowingHandler handler = new(new HttpRequestException("no such host is known"));
        using HttpClient client = new(handler);

        SweepException failure = await Assert.ThrowsAsync<SweepException>(
            () => new HttpSweepTransport(client).GetAsync(new Uri("https://crates.io/api/v1/crates"), default));

        Assert.Contains("crates.io", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_registry_that_stops_answering_ends_the_run_saying_so()
    {
        using ThrowingHandler handler = new(new TaskCanceledException("timed out"));
        using HttpClient client = new(handler);

        SweepException failure = await Assert.ThrowsAsync<SweepException>(
            () => new HttpSweepTransport(client).GetAsync(new Uri("https://api.github.com/x"), default));

        Assert.Contains("did not answer", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancellation_the_caller_asked_for_is_not_dressed_up_as_a_failure()
    {
        using ThrowingHandler handler = new(new TaskCanceledException("cancelled"));
        using HttpClient client = new(handler);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => new HttpSweepTransport(client).GetAsync(new Uri("https://crates.io/x"), cancelled.Token));
    }

    [Fact]
    public async Task A_registry_that_has_nothing_there_is_not_a_failure()
    {
        using StubHandler handler = new(HttpStatusCode.NotFound, "no such project");
        using HttpClient client = new(handler);

        Assert.Null(await new HttpSweepTransport(client).GetAsync(new Uri("https://pypi.org/pypi/x/json"), default));
    }

    [Fact]
    public async Task A_registry_answering_with_something_that_is_not_json_says_which_registry()
    {
        RecordingTransport transport = new("<html>we moved</html>");

        SweepException failure = await Assert.ThrowsAsync<SweepException>(
            () => new CratesIoSource(transport).CollectAsync(Query(SweepSource.CratesIo, "pdf"), default));

        Assert.Contains("crates.io", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_collector_says_how_it_reads_a_term()
    {
        // The registries disagree about what a term is, and the one with no search API reads
        // it as a name. A recipe author has to be able to find that out from the tool.
        UnusedTransport transport = new();

        foreach (SweepSource source in Enum.GetValues<SweepSource>())
        {
            IObservationSource collector = Collector(source, transport);

            Assert.Equal(source, collector.Source);
            Assert.False(string.IsNullOrWhiteSpace(collector.TermMeaning));
        }
    }

    private static IObservationSource Collector(SweepSource source, ISweepTransport transport) => source switch
    {
        SweepSource.CratesIo => new CratesIoSource(transport),
        SweepSource.NuGet => new NuGetSource(transport),
        SweepSource.Npm => new NpmSource(transport),
        SweepSource.PyPi => new PyPiSource(transport),
        SweepSource.GitHub => new GitHubSource(transport),
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    private static SweepQuery Query(SweepSource source, string term) =>
        new("a-query", source, term, Take: 5);
}
