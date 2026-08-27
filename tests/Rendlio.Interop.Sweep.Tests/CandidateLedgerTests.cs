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

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
