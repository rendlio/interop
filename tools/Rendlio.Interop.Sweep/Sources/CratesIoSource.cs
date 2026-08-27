using System.Text.Json;

namespace Rendlio.Interop.Sweep.Sources;

/// <summary>
/// Reads crates.io through its published API. Results come back ordered by most recently
/// updated, because what a watch wants from a registry is what moved, not what is popular.
/// </summary>
public sealed class CratesIoSource(ISweepTransport transport) : IObservationSource
{
    private const string SearchEndpoint = "https://crates.io/api/v1/crates";
    private const string CratePage = "https://crates.io/crates/";

    private readonly ISweepTransport transport = transport
        ?? throw new ArgumentNullException(nameof(transport));

    /// <inheritdoc />
    public SweepSource Source => SweepSource.CratesIo;

    /// <inheritdoc />
    public string TermMeaning => "a crates.io search expression, matched against name, description and keywords";

    /// <inheritdoc />
    public async Task<IReadOnlyList<Observation>> CollectAsync(
        SweepQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Uri uri = RegistryUri.Build(
            SearchEndpoint,
            ("q", query.Term),
            ("per_page", RegistryUri.Size(query.Take)),
            ("sort", "recent-update"));

        string? body = await transport.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return [];
        }

        using JsonDocument document = RegistryJson.Parse(body, Source);

        List<Observation> observations = [];
        foreach (JsonElement crate in RegistryJson.Array(document.RootElement, "crates"))
        {
            string? name = RegistryJson.Text(crate, "name") ?? RegistryJson.Text(crate, "id");
            if (name is null)
            {
                continue;
            }

            observations.Add(new Observation
            {
                Id = Observation.Identify(Source, name),
                Source = Source,
                Name = name,
                Url = CratePage + name,
                Version = RegistryJson.Text(crate, "max_version") ?? RegistryJson.Text(crate, "newest_version"),
                Description = RegistryJson.Text(crate, "description"),

                // Why the recent window rather than the lifetime total: the recipe's
                // thresholds are written against a window, and a lifetime total on an old
                // crate never moves enough to cross one.
                Downloads = RegistryJson.Count(crate, "recent_downloads") ?? RegistryJson.Count(crate, "downloads"),
                Updated = RegistryJson.Time(crate, "updated_at"),
                Queries = [query.Id],
            });
        }

        return observations;
    }
}
