using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Pins the build settings the README promises a consumer. Each one is a published claim —
/// that warnings are errors, that a package can only ship from <c>src/</c>, that an
/// undocumented public member fails the build, that an upstream is always the published
/// package, that a range resolves to the graph its lock records. Turning one off is a change
/// to what this repository promises, so it should show up as a failing test rather than as a
/// quiet edit to a props file.
/// </summary>
public sealed partial class BuildContractTests
{
    private const string NuGetOrg = "https://api.nuget.org/v3/index.json";

    private const string LockFileName = "packages.lock.json";

    private const string SolutionFileName = "Rendlio.Interop.slnx";

    private const string WorkflowPath = ".github/workflows/ci.yml";

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
        Assert.Equal("true", Property("src/Directory.Build.props", "GenerateDocumentationFile"));
    }

    [Fact]
    public void Every_package_this_repository_produces_is_mit()
    {
        // Rule 1: funnel code is MIT. This property is what stamps that onto the .nupkg.
        Assert.Equal("MIT", Property("src/Directory.Build.props", "PackageLicenseExpression"));
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
        string workflow = RepositoryLayout.ReadFile(WorkflowPath);

        // Guards the assertion below: a workflow that had stopped restoring altogether would
        // contain no unlocked restore either, and would pass without enforcing anything.
        Assert.Contains("dotnet restore", workflow, StringComparison.Ordinal);

        Assert.False(
            UnlockedRestorePattern().IsMatch(workflow),
            $"'{WorkflowPath}' restores without locked mode. Restore then resolves the ranges "
            + "afresh, and an upstream that drifted inside one enters the build unreviewed.");
    }

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
