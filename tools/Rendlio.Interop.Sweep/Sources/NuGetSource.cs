using System.Text.Json;

namespace Rendlio.Interop.Sweep.Sources;

/// <summary>
/// Reads nuget.org. The search endpoint is not a fixed address — nuget.org publishes where it
/// currently is in a service index, and clients are expected to ask. Hard-coding the address
/// the index happens to give today is how a tool quietly stops working, so this asks, and it
/// asks once per instance.
/// </summary>
public sealed class NuGetSource(ISweepTransport transport, string? serviceIndex = null) : IObservationSource
{
    /// <summary>
    /// The one address nuget.org does guarantee, and the same one this repository's restore
    /// is pinned to. Every other nuget.org address a run uses comes out of this document.
    /// </summary>
    public const string ServiceIndex = "https://api.nuget.org/v3/index.json";

    private const string SearchResourceType = "SearchQueryService";
    private const string PackagePage = "https://www.nuget.org/packages/";

    private readonly ISweepTransport transport = transport
        ?? throw new ArgumentNullException(nameof(transport));

    private readonly string serviceIndex = serviceIndex ?? ServiceIndex;

    private string? searchEndpoint;

    /// <inheritdoc />
    public SweepSource Source => SweepSource.NuGet;

    /// <inheritdoc />
    public string TermMeaning =>
        "a nuget.org search expression; its 'packageid:' qualifier turns a query into a lookup";

    /// <inheritdoc />
    public async Task<IReadOnlyList<Observation>> CollectAsync(
        SweepQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        string endpoint = await ResolveSearchAsync(cancellationToken).ConfigureAwait(false);

        Uri uri = RegistryUri.Build(
            endpoint,
            ("q", query.Term),
            ("take", RegistryUri.Size(query.Take)),
            ("prerelease", "true"),
            ("semVerLevel", "2.0.0"));

        string? body = await transport.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return [];
        }

        using JsonDocument document = RegistryJson.Parse(body, Source);

        List<Observation> observations = [];
        foreach (JsonElement package in RegistryJson.Array(document.RootElement, "data"))
        {
            string? name = RegistryJson.Text(package, "id");
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
                Downloads = RegistryJson.Count(package, "totalDownloads"),
                Queries = [query.Id],
            });
        }

        return observations;
    }

    /// <summary>
    /// Finds the search address in the service index. Cached for the instance's life: a
    /// recipe has many nuget.org queries and the index does not move inside one run.
    /// </summary>
    private async Task<string> ResolveSearchAsync(CancellationToken cancellationToken)
    {
        if (searchEndpoint is not null)
        {
            return searchEndpoint;
        }

        string body = await transport.GetAsync(new Uri(serviceIndex), cancellationToken).ConfigureAwait(false)
            ?? throw new SweepException($"nuget.org served no service index at {serviceIndex}.");

        using JsonDocument document = RegistryJson.Parse(body, Source);

        foreach (JsonElement resource in RegistryJson.Array(document.RootElement, "resources"))
        {
            string? type = RegistryJson.Text(resource, "@type");
            string? address = RegistryJson.Text(resource, "@id");

            // The index carries versioned names for the same resource — the bare name and
            // SearchQueryService/3.5.0 and so on. Any of them answers the query below.
            if (address is not null
                && type is not null
                && type.StartsWith(SearchResourceType, StringComparison.Ordinal))
            {
                searchEndpoint = address;

                return address;
            }
        }

        throw new SweepException(
            $"The service index at {serviceIndex} lists no {SearchResourceType} resource.");
    }
}
