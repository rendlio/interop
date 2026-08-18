using System.Diagnostics;
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
    /// files are excluded from the scan. Nothing else may be — which is why they are named by
    /// repository-relative path and not by bare file name: a name would exempt any future
    /// file called <c>RepositoryLayout.cs</c> anywhere in the tree, and an exemption nobody
    /// asked for is the one way a page could carry forbidden wording past this fixture.
    /// </summary>
    private static readonly string[] ScannerSources =
    [
        "tests/Rendlio.Interop.Conventions.Tests/PublicSurfaceRulesTests.cs",
        "tests/Rendlio.Interop.Conventions.Tests/RepositoryLayout.cs",
    ];

    /// <summary>
    /// Files whose whole name is an extension, and whose job is to name paths. An ignore rule
    /// has to name the private tree in order to keep it out of the history, and
    /// <c>docs/internal/ export-ignore</c> in <c>.gitattributes</c> is the ordinary way to
    /// keep it out of a published archive — so the internal-identifier rule cannot apply to
    /// them without failing a build for doing the right thing. Named one by one rather than
    /// matched by a leading dot, so that a dotfile added later has to be exempted on purpose.
    /// </summary>
    private static readonly string[] PathNamingConfiguration =
    [
        ".gitignore", ".gitattributes", ".editorconfig",
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
    public void The_scan_does_not_reach_a_generated_lock()
    {
        // A restore lock is committed and public, but it is written by restore and is mostly
        // base64 content hashes. Scanning that much random text for a three-letter forbidden
        // word finds one by coincidence sooner or later, on a regeneration nobody authored
        // and nobody can edit — a failure that would say nothing about what this repository
        // publishes. The claim a lock does have to satisfy, that one exists for every project
        // in the solution, is asserted in BuildContractTests.
        IEnumerable<string> names = RepositoryLayout.EnumerateShippedFiles()
            .Select(RepositoryLayout.Describe);

        Assert.DoesNotContain(names, name =>
            name.EndsWith("packages.lock.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_scan_reaches_nothing_this_repository_does_not_track()
    {
        // What a repository publishes is what it tracks, which is what RepositoryLayout says
        // it enumerates. Saying it and doing it came apart once already: the set used to come
        // from a walk of the working tree, and a walk counts a scratch file nobody committed
        // as published — so an unrelated note left beside the solution reddens these fixtures
        // on one machine while CI, which only ever has the checkout, stays green. Nothing
        // else here would notice a walk coming back, because on a clean checkout the two
        // answers agree; the difference only shows on the machine of whoever hits it.
        //
        // Asked of git directly rather than through the helper that produced the set: a set
        // compared against the code that built it agrees with itself whatever that code does.
        string[] tracked = TrackedPaths();
        List<string> scanned =
            [.. RepositoryLayout.EnumerateShippedFiles().Select(RepositoryLayout.Describe)];

        // Guards the assertion below, which an enumeration that had stopped finding anything
        // would satisfy while describing nothing.
        Assert.NotEmpty(scanned);

        string[] untracked = [.. scanned.Where(name => !tracked.Contains(name, StringComparer.Ordinal))];

        Assert.True(
            untracked.Length == 0,
            "These are scanned as published but git does not track them: "
            + $"{string.Join(", ", untracked)}.{Environment.NewLine}"
            + "What this repository publishes is what it tracks, so the enumeration comes from "
            + "the index rather than from a walk of the working tree. A walk reads whatever "
            + "happens to be on one machine, which is not what anybody else receives.");
    }

    [Fact]
    public void The_scan_excludes_its_own_sources_and_nothing_else()
    {
        List<string> shipped = [.. RepositoryLayout.EnumerateShippedFiles().Select(RepositoryLayout.Describe)];
        List<string> scanned =
            [.. Scannable(RepositoryLayout.EnumerateShippedFiles()).Select(RepositoryLayout.Describe)];

        foreach (string source in ScannerSources)
        {
            // Named by path, so a rename or a move leaves the exemption pointing at nothing.
            // That fails safe — the scanner's own sources quote every forbidden term, so the
            // next run is a wall of failures — but a wall of failures is a poor way to learn
            // that a file moved, and this says it in one line instead.
            Assert.Contains(source, shipped, StringComparer.Ordinal);
            Assert.DoesNotContain(source, scanned, StringComparer.Ordinal);
        }

        // "Nothing else may be": the two sets differ by exactly the two files above.
        Assert.Equal(shipped.Count - ScannerSources.Length, scanned.Count);
    }

    [Fact]
    public void The_internal_identifier_scan_skips_the_files_whose_job_is_to_name_a_path()
    {
        List<string> shipped = [.. RepositoryLayout.EnumerateShippedFiles().Select(RepositoryLayout.Describe)];
        List<string> scanned = [.. ProseAndCode().Select(RepositoryLayout.Describe)];

        foreach (string exempt in PathNamingConfiguration)
        {
            // Shipped, or the exemption is about a file that is not there and this test
            // passes by describing nothing.
            Assert.Contains(exempt, shipped, StringComparer.Ordinal);
            Assert.DoesNotContain(exempt, scanned, StringComparer.Ordinal);
        }

        // And nothing went with them. LICENSE is the one to name: it has no extension, so the
        // filter this replaced removed it and kept every file it meant to remove.
        Assert.Contains("README.md", scanned, StringComparer.Ordinal);
        Assert.Contains("LICENSE", scanned, StringComparer.Ordinal);
        Assert.Equal(shipped.Count - PathNamingConfiguration.Length, scanned.Count);
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

    [Fact]
    public void Shipped_pages_never_call_anything_open_source()
    {
        AssertNoMatch(
            RepositoryLayout.EnumerateShippedFiles(),
            OpenSourceClaimPattern(),
            "A shipped page calls something \"open source\". The rendering engine is "
            + "source-available; an upstream is described by naming the licence it carries.");
    }

    /// <summary>
    /// The wordings the rule above has to reach. Its reach is a character class rather than a
    /// phrase, and the class is the substance of the rule: narrow it back to a plain space and
    /// every other fixture here still passes, green, while the forms that actually put a claim
    /// on a page walk through. Asserted here rather than left to the scan, because the scan can
    /// only report on a tree that today contains none of them.
    /// </summary>
    [Theory]
    [InlineData("The engine is open source.")]
    [InlineData("The engine is open-source software.")]
    [InlineData("Released as Open Source.")]
    [InlineData("The engine is open\nsource under a permissive licence.")]
    [InlineData("The engine is open **source**.")]
    [InlineData("The engine was open sourced last year.")]
    public void The_licence_rule_reaches_a_claim_however_it_is_typeset(string page)
    {
        Assert.Matches(OpenSourceClaimPattern(), page);
    }

    /// <summary>
    /// And what it has to leave alone. These are the cost of the blanket ban, so they are the
    /// part worth pinning: a gate that reddens a link to the licence list, or a sentence that
    /// happens to end in "open", is one an author learns to route around instead of read.
    /// </summary>
    [Theory]
    [InlineData("MIT, see https://opensource.org/licenses/MIT for the text.")]
    [InlineData("The tool will reopen source files after a rebuild.")]
    [InlineData("The format is open. Source files live under src/.")]
    [InlineData("ClosedXML is MIT-licensed; the engine is source-available.")]
    public void The_licence_rule_leaves_alone_what_is_not_a_claim(string page)
    {
        Assert.DoesNotMatch(OpenSourceClaimPattern(), page);
    }

    [Fact]
    public void The_licence_rule_reads_across_a_list_boundary_and_that_is_accepted()
    {
        // The class bridges a line break and a marker, and a Markdown bullet is both — so two
        // adjacent items, one ending in "open" and the next opening with "Source", read as a
        // claim that nobody wrote.
        //
        // Left standing, deliberately. No such pair exists in the tree; narrowing the class to
        // exclude it would cost the line break it exists for, since that is the same
        // character; and the failure direction is the safe one — a false positive on a licence
        // gate is one loud build and a reworded sentence, a false negative is a false licence
        // claim on a public page. Pinned so the next person to meet it finds a decision on the
        // record instead of a surprise, and can overturn it knowing what it was for.
        Assert.Matches(
            OpenSourceClaimPattern(),
            "- the format is left open\n- Source files are read once");
    }

    /// <summary>
    /// Prose and code only: everything shipped except the files whose job is to name paths —
    /// see <see cref="PathNamingConfiguration"/>. Excluded by role rather than by shape,
    /// because the shape does not distinguish them: <c>Path.GetExtension(".gitignore")</c>
    /// returns <c>".gitignore"</c>, so a filter on having an extension keeps every one of
    /// them and removes only <c>LICENSE</c>.
    /// </summary>
    private static List<string> ProseAndCode() =>
        RepositoryLayout.EnumerateShippedFiles()
            .Where(path => !PathNamingConfiguration.Contains(
                Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
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

    /// <summary>
    /// The paths git tracks, repository-relative and forward-slashed as git writes them, for
    /// a fixture that has to know what is tracked without asking the code under test.
    /// Separated by NUL so that a name git would otherwise quote and escape arrives whole.
    /// </summary>
    private static string[] TrackedPaths()
    {
        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = RepositoryLayout.Root.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add("ls-files");
        start.ArgumentList.Add("-z");

        using Process git = Process.Start(start)
            ?? throw new InvalidOperationException(
                "Could not start 'git'. What this repository publishes is what it tracks, so "
                + "this fixture has to run from a checkout with git available.");

        // Drain both pipes before waiting: the listing is long enough to fill one, and a
        // process blocked on a pipe nobody is reading would hang rather than fail.
        Task<string> listing = git.StandardOutput.ReadToEndAsync();
        Task<string> failure = git.StandardError.ReadToEndAsync();
        git.WaitForExit();

        Assert.True(
            git.ExitCode == 0,
            $"'git ls-files' exited with {git.ExitCode}: {failure.Result.Trim()}");

        return listing.Result.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static IEnumerable<string> Scannable(IReadOnlyList<string> files) =>
        files.Where(file =>
            !ScannerSources.Contains(RepositoryLayout.Describe(file), StringComparer.Ordinal));

    private static void AssertClean(List<string> offenders, string rule)
    {
        bool clean = offenders.Count == 0;

        Assert.True(clean, $"{rule}{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// A licence claim this repository may not make. The rendering engine is BUSL-licensed,
    /// so calling it "open source" is not a loose synonym but a false statement about a
    /// licence, published to the audience that cares about the difference most.
    /// <para>
    /// Banned outright rather than only where it sits near "Rendlio" or "engine" — decided,
    /// not inherited. The phrase is legitimate about an upstream, and ClosedXML and MiniExcel
    /// genuinely are one; but scoping the match cannot tell the two apart on these pages. The
    /// prose is hard-wrapped, so a claim and its subject routinely sit on different lines,
    /// and a sentence naming an upstream alongside Rendlio is the ordinary shape of a
    /// sentence in an adapter repository — so a scope would have to be either wide enough to
    /// catch the upstream or narrow enough to miss the engine. A gate that guesses which noun
    /// a claim attaches to cannot be trusted in either direction. The outright ban costs an
    /// author nothing they need: naming the upstream's licence — "ClosedXML is MIT-licensed"
    /// — says strictly more than the phrase does, and is what README.md and
    /// UPSTREAM-PATCHES.md already do across every upstream they discuss without once
    /// reaching for it.
    /// </para>
    /// <para>
    /// A pattern rather than one more literal term, because the phrase comes apart: a wrap
    /// puts a line break between the two words, emphasis puts a marker there, and a hyphen
    /// joins them into something no search for the phrase will find. The two assertions this
    /// replaces each missed some of that. The README one read raw text, so all three forms
    /// passed it — it could have been satisfied by the very sentence it existed to stop. The
    /// UPSTREAM-PATCHES.md one normalised first and did catch the wrap and the marker, but the
    /// hyphen went through it too, and neither ever read a third page.
    /// <see cref="The_licence_rule_reaches_a_claim_however_it_is_typeset"/> pins the forms this
    /// one has to reach, and <see cref="The_licence_rule_leaves_alone_what_is_not_a_claim"/>
    /// the ones it must not.
    /// </para>
    /// </summary>
    [GeneratedRegex(@"\bopen[-\s*`]+source", RegexOptions.IgnoreCase)]
    private static partial Regex OpenSourceClaimPattern();

    [GeneratedRegex(@"\bwi-[0-9a-f]{8}\b", RegexOptions.IgnoreCase)]
    private static partial Regex TrackedItemIdPattern();

    [GeneratedRegex(@"\bP\d+[/-]E\d+\b|\bP\d+-[A-Z]{3,}\b")]
    private static partial Regex InternalNumberingPattern();
}
