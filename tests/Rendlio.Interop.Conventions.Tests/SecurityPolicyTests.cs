using System.Text.RegularExpressions;
using Xunit;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Pins the security policy. This is the page whose failure mode is not a slow one: a
/// reporter who cannot find a private route, or who is told the public one is barred and
/// offered nothing else, publishes the details instead — and that has already happened by
/// the time anybody here hears about it. What is pinned is the promise rather than the
/// prose. The page may be rewritten freely, but it must not stop giving a private route with
/// a fallback under it, stop routing a report that belongs to somebody else, or start asking
/// a reporter for something in return.
/// </summary>
public sealed partial class SecurityPolicyTests
{
    private const string Policy = "SECURITY.md";

    /// <summary>The triage policy, which carries the same route in miniature.</summary>
    private const string TriagePolicy = "SUPPORT.md";

    /// <summary>
    /// The section of each page that a reporter with something to send actually reads. They
    /// are named separately because the two pages arrive at the same subject from different
    /// directions: here it is the whole point of the document, there it is one section of a
    /// triage policy.
    /// </summary>
    private const string RouteSection = "Reporting a vulnerability";

    private const string TriageRouteSection = "Security";

    [Fact]
    public void The_policy_is_published_at_the_repository_root()
    {
        // At the root because that is where the host looks for it — the file at this path is
        // what puts a security policy in front of a reporter before they reach the issue
        // tracker — and inside the shipped set because that is what puts it under the
        // publish-hygiene scan rather than beside it.
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
    public void The_triage_policy_points_a_reporter_at_the_policy()
    {
        // The other door into the same room. Someone working out where a defect goes reads
        // the triage policy, discovers theirs is not an ordinary defect, and has to be able
        // to get here from there.
        Assert.Contains($"({Policy})", RepositoryLayout.ReadFile(TriagePolicy), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_link_the_policy_publishes_still_resolves()
    {
        // The page argues by pointing: the triage policy for how a defect is routed, the fork
        // rules for why a vulnerability is still not repaired in a private copy. A reader who
        // follows a dead link gets the assertion without the authority, and a renamed heading
        // breaks it silently.
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

    /// <summary>
    /// The route, sentence by sentence, as both pages have to give it — and have to give it
    /// in the section a reporter reads rather than merely somewhere on the page. Two pages
    /// naming one private channel is two places for it to drift, and the drift is silent:
    /// each page reads correctly on its own while a reporter who arrived by the other one was
    /// told something else. The scoping is what makes the middle claim mean anything, since
    /// it is the only one of the three that names the channel: asserted against a whole page
    /// it would stay green while the channel migrated out from under the prohibition it
    /// answers, leaving that section barring the public route and offering nothing in its
    /// place. The fallback is on the list for a third reason — it is the half that needs
    /// nothing switched on in repository settings.
    /// </summary>
    [Theory]
    [InlineData("do not open a public one describing it")]
    [InlineData("this repository's Security tab (Report a vulnerability)")]
    [InlineData("no detail, no reproduction, no file")]
    public void Both_pages_send_a_reporter_by_the_same_route(string claim)
    {
        // Asserted of both pages in one test on purpose: what matters is not that either one
        // says it but that they agree, and two tests that each check one page would pass
        // individually while the pair of them contradicted each other.
        Assert.Contains(claim, Section(Policy, RouteSection), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            claim,
            Section(TriagePolicy, TriageRouteSection),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_states_no_response_time_target_and_invents_no_number()
    {
        // The triage policy promised there would never be one, and a second page is where
        // that promise is most likely to be walked back without anyone deciding to — a
        // security page being exactly the place somebody reaches for a reassuring number.
        // Both halves matter: saying so plainly, and never acquiring the vocabulary that
        // contradicts it.
        string prose = Prose(Policy);

        Assert.Contains("no response-time target", prose, StringComparison.OrdinalIgnoreCase);

        Match target = ServiceLevelPattern().Match(prose);

        Assert.False(
            target.Success,
            $"The policy states a response-time target (\"{target.Value}\"), having promised "
            + "there would not be one.");
    }

    [Fact]
    public void The_policy_does_not_make_the_reporter_work_out_whose_defect_it_is()
    {
        // The routing table describes how the work is divided; it is not a quiz to pass
        // before reporting. Without the catch-all, someone who cannot tell either sends it
        // nowhere or sends it everywhere, and one of those is a public disclosure.
        //
        // The second half covers the reporter the first half does not: someone who knows
        // exactly whose defect it is, goes looking for that project's private channel, and
        // finds none published. The table sends two of its three rows somewhere we do not
        // control, so it cannot promise those routes exist — which makes the way back here
        // the part that has to be promised instead. A reporter holding a private-only
        // instruction and no private route posts in public.
        string section = Section(Policy, "Where a vulnerability belongs");

        Assert.Contains("Working out which one it is is not your job", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("neither is finding somebody else's channel", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("the route it points to is not one you can find", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Guessing wrong costs you nothing", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_forwards_a_misrouted_report_only_when_asked()
    {
        // The catch-all above invites a report that turns out to belong to somebody else,
        // which hands us a decision that is not ours to make: passing it on is a disclosure,
        // to an audience the reporter did not choose. Being trusted with a report does not
        // buy the right to make that call for them.
        string section = Section(Policy, "Where a vulnerability belongs");

        Assert.Contains("passed on only if you ask for that", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_asks_a_reporter_for_nothing_in_return()
    {
        // Where a security policy quietly becomes a bargain. A page that wants an agreement
        // or an embargo accepted before it will read a report is charging for the channel,
        // and the reporter who declines to pay still has the vulnerability.
        string prose = Prose(Policy);

        Assert.Contains("Nothing is asked of you in return", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No agreement to sign", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no embargo to accept before anyone will read it", prose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_leaves_the_disclosure_timetable_to_the_reporter()
    {
        // The clause a project under pressure would most like back. A deadline we could ask
        // to move is a deadline we would ask to move, repeatedly, and there is no way for a
        // reporter to refuse that costs them nothing — so it is given away here in advance,
        // where nobody is under pressure yet.
        string section = Section(Policy, "Disclosure");

        Assert.Contains("The timetable is yours", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("you will not be asked to move it", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not be asked to stay quiet once it has passed", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_repairs_an_upstream_vulnerability_upstream_rather_than_around_it()
    {
        // Rule 2, at the point of maximum temptation to make an exception to it: a quiet
        // patch to a private copy looks responsible under time pressure, and it is how a
        // fork begins. The page has to keep saying that urgency is not a licence to fork.
        //
        // The privacy half is pinned with it, and is the half that could actually leak.
        // "Sent upstream" on its own resolves, through this page's own cross-reference, to
        // the route the upstream patches policy describes — an issue first and a small file
        // that reproduces the defect — and for a vulnerability that file is the exploit. The
        // sentence that stops rule 2 being broken quietly would otherwise be the sentence
        // that publishes the thing, so neither claim is safe to pin without the other.
        string section = Section(Policy, "Disclosure");

        Assert.Contains("it is sent upstream", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never worked around in a private copy", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("that project's own private security channel", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "never as an ordinary public pull request",
            section,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_says_which_versions_a_fix_reaches()
    {
        // The question a consumer asks the moment an advisory lands, and the one a page like
        // this is most often silent on. Settled while nothing has shipped, because the same
        // question asked during an incident gets answered by whoever is most tired.
        string section = Section(Policy, "Which versions are covered");

        Assert.Contains("a new release of the affected package", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not patched in place", section, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The named section of a page, up to the next heading, as comparable prose. Takes the
    /// page rather than assuming the policy, because the claim that both pages give one route
    /// is only worth making about the section each of them gives it in.
    /// </summary>
    /// <remarks>
    /// This helper, <see cref="Normalise"/> and <see cref="ServiceLevelPattern"/> are near
    /// copies of ones <see cref="SupportPolicyTests"/> keeps, and are deliberately not shared
    /// with it. Two pages made the same promises separately and either may be rewritten
    /// without the other, so each fixture has to be able to change its mind alone. Sharing
    /// would also make one fixture's idea of what a deadline looks like binding on a page it
    /// never reads — and the day the two need to differ is the day somebody widens the shared
    /// copy to suit whichever page they happen to be editing, quietly loosening the other. It
    /// is the same reasoning <see cref="PrivateTreePolicyTests"/> gives for naming the private
    /// tree itself rather than borrowing the constant, and <see cref="PublicSurfaceRulesTests"/>
    /// for asking git for the tracked paths a second time.
    /// </remarks>
    private static string Section(string relativePath, string heading)
    {
        string text = Normalise(RepositoryLayout.ReadFile(relativePath));
        string marker = $"## {heading}";
        int start = text.IndexOf(marker, StringComparison.Ordinal);

        Assert.True(start >= 0, $"'{relativePath}' has no '{marker}' section.");

        int next = text.IndexOf("\n## ", start + marker.Length, StringComparison.Ordinal);

        return Comparable(next < 0 ? text[start..] : text[start..next]);
    }

    /// <summary>The whole page as comparable prose.</summary>
    private static string Prose(string relativePath) =>
        Comparable(Normalise(RepositoryLayout.ReadFile(relativePath)));

    /// <summary>
    /// Reduces a page to what it says. Emphasis and code markers are dropped and whitespace
    /// runs collapse to a single space, so a pinned phrase survives a re-wrap or a word being
    /// set in bold. The promises are pinned; the typesetting is not.
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
    /// The anchors a Markdown page offers, derived from its headings the way the host derives
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
    [GeneratedRegex("[*`]")]
    private static partial Regex MarkupPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunPattern();

    /// <summary>An inline link to another page in this repository, with an optional anchor.</summary>
    [GeneratedRegex(@"\]\((?<target>[A-Za-z0-9._/-]+\.md)(?:#(?<anchor>[^)]+))?\)")]
    private static partial Regex RelativeLinkPattern();

    /// <summary>Everything the host drops from a heading when it builds the anchor.</summary>
    [GeneratedRegex(@"[^a-z0-9 -]")]
    private static partial Regex AnchorNoisePattern();

    /// <summary>
    /// The vocabulary a response-time promise is written in. "Business days" has no other
    /// use, and a numeric deadline has no other shape.
    /// </summary>
    [GeneratedRegex(
        @"\b(business|working)\s+(day|hour)s?\b|\bwithin\s+\d+\s+(hour|day|week|month)s?\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ServiceLevelPattern();
}
