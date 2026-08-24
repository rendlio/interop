using Xunit;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Pins that the adapter API specification is published rather than kept beside the code. It
/// is the contract every <c>Rendlio.Interop.*</c> package is built to, and its audience is not
/// only whoever writes one: someone reading an installed adapter's surface has no other
/// document saying why the surface is that shape, which overloads exist, or what the
/// recalculation default does to the workbook they hand it. A contract nobody outside can read
/// is a contract in name only.
/// <para>
/// What is pinned here is the publication and the promises, not the prose. The page may be
/// rewritten freely — but it has to stay in the set this repository publishes, the README has
/// to keep sending a reader to it, and it must not quietly stop fixing the four things it says
/// it fixes.
/// </para>
/// </summary>
public sealed class AdapterSpecificationTests
{
    private const string Specification = "docs/adapter-api.md";

    [Fact]
    public void The_specification_is_published_rather_than_private()
    {
        // The claim the page's whole existence rests on, and the one thing nothing else here
        // would notice going away. Every scan in this project reads the set below, so a page
        // that left it does not fail a check — it stops being checked, silently, at the same
        // moment it stops being readable by anyone who installs a package.
        IEnumerable<string> shipped = RepositoryLayout.EnumerateShippedFiles()
            .Select(RepositoryLayout.Describe);

        Assert.Contains(Specification, shipped, StringComparer.Ordinal);
    }

    [Fact]
    public void The_readme_points_a_reader_at_the_specification()
    {
        // A contract nobody is sent to is a contract nobody reads. Presence is the claim here:
        // that the README still sends a reader this way at all. Whether the link resolves is
        // asked of every shipped page in ShippedLinkTests.
        Assert.Contains(
            $"({Specification})",
            RepositoryLayout.ReadFile("README.md"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_specification_points_back_at_the_rules_it_is_written_under()
    {
        // The page argues from the repository rules — the version-pinning policy, the ban on
        // forking a living upstream — and restates none of them itself, so a reader who cannot
        // reach them gets the requirement without the reason. Presence again; resolution is
        // ShippedLinkTests', which is also the only thing holding a link correct from a page
        // that does not sit at the repository root.
        Assert.Contains(
            "(../README.md)",
            RepositoryLayout.ReadFile(Specification),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("the SaveToRendlioPdf entry-point surface")]
    [InlineData("the compatibility report is returned on every successful call, never swallowed")]
    [InlineData("on, overridable, never silently off")]
    [InlineData("the path/Stream overload set")]
    public void The_specification_still_fixes_what_it_says_it_fixes(string promise)
    {
        // Its own scope list, one row each. These four are what an adapter is built to and
        // what a consumer is owed; a rewrite that dropped one would leave that much of the
        // surface undecided while the page still read as complete.
        Assert.Contains(promise, MarkdownPage.Prose(Specification), StringComparison.OrdinalIgnoreCase);
    }
}
