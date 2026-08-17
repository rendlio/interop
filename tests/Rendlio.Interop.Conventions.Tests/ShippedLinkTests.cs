using Xunit;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Pins that the pages this repository publishes still point where they say they do. These
/// pages argue by pointing — the triage policy for where the boundary between an adapter bug
/// and an upstream one is drawn, the fork rules for why a contribution fork is not the fork
/// rule 2 forbids — and a reader who follows a dead link gets the assertion without the
/// authority behind it.
/// <para>
/// Every shipped Markdown page is walked rather than one of them. The check started on the
/// upstream patches policy, where it was the only thing holding a link target at all; the
/// README and the triage policy had presence assertions for particular links and nothing
/// that resolved them, so a heading reworded or a page renamed out from under those two
/// rotted silently. A link is the one kind of claim a page makes that nobody rereads.
/// </para>
/// </summary>
public sealed class ShippedLinkTests
{
    [Fact]
    public void The_walk_reaches_the_pages_this_repository_publishes()
    {
        // Guards everything below, which a walk that had stopped finding pages would satisfy
        // while reading nothing. The private trees are pruned by the enumeration itself, and
        // PublicSurfaceRulesTests is what holds them pruned.
        List<string> pages = [.. Pages()];

        Assert.Contains("README.md", pages, StringComparer.Ordinal);
        Assert.Contains("SUPPORT.md", pages, StringComparer.Ordinal);
        Assert.Contains("UPSTREAM-PATCHES.md", pages, StringComparer.Ordinal);

        // And at least one page below the root, because that is the only kind whose links
        // resolve anywhere other than the repository root.
        Assert.Contains(pages, page => page.Contains('/'));
    }

    [Fact]
    public void The_walk_finds_the_links_those_pages_carry()
    {
        // Guards the anchor half in particular. A pattern that matched a target but stopped
        // capturing an anchor would leave the resolution below green while never asking the
        // question that catches a reworded heading. Named links rather than a count, so an
        // ordinary edit to a page does not redden this.
        List<(string Page, string Target, string Anchor)> links = [.. Links()];

        Assert.Contains(("SUPPORT.md", "README.md", "the-rules"), links);
        Assert.Contains(("UPSTREAM-PATCHES.md", "README.md", "2-fork-rules"), links);
        Assert.Contains(links, link => link.Target.Contains('/'));
    }

    [Fact]
    public void Every_link_a_shipped_page_publishes_still_resolves()
    {
        List<string> pages = [.. Pages()];
        List<string> broken = [];

        foreach ((string page, string target, string anchor) in Links())
        {
            string resolved = ResolveFrom(page, target);

            // Answered from what the repository publishes rather than from the filesystem.
            // File.Exists is case-insensitive on the machine these tests usually run on and
            // case-sensitive on the one serving the page, so a target whose spelling drifted
            // would pass here and be a 404 there.
            if (!pages.Contains(resolved, StringComparer.Ordinal))
            {
                broken.Add($"  {page} -> {target}: '{resolved}' is not a page this repository publishes.");
                continue;
            }

            if (anchor.Length == 0)
            {
                continue;
            }

            if (!MarkdownPage.HeadingAnchors(resolved).Contains(anchor, StringComparer.Ordinal))
            {
                broken.Add($"  {page} -> {target}#{anchor}: '{resolved}' has no heading with that anchor.");
            }
        }

        bool resolves = broken.Count == 0;

        Assert.True(
            resolves,
            $"A shipped page points somewhere that is no longer there.{Environment.NewLine}"
            + string.Join(Environment.NewLine, broken));
    }

    /// <summary>
    /// The resolution rule, for the shapes no page in the tree currently uses. Every page
    /// that carries a link sits at the repository root today, where resolving against the
    /// page and resolving against the root give the same answer — so nothing above would
    /// notice the distinction collapsing, and the first nested page to publish a link would
    /// find its links broken for a reason nobody could see.
    /// </summary>
    [Theory]
    [InlineData("README.md", "SUPPORT.md", "SUPPORT.md")]
    [InlineData("tools/Widget/README.md", "USAGE.md", "tools/Widget/USAGE.md")]
    [InlineData("tools/Widget/README.md", "./USAGE.md", "tools/Widget/USAGE.md")]
    [InlineData("tools/Widget/README.md", "../../README.md", "README.md")]
    public void A_relative_link_resolves_against_the_page_that_carries_it(
        string page, string target, string expected)
    {
        Assert.Equal(expected, ResolveFrom(page, target));
    }

    /// <summary>Every Markdown page this repository publishes, repository-relative.</summary>
    private static IEnumerable<string> Pages() =>
        RepositoryLayout.EnumerateShippedFiles()
            .Select(RepositoryLayout.Describe)
            .Where(page => page.EndsWith(".md", StringComparison.OrdinalIgnoreCase));

    /// <summary>Every relative link those pages publish, with the page carrying it.</summary>
    private static IEnumerable<(string Page, string Target, string Anchor)> Links() =>
        Pages().SelectMany(page => MarkdownPage.LinksFrom(page)
            .Select(link => (Page: page, Target: link.Target, Anchor: link.Anchor)));

    /// <summary>
    /// Where a link in a page actually points, repository-relative. A relative link resolves
    /// against the directory the page sits in, not against the repository root.
    /// </summary>
    private static string ResolveFrom(string page, string target) =>
        RepositoryLayout.Describe(Path.GetFullPath(Path.Combine(
            RepositoryLayout.Root.FullName,
            Path.GetDirectoryName(page) ?? string.Empty,
            target)));
}
