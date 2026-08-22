using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Pins the build settings the README promises a consumer. Each one is a published claim —
/// that warnings are errors, that a package can only ship from <c>src/</c>, that an
/// undocumented public member fails the build, that an upstream is always the published
/// package, that a range resolves to the graph its lock records, that a package page is
/// written rather than defaulted. Turning one off is a change to what this repository
/// promises, so it should show up as a failing test rather than as a quiet edit to a props
/// file.
/// </summary>
public sealed partial class BuildContractTests
{
    private const string NuGetOrg = "https://api.nuget.org/v3/index.json";

    private const string LockFileName = "packages.lock.json";

    private const string SolutionFileName = "Rendlio.Interop.slnx";

    private const string WorkflowDirectory = ".github/workflows";

    private const string PackagingProps = "src/Directory.Build.props";

    private const string RepositoryHome = "https://github.com/rendlio/rendlio-interop";

    /// <summary>Shared tags; a project appends its upstream rather than replacing these.</summary>
    private const string SharedPackageTags = "rendlio;rendlio-sheets;spreadsheet;interop;adapter";

    /// <summary>
    /// What the SDK puts in the <c>description</c> field of a package that sets none. It packs
    /// without complaint, so this exact string is what a consumer would read on the page.
    /// </summary>
    private const string SdkPlaceholderDescription = "Package Description";

    /// <summary>
    /// The property pack actually reads for the <c>description</c> field. The SDK defaults it
    /// from <c>Description</c>, so a guard on this one covers a project that set either.
    /// </summary>
    private const string DescriptionProperty = "$(PackageDescription)";

    /// <summary>
    /// The opt-out a project under <c>src/</c> uses to decline to ship. The description guard
    /// has to test it, because a <c>BeforeTargets</c> hook runs even when the target it
    /// precedes is skipped by its own condition.
    /// </summary>
    private const string PackableProperty = "$(IsPackable)";

    [Theory]
    [InlineData("Nullable", "enable")]
    [InlineData("TreatWarningsAsErrors", "true")]
    [InlineData("EnableNETAnalyzers", "true")]
    public void The_repository_wide_build_settings_hold(string property, string expected)
    {
        Assert.Equal(expected, Property("Directory.Build.props", property));
    }

    [Fact]
    public void Nothing_packs_unless_it_opts_in()
    {
        // The default is off repository-wide and back on only under src/, so a project can
        // ship a package only by being placed there.
        Assert.Equal("false", Property("Directory.Build.props", "IsPackable"));
        Assert.Equal("true", Property("src/Directory.Build.props", "IsPackable"));
        Assert.Equal("false", Property("tests/Directory.Build.props", "IsPackable"));

        // Restated under tools/ rather than left to the default. A tool is run from a
        // checkout, and one that started shipping to consumers by accident would be a change
        // to what this repository publishes.
        Assert.Equal("false", Property("tools/Directory.Build.props", "IsPackable"));
    }

    [Fact]
    public void A_package_documents_its_public_members()
    {
        // With warnings as errors this is what turns an undocumented public member into a
        // build failure, which is the promise the README's Contributing section makes.
        Assert.Equal("true", Property(PackagingProps, "GenerateDocumentationFile"));
    }

    [Fact]
    public void Every_package_this_repository_produces_is_mit()
    {
        // Rule 1: funnel code is MIT. This property is what stamps that onto the .nupkg.
        Assert.Equal("MIT", Property(PackagingProps, "PackageLicenseExpression"));
    }

    [Theory]
    [InlineData("PackageProjectUrl", RepositoryHome)]
    [InlineData("PackageTags", SharedPackageTags)]
    public void The_part_of_a_package_page_that_is_the_same_everywhere_is_written_once(
        string property, string expected)
    {
        // A package page is the first thing a consumer reads, and the SDK writes one for a
        // package that says nothing. Whatever is identical across every adapter is set here
        // so no adapter can ship a page by accident.
        Assert.Equal(expected, Property(PackagingProps, property));
    }

    [Fact]
    public void Every_package_ships_a_page_of_its_own()
    {
        // PackageReadmeFile only names the file; the item is what puts it in the package.
        // Both point at the project's own README rather than this repository's, because a
        // page describes the one package a consumer is installing. Pack fails when it is
        // missing, which is the point.
        Assert.Equal("README.md", Property(PackagingProps, "PackageReadmeFile"));

        List<XElement> packed =
        [
            .. Document(PackagingProps)
                .Descendants("None")
                .Where(none =>
                    none.Attribute("Include")?.Value.EndsWith("README.md", StringComparison.Ordinal) == true),
        ];

        // Exactly one, named rather than counted implicitly: two would be the shape of a
        // second readme item added later, and pack would then have to choose between them.
        Assert.True(
            packed.Count == 1,
            $"{PackagingProps} should pack exactly one README, but packs {packed.Count}.");

        Assert.Equal("true", packed[0].Attribute("Pack")?.Value);
        Assert.Contains(
            "MSBuildProjectDirectory",
            packed[0].Attribute("Include")?.Value ?? string.Empty,
            StringComparison.Ordinal);

        // Coupled to PackageReadmeFile above, which names a path relative to the package
        // root: pack it anywhere else and the name no longer resolves.
        Assert.Equal("/", packed[0].Attribute("PackagePath")?.Value);
    }

    [Fact]
    public void No_package_ships_without_a_description_of_its_own()
    {
        // Description names one package, so unlike the rest of the page it cannot be set
        // centrally — and the SDK's default for it packs without complaint, which is how
        // placeholder text reaches a public package page. This is the only place that
        // omission can be caught, so it is caught as a failed pack.
        List<XElement> guards =
        [
            .. Document(PackagingProps)
                .Descendants("Target")
                .Where(target => target.Attribute("BeforeTargets")?.Value == "GenerateNuspec")
                .Elements("Error"),
        ];

        Assert.True(
            guards.Count == 1,
            $"{PackagingProps} should fail the pack of a package that sets no description in "
            + $"exactly one place, but {guards.Count} were found.");

        string condition = guards[0].Attribute("Condition")?.Value ?? string.Empty;

        // A project under src/ may decline to ship, and MSBuild runs a BeforeTargets hook
        // even when the target it precedes is skipped by its own condition. Without this the
        // guard demands a description from a project that produces no package.
        Assert.Contains(PackableProperty, condition, StringComparison.Ordinal);

        // On the property pack reads, not on Description. The two are not interchangeable:
        // the SDK defaults this one from Description, so guarding it accepts a project that
        // set either spelling, while guarding Description would reject one that set only
        // PackageDescription.
        Assert.Contains(DescriptionProperty, condition, StringComparison.Ordinal);

        // Checking only for an empty description would let the SDK's placeholder through,
        // and the placeholder is the case that actually reaches a consumer.
        Assert.Contains(SdkPlaceholderDescription, condition, StringComparison.Ordinal);
    }

    [Fact]
    public void Package_versions_are_managed_centrally()
    {
        // Rule 3 is enforced by the ranges in Directory.Packages.props, which only bind
        // while central management is on.
        Assert.Equal("true", Property("Directory.Packages.props", "ManagePackageVersionsCentrally"));
    }

    [Fact]
    public void Restore_resolves_upstreams_from_nuget_org_only()
    {
        // Rule 2: an adapter's upstream is the published, unmodified package. Any other
        // feed could satisfy that dependency from somewhere else.
        XElement sources = Document("NuGet.config").Root?.Element("packageSources")
            ?? throw new InvalidOperationException("NuGet.config declares no <packageSources>.");

        Assert.Equal("clear", sources.Elements().First().Name.LocalName);

        string[] configured = [.. sources.Elements("add").Select(add => add.Attribute("value")?.Value ?? string.Empty)];

        Assert.Equal([NuGetOrg], configured);
    }

    [Fact]
    public void Nothing_but_the_configured_sources_decides_where_a_package_comes_from()
    {
        // Clearing packageSources is not the whole job: a package source mapping decides
        // which of the configured sources serves which package, and one is inherited from
        // whatever the machine happens to have. It cannot defeat rule 2 — a mapping only
        // narrows sources that are already defined, so a hostile one fails a restore loudly
        // rather than substituting an upstream quietly — but a mapping nobody here wrote is
        // a restore that reproduces differently on a contributor's box, and that has already
        // cost one false result.
        XElement mapping = Document("NuGet.config").Root?.Element("packageSourceMapping")
            ?? throw new InvalidOperationException(
                "NuGet.config declares no <packageSourceMapping>, so it inherits the machine's.");

        Assert.Equal("clear", mapping.Elements().First().Name.LocalName);

        // Nothing after the clear on purpose: with no pattern declared, mapping is off and
        // <packageSources> is the only answer. An entry here would be a second, quieter
        // place to change where a package comes from.
        Assert.Empty(mapping.Elements("packageSource"));
    }

    [Fact]
    public void Restore_writes_down_the_versions_a_range_resolved_to()
    {
        // Rule 3 constrains a range, and a range is satisfiable by more than one version.
        // The lock is what turns that constraint into a fact on disk that a reviewer can see.
        Assert.Equal("true", Property("Directory.Build.props", "RestorePackagesWithLockFile"));
    }

    [Fact]
    public void Every_project_in_the_solution_commits_its_lock()
    {
        string[] projects = [.. SolutionProjects()];

        // Guards the assertion below. An empty project list would satisfy it while checking
        // nothing, which is how this claim would go quiet without anyone noticing.
        Assert.NotEmpty(projects);

        string[] unlocked =
        [
            .. projects.Where(project => !File.Exists(Path.Combine(
                Path.GetDirectoryName(Path.Combine(RepositoryLayout.Root.FullName, project))
                    ?? RepositoryLayout.Root.FullName,
                LockFileName))),
        ];

        Assert.True(
            unlocked.Length == 0,
            $"These projects restore without a committed {LockFileName}, so nothing records "
            + $"what their ranges resolved to: {string.Join(", ", unlocked)}. Regenerate with "
            + "a force-evaluate restore and commit what it writes.");
    }

    [Fact]
    public void Ci_restores_against_the_committed_lock()
    {
        // Every workflow, not just the one that restores today. The lock is only binding
        // because CI refuses to resolve a range afresh, so a second workflow added later
        // that restored without the flag would be a way back out of rule 3 — and it would
        // be added by whoever needed a second workflow, not by whoever wrote this rule.
        (string Name, string Text)[] workflows = [.. Workflows()];

        // Guards the assertion below. No workflow at all, or none that restores, satisfies
        // "none of them restores unlocked" while enforcing nothing.
        Assert.NotEmpty(workflows);
        Assert.Contains(workflows, workflow =>
            workflow.Text.Contains("dotnet restore", StringComparison.Ordinal));

        string[] unlocked =
        [
            .. workflows
                .Where(workflow => UnlockedRestorePattern().IsMatch(workflow.Text))
                .Select(workflow => workflow.Name),
        ];

        Assert.True(
            unlocked.Length == 0,
            $"These workflows restore without locked mode: {string.Join(", ", unlocked)}. "
            + "Restore then resolves the ranges afresh, and an upstream that drifted inside "
            + "one enters the build unreviewed.");
    }

    /// <summary>Every CI workflow, as a repository-relative path and its text.</summary>
    private static IEnumerable<(string Name, string Text)> Workflows() =>
        Directory
            .EnumerateFiles(Path.Combine(RepositoryLayout.Root.FullName, WorkflowDirectory), "*.y*ml")
            .Order(StringComparer.Ordinal)
            .Select(path => (RepositoryLayout.Describe(path), File.ReadAllText(path)));

    /// <summary>The projects the solution builds, as repository-relative paths.</summary>
    private static IEnumerable<string> SolutionProjects() =>
        XDocument.Parse(RepositoryLayout.ReadFile(SolutionFileName))
            .Descendants("Project")
            .Select(project => project.Attribute("Path")?.Value ?? string.Empty)
            .Where(path => path.Length > 0);

    private static string? Property(string relativePath, string name) =>
        Document(relativePath).Descendants(name).FirstOrDefault()?.Value.Trim();

    private static XDocument Document(string relativePath) =>
        XDocument.Parse(RepositoryLayout.ReadFile(relativePath));

    /// <summary>
    /// A restore that is not held to the lock. The whitespace is loose on purpose: YAML is
    /// free to fold a command across lines, and a restore that lost the flag by being
    /// reformatted is the same defect as one that never had it.
    /// </summary>
    [GeneratedRegex(@"dotnet\s+restore(?!\s+--locked-mode\b)")]
    private static partial Regex UnlockedRestorePattern();
}
