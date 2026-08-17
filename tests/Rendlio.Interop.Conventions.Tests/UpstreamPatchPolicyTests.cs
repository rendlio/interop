using Xunit;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Pins the upstream patches policy. The program spends goodwill in other people's
/// repositories, where a mistake is not ours to withdraw: a patch that arrived with a
/// request attached, or from an account that did not say who it was, is read by a
/// maintainer once and remembered afterwards. So the constraints are pinned rather than
/// merely written down. What is pinned here is the promise, not the prose — the page may be
/// rewritten freely, but it must not quietly stop saying which bugs qualify, how a patch is
/// sent, what a contribution fork may never become, or that nothing is ever attached.
/// </summary>
public sealed class UpstreamPatchPolicyTests
{
    private const string Policy = "UPSTREAM-PATCHES.md";

    [Fact]
    public void The_policy_is_published_at_the_repository_root()
    {
        // At the root because that is where a contributor looks for it, and inside the
        // shipped set because that is what puts it under the publish-hygiene scan rather
        // than beside it.
        IEnumerable<string> shipped = RepositoryLayout.EnumerateShippedFiles()
            .Select(RepositoryLayout.Describe);

        Assert.Contains(Policy, shipped, StringComparer.Ordinal);
    }

    [Fact]
    public void The_readme_points_a_contributor_at_the_policy()
    {
        // A policy nobody is sent to is a policy nobody reads. Presence is the claim here:
        // that the README still sends a contributor this way at all. Whether the link
        // resolves is asked of every shipped page in ShippedLinkTests.
        Assert.Contains($"({Policy})", RepositoryLayout.ReadFile("README.md"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Guards the test below, and is the reason its answer can be believed. That test asserts
    /// something about every link the page publishes, so a page that published none would
    /// satisfy it while checking nothing — which is what a rewrite that dropped both links, or
    /// moved them to reference style (<c>[rule 2][fork-rules]</c>, which
    /// <see cref="RelativeLinkPattern"/> does not match), would quietly do. The page argues by
    /// pointing, so losing the links costs it the authority it argues from — the exact failure
    /// the test below was written to prevent, arriving by the one route that test cannot see.
    ///
    /// The two are named rather than counted, because a count is also satisfied by a rewrite
    /// that swapped one authority for another without anyone deciding to.
    /// </summary>
    [Fact]
    public void The_policy_publishes_the_two_links_it_argues_from()
    {
        List<(string Target, string Anchor)> links = LinksFrom(Policy).ToList();

        Assert.Contains(("SUPPORT.md", string.Empty), links);
        Assert.Contains(("README.md", "2-fork-rules"), links);
    }

    [Fact]
    public void The_policy_qualifies_a_bug_by_what_our_own_work_touches()
    {
        // The boundary is the whole reason the program is affordable. Both halves have to
        // survive — a defect the pipeline catches, and one that forces a workaround — and so
        // does the limit, because a policy that quietly widened into an open offer of free
        // labour would be a promise nobody here could keep.
        string prose = MarkdownPage.Prose(Policy);

        Assert.Contains("fidelity QA pipeline catches it", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("forces a workaround", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("We fix what our own work touches", prose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_sends_a_patch_the_upstream_way_rather_than_ours()
    {
        // A patch in our house style costs a maintainer time before it saves them any, so
        // the etiquette is theirs, not a compromise between theirs and ours.
        string prose = MarkdownPage.Prose(Policy);

        Assert.Contains("contribution guide is followed as written", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("an issue first where the project asks for one", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tests always", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("their code style rather than ours", prose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_says_who_is_sending_the_patch_and_forbids_a_second_voice()
    {
        // Astroturfing is the failure this program would be most tempted by and least able
        // to survive: it is cheap, it works briefly, and being caught at it discredits every
        // honest patch sent before and after. Both halves are pinned — turning up under our
        // own name, and never arriving twice.
        string prose = MarkdownPage.Prose(Policy);

        Assert.Contains("from an account that is visibly ours", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No unattributed accounts", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "no second voice arriving in a thread to agree with the first",
            prose,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_keeps_a_person_between_generated_code_and_an_upstream_tracker()
    {
        // Much of what we write starts as AI-authored code, and a maintainer inheriting it
        // inherits whatever provenance came with it. A reviewer whose name is on the commit
        // is what turns "we think this is fine" into someone actually answerable for it.
        string prose = MarkdownPage.Prose(Policy);

        Assert.Contains("A person reviews AI-authored code before it is sent", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("none of it reaches an upstream tracker unread", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("that person's name is on the commit", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("under the upstream project's own licence", prose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_sends_one_fix_at_a_time_with_something_that_reproduces_it()
    {
        // A maintainer's review time is the scarce resource being spent. One fix per pull
        // request with a reproduction is the shape that costs the least of it; a patch
        // carrying a drive-by refactor costs far more than the fix was worth.
        string prose = MarkdownPage.Prose(Policy);

        Assert.Contains("One pull request per fix", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("a small file that reproduces it", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No drive-by refactors", prose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_attaches_nothing_to_a_patch()
    {
        // The load-bearing sentence of the whole page. A patch that carries a request stops
        // being a contribution and becomes a trade, and a maintainer who senses the trade is
        // right to discount everything around it — including the fix.
        string prose = MarkdownPage.Prose(Policy);

        Assert.Contains("A patch never carries a request with it", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never a hint that one would be welcome", prose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_pays_no_cash()
    {
        // Stated absolutely on purpose. A rule with an exception in it is the exception, and
        // the moment a maintainer could reasonably wonder whether a decision of theirs was
        // bought, the goodwill this program exists to earn is already spent.
        string prose = MarkdownPage.Prose(Policy);

        Assert.Contains("This program pays no cash sponsorships", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not as a condition of anything", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not as thanks afterwards", prose, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The fork-workbench mechanics, one per case. Each is what keeps a contribution fork
    /// from becoming the thing rule 2 forbids: drop any one of them and the fork starts
    /// looking like a private continuation of somebody else's project.
    /// </summary>
    [Theory]
    [InlineData("live under the rendlio organisation")]
    [InlineData("A fork exists only to feed pull requests")]
    [InlineData("never publishes a package")]
    [InlineData("never presents itself as an alternative to the upstream")]
    [InlineData("is deleted once its patches are merged")]
    [InlineData("stays identical to upstream and is synced regularly")]
    [InlineData("All work happens in short-lived branches")]
    [InlineData("one branch and one pull request per fix")]
    [InlineData("Allow edits by maintainers is ticked on every pull request")]
    public void The_policy_fixes_the_mechanics_that_keep_a_contribution_fork_harmless(string mechanic)
    {
        Assert.Contains(mechanic, MarkdownPage.Prose(Policy), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_lets_a_decline_be_the_end_of_it()
    {
        // The one place the program could turn into pressure without anyone deciding to.
        // The third clause matters most: a declined patch reappearing as a patched copy
        // inside an adapter would be rule 2 broken by a route that never argued with it.
        string section = MarkdownPage.Section(Policy, "When a patch is declined");

        Assert.Contains("The fix is not resubmitted", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("the maintainer is not pursued", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "does not reappear as a patched copy inside an adapter",
            section,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_disclosure_states_the_identity_this_venture_actually_has()
    {
        // This paragraph is the only prose here that gets pasted into somebody else's issue
        // tracker, where a correction is not ours to make. An association in formation is
        // not an association yet, and saying otherwise on a public thread would be a
        // misstatement with our name on it.
        string prose = MarkdownPage.Prose(Policy);

        Assert.Contains("Swiss association in formation", prose, StringComparison.Ordinal);
        Assert.Contains("profits are pledged to charities", prose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_disclosure_calls_the_engine_source_available()
    {
        // The engine is BUSL-licensed, and this paragraph is the one that gets pasted into
        // somebody else's issue tracker, so it has to carry the accurate term itself rather
        // than lean on the README carrying it.
        //
        // The matching half of the rule — that the inaccurate term never appears — moved to
        // PublicSurfaceRulesTests. The search that stood here was not weak at what it did:
        // MarkdownPage.Prose() collapses a line wrap and drops an emphasis marker, so the
        // phrase was caught through either. It was blind to the hyphenated spelling, which
        // that normalisation leaves alone, and it only ever reached this one page. The rule
        // that replaced it catches the hyphen and holds every page the repository
        // publishes.
        //
        // Neither spelling is written out above, and cannot be. This file is shipped and is
        // not one of the two sources that rule exempts, so quoting the phrase in order to
        // discuss it is itself enough to redden it — as the first draft of this comment did.
        Assert.Contains("source-available", MarkdownPage.Prose(Policy), StringComparison.OrdinalIgnoreCase);
    }
}
