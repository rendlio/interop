using System.Text.RegularExpressions;
using Xunit;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Pins the support and triage policy. A free package whose support posture was never
/// stated is a promise nobody actually made, and the person who finds that out is always
/// the person who had already depended on it — so the policy has to be in place before the
/// first package ships, and has to stay in place afterwards. What is pinned here is the
/// promise rather than the prose: the page may be rewritten freely, but it must not quietly
/// stop saying where a defect goes, what a reporter can expect, or what will be declined.
/// </summary>
public sealed partial class SupportPolicyTests
{
    private const string Policy = "SUPPORT.md";

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

    [Fact]
    public void The_policy_states_no_response_time_target_and_invents_no_number()
    {
        // A target that could not be held through a quiet month would be decoration, and
        // decoration is worse than saying nothing because it gets believed. Both halves
        // matter: saying so plainly, and never acquiring the vocabulary that walks it back.
        string prose = MarkdownPage.Prose(Policy);

        Assert.Contains("no response-time target", prose, StringComparison.OrdinalIgnoreCase);

        Match target = ServiceLevelPattern().Match(prose);

        Assert.False(
            target.Success,
            $"The policy states a response-time target (\"{target.Value}\"), having promised "
            + "there would not be one.");
    }

    [Fact]
    public void The_policy_says_what_sets_priority()
    {
        // These packages exist to carry documents into the rendering engine. Leaving that
        // unsaid makes every decline look arbitrary.
        Assert.Contains("the engine's needs govern", MarkdownPage.Prose(Policy), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_sends_an_upstream_defect_upstream_rather_than_around_it()
    {
        // Rule 2. An adapter is glue over an unmodified package, so a defect on the far side
        // of the glue is repaired there. A triage policy that allowed a private patch
        // instead would be a licence to fork, arrived at sideways.
        string prose = MarkdownPage.Prose(Policy);

        Assert.Contains("reported upstream", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not worked around in a private copy", prose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_does_not_make_the_reporter_work_out_which_side_a_defect_is_on()
    {
        // The routing table describes how the work is divided; it is not a quiz to pass
        // before filing. Without the catch-all, someone who cannot tell files nothing.
        Assert.Contains("If you cannot tell them apart", MarkdownPage.Prose(Policy), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("In scope")]
    [InlineData("Out of scope")]
    public void The_policy_scopes_issues_and_pull_requests_alike(string heading)
    {
        // Four answers are needed rather than two: what is accepted and what is declined,
        // for an issue and for a pull request. A page that scopes only issues leaves a
        // contributor to discover the pull-request half after writing one.
        string section = MarkdownPage.Section(Policy, heading);

        Assert.Contains("Issues", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pull requests", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_policy_leaves_a_vulnerability_report_a_route_that_does_not_dead_end()
    {
        // Forbidding a public issue outright and then pointing at a private channel that may
        // not be switched on leaves a reporter holding a prohibition and no route, and the
        // realistic outcome is the public disclosure the section exists to prevent. Two
        // things keep it open: the ban reaches only an issue that describes the problem, and
        // the fallback needs nothing switched on. A rewrite has to keep both.
        string section = MarkdownPage.Section(Policy, "Security");

        Assert.Contains("do not open a public one describing it", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no detail, no reproduction, no file", section, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The vocabulary a response-time promise is written in. "Business days" has no other
    /// use, and a numeric deadline has no other shape.
    /// </summary>
    [GeneratedRegex(
        @"\b(business|working)\s+(day|hour)s?\b|\bwithin\s+\d+\s+(hour|day|week|month)s?\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ServiceLevelPattern();
}
