using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rendlio.Interop.Sweep;

/// <summary>
/// One observation as one run recorded it. The run stamp travels with the record rather than
/// in a header, because the file it lives in is only ever appended to: a reader can start
/// anywhere in it and still know which run it is reading.
/// </summary>
/// <param name="Run">The run that recorded this.</param>
/// <param name="ObservedUtc">When that run read the registry.</param>
/// <param name="Observation">What it read.</param>
public sealed record SweepRecord(string Run, DateTimeOffset ObservedUtc, Observation Observation);

/// <summary>
/// The append-only record of everything a watch has ever seen, one JSON object per line.
/// </summary>
/// <remarks>
/// Append-only is the discipline, not an implementation detail. A watch that rewrote its file
/// each run would answer "what is out there today" and lose the only question that matters
/// afterwards — when did this first appear, and what has it done since. So nothing here
/// deletes, nothing here rewrites, and a candidate that stops showing up stays in the file.
/// </remarks>
public static class CandidateLedger
{
    /// <summary>
    /// UTF-8 with no byte-order mark. <see cref="Encoding.UTF8"/> emits one when it creates a
    /// file, and that mark lands in front of the opening brace of line 1 — where every
    /// ordinary reader of a file like this, from jq to a three-line script, chokes on it. The
    /// point of one object per line is that other tools can read it.
    /// </summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions LineFormat = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Reads the ledger. A path that does not exist yet is the first run, not a failure.
    /// </summary>
    /// <param name="path">The ledger file.</param>
    /// <returns>Every record in the file, in the order it was written.</returns>
    /// <exception cref="SweepException">A line in the file is not a record.</exception>
    public static IReadOnlyList<SweepRecord> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return [];
        }

        List<SweepRecord> records = [];
        int number = 0;

        foreach (string line in File.ReadLines(path))
        {
            number++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                records.Add(JsonSerializer.Deserialize<SweepRecord>(line, LineFormat)
                    ?? throw new SweepException($"Line {number} of the ledger is null."));
            }
            catch (JsonException error)
            {
                // Refuse rather than skip. A ledger that silently drops a line it cannot read
                // would report the candidate on that line as a new entrant on the next run.
                throw new SweepException(
                    $"Line {number} of the ledger is not a record: {error.Message}", error);
            }
        }

        return records;
    }

    /// <summary>
    /// What the previous run saw: the last block of records appended to the file.
    /// </summary>
    /// <remarks>
    /// The run identifier alone does not answer this, because a caller may re-use one — the
    /// documented reason <c>--run</c> exists is to replay a run or repeat one that failed
    /// partway, and doing that against the same ledger appends a second block under the same
    /// id. Selecting by id would then return both blocks at once: the same candidate twice,
    /// plus anything the first block saw and the replay did not. So the block is bounded by
    /// the pair the whole of one append shares — its id and the moment it read the registries
    /// — which <see cref="Stamp"/> puts on every record of a run and which a later append
    /// under a re-used id does not match. A replay therefore supersedes rather than compounds,
    /// which is what a replay means, and it does so without rewriting a line.
    /// </remarks>
    /// <param name="records">The ledger, as <see cref="Read"/> returned it.</param>
    /// <returns>That run, ordered by identity; empty when the ledger is.</returns>
    public static IReadOnlyList<Observation> LatestRun(IReadOnlyList<SweepRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return [];
        }

        SweepRecord last = records[^1];
        int start = records.Count - 1;

        while (start > 0 && SameAppend(records[start - 1], last))
        {
            start--;
        }

        return
        [
            .. records.Skip(start)
                .Select(record => record.Observation)
                .OrderBy(observation => observation.Id, StringComparer.Ordinal),
        ];
    }

    private static bool SameAppend(SweepRecord record, SweepRecord last) =>
        string.Equals(record.Run, last.Run, StringComparison.Ordinal)
        && record.ObservedUtc == last.ObservedUtc;

    /// <summary>Appends a run to the ledger, creating the file and its directory if needed.</summary>
    /// <param name="path">The ledger file.</param>
    /// <param name="records">The run to append.</param>
    public static void Append(string path, IEnumerable<SweepRecord> records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(records);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        StringBuilder lines = new();
        foreach (SweepRecord record in records)
        {
            lines.Append(JsonSerializer.Serialize(record, LineFormat)).Append('\n');
        }

        // Append, never rewrite, and never through a mode that would truncate on the way in.
        File.AppendAllText(path, lines.ToString(), Utf8);
    }

    /// <summary>Stamps a run onto the observations it produced.</summary>
    /// <param name="run">The run identifier.</param>
    /// <param name="observedUtc">When the run read the registries.</param>
    /// <param name="observations">What it read.</param>
    /// <returns>The records to append.</returns>
    public static IReadOnlyList<SweepRecord> Stamp(
        string run, DateTimeOffset observedUtc, IEnumerable<Observation> observations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(run);
        ArgumentNullException.ThrowIfNull(observations);

        return [.. observations.Select(observation => new SweepRecord(run, observedUtc, observation))];
    }
}
