using System.Text.RegularExpressions;
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
public sealed partial class UpstreamPatchPolicyTests
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
        // A policy nobody is sent to is a policy nobody reads. This is also the only thing
        // holding the relative link, which would otherwise rot without a sound.
        Assert.Contains($"({Policy})", RepositoryLayout.ReadFile("README.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_link_the_policy_publishes_still_resolves()
    {
        // The page argues by pointing: the triage policy for where the boundary is drawn,
        // the fork rules for why a contribution fork is not the fork rule 2 forbids. A
        // reader who follows a dead link gets the assertion without the authority, and a
        // renamed heading breaks it silently.
        foreach ((string target, string anchor) in LinksFrom(Policy))
        {
            Assert.True(
                File.Exists(Path.Combine(RepositoryLayout.Root.FullName, target)),
                $"The policy links to '{target}', which does not exist.");

            if (anchor.Length == 0)
            {
                continue;
            }

            Assert.True(
                HeadingAnchors(target).Contains(anchor, StringComparer.Ordinal),
                $"The policy links to '{target}#{anchor}', but that page has no such heading.");
        }
    }

    [Fact]
    public void The_policy_qualifies_a_bug_by_what_our_own_work_touches()
    {
        // The boundary is the whole reason the program is affordable. Both halves have to
        // survive — a defect the pipeline catches, and one that forces a workaround — and so
        // does the limit, because a policy that quietly widened into an open offer of free
        // labour would be a promise nobody here could keep.
        string prose = Prose(Policy);

        Assert.Contains("fidelity QA pipeline catches it", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("forces a workaround", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("We fix what our own work touches", prose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_sends_a_patch_the_upstream_way_rather_than_ours()
    {
        // A patch in our house style costs a maintainer time before it saves them any, so
        // the etiquette is theirs, not a compromise between theirs and ours.
        string prose = Prose(Policy);

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
        string prose = Prose(Policy);

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
        string prose = Prose(Policy);

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
        string prose = Prose(Policy);

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
        string prose = Prose(Policy);

        Assert.Contains("A patch never carries a request with it", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never a hint that one would be welcome", prose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_pays_no_cash()
    {
        // Stated absolutely on purpose. A rule with an exception in it is the exception, and
        // the moment a maintainer could reasonably wonder whether a decision of theirs was
        // bought, the goodwill this program exists to earn is already spent.
        string prose = Prose(Policy);

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
        Assert.Contains(mechanic, Prose(Policy), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_lets_a_decline_be_the_end_of_it()
    {
        // The one place the program could turn into pressure without anyone deciding to.
        // The third clause matters most: a declined patch reappearing as a patched copy
        // inside an adapter would be rule 2 broken by a route that never argued with it.
        string section = Section("When a patch is declined");

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
        string prose = Prose(Policy);

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
        // PublicSurfaceRulesTests, which holds it over every published page rather than this
        // one, and matches through the line break or the emphasis marker that the literal
        // search standing here would have read as clean.
        Assert.Contains("source-available", Prose(Policy), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The named section, up to the next heading, as comparable prose.</summary>
    private static string Section(string heading)
    {
        string text = Normalise(RepositoryLayout.ReadFile(Policy));
        string marker = $"## {heading}";
        int start = text.IndexOf(marker, StringComparison.Ordinal);

        Assert.True(start >= 0, $"The policy has no '{marker}' section.");

        int next = text.IndexOf("\n## ", start + marker.Length, StringComparison.Ordinal);

        return Comparable(next < 0 ? text[start..] : text[start..next]);
    }

    /// <summary>The whole page as comparable prose.</summary>
    private static string Prose(string relativePath) =>
        Comparable(Normalise(RepositoryLayout.ReadFile(relativePath)));

    /// <summary>
    /// Reduces a page to what it says. Emphasis and code markers are dropped and whitespace
    /// runs collapse to a single space, so a pinned phrase survives a re-wrap or a word
    /// being set in bold. The promises are pinned; the typesetting is not.
    /// </summary>
    private static string Comparable(string text) =>
        WhitespaceRunPattern().Replace(MarkupPattern().Replace(text, string.Empty), " ").Trim();

    /// <summary>Line endings, so a checkout that converted them still finds the headings.</summary>
    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>The relative links a page publishes, split into target file and anchor.</summary>
    private static IEnumerable<(string Target, string Anchor)> LinksFrom(string relativePath)
    {
        foreach (Match link in RelativeLinkPattern().Matches(RepositoryLayout.ReadFile(relativePath)))
        {
            yield return (link.Groups["target"].Value, link.Groups["anchor"].Value);
        }
    }

    /// <summary>
    /// The anchors a Markdown page offers, derived from its headings the way GitHub derives
    /// them: lowercased, punctuation dropped, spaces hyphenated.
    /// </summary>
    private static List<string> HeadingAnchors(string relativePath) =>
        Normalise(RepositoryLayout.ReadFile(relativePath))
            .Split('\n')
            .Where(line => line.StartsWith('#'))
            .Select(line => line.TrimStart('#').Trim())
            .Select(heading => AnchorNoisePattern().Replace(heading.ToLowerInvariant(), string.Empty))
            .Select(heading => heading.Replace(' ', '-'))
            .ToList();

    /// <summary>Markdown emphasis and inline-code markers, which carry no promise.</summary>
    [GeneratedRegex(@"[*`]")]
    private static partial Regex MarkupPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunPattern();

    /// <summary>An inline link to another page in this repository, with an optional anchor.</summary>
    [GeneratedRegex(@"\]\((?<target>[A-Za-z0-9._/-]+\.md)(?:#(?<anchor>[^)]+))?\)")]
    private static partial Regex RelativeLinkPattern();

    /// <summary>Everything GitHub drops from a heading when it builds the anchor.</summary>
    [GeneratedRegex(@"[^a-z0-9 -]")]
    private static partial Regex AnchorNoisePattern();
}
