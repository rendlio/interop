using Rendlio.Interop.Sweep;
using Rendlio.Interop.Sweep.Sources;
using Xunit;

namespace Rendlio.Interop.Sweep.Tests;

/// <summary>
/// Pins the whole of one run, and then the run after it. The other fixtures each hold one
/// piece — a recipe parses, a source projects, a ledger round-trips, a diff compares — and
/// every one of them can be green while the pieces disagree at the seams. What the watch is
/// asked for is a job that can be re-run, so this drives recipe to registry to ledger to
/// report twice over against a registry that did not move, and holds it to reporting so.
/// </summary>
public sealed class SweepRunEndToEndTests : IDisposable
{
    private const string Recipe =
        """
        {
          "name": "a-watch",
          "sensitivity": { "minDownloadsDelta": 25, "minStarsDelta": 5 },
          "queries": [
            { "id": "crates", "source": "CratesIo", "term": "spreadsheet" },
            { "id": "repos", "source": "GitHub", "term": "spreadsheet" }
          ],
          "patterns": [ { "id": "reads-sheets", "expression": "spreadsheet" } ]
        }
        """;

    private const string Crates =
        """
        {"crates":[{"name":"alpha","max_version":"1.0.0","description":"reads spreadsheets",
        "recent_downloads":40,"updated_at":"2026-08-20T10:00:00Z"}]}
        """;

    private const string Repos =
        """
        {"items":[{"full_name":"someone/beta","html_url":"https://github.com/someone/beta",
        "description":"a spreadsheet reader","stargazers_count":7,"pushed_at":"2026-08-21T09:30:00Z"}]}
        """;

    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "sweep-run-" + Guid.NewGuid().ToString("N"));

    private string Ledger => Path.Combine(directory, "candidates.jsonl");

    [Fact]
    public async Task A_first_run_reports_the_field_and_the_run_after_it_reports_no_changes()
    {
        // The acceptance criterion, end to end and in one place: a run produces the record set
        // and a diff against the run before it, and a re-run over registries that did not move
        // says so. Everything between the recipe text and the report is real here — only the
        // network is not.
        SweepDiff first = await RunAsync("run-1", Crates, Repos);

        Assert.Equal(
            ["crates.io:alpha", "github:someone/beta"],
            first.Entrants.Select(entrant => entrant.Id));

        // Not incidental: the pattern fired on text that came out of a registry payload rather
        // than out of a fixture, so the scanner is wired to what a source actually projects.
        Assert.Equal(
            ["reads-sheets"],
            Assert.Single(first.Entrants, entrant => entrant.Id == "crates.io:alpha").Claims);

        SweepDiff second = await RunAsync("run-2", Crates, Repos);

        Assert.True(second.IsEmpty);
        Assert.Equal(SweepDiff.NoChanges + Environment.NewLine, second.Report());
    }

    [Fact]
    public async Task A_run_after_a_version_bump_reports_the_bump_and_nothing_else()
    {
        // The other half of the same claim. "No changes" is only worth trusting from a run that
        // would have said something had there been something — and it has to say the one thing
        // that moved rather than re-reporting the candidate as new.
        await RunAsync("run-1", Crates, Repos);

        SweepDiff second = await RunAsync(
            "run-2", Crates.Replace("1.0.0", "1.1.0", StringComparison.Ordinal), Repos);

        Assert.Empty(second.Entrants);
        Assert.Empty(second.Unseen);

        ObservationChange change = Assert.Single(second.Changes);

        Assert.Equal(
            ("crates.io:alpha", "version", "1.0.0", "1.1.0"),
            (change.Id, change.Field, change.Before, change.After));
    }

    [Fact]
    public async Task A_move_smaller_than_the_recipe_asked_about_is_not_a_change()
    {
        // The thresholds travel from the recipe text through the runner into the diff. A
        // download count that drifts by less than the recipe's floor has to stay out of the
        // report, or the entrants are buried under drift every week.
        await RunAsync("run-1", Crates, Repos);

        SweepDiff second = await RunAsync(
            "run-2",
            Crates.Replace("\"recent_downloads\":40", "\"recent_downloads\":50", StringComparison.Ordinal),
            Repos);

        Assert.True(second.IsEmpty);
    }

    [Fact]
    public async Task A_run_that_is_cancelled_does_not_come_back_with_what_it_had_so_far()
    {
        // What is at stake is the ledger. A cancelled run that returned its partial results
        // would have them appended under one stamp as though they were a whole run, and every
        // candidate it never reached would read as having disappeared next week.
        SweepRunner runner = new(
            [new CratesIoSource(new CancellingTransport()), new GitHubSource(new UnusedTransport())]);

        using CancellationTokenSource stopping = new();
        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.CollectAsync(SweepRecipe.Parse(Recipe), stopping.Token));

        Assert.Empty(CandidateLedger.Read(Ledger));
    }

    [Fact]
    public async Task The_same_run_written_twice_is_the_same_bytes()
    {
        // Reproducibility at the one layer where it can be checked as bytes. The ledger is read
        // by the next run, so if a record could serialize two ways, a re-run would compare
        // itself against a file that no longer matched what it had written.
        IReadOnlyList<Observation> observed = await CollectAsync(Crates, Repos);

        string first = Path.Combine(directory, "first.jsonl");
        string again = Path.Combine(directory, "again.jsonl");
        DateTimeOffset stamped = new(2026, 8, 27, 6, 0, 0, TimeSpan.Zero);

        CandidateLedger.Append(first, CandidateLedger.Stamp("run-1", stamped, observed));
        CandidateLedger.Append(again, CandidateLedger.Stamp("run-1", stamped, observed));

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(again));
    }

    /// <summary>
    /// One whole invocation, in the order the entry point does it: collect, read the run
    /// before, compare, then append. The ordering is part of what is pinned — appending first
    /// would leave every run comparing itself against itself.
    /// </summary>
    private async Task<SweepDiff> RunAsync(string run, string crates, string repos)
    {
        SweepRecipe recipe = SweepRecipe.Parse(Recipe);
        IReadOnlyList<Observation> observed = await CollectAsync(crates, repos);
        IReadOnlyList<Observation> previous = CandidateLedger.LatestRun(CandidateLedger.Read(Ledger));

        SweepDiff diff = SweepDiff.Between(previous, observed, recipe.Sensitivity);

        CandidateLedger.Append(Ledger, CandidateLedger.Stamp(run, Observed(run), observed));

        return diff;
    }

    private static async Task<IReadOnlyList<Observation>> CollectAsync(string crates, string repos) =>
        await new SweepRunner(
            [new CratesIoSource(new RecordingTransport(crates)), new GitHubSource(new RecordingTransport(repos))])
            .CollectAsync(SweepRecipe.Parse(Recipe));

    /// <summary>
    /// A distinct moment per run. A run is bounded in the ledger by the pair it stamps on every
    /// record, so two runs sharing an instant would be read back as one append.
    /// </summary>
    private static DateTimeOffset Observed(string run) =>
        new DateTimeOffset(2026, 8, 27, 6, 0, 0, TimeSpan.Zero).AddMinutes(run[^1]);

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
