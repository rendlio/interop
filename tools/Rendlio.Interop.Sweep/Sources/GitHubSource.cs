using System.Text.Json;

namespace Rendlio.Interop.Sweep.Sources;

/// <summary>
/// Searches GitHub repositories through its REST API. Ordered by most recently pushed, and
/// deliberately not filtered by stars: the whole reason a watch is a job rather than an
/// afternoon is that the interesting repository is usually the one nobody has starred yet.
/// </summary>
public sealed class GitHubSource(ISweepTransport transport) : IObservationSource
{
    private const string SearchEndpoint = "https://api.github.com/search/repositories";

    private readonly ISweepTransport transport = transport
        ?? throw new ArgumentNullException(nameof(transport));

    /// <inheritdoc />
    public SweepSource Source => SweepSource.GitHub;

    /// <inheritdoc />
    public string TermMeaning =>
        "a GitHub repository-search expression, with the qualifiers that search accepts";

    /// <inheritdoc />
    public async Task<IReadOnlyList<Observation>> CollectAsync(
        SweepQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Uri uri = RegistryUri.Build(
            SearchEndpoint,
            ("q", query.Term),
            ("sort", "updated"),
            ("order", "desc"),
            ("per_page", RegistryUri.Size(query.Take)));

        string? body = await transport.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return [];
        }

        using JsonDocument document = RegistryJson.Parse(body, Source);

        List<Observation> observations = [];
        foreach (JsonElement repository in RegistryJson.Array(document.RootElement, "items"))
        {
            string? name = RegistryJson.Text(repository, "full_name");
            if (name is null)
            {
                continue;
            }

            observations.Add(new Observation
            {
                Id = Observation.Identify(Source, name),
                Source = Source,
                Name = name,
                Url = RegistryJson.Text(repository, "html_url") ?? "https://github.com/" + name,

                // A repository has no version. Its releases are a separate call per
                // repository, which is not what a broad search should cost.
                Description = RegistryJson.Text(repository, "description"),
                Stars = RegistryJson.Count(repository, "stargazers_count"),
                Updated = RegistryJson.Time(repository, "pushed_at"),
                Queries = [query.Id],
            });
        }

        return observations;
    }
}
