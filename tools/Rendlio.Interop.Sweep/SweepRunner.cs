using Rendlio.Interop.Sweep.Sources;

namespace Rendlio.Interop.Sweep;

/// <summary>
/// Runs a recipe: every query against its registry, the results merged into one record per
/// candidate, and the recipe patterns applied to what each candidate publishes about itself.
/// </summary>
public sealed class SweepRunner
{
    private readonly Dictionary<SweepSource, IObservationSource> sources;

    /// <summary>Creates a runner over the given collectors.</summary>
    /// <param name="sources">One collector per registry.</param>
    /// <exception cref="SweepException">Two collectors claim the same registry.</exception>
    public SweepRunner(IEnumerable<IObservationSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        this.sources = [];

        foreach (IObservationSource source in sources)
        {
            if (!this.sources.TryAdd(source.Source, source))
            {
                throw new SweepException(
                    $"Two collectors were registered for {SweepSources.Name(source.Source)}.");
            }
        }
    }

    /// <summary>
    /// Collects everything the recipe asks for.
    /// </summary>
    /// <param name="recipe">The recipe to run.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>One record per candidate, ordered by identity.</returns>
    /// <exception cref="SweepException">
    /// The recipe names a registry with no collector, or a registry failed.
    /// </exception>
    public async Task<IReadOnlyList<Observation>> CollectAsync(
        SweepRecipe recipe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        recipe.Validate();

        // Checked before the first request. Half a sweep is worse than none: the candidates
        // the missing registry would have contributed would read as having disappeared.
        foreach (SweepQuery query in recipe.Queries)
        {
            if (!sources.ContainsKey(query.Source))
            {
                throw new SweepException(
                    $"Query {query.Id} reads {SweepSources.Name(query.Source)}, which this run has "
                    + "no collector for.");
            }
        }

        ClaimScanner scanner = new(recipe.Patterns);
        Dictionary<string, Observation> candidates = new(StringComparer.Ordinal);

        foreach (SweepQuery query in recipe.Queries)
        {
            IReadOnlyList<Observation> found = await sources[query.Source]
                .CollectAsync(query, cancellationToken)
                .ConfigureAwait(false);

            foreach (Observation observation in found)
            {
                candidates[observation.Id] = candidates.TryGetValue(observation.Id, out Observation? already)
                    ? Merge(already, observation)
                    : observation;
            }
        }

        return
        [
            .. candidates.Values
                .Select(candidate => candidate with
                {
                    Claims = scanner.Scan(RegistryJson.Published(candidate.Name, candidate.Description)),
                })
                .OrderBy(candidate => candidate.Id, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Folds a second sighting of the same candidate into the first. The registry answered
    /// about one package either way, so the fields agree; what differs is which query asked,
    /// and a query that returned a field the first one omitted fills it in.
    /// </summary>
    private static Observation Merge(Observation first, Observation second) => first with
    {
        Version = first.Version ?? second.Version,
        Description = first.Description ?? second.Description,
        Downloads = first.Downloads ?? second.Downloads,
        Stars = first.Stars ?? second.Stars,
        Updated = first.Updated ?? second.Updated,
        Queries = [.. first.Queries.Union(second.Queries, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal)],
    };
}
