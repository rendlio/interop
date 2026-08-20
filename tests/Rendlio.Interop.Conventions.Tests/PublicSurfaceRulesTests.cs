using System.Text.RegularExpressions;
using Xunit;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Scans everything this repository publishes for wording that must not appear on a public
/// page. The README pins the rules it quotes; this fixture pins the rules that constrain
/// every *other* page, which is where they are likely to be broken — by someone adding a
/// document months from now who never read them. Private trees are excluded, because the
/// same wording is legitimate there.
/// </summary>
public sealed partial class PublicSurfaceRulesTests
{
    /// <summary>
    /// The scanner's own sources: one has to quote every forbidden term in order to search
    /// for it, the other has to name the private trees in order to prune them. These two
    /// files are excluded from the scan. Nothing else may be.
    /// </summary>
    private static readonly string[] ScannerSources =
    [
        "PublicSurfaceRulesTests.cs", "RepositoryLayout.cs",
    ];

    /// <summary>
    /// How rendering fidelity is measured is not published. A page may refer to the
    /// fidelity QA pipeline; it may not describe what the pipeline compares against.
    /// </summary>
    private static readonly string[] MeasurementDisclosureTerms = ["oracle", "scored against"];

    /// <summary>
    /// Rendlio Sheets is the only product named in public. MiniWord stays off this list on
    /// purpose: the fork rules name it as an upstream we contribute to, which is already
    /// public — it is an *adapter* over it that would announce something unshipped.
    /// </summary>
    private static readonly string[] UnannouncedProductTerms =
    [
        "Rendlio Words", "Rendlio Slides", "Rendlio ODS", "Rendlio Suite",
        "MiniWord adapter", "Rendlio.Interop.MiniWord",
    ];

    /// <summary>Planning vocabulary. A public page states the what; the why stays internal.</summary>
    private static readonly string[] InternalIdentifierTerms = ["work item", "docs/internal"];

    /// <summary>
    /// Everything here is MIT funnel code, so it carries no commercial terms — see rule 1
    /// in the README.
    /// </summary>
    private static readonly string[] CommercialTerms = ["pricing", "per seat", "SKU"];

    /// <summary>
    /// Guards every other test in this fixture: if the walk ever stopped finding files they
    /// would all pass while checking nothing.
    /// </summary>
    [Fact]
    public void The_scan_reaches_the_files_this_repository_publishes()
    {
        IReadOnlyList<string> shipped = RepositoryLayout.EnumerateShippedFiles();
        IEnumerable<string> names = shipped.Select(RepositoryLayout.Describe);

        Assert.Contains("README.md", names, StringComparer.Ordinal);
        Assert.Contains("LICENSE", names, StringComparer.Ordinal);
        Assert.Contains(".github/workflows/ci.yml", names, StringComparer.Ordinal);
    }

    [Fact]
    public void The_scan_does_not_reach_the_private_trees()
    {
        IEnumerable<string> names = RepositoryLayout.EnumerateShippedFiles()
            .Select(RepositoryLayout.Describe);

        Assert.DoesNotContain(names, name =>
            name.StartsWith("docs/internal/", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(".conductor/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Shipped_pages_do_not_disclose_how_fidelity_is_measured()
    {
        AssertAbsent(
            RepositoryLayout.EnumerateShippedFiles(),
            MeasurementDisclosureTerms,
            "A shipped page describes how rendering fidelity is measured. Refer to the "
            + "fidelity QA pipeline instead, and to the published compat report.");
    }

    [Fact]
    public void Shipped_pages_name_no_product_that_has_not_shipped()
    {
        AssertAbsent(
            RepositoryLayout.EnumerateShippedFiles(),
            UnannouncedProductTerms,
            "A shipped page names a product that has not been announced. Rendlio Sheets is "
            + "the only product this repository may name.");
    }

    [Fact]
    public void Shipped_pages_carry_no_internal_identifier()
    {
        List<string> pages = ProseAndCode();

        AssertAbsent(
            pages,
            InternalIdentifierTerms,
            "A shipped page carries planning vocabulary or an internal path.");

        AssertNoMatch(pages, TrackedItemIdPattern(), "A shipped page carries an internal item id.");
        AssertNoMatch(pages, InternalNumberingPattern(), "A shipped page carries internal roadmap numbering.");
    }

    [Fact]
    public void Shipped_pages_carry_no_pricing_or_sku()
    {
        AssertAbsent(
            RepositoryLayout.EnumerateShippedFiles(),
            CommercialTerms,
            "A shipped page carries commercial terms. Everything in this repository is MIT.");
    }

    /// <summary>
    /// Prose and code only. Ignore files are excluded because naming a private path is
    /// exactly what an ignore rule is for.
    /// </summary>
    private static List<string> ProseAndCode() =>
        RepositoryLayout.EnumerateShippedFiles()
            .Where(path => Path.GetExtension(path).Length > 0)
            .ToList();

    private static void AssertAbsent(IReadOnlyList<string> files, string[] terms, string rule)
    {
        List<string> offenders = [];

        foreach (string file in Scannable(files))
        {
            string text = File.ReadAllText(file);

            offenders.AddRange(terms
                .Where(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(term => $"  {RepositoryLayout.Describe(file)}: \"{term}\""));
        }

        AssertClean(offenders, rule);
    }

    private static void AssertNoMatch(IReadOnlyList<string> files, Regex pattern, string rule)
    {
        List<string> offenders = [];

        foreach (string file in Scannable(files))
        {
            offenders.AddRange(pattern.Matches(File.ReadAllText(file))
                .Select(match => $"  {RepositoryLayout.Describe(file)}: \"{match.Value}\""));
        }

        AssertClean(offenders, rule);
    }

    private static IEnumerable<string> Scannable(IReadOnlyList<string> files) =>
        files.Where(file => !ScannerSources.Contains(Path.GetFileName(file), StringComparer.Ordinal));

    private static void AssertClean(List<string> offenders, string rule)
    {
        bool clean = offenders.Count == 0;

        Assert.True(clean, $"{rule}{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [GeneratedRegex(@"\bwi-[0-9a-f]{8}\b", RegexOptions.IgnoreCase)]
    private static partial Regex TrackedItemIdPattern();

    [GeneratedRegex(@"\bP\d+[/-]E\d+\b|\bP\d+-[A-Z]{3,}\b")]
    private static partial Regex InternalNumberingPattern();
}
