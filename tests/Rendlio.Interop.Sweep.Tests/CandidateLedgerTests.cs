using Rendlio.Interop.Sweep;
using Xunit;

namespace Rendlio.Interop.Sweep.Tests;

/// <summary>
/// Pins the append-only file. It is the only part of a run that outlives the run, so what it
/// has to survive is not one write but a year of them: earlier runs stay readable, the run
/// before this one is findable, and a line that cannot be read stops the run instead of
/// quietly becoming a new entrant next week.
/// </summary>
public sealed class CandidateLedgerTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "sweep-ledger-" + Guid.NewGuid().ToString("N"));

    private string Ledger => Path.Combine(directory, "candidates.jsonl");

    [Fact]
    public void A_ledger_that_does_not_exist_yet_is_a_first_run()
    {
        Assert.Empty(CandidateLedger.Read(Path.Combine(directory, "never-written.jsonl")));
    }

    [Fact]
    public void A_run_survives_the_round_trip_field_for_field()
    {
        Observation written = Sighting.Of("alpha", SweepSource.NuGet, "3.1.4", downloads: 900);

        CandidateLedger.Append(Ledger, CandidateLedger.Stamp("run-1", Stamped, [written]));

        SweepRecord record = Assert.Single(CandidateLedger.Read(Ledger));

        Assert.Equal("run-1", record.Run);
        Assert.Equal(Stamped, record.ObservedUtc);
        Assert.Equal(written, record.Observation);
    }

    [Fact]
    public void Appending_a_run_keeps_every_run_before_it()
    {
        CandidateLedger.Append(Ledger, CandidateLedger.Stamp("run-1", Stamped, [Sighting.Of("alpha")]));
        CandidateLedger.Append(Ledger, CandidateLedger.Stamp("run-2", Stamped, [Sighting.Of("beta")]));

        IReadOnlyList<SweepRecord> ledger = CandidateLedger.Read(Ledger);

        Assert.Equal(["run-1", "run-2"], ledger.Select(record => record.Run));
        Assert.Equal(["crates.io:alpha", "crates.io:beta"], ledger.Select(record => record.Observation.Id));
    }

    [Fact]
    public void The_previous_run_is_the_last_one_written_and_only_that_one()
    {
        CandidateLedger.Append(Ledger, CandidateLedger.Stamp("run-1", Stamped, [Sighting.Of("alpha")]));
        CandidateLedger.Append(
            Ledger,
            CandidateLedger.Stamp("run-2", Stamped, [Sighting.Of("beta"), Sighting.Of("gamma")]));

        IReadOnlyList<Observation> previous = CandidateLedger.LatestRun(CandidateLedger.Read(Ledger));

        Assert.Equal(["crates.io:beta", "crates.io:gamma"], previous.Select(observation => observation.Id));
    }

    [Fact]
    public void A_replay_under_a_reused_run_id_supersedes_rather_than_compounds()
    {
        // The documented reason --run exists is to replay a run or repeat one that failed
        // partway, and doing that against the same ledger appends a second block under the
        // same id. Selecting by id alone would hand the next run both blocks: every candidate
        // twice. Nothing downstream can join two runs on an identity that is no longer unique.
        CandidateLedger.Append(
            Ledger, CandidateLedger.Stamp("run-1", Stamped, [Sighting.Of("alpha"), Sighting.Of("beta")]));

        CandidateLedger.Append(
            Ledger, CandidateLedger.Stamp("run-1", Replayed, [Sighting.Of("alpha"), Sighting.Of("gamma")]));

        IReadOnlyList<Observation> previous = CandidateLedger.LatestRun(CandidateLedger.Read(Ledger));

        Assert.Equal(["crates.io:alpha", "crates.io:gamma"], previous.Select(observation => observation.Id));
    }

    [Fact]
    public void A_run_that_replays_is_still_a_run_the_next_one_can_be_compared_against()
    {
        // The end of the same story: the replayed block is what the next run diffs against,
        // and it does so without anything having been rewritten.
        CandidateLedger.Append(Ledger, CandidateLedger.Stamp("run-1", Stamped, [Sighting.Of("alpha")]));
        CandidateLedger.Append(Ledger, CandidateLedger.Stamp("run-1", Replayed, [Sighting.Of("alpha")]));

        SweepDiff diff = SweepDiff.Between(
            CandidateLedger.LatestRun(CandidateLedger.Read(Ledger)), [Sighting.Of("alpha")]);

        Assert.True(diff.IsEmpty);

        // Both blocks are still on disk. Superseding is a reading rule, not a deletion.
        Assert.Equal(2, File.ReadAllLines(Ledger).Length);
    }

    [Fact]
    public void An_older_block_under_the_same_run_id_is_not_pulled_back_in()
    {
        // A re-used id further back in the file belongs to a different run, whatever it is
        // called. Everything that block saw and the last one did not would otherwise arrive as
        // candidates that had gone missing.
        CandidateLedger.Append(Ledger, CandidateLedger.Stamp("run-1", Stamped, [Sighting.Of("alpha")]));
        CandidateLedger.Append(Ledger, CandidateLedger.Stamp("run-2", Replayed, [Sighting.Of("beta")]));
        CandidateLedger.Append(Ledger, CandidateLedger.Stamp("run-1", Later, [Sighting.Of("gamma")]));

        Assert.Equal(
            ["crates.io:gamma"],
            CandidateLedger.LatestRun(CandidateLedger.Read(Ledger)).Select(observation => observation.Id));
    }

    [Fact]
    public void The_previous_run_comes_back_ordered_however_it_was_written()
    {
        CandidateLedger.Append(
            Ledger,
            CandidateLedger.Stamp("run-1", Stamped, [Sighting.Of("gamma"), Sighting.Of("alpha")]));

        Assert.Equal(
            ["crates.io:alpha", "crates.io:gamma"],
            CandidateLedger.LatestRun(CandidateLedger.Read(Ledger)).Select(observation => observation.Id));
    }

    [Fact]
    public void One_record_is_one_line()
    {
        // The file is grepped and tailed by whoever reads it, and an appended run has to be
        // readable without the writer having rewritten anything that came before.
        CandidateLedger.Append(
            Ledger,
            CandidateLedger.Stamp("run-1", Stamped, [Sighting.Of("alpha"), Sighting.Of("beta")]));

        Assert.Equal(2, File.ReadAllLines(Ledger).Length);
    }

    [Fact]
    public void The_file_starts_with_a_record_and_not_a_byte_order_mark()
    {
        // The reason one object per line is worth anything is that other tools can read it,
        // and a mark in front of the opening brace of line 1 is where most of them stop.
        // The framework default writes one, so this is a decision rather than an accident.
        CandidateLedger.Append(Ledger, CandidateLedger.Stamp("run-1", Stamped, [Sighting.Of("alpha")]));

        byte[] written = File.ReadAllBytes(Ledger);

        Assert.Equal((byte)'{', written[0]);
    }

    [Fact]
    public void A_blank_line_is_not_a_record()
    {
        CandidateLedger.Append(Ledger, CandidateLedger.Stamp("run-1", Stamped, [Sighting.Of("alpha")]));
        File.AppendAllText(Ledger, "\n\n");

        Assert.Single(CandidateLedger.Read(Ledger));
    }

    [Fact]
    public void A_line_that_is_not_a_record_stops_the_run()
    {
        // Skipping it would be worse than failing: the candidate on that line would read as a
        // new entrant on the next run, every run, for as long as the line stayed there.
        CandidateLedger.Append(Ledger, CandidateLedger.Stamp("run-1", Stamped, [Sighting.Of("alpha")]));
        File.AppendAllText(Ledger, "{ this is not a record\n");

        SweepException failure = Assert.Throws<SweepException>(() => CandidateLedger.Read(Ledger));

        Assert.Contains("Line 2", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Appending_creates_the_directory_it_was_pointed_at()
    {
        string nested = Path.Combine(directory, "runs", "candidates.jsonl");

        CandidateLedger.Append(nested, CandidateLedger.Stamp("run-1", Stamped, [Sighting.Of("alpha")]));

        Assert.True(File.Exists(nested));
    }

    private static readonly DateTimeOffset Stamped =
        new(2026, 8, 27, 6, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Replayed = Stamped.AddHours(1);

    private static readonly DateTimeOffset Later = Stamped.AddHours(2);

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
