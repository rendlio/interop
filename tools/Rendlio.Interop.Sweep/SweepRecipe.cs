using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rendlio.Interop.Sweep;

/// <summary>
/// One thing to ask one registry. The source fixes which registry is contacted and the term
/// is the only part a recipe controls, so a recipe can change what a run looks for without
/// being able to change where a run connects to.
/// </summary>
/// <param name="Id">Stable name for the query, recorded on every candidate it surfaces.</param>
/// <param name="Source">The registry to ask.</param>
/// <param name="Term">
/// What to ask it. Each source documents how it reads this — for most it is a search
/// expression in that registry's own syntax, and for the one registry that publishes no
/// search API it is a project name.
/// </param>
/// <param name="Take">How many results to read, at most.</param>
public sealed record SweepQuery(string Id, SweepSource Source, string Term, int Take = 50);

/// <summary>
/// A pattern applied to the text a registry publishes about a candidate. The expression lives
/// in the recipe rather than here: what a run is watching for is the operator's to decide and
/// is not published by this repository.
/// </summary>
/// <param name="Id">Stable name, recorded on a candidate whose text the pattern matched.</param>
/// <param name="Expression">A .NET regular expression, matched case-insensitively.</param>
public sealed record ClaimPattern(string Id, string Expression);

/// <summary>
/// How much a number has to move before a run calls it a change. Downloads and stars drift on
/// every live package, so reporting each tick would bury the entrants and the version bumps
/// under noise — which is the failure the diff exists to avoid.
/// </summary>
/// <param name="MinDownloadsDelta">Ignore download moves smaller than this.</param>
/// <param name="MinStarsDelta">Ignore star moves smaller than this.</param>
public sealed record SweepSensitivity(long MinDownloadsDelta = 0, long MinStarsDelta = 0);

/// <summary>
/// The whole of what a run does, as data. The tool holds no queries and no patterns of its
/// own: a run is given a recipe file, and a recipe file is not part of this repository.
/// </summary>
public sealed record SweepRecipe
{
    /// <summary>Names the recipe, so a report says which one produced it.</summary>
    public required string Name { get; init; }

    /// <summary>The queries to run, in the order the recipe lists them.</summary>
    public required IReadOnlyList<SweepQuery> Queries { get; init; }

    /// <summary>Patterns applied to each candidate's published text. May be empty.</summary>
    public IReadOnlyList<ClaimPattern> Patterns { get; init; } = [];

    /// <summary>Thresholds below which a numeric move is not reported.</summary>
    public SweepSensitivity Sensitivity { get; init; } = new();

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Reads a recipe and refuses one that would not run. Validation is up front and total
    /// because the alternative is a weekly job that half-runs: a recipe naming a registry
    /// nobody collects from, or carrying a pattern that does not compile, should fail before
    /// the first request rather than after some of them.
    /// </summary>
    /// <param name="json">The recipe document.</param>
    /// <returns>The validated recipe.</returns>
    /// <exception cref="SweepException">The document is not a runnable recipe.</exception>
    public static SweepRecipe Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        SweepRecipe recipe;
        try
        {
            recipe = JsonSerializer.Deserialize<SweepRecipe>(json, ReadOptions)
                ?? throw new SweepException("The recipe is empty.");
        }
        catch (JsonException error)
        {
            throw new SweepException($"The recipe is not readable: {error.Message}", error);
        }

        recipe.Validate();

        return recipe;
    }

    /// <summary>
    /// Checks the recipe describes a runnable sweep.
    /// </summary>
    /// <exception cref="SweepException">It does not.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new SweepException("The recipe has no name.");
        }

        if (Queries is null || Queries.Count == 0)
        {
            throw new SweepException($"Recipe '{Name}' has no queries, so a run would read nothing.");
        }

        HashSet<string> queryIds = new(StringComparer.Ordinal);
        foreach (SweepQuery query in Queries)
        {
            if (string.IsNullOrWhiteSpace(query.Id))
            {
                throw new SweepException($"Recipe '{Name}' has a query with no id.");
            }

            if (!queryIds.Add(query.Id))
            {
                throw new SweepException(
                    $"Recipe '{Name}' uses the query id '{query.Id}' twice. Ids are recorded on "
                    + "the candidates a query surfaces, so they have to tell them apart.");
            }

            if (string.IsNullOrWhiteSpace(query.Term))
            {
                throw new SweepException($"Query '{query.Id}' has no term.");
            }

            if (query.Take is < 1 or > MaximumTake)
            {
                throw new SweepException(
                    $"Query '{query.Id}' asks for {query.Take} results; the registries cap a page "
                    + $"at {MaximumTake}.");
            }
        }

        HashSet<string> patternIds = new(StringComparer.Ordinal);
        foreach (ClaimPattern pattern in Patterns ?? [])
        {
            if (string.IsNullOrWhiteSpace(pattern.Id))
            {
                throw new SweepException($"Recipe '{Name}' has a pattern with no id.");
            }

            if (!patternIds.Add(pattern.Id))
            {
                throw new SweepException($"Recipe '{Name}' uses the pattern id '{pattern.Id}' twice.");
            }
        }
    }

    /// <summary>
    /// The largest page every registry in <see cref="SweepSource"/> serves. Asking for more
    /// silently gets fewer from some of them, which would read as candidates disappearing.
    /// </summary>
    public const int MaximumTake = 100;
}
