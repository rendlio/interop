using System.Text.Json;

namespace Rendlio.Interop.Sweep.Sources;

/// <summary>
/// Reads the npm registry's search API. It publishes no download counts on a search result,
/// so those stay null here rather than being fetched one package at a time — a per-package
/// download call for every hit would turn one query into a hundred requests.
/// </summary>
public sealed class NpmSource(ISweepTransport transport) : IObservationSource
{
    private const string SearchEndpoint = "https://registry.npmjs.org/-/v1/search";
    private const string PackagePage = "https://www.npmjs.com/package/";

    private readonly ISweepTransport transport = transport
        ?? throw new ArgumentNullException(nameof(transport));

    /// <inheritdoc />
    public SweepSource Source => SweepSource.Npm;

    /// <inheritdoc />
    public string TermMeaning => "an npm search expression, which may carry npm's own qualifiers";

    /// <inheritdoc />
    public async Task<IReadOnlyList<Observation>> CollectAsync(
        SweepQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Uri uri = RegistryUri.Build(
            SearchEndpoint, ("text", query.Term), ("size", RegistryUri.Size(query.Take)));

        string? body = await transport.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return [];
        }

        using JsonDocument document = RegistryJson.Parse(body, Source);

        List<Observation> observations = [];
        foreach (JsonElement result in RegistryJson.Array(document.RootElement, "objects"))
        {
            if (RegistryJson.Object(result, "package") is not JsonElement package)
            {
                continue;
            }

            string? name = RegistryJson.Text(package, "name");
            if (name is null)
            {
                continue;
            }

            observations.Add(new Observation
            {
                Id = Observation.Identify(Source, name),
                Source = Source,
                Name = name,
                Url = PackagePage + name,
                Version = RegistryJson.Text(package, "version"),
                Description = RegistryJson.Text(package, "description"),
                Updated = RegistryJson.Time(package, "date"),
                Queries = [query.Id],
            });
        }

        return observations;
    }
}
