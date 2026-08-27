namespace Rendlio.Interop.Sweep;

/// <summary>
/// A public registry a run reads from. The value is part of every observation's identity, so
/// renaming one renames every candidate that came from it — which is why the wire name is
/// pinned here rather than taken from the enum member.
/// </summary>
public enum SweepSource
{
    /// <summary>The crates.io registry API.</summary>
    CratesIo,

    /// <summary>The nuget.org v3 API, reached through its service index.</summary>
    NuGet,

    /// <summary>The npm registry API.</summary>
    Npm,

    /// <summary>The PyPI project API.</summary>
    PyPi,

    /// <summary>The GitHub REST API.</summary>
    GitHub,
}

/// <summary>Wire names for <see cref="SweepSource"/>, used in identities and reports.</summary>
public static class SweepSources
{
    /// <summary>The stable short name a source contributes to an observation identity.</summary>
    public static string Name(SweepSource source) => source switch
    {
        SweepSource.CratesIo => "crates.io",
        SweepSource.NuGet => "nuget",
        SweepSource.Npm => "npm",
        SweepSource.PyPi => "pypi",
        SweepSource.GitHub => "github",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown source."),
    };
}

/// <summary>
/// One candidate as one run saw it: the normalized record every source projects into, so a
/// diff can compare a crate against a crate and a package against a package without knowing
/// which registry either came from.
/// </summary>
/// <remarks>
/// Every field is metadata a registry publishes about itself. Nothing here is derived from
/// contacting a project, and nothing here is a number this repository publishes.
/// </remarks>
public sealed record Observation
{
    /// <summary>
    /// Stable identity, <c>source:name</c> lower-cased. It is what a diff joins two runs on,
    /// so it is built from the two things a registry does not let change underneath a
    /// package — which registry it is in, and what it is called there.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>The registry this record was read from.</summary>
    public required SweepSource Source { get; init; }

    /// <summary>The candidate's name in that registry.</summary>
    public required string Name { get; init; }

    /// <summary>Where a reader can see the same facts for themselves.</summary>
    public required string Url { get; init; }

    /// <summary>Latest version the registry reported, or null where it publishes none.</summary>
    public string? Version { get; init; }

    /// <summary>The registry's own one-line description, unedited.</summary>
    public string? Description { get; init; }

    /// <summary>Downloads the registry reported, on whatever window that registry uses.</summary>
    public long? Downloads { get; init; }

    /// <summary>Stars, where the source has them.</summary>
    public long? Stars { get; init; }

    /// <summary>When the registry says the candidate last moved.</summary>
    public DateTimeOffset? Updated { get; init; }

    /// <summary>
    /// Which recipe queries surfaced this candidate, sorted. A candidate found twice is one
    /// record naming both queries rather than two records.
    /// </summary>
    public IReadOnlyList<string> Queries { get; init; } = [];

    /// <summary>
    /// Ids of the recipe patterns that matched this candidate's published text, sorted. The
    /// patterns live in the recipe; this records which of them fired, never what they say.
    /// </summary>
    public IReadOnlyList<string> Claims { get; init; } = [];

    /// <summary>Builds the identity two runs are joined on.</summary>
    public static string Identify(SweepSource source, string name) =>
        $"{SweepSources.Name(source)}:{name.ToLowerInvariant()}";

    /// <summary>
    /// Whether two sightings say the same thing.
    /// </summary>
    /// <remarks>
    /// Written out rather than left to the compiler. A record compares its members with the
    /// default comparer, and for the two lists here that is reference equality — so a record
    /// read back from the ledger would never equal the one that was written, however
    /// identical. This type exists to be compared across runs; equality that quietly said no
    /// would be a trap for the next person to reach for it.
    /// </remarks>
    /// <param name="other">The other sighting.</param>
    /// <returns>True when every field matches.</returns>
    public bool Equals(Observation? other) =>
        other is not null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && Source == other.Source
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(Url, other.Url, StringComparison.Ordinal)
        && string.Equals(Version, other.Version, StringComparison.Ordinal)
        && string.Equals(Description, other.Description, StringComparison.Ordinal)
        && Downloads == other.Downloads
        && Stars == other.Stars
        && Updated == other.Updated
        && Queries.SequenceEqual(other.Queries, StringComparer.Ordinal)
        && Claims.SequenceEqual(other.Claims, StringComparer.Ordinal);

    /// <summary>
    /// Hashes the fields that identify a sighting. The lists are left out on purpose: equal
    /// records still hash equal, and the fields above already separate them.
    /// </summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(Id, Source, Name, Url, Version, Downloads, Stars, Updated);
}
