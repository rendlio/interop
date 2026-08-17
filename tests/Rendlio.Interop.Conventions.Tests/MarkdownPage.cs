using System.Text.RegularExpressions;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Reads a Markdown page the way a fixture that pins prose has to read it, so every
/// convention fixture agrees on one definition of what a page says.
/// <see cref="RepositoryLayout"/> answers which files this repository publishes; this
/// answers what one of them promises, and where it points.
/// </summary>
internal static partial class MarkdownPage
{
    /// <summary>The whole page as comparable prose.</summary>
    public static string Prose(string relativePath) =>
        Comparable(Normalise(RepositoryLayout.ReadFile(relativePath)));

    /// <summary>The named section of a page, up to the next heading, as comparable prose.</summary>
    /// <exception cref="InvalidOperationException">The page has no such section.</exception>
    public static string Section(string relativePath, string heading)
    {
        // Located in the normalised page rather than in the comparable one: collapsing
        // whitespace takes the line breaks the heading markers sit on with it.
        string text = Normalise(RepositoryLayout.ReadFile(relativePath));
        string marker = $"## {heading}";
        int start = text.IndexOf(marker, StringComparison.Ordinal);

        if (start < 0)
        {
            throw new InvalidOperationException($"'{relativePath}' has no '{marker}' section.");
        }

        int next = text.IndexOf("\n## ", start + marker.Length, StringComparison.Ordinal);

        return Comparable(next < 0 ? text[start..] : text[start..next]);
    }

    /// <summary>The relative links a page publishes, split into target page and anchor.</summary>
    public static IEnumerable<(string Target, string Anchor)> LinksFrom(string relativePath)
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
    public static IReadOnlyList<string> HeadingAnchors(string relativePath) =>
    [
        .. Normalise(RepositoryLayout.ReadFile(relativePath))
            .Split('\n')
            .Where(line => line.StartsWith('#'))
            .Select(line => line.TrimStart('#').Trim())
            .Select(heading => AnchorNoisePattern().Replace(heading.ToLowerInvariant(), string.Empty))
            .Select(heading => heading.Replace(' ', '-')),
    ];

    /// <summary>
    /// Reduces a page to what it says. Emphasis and inline-code markers are dropped and
    /// whitespace runs collapse to a single space, so a pinned phrase survives a re-wrap or a
    /// word being set in bold. The promises are pinned; the typesetting is not.
    /// <para>
    /// Dropping the markers is load-bearing rather than tidy, and two of the fork mechanics
    /// <see cref="UpstreamPatchPolicyTests"/> pins are what hold it: both sit inside emphasis
    /// or inline code on the page, so a strip taken out here reddens them instead of quietly
    /// narrowing what a phrase may be pinned across. This is also the half the two copies
    /// that preceded this file disagreed on — the support policy's pins were written against
    /// unstripped text, and read the same either way.
    /// </para>
    /// </summary>
    private static string Comparable(string text) =>
        WhitespaceRunPattern().Replace(MarkupPattern().Replace(text, string.Empty), " ").Trim();

    /// <summary>Line endings, so a checkout that converted them still finds the headings.</summary>
    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

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
