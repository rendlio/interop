using System.Globalization;
using System.Runtime.ExceptionServices;
using Xunit;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Pins <see cref="MarkdownPage"/> itself. The policy fixtures read their pages through it,
/// so they no longer describe how a page is read — they describe what it says, and a helper
/// that read the wrong part of it would leave them green while asserting against text they
/// never meant. The claims held here are the ones a page's own wording cannot hold: that a
/// section is a part of a page rather than the whole of it, that a heading nobody can find
/// says so, and that an anchor is derived the same way in every locale.
/// </summary>
public sealed class MarkdownPageTests
{
    /// <summary>
    /// A section and a heading that follows it on the same page. Both policy pages are
    /// represented, because both fixtures ask for sections and either page could be the one
    /// reorganised.
    /// </summary>
    [Theory]
    [InlineData("SUPPORT.md", "In scope", "Out of scope")]
    [InlineData("SUPPORT.md", "Security", "The rules still decide")]
    [InlineData("UPSTREAM-PATCHES.md", "Which bugs qualify", "How a patch is sent")]
    public void A_section_is_the_part_of_a_page_under_its_own_heading(
        string page, string heading, string next)
    {
        // The boundary is the whole point of asking for a section rather than for the page,
        // and nothing else in the suite notices it: every phrase a fixture pins to a section
        // is also somewhere on the page, so a Section() that returned the page entire would
        // quietly demote three assertions from "this section promises X" to "this page says
        // X somewhere" and stay green doing it.
        string section = MarkdownPage.Section(page, heading);

        Assert.StartsWith($"## {heading}", section, StringComparison.Ordinal);
        Assert.DoesNotContain($"## {next}", section, StringComparison.Ordinal);
        Assert.True(
            section.Length < MarkdownPage.Prose(page).Length,
            $"'{heading}' came back as long as the whole of '{page}', so the section "
            + "boundary is not being applied and every pin against a section is now a pin "
            + "against the page.");
    }

    [Fact]
    public void The_last_section_of_a_page_runs_to_the_end_of_it()
    {
        // The other branch: there is no following heading to stop at, so the section runs to
        // the end of the file. Found rather than named, so appending a section to the page
        // moves this test along rather than reddening it.
        const string Page = "UPSTREAM-PATCHES.md";

        string last = RepositoryLayout.ReadFile(Page)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line[3..].Trim())
            .Last();

        string prose = MarkdownPage.Prose(Page);
        string section = MarkdownPage.Section(Page, last);

        Assert.StartsWith($"## {last}", section, StringComparison.Ordinal);
        Assert.EndsWith(section, prose, StringComparison.Ordinal);
        Assert.True(
            section.Length < prose.Length,
            $"The last section of '{Page}' came back as the whole page.");
    }

    [Fact]
    public void A_section_nobody_can_find_names_the_page_it_was_looked_for_in()
    {
        // Section() gained a page parameter when it was extracted, which makes a transposed
        // call possible for the first time. Both ways of missing have to fail loudly: a
        // helper that answered either with an empty string would leave a fixture asserting
        // against nothing, and one that failed without naming the page would send whoever
        // hit it to the wrong file.
        InvalidOperationException missing = Assert.Throws<InvalidOperationException>(
            () => MarkdownPage.Section("SUPPORT.md", "A heading this page does not have"));

        Assert.Contains("SUPPORT.md", missing.Message, StringComparison.Ordinal);
        Assert.Contains("A heading this page does not have", missing.Message, StringComparison.Ordinal);

        Assert.Throws<FileNotFoundException>(() => MarkdownPage.Section("Security", "SUPPORT.md"));
    }

    [Fact]
    public void A_heading_anchor_is_derived_the_same_way_in_every_locale()
    {
        // Anchors are what a link is resolved against, and deriving one means lower-casing a
        // heading. Turkish maps 'I' to a dotless letter, which the anchor's own noise filter
        // then drops outright — so a lower-case that consulted the current culture would
        // turn this page's title into "rendlio-nterop" and report every link into it as
        // broken, on the machine of whoever runs the suite in that locale and nowhere else.
        IReadOnlyList<string> invariant = MarkdownPage.HeadingAnchors("README.md");

        // Guards the comparison below, which two empty lists would satisfy. Both anchors are
        // ones a shipped page actually links to, and the first is the one Turkish moves.
        Assert.Contains("rendlio-interop", invariant, StringComparer.Ordinal);
        Assert.Contains("the-rules", invariant, StringComparer.Ordinal);

        Assert.Equal(invariant, In("tr-TR", () => MarkdownPage.HeadingAnchors("README.md")));
    }

    /// <summary>
    /// Runs <paramref name="body"/> as a machine configured for <paramref name="locale"/>
    /// would. On a thread of its own rather than by setting and restoring the culture around
    /// the call: the culture belongs to a thread, and xUnit hands its threads to whatever
    /// runs next, so a restore that was missed leaves this locale behind on a thread nobody
    /// here owns. A thread that exists only for the call cannot leak one, because it is gone.
    /// </summary>
    private static T In<T>(string locale, Func<T> body)
    {
        T result = default!;
        Exception? failure = null;

        Thread thread = new(() =>
        {
            CultureInfo.CurrentCulture = new CultureInfo(locale);

            try
            {
                result = body();
            }
            catch (Exception error)
            {
                failure = error;
            }
        });

        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            // Rethrown rather than wrapped, so the failure reported is the one the body hit.
            ExceptionDispatchInfo.Throw(failure);
        }

        return result;
    }
}
