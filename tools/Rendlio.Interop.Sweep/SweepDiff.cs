using System.Globalization;
using System.Text;

namespace Rendlio.Interop.Sweep;

/// <summary>One field of one candidate that moved between two runs.</summary>
/// <param name="Id">The candidate.</param>
/// <param name="Field">Which field moved.</param>
/// <param name="Before">What the previous run recorded, or null where it recorded nothing.</param>
/// <param name="After">What this run recorded, or null where it recorded nothing.</param>
public sealed record ObservationChange(string Id, string Field, string? Before, string? After);

/// <summary>
/// What one run found that the previous one did not.
/// </summary>
/// <remarks>
/// This is the difference between a watch and a search. A run that reported the whole field
/// every week would be read once and skimmed thereafter, which is the same as not running it:
/// the thing worth a reader is the short list of what moved.
/// </remarks>
public sealed record SweepDiff
{
    /// <summary>Candidates this run saw that the previous run did not.</summary>
    public required IReadOnlyList<Observation> Entrants { get; init; }

    /// <summary>Fields that moved on candidates both runs saw.</summary>
    public required IReadOnlyList<ObservationChange> Changes { get; init; }

    /// <summary>
    /// Candidates the previous run saw and this one did not. Not the same as gone: a query
    /// that returns a page of results drops the tail when the page fills, and a renamed or
    /// yanked package leaves the same way. It is a prompt to look, not a finding.
    /// </summary>
    public required IReadOnlyList<string> Unseen { get; init; }

    /// <summary>Whether the run found nothing to report.</summary>
    public bool IsEmpty => Entrants.Count == 0 && Changes.Count == 0 && Unseen.Count == 0;

    /// <summary>What a run with nothing to report says, once, so it can be grepped for.</summary>
    public const string NoChanges = "no changes";

    /// <summary>
    /// Compares two runs.
    /// </summary>
    /// <param name="previous">The previous run, or empty for a first run.</param>
    /// <param name="current">This run.</param>
    /// <param name="sensitivity">How far a number has to move to count.</param>
    /// <returns>The difference, ordered by identity so two equal runs render identically.</returns>
    public static SweepDiff Between(
        IReadOnlyList<Observation> previous,
        IReadOnlyList<Observation> current,
        SweepSensitivity? sensitivity = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        SweepSensitivity thresholds = sensitivity ?? new SweepSensitivity();

        Dictionary<string, Observation> before = Index(previous);

        List<Observation> entrants = [];
        List<ObservationChange> changes = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (Observation after in current.OrderBy(observation => observation.Id, StringComparer.Ordinal))
        {
            seen.Add(after.Id);

            if (!before.TryGetValue(after.Id, out Observation? was))
            {
                entrants.Add(after);

                continue;
            }

            Compare(was, after, thresholds, changes);
        }

        return new SweepDiff
        {
            Entrants = entrants,
            Changes = changes,
            Unseen =
            [
                .. before.Keys.Where(id => !seen.Contains(id)).OrderBy(id => id, StringComparer.Ordinal),
            ],
        };
    }

    /// <summary>
    /// Renders the difference for a person to read.
    /// </summary>
    /// <returns>The report, ending in a newline.</returns>
    public string Report()
    {
        if (IsEmpty)
        {
            return NoChanges + Environment.NewLine;
        }

        StringBuilder report = new();

        if (Entrants.Count > 0)
        {
            report.Append("new (").Append(Count(Entrants.Count)).AppendLine("):");

            foreach (Observation entrant in Entrants)
            {
                report.Append("  ").Append(entrant.Id);

                if (entrant.Version is not null)
                {
                    report.Append("  ").Append(entrant.Version);
                }

                report.Append("  ").AppendLine(entrant.Url);

                if (entrant.Claims.Count > 0)
                {
                    report.Append("    matched: ").AppendLine(string.Join(", ", entrant.Claims));
                }
            }
        }

        if (Changes.Count > 0)
        {
            report.Append("changed (").Append(Count(Changes.Count)).AppendLine("):");

            foreach (ObservationChange change in Changes)
            {
                report.Append("  ").Append(change.Id).Append("  ").Append(change.Field)
                    .Append(": ").Append(change.Before ?? "-")
                    .Append(" -> ").AppendLine(change.After ?? "-");
            }
        }

        if (Unseen.Count > 0)
        {
            report.Append("not seen this run (").Append(Count(Unseen.Count)).AppendLine("):");

            foreach (string id in Unseen)
            {
                report.Append("  ").AppendLine(id);
            }
        }

        return report.ToString();
    }

    /// <summary>
    /// Keys the previous run by identity, refusing a set that carries one twice.
    /// </summary>
    /// <remarks>
    /// A run records each candidate once, so a repeat means the input is not one run — a
    /// hand-edited ledger, or two appends written under one stamp. Building the index with
    /// <c>ToDictionary</c> would end that in an <c>ArgumentException</c> nothing catches,
    /// which for a job that runs unattended is a stack trace where a sentence belongs.
    /// </remarks>
    private static Dictionary<string, Observation> Index(IReadOnlyList<Observation> previous)
    {
        Dictionary<string, Observation> index = new(StringComparer.Ordinal);

        foreach (Observation observation in previous)
        {
            if (!index.TryAdd(observation.Id, observation))
            {
                throw new SweepException(
                    $"The previous run carries {observation.Id} twice. A run records each "
                    + "candidate once, so the ledger holds more than one run under a single stamp.");
            }
        }

        return index;
    }

    private static void Compare(
        Observation before, Observation after, SweepSensitivity thresholds, List<ObservationChange> changes)
    {
        // Description and the query list are deliberately not compared. A description is prose
        // and churns; the query list describes the recipe rather than the candidate, so an
        // edit to the recipe would otherwise read as every candidate having changed. What a
        // pattern found in the description is compared, and that is the part that is signal.
        Text(before.Id, "url", before.Url, after.Url, changes);
        Text(before.Id, "version", before.Version, after.Version, changes);
        Number(before.Id, "downloads", before.Downloads, after.Downloads, thresholds.MinDownloadsDelta, changes);
        Number(before.Id, "stars", before.Stars, after.Stars, thresholds.MinStarsDelta, changes);
        Text(before.Id, "updated", Moment(before.Updated), Moment(after.Updated), changes);
        Text(before.Id, "matched", Joined(before.Claims), Joined(after.Claims), changes);
    }

    private static void Text(
        string id, string field, string? before, string? after, List<ObservationChange> changes)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changes.Add(new ObservationChange(id, field, before, after));
        }
    }

    private static void Number(
        string id, string field, long? before, long? after, long threshold, List<ObservationChange> changes)
    {
        if (before == after)
        {
            return;
        }

        // A number that appears or disappears is reported however small it is: the registry
        // started or stopped publishing it, which no threshold is about.
        if (before is long was && after is long now && Math.Abs(now - was) < threshold)
        {
            return;
        }

        changes.Add(new ObservationChange(id, field, Count(before), Count(after)));
    }

    private static string? Count(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    private static string Count(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string? Moment(DateTimeOffset? value) =>
        value?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string? Joined(IReadOnlyList<string> values) =>
        values.Count == 0 ? null : string.Join(", ", values);
}
