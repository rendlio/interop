using System.Globalization;
using Rendlio.Interop.Sweep;
using Rendlio.Interop.Sweep.Sources;
using Xunit;

namespace Rendlio.Interop.Sweep.Tests;

/// <summary>
/// Pins the recipe contract and what a run makes of it. A recipe is edited by hand between
/// runs, so the cases worth pinning are the ways a hand edit goes wrong — and the rule that
/// they are all caught before the first request rather than after some of them.
/// </summary>
public sealed class RecipeAndRunnerTests
{
    private const string Minimal =
        """
        {
          "name": "a-recipe",
          "queries": [ { "id": "q1", "source": "CratesIo", "term": "pdf" } ]
        }
        """;

    [Fact]
    public void A_recipe_reads_back_as_it_was_written()
    {
        SweepRecipe recipe = SweepRecipe.Parse(
            """
            {
              "name": "a-recipe",
              "sensitivity": { "minDownloadsDelta": 500, "minStarsDelta": 25 },
              "queries": [
                { "id": "q1", "source": "GitHub", "term": "ooxml", "take": 20 }
              ],
              "patterns": [ { "id": "p1", "expression": "layout" } ]
            }
            """);

        SweepQuery query = Assert.Single(recipe.Queries);

        Assert.Equal("a-recipe", recipe.Name);
        Assert.Equal((SweepSource.GitHub, "ooxml", 20), (query.Source, query.Term, query.Take));
        Assert.Equal("p1", Assert.Single(recipe.Patterns).Id);
        Assert.Equal(new SweepSensitivity(500, 25), recipe.Sensitivity);
    }

    [Fact]
    public void A_recipe_that_says_nothing_about_thresholds_reports_every_move()
    {
        Assert.Equal(new SweepSensitivity(), SweepRecipe.Parse(Minimal).Sensitivity);
    }

    [Theory]
    [InlineData("""{ "queries": [ { "id": "q1", "source": "Npm", "term": "x" } ] }""")]
    [InlineData("""{ "name": "a-recipe", "queries": [] }""")]
    [InlineData("""{ "name": "a-recipe", "queries": [ { "id": "", "source": "Npm", "term": "x" } ] }""")]
    [InlineData("""{ "name": "a-recipe", "queries": [ { "id": "q1", "source": "Npm", "term": " " } ] }""")]
    [InlineData("""{ "name": "a-recipe", "queries": [ { "id": "q1", "source": "Npm", "term": "x", "take": 0 } ] }""")]
    [InlineData("""{ "name": "a-recipe", "queries": [ { "id": "q1", "source": "Npm", "term": "x", "take": 5000 } ] }""")]
    [InlineData("not json at all")]
    public void A_recipe_that_would_not_run_is_refused_before_anything_runs(string json)
    {
        Assert.Throws<SweepException>(() => SweepRecipe.Parse(json));
    }

    [Theory]
    [InlineData(SweepRecipe.MaximumTake, true)]
    [InlineData(SweepRecipe.MaximumTake + 1, false)]
    public void The_largest_page_the_registries_serve_is_allowed_and_one_more_is_not(int take, bool allowed)
    {
        // The cap is on the boundary rather than near it, because the failure it exists to
        // prevent is silent: a registry handed a page size it does not serve returns a smaller
        // page instead of refusing, and the candidates past the end read as having disappeared.
        // A cap that was off by one would let exactly that through on every run.
        string json =
            $$"""
            {
              "name": "a-recipe",
              "queries": [ { "id": "q1", "source": "CratesIo", "term": "pdf", "take": {{take}} } ]
            }
            """;

        if (allowed)
        {
            Assert.Equal(take, Assert.Single(SweepRecipe.Parse(json).Queries).Take);

            return;
        }

        SweepException failure = Assert.Throws<SweepException>(() => SweepRecipe.Parse(json));

        Assert.Contains(
            SweepRecipe.MaximumTake.ToString(CultureInfo.InvariantCulture),
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Two_queries_cannot_share_an_id()
    {
        // Query ids are recorded on the candidates a query surfaced, so a shared one makes the
        // record unable to say which query found what.
        SweepException failure = Assert.Throws<SweepException>(() => SweepRecipe.Parse(
            """
            {
              "name": "a-recipe",
              "queries": [
                { "id": "q1", "source": "Npm", "term": "x" },
                { "id": "q1", "source": "CratesIo", "term": "y" }
              ]
            }
            """));

        Assert.Contains("twice", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pattern_that_does_not_compile_fails_validation_and_not_only_the_run()
    {
        // Validation is what a caller reaches for to ask whether a recipe is sound, and an
        // expression that will not compile is the commonest way one is not. Leaving it to the
        // runner would mean a recipe could be validated, pass, and then fail on use.
        SweepException failure = Assert.Throws<SweepException>(() => SweepRecipe.Parse(
            """
            {
              "name": "a-recipe",
              "queries": [ { "id": "q1", "source": "Npm", "term": "x" } ],
              "patterns": [ { "id": "p1", "expression": "(unclosed" } ]
            }
            """));

        Assert.Contains("p1", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pattern_that_is_not_an_expression_is_refused_when_the_scanner_is_built()
    {
        SweepException failure = Assert.Throws<SweepException>(
            () => new ClaimScanner([new ClaimPattern("p1", "(unclosed")]));

        Assert.Contains("p1", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pattern_matches_what_a_registry_publishes_about_a_candidate()
    {
        ClaimScanner scanner = new(
        [
            new ClaimPattern("mentions-layout", "layout"),
            new ClaimPattern("mentions-shaping", "shaping"),
        ]);

        Assert.Equal(["mentions-layout"], scanner.Scan("sample-crate a from-scratch LAYOUT engine"));
        Assert.Empty(scanner.Scan("sample-crate something else entirely"));
    }

    [Fact]
    public void Matched_pattern_ids_come_back_sorted()
    {
        // A run whose matched list depended on recipe order would report a change on every
        // candidate the next time the recipe was reordered.
        ClaimScanner scanner = new([new ClaimPattern("z", "a"), new ClaimPattern("a", "a")]);

        Assert.Equal(["a", "z"], scanner.Scan("a"));
    }

    [Fact]
    public async Task A_recipe_naming_a_registry_the_run_cannot_read_fails_before_the_first_request()
    {
        // Half a sweep is worse than none: everything the missing registry would have
        // contributed would read as having disappeared.
        SweepRunner runner = new([new CratesIoSource(new UnusedTransport())]);

        SweepException failure = await Assert.ThrowsAsync<SweepException>(() => runner.CollectAsync(
            SweepRecipe.Parse(
                """
                {
                  "name": "a-recipe",
                  "queries": [
                    { "id": "q1", "source": "CratesIo", "term": "pdf" },
                    { "id": "q2", "source": "GitHub", "term": "pdf" }
                  ]
                }
                """)));

        Assert.Contains("q2", failure.Message, StringComparison.Ordinal);
        Assert.Contains("github", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_collectors_for_one_registry_is_refused()
    {
        UnusedTransport transport = new();

        Assert.Throws<SweepException>(
            () => new SweepRunner([new NpmSource(transport), new NpmSource(transport)]));
    }

    [Fact]
    public async Task A_candidate_two_queries_found_is_one_record_naming_both()
    {
        // Otherwise the same crate appears twice, and a diff joins two runs on an identity
        // that is no longer unique.
        RecordingTransport transport = new(
            """{"crates":[{"name":"sample-crate","max_version":"0.3.0","description":"a layout engine"}]}""");

        SweepRunner runner = new([new CratesIoSource(transport)]);

        IReadOnlyList<Observation> observed = await runner.CollectAsync(SweepRecipe.Parse(
            """
            {
              "name": "a-recipe",
              "queries": [
                { "id": "by-keyword", "source": "CratesIo", "term": "pdf" },
                { "id": "by-dependency", "source": "CratesIo", "term": "layout" }
              ],
              "patterns": [ { "id": "mentions-layout", "expression": "layout" } ]
            }
            """));

        Observation crate = Assert.Single(observed);

        Assert.Equal(["by-dependency", "by-keyword"], crate.Queries);
        Assert.Equal(["mentions-layout"], crate.Claims);
    }

    [Fact]
    public async Task A_run_returns_its_candidates_ordered()
    {
        RecordingTransport transport = new(
            """{"crates":[{"name":"gamma"},{"name":"alpha"},{"name":"beta"}]}""");

        IReadOnlyList<Observation> observed = await new SweepRunner([new CratesIoSource(transport)])
            .CollectAsync(SweepRecipe.Parse(Minimal));

        Assert.Equal(
            ["crates.io:alpha", "crates.io:beta", "crates.io:gamma"],
            observed.Select(candidate => candidate.Id));
    }

    [Theory]
    [InlineData("--recipe")]
    [InlineData("--ledger")]
    public void A_run_has_to_be_told_both_of_its_files(string given)
    {
        // Neither is defaulted. What a run watches is not this repository to hold, and where
        // it writes is not either.
        SweepException failure = Assert.Throws<SweepException>(
            () => SweepOptions.Parse([given, "a-path"], When));

        Assert.Contains(SweepOptions.Usage, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_option_the_tool_does_not_have_is_refused()
    {
        Assert.Throws<SweepException>(
            () => SweepOptions.Parse(["--recipe", "r", "--ledger", "l", "--publish", "yes"], When));
    }

    [Theory]
    [InlineData("--recipe")]
    [InlineData("--ledger")]
    [InlineData("--run")]
    public void An_option_given_twice_is_refused(string repeated)
    {
        // Quietly keeping the last would let a job write somewhere nobody reads from, for
        // weeks, without saying anything.
        string[] all = ["--recipe", "r", "--ledger", "l", "--run", "id"];

        SweepException failure = Assert.Throws<SweepException>(
            () => SweepOptions.Parse([.. all, repeated, "again"], When));

        Assert.Contains(repeated, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_option_with_no_value_is_refused()
    {
        Assert.Throws<SweepException>(() => SweepOptions.Parse(["--recipe", "r", "--ledger"], When));
    }

    [Fact]
    public void A_run_that_is_not_given_an_identifier_is_stamped_with_the_moment_it_started()
    {
        SweepOptions options = SweepOptions.Parse(["--recipe", "r", "--ledger", "l"], When);

        Assert.Equal("20260827T060000Z", options.Run);
    }

    [Fact]
    public void A_run_identifier_can_be_supplied_so_two_runs_are_comparable()
    {
        SweepOptions options = SweepOptions.Parse(["--recipe", "r", "--ledger", "l", "--run", "replay"], When);

        Assert.Equal(("r", "l", "replay"), (options.RecipePath, options.LedgerPath, options.Run));
    }

    private static readonly DateTimeOffset When = new(2026, 8, 27, 6, 0, 0, TimeSpan.Zero);
}
