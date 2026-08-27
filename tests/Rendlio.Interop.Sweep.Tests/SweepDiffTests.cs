using Rendlio.Interop.Sweep;
using Xunit;

namespace Rendlio.Interop.Sweep.Tests;

/// <summary>
/// Pins what a run reports. The diff is the deliverable — a watch that re-reported the whole
/// field every week would be skimmed and then ignored — so the cases that matter are the two
/// ends of it: a run that found something says exactly what, and a run that found nothing says
/// so rather than saying everything.
/// </summary>
public sealed class SweepDiffTests
{
    [Fact]
    public void A_second_run_over_unchanged_inputs_reports_no_changes()
    {
        // The claim the whole tool rests on. If an unchanged field could produce a line, every
        // report would carry noise and the real entrants would be lost in it.
        Observation[] run = [Sighting.Of("alpha", downloads: 10), Sighting.Of("beta", stars: 4)];

        SweepDiff diff = SweepDiff.Between(run, [.. run]);

        Assert.True(diff.IsEmpty);
        Assert.Equal(SweepDiff.NoChanges + Environment.NewLine, diff.Report());
    }

    [Fact]
    public void Two_sightings_that_say_the_same_thing_are_equal()
    {
        // Not what a record does on its own: it would compare the two lists by reference and
        // answer no. This type is compared across runs, so equality that quietly said no would
        // be waiting for whoever next reached for it.
        Observation one = Sighting.Of("alpha", claims: ["p1", "p2"]);
        Observation copy = one with { Queries = [.. one.Queries], Claims = [.. one.Claims] };

        Assert.Equal(one, copy);
        Assert.Equal(one.GetHashCode(), copy.GetHashCode());
        Assert.NotEqual(one, one with { Claims = ["p1"] });
    }

    [Fact]
    public void A_first_run_reports_everything_it_saw_as_new()
    {
        SweepDiff diff = SweepDiff.Between([], [Sighting.Of("alpha"), Sighting.Of("beta")]);

        Assert.Equal(
            ["crates.io:alpha", "crates.io:beta"],
            diff.Entrants.Select(entrant => entrant.Id));

        Assert.Empty(diff.Changes);
    }

    [Fact]
    public void A_candidate_the_previous_run_did_not_have_is_an_entrant()
    {
        SweepDiff diff = SweepDiff.Between(
            [Sighting.Of("alpha")],
            [Sighting.Of("alpha"), Sighting.Of("beta")]);

        Assert.Equal("crates.io:beta", Assert.Single(diff.Entrants).Id);
        Assert.Empty(diff.Changes);
        Assert.Empty(diff.Unseen);
    }

    [Fact]
    public void A_version_bump_is_reported_with_both_versions()
    {
        SweepDiff diff = SweepDiff.Between(
            [Sighting.Of("alpha", version: "1.0.0")],
            [Sighting.Of("alpha", version: "1.1.0")]);

        ObservationChange change = Assert.Single(diff.Changes);

        Assert.Equal(("crates.io:alpha", "version", "1.0.0", "1.1.0"),
            (change.Id, change.Field, change.Before, change.After));
    }

    [Fact]
    public void A_pattern_that_starts_matching_is_reported()
    {
        // The case a watch exists for: the candidate did not change name or version, but what
        // it says about itself now matches something the recipe is watching for.
        SweepDiff diff = SweepDiff.Between(
            [Sighting.Of("alpha")],
            [Sighting.Of("alpha", claims: ["pattern-one"])]);

        ObservationChange change = Assert.Single(diff.Changes);

        Assert.Equal("matched", change.Field);
        Assert.Null(change.Before);
        Assert.Equal("pattern-one", change.After);
    }

    [Fact]
    public void A_candidate_the_previous_run_had_and_this_one_did_not_is_reported_as_unseen()
    {
        SweepDiff diff = SweepDiff.Between(
            [Sighting.Of("alpha"), Sighting.Of("beta")],
            [Sighting.Of("alpha")]);

        Assert.Equal("crates.io:beta", Assert.Single(diff.Unseen));
        Assert.Empty(diff.Entrants);
    }

    [Theory]
    [InlineData(1_000, 1_040, false)]
    [InlineData(1_000, 1_500, true)]
    [InlineData(1_500, 1_000, true)]
    public void A_download_count_has_to_move_past_the_threshold_to_be_reported(
        long before, long after, bool reported)
    {
        // Downloads tick on every live package. Without a threshold every run would report
        // every candidate, which is the same as reporting nothing.
        SweepDiff diff = SweepDiff.Between(
            [Sighting.Of("alpha", downloads: before)],
            [Sighting.Of("alpha", downloads: after)],
            new SweepSensitivity(MinDownloadsDelta: 100));

        Assert.Equal(reported, diff.Changes.Count == 1);
    }

    [Fact]
    public void A_number_that_appears_is_reported_however_small_it_is()
    {
        // The threshold is about drift. A registry that started publishing a count is not
        // drift, and neither is one that stopped.
        SweepDiff diff = SweepDiff.Between(
            [Sighting.Of("alpha", stars: null)],
            [Sighting.Of("alpha", stars: 1)],
            new SweepSensitivity(MinStarsDelta: 10_000));

        ObservationChange change = Assert.Single(diff.Changes);

        Assert.Equal("stars", change.Field);
        Assert.Null(change.Before);
        Assert.Equal("1", change.After);
    }

    [Fact]
    public void An_edit_to_a_description_is_not_a_change()
    {
        // Descriptions are prose and churn. What a pattern found in one is compared instead,
        // and that is the part that carries signal.
        Observation before = Sighting.Of("alpha");
        Observation after = before with { Description = "rewritten overnight" };

        Assert.True(SweepDiff.Between([before], [after]).IsEmpty);
    }

    [Fact]
    public void An_edit_to_the_recipe_is_not_a_change_to_every_candidate()
    {
        // The query list describes the recipe, not the candidate. Renaming a query would
        // otherwise report the entire field as having moved.
        Observation before = Sighting.Of("alpha");
        Observation after = before with { Queries = ["a-renamed-query"] };

        Assert.True(SweepDiff.Between([before], [after]).IsEmpty);
    }

    [Fact]
    public void Two_runs_that_saw_the_same_field_in_a_different_order_report_the_same_thing()
    {
        // Registries do not promise a stable result order, and a run whose report depended on
        // one would be unreadable week to week.
        Observation[] forwards = [Sighting.Of("alpha"), Sighting.Of("beta"), Sighting.Of("gamma")];
        Observation[] backwards = [.. forwards.Reverse()];

        Assert.Equal(
            SweepDiff.Between([], forwards).Report(),
            SweepDiff.Between([], backwards).Report());
    }

    [Fact]
    public void The_report_names_what_moved_and_what_it_moved_to()
    {
        SweepDiff diff = SweepDiff.Between(
            [Sighting.Of("alpha", version: "1.0.0")],
            [Sighting.Of("alpha", version: "2.0.0"), Sighting.Of("beta")]);

        string report = diff.Report();

        Assert.Contains("new (1):", report, StringComparison.Ordinal);
        Assert.Contains("crates.io:beta", report, StringComparison.Ordinal);
        Assert.Contains("changed (1):", report, StringComparison.Ordinal);
        Assert.Contains("version: 1.0.0 -> 2.0.0", report, StringComparison.Ordinal);
        Assert.DoesNotContain(SweepDiff.NoChanges, report, StringComparison.Ordinal);
    }
}
