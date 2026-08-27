using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rendlio.Interop.Sweep.Sources;

/// <summary>
/// Reads one PyPI project. PyPI is the one registry here with no search API to call — the
/// XML-RPC one was withdrawn, and what replaced it is the web page, which is not ours to
/// scrape. So a PyPI query names a project rather than describing one, and the recipe carries
/// the names. That is a real gap in coverage on this registry, and saying so here is more use
/// to whoever extends this than a search that quietly worked on the HTML.
/// </summary>
public sealed partial class PyPiSource(ISweepTransport transport) : IObservationSource
{
    private const string ProjectEndpoint = "https://pypi.org/pypi/";
    private const string ProjectPage = "https://pypi.org/project/";

    private readonly ISweepTransport transport = transport
        ?? throw new ArgumentNullException(nameof(transport));

    /// <inheritdoc />
    public SweepSource Source => SweepSource.PyPi;

    /// <inheritdoc />
    public string TermMeaning => "a PyPI project name; this registry publishes no search API";

    /// <inheritdoc />
    public async Task<IReadOnlyList<Observation>> CollectAsync(
        SweepQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The term lands in the path here rather than in a query string, so escaping it is not
        // the whole answer: it is also held to what PyPI allows a project to be called.
        if (!ProjectName().IsMatch(query.Term))
        {
            throw new SweepException(
                $"Query {query.Id} asks PyPI for \"{query.Term}\", which is not a project name. "
                + "PyPI publishes no search API, so a term here names one project.");
        }

        Uri uri = new(ProjectEndpoint + Uri.EscapeDataString(query.Term) + "/json");

        string? body = await transport.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            // A name that is not on PyPI is an ordinary answer for a watch: the project the
            // recipe expects to appear has not appeared yet.
            return [];
        }

        using JsonDocument document = RegistryJson.Parse(body, Source);

        if (RegistryJson.Object(document.RootElement, "info") is not JsonElement info)
        {
            throw new SweepException(
                $"pypi answered for {query.Term} without the info object its API documents.");
        }

        string name = RegistryJson.Text(info, "name") ?? query.Term;

        return
        [
            new Observation
            {
                Id = Observation.Identify(Source, name),
                Source = Source,
                Name = name,
                Url = RegistryJson.Text(info, "package_url") ?? ProjectPage + name,
                Version = RegistryJson.Text(info, "version"),
                Description = RegistryJson.Text(info, "summary"),
                Updated = LatestUpload(document.RootElement),
                Queries = [query.Id],
            },
        ];
    }

    /// <summary>
    /// When the current version was last uploaded. PyPI publishes no "last changed" field, so
    /// the newest file of the current release stands in for one.
    /// </summary>
    private static DateTimeOffset? LatestUpload(JsonElement root)
    {
        DateTimeOffset? latest = null;

        foreach (JsonElement file in RegistryJson.Array(root, "urls"))
        {
            DateTimeOffset? uploaded = RegistryJson.Time(file, "upload_time_iso_8601")
                ?? RegistryJson.Time(file, "upload_time");

            if (uploaded is not null && (latest is null || uploaded > latest))
            {
                latest = uploaded;
            }
        }

        return latest;
    }

    /// <summary>What PEP 508 allows a project to be called.</summary>
    [GeneratedRegex("^[A-Za-z0-9]([A-Za-z0-9._-]*[A-Za-z0-9])?$")]
    private static partial Regex ProjectName();
}
